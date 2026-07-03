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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;

namespace ForZip.GUI.ViewModels;

public partial class LogConsoleViewModel : ObservableObject
{
    private readonly ILogService _logService;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private bool _isExpanded = true;

    public LogConsoleViewModel(ILogService logService, ILocalizationService localization)
    {
        _logService = logService;
        _localization = localization;
        _localization.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(TooltipExport));
            OnPropertyChanged(nameof(TooltipClear));
        };
    }

    public string Title => _localization.Get("log_console_title");
    public string TooltipExport => _localization.Get("log_export_tooltip");
    public string TooltipClear => _localization.Get("log_clear_tooltip");
    public string ExportTitle => _localization.Get("log_export_title");

    public ObservableCollection<LogEntry> Entries => _logService.Entries;

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void Clear()
    {
        _logService.Clear();
    }
}
