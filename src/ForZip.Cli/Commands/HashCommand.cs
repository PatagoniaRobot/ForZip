// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Cli.Commands;

public class HashCommand
{
    private readonly IHashService _hashService;
    private readonly IReportService _reportService;
    private readonly ILocalizationService _localization;

    public HashCommand(IHashService hashService, IReportService reportService, ILocalizationService localization)
    {
        _hashService = hashService;
        _reportService = reportService;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var input = parser.GetOption("-i", "--input");
        var algoStr = parser.GetOption("-a", "--algo") ?? "sha256";
        var reportFile = parser.GetOption("-r", "--report");
        var operatorName = parser.GetOption("--operator", "--operator-name");
        var lang = parser.GetOption("--lang", "--language") ?? _localization.CurrentLanguage;
        var noSidecar = parser.HasOption("--no-sidecar", "--no-sidecar");

        if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine("Uso: forzip hash -i <file/pattern> [-a md5,sha256] [-r report.txt]");
            return 1;
        }

        var algorithms = ParseAlgorithms(algoStr);
        if (algorithms.Count == 0)
        {
            Console.WriteLine("Error: Debe especificar al menos un algoritmo válido.");
            return 1;
        }

        // Expandir patrones si es necesario (ej: *.txt)
        var files = ResolveFiles(input);
        if (files.Count == 0)
        {
            Console.WriteLine("Error: No se encontraron archivos coincidentes.");
            return 1;
        }

        Console.WriteLine($"Calculando hashes para {files.Count} archivos (en paralelo)...");

        // Cálculo en paralelo con concurrencia acotada; el orden de entrada se preserva
        var results = await _hashService.ComputeHashesBatchAsync(files, algorithms, 0, CancellationToken.None);

        for (int i = 0; i < files.Count; i++)
        {
            Console.WriteLine($"{Path.GetFileName(files[i])}:");
            foreach (var h in results[i].Hashes)
            {
                Console.WriteLine($"  {h.Key.ToString().PadRight(8)}: {h.Value}");
            }
        }

        if (!string.IsNullOrEmpty(reportFile))
        {
            var data = new ReportData
            {
                Operator = string.IsNullOrWhiteSpace(operatorName) ? null : new OperatorInfo { Name = operatorName },
                Operation = OperationType.HashBatch,
                Algorithms = algorithms,
                FileResults = results
            };

            var content = _reportService.GenerateReport(data, lang);
            await _reportService.SaveReportAsync(content, reportFile);
            Console.WriteLine($"\nInforme exportado a: {reportFile}");

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

    private List<string> ResolveFiles(string pattern)
    {
        if (File.Exists(pattern)) return new List<string> { pattern };
        
        var dir = Path.GetDirectoryName(pattern);
        if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
        
        var filePart = Path.GetFileName(pattern);
        if (Directory.Exists(dir))
        {
            return Directory.GetFiles(dir, filePart).ToList();
        }
        
        return new List<string>();
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
