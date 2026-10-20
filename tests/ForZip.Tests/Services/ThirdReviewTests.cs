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
using System.Text.RegularExpressions;
using ForZip.Cli;
using ForZip.Cli.Commands;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using ForZip.Core.Shell;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Tercera pasada del 2026-10-10: identidad del firmante por huella, Verificar sin contra qué, inventario de hashes
/// unificado, CLI estricta, carpeta de datos y configuración compartida entre procesos.
/// </summary>
public class ThirdReviewTests : IDisposable
{
    private readonly string _workDir;
    private readonly HashService _hashService = new();
    private readonly LocalizationService _localization = new();

    public ThirdReviewTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_third_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // -------------------------------------------------------------------------
    //  Firmante: huella y confianza
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Signature_ReportsTheCertificateFingerprint()
    {
        var (pfx, expected) = CreatePfx("CN=Perito X");
        var manifest = Path.Combine(_workDir, "caso.zip.manifest.json");
        File.WriteAllText(manifest, "{}");
        var signatures = new SignatureService();
        await signatures.SignAsync(manifest, pfx, "pfx", CancellationToken.None);

        var info = signatures.Verify(manifest);

        Assert.True(info.Valid);
        Assert.Equal(expected, info.SignerThumbprint);
    }

    [Fact]
    public void SignerTrust_SameNameDifferentCertificate_IsNotTrusted()
    {
        // Lo que identifica al certificado es la huella, no el nombre: dos "CN=Perito X" distintos no son el mismo.
        var (_, genuine) = CreatePfx("CN=Perito X");
        var (_, impostor) = CreatePfx("CN=Perito X");
        var trusted = new[] { new TrustedSigner { Name = "Perito X", Thumbprint = SignerTrust.Format(genuine) } };

        Assert.Equal(SignerTrustLevel.Trusted, SignerTrust.Evaluate(genuine, trusted).Level);
        Assert.Equal(SignerTrustLevel.Unknown, SignerTrust.Evaluate(impostor, trusted).Level);
        Assert.Equal(SignerTrustLevel.NoList, SignerTrust.Evaluate(genuine, Array.Empty<TrustedSigner>()).Level);
    }

    [Theory]
    [InlineData("ab cd")]
    [InlineData("no-es-hex-zz")]
    public void SignerTrust_RejectsWhatIsNotAFingerprint(string text) => Assert.Null(SignerTrust.TryNormalize(text));

    [Fact]
    public void SignerTrust_AcceptsTheFormsWindowsShows()
    {
        var hex = new string('a', 64);
        var withColons = string.Join(":", Enumerable.Range(0, 32).Select(_ => "AA"));
        Assert.Equal(new string('A', 64), SignerTrust.TryNormalize(hex));
        Assert.Equal(new string('A', 64), SignerTrust.TryNormalize(withColons));
        Assert.Equal(new string('A', 64), SignerTrust.TryNormalize("\u200E" + hex)); // la marca invisible de certmgr
    }

    [Fact]
    public async Task CliVerify_InvalidTrustedSignerOption_DoesNothing()
    {
        var reportService = new ReportService(_localization);
        var signatures = new SignatureService();
        var verification = new VerificationService(_hashService, signatures);
        var sealing = new SealingService(1000);
        var sealedVerification = new SealedVerificationService(
            sealing, new ReceiptService(_hashService, signatures), signatures, verification, _hashService);
        var command = new VerifyCommand(reportService, verification, sealedVerification, _localization);

        var code = await command.ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-m", Path.Combine(_workDir, "x.manifest.json"), "--trusted-signer", "no-es-una-huella"
        }));

        Assert.Equal(1, code);
    }

    // -------------------------------------------------------------------------
    //  Verificar: incompleto no es alterado
    // -------------------------------------------------------------------------

    [Fact]
    public void Report_WithoutSidecar_IsUnverifiableNotAltered()
    {
        var reports = new ReportService(_localization);
        var path = Path.Combine(_workDir, "informe.txt");
        File.WriteAllText(path, "informe");

        Assert.Equal(ReportIntegrity.Unverifiable, reports.CheckReport(path).status);

        File.WriteAllText(path + ".sha256", new string('0', 64) + "  informe.txt");
        Assert.Equal(ReportIntegrity.Altered, reports.CheckReport(path).status);
    }

    [Fact]
    public void ShellMenu_ThirdPartyFormats_DoNotOfferVerify()
    {
        var labels = new ShellMenuLabels("ForZip", "C", "CR", "H", "E", "EH", "ET", "V", "VS", "OS", "T");
        var entries = ShellMenuLayout.Build(@"C:\ForZip\ForZip.exe", labels);

        static List<string> CommandsFor(IEnumerable<ShellRegistryEntry> all, string ext) => all
            .Where(e => e.SubKey.StartsWith($@"Software\Classes\SystemFileAssociations\{ext}\shell\ForZip\shell\")
                        && e.SubKey.EndsWith(@"\command"))
            .Select(e => e.Value!)
            .ToList();

        Assert.DoesNotContain(CommandsFor(entries, ".rar"), v => v.Contains(" verify "));
        Assert.DoesNotContain(CommandsFor(entries, ".iso"), v => v.Contains(" verify "));
        Assert.Contains(CommandsFor(entries, ".rar"), v => v.Contains(" extract "));
        Assert.Contains(CommandsFor(entries, ".7z"), v => v.Contains(" verify "));
        Assert.Contains(CommandsFor(entries, ".zip"), v => v.Contains(" verify "));
    }

    // -------------------------------------------------------------------------
    //  Inventario de hashes
    // -------------------------------------------------------------------------

    [Fact]
    public void HashInventory_Folder_IsRecursiveWithRelativeNames()
    {
        var root = Path.Combine(_workDir, "caso");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "b");

        var items = HashInventory.Expand(new[] { root }, new List<OmittedFile>());

        Assert.Equal(new[] { "caso/a.txt", "caso/sub/b.txt" }, items.Select(i => i.DisplayName).OrderBy(n => n));
        Assert.All(items, i => Assert.True(Path.IsPathRooted(i.FullPath)));
    }

    [Fact]
    public async Task CliHash_UnreadableFile_IsDocumentedAndTheRestIsHashed()
    {
        var root = Path.Combine(_workDir, "lote");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sano.txt"), "sano");
        var locked = Path.Combine(root, "bloqueado.txt");
        File.WriteAllText(locked, "bloqueado");
        var report = Path.Combine(_workDir, "hashes.txt");

        int code;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            code = await new HashCommand(_hashService, new ReportService(_localization), _localization)
                .ExecuteAsync(new CommandParser(new[] { "hash", "-i", root, "-a", "SHA-256", "-r", report, "--lang", "es" }));
        }

        // Antes un archivo ilegible cortaba todo el lote; ahora va al informe como no leído y el código lo dice.
        Assert.Equal(ZipCommand.IncompleteExitCode, code);
        var text = File.ReadAllText(report);
        Assert.Contains("lote/sano.txt", text);
        Assert.Contains("no se pudo leer", text);
        Assert.True(File.Exists(report + ".sha256"));
    }

    [Fact]
    public void HashService_ResultCarriesFullPathAndModifiedTime()
    {
        var file = Path.Combine(_workDir, "a.txt");
        File.WriteAllText(file, "a");
        var when = new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, when);

        var result = _hashService.ComputeHashesAsync(file, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
            null, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(Path.GetFullPath(file), result.SourcePath);
        Assert.Equal(when, result.ModifiedUtc!.Value.UtcDateTime);
    }

    // -------------------------------------------------------------------------
    //  CLI estricta
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("sha-256,MD5", 2)]
    [InlineData("sha_512", 1)]
    public void CliAlgorithms_AcceptCommonSpellings(string text, int count)
    {
        Assert.True(CliOptions.TryParseAlgorithms(text, out var algorithms, out _));
        Assert.Equal(count, algorithms.Count);
    }

    [Fact]
    public void CliAlgorithms_UnknownName_IsAnError()
    {
        // Antes "-a sha-256,md5" calculaba solo MD5, sin aviso.
        Assert.False(CliOptions.TryParseAlgorithms("sha3,md5", out _, out var error));
        Assert.Contains("sha3", error);
    }

    [Fact]
    public void CliOptions_UnknownOption_IsReported()
    {
        // zip -r no existe (la opción es --report): antes se ignoraba y no salía el informe.
        Assert.Equal(new[] { "-r" }, CliOptions.Unknown("zip", new[] { "-i", "-o", "-r" }));
        Assert.Empty(CliOptions.Unknown("hash", new[] { "-i", "-r", "-a" }));
    }

    [Fact]
    public void CliOptions_TableCoversEveryOptionTheCommandsRead()
    {
        // Si un comando lee una opción que la tabla no tiene, la CLI la rechazaría: los dos tienen que coincidir.
        var commands = new Dictionary<string, string>
        {
            ["ZipCommand.cs"] = "zip", ["UnzipCommand.cs"] = "unzip", ["HashCommand.cs"] = "hash",
            ["VerifyCommand.cs"] = "verify", ["SealCommand.cs"] = "seal", ["UnsealCommand.cs"] = "unseal",
            ["SignCommand.cs"] = "sign", ["GenPassCommand.cs"] = "genpass"
        };
        var dir = Path.Combine(AppContext.BaseDirectory, "CliSources");
        var read = new Regex(@"(?:GetOption|HasOption)\(""([^""]+)"",\s*""([^""]+)""\)");

        foreach (var (file, command) in commands)
        {
            var source = File.ReadAllText(Path.Combine(dir, file));
            var used = read.Matches(source).SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value }).Distinct();
            Assert.Empty(CliOptions.Unknown(command, used));
        }
    }

    [Fact]
    public void Parser_PasswordOption_DoesNotSwallowAnotherOption()
    {
        var parser = new CommandParser(new[] { "seal", "-i", "x", "-p", "--gen-password", "--acta", "a.txt" });

        Assert.Equal(string.Empty, parser.GetOption("-p", "--password"));
        Assert.True(parser.HasOption("--gen-password", "--gen-password"));
    }

    // -------------------------------------------------------------------------
    //  Configuración y carpeta de datos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Config_ConcurrentReadersAndWriters_DoNotFail()
    {
        // La ventana principal y la del hash rápido son procesos distintos que comparten config.json.
        var dir = Path.Combine(_workDir, "cfg");
        Directory.CreateDirectory(dir);
        var a = new ConfigService(dir);
        var b = new ConfigService(dir);
        a.Save(new AppConfig { Language = "es" });

        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            for (int n = 0; n < 40; n++)
            {
                var service = i % 2 == 0 ? a : b;
                var config = service.Load();
                config.DefaultCompressionLevel = n % 9;
                service.Save(config);
            }
        }));

        await Task.WhenAll(tasks);
        Assert.NotNull(a.Load());
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void AppDataLocation_TempIsWritable() => Assert.True(AppDataLocation.IsWritable(_workDir));

    [Theory]
    [InlineData(@"C:\e\caso.tar.gz", "caso")]
    [InlineData(@"C:\e\caso.part1.rar", "caso")]
    [InlineData(@"C:\e\caso.zip.001", "caso")]
    [InlineData(@"C:\e\caso.zip", "caso")]
    [InlineData(@"C:\e\fotos.tar", "fotos")]
    public void FolderName_StripsCompoundExtensions(string path, string expected) =>
        Assert.Equal(expected, SplitArchive.FolderNameFor(path));

    // -------------------------------------------------------------------------

    private (string Path, string Thumbprint) CreatePfx(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_workDir, $"cert-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, "pfx"));
        return (path, cert.GetCertHashString(HashAlgorithmName.SHA256));
    }
}
