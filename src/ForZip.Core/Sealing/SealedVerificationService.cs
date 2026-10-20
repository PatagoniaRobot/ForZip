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

using System.Security.Cryptography;
using System.Text;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Sealing;

/// <summary>
/// Implementa la verificación de dos niveles del sobre sellado, reutilizando los servicios
/// forenses existentes: la firma la valida <see cref="ISignatureService"/> y el veredicto
/// por archivo lo produce <see cref="IVerificationService"/>, exactamente igual que hoy.
/// </summary>
public class SealedVerificationService : ISealedVerificationService
{
    private const string ManifestSuffix = ".manifest.json";
    private const string InnerPayloadFileName = "payload.zip";

    private readonly ISealingService _sealingService;
    private readonly IReceiptService _receiptService;
    private readonly ISignatureService _signatureService;
    private readonly IVerificationService _verificationService;
    private readonly IHashService _hashService;

    public SealedVerificationService(
        ISealingService sealingService,
        IReceiptService receiptService,
        ISignatureService signatureService,
        IVerificationService verificationService,
        IHashService hashService)
    {
        _sealingService = sealingService;
        _receiptService = receiptService;
        _signatureService = signatureService;
        _verificationService = verificationService;
        _hashService = hashService;
    }

    /// <inheritdoc />
    public async Task<SealedVerificationResult> VerifyAsync(
        string inputPath,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct,
        string? packagePassword = null)
    {
        var (sealedPath, receiptPath) = ResolvePaths(inputPath);
        var result = new SealedVerificationResult { SealedPath = sealedPath, ReceiptPath = receiptPath };

        if (!File.Exists(sealedPath))
        {
            result.FormatError = "No se encontró el sobre sellado.";
            return result;
        }

        // --- Nivel 1: formato ------------------------------------------------------
        SealedHeader header;
        try
        {
            header = await _sealingService.ReadHeaderAsync(sealedPath, ct).ConfigureAwait(false);
        }
        catch (SealedFormatException ex)
        {
            result.FormatError = ex.Message;
            return result;
        }

        result.FormatValid = true;
        result.Level = SealedVerificationLevel.Public;
        result.PackageId = header.PackageId;
        result.SealedAtUtc = header.SealedAtUtc;
        result.PayloadSize = header.PayloadSize;
        result.SealedSize = new FileInfo(sealedPath).Length;

        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
        var hash = await _hashService.ComputeHashesAsync(sealedPath, algorithms, null, ct).ConfigureAwait(false);
        result.SealedSha256 = hash.Hashes[HashAlgorithmType.SHA256];

        // --- Nivel 1: recibo y firma -----------------------------------------------
        await ApplyReceiptAsync(result, header, receiptPath, ct).ConfigureAwait(false);

        // --- Nivel 2: contenido ----------------------------------------------------
        if (password != null)
        {
            await VerifyContentAsync(result, password, packagePassword ?? password, progress, ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Resuelve el par sobre/recibo a partir de cualquiera de los dos, por convención de
    /// nombre. Es lo que permite que la vista Verify y el CLI acepten indistintamente uno u
    /// otro sin preguntarle nada al operador.
    /// </summary>
    private (string SealedPath, string ReceiptPath) ResolvePaths(string inputPath)
    {
        var fileName = Path.GetFileName(inputPath);
        if (fileName.EndsWith(ReceiptService.ReceiptSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return (_receiptService.GetSealedPathFor(inputPath), inputPath);
        }

        return (inputPath, _receiptService.GetReceiptPathFor(inputPath));
    }

    /// <summary>
    /// Contrasta el sobre contra su recibo y valida la firma del recibo. Sin recibo utilizable
    /// el nivel 1 queda degradado: se comprueba el formato, pero no el origen.
    /// </summary>
    private async Task ApplyReceiptAsync(
        SealedVerificationResult result,
        SealedHeader header,
        string receiptPath,
        CancellationToken ct)
    {
        if (!File.Exists(receiptPath))
        {
            result.ReceiptError = "No se encontró el recibo del sobre: se verifica el formato, pero no el origen.";
            return;
        }

        Receipt receipt;
        try
        {
            receipt = await _receiptService.ReadAsync(receiptPath, ct).ConfigureAwait(false);
        }
        catch (SealedFormatException ex)
        {
            // Un recibo ilegible no prueba nada, así que vale lo mismo que su ausencia, pero
            // el motivo tiene que quedar dicho.
            result.ReceiptError = ex.Message;
            return;
        }

        result.ReceiptPresent = true;
        result.SealedHashMatches = HashesMatch(result.SealedSha256, receipt.SealedSha256);
        result.SealedSizeMatches = result.SealedSize == receipt.SealedSize;
        result.PackageIdMatches = header.PackageId == receipt.PackageId;
        result.Signature = _signatureService.Verify(receiptPath);
    }

    /// <summary>
    /// Nivel 2: descifra el sobre en un temporal, extrae el contenedor interno y corre la
    /// verificación forense de siempre contra el manifiesto que viene adentro.
    /// </summary>
    private async Task VerifyContentAsync(
        SealedVerificationResult result,
        string password,
        string packagePassword,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        // Un sobre vacío solo puede ser fabricado: ForZip siempre mete al menos el extractor. Sin bloques no hay nada
        // que autentique la clave, así que "abriría" con cualquiera; se lo dice antes de intentarlo.
        if (result.PayloadSize == 0)
        {
            result.ContentError = "El sobre no tiene contenido (carga útil vacía): ForZip nunca sella un sobre vacío.";
            return;
        }

        var workspace = TempWorkspace.Create();
        try
        {
            var payloadPath = workspace.Combine(InnerPayloadFileName);

            SealedHeader header;
            try
            {
                header = await _sealingService
                    .UnsealFileAsync(result.SealedPath, payloadPath, password, progress, ct)
                    .ConfigureAwait(false);
            }
            catch (SealedIntegrityException ex)
            {
                result.AlteredChunkIndex = ex.ChunkIndex;
                result.ContentError = ex.Message;
                return;
            }
            catch (Exception ex) when (ex is SealedPasswordException or SealedFormatException)
            {
                result.ContentError = ex.Message;
                return;
            }

            // El tamaño ya viene autenticado en el header, pero comprobarlo contra lo escrito
            // cierra el caso de un fallo de escritura silencioso.
            if (new FileInfo(payloadPath).Length != header.PayloadSize)
            {
                result.ContentError = "La carga útil descifrada no tiene el tamaño que declara el sobre.";
                return;
            }

            var contentDir = workspace.Combine("contenido");
            List<string> extracted;
            try
            {
                await using var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                extracted = await SealedPayloadContainer.ExtractAsync(payload, contentDir, ct).ConfigureAwait(false);
            }
            catch (SealedFormatException ex)
            {
                result.ContentError = ex.Message;
                return;
            }

            var manifestPath = extracted.FirstOrDefault(
                p => p.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase));

            if (manifestPath == null)
            {
                result.ContentError = "El sobre no contiene un manifiesto forense: no hay contra qué verificar.";
                return;
            }

            result.Content = await _verificationService
                .VerifyArchiveAsync(manifestPath, null, packagePassword, null, ct)
                .ConfigureAwait(false);
            result.Level = SealedVerificationLevel.Full;
        }
        finally
        {
            // Se elimina siempre, también ante excepción o cancelación. Si el sistema no deja,
            // la ruta viaja en el resultado para avisarle al operador.
            workspace.Dispose();
            result.UndeletedTempPath = workspace.UndeletedPath;
        }
    }

    /// <summary>
    /// Compara dos huellas en tiempo fijo, sobre sus bytes y no sobre el texto, para no dar
    /// pistas por diferencia de tiempo ni por mayúsculas.
    /// </summary>
    private static bool HashesMatch(string? computed, string? declared)
    {
        if (computed == null || declared == null || computed.Length != declared.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(declared.ToLowerInvariant()));
    }
}
