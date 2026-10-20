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

using ForZip.Core.Models;

namespace ForZip.Core.Interfaces;

public interface IZipService
{
    Task<CompressionResult> CompressAsync(
        ZipOptions options,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct);

    /// <summary>
    /// Extrae un ZIP, un 7z (también divididos en volúmenes) o un TAR, TAR.GZ/BZ2/XZ, GZ, BZ2, XZ o ISO.
    /// El formato se reconoce por la firma. El resultado documenta lo que no salió tal cual.
    /// <paramref name="entryProgress"/> recibe el nombre de cada archivo al empezar a extraerlo (para mostrarlo
    /// debajo de la barra).
    /// </summary>
    Task<ExtractionResult> DecompressAsync(
        string zipPath,
        string outputDir,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct,
        IProgress<string>? entryProgress = null);

    /// <summary>
    /// Prueba un archivo comprimido sin su manifiesto (ZIP, 7z, RAR, TAR, GZ, BZ2, XZ, ISO, o un autoextraíble 7z o
    /// RAR, que se lee sin ejecutarlo): lo descomprime en memoria, sin escribir nada, y comprueba el control de cada
    /// archivo. Dice si está sano según sus propios controles, no si es el original.
    /// </summary>
    Task<ArchiveTestResult> TestAsync(
        string path,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct,
        IProgress<string>? entryProgress = null);
}
