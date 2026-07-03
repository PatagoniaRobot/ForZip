// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Cli.Commands;

public class VerifyCommand
{
    private readonly IReportService _reportService;
    private readonly IVerificationService _verificationService;
    private readonly ILocalizationService _localization;

    public VerifyCommand(
        IReportService reportService,
        IVerificationService verificationService,
        ILocalizationService localization)
    {
        _reportService = reportService;
        _verificationService = verificationService;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var reportPath = parser.GetOption("-r", "--report");
        var manifestPath = parser.GetOption("-m", "--manifest");
        var zipPath = parser.GetOption("-z", "--zip");
        var password = parser.GetOption("-p", "--password");

        if (string.IsNullOrEmpty(reportPath) && string.IsNullOrEmpty(manifestPath))
        {
            Console.WriteLine("Uso:");
            Console.WriteLine("  forzip verify -r <informe.txt>                 Verifica la integridad del informe (.sha256).");
            Console.WriteLine("  forzip verify -m <manifiesto.json> [-z <zip>] [-p <pass>]");
            Console.WriteLine("                                                 Re-hashea el ZIP y lo compara con el manifiesto.");
            return 1;
        }

        var exitCode = 0;

        if (!string.IsNullOrEmpty(reportPath))
        {
            exitCode = VerifyReportIntegrity(reportPath);
        }

        if (!string.IsNullOrEmpty(manifestPath))
        {
            var archiveExit = await VerifyArchiveAsync(manifestPath, zipPath, password);
            if (archiveExit != 0)
            {
                exitCode = archiveExit;
            }
        }

        return exitCode;
    }

    private int VerifyReportIntegrity(string reportPath)
    {
        Console.WriteLine($"Verificando integridad del informe: {reportPath}");
        var (isValid, details) = _reportService.VerifyReport(reportPath);

        if (isValid)
        {
            WriteColored(ConsoleColor.Green, $"[✓] INFORME VERIFICADO: {details}");
            return 0;
        }

        WriteColored(ConsoleColor.Red, $"[X] INFORME NO VERIFICADO: {details}");
        return 3; // verification fail
    }

    private async Task<int> VerifyArchiveAsync(string manifestPath, string? zipPath, string? password)
    {
        if (!File.Exists(manifestPath))
        {
            WriteColored(ConsoleColor.Red, $"Error: el manifiesto no existe: {manifestPath}");
            return 1;
        }

        Console.WriteLine($"\nVerificando evidencia contra el manifiesto: {manifestPath}");

        ArchiveVerificationResult result;
        try
        {
            result = await _verificationService.VerifyArchiveAsync(
                manifestPath, zipPath, password, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, $"Error durante la verificación: {ex.Message}");
            return 2;
        }

        if (result.Volumes is { Count: > 0 })
        {
            Console.WriteLine("Volúmenes:");
            foreach (var vol in result.Volumes)
            {
                var (vcolor, vtag) = StatusTag(vol.Status);
                WriteColored(vcolor, $"  [{vtag}] {vol.FileName}");
            }
            Console.WriteLine();
        }

        foreach (var entry in result.Entries)
        {
            var (color, tag) = StatusTag(entry.Status);
            WriteColored(color, $"  [{tag}] {entry.EntryName}");
        }

        Console.WriteLine();
        Console.WriteLine($"Resumen: {result.OkCount} OK, {result.AlteredCount} alterados, " +
                          $"{result.MissingCount} faltantes, {result.ExtraCount} añadidos.");
        if (result.ZipHashMatches.HasValue)
        {
            Console.WriteLine($"Hash global del ZIP: {(result.ZipHashMatches.Value ? "coincide" : "NO coincide")}.");
        }

        if (result.Signature is { Present: true } sig)
        {
            WriteColored(sig.Valid ? ConsoleColor.Green : ConsoleColor.Red,
                $"Firma digital: {(sig.Valid ? "VÁLIDA" : "INVÁLIDA")}");
            if (!string.IsNullOrEmpty(sig.SignerSubject))
            {
                Console.WriteLine($"  Firmante: {sig.SignerSubject}");
            }
            if (sig.SignedAtUtc.HasValue)
            {
                Console.WriteLine($"  Fecha de firma declarada (UTC): {sig.SignedAtUtc.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }
            if (sig.TimestampUtc.HasValue)
            {
                WriteColored(sig.TimestampValid == true ? ConsoleColor.Green : ConsoleColor.Red,
                    $"  Sello de tiempo RFC 3161 (UTC): {sig.TimestampUtc.Value:yyyy-MM-ddTHH:mm:ssZ} " +
                    $"({(sig.TimestampValid == true ? "VÁLIDO" : "NO VÁLIDO")})");
                if (!string.IsNullOrEmpty(sig.TimestampAuthority))
                {
                    Console.WriteLine($"  Autoridad de sellado (TSA): {sig.TimestampAuthority}");
                }
            }
            else if (sig.TimestampValid == false)
            {
                WriteColored(ConsoleColor.Red, "  Sello de tiempo RFC 3161: presente pero ilegible o corrupto.");
            }
            else
            {
                Console.WriteLine("  Sello de tiempo RFC 3161: ausente (fecha declarada por el firmante).");
            }
        }
        else
        {
            Console.WriteLine("Firma digital: ausente (manifiesto sin firmar).");
        }

        if (result.ContentVerificationError != null)
        {
            WriteColored(ConsoleColor.Yellow,
                $"\n[!] No se pudo verificar el contenido: {result.ContentVerificationError}");
            WriteColored(ConsoleColor.Yellow,
                "    (¿falta la contraseña con -p, o el archivo está corrupto?) La verificación por volumen sí se realizó.");
        }

        if (result.IsIntact)
        {
            WriteColored(ConsoleColor.Green, "\n[✓] EVIDENCIA ÍNTEGRA: todos los archivos coinciden con el manifiesto.");
            return 0;
        }

        // Distinguir manipulación detectada de una verificación que no pudo completarse.
        var tamperDetected = result.AlteredCount > 0 || result.MissingCount > 0 || result.ExtraCount > 0 ||
                             result.HasVolumeProblems || result.ZipHashMatches == false ||
                             result.Signature is { Valid: false } ||
                             result.Signature is { TimestampValid: false };

        if (tamperDetected)
        {
            WriteColored(ConsoleColor.Red, "\n[X] EVIDENCIA COMPROMETIDA: se detectaron discrepancias.");
            return 3;
        }

        WriteColored(ConsoleColor.Yellow, "\n[!] VERIFICACIÓN INCOMPLETA: no se detectaron alteraciones, pero el contenido no pudo verificarse por completo.");
        return 2;
    }

    private static (ConsoleColor color, string tag) StatusTag(FileVerificationStatus status) => status switch
    {
        FileVerificationStatus.Ok => (ConsoleColor.Green, "OK      "),
        FileVerificationStatus.Altered => (ConsoleColor.Red, "ALTERADO"),
        FileVerificationStatus.Missing => (ConsoleColor.Red, "FALTANTE"),
        FileVerificationStatus.Extra => (ConsoleColor.Yellow, "AÑADIDO "),
        _ => (ConsoleColor.Gray, "?       ")
    };

    private static void WriteColored(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
