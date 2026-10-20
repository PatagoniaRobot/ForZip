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
using System.Security.Cryptography;
using ForZip.Core.Interfaces;

namespace ForZip.Core.Sealing;

/// <summary>
/// Implementación del contenedor sellado <c>.fzs</c>: AES-256-GCM por chunks de 1 MiB,
/// clave derivada con PBKDF2-HMAC-SHA256 y header autenticado como AAD de cada chunk.
/// </summary>
/// <remarks>
/// Trabaja por streaming en los dos sentidos: un paquete de decenas de GB no se carga en
/// memoria ni se duplica en disco.
/// </remarks>
public class SealingService : ISealingService
{
    private readonly int _iterations;
    private readonly int _chunkSize;

    /// <summary>
    /// Crea el servicio con los parámetros de sellado. Los valores por defecto son los de
    /// producción; los tests bajan las iteraciones para no pagar el KDF en cada caso.
    /// </summary>
    public SealingService(
        int iterations = KeyDerivation.DefaultIterations,
        int chunkSize = SealedContainerFormat.DefaultChunkSize)
    {
        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (chunkSize < SealedContainerFormat.MinChunkSize || chunkSize > SealedContainerFormat.MaxChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        _iterations = iterations;
        _chunkSize = chunkSize;
    }

    /// <inheritdoc />
    public async Task<SealedHeader> SealAsync(
        Stream payload,
        Stream destination,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(destination);

        if (!payload.CanSeek)
        {
            throw new ArgumentException(
                "La carga útil debe permitir posicionamiento: el tamaño se declara en el header.",
                nameof(payload));
        }

        var payloadSize = payload.Length - payload.Position;
        var header = SealedHeader.Create(payloadSize, _iterations, _chunkSize, DateTimeOffset.UtcNow);
        var headerBytes = header.ToJsonBytes();

        await WritePreambleAsync(destination, headerBytes, ct).ConfigureAwait(false);

        var headerHash = SealedHeader.ComputeHash(headerBytes);
        var key = KeyDerivation.DeriveKey(password, header.Salt, header.Iterations);
        var plain = new byte[header.ChunkSize];
        var cipher = new byte[header.ChunkSize];

        try
        {
            using var aes = new AesGcm(key, SealedContainerFormat.TagSize);
            var nonce = new byte[SealedContainerFormat.NonceSize];
            var aad = new byte[SealedContainerFormat.AadSize];
            var tag = new byte[SealedContainerFormat.TagSize];
            long processed = 0;

            for (long index = 0; index < header.TotalChunks; index++)
            {
                ct.ThrowIfCancellationRequested();

                var length = SealedContainerFormat.ChunkLengthAt(index, payloadSize, header.ChunkSize);
                await payload.ReadExactlyAsync(plain.AsMemory(0, length), ct).ConfigureAwait(false);

                SealedContainerFormat.BuildNonce(header.NonceBase, (uint)index, nonce);
                SealedContainerFormat.BuildAad(headerHash, (uint)index, index == header.TotalChunks - 1, aad);
                aes.Encrypt(nonce, plain.AsSpan(0, length), cipher.AsSpan(0, length), tag, aad);

                await destination.WriteAsync(cipher.AsMemory(0, length), ct).ConfigureAwait(false);
                await destination.WriteAsync(tag, ct).ConfigureAwait(false);

                processed += length;
                progress?.Report((processed, payloadSize));
            }

            await destination.FlushAsync(ct).ConfigureAwait(false);
            return header;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <inheritdoc />
    public async Task<SealedHeader> UnsealAsync(
        Stream sealedInput,
        Stream destination,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sealedInput);
        ArgumentNullException.ThrowIfNull(destination);

        var (header, headerBytes) = await ReadHeaderCoreAsync(sealedInput, ct).ConfigureAwait(false);
        var headerHash = SealedHeader.ComputeHash(headerBytes);
        var key = KeyDerivation.DeriveKey(password, header.Salt, header.Iterations);
        var plain = new byte[header.ChunkSize];
        var cipher = new byte[header.ChunkSize];

        try
        {
            using var aes = new AesGcm(key, SealedContainerFormat.TagSize);
            var nonce = new byte[SealedContainerFormat.NonceSize];
            var aad = new byte[SealedContainerFormat.AadSize];
            var tag = new byte[SealedContainerFormat.TagSize];
            long processed = 0;

            for (long index = 0; index < header.TotalChunks; index++)
            {
                ct.ThrowIfCancellationRequested();

                var length = SealedContainerFormat.ChunkLengthAt(index, header.PayloadSize, header.ChunkSize);
                await ReadChunkAsync(sealedInput, cipher.AsMemory(0, length), tag, index, ct).ConfigureAwait(false);

                SealedContainerFormat.BuildNonce(header.NonceBase, (uint)index, nonce);
                SealedContainerFormat.BuildAad(headerHash, (uint)index, index == header.TotalChunks - 1, aad);

                try
                {
                    aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), aad);
                }
                catch (CryptographicException ex)
                {
                    throw DescribeTagFailure(index, ex);
                }

                await destination.WriteAsync(plain.AsMemory(0, length), ct).ConfigureAwait(false);

                processed += length;
                progress?.Report((processed, header.PayloadSize));
            }

            // Bytes agregados después del último chunk: el sobre ya no es el que se selló.
            if (sealedInput.CanSeek && sealedInput.Position != sealedInput.Length)
            {
                throw new SealedFormatException("El sobre tiene datos agregados después del último bloque.");
            }

            await destination.FlushAsync(ct).ConfigureAwait(false);
            return header;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <inheritdoc />
    public async Task<SealedHeader> SealFileAsync(
        string payloadPath,
        string sealedPath,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        return await RunToFileAsync(
            sealedPath,
            async destination =>
            {
                await using var payload = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await SealAsync(payload, destination, password, progress, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SealedHeader> UnsealFileAsync(
        string sealedPath,
        string payloadPath,
        string password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        return await RunToFileAsync(
            payloadPath,
            async destination =>
            {
                await using var input = new FileStream(sealedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await UnsealAsync(input, destination, password, progress, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SealedHeader> ReadHeaderAsync(Stream sealedInput, CancellationToken ct)
    {
        var (header, _) = await ReadHeaderCoreAsync(sealedInput, ct).ConfigureAwait(false);
        return header;
    }

    /// <inheritdoc />
    public async Task<SealedHeader> ReadHeaderAsync(string sealedPath, CancellationToken ct)
    {
        await using var input = new FileStream(sealedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ReadHeaderAsync(input, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Escribe magic, versión de formato y longitud del header, seguidos del header JSON.
    /// </summary>
    private static async Task WritePreambleAsync(Stream destination, byte[] headerBytes, CancellationToken ct)
    {
        var preamble = new byte[SealedContainerFormat.PreambleLength];
        SealedContainerFormat.MagicBytes.CopyTo(preamble);
        BinaryPrimitives.WriteUInt16LittleEndian(
            preamble.AsSpan(SealedContainerFormat.MagicLength),
            SealedContainerFormat.FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(
            preamble.AsSpan(SealedContainerFormat.MagicLength + sizeof(ushort)),
            (uint)headerBytes.Length);

        await destination.WriteAsync(preamble, ct).ConfigureAwait(false);
        await destination.WriteAsync(headerBytes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lee el preámbulo y el header, y devuelve además los bytes exactos del JSON: el AAD se
    /// calcula sobre ellos y no sobre una reserialización.
    /// </summary>
    private static async Task<(SealedHeader Header, byte[] HeaderBytes)> ReadHeaderCoreAsync(
        Stream sealedInput,
        CancellationToken ct)
    {
        var preamble = new byte[SealedContainerFormat.PreambleLength];
        try
        {
            await sealedInput.ReadExactlyAsync(preamble, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new SealedFormatException("No es un sobre ForZip válido.", ex);
        }

        if (!preamble.AsSpan(0, SealedContainerFormat.MagicLength).SequenceEqual(SealedContainerFormat.MagicBytes))
        {
            throw new SealedFormatException("No es un sobre ForZip válido.");
        }

        var formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(
            preamble.AsSpan(SealedContainerFormat.MagicLength));

        if (formatVersion > SealedContainerFormat.FormatVersion)
        {
            throw new SealedFormatException(
                $"El sobre usa la versión de formato {formatVersion} y esta versión de ForZip solo lee hasta la " +
                $"{SealedContainerFormat.FormatVersion}. Hace falta una versión más nueva de ForZip para abrirlo.");
        }

        if (formatVersion != SealedContainerFormat.FormatVersion)
        {
            throw new SealedFormatException($"El sobre declara una versión de formato desconocida ({formatVersion}).");
        }

        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(
            preamble.AsSpan(SealedContainerFormat.MagicLength + sizeof(ushort)));

        if (headerLength == 0 || headerLength > SealedContainerFormat.MaxHeaderLength)
        {
            throw new SealedFormatException("El encabezado del sobre declara una longitud inválida.");
        }

        var headerBytes = new byte[headerLength];
        try
        {
            await sealedInput.ReadExactlyAsync(headerBytes, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new SealedFormatException("El encabezado del sobre está truncado.", ex);
        }

        return (SealedHeader.Parse(headerBytes), headerBytes);
    }

    /// <summary>
    /// Lee el ciphertext y el tag de un chunk. Que falten bytes significa archivo truncado.
    /// </summary>
    private static async Task ReadChunkAsync(
        Stream sealedInput,
        Memory<byte> cipher,
        byte[] tag,
        long index,
        CancellationToken ct)
    {
        try
        {
            await sealedInput.ReadExactlyAsync(cipher, ct).ConfigureAwait(false);
            await sealedInput.ReadExactlyAsync(tag, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new SealedFormatException(
                $"El sobre está truncado: faltan datos a partir del bloque {index}.", ex);
        }
    }

    /// <summary>
    /// Traduce el fallo del tag GCM según §5: en el chunk 0 la causa habitual es la
    /// contraseña; a partir del 1, la clave ya se demostró correcta y lo que cambió es el
    /// archivo.
    /// </summary>
    private static Exception DescribeTagFailure(long index, CryptographicException inner)
    {
        if (index == 0)
        {
            return new SealedPasswordException(
                "No se pudo abrir el sobre: la contraseña es incorrecta o el primer bloque fue alterado.",
                inner);
        }

        return new SealedIntegrityException(
            index,
            $"El sobre fue alterado: falla la verificación del bloque {index}.",
            inner);
    }

    /// <summary>
    /// Ejecuta una operación que escribe un archivo de salida y garantiza que, ante error o
    /// cancelación, no quede el archivo parcial en disco.
    /// </summary>
    private static async Task<SealedHeader> RunToFileAsync(
        string outputPath,
        Func<Stream, Task<SealedHeader>> operation)
    {
        try
        {
            await using var destination = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var header = await operation(destination).ConfigureAwait(false);

            // Al disco antes de dar el archivo por escrito: la prueba de apertura que sigue al sellado tiene que
            // leer lo que quedó guardado, no solo lo que está en el caché del sistema.
            destination.Flush(flushToDisk: true);
            return header;
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // El archivo parcial quedó bloqueado por otro proceso: no hay nada más que hacer
            // acá y ocultar la excepción original sería peor.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
