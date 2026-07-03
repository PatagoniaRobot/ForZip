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

/// <summary>
/// Describe un volumen (segmento) de un archivo dividido estilo 7-Zip (.001, .002, …).
/// El SHA-256 se calcula durante la escritura, sin releer el segmento.
/// </summary>
public sealed class VolumeInfo
{
    /// <summary>Nombre del segmento sin ruta (p. ej. <c>caso.zip.001</c>).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Tamaño del segmento en bytes.</summary>
    public long Size { get; init; }

    /// <summary>SHA-256 del segmento, en minúsculas hex.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>
/// Resultado de una operación de compresión. Contiene los hashes por archivo y,
/// cuando el archivo se dividió en volúmenes, la lista de segmentos generados.
/// </summary>
public sealed class CompressionResult
{
    /// <summary>Hash forense de cada archivo empaquetado (vacío si no se pidieron hashes).</summary>
    public List<HashResult> FileHashes { get; init; } = new();

    /// <summary>Volúmenes generados. Vacío si el archivo no se dividió (archivo único).</summary>
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = Array.Empty<VolumeInfo>();

    /// <summary>Verdadero si la compresión produjo múltiples volúmenes.</summary>
    public bool IsSplit => Volumes.Count > 0;
}
