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

using System.ComponentModel;
using Avalonia.Controls;
using ForZip.GUI.ViewModels;

namespace ForZip.GUI.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Queda en <c>true</c> cuando el operador ya confirmó el cierre, para que el
    /// <c>Close()</c> que sigue a la confirmación no vuelva a preguntar.
    /// </summary>
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && e.NewValue is WindowState state and not WindowState.Minimized)
            {
                StateBeforeMinimize = state;
            }
        };
    }

    /// <summary>
    /// Cómo estaba la ventana antes de minimizarla. Arranca maximizada: al traerla de vuelta (otra acción del menú
    /// contextual), se restaura así y no al tamaño normal, que achicaba una ventana que el operador tenía maximizada.
    /// </summary>
    public WindowState StateBeforeMinimize { get; private set; } = WindowState.Maximized;

    /// <summary>
    /// Nunca se cierra de una. Cerrar por error mientras se empaqueta evidencia deja el
    /// paquete a medio escribir, y cerrar por error con el trabajo listo obliga a rehacerlo:
    /// en los dos casos la pregunta cuesta un clic y el error cuesta la sesión entera.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        // El cierre se cancela siempre en esta pasada: el diálogo es asíncrono y la ventana no
        // puede quedar esperándolo. Si el operador confirma, se vuelve a cerrar desde acá.
        e.Cancel = true;

        var running = (DataContext as MainWindowViewModel)?.IsOperationRunning ?? false;
        var localization = (DataContext as MainWindowViewModel)?.Localization;
        if (localization == null)
        {
            _closeConfirmed = true;
            Close();
            return;
        }

        var dialog = new CloseConfirmView(localization, running);
        var confirmed = await dialog.ShowDialog<bool>(this);

        if (confirmed)
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
