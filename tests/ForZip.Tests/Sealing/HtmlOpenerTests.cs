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
using System.Text.RegularExpressions;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Validación cruzada del abridor HTML (<c>Tools/Open_FZS.html</c>): un sobre de
/// ForZip abierto por el núcleo de la página tiene que dar el mismo contenedor interno, byte a
/// byte, que el que da ForZip. Mismo criterio que el test 23 con el extractor de Python.
/// </summary>
/// <remarks>
/// El núcleo se corre en Node, que tiene la misma Web Crypto que el navegador, y se toma del
/// HTML tal cual se publica: no hay una copia aparte que pueda quedar desincronizada. La
/// interfaz (selectores de archivo, guardado) no se prueba acá: eso es del navegador.
/// </remarks>
[Collection(SealingCollection.Name)]
public class HtmlOpenerTests : IDisposable
{
    private const string PackagePassword = "clave-del-sobre-de-prueba-2026";
    private const int TestIterations = 1000;
    private const int TagLength = 16;

    private readonly string _workDir;
    private readonly SealingService _sealingService;
    private readonly SealedPackageBuilder _builder;

    public HtmlOpenerTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_htmlopener_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);

        // Chunk mínimo para que haya varios bloques: el índice en el nonce y el isFinal del
        // AAD son justo donde dos implementaciones del formato divergen.
        _sealingService = new SealingService(TestIterations, SealedContainerFormat.MinChunkSize);
        _builder = new SealedPackageBuilder(
            _sealingService, new ReceiptService(new HashService(), new SignatureService()));
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
    //  Validación cruzada con Node
    // -------------------------------------------------------------------------

    [NodeOpenerFact]
    public async Task Abridor_AbreUnSobreDeForZip_YElContenidoEsIdenticoByteAByte()
    {
        var result = await SealAsync();

        var referencia = Path.Combine(_workDir, "referencia.zip");
        await _sealingService.UnsealFileAsync(
            result.SealedPath, referencia, PackagePassword, null, CancellationToken.None);

        var delAbridor = Path.Combine(_workDir, "abridor.zip");
        var run = RunOpener(result.SealedPath, delAbridor, PackagePassword);

        Assert.True(run.ExitCode == 0, $"El abridor falló: {run.StandardError}");
        Assert.Equal(Sha256Of(referencia), Sha256Of(delAbridor));

        using var archive = ZipFile.OpenRead(delAbridor);
        Assert.Contains(archive.Entries, e => e.Name == "evidencia.zip");
    }

    [NodeOpenerFact]
    public async Task Abridor_ConContrasenaIncorrecta_NoDejaNadaEscrito()
    {
        var result = await SealAsync();
        var salida = Path.Combine(_workDir, "no-deberia-existir.zip");

        var run = RunOpener(result.SealedPath, salida, "esta-no-es-la-clave");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("contrasena", run.StandardError);
        Assert.False(File.Exists(salida));
    }

    [NodeOpenerFact]
    public async Task Abridor_ConSobreTruncado_LoDetecta()
    {
        var result = await SealAsync();

        // Se corta el último chunk entero: solo el tamaño declarado en el header lo delata.
        var truncado = Path.Combine(_workDir, "truncado.fzs");
        var bytes = await File.ReadAllBytesAsync(result.SealedPath);
        await File.WriteAllBytesAsync(
            truncado, bytes[..(bytes.Length - SealedContainerFormat.MinChunkSize)]);

        var run = RunOpener(truncado, Path.Combine(_workDir, "truncado.zip"), PackagePassword);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("truncado", run.StandardError);
    }

    [NodeOpenerFact]
    public async Task Abridor_ConIteracionesDesmedidas_RechazaSinDerivar()
    {
        var result = await SealAsync();
        var alterado = Path.Combine(_workDir, "iteraciones.fzs");
        await File.WriteAllBytesAsync(alterado,
            SealingSafetyTests.WithIterations(await File.ReadAllBytesAsync(result.SealedPath), 2_000_000_000));

        var run = RunOpener(alterado, Path.Combine(_workDir, "iteraciones.zip"), PackagePassword);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("formato", run.StandardError);
    }

    [NodeOpenerFact]
    public async Task Abridor_ConUnBloqueAlteradoEnElMedio_InformaEseBloque()
    {
        var result = await SealAsync();
        var bytes = await File.ReadAllBytesAsync(result.SealedPath);

        // Un bit cambiado en el texto cifrado del chunk 2: la clave es correcta, así que el
        // veredicto tiene que ser "alterado en el bloque 2", no "contraseña incorrecta".
        var headerLen = BitConverter.ToInt32(bytes, 10);
        var chunkConTag = SealedContainerFormat.MinChunkSize + TagLength;
        bytes[14 + headerLen + 2 * chunkConTag + 10] ^= 0x01;
        var alterado = Path.Combine(_workDir, "alterado.fzs");
        await File.WriteAllBytesAsync(alterado, bytes);

        var salida = Path.Combine(_workDir, "alterado.zip");
        var run = RunOpener(alterado, salida, PackagePassword);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("alterado {\"chunk\":2}", run.StandardError);
        Assert.False(File.Exists(salida));
    }

    // -------------------------------------------------------------------------
    //  La página viaja con cada sobre
    // -------------------------------------------------------------------------

    [Fact]
    public void PaginaPublicada_EsLaMismaQueViajaEmbebida()
    {
        // Una copia embebida que se quedó atrás dejaría junto al sobre una página distinta de
        // la que se publica y se prueba.
        using var embebida = EnvelopeOpenerPage.OpenStream();
        using var memoria = new MemoryStream();
        embebida.CopyTo(memoria);

        Assert.Equal(File.ReadAllBytes(HtmlPath), memoria.ToArray());
        Assert.Equal(Sha256Of(HtmlPath), EnvelopeOpenerPage.Sha256);
    }

    [Fact]
    public async Task Sellar_DejaElAbridorJuntoAlSobreYAlRecibo()
    {
        var result = await SealAsync();

        Assert.NotNull(result.OpenerPagePath);
        Assert.Equal(EnvelopeOpenerPage.FileName, Path.GetFileName(result.OpenerPagePath));
        Assert.Equal(Path.GetDirectoryName(result.SealedPath), Path.GetDirectoryName(result.OpenerPagePath));
        Assert.Equal(Path.GetDirectoryName(result.ReceiptPath), Path.GetDirectoryName(result.OpenerPagePath));
        Assert.Equal(Sha256Of(HtmlPath), Sha256Of(result.OpenerPagePath!));
    }

    [Fact]
    public async Task Sellar_ReemplazaUnaCopiaViejaDelAbridor()
    {
        // Una página que no coincide con la de esta versión —vieja o tocada— no debe quedar
        // junto a un sobre nuevo.
        var salida = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(salida);
        await File.WriteAllTextAsync(Path.Combine(salida, EnvelopeOpenerPage.FileName), "pagina vieja");

        var result = await SealAsync();

        Assert.Equal(Sha256Of(HtmlPath), Sha256Of(result.OpenerPagePath!));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Acta_NombraElAbridorYTraeSuHash(string language)
    {
        var acta = new DeliveryActService(new LocalizationService()).Generate(
            new DeliveryActData
            {
                PackageId = Guid.NewGuid(),
                SealedFileName = "FZS-12345678.fzs",
                SealedSha256 = new string('a', 64),
                SealedAtUtc = DateTimeOffset.UtcNow,
                Password = "clave-de-prueba-para-el-acta-2026"
            },
            language);

        Assert.Contains(EnvelopeOpenerPage.FileName, acta);
        // El hash del acta es el de la página publicada: es contra lo que coteja el receptor.
        Assert.Contains(Sha256Of(HtmlPath), acta);
        Assert.All(acta.Split("\r\n"), line => Assert.True(line.Length <= 80, $"Línea de más de 80: {line}"));
    }

    // -------------------------------------------------------------------------
    //  Lo que la página promete, verificable sin navegador
    // -------------------------------------------------------------------------

    [Fact]
    public void Pagina_ProhibeTodaConexionPorPoliticaDeSeguridad()
    {
        var csp = Regex.Match(Html, "http-equiv=\"Content-Security-Policy\"\\s+content=\"([^\"]+)\"");

        Assert.True(csp.Success, "La página no declara Content-Security-Policy.");
        Assert.Contains("default-src 'none'", csp.Groups[1].Value);
        Assert.Contains("connect-src 'none'", csp.Groups[1].Value);
        Assert.Contains("form-action 'none'", csp.Groups[1].Value);
    }

    [Fact]
    public void Pagina_NoCargaNadaDeAfuera_NiGuardaNiRegistraNada()
    {
        // Ningún recurso con src/href a la red: la página tiene que andar en un laboratorio aislado.
        Assert.DoesNotMatch("(src|href)\\s*=\\s*\"(https?:)?//", Html);

        // La contraseña no puede terminar en ningún almacenamiento ni en la consola.
        Assert.DoesNotContain("localStorage", Html);
        Assert.DoesNotContain("sessionStorage", Html);
        Assert.DoesNotContain("indexedDB", Html);
        Assert.DoesNotContain("console.", Html);
    }

    [Fact]
    public void Pagina_TieneElNucleoSeparadoDeLaInterfaz()
    {
        // El arnés de Node y cualquier auditor dependen de que el núcleo esté en su propio bloque.
        Assert.Contains("<script id=\"fzs-nucleo\">", Html);
        Assert.DoesNotContain("document.", NucleoDe(Html));
    }

    // -------------------------------------------------------------------------
    //  Ayudantes
    // -------------------------------------------------------------------------

    private static string HtmlPath =>
        Path.Combine(AppContext.BaseDirectory, "Tools", "Open_FZS.html");

    private static string HarnessPath =>
        Path.Combine(AppContext.BaseDirectory, "HtmlOpener", "abridor_harness.mjs");

    private static string Html => File.ReadAllText(HtmlPath);

    private static string NucleoDe(string html) =>
        Regex.Match(html, "<script id=\"fzs-nucleo\">([\\s\\S]*?)</script>").Groups[1].Value;

    private sealed record OpenerRun(int ExitCode, string StandardOutput, string StandardError);

    private static OpenerRun RunOpener(string sealedPath, string outputPath, string password)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(HarnessPath);
        startInfo.ArgumentList.Add(HtmlPath);
        startInfo.ArgumentList.Add(sealedPath);
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo lanzar Node.");

        process.StandardInput.Write(password);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("El abridor no terminó a tiempo.");
        }

        return new OpenerRun(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Sella un paquete por el camino de producción, con datos incompresibles para que el
    /// sobre tenga varios chunks y cualquier diferencia de un byte se note.
    /// </summary>
    private async Task<SealedPackageResult> SealAsync()
    {
        var artefactos = Path.Combine(_workDir, "artefactos");
        Directory.CreateDirectory(artefactos);

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
        await File.WriteAllTextAsync(reportPath, "Informe forense de prueba — ñandú.\n");

        return await _builder.BuildAsync(
            new SealedPackageOptions
            {
                ArtifactPaths = new[] { zipPath, reportPath },
                OutputDirectory = Path.Combine(_workDir, "salida"),
                Password = PackagePassword
            },
            null,
            CancellationToken.None);
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
