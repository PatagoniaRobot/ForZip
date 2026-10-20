// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Services;

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
            Console.WriteLine("Uso: forzip hash -i <archivo | carpeta | patrón> [-a md5,sha256] [-r report.txt]");
            Console.WriteLine("     Una carpeta aporta todo lo que tiene adentro, recursivamente (sin seguir enlaces).");
            return 1;
        }

        if (!CliOptions.TryParseAlgorithms(algoStr, out var algorithms, out var algoError) || algorithms.Count == 0)
        {
            Console.WriteLine($"Error: {algoError ?? "Debe especificar al menos un algoritmo válido."}");
            return 1;
        }

        // Como en Hash Batch: el informe y su .sha256 dentro de la carpeta hasheada se mezclarían con la evidencia.
        if (!string.IsNullOrEmpty(reportFile) && Directory.Exists(input)
            && (PackageOutput.FindSourceContaining(new[] { input }, reportFile) != null
                || PackageOutput.FindSourceContaining(new[] { input }, reportFile + ".sha256") != null))
        {
            Console.WriteLine($"Error: el informe quedaría dentro de la carpeta que se hashea ({input}) y se mezclaría con la evidencia. Elija otra carpeta.");
            return 1;
        }

        var skipped = new List<OmittedFile>();
        var items = ResolveItems(input, skipped);
        if (items.Count == 0)
        {
            Console.WriteLine("Error: No se encontraron archivos coincidentes.");
            return 1;
        }

        Console.WriteLine($"Calculando hashes para {items.Count} archivos (en paralelo)...");

        // En paralelo y con el orden de entrada preservado. Un archivo que no se puede leer (antivirus, en uso,
        // borrado) no corta el lote: queda documentado como no leído, como en la GUI.
        var results = new HashResult?[items.Count];
        var failures = new OmittedFile?[items.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, token) =>
            {
                try
                {
                    var result = await _hashService.ComputeHashesAsync(items[i].FullPath, algorithms, null, token);
                    result.FilePath = items[i].DisplayName;
                    results[i] = result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures[i] = HashInventory.Unreadable(items[i].FullPath, items[i].DisplayName, ex);
                }
            });

        for (int i = 0; i < items.Count; i++)
        {
            Console.WriteLine($"{items[i].DisplayName}:");
            if (results[i] is { } r)
            {
                foreach (var h in r.Hashes)
                {
                    Console.WriteLine($"  {h.Key.ToString().PadRight(8)}: {h.Value}");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  NO SE PUDO LEER: {failures[i]!.SystemMessage}");
                Console.ResetColor();
            }
        }

        var omitted = failures.Where(f => f != null).Select(f => f!).Concat(skipped).ToList();
        foreach (var link in skipped)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ENLACE NO SEGUIDO: {link.EntryName} -> {link.LinkTarget}");
            Console.ResetColor();
        }

        // Una carpeta entera tiene huella de carpeta: la de ForZip (con nombres) y la "SHA256 for data" de 7-Zip.
        var folderDigests = new List<FolderDigest>();
        if (Directory.Exists(input))
        {
            if (!algorithms.Contains(HashAlgorithmType.SHA256))
            {
                Console.WriteLine("\nHuella de carpeta: se calcula con SHA-256 (agregue sha256 a -a).");
            }
            else
            {
                var files = items.Select((item, i) => (item.FullPath,
                        results[i]?.FileSize ?? failures[i]?.Size ?? 0L,
                        results[i] is { } r ? (string?)r.Hashes[HashAlgorithmType.SHA256] : null))
                    .ToList();
                folderDigests = FolderHash.Compute(new[] { input }, files, omitted);
                foreach (var folder in folderDigests)
                {
                    Console.WriteLine($"\nHuella de carpeta: {folder.FolderPath}");
                    Console.WriteLine($"  Archivos             : {folder.FileCount} ({folder.TotalSize} bytes)");
                    if (folder.IsComplete)
                    {
                        Console.WriteLine($"  Huella ForZip        : {folder.ForZipSha256}");
                        Console.WriteLine($"  7-Zip (data, SHA-256): {folder.SevenZipDataSha256}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  Sin huella: {folder.UnreadableCount} archivo(s) no se pudieron leer.");
                        Console.ResetColor();
                    }

                    if (folder.LinksNotFollowed > 0)
                    {
                        Console.WriteLine($"  {folder.LinksNotFollowed} enlace(s) no seguidos: no entran en la huella.");
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(reportFile))
        {
            var data = new ReportData
            {
                Operator = string.IsNullOrWhiteSpace(operatorName) ? null : new OperatorInfo { Name = operatorName },
                Operation = OperationType.HashBatch,
                Algorithms = algorithms,
                FileResults = results.Where(r => r != null).Select(r => r!).ToList(),
                OmittedFiles = omitted,
                FolderDigests = folderDigests
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

        // Código 3, como un paquete incompleto: hubo archivos que no se pudieron hashear.
        return failures.Any(f => f != null) ? ZipCommand.IncompleteExitCode : 0;
    }

    /// <summary>
    /// Un archivo, una carpeta (todo lo que tiene adentro, sin seguir enlaces) o un patrón (<c>C:\caso\*.jpg</c>).
    /// </summary>
    private static List<HashInventory.Item> ResolveItems(string input, List<OmittedFile> skipped)
    {
        if (File.Exists(input) || Directory.Exists(input))
        {
            return HashInventory.Expand(new[] { input }, skipped);
        }

        var dir = Path.GetDirectoryName(input);
        if (string.IsNullOrEmpty(dir))
        {
            dir = Directory.GetCurrentDirectory();
        }

        var filePart = Path.GetFileName(input);
        if (Directory.Exists(dir))
        {
            return Directory.GetFiles(dir, filePart)
                .Select(f => new HashInventory.Item(Path.GetFullPath(f), Path.GetFileName(f)))
                .ToList();
        }

        return new List<HashInventory.Item>();
    }
}
