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
/// Test que necesita un Python 3 con el paquete <c>cryptography</c>. Si no hay, se omite en
/// lugar de fallar.
/// </summary>
/// <remarks>
/// La validación cruzada del formato es exigible en la máquina de desarrollo, no en cualquier
/// clon: pedirle Python a quien solo quiere compilar ForZip convertiría una garantía del
/// formato en una barrera de entrada. Que se omita queda a la vista en la salida de
/// <c>dotnet test</c>, con el motivo.
/// </remarks>
public sealed class PythonExtractorFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Interpreter = new(FindInterpreter);

    public PythonExtractorFactAttribute()
    {
        if (Interpreter.Value == null)
        {
            Skip = "Requiere Python 3 con el paquete 'cryptography' en el PATH "
                 + "(pip install cryptography).";
        }
    }

    /// <summary>Intérprete que se usa para correr el extractor, o <c>null</c> si no hay.</summary>
    public static string? Path => Interpreter.Value;

    /// <summary>
    /// Devuelve el primer intérprete del PATH que además pueda importar <c>cryptography</c>:
    /// un Python sin el paquete no sirve, y en Windows conviven varios lanzadores.
    /// </summary>
    private static string? FindInterpreter()
    {
        foreach (var candidate in new[] { "python", "python3", "py" })
        {
            if (CanImportCryptography(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool CanImportCryptography(string interpreter)
    {
        try
        {
            var startInfo = new ProcessStartInfo(interpreter)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("import cryptography");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            // Intérprete inexistente o no ejecutable: se prueba el siguiente candidato.
            return false;
        }
    }
}
