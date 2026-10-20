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

namespace ForZip.Core.Sealing;

/// <summary>
/// Derivación de la clave del sobre sellado. Parámetros centralizados acá: el header
/// declara los que usó cada paquete, así que subirlos en el futuro no rompe la lectura de
/// los sobres ya emitidos.
/// </summary>
/// <remarks>
/// Se usa PBKDF2-HMAC-SHA256 con 600.000 iteraciones y no Argon2id: Argon2 exigiría una
/// dependencia externa y ampliaría la superficie a auditar de una herramienta forense.
/// Este KDF viene en el framework, es auditable y reemplaza al PBKDF2-HMAC-SHA1 de 1.000
/// iteraciones que impone el esquema WinZip AES del ZIP.
/// </remarks>
public static class KeyDerivation
{
    /// <summary>Iteraciones que usa ForZip al sellar. Los sobres ajenos declaran las suyas.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>
    /// Máximo que un lector acepta en un header (Formato_FZS_v1 §3). El header se autentica recién con el
    /// primer chunk, después de derivar la clave: sin tope, un header alterado con miles de millones de
    /// iteraciones dejaba la derivación corriendo horas, sin poder cancelarse, antes de delatarse.
    /// </summary>
    public const int MaxIterations = 10_000_000;

    /// <summary>
    /// Deriva la clave AES-256 a partir de la contraseña y el salt del header.
    /// </summary>
    /// <remarks>
    /// La contraseña se convierte a UTF-8 y esos bytes se limpian con
    /// <see cref="CryptographicOperations.ZeroMemory"/> apenas dejan de hacer falta. La
    /// clave se devuelve como <c>byte[]</c>, nunca como <c>string</c>, para que quien la
    /// use también pueda limpiarla.
    /// </remarks>
    public static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("La contraseña del sobre no puede estar vacía.", nameof(password));
        }

        if (salt.Length != SealedContainerFormat.SaltSize)
        {
            throw new ArgumentException("El salt debe tener 32 bytes.", nameof(salt));
        }

        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                SealedContainerFormat.KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
