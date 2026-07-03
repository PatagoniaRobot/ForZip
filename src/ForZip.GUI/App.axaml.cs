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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ForZip.Core.Interfaces;
using ForZip.Core.Services;
using ForZip.GUI.Services;
using ForZip.GUI.ViewModels;
using ForZip.GUI.Views;

namespace ForZip.GUI;

public partial class App : Application
{
    /// <summary>Coordinador de instancia única (solo lo asigna la instancia primaria).</summary>
    public static SingleInstance? SingleInstance { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: instanciamos servicios con sus dependencias resueltas a mano
            var localization = new LocalizationService();
            var logService = new LogService
            {
                // Marshalar las mutaciones de la colección al hilo de UI de Avalonia
                UiDispatcher = action => Avalonia.Threading.Dispatcher.UIThread.Post(action)
            };
            var hashService = new HashService();
            var passwordService = new PasswordService();
            var zipService = new ZipService(hashService);
            var reportService = new ReportService(localization);
            var signatureService = new SignatureService();
            var verificationService = new VerificationService(hashService, signatureService);
            var configService = new ConfigService();
            var themeService = new ThemeService();
            var operatorDialog = new OperatorDialogService();
            var shellIntegration = new ShellIntegrationService(localization);

            var mainViewModel = new MainWindowViewModel(
                zipService, hashService, passwordService,
                reportService, verificationService, signatureService, configService, localization, themeService,
                logService, operatorDialog, shellIntegration);

            UpdateResources(localization);

            // Las etiquetas del menú contextual viven en el registro con el idioma del momento
            // en que se registró: al cambiar el idioma de la app hay que reescribirlas.
            localization.LanguageChanged += () => RefreshShellLabels(shellIntegration);

            // Si la app fue invocada desde el menú contextual del Explorador (verbo + ruta),
            // abrimos la vista correspondiente con el/los archivo(s) ya cargados.
            mainViewModel.HandleShellRequest(ForZip.Core.Shell.ShellArgs.Parse(desktop.Args));

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow = mainWindow;

            // Al abrir la ventana: reparar una integración con ruta muerta (exe movido /
            // otra letra de USB) u ofrecer la integración la primera vez en esta PC.
            mainWindow.Opened += async (_, _) =>
            {
                try
                {
                    await OfferOrRepairShellIntegrationAsync(shellIntegration, logService, localization, mainWindow);
                }
                catch
                {
                    // La integración del shell nunca debe afectar el arranque de la app.
                }
            };

            // Instancia primaria: escucha reenvíos de otras invocaciones (multi-selección del
            // menú contextual) y los rutea a esta misma ventana, trayéndola al frente.
            SingleInstance?.StartServer(forwardedArgs =>
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        mainViewModel.HandleShellRequest(ForZip.Core.Shell.ShellArgs.Parse(forwardedArgs));
                        if (mainWindow.WindowState == WindowState.Minimized)
                        {
                            mainWindow.WindowState = WindowState.Normal;
                        }
                        mainWindow.Activate();
                    }
                    catch
                    {
                        // No dejar que un reenvío malformado afecte a la app en ejecución.
                    }
                }));

            desktop.Exit += (_, _) => SingleInstance?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void UpdateResources(ILocalizationService localization)
    {
        foreach (var key in localization.GetAllKeys())
        {
            Resources[key] = localization.Get(key);
        }
    }

    /// <summary>
    /// Mantenimiento de la integración con el menú contextual al arrancar:
    /// <list type="bullet">
    /// <item>Registrada pero apuntando a un exe que ya no existe → se re-registra en
    /// silencio con la ruta actual (típico: carpeta movida o USB con otra letra).
    /// Nunca pisa una integración cuyo exe registrado sigue existiendo (otra copia viva).</item>
    /// <item>Nunca registrada en esta PC → se ofrece una única vez integrarla.</item>
    /// </list>
    /// </summary>
    private static async Task OfferOrRepairShellIntegrationAsync(
        IShellIntegrationService shell, ILogService log, ILocalizationService localization, Window owner)
    {
        if (!shell.IsSupported)
        {
            return;
        }

        var registeredPath = shell.GetRegisteredPath();
        if (registeredPath != null)
        {
            if (string.Equals(registeredPath, shell.CurrentExePath, StringComparison.OrdinalIgnoreCase))
            {
                // Re-registro idempotente: refresca las etiquetas por si el idioma configurado
                // cambió desde la última vez (p. ej. se guardó otro idioma y se cerró la app).
                shell.Register();
            }
            else if (!File.Exists(registeredPath))
            {
                shell.Register();
                log.Info(localization.Get("log_shell_repaired"));
            }
            return;
        }

        if (shell.WasOfferShown())
        {
            return;
        }
        shell.MarkOfferShown();

        var accepted = await new ShellOfferView().ShowDialog<bool>(owner);
        if (accepted)
        {
            shell.Register();
            log.Success(localization.Get("log_shell_integrated"));
        }
    }

    /// <summary>
    /// Reescribe las entradas del menú contextual con el idioma actual, solo si la
    /// integración pertenece a este ejecutable (no pisa otra copia de ForZip).
    /// </summary>
    private static void RefreshShellLabels(IShellIntegrationService shell)
    {
        try
        {
            if (shell.IsSupported &&
                string.Equals(shell.GetRegisteredPath(), shell.CurrentExePath, StringComparison.OrdinalIgnoreCase))
            {
                shell.Register();
            }
        }
        catch
        {
            // El refresco de etiquetas nunca debe interrumpir la app.
        }
    }
}
