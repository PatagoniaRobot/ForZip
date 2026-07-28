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

using System.Globalization;

namespace ForZip.Core.Services;

/// <summary>
/// Comparación de versiones de publicación. Las etiquetas del repositorio son del estilo
/// <c>v1.3.0</c>, mientras que <see cref="AppInfo.Version"/> es <c>1.3.0</c>: acá se normaliza
/// una y otra forma para poder compararlas numéricamente y no como texto (donde "1.10.0"
/// resultaría anterior a "1.9.0").
/// </summary>
public static class VersionComparer
{
    /// <summary>
    /// Interpreta una versión con o sin la <c>v</c> inicial. Los sufijos de precompilación o
    /// metadatos (<c>1.4.0-beta.1</c>, <c>1.4.0+build7</c>) se descartan: la comparación es
    /// sobre los componentes numéricos.
    /// </summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();

        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            value = value[1..];
        }

        var suffix = value.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0)
        {
            value = value[..suffix];
        }

        var parts = value.Split('.');
        if (parts.Length == 0 || parts.Length > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            // NumberStyles.None rechaza signos, espacios y separadores de miles.
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            numbers[i] = number;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>
    /// <c>true</c> solo si ambas versiones son interpretables y <paramref name="candidate"/>
    /// es estrictamente posterior. Ante cualquier duda devuelve <c>false</c>: es preferible no
    /// anunciar una actualización que anunciar una inexistente.
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        return TryParse(candidate, out var candidateVersion)
            && TryParse(current, out var currentVersion)
            && candidateVersion > currentVersion;
    }
}
