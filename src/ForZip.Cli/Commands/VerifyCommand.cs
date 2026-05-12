// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;

namespace ForZip.Cli.Commands;

public class VerifyCommand
{
    private readonly IReportService _reportService;
    private readonly ILocalizationService _localization;

    public VerifyCommand(IReportService reportService, ILocalizationService localization)
    {
        _reportService = reportService;
        _localization = localization;
    }

    public Task<int> ExecuteAsync(CommandParser parser)
    {
        var reportPath = parser.GetOption("-r", "--report");

        if (string.IsNullOrEmpty(reportPath))
        {
            Console.WriteLine("Uso: forzip verify -r <informe.txt>");
            return Task.FromResult(1);
        }

        if (!File.Exists(reportPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: El archivo no existe: {reportPath}");
            Console.ResetColor();
            return Task.FromResult(1);
        }

        Console.WriteLine($"Verificando informe: {reportPath}");
        
        var (isValid, details) = _reportService.VerifyReport(reportPath);

        if (isValid)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[✓] INFORME VERIFICADO: El hash SHA-256 coincide con el contenido.");
            Console.ResetColor();
            return Task.FromResult(0);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[X] INFORME NO VERIFICADO: {details}");
            Console.ResetColor();
            return Task.FromResult(3); // Verification fail
        }
    }
}
