// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;

namespace ForZip.Cli.Commands;

public class VerifyCommand
{
    private readonly IReportService _reportService;
    private readonly IVerificationService _verificationService;
    private readonly ISealedVerificationService _sealedVerificationService;
    private readonly ILocalizationService _localization;
    private readonly IConfigService? _configService;
    private readonly IZipService _zipService;
    private List<TrustedSigner> _trustedSigners = new();

    public VerifyCommand(
        IReportService reportService,
        IVerificationService verificationService,
        ISealedVerificationService sealedVerificationService,
        ILocalizationService localization,
        IConfigService? configService = null,
        IZipService? zipService = null)
    {
        _zipService = zipService ?? new ZipService(new HashService());
        _reportService = reportService;
        _verificationService = verificationService;
        _sealedVerificationService = sealedVerificationService;
        _localization = localization;
        _configService = configService;
    }

    /// <summary>
    /// Firmantes de confianza: los de config.json y los que se pasen con --trusted-signer (huellas SHA-256
    /// separadas por coma). Devuelve false si alguna huella de la opción no es válida.
    /// </summary>
    private bool LoadTrustedSigners(CommandParser parser)
    {
        try
        {
            _trustedSigners = _configService?.Load().TrustedSigners ?? new List<TrustedSigner>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _trustedSigners = new List<TrustedSigner>();
        }

        var option = parser.GetOption("--trusted-signer", "--trusted-signer");
        if (string.IsNullOrEmpty(option))
        {
            return true;
        }

        foreach (var part in option.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var thumbprint = SignerTrust.TryNormalize(part);
            if (thumbprint == null)
            {
                WriteColored(ConsoleColor.Red, $"Error: '{part}' no es una huella SHA-256 (64 hexadecimales).");
                return false;
            }

            _trustedSigners.Add(new TrustedSigner { Thumbprint = thumbprint, Name = "huella indicada con --trusted-signer" });
        }

        return true;
    }

    /// <summary>
    /// Huella del certificado y si está entre los de confianza: el nombre del certificado lo pone quien lo crea,
    /// la huella es lo que lo identifica.
    /// </summary>
    private void PrintSignerIdentity(SignatureInfo signature, string indent)
    {
        if (string.IsNullOrEmpty(signature.SignerThumbprint))
        {
            return;
        }

        Console.WriteLine($"{indent}Huella SHA-256 del certificado: {SignerTrust.Format(signature.SignerThumbprint)}");

        // Con la firma inválida el certificado no firmó este archivo: que esté en la lista no significa nada.
        if (!signature.Valid)
        {
            WriteColored(ConsoleColor.Red, $"{indent}Identidad del firmante no evaluada: la firma no es válida para este archivo.");
            return;
        }

        var (level, name) = SignerTrust.Evaluate(signature.SignerThumbprint, _trustedSigners);
        switch (level)
        {
            case SignerTrustLevel.Trusted:
                WriteColored(ConsoleColor.Green, $"{indent}Firmante DE CONFIANZA: {name}.");
                break;
            case SignerTrustLevel.Unknown:
                WriteColored(ConsoleColor.Yellow, $"{indent}ATENCIÓN: este certificado NO está entre los firmantes de confianza; " +
                                                  "puede ser otro aunque diga el mismo nombre.");
                break;
            default:
                WriteColored(ConsoleColor.Yellow, $"{indent}Identidad del firmante NO comprobada (sin firmantes de confianza): " +
                                                  "compare la huella con la del firmante por otro medio, o use --trusted-signer.");
                break;
        }
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var reportPath = parser.GetOption("-r", "--report");
        var manifestPath = parser.GetOption("-m", "--manifest");
        var sealedPath = parser.GetOption("-i", "--input");
        var zipPath = parser.GetOption("-z", "--zip");
        var password = parser.GetOption("-p", "--password");
        var packagePassword = parser.GetOption("--package-password", "--package-password");
        var writeReport = parser.GetOption("--write-report", "--write-report");
        var lang = parser.GetOption("--lang", "--language") ?? _localization.CurrentLanguage;
        if (!LoadTrustedSigners(parser))
        {
            return 1;
        }

        // -i con un manifiesto o un informe se deriva a su verificación, como hace la GUI según la extensión. Antes
        // todo lo que llegaba por -i se trataba como sobre y respondía "no es un sobre ForZip válido".
        if (!string.IsNullOrEmpty(sealedPath) && string.IsNullOrEmpty(manifestPath) && string.IsNullOrEmpty(reportPath))
        {
            if (sealedPath.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                (manifestPath, sealedPath) = (sealedPath, null);
            }
            else if (sealedPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                     && !sealedPath.EndsWith(DeliveryActService.ActSuffix, StringComparison.OrdinalIgnoreCase))
            {
                (reportPath, sealedPath) = (sealedPath, null);
            }
            else if (!sealedPath.EndsWith(SealedContainerFormat.SealedExtension, StringComparison.OrdinalIgnoreCase)
                     && !sealedPath.EndsWith(ReceiptService.ReceiptSuffix, StringComparison.OrdinalIgnoreCase)
                     && File.Exists(sealedPath) && ZipService.IdentifyArchive(sealedPath) != null)
            {
                // Un paquete con su manifiesto al lado se verifica contra el manifiesto; sin él (o de terceros, o un
                // autoextraíble, que se lee sin ejecutarlo) se prueba contra sus propios controles.
                var manifest = SplitArchive.GetBasePath(sealedPath) + ".manifest.json";
                if (File.Exists(manifest))
                {
                    (manifestPath, sealedPath) = (manifest, null);
                }
                else
                {
                    return await TestArchiveAsync(sealedPath, string.IsNullOrEmpty(password) ? null : password);
                }
            }
            else if (sealedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(sealedPath))
            {
                // Antes caía en la verificación de sobre: "No es un sobre ForZip válido", en rojo, como si estuviera alterado.
                WriteColored(ConsoleColor.Yellow,
                    $"[!] {Path.GetFileName(sealedPath)} no es un autoextraíble 7z o RAR reconocible: no hay nada que verificar. ForZip no lo ejecutó.");
                return 1;
            }
        }

        // Un sobre sellado se verifica solo: no hace falta decirle a ForZip de qué nivel se
        // trata, lo resuelve segun haya contrasena o no.
        if (!string.IsNullOrEmpty(sealedPath))
        {
            // Una clave vacía es "no hay clave", no "la clave es la cadena vacía": un -p sin
            // valor debe hacer la verificación pública, no reventar contra el derivador.
            return await VerifySealedAsync(
                sealedPath,
                string.IsNullOrEmpty(password) ? null : password,
                string.IsNullOrEmpty(packagePassword) ? null : packagePassword);
        }

        if (string.IsNullOrEmpty(reportPath) && string.IsNullOrEmpty(manifestPath))
        {
            Console.WriteLine("Uso:");
            Console.WriteLine("  forzip verify -r <informe.txt>                 Informe con su .sha256 (versiones anteriores y hash).");
            Console.WriteLine("  forzip verify -m <manifiesto.json> [-z <zip>] [-p <pass>] [--write-report <informe.txt>] [--lang <es|en>]");
            Console.WriteLine("                                                 Re-hashea el ZIP y lo compara con el manifiesto.");
            Console.WriteLine("                                                 Con --write-report escribe el informe forense del");
            Console.WriteLine("                                                 paquete, con el veredicto de esta verificación");
            Console.WriteLine("                                                 (.html: página imprimible; si no, texto).");
            Console.WriteLine("  forzip verify -i <sobre.fzs | recibo.receipt.json> [-p <pass>]");
            Console.WriteLine("                                                 Verifica un sobre sellado. Sin -p hace la");
            Console.WriteLine("                                                 verificación pública; con -p, la completa.");
            Console.WriteLine("  forzip verify -i <archivo.zip|.7z|.rar|.tar|.iso|.exe> [-p <pass>]");
            Console.WriteLine("                                                 Sin manifiesto al lado, prueba el archivo contra sus");
            Console.WriteLine("                                                 propios controles (como 7z t), en memoria. Un");
            Console.WriteLine("                                                 autoextraíble 7z o RAR se lee, no se ejecuta.");
            Console.WriteLine("  --package-password <pass>                      Clave del paquete de adentro del sobre, si se cifró");
            Console.WriteLine("                                                 con otra que la del sobre (por defecto, la de -p).");
            Console.WriteLine("  --trusted-signer <huella>[,<huella>…]          Huellas SHA-256 de firmantes de confianza (se suman");
            Console.WriteLine("                                                 a las de config.json). Sin ellas, la firma prueba que");
            Console.WriteLine("                                                 el archivo no cambió, no quién firmó.");
            return 1;
        }

        var exitCode = 0;

        if (!string.IsNullOrEmpty(reportPath))
        {
            exitCode = VerifyReportIntegrity(reportPath);
        }

        if (!string.IsNullOrEmpty(writeReport) && string.IsNullOrEmpty(manifestPath))
        {
            WriteColored(ConsoleColor.Red, "Error: --write-report necesita -m <manifiesto>: el informe se arma desde el manifiesto del paquete.");
            return 1;
        }

        if (!string.IsNullOrEmpty(writeReport) && File.Exists(writeReport))
        {
            WriteColored(ConsoleColor.Red, $"Error: ya existe {writeReport}; ForZip no sobrescribe. Elija otro nombre.");
            return 1;
        }

        if (!string.IsNullOrEmpty(manifestPath))
        {
            // Lo que se muestra en consola es también el detalle que el informe deja asentado.
            var original = Console.Out;
            var captured = new StringWriter();
            int archiveExit;
            Console.SetOut(new TeeWriter(original, captured));
            try
            {
                archiveExit = await VerifyArchiveAsync(manifestPath, zipPath, password);
            }
            finally
            {
                Console.SetOut(original);
            }

            if (!string.IsNullOrEmpty(writeReport))
            {
                var reportExit = await WriteVerificationReportAsync(manifestPath, writeReport, archiveExit, captured.ToString(), lang);
                if (reportExit != 0 && archiveExit == 0)
                {
                    archiveExit = reportExit;
                }
            }

            if (archiveExit != 0)
            {
                exitCode = archiveExit;
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Informe a pedido: el del paquete, armado desde su manifiesto, con el veredicto de esta verificación al final.
    /// Sin .sha256: lo que prueba la integridad es la huella del paquete anotada en la planilla de custodia.
    /// </summary>
    private async Task<int> WriteVerificationReportAsync(string manifestPath, string reportPath, int verifyExit, string details, string lang)
    {
        // 0 íntegra, 3 comprometida, 2 incompleta. Un 1 (no hay manifiesto) o un 2 por excepción no tienen veredicto
        // que asentar: el detalle ya lo dijo.
        var (badge, lead) = verifyExit switch
        {
            0 => ("verify_badge_valid_package", "verify_lead_valid"),
            3 => ("verify_badge_invalid_package", "verify_lead_invalid"),
            _ => ("verify_badge_inconclusive", "verify_lead_inconclusive")
        };

        ForensicManifest manifest;
        try
        {
            manifest = _verificationService.ParseManifest(await File.ReadAllTextAsync(manifestPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            WriteColored(ConsoleColor.Red, $"Error: no se pudo leer el manifiesto para el informe: {ex.Message}");
            return 2;
        }

        var data = _reportService.FromManifest(manifest, Path.GetFileName(manifestPath));
        data.Verification = new ReportVerification
        {
            Verdict = _localization.Get(badge, lang) + " — " + _localization.Get(lead, lang),
            Outcome = verifyExit switch
            {
                0 => VerificationOutcome.Intact,
                3 => VerificationOutcome.Compromised,
                _ => VerificationOutcome.Incomplete
            },
            Details = details.Trim()
        };

        try
        {
            var reportDir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(reportDir))
            {
                Directory.CreateDirectory(reportDir);
            }

            await _reportService.SaveReportAsync(_reportService.GenerateReportFor(reportPath, data, lang), reportPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteColored(ConsoleColor.Red, $"Error: no se pudo escribir el informe: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"Informe forense generado: {reportPath}");
        return 0;
    }

    /// <summary>Escribe en la consola y, a la vez, guarda el texto (sin colores) para el informe.</summary>
    private sealed class TeeWriter(TextWriter console, TextWriter copy) : TextWriter
    {
        public override System.Text.Encoding Encoding => console.Encoding;

        public override void Write(char value)
        {
            console.Write(value);
            copy.Write(value);
        }

        public override void Write(string? value)
        {
            console.Write(value);
            copy.Write(value);
        }

        public override void WriteLine(string? value)
        {
            console.WriteLine(value);
            copy.WriteLine(value);
        }

        public override void Flush() => console.Flush();
    }

    /// <summary>
    /// Verifica un sobre sellado. Sin contraseña se ejecuta el nivel público, que es el que
    /// se lleva al expediente: dice si el sobre es el que se selló y quién lo firmó, sin
    /// tocar el contenido. Con contraseña se agrega el veredicto por archivo.
    /// </summary>
    private async Task<int> VerifySealedAsync(string inputPath, string? password, string? packagePassword)
    {
        if (!File.Exists(inputPath))
        {
            // Un archivo que no está no es un archivo alterado: el código de salida tiene que
            // distinguir "no lo encontré" de "lo encontré y no es el que se selló".
            WriteColored(ConsoleColor.Red, $"Error: no se encontró el archivo: {inputPath}");
            return 1;
        }

        Console.WriteLine($"Verificando sobre sellado: {inputPath}");

        var result = await _sealedVerificationService.VerifyAsync(
            inputPath, password, null, CancellationToken.None, packagePassword);

        if (!result.FormatValid)
        {
            WriteColored(ConsoleColor.Red, $"\n[X] {result.FormatError}");
            return 3;
        }

        Console.WriteLine($"  Identificador     : {result.PackageId:D}");
        Console.WriteLine($"  Sellado (UTC)     : {result.SealedAtUtc:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Tamaño            : {result.SealedSize} bytes");
        Console.WriteLine($"  SHA-256           : {result.SealedSha256}");

        Console.WriteLine();
        if (result.ReceiptPresent)
        {
            WriteMatch("Huella contra el recibo", result.SealedHashMatches);
            WriteMatch("Tamaño contra el recibo", result.SealedSizeMatches);
            WriteMatch("Identificador contra el recibo", result.PackageIdMatches);
        }
        else
        {
            WriteColored(ConsoleColor.Yellow, $"[!] {result.ReceiptError}");
        }

        ReportSignature(result.Signature);

        if (result.Content != null)
        {
            Console.WriteLine();
            Console.WriteLine("Contenido del sobre:");
            foreach (var entry in result.Content.Entries)
            {
                var (color, tag) = StatusTag(entry.Status);
                WriteColored(color, $"  [{tag}] {entry.EntryName}");
            }

            Console.WriteLine($"Resumen: {result.Content.OkCount} OK, {result.Content.AlteredCount} alterados, " +
                              $"{result.Content.MissingCount} faltantes, {result.Content.ExtraCount} añadidos.");
            if (result.Content.ZipHashMatches.HasValue)
            {
                Console.WriteLine($"Hash global del {ArchiveFormats.DisplayName(result.Content.ArchiveFormat)}: " +
                                  $"{(result.Content.ZipHashMatches.Value ? "coincide" : "NO coincide")}.");
            }

            if (result.Content.ContentVerificationError != null)
            {
                // Antes solo se decía "no pudo verificarse", sin el motivo.
                PrintContentError(result.Content);
                if (result.Content.ZipHashMatches == true)
                {
                    WriteColored(ConsoleColor.Yellow,
                        "    Si el paquete se cifró con otra clave que la del sobre, indíquela con --package-password.");
                }
            }
        }
        else if (result.ContentError != null)
        {
            WriteColored(ConsoleColor.Yellow, $"\n[!] No se pudo llegar al contenido: {result.ContentError}");
        }
        else if (password == null)
        {
            Console.WriteLine("\nVerificación pública: el contenido no se abrió (no hace falta la clave para esto).");
        }

        if (result.UndeletedTempPath != null)
        {
            WriteColored(ConsoleColor.Yellow,
                $"[!] No se pudo borrar el temporal '{result.UndeletedTempPath}'. Bórrelo a mano.");
        }

        Console.WriteLine();
        if (result.IsIntact)
        {
            // Sin recibo no hay contra qué comparar el sobre: decir "es el mismo archivo que se selló" sería
            // afirmar lo que no se comprobó.
            if (!result.ReceiptPresent)
            {
                WriteColored(ConsoleColor.Yellow, result.Content != null
                    ? "[!] Contenido verificado contra el manifiesto, pero sin recibo no se puede acreditar que sea el sobre que se selló."
                    : "[!] Sobre bien formado, pero sin recibo no se puede acreditar que sea el que se selló: el veredicto es solo sobre el formato.");
                return 2;
            }

            var alcance = result.Content != null
                ? "sobre íntegro y contenido verificado contra el manifiesto."
                : "sobre íntegro: es el mismo archivo que se selló.";
            WriteColored(ConsoleColor.Green, $"[✓] {alcance}");
            return 0;
        }

        if (result.TamperDetected)
        {
            WriteColored(ConsoleColor.Red, "[X] SOBRE ALTERADO: no es el archivo que se selló.");
            if (result.AlteredChunkIndex.HasValue)
            {
                Console.WriteLine($"    La alteración está en el bloque {result.AlteredChunkIndex.Value}.");
            }

            return 3;
        }

        WriteColored(ConsoleColor.Yellow, "[!] VERIFICACIÓN INCOMPLETA: no se detectaron alteraciones del sobre,");
        WriteColored(ConsoleColor.Yellow, "    pero el contenido no pudo verificarse.");
        return 2;
    }

    /// <summary>
    /// Prueba un archivo comprimido sin manifiesto, como <c>7z t</c>: en memoria, sin escribir nada. Un autoextraíble se
    /// lee, no se ejecuta. Código 0 sin errores, 3 dañado, 2 si no se pudo probar todo (contraseña, TAR o ISO sin
    /// controles, un volumen que falta).
    /// </summary>
    private async Task<int> TestArchiveAsync(string path, string? password)
    {
        Console.WriteLine($"Probando el archivo (sin manifiesto): {path}");
        var result = await _zipService.TestAsync(path, password, null, CancellationToken.None);

        if (!string.IsNullOrEmpty(result.Format))
        {
            Console.WriteLine($"  Formato: {result.Format}");
        }

        if (result.IsSelfExtracting)
        {
            Console.WriteLine("  Autoextraíble: se leyó como archivo comprimido, no se ejecutó.");
        }

        Console.WriteLine();
        foreach (var entry in result.Entries)
        {
            WriteColored(ConsoleColor.Green, $"  [OK      ] {entry.Name}");
        }

        foreach (var damaged in result.Damaged)
        {
            WriteColored(ConsoleColor.Red, $"  [DAÑADO  ] {damaged.Name}: {damaged.Reason}");
        }

        if (result.Outcome is ArchiveTestOutcome.NoErrors or ArchiveTestOutcome.Damaged or ArchiveTestOutcome.NoChecks)
        {
            Console.WriteLine($"Resumen: {result.FilesOk} sin errores, {result.Damaged.Count} dañado(s).");
            if (result.Skipped.Count > 0)
            {
                Console.WriteLine($"{result.Skipped.Count} enlace(s), dispositivo(s) o flujo(s) NTFS sin contenido que probar.");
            }
        }

        foreach (var (fileName, size, sha256) in result.ArchiveFiles)
        {
            Console.WriteLine($"SHA-256 de {fileName} ({size} bytes): {sha256}");
        }

        Console.WriteLine();
        switch (result.Outcome)
        {
            case ArchiveTestOutcome.NoErrors:
                WriteColored(ConsoleColor.Green, "[✓] SIN ERRORES: cada archivo pasó su propio control.");
                WriteColored(ConsoleColor.Yellow, "    Sin manifiesto, eso no prueba que sea el original: anote la huella SHA-256.");
                return 0;
            case ArchiveTestOutcome.Damaged:
                WriteColored(ConsoleColor.Red, "[X] ARCHIVO DAÑADO: hay contenido que no pasa su propio control (dañado o alterado).");
                if (result.Error != null)
                {
                    Console.WriteLine($"    {result.Error}");
                }

                return 3;
            case ArchiveTestOutcome.NoChecks:
                WriteColored(ConsoleColor.Yellow, "[!] SE LEYÓ ENTERO, PERO EL FORMATO NO TRAE CONTROLES (TAR, ISO): una alteración no se puede detectar.");
                return 2;
            case ArchiveTestOutcome.PasswordRequired:
                WriteColored(ConsoleColor.Yellow, "[!] El archivo está cifrado: indique la contraseña con -p. No se probó nada.");
                return 2;
            case ArchiveTestOutcome.WrongPassword:
                WriteColored(ConsoleColor.Yellow, result.PasswordVerifiedWrong
                    ? "[!] La contraseña es incorrecta (el archivo guarda un valor de chequeo y no coincide). No se probó nada."
                    : "[!] La contraseña no abre el archivo, o el primer archivo cifrado está dañado: no se distinguen.");
                return 2;
            default:
                WriteColored(ConsoleColor.Yellow, $"[!] No se pudo probar: {result.Error}");
                return 2;
        }
    }

    private static void WriteMatch(string label, bool? matches)
    {
        if (matches == null)
        {
            Console.WriteLine($"  {label}: sin dato.");
            return;
        }

        WriteColored(matches.Value ? ConsoleColor.Green : ConsoleColor.Red,
            $"  {label}: {(matches.Value ? "coincide" : "NO coincide")}");
    }

    /// <summary>
    /// Informa la firma con el mismo criterio que la verificación de manifiestos de 1.2.0:
    /// una firma inválida es evidencia comprometida, no una advertencia.
    /// </summary>
    private void ReportSignature(SignatureInfo? signature)
    {
        if (signature is not { Present: true })
        {
            Console.WriteLine("  Firma del recibo: ausente (recibo sin firmar).");
            return;
        }

        WriteColored(signature.Valid ? ConsoleColor.Green : ConsoleColor.Red,
            $"  Firma del recibo: {(signature.Valid ? "VÁLIDA" : "INVÁLIDA")}");

        if (!string.IsNullOrEmpty(signature.SignerSubject))
        {
            Console.WriteLine($"    Firmante: {signature.SignerSubject}");
        }

        PrintSignerIdentity(signature, "    ");

        if (signature.TimestampUtc.HasValue)
        {
            WriteColored(signature.TimestampValid == true ? ConsoleColor.Green : ConsoleColor.Red,
                $"    Sello de tiempo RFC 3161 (UTC): {signature.TimestampUtc.Value:yyyy-MM-ddTHH:mm:ssZ} " +
                $"({(signature.TimestampValid == true ? "VÁLIDO" : "NO VÁLIDO")})");

            if (!string.IsNullOrEmpty(signature.TimestampAuthority))
            {
                Console.WriteLine($"    Autoridad de sellado (TSA): {signature.TimestampAuthority}");
            }
        }
        else if (signature.SignedAtUtc.HasValue)
        {
            Console.WriteLine($"    Fecha declarada por el firmante (UTC): {signature.SignedAtUtc.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }
    }

    private int VerifyReportIntegrity(string reportPath)
    {
        Console.WriteLine($"Verificando integridad del informe: {reportPath}");
        var (status, details) = _reportService.CheckReport(reportPath);

        switch (status)
        {
            case ReportIntegrity.Valid:
                WriteColored(ConsoleColor.Green, $"[✓] INFORME VERIFICADO: {details}");
                return 0;
            case ReportIntegrity.Unverifiable:
                // Sin .sha256 no hay contra qué comparar: no es una alteración y no se dice como tal.
                WriteColored(ConsoleColor.Yellow, $"[!] VERIFICACIÓN INCOMPLETA: {details}");
                return 2;
            default:
                WriteColored(ConsoleColor.Red, $"[X] INFORME ALTERADO: {details}");
                return 3;
        }
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

        if (result.MissingVolumes.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Resumen: {result.OkCount} OK, {result.AlteredCount} alterados, " +
                              $"{result.MissingCount} faltantes, {result.ExtraCount} añadidos.");
        }
        if (result.UnverifiedCount > 0)
        {
            WriteColored(ConsoleColor.Yellow,
                $"{result.UnverifiedCount} archivo(s) sin hash en el manifiesto: se constató que están, no su contenido.");
        }
        if (result.ZipHashMatches.HasValue)
        {
            Console.WriteLine($"Hash global del {ArchiveFormats.DisplayName(result.ArchiveFormat)}: {(result.ZipHashMatches.Value ? "coincide" : "NO coincide")}.");
        }

        if (result.Signature is { Present: true } sig)
        {
            WriteColored(sig.Valid ? ConsoleColor.Green : ConsoleColor.Red,
                $"Firma digital: {(sig.Valid ? "VÁLIDA" : "INVÁLIDA")}");
            if (!string.IsNullOrEmpty(sig.SignerSubject))
            {
                Console.WriteLine($"  Firmante: {sig.SignerSubject}");
            }
            PrintSignerIdentity(sig, "  ");
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
            PrintContentError(result);
        }

        if (result.IsIntact)
        {
            WriteColored(ConsoleColor.Green, "\n[✓] EVIDENCIA ÍNTEGRA: todos los archivos coinciden con el manifiesto.");
            return 0;
        }

        // Distinguir manipulación detectada de una verificación que no pudo completarse.
        if (result.TamperDetected)
        {
            WriteColored(ConsoleColor.Red, "\n[X] EVIDENCIA COMPROMETIDA: se detectaron discrepancias.");
            return 3;
        }

        WriteColored(ConsoleColor.Yellow, "\n[!] VERIFICACIÓN INCOMPLETA: no se detectaron alteraciones, pero el contenido no pudo verificarse por completo.");
        return 2;
    }

    /// <summary>
    /// Motivo por el que no se pudo leer el contenido y qué dice el hash global, que se compara aparte: intacto
    /// (falla la contraseña) o alterado.
    /// </summary>
    private static void PrintContentError(ArchiveVerificationResult result)
    {
        WriteColored(ConsoleColor.Yellow,
            $"\n[!] No se pudo verificar el contenido: {result.ContentVerificationError}");
        if (result.MissingVolumes.Count > 0)
        {
            // El motivo ya nombra lo que falta; la pista de la contraseña no aplica.
            return;
        }

        var (hintColor, hint) = result.ZipHashMatches switch
        {
            true => (ConsoleColor.Yellow,
                "    El paquete coincide con su hash registrado: no fue alterado. Lo más probable es que falte la " +
                "contraseña (-p) o que no sea la correcta."),
            false => (ConsoleColor.Red,
                "    El hash global del paquete NO coincide con el registrado: el paquete fue alterado o está dañado."),
            null => (ConsoleColor.Yellow, "    (¿falta la contraseña con -p, o el archivo está corrupto?)")
        };
        WriteColored(hintColor, hint);
        if (result.Volumes is { Count: > 0 })
        {
            WriteColored(ConsoleColor.Yellow, "    La verificación por volumen sí se realizó.");
        }
    }

    private static (ConsoleColor color, string tag) StatusTag(FileVerificationStatus status) => status switch
    {
        FileVerificationStatus.Ok => (ConsoleColor.Green, "OK      "),
        FileVerificationStatus.Altered => (ConsoleColor.Red, "ALTERADO"),
        FileVerificationStatus.Missing => (ConsoleColor.Red, "FALTANTE"),
        FileVerificationStatus.Extra => (ConsoleColor.Yellow, "AÑADIDO "),
        FileVerificationStatus.Unverified => (ConsoleColor.Yellow, "SIN HASH"),
        _ => (ConsoleColor.Gray, "?       ")
    };

    private static void WriteColored(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
