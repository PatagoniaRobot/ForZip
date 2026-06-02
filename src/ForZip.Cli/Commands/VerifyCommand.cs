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

        foreach (var entry in result.Entries)
        {
            var (color, tag) = entry.Status switch
            {
                FileVerificationStatus.Ok => (ConsoleColor.Green, "OK      "),
                FileVerificationStatus.Altered => (ConsoleColor.Red, "ALTERADO"),
                FileVerificationStatus.Missing => (ConsoleColor.Red, "FALTANTE"),
                FileVerificationStatus.Extra => (ConsoleColor.Yellow, "AÑADIDO "),
                _ => (ConsoleColor.Gray, "?       ")
            };
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
                Console.WriteLine($"  Fecha de firma (UTC): {sig.SignedAtUtc.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }
        }
        else
        {
            Console.WriteLine("Firma digital: ausente (manifiesto sin firmar).");
        }

        if (result.IsIntact)
        {
            WriteColored(ConsoleColor.Green, "\n[✓] EVIDENCIA ÍNTEGRA: todos los archivos coinciden con el manifiesto.");
            return 0;
        }

        WriteColored(ConsoleColor.Red, "\n[X] EVIDENCIA COMPROMETIDA: se detectaron discrepancias.");
        return 3;
    }

    private static void WriteColored(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
