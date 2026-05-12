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
using ForZip.Core.Models;
using ICSharpCode.SharpZipLib.Zip;

namespace ForZip.Core.Services;

public class ZipService : IZipService
{
    private const int BufferSize = 65536;
    private static readonly int[] ValidLevels = { 0, 1, 3, 5, 7, 9 };

    private readonly IHashService _hashService;

    public ZipService(IHashService hashService)
    {
        _hashService = hashService;
    }

    public async Task<List<HashResult>> CompressAsync(
        ZipOptions options,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        ValidateCompressOptions(options);

        var files = EnumerateSourceFiles(options.SourcePaths);
        var totalBytes = files.Sum(f => f.Size);
        var hashResults = new List<HashResult>();

        // El trabajo total es Hashing (totalBytes) + Empaquetado (totalBytes)
        var hasHashing = options.HashAlgorithms.Count > 0;
        var totalWork = hasHashing ? totalBytes * 2 : totalBytes;

        // Fase 1: Hashing
        if (hasHashing)
        {
            long hashedBytes = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                
                var result = await _hashService.ComputeHashesAsync(
                    file.FullPath, 
                    options.HashAlgorithms, 
                    new Progress<double>(p => {
                        long currentHashed = hashedBytes + (long)(p * file.Size);
                        progress?.Report((currentHashed, totalWork));
                    }), 
                    ct);
                
                hashedBytes += file.Size;
                result.FilePath = file.EntryName;
                hashResults.Add(result);
                progress?.Report((hashedBytes, totalWork));
            }
        }

        // Fase 2: Empaquetado
        long packedBytes = 0;
        long baseOffset = hasHashing ? totalBytes : 0;

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

            var buffer = new byte[BufferSize];
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var entry = new ZipEntry(file.EntryName)
                {
                    DateTime = File.GetLastWriteTime(file.FullPath),
                    Size = file.Size,
                    AESKeySize = hasPassword ? 256 : 0
                };

                zipStream.PutNextEntry(entry);

                await using (var inFs = new FileStream(
                    file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    BufferSize, useAsync: true))
                {
                    int n;
                    while ((n = await inFs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await zipStream.WriteAsync(buffer.AsMemory(0, n), ct);
                        packedBytes += n;
                        progress?.Report((baseOffset + packedBytes, totalWork));
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
                if (!entry.IsFile)
                {
                    continue;
                }

                var combined = Path.Combine(outputDir, entry.Name);
                var targetPath = Path.GetFullPath(combined);

                // Defensa contra Zip Slip: la ruta final debe quedar dentro del directorio destino
                if (!targetPath.StartsWith(canonicalOutput + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !string.Equals(targetPath, canonicalOutput, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Entrada ZIP fuera del directorio destino (Zip Slip): {entry.Name}");
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
                    while ((n = inStream.Read(buffer, 0, buffer.Length)) > 0)
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

    private static List<SourceFile> EnumerateSourceFiles(IEnumerable<string> sourcePaths)
    {
        var files = new List<SourceFile>();

        foreach (var src in sourcePaths)
        {
            if (File.Exists(src))
            {
                var fi = new FileInfo(src);
                files.Add(new SourceFile(fi.FullName, fi.Name, fi.Length));
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
                    files.Add(new SourceFile(fi.FullName, entryName, fi.Length));
                }
            }
            else
            {
                throw new FileNotFoundException("Origen no encontrado.", src);
            }
        }

        return files;
    }

    private sealed record SourceFile(string FullPath, string EntryName, long Size);
}
