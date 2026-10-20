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
/// Toma un paquete forense ya generado —el ZIP y todos sus sidecar— y lo convierte en el
/// par de artefactos del perfil Reservado: el sobre opaco y el recibo público.
/// </summary>
/// <remarks>
/// Es el paso 2 del perfil: el paso 1 (comprimir, hashear, informar, firmar) no cambia y lo
/// siguen haciendo los servicios de siempre. Esta separación es la que permite que un
/// paquete Estándar y uno Reservado compartan exactamente el mismo código de empaquetado.
/// </remarks>
public interface ISealedPackageBuilder
{
    /// <summary>
    /// Sella los artefactos indicados y emite el recibo. Devuelve las rutas resultantes.
    /// </summary>
    /// <exception cref="IOException">No hay espacio suficiente para el sobre.</exception>
    Task<SealedPackageResult> BuildAsync(
        SealedPackageOptions options,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);
}
