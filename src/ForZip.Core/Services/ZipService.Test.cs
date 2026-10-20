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
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip;
using PrZip.Exceptions;
using PrZip.Formats;

namespace ForZip.Core.Services;

/// <summary>
/// Prueba de un archivo comprimido sin manifiesto (como <c>7z t</c>): se descomprime todo en memoria, sin escribir
/// nada en disco, y se comprueba el control de cada archivo. Sirve para lo que llega de terceros: un ZIP, un 7z, un
/// RAR, un TAR o un autoextraíble, que se lee sin ejecutarlo.
/// </summary>
public partial class ZipService
{
    /// <summary>
    /// Formato de un archivo comprimido que ForZip sabe probar, o null si no lo es (un .exe cualquiera, un documento).
    /// "7z" incluye el autoextraíble 7z; "rar", el autoextraíble RAR.
    /// </summary>
    public static string? IdentifyArchive(string path)
    {
        try
        {
            IReadOnlyList<string> segments;
            try
            {
                segments = SplitArchive.ResolveSegments(path);
            }
            catch (FileNotFoundException)
            {
                // Un juego de volúmenes con un hueco sigue siendo un archivo comprimido: la prueba dirá qué falta.
                segments = new[] { path };
            }

            if (segments.Count == 0 || !File.Exists(segments[0]))
            {
                return null;
            }

            if (ArchiveFormats.Detect(segments[0]) == ArchiveFormat.SevenZ)
            {
                return "7z";
            }

            if (segments.Count == 1 && ArchiveReader.DetectFormat(segments[0]) is { } other)
            {
                return other;
            }

            return StartsLikeZip(segments[0]) ? "ZIP" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<ArchiveTestResult> TestAsync(
        string path,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct,
        IProgress<string>? entryProgress = null)
    {
        var result = new ArchiveTestResult();
        var pw = string.IsNullOrEmpty(password) ? null : password;

        IReadOnlyList<string> segments;
        try
        {
            segments = SplitArchive.ResolveSegments(path);
        }
        catch (FileNotFoundException ex)
        {
            // Un volumen del medio que falta: se dice cuál, en vez de reportar un archivo "dañado".
            result.Outcome = ArchiveTestOutcome.Unreadable;
            result.Error = ex.Message;
            return result;
        }

        if (segments.Count == 0 || !File.Exists(segments[0]))
        {
            result.Outcome = ArchiveTestOutcome.Unreadable;
            result.Error = $"No se encontró el archivo: {path}";
            return result;
        }

        var format = IdentifyArchive(segments[0]) ?? (segments.Count > 1 ? "ZIP" : null);
        if (format == null)
        {
            result.Outcome = ArchiveTestOutcome.Unreadable;
            result.Error = "No es un archivo comprimido que ForZip reconozca (ZIP, 7z, RAR, TAR, GZ, BZ2, XZ, ISO o un " +
                           "autoextraíble 7z o RAR).";
            return result;
        }

        result.Format = format;
        result.IsSelfExtracting = StartsWithMz(segments[0]);

        bool anyEncryptedPassed = false;
        try
        {
            await Task.Run(() =>
            {
                switch (format)
                {
                    case "7z":
                        Test7z(segments, pw, result, progress, entryProgress, ref anyEncryptedPassed, ct);
                        break;
                    case "ZIP":
                        TestZip(segments, pw, result, progress, entryProgress, ref anyEncryptedPassed, ct);
                        break;
                    default:
                        TestOther(segments[0], pw, result, progress, entryProgress, ct);
                        break;
                }
            }, ct);

            result.Outcome = result.Damaged.Count > 0 ? ArchiveTestOutcome.Damaged
                : format is "tar" or "iso" ? ArchiveTestOutcome.NoChecks
                : ArchiveTestOutcome.NoErrors;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsPasswordProblem(ex) && !anyEncryptedPassed)
        {
            result.Outcome = pw == null ? ArchiveTestOutcome.PasswordRequired : ArchiveTestOutcome.WrongPassword;
            result.PasswordVerifiedWrong = pw != null && ex is PasswordIncorrectException;
            result.Error = ex.Message;
        }
        catch (Exception ex) when (ex is IOException and not EndOfStreamException || ex is UnauthorizedAccessException)
        {
            // Incluye un volumen de RAR que no está (FileNotFoundException): no es daño, falta una parte.
            result.Outcome = ArchiveTestOutcome.Unreadable;
            result.Error = ex.Message;
        }
        catch (Exception ex) when (ex is PrZipException or SharpZipBaseException or InvalidDataException
                                       or EndOfStreamException)
        {
            // El daño cortó la lectura (un TAR comprimido, un encabezado roto): lo probado antes no alcanza para
            // decir que el archivo está sano.
            result.Outcome = ArchiveTestOutcome.Damaged;
            result.Error = segments.Count > 1
                ? $"{ex.Message} (si falta el último volumen, el efecto es el mismo)"
                : ex.Message;
        }

        // La huella de lo que se probó, para anotarla: es lo único que después dice que es el mismo archivo.
        foreach (var file in segments)
        {
            ct.ThrowIfCancellationRequested();
            result.ArchiveFiles.Add((Path.GetFileName(file), new FileInfo(file).Length, await Sha256Async(file, ct)));
        }

        return result;
    }

    /// <summary>
    /// La contraseña falta o no abre el archivo. Igual que al extraer: si el primer archivo cifrado ya falla, lo más
    /// probable es la contraseña; pasado uno que se abrió bien, un fallo es daño.
    /// </summary>
    private static bool IsPasswordProblem(Exception ex) =>
        ex is InvalidPasswordOrCorruptDataException
        || ex is ZipException && (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                                  || ex.Message.Contains("contraseña", StringComparison.OrdinalIgnoreCase));

    private static void Test7z(
        IReadOnlyList<string> segments, string? password, ArchiveTestResult result,
        IProgress<(long, long)>? progress, IProgress<string>? entryProgress, ref bool anyEncryptedPassed,
        CancellationToken ct)
    {
        using var archive = Reading7z.Open(segments, password);
        long total = archive.Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
        long done = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                continue;
            }

            var name = Reading7z.EntryName(entry);
            entryProgress?.Report(name);
            try
            {
                archive.Extract(entry, new Reading7z.ProgressWriteStream(Stream.Null, n =>
                {
                    done += n;
                    progress?.Report((done, total));
                }, ct));
            }
            catch (PrZipException ex) when (
                !(ex is InvalidPasswordOrCorruptDataException && entry.IsEncrypted && !anyEncryptedPassed))
            {
                // Cada archivo de un 7z va por separado (ForZip no usa modo sólido; uno de terceros puede): el daño
                // de uno se informa y se sigue con los demás.
                result.Damaged.Add(new DamagedEntry(name, entry.Size, ex.Message));
                continue;
            }

            anyEncryptedPassed |= entry.IsEncrypted;
            result.Entries.Add(new TestedEntry(name, entry.Size));
        }
    }

    private static void TestZip(
        IReadOnlyList<string> segments, string? password, ArchiveTestResult result,
        IProgress<(long, long)>? progress, IProgress<string>? entryProgress, ref bool anyEncryptedPassed,
        CancellationToken ct)
    {
        Stream archiveStream = segments.Count == 1
            ? new FileStream(segments[0], FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize)
            : new ConcatenatedReadStream(segments);

        using var zipFile = new ZipFile(archiveStream) { IsStreamOwner = true };
        if (password != null)
        {
            zipFile.Password = password;
        }

        long total = 0;
        foreach (ZipEntry e in zipFile)
        {
            if (e.IsFile)
            {
                total += e.Size;
            }
        }

        long done = 0;
        var buffer = new byte[BufferSize];
        foreach (ZipEntry entry in zipFile)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.IsFile)
            {
                continue;
            }

            entryProgress?.Report(entry.Name);
            var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
            long written = 0;
            try
            {
                using var input = zipFile.GetInputStream(entry);
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    crc.Update(new ArraySegment<byte>(buffer, 0, n));
                    written += n;
                    done += n;
                    progress?.Report((done, total));
                }

                CheckEntryIntegrity(entry, crc.Value, written);
            }
            catch (Exception ex) when (ex is SharpZipBaseException or InvalidDataException
                                       && !(entry.IsCrypted && !anyEncryptedPassed))
            {
                result.Damaged.Add(new DamagedEntry(entry.Name, entry.Size >= 0 ? entry.Size : null, ex.Message));
                continue;
            }

            anyEncryptedPassed |= entry.IsCrypted;
            result.Entries.Add(new TestedEntry(entry.Name, written));
        }
    }

    private static void TestOther(
        string path, string? password, ArchiveTestResult result,
        IProgress<(long, long)>? progress, IProgress<string>? entryProgress, CancellationToken ct)
    {
        using var reader = ArchiveReader.TryOpen(path, password)
            ?? throw new InvalidDataException("Formato de archivo no reconocido.");
        result.Format = reader.FormatName;

        while (reader.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Current;
            if (entry.Kind == ArchiveEntryKind.Directory)
            {
                continue;
            }

            if (entry.Kind != ArchiveEntryKind.File)
            {
                long? size = entry.Kind == ArchiveEntryKind.AlternateStream ? entry.Size : null;
                result.Skipped.Add(new SkippedEntry(entry.Name, entry.Kind.ToString(), entry.LinkTarget, size));
                continue;
            }

            entryProgress?.Report(entry.Name);
            long written = 0;
            try
            {
                reader.CopyCurrentTo(new Reading7z.ProgressWriteStream(Stream.Null, n =>
                {
                    written += n;
                    progress?.Report((reader.BytesRead, reader.ArchiveLength));
                }, ct));
            }
            catch (PrZipException ex) when (
                HasPerEntryCheck(reader.FormatName)
                && !(ex is InvalidPasswordOrCorruptDataException && result.Entries.Count == 0))
            {
                // Solo donde cada archivo trae su propio control (RAR): en un TAR comprimido o un ISO, un fallo es
                // del flujo entero y corta la prueba.
                result.Damaged.Add(new DamagedEntry(entry.Name, entry.Size, ex.Message));
                continue;
            }

            result.Entries.Add(new TestedEntry(entry.Name, written));
        }

        // El último MoveNext ya leyó hasta el final y verificó el control del comprimido (GZ, BZ2, XZ).
        progress?.Report((reader.ArchiveLength, reader.ArchiveLength));
    }

    private static bool StartsWithMz(string path)
    {
        Span<byte> head = stackalloc byte[2];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        return fs.ReadAtLeast(head, 2, throwOnEndOfStream: false) == 2 && head[0] == 'M' && head[1] == 'Z';
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
    }
}
