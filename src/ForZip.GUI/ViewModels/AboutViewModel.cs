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
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.GUI.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly IUpdateService _updateService;

    /// <summary>Página concreta de la publicación encontrada; si no hay, se usa el listado.</summary>
    private string? _releaseUrl;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    public AboutViewModel(ILocalizationService localization, IUpdateService updateService)
    {
        _localization = localization;
        _updateService = updateService;
    }

    public string AppName => "ForZip";
    public string Version => Core.AppInfo.DisplayVersion;
    public string Subtitle => "Forensic ZIP Tool — Open Source";
    public string Author => "Claudio Andino";
    public string Email => "claudio@patagoniarobot.com";
    public string LicenseName => "Apache License 2.0";
    public string Initiative => "Patagonia Robot";
    public string DisclaimerText => _localization.Get("report_disclaimer_text");

    public string LabelUpdates => _localization.Get("update_section");
    public string ButtonCheckUpdates => _localization.Get("update_check");
    public string ButtonOpenRelease => _localization.Get("update_open_release");
    public string LabelReleaseNotes => _localization.Get("update_notes");
    public string LabelManualNote => _localization.Get("update_manual_note");

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    /// <summary>
    /// Consulta si hay una versión más reciente. No descarga ni instala nada: si la hay, se
    /// informa y se ofrece abrir la publicación en el navegador para que el operador decida.
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken ct)
    {
        IsCheckingUpdates = true;
        UpdateStatusMessage = _localization.Get("update_checking");
        ReleaseNotes = string.Empty;
        OnPropertyChanged(nameof(HasReleaseNotes));

        try
        {
            var result = await _updateService.CheckAsync(ct);
            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            UpdateStatusMessage = string.Empty;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    /// <summary>Vuelca un resultado en la vista. Reutilizado por el chequeo automático al iniciar.</summary>
    public void ApplyResult(UpdateCheckResult result)
    {
        _releaseUrl = result.ReleaseUrl;
        IsUpdateAvailable = result.Status == UpdateCheckStatus.UpdateAvailable;

        UpdateStatusMessage = result.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_available"),
                result.LatestVersion),

            UpdateCheckStatus.UpToDate => _localization.Get("update_up_to_date"),

            _ => string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_failed"),
                result.ErrorMessage)
        };

        ReleaseNotes = IsUpdateAvailable ? result.ReleaseNotes ?? string.Empty : string.Empty;
        OnPropertyChanged(nameof(HasReleaseNotes));
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        OpenWithDefaultApp(_releaseUrl ?? _updateService.ReleasesPageUrl);
    }

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
