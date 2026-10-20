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

using System.Buffers.Binary;
using System.Text;
using ForZip.Core.Interfaces;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Revisión del 2026-10-10: un header con iteraciones desmedidas se rechaza antes de derivar la clave, y un
/// sobre que queda dañado al escribirse no se entrega (los archivos en claro siguen siendo la única copia).
/// </summary>
[Collection(SealingCollection.Name)]
public class SealingSafetyTests : IDisposable
{
    private const string Password = "clave-de-prueba-del-sobre-2026";
    private readonly string _workDir;
    private readonly SealingService _sealing = new(1000, SealedContainerFormat.MinChunkSize);

    public SealingSafetyTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_sealsafe_{Guid.NewGuid():N}");
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

    private string Artifact()
    {
        var path = Path.Combine(_workDir, "paquete.zip");
        var data = new byte[50_000];
        new Random(3).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public async Task Header_WithHugeIterations_IsRejectedWithoutDerivingTheKey()
    {
        var sealedPath = Path.Combine(_workDir, "s.fzs");
        await _sealing.SealFileAsync(Artifact(), sealedPath, Password, null, CancellationToken.None);

        // Se reescribe el header con 2.000 millones de iteraciones.
        File.WriteAllBytes(sealedPath, WithIterations(File.ReadAllBytes(sealedPath), 2_000_000_000));

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<SealedFormatException>(
            () => _sealing.UnsealFileAsync(sealedPath, Path.Combine(_workDir, "abierto.zip"), Password, null, CancellationToken.None));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "derivó la clave antes de rechazar el header");
    }

    /// <summary>
    /// El mismo sobre con otro valor de <c>kdf.iterations</c> en el header (y la longitud del header ajustada
    /// en el preámbulo). Lo usan también los tests del extractor Python y del abridor HTML.
    /// </summary>
    internal static byte[] WithIterations(byte[] sealedBytes, long iterations)
    {
        int lengthAt = SealedContainerFormat.MagicLength + sizeof(ushort);
        int headerLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(sealedBytes.AsSpan(lengthAt));
        var header = Encoding.UTF8.GetString(sealedBytes, SealedContainerFormat.PreambleLength, headerLength);
        var changed = System.Text.RegularExpressions.Regex.Replace(header, "(\"iterations\"\\s*:\\s*)\\d+", "${1}" + iterations);
        Assert.NotEqual(header, changed);
        var tampered = Encoding.UTF8.GetBytes(changed);

        var rebuilt = new MemoryStream();
        rebuilt.Write(sealedBytes, 0, SealedContainerFormat.PreambleLength);
        rebuilt.Write(tampered);
        int rest = SealedContainerFormat.PreambleLength + headerLength;
        rebuilt.Write(sealedBytes, rest, sealedBytes.Length - rest);
        var result = rebuilt.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(lengthAt), (uint)tampered.Length);
        return result;
    }

    [Fact]
    public async Task Builder_EnvelopeDamagedOnDisk_IsNotDelivered()
    {
        var artifact = Artifact();
        var output = Path.Combine(_workDir, "salida");
        var builder = new SealedPackageBuilder(
            new CorruptingSealingService(_sealing), new ReceiptService(new HashService(), new SignatureService()));

        var ex = await Assert.ThrowsAsync<IOException>(() => builder.BuildAsync(new SealedPackageOptions
        {
            ArtifactPaths = new[] { artifact },
            OutputDirectory = output,
            Password = Password
        }, null, CancellationToken.None));

        Assert.Contains("prueba de apertura", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(output, "*.fzs"));
        Assert.Empty(Directory.GetFiles(output, "*.receipt.json"));
        Assert.True(File.Exists(artifact), "el artefacto en claro tiene que seguir ahí");
    }

    [Fact]
    public async Task Builder_WithUnusableCertificate_FailsBeforeSealingAnything()
    {
        // Una contraseña de PFX mal tipeada aparecía al firmar el recibo, con el sobre ya cifrado y probado.
        var artifact = Artifact();
        var output = Path.Combine(_workDir, "salida");
        var builder = new SealedPackageBuilder(_sealing, new ReceiptService(new HashService(), new SignatureService()));

        await Assert.ThrowsAsync<SigningCertificateException>(() => builder.BuildAsync(new SealedPackageOptions
        {
            ArtifactPaths = new[] { artifact },
            OutputDirectory = output,
            Password = Password,
            SigningCertificatePath = CreatePfx("pfx-correcta"),
            SigningCertificatePassword = "pfx-equivocada"
        }, null, CancellationToken.None));

        Assert.False(Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any(),
            "no se tiene que haber escrito nada en la salida");
        Assert.True(File.Exists(artifact));
    }

    [Fact]
    public async Task Builder_WhenReceiptSigningFails_LeavesNoOrphanReceipt()
    {
        var artifact = Artifact();
        var output = Path.Combine(_workDir, "salida");
        var builder = new SealedPackageBuilder(
            _sealing, new FailingSignReceiptService(new ReceiptService(new HashService(), new SignatureService())));

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(new SealedPackageOptions
        {
            ArtifactPaths = new[] { artifact },
            OutputDirectory = output,
            Password = Password,
            SigningCertificatePath = CreatePfx("pfx"),
            SigningCertificatePassword = "pfx"
        }, null, CancellationToken.None));

        // Antes quedaba un .receipt.json que nombraba a un sobre que ya se había borrado.
        Assert.Empty(Directory.GetFiles(output, "*.fzs"));
        Assert.Empty(Directory.GetFiles(output, "*.receipt.json*"));
        Assert.True(File.Exists(artifact));
    }

    [Fact]
    public async Task Builder_Progress_CoversSealAndTestOpenAsOneRun()
    {
        var reports = new List<(long Done, long Total)>();
        var builder = new SealedPackageBuilder(_sealing, new ReceiptService(new HashService(), new SignatureService()));

        await builder.BuildAsync(new SealedPackageOptions
        {
            ArtifactPaths = new[] { Artifact() },
            OutputDirectory = Path.Combine(_workDir, "salida"),
            Password = Password
        }, new ListProgress(reports), CancellationToken.None);

        // Antes la barra llegaba al 100 % al sellar y volvía a empezar con la prueba de apertura.
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal(reports[0].Total, r.Total));
        Assert.True(reports.Zip(reports.Skip(1)).All(p => p.Second.Done >= p.First.Done), "el avance no retrocede");
        Assert.Equal(reports[^1].Total, reports[^1].Done);
    }

    private sealed class ListProgress(List<(long Done, long Total)> sink) : IProgress<(long bytesProcessed, long totalBytes)>
    {
        public void Report((long bytesProcessed, long totalBytes) value) => sink.Add((value.bytesProcessed, value.totalBytes));
    }

    [Fact]
    public async Task Verify_WithWrongPassword_IsIncompleteNotTampered()
    {
        var (sealedPath, verifier) = await SealWithReceiptAsync();

        var result = await verifier.VerifyAsync(sealedPath, "no-es-la-clave", null, CancellationToken.None);

        // La GUI mostraba esto como "NO VERIFICADO", igual que una alteración; la CLI, como incompleta.
        Assert.True(result.EnvelopeIntact);
        Assert.False(result.IsIntact);
        Assert.False(result.TamperDetected);
        Assert.NotNull(result.ContentError);
    }

    [Fact]
    public async Task Verify_AlteredEnvelope_IsTampered()
    {
        var (sealedPath, verifier) = await SealWithReceiptAsync();
        var bytes = File.ReadAllBytes(sealedPath);
        bytes[^100] ^= 0x01;
        File.WriteAllBytes(sealedPath, bytes);

        var result = await verifier.VerifyAsync(sealedPath, null, null, CancellationToken.None);

        Assert.True(result.TamperDetected);
    }

    private async Task<(string SealedPath, SealedVerificationService Verifier)> SealWithReceiptAsync()
    {
        var hash = new HashService();
        var signature = new SignatureService();
        var receipts = new ReceiptService(hash, signature);
        var built = await new SealedPackageBuilder(_sealing, receipts).BuildAsync(new SealedPackageOptions
        {
            ArtifactPaths = new[] { Artifact() },
            OutputDirectory = Path.Combine(_workDir, "salida"),
            Password = Password
        }, null, CancellationToken.None);

        var verifier = new SealedVerificationService(
            _sealing, receipts, signature, new VerificationService(hash, signature), hash);
        return (built.SealedPath, verifier);
    }

    private string CreatePfx(string pfxPassword)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=ForZip Test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_workDir, $"cert-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, pfxPassword));
        return path;
    }

    /// <summary>Recibos de verdad, pero la firma falla (como una TSA o un almacén que se cae a mitad).</summary>
    private sealed class FailingSignReceiptService(IReceiptService inner) : IReceiptService
    {
        public Task<Receipt> CreateAsync(string sealedPath, SealedHeader header, IProgress<double>? progress, CancellationToken ct) =>
            inner.CreateAsync(sealedPath, header, progress, ct);

        public Task<string> WriteAsync(Receipt receipt, string receiptPath, CancellationToken ct) =>
            inner.WriteAsync(receipt, receiptPath, ct);

        public Task<Receipt> ReadAsync(string receiptPath, CancellationToken ct) => inner.ReadAsync(receiptPath, ct);

        public Task SignAsync(string receiptPath, string pfxPath, string? pfxPassword, string? tsaUrl, CancellationToken ct) =>
            throw new InvalidOperationException("la firma falló a mitad");

        public string GetReceiptPathFor(string sealedPath) => inner.GetReceiptPathFor(sealedPath);

        public string GetSealedPathFor(string receiptPath) => inner.GetSealedPathFor(receiptPath);
    }

    /// <summary>Sella de verdad y después daña un byte del sobre en disco, como un fallo de escritura.</summary>
    private sealed class CorruptingSealingService(ISealingService inner) : ISealingService
    {
        public Task<SealedHeader> SealAsync(Stream payload, Stream destination, string password,
            IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct) =>
            inner.SealAsync(payload, destination, password, progress, ct);

        public Task<SealedHeader> UnsealAsync(Stream sealedInput, Stream destination, string password,
            IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct) =>
            inner.UnsealAsync(sealedInput, destination, password, progress, ct);

        public async Task<SealedHeader> SealFileAsync(string payloadPath, string sealedPath, string password,
            IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct)
        {
            var header = await inner.SealFileAsync(payloadPath, sealedPath, password, progress, ct);
            var bytes = File.ReadAllBytes(sealedPath);
            bytes[^100] ^= 0x01;
            File.WriteAllBytes(sealedPath, bytes);
            return header;
        }

        public Task<SealedHeader> UnsealFileAsync(string sealedPath, string payloadPath, string password,
            IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct) =>
            inner.UnsealFileAsync(sealedPath, payloadPath, password, progress, ct);

        public Task<SealedHeader> ReadHeaderAsync(Stream sealedInput, CancellationToken ct) => inner.ReadHeaderAsync(sealedInput, ct);

        public Task<SealedHeader> ReadHeaderAsync(string sealedPath, CancellationToken ct) => inner.ReadHeaderAsync(sealedPath, ct);
    }
}
