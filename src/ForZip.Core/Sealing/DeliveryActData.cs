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

namespace ForZip.Core.Sealing;

/// <summary>
/// Datos que componen el acta de entrega de clave.
/// </summary>
/// <remarks>
/// Es el único lugar de todo el perfil Reservado donde la contraseña y los datos de la causa
/// conviven, y es deliberado: el acta es papel, se firma en mano y no sale a la red. La
/// contraseña que viaja acá <b>no</b> puede terminar en el log, en <c>config.json</c>, en el
/// informe ni en el recibo.
/// </remarks>
public sealed class DeliveryActData
{
    /// <summary>Identificador aleatorio del paquete.</summary>
    public Guid PackageId { get; set; }

    /// <summary>Nombre del archivo sellado que se entrega.</summary>
    public string SealedFileName { get; set; } = string.Empty;

    /// <summary>SHA-256 del sobre, para que el receptor coteje lo que le llegó contra el papel.</summary>
    public string SealedSha256 { get; set; } = string.Empty;

    /// <summary>Fecha de sellado en UTC.</summary>
    public DateTimeOffset SealedAtUtc { get; set; }

    /// <summary>Contraseña del paquete, generada y distinta por paquete.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Número de causa.</summary>
    public string? CaseNumber { get; set; }

    /// <summary>Carátula o descripción de la causa.</summary>
    public string? CaseDescription { get; set; }

    /// <summary>Dependencia judicial destinataria.</summary>
    public string? Court { get; set; }

    /// <summary>Operador que sella.</summary>
    public OperatorInfo? Operator { get; set; }

    /// <summary>Versión de ForZip que produjo el sobre.</summary>
    public string ForZipVersion { get; set; } = AppInfo.DisplayVersion;
}
