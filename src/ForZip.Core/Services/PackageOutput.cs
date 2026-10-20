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

namespace ForZip.Core.Services;

/// <summary>
/// Reglas sobre dónde puede ir un paquete: nunca encima de otro paquete ni dentro de lo que se empaqueta.
/// </summary>
/// <remarks>
/// Sobrescribir no era solo perder el paquete anterior: un <c>caso.zip</c> viejo al lado de los volúmenes
/// nuevos <c>caso.zip.001…</c> le ganaba a estos al verificar (y al sellar), y la verificación de un paquete
/// correcto terminaba en "evidencia comprometida". Los volúmenes sobrantes de una división anterior tenían
/// el mismo efecto.
/// </remarks>
public static class PackageOutput
{
    private const string ManifestSuffix = ".manifest.json";

    /// <summary>
    /// La salida nombra una carpeta (una que existe, o una ruta que termina en barra) y no un paquete: falta el
    /// nombre del archivo. Se pregunta antes que <see cref="FindExisting"/>, que la daría por "paquete existente"
    /// con un nombre vacío.
    /// </summary>
    public static bool IsFolder(string outputPath) =>
        Path.EndsInDirectorySeparator(outputPath) || Directory.Exists(outputPath);

    /// <summary>
    /// Lo que ya existe de un paquete con esa salida: el archivo y cualquier volumen <c>.NNN</c>; con
    /// <paramref name="includeSidecars"/>, también el manifiesto y su firma, y el informe con su <c>.sha256</c>
    /// si se indica (los escriben la CLI y la GUI, no el empaquetado).
    /// </summary>
    public static List<string> FindExisting(string outputPath, string? reportPath = null, bool includeSidecars = true)
    {
        var full = Path.GetFullPath(outputPath);
        var found = new List<string>();
        if (File.Exists(full) || Directory.Exists(full))
        {
            found.Add(full);
        }

        found.AddRange(SplitArchive.FindVolumeFiles(full));
        if (!includeSidecars)
        {
            return found;
        }

        var candidates = new List<string> { full + ManifestSuffix, full + ManifestSuffix + ".p7s" };
        if (!string.IsNullOrEmpty(reportPath))
        {
            var report = Path.GetFullPath(reportPath);
            candidates.Add(report);
            candidates.Add(report + ".sha256");
        }

        found.AddRange(candidates.Where(File.Exists));
        return found;
    }

    /// <summary>
    /// El origen dentro del cual quedaría la salida (o que la salida pisaría), o null si no hay ninguno.
    /// </summary>
    public static string? FindSourceContaining(IEnumerable<string> sourcePaths, string outputPath)
    {
        var output = Path.GetFullPath(outputPath);
        foreach (var source in sourcePaths)
        {
            if (Directory.Exists(source) ? EvidenceSelection.IsWithinSource(source, output)
                                         : string.Equals(Path.GetFullPath(source), output, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>
    /// Ruta de salida libre a partir de la deseada: la misma si no choca con nada, si no "caso (2).zip",
    /// "caso (3).zip"… <paramref name="reportFor"/> da, para cada candidata, la ruta del informe que le
    /// correspondería (o null si no hay informe aparte).
    /// </summary>
    public static string SuggestFree(string desiredPath, Func<string, string?>? reportFor = null)
    {
        var full = Path.GetFullPath(desiredPath);
        var dir = Path.GetDirectoryName(full) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full);
        var candidate = full;
        for (int n = 2; FindExisting(candidate, reportFor?.Invoke(candidate)).Count > 0; n++)
        {
            candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
        }

        return candidate;
    }

    /// <summary>
    /// Lo que el operador eligió en el diálogo de guardar, cuando cambió de carpeta sin tocar el nombre que
    /// mostraba el diálogo (<paramref name="shownPath"/>): el nombre libre se vuelve a calcular en la carpeta
    /// nueva desde el que quiere el operador (<paramref name="desiredPath"/>, sin el "(n)" que haya puesto
    /// ForZip). Así un "(2)" no viaja a una carpeta donde no choca nada, y un nombre que choca en la carpeta
    /// nueva recibe su "(2)" ahí. Un nombre que escribió el operador, o la misma carpeta, se respetan tal cual.
    /// </summary>
    public static string RelocateSuggestion(string pickedPath, string? shownPath, string? desiredPath,
                                            Func<string, string?>? reportFor = null)
    {
        if (string.IsNullOrEmpty(shownPath) || string.IsNullOrEmpty(desiredPath))
        {
            return pickedPath;
        }

        var picked = Path.GetFullPath(pickedPath);
        var shown = Path.GetFullPath(shownPath);
        var pickedDir = Path.GetDirectoryName(picked) ?? string.Empty;

        // Se compara sin extensión: el operador puede haber pasado de ZIP a 7z después de la sugerencia.
        bool sameName = string.Equals(Path.GetFileNameWithoutExtension(picked), Path.GetFileNameWithoutExtension(shown),
                                      StringComparison.OrdinalIgnoreCase);
        bool otherFolder = !string.Equals(pickedDir, Path.GetDirectoryName(shown), StringComparison.OrdinalIgnoreCase);
        if (!sameName || !otherFolder)
        {
            return pickedPath;
        }

        return SuggestFree(Path.Combine(pickedDir, Path.GetFileNameWithoutExtension(desiredPath) + Path.GetExtension(picked)),
                           reportFor);
    }

    /// <summary>
    /// Lo mismo que <see cref="RelocateSuggestion"/> para un archivo suelto (el informe), que además puede cambiar
    /// de tipo en el diálogo: Windows conserva el nombre sugerido y solo le cambia la extensión, y un "(2)" que se
    /// calculó porque existía el .html terminaba en un .txt que no chocaba con nada. Si el operador no tocó el
    /// nombre pero cambió de carpeta o de tipo, el nombre libre se recalcula ahí; si lo tocó, se respeta tal cual.
    /// </summary>
    public static string RetargetFileSuggestion(string pickedPath, string? shownPath, string? desiredPath)
    {
        if (string.IsNullOrEmpty(shownPath) || string.IsNullOrEmpty(desiredPath))
        {
            return pickedPath;
        }

        var picked = Path.GetFullPath(pickedPath);
        var shown = Path.GetFullPath(shownPath);
        var pickedDir = Path.GetDirectoryName(picked) ?? string.Empty;

        bool sameName = string.Equals(Path.GetFileNameWithoutExtension(picked), Path.GetFileNameWithoutExtension(shown),
                                      StringComparison.OrdinalIgnoreCase);
        bool sameFolder = string.Equals(pickedDir, Path.GetDirectoryName(shown), StringComparison.OrdinalIgnoreCase);
        bool sameType = string.Equals(Path.GetExtension(picked), Path.GetExtension(shown), StringComparison.OrdinalIgnoreCase);
        if (!sameName || (sameFolder && sameType))
        {
            return pickedPath;
        }

        return SuggestFree(Path.Combine(pickedDir, Path.GetFileNameWithoutExtension(desiredPath) + Path.GetExtension(picked)));
    }

    /// <summary>
    /// Carpeta libre para extraer: la deseada si no existe o está vacía, si no "caso (2)", "caso (3)"… Extraer
    /// otra vez en la misma carpeta no pisaba nada, pero dejaba cada archivo repetido como "nombre~2" mezclado
    /// con la extracción anterior. <paramref name="claimed"/> son carpetas ya asignadas que todavía no existen
    /// (caso.zip y caso.7z elegidos juntos irían a la misma); la elegida se suma ahí.
    /// </summary>
    public static string FreeFolder(string desiredFolder, ICollection<string>? claimed = null)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(desiredFolder));
        var candidate = full;
        for (int n = 2; IsOccupied(candidate) || IsClaimed(claimed, candidate); n++)
        {
            candidate = $"{full} ({n})";
        }

        claimed?.Add(candidate);
        return candidate;
    }

    private static bool IsClaimed(ICollection<string>? claimed, string folder) =>
        claimed != null && claimed.Any(c => string.Equals(c, folder, StringComparison.OrdinalIgnoreCase));

    private static bool IsOccupied(string folder)
    {
        if (File.Exists(folder))
        {
            return true;
        }

        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Una carpeta que no se deja leer no se da por vacía.
            return true;
        }
    }

    /// <summary>
    /// Nombre libre para un archivo que se guarda en <paramref name="folder"/>: el mismo si no existe, si no
    /// "nombre (2).ext"… Sin carpeta conocida, el nombre tal cual.
    /// </summary>
    public static string FreeFileName(string? folder, string fileName) =>
        string.IsNullOrEmpty(folder) ? fileName : Path.GetFileName(SuggestFree(Path.Combine(folder, fileName)));

    /// <summary>
    /// Ruta sugerida para empaquetar un origen: al lado de él y con su nombre. Null si el origen es la raíz de
    /// una unidad, que no tiene "al lado": ahí la salida la tiene que elegir el operador.
    /// </summary>
    public static string? DefaultFor(string sourcePath, string extension)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var dir = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }

        var name = File.Exists(full) ? Path.GetFileNameWithoutExtension(full) : Path.GetFileName(full);
        if (string.IsNullOrEmpty(name))
        {
            name = "Evidencia";
        }

        return Path.Combine(dir, name + extension);
    }
}
