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
using System.Security.Cryptography;

namespace ForZip.Core.Sealing;

/// <summary>
/// Copia embebida del abridor <c>Open_FZS.html</c>, que ForZip deja junto a cada
/// sobre para quien lo recibe.
/// </summary>
/// <remarks>
/// Resuelve la entrega, no la continuidad: quien recibe un <c>.fzs</c> lo abre con doble clic
/// en Edge o Chrome, sin instalar ForZip ni Python. Es una copia genérica, idéntica para todos
/// los sobres, y su SHA-256 va en el acta para que el receptor compruebe que es la original.
/// </remarks>
public static class EnvelopeOpenerPage
{
    /// <summary>Nombre con el que se deja junto al sobre.</summary>
    public const string FileName = "Open_FZS.html";

    private const string ResourceName = "ForZip.Core.Resources.Open_FZS.html";

    private static readonly Lazy<byte[]> Content = new(ReadContent);

    /// <summary>SHA-256 de la página, en hexadecimal minúscula.</summary>
    public static string Sha256 => Convert.ToHexString(SHA256.HashData(Content.Value)).ToLowerInvariant();

    /// <summary>Abre el contenido de la página embebida.</summary>
    public static Stream OpenStream() => new MemoryStream(Content.Value, writable: false);

    /// <summary>
    /// Deja una copia de la página en <paramref name="directory"/> y devuelve su ruta. Si ya hay
    /// un archivo con ese nombre se reemplaza: es una copia genérica, y una que no coincide con
    /// la de esta versión —vieja o tocada— no debe quedar junto a un sobre nuevo.
    /// </summary>
    public static async Task<string> WriteToAsync(string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, FileName);
        await File.WriteAllBytesAsync(path, Content.Value, ct).ConfigureAwait(false);
        return path;
    }

    private static byte[] ReadContent()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso embebido '{ResourceName}'.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
