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

public partial class VerifyReportView : UserControl
{
    public VerifyReportView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // Acepta tanto informes (.txt) como manifiestos forenses (.json) para verificación de evidencia
    private static bool IsSupported(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (path!.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var first = e.Data.GetFiles()?.FirstOrDefault();
        var path = first?.TryGetLocalPath();
        e.DragEffects = IsSupported(path) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not VerifyReportViewModel vm)
        {
            return;
        }

        var first = e.Data.GetFiles()?.FirstOrDefault();
        var path = first?.TryGetLocalPath();
        if (IsSupported(path))
        {
            vm.ReportFilePath = path!;
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not VerifyReportViewModel vm)
        {
            return;
        }

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Seleccionar informe o manifiesto forense",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Informe o manifiesto ForZip")
                {
                    Patterns = new[] { "*.txt", "*.json" }
                }
            }
        });

        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.ReportFilePath = path;
        }
    }
}
