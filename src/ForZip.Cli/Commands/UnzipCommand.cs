// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;

namespace ForZip.Cli.Commands;

public class UnzipCommand
{
    private readonly IZipService _zipService;
    private readonly ILocalizationService _localization;

    public UnzipCommand(IZipService zipService, ILocalizationService localization)
    {
        _zipService = zipService;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var input = parser.GetOption("-i", "--input");
        var output = parser.GetOption("-o", "--output");
        var password = parser.GetOption("-p", "--password");

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
        {
            Console.WriteLine("Uso: forzip unzip -i <input.zip> -o <outputDir> [-p <password>]");
            return 1;
        }

        Console.WriteLine($"Extrayendo: {input} -> {output}");

        var progress = new Progress<(long processed, long total)>(p =>
        {
            if (p.total > 0)
            {
                var percent = (int)(100.0 * p.processed / p.total);
                Console.Write($"\rProgreso: [{new string('#', percent / 5)}{new string('-', 20 - percent / 5)}] {percent}% ");
            }
        });

        await _zipService.DecompressAsync(input, output, password, progress, CancellationToken.None);

        Console.WriteLine("\n¡Extracción completada exitosamente!");
        return 0;
    }
}
