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

using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ForZip.GUI.Services;

/// <summary>
/// Códigos Alt de Windows (Alt+92 = <c>\</c>, Alt+64 = <c>@</c>, Alt+0169 = <c>©</c>) en los campos de texto.
/// </summary>
/// <remarks>
/// Con Avalonia 11.2 en Windows el carácter que llegaba era otro (Alt+92 daba <c>┬</c> y Alt+64 <c>¶</c>; pruebas del
/// 2026-10-12, V8): el número que arma Windows no es el que se tecleó. Acá se anotan los dígitos del teclado
/// numérico mientras Alt está apretado y, cuando llega el carácter, se lo reemplaza por el que corresponde, con la
/// misma regla que Windows: sin cero inicial, la página de códigos OEM; con cero inicial, la ANSI. Para quien
/// transcribe a mano una contraseña o una ruta, una barra que sale como otro símbolo es un error silencioso.
/// </remarks>
public static class AltCodeInput
{
    private static readonly StringBuilder Digits = new();
    private static bool _pending;

    /// <summary>Se registra una sola vez para toda la aplicación (todas las ventanas y todos los TextBox).</summary>
    public static void Register()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyUpEvent.AddClassHandler<TextBox>(OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.TextInputEvent.AddClassHandler<TextBox>(OnTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnKeyDown(TextBox box, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            if (!_pending)
            {
                Digits.Clear();
                _pending = true;
            }

            Digits.Append((char)('0' + (e.Key - Key.NumPad0)));
        }
        else if (e.Key is not (Key.LeftAlt or Key.RightAlt or Key.NumLock))
        {
            Reset();
        }
    }

    private static void OnKeyUp(TextBox box, KeyEventArgs e)
    {
        // Al soltar Alt sin dígitos no hubo código: no hay nada que corregir.
        if (e.Key is Key.LeftAlt or Key.RightAlt && Digits.Length == 0)
        {
            _pending = false;
        }
    }

    private static void OnTextInput(TextBox box, TextInputEventArgs e)
    {
        if (!_pending || Digits.Length == 0)
        {
            return;
        }

        var composed = Compose(Digits.ToString());
        Reset();
        if (composed != null)
        {
            e.Text = composed;
        }
    }

    private static void Reset()
    {
        Digits.Clear();
        _pending = false;
    }

    /// <summary>El carácter que Windows arma con esos dígitos, o <c>null</c> si no corresponde a ninguno.</summary>
    public static string? Compose(string digits)
    {
        if (digits.Length == 0 || !long.TryParse(digits, out var value))
        {
            return null;
        }

        try
        {
            var codePage = digits[0] == '0' ? GetACP() : GetOEMCP();
            var b = (byte)(value % 256);
            if (b == 0)
            {
                return null;
            }

            var text = Encoding.GetEncoding((int)codePage).GetString(new[] { b });
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
