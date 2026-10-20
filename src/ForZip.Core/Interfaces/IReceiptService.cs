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

using ForZip.Core.Sealing;

namespace ForZip.Core.Interfaces;

/// <summary>
/// Emisión y lectura del recibo público que acompaña a un sobre sellado.
/// </summary>
public interface IReceiptService
{
    /// <summary>
    /// Arma el recibo de un sobre ya escrito en disco: calcula su SHA-256 y su tamaño y los
    /// combina con los datos del header.
    /// </summary>
    Task<Receipt> CreateAsync(
        string sealedPath,
        SealedHeader header,
        IProgress<double>? progress,
        CancellationToken ct);

    /// <summary>
    /// Escribe el recibo en disco (UTF-8 sin BOM) y devuelve la ruta escrita.
    /// </summary>
    Task<string> WriteAsync(Receipt receipt, string receiptPath, CancellationToken ct);

    /// <summary>Lee un recibo de disco.</summary>
    /// <exception cref="SealedFormatException">El recibo está malformado o lleva campos ajenos.</exception>
    Task<Receipt> ReadAsync(string receiptPath, CancellationToken ct);

    /// <summary>
    /// Firma el recibo con el certificado del operador, delegando en el servicio de firma
    /// existente. La firma cubre la totalidad de los bytes del archivo.
    /// </summary>
    Task SignAsync(string receiptPath, string pfxPath, string? pfxPassword, string? tsaUrl, CancellationToken ct);

    /// <summary>Ruta del recibo que corresponde a un sobre: <c>&lt;base&gt;.receipt.json</c>.</summary>
    string GetReceiptPathFor(string sealedPath);

    /// <summary>Ruta del sobre que corresponde a un recibo: <c>&lt;base&gt;.fzs</c>.</summary>
    string GetSealedPathFor(string receiptPath);
}
