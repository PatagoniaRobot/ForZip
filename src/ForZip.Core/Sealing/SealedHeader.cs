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

using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ForZip.Core.Sealing;

/// <summary>
/// Header del sobre sellado. Viaja <b>en claro</b> porque contiene los parámetros
/// necesarios para derivar la clave, y por eso su contenido está deliberadamente limitado
/// a lo criptográficamente imprescindible.
/// </summary>
/// <remarks>
/// <b>Está prohibido agregar cualquier campo derivado del contenido</b>: nada de nombres
/// de archivo, cantidad de archivos, número de causa, operador ni dependencia judicial.
/// El <see cref="PackageId"/> es aleatorio y es el único identificador que sale a la nube.
/// </remarks>
public sealed class SealedHeader
{
    // Fecha ISO 8601 en UTC con precisión de segundo. La T y la Z van entrecomilladas
    // para que el formateador las trate como literales y no como especificadores.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private SealedHeader(
        Guid packageId,
        DateTimeOffset sealedAtUtc,
        int iterations,
        byte[] salt,
        int chunkSize,
        byte[] nonceBase,
        long payloadSize,
        long totalChunks)
    {
        PackageId = packageId;
        SealedAtUtc = sealedAtUtc;
        Iterations = iterations;
        Salt = salt;
        ChunkSize = chunkSize;
        NonceBase = nonceBase;
        PayloadSize = payloadSize;
        TotalChunks = totalChunks;
    }

    /// <summary>UUID v4 aleatorio. No se deriva de la causa, del operador ni de la fecha.</summary>
    public Guid PackageId { get; }

    /// <summary>Fecha de sellado en UTC, con precisión de segundo.</summary>
    public DateTimeOffset SealedAtUtc { get; }

    /// <summary>Iteraciones de PBKDF2-HMAC-SHA256.</summary>
    public int Iterations { get; }

    /// <summary>Salt del KDF, 32 bytes aleatorios por paquete.</summary>
    public byte[] Salt { get; }

    /// <summary>Tamaño del chunk de cifrado, en bytes.</summary>
    public int ChunkSize { get; }

    /// <summary>Parte fija del nonce, 8 bytes aleatorios por paquete.</summary>
    public byte[] NonceBase { get; }

    /// <summary>Tamaño de la carga útil en claro, en bytes. Permite detectar truncamiento.</summary>
    public long PayloadSize { get; }

    /// <summary>Cantidad de chunks que ocupa la carga útil.</summary>
    public long TotalChunks { get; }

    /// <summary>Nombre base de los artefactos del sobre. Ejemplo: <c>FZS-8f3a1c9e</c>.</summary>
    public string BaseName => SealedContainerFormat.BuildBaseName(PackageId);

    /// <summary>
    /// Arma el header de un sobre nuevo, con identificador, salt y nonce base aleatorios.
    /// </summary>
    public static SealedHeader Create(long payloadSize, int iterations, int chunkSize, DateTimeOffset sealedAtUtc)
    {
        if (payloadSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadSize));
        }

        if (iterations <= 0 || iterations > KeyDerivation.MaxIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (chunkSize < SealedContainerFormat.MinChunkSize || chunkSize > SealedContainerFormat.MaxChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        return new SealedHeader(
            Guid.NewGuid(),
            TruncateToSecond(sealedAtUtc),
            iterations,
            RandomNumberGenerator.GetBytes(SealedContainerFormat.SaltSize),
            chunkSize,
            RandomNumberGenerator.GetBytes(SealedContainerFormat.NonceBaseSize),
            payloadSize,
            SealedContainerFormat.ChunkCountFor(payloadSize, chunkSize));
    }

    /// <summary>
    /// Serializa el header a UTF-8 sin BOM y sin saltos de línea, con orden de campos fijo.
    /// Los bytes que devuelve son los que se escriben en el sobre y sobre los que se calcula
    /// el AAD: el header <b>nunca</b> se reserializa para autenticar.
    /// </summary>
    public byte[] ToJsonBytes()
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        var options = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteString("packageId", PackageId.ToString("D"));
            writer.WriteString("sealedAtUtc", SealedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));

            writer.WriteStartObject("kdf");
            writer.WriteString("algo", SealedContainerFormat.KdfAlgorithm);
            writer.WriteNumber("iterations", Iterations);
            writer.WriteString("saltB64", Convert.ToBase64String(Salt));
            writer.WriteEndObject();

            writer.WriteStartObject("cipher");
            writer.WriteString("algo", SealedContainerFormat.CipherAlgorithm);
            writer.WriteNumber("chunkSize", ChunkSize);
            writer.WriteString("nonceBaseB64", Convert.ToBase64String(NonceBase));
            writer.WriteEndObject();

            writer.WriteNumber("payloadSize", PayloadSize);
            writer.WriteNumber("totalChunks", TotalChunks);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// SHA-256 de los bytes exactos del header, tal como se escribieron en el sobre. Es la
    /// primera parte del AAD de cada chunk.
    /// </summary>
    public static byte[] ComputeHash(ReadOnlySpan<byte> headerJsonBytes) => SHA256.HashData(headerJsonBytes);

    /// <summary>
    /// Interpreta el header de un sobre. Rechaza con un error de formato todo lo que esta
    /// versión no sepa descifrar, sin revelar nada del contenido.
    /// </summary>
    /// <exception cref="SealedFormatException">El header está malformado o declara parámetros no soportados.</exception>
    public static SealedHeader Parse(ReadOnlySpan<byte> headerJsonBytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(headerJsonBytes.ToArray());
        }
        catch (JsonException ex)
        {
            throw new SealedFormatException("El encabezado del sobre está malformado.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SealedFormatException("El encabezado del sobre está malformado.");
            }

            var packageId = ReadGuid(root, "packageId");
            var sealedAtUtc = ReadTimestamp(root, "sealedAtUtc");

            var kdf = ReadObject(root, "kdf");
            RequireAlgorithm(kdf, SealedContainerFormat.KdfAlgorithm, "derivación de clave");
            var iterations = ReadInt32(kdf, "iterations");
            var salt = ReadBase64(kdf, "saltB64");

            var cipher = ReadObject(root, "cipher");
            RequireAlgorithm(cipher, SealedContainerFormat.CipherAlgorithm, "cifrado");
            var chunkSize = ReadInt32(cipher, "chunkSize");
            var nonceBase = ReadBase64(cipher, "nonceBaseB64");

            var payloadSize = ReadInt64(root, "payloadSize");
            var totalChunks = ReadInt64(root, "totalChunks");

            if (iterations <= 0 || iterations > KeyDerivation.MaxIterations)
            {
                throw new SealedFormatException("El encabezado declara un número de iteraciones inválido.");
            }

            if (salt.Length != SealedContainerFormat.SaltSize)
            {
                throw new SealedFormatException("El encabezado declara un salt de longitud inválida.");
            }

            if (nonceBase.Length != SealedContainerFormat.NonceBaseSize)
            {
                throw new SealedFormatException("El encabezado declara un nonce base de longitud inválida.");
            }

            if (chunkSize < SealedContainerFormat.MinChunkSize || chunkSize > SealedContainerFormat.MaxChunkSize)
            {
                throw new SealedFormatException("El encabezado declara un tamaño de chunk fuera de rango.");
            }

            if (payloadSize < 0)
            {
                throw new SealedFormatException("El encabezado declara un tamaño de carga útil inválido.");
            }

            if (totalChunks != SealedContainerFormat.ChunkCountFor(payloadSize, chunkSize))
            {
                throw new SealedFormatException("El encabezado declara una cantidad de chunks incoherente con el tamaño.");
            }

            if (totalChunks > uint.MaxValue)
            {
                throw new SealedFormatException("El sobre declara más chunks de los que admite el formato.");
            }

            return new SealedHeader(packageId, sealedAtUtc, iterations, salt, chunkSize, nonceBase, payloadSize, totalChunks);
        }
    }

    // El header declara la fecha con precisión de segundo: truncar acá evita que la
    // serialización pierda información que el parseo no puede recuperar.
    private static DateTimeOffset TruncateToSecond(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerSecond));
    }

    private static JsonElement ReadObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new SealedFormatException($"Falta el bloque '{name}' en el encabezado del sobre.");
        }

        return value;
    }

    private static void RequireAlgorithm(JsonElement parent, string expected, string role)
    {
        if (!parent.TryGetProperty("algo", out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new SealedFormatException($"Falta el algoritmo de {role} en el encabezado del sobre.");
        }

        if (!string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            throw new SealedFormatException($"El sobre usa un algoritmo de {role} que esta versión de ForZip no soporta.");
        }
    }

    private static Guid ReadGuid(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(value.GetString(), "D", out var parsed))
        {
            throw new SealedFormatException($"El campo '{name}' del encabezado es inválido.");
        }

        return parsed;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParseExact(
                value.GetString(),
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new SealedFormatException($"El campo '{name}' del encabezado es inválido.");
        }

        return parsed;
    }

    private static int ReadInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed))
        {
            throw new SealedFormatException($"El campo '{name}' del encabezado es inválido.");
        }

        return parsed;
    }

    private static long ReadInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var parsed))
        {
            throw new SealedFormatException($"El campo '{name}' del encabezado es inválido.");
        }

        return parsed;
    }

    private static byte[] ReadBase64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new SealedFormatException($"El campo '{name}' del encabezado es inválido.");
        }

        try
        {
            return Convert.FromBase64String(value.GetString()!);
        }
        catch (FormatException ex)
        {
            throw new SealedFormatException($"El campo '{name}' del encabezado es inválido.", ex);
        }
    }
}
