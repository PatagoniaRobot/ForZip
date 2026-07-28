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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.GUI.ViewModels;

/// <summary>
/// Modo "hash rápido": la ventanita que abre el menú contextual del Explorador para mostrar
/// nombre, tamaño, fecha y hashes de los archivos seleccionados, sin levantar la aplicación
/// completa. El resultado se puede copiar al portapapeles o exportar como informe .txt con
/// el mismo formato que el resto de ForZip.
/// </summary>
public partial class QuickHashViewModel : ViewModelBase
{
    private readonly IHashService _hashService;
    private readonly IReportService _reportService;
    private readonly IConfigService _configService;

    /// <summary>Evita dos bombas de cálculo simultáneas (ver <see cref="PumpAsync"/>).</summary>
    private bool _pumping;

    [ObservableProperty]
    private bool _useMd5;

    [ObservableProperty]
    private bool _useSha1;

    [ObservableProperty]
    private bool _useSha256 = true;

    [ObservableProperty]
    private bool _useSha512;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public QuickHashViewModel(
        IHashService hashService,
        IReportService reportService,
        IConfigService configService,
        ILocalizationService localization) : base(localization)
    {
        _hashService = hashService;
        _reportService = reportService;
        _configService = configService;

        var config = _configService.Load();
        UseMd5 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.MD5);
        UseSha1 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA1);
        UseSha256 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA256);
        UseSha512 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA512);
    }

    public ObservableCollection<QuickHashItem> Items { get; } = new();

    public string Heading => _localization.Get("quick_hash_heading");
    public string LabelAlgorithms => _localization.Get("hash_algorithms");
    public string LabelPath => _localization.Get("quick_hash_path");
    public string LabelSize => _localization.Get("quick_hash_size");
    public string LabelModified => _localization.Get("quick_hash_modified");
    public string ButtonCopy => _localization.Get("quick_hash_copy");
    public string ButtonExport => _localization.Get("quick_hash_export");
    public string ButtonClose => _localization.Get("quick_hash_close");

    /// <summary>Resumen "N archivo(s) · tamaño total" del pie de la ventana.</summary>
    public string Summary => string.Format(
        CultureInfo.InvariantCulture,
        _localization.Get("quick_hash_summary"),
        Items.Count,
        FormatBytes(Items.Sum(i => i.Size)));

    public bool HasResults => Items.Count > 0;

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(LabelAlgorithms));
        OnPropertyChanged(nameof(LabelPath));
        OnPropertyChanged(nameof(LabelSize));
        OnPropertyChanged(nameof(LabelModified));
        OnPropertyChanged(nameof(ButtonCopy));
        OnPropertyChanged(nameof(ButtonExport));
        OnPropertyChanged(nameof(ButtonClose));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>
    /// Agrega archivos y arranca el cálculo. Se llama tanto al abrir la ventana como cada vez
    /// que otra invocación del menú contextual reenvía sus rutas a esta instancia (multi-selección:
    /// el Explorador lanza un proceso por archivo).
    /// </summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var full = Path.GetFullPath(path);
            if (Items.Any(i => string.Equals(i.FullPath, full, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var info = new FileInfo(full);
            Items.Add(new QuickHashItem
            {
                FileName = info.Name,
                FullPath = info.FullName,
                Size = info.Length,
                SizeText = FormatBytes(info.Length),
                ModifiedUtc = info.LastWriteTimeUtc,
                ModifiedText = info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                StatusText = _localization.Get("ready")
            });
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasResults));
        _ = PumpAsync();
    }

    /// <summary>
    /// Calcula, de a un archivo por vez, todo lo que falte según los algoritmos seleccionados.
    /// El bucle vuelve a barrer la lista después de cada archivo, así que los que llegan mientras
    /// se está calculando (multi-selección) y los algoritmos que se marcan a mitad de camino
    /// entran solos, sin necesidad de cancelar y reiniciar.
    /// </summary>
    private async Task PumpAsync()
    {
        if (_pumping)
        {
            return;
        }

        _pumping = true;
        IsProcessing = true;

        try
        {
            while (true)
            {
                var algorithms = BuildSelectedAlgorithms();
                if (algorithms.Count == 0)
                {
                    StatusMessage = _localization.Get("error_no_algorithms");
                    break;
                }

                var pending = Items.FirstOrDefault(i => !i.HasError && IsMissingAny(i, algorithms));
                if (pending == null)
                {
                    StatusMessage = _localization.Get("hash_success");
                    ProgressPercent = 100;
                    break;
                }

                StatusMessage = _localization.Get("processing");
                pending.StatusText = _localization.Get("processing");

                try
                {
                    var result = await _hashService.ComputeHashesAsync(
                        pending.FullPath, algorithms, null, CancellationToken.None);

                    foreach (var (algorithm, hash) in result.Hashes)
                    {
                        pending.Hashes[algorithm] = hash;
                    }
                    pending.StatusText = string.Empty;
                }
                catch (Exception ex)
                {
                    // Un archivo bloqueado o borrado no puede tumbar el lote: se marca y se sigue.
                    pending.HasError = true;
                    pending.StatusText = string.Format(
                        CultureInfo.InvariantCulture,
                        _localization.Get("quick_hash_unreadable"),
                        ex.Message);
                }

                RefreshLines(pending);

                var done = Items.Count(i => i.HasError || !IsMissingAny(i, algorithms));
                ProgressPercent = Items.Count == 0 ? 0 : 100.0 * done / Items.Count;
            }
        }
        finally
        {
            _pumping = false;
            IsProcessing = false;
        }
    }

    /// <summary>Reconstruye las líneas visibles de un archivo según los algoritmos marcados.</summary>
    private void RefreshLines(QuickHashItem item)
    {
        var algorithms = BuildSelectedAlgorithms();
        item.Lines.Clear();

        foreach (var algorithm in HashAlgorithmNames.DisplayOrder)
        {
            if (!algorithms.Contains(algorithm) || !item.Hashes.TryGetValue(algorithm, out var hash))
            {
                continue;
            }

            item.Lines.Add(new QuickHashLine
            {
                Algorithm = HashAlgorithmNames.Display(algorithm),
                Value = hash
            });
        }
    }

    private void RefreshAllLines()
    {
        foreach (var item in Items)
        {
            RefreshLines(item);
        }
    }

    /// <summary>Al (des)marcar un algoritmo se re-proyectan las líneas y se calcula lo que falte.</summary>
    private void OnAlgorithmToggled()
    {
        RefreshAllLines();
        _ = PumpAsync();
    }

    partial void OnUseMd5Changed(bool value) => OnAlgorithmToggled();
    partial void OnUseSha1Changed(bool value) => OnAlgorithmToggled();
    partial void OnUseSha256Changed(bool value) => OnAlgorithmToggled();
    partial void OnUseSha512Changed(bool value) => OnAlgorithmToggled();

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Items.Count == 0)
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var clipboard = desktop.MainWindow?.Clipboard;
        if (clipboard == null)
        {
            return;
        }

        await clipboard.SetTextAsync(BuildPlainText());
        StatusMessage = _localization.Get("copied");
    }

    /// <summary>
    /// Exporta el informe forense .txt en el mismo formato que el resto de ForZip. Toma los
    /// datos del operador de la configuración guardada, sin diálogo de confirmación: este es
    /// el camino rápido y el operador ya quedó definido en Ajustes.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync(string outputPath)
    {
        try
        {
            var config = _configService.Load();
            var algorithms = BuildSelectedAlgorithms();

            var data = new ReportData
            {
                Operator = config.Operator,
                Operation = OperationType.HashBatch,
                Algorithms = algorithms,
                FileResults = Items.Select(i => new HashResult
                {
                    FilePath = i.FileName,
                    SourcePath = i.FullPath,
                    FileSize = i.Size,
                    ModifiedUtc = i.ModifiedUtc,
                    Hashes = algorithms
                        .Where(a => i.Hashes.ContainsKey(a))
                        .ToDictionary(a => a, a => i.Hashes[a])
                }).ToList()
            };

            var content = _reportService.GenerateReport(data, _localization.CurrentLanguage);
            await _reportService.SaveReportAsync(content, outputPath);

            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("quick_hash_exported"),
                Path.GetFileName(outputPath));
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("quick_hash_export_error"),
                ex.Message);
        }
    }

    /// <summary>Volcado en texto plano para el portapapeles, con el mismo detalle que la ventana.</summary>
    public string BuildPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ForZip {Core.AppInfo.DisplayVersion} — {Heading}");
        sb.AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        sb.AppendLine();

        foreach (var item in Items)
        {
            sb.AppendLine(item.FileName);
            sb.AppendLine($"  {LabelPath,-10}: {item.FullPath}");
            sb.AppendLine($"  {LabelSize,-10}: {item.SizeText}");
            sb.AppendLine($"  {LabelModified,-10}: {item.ModifiedText}");

            if (item.HasError)
            {
                sb.AppendLine($"  {item.StatusText}");
            }
            else
            {
                foreach (var line in item.Lines)
                {
                    sb.AppendLine($"  {line.Algorithm,-10}: {line.Value}");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine(Summary);
        return sb.ToString();
    }

    private static bool IsMissingAny(QuickHashItem item, HashSet<HashAlgorithmType> algorithms)
        => algorithms.Any(a => !item.Hashes.ContainsKey(a));

    private HashSet<HashAlgorithmType> BuildSelectedAlgorithms()
    {
        var set = new HashSet<HashAlgorithmType>();
        if (UseMd5) set.Add(HashAlgorithmType.MD5);
        if (UseSha1) set.Add(HashAlgorithmType.SHA1);
        if (UseSha256) set.Add(HashAlgorithmType.SHA256);
        if (UseSha512) set.Add(HashAlgorithmType.SHA512);
        return set;
    }

    private string FormatBytes(long bytes)
    {
        var culture = _localization.CurrentLanguage == "es"
            ? CultureInfo.GetCultureInfo("es-AR")
            : CultureInfo.GetCultureInfo("en-US");

        var bytesPart = $"{bytes.ToString("N0", culture)} B";
        if (bytes < 1024L * 1024)
        {
            return bytesPart;
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytesPart} ({(bytes / 1048576.0).ToString("N2", culture)} MB)";
        }

        return $"{bytesPart} ({(bytes / (1024.0 * 1024 * 1024)).ToString("N2", culture)} GB)";
    }
}
