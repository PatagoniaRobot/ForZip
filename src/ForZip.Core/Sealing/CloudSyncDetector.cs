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

namespace ForZip.Core.Sealing;

/// <summary>
/// Detecta si una ruta cae dentro de una carpeta sincronizada con un servicio de nube.
/// </summary>
/// <remarks>
/// El escenario de fuga más verosímil de todo el perfil Reservado no es criptográfico: es
/// que alguien abra el sobre y extraiga el contenido dentro de una carpeta sincronizada con
/// Drive, OneDrive o Dropbox. El material sube solo, en claro, con nombres y todo, sin que
/// nadie tome una decisión y sin dejar señal hasta que ya ocurrió.
/// <para>
/// La detección es <b>heurística y puede fallar en los dos sentidos</b>: hay clientes que no
/// dejan marcador y carpetas con marcador que ya no se sincronizan. Ante la duda hay que
/// avisar: un falso positivo cuesta un clic y un falso negativo cuesta la evidencia.
/// </para>
/// </remarks>
public class CloudSyncDetector : ICloudSyncDetector
{
    /// <summary>
    /// Marcadores que los clientes dejan en la raíz de la carpeta sincronizada. Se buscan
    /// hacia arriba desde la ruta consultada, así que alcanzan también para carpetas
    /// sincronizadas que el operador movió de lugar.
    /// </summary>
    private static readonly (string Provider, string Marker)[] RootMarkers =
    {
        // OneDrive deja este archivo oculto con el CLSID de su carpeta raíz.
        ("OneDrive", ".849C9593-D756-4E56-8D6E-42412F2A707B"),
        ("Dropbox", ".dropbox"),
        ("Dropbox", ".dropbox.cache"),
        ("Google Drive", ".shortcut-targets-by-id"),
        ("Google Drive", ".tmp.drivedownload"),
        ("Google Drive", ".tmp.driveupload")
    };

    private readonly IReadOnlyList<CloudSyncRoot> _knownRoots;

    /// <summary>
    /// Crea el detector. Sin argumentos descubre las carpetas sincronizadas del equipo; con
    /// una lista explícita usa solo esas, que es como lo ejercitan los tests.
    /// </summary>
    public CloudSyncDetector(IReadOnlyList<CloudSyncRoot>? knownRoots = null)
    {
        _knownRoots = knownRoots ?? DiscoverRoots();
    }

    /// <inheritdoc />
    public CloudSyncMatch? Detect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        foreach (var root in _knownRoots)
        {
            if (IsInside(full, root.Path))
            {
                return new CloudSyncMatch(root.Provider, root.Path, CloudSyncEvidence.ConfiguredRoot);
            }
        }

        // Se camina hacia arriba: el marcador vive en la raíz de la carpeta sincronizada, no
        // en cada subcarpeta.
        var current = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(current))
        {
            foreach (var (provider, marker) in RootMarkers)
            {
                var candidate = Path.Combine(current, marker);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return new CloudSyncMatch(provider, current, CloudSyncEvidence.RootMarker);
                }
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>
    /// Carpetas sincronizadas declaradas por el propio sistema: las variables de entorno que
    /// publica OneDrive y las raíces de los clientes de Dropbox y Google Drive.
    /// </summary>
    private static List<CloudSyncRoot> DiscoverRoots()
    {
        var roots = new List<CloudSyncRoot>();

        foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                roots.Add(new CloudSyncRoot("OneDrive", Path.GetFullPath(value)));
            }
        }

        AddIfExists(roots, "Dropbox", Environment.GetEnvironmentVariable("USERPROFILE"), "Dropbox");
        AddIfExists(roots, "Google Drive", Environment.GetEnvironmentVariable("USERPROFILE"), "Google Drive");
        AddIfExists(roots, "Google Drive", Environment.GetEnvironmentVariable("USERPROFILE"), "My Drive");

        return roots;
    }

    private static void AddIfExists(List<CloudSyncRoot> roots, string provider, string? baseDir, string folder)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return;
        }

        var candidate = Path.Combine(baseDir, folder);
        if (Directory.Exists(candidate))
        {
            roots.Add(new CloudSyncRoot(provider, Path.GetFullPath(candidate)));
        }
    }

    /// <summary>
    /// Comprueba contención real por segmentos de ruta: <c>C:\Datos2</c> no está adentro de
    /// <c>C:\Datos</c>, aunque el texto empiece igual.
    /// </summary>
    private static bool IsInside(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
