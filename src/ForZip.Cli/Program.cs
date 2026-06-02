// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Cli.Commands;
using ForZip.Core.Interfaces;
using ForZip.Core.Services;

namespace ForZip.Cli;

internal static class Program
{
    private static ILocalizationService _localization = null!;
    private static ILogService _logService = null!;
    private static IZipService _zipService = null!;
    private static IHashService _hashService = null!;
    private static IReportService _reportService = null!;
    private static IPasswordService _passwordService = null!;
    private static IVerificationService _verificationService = null!;
    private static ISignatureService _signatureService = null!;

    public static async Task<int> Main(string[] args)
    {
        InitializeServices();
        var parser = new CommandParser(args);

        if (args.Length == 0 || parser.HasOption("-h", "--help"))
        {
            ShowHelp();
            return 0;
        }

        if (parser.HasOption("-v", "--version"))
        {
            Console.WriteLine($"ForZip v1.0.0");
            return 0;
        }

        try
        {
            return parser.Command?.ToLower() switch
            {
                "zip" => await new ZipCommand(_zipService, _reportService, _hashService, _signatureService, _localization).ExecuteAsync(parser),
                "unzip" => await new UnzipCommand(_zipService, _localization).ExecuteAsync(parser),
                "hash" => await new HashCommand(_hashService, _reportService, _localization).ExecuteAsync(parser),
                "verify" => await new VerifyCommand(_reportService, _verificationService, _localization).ExecuteAsync(parser),
                "genpass" => new GenPassCommand(_passwordService, _localization).Execute(parser),
                _ => UnknownCommand(parser.Command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] {ex.Message}");
            Console.ResetColor();
            return 2; // Operation error
        }
    }

    private static void InitializeServices()
    {
        _localization = new LocalizationService();
        _logService = new LogService(); // CLI might use it for file logging too
        _hashService = new HashService();
        _passwordService = new PasswordService();
        _zipService = new ZipService(_hashService);
        _reportService = new ReportService(_localization);
        _signatureService = new SignatureService();
        _verificationService = new VerificationService(_hashService, _signatureService);
    }

    private static int UnknownCommand(string? cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Comando desconocido: {cmd}");
        Console.ResetColor();
        Console.WriteLine("Use 'forzip --help' para ver los comandos disponibles.");
        return 1;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("=============================================================================");
        Console.WriteLine("  ForZip — Herramienta Forense de Compresión y Verificación");
        Console.WriteLine("=============================================================================");
        Console.WriteLine("\nUso: forzip <comando> [opciones]");
        Console.WriteLine("\nComandos:");
        Console.WriteLine("  zip      Crea un archivo ZIP (opcionalmente con hash, informe y manifiesto).");
        Console.WriteLine("  unzip    Extrae el contenido de un archivo ZIP.");
        Console.WriteLine("  hash     Calcula hashes de uno o más archivos.");
        Console.WriteLine("  verify   Verifica un informe (.sha256) o la evidencia contra su manifiesto.");
        Console.WriteLine("  genpass  Genera una contraseña aleatoria segura.");
        Console.WriteLine("\nOpciones de zip/hash:");
        Console.WriteLine("  --operator <nombre>  Nombre del operador para la cadena de custodia.");
        Console.WriteLine("  --case <nro>         Número de caso (solo zip).");
        Console.WriteLine("  --court <juzgado>    Juzgado interviniente (solo zip).");
        Console.WriteLine("  --lang <es|en>       Idioma del informe (por defecto, el del sistema).");
        Console.WriteLine("  --no-sidecar         No generar el archivo de integridad .sha256.");
        Console.WriteLine("  --sign-cert <pfx>    Firma el manifiesto (CMS) con un certificado X.509 (solo zip).");
        Console.WriteLine("  --sign-cert-password Contraseña del PFX, si la tiene.");
        Console.WriteLine("\nEjemplos:");
        Console.WriteLine("  forzip zip -i evidencia.dd -o evidencia.zip --hash sha256 --report r.txt --operator \"J. Pérez\"");
        Console.WriteLine("  forzip unzip -i evidencia.zip -o ./resultado -p secreto123");
        Console.WriteLine("  forzip hash -i *.* --algo sha256,md5");
        Console.WriteLine("  forzip verify -r informe.txt");
        Console.WriteLine("  forzip verify -m evidencia.zip.manifest.json -z evidencia.zip");
        Console.WriteLine("\nGlobal:");
        Console.WriteLine("  -h, --help     Muestra esta ayuda.");
        Console.WriteLine("  -v, --version  Muestra la versión de la aplicación.");
        Console.WriteLine("=============================================================================");
    }
}
