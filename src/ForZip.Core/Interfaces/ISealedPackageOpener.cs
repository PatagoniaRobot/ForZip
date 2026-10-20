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

using ForZip.Core.Models;
using ForZip.Core.Sealing;

namespace ForZip.Core.Interfaces;

/// <summary>
/// Abre un sobre <c>.fzs</c> hacia una carpeta elegida por el operador y verifica su contenido
/// contra el manifiesto que trae adentro.
/// </summary>
public interface ISealedPackageOpener
{
    /// <summary>Lee el header en claro del sobre, sin contraseña: identificador y fecha.</summary>
    Task<SealedHeader> ReadHeaderAsync(string sealedPath, CancellationToken ct);

    /// <summary>
    /// Descifra el sobre, extrae el contenedor interno en <paramref name="outputDirectory"/> y
    /// verifica el manifiesto. La carpeta tiene que no existir o estar vacía: abrir un sobre
    /// nunca sobrescribe archivos.
    /// </summary>
    /// <exception cref="SealedPasswordException">Contraseña incorrecta.</exception>
    /// <exception cref="SealedIntegrityException">Un bloque no autentica: sobre alterado.</exception>
    /// <exception cref="SealedFormatException">No es un sobre válido o su contenido es ilegible.</exception>
    /// <exception cref="IOException">El destino ya tiene archivos.</exception>
    Task<SealedOpenResult> OpenAsync(
        string sealedPath,
        string outputDirectory,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);
}

/// <summary>Resultado de abrir un sobre.</summary>
public sealed class SealedOpenResult
{
    public SealedHeader Header { get; init; } = null!;

    /// <summary>Carpeta donde quedó el contenido, en claro.</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>Archivos extraídos, con su ruta completa.</summary>
    public IReadOnlyList<string> ExtractedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Veredicto contra el manifiesto que venía adentro. <c>null</c> si el sobre no traía
    /// manifiesto (se selló con <c>seal</c> sobre archivos sueltos) o si no pudo verificarse.
    /// </summary>
    public ArchiveVerificationResult? Verification { get; init; }

    /// <summary>Verdadero si el sobre no traía un manifiesto forense contra el cual verificar.</summary>
    public bool HasNoManifest { get; init; }

    /// <summary>Motivo por el que la verificación no pudo completarse, si fue el caso.</summary>
    public string? VerificationError { get; init; }

    /// <summary>
    /// Temporal que no se pudo borrar. Contiene el paquete en claro: hay que avisarle al
    /// operador para que lo borre a mano.
    /// </summary>
    public string? UndeletedTempPath { get; init; }
}
