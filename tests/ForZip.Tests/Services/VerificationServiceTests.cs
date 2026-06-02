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

public class VerificationServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly HashService _hashService = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly VerificationService _verificationService;

    public VerificationServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _zipService = new ZipService(_hashService);
        _reportService = new ReportService(new LocalizationService());
        _verificationService = new VerificationService(_hashService, new SignatureService());
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
            // Limpieza best-effort
        }
    }

    [Fact]
    public async Task VerifyArchive_UntouchedZip_IsIntact()
    {
        var (zipPath, manifestPath) = await BuildEvidenceAsync();

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, null, null, CancellationToken.None);

        Assert.True(result.IsIntact);
        Assert.Equal(2, result.OkCount);
        Assert.Equal(0, result.AlteredCount);
        Assert.True(result.ZipHashMatches);
    }

    [Fact]
    public async Task VerifyArchive_TamperedContent_DetectsAlteration()
    {
        var (zipPath, manifestPath) = await BuildEvidenceAsync();

        // Re-empaquetar con el contenido de un archivo modificado, reutilizando el manifiesto original
        await File.WriteAllTextAsync(Path.Combine(_workDir, "src", "a.txt"), "CONTENIDO ALTERADO");
        await _zipService.CompressAsync(new ZipOptions
        {
            SourcePaths = new List<string> { Path.Combine(_workDir, "src") },
            OutputPath = zipPath,
            CompressionLevel = 5
        }, null, CancellationToken.None);

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, null, null, CancellationToken.None);

        Assert.False(result.IsIntact);
        Assert.True(result.AlteredCount >= 1);
    }

    [Fact]
    public async Task VerifyArchive_ResolvesZipFromManifestName()
    {
        var (_, manifestPath) = await BuildEvidenceAsync();

        // Sin pasar la ruta del ZIP: debe resolverla desde el manifiesto
        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, null, null, null, CancellationToken.None);

        Assert.True(result.IsIntact);
    }

    private async Task<(string zipPath, string manifestPath)> BuildEvidenceAsync()
    {
        var srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), "contenido A");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "b.txt"), "contenido B");

        var zipPath = Path.Combine(_workDir, "evidence.zip");
        var options = new ZipOptions
        {
            SourcePaths = new List<string> { srcDir },
            OutputPath = zipPath,
            CompressionLevel = 5,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
        };

        var results = await _zipService.CompressAsync(options, null, CancellationToken.None);

        var zipHash = (await _hashService.ComputeHashesAsync(
            zipPath, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None))
            .Hashes[HashAlgorithmType.SHA256];

        var data = new ReportData
        {
            Operation = OperationType.Compression,
            CompressionLevel = 5,
            Algorithms = options.HashAlgorithms,
            ZipFilePath = zipPath,
            ZipFileSize = new FileInfo(zipPath).Length,
            ZipHash = zipHash,
            FileResults = results
        };

        var manifestPath = zipPath + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));

        return (zipPath, manifestPath);
    }
}
