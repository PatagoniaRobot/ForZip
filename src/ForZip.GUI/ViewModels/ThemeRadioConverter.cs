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
using Avalonia.Data.Converters;

namespace ForZip.GUI.ViewModels;

// Convierte un código de tema (string) a bool, comparando contra el ConverterParameter.
// Permite enlazar dos RadioButtons mutuamente excluyentes a la propiedad string Theme.
public class ThemeRadioConverter : IValueConverter
{
    public static readonly ThemeRadioConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string themeCode && parameter is string expected && themeCode == expected;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string expected)
        {
            return expected;
        }
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
