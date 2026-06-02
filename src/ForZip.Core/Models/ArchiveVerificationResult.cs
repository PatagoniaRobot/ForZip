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

/// <summary>Veredicto de verificación para una entrada individual del archivo.</summary>
public enum FileVerificationStatus
{
    /// <summary>El hash recalculado coincide con el registrado en el manifiesto.</summary>
    Ok,

    /// <summary>El hash recalculado NO coincide: el contenido fue alterado.</summary>
    Altered,

    /// <summary>El archivo estaba en el manifiesto pero falta en el ZIP.</summary>
    Missing,

    /// <summary>El archivo está en el ZIP pero no figura en el manifiesto.</summary>
    Extra
}

/// <summary>Resultado de verificar una entrada concreta contra el manifiesto.</summary>
public sealed record FileVerificationEntry(
    string EntryName,
    FileVerificationStatus Status,
    string? ExpectedHash = null,
    string? ActualHash = null);

/// <summary>Estado de la firma digital (CMS/PKCS#7) de un manifiesto.</summary>
public sealed record SignatureInfo(
    bool Present,
    bool Valid,
    string? SignerSubject,
    DateTimeOffset? SignedAtUtc,
    string Details);

/// <summary>
/// Resultado completo de re-hashear un ZIP y contrastarlo contra su manifiesto forense.
/// </summary>
public sealed class ArchiveVerificationResult
{
    public List<FileVerificationEntry> Entries { get; } = new();

    /// <summary>Verdadero si el hash SHA-256 global del ZIP coincide (cuando hay dato).</summary>
    public bool? ZipHashMatches { get; set; }

    /// <summary>Estado de la firma digital del manifiesto, si existe un archivo .p7s.</summary>
    public SignatureInfo? Signature { get; set; }

    public int OkCount => Entries.Count(e => e.Status == FileVerificationStatus.Ok);
    public int AlteredCount => Entries.Count(e => e.Status == FileVerificationStatus.Altered);
    public int MissingCount => Entries.Count(e => e.Status == FileVerificationStatus.Missing);
    public int ExtraCount => Entries.Count(e => e.Status == FileVerificationStatus.Extra);

    /// <summary>
    /// La evidencia es íntegra si no hay archivos alterados, faltantes ni añadidos,
    /// el hash global del ZIP coincide (si se conoce) y la firma digital del
    /// manifiesto es válida (si existe).
    /// </summary>
    public bool IsIntact =>
        AlteredCount == 0 &&
        MissingCount == 0 &&
        ExtraCount == 0 &&
        ZipHashMatches != false &&
        (Signature == null || Signature.Valid);
}
