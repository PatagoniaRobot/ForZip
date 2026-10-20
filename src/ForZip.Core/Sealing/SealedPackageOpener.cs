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

using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Sealing;

/// <summary>
/// Abre un sobre hacia el destino que elige el operador. Es el mismo recorrido que la
/// verificación de nivel 2, pero el contenido queda donde se lo pidió en vez de en un temporal.
/// </summary>
/// <remarks>
/// El sobre se descifra primero a un temporal y recién con todos los bloques autenticados se
/// extrae al destino: un sobre alterado o una contraseña equivocada no dejan nada a medio abrir
/// en la carpeta del operador. La política de carpetas sincronizadas es del llamador —la GUI
/// pregunta, la CLI exige un indicador—, porque la forma de confirmar depende de la interfaz.
/// </remarks>
public sealed class SealedPackageOpener : ISealedPackageOpener
{
    private const string InnerPayloadFileName = "payload.zip";
    private const string ManifestSuffix = ".manifest.json";

    private readonly ISealingService _sealingService;
    private readonly IVerificationService _verificationService;

    public SealedPackageOpener(ISealingService sealingService, IVerificationService verificationService)
    {
        _sealingService = sealingService;
        _verificationService = verificationService;
    }

    public Task<SealedHeader> ReadHeaderAsync(string sealedPath, CancellationToken ct) =>
        _sealingService.ReadHeaderAsync(sealedPath, ct);

    public async Task<SealedOpenResult> OpenAsync(
        string sealedPath,
        string outputDirectory,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(sealedPath);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        ArgumentException.ThrowIfNullOrEmpty(password);

        // Abrir un sobre no pisa nada: si en el destino ya hay algo, puede ser otra evidencia.
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new IOException($"La carpeta de destino no está vacía: {outputDirectory}");
        }

        // Un sobre vacío solo puede ser fabricado (ForZip siempre mete al menos el extractor), y sin bloques no hay
        // nada que autentique la clave: "abriría" con cualquiera. Se lo rechaza antes de pedir nada más.
        var declared = await _sealingService.ReadHeaderAsync(sealedPath, ct).ConfigureAwait(false);
        if (declared.PayloadSize == 0)
        {
            throw new SealedFormatException("El sobre no tiene contenido (carga útil vacía): ForZip nunca sella un sobre vacío.");
        }

        var destinationExisted = Directory.Exists(outputDirectory);
        var workspace = TempWorkspace.Create();
        try
        {
            var payloadPath = workspace.Combine(InnerPayloadFileName);
            var header = await _sealingService
                .UnsealFileAsync(sealedPath, payloadPath, password, progress, ct)
                .ConfigureAwait(false);

            List<string> extracted;
            try
            {
                await using var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                extracted = await SealedPayloadContainer.ExtractAsync(payload, outputDirectory, ct).ConfigureAwait(false);
            }
            catch
            {
                // ExtractAsync ya borró lo que escribió; la carpeta, si la creó esta apertura, tampoco queda.
                if (!destinationExisted)
                {
                    TryDeleteEmptyDirectory(outputDirectory);
                }

                throw;
            }

            var (verification, noManifest, error) = await VerifyAsync(extracted, password, ct).ConfigureAwait(false);

            workspace.Dispose();
            return new SealedOpenResult
            {
                Header = header,
                OutputDirectory = outputDirectory,
                ExtractedFiles = extracted,
                Verification = verification,
                HasNoManifest = noManifest,
                VerificationError = error,
                UndeletedTempPath = workspace.UndeletedPath
            };
        }
        finally
        {
            // Idempotente: si ya se eliminó arriba no hace nada. Cubre excepción y cancelación.
            workspace.Dispose();
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Verifica el contenido recién extraído contra su manifiesto. Un fallo de verificación no
    /// deshace la apertura: el contenido ya está en disco y el operador tiene que saber el
    /// veredicto, sea cual sea.
    /// </summary>
    private async Task<(ArchiveVerificationResult? Result, bool NoManifest, string? Error)> VerifyAsync(
        IReadOnlyList<string> extracted, string password, CancellationToken ct)
    {
        var manifestPath = extracted.FirstOrDefault(
            p => p.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase));
        if (manifestPath == null)
        {
            return (null, true, null);
        }

        try
        {
            var result = await _verificationService
                .VerifyArchiveAsync(manifestPath, null, password, null, ct)
                .ConfigureAwait(false);
            return (result, false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, false, ex.Message);
        }
    }
}
