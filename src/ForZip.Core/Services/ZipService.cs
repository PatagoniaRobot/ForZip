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
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip;
using PrZip;
using PrZip.Exceptions;
using PrZip.Formats;

namespace ForZip.Core.Services;

public partial class ZipService : IZipService
{
    private const int BufferSize = 65536;
    // Tamaño mínimo de volumen al dividir: evita generar miles de archivos diminutos.
    private const long MinSplitSize = 64 * 1024;
    private static readonly int[] ValidLevels = { 0, 1, 3, 5, 7, 9 };

    public ZipService(IHashService hashService)
    {
        // El hashing se realiza ahora en un único pase durante el empaquetado, por lo
        // que ZipService ya no necesita IHashService. Se conserva el parámetro por
        // compatibilidad con la composición de dependencias existente (DI y tests).
        _ = hashService;
    }

    public async Task<CompressionResult> CompressAsync(
        ZipOptions options,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        ValidateCompressOptions(options);

        if (options.Format == ArchiveFormat.SevenZ)
        {
            return await Packager7z.CompressAsync(options, progress, ct);
        }

        var skippedLinks = new List<OmittedFile>();
        var files = EvidenceSelection.EnumerateSourceFiles(options.SourcePaths, options.SelectionSnapshot, skippedLinks);
        var emptyDirs = EvidenceSelection.EnumerateEmptyDirectories(options.SourcePaths);
        var totalBytes = files.Sum(f => f.Size);
        var hashResults = new List<HashResult>();

        // Lo que el operador seleccionó y ya no está —típicamente, puesto en cuarentena por el
        // antivirus entre la selección y el empaquetado— se documenta como omitido. Sin esto,
        // el paquete diría "completo" con menos archivos de los que se eligieron. Los enlaces que
        // no se siguieron también.
        var omittedFiles = EvidenceSelection.FindMissingSinceSelection(options, files);
        omittedFiles.AddRange(skippedLinks);

        // Un único pase: leemos cada archivo una sola vez y, con el mismo buffer,
        // alimentamos los algoritmos de hash y el stream de compresión. Esto evita
        // releer la evidencia (antes se leía dos veces) y reduce a la mitad el I/O.
        var hasHashing = options.HashAlgorithms.Count > 0;

        long packedBytes = 0;
        bool success = false;
        var hasPassword = !string.IsNullOrEmpty(options.Password);

        // Si se solicitó división, el ZIP se escribe a través de un stream que reparte los
        // bytes en volúmenes .001/.002/… y hashea cada uno; si no, a un FileStream normal.
        var splitEnabled = options.SplitSize.HasValue;
        Stream? baseOut = null;
        SplittingWriteStream? splitter = null;
        ZipOutputStream? zipStream = null;

        try
        {
            if (splitEnabled)
            {
                splitter = new SplittingWriteStream(options.OutputPath, options.SplitSize!.Value);
                baseOut = splitter;
            }
            else
            {
                baseOut = new FileStream(
                    options.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    BufferSize, useAsync: true);
            }

            zipStream = new ZipOutputStream(baseOut);
            zipStream.SetLevel(options.CompressionLevel);
            zipStream.UseZip64 = UseZip64.On;
            if (hasPassword)
            {
                zipStream.Password = options.Password;
            }

            // Entradas de directorio para preservar carpetas vacías (estructura fiel)
            foreach (var dirEntryName in emptyDirs)
            {
                ct.ThrowIfCancellationRequested();
                zipStream.PutNextEntry(new ZipEntry(dirEntryName));
                zipStream.CloseEntry();
            }

            var buffer = new byte[BufferSize];
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                // Se abre antes de crear la entrada: si el antivirus niega la lectura, el archivo
                // queda afuera y documentado, sin dejar en el ZIP una entrada a medio escribir.
                var inFs = EvidenceSelection.TryOpenEvidence(file, omittedFiles);
                if (inFs == null)
                {
                    packedBytes += file.Size;
                    progress?.Report((packedBytes, totalBytes));
                    continue;
                }

                // Timestamp en UTC para que el ZIP sea reproducible con independencia
                // de la zona horaria de la máquina que lo genera.
                var entry = new ZipEntry(file.EntryName)
                {
                    DateTime = file.ModifiedUtc.UtcDateTime,
                    Size = file.Size,
                    AESKeySize = hasPassword ? 256 : 0
                };

                zipStream.PutNextEntry(entry);

                var hashers = hasHashing ? EvidenceSelection.CreateHashers(options.HashAlgorithms) : null;
                try
                {
                    await using (inFs)
                    {
                        int n;
                        while ((n = await ReadEvidenceAsync(inFs, buffer, file.FullPath, ct)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (hashers != null)
                            {
                                foreach (var hasher in hashers.Values)
                                {
                                    hasher.TransformBlock(buffer, 0, n, null, 0);
                                }
                            }

                            await zipStream.WriteAsync(buffer.AsMemory(0, n), ct);
                            packedBytes += n;
                            progress?.Report((packedBytes, totalBytes));
                        }
                    }

                    if (hashers != null)
                    {
                        var result = new HashResult
                        {
                            FilePath = file.EntryName,
                            FileSize = file.Size,
                            SourcePath = file.FullPath,
                            ModifiedUtc = file.ModifiedUtc
                        };
                        foreach (var (algo, hasher) in hashers)
                        {
                            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                            result.Hashes[algo] = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
                        }
                        hashResults.Add(result);
                    }
                }
                finally
                {
                    if (hashers != null)
                    {
                        foreach (var hasher in hashers.Values)
                        {
                            hasher.Dispose();
                        }
                    }
                }

                zipStream.CloseEntry();
            }

            zipStream.Finish();
            success = true;
        }
        catch (Exception ex) when (ex is not SecuritySoftwareBlockedException
                                   && SecuritySoftwareInterference.IsBlocked(ex))
        {
            // Lo que no vino de leer la evidencia vino de escribir el paquete. Con división
            // no se sabe en qué volumen fue, así que se nombra el destino lógico.
            throw new SecuritySoftwareBlockedException(SecurityBlockStage.WritingPackage, options.OutputPath, ex);
        }
        finally
        {
            // Disposal defensivo: tras cancelación, Finish() interno de ZipOutputStream
            // detecta entradas incompletas y lanza ZipException que ocultaría la cancelación.
            // Al disponer zipStream se cierra también baseOut (incl. el último volumen del
            // splitter), por lo que sus volúmenes quedan consolidados a partir de aquí.
            if (zipStream != null)
            {
                try
                {
                    zipStream.Dispose();
                }
                catch when (!success)
                {
                    // Excepciones durante el cierre tras cancelación/error son irrelevantes
                }
            }

            if (baseOut != null)
            {
                try
                {
                    await baseOut.DisposeAsync();
                }
                catch when (!success)
                {
                    // Igual que arriba: silenciar fallos de cierre tras cancelación
                }
            }

            if (!success)
            {
                // Limpieza: borrar la salida parcial. Con división hay que borrar TODOS los
                // segmentos creados, no solo OutputPath (que ni siquiera existe como tal).
                var partialFiles = splitter != null
                    ? splitter.CreatedFiles
                    : new[] { options.OutputPath };

                foreach (var partial in partialFiles)
                {
                    try
                    {
                        if (File.Exists(partial)) File.Delete(partial);
                    }
                    catch (IOException)
                    {
                        // El archivo puede estar bloqueado momentáneamente; se ignora silenciosamente
                    }
                }
            }
        }

        return new CompressionResult
        {
            FileHashes = hashResults,
            Volumes = splitter?.Volumes.ToList() ?? new List<VolumeInfo>(),
            OmittedFiles = omittedFiles
        };
    }

    public async Task<ExtractionResult> DecompressAsync(
        string zipPath,
        string outputDir,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct,
        IProgress<string>? entryProgress = null)
    {
        // Detecta automáticamente si se trata de un archivo dividido (.001, .002, …): admite
        // tanto la ruta lógica (caso.zip) como el primer volumen (caso.zip.001).
        var segments = SplitArchive.ResolveSegments(zipPath);
        if (segments.Count == 1 && !File.Exists(segments[0]))
        {
            throw new FileNotFoundException("Archivo ZIP no encontrado.", zipPath);
        }

        // Una extracción que no termina (contraseña mala, daño que corta, cancelación) ya borraba sus archivos, pero
        // dejaba las carpetas que había creado, empezando por la de destino: una "caso (3)" vacía que parecía un
        // resultado. Ahora también se van esas, y solo esas: una carpeta que ya existía no se toca.
        var createdDirectories = new CreatedDirectories();
        createdDirectories.Ensure(outputDir);
        try
        {
            return await DecompressCoreAsync(segments, zipPath, outputDir, password, progress, ct, entryProgress, createdDirectories);
        }
        catch
        {
            createdDirectories.RemoveEmpty();
            throw;
        }
    }

    /// <summary>
    /// Las carpetas que creó una extracción, para quitarlas si no termina. Solo se borran vacías y de la más profunda a
    /// la menos: si alguien dejó algo adentro mientras tanto, queda.
    /// </summary>
    private sealed class CreatedDirectories
    {
        private readonly List<string> _created = new();

        /// <summary>Crea la carpeta y las que falten arriba de ella, y anota las que no existían.</summary>
        public void Ensure(string directory)
        {
            var missing = new Stack<string>();
            for (var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
                 !string.IsNullOrEmpty(current) && !Directory.Exists(current);
                 current = Path.GetDirectoryName(current))
            {
                missing.Push(current);
            }

            Directory.CreateDirectory(directory);
            _created.AddRange(missing);
        }

        public void RemoveEmpty()
        {
            foreach (var directory in _created.OrderByDescending(d => d.Length))
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Una carpeta que no se pudo borrar queda: es solo una carpeta vacía, no evidencia a medias.
                }
            }
        }
    }

    private async Task<ExtractionResult> DecompressCoreAsync(
        IReadOnlyList<string> segments,
        string zipPath,
        string outputDir,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct,
        IProgress<string>? entryProgress,
        CreatedDirectories createdDirectories)
    {
        var canonicalOutput = Path.GetFullPath(outputDir).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // El formato se reconoce por la firma del primer volumen, no por la extensión.
        if (ArchiveFormats.Detect(segments[0]) == ArchiveFormat.SevenZ)
        {
            return await Decompress7zAsync(segments, outputDir, canonicalOutput, password, progress, entryProgress, createdDirectories, ct);
        }

        // TAR, TAR.GZ/BZ2/XZ, GZ, BZ2, XZ, ISO y RAR: el lector común del motor. Los volúmenes de RAR
        // (.part1.rar…, .r00…) los resuelve el propio lector desde cualquier parte.
        if (segments.Count == 1 && ArchiveReader.DetectFormat(segments[0]) is { } other && other != "7z")
        {
            return await DecompressOtherFormatAsync(segments[0], outputDir, password, progress, entryProgress, createdDirectories, ct);
        }

        // Volúmenes .001, .002… que no empiezan como un ZIP: lo más probable es una imagen de disco partida (dd/raw,
        // muy común en pericias), no un paquete. Se dice eso en vez del error genérico de "ZIP inválido".
        if (segments.Count > 1 && !StartsLikeZip(segments[0]))
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(segments[0])}' no es el primer volumen de un ZIP o 7z dividido. Si es una imagen " +
                "de disco partida (dd/raw), ForZip no la extrae: ábrala con una herramienta de imágenes forenses.");
        }

        // ConcatenatedReadStream presenta los volúmenes como un único flujo seekable;
        // un archivo único se abre con un FileStream normal. ZipFile es dueño del stream.
        Stream archiveStream = segments.Count == 1
            ? new FileStream(segments[0], FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize)
            : new ConcatenatedReadStream(segments);

        using var zipFile = new ZipFile(archiveStream) { IsStreamOwner = true };
        if (!string.IsNullOrEmpty(password))
        {
            zipFile.Password = password;
        }

        long totalBytes = 0;
        foreach (ZipEntry e in zipFile)
        {
            if (e.IsFile)
            {
                totalBytes += e.Size;
            }
        }

        long processedBytes = 0;
        var spaceGuard = new ExtractionSpaceGuard(outputDir);
        var buffer = new byte[BufferSize];
        var createdFiles = new List<string>();
        var result = new ExtractionResult { Format = "ZIP" };
        var usedPaths = EntryPathSanitizer.CreatePathSet();
        var directoryRemap = new List<(string From, string To)>();
        bool success = false;

        // Si la primera entrada cifrada ya falla, lo más probable es la contraseña: eso aborta todo. Pasada una
        // entrada cifrada que se abrió bien, un fallo es daño de esa entrada.
        bool encryptedEntryExtracted = false;
        try
        {
            foreach (ZipEntry entry in zipFile)
            {
                ct.ThrowIfCancellationRequested();

                if (!entry.IsDirectory && !entry.IsFile)
                {
                    continue;
                }

                int renamedBefore = result.Renamed.Count;

                // Mismas reglas de nombres que el resto de los formatos: un ':' (que en NTFS escribiría un
                // flujo alternativo oculto dentro de otro archivo), un nombre reservado de Windows o una ruta
                // absoluta se sanean y quedan documentados, y dos entradas con el mismo nombre se conservan
                // las dos. Un ".." sigue abortando: en un ZIP es un intento de salir del destino (Zip Slip).
                var targetPath = ResolveEntryTarget(canonicalOutput, entry.Name, entry.IsDirectory, usedPaths, result);
                targetPath = PrepareTarget(
                    canonicalOutput, entry.Name, targetPath, entry.IsDirectory, directoryRemap, result, renamedBefore,
                    createdDirectories);

                // Entrada de directorio (incl. carpetas vacías): ya quedó creada
                if (entry.IsDirectory)
                {
                    continue;
                }

                entryProgress?.Report(entry.Name);
                var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
                long written = 0;
                try
                {
                    using var inStream = zipFile.GetInputStream(entry);
                    await using (var outStream = new FileStream(
                        targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        BufferSize, useAsync: true))
                    {
                        createdFiles.Add(targetPath);
                        int n;
                        while ((n = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            crc.Update(new ArraySegment<byte>(buffer, 0, n));
                            await outStream.WriteAsync(buffer.AsMemory(0, n), ct);
                            written += n;
                            processedBytes += n;
                            spaceGuard.Account(n);
                            progress?.Report((processedBytes, totalBytes));
                        }
                    }

                    CheckEntryIntegrity(entry, crc.Value, written);
                }
                catch (Exception ex) when (ex is SharpZipBaseException or InvalidDataException
                                           && !(entry.IsCrypted && !encryptedEntryExtracted))
                {
                    DiscardDamaged(targetPath, createdFiles, result, renamedBefore,
                        entry.Name, entry.Size >= 0 ? entry.Size : null, ex.Message);
                    continue;
                }

                encryptedEntryExtracted |= entry.IsCrypted;
                RestoreModifiedTime(targetPath, ZipEntryModifiedUtc(entry));
                CountExtracted(result, canonicalOutput, targetPath);
            }
            success = true;
            return result;
        }
        finally
        {
            if (!success)
            {
                foreach (var file in createdFiles)
                {
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch { /* Ignorar fallos de limpieza en cascada */ }
                }
            }
        }
    }

    /// <summary>
    /// Destino de una entrada, saneado con las mismas reglas para todos los formatos. Lo que cambia de nombre
    /// (saneado, una segunda entrada con el mismo nombre, o un archivo que ya estaba en el destino) queda en el
    /// resultado. Un ".." aborta la extracción, salvo con <paramref name="neutralizeParentReferences"/> (TAR y
    /// demás formatos de terceros, donde se reemplaza y se documenta).
    /// </summary>
    private static string ResolveEntryTarget(
        string root, string name, bool isDirectory, HashSet<string> usedPaths, ExtractionResult result,
        bool neutralizeParentReferences = false)
    {
        string target;
        bool renamed;
        try
        {
            target = EntryPathSanitizer.ResolveTarget(
                root, name, isDirectory, flatten: false, usedPaths, neutralizeParentReferences, out renamed);
        }
        catch (PrZip.Exceptions.CorruptArchiveException ex)
        {
            throw new InvalidOperationException($"Entrada fuera del directorio destino (Zip Slip): {name}", ex);
        }

        var reason = RenameReason.AlreadyInDestination;
        if (renamed)
        {
            EntryPathSanitizer.ToSafeRelativePath(
                name, OperatingSystem.IsWindows(), neutralizeParentReferences, out bool sanitized);
            reason = !sanitized ? RenameReason.Repeated
                : name.Split('/', '\\').Any(part => part.Length > MaxNameLength) ? RenameReason.TooLong
                : RenameReason.Invalid;
        }

        // Un archivo que ya estaba en el destino antes de extraer no se pisa: puede ser otra evidencia, y si la
        // extracción fallaba, la limpieza lo borraba como si fuera propio. El extraído va con otro nombre.
        if (!isDirectory && (File.Exists(target) || Directory.Exists(target)))
        {
            var dir = Path.GetDirectoryName(target) ?? root;
            var stem = Path.GetFileNameWithoutExtension(target);
            var ext = Path.GetExtension(target);
            for (int i = 2; ; i++)
            {
                var candidate = Path.Combine(dir, $"{stem}~{i}{ext}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate) && usedPaths.Add(candidate))
                {
                    target = candidate;
                    break;
                }
            }

            renamed = true;
        }

        if (renamed)
        {
            result.Renamed.Add(new RenamedEntry(name, Path.GetRelativePath(root, target).Replace('\\', '/'), reason));
        }

        return target;
    }

    /// <summary>Largo máximo de un nombre en NTFS (el mismo límite que aplica el saneador del motor).</summary>
    private const int MaxNameLength = 255;

    /// <summary>Cuenta un archivo extraído y anota si su ruta queda fuera del alcance del Explorador de Windows.</summary>
    private static void CountExtracted(ExtractionResult result, string root, string target)
    {
        result.FilesExtracted++;
        if (target.Length > ExtractionResult.ExplorerMaxPath)
        {
            result.LongPaths.Add(Path.GetRelativePath(root, target).Replace('\\', '/'));
        }
    }

    /// <summary>Formatos de terceros con un control por archivo, donde una entrada dañada no compromete a las demás.</summary>
    private static bool HasPerEntryCheck(string formatName) =>
        string.Equals(formatName, "rar", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Aparta una entrada que no pasó su control: se borra lo que se llegó a escribir (no puede quedar en el
    /// destino con su nombre, como si fuera buena), se deshace el renombrado que se había anotado para ella y se
    /// documenta. Si no se puede borrar, la extracción no sigue: un archivo dañado con su nombre no se deja.
    /// </summary>
    /// <summary>
    /// Crea las carpetas de una entrada y devuelve su destino final. Si una carpeta del camino ya existe como archivo
    /// (un ZIP con el archivo "k" y la carpeta "k/"), la carpeta va con otro nombre ("k~2") y todo lo que venía
    /// adentro la sigue; queda documentado como renombrado. Antes ese choque abortaba la extracción entera.
    /// </summary>
    private static string PrepareTarget(
        string root, string name, string target, bool isDirectory,
        List<(string From, string To)> directoryRemap, ExtractionResult result, int renamedBefore,
        CreatedDirectories createdDirectories)
    {
        var original = target;
        foreach (var (from, to) in directoryRemap)
        {
            if (target.Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                target = to;
            }
            else if (target.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                target = to + target[from.Length..];
            }
        }

        var directory = isDirectory ? target : Path.GetDirectoryName(target)!;
        var current = root;
        foreach (var part in Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".")
            {
                continue;
            }

            var next = Path.Combine(current, part);
            if (File.Exists(next))
            {
                var free = Enumerable.Range(2, int.MaxValue - 2)
                    .Select(i => $"{next}~{i}")
                    .First(c => !File.Exists(c) && !Directory.Exists(c));
                directoryRemap.Add((next, free));
                next = free;
            }

            current = next;
        }

        createdDirectories.Ensure(current);
        target = isDirectory ? current : Path.Combine(current, Path.GetFileName(target));

        if (!target.Equals(original, StringComparison.OrdinalIgnoreCase))
        {
            var savedAs = Path.GetRelativePath(root, target).Replace('\\', '/');
            if (result.Renamed.Count > renamedBefore)
            {
                result.Renamed[^1] = result.Renamed[^1] with { SavedAs = savedAs };
            }
            else
            {
                result.Renamed.Add(new RenamedEntry(name, savedAs, RenameReason.FolderIsFile));
            }
        }

        return target;
    }

    private static void DiscardDamaged(
        string target, List<string> createdFiles, ExtractionResult result, int renamedBefore,
        string name, long? expectedSize, string reason)
    {
        try
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"'{name}' está dañado y no se pudo apartar del destino ({ex.Message}). No se sigue extrayendo.", ex);
        }

        createdFiles.Remove(target);
        if (result.Renamed.Count > renamedBefore)
        {
            result.Renamed.RemoveRange(renamedBefore, result.Renamed.Count - renamedBefore);
        }

        result.Damaged.Add(new DamagedEntry(name, expectedSize, reason));
    }

    /// <summary>
    /// Deja en el archivo extraído la fecha de modificación que trae el archivo comprimido, cuando se la conoce sin
    /// ambigüedad. Antes todo quedaba con la fecha de la extracción. No poder fijarla no es un error: el contenido
    /// ya está verificado.
    /// </summary>
    private static void RestoreModifiedTime(string path, DateTimeOffset? modified)
    {
        if (modified is not { } time)
        {
            return;
        }

        try
        {
            File.SetLastWriteTimeUtc(path, time.UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    /// <summary>
    /// Fecha de una entrada ZIP solo si es inequívoca: la del campo extendido NTFS o Unix, que están en UTC. La
    /// fecha DOS de la cabecera no dice su zona horaria (los ZIP de ForZip guardan UTC, el Explorador de Windows
    /// guarda la hora local), así que sola no se usa: mejor la fecha de extracción que una corrida unas horas.
    /// </summary>
    private static DateTimeOffset? ZipEntryModifiedUtc(ZipEntry entry)
    {
        try
        {
            var extra = new ZipExtraData(entry.ExtraData);
            if (extra.GetData<NTTaggedData>() is { } nt && nt.LastModificationTime > DateTime.MinValue)
            {
                return new DateTimeOffset(DateTime.SpecifyKind(nt.LastModificationTime, DateTimeKind.Utc));
            }

            if (extra.GetData<ExtendedUnixData>() is { } unix
                && (unix.Include & ExtendedUnixData.Flags.ModificationTime) != 0)
            {
                return new DateTimeOffset(DateTime.SpecifyKind(unix.ModificationTime, DateTimeKind.Utc));
            }
        }
        catch (Exception ex) when (ex is ZipException or ArgumentException or IndexOutOfRangeException)
        {
            // Un campo extra mal formado no impide extraer: solo se queda sin fecha.
        }

        return null;
    }

    /// <summary>
    /// Verifica lo extraído de una entrada ZIP contra su tamaño y su CRC32. SharpZipLib no lo hace al leer
    /// con <c>ZipFile</c>: sin esto, un archivo almacenado con un byte cambiado se extraía sin aviso. Las
    /// entradas AES (AE-2) no guardan CRC; las protege su código de autenticación, que verifica la lectura.
    /// </summary>
    private static void CheckEntryIntegrity(ZipEntry entry, long crc, long written)
    {
        if (entry.Size >= 0 && written != entry.Size)
        {
            throw new InvalidDataException(
                $"'{entry.Name}' dio {written} bytes en lugar de {entry.Size}: el ZIP está dañado o fue alterado.");
        }

        if (entry.AESKeySize == 0 && entry.HasCrc && (crc & 0xFFFFFFFFL) != (entry.Crc & 0xFFFFFFFFL))
        {
            throw new InvalidDataException(
                $"La suma CRC32 de '{entry.Name}' no coincide: el ZIP está dañado o fue alterado.");
        }
    }

    /// <summary>
    /// Extracción de un 7z con las mismas reglas que el ZIP: nombres saneados, nada fuera del destino, carpetas
    /// vacías recreadas y, ante un error o una cancelación, se borra lo que se llegó a escribir. El motor
    /// verifica el CRC de cada archivo y falla si no coincide.
    /// </summary>
    private static async Task<ExtractionResult> Decompress7zAsync(
        IReadOnlyList<string> segments,
        string outputDir,
        string canonicalOutput,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        IProgress<string>? entryProgress,
        CreatedDirectories createdDirectories,
        CancellationToken ct)
    {
        var createdFiles = new List<string>();
        var result = new ExtractionResult { Format = "7z" };
        bool success = false;
        try
        {
            await Task.Run(() =>
            {
                using var archive = Reading7z.Open(segments, password);
                long totalBytes = archive.Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
                long processedBytes = 0;
                var spaceGuard = new ExtractionSpaceGuard(outputDir);
                var usedPaths = EntryPathSanitizer.CreatePathSet();
                var directoryRemap = new List<(string From, string To)>();
                bool encryptedEntryExtracted = false;

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    var name = Reading7z.EntryName(entry);
                    int renamedBefore = result.Renamed.Count;
                    var targetPath = ResolveEntryTarget(canonicalOutput, name, entry.IsDirectory, usedPaths, result);
                    targetPath = PrepareTarget(
                        canonicalOutput, name, targetPath, entry.IsDirectory, directoryRemap, result, renamedBefore,
                        createdDirectories);

                    if (entry.IsDirectory)
                    {
                        continue;
                    }

                    entryProgress?.Report(name);
                    try
                    {
                        using var outStream = new FileStream(
                            targetPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);
                        createdFiles.Add(targetPath);
                        archive.Extract(entry, new Reading7z.ProgressWriteStream(outStream, n =>
                        {
                            processedBytes += n;
                            spaceGuard.Account(n);
                            progress?.Report((processedBytes, totalBytes));
                        }, ct));
                    }
                    catch (PrZipException ex) when (
                        !(ex is InvalidPasswordOrCorruptDataException && entry.IsEncrypted && !encryptedEntryExtracted))
                    {
                        // El motor verifica el CRC de cada archivo: uno que no coincide (o que no se puede
                        // descomprimir) se aparta y la extracción sigue. En un 7z sólido el daño puede arrastrar a
                        // los que siguen en el mismo bloque: también quedan apartados y documentados.
                        DiscardDamaged(targetPath, createdFiles, result, renamedBefore, name, entry.Size, ex.Message);
                        continue;
                    }

                    encryptedEntryExtracted |= entry.IsEncrypted;
                    RestoreModifiedTime(targetPath, entry.LastWriteTime);
                    CountExtracted(result, canonicalOutput, targetPath);
                }
            }, ct);
            success = true;
            return result;
        }
        finally
        {
            if (!success)
            {
                foreach (var file in createdFiles)
                {
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch { /* Ignorar fallos de limpieza en cascada */ }
                }
            }
        }
    }

    /// <summary>
    /// Formatos que llegan como evidencia de terceros. A diferencia del ZIP, un nombre peligroso (ruta
    /// absoluta, "..", nombre reservado de Windows) no aborta: se sanea y queda en el resultado, porque en un
    /// TAR es común y abortar dejaría al operador sin la evidencia. Dos entradas con el mismo nombre (un TAR
    /// con una versión agregada después) se conservan las dos. Enlaces y dispositivos no se crean: se
    /// documentan. Un error o una cancelación borran lo escrito.
    /// </summary>
    private static async Task<ExtractionResult> DecompressOtherFormatAsync(
        string path,
        string outputDir,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        IProgress<string>? entryProgress,
        CreatedDirectories createdDirectories,
        CancellationToken ct)
    {
        var createdFiles = new List<string>();
        ExtractionResult? result = null;
        bool success = false;
        try
        {
            await Task.Run(() =>
            {
                using var reader = ArchiveReader.TryOpen(path, password)
                    ?? throw new InvalidDataException("Formato de archivo no reconocido.");
                result = new ExtractionResult { Format = reader.FormatName };
                if (reader.Comment is { } comment)
                {
                    result.ArchiveComment = new CommentInfo(comment.Text, comment.Problem);
                }

                var usedPaths = EntryPathSanitizer.CreatePathSet();
                var directoryRemap = new List<(string From, string To)>();
                string root = Path.GetFullPath(outputDir);
                var spaceGuard = new ExtractionSpaceGuard(outputDir);

                while (reader.MoveNext())
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = reader.Current;
                    if (entry.Comment is { } entryComment)
                    {
                        result.EntryComments.Add(new EntryComment(entry.Name, new CommentInfo(entryComment.Text, entryComment.Problem)));
                    }

                    if (entry.Kind is not (ArchiveEntryKind.File or ArchiveEntryKind.Directory))
                    {
                        long? size = entry.Kind == ArchiveEntryKind.AlternateStream ? entry.Size : null;
                        result.Skipped.Add(new SkippedEntry(entry.Name, entry.Kind.ToString(), entry.LinkTarget, size));
                        continue;
                    }

                    bool isDirectory = entry.Kind == ArchiveEntryKind.Directory;
                    int renamedBefore = result.Renamed.Count;
                    string target = ResolveEntryTarget(root, entry.Name, isDirectory, usedPaths, result,
                        neutralizeParentReferences: true);
                    target = PrepareTarget(root, entry.Name, target, isDirectory, directoryRemap, result, renamedBefore,
                        createdDirectories);

                    if (isDirectory)
                    {
                        continue;
                    }

                    entryProgress?.Report(entry.Name);
                    try
                    {
                        using var outStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);
                        createdFiles.Add(target);
                        reader.CopyCurrentTo(new Reading7z.ProgressWriteStream(outStream,
                            n =>
                            {
                                spaceGuard.Account(n);
                                progress?.Report((reader.BytesRead, reader.ArchiveLength));
                            }, ct));
                    }
                    catch (PrZipException ex) when (
                        HasPerEntryCheck(reader.FormatName)
                        && !(ex is InvalidPasswordOrCorruptDataException && result.FilesExtracted == 0))
                    {
                        // Solo donde cada archivo trae su propio control (RAR). En un TAR comprimido o un ISO un
                        // fallo es del flujo entero: lo extraído antes no quedaría verificado, así que aborta todo.
                        DiscardDamaged(target, createdFiles, result, renamedBefore, entry.Name, entry.Size, ex.Message);
                        continue;
                    }

                    RestoreModifiedTime(target, entry.LastWriteTime);
                    CountExtracted(result, root, target);
                }

                // El último MoveNext ya leyó hasta el final y verificó el comprimido.
                progress?.Report((reader.ArchiveLength, reader.ArchiveLength));
            }, ct);
            success = true;
            return result!;
        }
        finally
        {
            if (!success)
            {
                foreach (var file in createdFiles)
                {
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch { /* Ignorar fallos de limpieza en cascada */ }
                }
            }
        }
    }

    /// <summary>Firma "PK" de un registro ZIP al comienzo del archivo.</summary>
    private static bool StartsLikeZip(string path)
    {
        Span<byte> head = stackalloc byte[2];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        return fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == 2 && head[0] == (byte)'P' && head[1] == (byte)'K';
    }

    private static void ValidateCompressOptions(ZipOptions options)
    {
        if (options.SourcePaths == null || options.SourcePaths.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos un archivo de origen.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("Se requiere ruta de salida del ZIP.", nameof(options));
        }

        // La salida no puede quedar dentro de lo que se empaqueta: se escribiría en la carpeta de la
        // evidencia y el paquete siguiente la incluiría.
        if (PackageOutput.FindSourceContaining(options.SourcePaths, options.OutputPath) is { } source)
        {
            throw new ArgumentException(
                $"La salida '{options.OutputPath}' queda dentro de lo que se empaqueta ('{source}'). Elija otra carpeta.",
                nameof(options));
        }

        // Nunca encima de un paquete anterior: además de perderlo, un ZIP o volúmenes viejos con el mismo
        // nombre le ganan al paquete nuevo al verificarlo o sellarlo.
        if (PackageOutput.FindExisting(options.OutputPath, includeSidecars: false) is { Count: > 0 } existing)
        {
            throw new IOException(
                $"Ya existe un paquete con ese nombre ({Path.GetFileName(existing[0])}). ForZip no sobrescribe " +
                "un paquete anterior: elija otro nombre o mueva el existente.");
        }

        if (!ValidLevels.Contains(options.CompressionLevel))
        {
            throw new ArgumentException(
                $"Nivel inválido: {options.CompressionLevel}. Valores válidos: 0, 1, 3, 5, 7, 9.",
                nameof(options));
        }

        if (options.SplitSize.HasValue && options.SplitSize.Value < MinSplitSize)
        {
            throw new ArgumentException(
                $"Tamaño de volumen demasiado pequeño: {options.SplitSize.Value} bytes. Mínimo: {MinSplitSize} bytes (64 KB).",
                nameof(options));
        }
    }

    /// <summary>
    /// Lee un bloque de la evidencia. Un bloqueo a mitad de lectura sí aborta: la entrada ya
    /// está abierta en el ZIP y no se puede retirar, así que seguir dejaría un archivo truncado.
    /// </summary>
    private static async ValueTask<int> ReadEvidenceAsync(FileStream stream, byte[] buffer, string path, CancellationToken ct)
    {
        try
        {
            return await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
        }
        catch (Exception ex) when (SecuritySoftwareInterference.IsBlocked(ex))
        {
            throw new SecuritySoftwareBlockedException(SecurityBlockStage.ReadingEvidence, path, ex);
        }
    }
}
