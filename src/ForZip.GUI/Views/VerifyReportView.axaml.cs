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

    // Se acepta cualquier archivo: sobres, recibos, manifiestos, informes y paquetes (también un volumen suelto,
    // que lleva a su manifiesto). Lo que no trae contra qué verificarse lo responde la verificación, en ámbar.
    // Antes solo se aceptaban .txt y .json, y un sobre solo se podía verificar desde el menú contextual.
    private static bool IsSupported(string? path) => !string.IsNullOrEmpty(path) && File.Exists(path);

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
            vm.ReportFilePath = VerifyReportViewModel.ResolveVerifyTarget(path!);
        }
    }

    // Enter en la contraseña verifica, como el botón.
    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is VerifyReportViewModel vm && vm.VerifyCommand.CanExecute(null))
        {
            e.Handled = true;
            vm.VerifyCommand.Execute(null);
        }
    }

    private async void OnSaveReportClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not VerifyReportViewModel vm)
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
            Title = vm.LabelPickerTitle,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(vm.LabelPickerForZip)
                {
                    Patterns = new[] { "*.fzs", "*.receipt.json", "*.manifest.json", "*.p7s", "*.txt", "*.zip", "*.7z", "*.0*" }
                },
                // Lo de terceros, sin manifiesto: se prueba contra sus propios controles. Un .exe se lee, no se ejecuta.
                new FilePickerFileType(vm.LabelPickerArchives)
                {
                    Patterns = new[] { "*.zip", "*.7z", "*.rar", "*.r00", "*.tar", "*.gz", "*.tgz", "*.bz2", "*.tbz2", "*.xz", "*.txz", "*.iso", "*.exe", "*.0*" }
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
            vm.ReportFilePath = VerifyReportViewModel.ResolveVerifyTarget(path);
        }
    }
}
