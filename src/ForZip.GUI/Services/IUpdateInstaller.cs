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

public sealed record InstallOutcome(bool Success, string? ErrorMessage);

public interface IUpdateInstaller
{
    /// <summary>Nombre del archivo del ejecutable en ejecución (p. ej. <c>ForZip.exe</c>).</summary>
    string CurrentExeName { get; }

    /// <summary>Carpeta donde vive el ejecutable.</summary>
    string InstallDirectory { get; }

    /// <summary>
    /// <c>true</c> si se puede escribir junto al ejecutable. En un pendrive protegido, un CD o
    /// una carpeta sin permisos hay que ofrecer la descarga manual en vez de intentar el
    /// reemplazo y fallar a mitad de camino.
    /// </summary>
    bool CanInstall();

    /// <summary>
    /// Reemplaza el ejecutable en ejecución por <paramref name="newExePath"/> y vuelve a
    /// lanzarlo. Si devuelve éxito, quien llama debe cerrar la aplicación de inmediato.
    /// </summary>
    InstallOutcome ReplaceAndRelaunch(string newExePath);

    /// <summary>
    /// Borra el ejecutable anterior que quedó del reemplazo previo. Se llama al arrancar, cuando
    /// el proceso viejo ya murió y el archivo dejó de estar bloqueado.
    /// </summary>
    void CleanupPreviousVersion();
}
