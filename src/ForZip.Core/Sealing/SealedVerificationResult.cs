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

namespace ForZip.Core.Sealing;

/// <summary>Nivel de verificación que efectivamente se pudo ejecutar.</summary>
public enum SealedVerificationLevel
{
    /// <summary>Ni siquiera se pudo leer el sobre como tal.</summary>
    None,

    /// <summary>
    /// Público: formato, huella, tamaño, identificador y firma. Sin contraseña y sin acceso
    /// al contenido.
    /// </summary>
    Public,

    /// <summary>
    /// Completo: lo anterior más el descifrado del sobre y el veredicto por archivo contra
    /// el manifiesto interno.
    /// </summary>
    Full
}

/// <summary>
/// Resultado de verificar un sobre sellado, en sus dos niveles.
/// </summary>
/// <remarks>
/// Los <c>bool?</c> distinguen las tres situaciones que importan en sede judicial: la
/// comprobación dio bien, dio mal, o no se pudo hacer. Colapsarlas en un <c>bool</c> haría
/// que "no verificado" se leyera como "verificado y correcto".
/// </remarks>
public sealed class SealedVerificationResult
{
    /// <summary>Nivel efectivamente alcanzado.</summary>
    public SealedVerificationLevel Level { get; set; } = SealedVerificationLevel.None;

    /// <summary>Ruta del sobre verificado.</summary>
    public string SealedPath { get; set; } = string.Empty;

    /// <summary>Verdadero si la marca, la versión de formato y el header son válidos.</summary>
    public bool FormatValid { get; set; }

    /// <summary>Motivo por el que el archivo no es un sobre legible, si corresponde.</summary>
    public string? FormatError { get; set; }

    /// <summary>Identificador declarado por el sobre.</summary>
    public Guid? PackageId { get; set; }

    /// <summary>Fecha de sellado declarada por el sobre.</summary>
    public DateTimeOffset? SealedAtUtc { get; set; }

    /// <summary>SHA-256 recalculado sobre el <c>.fzs</c> completo.</summary>
    public string? SealedSha256 { get; set; }

    /// <summary>Tamaño real del <c>.fzs</c> en disco.</summary>
    public long SealedSize { get; set; }

    /// <summary>Tamaño de la carga útil que declara el header (autenticado recién al descifrar).</summary>
    public long PayloadSize { get; set; }

    /// <summary>
    /// Verdadero si se encontró el recibo. Sin recibo la verificación queda degradada: se
    /// comprueba el formato, pero no el origen.
    /// </summary>
    public bool ReceiptPresent { get; set; }

    /// <summary>Ruta del recibo, si se encontró.</summary>
    public string? ReceiptPath { get; set; }

    /// <summary>Motivo por el que el recibo no pudo usarse, si corresponde.</summary>
    public string? ReceiptError { get; set; }

    /// <summary>El SHA-256 recalculado coincide con el que declara el recibo.</summary>
    public bool? SealedHashMatches { get; set; }

    /// <summary>El tamaño en disco coincide con el que declara el recibo.</summary>
    public bool? SealedSizeMatches { get; set; }

    /// <summary>El identificador del header coincide con el del recibo.</summary>
    public bool? PackageIdMatches { get; set; }

    /// <summary>Estado de la firma CMS del recibo, si existe el <c>.p7s</c>.</summary>
    public SignatureInfo? Signature { get; set; }

    /// <summary>
    /// Veredicto por archivo contra el manifiesto interno. Solo se completa en el nivel
    /// completo, con contraseña.
    /// </summary>
    public ArchiveVerificationResult? Content { get; set; }

    /// <summary>Motivo por el que no se pudo llegar al contenido, si se intentó.</summary>
    public string? ContentError { get; set; }

    /// <summary>
    /// Índice del bloque cuya verificación falló, cuando el sobre resultó alterado. Permite
    /// ubicar la alteración dentro del archivo.
    /// </summary>
    public long? AlteredChunkIndex { get; set; }

    /// <summary>
    /// Directorio temporal que no se pudo eliminar, para avisarle al operador. Debe mostrarse:
    /// dejaría contenido en claro en el disco.
    /// </summary>
    public string? UndeletedTempPath { get; set; }

    /// <summary>
    /// El sobre está íntegro: es un sobre válido, ninguna comprobación contra el recibo
    /// falló y, si el recibo está firmado, la firma y su sello de tiempo son válidos.
    /// </summary>
    /// <remarks>
    /// Un sobre sin recibo puede estar íntegro como archivo y aun así no probar autoría: eso
    /// se lee en <see cref="ReceiptPresent"/>, que la capa de presentación debe mostrar junto
    /// al veredicto.
    /// </remarks>
    public bool EnvelopeIntact =>
        FormatValid &&
        SealedHashMatches != false &&
        SealedSizeMatches != false &&
        PackageIdMatches != false &&
        AlteredChunkIndex == null &&
        (Signature == null || !Signature.Present || (Signature.Valid && Signature.TimestampValid != false));

    /// <summary>
    /// La evidencia es íntegra de punta a punta: el sobre está íntegro y, si se llegó al
    /// contenido, el manifiesto interno también verifica.
    /// </summary>
    public bool IsIntact =>
        EnvelopeIntact &&
        ContentError == null &&
        (Content == null || Content.IsIntact);

    /// <summary>
    /// Se comprobó una alteración: el sobre no coincide con su recibo o su firma, un bloque no autentica, o el
    /// contenido no coincide con su manifiesto. Lo que no es íntegro sin ser esto —la contraseña no abrió el
    /// sobre, por ejemplo— es una verificación incompleta, no una manipulación: la CLI y la GUI lo dicen igual.
    /// </summary>
    public bool TamperDetected => !EnvelopeIntact || Content is { TamperDetected: true };
}
