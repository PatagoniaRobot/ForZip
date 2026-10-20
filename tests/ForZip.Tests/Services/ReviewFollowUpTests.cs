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
using System.Text.Json.Nodes;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using ForZip.Core.Shell;
using ICSharpCode.SharpZipLib.Zip;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Los defectos moderados y leves de la segunda pasada del 2026-10-10: verificación de entradas sin hash y de
/// entradas dañadas, fechas al extraer, imágenes de disco partidas, apertura de sobre cortada, bitácora redactada
/// y la raíz de una unidad desde el menú contextual.
/// </summary>
public class ReviewFollowUpTests : IDisposable
{
    private readonly string _workDir;
    private readonly HashService _hashService = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService = new(new LocalizationService());
    private readonly VerificationService _verificationService;

    public ReviewFollowUpTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_followup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _zipService = new ZipService(_hashService);
        _verificationService = new VerificationService(_hashService, new SignatureService());
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
    //  Verificación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Verify_EntryWithoutHashInManifest_IsUnverifiedNotOk()
    {
        var (_, manifestPath) = await BuildEvidenceAsync();
        var json = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var files = Property(json, "files").AsArray();
        var first = files[0]!.AsObject();
        first[PropertyName(first, "hashes")] = new JsonObject();
        File.WriteAllText(manifestPath, json.ToJsonString());

        var result = await _verificationService.VerifyArchiveAsync(manifestPath, null, null, null, CancellationToken.None);

        // Antes contaba como OK con solo estar: un archivo cuyo contenido nadie comparó salía "íntegro".
        Assert.Equal(1, result.UnverifiedCount);
        Assert.Equal(1, result.OkCount);
        Assert.False(result.IsIntact);
        Assert.False(result.TamperDetected);
    }

    [Fact]
    public async Task Verify_DamagedZipEntry_IsReportedAndTheRestIsStillVerified()
    {
        var (zipPath, manifestPath) = await BuildEvidenceAsync();
        var bytes = File.ReadAllBytes(zipPath);
        int nameLength = BitConverter.ToUInt16(bytes, 26);
        int extraLength = BitConverter.ToUInt16(bytes, 28);
        int dataStart = 30 + nameLength + extraLength;
        bytes[dataStart + 1] ^= 0xFF;
        bytes[dataStart + 2] ^= 0xFF;
        File.WriteAllBytes(zipPath, bytes);

        var result = await _verificationService.VerifyArchiveAsync(manifestPath, null, null, null, CancellationToken.None);

        // Antes, una entrada que no se podía descomprimir cortaba todo: ni el resto se verificaba.
        Assert.Null(result.ContentVerificationError);
        Assert.Equal(1, result.AlteredCount);
        Assert.Equal(1, result.OkCount);
        Assert.True(result.TamperDetected);
    }

    // -------------------------------------------------------------------------
    //  Extracción
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Extract7z_KeepsTheOriginalModifiedTime()
    {
        var src = Path.Combine(_workDir, "src7z");
        Directory.CreateDirectory(src);
        var file = Path.Combine(src, "a.txt");
        File.WriteAllText(file, "contenido");
        var original = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, original);

        var archive = Path.Combine(_workDir, "caso.7z");
        await _zipService.CompressAsync(new ZipOptions
        {
            SourcePaths = new List<string> { src },
            OutputPath = archive,
            CompressionLevel = 5,
            Format = ArchiveFormat.SevenZ
        }, null, CancellationToken.None);

        var outDir = Path.Combine(_workDir, "out7z");
        await _zipService.DecompressAsync(archive, outDir, null, null, CancellationToken.None);

        Assert.Equal(original, File.GetLastWriteTimeUtc(Path.Combine(outDir, "src7z", "a.txt")));
    }

    [Fact]
    public async Task ExtractZip_WithNtfsTimestamp_KeepsIt_AndWithoutOneDoesNotGuess()
    {
        var original = new DateTime(2019, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var zipPath = Path.Combine(_workDir, "tercero.zip");
        using (var zip = new ZipOutputStream(File.Create(zipPath)))
        {
            var extra = new ZipExtraData();
            extra.AddEntry(new NTTaggedData { LastModificationTime = original });
            zip.PutNextEntry(new ZipEntry("con_fecha.txt") { ExtraData = extra.GetEntryData(), DateTime = original });
            zip.Write("a"u8);
            zip.CloseEntry();

            zip.PutNextEntry(new ZipEntry("sin_fecha.txt") { DateTime = original });
            zip.Write("b"u8);
            zip.CloseEntry();
        }

        var outDir = Path.Combine(_workDir, "outzip");
        var before = DateTime.UtcNow.AddMinutes(-1);
        await _zipService.DecompressAsync(zipPath, outDir, null, null, CancellationToken.None);

        Assert.Equal(original, File.GetLastWriteTimeUtc(Path.Combine(outDir, "con_fecha.txt")));
        // La fecha DOS sola no dice su zona horaria: no se aplica.
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(outDir, "sin_fecha.txt")) > before);
    }

    [Fact]
    public async Task Extract_SplitDiskImage_SaysItIsNotAPackage()
    {
        var data = new byte[70_000];
        new Random(5).NextBytes(data);
        data[0] = 0xEB; // como el arranque de un disco, no "PK"
        File.WriteAllBytes(Path.Combine(_workDir, "disco.img.001"), data[..35_000]);
        File.WriteAllBytes(Path.Combine(_workDir, "disco.img.002"), data[35_000..]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => _zipService.DecompressAsync(
            Path.Combine(_workDir, "disco.img.001"), Path.Combine(_workDir, "outimg"), null, null, CancellationToken.None));

        Assert.Contains("imagen", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenEnvelope_ContainerFailingHalfway_LeavesNothingInTheDestination()
    {
        var container = new MemoryStream();
        using (var zip = new ZipArchive(container, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("bueno.txt").Open()))
            {
                w.Write("se escribe primero");
            }

            using (var w = new StreamWriter(zip.CreateEntry("sub/malo.txt").Open()))
            {
                w.Write("entrada con ruta: el contenedor la rechaza");
            }
        }

        container.Position = 0;
        var destination = Path.Combine(_workDir, "abierto");

        await Assert.ThrowsAsync<SealedFormatException>(() =>
            SealedPayloadContainer.ExtractAsync(container, destination, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(destination));
    }

    // -------------------------------------------------------------------------
    //  Bitácora y menú contextual
    // -------------------------------------------------------------------------

    [Fact]
    public void LogEvidence_InRedactedMode_DropsThePath()
    {
        var log = new LogService { Redacted = true };
        log.LogEvidence(LogLevel.Info, "- Destino: {0}", @"C:\Causas\4471-26\extraido");
        // El dato no aparece, y la línea dice por qué en vez de quedar cortada.
        Assert.Equal("- Destino: " + log.RedactedMarker, log.Entries[^1].Message);

        // Plantilla sin "{0}" (el dato va aparte): la marca va al final, no queda "...en:" cortado.
        log.LogEvidence(LogLevel.Warning, "Extracción incompleta en:", @"C:\Causas\4471-26\extraido");
        Assert.Equal("Extracción incompleta en: " + log.RedactedMarker, log.Entries[^1].Message);

        log.Redacted = false;
        log.LogEvidence(LogLevel.Info, "- Destino: {0}", @"C:\Causas\4471-26\extraido");
        Assert.Equal(@"- Destino: C:\Causas\4471-26\extraido", log.Entries[^1].Message);
    }

    [Fact]
    public void ShellArgs_DriveRootFromFolderBackground_IsRepaired()
    {
        // El Explorador arma "D:\" y la barra antes de la comilla la escapa: llega D:"
        var request = ShellArgs.Parse(new[] { "D:\"" });

        Assert.NotNull(request);
        Assert.Equal(@"D:\", request!.Paths[0]);
    }

    // -------------------------------------------------------------------------

    private async Task<(string zipPath, string manifestPath)> BuildEvidenceAsync()
    {
        var srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), string.Concat(Enumerable.Repeat("contenido A ", 200)));
        await File.WriteAllTextAsync(Path.Combine(srcDir, "b.txt"), string.Concat(Enumerable.Repeat("contenido B ", 200)));

        var zipPath = Path.Combine(_workDir, "evidence.zip");
        var options = new ZipOptions
        {
            SourcePaths = new List<string> { srcDir },
            OutputPath = zipPath,
            CompressionLevel = 5,
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
            FileResults = result.FileHashes
        };

        var manifestPath = zipPath + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));
        return (zipPath, manifestPath);
    }

    private static JsonNode Property(JsonObject obj, string name) => obj[PropertyName(obj, name)]!;

    private static string PropertyName(JsonObject obj, string name) =>
        obj.First(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Key;
}
