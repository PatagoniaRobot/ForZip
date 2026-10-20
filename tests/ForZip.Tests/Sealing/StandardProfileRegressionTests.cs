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
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// No regresión del perfil Estándar: la regla de oro de 1.5.0 es que el comportamiento actual
/// no cambia, y todo lo del sobre sellado es aditivo.
/// </summary>
/// <remarks>
/// El paquete de <c>TestData/Reference140</c> lo generó <b>ForZip 1.4.0 de verdad</b> (compilado
/// desde el tag <c>v1.4.0</c>), no esta versión: un artefacto que 1.5.0 se fabricara a sí misma
/// no probaría nada. Lo único retocado a mano son las rutas de origen, el equipo y el usuario del
/// informe (datos de la máquina donde se generó), con su <c>.sha256</c> recalculado.
/// <para>
/// El criterio acordado fue comparar el <b>contenido forense</b> y no los bytes crudos, porque
/// el ZIP guarda la fecha de modificación de cada entrada y el informe lleva su fecha de
/// generación. En la práctica resultó ser más fuerte que eso: repuesta la mtime de las
/// entradas, el ZIP que produce 1.5.0 es <b>byte a byte idéntico</b> al de 1.4.0, y el test lo
/// exige comparando también <c>ZipSha256</c>. Lo único que se deja afuera es lo que por
/// construcción depende del momento y de la máquina: <c>GeneratedAtUtc</c>, <c>SourcePath</c>
/// y la versión declarada.
/// </para>
/// </remarks>
[Collection(SealingCollection.Name)]
public class StandardProfileRegressionTests : IDisposable
{
    /// <summary>
    /// Fecha de modificación con la que se generó la referencia. Git no preserva la mtime, así
    /// que hay que reponerla antes de regenerar o la comparación fallaría por el reloj.
    /// </summary>
    private static readonly DateTime FixedModifiedUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static string ReferenceDir =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Reference140");

    private readonly string _workDir;
    private readonly HashService _hashService = new();
    private readonly SignatureService _signatureService = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly VerificationService _verificationService;

    public StandardProfileRegressionTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_reg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);

        _zipService = new ZipService(_hashService);
        _reportService = new ReportService(new LocalizationService());
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

        GC.SuppressFinalize(this);
    }

    // -------------------------------------------------------------------------
    //  14. Un paquete de 1.4.0 verifica correctamente en 1.5.0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PaqueteDe140_VerificaEn150()
    {
        var manifestPath = Path.Combine(ReferenceDir, "referencia.zip.manifest.json");
        var zipPath = Path.Combine(ReferenceDir, "referencia.zip");

        Assert.True(File.Exists(manifestPath), "Falta el paquete de referencia de 1.4.0.");

        var result = await _verificationService.VerifyArchiveAsync(
            manifestPath, zipPath, null, null, CancellationToken.None);

        Assert.True(result.IsIntact);
        Assert.Equal(2, result.OkCount);
        Assert.Equal(0, result.AlteredCount);
        Assert.True(result.ZipHashMatches);
    }

    [Fact]
    public void ManifiestoDe140_SeLeeSinPerderCampos()
    {
        var json = File.ReadAllText(Path.Combine(ReferenceDir, "referencia.zip.manifest.json"));

        var manifest = _verificationService.ParseManifest(json);

        Assert.Equal("v1.4.0", manifest.ForZipVersion);
        Assert.Equal("REF-140", manifest.CaseNumber);
        Assert.Equal(2, manifest.Files.Count);
        Assert.False(manifest.IsSplit);
    }

    [Fact]
    public void InformeDe140_VerificaContraSuSidecar()
    {
        var reportPath = Path.Combine(ReferenceDir, "referencia.report.txt");

        var (isValid, _) = _reportService.VerifyReport(reportPath);

        Assert.True(isValid, "El informe de 1.4.0 dejó de verificar contra su .sha256.");
    }

    // -------------------------------------------------------------------------
    //  13. El perfil Estándar produce el mismo paquete que 1.4.0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PerfilEstandar_ProduceElMismoContenidoForenseQue140()
    {
        var regenerated = await RegeneratePackageAsync();
        var reference = ReadManifest(Path.Combine(ReferenceDir, "referencia.zip.manifest.json"));

        // Parámetros de la operación: si alguno cambió, el perfil Estándar dejó de ser el de
        // siempre y la promesa de esta versión está rota.
        // FormatVersion no: desde 2.0 el manifiesto es 1.4, porque registra el entorno que antes solo estaba en el
        // informe. Lo que se verifica (paquete y contenido) sigue idéntico al de 1.4.0.
        Assert.Equal("1.4", regenerated.RootElement.GetProperty("FormatVersion").GetString());
        foreach (var field in new[] { "Operation", "CompressionLevel", "HasPassword", "IsSplit", "ZipFileSize", "ZipSha256" })
        {
            Assert.Equal(
                reference.RootElement.GetProperty(field).ToString(),
                regenerated.RootElement.GetProperty(field).ToString());
        }

        Assert.Equal(
            reference.RootElement.GetProperty("Algorithms").ToString(),
            regenerated.RootElement.GetProperty("Algorithms").ToString());

        // Veredicto archivo por archivo: nombre de entrada, tamaño, fecha de origen y hash.
        var referenceFiles = ReadFileEntries(reference);
        var regeneratedFiles = ReadFileEntries(regenerated);

        Assert.Equal(referenceFiles.Keys.OrderBy(k => k), regeneratedFiles.Keys.OrderBy(k => k));

        foreach (var (entryName, expected) in referenceFiles)
        {
            Assert.Equal(expected, regeneratedFiles[entryName]);
        }

        reference.Dispose();
        regenerated.Dispose();
    }

    [Fact]
    public async Task PerfilEstandar_ElInformeSoloCambiaEnLaFechaDeGeneracion()
    {
        await RegeneratePackageAsync();

        var reference = NormalizeReport(File.ReadAllLines(Path.Combine(ReferenceDir, "referencia.report.txt")));
        var regenerated = NormalizeReport(File.ReadAllLines(Path.Combine(_workDir, "referencia.report.txt")));

        Assert.Equal(reference, regenerated);
    }

    // -------------------------------------------------------------------------
    //  Ayudantes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Regenera el paquete con esta versión, en perfil Estándar y desde las mismas entradas,
    /// reponiendo la fecha de modificación con la que se generó la referencia.
    /// </summary>
    private async Task<JsonDocument> RegeneratePackageAsync()
    {
        var srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(srcDir);

        foreach (var file in Directory.GetFiles(Path.Combine(ReferenceDir, "src")))
        {
            var target = Path.Combine(srcDir, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
            File.SetLastWriteTimeUtc(target, FixedModifiedUtc);
        }

        var zipPath = Path.Combine(_workDir, "referencia.zip");
        var options = new ZipOptions
        {
            SourcePaths = new List<string> { srcDir },
            OutputPath = zipPath,
            CompressionLevel = 5,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
        };

        var compression = await _zipService.CompressAsync(options, null, CancellationToken.None);

        var zipHash = (await _hashService.ComputeHashesAsync(
                zipPath, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None))
            .Hashes[HashAlgorithmType.SHA256];

        var data = new ReportData
        {
            Operator = new OperatorInfo { Name = "Operador de referencia" },
            CaseNumber = "REF-140",
            Operation = OperationType.Compression,
            CompressionLevel = 5,
            HasPassword = false,
            Algorithms = options.HashAlgorithms,
            ZipFilePath = zipPath,
            ZipFileSize = new FileInfo(zipPath).Length,
            ZipHash = zipHash,
            FileResults = compression.FileHashes
        };

        var manifestPath = Path.Combine(_workDir, "regenerado.manifest.json");
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));

        await _reportService.SaveReportAsync(
            _reportService.GenerateReport(data, "es"),
            Path.Combine(_workDir, "referencia.report.txt"));

        return ReadManifest(manifestPath);
    }

    private static JsonDocument ReadManifest(string path) => JsonDocument.Parse(File.ReadAllText(path));

    /// <summary>
    /// Entradas del manifiesto por nombre, con lo que debe ser idéntico entre versiones. Se
    /// deja afuera <c>SourcePath</c>, que es la ruta absoluta de la máquina que empaquetó.
    /// </summary>
    private static Dictionary<string, string> ReadFileEntries(JsonDocument manifest)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in manifest.RootElement.GetProperty("Files").EnumerateArray())
        {
            var name = file.GetProperty("EntryName").GetString()!;
            var size = file.GetProperty("Size").ToString();
            var modified = file.GetProperty("ModifiedUtc").ToString();
            var hashes = file.GetProperty("Hashes").ToString();

            entries[name] = $"{size}|{modified}|{hashes}";
        }

        return entries;
    }

    /// <summary>
    /// Quita del informe lo que cambia por construcción en cada corrida: la fecha de
    /// generación, la versión declarada, las rutas de origen y los datos del equipo.
    /// </summary>
    private static List<string> NormalizeReport(IEnumerable<string> lines)
    {
        var volatilePrefixes = new[]
        {
            "  Fecha", "  Generado", "  INFORME FORENSE", "  Equipo", "  Usuario",
            "  Sistema", "  Ruta", "  Origen", "  Archivo ZIP", "  Version", "  Versión"
        };

        return lines
            .Where(line => !volatilePrefixes.Any(p => line.StartsWith(p, StringComparison.Ordinal)))
            .Where(line => !line.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains(@"C:\", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains(@"D:\", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
