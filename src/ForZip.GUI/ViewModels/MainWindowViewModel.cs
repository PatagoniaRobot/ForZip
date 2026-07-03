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

    private ZipViewModel? _zipVm;
    private UnzipViewModel? _unzipVm;
    private HashBatchViewModel? _hashBatchVm;
    private VerifyReportViewModel? _verifyVm;
    private PasswordGeneratorViewModel? _passwordGenVm;
    private SettingsViewModel? _settingsVm;
    private AboutViewModel? _aboutVm;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _activeSection = "zip";

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
        IShellIntegrationService shellIntegration) : base(localization)
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

        LogConsole = new LogConsoleViewModel(_logService, localization);

        // Aplicar configuración guardada (idioma, tema) antes de inicializar la vista por defecto
        var config = _configService.Load();
        _localization.SetLanguage(config.Language);
        _themeService.ApplyTheme(config.Theme);
        CurrentLanguage = config.Language;

        NavigateToZip();
    }

    public string AppTitle => $"ForZip {_localization.Get("app_version")}";
    public string MenuZip => $"📦  {_localization.Get("compress")}";
    public string MenuUnzip => $"📂  {_localization.Get("extract")}";
    public string MenuHashBatch => $"#   {_localization.Get("hash_batch")}";
    public string MenuVerify => $"✓   {_localization.Get("verify")}";
    public string MenuPassword => $"🔑  {_localization.Get("password_gen")}";
    public string MenuSettings => $"⚙   {_localization.Get("settings")}";
    public string MenuAbout => $"ℹ   {_localization.Get("about")}";
    public string StatusReady => _localization.Get("ready");

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AppTitle));
        OnPropertyChanged(nameof(MenuZip));
        OnPropertyChanged(nameof(MenuUnzip));
        OnPropertyChanged(nameof(MenuHashBatch));
        OnPropertyChanged(nameof(MenuVerify));
        OnPropertyChanged(nameof(MenuPassword));
        OnPropertyChanged(nameof(MenuSettings));
        OnPropertyChanged(nameof(MenuAbout));
        OnPropertyChanged(nameof(StatusReady));
    }

    [RelayCommand]
    private void NavigateToZip()
    {
        _zipVm ??= new ZipViewModel(_zipService, _hashService, _passwordService, _reportService, _signatureService, _localization, _configService, _logService, _operatorDialog);
        CurrentView = _zipVm;
        ActiveSection = "zip";
        LogNavigation("compress");
    }

    [RelayCommand]
    private void NavigateToUnzip()
    {
        _unzipVm ??= new UnzipViewModel(_zipService, _localization, _configService, _logService);
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
        _verifyVm ??= new VerifyReportViewModel(_reportService, _hashService, _verificationService, _localization);
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
        _aboutVm ??= new AboutViewModel(_localization);
        CurrentView = _aboutVm;
        ActiveSection = "about";
        LogNavigation("about");
    }

    /// <summary>
    /// Procesa una invocación desde el menú contextual del Explorador: navega a la vista
    /// correspondiente y carga el/los archivo(s). No hace nada si no hay petición accionable.
    /// </summary>
    public void HandleShellRequest(ShellRequest? request)
    {
        if (request == null || request.Paths.Count == 0)
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

                case ShellVerb.Verify:
                    NavigateToVerifyReport();
                    _verifyVm!.ReportFilePath = ResolveVerifyTarget(request.Paths[0]);
                    break;
            }

            _logService.Info(string.Format(_localization.Get("log_shell_action"), request.Verb, request.Paths.Count));
        }
        catch (Exception ex)
        {
            _logService.Error(string.Format(_localization.Get("log_shell_action_error"), ex.Message));
        }
    }

    /// <summary>Registra la navegación en la bitácora, con el nombre localizado de la sección.</summary>
    private void LogNavigation(string sectionKey)
        => _logService.Info(string.Format(_localization.Get("log_nav"), _localization.Get(sectionKey)));

    private void ConfigureUnzip(ShellVerb verb, string archivePath)
    {
        _unzipVm!.SetZipPath(archivePath);

        // Si es un volumen .001, la ruta lógica (caso.zip) da el nombre/carpeta correctos.
        var logical = SplitArchive.GetBasePath(archivePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(logical)) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(logical);

        _unzipVm.OutputDirectory = verb == ShellVerb.ExtractHere
            ? dir                              // Extraer aquí: misma carpeta
            : Path.Combine(dir, name);         // Extraer a "Nombre\" (y sugerencia para Extraer…)
    }

    private static string ResolveVerifyTarget(string path)
    {
        // Firma desacoplada: verificar el manifiesto al que acompaña
        // (caso.zip.manifest.json.p7s → caso.zip.manifest.json).
        if (path.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase))
        {
            var signedFile = path[..^".p7s".Length];
            if (File.Exists(signedFile))
            {
                return signedFile;
            }
        }

        // Informe o manifiesto: se usan tal cual.
        if (path.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        // ZIP o volumen .001: la verificación forense usa el manifiesto <zip>.manifest.json.
        var logical = SplitArchive.GetBasePath(path);
        var manifest = logical + ".manifest.json";
        return File.Exists(manifest) ? manifest : path;
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var newLang = _localization.CurrentLanguage == "es" ? "en" : "es";
        _localization.SetLanguage(newLang);
        CurrentLanguage = newLang;
    }
}
