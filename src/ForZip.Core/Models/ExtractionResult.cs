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

namespace ForZip.Core.Models;

/// <summary>
/// Lo que pasó al extraer: el formato reconocido, cuántos archivos se escribieron y todo lo que no salió
/// tal cual venía, para que quede documentado (nombres saneados, enlaces y dispositivos no creados).
/// </summary>
public sealed class ExtractionResult
{
    /// <summary>Formato reconocido por la firma: "ZIP", "7z", "tar", "tar.gz", "xz", "iso (Joliet)"…</summary>
    public string Format { get; init; } = string.Empty;

    public int FilesExtracted { get; set; }

    /// <summary>Entradas guardadas con otro nombre porque el original era peligroso o inválido en Windows, demasiado largo o repetido.</summary>
    public List<RenamedEntry> Renamed { get; } = new();

    /// <summary>
    /// Entradas que no se materializan: enlaces simbólicos o duros, dispositivos, FIFOs y flujos NTFS
    /// alternativos ("archivo:flujo", con su tamaño).
    /// </summary>
    public List<SkippedEntry> Skipped { get; } = new();

    /// <summary>Comentario del archivo comprimido (hoy, RAR), o null si no tiene.</summary>
    public CommentInfo? ArchiveComment { get; set; }

    /// <summary>Comentarios de entradas puntuales (RAR 1.5-2.x permitía comentar cada archivo).</summary>
    public List<EntryComment> EntryComments { get; } = new();

    /// <summary>
    /// Archivos que no pasaron su control (CRC, tamaño, datos que no se pueden descomprimir). No quedan en el
    /// destino: se borran y se documentan, y el resto de la extracción sigue. Lo extraído siempre está verificado;
    /// esto dice qué faltó y por qué.
    /// </summary>
    public List<DamagedEntry> Damaged { get; } = new();

    /// <summary>Se extrajo todo lo que el archivo contenía (sin entradas dañadas).</summary>
    public bool IsComplete => Damaged.Count == 0;

    /// <summary>Ruta más larga que abre el Explorador de Windows: MAX_PATH son 260 con el nulo final.</summary>
    public const int ExplorerMaxPath = 259;

    /// <summary>
    /// Archivos extraídos cuya ruta completa pasa de <see cref="ExplorerMaxPath"/> caracteres (relativas al destino,
    /// con '/'). ForZip los crea y los lee bien, pero el Explorador de Windows y muchos programas no los abren: el
    /// operador tiene que saberlo antes de encontrárselos. No vuelven incompleta la extracción.
    /// </summary>
    public List<string> LongPaths { get; } = new();
}

/// <summary>Una entrada que no se extrajo porque no pasó su control.</summary>
public sealed record DamagedEntry(string Name, long? ExpectedSize, string Reason);

/// <summary>Nombre que traía el archivo, ruta relativa con la que se guardó y por qué.</summary>
public sealed record RenamedEntry(string OriginalName, string SavedAs, RenameReason Reason = RenameReason.Invalid);

/// <summary>
/// Por qué una entrada se guardó con otro nombre. Antes el aviso juntaba todas las causas en una frase y el
/// operador no sabía si el archivo traía un nombre raro o si ya había algo en el destino.
/// </summary>
public enum RenameReason
{
    /// <summary>Caracteres o nombre que Windows no admite o que son peligrosos (":", "CON", "..", etc.).</summary>
    Invalid,

    /// <summary>Un nombre de más de 255 caracteres: se acorta con "~" y un código.</summary>
    TooLong,

    /// <summary>El archivo comprimido trae dos entradas que van al mismo lugar.</summary>
    Repeated,

    /// <summary>Ya había un archivo con ese nombre en el destino, de antes de extraer: no se pisa.</summary>
    AlreadyInDestination,

    /// <summary>Una carpeta del camino existe como archivo en el destino: la carpeta va con otro nombre.</summary>
    FolderIsFile,
}

/// <summary>
/// Entrada no creada, con su tipo ("SymbolicLink", "HardLink", "Other", "AlternateStream"), el destino si es
/// enlace y el tamaño si es un flujo NTFS.
/// </summary>
public sealed record SkippedEntry(string Name, string Kind, string? LinkTarget, long? Size = null);

/// <summary>Un comentario: el texto, o el motivo por el que estaba pero no se pudo leer.</summary>
public sealed record CommentInfo(string? Text, string? Problem);

/// <summary>Comentario de una entrada.</summary>
public sealed record EntryComment(string Name, CommentInfo Comment);

/// <summary>Texto de un comentario listo para mostrar.</summary>
public static class CommentText
{
    /// <summary>
    /// Los caracteres de control (salvo saltos de línea y tabulaciones) se ven como '?': un comentario lo
    /// escribe quien armó el archivo y puede traer secuencias de escape para la terminal. Sin saltos finales.
    /// </summary>
    public static string ForDisplay(string text)
    {
        var safe = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            safe.Append(char.IsControl(c) && c is not ('\r' or '\n' or '\t') ? '?' : c);
        }

        return safe.ToString().TrimEnd('\r', '\n');
    }
}
