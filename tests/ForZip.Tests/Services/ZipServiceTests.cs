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

using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class ZipServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly ZipService _service;

    public ZipServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _service = new ZipService(new HashService());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir))
            {
                Directory.Delete(_workDir, recursive: true);
            }
        }
        catch
        {
            // Limpieza best-effort: no bloquear los tests por archivos en uso
        }
    }

    [Fact]
    public async Task CompressAndDecompress_SimpleFile_ContentIdentical()
    {
        var srcFile = Path.Combine(_workDir, "input.txt");
        var content = "Contenido de prueba para ForZip — UTF-8 y acentos: ñáéíóú.";
        await File.WriteAllTextAsync(srcFile, content);

        var zipPath = Path.Combine(_workDir, "out.zip");
        var extractDir = Path.Combine(_workDir, "extracted");

        await _service.CompressAsync(BuildOptions(srcFile, zipPath, level: 5), null, CancellationToken.None);
        await _service.DecompressAsync(zipPath, extractDir, null, null, CancellationToken.None);

        var extractedFile = Path.Combine(extractDir, "input.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(content, await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task CompressAndDecompress_WithAes256_ContentIdentical()
    {
        var srcFile = Path.Combine(_workDir, "secret.txt");
        var content = "Datos sensibles cifrados con AES-256.";
        await File.WriteAllTextAsync(srcFile, content);

        var zipPath = Path.Combine(_workDir, "out.zip");
        var extractDir = Path.Combine(_workDir, "extracted");

        var opts = BuildOptions(srcFile, zipPath, level: 5);
        opts.Password = TestPasswords.Simple;

        await _service.CompressAsync(opts, null, CancellationToken.None);
        await _service.DecompressAsync(zipPath, extractDir, TestPasswords.Simple, null, CancellationToken.None);

        var extractedFile = Path.Combine(extractDir, "secret.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(content, await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task Decompress_WrongPassword_ThrowsException()
    {
        var srcFile = Path.Combine(_workDir, "secret.txt");
        await File.WriteAllTextAsync(srcFile, "datos");

        var zipPath = Path.Combine(_workDir, "out.zip");
        var extractDir = Path.Combine(_workDir, "extracted");

        var opts = BuildOptions(srcFile, zipPath, level: 5);
        opts.Password = TestPasswords.Simple;
        await _service.CompressAsync(opts, null, CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.DecompressAsync(zipPath, extractDir, "ContraseñaIncorrecta", null, CancellationToken.None));
    }

    [Fact]
    public async Task Compress_Level0Store_ValidZip()
    {
        var srcFile = Path.Combine(_workDir, "data.bin");
        var bytes = new byte[10_000];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i & 0xFF);
        }
        await File.WriteAllBytesAsync(srcFile, bytes);

        var zipPath = Path.Combine(_workDir, "store.zip");
        var extractDir = Path.Combine(_workDir, "extracted");

        await _service.CompressAsync(BuildOptions(srcFile, zipPath, level: 0), null, CancellationToken.None);
        await _service.DecompressAsync(zipPath, extractDir, null, null, CancellationToken.None);

        var zipSize = new FileInfo(zipPath).Length;
        Assert.True(zipSize >= bytes.Length, $"Nivel 0 debería ser ≥ original; ZIP={zipSize}, original={bytes.Length}");

        var extracted = await File.ReadAllBytesAsync(Path.Combine(extractDir, "data.bin"));
        Assert.Equal(bytes, extracted);
    }

    [Fact]
    public async Task Compress_Level9_SmallerThanLevel0()
    {
        var srcFile = Path.Combine(_workDir, "repetitive.txt");
        // Texto altamente repetitivo: muy compresible
        var content = string.Concat(Enumerable.Repeat("ForZip-AAAAAAAAAA-1234567890-", 2000));
        await File.WriteAllTextAsync(srcFile, content);

        var zip0 = Path.Combine(_workDir, "lvl0.zip");
        var zip9 = Path.Combine(_workDir, "lvl9.zip");

        await _service.CompressAsync(BuildOptions(srcFile, zip0, level: 0), null, CancellationToken.None);
        await _service.CompressAsync(BuildOptions(srcFile, zip9, level: 9), null, CancellationToken.None);

        var size0 = new FileInfo(zip0).Length;
        var size9 = new FileInfo(zip9).Length;

        Assert.True(size9 < size0, $"Nivel 9 ({size9}) debería ser menor que nivel 0 ({size0}).");
    }

    [Fact]
    public async Task Compress_FolderWithSubfolders_PreservesStructure()
    {
        var srcDir = Path.Combine(_workDir, "myfolder");
        var subDir = Path.Combine(srcDir, "sub1");
        var deeperDir = Path.Combine(subDir, "deep");
        Directory.CreateDirectory(deeperDir);

        var rootFile = Path.Combine(srcDir, "root.txt");
        var subFile = Path.Combine(subDir, "child.txt");
        var deepFile = Path.Combine(deeperDir, "grandchild.txt");

        await File.WriteAllTextAsync(rootFile, "root");
        await File.WriteAllTextAsync(subFile, "child");
        await File.WriteAllTextAsync(deepFile, "grandchild");

        var zipPath = Path.Combine(_workDir, "folder.zip");
        var extractDir = Path.Combine(_workDir, "extracted");

        var opts = new ZipOptions
        {
            SourcePaths = new List<string> { srcDir },
            OutputPath = zipPath,
            CompressionLevel = 5
        };

        await _service.CompressAsync(opts, null, CancellationToken.None);
        await _service.DecompressAsync(zipPath, extractDir, null, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(extractDir, "myfolder", "root.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "myfolder", "sub1", "child.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "myfolder", "sub1", "deep", "grandchild.txt")));
    }

    [Fact]
    public async Task Compress_Cancellation_ThrowsAndCleansUp()
    {
        var srcFile = Path.Combine(_workDir, "big.bin");
        // Tamaño suficiente para tener varios bloques de 64 KB
        var bytes = new byte[5 * 1024 * 1024];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i & 0xFF);
        }
        await File.WriteAllBytesAsync(srcFile, bytes);

        var zipPath = Path.Combine(_workDir, "cancel.zip");
        using var cts = new CancellationTokenSource();

        var progress = new Progress<(long, long)>(_ => cts.Cancel());

        // OperationCanceledException o cualquiera de sus derivadas (TaskCanceledException) son válidas
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.CompressAsync(BuildOptions(srcFile, zipPath, level: 5), progress, cts.Token));

        // No debe quedar el ZIP parcial en disco
        Assert.False(File.Exists(zipPath), "El archivo ZIP parcial debería haberse eliminado.");
    }

    [Fact]
    public async Task Compress_EmptyFile_HandledCorrectly()
    {
        var srcFile = Path.Combine(_workDir, "empty.dat");
        await File.WriteAllBytesAsync(srcFile, Array.Empty<byte>());

        var zipPath = Path.Combine(_workDir, "empty.zip");
        var extractDir = Path.Combine(_workDir, "extracted");

        await _service.CompressAsync(BuildOptions(srcFile, zipPath, level: 5), null, CancellationToken.None);
        await _service.DecompressAsync(zipPath, extractDir, null, null, CancellationToken.None);

        var extractedFile = Path.Combine(extractDir, "empty.dat");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(0, new FileInfo(extractedFile).Length);
    }

    [Fact]
    public async Task Compress_WithHashes_ReturnsHashResults()
    {
        var srcFile = Path.Combine(_workDir, "hashed.txt");
        var content = "Verificación de hashes durante compresión.";
        await File.WriteAllTextAsync(srcFile, content);

        var zipPath = Path.Combine(_workDir, "hashed.zip");
        var opts = BuildOptions(srcFile, zipPath, level: 5);
        opts.HashAlgorithms = new HashSet<HashAlgorithmType>
        {
            HashAlgorithmType.SHA256
        };

        var result = await _service.CompressAsync(opts, null, CancellationToken.None);
        var results = result.FileHashes;

        Assert.Single(results);
        Assert.True(results[0].Hashes.ContainsKey(HashAlgorithmType.SHA256));
        Assert.Equal(64, results[0].Hashes[HashAlgorithmType.SHA256].Length);
        Assert.False(result.IsSplit);

        // Verificación cruzada con el HashService directo
        var direct = await new HashService().ComputeHashesAsync(
            srcFile,
            new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
            null,
            CancellationToken.None);
        Assert.Equal(direct.Hashes[HashAlgorithmType.SHA256], results[0].Hashes[HashAlgorithmType.SHA256]);
    }

    [Fact]
    public async Task Compress_DuplicateFileNames_ProducesDistinctEntries()
    {
        // Dos archivos con el mismo nombre desde carpetas distintas: no deben colisionar
        // en una única entrada (eso generaría un ZIP ambiguo y rompería la verificación).
        var dirA = Path.Combine(_workDir, "a");
        var dirB = Path.Combine(_workDir, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        var fileA = Path.Combine(dirA, "evidencia.txt");
        var fileB = Path.Combine(dirB, "evidencia.txt");
        await File.WriteAllTextAsync(fileA, "contenido A");
        await File.WriteAllTextAsync(fileB, "contenido B");

        var zipPath = Path.Combine(_workDir, "dup.zip");
        var extractDir = Path.Combine(_workDir, "extracted");

        var opts = new ZipOptions
        {
            SourcePaths = new List<string> { fileA, fileB },
            OutputPath = zipPath,
            CompressionLevel = 5,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
        };

        var results = (await _service.CompressAsync(opts, null, CancellationToken.None)).FileHashes;

        // Dos resultados de hash con nombres de entrada únicos
        Assert.Equal(2, results.Count);
        Assert.Equal(2, results.Select(r => r.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Ambos archivos sobreviven a la extracción (ninguno se sobrescribe)
        await _service.DecompressAsync(zipPath, extractDir, null, null, CancellationToken.None);
        var extracted = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
        Assert.Equal(2, extracted.Length);
    }

    [Fact]
    public async Task Compress_WithSplit_RoundTripsAndReportsVolumes()
    {
        var srcFile = Path.Combine(_workDir, "evidencia.bin");
        var bytes = new byte[300_000];
        new Random(7).NextBytes(bytes);
        await File.WriteAllBytesAsync(srcFile, bytes);

        var logicalZip = Path.Combine(_workDir, "caso.zip");
        var opts = BuildOptions(srcFile, logicalZip, level: 0);
        opts.SplitSize = 128 * 1024;

        var result = await _service.CompressAsync(opts, null, CancellationToken.None);

        Assert.True(result.IsSplit);
        Assert.Equal(3, result.Volumes.Count);
        Assert.All(result.Volumes, v => Assert.True(v.Size <= 128 * 1024));
        // Cada volumen lógico existe en disco con el nombre esperado (.001, .002, .003)
        Assert.True(File.Exists(logicalZip + ".001"));
        Assert.True(File.Exists(logicalZip + ".003"));
        Assert.False(File.Exists(logicalZip)); // no debe quedar el ZIP único

        // Reensamblado transparente: extraer desde el primer volumen reproduce el contenido
        var extractDir = Path.Combine(_workDir, "extracted");
        await _service.DecompressAsync(logicalZip + ".001", extractDir, null, null, CancellationToken.None);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(extractDir, "evidencia.bin")));
    }

    [Fact]
    public async Task Compress_SplitBelowMinimum_Throws()
    {
        var srcFile = Path.Combine(_workDir, "x.bin");
        await File.WriteAllBytesAsync(srcFile, new byte[1000]);

        var opts = BuildOptions(srcFile, Path.Combine(_workDir, "x.zip"), level: 0);
        opts.SplitSize = 1024; // por debajo del mínimo (64 KB)

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CompressAsync(opts, null, CancellationToken.None));
    }

    [Fact]
    public void ConcatenatedReadStream_MissingSegment_Throws()
    {
        var existing = Path.Combine(_workDir, "a.001");
        File.WriteAllBytes(existing, new byte[10]);
        var missing = Path.Combine(_workDir, "a.002");

        Assert.Throws<FileNotFoundException>(() =>
            new ConcatenatedReadStream(new[] { existing, missing }));
    }

    private static ZipOptions BuildOptions(string srcFile, string outputPath, int level)
    {
        return new ZipOptions
        {
            SourcePaths = new List<string> { srcFile },
            OutputPath = outputPath,
            CompressionLevel = level
        };
    }
}
