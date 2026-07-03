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

namespace ForZip.Core.Services;

/// <summary>
/// Stream de solo escritura que reparte los bytes recibidos en volúmenes de tamaño fijo
/// (<c>base.001</c>, <c>base.002</c>, …), estilo 7-Zip. Calcula el SHA-256 de cada volumen
/// de forma incremental durante la escritura, evitando releer los segmentos.
/// <para/>
/// El flujo lógico (la concatenación de todos los volúmenes) es un ZIP estándar válido:
/// este stream no interpreta el contenido, solo corta por offset de byte.
/// </summary>
public sealed class SplittingWriteStream : Stream
{
    private const int FileBufferSize = 65536;

    private readonly string _basePath;
    private readonly long _splitSize;
    private readonly List<VolumeInfo> _volumes = new();
    private readonly List<string> _createdFiles = new();

    private FileStream? _current;
    private IncrementalHash? _hash;
    private long _currentLength;
    private int _volumeIndex; // 0 = ninguno abierto todavía
    private bool _finalized;

    public SplittingWriteStream(string basePath, long splitSize)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Ruta base requerida.", nameof(basePath));
        }
        if (splitSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(splitSize), "El tamaño de volumen debe ser positivo.");
        }

        _basePath = basePath;
        _splitSize = splitSize;
    }

    /// <summary>Volúmenes generados (con nombre, tamaño y SHA-256). Válido tras cerrar el stream.</summary>
    public IReadOnlyList<VolumeInfo> Volumes => _volumes;

    /// <summary>Rutas de todos los segmentos creados (para limpieza ante cancelación/error).</summary>
    public IReadOnlyList<string> CreatedFiles => _createdFiles;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
        => WriteCore(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer) => WriteCore(buffer);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remaining = buffer;
        while (!remaining.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCurrentOpen();

            long capacity = _splitSize - _currentLength;
            int take = (int)Math.Min(capacity, remaining.Length);

            var slice = remaining[..take];
            await _current!.WriteAsync(slice, cancellationToken).ConfigureAwait(false);
            _hash!.AppendData(slice.Span);
            _currentLength += take;
            remaining = remaining[take..];

            if (_currentLength >= _splitSize)
            {
                CloseCurrentVolume();
            }
        }
    }

    private void WriteCore(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            EnsureCurrentOpen();

            long capacity = _splitSize - _currentLength;
            int take = (int)Math.Min(capacity, buffer.Length);

            var slice = buffer[..take];
            _current!.Write(slice);
            _hash!.AppendData(slice);
            _currentLength += take;
            buffer = buffer[take..];

            if (_currentLength >= _splitSize)
            {
                CloseCurrentVolume();
            }
        }
    }

    private void EnsureCurrentOpen()
    {
        if (_current != null)
        {
            return;
        }

        _volumeIndex++;
        var path = SplitArchive.GetVolumePath(_basePath, _volumeIndex);
        _current = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, FileBufferSize);
        _createdFiles.Add(path);
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _currentLength = 0;
    }

    private void CloseCurrentVolume()
    {
        if (_current == null)
        {
            return;
        }

        var path = _current.Name;
        _current.Flush();
        _current.Dispose();

        var digest = Convert.ToHexString(_hash!.GetHashAndReset()).ToLowerInvariant();
        _hash.Dispose();
        _hash = null;

        _volumes.Add(new VolumeInfo
        {
            FileName = Path.GetFileName(path),
            Size = _currentLength,
            Sha256 = digest
        });

        _current = null;
        _currentLength = 0;
    }

    public override void Flush() => _current?.Flush();

    /// <summary>Cierra el volumen en curso y consolida la lista de volúmenes.</summary>
    public void Finish()
    {
        if (_finalized)
        {
            return;
        }
        CloseCurrentVolume();
        _finalized = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Finish();
        }
        base.Dispose(disposing);
    }

    // Operaciones no soportadas en un stream de solo escritura segmentado.
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
