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

/// <summary>Ventana autónoma "Extraer" del menú contextual (ver <see cref="ShellExtractViewModel"/>).</summary>
public partial class ShellExtractWindow : Window
{
    private bool _closeConfirmed;

    public ShellExtractWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Con una extracción en curso se pregunta, como en la ventana completa: cortarla deja el destino a medio
    /// escribir. Sin nada en curso se cierra de una: el resultado ya quedó en el disco y en la bitácora.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not ShellExtractViewModel { IsOperationRunning: true } vm)
        {
            return;
        }

        e.Cancel = true;
        var confirmed = await new CloseConfirmView(vm.Localization, operationRunning: true).ShowDialog<bool>(this);
        if (confirmed)
        {
            vm.CancelAll();
            _closeConfirmed = true;
            Close();
        }
    }

    private async void OnBrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ShellExtractItem item)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = item.LabelDestination,
            AllowMultiple = false
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            item.Unzip.OutputDirectory = path;
        }
    }

    /// <summary>Enter en la contraseña extrae, como en la pantalla Extraer.</summary>
    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as Control)?.DataContext is ShellExtractItem item
            && DataContext is ShellExtractViewModel vm)
        {
            e.Handled = true;
            vm.StartCommand.Execute(item);
        }
    }
}
