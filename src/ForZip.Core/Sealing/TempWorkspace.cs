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

namespace ForZip.Core.Sealing;

/// <summary>
/// Directorio temporal de un solo uso para el contenedor interno y para la extracción de
/// verificación.
/// </summary>
/// <remarks>
/// Vive bajo el temp del usuario con un nombre aleatorio, <b>nunca</b> junto al archivo de
/// salida ni en la carpeta de la evidencia, y se elimina siempre —también ante excepción o
/// cancelación— porque <see cref="Dispose"/> corre en el <c>finally</c> del <c>using</c>.
/// Si el sistema no lo deja borrar, la ruta queda en <see cref="UndeletedPath"/> para
/// avisarle al operador: fallar en silencio dejaría evidencia en claro en el disco.
/// </remarks>
public sealed class TempWorkspace : IDisposable
{
    private const string FolderPrefix = "ForZip_";

    private TempWorkspace(string path)
    {
        Path = path;
    }

    /// <summary>Ruta del directorio temporal.</summary>
    public string Path { get; }

    /// <summary>
    /// Ruta que no se pudo eliminar, o <c>null</c> si la limpieza fue correcta. Se consulta
    /// después de liberar el objeto.
    /// </summary>
    public string? UndeletedPath { get; private set; }

    /// <summary>Crea un directorio temporal aleatorio bajo el temp del usuario.</summary>
    public static TempWorkspace Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            FolderPrefix + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return new TempWorkspace(path);
    }

    /// <summary>Ruta de un archivo dentro del directorio temporal.</summary>
    public string Combine(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }

            UndeletedPath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No se propaga: la excepción que llevó hasta acá importa más que la limpieza.
            // Queda la ruta para que la capa de presentación la muestre.
            UndeletedPath = Path;
        }
    }
}
