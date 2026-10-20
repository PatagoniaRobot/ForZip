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
    // También un sobre .fzs, que se abre con su contraseña en vez de extraerse.
    private static bool IsAcceptedArchive(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (OpenableExtensions.Any(ext => path!.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
         || SplitArchive.IsVolumePath(path!)
         || UnzipViewModel.IsSealedPath(path)
         || UnzipViewModel.IsOldStyleRarVolume(path!)
         || IsSelfExtractingArchive(path!));

    private static string? _sniffedPath;
    private static bool _sniffedResult;

    /// <summary>
    /// Un .exe se acepta solo si adentro hay un archivo 7z o RAR (autoextraíble): se mira el contenido, nunca se ejecuta.
    /// DragOver llega muchas veces por segundo; el resultado se guarda para la última ruta.
    /// </summary>
    private static bool IsSelfExtractingArchive(string path)
    {
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(path, _sniffedPath, StringComparison.OrdinalIgnoreCase))
        {
            _sniffedResult = UnzipViewModel.IsSelfExtractingArchive(path);
            _sniffedPath = path;
        }

        return _sniffedResult;
    }

    /// <summary>Extensiones que ofrece el selector y acepta al soltar (al abrir manda la firma).</summary>
    private static readonly string[] OpenableExtensions =
        [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".xz", ".txz", ".iso"];

    public UnzipView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // Enter en la contraseña extrae (o abre el sobre), como el botón.
    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is UnzipViewModel vm && !vm.IsProcessing
            && vm.DecompressCommand.CanExecute(null))
        {
            e.Handled = true;
            vm.DecompressCommand.Execute(null);
        }
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
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ZIP / 7z / RAR / TAR / GZ / BZ2 / XZ / ISO / .001 / .r00 / SFX .exe / .fzs")
                {
                    Patterns = OpenableExtensions.Select(e => "*" + e)
                        .Concat(new[] { "*.001", "*.r??", "*.exe", "*.fzs" }).ToArray()
                },
                new FilePickerFileType(vm.LabelPickerAll)
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
