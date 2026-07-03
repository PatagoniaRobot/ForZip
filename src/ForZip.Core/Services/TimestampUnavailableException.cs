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

namespace ForZip.Core.Services;

/// <summary>
/// El sellado de tiempo RFC 3161 no pudo obtenerse (TSA inaccesible o respuesta inválida).
/// Cuando <see cref="SignatureService.SignAsync(string, string, string?, string?, CancellationToken)"/>
/// lanza esta excepción, la firma CMS <b>sí</b> quedó escrita en disco: solo falta el sello.
/// </summary>
public class TimestampUnavailableException : Exception
{
    public TimestampUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
