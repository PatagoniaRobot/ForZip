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

using ForZip.Core.Sealing;

namespace ForZip.Core.Shell;

/// <summary>La ventana que abre una acción del menú contextual.</summary>
public enum ShellWindow
{
    /// <summary>ForZip entero: empaquetar, abrir un sobre, o arranque sin acción.</summary>
    Main,

    /// <summary>"Calcular hash": la ventanita de hashes.</summary>
    QuickHash,

    /// <summary>"Extraer aquí", "Extraer en Nombre\" y "Extraer…": la ventana de extracción.</summary>
    Extract,

    /// <summary>"Verificar": la ventana del veredicto.</summary>
    Verify
}

/// <summary>
/// Qué ventana abre cada acción del menú contextual. Quien hace clic derecho sobre un archivo quiere esa acción
/// puntual, no el programa entero (como 7-Zip y WinRAR). Empaquetar sigue en la ventana completa: es el flujo con
/// más decisiones y con el resultado que el perito tiene que ver entero (huella, informe, acta).
/// </summary>
public static class ShellWindowRouting
{
    public static ShellWindow WindowFor(ShellRequest? request)
    {
        if (request == null)
        {
            return ShellWindow.Main;
        }

        return request.Verb switch
        {
            ShellVerb.Hash => ShellWindow.QuickHash,
            // Un sobre no se extrae: se abre eligiendo destino y contraseña, con el freno de carpeta sincronizada,
            // y eso es la pantalla Extraer de la ventana completa.
            ShellVerb.Extract or ShellVerb.ExtractHere or ShellVerb.ExtractTo
                when !request.Paths.Any(IsSealed) => ShellWindow.Extract,
            ShellVerb.Verify => ShellWindow.Verify,
            _ => ShellWindow.Main
        };
    }

    public static ShellWindow WindowFor(string[]? args) => WindowFor(ShellArgs.Parse(args));

    /// <summary>
    /// Identificador de instancia única de cada ventana. Son canales aparte: una extracción no se acumula dentro de
    /// una ForZip abierta (ni al revés), pero las invocaciones de una misma acción sí se juntan en una sola ventana,
    /// porque el Explorador lanza un proceso por archivo seleccionado.
    /// </summary>
    public static string InstanceId(ShellWindow window) => window switch
    {
        ShellWindow.QuickHash => "ForZip.QuickHash",
        ShellWindow.Extract => "ForZip.Extract",
        ShellWindow.Verify => "ForZip.Verify",
        _ => "ForZip"
    };

    private static bool IsSealed(string path) =>
        path.EndsWith(SealedContainerFormat.SealedExtension, StringComparison.OrdinalIgnoreCase);
}
