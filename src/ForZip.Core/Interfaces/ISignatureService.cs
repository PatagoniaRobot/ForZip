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

using ForZip.Core.Models;

namespace ForZip.Core.Interfaces;

/// <summary>
/// Firma y verifica manifiestos forenses con una firma CMS/PKCS#7 desacoplada
/// (archivo <c>.p7s</c> junto al manifiesto), usando un certificado X.509 del operador.
/// </summary>
public interface ISignatureService
{
    /// <summary>Indica si existe un archivo de firma (.p7s) junto al manifiesto.</summary>
    bool IsSignaturePresent(string manifestPath);

    /// <summary>
    /// Firma el manifiesto con el certificado del archivo PFX/PKCS#12 indicado y
    /// escribe la firma desacoplada en <c>&lt;manifiesto&gt;.p7s</c>.
    /// </summary>
    Task SignAsync(string manifestPath, string pfxPath, string? pfxPassword, CancellationToken ct);

    /// <summary>
    /// Igual que <see cref="SignAsync(string, string, string?, CancellationToken)"/>, pero si
    /// <paramref name="tsaUrl"/> no es nulo además solicita un sello de tiempo RFC 3161 a esa
    /// TSA y lo incrusta en la firma (atributo no firmado id-aa-timeStampToken). Si la TSA no
    /// responde, la firma igualmente se escribe y se lanza
    /// <see cref="Services.TimestampUnavailableException"/> para informarlo.
    /// </summary>
    Task SignAsync(string manifestPath, string pfxPath, string? pfxPassword, string? tsaUrl, CancellationToken ct);

    /// <summary>
    /// Verifica la firma del manifiesto (si existe). La validez confirma que el
    /// manifiesto no cambió desde la firma; la confianza en la identidad del firmante
    /// depende de validar su certificado por fuera (no se valida la cadena a una CA).
    /// Si la firma incluye un sello de tiempo RFC 3161, se valida y reporta también.
    /// </summary>
    SignatureInfo Verify(string manifestPath);
}
