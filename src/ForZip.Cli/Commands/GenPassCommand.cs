// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Cli.Commands;

public class GenPassCommand
{
    private readonly IPasswordService _passwordService;
    private readonly ILocalizationService _localization;

    public GenPassCommand(IPasswordService passwordService, ILocalizationService localization)
    {
        _passwordService = passwordService;
        _localization = localization;
    }

    public int Execute(CommandParser parser)
    {
        var lenStr = parser.GetOption("-n", "--length") ?? "16";
        int length = int.TryParse(lenStr, out var l) ? l : 16;

        var options = new PasswordOptions
        {
            Length = length,
            IncludeUppercase = !parser.HasOption("--no-upper", "--no-upper"),
            IncludeLowercase = !parser.HasOption("--no-lower", "--no-lower"),
            IncludeDigits = !parser.HasOption("--no-digits", "--no-digits"),
            IncludeSymbols = parser.HasOption("-s", "--symbols"),
            ExcludeAmbiguous = parser.HasOption("-a", "--avoid-ambiguous")
        };

        try
        {
            var password = _passwordService.GeneratePassword(options);
            Console.WriteLine(password);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
