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
/// Lectura de archivos de huella externa en el formato habitual de <c>sha256sum</c>
/// (<c>&lt;hash&gt;␠␠&lt;archivo&gt;</c>). Se usa tanto para los sidecar de los informes forenses
/// como para verificar el binario descargado por el actualizador.
/// </summary>
public static class Sha256Sidecar
{
    private const int Sha256HexLength = 64;

    /// <summary>
    /// Extrae el primer SHA-256 (64 dígitos hexadecimales) que aparezca en el texto, en
    /// minúsculas. Tolera el formato con o sin nombre de archivo y con saltos de línea de
    /// cualquier plataforma. Devuelve <c>null</c> si no hay ninguno.
    /// </summary>
    public static string? Extract(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var separators = new[] { ' ', '\t', '\r', '\n', '*' };
        foreach (var token in content.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == Sha256HexLength && token.All(Uri.IsHexDigit))
            {
                return token.ToLowerInvariant();
            }
        }

        return null;
    }
}
