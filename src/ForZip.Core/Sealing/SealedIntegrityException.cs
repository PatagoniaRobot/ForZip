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
/// Falló el tag de autenticación de un chunk posterior al primero: el sobre <b>fue
/// alterado</b> después de sellarse.
/// </summary>
/// <remarks>
/// La distinción con <see cref="SealedPasswordException"/> es deliberada y sigue el §5 de
/// la especificación: si falla el chunk 0, la causa más probable es la contraseña; si los
/// primeros chunks validan y uno posterior falla, la clave era correcta y lo que cambió es
/// el archivo. Se informa la posición para que el perito pueda ubicar la alteración.
/// </remarks>
public class SealedIntegrityException : Exception
{
    public SealedIntegrityException(long chunkIndex, string message, Exception? inner = null)
        : base(message, inner)
    {
        ChunkIndex = chunkIndex;
    }

    /// <summary>Índice del chunk cuyo tag no validó, base 0.</summary>
    public long ChunkIndex { get; }
}
