// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using System.Diagnostics;

namespace ForZip.Cli.Commands;

public class ZipCommand
{
    private readonly IZipService _zipService;
    private readonly IReportService _reportService;
    private readonly IHashService _hashService;
    private readonly ISignatureService _signatureService;
    private readonly ILocalizationService _localization;

    public ZipCommand(
        IZipService zipService,
        IReportService reportService,
        IHashService hashService,
        ISignatureService signatureService,
        ILocalizationService localization)
    {
        _zipService = zipService;
        _reportService = reportService;
        _hashService = hashService;
        _signatureService = signatureService;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var input = parser.GetOption("-i", "--input");
        var output = parser.GetOption("-o", "--output");
        var password = parser.GetOption("-p", "--password");
        var levelStr = parser.GetOption("-l", "--level") ?? "5";
        var hashStr = parser.GetOption("--hash", "--hashes");
        var reportFile = parser.GetOption("--report", "--report-file");

        // Datos de cadena de custodia y preferencias (paridad con la GUI)
        var operatorName = parser.GetOption("--operator", "--operator-name");
        var caseNumber = parser.GetOption("--case", "--case-number");
        var court = parser.GetOption("--court", "--court");
        var lang = parser.GetOption("--lang", "--language") ?? _localization.CurrentLanguage;
        var noSidecar = parser.HasOption("--no-sidecar", "--no-sidecar");
        var signCert = parser.GetOption("--sign-cert", "--sign-cert");
        var signCertPassword = parser.GetOption("--sign-cert-password", "--sign-cert-password");

        if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine("Uso: forzip zip -i <input> [-o <output>] [opciones]");
            return 1;
        }

        if (string.IsNullOrEmpty(output))
        {
            var dir = Path.GetDirectoryName(input) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = "Evidencia";
            output = Path.Combine(dir, name + ".zip");
        }

        int level = int.TryParse(levelStr, out var l) ? l : 5;
        var algorithms = ParseAlgorithms(hashStr);

        var options = new ZipOptions
        {
            SourcePaths = new List<string> { input },
            OutputPath = output,
            CompressionLevel = level,
            Password = password,
            HashAlgorithms = algorithms
        };

        Console.WriteLine($"Comprimiendo: {input} -> {output}");
        if (!string.IsNullOrEmpty(password)) Console.WriteLine("Cifrado: AES-256 activado.");
        
        var sw = Stopwatch.StartNew();
        var progress = new Progress<(long processed, long total)>(p =>
        {
            if (p.total > 0)
            {
                var percent = (int)(100.0 * p.processed / p.total);
                Console.Write($"\rProgreso: [{new string('#', percent / 5)}{new string('-', 20 - percent / 5)}] {percent}% ");
            }
        });

        var results = await _zipService.CompressAsync(options, progress, CancellationToken.None);
        sw.Stop();

        Console.WriteLine($"\n¡Éxito! Tiempo: {sw.Elapsed.TotalSeconds:F2}s");

        if (!string.IsNullOrEmpty(reportFile))
        {
            if (algorithms.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Aviso: sin --hash no se registran hashes por archivo; la verificación de contenido no será posible.");
                Console.ResetColor();
            }

            // Hash global del ZIP (SHA-256), igual que la GUI
            var zipHashResult = await _hashService.ComputeHashesAsync(
                output, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None);

            var data = new ReportData
            {
                Operator = string.IsNullOrWhiteSpace(operatorName) ? null : new OperatorInfo { Name = operatorName },
                CaseNumber = caseNumber,
                Court = court,
                Operation = OperationType.Compression,
                CompressionLevel = level,
                HasPassword = !string.IsNullOrEmpty(password),
                Algorithms = algorithms,
                ZipFilePath = output,
                ZipFileSize = new FileInfo(output).Length,
                ZipHash = zipHashResult.Hashes[HashAlgorithmType.SHA256],
                FileResults = results
            };

            var content = _reportService.GenerateReport(data, lang);
            await _reportService.SaveReportAsync(content, reportFile);
            Console.WriteLine($"Informe forense generado: {reportFile}");

            // Manifiesto JSON (fuente de verdad para verificación automática), junto al ZIP
            var manifestPath = output + ".manifest.json";
            await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));
            Console.WriteLine($"Manifiesto forense generado: {manifestPath}");

            // Firma digital del manifiesto (CMS/PKCS#7) con el certificado del operador
            if (!string.IsNullOrEmpty(signCert))
            {
                await _signatureService.SignAsync(manifestPath, signCert, signCertPassword, CancellationToken.None);
                Console.WriteLine($"Manifiesto firmado digitalmente: {manifestPath}.p7s");
            }

            // Sidecar de integridad del informe (.sha256)
            if (!noSidecar)
            {
                var reportHash = await _hashService.ComputeHashesAsync(
                    reportFile, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None);
                var sidecarPath = reportFile + ".sha256";
                await File.WriteAllTextAsync(sidecarPath, $"{reportHash.Hashes[HashAlgorithmType.SHA256]}  {Path.GetFileName(reportFile)}");
                Console.WriteLine($"Archivo de integridad generado: {sidecarPath}");
            }
        }

        return 0;
    }

    private HashSet<HashAlgorithmType> ParseAlgorithms(string? hashStr)
    {
        var set = new HashSet<HashAlgorithmType>();
        if (string.IsNullOrEmpty(hashStr)) return set;

        var parts = hashStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (Enum.TryParse<HashAlgorithmType>(p, true, out var type))
                set.Add(type);
        }
        return set;
    }
}
