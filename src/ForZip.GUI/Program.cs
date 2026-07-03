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

using System;
using Avalonia;
using ForZip.GUI.Services;

namespace ForZip.GUI;

internal class Program
{
    // Punto de entrada de la aplicación Avalonia.
    [STAThread]
    public static int Main(string[] args)
    {
        // Comandos headless de integración con el Explorador (sin abrir ventana).
        if (ShellIntegrationCli.TryHandle(args, out var exitCode))
        {
            return exitCode;
        }

        // Instancia única: si ya hay una abierta, le reenviamos los argumentos (p. ej. la acción
        // del menú contextual) para acumular en una sola ventana, y salimos. Tolerante a fallos.
        SingleInstance? single = null;
        try
        {
            single = new SingleInstance("ForZip");
            if (!single.IsFirstInstance)
            {
                if (single.TrySendArgs(args))
                {
                    single.Dispose();
                    return 0;
                }
                // No se pudo contactar la instancia primaria: seguir como ventana propia.
            }
        }
        catch
        {
            single = null; // fail-safe: arranque normal multi-ventana
        }

        App.SingleInstance = single is { IsFirstInstance: true } ? single : null;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Configuración de Avalonia, también usada por el diseñador visual.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
