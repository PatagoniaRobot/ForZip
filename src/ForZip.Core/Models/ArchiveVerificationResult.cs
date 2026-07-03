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
/// <remarks>
/// <c>SignedAtUtc</c> es el atributo signingTime declarado por el firmante (no oponible).
/// <c>TimestampUtc</c>/<c>TimestampValid</c> corresponden al sello de tiempo RFC 3161 emitido
/// por una TSA externa, si la firma lo incluye: esa fecha sí es verificable por terceros.
/// </remarks>
public sealed record SignatureInfo(
    bool Present,
    bool Valid,
    string? SignerSubject,
    DateTimeOffset? SignedAtUtc,
    string Details,
    DateTimeOffset? TimestampUtc = null,
    string? TimestampAuthority = null,
    bool? TimestampValid = null);

/// <summary>Veredicto de verificación de un volumen (segmento) de un archivo dividido.</summary>
public sealed record VolumeVerificationEntry(
    string FileName,
    FileVerificationStatus Status,
    string? ExpectedHash = null,
    string? ActualHash = null);

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

    /// <summary>
    /// Veredicto por volumen, cuando el archivo está dividido. <c>null</c> para archivo único.
    /// </summary>
    public List<VolumeVerificationEntry>? Volumes { get; set; }

    /// <summary>
    /// Si la verificación de contenido (apertura del ZIP, re-hash por archivo, hash global) no
    /// pudo completarse —p. ej. falta la contraseña o el archivo está corrupto—, contiene el
    /// motivo. La verificación por volumen sí se conserva, porque no requiere descomprimir.
    /// </summary>
    public string? ContentVerificationError { get; set; }

    public int OkCount => Entries.Count(e => e.Status == FileVerificationStatus.Ok);
    public int AlteredCount => Entries.Count(e => e.Status == FileVerificationStatus.Altered);
    public int MissingCount => Entries.Count(e => e.Status == FileVerificationStatus.Missing);
    public int ExtraCount => Entries.Count(e => e.Status == FileVerificationStatus.Extra);

    /// <summary>Verdadero si algún volumen está alterado o faltante.</summary>
    public bool HasVolumeProblems =>
        Volumes != null && Volumes.Any(v => v.Status != FileVerificationStatus.Ok);

    /// <summary>
    /// La evidencia es íntegra si la verificación de contenido se completó sin error, no hay
    /// archivos alterados, faltantes ni añadidos, todos los volúmenes coinciden (si está
    /// dividida), el hash global del ZIP coincide (si se conoce) y la firma digital del
    /// manifiesto es válida (si existe), incluido su sello de tiempo RFC 3161 (si lo tiene).
    /// </summary>
    public bool IsIntact =>
        ContentVerificationError == null &&
        AlteredCount == 0 &&
        MissingCount == 0 &&
        ExtraCount == 0 &&
        !HasVolumeProblems &&
        ZipHashMatches != false &&
        (Signature == null || (Signature.Valid && Signature.TimestampValid != false));
}
