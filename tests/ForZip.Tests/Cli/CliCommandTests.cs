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
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Cli;

// En serie con los demás que tocan la consola: verify --write-report la redirige mientras verifica, y en paralelo
// podía devolverle a otro test un Console.Out que ya no era el suyo.
[Collection(ForZip.Tests.Sealing.SealingCollection.Name)]
public class CliCommandTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _srcDir;

    private readonly HashService _hashService = new();
    private readonly LocalizationService _localization = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly SignatureService _signatureService = new();
    private readonly PasswordService _passwordService = new();
    private readonly VerificationService _verificationService;
    private readonly SealingService _sealingService;
    private readonly ReceiptService _receiptService;
    private readonly SealedPackageBuilder _sealedPackageBuilder;
    private readonly SealedVerificationService _sealedVerificationService;
    private readonly DeliveryActService _actService;

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

        // Pocas iteraciones de KDF: lo que se ejerce acá son los comandos, no el costo del
        // derivado, que en producción es de 600.000.
        _sealingService = new SealingService(1000);
        _receiptService = new ReceiptService(_hashService, _signatureService);
        _sealedPackageBuilder = new SealedPackageBuilder(_sealingService, _receiptService);
        _sealedVerificationService = new SealedVerificationService(
            _sealingService, _receiptService, _signatureService, _verificationService, _hashService);
        _actService = new DeliveryActService(_localization);
    }

    private VerifyCommand CreateVerifyCommand() =>
        new(_reportService, _verificationService, _sealedVerificationService, _localization);

    private ZipCommand CreateZipCommand() =>
        new(_zipService, _reportService, _hashService, _signatureService,
            _sealedPackageBuilder, _passwordService, _actService, _localization);

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
    public async Task ZipCommand_GeneratesTheRequestedReportAndManifest_WithoutSidecar()
    {
        var (zip, report, _) = await RunZipAsync();

        Assert.True(File.Exists(zip), "el ZIP no se creó");
        Assert.True(File.Exists(report), "el informe no se creó");
        Assert.False(File.Exists(report + ".sha256"), "el .sha256 del informe ya no se genera");
        Assert.True(File.Exists(zip + ".manifest.json"), "el manifiesto no se creó");
    }

    [Fact]
    public async Task ZipCommand_WithHashAndNoReport_LeavesOnlyThePackageAndItsManifest()
    {
        var output = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(output);
        var zip = Path.Combine(output, "caso.zip");

        var code = await CreateZipCommand().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", zip, "--hash", "sha256", "--operator", "Perito", "--case", "12/26"
        }));

        Assert.Equal(0, code);
        Assert.Equal(new[] { "caso.zip", "caso.zip.manifest.json" },
            Directory.GetFiles(output).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        var manifest = _verificationService.ParseManifest(File.ReadAllText(zip + ".manifest.json"));
        Assert.Equal("12/26", manifest.CaseNumber);
        Assert.Equal(Environment.MachineName, manifest.Environment?.MachineName);
    }

    [Fact]
    public void ZipCommand_NoSidecarOption_IsGone()
    {
        // 2.0: el zip ya no escribe .sha256 del informe; la opción se rechaza en vez de aceptarse sin efecto.
        Assert.Equal(new[] { "--no-sidecar" }, CliOptions.Unknown("zip", new[] { "-i", "-o", "--no-sidecar" }));
        Assert.Empty(CliOptions.Unknown("hash", new[] { "-i", "--no-sidecar" }));
    }

    [Fact]
    public async Task ZipCommand_ReservedProfileWithReport_IsRejected()
    {
        var (code, output) = await CaptureAsync(() => CreateZipCommand().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", Path.Combine(_workDir, "r.zip"), "--profile", "reserved",
            "--acta", Path.Combine(_workDir, "acta.txt"), "--report", Path.Combine(_workDir, "r.txt")
        })));

        Assert.Equal(1, code);
        Assert.Contains("--write-report", output);
        Assert.False(File.Exists(Path.Combine(_workDir, "r.zip")));
    }

    [Fact]
    public async Task VerifyCommand_WriteReport_RecordsTheVerdict()
    {
        var (zip, _, manifest) = await RunZipAsync(generateReport: false);
        var report = Path.Combine(_workDir, "informe", "ev.informe.txt");

        var code = await CreateVerifyCommand().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-m", manifest, "--write-report", report, "--lang", "es"
        }));

        Assert.Equal(0, code);
        var text = File.ReadAllText(report);
        Assert.Contains("INFORME ARMADO A PEDIDO", text);
        Assert.Contains("VERIFICACIÓN", text);
        Assert.Contains("Evidencia íntegra", text);
        Assert.Contains("a.txt", text);
        Assert.Contains(_verificationService.ParseManifest(File.ReadAllText(manifest)).ZipSha256!, text);
        Assert.False(File.Exists(report + ".sha256"));
    }

    [Fact]
    public async Task VerifyCommand_WriteReport_OfATamperedPackage_SaysCompromised()
    {
        var (zip, _, manifest) = await RunZipAsync(generateReport: false);
        var bytes = File.ReadAllBytes(zip);
        bytes[^30] ^= 0xFF;
        File.WriteAllBytes(zip, bytes);
        var report = Path.Combine(_workDir, "alterado.informe.txt");

        var code = await CreateVerifyCommand().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-m", manifest, "--write-report", report, "--lang", "es"
        }));

        Assert.Equal(3, code);
        Assert.Contains("Evidencia comprometida", File.ReadAllText(report), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyCommand_WriteReport_AsHtml()
    {
        var (_, _, manifest) = await RunZipAsync(generateReport: false);
        var report = Path.Combine(_workDir, "ev.informe.html");

        var code = await CreateVerifyCommand().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-m", manifest, "--write-report", report, "--lang", "es"
        }));

        Assert.Equal(0, code);
        var html = File.ReadAllText(report);
        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart('\uFEFF'));
        Assert.Contains("class=\"verdict ok\"", html);
        Assert.Contains("Evidencia íntegra", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZipCommand_ReportAsHtml()
    {
        var zip = Path.Combine(_workDir, "html.zip");
        var report = Path.Combine(_workDir, "html.informe.html");

        var code = await CreateZipCommand().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", zip, "--hash", "sha256", "--report", report
        }));

        Assert.Equal(0, code);
        Assert.Contains("<table class=\"grid\">", File.ReadAllText(report));
        Assert.False(File.Exists(report + ".sha256"));
    }

    [Fact]
    public async Task VerifyCommand_WriteReport_DoesNotOverwrite()
    {
        var (_, _, manifest) = await RunZipAsync(generateReport: false);
        var report = Path.Combine(_workDir, "ya_existe.txt");
        File.WriteAllText(report, "no tocar");

        var code = await CreateVerifyCommand().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-m", manifest, "--write-report", report
        }));

        Assert.Equal(1, code);
        Assert.Equal("no tocar", File.ReadAllText(report));
    }

    [Fact]
    public async Task VerifyCommand_ReportSidecar_ReturnsZero()
    {
        // Los paquetes de antes de 2.0 traían el informe con su .sha256: se siguen verificando.
        var (_, report, _) = await RunZipAsync();
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(report))).ToLowerInvariant();
        File.WriteAllText(report + ".sha256", $"{hash}  {Path.GetFileName(report)}");

        var parser = new CommandParser(new[] { "verify", "-r", report });
        var code = await CreateVerifyCommand().ExecuteAsync(parser);

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task VerifyCommand_IntactArchive_ReturnsZero()
    {
        var (zip, _, _) = await RunZipAsync();

        var parser = new CommandParser(new[] { "verify", "-m", zip + ".manifest.json", "-z", zip });
        var code = await CreateVerifyCommand().ExecuteAsync(parser);

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
        var code = await CreateVerifyCommand().ExecuteAsync(parser);

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
        var verifyCode = await CreateVerifyCommand().ExecuteAsync(verifyParser);
        Assert.Equal(0, verifyCode);
    }

    [Fact]
    public async Task VerifyCommand_NoArguments_ReturnsOne()
    {
        var parser = new CommandParser(new[] { "verify" });
        var code = await CreateVerifyCommand().ExecuteAsync(parser);

        Assert.Equal(1, code);
    }

    [Fact]
    public async Task ZipCommand_OutputWith7zExtension_Packs7z_AndVerifyAndUnzipWork()
    {
        var archive = Path.Combine(_workDir, "ev.7z");
        var (_, _, manifest) = await RunZipAsync(archive);

        Assert.Equal(ArchiveFormat.SevenZ, ArchiveFormats.Detect(archive));
        Assert.Contains("\"ArchiveFormat\": \"7z\"", File.ReadAllText(manifest));

        var verify = new CommandParser(new[] { "verify", "-m", manifest });
        Assert.Equal(0, await CreateVerifyCommand().ExecuteAsync(verify));

        var outDir = Path.Combine(_workDir, "extraido");
        var unzip = new CommandParser(new[] { "unzip", "-i", archive, "-o", outDir });
        Assert.Equal(0, await new UnzipCommand(_zipService, _localization).ExecuteAsync(unzip));
        Assert.Equal("contenido A", File.ReadAllText(Path.Combine(outDir, "src", "a.txt")));
    }

    [Fact]
    public async Task ZipCommand_FormatOption_WithoutOutput_UsesThe7zExtension()
    {
        var parser = new CommandParser(new[] { "zip", "-i", _srcDir, "--format", "7z" });
        Assert.Equal(0, await CreateZipCommand().ExecuteAsync(parser));

        var expected = Path.Combine(_workDir, "src.7z");
        Assert.True(File.Exists(expected), "no se creó src.7z junto al origen");
        Assert.Equal(ArchiveFormat.SevenZ, ArchiveFormats.Detect(expected));
    }

    [Fact]
    public async Task ZipCommand_UnknownFormat_ReturnsOne()
    {
        var parser = new CommandParser(new[] { "zip", "-i", _srcDir, "--format", "rar" });
        Assert.Equal(1, await CreateZipCommand().ExecuteAsync(parser));
    }

    [Fact]
    public async Task ZipCommand_ReservedProfileWith7z_IsRejectedBeforeWritingAnything()
    {
        var output = Path.Combine(_workDir, "reservado.7z");
        var parser = new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", output, "--profile", "reserved", "--acta", Path.Combine(_workDir, "acta.txt")
        });

        Assert.Equal(1, await CreateZipCommand().ExecuteAsync(parser));
        Assert.Empty(Directory.EnumerateFiles(_workDir, "reservado*"));
        Assert.False(File.Exists(Path.Combine(_workDir, "acta.txt")));
    }

    [Fact]
    public async Task VerifyCommand_ArchiveWithoutManifest_IsTested_ExitCodesByOutcome()
    {
        var (zip, _, manifest) = await RunZipAsync(generateReport: false);
        File.Delete(manifest);

        var (okCode, okOutput) = await CaptureAsync(() => CreateVerifyCommand().ExecuteAsync(new CommandParser(new[] { "verify", "-i", zip })));
        Assert.Equal(0, okCode);
        Assert.Contains("SIN ERRORES", okOutput);
        Assert.Contains("Formato: ZIP", okOutput);

        var exe = Path.Combine(_workDir, "programa.exe");
        File.WriteAllBytes(exe, [(byte)'M', (byte)'Z', 1, 2, 3]);
        var (exeCode, exeOutput) = await CaptureAsync(() => CreateVerifyCommand().ExecuteAsync(new CommandParser(new[] { "verify", "-i", exe })));
        Assert.Equal(1, exeCode); // un .exe cualquiera no tiene nada que verificar: no es una alteración
        Assert.Contains("no es un autoextraíble", exeOutput);
    }

    [Fact]
    public async Task VerifyCommand_ArchiveWithItsManifest_GoesToTheManifest()
    {
        var (zip, _, _) = await RunZipAsync(generateReport: false);

        var (code, output) = await CaptureAsync(() => CreateVerifyCommand().ExecuteAsync(new CommandParser(new[] { "verify", "-i", zip })));

        Assert.Equal(0, code);
        Assert.Contains("EVIDENCIA ÍNTEGRA", output);
    }

    [Fact]
    public async Task HashCommand_Folder_PrintsTheFolderFingerprints_AndRefusesAReportInside()
    {
        var (code, output) = await CaptureAsync(() => new HashCommand(_hashService, _reportService, _localization)
            .ExecuteAsync(new CommandParser(new[] { "hash", "-i", _srcDir, "-a", "sha256" })));

        Assert.Equal(0, code);
        string Sha(string s) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
        Assert.Contains(FolderHash.ForZipDigest(new[] { ("a.txt", Sha("contenido A")), ("b.txt", Sha("contenido B")) }), output);
        Assert.Contains(FolderHash.SevenZipDataSum(new[] { Sha("contenido A"), Sha("contenido B") }), output);

        var inside = Path.Combine(_srcDir, "informe.txt");
        var (refused, _) = await CaptureAsync(() => new HashCommand(_hashService, _reportService, _localization)
            .ExecuteAsync(new CommandParser(new[] { "hash", "-i", _srcDir, "-r", inside })));
        Assert.Equal(1, refused);
        Assert.False(File.Exists(inside));
    }

    private static async Task<(int Code, string Output)> CaptureAsync(Func<Task<int>> action)
    {
        var originalOut = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var code = await action();
            return (code, captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
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
        var code = await CreateZipCommand()
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

    // -------------------------------------------------------------------------
    //  Opciones repetidas y varios orígenes en zip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ZipCommand_SeveralInputs_PackThemAllTogether()
    {
        // Como la selección múltiple de la GUI: un archivo suelto y una carpeta en el mismo paquete.
        var loose = Path.Combine(_workDir, "acta.txt");
        File.WriteAllText(loose, "acta suelta");
        var zip = Path.Combine(_workDir, "varios.zip");

        var code = await CreateZipCommand().ExecuteAsync(new CommandParser(new[] { "zip", "-i", loose, "--input", _srcDir, "-o", zip }));

        Assert.Equal(0, code);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "acta.txt", "src/a.txt", "src/b.txt" }, names);
    }

    [Fact]
    public async Task ZipCommand_SeveralInputsWithoutOutput_IsRejected()
    {
        var loose = Path.Combine(_workDir, "acta.txt");
        File.WriteAllText(loose, "acta suelta");

        var code = await CreateZipCommand().ExecuteAsync(new CommandParser(new[] { "zip", "-i", loose, "-i", _srcDir }));

        Assert.Equal(1, code);
        Assert.Empty(Directory.EnumerateFiles(_workDir, "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ZipCommand_SameInputTwice_IsRejected()
    {
        var code = await CreateZipCommand().ExecuteAsync(
            new CommandParser(new[] { "zip", "-i", _srcDir, "-i", _srcDir + Path.DirectorySeparatorChar, "-o", Path.Combine(_workDir, "x.zip") }));

        Assert.Equal(1, code);
        Assert.False(File.Exists(Path.Combine(_workDir, "x.zip")));
    }

    [Theory]
    [InlineData("zip", new[] { "-p", "a", "-p", "b" }, "-p")]
    [InlineData("zip", new[] { "-p", "a", "--password", "b" }, "-p")]
    [InlineData("zip", new[] { "--split", "1M", "--volume-size", "2M" }, "--split")]
    [InlineData("unzip", new[] { "-i", "a.zip", "-i", "b.zip" }, "-i")]
    [InlineData("verify", new[] { "-m", "a.json", "--manifest", "b.json" }, "-m")]
    [InlineData("sign", new[] { "-p", "x", "--cert-password", "y" }, "-p")]
    public void RepeatedOption_IsReported(string command, string[] options, string expected)
    {
        // Antes ganaba la última sin aviso: "-p a -p b" cifraba con b.
        var parser = new CommandParser(new[] { command }.Concat(options).ToArray());
        Assert.Equal(new[] { expected }, CliOptions.Repeated(command, parser.OptionOccurrences));
    }

    [Fact]
    public void RepeatedInput_InZip_IsAllowed()
    {
        var parser = new CommandParser(new[] { "zip", "-i", "a", "--input", "b", "-o", "c.zip" });
        Assert.Empty(CliOptions.Repeated("zip", parser.OptionOccurrences));
        Assert.Equal(new[] { "a", "b" }, parser.GetOptionValues("-i", "--input"));
    }

    [Fact]
    public void Synonyms_MatchWhatTheCommandsRead()
    {
        // Cada GetOption("corto", "largo") de un comando tiene que estar en CliOptions.Synonyms de ese comando:
        // si no, "-x a --xx b" no se vería como repetida.
        var commands = new Dictionary<string, string>
        {
            ["ZipCommand.cs"] = "zip", ["UnzipCommand.cs"] = "unzip", ["HashCommand.cs"] = "hash",
            ["VerifyCommand.cs"] = "verify", ["SealCommand.cs"] = "seal", ["UnsealCommand.cs"] = "unseal",
            ["SignCommand.cs"] = "sign", ["GenPassCommand.cs"] = "genpass"
        };
        var read = new System.Text.RegularExpressions.Regex(@"(?:GetOption|HasOption|GetOptionValues)\(""([^""]+)"",\s*""([^""]+)""\)");

        foreach (var (file, command) in commands)
        {
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CliSources", file));
            foreach (System.Text.RegularExpressions.Match m in read.Matches(source))
            {
                var (shortName, longName) = (m.Groups[1].Value, m.Groups[2].Value);
                if (shortName != longName)
                {
                    Assert.Contains((shortName, longName), CliOptions.Synonyms[command]);
                }
            }
        }
    }
}

