using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using ForZip.Core.Interfaces;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ForZip.GUI.ViewModels;

namespace ForZip.GUI.Views;

public partial class LogConsoleView : UserControl
{
    public LogConsoleView()
    {
        InitializeComponent();
        
        var logList = this.FindControl<ListBox>("LogList");
        if (logList != null)
        {
            ((INotifyCollectionChanged)logList.Items).CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    logList.ScrollIntoView(logList.Items.Count - 1);
                }
            };
        }
    }

    private async void OnExportLogClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not LogConsoleViewModel vm) return;

        var picked = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.ExportTitle,
            DefaultExtension = "txt",
            SuggestedFileName = $"ForZip_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("TXT") { Patterns = new[] { "*.txt" } }
            }
        });

        if (picked != null)
        {
            var path = picked.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                await File.WriteAllTextAsync(path, LogConsoleViewModel.FormatAll(vm.Entries));
            }
        }
    }

    private LogWindow? _window;

    /// <summary>Abre la bitácora en su ventana (una sola: si ya está abierta, la trae al frente).</summary>
    private void OnPopOutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LogConsoleViewModel vm)
        {
            return;
        }

        if (_window != null)
        {
            _window.Activate();
            return;
        }

        _window = new LogWindow(vm);
        _window.Closed += (_, _) => _window = null;
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            _window.Show(owner);
        }
        else
        {
            _window.Show();
        }
    }

    private async void OnCopySelectedClick(object? sender, RoutedEventArgs e) => await CopySelectedAsync();

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogConsoleViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(LogConsoleViewModel.FormatAll(vm.Entries));
        }
    }

    /// <summary>Ctrl+C sobre las filas elegidas (clic, Ctrl+clic, Mayús+clic) las copia en orden.</summary>
    private async void OnLogListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await CopySelectedAsync();
        }
    }

    private async Task CopySelectedAsync()
    {
        if (DataContext is not LogConsoleViewModel vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var selected = LogList.SelectedItems?.OfType<LogEntry>().ToHashSet() ?? new HashSet<LogEntry>();
        if (selected.Count == 0)
        {
            return;
        }

        // En el orden de la bitácora, no en el del clic.
        await clipboard.SetTextAsync(LogConsoleViewModel.FormatAll(vm.Entries.Where(selected.Contains)));
    }
}
