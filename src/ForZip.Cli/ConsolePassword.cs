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

namespace ForZip.Cli;

/// <summary>
/// Lectura de contraseñas por consola con el eco desactivado.
/// </summary>
/// <remarks>
/// Existe porque <c>--password</c> en la línea de comandos queda escrito en el historial del
/// shell y en la lista de procesos. Para un paquete cuya clave se entrega en mano y bajo
/// firma, eso anula el procedimiento entero, así que la ayuda recomienda esta vía y los
/// comandos la usan cuando la opción no viene dada.
/// </remarks>
public static class ConsolePassword
{
    /// <summary>
    /// Pide una contraseña sin mostrarla. Si la entrada está redirigida —un script que
    /// canaliza la clave por una tubería— se lee una línea normal, porque ahí no hay terminal
    /// que apagar.
    /// </summary>
    public static string Read(string prompt)
    {
        Console.Write(prompt);

        if (Console.IsInputRedirected)
        {
            var piped = Console.ReadLine() ?? string.Empty;
            Console.WriteLine();
            return piped;
        }

        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }

                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return string.Empty;
            }

            // Se descartan las teclas de control (flechas, funciones): no forman parte de la
            // clave y de otro modo entrarían como caracteres nulos.
            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }

    /// <summary>
    /// Pide la contraseña dos veces y exige que coincidan. Se usa al sellar: una errata al
    /// tipear deja un sobre que no abre nadie, y no hay recuperación posible.
    /// </summary>
    public static string ReadTwice(string prompt, string confirmPrompt)
    {
        while (true)
        {
            var first = Read(prompt);
            if (first.Length == 0)
            {
                return string.Empty;
            }

            var second = Read(confirmPrompt);
            if (string.Equals(first, second, StringComparison.Ordinal))
            {
                return first;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Las contraseñas no coinciden. Intente nuevamente.");
            Console.ResetColor();
        }
    }
}
