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
using ForZip.Core.Models;
using Microsoft.Win32.SafeHandles;
using PrZip;
using PrZip.Primitives.IO;

namespace ForZip.Core.Services;

/// <summary>
/// Lectura de paquetes 7z para extraer y verificar: archivo único o dividido en volúmenes (.001, .002, …),
/// nombres de entrada con separador POSIX como en el manifiesto, y destinos que cuentan progreso o hashean.
/// El motor verifica además el CRC de cada archivo al extraerlo.
/// </summary>
internal static class Reading7z
{
    public static Archive7z Open(IReadOnlyList<string> segments, string? password)
    {
        string? pw = string.IsNullOrEmpty(password) ? null : password;
        try
        {
            return OpenCore(segments, pw);
        }
        catch (PrZip.Exceptions.PrZipException ex) when (pw != null && ex is not PrZip.Exceptions.InvalidPasswordOrCorruptDataException)
        {
            // Con los nombres cifrados (los de ForZip, o -mhe=on de 7-Zip), una contraseña equivocada deja el encabezado
            // ilegible y el motor lo informa como daño. Con una contraseña escrita no se puede saber cuál de las dos
            // es: se dice así, en vez de "archivo dañado".
            throw new PrZip.Exceptions.InvalidPasswordOrCorruptDataException(
                "La contraseña no abre el archivo, o su encabezado está dañado: no se pueden distinguir.", ex);
        }
    }

    private static Archive7z OpenCore(IReadOnlyList<string> segments, string? pw)
    {
        if (segments.Count == 1)
        {
            return Archive7z.Open(segments[0], pw);
        }

        var source = new SegmentedSource(segments);
        try
        {
            return Archive7z.Open(source, pw);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Nombre de la entrada con '/' como separador, igual que en el manifiesto.</summary>
    public static string EntryName(Entry7z entry) => entry.Name.Replace('\\', '/');

    /// <summary>
    /// Los volúmenes como un único origen de lectura posicional. Cada lectura va directo al segmento que
    /// corresponde (sin posición compartida), así que admite lecturas desde varios hilos.
    /// </summary>
    internal sealed class SegmentedSource : IRandomAccessSource
    {
        private readonly SafeFileHandle[] _handles;
        private readonly long[] _starts;

        public SegmentedSource(IReadOnlyList<string> segments)
        {
            _handles = new SafeFileHandle[segments.Count];
            _starts = new long[segments.Count];
            long offset = 0;
            try
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    _handles[i] = File.OpenHandle(segments[i], FileMode.Open, FileAccess.Read, FileShare.Read);
                    _starts[i] = offset;
                    offset += RandomAccess.GetLength(_handles[i]);
                }
            }
            catch
            {
                Dispose();
                throw;
            }

            Length = offset;
        }

        public long Length { get; }

        public int Read(long offset, Span<byte> destination)
        {
            if (offset >= Length || destination.IsEmpty)
            {
                return 0;
            }

            int index = Array.BinarySearch(_starts, offset);
            if (index < 0)
            {
                index = ~index - 1;
            }

            // Un volumen vacío comparte inicio con el siguiente: se avanza hasta uno con datos.
            while (index < _handles.Length - 1 && _starts[index + 1] <= offset)
            {
                index++;
            }

            int total = 0;
            while (!destination.IsEmpty && index < _handles.Length)
            {
                int read = RandomAccess.Read(_handles[index], destination, offset - _starts[index]);
                if (read == 0)
                {
                    index++;
                    continue;
                }

                total += read;
                offset += read;
                destination = destination[read..];
            }

            return total;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, destination.Span));
        }

        public void Dispose()
        {
            foreach (var handle in _handles)
            {
                handle?.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Destino que cuenta los bytes escritos y corta si se cancela.</summary>
    internal sealed class ProgressWriteStream(Stream inner, Action<int> onWrite, CancellationToken ct) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            inner.Write(buffer);
            onWrite(buffer.Length);
        }

        public override void WriteByte(byte value) => Write([value]);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>Destino que solo calcula el hash de lo que recibe.</summary>
    internal sealed class HashingWriteStream : Stream
    {
        private readonly IncrementalHash _hash;
        private readonly Action<int> _onWrite;
        private readonly CancellationToken _ct;

        public HashingWriteStream(HashAlgorithmType algorithm, Action<int> onWrite, CancellationToken ct)
        {
            _hash = IncrementalHash.CreateHash(algorithm switch
            {
                HashAlgorithmType.MD5 => HashAlgorithmName.MD5,
                HashAlgorithmType.SHA1 => HashAlgorithmName.SHA1,
                HashAlgorithmType.SHA256 => HashAlgorithmName.SHA256,
                HashAlgorithmType.SHA512 => HashAlgorithmName.SHA512,
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Algoritmo no soportado.")
            });
            _onWrite = onWrite;
            _ct = ct;
        }

        /// <summary>Hash en hexadecimal minúscula de todo lo recibido.</summary>
        public string FinishHex() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _ct.ThrowIfCancellationRequested();
            _hash.AppendData(buffer);
            _onWrite(buffer.Length);
        }

        public override void WriteByte(byte value) => Write([value]);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
