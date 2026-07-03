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
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.GUI.ViewModels;

public partial class PasswordGeneratorViewModel : ObservableObject
{
    private readonly IPasswordService _passwordService;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private int _length = 16;

    [ObservableProperty]
    private bool _includeUppercase = true;

    [ObservableProperty]
    private bool _includeLowercase = true;

    [ObservableProperty]
    private bool _includeDigits = true;

    [ObservableProperty]
    private bool _includeSymbols = true;

    [ObservableProperty]
    private bool _excludeAmbiguous;

    [ObservableProperty]
    private string _generatedPassword = string.Empty;

    [ObservableProperty]
    private double _entropyBits;

    [ObservableProperty]
    private string _entropyLabel = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public PasswordGeneratorViewModel(
        IPasswordService passwordService,
        ILocalizationService localization)
    {
        _passwordService = passwordService;
        _localization = localization;
        Generate();
    }

    [RelayCommand]
    private void Generate()
    {
        try
        {
            var options = BuildOptions();
            GeneratedPassword = _passwordService.GeneratePassword(options);
            EntropyBits = _passwordService.CalculateEntropy(options, options.Length);
            EntropyLabel = ComputeEntropyLabel(EntropyBits);
            StatusMessage = string.Empty;
        }
        catch (ArgumentException ex)
        {
            GeneratedPassword = string.Empty;
            EntropyBits = 0;
            EntropyLabel = string.Empty;
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (string.IsNullOrEmpty(GeneratedPassword)) return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(GeneratedPassword);
                StatusMessage = _localization.Get("copied");
            }
        }
    }

    private PasswordOptions BuildOptions()
    {
        return new PasswordOptions
        {
            Length = Length,
            IncludeUppercase = IncludeUppercase,
            IncludeLowercase = IncludeLowercase,
            IncludeDigits = IncludeDigits,
            IncludeSymbols = IncludeSymbols,
            ExcludeAmbiguous = ExcludeAmbiguous
        };
    }

    private string ComputeEntropyLabel(double bits)
    {
        if (bits < 40) return _localization.Get("entropy_weak");
        if (bits < 60) return _localization.Get("entropy_fair");
        if (bits < 80) return _localization.Get("entropy_strong");
        return _localization.Get("entropy_very_strong");
    }

    // Cualquier cambio en las opciones dispara una nueva generación
    partial void OnLengthChanged(int value) => Generate();
    partial void OnIncludeUppercaseChanged(bool value) => Generate();
    partial void OnIncludeLowercaseChanged(bool value) => Generate();
    partial void OnIncludeDigitsChanged(bool value) => Generate();
    partial void OnIncludeSymbolsChanged(bool value) => Generate();
    partial void OnExcludeAmbiguousChanged(bool value) => Generate();
}
