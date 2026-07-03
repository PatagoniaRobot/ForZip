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

namespace ForZip.GUI.Services;

public class OperatorConfirmationResult
{
    public OperatorInfo Operator { get; set; } = new();

    /// <summary>Número de caso/expediente para la cadena de custodia.</summary>
    public string? CaseNumber { get; set; }

    /// <summary>Carátula o descripción del caso.</summary>
    public string? CaseDescription { get; set; }

    /// <summary>Juzgado o dependencia interviniente.</summary>
    public string? Court { get; set; }

    public bool GenerateExternalHash { get; set; }

    /// <summary>Si el usuario pidió firmar digitalmente el manifiesto.</summary>
    public bool SignManifest { get; set; }

    /// <summary>Ruta al certificado PFX/PKCS#12 del operador.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Contraseña del certificado, si la tiene.</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>URL de la TSA (RFC 3161) para sellar la firma; null si no se pidió sello.</summary>
    public string? TimestampUrl { get; set; }
}

public interface IOperatorDialogService
{
    Task<OperatorConfirmationResult?> ConfirmOperatorAsync(OperatorInfo currentInfo, string? defaultTimestampUrl = null);
}
