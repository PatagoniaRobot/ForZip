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

namespace ForZip.Core.Services;

/// <summary>
/// Lo común a los inventarios de hashes (Hash Batch, hash rápido y <c>forzip hash</c>): qué archivos entran y cómo
/// se documenta lo que no se pudo leer. Antes cada uno lo resolvía distinto: uno cortaba el lote ante un archivo
/// bloqueado y otro seguía, uno anotaba la ruta y la fecha y otro solo el nombre.
/// </summary>
public static class HashInventory
{
    /// <summary>Un archivo a hashear: ruta completa y el nombre con que figura en el informe.</summary>
    public sealed record Item(string FullPath, string DisplayName);

    /// <summary>
    /// Archivos y carpetas a la lista de archivos: una carpeta aporta todo lo que tiene adentro, recursivamente y sin
    /// entrar en enlaces ni junctions (como al empaquetar); los enlaces encontrados van a <paramref name="skipped"/>.
    /// Un archivo figura con su nombre; uno de una carpeta, con la ruta relativa desde ella ("caso/sub/acta.txt").
    /// </summary>
    public static List<Item> Expand(IEnumerable<string> paths, List<OmittedFile> skipped)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        return EvidenceSelection.EnumerateSourceFiles(existing, null, skipped)
            .Select(f => new Item(f.FullPath, f.EntryName))
            .ToList();
    }

    /// <summary>Un archivo que no se pudo leer, con el código y el mensaje de Windows.</summary>
    public static OmittedFile Unreadable(string fullPath, string displayName, Exception ex)
    {
        long size = 0;
        DateTimeOffset modified = default;
        try
        {
            var info = new FileInfo(fullPath);
            if (info.Exists)
            {
                size = info.Length;
                modified = info.LastWriteTimeUtc;
            }
        }
        catch (Exception probe) when (probe is IOException or UnauthorizedAccessException)
        {
        }

        return new OmittedFile
        {
            Reason = OmissionReason.Unreadable,
            EntryName = displayName,
            SourcePath = fullPath,
            Size = size,
            ModifiedUtc = modified,
            WindowsErrorCode = SecuritySoftwareInterference.GetBlockingErrorCode(ex) ?? (ex.HResult & 0xFFFF),
            SystemMessage = ex.Message,
            RecordedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
