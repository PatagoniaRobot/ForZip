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

using System.Security.Cryptography;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

/// <summary>
/// Lo que el empaquetado hace con la evidencia antes de que exista el paquete, igual para ZIP y para 7z:
/// recorrer los orígenes con nombres de entrada únicos, encontrar las carpetas vacías, comparar con lo que
/// el operador vio al seleccionar y abrir cada archivo documentando lo que el sistema no deja leer.
/// </summary>
internal static class EvidenceSelection
{
    private const int BufferSize = 65536;

    internal static Dictionary<HashAlgorithmType, HashAlgorithm> CreateHashers(
        IEnumerable<HashAlgorithmType> algorithms)
    {
        var hashers = new Dictionary<HashAlgorithmType, HashAlgorithm>();
        foreach (var algo in algorithms)
        {
            hashers[algo] = algo switch
            {
                HashAlgorithmType.MD5 => MD5.Create(),
                HashAlgorithmType.SHA1 => SHA1.Create(),
                HashAlgorithmType.SHA256 => SHA256.Create(),
                HashAlgorithmType.SHA512 => SHA512.Create(),
                _ => throw new ArgumentOutOfRangeException(nameof(algorithms), algo, "Algoritmo no soportado.")
            };
        }
        return hashers;
    }

    /// <summary>
    /// Abre un archivo de la evidencia. Si el antivirus o el EDR niega la lectura, devuelve null
    /// y registra la omisión con todo lo que se sabe del archivo: ningún programa puede leer lo
    /// que el sistema bloquea, y detener el empaquetado entero por eso deja al operador sin
    /// paquete. Cualquier otro error de apertura sigue abortando, como siempre.
    /// </summary>
    internal static FileStream? TryOpenEvidence(SourceFile file, List<OmittedFile> omittedFiles)
    {
        try
        {
            return new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        }
        catch (Exception ex) when (SecuritySoftwareInterference.GetBlockingErrorCode(ex) is int code)
        {
            omittedFiles.Add(new OmittedFile
            {
                Reason = OmissionReason.SecuritySoftwareBlocked,
                EntryName = file.EntryName,
                SourcePath = file.FullPath,
                Size = file.Size,
                ModifiedUtc = file.ModifiedUtc,
                WindowsErrorCode = code,
                SystemMessage = ex.Message,
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Estaba al recorrer el origen y ya no está al abrirlo. Abortar dejaba al operador
            // sin paquete por un archivo que, de todos modos, nadie puede leer.
            omittedFiles.Add(new OmittedFile
            {
                Reason = OmissionReason.VanishedDuringPackaging,
                EntryName = file.EntryName,
                SourcePath = file.FullPath,
                Size = file.Size,
                ModifiedUtc = file.ModifiedUtc,
                WindowsErrorCode = ex.HResult & 0xFFFF,
                SystemMessage = ex.Message,
                RecordedAtUtc = DateTimeOffset.UtcNow
            });
            return null;
        }
    }

    /// <summary>
    /// Compara lo que el operador vio al seleccionar con lo que hay ahora, y devuelve como
    /// omitido cada archivo que estaba y ya no está.
    /// </summary>
    internal static List<OmittedFile> FindMissingSinceSelection(ZipOptions options, List<SourceFile> found)
    {
        var omitted = new List<OmittedFile>();
        if (options.SelectionSnapshot is not { Count: > 0 } snapshot)
        {
            return omitted;
        }

        var present = new HashSet<string>(found.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        foreach (var seen in snapshot)
        {
            if (present.Contains(Path.GetFullPath(seen.FullPath)))
            {
                continue;
            }

            omitted.Add(new OmittedFile
            {
                Reason = OmissionReason.MissingAtPackaging,
                EntryName = ExpectedEntryName(options.SourcePaths, seen.FullPath),
                SourcePath = Path.GetFullPath(seen.FullPath),
                Size = seen.Size,
                ModifiedUtc = seen.ModifiedUtc,
                SeenAtUtc = seen.SeenAtUtc,
                RecordedAtUtc = now
            });
        }

        return omitted;
    }

    /// <summary>Verdadero si <paramref name="path"/> es el origen mismo o está dentro de él.</summary>
    internal static bool IsWithinSource(string source, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var target = Path.GetFullPath(path);
        // La raíz de una unidad conserva su barra ("D:\"): sumarle otra no coincidía con nada de adentro.
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(root, target, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El nombre que un archivo habría tenido dentro del ZIP, con la misma regla que
    /// <see cref="EnumerateSourceFiles"/>: así el informe lo nombra igual que al resto.
    /// </summary>
    internal static string ExpectedEntryName(IEnumerable<string> sourcePaths, string fullPath)
    {
        var target = Path.GetFullPath(fullPath);
        foreach (var src in sourcePaths)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(src));
            if (string.Equals(root, target, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(target);
            }

            if (target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(root, target);
                return Path.Combine(Path.GetFileName(root), rel).Replace('\\', '/');
            }
        }

        return Path.GetFileName(target);
    }

    /// <summary>
    /// Archivos de los orígenes, con su nombre de entrada único. Los enlaces simbólicos y junctions que hay
    /// dentro de una carpeta no se siguen (ver <see cref="EvidenceTree"/>): van a <paramref name="skippedLinks"/>
    /// como no incluidos, con su destino.
    /// </summary>
    internal static List<SourceFile> EnumerateSourceFiles(
        IEnumerable<string> sourcePaths, IReadOnlyList<SelectedSourceFile>? snapshot = null,
        List<OmittedFile>? skippedLinks = null)
    {
        var files = new List<SourceFile>();
        // Nombres ya usados: evita entradas duplicadas en el ZIP. Comparación
        // ignorando mayúsculas porque la extracción en Windows es case-insensitive.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var src in sourcePaths)
        {
            if (File.Exists(src))
            {
                var fi = new FileInfo(src);
                var entryName = MakeUniqueEntryName(fi.Name, usedNames);
                files.Add(new SourceFile(fi.FullName, entryName, fi.Length, fi.LastWriteTimeUtc));
            }
            else if (Directory.Exists(src))
            {
                var baseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(src));
                var links = new List<EvidenceTree.SkippedLink>();
                // Sin lista donde documentarlas, una carpeta ilegible corta como siempre.
                var unreadable = skippedLinks != null ? new List<EvidenceTree.UnreadableFolder>() : null;
                foreach (var file in EvidenceTree.EnumerateFiles(src, links, unreadable))
                {
                    var rel = Path.GetRelativePath(src, file);
                    // ZIP exige separador POSIX en los nombres de entrada
                    var entryName = MakeUniqueEntryName(Path.Combine(baseName, rel).Replace('\\', '/'), usedNames);
                    var fi = new FileInfo(file);
                    files.Add(new SourceFile(fi.FullName, entryName, fi.Length, fi.LastWriteTimeUtc));
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var link in links)
                {
                    var name = Path.Combine(baseName, Path.GetRelativePath(src, link.Path)).Replace('\\', '/');
                    skippedLinks?.Add(new OmittedFile
                    {
                        Reason = OmissionReason.LinkNotFollowed,
                        EntryName = link.IsDirectory ? name + "/" : name,
                        SourcePath = link.Path,
                        Size = 0,
                        ModifiedUtc = link.ModifiedUtc,
                        LinkTarget = link.Target,
                        RecordedAtUtc = now
                    });
                }

                foreach (var folder in unreadable ?? new List<EvidenceTree.UnreadableFolder>())
                {
                    var rel = Path.GetRelativePath(src, folder.Path);
                    var name = (rel == "." ? baseName : Path.Combine(baseName, rel)).Replace('\\', '/');
                    skippedLinks!.Add(new OmittedFile
                    {
                        Reason = OmissionReason.FolderUnreadable,
                        EntryName = name.TrimEnd('/') + "/",
                        SourcePath = folder.Path,
                        Size = 0,
                        ModifiedUtc = folder.ModifiedUtc,
                        WindowsErrorCode = folder.WindowsErrorCode,
                        SystemMessage = folder.Message,
                        RecordedAtUtc = now
                    });
                }
            }
            else if (snapshot != null && snapshot.Any(f => IsWithinSource(src, f.FullPath)))
            {
                // El origen entero desapareció después de seleccionarlo: sus archivos quedan
                // documentados como omitidos por la comparación con la selección.
                continue;
            }
            else
            {
                throw new FileNotFoundException("Origen no encontrado.", src);
            }
        }

        return files;
    }

    /// <summary>
    /// Garantiza nombres de entrada únicos dentro del ZIP. Dos archivos con el mismo nombre
    /// (p. ej. seleccionados desde carpetas distintas) producirían entradas duplicadas, lo que
    /// genera un ZIP ambiguo y rompe la verificación (el manifiesto se indexa por nombre).
    /// Ante una colisión se inserta un sufijo " (n)" antes de la extensión.
    /// </summary>
    internal static string MakeUniqueEntryName(string entryName, HashSet<string> usedNames)
    {
        if (usedNames.Add(entryName))
        {
            return entryName;
        }

        // Path.GetDirectoryName interpreta tanto '/' como '\' en Windows; renormalizamos a POSIX.
        var dir = Path.GetDirectoryName(entryName)?.Replace('\\', '/');
        var stem = Path.GetFileNameWithoutExtension(entryName);
        var ext = Path.GetExtension(entryName);

        for (int n = 2; ; n++)
        {
            var candidateName = $"{stem} ({n}){ext}";
            var candidate = string.IsNullOrEmpty(dir) ? candidateName : $"{dir}/{candidateName}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Devuelve los nombres de entrada (separador POSIX, con barra final) de las
    /// carpetas que no contienen ningún archivo en todo su subárbol. Sin esto, una
    /// carpeta vacía del origen se perdería al empaquetar.
    /// </summary>
    internal static List<string> EnumerateEmptyDirectories(IEnumerable<string> sourcePaths)
    {
        var dirs = new List<string>();
        var comparer = StringComparer.OrdinalIgnoreCase;

        foreach (var src in sourcePaths)
        {
            if (!Directory.Exists(src))
            {
                continue;
            }

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(src));
            var baseName = Path.GetFileName(root);

            // Carpetas que tienen algún archivo empaquetado en su subárbol (sin seguir enlaces, igual que el
            // recorrido de archivos): esas ya quedan recreadas por las entradas de archivo.
            var withFiles = new HashSet<string>(comparer);
            // Las carpetas ilegibles ya las documenta EnumerateSourceFiles; acá solo no tienen que cortar.
            var ignored = new List<EvidenceTree.UnreadableFolder>();
            foreach (var file in EvidenceTree.EnumerateFiles(root, null, ignored))
            {
                for (var d = Path.GetDirectoryName(file); d != null && d.Length >= root.Length; d = Path.GetDirectoryName(d))
                {
                    if (!withFiles.Add(d))
                    {
                        break;
                    }
                }
            }

            var subdirs = EvidenceTree.EnumerateDirectories(root, ignored)
                .Where(d => !ignored.Any(u => string.Equals(u.Path, d, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // La propia raíz, si no queda nada de ella en el paquete
            if (!withFiles.Contains(root) && subdirs.Count == 0)
            {
                dirs.Add(baseName.Replace('\\', '/') + "/");
                continue;
            }

            foreach (var dir in subdirs)
            {
                if (withFiles.Contains(dir))
                {
                    continue; // las entradas de archivo ya recrean esta carpeta
                }

                var rel = Path.GetRelativePath(root, dir);
                var entryName = Path.Combine(baseName, rel).Replace('\\', '/');
                dirs.Add(entryName + "/");
            }
        }

        // Dos orígenes con el mismo nombre pueden traer la misma carpeta vacía: va una sola vez (el
        // motor 7z rechaza los nombres repetidos y en el ZIP sería una entrada duplicada).
        return dirs.Distinct(comparer).ToList();
    }

    internal sealed record SourceFile(string FullPath, string EntryName, long Size, DateTimeOffset ModifiedUtc);
}
