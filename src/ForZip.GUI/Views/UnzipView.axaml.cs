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
using ForZip.Core.Services;
using ForZip.GUI.ViewModels;

namespace ForZip.GUI.Views;

public partial class UnzipView : UserControl
{
    // Acepta el ZIP lógico o cualquier volumen de un archivo dividido (.001, .002, …);
    // el servicio detecta el resto de segmentos automáticamente.
    private static bool IsAcceptedArchive(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (path!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || SplitArchive.IsVolumePath(path));

    public UnzipView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Aceptamos un ZIP o el primer/cualquier volumen de un archivo dividido
        var files = e.Data.GetFiles();
        var first = files?.FirstOrDefault();
        var path = first?.TryGetLocalPath();
        e.DragEffects = IsAcceptedArchive(path)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not UnzipViewModel vm)
        {
            return;
        }

        var first = e.Data.GetFiles()?.FirstOrDefault();
        var path = first?.TryGetLocalPath();
        if (IsAcceptedArchive(path))
        {
            vm.SetZipPath(path!);
        }
    }

    private async void OnBrowseZipClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not UnzipViewModel vm)
        {
            return;
        }

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Seleccionar archivo ZIP",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Archivo ZIP o volumen")
                {
                    Patterns = new[] { "*.zip", "*.001" }
                },
                new FilePickerFileType("Todos los archivos")
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.SetZipPath(path);
        }
    }

    private async void OnBrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not UnzipViewModel vm)
        {
            return;
        }

        var picked = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Seleccionar carpeta de destino"
        });

        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.OutputDirectory = path;
        }
    }
}
