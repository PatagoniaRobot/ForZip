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

    /// <summary>
    /// Archivos seleccionados que no entraron al paquete, cada uno con su motivo. Vacío cuando
    /// el paquete está completo.
    /// </summary>
    public List<OmittedFile> OmittedFiles { get; init; } = new();
}

/// <summary>Por qué un archivo seleccionado quedó fuera del paquete.</summary>
public enum OmissionReason
{
    /// <summary>Windows, a pedido del antivirus o del EDR, negó su lectura (225/226).</summary>
    SecuritySoftwareBlocked,

    /// <summary>Estaba cuando el operador seleccionó la carpeta y ya no estaba al empaquetar.</summary>
    MissingAtPackaging,

    /// <summary>Se encontró al recorrer el origen y desapareció antes de poder abrirlo.</summary>
    VanishedDuringPackaging,

    /// <summary>
    /// Enlace simbólico o junction dentro del origen: no se sigue, porque lleva a algo que está fuera de
    /// lo seleccionado (o a un bucle). Se documenta con su destino.
    /// </summary>
    LinkNotFollowed,

    /// <summary>
    /// No se pudo leer al calcular su hash (inventario de hashes: Hash Batch, hash rápido, <c>forzip hash</c>). El
    /// código y el mensaje de Windows dicen por qué; ERROR_VIRUS_INFECTED es el antivirus.
    /// </summary>
    Unreadable,

    /// <summary>
    /// Una carpeta del origen que Windows no dejó recorrer (por ejemplo <c>System Volume Information</c> en la raíz
    /// de una unidad). Lo que tenga adentro no se conoce; el resto del origen se empaqueta igual.
    /// </summary>
    FolderUnreadable
}

/// <summary>
/// Un archivo seleccionado que quedó fuera del paquete. No tiene hash: nadie pudo leer sus
/// bytes. Se registra todo lo que sí se conoce para que la omisión quede documentada y el
/// original, si todavía existe, pueda adquirirse después en condiciones controladas.
/// </summary>
public sealed class OmittedFile
{
    public OmissionReason Reason { get; init; } = OmissionReason.SecuritySoftwareBlocked;

    /// <summary>Nombre que habría tenido dentro del ZIP (separador POSIX).</summary>
    public string EntryName { get; init; } = string.Empty;

    /// <summary>Ruta absoluta del original, que ForZip no modificó.</summary>
    public string SourcePath { get; init; } = string.Empty;

    public long Size { get; init; }

    public DateTimeOffset ModifiedUtc { get; init; }

    /// <summary>
    /// Código de Windows, cuando hubo uno: 225 o 226 si lo bloqueó el software de seguridad,
    /// 2 o 3 si el archivo ya no existía al abrirlo. <c>null</c> si la ausencia se constató
    /// sin intentar abrirlo.
    /// </summary>
    public int? WindowsErrorCode { get; init; }

    /// <summary>Texto con el que Windows informó el error, tal cual, cuando lo hubo.</summary>
    public string? SystemMessage { get; init; }

    /// <summary>Adónde apunta, si es un enlace no seguido.</summary>
    public string? LinkTarget { get; init; }

    /// <summary>Cuándo el operador lo vio al seleccionar, si la selección quedó registrada.</summary>
    public DateTimeOffset? SeenAtUtc { get; init; }

    /// <summary>Momento en que ForZip constató la omisión.</summary>
    public DateTimeOffset RecordedAtUtc { get; init; }
}
