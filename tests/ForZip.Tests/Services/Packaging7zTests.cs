using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Paquetes 7z: las mismas garantías que el ZIP (estructura fiel, hashes en un solo pase, omitidos,
/// volúmenes, nada a medias) más la verificación por archivo y la apertura reconociendo la firma.
/// </summary>
public class Packaging7zTests : IDisposable
{
    private readonly string _workDir;
    private readonly HashService _hashService = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly VerificationService _verificationService;

    public Packaging7zTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_7z_{Guid.NewGuid():N}");
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

    /// <summary>Árbol de evidencia: subcarpetas, una vacía, un archivo vacío, Unicode y un binario grande.</summary>
    private string BuildSourceTree()
    {
        var root = Path.Combine(_workDir, "Caso_42");
        Directory.CreateDirectory(Path.Combine(root, "docs", "sub"));
        Directory.CreateDirectory(Path.Combine(root, "vacia", "tambien_vacia"));
        for (int i = 0; i < 25; i++)
        {
            File.WriteAllText(Path.Combine(root, "docs", $"nota_{i:D2}.txt"),
                string.Concat(Enumerable.Range(0, 40 + i * 9).Select(k => $"renglón {k} del acta {i};\n")), Encoding.UTF8);
        }

        File.WriteAllText(Path.Combine(root, "docs", "sub", "declaración_ñandú.txt"), "año 2026, 漢字", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(root, "cero.bin"), []);
        var binary = new byte[400_000];
        new Random(7).NextBytes(binary.AsSpan(0, 200_000));
        File.WriteAllBytes(Path.Combine(root, "imagen.bin"), binary);
        return root;
    }

    private static ZipOptions Options(string source, string output, string? password = null, long? split = null, int level = 5) => new()
    {
        SourcePaths = new List<string> { source },
        OutputPath = output,
        CompressionLevel = level,
        Password = password,
        HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256, HashAlgorithmType.MD5 },
        SplitSize = split,
        Format = ArchiveFormat.SevenZ
    };

    private static void AssertSameTree(string expectedRoot, string actualRoot)
    {
        var expectedFiles = Directory.EnumerateFiles(expectedRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(expectedRoot, f)).Order(StringComparer.Ordinal).ToList();
        var actualFiles = Directory.EnumerateFiles(actualRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(actualRoot, f)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expectedFiles, actualFiles);
        foreach (var rel in expectedFiles)
        {
            Assert.True(File.ReadAllBytes(Path.Combine(expectedRoot, rel)).AsSpan()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(actualRoot, rel))), rel);
        }

        var expectedDirs = Directory.EnumerateDirectories(expectedRoot, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(expectedRoot, d)).Order(StringComparer.Ordinal).ToList();
        var actualDirs = Directory.EnumerateDirectories(actualRoot, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(actualRoot, d)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expectedDirs, actualDirs);
    }

    private async Task<string> WriteManifestAsync(string archivePath, ZipOptions options, CompressionResult result)
    {
        var segments = result.IsSplit
            ? result.Volumes.Select(v => Path.Combine(Path.GetDirectoryName(archivePath)!, v.FileName)).ToList()
            : new List<string> { archivePath };
        string archiveHash;
        await using (Stream logical = segments.Count == 1 ? File.OpenRead(segments[0]) : new ConcatenatedReadStream(segments))
        {
            archiveHash = (await _hashService.ComputeHashesAsync(
                logical, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, CancellationToken.None))[HashAlgorithmType.SHA256];
        }

        var data = new ReportData
        {
            Operation = OperationType.Compression,
            CompressionLevel = options.CompressionLevel,
            HasPassword = options.Password != null,
            Algorithms = options.HashAlgorithms,
            ZipFilePath = archivePath,
            ArchiveFormat = ArchiveFormat.SevenZ,
            ZipFileSize = segments.Sum(s => new FileInfo(s).Length),
            ZipHash = archiveHash,
            Volumes = result.Volumes.ToList(),
            FileResults = result.FileHashes,
            OmittedFiles = result.OmittedFiles
        };
        var manifestPath = archivePath + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));
        return manifestPath;
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(9)]
    public async Task CompressAndDecompress_FullTree_IdenticalWithHashesFromSinglePass(int level)
    {
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "caso.7z");

        var result = await _zipService.CompressAsync(Options(source, archive, level: level), null, CancellationToken.None);

        Assert.Equal(ArchiveFormat.SevenZ, ArchiveFormats.Detect(archive));
        Assert.Equal(28, result.FileHashes.Count);
        foreach (var hash in result.FileHashes)
        {
            var bytes = File.ReadAllBytes(hash.SourcePath!);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hash.Hashes[HashAlgorithmType.SHA256]);
            Assert.Equal(Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), hash.Hashes[HashAlgorithmType.MD5]);
        }

        var extracted = Path.Combine(_workDir, "extraido");
        await _zipService.DecompressAsync(archive, extracted, null, null, CancellationToken.None);
        AssertSameTree(source, Path.Combine(extracted, "Caso_42"));
    }

    [Theory]
    [InlineData(ArchiveFormat.SevenZ)]
    [InlineData(ArchiveFormat.Zip)]
    public async Task Decompress_ReportsEachFileName_ForTheProgressLine(ArchiveFormat format)
    {
        // Debajo de la barra va el archivo que se está extrayendo.
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, format == ArchiveFormat.Zip ? "caso.zip" : "caso.7z");
        var options = Options(source, archive);
        options.Format = format;
        await _zipService.CompressAsync(options, null, CancellationToken.None);

        var names = new NameCollector();
        var result = await _zipService.DecompressAsync(archive, Path.Combine(_workDir, "extraido"), null, null,
            CancellationToken.None, names);

        Assert.Equal(result.FilesExtracted, names.Names.Count);
        Assert.All(names.Names, n => Assert.StartsWith("Caso_42/", n.Replace('\\', '/')));
        Assert.Equal(names.Names.Count, names.Names.Distinct().Count());
    }

    /// <summary>Junta los nombres en el momento: Progress&lt;T&gt; los entregaría después, en otro hilo.</summary>
    private sealed class NameCollector : IProgress<string>
    {
        public List<string> Names { get; } = new();
        public void Report(string value) => Names.Add(value);
    }

    [Fact]
    public async Task Password_EncryptsContentAndNames_AndOpensOnlyWithIt()
    {
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "cifrado.7z");
        await _zipService.CompressAsync(Options(source, archive, password: "Clave-Correcta-123"), null, CancellationToken.None);

        // Ni el contenido ni los nombres aparecen en claro (7z guarda los nombres en UTF-16LE).
        var raw = File.ReadAllBytes(archive);
        Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.Unicode.GetBytes("declaración_ñandú")));
        Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes("renglón 3 del acta")));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _zipService.DecompressAsync(archive, Path.Combine(_workDir, "sin_clave"), null, null, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _zipService.DecompressAsync(archive, Path.Combine(_workDir, "mala"), "Otra", null, CancellationToken.None));

        var extracted = Path.Combine(_workDir, "con_clave");
        await _zipService.DecompressAsync(archive, extracted, "Clave-Correcta-123", null, CancellationToken.None);
        AssertSameTree(source, Path.Combine(extracted, "Caso_42"));
    }

    [Fact]
    public async Task Split_VolumeHashesMatchTheFiles_AndOpensFromTheLogicalPath()
    {
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "dividido.7z");
        var result = await _zipService.CompressAsync(Options(source, archive, split: 64 * 1024, level: 1), null, CancellationToken.None);

        Assert.True(result.Volumes.Count > 2);
        foreach (var volume in result.Volumes)
        {
            var bytes = File.ReadAllBytes(Path.Combine(_workDir, volume.FileName));
            Assert.Equal(volume.Size, bytes.Length);
            // El primero se reescribe al cerrar (firma del 7z): su hash tiene que ser el del archivo final.
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), volume.Sha256);
        }

        Assert.False(File.Exists(archive));
        var extracted = Path.Combine(_workDir, "de_volumenes");
        await _zipService.DecompressAsync(archive, extracted, null, null, CancellationToken.None);
        AssertSameTree(source, Path.Combine(extracted, "Caso_42"));

        var manifest = await WriteManifestAsync(archive, Options(source, archive, split: 64 * 1024, level: 1), result);
        var verification = await _verificationService.VerifyArchiveAsync(manifest, null, null, null, CancellationToken.None);
        Assert.Null(verification.ContentVerificationError);
        Assert.All(verification.Volumes!, v => Assert.Equal(FileVerificationStatus.Ok, v.Status));
        Assert.All(verification.Entries, e => Assert.Equal(FileVerificationStatus.Ok, e.Status));
        Assert.True(verification.ZipHashMatches);
    }

    [Fact]
    public async Task Verify_Intact_AllOk_AndManifestDeclares7z()
    {
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "verificar.7z");
        var options = Options(source, archive, password: "Clave-123");
        var result = await _zipService.CompressAsync(options, null, CancellationToken.None);
        var manifest = await WriteManifestAsync(archive, options, result);

        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifest)))
        {
            Assert.Equal("1.4", doc.RootElement.GetProperty("FormatVersion").GetString());
            Assert.Equal("7z", doc.RootElement.GetProperty("ArchiveFormat").GetString());
        }

        var verification = await _verificationService.VerifyArchiveAsync(manifest, null, "Clave-123", null, CancellationToken.None);
        Assert.Null(verification.ContentVerificationError);
        Assert.Equal(28, verification.Entries.Count);
        Assert.All(verification.Entries, e => Assert.Equal(FileVerificationStatus.Ok, e.Status));
        Assert.True(verification.ZipHashMatches);

        var wrong = await _verificationService.VerifyArchiveAsync(manifest, null, "Incorrecta", null, CancellationToken.None);
        Assert.NotNull(wrong.ContentVerificationError);
        Assert.DoesNotContain(wrong.Entries, e => e.Status == FileVerificationStatus.Ok);
    }

    [Fact]
    public async Task Verify_OneFileTampered_OnlyThatFileIsAltered()
    {
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "alterado.7z");
        // Sin compresión: el contenido de cada archivo está tal cual en el paquete y se lo puede alterar.
        var options = Options(source, archive, level: 0);
        var result = await _zipService.CompressAsync(options, null, CancellationToken.None);
        var manifest = await WriteManifestAsync(archive, options, result);

        var raw = File.ReadAllBytes(archive);
        int at = raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes("renglón 30 del acta 7;"));
        Assert.True(at > 0);
        raw[at + 2] ^= 0x20;
        File.WriteAllBytes(archive, raw);

        var verification = await _verificationService.VerifyArchiveAsync(manifest, null, null, null, CancellationToken.None);
        Assert.Null(verification.ContentVerificationError);
        var altered = Assert.Single(verification.Entries, e => e.Status != FileVerificationStatus.Ok);
        Assert.Equal("Caso_42/docs/nota_07.txt", altered.EntryName);
        Assert.Equal(FileVerificationStatus.Altered, altered.Status);
        Assert.Equal(27, verification.Entries.Count(e => e.Status == FileVerificationStatus.Ok));
        Assert.False(verification.ZipHashMatches);
    }

    [Fact]
    public async Task Decompress_RecognizesTheSignature_NotTheExtension()
    {
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "engañoso.zip");
        await _zipService.CompressAsync(Options(source, archive), null, CancellationToken.None);

        var extracted = Path.Combine(_workDir, "por_firma");
        await _zipService.DecompressAsync(archive, extracted, null, null, CancellationToken.None);
        AssertSameTree(source, Path.Combine(extracted, "Caso_42"));
    }

    [Fact]
    public async Task TwoSourcesWithTheSameName_AndTheSameEmptyFolder_StillPackage()
    {
        // Dos carpetas "docs" elegidas de lugares distintos, cada una con su "sub" vacía: la carpeta vacía
        // salía dos veces con el mismo nombre, y el motor ahora rechaza los nombres repetidos.
        var sources = new List<string>();
        foreach (var origin in new[] { "A", "B" })
        {
            var docs = Path.Combine(_workDir, origin, "docs");
            Directory.CreateDirectory(Path.Combine(docs, "sub"));
            File.WriteAllText(Path.Combine(docs, "nota.txt"), "de " + origin);
            sources.Add(docs);
        }

        var archive = Path.Combine(_workDir, "dos_docs.7z");
        var options = Options(sources[0], archive);
        options.SourcePaths = sources;
        await _zipService.CompressAsync(options, null, CancellationToken.None);

        var extracted = Path.Combine(_workDir, "extraido");
        await _zipService.DecompressAsync(archive, extracted, null, null, CancellationToken.None);
        Assert.True(Directory.Exists(Path.Combine(extracted, "docs", "sub")));
        Assert.Equal("de A", File.ReadAllText(Path.Combine(extracted, "docs", "nota.txt")));
        Assert.Equal("de B", File.ReadAllText(Path.Combine(extracted, "docs", "nota (2).txt")));
    }

    [Fact]
    public async Task Cancellation_LeavesNoOutput()
    {
        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "cancelado.7z");
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress(p =>
        {
            if (p.bytesProcessed > 100_000) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _zipService.CompressAsync(Options(source, archive, split: 64 * 1024), progress, cts.Token));
        Assert.Empty(Directory.EnumerateFiles(_workDir, "cancelado.7z*"));
    }

    [Fact]
    public async Task MissingSinceSelection_IsDocumentedAsOmitted()
    {
        var source = BuildSourceTree();
        var gone = Path.Combine(source, "docs", "nota_03.txt");
        var snapshot = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(f => new SelectedSourceFile(f, new FileInfo(f).Length, File.GetLastWriteTimeUtc(f), DateTimeOffset.UtcNow))
            .ToList();
        File.Delete(gone);

        var options = Options(source, Path.Combine(_workDir, "incompleto.7z"));
        options.SelectionSnapshot = snapshot;
        var result = await _zipService.CompressAsync(options, null, CancellationToken.None);

        var omitted = Assert.Single(result.OmittedFiles);
        Assert.Equal(OmissionReason.MissingAtPackaging, omitted.Reason);
        Assert.Equal("Caso_42/docs/nota_03.txt", omitted.EntryName);
        Assert.Equal(27, result.FileHashes.Count);
    }

    [Requires7zFact]
    public async Task Official7Zip_TestsAndExtractsWhatForZipWrote()
    {
        var exe7z = Requires7zFactAttribute.Find7z()!;

        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "para_7zip.7z");
        await _zipService.CompressAsync(Options(source, archive, password: "Clave-123", split: 96 * 1024), null, CancellationToken.None);

        var (code, output) = Run(exe7z, $"t \"{archive}.001\" -pClave-123");
        Assert.True(code == 0, output);
        Assert.Contains("Everything is Ok", output, StringComparison.Ordinal);

        var extracted = Path.Combine(_workDir, "por_7zip");
        (code, output) = Run(exe7z, $"x \"{archive}.001\" -o\"{extracted}\" -pClave-123 -y");
        Assert.True(code == 0, output);
        AssertSameTree(source, Path.Combine(extracted, "Caso_42"));
    }

    [Requires7zFact]
    public async Task ForZip_OpensWhatOfficial7ZipWrote()
    {
        var exe7z = Requires7zFactAttribute.Find7z()!;

        var source = BuildSourceTree();
        var archive = Path.Combine(_workDir, "de_7zip.7z");
        var (code, output) = Run(exe7z, $"a \"{archive}\" \"{source}\" -mx5 -pClave-123 -mhe=on -v100k");
        Assert.True(code == 0, output);

        var extracted = Path.Combine(_workDir, "de_7zip_extraido");
        await _zipService.DecompressAsync(archive + ".001", extracted, "Clave-123", null, CancellationToken.None);
        AssertSameTree(source, Path.Combine(extracted, "Caso_42"));
    }

    private static (int Code, string Output) Run(string exe, string arguments)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(120_000);
        return (proc.ExitCode, output);
    }

    /// <summary>IProgress que reporta en el acto, en el hilo que lo llama (Progress&lt;T&gt; difiere el aviso).</summary>
    private sealed class SynchronousProgress(Action<(long bytesProcessed, long totalBytes)> onReport)
        : IProgress<(long bytesProcessed, long totalBytes)>
    {
        public void Report((long bytesProcessed, long totalBytes) value) => onReport(value);
    }
}
