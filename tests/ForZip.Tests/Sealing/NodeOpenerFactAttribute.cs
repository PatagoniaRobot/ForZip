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

using System.Diagnostics;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Test que necesita Node.js 20 o posterior para correr el núcleo del abridor HTML fuera del
/// navegador. Si no hay, se omite en lugar de fallar.
/// </summary>
/// <remarks>
/// Node trae la misma Web Crypto que Edge y Chrome, que es lo único que el núcleo usa. Igual
/// que con Python, la validación cruzada es exigible en la máquina de desarrollo y no en
/// cualquier clon: que se omita queda a la vista en la salida de <c>dotnet test</c>.
/// </remarks>
public sealed class NodeOpenerFactAttribute : FactAttribute
{
    private const int MinimumMajorVersion = 20;

    private static readonly Lazy<bool> Available = new(IsNodeAvailable);

    public NodeOpenerFactAttribute()
    {
        if (!Available.Value)
        {
            Skip = $"Requiere Node.js {MinimumMajorVersion} o posterior en el PATH.";
        }
    }

    private static bool IsNodeAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            var version = process.StandardOutput.ReadToEnd().Trim().TrimStart('v');
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            var major = version.Split('.')[0];
            return process.ExitCode == 0
                && int.TryParse(major, out var number)
                && number >= MinimumMajorVersion;
        }
        catch
        {
            // Node no instalado o no ejecutable.
            return false;
        }
    }
}
