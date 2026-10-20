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

/// <summary>Formato del paquete que produce o abre ForZip.</summary>
public enum ArchiveFormat
{
    /// <summary>ZIP con cifrado WinZip AES-256. Es el formato de siempre y el único que va dentro de un sobre .fzs.</summary>
    Zip,

    /// <summary>7z (LZMA2, AES-256 con nombres cifrados si hay contraseña), con el motor propio de ForZip (PrZip). El
    /// config.json guarda el número, no el nombre: renombrar el miembro no rompe configuraciones viejas.</summary>
    SevenZ
}

/// <summary>Extensiones y detección de formato.</summary>
public static class ArchiveFormats
{
    private static readonly byte[] Signature7z = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };

    /// <summary>Extensión con punto: ".zip" o ".7z".</summary>
    public static string Extension(ArchiveFormat format) => format == ArchiveFormat.SevenZ ? ".7z" : ".zip";

    /// <summary>Nombre para mostrar: "ZIP" o "7z".</summary>
    public static string DisplayName(ArchiveFormat format) => format == ArchiveFormat.SevenZ ? "7z" : "ZIP";

    /// <summary>
    /// Formato que corresponde a una ruta de salida por su extensión (".7z" → 7z; cualquier otra → ZIP).
    /// Sirve para elegir el formato al empaquetar; al abrir manda la firma, no la extensión.
    /// </summary>
    public static ArchiveFormat FromOutputPath(string path) =>
        path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.SevenZ : ArchiveFormat.Zip;

    /// <summary>
    /// Formato según los primeros bytes. Un .7z empieza siempre con su firma de seis bytes; todo lo demás
    /// se trata como ZIP, que es lo que ForZip abría hasta ahora (y da su propio error si no lo es).
    /// </summary>
    public static ArchiveFormat Detect(ReadOnlySpan<byte> head) =>
        head.Length >= Signature7z.Length && head[..Signature7z.Length].SequenceEqual(Signature7z)
            ? ArchiveFormat.SevenZ
            : ArchiveFormat.Zip;

    /// <summary>
    /// Detecta el formato leyendo el comienzo del archivo (o del primer volumen). Un autoextraíble 7z (un .exe con el
    /// 7z pegado detrás) también es 7z: el motor lo abre buscando su firma, sin ejecutar nada.
    /// </summary>
    public static ArchiveFormat Detect(string path)
    {
        Span<byte> head = stackalloc byte[6];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        int read = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var format = Detect(head[..read]);
        if (format == ArchiveFormat.Zip && read >= 2 && head[0] == 'M' && head[1] == 'Z' && Find7zInExecutable(fs) >= 0)
        {
            return ArchiveFormat.SevenZ;
        }

        return format;
    }

    /// <summary>Hasta dónde se busca el 7z dentro de un ejecutable: el mismo límite que usa el motor al abrirlo.</summary>
    public const int SfxScanLimit = 1024 * 1024;

    /// <summary>Un .exe que lleva un 7z adentro (autoextraíble de 7-Zip u otro). Se lee, nunca se ejecuta.</summary>
    public static bool Is7zSelfExtracting(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
            Span<byte> head = stackalloc byte[2];
            return fs.ReadAtLeast(head, 2, throwOnEndOfStream: false) == 2 && head[0] == 'M' && head[1] == 'Z'
                   && Find7zInExecutable(fs) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Posición del 7z dentro de un ejecutable, o -1. Cuenta solo una firma cuyo encabezado de inicio pasa su CRC32,
    /// como hace el motor: los seis bytes de la firma pueden aparecer por casualidad dentro del código del programa.
    /// </summary>
    private static long Find7zInExecutable(FileStream fs)
    {
        const int headerSize = 32;
        long limit = Math.Min(fs.Length - headerSize, SfxScanLimit);
        var buffer = new byte[64 * 1024 + headerSize];
        var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
        for (long start = 0; start <= limit; start += buffer.Length - headerSize)
        {
            fs.Position = start;
            int read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            for (int i = 0; i + headerSize <= read && start + i <= limit; i++)
            {
                if (!buffer.AsSpan(i, Signature7z.Length).SequenceEqual(Signature7z))
                {
                    continue;
                }

                crc.Reset();
                crc.Update(new ArraySegment<byte>(buffer, i + 12, 20));
                if ((uint)crc.Value == System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i + 8, 4)))
                {
                    return start + i;
                }
            }

            if (read < buffer.Length)
            {
                break;
            }
        }

        return -1;
    }
}
