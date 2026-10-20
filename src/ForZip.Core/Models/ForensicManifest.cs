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
/// Manifiesto forense legible por máquina (JSON). Es la fuente de verdad para la
/// verificación automática de evidencia: documenta cada archivo empaquetado, sus
/// hashes y metadatos, además del hash global del ZIP. Complementa al informe .txt
/// (orientado a humanos), que es frágil de parsear por su formato de columnas fijas.
/// </summary>
public class ForensicManifest
{
    /// <summary>Versión del esquema del manifiesto, para compatibilidad futura.</summary>
    public string FormatVersion { get; set; } = "1.1";

    public string ForZipVersion { get; set; } = AppInfo.DisplayVersion;

    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public OperatorInfo? Operator { get; set; }
    public string? CaseNumber { get; set; }
    public string? CaseDescription { get; set; }
    public string? Court { get; set; }

    /// <summary>
    /// Equipo, sistema, usuario y hora local del empaquetado (esquema 1.4). Antes solo estaban en el informe; desde
    /// que el informe se arma a pedido, el manifiesto es lo único que los conserva. Null en manifiestos anteriores.
    /// </summary>
    public PackagingEnvironment? Environment { get; set; }

    public OperationType Operation { get; set; }
    public int CompressionLevel { get; set; }
    public bool HasPassword { get; set; }

    public List<HashAlgorithmType> Algorithms { get; set; } = new();

    /// <summary>Nombre del archivo ZIP (sin ruta) al que pertenece este manifiesto.</summary>
    /// <summary>
    /// "7z" cuando el paquete es un 7z (esquema 1.3). Ausente en un ZIP, que sale idéntico al de antes:
    /// los campos Zip* nombran al paquete cualquiera sea su formato.
    /// </summary>
    public string? ArchiveFormat { get; set; }

    public string? ZipFileName { get; set; }
    public long? ZipFileSize { get; set; }

    /// <summary>
    /// SHA-256 del ZIP <b>lógico</b> (la concatenación de todos los volúmenes si está dividido).
    /// Es la base de la verificación de contenido y no cambia entre archivo único y dividido.
    /// </summary>
    public string? ZipSha256 { get; set; }

    /// <summary>Verdadero si el archivo se dividió en volúmenes (.001, .002, …).</summary>
    public bool IsSplit { get; set; }

    /// <summary>
    /// Volúmenes que componen el archivo, en orden, con su hash individual. <c>null</c> o vacío
    /// para archivos únicos. Permite verificar la integridad de cada segmento por separado.
    /// </summary>
    public List<ManifestVolume>? Volumes { get; set; }

    public List<ManifestFileEntry> Files { get; set; } = new();

    /// <summary>
    /// Archivos seleccionados que no están en el paquete, cada uno con su motivo. <c>null</c>
    /// cuando el paquete está completo. La verificación no los reclama
    /// como faltantes: su ausencia está declarada, no es una alteración.
    /// </summary>
    public List<ManifestOmittedFile>? OmittedFiles { get; set; }
}

/// <summary>Un archivo declarado como no incluido, dentro del manifiesto.</summary>
public class ManifestOmittedFile
{
    public string EntryName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }

    /// <summary>
    /// Motivo normalizado: <c>security_software_blocked</c>, <c>missing_at_packaging</c>
    /// (estaba al seleccionar, no al empaquetar), <c>vanished_during_packaging</c> o
    /// <c>link_not_followed</c> (enlace o junction dentro del origen, con su destino en <see cref="LinkTarget"/>),
    /// <c>unreadable</c> (inventario de hashes) o <c>folder_unreadable</c> (carpeta que Windows no dejó recorrer;
    /// el nombre termina en <c>/</c> y el código de Windows dice por qué).
    /// </summary>
    public string Reason { get; set; } = "security_software_blocked";

    public string? LinkTarget { get; set; }

    public int? WindowsErrorCode { get; set; }

    public string? SystemMessage { get; set; }

    public DateTimeOffset? SeenAtUtc { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>Un volumen (segmento) del archivo dividido, dentro del manifiesto.</summary>
public class ManifestVolume
{
    /// <summary>Nombre del segmento sin ruta (p. ej. <c>caso.zip.001</c>).</summary>
    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>SHA-256 del segmento, en minúsculas hex.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>Una entrada de archivo dentro del manifiesto.</summary>
public class ManifestFileEntry
{
    /// <summary>Nombre de la entrada dentro del ZIP (separador POSIX).</summary>
    public string EntryName { get; set; } = string.Empty;

    /// <summary>Ruta absoluta original (cadena de custodia).</summary>
    public string? SourcePath { get; set; }

    public long Size { get; set; }

    public DateTimeOffset? ModifiedUtc { get; set; }

    public Dictionary<HashAlgorithmType, string> Hashes { get; set; } = new();
}
