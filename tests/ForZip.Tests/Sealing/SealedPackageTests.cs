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

using System.Text.Json;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Tests del recibo público y de la verificación de dos niveles (Fase B de la
/// especificación 1.5.0). Trabajan sobre un paquete real —ZIP cifrado, informe, manifiesto
/// y sidecar— sellado de punta a punta, no sobre datos sintéticos.
/// </summary>
[Collection(ForZip.Tests.Sealing.SealingCollection.Name)]
public class SealedPackageTests : IDisposable
{
    private const string PackagePassword = "clave-generada-del-paquete-2026";
    private const int TestIterations = 1000;

    private readonly string _workDir;
    private readonly HashService _hashService = new();
    private readonly SignatureService _signatureService = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly VerificationService _verificationService;
    private readonly SealingService _sealingService;
    private readonly ReceiptService _receiptService;
    private readonly SealedVerificationService _sealedVerification;

    public SealedPackageTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_sealed_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);

        _zipService = new ZipService(_hashService);
        _reportService = new ReportService(new LocalizationService());
        _verificationService = new VerificationService(_hashService, _signatureService);
        // Chunk mínimo: el paquete de prueba pesa unos pocos KB y con el chunk de
        // producción entraría entero en el bloque 0, con lo que no habría forma de
        // ejercer la distinción entre contraseña incorrecta y sobre alterado.
        _sealingService = new SealingService(TestIterations, SealedContainerFormat.MinChunkSize);
        _receiptService = new ReceiptService(_hashService, _signatureService);
        _sealedVerification = new SealedVerificationService(
            _sealingService, _receiptService, _signatureService, _verificationService, _hashService);
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

        GC.SuppressFinalize(this);
    }

    // -------------------------------------------------------------------------
    //  9. El recibo no puede llevar un solo campo de más
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Receipt_DeclaraExactamenteLosCamposDeLaEspecificacion()
    {
        var package = await SealPackageAsync();

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(package.ReceiptPath));
        var campos = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        // Si este test falla porque alguien agregó un campo, la pregunta no es cómo
        // arreglarlo: es si ese campo revela algo del contenido. Ver §4.
        Assert.Equal(
            new[]
            {
                "receiptVersion",
                "packageId",
                "sealedFileName",
                "sealedSha256",
                "sealedSize",
                "sealedAtUtc",
                "forZipVersion",
                "formatVersion",
                "profile"
            },
            campos);
    }

    [Fact]
    public async Task Receipt_NoFiltraNadaDelContenido()
    {
        var package = await SealPackageAsync();
        var contenido = await File.ReadAllTextAsync(package.ReceiptPath);

        // Nada de la causa, del operador ni de los archivos que entraron al paquete.
        foreach (var prohibido in new[] { "CAUSA-4471/26", "Peritaje de prueba", "Juzgado", "Claudio", "a.txt", "b.txt", "evidencia" })
        {
            Assert.DoesNotContain(prohibido, contenido, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Receipt_NombreDeArchivoSoloDerivaDelIdentificador()
    {
        var package = await SealPackageAsync();
        var receipt = await _receiptService.ReadAsync(package.ReceiptPath, CancellationToken.None);

        Assert.Equal(SealedContainerFormat.BuildBaseName(receipt.PackageId) + ".fzs", receipt.SealedFileName);
        Assert.DoesNotContain("4471", receipt.SealedFileName, StringComparison.Ordinal);
        Assert.Equal(Receipt.ReservedProfile, receipt.Profile);
        Assert.Equal(SealedContainerFormat.FormatVersion, receipt.FormatVersion);
    }

    [Fact]
    public void Receipt_ConCampoAjeno_EsRechazado()
    {
        var json = """
            {
              "receiptVersion": 1,
              "packageId": "8f3a1c9e-4b21-4d7e-9a03-1c5e7f2b8d64",
              "sealedFileName": "FZS-8f3a1c9e.fzs",
              "sealedSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "sealedSize": 1024,
              "sealedAtUtc": "2026-09-04T17:22:11Z",
              "forZipVersion": "1.5.0",
              "formatVersion": 1,
              "profile": "Reservado",
              "caseNumber": "CAUSA-4471/26"
            }
            """;

        var error = Assert.Throws<SealedFormatException>(() => Receipt.Parse(json));
        Assert.Contains("caseNumber", error.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    //  10. Nivel 1 con recibo válido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_NivelPublicoSinContrasena_ReportaSobreIntegro()
    {
        var package = await SealPackageAsync();

        var result = await _sealedVerification.VerifyAsync(package.SealedPath, null, null, CancellationToken.None);

        Assert.Equal(SealedVerificationLevel.Public, result.Level);
        Assert.True(result.FormatValid);
        Assert.True(result.ReceiptPresent);
        Assert.True(result.SealedHashMatches);
        Assert.True(result.SealedSizeMatches);
        Assert.True(result.PackageIdMatches);
        Assert.True(result.EnvelopeIntact);
        Assert.True(result.IsIntact);

        // Nivel público: no se tocó el contenido.
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task VerifyAsync_DesdeElRecibo_ResuelveElSobre()
    {
        var package = await SealPackageAsync();

        var result = await _sealedVerification.VerifyAsync(package.ReceiptPath, null, null, CancellationToken.None);

        Assert.Equal(package.SealedPath, result.SealedPath);
        Assert.True(result.EnvelopeIntact);
    }

    [Fact]
    public async Task VerifyAsync_SinRecibo_QuedaDegradadoYLoAvisa()
    {
        var package = await SealPackageAsync();
        File.Delete(package.ReceiptPath);

        var result = await _sealedVerification.VerifyAsync(package.SealedPath, null, null, CancellationToken.None);

        Assert.True(result.FormatValid);
        Assert.False(result.ReceiptPresent);
        Assert.NotNull(result.ReceiptError);

        // Sin recibo no hay nada contra qué contrastar el origen.
        Assert.Null(result.SealedHashMatches);
        Assert.Null(result.PackageIdMatches);
    }

    // -------------------------------------------------------------------------
    //  11. Un byte alterado en el .fzs, detectado sin contraseña
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_ByteAlteradoEnElSobre_LoDetectaSinContrasena()
    {
        var package = await SealPackageAsync();

        var bytes = await File.ReadAllBytesAsync(package.SealedPath);
        bytes[^32] ^= 0xFF;
        await File.WriteAllBytesAsync(package.SealedPath, bytes);

        var result = await _sealedVerification.VerifyAsync(package.SealedPath, null, null, CancellationToken.None);

        Assert.False(result.SealedHashMatches);
        Assert.False(result.EnvelopeIntact);
        Assert.False(result.IsIntact);
    }

    [Fact]
    public async Task VerifyAsync_SobreTruncado_LoDetectaSinContrasena()
    {
        var package = await SealPackageAsync();

        var bytes = await File.ReadAllBytesAsync(package.SealedPath);
        await File.WriteAllBytesAsync(package.SealedPath, bytes[..^64]);

        var result = await _sealedVerification.VerifyAsync(package.SealedPath, null, null, CancellationToken.None);

        Assert.False(result.SealedSizeMatches);
        Assert.False(result.SealedHashMatches);
        Assert.False(result.EnvelopeIntact);
    }

    // -------------------------------------------------------------------------
    //  12. packageId divergente entre header y recibo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_PackageIdDivergente_ReportaAlterado()
    {
        var package = await SealPackageAsync();

        // Se reescribe el recibo con otro identificador, dejando intactos hash y tamaño:
        // lo único que delata la sustitución es el cruce contra el header del sobre.
        var original = await _receiptService.ReadAsync(package.ReceiptPath, CancellationToken.None);
        var suplantado = new Receipt(
            original.ReceiptVersion,
            Guid.NewGuid(),
            original.SealedFileName,
            original.SealedSha256,
            original.SealedSize,
            original.SealedAtUtc,
            original.ForZipVersion,
            original.FormatVersion,
            original.Profile);

        await _receiptService.WriteAsync(suplantado, package.ReceiptPath, CancellationToken.None);

        var result = await _sealedVerification.VerifyAsync(package.SealedPath, null, null, CancellationToken.None);

        Assert.True(result.SealedHashMatches);
        Assert.False(result.PackageIdMatches);
        Assert.False(result.EnvelopeIntact);
    }

    // -------------------------------------------------------------------------
    //  Nivel 2: el veredicto por archivo, igual que hoy pero bajo llave
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_NivelCompletoConContrasena_DaVeredictoPorArchivo()
    {
        var package = await SealPackageAsync();

        var result = await _sealedVerification.VerifyAsync(
            package.SealedPath, PackagePassword, null, CancellationToken.None);

        Assert.Equal(SealedVerificationLevel.Full, result.Level);
        Assert.True(result.EnvelopeIntact);
        Assert.NotNull(result.Content);
        Assert.Equal(2, result.Content!.OkCount);
        Assert.Equal(0, result.Content.AlteredCount);
        Assert.True(result.Content.IsIntact);
        Assert.True(result.IsIntact);
        Assert.Null(result.ContentError);
    }

    [Fact]
    public async Task VerifyAsync_ContrasenaIncorrecta_NoLlegaAlContenidoYNoDejaTemporales()
    {
        var package = await SealPackageAsync();
        var temporalesAntes = ContarTemporalesDeForZip();

        var result = await _sealedVerification.VerifyAsync(
            package.SealedPath, "otra-clave-cualquiera", null, CancellationToken.None);

        // El sobre sigue siendo el que se selló: lo que falla es la clave, no la integridad.
        Assert.True(result.EnvelopeIntact);
        Assert.Null(result.Content);
        Assert.NotNull(result.ContentError);
        Assert.Null(result.AlteredChunkIndex);
        Assert.Null(result.UndeletedTempPath);
        Assert.Equal(temporalesAntes, ContarTemporalesDeForZip());
    }

    [Fact]
    public async Task VerifyAsync_SobreAlteradoConContrasena_ReportaElBloque()
    {
        var package = await SealPackageAsync();

        // El bloque alterado tiene que ser posterior al 0: en el 0 la falla del tag es
        // indistinguible de una contraseña incorrecta, y así lo dice el mensaje.
        Assert.True(package.Header.TotalChunks > 1, "El paquete de prueba debería ocupar más de un bloque.");

        var bytes = await File.ReadAllBytesAsync(package.SealedPath);
        bytes[^32] ^= 0xFF;
        await File.WriteAllBytesAsync(package.SealedPath, bytes);

        var result = await _sealedVerification.VerifyAsync(
            package.SealedPath, PackagePassword, null, CancellationToken.None);

        Assert.False(result.EnvelopeIntact);
        Assert.Equal(package.Header.TotalChunks - 1, result.AlteredChunkIndex);
        Assert.NotNull(result.ContentError);
        Assert.Null(result.UndeletedTempPath);
    }

    [Fact]
    public async Task VerifyAsync_NivelCompleto_NoDejaTemporalesEnDisco()
    {
        var package = await SealPackageAsync();
        var temporalesAntes = ContarTemporalesDeForZip();

        var result = await _sealedVerification.VerifyAsync(
            package.SealedPath, PackagePassword, null, CancellationToken.None);

        Assert.True(result.IsIntact);
        Assert.Null(result.UndeletedTempPath);
        Assert.Equal(temporalesAntes, ContarTemporalesDeForZip());
    }

    // -------------------------------------------------------------------------
    //  Contenedor interno
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SealedPayloadContainer_GuardaLosArtefactosEnLaRaiz()
    {
        var package = await SealPackageAsync();
        var destino = Path.Combine(_workDir, "abierto");
        var payloadPath = Path.Combine(_workDir, "payload.zip");

        await _sealingService.UnsealFileAsync(
            package.SealedPath, payloadPath, PackagePassword, null, CancellationToken.None);

        await using var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read);
        var extraidos = await SealedPayloadContainer.ExtractAsync(payload, destino, CancellationToken.None);

        var nombres = extraidos.Select(Path.GetFileName).ToList();
        Assert.Contains("evidencia.zip", nombres);
        Assert.Contains("evidencia.zip.manifest.json", nombres);
        Assert.Contains("evidencia.report.txt", nombres);
        Assert.Contains("evidencia.report.txt.sha256", nombres);
    }

    // -------------------------------------------------------------------------
    //  Ayudantes
    // -------------------------------------------------------------------------

    private sealed record SealedPackage(string SealedPath, string ReceiptPath, SealedHeader Header);

    /// <summary>
    /// Arma un paquete forense real —ZIP cifrado, informe, manifiesto y sidecar—, lo mete en
    /// el contenedor interno, lo sella y emite el recibo. Es el flujo del perfil Reservado
    /// completo, tal como lo van a ejecutar el CLI y la GUI.
    /// </summary>
    private async Task<SealedPackage> SealPackageAsync()
    {
        var srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), "contenido A de la evidencia");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "b.txt"), "contenido B de la evidencia");

        var zipPath = Path.Combine(_workDir, "evidencia.zip");
        var options = new ZipOptions
        {
            SourcePaths = new List<string> { srcDir },
            OutputPath = zipPath,
            CompressionLevel = 5,
            Password = PackagePassword,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
        };

        var compression = await _zipService.CompressAsync(options, null, CancellationToken.None);
        var zipHash = await ComputeSha256Async(zipPath);

        var data = new ReportData
        {
            Operation = OperationType.Compression,
            CompressionLevel = 5,
            HasPassword = true,
            Algorithms = options.HashAlgorithms,
            ZipFilePath = zipPath,
            ZipFileSize = new FileInfo(zipPath).Length,
            ZipHash = zipHash,
            FileResults = compression.FileHashes,
            CaseNumber = "CAUSA-4471/26",
            CaseDescription = "Peritaje de prueba",
            Court = "Juzgado de prueba",
            Operator = new OperatorInfo { Name = "Claudio Andino", Organization = "Patagonia Robot" }
        };

        var manifestPath = zipPath + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));

        var reportPath = Path.Combine(_workDir, "evidencia.report.txt");
        await _reportService.SaveReportAsync(_reportService.GenerateReport(data, "es"), reportPath);

        var sidecarPath = reportPath + ".sha256";
        await File.WriteAllTextAsync(
            sidecarPath, $"{await ComputeSha256Async(reportPath)}  {Path.GetFileName(reportPath)}");

        // Contenedor interno con el paquete completo, y sellado del contenedor.
        var innerPath = Path.Combine(_workDir, "inner.zip");
        await using (var inner = new FileStream(innerPath, FileMode.Create, FileAccess.Write))
        {
            await SealedPayloadContainer.CreateAsync(
                new[] { zipPath, reportPath, manifestPath, sidecarPath }, inner, CancellationToken.None);
        }

        var provisional = Path.Combine(_workDir, "provisional.fzs");
        var header = await _sealingService.SealFileAsync(
            innerPath, provisional, PackagePassword, null, CancellationToken.None);

        // El nombre definitivo se deriva del identificador aleatorio, nunca de la causa.
        var sealedPath = Path.Combine(_workDir, header.BaseName + SealedContainerFormat.SealedExtension);
        File.Move(provisional, sealedPath);

        var receipt = await _receiptService.CreateAsync(sealedPath, header, null, CancellationToken.None);
        var receiptPath = await _receiptService.WriteAsync(
            receipt, _receiptService.GetReceiptPathFor(sealedPath), CancellationToken.None);

        // Los artefactos intermedios no deben quedar sueltos fuera del sobre (§2.2).
        foreach (var intermedio in new[] { zipPath, reportPath, manifestPath, sidecarPath, innerPath })
        {
            File.Delete(intermedio);
        }

        Directory.Delete(srcDir, recursive: true);

        return new SealedPackage(sealedPath, receiptPath, header);
    }

    private async Task<string> ComputeSha256Async(string path)
    {
        var hashes = await _hashService.ComputeHashesAsync(
            path, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None);
        return hashes.Hashes[HashAlgorithmType.SHA256];
    }

    private static int ContarTemporalesDeForZip() =>
        Directory.GetDirectories(Path.GetTempPath(), "ForZip_*").Length;
}
