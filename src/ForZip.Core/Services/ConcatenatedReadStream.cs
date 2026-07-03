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

namespace ForZip.Core.Services;

/// <summary>
/// Presenta una secuencia ordenada de segmentos en disco como un único flujo de lectura
/// continuo y <b>seekable</b>. Lo necesita <c>ZipFile</c>, que salta al final del flujo
/// para leer el directorio central. Solo mantiene abierto el segmento que se está leyendo.
/// </summary>
public sealed class ConcatenatedReadStream : Stream
{
    private readonly string[] _paths;
    private readonly long[] _lengths;
    private readonly long[] _startOffsets; // offset lógico donde comienza cada segmento
    private readonly long _totalLength;

    private long _position;
    private int _openIndex = -1;
    private FileStream? _openFile;

    public ConcatenatedReadStream(IReadOnlyList<string> segmentPaths)
    {
        if (segmentPaths == null || segmentPaths.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos un segmento.", nameof(segmentPaths));
        }

        _paths = segmentPaths.ToArray();
        _lengths = new long[_paths.Length];
        _startOffsets = new long[_paths.Length];

        long running = 0;
        for (int i = 0; i < _paths.Length; i++)
        {
            if (!File.Exists(_paths[i]))
            {
                throw new FileNotFoundException(
                    $"Falta el volumen {i + 1} de {_paths.Length}: {Path.GetFileName(_paths[i])}", _paths[i]);
            }
            _startOffsets[i] = running;
            _lengths[i] = new FileInfo(_paths[i]).Length;
            running += _lengths[i];
        }
        _totalLength = running;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _totalLength;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _totalLength || buffer.IsEmpty)
        {
            return 0;
        }

        int segment = FindSegment(_position);
        EnsureOpen(segment);

        long offsetInSegment = _position - _startOffsets[segment];
        if (_openFile!.Position != offsetInSegment)
        {
            _openFile.Seek(offsetInSegment, SeekOrigin.Begin);
        }

        // Leemos como mucho hasta el final del segmento actual; el llamador volverá a
        // llamar para cruzar al siguiente. Esto mantiene la semántica de Stream.Read.
        long bytesLeftInSegment = _lengths[segment] - offsetInSegment;
        int toRead = (int)Math.Min(buffer.Length, bytesLeftInSegment);

        int read = _openFile.Read(buffer[..toRead]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _totalLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0)
        {
            throw new IOException("Intento de posicionarse antes del inicio del flujo.");
        }

        _position = target;
        return _position;
    }

    private int FindSegment(long logicalPosition)
    {
        // Pocos segmentos en la práctica: búsqueda lineal es suficiente y clara.
        for (int i = _paths.Length - 1; i >= 0; i--)
        {
            if (logicalPosition >= _startOffsets[i])
            {
                return i;
            }
        }
        return 0;
    }

    private void EnsureOpen(int segment)
    {
        if (_openIndex == segment && _openFile != null)
        {
            return;
        }

        _openFile?.Dispose();
        _openFile = new FileStream(_paths[segment], FileMode.Open, FileAccess.Read, FileShare.Read);
        _openIndex = segment;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _openFile?.Dispose();
            _openFile = null;
        }
        base.Dispose(disposing);
    }
}
