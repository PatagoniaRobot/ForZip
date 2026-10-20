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

using System.Runtime.CompilerServices;
using PrZip.Rar;

namespace ForZip.Core.Services;

/// <summary>
/// Formatos del motor que no vienen en su núcleo. RAR vive en un ensamblado aparte con la licencia de unrar
/// (que permite leer RAR en cualquier software, no crearlo) y hay que sumarlo: se hace al cargar ForZip.Core,
/// antes de que cualquier servicio abra un archivo, tanto en la GUI como en la CLI.
/// </summary>
internal static class EngineFormats
{
#pragma warning disable CA2255 // Inicialización de módulo a propósito: vale para todo lo que use ForZip.Core.
    [ModuleInitializer]
    internal static void Register() => RarSupport.Register();
#pragma warning restore CA2255
}
