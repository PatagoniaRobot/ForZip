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

namespace ForZip.Core.Services;

/// <summary>Se detuvo una extracción porque el disco de destino se quedaba sin espacio.</summary>
public sealed class ExtractionSpaceException(string message) : IOException(message);

/// <summary>
/// Frena una extracción antes de llenar el disco. Cada <see cref="CheckEvery"/> bytes escritos consulta el espacio
/// libre de la unidad de destino y, si queda menos de <see cref="MinFreeBytes"/>, corta con
/// <see cref="ExtractionSpaceException"/>; quien extrae borra lo ya escrito, como ante cualquier otro error.
/// </summary>
/// <remarks>
/// Una "bomba" (un archivo chico que se expande a muchísimo: un GZip de ceros, una ISO con entradas superpuestas)
/// llenaba el disco hasta que Windows daba error, y con el destino en C: el sistema quedaba un rato sin espacio.
/// En un destino donde no se puede consultar (algunas unidades de red) no frena nada.
/// </remarks>
public sealed class ExtractionSpaceGuard
{
    public const long MinFreeBytes = 1L << 30;
    public const long CheckEvery = 64L << 20;

    private readonly Func<long?> _freeSpace;
    private readonly string _drive;
    private long _sinceCheck;

    public ExtractionSpaceGuard(string destination)
        : this(destination, () => FreeSpaceOf(destination))
    {
    }

    /// <summary>Con una consulta de espacio propia (null = no se puede saber, no frena).</summary>
    public ExtractionSpaceGuard(string destination, Func<long?> freeSpace)
    {
        _freeSpace = freeSpace;
        _drive = SafeRoot(destination);
    }

    /// <summary>Suma bytes escritos; cada <see cref="CheckEvery"/> mira el espacio libre.</summary>
    public void Account(long bytes)
    {
        _sinceCheck += bytes;
        if (_sinceCheck < CheckEvery)
        {
            return;
        }

        _sinceCheck = 0;
        if (_freeSpace() is { } free && free < MinFreeBytes)
        {
            throw new ExtractionSpaceException(
                $"Se detuvo la extracción: en {_drive} quedan {free / (1024 * 1024):N0} MB libres y ForZip deja siempre " +
                $"al menos {MinFreeBytes / (1024 * 1024 * 1024)} GB. Lo extraído se descartó. Si el archivo es legítimo, " +
                "extraerlo en un disco con más espacio; si se expande mucho más que su tamaño, puede estar armado para llenar el disco.");
        }
    }

    private static long? FreeSpaceOf(string destination)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(destination));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string SafeRoot(string destination)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(destination)) ?? destination;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return destination;
        }
    }
}
