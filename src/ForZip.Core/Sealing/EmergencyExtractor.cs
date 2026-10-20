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

using System.Reflection;

namespace ForZip.Core.Sealing;

/// <summary>
/// Copia embebida del extractor autónomo <c>fzs_extract.py</c>, que se incluye dentro del
/// contenedor interno de cada sobre.
/// </summary>
/// <remarks>
/// Es una medida de continuidad: dentro de diez años, quien tenga el sobre y la contraseña
/// tiene también, adentro, las cien líneas de Python que alcanzan para recuperar el contenido
/// aunque ForZip ya no corra. La copia que manda es la del repositorio y la de
/// <c>Documentation/Formato_FZS_v1.md</c>; esta es para el sobre que quedó suelto.
/// </remarks>
public static class EmergencyExtractor
{
    /// <summary>Nombre de la entrada dentro del contenedor interno.</summary>
    public const string FileName = "fzs_extract.py";

    private const string ResourceName = "ForZip.Core.Resources.fzs_extract.py";

    /// <summary>
    /// Fecha de la entrada en el ZIP. Es fija a propósito: el extractor no es evidencia y su
    /// contenido no cambia entre sellados, así que ponerle la hora de ejecución solo agregaría
    /// una variación que después hay que explicar.
    /// </summary>
    public static readonly DateTimeOffset EntryTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Abre el contenido del extractor embebido.
    /// </summary>
    /// <exception cref="InvalidOperationException">El recurso no está en el ensamblado.</exception>
    public static Stream OpenStream()
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso embebido '{ResourceName}'.");
    }
}
