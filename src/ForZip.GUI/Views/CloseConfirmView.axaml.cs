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
using Avalonia.Media;
using ForZip.Core.Interfaces;

namespace ForZip.GUI.Views;

/// <summary>
/// Confirmación de cierre. Devuelve <c>true</c> vía <c>ShowDialog&lt;bool&gt;</c> si el
/// operador confirma que quiere cerrar.
/// </summary>
/// <remarks>
/// Tiene dos caras según haya o no una operación en curso. Con el programa quieto es una
/// pregunta; con una operación andando es una advertencia, porque cerrar ahí deja el paquete a
/// medio escribir y lo que queda en disco no sirve como material probatorio.
/// </remarks>
public partial class CloseConfirmView : Window
{
    public CloseConfirmView()
    {
        InitializeComponent();
    }

    public CloseConfirmView(ILocalizationService localization, bool operationRunning) : this()
    {
        var prefix = operationRunning ? "close_busy" : "close_confirm";

        TitleText.Text = localization.Get(prefix + "_title");
        MessageText.Text = localization.Get(prefix + "_msg");
        AcceptButton.Content = localization.Get(prefix + "_accept");
        CancelButton.Content = localization.Get(prefix + "_cancel");

        // Con una operación andando el título es una advertencia, no un encabezado más.
        TitleText.Foreground = operationRunning
            ? this.FindResource("WarningBrush") as IBrush
            : this.FindResource("AccentBrush") as IBrush;
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
