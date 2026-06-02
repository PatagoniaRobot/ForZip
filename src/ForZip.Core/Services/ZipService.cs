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
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ICSharpCode.SharpZipLib.Zip;

namespace ForZip.Core.Services;

public class ZipService : IZipService
{
    private const int BufferSize = 65536;
    private static readonly int[] ValidLevels = { 0, 1, 3, 5, 7, 9 };

    public ZipService(IHashService hashService)
    {
        // El hashing se realiza ahora en un único pase durante el empaquetado, por lo
        // que ZipService ya no necesita IHashService. Se conserva el parámetro por
        // compatibilidad con la composición de dependencias existente (DI y tests).
        _ = hashService;
    }

    public async Task<List<HashResult>> CompressAsync(
        ZipOptions options,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        ValidateCompressOptions(options);

        var files = EnumerateSourceFiles(options.SourcePaths);
        var emptyDirs = EnumerateEmptyDirectories(options.SourcePaths);
        var totalBytes = files.Sum(f => f.Size);
        var hashResults = new List<HashResult>();

        // Un único pase: leemos cada archivo una sola vez y, con el mismo buffer,
        // alimentamos los algoritmos de hash y el stream de compresión. Esto evita
        // releer la evidencia (antes se leía dos veces) y reduce a la mitad el I/O.
        var hasHashing = options.HashAlgorithms.Count > 0;

        long packedBytes = 0;
        bool success = false;
        var hasPassword = !string.IsNullOrEmpty(options.Password);

        FileStream? fsOut = null;
        ZipOutputStream? zipStream = null;

        try
        {
            fsOut = new FileStream(
                options.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, useAsync: true);
            zipStream = new ZipOutputStream(fsOut);
            zipStream.SetLevel(options.CompressionLevel);
            zipStream.UseZip64 = UseZip64.On;
            if (hasPassword)
            {
                zipStream.Password = options.Password;
            }

            // Entradas de directorio para preservar carpetas vacías (estructura fiel)
            foreach (var dirEntryName in emptyDirs)
            {
                ct.ThrowIfCancellationRequested();
                zipStream.PutNextEntry(new ZipEntry(dirEntryName));
                zipStream.CloseEntry();
            }

            var buffer = new byte[BufferSize];
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                // Timestamp en UTC para que el ZIP sea reproducible con independencia
                // de la zona horaria de la máquina que lo genera.
                var entry = new ZipEntry(file.EntryName)
                {
                    DateTime = file.ModifiedUtc.UtcDateTime,
                    Size = file.Size,
                    AESKeySize = hasPassword ? 256 : 0
                };

                zipStream.PutNextEntry(entry);

                var hashers = hasHashing ? CreateHashers(options.HashAlgorithms) : null;
                try
                {
                    await using (var inFs = new FileStream(
                        file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        BufferSize, useAsync: true))
                    {
                        int n;
                        while ((n = await inFs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (hashers != null)
                            {
                                foreach (var hasher in hashers.Values)
                                {
                                    hasher.TransformBlock(buffer, 0, n, null, 0);
                                }
                            }

                            await zipStream.WriteAsync(buffer.AsMemory(0, n), ct);
                            packedBytes += n;
                            progress?.Report((packedBytes, totalBytes));
                        }
                    }

                    if (hashers != null)
                    {
                        var result = new HashResult
                        {
                            FilePath = file.EntryName,
                            FileSize = file.Size,
                            SourcePath = file.FullPath,
                            ModifiedUtc = file.ModifiedUtc
                        };
                        foreach (var (algo, hasher) in hashers)
                        {
                            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                            result.Hashes[algo] = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
                        }
                        hashResults.Add(result);
                    }
                }
                finally
                {
                    if (hashers != null)
                    {
                        foreach (var hasher in hashers.Values)
                        {
                            hasher.Dispose();
                        }
                    }
                }

                zipStream.CloseEntry();
            }

            zipStream.Finish();
            success = true;
        }
        finally
        {
            // Disposal defensivo: tras cancelación, Finish() interno de ZipOutputStream
            // detecta entradas incompletas y lanza ZipException que ocultaría la cancelación
            if (zipStream != null)
            {
                try
                {
                    zipStream.Dispose();
                }
                catch when (!success)
                {
                    // Excepciones durante el cierre tras cancelación/error son irrelevantes
                }
            }

            if (fsOut != null)
            {
                try
                {
                    await fsOut.DisposeAsync();
                }
                catch when (!success)
                {
                    // Igual que arriba: silenciar fallos de cierre tras cancelación
                }
            }

            if (!success && File.Exists(options.OutputPath))
            {
                try
                {
                    File.Delete(options.OutputPath);
                }
                catch (IOException)
                {
                    // El archivo puede estar bloqueado momentáneamente; se ignora silenciosamente
                }
            }
        }

        return hashResults;
    }

    public async Task DecompressAsync(
        string zipPath,
        string outputDir,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Archivo ZIP no encontrado.", zipPath);
        }

        Directory.CreateDirectory(outputDir);
        var canonicalOutput = Path.GetFullPath(outputDir).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        using var zipFile = new ZipFile(zipPath);
        if (!string.IsNullOrEmpty(password))
        {
            zipFile.Password = password;
        }

        long totalBytes = 0;
        foreach (ZipEntry e in zipFile)
        {
            if (e.IsFile)
            {
                totalBytes += e.Size;
            }
        }

        long processedBytes = 0;
        var buffer = new byte[BufferSize];
        var createdFiles = new List<string>();
        bool success = false;

        try
        {
            foreach (ZipEntry entry in zipFile)
            {
                ct.ThrowIfCancellationRequested();

                var combined = Path.Combine(outputDir, entry.Name);
                var targetPath = Path.GetFullPath(combined);

                // Defensa contra Zip Slip: la ruta final debe quedar dentro del directorio destino
                if (!targetPath.StartsWith(canonicalOutput + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !string.Equals(targetPath, canonicalOutput, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Entrada ZIP fuera del directorio destino (Zip Slip): {entry.Name}");
                }

                // Entrada de directorio (incl. carpetas vacías): recrearla y continuar
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                if (!entry.IsFile)
                {
                    continue;
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                using var inStream = zipFile.GetInputStream(entry);
                await using (var outStream = new FileStream(
                    targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    BufferSize, useAsync: true))
                {
                    createdFiles.Add(targetPath);
                    int n;
                    while ((n = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await outStream.WriteAsync(buffer.AsMemory(0, n), ct);
                        processedBytes += n;
                        progress?.Report((processedBytes, totalBytes));
                    }
                }
            }
            success = true;
        }
        finally
        {
            if (!success)
            {
                foreach (var file in createdFiles)
                {
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch { /* Ignorar fallos de limpieza en cascada */ }
                }
            }
        }
    }

    private static void ValidateCompressOptions(ZipOptions options)
    {
        if (options.SourcePaths == null || options.SourcePaths.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos un archivo de origen.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("Se requiere ruta de salida del ZIP.", nameof(options));
        }

        if (!ValidLevels.Contains(options.CompressionLevel))
        {
            throw new ArgumentException(
                $"Nivel inválido: {options.CompressionLevel}. Valores válidos: 0, 1, 3, 5, 7, 9.",
                nameof(options));
        }
    }

    private static Dictionary<HashAlgorithmType, HashAlgorithm> CreateHashers(
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

    private static List<SourceFile> EnumerateSourceFiles(IEnumerable<string> sourcePaths)
    {
        var files = new List<SourceFile>();

        foreach (var src in sourcePaths)
        {
            if (File.Exists(src))
            {
                var fi = new FileInfo(src);
                files.Add(new SourceFile(fi.FullName, fi.Name, fi.Length, fi.LastWriteTimeUtc));
            }
            else if (Directory.Exists(src))
            {
                var baseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(src));
                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, file);
                    // ZIP exige separador POSIX en los nombres de entrada
                    var entryName = Path.Combine(baseName, rel).Replace('\\', '/');
                    var fi = new FileInfo(file);
                    files.Add(new SourceFile(fi.FullName, entryName, fi.Length, fi.LastWriteTimeUtc));
                }
            }
            else
            {
                throw new FileNotFoundException("Origen no encontrado.", src);
            }
        }

        return files;
    }

    /// <summary>
    /// Devuelve los nombres de entrada (separador POSIX, con barra final) de las
    /// carpetas que no contienen ningún archivo en todo su subárbol. Sin esto, una
    /// carpeta vacía del origen se perdería al empaquetar.
    /// </summary>
    private static List<string> EnumerateEmptyDirectories(IEnumerable<string> sourcePaths)
    {
        var dirs = new List<string>();

        foreach (var src in sourcePaths)
        {
            if (!Directory.Exists(src))
            {
                continue;
            }

            var baseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(src));

            // La propia raíz, si está totalmente vacía
            if (!Directory.EnumerateFileSystemEntries(src).Any())
            {
                dirs.Add(baseName.Replace('\\', '/') + "/");
                continue;
            }

            foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            {
                if (Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
                {
                    continue; // las entradas de archivo ya recrean esta carpeta
                }

                var rel = Path.GetRelativePath(src, dir);
                var entryName = Path.Combine(baseName, rel).Replace('\\', '/');
                dirs.Add(entryName + "/");
            }
        }

        return dirs;
    }

    private sealed record SourceFile(string FullPath, string EntryName, long Size, DateTimeOffset ModifiedUtc);
}
