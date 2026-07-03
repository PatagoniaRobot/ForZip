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

using System.Text.RegularExpressions;

namespace ForZip.Core.Services;

/// <summary>
/// Convenciones de nomenclatura y descubrimiento para archivos divididos en volúmenes
/// estilo 7-Zip: <c>base.zip.001</c>, <c>base.zip.002</c>, … La numeración empieza en 1,
/// con al menos 3 dígitos; si se superan 999 volúmenes, el número crece (p. ej. <c>.1000</c>),
/// igual que 7-Zip.
/// </summary>
public static partial class SplitArchive
{
    [GeneratedRegex(@"\.(\d{3,})$")]
    private static partial Regex VolumeSuffixRegex();

    /// <summary>Devuelve el nombre del volumen 1-based para una ruta/base dada.</summary>
    public static string GetVolumePath(string basePath, int oneBasedIndex)
    {
        if (oneBasedIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex), "El índice de volumen empieza en 1.");
        }

        return $"{basePath}.{oneBasedIndex:D3}";
    }

    /// <summary>Indica si la ruta tiene el sufijo de un volumen (<c>.001</c>, <c>.002</c>, …).</summary>
    public static bool IsVolumePath(string path) => VolumeSuffixRegex().IsMatch(path);

    /// <summary>Indica si la ruta es el primer volumen (<c>.001</c>).</summary>
    public static bool IsFirstVolume(string path)
    {
        var m = VolumeSuffixRegex().Match(path);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n == 1;
    }

    /// <summary>
    /// Quita el sufijo de volumen para recuperar la ruta lógica del archivo
    /// (<c>caso.zip.001</c> → <c>caso.zip</c>). Si no hay sufijo, devuelve la ruta tal cual.
    /// </summary>
    public static string GetBasePath(string volumePath)
    {
        var m = VolumeSuffixRegex().Match(volumePath);
        return m.Success ? volumePath[..m.Index] : volumePath;
    }

    /// <summary>
    /// Enumera, en orden, los volúmenes existentes de un archivo lógico, empezando por
    /// <c>.001</c> y deteniéndose en el primer hueco. Devuelve lista vacía si no existe
    /// el primer volumen.
    /// </summary>
    public static IReadOnlyList<string> EnumerateExisting(string basePath)
    {
        var volumes = new List<string>();
        for (int i = 1; ; i++)
        {
            var path = GetVolumePath(basePath, i);
            if (!File.Exists(path))
            {
                break;
            }
            volumes.Add(path);
        }
        return volumes;
    }

    /// <summary>
    /// Resuelve la entrada de un archivo (posiblemente dividido) a la lista ordenada de
    /// archivos físicos a leer:
    /// <list type="bullet">
    /// <item>Si <paramref name="path"/> es un volumen (<c>.001</c>), recoge todos los segmentos.</item>
    /// <item>Si <paramref name="path"/> es la ruta lógica y existe como archivo único, la devuelve.</item>
    /// <item>Si <paramref name="path"/> es lógica y no existe pero sí su <c>.001</c>, recoge los segmentos.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> ResolveSegments(string path)
    {
        if (IsVolumePath(path))
        {
            var basePath = GetBasePath(path);
            var segments = EnumerateExisting(basePath);
            return segments.Count > 0 ? segments : new[] { path };
        }

        if (File.Exists(path))
        {
            return new[] { path };
        }

        // La ruta lógica no existe: ¿se trata de un archivo dividido?
        var split = EnumerateExisting(path);
        if (split.Count > 0)
        {
            return split;
        }

        // No existe nada: devolver la ruta original para que el llamador reporte "no encontrado".
        return new[] { path };
    }
}
