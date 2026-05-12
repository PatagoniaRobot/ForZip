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

namespace ForZip.GUI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly int[] Levels = { 0, 1, 3, 5, 7, 9 };

    private readonly IConfigService _configService;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private string _language = "es";

    [ObservableProperty]
    private string _theme = "dark";

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private int _defaultCompressionLevel = 5;

    [ObservableProperty]
    private bool _defaultUseMd5;

    [ObservableProperty]
    private bool _defaultUseSha1;

    [ObservableProperty]
    private bool _defaultUseSha256 = true;

    [ObservableProperty]
    private bool _defaultUseSha512;

    [ObservableProperty]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string _operatorTitle = string.Empty;

    [ObservableProperty]
    private string _operatorOrganization = string.Empty;

    [ObservableProperty]
    private string _operatorEmail = string.Empty;

    [ObservableProperty]
    private string _operatorPhone = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel(
        IConfigService configService,
        ILocalizationService localization,
        IThemeService themeService) : base(localization)
    {
        _configService = configService;
        _themeService = themeService;
        LoadFromConfig();
    }

    public string Title => _localization.Get("settings").ToUpper();
    public string TabGeneral => _localization.Get("settings_general");
    public string TabOperator => _localization.Get("settings_operator");
    public string TabDefaults => _localization.Get("settings_defaults");
    public string LabelLanguage => _localization.Get("settings_language");
    public string LabelTheme => _localization.Get("settings_theme");
    public string LabelThemeDark => _localization.Get("settings_theme_dark");
    public string LabelThemeLight => _localization.Get("settings_theme_light");
    public string LabelOutputDir => _localization.Get("settings_output_dir");
    public string ButtonSave => _localization.Get("save");
    public string ButtonReset => _localization.Get("settings_reset");

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(TabGeneral));
        OnPropertyChanged(nameof(TabOperator));
        OnPropertyChanged(nameof(TabDefaults));
        OnPropertyChanged(nameof(LabelLanguage));
        OnPropertyChanged(nameof(LabelTheme));
        OnPropertyChanged(nameof(LabelThemeDark));
        OnPropertyChanged(nameof(LabelThemeLight));
        OnPropertyChanged(nameof(LabelOutputDir));
        OnPropertyChanged(nameof(ButtonSave));
        OnPropertyChanged(nameof(ButtonReset));
        OnPropertyChanged(nameof(StatusMessage));
    }

    public IEnumerable<int> AvailableLevels => Levels;
    public string[] AvailableLanguages => new[] { "es", "en" };

    [RelayCommand]
    private void Save()
    {
        var config = new AppConfig
        {
            Language = Language,
            Theme = Theme,
            DefaultCompressionLevel = DefaultCompressionLevel,
            DefaultHashAlgorithms = BuildAlgorithms(),
            DefaultOutputDirectory = string.IsNullOrEmpty(OutputDirectory) ? null : OutputDirectory,
            Operator = new OperatorInfo
            {
                Name = ToNullIfEmpty(OperatorName),
                Title = ToNullIfEmpty(OperatorTitle),
                Organization = ToNullIfEmpty(OperatorOrganization),
                Email = ToNullIfEmpty(OperatorEmail),
                Phone = ToNullIfEmpty(OperatorPhone)
            }
        };

        _configService.Save(config);
        _localization.SetLanguage(Language);
        _themeService.ApplyTheme(Theme);
        StatusMessage = _localization.Get("settings_saved");
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        var defaults = new AppConfig();
        ApplyConfig(defaults);
        StatusMessage = _localization.Get("settings_saved");
    }

    partial void OnLanguageChanged(string value)
    {
        _localization.SetLanguage(value);
    }

    partial void OnThemeChanged(string value)
    {
        _themeService.ApplyTheme(value);
    }

    private void LoadFromConfig()
    {
        var config = _configService.Load();
        ApplyConfig(config);
        _localization.SetLanguage(Language);
        _themeService.ApplyTheme(Theme);
    }

    private void ApplyConfig(AppConfig config)
    {
        Language = config.Language;
        Theme = config.Theme;
        OutputDirectory = config.DefaultOutputDirectory ?? string.Empty;
        DefaultCompressionLevel = config.DefaultCompressionLevel;

        DefaultUseMd5 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.MD5);
        DefaultUseSha1 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA1);
        DefaultUseSha256 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA256);
        DefaultUseSha512 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA512);

        OperatorName = config.Operator.Name ?? string.Empty;
        OperatorTitle = config.Operator.Title ?? string.Empty;
        OperatorOrganization = config.Operator.Organization ?? string.Empty;
        OperatorEmail = config.Operator.Email ?? string.Empty;
        OperatorPhone = config.Operator.Phone ?? string.Empty;
    }

    private HashSet<HashAlgorithmType> BuildAlgorithms()
    {
        var set = new HashSet<HashAlgorithmType>();
        if (DefaultUseMd5) set.Add(HashAlgorithmType.MD5);
        if (DefaultUseSha1) set.Add(HashAlgorithmType.SHA1);
        if (DefaultUseSha256) set.Add(HashAlgorithmType.SHA256);
        if (DefaultUseSha512) set.Add(HashAlgorithmType.SHA512);
        return set;
    }

    private static string? ToNullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
