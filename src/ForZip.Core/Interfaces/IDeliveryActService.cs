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
/// Acta de entrega de clave: el papel que acompaña la entrega presencial, bajo recibo y
/// firma, de la contraseña de un paquete sellado.
/// </summary>
/// <remarks>
/// <b>Regla que la capa de presentación debe respetar:</b> el acta nunca se guarda
/// automáticamente junto al <c>.fzs</c>. La opción por defecto es imprimirla; guardarla en
/// disco exige que el operador elija la ruta y acepte la advertencia correspondiente.
/// </remarks>
public interface IDeliveryActService
{
    /// <summary>
    /// Genera el texto del acta, listo para imprimir (monoespaciado, 80 columnas).
    /// </summary>
    string Generate(DeliveryActData data, string language);

    /// <summary>
    /// Escribe el acta en la ruta indicada, en UTF-8 con BOM, igual que el informe forense.
    /// Solo debe llamarse cuando el operador eligió la ruta explícitamente.
    /// </summary>
    Task SaveAsync(string content, string outputPath);

    /// <summary>
    /// Agrupa la contraseña en bloques de cuatro caracteres separados por espacios, para que
    /// se pueda transcribir sin errores. Los espacios no forman parte de la clave.
    /// </summary>
    string FormatPasswordForTranscription(string password);

    /// <summary>Nombre de archivo sugerido para el acta: <c>FZS-&lt;id&gt;.acta.txt</c>.</summary>
    string GetSuggestedFileName(Guid packageId);
}
