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
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class HashServiceTests
{
    private static string SampleFilePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample.txt");

    [Fact]
    public async Task ComputeHashesAsync_KnownFile_ReturnsCorrectSha256()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        // Verificación cruzada: hashear los bytes del archivo con la API directa de .NET
        var fileBytes = await File.ReadAllBytesAsync(SampleFilePath);
        var expected = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
        Assert.Equal(expected, result.Hashes[HashAlgorithmType.SHA256]);
    }

    [Fact]
    public async Task ComputeHashesAsync_KnownFile_ReturnsCorrectMd5()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.MD5 };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        var fileBytes = await File.ReadAllBytesAsync(SampleFilePath);
        var expected = Convert.ToHexString(MD5.HashData(fileBytes)).ToLowerInvariant();
        Assert.Equal(expected, result.Hashes[HashAlgorithmType.MD5]);
    }

    [Fact]
    public async Task ComputeHashesAsync_AllAlgorithms_ReturnsAllFour()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType>
        {
            HashAlgorithmType.MD5,
            HashAlgorithmType.SHA1,
            HashAlgorithmType.SHA256,
            HashAlgorithmType.SHA512
        };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        Assert.Equal(4, result.Hashes.Count);
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.MD5));
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA1));
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA256));
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA512));
    }

    [Fact]
    public async Task ComputeHashesAsync_OnlySha256_ReturnsOnlySha256()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        Assert.Single(result.Hashes);
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA256));
    }

    [Fact]
    public async Task ComputeHashesAsync_EmptyFile_ReturnsValidHashes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, Array.Empty<byte>());
            var service = new HashService();
            var algorithms = new HashSet<HashAlgorithmType>
            {
                HashAlgorithmType.MD5,
                HashAlgorithmType.SHA256
            };

            var result = await service.ComputeHashesAsync(tempFile, algorithms, null, CancellationToken.None);

            // Hashes conocidos del contenido vacío
            Assert.Equal(0, result.FileSize);
            Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", result.Hashes[HashAlgorithmType.MD5]);
            Assert.Equal(
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                result.Hashes[HashAlgorithmType.SHA256]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeHashesAsync_NoAlgorithms_ThrowsArgumentException()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComputeHashesAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
        var missingPath = Path.Combine(Path.GetTempPath(), $"forzip_missing_{Guid.NewGuid():N}.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ComputeHashesAsync(missingPath, algorithms, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComputeHashesAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ComputeHashesAsync(SampleFilePath, algorithms, null, cts.Token));
    }

    [Fact]
    public void ComputeSha256_KnownString_ReturnsExpectedHash()
    {
        var service = new HashService();

        var hash = service.ComputeSha256("test");

        Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", hash);
    }
}
