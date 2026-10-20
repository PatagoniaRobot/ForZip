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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using ForZip.Core.Shell;
using ForZip.GUI.Services;

namespace ForZip.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IZipService _zipService;
    private readonly IHashService _hashService;
    private readonly IPasswordService _passwordService;
    private readonly IReportService _reportService;
    private readonly IVerificationService _verificationService;
    private readonly ISignatureService _signatureService;
    private readonly IConfigService _configService;
    private readonly IThemeService _themeService;
    private readonly ILogService _logService;
    private readonly IOperatorDialogService _operatorDialog;
    private readonly IShellIntegrationService _shellIntegration;
    private readonly ISealedPackageBuilder _sealedPackageBuilder;
    private readonly ISealedVerificationService _sealedVerificationService;
    private readonly IDeliveryActService _actService;
    private readonly ICloudSyncDetector _cloudSyncDetector;
    private readonly ISealedPackageOpener _sealedPackageOpener;
    private readonly IConfirmDialogService _confirmDialog;
    private readonly AboutViewModel _aboutVm;

    private HomeViewModel? _homeVm;
    private ZipViewModel? _zipVm;
    private UnzipViewModel? _unzipVm;
    private HashBatchViewModel? _hashBatchVm;
    private VerifyReportViewModel? _verifyVm;
    private PasswordGeneratorViewModel? _passwordGenVm;
    private SettingsViewModel? _settingsVm;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _activeSection = "home";

    [ObservableProperty]
    private string _currentLanguage = "es";

    [ObservableProperty]
    private LogConsoleViewModel _logConsole;

    public MainWindowViewModel(
        IZipService zipService,
        IHashService hashService,
        IPasswordService passwordService,
        IReportService reportService,
        IVerificationService verificationService,
        ISignatureService signatureService,
        IConfigService configService,
        ILocalizationService localization,
        IThemeService themeService,
        ILogService logService,
        IOperatorDialogService operatorDialog,
        IShellIntegrationService shellIntegration,
        ISealedPackageBuilder sealedPackageBuilder,
        ISealedVerificationService sealedVerificationService,
        IDeliveryActService actService,
        ICloudSyncDetector cloudSyncDetector,
        ISealedPackageOpener sealedPackageOpener,
        IConfirmDialogService confirmDialog,
        IUpdateService updateService,
        IUpdateInstaller updateInstaller,
        IUpdateDialogService updateDialog) : base(localization)
    {
        _zipService = zipService;
        _hashService = hashService;
        _passwordService = passwordService;
        _reportService = reportService;
        _verificationService = verificationService;
        _signatureService = signatureService;
        _configService = configService;
        _themeService = themeService;
        _logService = logService;
        _operatorDialog = operatorDialog;
        _shellIntegration = shellIntegration;
        _sealedPackageBuilder = sealedPackageBuilder;
        _sealedVerificationService = sealedVerificationService;
        _actService = actService;
        _cloudSyncDetector = cloudSyncDetector;
        _sealedPackageOpener = sealedPackageOpener;
        _confirmDialog = confirmDialog;

        LogConsole = new LogConsoleViewModel(_logService, localization);

        // Acerca de se crea de entrada (es solo texto) porque el aviso de versión nueva se
        // muestra en el menú lateral, sin esperar a que el usuario entre a la sección.
        _aboutVm = new AboutViewModel(
            localization, updateService, updateInstaller, updateDialog, configService, logService);
        _aboutVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AboutViewModel.IsUpdateAvailable))
            {
                OnPropertyChanged(nameof(MenuAbout));
            }
        };

        // Aplicar configuración guardada (idioma, tema) antes de inicializar la vista por defecto
        var config = _configService.Load();
        _localization.SetLanguage(config.Language);
        _themeService.ApplyTheme(config.Theme);
        CurrentLanguage = config.Language;
        _logService.RedactedMarker = _localization.Get("log_redacted_marker");

        // Empaquetar se crea de entrada aunque se arranque en Inicio: lee el perfil guardado y, si es Reservado,
        // apaga nombres y rutas en la bitácora antes de la primera entrada de cualquier sección.
        EnsureZipViewModel();
        NavigateToHome();

        if (IsElevated)
        {
            _logService.Warning(_localization.Get("log_running_elevated"));
        }

        if (!TryShowPostUpdateSummary(config) && config.CheckForUpdatesOnStartup)
        {
            // Sin await: la consulta es opcional y no debe demorar la apertura de la ventana.
            // El comando absorbe sus propios errores, así que no puede tumbar el arranque.
            _ = _aboutVm.CheckUpdatesCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Primer arranque después de un reemplazo: abre Acerca de con el resumen de qué cambió y
    /// borra las marcas para que el aviso no se repita. Devuelve <c>true</c> si hubo actualización.
    /// </summary>
    private bool TryShowPostUpdateSummary(AppConfig config)
    {
        var previous = config.UpdatedFromVersion;
        if (string.IsNullOrWhiteSpace(previous))
        {
            return false;
        }

        _aboutVm.ShowPostUpdateSummary(previous!, config.UpdatedReleaseNotes);
        NavigateToAbout();

        _logService.Success(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _localization.Get("log_update_done"),
            previous,
            Core.AppInfo.Version));

        config.UpdatedFromVersion = null;
        config.UpdatedReleaseNotes = null;
        _configService.Save(config);

        return true;
    }

    public string AppTitle => IsReservedProfileActive
        ? $"ForZip {Core.AppInfo.DisplayVersion} — {LabelProfileBadge}"
        : $"ForZip {Core.AppInfo.DisplayVersion}";

    /// <summary>Versión mostrada en el pie del sidebar (fuente única: AppInfo).</summary>
    public string AppVersion => Core.AppInfo.DisplayVersion;
    // El icono ya no viaja adentro del texto: cada boton de la barra lleva su geometria
    // (Styles/Icons.axaml) y estas propiedades son solo la etiqueta.
    public string MenuHome => _localization.Get("menu_home");
    public string MenuToolsHeader => _localization.Get("menu_tools_header");
    public string LabelLogToggle => _localization.Get("log_toggle");
    public string MenuZip => _localization.Get("compress");
    public string MenuUnzip => _localization.Get("extract");
    public string MenuHashBatch => _localization.Get("hash_batch");
    public string MenuVerify => _localization.Get("verify");
    public string MenuPassword => _localization.Get("password_gen");
    public string MenuSettings => _localization.Get("settings");
    /// <summary>El punto marca que hay una versión nueva publicada, sin robar el foco.</summary>
    public string MenuAbout => _aboutVm.IsUpdateAvailable
        ? $"{_localization.Get("about")}   •"
        : _localization.Get("about");
    public string StatusReady => _localization.Get("ready");

    /// <summary>
    /// ForZip corre como administrador. Windows no deja arrastrar desde un Explorador normal a una ventana
    /// elevada, y el candado de instancia única de esta ventana no lo puede abrir un proceso normal: cada acción
    /// del menú contextual abría otra ventana. No se tiende un puente a propósito (un programa sin permisos podría
    /// hacerle leer al ForZip elevado archivos protegidos); se avisa, porque ForZip no necesita administrador.
    /// </summary>
    public bool IsElevated { get; } = Environment.IsPrivilegedProcess;

    public string StatusElevated => _localization.Get("status_running_elevated");
    public string TooltipElevated => _localization.Get("status_running_elevated_tip");

    /// <summary>Servicio de idioma, para los diálogos que la ventana abre por su cuenta.</summary>
    public ILocalizationService Localization => _localization;

    /// <summary>
    /// Hay una operación larga en curso en alguna de las secciones.
    /// </summary>
    /// <remarks>
    /// Se consultan solo las vistas que escriben sobre la evidencia o sobre el disco —empaquetar,
    /// extraer, hashear en lote— más la descarga de una actualización. Las secciones que solo
    /// leen o muestran no cuentan: interrumpirlas no deja nada a medio escribir.
    /// Las vistas se crean recién al visitarlas, así que una nula es una sección que ni se abrió.
    /// </remarks>
    public bool IsOperationRunning =>
        (_zipVm?.IsProcessing ?? false)
        || (_unzipVm?.IsProcessing ?? false)
        || (_hashBatchVm?.IsProcessing ?? false)
        || _aboutVm.IsBusy;

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AppTitle));
        OnPropertyChanged(nameof(MenuHome));
        OnPropertyChanged(nameof(MenuToolsHeader));
        OnPropertyChanged(nameof(LabelLogToggle));
        OnPropertyChanged(nameof(MenuZip));
        OnPropertyChanged(nameof(MenuUnzip));
        OnPropertyChanged(nameof(MenuHashBatch));
        OnPropertyChanged(nameof(MenuVerify));
        OnPropertyChanged(nameof(MenuPassword));
        OnPropertyChanged(nameof(MenuSettings));
        OnPropertyChanged(nameof(MenuAbout));
        OnPropertyChanged(nameof(StatusReady));
        OnPropertyChanged(nameof(StatusElevated));
        OnPropertyChanged(nameof(TooltipElevated));
        NotifyProfileBadge();
        _logService.RedactedMarker = _localization.Get("log_redacted_marker");
    }

    // ---- Perfil activo, a la vista en todas las secciones ----
    //
    // El perfil se elige en Empaquetar pero gobierna la bitácora de toda la aplicación: con Reservado activo,
    // Extraer y Verificar tampoco registran nombres ni rutas. Si solo se veía en Empaquetar, se podía trabajar una
    // tanda entera en Reservado sin saberlo (pasó en las pruebas del 2026-10-12).

    /// <summary>El perfil Reservado está activo (elegido o de un solo uso desde el menú contextual).</summary>
    public bool IsReservedProfileActive => _zipVm?.IsReservedProfile == true;

    public string LabelProfileBadge => string.Format(_localization.Get("profile_badge"),
        _localization.Get(IsReservedProfileActive ? "profile_reserved" : "profile_standard"));

    public string TooltipProfileBadge => _localization.Get(
        IsReservedProfileActive ? "profile_badge_reserved_tip" : "profile_badge_standard_tip");

    private void OnZipViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ZipViewModel.IsReservedProfile))
        {
            NotifyProfileBadge();
        }
    }

    private void NotifyProfileBadge()
    {
        OnPropertyChanged(nameof(IsReservedProfileActive));
        OnPropertyChanged(nameof(LabelProfileBadge));
        OnPropertyChanged(nameof(TooltipProfileBadge));
        OnPropertyChanged(nameof(AppTitle));
    }

    [RelayCommand]
    private void NavigateToHome()
    {
        _homeVm ??= new HomeViewModel(
            _localization, NavigateToZipCommand, NavigateToUnzipCommand, NavigateToVerifyReportCommand);
        CurrentView = _homeVm;
        ActiveSection = "home";
        LogNavigation("menu_home");
    }

    private ZipViewModel EnsureZipViewModel()
    {
        if (_zipVm == null)
        {
            _zipVm = new ZipViewModel(_zipService, _hashService, _passwordService, _reportService, _signatureService, _localization, _configService, _logService, _operatorDialog, _sealedPackageBuilder, _actService, _cloudSyncDetector);
            _zipVm.PropertyChanged += OnZipViewModelChanged;
            NotifyProfileBadge();
        }

        return _zipVm;
    }

    [RelayCommand]
    private void NavigateToZip()
    {
        CurrentView = EnsureZipViewModel();
        ActiveSection = "zip";
        LogNavigation("compress");
    }

    [RelayCommand]
    private void NavigateToUnzip()
    {
        _unzipVm ??= new UnzipViewModel(
            _zipService, _localization, _configService, _logService,
            _sealedPackageOpener, _cloudSyncDetector, _confirmDialog);
        CurrentView = _unzipVm;
        ActiveSection = "unzip";
        LogNavigation("extract");
    }

    [RelayCommand]
    private void NavigateToHashBatch()
    {
        _hashBatchVm ??= new HashBatchViewModel(_hashService, _reportService, _localization, _configService, _logService, _operatorDialog);
        CurrentView = _hashBatchVm;
        ActiveSection = "hash";
        LogNavigation("hash_batch");
    }

    [RelayCommand]
    private void NavigateToVerifyReport()
    {
        _verifyVm ??= new VerifyReportViewModel(_reportService, _hashService, _verificationService, _sealedVerificationService, _localization, _configService, _zipService);
        CurrentView = _verifyVm;
        ActiveSection = "verify";
        LogNavigation("verify");
    }

    [RelayCommand]
    private void NavigateToPasswordGenerator()
    {
        _passwordGenVm ??= new PasswordGeneratorViewModel(_passwordService, _localization);
        CurrentView = _passwordGenVm;
        ActiveSection = "password";
        LogNavigation("password_gen");
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _settingsVm ??= new SettingsViewModel(_configService, _localization, _themeService, _shellIntegration);
        CurrentView = _settingsVm;
        ActiveSection = "settings";
        LogNavigation("settings");
    }

    [RelayCommand]
    private void NavigateToAbout()
    {
        CurrentView = _aboutVm;
        ActiveSection = "about";
        LogNavigation("about");
    }

    /// <summary>
    /// Procesa una invocación desde el menú contextual del Explorador: navega a la vista
    /// correspondiente y carga el/los archivo(s). No hace nada si no hay petición accionable.
    /// </summary>
    /// <summary>
    /// Cuándo llegó el último archivo para una vista que trabaja de a uno (Extraer, Verificar). Con varios archivos
    /// elegidos, el Explorador lanza un proceso por cada uno y cada uno reenvía el suyo: el siguiente pisaba al
    /// anterior y quedaba cargado solo el último, sin aviso.
    /// </summary>
    private DateTime _singleTargetLoadedAt = DateTime.MinValue;

    private static readonly TimeSpan MultiSelectionWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Para Extraer y Verificar, que trabajan de a un archivo: si llega otro en la misma tanda (multi-selección), se
    /// queda el primero y el resto se avisa, en vez de reemplazarlo en silencio.
    /// </summary>
    private bool AcceptSingleTarget(ShellRequest request)
    {
        var now = DateTime.UtcNow;
        var sameBatch = now - _singleTargetLoadedAt < MultiSelectionWindow;
        _singleTargetLoadedAt = now;
        var ignored = sameBatch ? request.Paths : request.Paths.Skip(1);
        var names = string.Join(", ", ignored.Select(Path.GetFileName));
        if (names.Length > 0)
        {
            var message = _localization.Get("shell_one_at_a_time");
            _logService.LogEvidence(LogLevel.Warning, message, names);
        }

        return !sameBatch;
    }

    public void HandleShellRequest(ShellRequest? request)
    {
        if (request == null || request.Paths.Count == 0)
        {
            return;
        }

        if (request.Verb is ShellVerb.Extract or ShellVerb.ExtractHere or ShellVerb.ExtractTo
                or ShellVerb.OpenEnvelope or ShellVerb.Verify
            && !AcceptSingleTarget(request))
        {
            return;
        }

        try
        {
            switch (request.Verb)
            {
                case ShellVerb.Compress:
                    NavigateToZip();
                    _zipVm!.AddPaths(request.Paths);
                    break;

                case ShellVerb.CompressReserved:
                    NavigateToZip();
                    // Mismo efecto que elegir Reservado en la pantalla —cifrado, SHA-256 y
                    // contraseña generada fijados, bitácora redactada—, pero solo para este
                    // paquete: la opción del menú no cambia el perfil predeterminado.
                    _zipVm!.UseReservedProfileOnce();
                    _zipVm.AddPaths(request.Paths);
                    break;

                case ShellVerb.Hash:
                    NavigateToHashBatch();
                    _hashBatchVm!.AddPaths(request.Paths);
                    break;

                case ShellVerb.Extract:
                case ShellVerb.ExtractHere:
                case ShellVerb.ExtractTo:
                    NavigateToUnzip();
                    ConfigureUnzip(request.Verb, request.Paths[0]);
                    break;

                case ShellVerb.OpenEnvelope:
                    // Doble clic o "Abrir sobre…": se carga el sobre y nada más. La contraseña y
                    // el destino los elige el operador en la pantalla.
                    NavigateToUnzip();
                    _unzipVm!.SetZipPath(request.Paths[0]);
                    break;

                case ShellVerb.Verify:
                    NavigateToVerifyReport();
                    _verifyVm!.ReportFilePath = VerifyReportViewModel.ResolveVerifyTarget(request.Paths[0]);
                    break;
            }

            _logService.Info(string.Format(_localization.Get("log_shell_action"), request.Verb, request.Paths.Count));
        }
        catch (Exception ex)
        {
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_shell_action_error"), ex.Message);
        }
    }

    /// <summary>Registra la navegación en la bitácora, con el nombre localizado de la sección.</summary>
    private void LogNavigation(string sectionKey)
        => _logService.Info(string.Format(_localization.Get("log_nav"), _localization.Get(sectionKey)));

    private void ConfigureUnzip(ShellVerb verb, string archivePath)
    {
        _unzipVm!.SetZipPath(archivePath);
        _unzipVm.OutputDirectory = UnzipViewModel.DestinationFor(verb, archivePath);
    }

    /// <summary>
    /// Cambia el idioma y lo guarda, como Ajustes. Antes no se guardaba: al reabrir ForZip volvía el idioma
    /// anterior y los informes salían en ese idioma aunque se hubiera elegido otro.
    /// </summary>
    [RelayCommand]
    private void ToggleLanguage()
    {
        var newLang = _localization.CurrentLanguage == "es" ? "en" : "es";
        _localization.SetLanguage(newLang);
        CurrentLanguage = newLang;

        try
        {
            var config = _configService.Load();
            if (config.Language != newLang)
            {
                config.Language = newLang;
                _configService.Save(config);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin poder guardar (medio de solo lectura), el cambio vale para esta sesión.
        }

        // Ajustes, si ya se abrió, muestra el idioma vigente: guardar desde ahí no tiene que volver al anterior.
        if (_settingsVm != null)
        {
            _settingsVm.Language = newLang;
        }
    }
}
