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

public class ZipOptions
{
    public List<string> SourcePaths { get; set; } = new();
    public string OutputPath { get; set; } = string.Empty;
    public int CompressionLevel { get; set; } = 5;
    public string? Password { get; set; }
    public HashSet<HashAlgorithmType> HashAlgorithms { get; set; } = new();

    /// <summary>
    /// Formato del paquete. ZIP por defecto; 7z comprime con LZMA2 y varios archivos a la vez, y con
    /// contraseña cifra también los nombres. El perfil Reservado siempre usa ZIP: es lo que lleva el sobre.
    /// </summary>
    public ArchiveFormat Format { get; set; } = ArchiveFormat.Zip;

    /// <summary>
    /// Si tiene valor, el ZIP se divide en volúmenes de este tamaño (en bytes), estilo
    /// 7-Zip (<c>.001</c>, <c>.002</c>, …). <c>null</c> = archivo único (comportamiento por
    /// defecto). El último volumen puede ser más pequeño.
    /// </summary>
    public long? SplitSize { get; set; }

    /// <summary>
    /// Lo que el operador vio al seleccionar: cada archivo de cada origen, con la hora en que
    /// se lo listó. Si al empaquetar falta alguno, entra al paquete como omitido con su motivo
    /// en vez de desaparecer en silencio. <c>null</c> cuando no hubo una selección previa
    /// (la línea de comandos recorre el origen una sola vez).
    /// </summary>
    public IReadOnlyList<SelectedSourceFile>? SelectionSnapshot { get; set; }
}

/// <summary>Un archivo tal como estaba cuando el operador lo seleccionó.</summary>
public sealed record SelectedSourceFile(string FullPath, long Size, DateTimeOffset ModifiedUtc, DateTimeOffset SeenAtUtc);
