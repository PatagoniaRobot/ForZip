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

public class ReportData
{
    public OperatorInfo? Operator { get; set; }
    public string? CaseNumber { get; set; }
    public string? CaseDescription { get; set; }
    public string? Court { get; set; }
    public OperationType Operation { get; set; }
    public int CompressionLevel { get; set; }
    public bool HasPassword { get; set; }
    public HashSet<HashAlgorithmType> Algorithms { get; set; } = new();
    public string? ZipFilePath { get; set; }

    /// <summary>Formato del paquete al que se refieren ZipFilePath, ZipFileSize y ZipHash.</summary>
    public ArchiveFormat ArchiveFormat { get; set; } = ArchiveFormat.Zip;
    public long? ZipFileSize { get; set; }
    public string? ZipHash { get; set; }
    public List<HashResult> FileResults { get; set; } = new();

    /// <summary>Volúmenes del archivo dividido (vacío para archivo único).</summary>
    public List<VolumeInfo> Volumes { get; set; } = new();

    /// <summary>Archivos que el software de seguridad no dejó leer (vacío si el paquete está completo).</summary>
    public List<OmittedFile> OmittedFiles { get; set; } = new();

    /// <summary>Huella de cada carpeta hasheada entera (Hash Batch, hash rápido, <c>forzip hash</c>).</summary>
    public List<Services.FolderDigest> FolderDigests { get; set; } = new();

    public string ForZipVersion { get; set; } = AppInfo.DisplayVersion;

    /// <summary>
    /// Equipo, sistema, usuario y hora del empaquetado. Se toma al crear los datos; armado desde un manifiesto es el
    /// que este registró, o null si es anterior al esquema 1.4 (el informe dice "no registrado", no inventa).
    /// </summary>
    public PackagingEnvironment? Environment { get; set; } = PackagingEnvironment.Capture();

    /// <summary>
    /// Nombre del manifiesto del que salió este informe, si se armó a pedido después de empaquetar. Null en el
    /// informe que se arma con los datos recién empaquetados.
    /// </summary>
    public string? SourceManifestName { get; set; }

    /// <summary>La verificación hecha antes de armar el informe, si se armó desde Verificar.</summary>
    public ReportVerification? Verification { get; set; }
}
