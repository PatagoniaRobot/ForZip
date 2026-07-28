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

namespace ForZip.Core.Models;

public enum UpdateDownloadStatus
{
    /// <summary>Descargado y con huella coincidente: apto para instalar.</summary>
    Verified,

    /// <summary>No se pudo descargar el binario o su huella.</summary>
    DownloadFailed,

    /// <summary>
    /// La publicación no adjunta el <c>.sha256</c> del binario. Se rechaza la instalación:
    /// reemplazar el ejecutable con algo que no se puede verificar sería ejecutar código
    /// remoto a ciegas, justo lo que una herramienta forense no puede permitirse.
    /// </summary>
    ChecksumMissing,

    /// <summary>La huella del archivo descargado no coincide con la publicada.</summary>
    ChecksumMismatch
}

public sealed class UpdateDownloadResult
{
    public required UpdateDownloadStatus Status { get; init; }

    /// <summary>Ruta del archivo descargado, solo si quedó verificado.</summary>
    public string? FilePath { get; init; }

    public string? ExpectedSha256 { get; init; }

    public string? ActualSha256 { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsVerified => Status == UpdateDownloadStatus.Verified;
}
