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

namespace ForZip.GUI.ViewModels;

/// <summary>
/// Una observación de la extracción, para la tarjeta de Extraer: qué pasó (<paramref name="Title"/>), con qué
/// archivo (<paramref name="Detail"/>) y, si hace falta, qué puede hacer el operador (<paramref name="Advice"/>).
/// Es lo mismo que queda en la bitácora, puesto donde el operador está mirando.
/// </summary>
public sealed record ExtractionObservation(string Title, string Detail, string Advice = "", bool IsError = false)
{
    public bool HasAdvice => Advice.Length > 0;
}
