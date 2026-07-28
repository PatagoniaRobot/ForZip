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

public class PasswordOptions
{
    /// <summary>Longitud mínima admitida. Por debajo de 8 la contraseña no se considera utilizable.</summary>
    public const int MinLength = 8;

    public const int MaxLength = 128;

    public const int DefaultLength = 16;

    /// <summary>
    /// Caracteres visualmente ambiguos (cero/O/o, uno/I/l, barra vertical). Se excluyen
    /// SIEMPRE del pool: una contraseña forense se transcribe y se dicta a mano, y un
    /// carácter que no se puede leer sin ambigüedad es un error de transcripción esperando
    /// ocurrir. No es opcional por diseño.
    /// </summary>
    public const string AmbiguousChars = "0OoIl1|";

    public int Length { get; set; } = DefaultLength;
    public bool IncludeUppercase { get; set; } = true;
    public bool IncludeLowercase { get; set; } = true;
    public bool IncludeDigits { get; set; } = true;
    public bool IncludeSymbols { get; set; } = true;
}
