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

using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Tests del control de fugas colaterales del perfil Reservado (§8). De nada sirve sellar el
/// paquete si el nombre de la evidencia queda escrito en la bitácora, en la configuración o
/// en un temporal que nadie borró.
/// </summary>
[Collection(SealingCollection.Name)]
public class LeakControlTests : IDisposable
{
    private const string EvidenceName = "Peritaje-4471-material.zip";
    private const string EvidencePath = @"D:\Causas\4471-26\Secuestro\Peritaje-4471-material.zip";

    private readonly string _workDir;

    public LeakControlTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_leaks_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
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
    //  16. La bitácora no registra nombres ni rutas en perfil Reservado
    // -------------------------------------------------------------------------

    [Fact]
    public void LogService_EnModoRedaccion_NoRegistraNombresNiRutas()
    {
        ILogService log = new LogService { Redacted = true };

        log.Info("Sellado iniciado. 342 elementos.", $"Salida: {EvidencePath}");
        log.Success("Sellado completado. Paquete FZS-8f3a1c9e.", $"Archivo: {EvidenceName}");
        log.Warning("No se pudo borrar un artefacto ya sellado:", EvidencePath);

        foreach (var entry in log.Entries)
        {
            Assert.DoesNotContain(EvidenceName, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(EvidencePath, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("4471", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(@"D:\", entry.Message, StringComparison.Ordinal);
        }

        // Lo que sí queda es la operación y el identificador del paquete, que no revelan nada.
        Assert.Contains(log.Entries, e => e.Message.Contains("342 elementos", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.Contains("FZS-8f3a1c9e", StringComparison.Ordinal));
    }

    [Fact]
    public void LogService_SinRedaccion_ConservaElDetalle()
    {
        // El perfil Estándar no cambia: la bitácora sigue diciendo lo de siempre.
        ILogService log = new LogService { Redacted = false };

        log.Info("Empaquetado iniciado.", $"Salida: {EvidenceName}");

        Assert.Contains(EvidenceName, log.Entries[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LogService_ModoRedaccion_DescartaElDetalleEnteroYNoLoRecorta()
    {
        // Enmascarar parcialmente sería peor que no hacer nada: una ruta a medias sigue
        // diciendo en qué causa se está trabajando.
        ILogService log = new LogService { Redacted = true };

        log.Info("Manifiesto forense generado.", $"Archivo: {EvidenceName}");

        Assert.Equal("Manifiesto forense generado.", log.Entries[0].Message);
    }

    // -------------------------------------------------------------------------
    //  17. La configuración no guarda rutas del paquete
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigService_TrasGuardarElPerfil_NoEscribeRutasDeLaEvidencia()
    {
        var configDir = Path.Combine(_workDir, "config");
        Directory.CreateDirectory(configDir);
        var service = new ConfigService(configDir);

        var config = service.Load();
        config.Profile = ConfidentialityProfile.Reserved;
        service.Save(config);

        var raw = File.ReadAllText(Path.Combine(configDir, "config.json"));

        // El perfil sí se guarda (como valor del enum) y vuelve a leerse.
        Assert.Equal(ConfidentialityProfile.Reserved, service.Load().Profile);
        Assert.DoesNotContain(EvidenceName, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4471", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(@"D:\Causas", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppConfig_ElAvisoDelPerfilEstandar_NoSePersiste()
    {
        var configDir = Path.Combine(_workDir, "config-aviso");
        Directory.CreateDirectory(configDir);
        var service = new ConfigService(configDir);

        var config = service.Load();
        config.StandardProfileNoticeShown = true;
        service.Save(config);

        var raw = File.ReadAllText(Path.Combine(configDir, "config.json"));

        // Es de sesión: quien cambia de máquina tiene que volver a ver la advertencia.
        Assert.DoesNotContain("StandardProfileNoticeShown", raw, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.Load().StandardProfileNoticeShown);
    }

    // -------------------------------------------------------------------------
    //  18. Los temporales se eliminan aunque la operación falle
    // -------------------------------------------------------------------------

    [Fact]
    public void TempWorkspace_AnteExcepcion_SeElimina()
    {
        string path;

        var workspace = TempWorkspace.Create();
        path = workspace.Path;
        Assert.True(Directory.Exists(path));

        try
        {
            File.WriteAllText(workspace.Combine("payload.bin"), "contenido en claro");
            throw new InvalidOperationException("falla forzada a mitad de la operación");
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            workspace.Dispose();
        }

        Assert.False(Directory.Exists(path));
        Assert.Null(workspace.UndeletedPath);
    }

    [Fact]
    public async Task SealedPackageBuilder_SiFallaAMitad_NoDejaTemporalesNiSobreParcial()
    {
        var artifactPath = Path.Combine(_workDir, "evidencia.zip");
        await File.WriteAllTextAsync(artifactPath, new string('x', 4096));

        var outputDir = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(outputDir);

        var builder = new SealedPackageBuilder(
            new SealingService(1000),
            new ReceiptService(new HashService(), new SignatureService()));

        var temporalesAntes = ContarTemporales();

        // El artefacto queda bloqueado para lectura: el armado del contenedor interno falla a
        // mitad de camino, que es justo el momento en que un temporal quedaría huérfano.
        await using (new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => builder.BuildAsync(
                new SealedPackageOptions
                {
                    ArtifactPaths = new[] { artifactPath },
                    OutputDirectory = outputDir,
                    Password = "clave-de-prueba-del-sobre-2026"
                },
                null,
                CancellationToken.None));
        }

        Assert.Equal(temporalesAntes, ContarTemporales());
        Assert.Empty(Directory.GetFiles(outputDir));
    }

    // -------------------------------------------------------------------------
    //  19 y 20. El identificador es aleatorio y el nombre no sale de la causa
    // -------------------------------------------------------------------------

    [Fact]
    public void SealedHeader_MilSellados_ProducenMilIdentificadoresDistintos()
    {
        const int Sellados = 1000;
        var vistos = new HashSet<Guid>();

        for (var i = 0; i < Sellados; i++)
        {
            // Misma entrada, mismos parámetros: lo único que cambia es el azar.
            var header = SealedHeader.Create(4096, 1000, SealedContainerFormat.MinChunkSize, DateTimeOffset.UtcNow);
            Assert.True(vistos.Add(header.PackageId), $"Identificador repetido en el sellado {i}.");
        }

        Assert.Equal(Sellados, vistos.Count);
    }

    [Fact]
    public void BuildBaseName_NoContieneNadaDeLaCausaNiDelOperador()
    {
        var header = SealedHeader.Create(4096, 1000, SealedContainerFormat.MinChunkSize, DateTimeOffset.UtcNow);
        var name = header.BaseName + SealedContainerFormat.SealedExtension;

        Assert.StartsWith("FZS-", name, StringComparison.Ordinal);
        Assert.Equal("FZS-".Length + SealedContainerFormat.FileNameIdLength + ".fzs".Length, name.Length);

        foreach (var prohibido in new[] { "4471", "Peritaje", "Andino", "material", "Causas" })
        {
            Assert.DoesNotContain(prohibido, name, StringComparison.OrdinalIgnoreCase);
        }

        // El sufijo son los primeros 8 hexadecimales del identificador, y nada más.
        var suffix = header.BaseName["FZS-".Length..];
        Assert.Equal(header.PackageId.ToString("N")[..SealedContainerFormat.FileNameIdLength], suffix);
    }

    private static int ContarTemporales() =>
        Directory.GetDirectories(Path.GetTempPath(), "ForZip_*").Length;
}
