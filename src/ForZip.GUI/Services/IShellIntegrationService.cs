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

namespace ForZip.GUI.Services;

/// <summary>
/// Gestiona la integración portable con el menú contextual del Explorador de Windows,
/// escribiendo/borrando claves bajo <c>HKCU</c> (sin admin, reversible, sin instalador).
/// </summary>
public interface IShellIntegrationService
{
    /// <summary>Verdadero solo en Windows; en otras plataformas la integración es no-op.</summary>
    bool IsSupported { get; }

    /// <summary>Indica si el menú contextual de ForZip está actualmente registrado.</summary>
    bool IsRegistered();

    /// <summary>
    /// Ruta del ejecutable con la que se registró el menú, o <c>null</c> si no hay registro.
    /// Permite detectar que el .exe se movió (las entradas apuntarían a una ruta obsoleta).
    /// </summary>
    string? GetRegisteredPath();

    /// <summary>Ruta del ejecutable actual de ForZip.</summary>
    string CurrentExePath { get; }

    /// <summary>Registra el menú contextual apuntando al ejecutable actual.</summary>
    void Register();

    /// <summary>Elimina por completo el menú contextual de ForZip.</summary>
    void Unregister();

    /// <summary>
    /// Indica si la oferta de integración de primer uso ya se mostró en esta PC
    /// (la marca vive en HKCU, por equipo y usuario — no viaja con el ejecutable portable).
    /// </summary>
    bool WasOfferShown();

    /// <summary>Marca que la oferta de primer uso ya se mostró en esta PC.</summary>
    void MarkOfferShown();
}
