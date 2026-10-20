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
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using ForZip.Core.Shell;
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
        ForZip.GUI.Services.AltCodeInput.Register();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Calcular hash, Extraer y Verificar desde el menú contextual no levantan la aplicación entera: abren
            // solo su ventana, como hace 7-Zip (ver ShellWindowRouting).
            switch (ShellWindowRouting.WindowFor(desktop.Args))
            {
                case ShellWindow.QuickHash:
                    StartQuickHashMode(desktop);
                    break;
                case ShellWindow.Extract:
                    StartShellExtractMode(desktop);
                    break;
                case ShellWindow.Verify:
                    StartShellVerifyMode(desktop);
                    break;
                default:
                    StartFullApplication(desktop);
                    break;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Modo "hash rápido": una única ventana autónoma, sin barra lateral, sin bitácora y sin
    /// la oferta de integración con el Explorador (quien llega por acá ya la tiene integrada).
    /// </summary>
    private void StartQuickHashMode(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var localization = new LocalizationService();
        var configService = new ConfigService();

        var config = configService.Load();
        localization.SetLanguage(config.Language);
        new ThemeService().ApplyTheme(config.Theme);
        UpdateResources(localization);

        var viewModel = new QuickHashViewModel(
            new HashService(), new ReportService(localization), configService, localization);

        var window = new QuickHashWindow { DataContext = viewModel };
        desktop.MainWindow = window;

        var request = ShellArgs.Parse(desktop.Args);
        if (request != null)
        {
            viewModel.AddPaths(request.Paths);
        }

        // Multi-selección: el Explorador lanza un proceso por archivo. Los que no ganaron el
        // mutex reenvían sus rutas acá y esta ventana las acumula.
        SingleInstance?.StartServer(forwardedArgs =>
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var forwarded = ShellArgs.Parse(forwardedArgs);
                    if (forwarded?.Verb != ShellVerb.Hash)
                    {
                        return;
                    }

                    viewModel.AddPaths(forwarded.Paths);
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    window.Activate();
                }
                catch
                {
                    // No dejar que un reenvío malformado afecte a la ventana en uso.
                }
            }));

        desktop.Exit += (_, _) => SingleInstance?.Dispose();
    }

    /// <summary>
    /// Idioma, tema y bitácora de una ventana del menú contextual. La bitácora de evidencia escribe en el mismo
    /// archivo que la ventana completa y, con el perfil Reservado guardado, sin nombres ni rutas desde la primera
    /// entrada.
    /// </summary>
    private (LocalizationService localization, ConfigService config, ILogService log) StartShellWindowServices()
    {
        var localization = new LocalizationService();
        var configService = new ConfigService();
        var config = configService.Load();
        localization.SetLanguage(config.Language);
        new ThemeService().ApplyTheme(config.Theme);
        UpdateResources(localization);

        ILogService logService = new LogService
        {
            UiDispatcher = action => Dispatcher.UIThread.Post(action),
            RedactedMarker = localization.Get("log_redacted_marker"),
            Redacted = config.Profile == ConfidentialityProfile.Reserved
        };
        if (logService.Redacted)
        {
            logService.Info(localization.Get("log_profile_reserved"));
        }

        if (Environment.IsPrivilegedProcess)
        {
            logService.Warning(localization.Get("log_running_elevated"));
        }

        return (localization, configService, logService);
    }

    /// <summary>
    /// Instancia primaria de una ventana del menú contextual: las demás invocaciones de la misma ventana (un proceso
    /// por archivo seleccionado) le reenvían sus rutas, y la ventana las suma y se trae al frente.
    /// </summary>
    private static void ListenForForwardedRequests(IClassicDesktopStyleApplicationLifetime desktop, Window window,
                                                   ShellWindow kind, Action<ShellRequest> add)
    {
        SingleInstance?.StartServer(forwardedArgs =>
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var forwarded = ShellArgs.Parse(forwardedArgs);
                    if (forwarded == null || ShellWindowRouting.WindowFor(forwarded) != kind)
                    {
                        return;
                    }

                    add(forwarded);
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    window.Activate();
                }
                catch
                {
                    // No dejar que un reenvío malformado afecte a la ventana en uso.
                }
            }));

        desktop.Exit += (_, _) => SingleInstance?.Dispose();
    }

    /// <summary>"Extraer aquí", "Extraer en Nombre\" y "Extraer…": la ventana de extracción.</summary>
    private void StartShellExtractMode(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var (localization, configService, logService) = StartShellWindowServices();
        var hashService = new HashService();
        var zipService = new ZipService(hashService);
        var signatureService = new SignatureService();
        var verificationService = new VerificationService(hashService, signatureService);
        var sealedOpener = new SealedPackageOpener(new SealingService(), verificationService);
        var cloudSyncDetector = new CloudSyncDetector();
        var confirmDialog = new ConfirmDialogService();

        var viewModel = new ShellExtractViewModel(
            () => new UnzipViewModel(zipService, localization, configService, logService, sealedOpener,
                                     cloudSyncDetector, confirmDialog),
            logService, localization);
        var window = new ShellExtractWindow { DataContext = viewModel };
        desktop.MainWindow = window;

        if (ShellArgs.Parse(desktop.Args) is { } request)
        {
            viewModel.AddRequest(request);
        }

        ListenForForwardedRequests(desktop, window, ShellWindow.Extract, viewModel.AddRequest);
    }

    /// <summary>"Verificar": la ventana del veredicto.</summary>
    private void StartShellVerifyMode(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var (localization, configService, logService) = StartShellWindowServices();
        var hashService = new HashService();
        var zipService = new ZipService(hashService);
        var reportService = new ReportService(localization);
        var signatureService = new SignatureService();
        var verificationService = new VerificationService(hashService, signatureService);
        var sealingService = new SealingService();
        var receiptService = new ReceiptService(hashService, signatureService);
        var sealedVerificationService = new SealedVerificationService(
            sealingService, receiptService, signatureService, verificationService, hashService);

        var viewModel = new ShellVerifyViewModel(
            () => new VerifyReportViewModel(reportService, hashService, verificationService,
                                            sealedVerificationService, localization, configService, zipService),
            logService, localization);
        var window = new ShellVerifyWindow { DataContext = viewModel };
        desktop.MainWindow = window;

        if (ShellArgs.Parse(desktop.Args) is { } request)
        {
            viewModel.AddRequest(request);
        }

        ListenForForwardedRequests(desktop, window, ShellWindow.Verify, viewModel.AddRequest);
    }

    private void StartFullApplication(IClassicDesktopStyleApplicationLifetime desktop)
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
        // Perfil Reservado: el sobre sellado, su recibo y el acta de entrega (1.5.0).
        var sealingService = new SealingService();
        var receiptService = new ReceiptService(hashService, signatureService);
        var sealedPackageBuilder = new SealedPackageBuilder(sealingService, receiptService);
        var sealedVerificationService = new SealedVerificationService(
            sealingService, receiptService, signatureService, verificationService, hashService);
        var actService = new DeliveryActService(localization);
        var cloudSyncDetector = new CloudSyncDetector();
        var sealedPackageOpener = new SealedPackageOpener(sealingService, verificationService);
        var confirmDialog = new ConfirmDialogService();

        var updateService = new UpdateService();
        var updateInstaller = new UpdateInstaller();
        var updateDialog = new UpdateDialogService(localization);

        // Si venimos de un reemplazo, el ejecutable anterior sigue en disco como .old. Ahora que
        // aquel proceso ya murió, se puede borrar.
        updateInstaller.CleanupPreviousVersion();

        var mainViewModel = new MainWindowViewModel(
            zipService, hashService, passwordService,
            reportService, verificationService, signatureService, configService, localization, themeService,
            logService, operatorDialog, shellIntegration,
            sealedPackageBuilder, sealedVerificationService, actService, cloudSyncDetector,
            sealedPackageOpener, confirmDialog,
            updateService, updateInstaller, updateDialog);

        UpdateResources(localization);

        // Las etiquetas del menú contextual viven en el registro con el idioma del momento
        // en que se registró: al cambiar el idioma de la app hay que reescribirlas.
        localization.LanguageChanged += () => RefreshShellLabels(shellIntegration);

        // Si la app fue invocada desde el menú contextual del Explorador (verbo + ruta),
        // abrimos la vista correspondiente con el/los archivo(s) ya cargados.
        mainViewModel.HandleShellRequest(ShellArgs.Parse(desktop.Args));

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
                    mainViewModel.HandleShellRequest(ShellArgs.Parse(forwardedArgs));
                    if (mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = mainWindow.StateBeforeMinimize;
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
