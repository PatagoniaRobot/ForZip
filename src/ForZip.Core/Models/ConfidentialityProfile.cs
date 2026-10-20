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

namespace ForZip.Core.Models;

/// <summary>
/// Perfil de confidencialidad del paquete. Es un eje <b>ortogonal</b> al interruptor
/// Simple/Avanzado: aquel decide cuántos controles ve el operador, este decide qué
/// artefactos se producen y qué información queda expuesta. Por eso vive en un campo
/// propio de la configuración y nunca se mezcla con <c>SimpleMode</c>.
/// </summary>
public enum ConfidentialityProfile
{
    /// <summary>
    /// Comportamiento histórico de ForZip: ZIP cifrado más los sidecar en claro
    /// (informe, manifiesto, <c>.sha256</c> y firma). Los nombres de archivo y la
    /// estructura de carpetas quedan legibles, que es propiedad del formato ZIP.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// El paquete completo —ZIP y todos sus sidecar— queda encerrado en un sobre
    /// sellado <c>.fzs</c>, opaco. Hacia afuera solo salen el sobre y un recibo público
    /// que prueba integridad y autoría sin revelar nada del contenido.
    /// </summary>
    Reserved = 1
}
