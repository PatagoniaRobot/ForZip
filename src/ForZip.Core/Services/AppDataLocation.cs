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

namespace ForZip.Core.Services;

/// <summary>
/// Dónde guarda ForZip su configuración y su bitácora: al lado del ejecutable si esa carpeta admite escritura (modo
/// portable, como hasta ahora), o en <c>%LOCALAPPDATA%\ForZip</c> si no.
/// </summary>
/// <remarks>
/// Antes se escribía siempre al lado del ejecutable, sin manejar el error: desde Program Files o un pendrive
/// protegido contra escritura, ForZip no arrancaba.
/// </remarks>
public static class AppDataLocation
{
    private static readonly Lazy<string> Resolved = new(Resolve);

    /// <summary>Carpeta de datos de esta ejecución.</summary>
    public static string Directory => Resolved.Value;

    /// <summary>Carpeta del ejecutable (donde puede haber un config.json de solo lectura).</summary>
    public static string ExecutableDirectory => AppContext.BaseDirectory;

    private static string Resolve()
    {
        var baseDir = AppContext.BaseDirectory;
        if (IsWritable(baseDir))
        {
            return baseDir;
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForZip");
        try
        {
            System.IO.Directory.CreateDirectory(local);
            return local;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return baseDir;
        }
    }

    /// <summary>Prueba real de escritura: los permisos declarados no siempre coinciden con lo que el medio deja.</summary>
    public static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".forzip-write-probe-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
