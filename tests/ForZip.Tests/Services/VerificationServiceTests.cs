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
    public async Task VerifyArchive_DeclaredOmittedFile_IsNotReportedMissing()
    {
        // Un archivo que el antivirus no dejó leer está declarado en el manifiesto como no
        // incluido: su ausencia en el ZIP es lo esperado, no una alteración.
        var omitted = new List<OmittedFile>
        {
            new()
            {
                EntryName = "src/adjunto.txt",
                SourcePath = @"C:\caso\src\adjunto.txt",
                Size = 68,
                ModifiedUtc = DateTimeOffset.UtcNow,
                WindowsErrorCode = SecuritySoftwareInterference.VirusInfectedError,
                SystemMessage = "Contiene un virus.",
                RecordedAtUtc = DateTimeOffset.UtcNow
            }
        };
        var (zipPath, manifestPath) = await BuildEvidenceAsync(omitted);

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, null, null, CancellationToken.None);

        Assert.True(result.IsIntact);
        Assert.Equal(2, result.OkCount);
        Assert.DoesNotContain(result.Entries, e => e.Status == FileVerificationStatus.Missing);
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

        // Re-empaquetar con el contenido de un archivo modificado, reutilizando el manifiesto original. ForZip no
        // sobrescribe un paquete: quien lo sustituye borra primero el original.
        await File.WriteAllTextAsync(Path.Combine(_workDir, "src", "a.txt"), "CONTENIDO ALTERADO");
        File.Delete(zipPath);
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

    [Fact]
    public async Task VerifyArchive_SplitUntouched_IsIntact()
    {
        var (logicalZip, manifestPath) = await BuildSplitEvidenceAsync();

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, logicalZip, null, null, CancellationToken.None);

        Assert.True(result.IsIntact);
        Assert.True(result.ZipHashMatches);
        Assert.NotNull(result.Volumes);
        Assert.True(result.Volumes!.Count >= 2);
        Assert.All(result.Volumes!, v => Assert.Equal(FileVerificationStatus.Ok, v.Status));
    }

    [Fact]
    public async Task VerifyArchive_SplitTamperedVolume_DetectsAlteration()
    {
        var (logicalZip, manifestPath) = await BuildSplitEvidenceAsync();

        // Alterar un byte de datos en el primer volumen (nivel 0 = Store, no rompe el parseo).
        var firstVolume = logicalZip + ".001";
        var raw = await File.ReadAllBytesAsync(firstVolume);
        raw[40_000] ^= 0xFF;
        await File.WriteAllBytesAsync(firstVolume, raw);

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, logicalZip, null, null, CancellationToken.None);

        Assert.False(result.IsIntact);
        Assert.NotNull(result.Volumes);
        Assert.Contains(result.Volumes!, v => v.Status == FileVerificationStatus.Altered);
    }

    [Theory]
    [InlineData(".001")] // antes daba "Archivo ZIP no encontrado" sin nombrar nada
    [InlineData(".002")]
    public async Task VerifyArchive_SplitMissingVolume_NamesItAndChecksTheRest(string missingSuffix)
    {
        var (logicalZip, manifestPath) = await BuildSplitEvidenceAsync();
        File.Delete(logicalZip + missingSuffix);

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, null, null, null, CancellationToken.None);

        Assert.Equal(new[] { Path.GetFileName(logicalZip + missingSuffix) }, result.MissingVolumes);
        Assert.Contains(Path.GetFileName(logicalZip + missingSuffix), result.ContentVerificationError, StringComparison.Ordinal);
        // Los presentes se verificaron contra su hash, uno por uno.
        Assert.All(result.Volumes!.Where(v => v.Status != FileVerificationStatus.Missing),
            v => Assert.Equal(FileVerificationStatus.Ok, v.Status));
        Assert.False(result.IsIntact);
        Assert.True(result.TamperDetected);
    }

    [Fact]
    public async Task VerifyArchive_SplitEncryptedNoPassword_ReportsContentErrorButVolumesOk()
    {
        var (logicalZip, manifestPath) = await BuildSplitEvidenceAsync(password: "clave-secreta");

        // Verificamos SIN contraseña: el contenido no puede descifrarse, pero los volúmenes
        // (que solo se hashean) deben verificarse igual y no perderse.
        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, logicalZip, null, null, CancellationToken.None);

        Assert.NotNull(result.ContentVerificationError);
        Assert.False(result.IsIntact);
        Assert.NotNull(result.Volumes);
        Assert.All(result.Volumes!, v => Assert.Equal(FileVerificationStatus.Ok, v.Status));
    }

    // Un paquete dañado o alterado no puede salir "sin alteraciones" solo porque no se lo pudo abrir: el hash
    // global se compara aparte del contenido. Antes los tres casos de abajo daban VERIFICACIÓN INCOMPLETA (ámbar).

    [Fact]
    public async Task VerifyArchive_TruncatedZip_IsTamperDetected()
    {
        var (zipPath, manifestPath) = await BuildEvidenceAsync();
        await using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Write))
        {
            fs.SetLength(fs.Length / 2); // sin directorio central: el ZIP ya no se puede abrir
        }

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, null, null, CancellationToken.None);

        Assert.NotNull(result.ContentVerificationError);
        Assert.False(result.ZipHashMatches);
        Assert.True(result.TamperDetected);
    }

    [Fact]
    public async Task VerifyArchive_EncryptedZipFirstEntryAltered_IsTamperDetected()
    {
        var (zipPath, manifestPath) = await BuildEvidenceAsync(password: "clave-secreta");

        // Un byte del primer archivo cifrado: la verificación lo toma como contraseña equivocada y aborta el
        // contenido, así que solo el hash global puede delatarlo.
        long dataOffset;
        using (var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipPath))
        {
            var first = zip[0];
            var header = new byte[30];
            await using var raw = File.OpenRead(zipPath);
            raw.Position = first.Offset;
            raw.ReadExactly(header);
            dataOffset = first.Offset + 30 + BitConverter.ToUInt16(header, 26) + BitConverter.ToUInt16(header, 28);
            dataOffset += first.CompressedSize - 11; // último byte cifrado, antes del código de autenticación
        }
        FlipByte(zipPath, dataOffset);

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, "clave-secreta", null, CancellationToken.None);

        Assert.NotNull(result.ContentVerificationError);
        Assert.False(result.ZipHashMatches);
        Assert.True(result.TamperDetected);
    }

    [Fact]
    public async Task VerifyArchive_Encrypted7zAltered_IsTamperDetected()
    {
        var (zipPath, manifestPath) = await BuildEvidenceAsync(password: "clave-secreta", format: ArchiveFormat.SevenZ);
        FlipByte(zipPath, 40); // primeros datos empaquetados, justo después de la cabecera de 32 bytes

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, "clave-secreta", null, CancellationToken.None);

        Assert.False(result.IsIntact);
        Assert.False(result.ZipHashMatches);
        Assert.True(result.TamperDetected);
    }

    [Fact]
    public async Task VerifyArchive_EncryptedZipWrongPassword_IsInconclusiveWithMatchingHash()
    {
        // El paquete intacto con otra contraseña sigue siendo ámbar, no rojo: el hash global dice que no se tocó.
        var (zipPath, manifestPath) = await BuildEvidenceAsync(password: "clave-secreta");

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, "otra-clave", null, CancellationToken.None);

        Assert.NotNull(result.ContentVerificationError);
        Assert.True(result.ZipHashMatches);
        Assert.False(result.TamperDetected);
        Assert.False(result.IsIntact);
    }

    private static void FlipByte(string path, long offset)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        fs.Position = offset;
        var b = fs.ReadByte();
        fs.Position = offset;
        fs.WriteByte((byte)(b ^ 0x01));
    }

    private async Task<(string logicalZip, string manifestPath)> BuildSplitEvidenceAsync(string? password = null)
    {
        var srcDir = Path.Combine(_workDir, "bigsrc");
        Directory.CreateDirectory(srcDir);
        var payload = new byte[300_000];
        new Random(99).NextBytes(payload);
        await File.WriteAllBytesAsync(Path.Combine(srcDir, "big.bin"), payload);

        var logicalZip = Path.Combine(_workDir, "split_evidence.zip");
        var options = new ZipOptions
        {
            SourcePaths = new List<string> { srcDir },
            OutputPath = logicalZip,
            CompressionLevel = 0, // Store: alterar datos no rompe el parseo del ZIP
            Password = password,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
            SplitSize = 128 * 1024
        };

        var result = await _zipService.CompressAsync(options, null, CancellationToken.None);
        Assert.True(result.IsSplit);

        // Hash del ZIP lógico = concatenación de los volúmenes
        var segments = result.Volumes.Select(v => Path.Combine(_workDir, v.FileName)).ToList();
        string zipHash;
        await using (var logical = new ConcatenatedReadStream(segments))
        {
            zipHash = (await _hashService.ComputeHashesAsync(
                logical, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, CancellationToken.None))
                [HashAlgorithmType.SHA256];
        }

        var data = new ReportData
        {
            Operation = OperationType.Compression,
            CompressionLevel = 0,
            Algorithms = options.HashAlgorithms,
            ZipFilePath = logicalZip,
            ZipFileSize = result.Volumes.Sum(v => v.Size),
            ZipHash = zipHash,
            Volumes = result.Volumes.ToList(),
            FileResults = result.FileHashes
        };

        var manifestPath = logicalZip + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));

        return (logicalZip, manifestPath);
    }

    private async Task<(string zipPath, string manifestPath)> BuildEvidenceAsync(
        List<OmittedFile>? omitted = null, string? password = null, ArchiveFormat format = ArchiveFormat.Zip)
    {
        var srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), "contenido A");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "b.txt"), "contenido B");

        var zipPath = Path.Combine(_workDir, format == ArchiveFormat.SevenZ ? "evidence.7z" : "evidence.zip");
        var options = new ZipOptions
        {
            SourcePaths = new List<string> { srcDir },
            OutputPath = zipPath,
            CompressionLevel = 5,
            Password = password,
            Format = format,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
        };

        var result = await _zipService.CompressAsync(options, null, CancellationToken.None);

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
            FileResults = result.FileHashes,
            OmittedFiles = omitted ?? new List<OmittedFile>()
        };

        var manifestPath = zipPath + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));

        return (zipPath, manifestPath);
    }
}
