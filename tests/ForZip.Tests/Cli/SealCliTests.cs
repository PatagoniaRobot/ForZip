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

using ForZip.Cli;
using ForZip.Cli.Commands;
using ForZip.Core.Interfaces;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Cli;

/// <summary>
/// Tests del CLI del perfil Reservado (Fase C): <c>zip --profile reserved</c>, <c>seal</c>,
/// <c>unseal</c> y <c>verify -i</c>.
/// </summary>
[Collection(ForZip.Tests.Sealing.SealingCollection.Name)]
public class SealCliTests : IDisposable
{
    private const string Password = "clave-de-prueba-del-sobre-2026-larga";

    private readonly string _workDir;
    private readonly string _srcDir;

    private readonly HashService _hashService = new();
    private readonly LocalizationService _localization = new();
    private readonly PasswordService _passwordService = new();
    private readonly SignatureService _signatureService = new();
    private readonly ZipService _zipService;
    private readonly ReportService _reportService;
    private readonly VerificationService _verificationService;
    private readonly SealingService _sealingService;
    private readonly ReceiptService _receiptService;
    private readonly SealedPackageBuilder _sealedPackageBuilder;
    private readonly SealedVerificationService _sealedVerificationService;
    private readonly DeliveryActService _actService;
    private readonly CloudSyncDetector _cloudSyncDetector = new(Array.Empty<CloudSyncRoot>());

    public SealCliTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_seal_cli_{Guid.NewGuid():N}");
        _srcDir = Path.Combine(_workDir, "src");
        Directory.CreateDirectory(_srcDir);
        File.WriteAllText(Path.Combine(_srcDir, "a.txt"), "contenido A de la evidencia");
        File.WriteAllText(Path.Combine(_srcDir, "b.txt"), "contenido B de la evidencia");

        _zipService = new ZipService(_hashService);
        _reportService = new ReportService(_localization);
        _verificationService = new VerificationService(_hashService, _signatureService);
        _sealingService = new SealingService(1000);
        _receiptService = new ReceiptService(_hashService, _signatureService);
        _sealedPackageBuilder = new SealedPackageBuilder(_sealingService, _receiptService);
        _sealedVerificationService = new SealedVerificationService(
            _sealingService, _receiptService, _signatureService, _verificationService, _hashService);
        _actService = new DeliveryActService(_localization);
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

    private ZipCommand Zip() =>
        new(_zipService, _reportService, _hashService, _signatureService,
            _sealedPackageBuilder, _passwordService, _actService, _localization);

    private VerifyCommand Verify() =>
        new(_reportService, _verificationService, _sealedVerificationService, _localization);

    private SealCommand Seal() =>
        new(_sealedPackageBuilder, _passwordService, _actService, _cloudSyncDetector, _localization);

    private UnsealCommand Unseal() => new(new SealedPackageOpener(_sealingService, _verificationService), _cloudSyncDetector);

    // -------------------------------------------------------------------------
    //  zip --profile reserved
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ZipReservado_DejaSoloElSobreElReciboYElAbridor()
    {
        var outputDir = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(outputDir);
        var zipPath = Path.Combine(outputDir, "caso.zip");
        var actaPath = Path.Combine(_workDir, "acta.txt");

        var code = await Zip().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", zipPath, "--hash", "sha256",
            "--profile", "reserved", "--acta", actaPath,
            "--case", "CAUSA-4471/26", "--court", "Juzgado de prueba", "--lang", "es"
        }));

        Assert.Equal(0, code);

        // Los artefactos del paso 1 no quedan sueltos: fuera del sobre no hay nada en claro.
        // El abridor HTML es una página genérica, igual para todo sobre: no contiene evidencia.
        var remaining = Directory.GetFiles(outputDir).Select(Path.GetFileName).ToList();
        Assert.Equal(3, remaining.Count);
        Assert.Contains(remaining, f => f!.EndsWith(".fzs", StringComparison.Ordinal));
        Assert.Contains(remaining, f => f!.EndsWith(ReceiptService.ReceiptSuffix, StringComparison.Ordinal));
        Assert.Contains(EnvelopeOpenerPage.FileName, remaining);
        Assert.DoesNotContain(remaining, f => f!.EndsWith(".zip", StringComparison.Ordinal));

        // El acta se escribe donde el operador la pidió, nunca junto al sobre.
        Assert.True(File.Exists(actaPath));
        Assert.False(File.Exists(Path.Combine(outputDir, Path.GetFileName(actaPath))));
    }

    [Fact]
    public async Task ZipReservado_SinActa_Aborta()
    {
        var zipPath = Path.Combine(_workDir, "caso.zip");

        var code = await Zip().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", zipPath, "--profile", "reserved"
        }));

        // Sin acta la contraseña generada se perdería y el paquete quedaría inabrible.
        Assert.Equal(1, code);
        Assert.False(File.Exists(zipPath));
    }

    [Fact]
    public async Task ZipReservado_ConUnActaQueYaExiste_NoLaPisaYNoEmpaqueta()
    {
        // Un acta anterior guarda la contraseña de otro sobre: pisarla lo dejaba inabrible.
        var zipPath = Path.Combine(_workDir, "caso.zip");
        var actaPath = Path.Combine(_workDir, "acta.txt");
        File.WriteAllText(actaPath, "acta de otro sobre, con su contraseña");

        var code = await Zip().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", zipPath, "--profile", "reserved", "--acta", actaPath
        }));

        Assert.Equal(1, code);
        Assert.Equal("acta de otro sobre, con su contraseña", File.ReadAllText(actaPath));
        Assert.False(File.Exists(zipPath));
    }

    [Fact]
    public async Task ZipReservado_ConUnCertificadoQueNoAbre_NoEmpaqueta()
    {
        // La contraseña del PFX mal tipeada aparecía recién al firmar el recibo, con todo hecho.
        var zipPath = Path.Combine(_workDir, "caso.zip");
        var actaPath = Path.Combine(_workDir, "acta.txt");

        var code = await Zip().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", zipPath, "--profile", "reserved", "--acta", actaPath,
            "--sign-cert", CreatePfx("correcta"), "--sign-cert-password", "equivocada"
        }));

        Assert.Equal(1, code);
        Assert.False(File.Exists(zipPath));
        Assert.False(File.Exists(actaPath));
    }

    [Fact]
    public async Task ZipReservado_SiElSelladoFalla_DescartaElPaqueteIntermedio()
    {
        // Sin sobre no hay acta, y la contraseña generada solo iba a quedar en el acta: quedaban un ZIP que
        // nadie podía abrir y el informe y el manifiesto en claro, con los datos de la causa.
        var outputDir = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(outputDir);
        var actaPath = Path.Combine(_workDir, "acta.txt");
        var zip = new ZipCommand(_zipService, _reportService, _hashService, _signatureService,
            new FailingBuilder(), _passwordService, _actService, _localization);

        var code = await zip.ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", Path.Combine(outputDir, "caso.zip"), "--profile", "reserved",
            "--acta", actaPath, "--case", "CAUSA-4471/26"
        }));

        Assert.Equal(2, code);
        Assert.Empty(Directory.GetFiles(outputDir));
        Assert.False(File.Exists(actaPath));
        Assert.Equal(2, Directory.GetFiles(_srcDir).Length); // la evidencia original, intacta
    }

    [Fact]
    public async Task Verify_InputIsAManifest_VerifiesThePackage()
    {
        // -i se trataba siempre como sobre: un manifiesto respondía "no es un sobre ForZip válido".
        var zipPath = Path.Combine(_workDir, "caso.zip");
        Assert.Equal(0, await Zip().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", zipPath, "--hash", "sha256", "--report", zipPath + ".report.txt"
        })));

        var code = await Verify().ExecuteAsync(new CommandParser(new[] { "verify", "-i", zipPath + ".manifest.json" }));

        Assert.Equal(0, code);
    }

    [Fact]
    public void Collect_TakesTheGuiReportName_AndLeavesOutOpenerPagesAndLeftovers()
    {
        var dir = Path.Combine(_workDir, "paquete-gui");
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "caso.zip", "caso.zip.manifest.json", "caso.report.txt", "caso.report.txt.sha256",
                     "Open_FZS.html", "Abrir_sobre_ForZip.html", ".forzip-sellando-0123.tmp" })
        {
            File.WriteAllText(Path.Combine(dir, name), "x");
        }

        // La GUI llama al informe caso.report.txt: quedaba afuera del sobre, y en claro con --delete-sources.
        var fromFile = SealedArtifacts.Collect(Path.Combine(dir, "caso.zip")).Select(Path.GetFileName).ToList();
        Assert.Contains("caso.report.txt", fromFile);
        Assert.Contains("caso.report.txt.sha256", fromFile);

        // De una carpeta ya sellada, la página para abrir sobres y los temporales no son evidencia.
        var fromFolder = SealedArtifacts.Collect(dir).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain("Open_FZS.html", fromFolder);
        Assert.DoesNotContain("Abrir_sobre_ForZip.html", fromFolder);
        Assert.DoesNotContain(".forzip-sellando-0123.tmp", fromFolder);
        Assert.Contains("caso.report.txt", fromFolder);
    }

    private sealed class FailingBuilder : ISealedPackageBuilder
    {
        public Task<SealedPackageResult> BuildAsync(SealedPackageOptions options,
            IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct) =>
            throw new IOException("No hay espacio suficiente para el sobre sellado.");
    }

    private string CreatePfx(string pfxPassword)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=ForZip Test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_workDir, "operador.pfx");
        File.WriteAllBytes(path, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, pfxPassword));
        return path;
    }

    [Fact]
    public async Task Seal_ConActaEnUnaCarpetaQueNoExiste_NoSellaNada()
    {
        // Con contraseña generada el acta es el único lugar donde queda: si no se puede escribir, se
        // descubría recién con el sobre ya hecho e inabrible.
        var paquete = Path.Combine(_workDir, "paquete");
        Directory.CreateDirectory(paquete);
        File.WriteAllText(Path.Combine(paquete, "caso.zip"), "contenido");
        var salida = Path.Combine(_workDir, "salida");

        var code = await Seal().ExecuteAsync(new CommandParser(new[]
        {
            "seal", "-i", paquete, "-o", salida, "--gen-password", "--acta", Path.Combine(_workDir, "no-existe", "acta.txt")
        }));

        Assert.Equal(1, code);
        Assert.False(Directory.Exists(salida) && Directory.EnumerateFiles(salida, "*.fzs").Any());
    }

    [Fact]
    public async Task ZipReservado_ElNombreDelSobreNoSaleDeLaCausa()
    {
        var outputDir = await BuildReservedPackageAsync();

        var sealedName = Path.GetFileName(FindSealed(outputDir));

        Assert.StartsWith(SealedContainerFormat.FileNamePrefix, sealedName, StringComparison.Ordinal);
        Assert.DoesNotContain("4471", sealedName, StringComparison.Ordinal);
        Assert.DoesNotContain("caso", sealedName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZipReservado_LaClaveGeneradaAbreElSobre()
    {
        var outputDir = await BuildReservedPackageAsync();
        var actaPath = Path.Combine(_workDir, "acta.txt");

        // La clave existe en un solo lugar: el acta. Es todo lo que tiene el receptor.
        var password = ExtractPasswordFromAct(actaPath);
        Assert.Equal(SealedPasswordPolicy.DefaultLength, password.Length);

        var code = await Verify().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-i", FindSealed(outputDir), "-p", password
        }));

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task ZipReservado_PerfilDesconocido_Falla()
    {
        var parser = new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", Path.Combine(_workDir, "caso.zip"), "--profile", "confidencial"
        });

        await Assert.ThrowsAsync<ArgumentException>(() => Zip().ExecuteAsync(parser));
    }

    // -------------------------------------------------------------------------
    //  verify -i
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Verify_SobreIntacto_DevuelveCero()
    {
        var outputDir = await BuildReservedPackageAsync();

        var code = await Verify().ExecuteAsync(new CommandParser(new[] { "verify", "-i", FindSealed(outputDir) }));

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Verify_DesdeElRecibo_DevuelveCero()
    {
        var outputDir = await BuildReservedPackageAsync();
        var receiptPath = Directory.GetFiles(outputDir, "*" + ReceiptService.ReceiptSuffix).Single();

        var code = await Verify().ExecuteAsync(new CommandParser(new[] { "verify", "-i", receiptPath }));

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Verify_SobreAlterado_DevuelveTres()
    {
        var outputDir = await BuildReservedPackageAsync();
        var sealedPath = FindSealed(outputDir);

        var bytes = await File.ReadAllBytesAsync(sealedPath);
        bytes[^40] ^= 0xFF;
        await File.WriteAllBytesAsync(sealedPath, bytes);

        var code = await Verify().ExecuteAsync(new CommandParser(new[] { "verify", "-i", sealedPath }));

        Assert.Equal(3, code);
    }

    [Fact]
    public async Task Verify_ClaveIncorrecta_DevuelveDos()
    {
        var outputDir = await BuildReservedPackageAsync();

        var code = await Verify().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-i", FindSealed(outputDir), "-p", "una-clave-que-no-es"
        }));

        // El sobre está intacto: lo que falló es la clave, no la integridad.
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Verify_ClaveVacia_HaceLaVerificacionPublica()
    {
        var outputDir = await BuildReservedPackageAsync();

        var code = await Verify().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-i", FindSealed(outputDir), "-p", string.Empty
        }));

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Verify_ArchivoInexistente_DevuelveUno()
    {
        var code = await Verify().ExecuteAsync(new CommandParser(new[]
        {
            "verify", "-i", Path.Combine(_workDir, "no-existe.fzs")
        }));

        // Un archivo que no está no es un archivo alterado.
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Verify_SinRecibo_DevuelveDosYAvisa()
    {
        var outputDir = await BuildReservedPackageAsync();
        File.Delete(Directory.GetFiles(outputDir, "*" + ReceiptService.ReceiptSuffix).Single());

        var originalOut = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        int code;
        try
        {
            code = await Verify().ExecuteAsync(new CommandParser(new[] { "verify", "-i", FindSealed(outputDir) }));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // El formato verifica, pero sin recibo no se puede acreditar el origen: no se afirma que sea el
        // archivo que se selló (antes lo decía en verde, con la advertencia debajo).
        Assert.Equal(2, code);
        Assert.DoesNotContain("es el mismo archivo que se selló", captured.ToString());
        Assert.DoesNotContain("[✓]", captured.ToString());
    }

    // -------------------------------------------------------------------------
    //  unseal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Unseal_DevuelveElPaqueteCompleto()
    {
        var outputDir = await BuildReservedPackageAsync();
        var password = ExtractPasswordFromAct(Path.Combine(_workDir, "acta.txt"));
        var destination = Path.Combine(_workDir, "abierto");

        var code = await Unseal().ExecuteAsync(new CommandParser(new[]
        {
            "unseal", "-i", FindSealed(outputDir), "-o", destination, "-p", password
        }));

        Assert.Equal(0, code);

        var recovered = Directory.GetFiles(destination).Select(Path.GetFileName).ToList();
        Assert.Contains("caso.zip", recovered);
        Assert.Contains("caso.zip.manifest.json", recovered);
        // Desde 2.0 el sobre no lleva informe ni .sha256: el informe se arma después, con verify --write-report.
        Assert.DoesNotContain(recovered, n => n!.Contains(".report.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unseal_ClaveIncorrecta_DevuelveTresYNoEscribeNada()
    {
        var outputDir = await BuildReservedPackageAsync();
        var destination = Path.Combine(_workDir, "abierto");

        var code = await Unseal().ExecuteAsync(new CommandParser(new[]
        {
            "unseal", "-i", FindSealed(outputDir), "-o", destination, "-p", "clave-equivocada"
        }));

        Assert.Equal(3, code);
        // Con la contraseña mala no se llega a extraer: la carpeta ni siquiera se crea.
        Assert.False(Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any());
    }

    [Fact]
    public async Task Unseal_VerificaElContenidoContraElManifiesto()
    {
        var outputDir = await BuildReservedPackageAsync();
        var password = ExtractPasswordFromAct(Path.Combine(_workDir, "acta.txt"));
        var destination = Path.Combine(_workDir, "abierto-verificado");

        var originalOut = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        int code;
        try
        {
            code = await Unseal().ExecuteAsync(new CommandParser(new[]
            {
                "unseal", "-i", FindSealed(outputDir), "-o", destination, "-p", password
            }));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, code);
        Assert.Contains("EVIDENCIA ÍNTEGRA", captured.ToString());
    }

    [Fact]
    public async Task Unseal_HaciaCarpetaConArchivos_NoSobrescribe()
    {
        var outputDir = await BuildReservedPackageAsync();
        var password = ExtractPasswordFromAct(Path.Combine(_workDir, "acta.txt"));
        var destination = Path.Combine(_workDir, "ocupada");
        Directory.CreateDirectory(destination);
        var previo = Path.Combine(destination, "caso.zip");
        await File.WriteAllTextAsync(previo, "otra evidencia");

        var code = await Unseal().ExecuteAsync(new CommandParser(new[]
        {
            "unseal", "-i", FindSealed(outputDir), "-o", destination, "-p", password
        }));

        Assert.Equal(1, code);
        Assert.Equal("otra evidencia", await File.ReadAllTextAsync(previo));
    }

    // -------------------------------------------------------------------------
    //  seal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Seal_SobreUnaCarpeta_SellaTodoLoQueTieneAdentro()
    {
        var packageDir = Path.Combine(_workDir, "paquete");
        Directory.CreateDirectory(packageDir);
        await File.WriteAllTextAsync(Path.Combine(packageDir, "evidencia.zip"), "no es un zip real");
        await File.WriteAllTextAsync(Path.Combine(packageDir, "evidencia.zip.manifest.json"), "{}");

        var outputDir = Path.Combine(_workDir, "sellado");

        var code = await Seal().ExecuteAsync(new CommandParser(new[]
        {
            "seal", "-i", packageDir, "-o", outputDir, "-p", Password
        }));

        Assert.Equal(0, code);
        Assert.Single(Directory.GetFiles(outputDir, "*.fzs"));
        Assert.Single(Directory.GetFiles(outputDir, "*" + ReceiptService.ReceiptSuffix));

        // Sin --delete-sources los originales siguen donde estaban.
        Assert.Equal(2, Directory.GetFiles(packageDir).Length);
    }

    [Fact]
    public async Task Seal_ConDeleteSources_NoDejaNadaEnClaro()
    {
        var packageDir = Path.Combine(_workDir, "paquete");
        Directory.CreateDirectory(packageDir);
        await File.WriteAllTextAsync(Path.Combine(packageDir, "evidencia.zip"), "no es un zip real");

        var code = await Seal().ExecuteAsync(new CommandParser(new[]
        {
            "seal", "-i", packageDir, "-o", Path.Combine(_workDir, "sellado"), "-p", Password,
            "--delete-sources"
        }));

        Assert.Equal(0, code);
        Assert.Empty(Directory.GetFiles(packageDir));
    }

    [Fact]
    public async Task Seal_GenPasswordSinActa_Aborta()
    {
        var packageDir = Path.Combine(_workDir, "paquete");
        Directory.CreateDirectory(packageDir);
        await File.WriteAllTextAsync(Path.Combine(packageDir, "evidencia.zip"), "no es un zip real");

        var code = await Seal().ExecuteAsync(new CommandParser(new[]
        {
            "seal", "-i", packageDir, "-o", Path.Combine(_workDir, "sellado"), "--gen-password"
        }));

        Assert.Equal(1, code);
        Assert.False(Directory.Exists(Path.Combine(_workDir, "sellado"))
            && Directory.GetFiles(Path.Combine(_workDir, "sellado"), "*.fzs").Length > 0);
    }

    [Fact]
    public async Task Seal_EntradaInexistente_Aborta()
    {
        var code = await Seal().ExecuteAsync(new CommandParser(new[]
        {
            "seal", "-i", Path.Combine(_workDir, "no-existe"), "-p", Password
        }));

        Assert.Equal(1, code);
    }

    // -------------------------------------------------------------------------
    //  Reunión de artefactos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Collect_SobreUnArchivo_SumaSusSidecar()
    {
        var dir = Path.Combine(_workDir, "paquete");
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "evidencia.zip");
        await File.WriteAllTextAsync(zipPath, "zip");
        await File.WriteAllTextAsync(zipPath + ".manifest.json", "{}");
        await File.WriteAllTextAsync(zipPath + ".manifest.json.p7s", "firma");
        await File.WriteAllTextAsync(Path.Combine(dir, "otro.txt"), "ajeno al paquete");
        // Empieza igual pero no es un sidecar: antes entraba al sobre y --delete-sources lo borraba.
        await File.WriteAllTextAsync(Path.Combine(dir, "evidencia.zipviejo"), "otro paquete");

        var collected = SealedArtifacts.Collect(zipPath).Select(Path.GetFileName).ToList();

        Assert.Equal(3, collected.Count);
        Assert.Contains("evidencia.zip", collected);
        Assert.Contains("evidencia.zip.manifest.json", collected);
        Assert.Contains("evidencia.zip.manifest.json.p7s", collected);
        Assert.DoesNotContain("otro.txt", collected);
        Assert.DoesNotContain("evidencia.zipviejo", collected);
    }

    [Fact]
    public async Task Collect_NoVuelveASellarLoQueYaEsSalidaDeUnSellado()
    {
        var dir = Path.Combine(_workDir, "paquete");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "evidencia.zip"), "zip");
        await File.WriteAllTextAsync(Path.Combine(dir, "FZS-12345678.fzs"), "sobre anterior");
        await File.WriteAllTextAsync(Path.Combine(dir, "FZS-12345678" + ReceiptService.ReceiptSuffix), "{}");
        await File.WriteAllTextAsync(Path.Combine(dir, "FZS-12345678" + DeliveryActService.ActSuffix), "acta");

        var collected = SealedArtifacts.Collect(dir).Select(Path.GetFileName).ToList();

        Assert.Equal(new[] { "evidencia.zip" }, collected);
    }

    // -------------------------------------------------------------------------
    //  Ayudantes
    // -------------------------------------------------------------------------

    /// <summary>Ejecuta el flujo Reservado completo y devuelve la carpeta de salida.</summary>
    private async Task<string> BuildReservedPackageAsync()
    {
        var outputDir = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(outputDir);

        var code = await Zip().ExecuteAsync(new CommandParser(new[]
        {
            "zip", "-i", _srcDir, "-o", Path.Combine(outputDir, "caso.zip"), "--hash", "sha256",
            "--profile", "reserved", "--acta", Path.Combine(_workDir, "acta.txt"),
            "--case", "CAUSA-4471/26", "--lang", "es"
        }));

        Assert.Equal(0, code);
        return outputDir;
    }

    private static string FindSealed(string directory) => Directory.GetFiles(directory, "*.fzs").Single();

    /// <summary>
    /// Recupera la contraseña del acta como lo haría el receptor: leyendo el bloque destacado
    /// y sacándole los espacios de transcripción.
    /// </summary>
    private string ExtractPasswordFromAct(string actaPath)
    {
        var lines = File.ReadAllLines(actaPath);
        var title = _localization.Get("act_password_title");
        var index = Array.FindIndex(lines, l => l.Contains(title, StringComparison.Ordinal));

        Assert.True(index >= 0, "El acta no tiene el bloque de la contraseña.");
        return lines[index + 2].Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    //  22. Carpeta sincronizada: el CLI aborta salvo --allow-cloud-folder
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Unseal_HaciaCarpetaSincronizada_AbortaSinElFlag()
    {
        var outputDir = await BuildReservedPackageAsync();
        var password = ExtractPasswordFromAct(Path.Combine(_workDir, "acta.txt"));
        var destination = CreateSyncedFolder();

        var command = new UnsealCommand(new SealedPackageOpener(_sealingService, _verificationService), new CloudSyncDetector());

        var code = await command.ExecuteAsync(new CommandParser(new[]
        {
            "unseal", "-i", FindSealed(outputDir), "-o", destination, "-p", password
        }));

        // Un script no puede quedarse esperando una confirmación que nadie va a tipear, así
        // que el CLI aborta con un mensaje claro en vez de preguntar.
        Assert.Equal(1, code);
        Assert.Empty(Directory.GetFiles(destination));
    }

    [Fact]
    public async Task Unseal_HaciaCarpetaSincronizada_ProcedeConElFlag()
    {
        var outputDir = await BuildReservedPackageAsync();
        var password = ExtractPasswordFromAct(Path.Combine(_workDir, "acta.txt"));
        var destination = CreateSyncedFolder();

        var command = new UnsealCommand(new SealedPackageOpener(_sealingService, _verificationService), new CloudSyncDetector());

        var code = await command.ExecuteAsync(new CommandParser(new[]
        {
            "unseal", "-i", FindSealed(outputDir), "-o", destination, "-p", password,
            "--allow-cloud-folder"
        }));

        Assert.Equal(0, code);
        Assert.NotEmpty(Directory.GetFiles(destination));
    }

    /// <summary>
    /// Subcarpeta dentro de una raíz con el marcador que deja Dropbox. El destino real está
    /// varios niveles adentro, que es como se ve en la práctica.
    /// </summary>
    private string CreateSyncedFolder()
    {
        var root = Path.Combine(_workDir, "Dropbox");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".dropbox"), string.Empty);

        var destination = Path.Combine(root, "Casos", "abierto");
        Directory.CreateDirectory(destination);
        return destination;
    }
}
