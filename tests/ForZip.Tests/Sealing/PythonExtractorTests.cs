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

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Test 23 de la especificación 1.5.0: un sobre generado por ForZip se abre con
/// <c>Tools/fzs_extract.py</c> y el contenido resultante es idéntico byte a byte.
/// </summary>
/// <remarks>
/// Es la validación cruzada del formato, y su valor está en que el script se escribió desde
/// <c>Documentation/Formato_FZS_v1.md</c> y no desde el C#. Si este test falla, la pregunta
/// no es cuál de los dos hay que ajustar para que pase: es cuál de los dos está mal.
/// Todo cambio en el formato tiene que volver a pasar por acá.
/// </remarks>
[Collection(SealingCollection.Name)]
public class PythonExtractorTests : IDisposable
{
    private const string PackagePassword = "clave-del-sobre-de-prueba-2026";
    private const int TestIterations = 1000;

    private readonly string _workDir;
    private readonly HashService _hashService = new();
    private readonly SealingService _sealingService;
    private readonly SealedPackageBuilder _builder;

    public PythonExtractorTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_pyextract_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);

        // Iteraciones bajas para no gastar medio segundo por lado, y chunk mínimo para que el
        // sobre tenga varios bloques: con uno solo no se ejercitarían ni el índice dentro del
        // nonce ni el isFinal del AAD, que es justo donde dos implementaciones divergen.
        _sealingService = new SealingService(TestIterations, SealedContainerFormat.MinChunkSize);
        _builder = new SealedPackageBuilder(
            _sealingService, new ReceiptService(_hashService, new SignatureService()));
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
    //  23. Validación cruzada del formato
    // -------------------------------------------------------------------------

    [PythonExtractorFact]
    public async Task Extractor_AbreUnSobreDeForZip_YElContenidoEsIdenticoByteAByte()
    {
        var result = await SealAsync();

        // Lo que ForZip considera la carga útil del sobre.
        var referencia = Path.Combine(_workDir, "referencia.zip");
        await _sealingService.UnsealFileAsync(
            result.SealedPath, referencia, PackagePassword, null, CancellationToken.None);

        // Lo que el script escribe a partir de la especificación publicada.
        var delScript = Path.Combine(_workDir, "del-script.zip");
        var run = RunExtractor(result.SealedPath, delScript, PackagePassword);

        Assert.True(run.ExitCode == 0, $"El extractor falló: {run.StandardError}");
        Assert.Equal(Sha256Of(referencia), Sha256Of(delScript));
        Assert.Equal(new FileInfo(referencia).Length, new FileInfo(delScript).Length);

        // Y lo que sale de ahí es un ZIP común, con los artefactos en la raíz.
        using var archive = ZipFile.OpenRead(delScript);
        Assert.Contains(archive.Entries, e => e.Name == "evidencia.zip");
        Assert.Contains(archive.Entries, e => e.Name == "evidencia.report.txt");
    }

    [PythonExtractorFact]
    public async Task Extractor_ConContrasenaIncorrecta_NoDejaNadaEscrito()
    {
        var result = await SealAsync();
        var salida = Path.Combine(_workDir, "no-deberia-existir.zip");

        var run = RunExtractor(result.SealedPath, salida, "esta-no-es-la-clave");

        Assert.Equal(2, run.ExitCode);
        Assert.False(File.Exists(salida));
        Assert.False(File.Exists(salida + ".partial"));
    }

    [PythonExtractorFact]
    public async Task Extractor_ConSobreTruncado_LoDetecta()
    {
        var result = await SealAsync();

        // Se corta el último chunk entero: el archivo sigue siendo un sobre bien formado y
        // solo el tamaño declarado en el header delata el recorte.
        var truncado = Path.Combine(_workDir, "truncado.fzs");
        var bytes = await File.ReadAllBytesAsync(result.SealedPath);
        await File.WriteAllBytesAsync(
            truncado, bytes[..(bytes.Length - SealedContainerFormat.MinChunkSize)]);

        var run = RunExtractor(truncado, Path.Combine(_workDir, "truncado.zip"), PackagePassword);

        Assert.Equal(3, run.ExitCode);
    }

    [PythonExtractorFact]
    public async Task Extractor_ConIteracionesDesmedidas_RechazaSinDerivar()
    {
        var result = await SealAsync();
        var alterado = Path.Combine(_workDir, "iteraciones.fzs");
        await File.WriteAllBytesAsync(alterado,
            SealingSafetyTests.WithIterations(await File.ReadAllBytesAsync(result.SealedPath), 2_000_000_000));

        var run = RunExtractor(alterado, Path.Combine(_workDir, "iteraciones.zip"), PackagePassword);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("iteraciones", run.StandardError);
    }

    // -------------------------------------------------------------------------
    //  El extractor viaja adentro del sobre
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ContenedorInterno_LlevaUnaCopiaDelExtractor()
    {
        var result = await SealAsync();

        var payload = Path.Combine(_workDir, "payload.zip");
        await _sealingService.UnsealFileAsync(
            result.SealedPath, payload, PackagePassword, null, CancellationToken.None);

        using var archive = ZipFile.OpenRead(payload);
        var entry = archive.GetEntry(EmergencyExtractor.FileName);

        Assert.NotNull(entry);

        // Tiene que ser el mismo archivo que publica el repositorio: una copia que se quedó
        // atrás no sirve para abrir un sobre de este formato.
        await using var incluido = entry!.Open();
        await using var enElRepo = EmergencyExtractor.OpenStream();
        Assert.Equal(await ReadAllAsync(enElRepo), await ReadAllAsync(incluido));
    }

    [Fact]
    public void ExtractorPublicado_EsElMismoQueViajaEmbebido()
    {
        using var embebido = EmergencyExtractor.OpenStream();
        using var memoria = new MemoryStream();
        embebido.CopyTo(memoria);

        Assert.Equal(File.ReadAllBytes(ScriptPath), memoria.ToArray());
    }

    // -------------------------------------------------------------------------
    //  Ayudantes
    // -------------------------------------------------------------------------

    private static string ScriptPath =>
        Path.Combine(AppContext.BaseDirectory, "Tools", "fzs_extract.py");

    private sealed record ExtractorRun(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Corre el extractor sobre un sobre y le pasa la contraseña por la entrada estándar.
    /// </summary>
    private static ExtractorRun RunExtractor(string sealedPath, string outputPath, string password)
    {
        var startInfo = new ProcessStartInfo(PythonExtractorFactAttribute.Path ?? "python")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add(sealedPath);
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo lanzar el intérprete de Python.");

        process.StandardInput.WriteLine(password);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("El extractor no terminó a tiempo.");
        }

        return new ExtractorRun(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Sella un paquete por el camino de producción: artefactos reales, contenedor interno,
    /// sobre y recibo. Lo que se le da al script es lo mismo que sale de la GUI y del CLI.
    /// </summary>
    private async Task<SealedPackageResult> SealAsync()
    {
        var artefactos = Path.Combine(_workDir, "artefactos");
        Directory.CreateDirectory(artefactos);

        // Un ZIP con datos incompresibles, para que el sobre tenga varios chunks y para que
        // cualquier diferencia de un solo byte entre las dos implementaciones se note.
        var zipPath = Path.Combine(artefactos, "evidencia.zip");
        var ruido = RandomNumberGenerator.GetBytes(3 * SealedContainerFormat.MinChunkSize);
        await using (var zip = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("evidencia.bin", CompressionLevel.NoCompression);
            await using var target = entry.Open();
            await target.WriteAsync(ruido);
        }

        var reportPath = Path.Combine(artefactos, "evidencia.report.txt");
        await File.WriteAllTextAsync(reportPath, "Informe forense de prueba.\n");

        var salida = Path.Combine(_workDir, "salida");
        return await _builder.BuildAsync(
            new SealedPackageOptions
            {
                ArtifactPaths = new[] { zipPath, reportPath },
                OutputDirectory = salida,
                Password = PackagePassword
            },
            null,
            CancellationToken.None);
    }

    private string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var memoria = new MemoryStream();
        await stream.CopyToAsync(memoria);
        return memoria.ToArray();
    }
}
