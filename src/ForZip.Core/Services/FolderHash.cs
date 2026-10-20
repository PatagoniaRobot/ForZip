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

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

/// <summary>Huella de una carpeta entera, calculada a partir del SHA-256 de cada archivo que tiene adentro.</summary>
public sealed class FolderDigest
{
    /// <summary>Ruta completa de la carpeta.</summary>
    public string FolderPath { get; init; } = string.Empty;

    public int FileCount { get; init; }
    public long TotalSize { get; init; }

    /// <summary>
    /// Huella ForZip: SHA-256 de la lista ordenada "SHA-256␠␠ruta/relativa" (ver
    /// <see cref="FolderHash.ForZipDigest"/>). Cambia si cambia un byte, un nombre o la ubicación de un archivo.
    /// Null si algún archivo no se pudo leer.
    /// </summary>
    public string? ForZipSha256 { get; init; }

    /// <summary>
    /// El "SHA256 for data" de 7-Zip (<c>7z h -scrcSHA256</c>), con su sufijo de desbordes: se coteja con 7-Zip.
    /// No depende de los nombres. Null si algún archivo no se pudo leer.
    /// </summary>
    public string? SevenZipDataSha256 { get; init; }

    /// <summary>Archivos de adentro que no se pudieron leer: con alguno, no hay huella.</summary>
    public int UnreadableCount { get; init; }

    /// <summary>Enlaces y junctions de adentro, que no se siguen: no entran en la huella.</summary>
    public int LinksNotFollowed { get; init; }

    public bool IsComplete => UnreadableCount == 0;
}

/// <summary>
/// Huella de carpeta. Se publica en <c>Documentation/Huella_de_Carpeta_v1.md</c> con un script de referencia
/// (<c>Tools/huella_carpeta.py</c>): cualquiera la recalcula sin ForZip.
/// </summary>
public static class FolderHash
{
    private static readonly BigInteger Modulus = BigInteger.One << 256;

    /// <summary>
    /// Huella ForZip de una carpeta. Por cada archivo, una línea "sha256 en hexadecimal minúscula", dos espacios, la
    /// ruta relativa a la carpeta con "/" y un salto "\n" (el formato de <c>sha256sum</c>); las líneas, ordenadas por
    /// los bytes UTF-8 de la ruta; el resultado, el SHA-256 de ese texto en UTF-8. Las carpetas vacías no cuentan.
    /// </summary>
    public static string ForZipDigest(IEnumerable<(string RelativePath, string Sha256)> files)
    {
        var lines = files
            .Select(f => (Path: Encoding.UTF8.GetBytes(f.RelativePath), Line: $"{f.Sha256.ToLowerInvariant()}  {f.RelativePath}\n"))
            .OrderBy(f => f.Path, Utf8Order.Instance);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (_, line) in lines)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// El "SHA256 for data" que muestra 7-Zip para una carpeta: la suma de los SHA-256 de cada archivo, leídos como
    /// números de 256 bits en little-endian, módulo 2^256, en hexadecimal; detrás, un guion y la cantidad de veces que
    /// la suma pasó de 2^256, en ocho dígitos hexadecimales. Establecido con pruebas de caja negra contra 7-Zip 24.08.
    /// </summary>
    public static string SevenZipDataSum(IEnumerable<string> sha256s)
    {
        var sum = BigInteger.Zero;
        foreach (var hex in sha256s)
        {
            sum += new BigInteger(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: false);
        }

        var low = sum % Modulus;
        var carries = sum / Modulus;
        var bytes = new byte[32];
        low.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
        return Convert.ToHexString(bytes).ToLowerInvariant() + "-" + ((uint)carries).ToString("X8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Una huella por cada carpeta de <paramref name="roots"/> (los archivos sueltos no tienen), con los SHA-256 ya
    /// calculados de <paramref name="files"/> (null: no se pudo leer). Los omitidos que caen adentro de la carpeta
    /// (enlaces no seguidos) se cuentan para decirlos.
    /// </summary>
    public static List<FolderDigest> Compute(
        IEnumerable<string> roots,
        IReadOnlyCollection<(string FullPath, long Size, string? Sha256)> files,
        IEnumerable<OmittedFile> omitted)
    {
        var omittedList = omitted.ToList();
        var result = new List<FolderDigest>();
        foreach (var root in roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))
                                  .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var inside = files.Where(f => EvidenceSelection.IsWithinSource(root, f.FullPath)).ToList();
            int unreadable = inside.Count(f => f.Sha256 == null)
                             + omittedList.Count(o => o.Reason is OmissionReason.Unreadable or OmissionReason.FolderUnreadable
                                                      && o.SourcePath != null && EvidenceSelection.IsWithinSource(root, o.SourcePath)
                                                      && !inside.Any(f => string.Equals(f.FullPath, o.SourcePath, StringComparison.OrdinalIgnoreCase)));
            int links = omittedList.Count(o => o.Reason == OmissionReason.LinkNotFollowed
                                               && o.SourcePath != null && EvidenceSelection.IsWithinSource(root, o.SourcePath));
            bool complete = unreadable == 0;

            result.Add(new FolderDigest
            {
                FolderPath = root,
                FileCount = inside.Count,
                TotalSize = inside.Sum(f => f.Size),
                UnreadableCount = unreadable,
                LinksNotFollowed = links,
                ForZipSha256 = complete
                    ? ForZipDigest(inside.Select(f => (RelativePath(root, f.FullPath), f.Sha256!)))
                    : null,
                SevenZipDataSha256 = complete ? SevenZipDataSum(inside.Select(f => f.Sha256!)) : null
            });
        }

        return result;
    }

    /// <summary>Ruta de un archivo relativa a la carpeta, con "/".</summary>
    public static string RelativePath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    /// <summary>Orden por bytes UTF-8, el mismo que el <c>sorted()</c> de Python sobre texto.</summary>
    private sealed class Utf8Order : IComparer<byte[]>
    {
        public static readonly Utf8Order Instance = new();
        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
