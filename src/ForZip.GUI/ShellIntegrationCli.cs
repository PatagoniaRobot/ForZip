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
using ForZip.Core.Services;
using ForZip.GUI.Services;

namespace ForZip.GUI;

/// <summary>
/// Maneja, antes de arrancar la interfaz, los comandos headless de integración con el menú
/// contextual: <c>--register-shell</c>, <c>--unregister-shell</c>, <c>--shell-status</c>.
/// Permite a scripts (o a la propia app) registrar/quitar la integración sin abrir ventana.
/// </summary>
internal static class ShellIntegrationCli
{
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0)
        {
            return false;
        }

        var cmd = args[0].ToLowerInvariant();
        if (cmd is not ("--register-shell" or "--unregister-shell" or "--shell-status"))
        {
            return false;
        }

        AttachParentConsole();

        // Las etiquetas del menú deben salir en el idioma configurado por el usuario
        // (config.json), no en el auto-detectado del sistema operativo.
        var localization = new LocalizationService();
        try
        {
            localization.SetLanguage(new ConfigService().Load().Language);
        }
        catch
        {
            // Sin config legible: se mantiene el idioma auto-detectado.
        }

        var service = new ShellIntegrationService(localization);
        if (!service.IsSupported)
        {
            Console.WriteLine("La integración con el menú contextual solo está disponible en Windows.");
            exitCode = 1;
            return true;
        }

        try
        {
            switch (cmd)
            {
                case "--register-shell":
                    service.Register();
                    Console.WriteLine($"Menú contextual de ForZip registrado para: {service.CurrentExePath}");
                    break;
                case "--unregister-shell":
                    service.Unregister();
                    Console.WriteLine("Menú contextual de ForZip eliminado.");
                    break;
                case "--shell-status":
                    Console.WriteLine(service.IsRegistered()
                        ? $"Registrado. Ejecutable: {service.GetRegisteredPath()}"
                        : "No registrado.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            exitCode = 2;
        }

        return true;
    }

    // Una app WinExe no tiene consola; nos enganchamos a la del proceso padre (si la hay)
    // para que la salida de los comandos headless se vea al ejecutarlos desde una terminal.
    private static void AttachParentConsole()
    {
        if (OperatingSystem.IsWindows())
        {
            try { AttachConsole(AttachParentProcess); } catch { /* sin consola: se ignora */ }
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
}
