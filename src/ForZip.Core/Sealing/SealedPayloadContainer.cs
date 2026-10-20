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

using System.IO.Compression;

namespace ForZip.Core.Sealing;

/// <summary>
/// Contenedor interno del sobre: la carga útil que se cifra es un ZIP sin contraseña y con
/// nivel de compresión <c>Store</c> que agrupa, en la raíz, el paquete y sus sidecar
/// (<c>.zip</c> o volúmenes, <c>.manifest.json</c> y el <c>.p7s</c> si se firmó; en sobres de
/// antes de 2.0, también <c>.report.txt</c> y su <c>.sha256</c>), más una copia del extractor
/// autónomo <c>fzs_extract.py</c>.
/// </summary>
/// <remarks>
/// Se usa <c>Store</c> porque el contenido ya está comprimido: recomprimir solo consume
/// tiempo. Y se usa ZIP, y no un formato propio, para que una vez abierto el sobre el
/// contenido sea inspeccionable con herramientas estándar.
/// </remarks>
public static class SealedPayloadContainer
{
    /// <summary>
    /// Arma el contenedor interno con los archivos indicados, todos en la raíz y con su
    /// nombre de archivo como nombre de entrada, y agrega al final una copia del extractor
    /// autónomo <c>fzs_extract.py</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Dos archivos comparten el mismo nombre.</exception>
    public static async Task CreateAsync(IEnumerable<string> filePaths, Stream destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(destination);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var filePath in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            var entryName = Path.GetFileName(filePath);
            if (!seen.Add(entryName))
            {
                throw new ArgumentException(
                    $"El contenedor interno no admite dos entradas con el nombre '{entryName}'.",
                    nameof(filePaths));
            }

            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            entry.LastWriteTime = File.GetLastWriteTime(filePath);

            await using var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var target = entry.Open();
            await source.CopyToAsync(target, ct).ConfigureAwait(false);
        }

        await AddEmergencyExtractorAsync(archive, seen, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Agrega la copia del extractor autónomo, para que quien tenga el sobre y la contraseña
    /// tenga adentro la herramienta que alcanza para abrirlo sin ForZip.
    /// </summary>
    /// <remarks>
    /// Si un artefacto ya ocupa ese nombre, no se agrega: la evidencia manda y no se la
    /// sombrea. Es un caso de laboratorio —sellar a mano una carpeta que ya contiene el
    /// script—, no el flujo del perfil Reservado.
    /// </remarks>
    private static async Task AddEmergencyExtractorAsync(
        ZipArchive archive, HashSet<string> seen, CancellationToken ct)
    {
        if (!seen.Add(EmergencyExtractor.FileName))
        {
            return;
        }

        var entry = archive.CreateEntry(EmergencyExtractor.FileName, CompressionLevel.NoCompression);
        entry.LastWriteTime = EmergencyExtractor.EntryTimestamp;

        await using var source = EmergencyExtractor.OpenStream();
        await using var target = entry.Open();
        await source.CopyToAsync(target, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Extrae el contenedor interno al directorio indicado y devuelve las rutas escritas.
    /// </summary>
    /// <remarks>
    /// Las entradas del contenedor van todas en la raíz por especificación, así que cualquier
    /// separador de directorio o ruta absoluta en un nombre de entrada es un intento de
    /// escribir fuera del destino (Zip Slip) y se rechaza sin extraer nada.
    /// </remarks>
    /// <exception cref="SealedFormatException">El contenedor interno es inválido.</exception>
    public static async Task<List<string>> ExtractAsync(Stream source, string destinationDir, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);

        Directory.CreateDirectory(destinationDir);
        var written = new List<string>();

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new SealedFormatException("El contenedor interno del sobre es ilegible.", ex);
        }

        using (archive)
        {
            string? current = null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // Entrada de directorio: el contenedor es plano, no debería haberlas.
                        continue;
                    }

                    if (entry.FullName != entry.Name || Path.IsPathRooted(entry.FullName))
                    {
                        throw new SealedFormatException(
                            "El contenedor interno tiene una entrada con ruta, y solo admite archivos en la raíz.");
                    }

                    // ForZip solo mete nombres de archivo comunes. Un ':' escribiría un flujo NTFS oculto dentro de otro
                    // archivo, un nombre reservado o con punto final no es el que se ve, y un nombre repetido pisaría al
                    // primero: todo eso es un sobre armado a mano y se rechaza sin extraer.
                    if (!IsPlainFileName(entry.Name))
                    {
                        throw new SealedFormatException(
                            $"El contenedor interno tiene un nombre de archivo inválido en Windows: '{entry.Name}'.");
                    }

                    if (!names.Add(entry.Name))
                    {
                        throw new SealedFormatException(
                            $"El contenedor interno tiene dos entradas con el nombre '{entry.Name}'.");
                    }

                    var targetPath = Path.Combine(destinationDir, entry.Name);

                    current = targetPath;
                    await using (var entryStream = entry.Open())
                    await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await entryStream.CopyToAsync(target, ct).ConfigureAwait(false);
                    }

                    written.Add(targetPath);
                    current = null;
                }
            }
            catch
            {
                // Un corte a mitad (disco lleno, cancelación, contenedor inválido) no deja archivos a medias en el
                // destino, que es la carpeta del operador: se borra lo que se llegó a escribir.
                foreach (var path in current == null ? written : written.Append(current))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                throw;
            }
        }

        return written;
    }

    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Nombre que Windows guarda tal cual: sin caracteres inválidos (incluido ':'), sin punto ni espacio final y
    /// sin nombre de dispositivo reservado.
    /// </summary>
    private static bool IsPlainFileName(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':')
            || name.EndsWith('.') || name.EndsWith(' '))
        {
            return false;
        }

        var stem = name.Split('.')[0].TrimEnd(' ');
        return !ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}
