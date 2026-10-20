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
using System.Security.Cryptography.X509Certificates;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

/// <summary>Si un firmante es de confianza para el operador.</summary>
public enum SignerTrustLevel
{
    /// <summary>No hay firma, o no trae certificado.</summary>
    NotApplicable,

    /// <summary>La huella del certificado está en la lista de firmantes de confianza.</summary>
    Trusted,

    /// <summary>Hay lista, y este certificado no está: puede ser otro, aunque diga el mismo nombre.</summary>
    Unknown,

    /// <summary>No hay lista cargada: la identidad del firmante no se pudo comparar con nada.</summary>
    NoList
}

/// <summary>
/// Comparación de la huella SHA-256 del certificado firmante con los firmantes de confianza.
/// </summary>
/// <remarks>
/// La firma prueba que el archivo no cambió desde que se firmó; no prueba quién firmó. El nombre del certificado
/// (<c>CN=…</c>) lo pone quien lo crea, y un certificado autofirmado puede decir el de cualquiera: alguien podía
/// alterar la evidencia, rehacer el manifiesto y firmarlo con un certificado propio con el mismo nombre, y la
/// verificación lo mostraba igual. La huella no se puede imitar: es lo que se compara.
/// </remarks>
public static class SignerTrust
{
    public static (SignerTrustLevel Level, string? Name) Evaluate(string? thumbprint, IEnumerable<TrustedSigner>? trusted)
    {
        if (string.IsNullOrEmpty(thumbprint))
        {
            return (SignerTrustLevel.NotApplicable, null);
        }

        var list = trusted?.ToList() ?? new List<TrustedSigner>();
        if (list.Count == 0)
        {
            return (SignerTrustLevel.NoList, null);
        }

        var normalized = Normalize(thumbprint);
        var match = list.FirstOrDefault(t => Normalize(t.Thumbprint) == normalized);
        return match != null ? (SignerTrustLevel.Trusted, match.Name) : (SignerTrustLevel.Unknown, null);
    }

    /// <summary>
    /// Huella en forma canónica: 64 hexadecimales en mayúsculas, sin espacios ni dos puntos (como se la copia de
    /// las propiedades del certificado en Windows). Null si no es una huella SHA-256.
    /// </summary>
    public static string? TryNormalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var hex = new string(text.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        var separatorsOnly = text.All(c => Uri.IsHexDigit(c) || c is ' ' or ':' or '-' || char.IsWhiteSpace(c) || c == '‎');
        return hex.Length == 64 && separatorsOnly ? hex : null;
    }

    /// <summary>Agrupa la huella de a dos para leerla y dictarla (AB:CD:…).</summary>
    public static string Format(string thumbprint)
    {
        var hex = Normalize(thumbprint);
        return string.Join(":", Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }

    /// <summary>
    /// Lee un certificado (.cer, .crt, .pem; o el de un .p7s firmado) y devuelve su huella SHA-256 y su nombre.
    /// Solo la parte pública: no hace falta ni se pide la clave privada.
    /// </summary>
    public static (string Thumbprint, string Subject) ReadCertificate(string path)
    {
        if (path.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase))
        {
            var cms = new System.Security.Cryptography.Pkcs.SignedCms();
            cms.Decode(File.ReadAllBytes(path));
            var signerCert = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
            if (signerCert == null)
            {
                throw new CryptographicException("La firma no incluye el certificado del firmante.");
            }

            return (signerCert.GetCertHashString(HashAlgorithmName.SHA256), signerCert.Subject);
        }

        using var cert = X509CertificateLoader.LoadCertificateFromFile(path);
        return (cert.GetCertHashString(HashAlgorithmName.SHA256), cert.Subject);
    }

    private static string Normalize(string thumbprint) =>
        new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
}
