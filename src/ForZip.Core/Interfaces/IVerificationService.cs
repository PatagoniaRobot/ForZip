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

public interface IVerificationService
{
    /// <summary>Deserializa un manifiesto forense desde su contenido JSON.</summary>
    ForensicManifest ParseManifest(string json);

    /// <summary>
    /// Re-hashea el contenido de un ZIP y lo contrasta contra el manifiesto forense,
    /// produciendo un veredicto archivo por archivo (OK / alterado / faltante / añadido)
    /// y, si está disponible, la verificación del hash global del ZIP.
    /// </summary>
    /// <param name="manifestPath">Ruta al archivo .manifest.json.</param>
    /// <param name="zipPathOverride">Ruta al ZIP; si es nula, se resuelve junto al manifiesto.</param>
    /// <param name="password">Contraseña del ZIP, si está cifrado.</param>
    Task<ArchiveVerificationResult> VerifyArchiveAsync(
        string manifestPath,
        string? zipPathOverride,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);
}
