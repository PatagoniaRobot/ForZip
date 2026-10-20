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

using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ForZip.Cli;
using ForZip.Cli.Commands;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Cuarta pasada del 2026-10-10: firma inválida sin "de confianza", clave aparte para el paquete de adentro del
/// sobre, sobre vacío, nombres del contenedor interno, archivo y carpeta con el mismo nombre al extraer, y la CLI
/// de zip (carpeta de salida, opciones sin --report).
/// </summary>
/// <remarks>En la colección de sellado porque varios capturan la consola, que es global.</remarks>
[Collection(ForZip.Tests.Sealing.SealingCollection.Name)]
public class FourthReviewTests : IDisposable
{
    private const string PackagePassword = "clave-del-paquete-2026";
    private const string EnvelopePassword = "otra-clave-distinta-del-sobre-2026";

    private readonly string _workDir;
    private readonly string _srcDir;
    private readonly HashService _hashService = new();
    private readonly LocalizationService _localization = new();
    private readonly SignatureService _signatureService = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly VerificationService _verificationService;
    private readonly SealingService _sealingService;
    private readonly ReceiptService _receiptService;
    private readonly SealedVerificationService _sealedVerification;

    public FourthReviewTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_fourth_{Guid.NewGuid():N}");
        _srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(_srcDir);
        File.WriteAllText(Path.Combine(_srcDir, "a.txt"), "contenido A");
        File.WriteAllText(Path.Combine(_srcDir, "b.txt"), "contenido B");

        _zipService = new ZipService(_hashService);
        _reportService = new ReportService(_localization);
        _verificationService = new VerificationService(_hashService, _signatureService);
        _sealingService = new SealingService(1000, SealedContainerFormat.MinChunkSize);
        _receiptService = new ReceiptService(_hashService, _signatureService);
        _sealedVerification = new SealedVerificationService(
            _sealingService, _receiptService, _signatureService, _verificationService, _hashService);
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
    //  Firma inválida
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Verify_InvalidSignature_DoesNotCallTheSignerTrusted()
    {
        var (zip, manifest) = await BuildPackageAsync(null);
        var (pfx, thumbprint) = CreatePfx("CN=Perito Prueba");
        await _signatureService.SignAsync(manifest, pfx, "pfx", CancellationToken.None);
        await File.AppendAllTextAsync(manifest, " "); // el manifiesto cambia después de firmado

        var (code, output) = await CaptureAsync(() => CreateVerifyCommand().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-m", manifest, "-z", zip, "--trusted-signer", thumbprint
        })));

        // Antes decía "Firmante DE CONFIANZA" en verde debajo de "Firma digital: INVÁLIDA".
        Assert.Equal(3, code);
        Assert.Contains("INVÁLIDA", output);
        Assert.DoesNotContain("DE CONFIANZA", output);
        Assert.Contains("no evaluada", output);
    }

    // -------------------------------------------------------------------------
    //  Sobres
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SealedVerify_InnerPackageWithItsOwnPassword_NeedsPackagePassword()
    {
        var sealedPath = await SealAsync(EnvelopePassword, PackagePassword);

        // Con la clave del sobre sola: el paquete está intacto (hash global) pero no se lo puede leer.
        var withoutPackage = await _sealedVerification.VerifyAsync(
            sealedPath, EnvelopePassword, null, CancellationToken.None);
        Assert.NotNull(withoutPackage.Content);
        Assert.NotNull(withoutPackage.Content!.ContentVerificationError);
        Assert.True(withoutPackage.Content.ZipHashMatches);
        Assert.False(withoutPackage.TamperDetected);

        var withPackage = await _sealedVerification.VerifyAsync(
            sealedPath, EnvelopePassword, null, CancellationToken.None, PackagePassword);
        Assert.True(withPackage.Content!.IsIntact);
        Assert.Equal(2, withPackage.Content.OkCount);
    }

    [Fact]
    public async Task SealedVerifyCli_PrintsWhyTheContentCouldNotBeVerified()
    {
        var sealedPath = await SealAsync(EnvelopePassword, PackagePassword);

        var (code, output) = await CaptureAsync(() => CreateVerifyCommand().ExecuteAsync(
            new CommandParser(new[] { "verify", "-i", sealedPath, "-p", EnvelopePassword })));
        Assert.Equal(2, code);
        Assert.Contains("No se pudo verificar el contenido", output);
        Assert.Contains("--package-password", output);

        (code, output) = await CaptureAsync(() => CreateVerifyCommand().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-i", sealedPath, "-p", EnvelopePassword, "--package-password", PackagePassword
        })));
        Assert.Contains("2 OK", output);
        Assert.DoesNotContain("No se pudo verificar el contenido", output);
    }

    [Fact]
    public async Task EmptyEnvelope_IsRejectedInsteadOfOpeningWithAnyPassword()
    {
        // Sin bloques no hay nada que autentique la clave. ForZip nunca sella un sobre vacío.
        var sealedPath = Path.Combine(_workDir, "vacio.fzs");
        await _sealingService.SealFileAsync(
            await WriteEmptyFileAsync(), sealedPath, "cualquiera", null, CancellationToken.None);

        var result = await _sealedVerification.VerifyAsync(sealedPath, "otra", null, CancellationToken.None);
        Assert.Contains("vacía", result.ContentError);

        var opener = new SealedPackageOpener(_sealingService, _verificationService);
        var destination = Path.Combine(_workDir, "abierto");
        await Assert.ThrowsAsync<SealedFormatException>(() =>
            opener.OpenAsync(sealedPath, destination, "otra", null, CancellationToken.None));
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData("a.txt:oculto")]
    [InlineData("CON.txt")]
    [InlineData("nombre.")]
    public async Task InnerContainer_RejectsNamesWindowsWouldNotKeep(string name)
    {
        var destination = Path.Combine(_workDir, "contenido");
        await using var container = BuildRawZip((name, "x"));

        await Assert.ThrowsAsync<SealedFormatException>(() =>
            SealedPayloadContainer.ExtractAsync(container, destination, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    [Fact]
    public async Task InnerContainer_RejectsDuplicateNames()
    {
        var destination = Path.Combine(_workDir, "contenido");
        await using var container = BuildRawZip(("caso.zip", "primero"), ("CASO.zip", "segundo"));

        await Assert.ThrowsAsync<SealedFormatException>(() =>
            SealedPayloadContainer.ExtractAsync(container, destination, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    // -------------------------------------------------------------------------
    //  Extraer: archivo y carpeta con el mismo nombre
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Extract_FileAndFolderWithTheSameName_KeepsBoth()
    {
        var zip = Path.Combine(_workDir, "choque.zip");
        await using (var raw = BuildRawZip(("k", "archivo"), ("k/y.txt", "hijo"), ("k/z/w.txt", "nieto")))
        await using (var file = File.Create(zip))
        {
            await raw.CopyToAsync(file);
        }

        var output = Path.Combine(_workDir, "salida");
        var result = await _zipService.DecompressAsync(zip, output, null, null, CancellationToken.None);

        // Antes abortaba todo con "ya existe un archivo con ese nombre".
        Assert.Equal(3, result.FilesExtracted);
        Assert.Equal("archivo", File.ReadAllText(Path.Combine(output, "k")));
        Assert.Equal("hijo", File.ReadAllText(Path.Combine(output, "k~2", "y.txt")));
        Assert.Equal("nieto", File.ReadAllText(Path.Combine(output, "k~2", "z", "w.txt")));
        Assert.Contains(result.Renamed, r => r.OriginalName == "k/y.txt" && r.SavedAs == "k~2/y.txt"
            && r.Reason == RenameReason.FolderIsFile);
        Assert.Contains(result.Renamed, r => r.OriginalName == "k/z/w.txt" && r.SavedAs == "k~2/z/w.txt"
            && r.Reason == RenameReason.FolderIsFile);
    }

    // -------------------------------------------------------------------------
    //  CLI zip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ZipCli_CreatesTheOutputFolderAndWarnsAboutOptionsWithoutReport()
    {
        var output = Path.Combine(_workDir, "nueva", "sub", "caso.zip");

        var (code, text) = await CaptureAsync(() => CreateZipCommand().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", output, "--operator", "Perito", "--case", "12/26"
        })));

        Assert.Equal(0, code);
        Assert.True(File.Exists(output));
        Assert.Contains("sin --hash", text);
        Assert.Contains("--operator", text);
        Assert.Contains("--case", text);
    }

    [Fact]
    public async Task ZipCli_DoesNotCreateAFolderInsideTheEvidence()
    {
        var output = Path.Combine(_srcDir, "adentro", "caso.zip");

        // El núcleo se niega (y Program lo informa como error); lo que importa acá es que no quede una carpeta nueva.
        await Assert.ThrowsAsync<ArgumentException>(() => CaptureAsync(() => CreateZipCommand().ExecuteAsync(
            new CommandParser(new[] { "zip", "-i", _srcDir, "-o", output }))));

        Assert.False(Directory.Exists(Path.Combine(_srcDir, "adentro")));
    }

    [Fact]
    public async Task ZipCli_ReportRecordsTheFullPackagePath()
    {
        var output = Path.Combine(_workDir, "caso.zip");
        var report = Path.Combine(_workDir, "caso.report.txt");

        var (code, _) = await CaptureAsync(() => CreateZipCommand().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", Path.GetRelativePath(Environment.CurrentDirectory, output),
            "--hash", "sha256", "--report", report
        })));

        Assert.Equal(0, code);
        Assert.Contains(Path.GetFullPath(output), File.ReadAllText(report));
    }

    // -------------------------------------------------------------------------
    //  Ayudantes
    // -------------------------------------------------------------------------

    private VerifyCommand CreateVerifyCommand() =>
        new(_reportService, _verificationService, _sealedVerification, _localization);

    private ZipCommand CreateZipCommand() =>
        new(_zipService, _reportService, _hashService, _signatureService,
            new SealedPackageBuilder(_sealingService, _receiptService), new PasswordService(),
            new DeliveryActService(_localization), _localization);

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

    /// <summary>ZIP con manifiesto, como lo deja ForZip.</summary>
    private async Task<(string Zip, string Manifest)> BuildPackageAsync(string? password)
    {
        var zip = Path.Combine(_workDir, "evidencia.zip");
        var options = new ZipOptions
        {
            SourcePaths = new List<string> { _srcDir },
            OutputPath = zip,
            CompressionLevel = 5,
            Password = password,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
        };
        var compression = await _zipService.CompressAsync(options, null, CancellationToken.None);

        var zipHash = (await _hashService.ComputeHashesAsync(
            zip, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None))
            .Hashes[HashAlgorithmType.SHA256];
        var data = new ReportData
        {
            Operation = OperationType.Compression,
            CompressionLevel = 5,
            HasPassword = password != null,
            Algorithms = options.HashAlgorithms,
            ZipFilePath = zip,
            ZipFileSize = new FileInfo(zip).Length,
            ZipHash = zipHash,
            FileResults = compression.FileHashes
        };

        var manifest = zip + ".manifest.json";
        await File.WriteAllTextAsync(manifest, _reportService.GenerateManifestJson(data));
        return (zip, manifest);
    }

    /// <summary>Sobre con un paquete cifrado con su propia clave, sellado con otra (sellado a mano).</summary>
    private async Task<string> SealAsync(string envelopePassword, string packagePassword)
    {
        var (zip, manifest) = await BuildPackageAsync(packagePassword);

        var inner = Path.Combine(_workDir, "inner.zip");
        await using (var stream = new FileStream(inner, FileMode.Create, FileAccess.Write))
        {
            await SealedPayloadContainer.CreateAsync(new[] { zip, manifest }, stream, CancellationToken.None);
        }

        var provisional = Path.Combine(_workDir, "provisional.fzs");
        var header = await _sealingService.SealFileAsync(
            inner, provisional, envelopePassword, null, CancellationToken.None);
        var sealedPath = Path.Combine(_workDir, header.BaseName + SealedContainerFormat.SealedExtension);
        File.Move(provisional, sealedPath);

        var receipt = await _receiptService.CreateAsync(sealedPath, header, null, CancellationToken.None);
        await _receiptService.WriteAsync(receipt, _receiptService.GetReceiptPathFor(sealedPath), CancellationToken.None);
        return sealedPath;
    }

    private async Task<string> WriteEmptyFileAsync()
    {
        var path = Path.Combine(_workDir, "vacio.bin");
        await File.WriteAllBytesAsync(path, Array.Empty<byte>());
        return path;
    }

    /// <summary>ZIP armado a mano, con nombres que ForZip nunca escribiría.</summary>
    private static MemoryStream BuildRawZip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private (string Pfx, string Thumbprint) CreatePfx(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_workDir, "perito.pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, "pfx"));
        return (path, cert.GetCertHashString(HashAlgorithmName.SHA256));
    }
}
