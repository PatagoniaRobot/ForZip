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

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;

namespace ForZip.GUI.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    public AboutViewModel(ILocalizationService localization)
    {
        _localization = localization;
    }

    public string AppName => "ForZip";
    public string Version => "v0.9.0 Beta";
    public string Subtitle => "Forensic ZIP Tool — Open Source";
    public string Author => "Claudio Andino";
    public string Email => "claudio@patagoniarobot.com";
    public string LicenseName => "Apache License 2.0";
    public string Initiative => "Patagonia Robot";
    public string DisclaimerText => _localization.Get("report_disclaimer_text");

    [RelayCommand]
    private void OpenHelp()
    {
        var helpPath = Path.Combine(AppContext.BaseDirectory, "Help_ForZip.html");
        if (File.Exists(helpPath))
        {
            OpenWithDefaultApp(helpPath);
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        OpenWithDefaultApp("https://github.com/patagoniarobot/forzip");
    }

    private static void OpenWithDefaultApp(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Si el sistema no puede abrir el recurso, ignoramos silenciosamente: no es crítico
        }
    }
}
