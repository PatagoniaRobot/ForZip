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

namespace ForZip.GUI.Views;

/// <summary>
/// Confirmación previa a actualizar. Deja explícito que ForZip se va a cerrar y volver a abrir,
/// para que nadie pierda trabajo en curso por un clic distraído.
/// </summary>
public partial class UpdateConfirmView : Window
{
    public UpdateConfirmView()
    {
        InitializeComponent();
    }

    public void SetTexts(string title, string message, string steps, string accept, string cancel)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        StepsText.Text = steps;
        AcceptButton.Content = accept;
        CancelButton.Content = cancel;
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
