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

/// <summary>Cómo terminó la prueba de un archivo comprimido que no trae manifiesto.</summary>
public enum ArchiveTestOutcome
{
    /// <summary>Se leyó entero y cada archivo pasó el control que trae el formato.</summary>
    NoErrors,

    /// <summary>Al menos un archivo no pasó su control, o el archivo comprimido está dañado.</summary>
    Damaged,

    /// <summary>Está cifrado y no se dio contraseña: no se probó nada.</summary>
    PasswordRequired,

    /// <summary>La contraseña no abre el archivo (o el primer archivo cifrado está dañado: no se distinguen).</summary>
    WrongPassword,

    /// <summary>Se leyó entero, pero el formato no trae controles (TAR, ISO): una alteración no se puede detectar.</summary>
    NoChecks,

    /// <summary>No se pudo leer: falta un volumen, no es un formato conocido, error de disco.</summary>
    Unreadable
}

/// <summary>
/// Resultado de probar un archivo comprimido sin su manifiesto: se descomprime en memoria, sin escribir nada, y se
/// comprueba el control de cada archivo (CRC32, el código de autenticación de AES, BLAKE2sp en RAR5). Prueba que el
/// archivo está sano según sus propios controles; no que sea el original, que solo lo dice un manifiesto o una huella
/// anotada aparte.
/// </summary>
public sealed class ArchiveTestResult
{
    public ArchiveTestOutcome Outcome { get; set; }

    /// <summary>Formato reconocido por la firma: "ZIP", "7z", "rar", "tar", "gz"…</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Es un ejecutable con el archivo comprimido adentro. Se leyó, no se ejecutó.</summary>
    public bool IsSelfExtracting { get; set; }

    /// <summary>Archivos que pasaron su control, con su tamaño.</summary>
    public List<TestedEntry> Entries { get; } = new();

    /// <summary>Archivos que no pasaron su control.</summary>
    public List<DamagedEntry> Damaged { get; } = new();

    /// <summary>Enlaces, dispositivos y flujos NTFS: no tienen contenido que probar.</summary>
    public List<SkippedEntry> Skipped { get; } = new();

    /// <summary>Huella SHA-256 de cada archivo en disco (uno, o cada volumen): la que se anota en la planilla.</summary>
    public List<(string FileName, long Size, string Sha256)> ArchiveFiles { get; } = new();

    /// <summary>
    /// Con <see cref="ArchiveTestOutcome.WrongPassword"/>: el formato guarda un valor de chequeo de la contraseña (RAR5) y
    /// no coincide, así que es seguro que la contraseña es incorrecta y no que el archivo esté dañado. En ZIP, 7z y RAR
    /// anteriores a RAR5 no se puede saber.
    /// </summary>
    public bool PasswordVerifiedWrong { get; set; }

    /// <summary>Por qué no se pudo probar, o qué se dañó cuando el daño cortó la lectura.</summary>
    public string? Error { get; set; }

    public int FilesOk => Entries.Count;
}

/// <summary>Un archivo de adentro que pasó su control.</summary>
public sealed record TestedEntry(string Name, long Size);
