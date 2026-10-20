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
using PrZip;

namespace ForZip.Core.Services;

/// <summary>
/// Empaquetado en 7z con el motor propio, con las mismas garantías que el ZIP: cada archivo se lee una
/// sola vez y alimenta a la vez los hashes y el compresor; lo que el sistema no deja leer o ya no está
/// queda documentado como omitido; las carpetas vacías se conservan; un fallo o una cancelación no dejan
/// salida parcial. Sin modo sólido (un daño afecta solo a su archivo) y con todos los núcleos: el motor
/// comprime varios archivos a la vez y el resultado es el mismo para cualquier cantidad de hilos.
/// </summary>
internal static class Packager7z
{
    private const int BufferSize = 65536;

    public static async Task<CompressionResult> CompressAsync(
        ZipOptions options,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        var skippedLinks = new List<OmittedFile>();
        var files = EvidenceSelection.EnumerateSourceFiles(options.SourcePaths, options.SelectionSnapshot, skippedLinks);
        var emptyDirs = EvidenceSelection.EnumerateEmptyDirectories(options.SourcePaths);
        var omittedFiles = EvidenceSelection.FindMissingSinceSelection(options, files);
        omittedFiles.AddRange(skippedLinks);
        var counter = new ProgressCounter(files.Sum(f => f.Size), progress);
        var hasPassword = !string.IsNullOrEmpty(options.Password);

        var writerOptions = new WriterOptions7z
        {
            Method = CompressionMethod.Lzma2,
            Level = options.CompressionLevel,
            Threads = 0,
            Password = hasPassword ? options.Password : null,
            // Con contraseña se cifran también los nombres: sin ella no se ve ni qué contiene el paquete.
            EncryptHeaders = hasPassword,
            Solid = false
        };

        // El 7z escribe su firma en los primeros 32 bytes al cerrar: con división, el primer volumen
        // queda abierto hasta el final para poder reescribirlo.
        SplittingWriteStream? splitter = options.SplitSize.HasValue
            ? new SplittingWriteStream(options.OutputPath, options.SplitSize.Value, rewritableFirstVolume: true)
            : null;
        Stream output = (Stream?)splitter ?? new FileStream(
            options.OutputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize);

        var packed = new List<(EvidenceSelection.SourceFile File, EvidenceReadStream Stream)>();
        bool success = false;
        try
        {
            await Task.Run(() =>
            {
                using var writer = Writer7z.Create(output, writerOptions);
                foreach (var dir in emptyDirs)
                {
                    ct.ThrowIfCancellationRequested();
                    writer.Add(new WriterEntry7z { Name = dir.TrimEnd('/'), IsDirectory = true });
                }

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    // Se abre ahora: si el antivirus niega la lectura queda afuera y documentado.
                    var inFs = EvidenceSelection.TryOpenEvidence(file, omittedFiles);
                    if (inFs == null)
                    {
                        counter.Add(file.Size);
                        continue;
                    }

                    var hashers = options.HashAlgorithms.Count > 0 ? EvidenceSelection.CreateHashers(options.HashAlgorithms) : null;
                    var evidence = new EvidenceReadStream(inFs, file.FullPath, hashers, counter, ct);
                    packed.Add((file, evidence));
                    writer.AddOwned(new WriterEntry7z { Name = file.EntryName, LastWriteTime = file.ModifiedUtc }, evidence);
                }

                writer.Finish();

                // Como en el ZIP (SharpZipLib lo exige al cerrar cada entrada): si un archivo cambió de tamaño
                // entre que se lo vio y que se lo leyó, el paquete no se da por bueno. Sin esto, el manifiesto
                // declaraba el tamaño visto con el hash de otro contenido.
                foreach (var (file, evidence) in packed)
                {
                    if (evidence.BytesRead != file.Size)
                    {
                        throw new IOException(
                            $"'{file.FullPath}' cambió mientras se empaquetaba ({file.Size} bytes al recorrer, " +
                            $"{evidence.BytesRead} al leer). Vuelva a empaquetar cuando nada lo esté modificando.");
                    }
                }
            }, ct).ConfigureAwait(false);
            success = true;
        }
        catch (Exception ex) when (ex is not SecuritySoftwareBlockedException
                                   && ex is not OperationCanceledException
                                   && SecuritySoftwareInterference.IsBlocked(ex))
        {
            throw new SecuritySoftwareBlockedException(SecurityBlockStage.WritingPackage, options.OutputPath, ex);
        }
        finally
        {
            try
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }
            catch when (!success)
            {
                // Fallos al cerrar tras un error o una cancelación: lo que se informa es lo original.
            }

            if (!success)
            {
                var partialFiles = splitter != null ? splitter.CreatedFiles : new[] { options.OutputPath };
                foreach (var partial in partialFiles)
                {
                    try
                    {
                        if (File.Exists(partial)) File.Delete(partial);
                    }
                    catch (IOException)
                    {
                        // Bloqueado un instante: se ignora, como en el ZIP.
                    }
                }
            }
        }

        var hashResults = new List<HashResult>();
        if (options.HashAlgorithms.Count > 0)
        {
            foreach (var (file, evidence) in packed)
            {
                hashResults.Add(new HashResult
                {
                    FilePath = file.EntryName,
                    FileSize = file.Size,
                    SourcePath = file.FullPath,
                    ModifiedUtc = file.ModifiedUtc,
                    Hashes = new Dictionary<HashAlgorithmType, string>(evidence.Hashes)
                });
            }
        }

        return new CompressionResult
        {
            FileHashes = hashResults,
            Volumes = splitter?.Volumes.ToList() ?? new List<VolumeInfo>(),
            OmittedFiles = omittedFiles
        };
    }

    /// <summary>
    /// Progreso por bytes leídos de la evidencia, sumado desde varios hilos. Avisa cada medio por mil del
    /// total (como mínimo cada 256 KB) y al completar: un aviso por bloque leído, desde varios hilos a la
    /// vez, inunda la interfaz y puede llegar desordenado.
    /// </summary>
    internal sealed class ProgressCounter(long total, IProgress<(long bytesProcessed, long totalBytes)>? progress)
    {
        private readonly long _step = Math.Max(total / 2000, 256 * 1024);
        private long _done;
        private long _reported;

        public void Add(long bytes)
        {
            long done = Interlocked.Add(ref _done, bytes);
            if (progress == null)
            {
                return;
            }

            // El aviso final sale siempre; los intermedios, de a uno por tramo.
            long reported = Interlocked.Read(ref _reported);
            if (done >= total
                || (done - reported >= _step && Interlocked.CompareExchange(ref _reported, done, reported) == reported))
            {
                progress.Report((done, total));
            }
        }
    }

    /// <summary>
    /// Lectura de un archivo de evidencia que calcula sus hashes y cuenta el progreso mientras el motor
    /// lo consume (desde un único hilo, aunque no sea el que lo abrió). Al cerrarse deja los hashes
    /// finales en <see cref="Hashes"/>. Un bloqueo del antivirus a mitad de lectura aborta el paquete,
    /// como en el ZIP: la entrada ya empezó y seguir dejaría un archivo truncado.
    /// </summary>
    internal sealed class EvidenceReadStream(
        FileStream inner,
        string path,
        Dictionary<HashAlgorithmType, HashAlgorithm>? hashers,
        ProgressCounter counter,
        CancellationToken ct) : Stream
    {
        private bool _disposed;

        public Dictionary<HashAlgorithmType, string> Hashes { get; } = new();

        /// <summary>Bytes efectivamente leídos (y hasheados) del archivo.</summary>
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ct.ThrowIfCancellationRequested();
            int n;
            try
            {
                n = inner.Read(buffer, offset, count);
            }
            catch (Exception ex) when (SecuritySoftwareInterference.IsBlocked(ex))
            {
                throw new SecuritySoftwareBlockedException(SecurityBlockStage.ReadingEvidence, path, ex);
            }

            if (n > 0)
            {
                if (hashers != null)
                {
                    foreach (var hasher in hashers.Values)
                    {
                        hasher.TransformBlock(buffer, offset, n, null, 0);
                    }
                }

                counter.Add(n);
                BytesRead += n;
            }

            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (hashers != null)
                {
                    foreach (var (algo, hasher) in hashers)
                    {
                        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        Hashes[algo] = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
                        hasher.Dispose();
                    }
                }

                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => true;

        // El motor consulta el largo para decidir si comprime el archivo en otro hilo; no se busca.
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
