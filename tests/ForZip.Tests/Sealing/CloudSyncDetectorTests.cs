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
using ForZip.Core.Sealing;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Tests del detector de carpetas sincronizadas (§8.5). El escenario de fuga más verosímil
/// del perfil Reservado no es criptográfico: es que alguien extraiga el contenido dentro de
/// una carpeta que sube sola a la nube.
/// </summary>
public class CloudSyncDetectorTests : IDisposable
{
    private readonly string _workDir;

    public CloudSyncDetectorTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_cloud_{Guid.NewGuid():N}");
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

    private CloudSyncDetector CreateDetector(params CloudSyncRoot[] roots) => new(roots);

    /// <summary>Crea una carpeta con el marcador que deja el cliente indicado en su raíz.</summary>
    private string CreateSyncedRoot(string folder, string marker)
    {
        var root = Path.Combine(_workDir, folder);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, marker), string.Empty);
        return root;
    }

    // -------------------------------------------------------------------------
    //  21. Reconoce los marcadores y no marca rutas normales
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("OneDrive", ".849C9593-D756-4E56-8D6E-42412F2A707B")]
    [InlineData("Dropbox", ".dropbox")]
    [InlineData("Google Drive", ".shortcut-targets-by-id")]
    public void Detect_CarpetaConMarcador_LaReconoce(string provider, string marker)
    {
        var root = CreateSyncedRoot(provider.Replace(" ", string.Empty), marker);
        var detector = CreateDetector();

        var match = detector.Detect(root);

        Assert.NotNull(match);
        Assert.Equal(provider, match!.Provider);
        Assert.Equal(CloudSyncEvidence.RootMarker, match.Evidence);
    }

    [Fact]
    public void Detect_SubcarpetaProfunda_TambienLaReconoce()
    {
        // El marcador vive en la raíz de la carpeta sincronizada, no en cada subcarpeta: hay
        // que caminar hacia arriba o la evidencia se extrae tres niveles adentro sin aviso.
        var root = CreateSyncedRoot("Dropbox", ".dropbox");
        var deep = Path.Combine(root, "Casos", "2026", "4471", "salida");
        Directory.CreateDirectory(deep);

        var match = CreateDetector().Detect(deep);

        Assert.NotNull(match);
        Assert.Equal("Dropbox", match!.Provider);
        Assert.Equal(root, match.RootPath);
    }

    [Fact]
    public void Detect_RutaNormal_NoLaMarca()
    {
        var normal = Path.Combine(_workDir, "Trabajo", "Pericias");
        Directory.CreateDirectory(normal);

        Assert.Null(CreateDetector().Detect(normal));
    }

    [Fact]
    public void Detect_CarpetaDeclaradaPorElSistema_LaReconoce()
    {
        var root = Path.Combine(_workDir, "OneDrive - Organismo");
        Directory.CreateDirectory(root);
        var inside = Path.Combine(root, "Evidencia");
        Directory.CreateDirectory(inside);

        var detector = CreateDetector(new CloudSyncRoot("OneDrive", root));

        var match = detector.Detect(inside);

        Assert.NotNull(match);
        Assert.Equal(CloudSyncEvidence.ConfiguredRoot, match!.Evidence);
    }

    [Fact]
    public void Detect_NombreQueEmpiezaIgual_NoCuentaComoAdentro()
    {
        // "C:\Datos2" no está dentro de "C:\Datos", aunque el texto empiece igual. Comparar
        // por prefijo de cadena en vez de por segmentos daría un falso positivo acá.
        var root = Path.Combine(_workDir, "Datos");
        var vecino = Path.Combine(_workDir, "Datos2");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(vecino);

        var detector = CreateDetector(new CloudSyncRoot("Dropbox", root));

        Assert.NotNull(detector.Detect(root));
        Assert.Null(detector.Detect(vecino));
    }

    [Fact]
    public void Detect_RutaVaciaOInvalida_DevuelveNull()
    {
        var detector = CreateDetector();

        Assert.Null(detector.Detect(null));
        Assert.Null(detector.Detect(string.Empty));
        Assert.Null(detector.Detect("   "));
    }

    [Fact]
    public void Detect_ArchivoDentroDeCarpetaSincronizada_LoReconocePorSuCarpeta()
    {
        var root = CreateSyncedRoot("Drive", ".tmp.driveupload");
        var file = Path.Combine(root, "FZS-8f3a1c9e.fzs");
        File.WriteAllText(file, "sobre");

        var match = CreateDetector().Detect(file);

        Assert.NotNull(match);
        Assert.Equal("Google Drive", match!.Provider);
    }
}
