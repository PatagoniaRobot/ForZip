// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================
//
//  Author : Claudio Andino
//  Email  : claudio@patagoniarobot.com
//
//  Copyright (c) 2026 Claudio Andino
//  Developed under the Patagonia Robot initiative
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at:
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Sealing;
using ForZip.Core.Services;

namespace ForZip.Cli.Commands;

/// <summary>
/// Utilidades compartidas por los comandos que trabajan con sobres sellados: qué archivos
/// entran, dónde salen y cómo se informa el resultado.
/// </summary>
public static class SealedArtifacts
{
    /// <summary>
    /// Artefactos que ya son salida de un sellado y por lo tanto nunca vuelven a entrar en
    /// uno nuevo.
    /// </summary>
    private static readonly string[] ExcludedSuffixes =
    {
        SealedContainerFormat.SealedExtension,
        ReceiptService.ReceiptSuffix,
        ReceiptService.ReceiptSuffix + ".p7s",
        DeliveryActService.ActSuffix
    };

    /// <summary>
    /// Salidas de un sellado anterior que no son evidencia: la página para abrir sobres (con su nombre actual y
    /// el anterior al cambio de nombre). Se dejaba junto al sobre, así que en una carpeta ya sellada estaba.
    /// </summary>
    private static readonly string[] ExcludedFileNames =
    {
        EnvelopeOpenerPage.FileName,
        "Abrir_sobre_ForZip.html"
    };

    /// <summary>Temporales del sellado (<c>.forzip-sellando-….tmp</c>) que un corte pudo dejar.</summary>
    private const string WorkingFilePrefix = ".forzip-sellando-";

    /// <summary>Informe con el nombre que le da la GUI: <c>caso.report.txt</c> para <c>caso.zip</c>.</summary>
    private const string GuiReportSuffix = ".report.txt";

    /// <summary>
    /// Reúne los archivos que van adentro del sobre. Una carpeta aporta todo lo que tiene en
    /// su raíz; un archivo aporta además sus volúmenes y sus sidecar, que son los que llevan
    /// su mismo nombre como prefijo (<c>evidencia.zip.manifest.json</c>, <c>…​.sha256</c>).
    /// </summary>
    public static List<string> Collect(string input)
    {
        if (Directory.Exists(input))
        {
            return Directory.GetFiles(input)
                .Where(IsSealable)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!File.Exists(input))
        {
            return new List<string>();
        }

        var full = Path.GetFullPath(input);
        var directory = Path.GetDirectoryName(full) ?? string.Empty;
        var fileName = Path.GetFileName(full);

        var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Archivo dividido: entran todos los volúmenes, no solo el que se nombró.
        foreach (var segment in SplitArchive.ResolveSegments(full))
        {
            if (File.Exists(segment))
            {
                collected.Add(segment);
            }
        }

        collected.Add(full);

        // La GUI nombra el informe sin la extensión del paquete (caso.report.txt, no caso.zip.report.txt): sin
        // esto quedaba afuera del sobre, y con --delete-sources quedaba en claro al lado del sobre.
        var packageName = Path.GetFileName(SplitArchive.GetBasePath(full));
        var guiReport = Path.Combine(directory, Path.GetFileNameWithoutExtension(packageName) + GuiReportSuffix);
        foreach (var candidate in new[] { guiReport, guiReport + ".sha256" })
        {
            if (File.Exists(candidate))
            {
                collected.Add(candidate);
            }
        }

        foreach (var sibling in Directory.GetFiles(directory))
        {
            var siblingName = Path.GetFileName(sibling);
            // Sidecar y volúmenes siguen al nombre con un punto (caso.zip.manifest.json, caso.zip.001). Sin
            // exigirlo entraba —y con --delete-sources se borraba— cualquier vecino que empezara igual
            // ("caso.zipviejo").
            if (siblingName.Length > fileName.Length + 1
                && siblingName.StartsWith(fileName + ".", StringComparison.OrdinalIgnoreCase)
                && IsSealable(sibling))
            {
                collected.Add(sibling);
            }
        }

        return collected.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Resuelve la carpeta de destino. Si se pasó una ruta con nombre de sobre se toma su
    /// carpeta: el nombre del <c>.fzs</c> lo decide ForZip a partir del identificador
    /// aleatorio, y no puede derivarse de la causa ni de la evidencia.
    /// </summary>
    public static string ResolveOutputDirectory(string? output, string input)
    {
        if (string.IsNullOrEmpty(output))
        {
            return Directory.Exists(input)
                ? Path.GetFullPath(input)
                : Path.GetDirectoryName(Path.GetFullPath(input)) ?? Directory.GetCurrentDirectory();
        }

        if (Path.GetExtension(output).Equals(SealedContainerFormat.SealedExtension, StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Aviso: el nombre del sobre no es configurable (se deriva del identificador");
            Console.WriteLine("       aleatorio del paquete). Se usa la carpeta indicada.");
            Console.ResetColor();
            return Path.GetDirectoryName(Path.GetFullPath(output)) ?? Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(output);
    }

    /// <summary>
    /// Antes de sellar: el acta se tiene que poder escribir y no puede pisar otra. Con contraseña generada el
    /// acta es el único lugar donde queda; descubrir al final que la carpeta no existe dejaba un sobre que
    /// nadie puede abrir, y reusar la ruta de un acta anterior borraba la contraseña de otro sobre.
    /// </summary>
    public static bool CheckActaPath(string? actaPath)
    {
        if (string.IsNullOrEmpty(actaPath))
        {
            return true;
        }

        var full = Path.GetFullPath(actaPath);
        string? problem = File.Exists(full)
            ? $"ya existe '{full}'. Un acta anterior guarda la contraseña de otro sobre: no se sobrescribe."
            : !Directory.Exists(Path.GetDirectoryName(full))
                ? $"no existe la carpeta de '{full}'."
                : null;
        if (problem == null)
        {
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: acta de entrega: {problem}");
        Console.WriteLine("       No se selló nada. Indique otra ruta con --acta.");
        Console.ResetColor();
        return false;
    }

    /// <summary>Barra de progreso por bytes, con el mismo aspecto que la de <c>zip</c>.</summary>
    public static IProgress<(long bytesProcessed, long totalBytes)> CreateProgress() =>
        new Progress<(long processed, long total)>(p =>
        {
            if (p.total > 0)
            {
                var percent = (int)(100.0 * p.processed / p.total);
                Console.Write($"\rProgreso: [{new string('#', percent / 5)}{new string('-', 20 - percent / 5)}] {percent}% ");
            }
        });

    /// <summary>
    /// Informa qué se produjo y, sobre todo, qué hace cada archivo: es la diferencia entre un
    /// operador que sube lo correcto y uno que sube el paquete entero.
    /// </summary>
    public static void ReportResult(SealedPackageResult result)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[✓] Sobre sellado.");
        Console.ResetColor();

        Console.WriteLine($"  A la nube (solo esto)  : {result.SealedPath}");
        Console.WriteLine($"  Al expediente          : {result.ReceiptPath}");

        if (result.ReceiptSignaturePath != null)
        {
            Console.WriteLine($"  Firma del recibo       : {result.ReceiptSignaturePath}");
        }

        if (result.OpenerPagePath != null)
        {
            Console.WriteLine($"  Para quien recibe      : {result.OpenerPagePath}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Aviso: no se pudo dejar {EnvelopeOpenerPage.FileName} junto al sobre; quien lo reciba necesitará ForZip.");
            Console.ResetColor();
        }

        Console.WriteLine($"  Identificador          : {result.Header.BaseName}");
        Console.WriteLine($"  SHA-256 del sobre      : {result.Receipt.SealedSha256}");

        if (result.TimestampWarning != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Aviso: la firma se generó SIN sello de tiempo. {result.TimestampWarning}");
            Console.ResetColor();
        }

        if (result.UndeletedTempPath != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Aviso: no se pudo borrar el temporal '{result.UndeletedTempPath}'.");
            Console.WriteLine("       Adentro quedó una copia del paquete en claro: bórrelo a mano.");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Sin la contraseña el contenido es irrecuperable: no hay recuperación ni respaldo.");
        Console.ResetColor();
    }

    /// <summary>Borra los artefactos ya encerrados en el sobre, informando lo que no pudo.</summary>
    public static void DeleteAll(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Aviso: no se pudo borrar '{path}': {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    /// <summary>
    /// Comprueba el destino contra las carpetas sincronizadas conocidas. Devuelve <c>false</c>
    /// si hay que abortar.
    /// </summary>
    /// <remarks>
    /// El CLI aborta en vez de preguntar: un script que se queda esperando una confirmación
    /// que nadie va a tipear es peor que uno que falla con un mensaje claro. Para eso está
    /// <c>--allow-cloud-folder</c>.
    /// </remarks>
    public static bool EnsureNotCloudSynced(
        ICloudSyncDetector detector, string? path, bool allowed, bool blocking)
    {
        var match = detector.Detect(path);
        if (match == null)
        {
            return true;
        }

        Console.ForegroundColor = allowed || !blocking ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"{(allowed || !blocking ? "Aviso" : "Error")}: '{path}' parece estar dentro de una");
        Console.WriteLine($"       carpeta sincronizada con {match.Provider} ({match.RootPath}).");
        Console.WriteLine("       Lo que se escriba ahí se sube a la nube en claro y automáticamente.");

        if (!allowed && blocking)
        {
            Console.WriteLine("       Use --allow-cloud-folder si de verdad es lo que quiere.");
        }

        Console.ResetColor();
        return allowed || !blocking;
    }

    /// <summary>
    /// Subcarpetas de una carpeta a sellar: no entran (el sobre lleva un paquete, que es plano), y hay que
    /// decirlo en vez de dejarlas afuera en silencio.
    /// </summary>
    public static IReadOnlyList<string> SkippedSubfolders(string input) =>
        Directory.Exists(input) ? Directory.GetDirectories(input) : Array.Empty<string>();

    private static bool IsSealable(string path)
    {
        var name = Path.GetFileName(path);
        return !ExcludedSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
               && !ExcludedFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
               && !name.StartsWith(WorkingFilePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
