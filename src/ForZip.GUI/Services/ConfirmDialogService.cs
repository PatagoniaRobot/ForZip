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

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ForZip.GUI.Views;

namespace ForZip.GUI.Services;

/// <summary>Confirmación genérica que reutiliza la ventana del aviso de actualización.</summary>
public class ConfirmDialogService : IConfirmDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string details, string accept, string cancel)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            // Sin ventana a la cual anclar la pregunta no se asume un sí.
            return false;
        }

        var dialog = new UpdateConfirmView();
        dialog.SetTexts(title, message, details, accept, cancel);
        dialog.PreferCancel();
        return await dialog.ShowDialog<bool>(desktop.MainWindow);
    }
}
