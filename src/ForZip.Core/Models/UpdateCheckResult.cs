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

/// <summary>
/// Resultado de consultar la última publicación de ForZip. Es solo informativo: la descarga y
/// la instalación quedan en manos del operador. Una herramienta forense no debe reemplazar su
/// propio binario sin que la persona lo decida, porque el informe declara con qué versión se
/// generó cada evidencia y muchos laboratorios trabajan con versiones validadas y congeladas.
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
}
