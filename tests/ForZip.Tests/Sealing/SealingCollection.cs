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

using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Colección que agrupa los tests del sobre sellado y los ejecuta en serie.
/// </summary>
/// <remarks>
/// Varios de estos tests comprueban que no queden directorios temporales tras la operación, y
/// para eso miran el temp del usuario, que es compartido. En paralelo, el temporal legítimo de
/// una clase aparece como basura de otra: el fallo sería del reloj, no del código.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class SealingCollection
{
    public const string Name = "Sobre sellado";
}
