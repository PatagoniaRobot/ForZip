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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ForZip.Core.Models;

namespace ForZip.GUI.ViewModels;

/// <summary>Una línea "ALGORITMO : hash" dentro de la ficha de un archivo.</summary>
public partial class QuickHashLine : ObservableObject
{
    [ObservableProperty]
    private string _algorithm = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;
}

/// <summary>
/// Ficha de un archivo en la ventana de hash rápido. A diferencia de <see cref="HashRowItem"/>
/// (una fila de tabla con una columna fija por algoritmo), acá los hashes se muestran apilados
/// y solo los seleccionados, por lo que se exponen como una colección reconstruible.
/// </summary>
public partial class QuickHashItem : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    /// <summary>Estado neutro del archivo: "Pendiente", "Calculando… 42 %" o vacío al terminar.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Mensaje de error de lectura. Va aparte del estado porque se pinta en rojo.</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>Lo que se anota en el informe cuando no se pudo leer (motivo, código y mensaje de Windows).</summary>
    public ForZip.Core.Models.OmittedFile? Omission { get; set; }

    /// <summary>Progreso 0–100 del archivo que se está calculando en este momento.</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>La barra por archivo solo aparece mientras ese archivo se está leyendo.</summary>
    [ObservableProperty]
    private bool _isProcessing;

    public DateTimeOffset? ModifiedUtc { get; set; }

    /// <summary>
    /// Hashes ya calculados, indexados por algoritmo. Persisten aunque el usuario desmarque
    /// un algoritmo: volver a marcarlo no obliga a releer el archivo del disco.
    /// </summary>
    public Dictionary<HashAlgorithmType, string> Hashes { get; } = new();

    /// <summary>Proyección visible de <see cref="Hashes"/> para los algoritmos seleccionados.</summary>
    public ObservableCollection<QuickHashLine> Lines { get; } = new();
}
