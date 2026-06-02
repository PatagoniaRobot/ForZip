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
using ForZip.GUI.Services;

namespace ForZip.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IZipService _zipService;
    private readonly IHashService _hashService;
    private readonly IPasswordService _passwordService;
    private readonly IReportService _reportService;
    private readonly IVerificationService _verificationService;
    private readonly IConfigService _configService;
    private readonly IThemeService _themeService;
    private readonly ILogService _logService;
    private readonly IOperatorDialogService _operatorDialog;

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
        IConfigService configService,
        ILocalizationService localization,
        IThemeService themeService,
        ILogService logService,
        IOperatorDialogService operatorDialog) : base(localization)
    {
        _zipService = zipService;
        _hashService = hashService;
        _passwordService = passwordService;
        _reportService = reportService;
        _verificationService = verificationService;
        _configService = configService;
        _themeService = themeService;
        _logService = logService;
        _operatorDialog = operatorDialog;

        LogConsole = new LogConsoleViewModel(_logService);

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
        _zipVm ??= new ZipViewModel(_zipService, _hashService, _passwordService, _reportService, _localization, _configService, _logService, _operatorDialog);
        CurrentView = _zipVm;
        ActiveSection = "zip";
        _logService.Info("Navegando a: Comprimir");
    }

    [RelayCommand]
    private void NavigateToUnzip()
    {
        _unzipVm ??= new UnzipViewModel(_zipService, _localization, _configService, _logService);
        CurrentView = _unzipVm;
        ActiveSection = "unzip";
        _logService.Info("Navegando a: Extraer");
    }

    [RelayCommand]
    private void NavigateToHashBatch()
    {
        _hashBatchVm ??= new HashBatchViewModel(_hashService, _reportService, _localization, _configService, _logService, _operatorDialog);
        CurrentView = _hashBatchVm;
        ActiveSection = "hash";
        _logService.Info("Navegando a: Hash Batch");
    }

    [RelayCommand]
    private void NavigateToVerifyReport()
    {
        _verifyVm ??= new VerifyReportViewModel(_reportService, _hashService, _verificationService, _localization);
        CurrentView = _verifyVm;
        ActiveSection = "verify";
        _logService.Info("Navegando a: Verificar");
    }

    [RelayCommand]
    private void NavigateToPasswordGenerator()
    {
        _passwordGenVm ??= new PasswordGeneratorViewModel(_passwordService, _localization);
        CurrentView = _passwordGenVm;
        ActiveSection = "password";
        _logService.Info("Navegando a: Generador de Contraseñas");
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _settingsVm ??= new SettingsViewModel(_configService, _localization, _themeService);
        CurrentView = _settingsVm;
        ActiveSection = "settings";
        _logService.Info("Navegando a: Ajustes");
    }

    [RelayCommand]
    private void NavigateToAbout()
    {
        _aboutVm ??= new AboutViewModel(_localization);
        CurrentView = _aboutVm;
        ActiveSection = "about";
        _logService.Info("Navegando a: Acerca de");
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var newLang = _localization.CurrentLanguage == "es" ? "en" : "es";
        _localization.SetLanguage(newLang);
        CurrentLanguage = newLang;
    }
}
