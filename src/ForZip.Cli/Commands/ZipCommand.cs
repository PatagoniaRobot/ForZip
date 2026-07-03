// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

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
        var timestampUrl = parser.GetOption("--timestamp-url", "--tsa");
        var splitStr = parser.GetOption("--split", "--volume-size");

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
        long? splitSize = ParseSize(splitStr);

        var options = new ZipOptions
        {
            SourcePaths = new List<string> { input },
            OutputPath = output,
            CompressionLevel = level,
            Password = password,
            HashAlgorithms = algorithms,
            SplitSize = splitSize
        };

        Console.WriteLine($"Comprimiendo: {input} -> {output}");
        if (!string.IsNullOrEmpty(password)) Console.WriteLine("Cifrado: AES-256 activado.");
        if (splitSize.HasValue) Console.WriteLine($"División en volúmenes de {FormatSize(splitSize.Value)} (.001, .002, …).");
        
        var sw = Stopwatch.StartNew();
        var progress = new Progress<(long processed, long total)>(p =>
        {
            if (p.total > 0)
            {
                var percent = (int)(100.0 * p.processed / p.total);
                Console.Write($"\rProgreso: [{new string('#', percent / 5)}{new string('-', 20 - percent / 5)}] {percent}% ");
            }
        });

        var result = await _zipService.CompressAsync(options, progress, CancellationToken.None);
        sw.Stop();

        Console.WriteLine($"\n¡Éxito! Tiempo: {sw.Elapsed.TotalSeconds:F2}s");
        if (result.IsSplit)
        {
            Console.WriteLine($"Generados {result.Volumes.Count} volúmenes: " +
                              $"{result.Volumes[0].FileName} … {result.Volumes[^1].FileName}");
        }

        if (!string.IsNullOrEmpty(reportFile))
        {
            if (algorithms.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Aviso: sin --hash no se registran hashes por archivo; la verificación de contenido no será posible.");
                Console.ResetColor();
            }

            // Hash global y tamaño del ZIP lógico (concatenación de volúmenes si está dividido)
            var (zipHash, zipSize) = await ComputeLogicalZipHashAsync(output, result);

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
                ZipFileSize = zipSize,
                ZipHash = zipHash,
                Volumes = result.Volumes.ToList(),
                FileResults = result.FileHashes
            };

            var content = _reportService.GenerateReport(data, lang);
            await _reportService.SaveReportAsync(content, reportFile);
            Console.WriteLine($"Informe forense generado: {reportFile}");

            // Manifiesto JSON (fuente de verdad para verificación automática), junto al ZIP
            var manifestPath = output + ".manifest.json";
            await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));
            Console.WriteLine($"Manifiesto forense generado: {manifestPath}");

            // Firma digital del manifiesto (CMS/PKCS#7) con el certificado del operador,
            // con sello de tiempo RFC 3161 opcional (--timestamp-url)
            if (!string.IsNullOrEmpty(signCert))
            {
                try
                {
                    await _signatureService.SignAsync(manifestPath, signCert, signCertPassword, timestampUrl, CancellationToken.None);
                    Console.WriteLine($"Manifiesto firmado digitalmente: {manifestPath}.p7s");
                    if (!string.IsNullOrEmpty(timestampUrl))
                    {
                        Console.WriteLine($"Sello de tiempo RFC 3161 incluido (TSA: {timestampUrl}).");
                    }
                }
                catch (TimestampUnavailableException ex)
                {
                    Console.WriteLine($"Manifiesto firmado digitalmente: {manifestPath}.p7s");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Aviso: la firma se generó SIN sello de tiempo. {ex.Message}");
                    Console.ResetColor();
                }
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

    /// <summary>
    /// Calcula el SHA-256 y el tamaño del ZIP lógico. Para un archivo dividido, hashea la
    /// concatenación de los volúmenes (que ya no existe como archivo único en disco).
    /// </summary>
    private async Task<(string hash, long size)> ComputeLogicalZipHashAsync(string output, CompressionResult result)
    {
        if (result.IsSplit)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(output)) ?? string.Empty;
            var segments = result.Volumes.Select(v => Path.Combine(dir, v.FileName)).ToList();
            var size = result.Volumes.Sum(v => v.Size);

            await using var logical = new ConcatenatedReadStream(segments);
            var hashes = await _hashService.ComputeHashesAsync(
                logical, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, CancellationToken.None);
            return (hashes[HashAlgorithmType.SHA256], size);
        }

        var single = await _hashService.ComputeHashesAsync(
            output, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None);
        return (single.Hashes[HashAlgorithmType.SHA256], new FileInfo(output).Length);
    }

    /// <summary>
    /// Parsea un tamaño con sufijo opcional: <c>700M</c>, <c>4096MB</c>, <c>1.5G</c>, <c>100K</c>,
    /// o un número de bytes a secas. Devuelve <c>null</c> si no se especificó.
    /// </summary>
    private static long? ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var m = Regex.Match(text.Trim(), @"^(\d+(?:[.,]\d+)?)\s*(k|kb|m|mb|g|gb|b)?$", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            throw new ArgumentException($"Tamaño de volumen inválido: '{text}'. Ejemplos: 700M, 4096MB, 1.5G, 100K.");
        }

        var value = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        long multiplier = m.Groups[2].Value.ToLowerInvariant() switch
        {
            "k" or "kb" => 1024L,
            "m" or "mb" => 1024L * 1024,
            "g" or "gb" => 1024L * 1024 * 1024,
            _ => 1L
        };

        return (long)(value * multiplier);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double n = bytes;
        int u = 0;
        while (n >= 1024 && u < units.Length - 1)
        {
            n /= 1024;
            u++;
        }
        return $"{n.ToString("0.##", CultureInfo.InvariantCulture)} {units[u]}";
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
