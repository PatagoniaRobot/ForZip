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

namespace ForZip.Core.Interfaces;

/// <summary>Cómo se llegó a la conclusión de que una ruta está sincronizada.</summary>
public enum CloudSyncEvidence
{
    /// <summary>La carpeta la declara el propio sistema (variable de entorno o cliente instalado).</summary>
    ConfiguredRoot,

    /// <summary>Se encontró el marcador que el cliente deja en la raíz de la carpeta sincronizada.</summary>
    RootMarker
}

/// <summary>Carpeta sincronizada conocida.</summary>
public sealed record CloudSyncRoot(string Provider, string Path);

/// <summary>Resultado de una detección positiva.</summary>
public sealed record CloudSyncMatch(string Provider, string RootPath, CloudSyncEvidence Evidence);

/// <summary>
/// Detecta rutas que caen dentro de carpetas sincronizadas con servicios de nube.
/// </summary>
public interface ICloudSyncDetector
{
    /// <summary>
    /// Devuelve la coincidencia si la ruta parece estar sincronizada, o <c>null</c> si no lo
    /// parece. Que devuelva <c>null</c> no prueba que no lo esté: la detección es heurística.
    /// </summary>
    CloudSyncMatch? Detect(string? path);
}
