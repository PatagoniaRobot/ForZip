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
using ForZip.Cli;
using ForZip.Cli.Commands;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Cli;

public class CliCommandTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _srcDir;

    private readonly HashService _hashService = new();
    private readonly LocalizationService _localization = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly SignatureService _signatureService = new();
    private readonly VerificationService _verificationService;

    public CliCommandTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_cli_{Guid.NewGuid():N}");
        _srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(_srcDir);
        File.WriteAllText(Path.Combine(_srcDir, "a.txt"), "contenido A");
        File.WriteAllText(Path.Combine(_srcDir, "b.txt"), "contenido B");

        _zipService = new ZipService(_hashService);
        _reportService = new ReportService(_localization);
        _verificationService = new VerificationService(_hashService, _signatureService);
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
    public async Task ZipCommand_GeneratesReportManifestAndSidecar()
    {
        var (zip, report, _) = await RunZipAsync();

        Assert.True(File.Exists(zip), "el ZIP no se creó");
        Assert.True(File.Exists(report), "el informe no se creó");
        Assert.True(File.Exists(report + ".sha256"), "el sidecar .sha256 no se creó");
        Assert.True(File.Exists(zip + ".manifest.json"), "el manifiesto no se creó");
    }

    [Fact]
    public async Task VerifyCommand_ReportSidecar_ReturnsZero()
    {
        var (_, report, _) = await RunZipAsync();

        var parser = new CommandParser(new[] { "verify", "-r", report });
        var code = await new VerifyCommand(_reportService, _verificationService, _localization).ExecuteAsync(parser);

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task VerifyCommand_IntactArchive_ReturnsZero()
    {
        var (zip, _, _) = await RunZipAsync();

        var parser = new CommandParser(new[] { "verify", "-m", zip + ".manifest.json", "-z", zip });
        var code = await new VerifyCommand(_reportService, _verificationService, _localization).ExecuteAsync(parser);

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task VerifyCommand_TamperedArchive_ReturnsThree()
    {
        var (zip, _, manifest) = await RunZipAsync();

        // Re-empaquetar a otro ZIP con contenido alterado, verificando con el manifiesto original
        File.WriteAllText(Path.Combine(_srcDir, "a.txt"), "ALTERADO");
        var tamperedZip = Path.Combine(_workDir, "tampered.zip");
        await RunZipAsync(tamperedZip, generateReport: false);

        var parser = new CommandParser(new[] { "verify", "-m", manifest, "-z", tamperedZip });
        var code = await new VerifyCommand(_reportService, _verificationService, _localization).ExecuteAsync(parser);

        Assert.Equal(3, code);
    }

    [Fact]
    public async Task SignCommand_SignsExistingManifest()
    {
        var (zip, _, manifest) = await RunZipAsync();
        var pfx = CreateSelfSignedPfx("CN=CLI Test");

        var parser = new CommandParser(new[] { "sign", "-m", manifest, "-c", pfx, "-p", "test" });
        var code = await new SignCommand(_signatureService, _localization).ExecuteAsync(parser);

        Assert.Equal(0, code);
        Assert.True(File.Exists(manifest + ".p7s"), "no se generó la firma .p7s");

        // Y al verificar, la firma debe aparecer como válida (evidencia íntegra)
        var verifyParser = new CommandParser(new[] { "verify", "-m", manifest, "-z", zip });
        var verifyCode = await new VerifyCommand(_reportService, _verificationService, _localization).ExecuteAsync(verifyParser);
        Assert.Equal(0, verifyCode);
    }

    [Fact]
    public async Task VerifyCommand_NoArguments_ReturnsOne()
    {
        var parser = new CommandParser(new[] { "verify" });
        var code = await new VerifyCommand(_reportService, _verificationService, _localization).ExecuteAsync(parser);

        Assert.Equal(1, code);
    }

    private async Task<(string zip, string report, string manifest)> RunZipAsync(
        string? zipPath = null, bool generateReport = true)
    {
        var zip = zipPath ?? Path.Combine(_workDir, "ev.zip");
        var report = Path.Combine(_workDir, "ev.report.txt");

        var args = new List<string> { "zip", "-i", _srcDir, "-o", zip, "--hash", "sha256" };
        if (generateReport)
        {
            args.AddRange(new[] { "--report", report });
        }

        var parser = new CommandParser(args.ToArray());
        var code = await new ZipCommand(_zipService, _reportService, _hashService, _signatureService, _localization)
            .ExecuteAsync(parser);
        Assert.Equal(0, code);

        return (zip, report, zip + ".manifest.json");
    }

    private string CreateSelfSignedPfx(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var pfxBytes = cert.Export(X509ContentType.Pfx, "test");
        var pfxPath = Path.Combine(_workDir, "cli.pfx");
        File.WriteAllBytes(pfxPath, pfxBytes);
        return pfxPath;
    }
}
