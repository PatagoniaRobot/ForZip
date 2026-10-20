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

namespace ForZip.Core.Sealing;

/// <summary>
/// Parámetros del sellado de un paquete completo.
/// </summary>
public sealed class SealedPackageOptions
{
    /// <summary>
    /// Artefactos que van adentro del sobre: el ZIP (o sus volúmenes), el manifiesto y la
    /// firma <c>.p7s</c> si la hay. El sellador no los interpreta: <c>forzip seal</c> sobre un
    /// paquete de antes de 2.0 encierra también su informe y su <c>.sha256</c>.
    /// </summary>
    public IReadOnlyList<string> ArtifactPaths { get; set; } = Array.Empty<string>();

    /// <summary>Carpeta donde quedan el sobre y el recibo.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Contraseña del paquete. Una distinta por paquete, siempre.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Certificado PFX del operador para firmar el recibo. Opcional.</summary>
    public string? SigningCertificatePath { get; set; }

    /// <summary>Contraseña del PFX, si la tiene.</summary>
    public string? SigningCertificatePassword { get; set; }

    /// <summary>URL de la TSA RFC 3161 para sellar la firma del recibo. Opcional.</summary>
    public string? TimestampUrl { get; set; }
}

/// <summary>
/// Artefactos que produjo el sellado de un paquete en perfil Reservado.
/// </summary>
public sealed class SealedPackageResult
{
    /// <summary>Ruta del sobre. <b>Es lo único que se sube a la nube.</b></summary>
    public string SealedPath { get; set; } = string.Empty;

    /// <summary>Ruta del recibo público. Es lo que va al expediente.</summary>
    public string ReceiptPath { get; set; } = string.Empty;

    /// <summary>Ruta de la firma del recibo, si se firmó.</summary>
    public string? ReceiptSignaturePath { get; set; }

    /// <summary>
    /// Ruta de la copia del abridor HTML que se dejó junto al sobre, para quien lo recibe.
    /// <c>null</c> si no se pudo escribir: el sobre igual quedó sellado y se abre con ForZip.
    /// </summary>
    public string? OpenerPagePath { get; set; }

    /// <summary>Header del sobre emitido.</summary>
    public SealedHeader Header { get; set; } = null!;

    /// <summary>Recibo emitido.</summary>
    public Receipt Receipt { get; set; } = null!;

    /// <summary>
    /// Aviso de que la firma quedó sin sello de tiempo porque la TSA no respondió. La firma
    /// sí se generó: es una advertencia, no un error, igual que en 1.2.0.
    /// </summary>
    public string? TimestampWarning { get; set; }

    /// <summary>
    /// Directorio temporal que no se pudo eliminar. Hay que mostrárselo al operador: adentro
    /// quedó una copia del paquete en claro.
    /// </summary>
    public string? UndeletedTempPath { get; set; }
}
