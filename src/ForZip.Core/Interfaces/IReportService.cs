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

namespace ForZip.Core.Interfaces;

public interface IReportService
{
    string GenerateReport(ReportData data, string language);

    /// <summary>
    /// El mismo informe como página HTML imprimible, para que lo abra con doble clic quien no lee texto de columnas
    /// fijas. Es una presentación del manifiesto: no se firma ni se verifica. No lleva scripts ni recursos externos.
    /// </summary>
    string GenerateReportHtml(ReportData data, string language);

    /// <summary>
    /// El informe en el formato que pide la extensión de <paramref name="path"/>: HTML para <c>.html</c> o
    /// <c>.htm</c>, texto para cualquier otra.
    /// </summary>
    string GenerateReportFor(string path, ReportData data, string language);

    /// <summary>
    /// Genera el manifiesto forense en formato JSON (legible por máquina), fuente de
    /// verdad para la verificación automática de la evidencia.
    /// </summary>
    string GenerateManifestJson(ReportData data);

    /// <summary>
    /// Los datos del informe a partir de un manifiesto, para armarlo a pedido después de empaquetar. El entorno es el
    /// que registró el manifiesto (null si es anterior al esquema 1.4), nunca el de la PC donde se arma.
    /// </summary>
    ReportData FromManifest(ForensicManifest manifest, string manifestFileName);

    Task SaveReportAsync(string content, string outputPath);

    /// <summary>
    /// Verifica la integridad de un informe contrastando su contenido con el hash
    /// SHA-256 almacenado en el archivo externo <c>&lt;informe&gt;.sha256</c>.
    /// </summary>
    (bool isValid, string details) VerifyReport(string reportPath);

    /// <summary>
    /// Como <see cref="VerifyReport"/>, pero distingue un informe alterado de uno que no se puede verificar (sin
    /// <c>.sha256</c>, o ilegible): lo segundo no es una alteración y no se presenta como tal.
    /// </summary>
    (ReportIntegrity status, string details) CheckReport(string reportPath);
}

/// <summary>Resultado de verificar un informe contra su <c>.sha256</c>.</summary>
public enum ReportIntegrity
{
    Valid,
    Altered,
    Unverifiable
}
