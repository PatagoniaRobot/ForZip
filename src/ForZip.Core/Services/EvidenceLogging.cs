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

using ForZip.Core.Interfaces;

namespace ForZip.Core.Services;

/// <summary>
/// Registro de mensajes cuya plantilla termina en un dato derivado de la evidencia (<c>"Destino: {0}"</c>, o el
/// mensaje de una excepción, que suele nombrar el archivo).
/// </summary>
/// <remarks>
/// Con <see cref="ILogService.Redacted"/> activo (perfil Reservado), el dato se descarta y queda solo el texto
/// fijo y, en el lugar del dato, <see cref="ILogService.RedactedMarker"/>; sin redacción la línea sale igual que
/// antes. Formatear la plantilla en el mensaje, como se hacía, dejaba rutas y nombres en la bitácora de una causa
/// reservada.
/// </remarks>
public static class EvidenceLogging
{
    public static void LogEvidence(this ILogService log, LogLevel level, string template, string? evidence)
    {
        if (log.Redacted && !string.IsNullOrEmpty(evidence))
        {
            // Las plantillas que terminan en el dato sin "{0}" ("Extracción incompleta en:") quedaban cortadas en el
            // aire; la marca va al final, para que se lea que algo se omitió a propósito.
            var redacted = template.Contains("{0}", StringComparison.Ordinal)
                ? template.Replace("{0}", log.RedactedMarker, StringComparison.Ordinal)
                : template.TrimEnd() + " " + log.RedactedMarker;
            log.Log(level, redacted.TrimEnd());
            return;
        }

        var summary = template.Replace("{0}", string.Empty, StringComparison.Ordinal).TrimEnd();
        log.Log(level, summary, evidence);
    }
}
