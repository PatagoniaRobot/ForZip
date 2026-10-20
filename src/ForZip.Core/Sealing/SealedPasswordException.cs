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
/// Falló el tag de autenticación del primer chunk. La causa habitual es una contraseña
/// incorrecta, aunque una alteración del propio chunk 0 produce el mismo síntoma: el
/// cifrado autenticado no permite distinguirlas, y el mensaje no debe afirmar más de lo
/// que se puede probar.
/// </summary>
/// <remarks>
/// No se lleva conteo de intentos ni se bloquea nada: es una herramienta forense de
/// escritorio, no un servicio de autenticación, y un contador solo lograría dejar sin
/// abrir evidencia legítima.
/// </remarks>
public class SealedPasswordException : Exception
{
    public SealedPasswordException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
