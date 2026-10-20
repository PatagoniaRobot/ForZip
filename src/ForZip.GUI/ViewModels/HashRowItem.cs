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

using CommunityToolkit.Mvvm.ComponentModel;

namespace ForZip.GUI.ViewModels;

public partial class HashRowItem : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortName), nameof(Folder))]
    private string _fileName = string.Empty;

    /// <summary>
    /// El nombre del archivo, en la primera línea de la grilla. <see cref="FileName"/> es la ruta relativa
    /// (<c>caso/fotos/2026/imagen.bin</c>): mostrada sola en una columna angosta, todas las filas empezaban igual y
    /// el nombre, que va al final, quedaba cortado.
    /// </summary>
    public string ShortName
    {
        get
        {
            var cut = FileName.LastIndexOf('/');
            return cut >= 0 ? FileName[(cut + 1)..] : FileName;
        }
    }

    /// <summary>La carpeta relativa, en la segunda línea (vacía para un archivo suelto).</summary>
    public string Folder
    {
        get
        {
            var cut = FileName.LastIndexOf('/');
            return cut >= 0 ? FileName[..cut] + "/" : string.Empty;
        }
    }

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long _size;

    public string SizeText => FormatSize(Size);

    internal static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    [ObservableProperty]
    private string _md5 = string.Empty;

    [ObservableProperty]
    private string _sha1 = string.Empty;

    [ObservableProperty]
    private string _sha256 = string.Empty;

    [ObservableProperty]
    private string _sha512 = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>No se pudo leer: la fila queda marcada y el archivo va al informe como no leído.</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>Fecha de modificación tomada al calcular el hash.</summary>
    public DateTimeOffset? ModifiedUtc { get; set; }

    /// <summary>Lo que se anota en el informe cuando no se pudo leer.</summary>
    public ForZip.Core.Models.OmittedFile? Omission { get; set; }
}
