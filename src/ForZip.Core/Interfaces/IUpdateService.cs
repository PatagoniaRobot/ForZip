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

public interface IUpdateService
{
    /// <summary>
    /// Consulta cuál es la última versión publicada. Nunca lanza por problemas de red: un fallo
    /// se informa como <see cref="UpdateCheckStatus.Failed"/>, porque no poder averiguar si hay
    /// una versión nueva jamás debe interrumpir el trabajo en curso.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct);

    /// <summary>
    /// Descarga <paramref name="asset"/> a <paramref name="destinationPath"/> y verifica su
    /// SHA-256 contra <paramref name="checksumAsset"/>.
    /// <para/>
    /// Si no hay <c>.sha256</c> publicado, o si la huella no coincide, el archivo descargado se
    /// descarta y el resultado NO queda verificado: quien llame no debe instalar nada salvo que
    /// <see cref="UpdateDownloadResult.IsVerified"/> sea verdadero.
    /// </summary>
    Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateAsset asset,
        UpdateAsset? checksumAsset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct);

    /// <summary>Página de publicaciones, como destino de respaldo si no hay una URL puntual.</summary>
    string ReleasesPageUrl { get; }
}
