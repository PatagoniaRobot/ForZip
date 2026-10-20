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

using System.Collections.Specialized;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ForZip.Core.Interfaces;
using ForZip.GUI.ViewModels;

namespace ForZip.GUI.Views;

/// <summary>
/// La bitácora en una ventana aparte, como texto de solo lectura: se puede seleccionar cualquier parte y
/// copiarla. Sigue a la bitácora en vivo; si el operador tiene algo seleccionado, la selección se conserva.
/// </summary>
public partial class LogWindow : Window
{
    private LogConsoleViewModel? _vm;

    public LogWindow()
    {
        InitializeComponent();
    }

    public LogWindow(LogConsoleViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        Rebuild();
        vm.Entries.CollectionChanged += OnEntriesChanged;
        Closed += (_, _) => vm.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            var sb = new StringBuilder();
            foreach (LogEntry entry in e.NewItems)
            {
                sb.Append(LogConsoleViewModel.FormatLine(entry)).Append(Environment.NewLine);
            }

            Append(sb.ToString());
        }
        else
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (_vm == null)
        {
            return;
        }

        LogText.Text = LogConsoleViewModel.FormatAll(_vm.Entries);
        LogText.CaretIndex = LogText.Text?.Length ?? 0;
    }

    private void Append(string text)
    {
        // Lo nuevo va al final: los índices de una selección en curso no cambian y se restauran tal cual.
        int start = LogText.SelectionStart, end = LogText.SelectionEnd;
        var selecting = start != end;
        LogText.Text = (LogText.Text ?? string.Empty) + text;
        if (selecting)
        {
            LogText.SelectionStart = start;
            LogText.SelectionEnd = end;
        }
        else
        {
            LogText.CaretIndex = LogText.Text.Length;
        }
    }

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null && _vm != null)
        {
            await Clipboard.SetTextAsync(LogConsoleViewModel.FormatAll(_vm.Entries));
        }
    }
}
