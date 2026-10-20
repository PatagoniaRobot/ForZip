// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Models;

namespace ForZip.Cli;

/// <summary>
/// Opciones que acepta cada comando, y algoritmos de hash por nombre.
/// </summary>
/// <remarks>
/// Antes una opción mal escrita se ignoraba sin aviso: <c>zip -r informe.txt</c> no generaba informe (la opción es
/// <c>--report</c>) y <c>-a sha-256,md5</c> calculaba solo MD5. En una herramienta forense, hacer menos de lo
/// pedido sin decirlo es peor que negarse: ahora se rechaza con la lista de lo válido. Un test cruza esta tabla con
/// las opciones que leen los comandos, para que no se desincronicen.
/// </remarks>
public static class CliOptions
{
    private static readonly string[] Global = { "-h", "--help", "-v", "--version" };

    public static readonly IReadOnlyDictionary<string, string[]> ByCommand = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["zip"] = new[]
        {
            "-i", "--input", "-o", "--output", "-p", "--password", "-l", "--level", "--hash", "--hashes",
            "--report", "--report-file", "--lang", "--language", "--operator", "--operator-name",
            "--operator-org", "--operator-title", "--case", "--case-number", "--court", "--desc", "--description",
            "--sign-cert", "--sign-cert-password", "--timestamp-url", "--tsa", "--split", "--volume-size",
            "--format", "--profile", "--acta", "--act"
        },
        ["unzip"] = new[] { "-i", "--input", "-o", "--output", "-p", "--password" },
        ["hash"] = new[]
        {
            "-i", "--input", "-a", "--algo", "-r", "--report", "--operator", "--operator-name", "--lang", "--language",
            "--no-sidecar"
        },
        ["verify"] = new[]
        {
            "-r", "--report", "-m", "--manifest", "-i", "--input", "-z", "--zip", "-p", "--password", "--trusted-signer",
            "--package-password", "--write-report", "--lang", "--language"
        },
        ["seal"] = new[]
        {
            "-i", "--input", "-o", "--output", "-p", "--password", "--gen-password", "-n", "--length", "--acta", "--act",
            "--sign-cert", "--sign-cert-password", "--timestamp-url", "--tsa", "--delete-sources", "--allow-cloud-folder",
            "--case", "--case-number", "--desc", "--description", "--court", "--operator", "--operator-name",
            "--operator-title", "--operator-org", "--lang", "--language"
        },
        ["unseal"] = new[] { "-i", "--input", "-o", "--output", "-p", "--password", "--allow-cloud-folder" },
        ["sign"] = new[] { "-m", "--manifest", "-c", "--cert", "-p", "--cert-password", "-t", "--timestamp-url" },
        ["genpass"] = new[] { "-n", "--length", "-s", "--symbols", "--no-upper", "--no-lower", "--no-digits" }
    };

    /// <summary>Todas las opciones de todos los comandos: el parser no toma una de estas como valor de otra.</summary>
    public static readonly HashSet<string> All = new(
        ByCommand.Values.SelectMany(o => o).Concat(Global), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Las opciones que son interruptores: no llevan valor. Todas las demás lo necesitan, y una que llega sin él
    /// ("--split" al final, o "--split --hash sha256") se rechaza: antes se ignoraba en silencio y el paquete
    /// salía sin dividir, que no es lo que se pidió.
    /// </summary>
    public static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "-h", "--help", "-v", "--version", "--no-sidecar", "--gen-password", "--delete-sources",
        "--allow-cloud-folder", "-s", "--symbols", "--no-upper", "--no-lower", "--no-digits"
    };

    /// <summary>Opciones que necesitan un valor y llegaron sin él.</summary>
    public static List<string> MissingValue(IEnumerable<string> withoutValue) =>
        withoutValue.Where(o => !Flags.Contains(o)).ToList();

    /// <summary>Opciones presentes que el comando no conoce. Vacío si el comando no está en la tabla.</summary>
    public static List<string> Unknown(string command, IEnumerable<string> present)
    {
        if (!ByCommand.TryGetValue(command, out var known))
        {
            return new List<string>();
        }

        var accepted = new HashSet<string>(known.Concat(Global), StringComparer.OrdinalIgnoreCase);
        return present.Where(o => !accepted.Contains(o)).ToList();
    }

    /// <summary>
    /// Los dos nombres de una misma opción, por comando, tal como los leen los comandos (GetOption("-p",
    /// "--password")). En sign, "-p" es la contraseña del certificado. Un test cruza esta tabla con el código.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Short, string Long)[]> Synonyms =
        new Dictionary<string, (string, string)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["zip"] = new[]
            {
                ("-i", "--input"), ("-o", "--output"), ("-p", "--password"), ("-l", "--level"), ("--hash", "--hashes"),
                ("--report", "--report-file"), ("--lang", "--language"), ("--operator", "--operator-name"),
                ("--case", "--case-number"), ("--desc", "--description"), ("--timestamp-url", "--tsa"),
                ("--split", "--volume-size"), ("--acta", "--act")
            },
            ["unzip"] = new[] { ("-i", "--input"), ("-o", "--output"), ("-p", "--password") },
            ["hash"] = new[] { ("-i", "--input"), ("-a", "--algo"), ("-r", "--report"), ("--operator", "--operator-name"), ("--lang", "--language") },
            ["verify"] = new[]
            {
                ("-r", "--report"), ("-m", "--manifest"), ("-i", "--input"), ("-z", "--zip"), ("-p", "--password"),
                ("--lang", "--language")
            },
            ["seal"] = new[]
            {
                ("-i", "--input"), ("-o", "--output"), ("-p", "--password"), ("-n", "--length"), ("--acta", "--act"),
                ("--timestamp-url", "--tsa"), ("--case", "--case-number"), ("--desc", "--description"),
                ("--operator", "--operator-name"), ("--lang", "--language")
            },
            ["unseal"] = new[] { ("-i", "--input"), ("-o", "--output"), ("-p", "--password") },
            ["sign"] = new[] { ("-m", "--manifest"), ("-c", "--cert"), ("-p", "--cert-password"), ("-t", "--timestamp-url") },
            ["genpass"] = new[] { ("-n", "--length"), ("-s", "--symbols") }
        };

    /// <summary>
    /// Las opciones que se pueden repetir: en zip, -i, para empaquetar varios orígenes juntos como la selección
    /// múltiple de la GUI (archivos y carpetas en un solo paquete).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Repeatable = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["zip"] = new[] { "-i" }
    };

    /// <summary>
    /// Opciones indicadas más de una vez (contando sus dos nombres, "-p" y "--password") que el comando no deja
    /// repetir. Antes la última ganaba sin aviso: "-i a -i b" empaquetaba solo b, y "-p x -p y" cifraba con y.
    /// Devuelve cada una por su primer nombre.
    /// </summary>
    public static List<string> Repeated(string command, IEnumerable<string> occurrences)
    {
        var synonyms = Synonyms.TryGetValue(command, out var pairs) ? pairs : Array.Empty<(string Short, string Long)>();
        var repeatable = Repeatable.TryGetValue(command, out var r) ? r : Array.Empty<string>();

        string Canonical(string option)
        {
            foreach (var (shortName, longName) in synonyms)
            {
                if (option.Equals(shortName, StringComparison.OrdinalIgnoreCase) || option.Equals(longName, StringComparison.OrdinalIgnoreCase))
                {
                    return shortName;
                }
            }

            return option.ToLowerInvariant();
        }

        return occurrences.GroupBy(Canonical, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !repeatable.Contains(g.Key, StringComparer.OrdinalIgnoreCase))
            .Select(g => g.Key)
            .ToList();
    }

    /// <summary>
    /// Algoritmos por nombre, con o sin guion (sha256, SHA-256, sha_256…). Un nombre desconocido es un error, no
    /// un algoritmo menos.
    /// </summary>
    public static bool TryParseAlgorithms(string? text, out HashSet<HashAlgorithmType> algorithms, out string? error)
    {
        algorithms = new HashSet<HashAlgorithmType>();
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = part.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            HashAlgorithmType? algo = key switch
            {
                "md5" => HashAlgorithmType.MD5,
                "sha1" => HashAlgorithmType.SHA1,
                "sha256" => HashAlgorithmType.SHA256,
                "sha512" => HashAlgorithmType.SHA512,
                _ => null
            };

            if (algo == null)
            {
                error = $"Algoritmo desconocido: '{part}'. Válidos: md5, sha1, sha256, sha512.";
                return false;
            }

            algorithms.Add(algo.Value);
        }

        return true;
    }
}
