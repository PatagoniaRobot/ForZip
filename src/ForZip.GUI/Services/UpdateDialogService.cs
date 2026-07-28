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

using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ForZip.Core.Interfaces;
using ForZip.GUI.Views;

namespace ForZip.GUI.Services;

public class UpdateDialogService : IUpdateDialogService
{
    private readonly ILocalizationService _localization;

    public UpdateDialogService(ILocalizationService localization)
    {
        _localization = localization;
    }

    public async Task<bool> ConfirmUpdateAsync(string latestVersion, long downloadSizeBytes)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            return false;
        }

        var dialog = new UpdateConfirmView();
        dialog.SetTexts(
            title: string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_confirm_title"),
                latestVersion),
            message: string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_confirm_msg"),
                latestVersion,
                FormatSize(downloadSizeBytes)),
            steps: _localization.Get("update_confirm_steps"),
            accept: _localization.Get("update_confirm_accept"),
            cancel: _localization.Get("update_confirm_cancel"));

        return await dialog.ShowDialog<bool>(desktop.MainWindow);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        var mb = bytes / 1048576.0;
        return $"{mb.ToString("N1", CultureInfo.InvariantCulture)} MB";
    }
}
