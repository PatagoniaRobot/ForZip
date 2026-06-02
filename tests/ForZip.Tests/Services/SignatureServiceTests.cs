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
using System.Security.Cryptography.X509Certificates;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class SignatureServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly SignatureService _service = new();

    public SignatureServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_sig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
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
    public async Task Sign_ThenVerify_IsValid()
    {
        var manifestPath = await CreateManifestAsync("{ \"data\": 1 }");
        var pfxPath = CreateSelfSignedPfx("CN=Perito Test");

        await _service.SignAsync(manifestPath, pfxPath, "test", CancellationToken.None);
        var info = _service.Verify(manifestPath);

        Assert.True(info.Present);
        Assert.True(info.Valid);
        Assert.Contains("Perito Test", info.SignerSubject);
        Assert.NotNull(info.SignedAtUtc);
    }

    [Fact]
    public async Task Verify_TamperedManifest_IsInvalid()
    {
        var manifestPath = await CreateManifestAsync("{ \"data\": 1 }");
        var pfxPath = CreateSelfSignedPfx("CN=Perito Test");

        await _service.SignAsync(manifestPath, pfxPath, "test", CancellationToken.None);

        // Alterar el manifiesto después de firmar
        await File.WriteAllTextAsync(manifestPath, "{ \"data\": 999 }");
        var info = _service.Verify(manifestPath);

        Assert.True(info.Present);
        Assert.False(info.Valid);
    }

    [Fact]
    public async Task Verify_NoSignature_ReportsAbsent()
    {
        var manifestPath = await CreateManifestAsync("{ \"data\": 1 }");

        var info = _service.Verify(manifestPath);

        Assert.False(info.Present);
        Assert.False(info.Valid);
    }

    private async Task<string> CreateManifestAsync(string content)
    {
        var path = Path.Combine(_workDir, "evidence.zip.manifest.json");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private string CreateSelfSignedPfx(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var pfxBytes = cert.Export(X509ContentType.Pfx, "test");
        var pfxPath = Path.Combine(_workDir, "operator.pfx");
        File.WriteAllBytes(pfxPath, pfxBytes);
        return pfxPath;
    }
}
