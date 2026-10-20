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
/// Verificación de un sobre sellado en dos niveles.
/// </summary>
/// <remarks>
/// El nivel <b>público</b> no necesita contraseña y responde la pregunta que se lleva al
/// expediente: si el sobre es el mismo que se selló y quién lo firmó. El nivel
/// <b>completo</b> agrega el veredicto por archivo, y para eso sí hace falta la clave.
/// </remarks>
public interface ISealedVerificationService
{
    /// <summary>
    /// Verifica un sobre. Acepta indistintamente la ruta de un <c>.fzs</c> o la de un
    /// <c>.receipt.json</c> y resuelve la contraparte por nombre.
    /// </summary>
    /// <param name="password">
    /// Contraseña del paquete. Si es nula se ejecuta solo el nivel público; si se indica, se
    /// descifra el sobre y se verifica el manifiesto interno.
    /// </param>
    /// <param name="packagePassword">
    /// Contraseña del paquete interior, solo si se cifró con otra que la del sobre (un paquete cifrado sellado a
    /// mano). Nula: se usa la del sobre, que es lo que hace el perfil Reservado.
    /// </param>
    Task<SealedVerificationResult> VerifyAsync(
        string inputPath,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct,
        string? packagePassword = null);
}
