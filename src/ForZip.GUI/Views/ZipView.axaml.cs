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

public partial class ZipView : UserControl
{
    public ZipView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ZipViewModel vm)
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        vm.AddPaths(paths);
    }

    private async void OnBrowseFilesClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not ZipViewModel vm)
        {
            return;
        }

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = vm.PickerFilesTitle
        });

        var paths = picked
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        vm.AddPaths(paths);
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not ZipViewModel vm)
        {
            return;
        }

        var picked = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = vm.PickerFoldersTitle
        });

        var paths = picked
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        vm.AddPaths(paths);
    }

    private async void OnBrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not ZipViewModel vm)
        {
            return;
        }

        var shown = vm.OutputPath;
        var picked = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.PickerSaveZipTitle,
            DefaultExtension = vm.EffectiveFormat == ForZip.Core.Models.ArchiveFormat.SevenZ ? "7z" : "zip",
            SuggestedFileName = System.IO.Path.GetFileName(shown),
            // ForZip nunca reemplaza un paquete: si el nombre choca en la carpeta elegida le busca el "(2)" o
            // frena al empaquetar. La pregunta de Windows "¿Desea reemplazarlo?" prometía lo contrario.
            ShowOverwritePrompt = false,
            FileTypeChoices = new[]
            {
                vm.EffectiveFormat == ForZip.Core.Models.ArchiveFormat.SevenZ
                    ? new FilePickerFileType("7z") { Patterns = new[] { "*.7z" } }
                    : new FilePickerFileType("ZIP") { Patterns = new[] { "*.zip" } }
            }
        });

        if (picked != null)
        {
            var path = picked.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                vm.SetPickedOutputPath(path, shown);
            }
        }
    }

    /// <summary>"Guardar informe…": el informe del empaquetado, donde elija el operador.</summary>
    private async void OnSaveReportClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not ZipViewModel vm)
        {
            return;
        }

        var suggested = vm.SuggestedReportFileName;
        var picked = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.PickerSaveReportTitle,
            SuggestedFileName = suggested,
            // Sin esto abría en la última carpeta que se usó en cualquier diálogo, no en la del paquete.
            SuggestedStartLocation = string.IsNullOrEmpty(vm.ReportFolder)
                ? null
                : await topLevel.StorageProvider.TryGetFolderFromPathAsync(vm.ReportFolder),
            // HTML primero: es el que abre con doble clic quien recibe el informe. El texto sigue disponible.
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

    /// <summary>
    /// Guarda el acta de entrega. Requiere que el operador elija la ruta a mano: el acta
    /// nunca se escribe sola junto al sobre porque contiene la contraseña en claro.
    /// </summary>
    private async void OnSaveDeliveryActClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not ZipViewModel vm)
        {
            return;
        }

        var picked = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.LabelSaveAct,
            SuggestedFileName = vm.SuggestedActFileName,
            DefaultExtension = "txt"
        });

        var path = picked?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await vm.SaveDeliveryActAsync(path);
        }
    }
}
