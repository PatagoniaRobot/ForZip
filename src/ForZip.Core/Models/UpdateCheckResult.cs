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

public enum UpdateCheckStatus
{
    /// <summary>La versión en uso es la última publicada (o es posterior, en una compilación propia).</summary>
    UpToDate,

    /// <summary>Hay una publicación con una versión superior a la que está corriendo.</summary>
    UpdateAvailable,

    /// <summary>No se pudo consultar: sin conexión, bloqueado por la red, respuesta inesperada.</summary>
    Failed
}

/// <summary>Un archivo adjunto a la publicación (el ejecutable, o su <c>.sha256</c>).</summary>
public sealed class UpdateAsset
{
    public required string Name { get; init; }
    public required string DownloadUrl { get; init; }
    public long Size { get; init; }
}

/// <summary>
/// Resultado de consultar la última publicación de ForZip. Nunca dispara la instalación por su
/// cuenta: descargar y reemplazar el binario es siempre una acción que el operador pide, porque
/// el informe declara con qué versión se generó cada evidencia y muchos laboratorios trabajan
/// con versiones validadas y congeladas.
/// </summary>
public sealed class UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }

    /// <summary>Versión en ejecución al momento de consultar.</summary>
    public required string CurrentVersion { get; init; }

    /// <summary>Versión publicada, sin la <c>v</c> inicial. <c>null</c> si la consulta falló.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>Notas de la publicación, tal como vienen del repositorio (texto markdown).</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>Página de la publicación, para abrirla en el navegador.</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>Detalle del fallo cuando <see cref="Status"/> es <see cref="UpdateCheckStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Archivos adjuntos a la publicación.</summary>
    public IReadOnlyList<UpdateAsset> Assets { get; init; } = Array.Empty<UpdateAsset>();

    /// <summary>
    /// Busca el adjunto que corresponde a un ejecutable dado. Se compara por nombre exacto
    /// (sin distinguir mayúsculas) contra el nombre del archivo en ejecución: así una copia
    /// llamada <c>ForZip.exe</c> nunca se actualiza con el binario de la CLI, y una publicación
    /// que renombró sus adjuntos simplemente no ofrece actualización en vez de instalar
    /// cualquier cosa.
    /// </summary>
    public UpdateAsset? FindAsset(string fileName)
        => Assets.FirstOrDefault(a => string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Busca el <c>.sha256</c> que acompaña a un adjunto.</summary>
    public UpdateAsset? FindChecksumFor(string fileName)
        => FindAsset(fileName + ".sha256");
}
