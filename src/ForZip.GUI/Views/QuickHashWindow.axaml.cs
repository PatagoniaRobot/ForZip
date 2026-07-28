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
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ForZip.GUI.ViewModels;

namespace ForZip.GUI.Views;

/// <summary>
/// Ventana autónoma del modo "hash rápido" del menú contextual: muestra los hashes de los
/// archivos seleccionados sin abrir la aplicación completa (estilo 7-Zip).
/// </summary>
public partial class QuickHashWindow : Window
{
    public QuickHashWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not QuickHashViewModel vm)
        {
            return;
        }

        // Sugerir el nombre del primer archivo: con un solo archivo (el caso habitual del
        // menú contextual) el informe queda junto a su evidencia y con su mismo nombre.
        var first = vm.Items.FirstOrDefault();
        var suggested = first != null
            ? $"{Path.GetFileName(first.FullPath)}.hash.txt"
            : $"ForZip_Hash_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

        var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.ButtonExport,
            DefaultExtension = "txt",
            SuggestedFileName = suggested,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Informe ForZip")
                {
                    Patterns = new[] { "*.txt" }
                }
            }
        });

        var path = picked?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.ExportCommand.Execute(path);
        }
    }
}
