using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.IO;
using Avalonia.Controls;
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
            Title = "Exportar Bitácora",
            DefaultExtension = "txt",
            SuggestedFileName = $"ForZip_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Archivo de texto") { Patterns = new[] { "*.txt" } }
            }
        });

        if (picked != null)
        {
            var path = picked.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                var sb = new StringBuilder();
                foreach (var entry in vm.Entries)
                {
                    sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Level}: {entry.Message}");
                }
                await File.WriteAllTextAsync(path, sb.ToString());
            }
        }
    }
}
