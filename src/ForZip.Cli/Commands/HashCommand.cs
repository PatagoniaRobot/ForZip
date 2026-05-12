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

        Console.WriteLine($"Calculando hashes para {files.Count} archivos...");
        var results = new List<HashResult>();

        foreach (var file in files)
        {
            Console.Write($"Procesando: {Path.GetFileName(file)}... ");
            var result = await _hashService.ComputeHashesAsync(file, algorithms, null, CancellationToken.None);
            results.Add(result);
            Console.WriteLine("Listo.");
            
            foreach (var h in result.Hashes)
            {
                Console.WriteLine($"  {h.Key.ToString().PadRight(8)}: {h.Value}");
            }
        }

        if (!string.IsNullOrEmpty(reportFile))
        {
            var data = new ReportData
            {
                Operator = new OperatorInfo { Name = "CLI Operator" },
                Operation = OperationType.HashBatch,
                Algorithms = algorithms,
                FileResults = results
            };

            var content = _reportService.GenerateReport(data, "es");
            await _reportService.SaveReportAsync(content, reportFile);
            Console.WriteLine($"\nInforme exportado a: {reportFile}");
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
