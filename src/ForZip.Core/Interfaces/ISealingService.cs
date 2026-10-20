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

using ForZip.Core.Sealing;

namespace ForZip.Core.Interfaces;

/// <summary>
/// Sellado y apertura del contenedor <c>.fzs</c>: cifra una carga útil arbitraria como un
/// único blob opaco y la devuelve idéntica al abrirla.
/// </summary>
/// <remarks>
/// El servicio trabaja sobre bytes y no sabe nada de evidencia, causas ni manifiestos: la
/// carga útil que recibe ya viene armada. Esa separación es lo que permite auditar el
/// formato por separado de la lógica forense.
/// </remarks>
public interface ISealingService
{
    /// <summary>
    /// Sella la carga útil sobre el flujo de destino y devuelve el header emitido.
    /// </summary>
    /// <param name="payload">Carga útil en claro. Debe permitir posicionamiento: el tamaño va en el header.</param>
    /// <param name="destination">Flujo donde se escribe el sobre.</param>
    /// <param name="password">Contraseña del paquete. Una distinta por paquete, siempre.</param>
    Task<SealedHeader> SealAsync(
        Stream payload,
        Stream destination,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);

    /// <summary>
    /// Abre un sobre y vuelca la carga útil en claro sobre el flujo de destino.
    /// </summary>
    /// <exception cref="SealedFormatException">El archivo no es un sobre válido o está truncado.</exception>
    /// <exception cref="SealedPasswordException">Falló el primer chunk: contraseña incorrecta.</exception>
    /// <exception cref="SealedIntegrityException">Falló un chunk posterior: el sobre fue alterado.</exception>
    Task<SealedHeader> UnsealAsync(
        Stream sealedInput,
        Stream destination,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);

    /// <summary>
    /// Sella un archivo. Si la operación falla o se cancela, el sobre parcial se elimina:
    /// nunca queda un <c>.fzs</c> incompleto en disco.
    /// </summary>
    Task<SealedHeader> SealFileAsync(
        string payloadPath,
        string sealedPath,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);

    /// <summary>
    /// Abre un sobre y escribe la carga útil en el archivo indicado. Si falla o se cancela,
    /// la salida parcial se elimina.
    /// </summary>
    Task<SealedHeader> UnsealFileAsync(
        string sealedPath,
        string payloadPath,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);

    /// <summary>
    /// Lee el header del sobre sin descifrar nada y sin necesidad de contraseña. Es la base
    /// de la verificación pública de nivel 1.
    /// </summary>
    Task<SealedHeader> ReadHeaderAsync(Stream sealedInput, CancellationToken ct);

    /// <summary>
    /// Lee el header del sobre indicado sin descifrar nada y sin necesidad de contraseña.
    /// </summary>
    Task<SealedHeader> ReadHeaderAsync(string sealedPath, CancellationToken ct);
}
