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
    private int _length = PasswordOptions.DefaultLength;

    [ObservableProperty]
    private bool _includeUppercase = true;

    [ObservableProperty]
    private bool _includeLowercase = true;

    [ObservableProperty]
    private bool _includeDigits = true;

    [ObservableProperty]
    private bool _includeSymbols = true;

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

    /// <summary>Cotas de longitud, expuestas para que el Slider y el campo numérico las tomen del modelo.</summary>
    public int MinLength => PasswordOptions.MinLength;

    public int MaxLength => PasswordOptions.MaxLength;

    /// <summary>Caracteres ambiguos excluidos siempre, separados para que se lean de a uno.</summary>
    public string ExcludedCharsDisplay => string.Join(' ', PasswordOptions.AmbiguousChars.ToCharArray());

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
            IncludeSymbols = IncludeSymbols
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
    partial void OnLengthChanged(int value)
    {
        // El campo numérico admite tipeo libre: una longitud fuera de rango se corrige aquí
        // en vez de dejar que el servicio lance. Reasignar dispara este mismo handler con el
        // valor ya válido, que es el que genera.
        var clamped = Math.Clamp(value, PasswordOptions.MinLength, PasswordOptions.MaxLength);
        if (clamped != value)
        {
            Length = clamped;
            return;
        }

        Generate();
    }

    partial void OnIncludeUppercaseChanged(bool value) => Generate();
    partial void OnIncludeLowercaseChanged(bool value) => Generate();
    partial void OnIncludeDigitsChanged(bool value) => Generate();
    partial void OnIncludeSymbolsChanged(bool value) => Generate();
}
