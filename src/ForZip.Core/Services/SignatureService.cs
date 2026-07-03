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

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

public class SignatureService : ISignatureService
{
    private const string SignatureSuffix = ".p7s";

    // SHA-256 como algoritmo de digest de la firma
    private static readonly Oid Sha256Oid = new("2.16.840.1.101.3.4.2.1");

    // id-aa-timeStampToken (RFC 3161 / RFC 5035): atributo no firmado que porta el sello de tiempo
    private const string TimestampTokenOidValue = "1.2.840.113549.1.9.16.2.14";

    private static readonly HttpClient TsaHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public bool IsSignaturePresent(string manifestPath) => File.Exists(manifestPath + SignatureSuffix);

    public Task SignAsync(string manifestPath, string pfxPath, string? pfxPassword, CancellationToken ct)
        => SignAsync(manifestPath, pfxPath, pfxPassword, tsaUrl: null, ct);

    public async Task SignAsync(string manifestPath, string pfxPath, string? pfxPassword, string? tsaUrl, CancellationToken ct)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Manifiesto no encontrado.", manifestPath);
        }
        if (!File.Exists(pfxPath))
        {
            throw new FileNotFoundException("Certificado (PFX/PKCS#12) no encontrado.", pfxPath);
        }

        var content = await File.ReadAllBytesAsync(manifestPath, ct);

        // EphemeralKeySet evita persistir la clave privada en el almacén de la máquina
        using var cert = new X509Certificate2(
            pfxPath, pfxPassword, X509KeyStorageFlags.EphemeralKeySet);

        if (!cert.HasPrivateKey)
        {
            throw new InvalidOperationException("El certificado no contiene clave privada; no se puede firmar.");
        }

        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var signer = new CmsSigner(cert)
        {
            // Sólo el certificado del firmante (los certificados de operador suelen ser autofirmados)
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = Sha256Oid
        };
        signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));

        cms.ComputeSignature(signer, silent: true);

        // Sello de tiempo RFC 3161: la TSA certifica que la firma existía en ese instante,
        // con una fecha verificable por terceros (a diferencia de signingTime).
        TimestampUnavailableException? tsaFailure = null;
        if (!string.IsNullOrWhiteSpace(tsaUrl))
        {
            try
            {
                await AddTimestampAsync(cms.SignerInfos[0], tsaUrl!, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tsaFailure = new TimestampUnavailableException(
                    $"No se pudo obtener el sello de tiempo de '{tsaUrl}': {ex.Message}", ex);
            }
        }

        await File.WriteAllBytesAsync(manifestPath + SignatureSuffix, cms.Encode(), ct);

        // La firma quedó escrita; se informa la falta del sello para que el operador decida.
        if (tsaFailure != null)
        {
            throw tsaFailure;
        }
    }

    /// <summary>
    /// Solicita a la TSA un token RFC 3161 sobre el valor de la firma y lo incrusta como
    /// atributo no firmado (id-aa-timeStampToken) del firmante.
    /// </summary>
    private static async Task AddTimestampAsync(SignerInfo signerInfo, string tsaUrl, CancellationToken ct)
    {
        // El nonce liga la respuesta a esta petición concreta (anti-replay)
        var nonce = RandomNumberGenerator.GetBytes(16);
        var request = Rfc3161TimestampRequest.CreateFromSignerInfo(
            signerInfo,
            HashAlgorithmName.SHA256,
            nonce: nonce,
            requestSignerCertificates: true);

        using var body = new ByteArrayContent(request.Encode());
        body.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");

        using var response = await TsaHttpClient.PostAsync(tsaUrl, body, ct);
        response.EnsureSuccessStatusCode();
        var replyBytes = await response.Content.ReadAsByteArrayAsync(ct);

        // ProcessResponse valida que el token corresponda a la petición (imprint + nonce)
        var token = request.ProcessResponse(replyBytes, out _);

        signerInfo.AddUnsignedAttribute(
            new AsnEncodedData(new Oid(TimestampTokenOidValue), token.AsSignedCms().Encode()));
    }

    public SignatureInfo Verify(string manifestPath)
    {
        var sigPath = manifestPath + SignatureSuffix;
        if (!File.Exists(sigPath))
        {
            return new SignatureInfo(Present: false, Valid: false, SignerSubject: null,
                SignedAtUtc: null, Details: "El manifiesto no está firmado (no se encontró .p7s).");
        }

        if (!File.Exists(manifestPath))
        {
            return new SignatureInfo(true, false, null, null, "No se encontró el manifiesto a verificar.");
        }

        try
        {
            var content = File.ReadAllBytes(manifestPath);
            var signature = File.ReadAllBytes(sigPath);

            var cms = new SignedCms(new ContentInfo(content), detached: true);
            cms.Decode(signature);

            // verifySignatureOnly: comprueba la firma sin validar la cadena del
            // certificado contra una CA de confianza (modelo de confianza externo).
            bool valid;
            string details;
            try
            {
                cms.CheckSignature(verifySignatureOnly: true);
                valid = true;
                details = "Firma válida: el manifiesto no cambió desde la firma. " +
                          "La confianza en la identidad del firmante debe validarse por fuera (certificado).";
            }
            catch (CryptographicException ex)
            {
                valid = false;
                details = $"Firma inválida: {ex.Message}";
            }

            var signer = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0] : null;
            var subject = signer?.Certificate?.Subject;
            var signedAt = ExtractSigningTime(signer);
            var (tsUtc, tsAuthority, tsValid) = ExtractTimestamp(signer);

            return new SignatureInfo(true, valid, subject, signedAt, details,
                tsUtc, tsAuthority, tsValid);
        }
        catch (Exception ex)
        {
            return new SignatureInfo(true, false, null, null, $"No se pudo procesar la firma: {ex.Message}");
        }
    }

    private static DateTimeOffset? ExtractSigningTime(SignerInfo? signer)
    {
        if (signer == null)
        {
            return null;
        }

        foreach (var attr in signer.SignedAttributes)
        {
            if (attr.Oid?.Value == "1.2.840.113549.1.9.5") // signingTime
            {
                foreach (var value in attr.Values)
                {
                    if (value is Pkcs9SigningTime signingTime)
                    {
                        return new DateTimeOffset(signingTime.SigningTime.ToUniversalTime(), TimeSpan.Zero);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extrae y valida el sello de tiempo RFC 3161 (si existe) de los atributos no firmados.
    /// La validez confirma que el token cubre exactamente esta firma y que la firma de la
    /// propia TSA es correcta; la confianza en la TSA se valida por fuera (su certificado).
    /// </summary>
    private static (DateTimeOffset? tsUtc, string? tsAuthority, bool? tsValid) ExtractTimestamp(SignerInfo? signer)
    {
        if (signer == null)
        {
            return (null, null, null);
        }

        foreach (var attr in signer.UnsignedAttributes)
        {
            if (attr.Oid?.Value != TimestampTokenOidValue)
            {
                continue;
            }

            foreach (var value in attr.Values)
            {
                if (value is not AsnEncodedData data)
                {
                    continue;
                }

                if (!Rfc3161TimestampToken.TryDecode(data.RawData, out var token, out _))
                {
                    return (null, null, false);
                }

                bool tokenValid;
                X509Certificate2? tsaCert = null;
                try
                {
                    tokenValid = token.VerifySignatureForSignerInfo(signer, out tsaCert);
                }
                catch (CryptographicException)
                {
                    tokenValid = false;
                }

                return (token.TokenInfo.Timestamp.ToUniversalTime(), tsaCert?.Subject, tokenValid);
            }
        }

        return (null, null, null);
    }
}
