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

using System.Buffers.Binary;
using System.Text;

namespace ForZip.Core.Sealing;

/// <summary>
/// Constantes del contenedor sellado <c>.fzs</c>. Es la única fuente de verdad del layout
/// binario; la especificación pública del formato y el extractor de emergencia en Python
/// deben coincidir con estos valores.
/// </summary>
/// <remarks>
/// Layout, con todos los enteros en <b>little-endian</b>:
/// <code>
/// Offset  Tamaño     Campo
/// ------  ---------  ---------------------------------------------
/// 0       8          magic      = ASCII "FZSEAL01"
/// 8       2          formatVer  = uint16, valor 1
/// 10      4          headerLen  = uint32, longitud del header JSON
/// 14      headerLen  headerJson = UTF-8 sin BOM, sin saltos de línea
/// 14+HL   ...        stream de chunks: ciphertext[i] ‖ tag[i]
/// </code>
/// </remarks>
public static class SealedContainerFormat
{
    /// <summary>Marca de formato, en ASCII, al inicio de todo sobre.</summary>
    public const string Magic = "FZSEAL01";

    /// <summary>Longitud en bytes de <see cref="Magic"/>.</summary>
    public const int MagicLength = 8;

    /// <summary>Versión de formato que esta compilación escribe y sabe leer.</summary>
    public const ushort FormatVersion = 1;

    /// <summary>Bytes que preceden al header JSON: magic (8) + formatVer (2) + headerLen (4).</summary>
    public const int PreambleLength = MagicLength + sizeof(ushort) + sizeof(uint);

    /// <summary>
    /// Tope defensivo para <c>headerLen</c>. El header legítimo ronda los 300 bytes; el
    /// límite evita que un archivo corrupto o malicioso reserve memoria arbitraria.
    /// </summary>
    public const int MaxHeaderLength = 64 * 1024;

    /// <summary>Tamaño de chunk por defecto: 1 MiB.</summary>
    public const int DefaultChunkSize = 1024 * 1024;

    /// <summary>Chunk mínimo aceptado al leer un sobre ajeno.</summary>
    public const int MinChunkSize = 4 * 1024;

    /// <summary>Chunk máximo aceptado al leer un sobre ajeno: 64 MiB.</summary>
    public const int MaxChunkSize = 64 * 1024 * 1024;

    /// <summary>Longitud del tag de autenticación GCM, en bytes.</summary>
    public const int TagSize = 16;

    /// <summary>Longitud del nonce GCM: <c>nonceBase</c> (8) ‖ <c>chunkIndex</c> (4, LE).</summary>
    public const int NonceSize = 12;

    /// <summary>Longitud de la parte aleatoria y fija del nonce, por paquete.</summary>
    public const int NonceBaseSize = 8;

    /// <summary>Longitud del salt del KDF, en bytes.</summary>
    public const int SaltSize = 32;

    /// <summary>Longitud de la clave derivada (AES-256), en bytes.</summary>
    public const int KeySize = 32;

    /// <summary>Longitud del AAD: SHA-256 del header (32) ‖ índice (4) ‖ isFinal (1).</summary>
    public const int AadSize = 32 + sizeof(uint) + 1;

    /// <summary>Nombre del algoritmo de derivación tal como se declara en el header.</summary>
    public const string KdfAlgorithm = "PBKDF2-HMAC-SHA256";

    /// <summary>Nombre del algoritmo de cifrado tal como se declara en el header.</summary>
    public const string CipherAlgorithm = "AES-256-GCM";

    /// <summary>Extensión del sobre sellado.</summary>
    public const string SealedExtension = ".fzs";

    /// <summary>Prefijo del nombre de los artefactos de salida del sellado.</summary>
    public const string FileNamePrefix = "FZS-";

    /// <summary>
    /// Cantidad de caracteres hexadecimales del <c>packageId</c> que se usan como sufijo
    /// del nombre de archivo. Está prohibido derivar el nombre de la causa o del operador.
    /// </summary>
    public const int FileNameIdLength = 8;

    /// <summary>Bytes de <see cref="Magic"/>, para comparar sin reconvertir en cada lectura.</summary>
    public static ReadOnlySpan<byte> MagicBytes => "FZSEAL01"u8;

    /// <summary>UTF-8 sin BOM: codificación del header JSON.</summary>
    public static readonly Encoding HeaderEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Nombre base de los artefactos de un sobre, con el sufijo derivado del identificador
    /// aleatorio del paquete. Ejemplo: <c>FZS-8f3a1c9e</c>.
    /// </summary>
    public static string BuildBaseName(Guid packageId) =>
        FileNamePrefix + packageId.ToString("N")[..FileNameIdLength];

    /// <summary>
    /// Cantidad de chunks que ocupa una carga útil del tamaño indicado. Una carga de cero
    /// bytes ocupa cero chunks.
    /// </summary>
    public static long ChunkCountFor(long payloadSize, int chunkSize)
    {
        if (payloadSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadSize));
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        return (payloadSize + chunkSize - 1) / chunkSize;
    }

    /// <summary>
    /// Longitud en bytes del chunk indicado. El último chunk es el resto; los demás son
    /// completos.
    /// </summary>
    public static int ChunkLengthAt(long chunkIndex, long payloadSize, int chunkSize)
    {
        var offset = chunkIndex * chunkSize;
        var remaining = payloadSize - offset;
        return remaining >= chunkSize ? chunkSize : (int)remaining;
    }

    /// <summary>
    /// Construye el nonce del chunk: <c>nonceBase</c> (8 bytes) ‖ índice como uint32 LE.
    /// Como el salt y el <c>nonceBase</c> son aleatorios por paquete, la clave es única y
    /// la reutilización de nonce con la misma clave es imposible.
    /// </summary>
    public static void BuildNonce(ReadOnlySpan<byte> nonceBase, uint chunkIndex, Span<byte> destination)
    {
        if (nonceBase.Length != NonceBaseSize)
        {
            throw new ArgumentException("El nonce base debe tener 8 bytes.", nameof(nonceBase));
        }

        if (destination.Length != NonceSize)
        {
            throw new ArgumentException("El nonce debe tener 12 bytes.", nameof(destination));
        }

        nonceBase.CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[NonceBaseSize..], chunkIndex);
    }

    /// <summary>
    /// Construye el AAD del chunk: SHA-256 del header ‖ índice uint32 LE ‖ <c>isFinal</c>.
    /// Autenticar el header impide alterar iteraciones, salt o tamaño declarado, y el
    /// marcador <c>isFinal</c> es lo que vuelve detectable el truncamiento del archivo.
    /// </summary>
    public static void BuildAad(ReadOnlySpan<byte> headerHash, uint chunkIndex, bool isFinal, Span<byte> destination)
    {
        if (headerHash.Length != 32)
        {
            throw new ArgumentException("El hash del header debe tener 32 bytes.", nameof(headerHash));
        }

        if (destination.Length != AadSize)
        {
            throw new ArgumentException("El AAD debe tener 37 bytes.", nameof(destination));
        }

        headerHash.CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[32..], chunkIndex);
        destination[^1] = isFinal ? (byte)0x01 : (byte)0x00;
    }
}
