// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Cli.Commands;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
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
    private static ISealingService _sealingService = null!;
    private static IReceiptService _receiptService = null!;
    private static ISealedPackageBuilder _sealedPackageBuilder = null!;
    private static ISealedVerificationService _sealedVerificationService = null!;
    private static IDeliveryActService _actService = null!;
    private static ICloudSyncDetector _cloudSyncDetector = null!;

    public static async Task<int> Main(string[] args)
    {
        InitializeServices();
        var parser = new CommandParser(args);

        // El parser trata el primer argumento como comando, por lo que "-h"/"-v" en esa
        // posición no aparecen como opciones: se contemplan acá explícitamente.
        var command = parser.Command?.ToLower();

        if (args.Length == 0 || command is "-h" or "--help" or "help" || parser.HasOption("-h", "--help"))
        {
            ShowHelp();
            return 0;
        }

        if (command is "-v" or "--version" || parser.HasOption("-v", "--version"))
        {
            Console.WriteLine($"ForZip {ForZip.Core.AppInfo.DisplayVersion}");
            return 0;
        }

        // Las licencias van dentro del ejecutable: la CLI puede circular sin los archivos que la acompañan.
        if (command is "--licenses" || parser.HasOption("--licenses", "--licenses"))
        {
            Console.Write(LicenseTexts.Combined());
            return 0;
        }

        // Una opción mal escrita no se ignora: se rechaza antes de hacer nada, con la lista de las válidas.
        if (parser.Command is { } cmd && CliOptions.Unknown(cmd, parser.OptionNames) is { Count: > 0 } unknown)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: opción desconocida para '{cmd}': {string.Join(", ", unknown)}. No se hizo nada.");
            Console.ResetColor();
            Console.WriteLine($"Opciones válidas: {string.Join(" ", CliOptions.ByCommand[cmd])}");
            return 1;
        }

        if (CliOptions.MissingValue(parser.OptionsWithoutValue) is { Count: > 0 } missing)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: falta el valor de {string.Join(", ", missing)}. No se hizo nada.");
            Console.ResetColor();
            Console.WriteLine("Cada una de esas opciones necesita un valor a continuación (por ejemplo: --split 700M).");
            return 1;
        }

        // Una opción repetida es ambigua: antes ganaba la última sin aviso. Solo -i de zip se repite (varios orígenes).
        if (parser.Command is { } repeatCmd && CliOptions.Repeated(repeatCmd, parser.OptionOccurrences) is { Count: > 0 } repeated)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {string.Join(", ", repeated)} se indicó más de una vez (con el mismo nombre o con su otro nombre). No se hizo nada.");
            Console.ResetColor();
            Console.WriteLine("Indique cada opción una sola vez. Para empaquetar varios orígenes: forzip zip -i <a> -i <b> -o <paquete>.");
            return 1;
        }

        try
        {
            return parser.Command?.ToLower() switch
            {
                "zip" => await new ZipCommand(_zipService, _reportService, _hashService, _signatureService, _sealedPackageBuilder, _passwordService, _actService, _localization).ExecuteAsync(parser),
                "unzip" => await new UnzipCommand(_zipService, _localization).ExecuteAsync(parser),
                "hash" => await new HashCommand(_hashService, _reportService, _localization).ExecuteAsync(parser),
                "verify" => await new VerifyCommand(_reportService, _verificationService, _sealedVerificationService, _localization, new ConfigService(), _zipService).ExecuteAsync(parser),
                "seal" => await new SealCommand(_sealedPackageBuilder, _passwordService, _actService, _cloudSyncDetector, _localization).ExecuteAsync(parser),
                "unseal" => await new UnsealCommand(new SealedPackageOpener(_sealingService, _verificationService), _cloudSyncDetector).ExecuteAsync(parser),
                "sign" => await new SignCommand(_signatureService, _localization).ExecuteAsync(parser),
                "genpass" => new GenPassCommand(_passwordService, _localization).Execute(parser),
                _ => UnknownCommand(parser.Command)
            };
        }
        catch (SecuritySoftwareBlockedException blocked)
        {
            var (message, logSummary, logDetail) = SecuritySoftwareInterference.Describe(blocked, _localization);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] {message}");
            Console.ResetColor();
            Console.WriteLine($"        {logSummary}");
            Console.WriteLine($"        {logDetail}");
            return 2;
        }
        catch (Exception ex) when (SecuritySoftwareInterference.IsBlocked(ex))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] {_localization.Get("error_security_software_blocked")}");
            Console.ResetColor();
            return 2;
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

        // Perfil Reservado: el sobre sellado y su recibo (1.5.0).
        _sealingService = new SealingService();
        _receiptService = new ReceiptService(_hashService, _signatureService);
        _sealedPackageBuilder = new SealedPackageBuilder(_sealingService, _receiptService);
        _sealedVerificationService = new SealedVerificationService(
            _sealingService, _receiptService, _signatureService, _verificationService, _hashService);
        _actService = new DeliveryActService(_localization);
        _cloudSyncDetector = new CloudSyncDetector();
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
        Console.WriteLine("  zip      Crea un archivo ZIP o 7z (con --hash, también su manifiesto).");
        Console.WriteLine("  unzip    Extrae un ZIP, 7z, RAR, TAR, GZ, BZ2, XZ o ISO (lo reconoce por su firma).");
        Console.WriteLine("  hash     Calcula hashes de uno o más archivos.");
        Console.WriteLine("  verify   Verifica la evidencia contra su manifiesto (y, si se pide, escribe el informe).");
        Console.WriteLine("  sign     Firma un manifiesto existente (CMS) con un certificado X.509.");
        Console.WriteLine("  genpass  Genera una contraseña aleatoria segura.");
        Console.WriteLine("  seal     Encierra un paquete ya generado en un sobre sellado .fzs + recibo.");
        Console.WriteLine("  unseal   Abre un sobre .fzs y vuelca el paquete que tiene adentro.");
        Console.WriteLine("\nOpciones de zip/hash:");
        Console.WriteLine("  --operator <nombre>  Nombre del operador para la cadena de custodia.");
        Console.WriteLine("  --case <nro>         Número de caso (solo zip).");
        Console.WriteLine("  --court <dependencia> Dependencia judicial interviniente: juzgado, fiscalía, etc. (solo zip).");
        Console.WriteLine("  --lang <es|en>       Idioma del informe (por defecto, el del sistema).");
        Console.WriteLine("  --hash <algos>       Hashes por archivo (ej: sha256,md5). En zip, además genera el manifiesto");
        Console.WriteLine("                       caso.zip.manifest.json, que es contra lo que se verifica después.");
        Console.WriteLine("  --report <archivo>   Escribe también el informe forense (zip: opcional; no en perfil");
        Console.WriteLine("                       Reservado). Con .html sale como página imprimible; con otra extensión,");
        Console.WriteLine("                       en texto. Después se puede armar con verify --write-report.");
        Console.WriteLine("  --no-sidecar         No generar el .sha256 del informe (solo hash).");
        Console.WriteLine("  --sign-cert <pfx>    Firma el manifiesto (CMS) con un certificado X.509 (solo zip).");
        Console.WriteLine("  --sign-cert-password Contraseña del PFX, si la tiene.");
        Console.WriteLine("  --timestamp-url <url> Sella la firma con una TSA RFC 3161 (ej: http://timestamp.digicert.com).");
        Console.WriteLine("  --format <zip|7z>    Formato del paquete (solo zip). Sin esta opción lo decide la extensión");
        Console.WriteLine("                       de -o (caso.7z → 7z); si no, ZIP. 7z: LZMA2, cada archivo por separado,");
        Console.WriteLine("                       todos los núcleos; con contraseña cifra también los nombres.");
        Console.WriteLine("  --split <tamaño>     Divide el paquete en volúmenes .001/.002 (ej: 700M, 4096MB, 1.5G).");
        Console.WriteLine("  --profile <perfil>   'standard' (por defecto) o 'reserved' (solo zip; siempre en formato ZIP).");
        Console.WriteLine();
        Console.WriteLine("Perfil Reservado (--profile reserved):");
        Console.WriteLine("  Empaqueta como siempre y después encierra el paquete completo —ZIP, manifiesto");
        Console.WriteLine("  y firma— en un sobre .fzs opaco, del que sale un recibo");
        Console.WriteLine("  público que prueba integridad y autoría sin revelar nada del contenido.");
        Console.WriteLine("  A la nube se sube SOLO el .fzs. El nombre del sobre lo decide ForZip: se deriva");
        Console.WriteLine("  de un identificador aleatorio, nunca de la causa ni de la evidencia.");
        Console.WriteLine("  --acta <ruta>        Obligatorio: la contraseña se genera y solo queda escrita ahí.");
        Console.WriteLine("  --desc <carátula>    Carátula de la causa, para el acta.");
        Console.WriteLine("  --operator-title / --operator-org   Cargo y organismo, para el acta.");
        Console.WriteLine();
        Console.WriteLine("Opciones de seal/unseal/verify de sobres:");
        Console.WriteLine("  --gen-password       Genera la contraseña del sobre (seal; requiere --acta).");
        Console.WriteLine($"  -n, --length <n>     Longitud de la contraseña generada (mínimo {SealedPasswordPolicy.MinLength}, por defecto {SealedPasswordPolicy.DefaultLength}).");
        Console.WriteLine("  --delete-sources     Borra los artefactos originales una vez sellados (seal).");
        Console.WriteLine("  --allow-cloud-folder Permite escribir en una carpeta sincronizada con la nube.");
        Console.WriteLine("                       Sin este flag, unseal aborta en vez de dejar la evidencia");
        Console.WriteLine("                       en claro donde se sube sola.");
        Console.WriteLine("  Omita -p/--password y ForZip la pedirá por consola sin mostrarla: escrita en la");
        Console.WriteLine("  línea de comandos queda en el historial del shell y en la lista de procesos.");
        Console.WriteLine("\nOpciones de genpass:");
        Console.WriteLine($"  -n, --length <n>     Longitud de la contraseña (mínimo {PasswordOptions.MinLength}, máximo {PasswordOptions.MaxLength}; por defecto {PasswordOptions.DefaultLength}).");
        Console.WriteLine("  -s, --symbols        Incluye símbolos además de letras y dígitos.");
        Console.WriteLine("  --no-upper           Excluye mayúsculas.");
        Console.WriteLine("  --no-lower           Excluye minúsculas.");
        Console.WriteLine("  --no-digits          Excluye dígitos.");
        Console.WriteLine($"  Los caracteres ambiguos ({PasswordOptions.AmbiguousChars}) se excluyen siempre; no es configurable.");
        Console.WriteLine("\nEjemplos:");
        Console.WriteLine("  forzip zip -i evidencia.dd -o evidencia.zip --hash sha256 --operator \"J. Pérez\"");
        Console.WriteLine("  forzip zip -i evidencia.dd -o evidencia.zip --hash sha256 --report informe.txt --split 700M");
        Console.WriteLine("  forzip zip -i ./evidencia -o caso.7z --hash sha256 -p secreto123");
        Console.WriteLine("  forzip zip -i acta.pdf -i ./fotos -o caso.zip --hash sha256   (varios orígenes: -o obligatorio)");
        Console.WriteLine("  forzip verify -m caso.zip.manifest.json --write-report caso.informe.html");
        Console.WriteLine("  forzip unzip -i evidencia.zip.001 -o ./resultado     (los .002, .003… se detectan solos)");
        Console.WriteLine("  forzip unzip -i evidencia.zip -o ./resultado -p secreto123");
        Console.WriteLine("  forzip hash -i *.* --algo sha256,md5");
        Console.WriteLine("  forzip verify -r informe.txt");
        Console.WriteLine("  forzip verify -m evidencia.zip.manifest.json -z evidencia.zip");
        Console.WriteLine("  forzip sign -m evidencia.zip.manifest.json -c operador.pfx -p clave -t http://timestamp.digicert.com");
        Console.WriteLine("  forzip zip -i ./evidencia -o caso.zip --profile reserved --acta acta.txt --case 4471/26");
        Console.WriteLine("  forzip seal -i ./paquete -o ./salida --gen-password --acta acta.txt");
        Console.WriteLine("  forzip verify -i FZS-8f3a1c9e.fzs                    (verificación pública, sin clave)");
        Console.WriteLine("  forzip verify -i FZS-8f3a1c9e.fzs -p clave           (verificación completa)");
        Console.WriteLine("  forzip unseal -i FZS-8f3a1c9e.fzs -o ./abierto       (pide la clave por consola)");
        Console.WriteLine("\nGlobal:");
        Console.WriteLine("  -h, --help     Muestra esta ayuda.");
        Console.WriteLine("  -v, --version  Muestra la versión de la aplicación.");
        Console.WriteLine("  --licenses     Muestra la licencia de ForZip y las de los componentes que incluye.");
        Console.WriteLine("=============================================================================");
    }
}
