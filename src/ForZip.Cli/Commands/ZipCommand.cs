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
    private readonly ILocalizationService _localization;

    public ZipCommand(IZipService zipService, IReportService reportService, ILocalizationService localization)
    {
        _zipService = zipService;
        _reportService = reportService;
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

        if (!string.IsNullOrEmpty(reportFile) && algorithms.Count > 0)
        {
            var data = new ReportData
            {
                Operator = new OperatorInfo { Name = "CLI Operator" },
                Operation = OperationType.Compression,
                CompressionLevel = level,
                HasPassword = !string.IsNullOrEmpty(password),
                Algorithms = algorithms,
                ZipFilePath = output,
                ZipFileSize = new FileInfo(output).Length,
                FileResults = results
            };

            var content = _reportService.GenerateReport(data, "es");
            await _reportService.SaveReportAsync(content, reportFile);
            Console.WriteLine($"Informe forense generado: {reportFile}");
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
