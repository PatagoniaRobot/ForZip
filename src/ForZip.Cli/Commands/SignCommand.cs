// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;

namespace ForZip.Cli.Commands;

public class SignCommand
{
    private readonly ISignatureService _signatureService;
    private readonly ILocalizationService _localization;

    public SignCommand(ISignatureService signatureService, ILocalizationService localization)
    {
        _signatureService = signatureService;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var manifestPath = parser.GetOption("-m", "--manifest");
        var certPath = parser.GetOption("-c", "--cert");
        var certPassword = parser.GetOption("-p", "--cert-password");

        if (string.IsNullOrEmpty(manifestPath) || string.IsNullOrEmpty(certPath))
        {
            Console.WriteLine("Uso: forzip sign -m <manifiesto.json> -c <certificado.pfx> [-p <password>]");
            return 1;
        }

        if (!File.Exists(manifestPath))
        {
            WriteColored(ConsoleColor.Red, $"Error: el manifiesto no existe: {manifestPath}");
            return 1;
        }

        if (!File.Exists(certPath))
        {
            WriteColored(ConsoleColor.Red, $"Error: el certificado no existe: {certPath}");
            return 1;
        }

        try
        {
            await _signatureService.SignAsync(manifestPath, certPath, certPassword, CancellationToken.None);
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, $"Error al firmar: {ex.Message}");
            return 2;
        }

        WriteColored(ConsoleColor.Green, $"[✓] Manifiesto firmado: {manifestPath}.p7s");

        // Verificación inmediata como confirmación
        var info = _signatureService.Verify(manifestPath);
        if (info.Valid)
        {
            if (!string.IsNullOrEmpty(info.SignerSubject))
            {
                Console.WriteLine($"  Firmante: {info.SignerSubject}");
            }
            if (info.SignedAtUtc.HasValue)
            {
                Console.WriteLine($"  Fecha de firma (UTC): {info.SignedAtUtc.Value:yyyy-MM-ddTHH:mm:ssZ}");
            }
        }

        return 0;
    }

    private static void WriteColored(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
