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

using System.Text;

namespace ForZip.Core.Services;

/// <summary>
/// Las licencias que viajan dentro de los ejecutables: la de ForZip, su NOTICE, los avisos de terceros y la de
/// UnRAR. Los archivos sueltos junto al .exe pueden faltar (la actualización desde la 1.4 reemplaza solo el
/// ejecutable, y la CLI puede circular sola) o ser de otra versión; estas son siempre las de esta.
/// </summary>
public static class LicenseTexts
{
    /// <summary>En el orden en que se muestran.</summary>
    public static readonly IReadOnlyList<string> Files = ["NOTICE", "LICENSE", "THIRD-PARTY-NOTICES.txt", "LICENSE-UNRAR.txt"];

    /// <summary>Todas juntas, cada una bajo su nombre de archivo.</summary>
    public static string Combined()
    {
        var text = new StringBuilder();
        foreach (var name in Files)
        {
            using var resource = typeof(LicenseTexts).Assembly.GetManifestResourceStream($"ForZip.Core.Licenses.{name}")
                ?? throw new InvalidOperationException($"Falta la licencia embebida {name}.");
            using var reader = new StreamReader(resource);
            text.Append('=', 80).AppendLine().AppendLine(name).Append('=', 80).AppendLine().AppendLine();
            text.AppendLine(reader.ReadToEnd().TrimEnd()).AppendLine().AppendLine();
        }

        return text.ToString();
    }
}
