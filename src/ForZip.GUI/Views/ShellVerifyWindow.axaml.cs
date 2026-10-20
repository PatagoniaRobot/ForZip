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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ForZip.GUI.ViewModels;

namespace ForZip.GUI.Views;

/// <summary>Ventana autónoma "Verificar" del menú contextual (ver <see cref="ShellVerifyViewModel"/>).</summary>
public partial class ShellVerifyWindow : Window
{
    public ShellVerifyWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Enter en la contraseña verifica de nuevo, como en la pantalla Verificar.</summary>
    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as Control)?.DataContext is ShellVerifyItem item
            && DataContext is ShellVerifyViewModel vm)
        {
            e.Handled = true;
            vm.RetryCommand.Execute(item);
        }
    }

    /// <summary>"Guardar informe…" de una ficha: el mismo diálogo que la pantalla Verificar.</summary>
    private async void OnSaveReportClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ShellVerifyItem { Verify: var vm })
        {
            return;
        }

        var suggested = vm.SuggestedReportFileName;
        var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.PickerSaveReportTitle,
            SuggestedFileName = suggested,
            SuggestedStartLocation = string.IsNullOrEmpty(vm.ReportFolder)
                ? null
                : await StorageProvider.TryGetFolderFromPathAsync(vm.ReportFolder),
            DefaultExtension = "html",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(vm.PickerReportHtml) { Patterns = new[] { "*.html" } },
                new FilePickerFileType(vm.PickerReportTxt) { Patterns = new[] { "*.txt" } }
            }
        });

        var path = picked?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await vm.SaveReportAsync(path, suggested);
        }
    }
}
