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
using ForZip.Core.Services;

namespace ForZip.GUI.ViewModels;

/// <summary>
/// Modo "hash rápido": la ventanita que abre el menú contextual del Explorador para mostrar
/// nombre, tamaño, fecha y hashes de los archivos seleccionados, sin levantar la aplicación
/// completa. El resultado se puede copiar al portapapeles o exportar como informe .txt con
/// el mismo formato que el resto de ForZip.
///
/// El cálculo no arranca solo: la ventana se muestra con los archivos listados y el operador
/// elige qué algoritmos quiere antes de presionar Calcular, igual que 7-Zip. Leer un archivo
/// de decenas de GB es una decisión, no un efecto secundario de abrir un menú contextual.
/// </summary>
public partial class QuickHashViewModel : ViewModelBase
{
    private readonly IHashService _hashService;
    private readonly IReportService _reportService;
    private readonly IConfigService _configService;

    /// <summary>Enlaces encontrados dentro de una carpeta: no se siguen y van al informe como omitidos.</summary>
    private readonly List<OmittedFile> _skippedLinks = new();

    /// <summary>Evita dos bombas de cálculo simultáneas (ver <see cref="PumpAsync"/>).</summary>
    private bool _pumping;

    /// <summary>Vive solo mientras hay un cálculo en curso; lo dispara el botón Cancelar.</summary>
    private CancellationTokenSource? _cts;

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

    /// <summary>Detalle bajo la barra: qué archivo se está leyendo y en qué posición del lote.</summary>
    [ObservableProperty]
    private string _progressDetail = string.Empty;

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

        StatusMessage = _localization.Get("quick_hash_choose");
    }

    public ObservableCollection<QuickHashItem> Items { get; } = new();

    /// <summary>Carpetas que llegaron enteras (el menú contextual sobre una carpeta): tienen huella de carpeta.</summary>
    private readonly List<string> _folders = new();

    public ObservableCollection<FolderDigestItem> FolderDigests { get; } = new();

    /// <summary>Hay carpetas pero no está marcado SHA-256: la huella de carpeta no se puede calcular.</summary>
    [ObservableProperty]
    private string _folderNotice = string.Empty;

    /// <summary>
    /// Huella de cada carpeta, cuando todos sus archivos tienen SHA-256 (o no se pudieron leer: entonces dice que no
    /// hay huella). Mientras falte calcular algo, no se muestra una huella a medias.
    /// </summary>
    private void UpdateFolderDigests()
    {
        FolderDigests.Clear();
        FolderNotice = string.Empty;
        if (_folders.Count == 0)
        {
            return;
        }

        if (!UseSha256)
        {
            FolderNotice = _localization.Get("hash_folder_need_sha256");
            return;
        }

        if (Items.Any(i => !i.HasError && !i.Hashes.ContainsKey(HashAlgorithmType.SHA256)))
        {
            return;
        }

        var files = Items.Select(i => (i.FullPath, i.Size,
            i.HasError ? null : (string?)i.Hashes[HashAlgorithmType.SHA256])).ToList();
        var omitted = Items.Where(i => i.Omission != null).Select(i => i.Omission!).Concat(_skippedLinks);
        foreach (var digest in FolderHash.Compute(_folders, files, omitted))
        {
            FolderDigests.Add(new FolderDigestItem(digest, _localization, FormatBytes));
        }
    }

    public string Heading => _localization.Get("quick_hash_heading");
    public string LabelAlgorithms => _localization.Get("hash_algorithms");
    public string LabelPath => _localization.Get("quick_hash_path");
    public string LabelSize => _localization.Get("quick_hash_size");
    public string LabelModified => _localization.Get("quick_hash_modified");
    public string ButtonCopy => _localization.Get("quick_hash_copy");
    public string ButtonExport => _localization.Get("quick_hash_export");
    public string ButtonClose => _localization.Get("quick_hash_close");
    public string ButtonCalculate => _localization.Get("quick_hash_calculate");
    public string ButtonCancel => _localization.Get("cancel");

    /// <summary>Resumen "N archivo(s) · tamaño total" del pie de la ventana.</summary>
    public string Summary => string.Format(
        CultureInfo.InvariantCulture,
        _localization.Get("quick_hash_summary"),
        Items.Count,
        FormatBytes(Items.Sum(i => i.Size)));

    /// <summary>Hay al menos un hash calculado, es decir algo que copiar o exportar.</summary>
    public bool HasResults => Items.Any(i => i.Hashes.Count > 0);

    /// <summary>Los algoritmos solo se pueden tocar entre cálculos, no a mitad de uno.</summary>
    public bool CanEditAlgorithms => !IsProcessing;

    /// <summary>Queda algo por calcular con la selección actual de algoritmos.</summary>
    public bool CanCalculate
    {
        get
        {
            if (IsProcessing)
            {
                return false;
            }

            var algorithms = BuildSelectedAlgorithms();
            return algorithms.Count > 0
                && Items.Any(i => !i.HasError && IsMissingAny(i, algorithms));
        }
    }

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
        OnPropertyChanged(nameof(ButtonCalculate));
        OnPropertyChanged(nameof(ButtonCancel));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>
    /// Agrega archivos a la lista. Se llama tanto al abrir la ventana como cada vez que otra
    /// invocación del menú contextual reenvía sus rutas a esta instancia (multi-selección: el
    /// Explorador lanza un proceso por archivo). No dispara el cálculo: los archivos quedan
    /// pendientes hasta que el operador presiona Calcular. Si ya hay un lote en curso, en
    /// cambio, los recién llegados se suman solos.
    /// </summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        // Una carpeta (el menú contextual la ofrece) se recorre entera, como en Hash Batch: cada archivo con su ruta
        // relativa, y los enlaces quedan asentados en el informe como no seguidos. Antes la carpeta se ignoraba.
        List<HashInventory.Item> expanded;
        try
        {
            var list = paths.ToList();
            foreach (var folder in list.Where(Directory.Exists).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))))
            {
                if (!_folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    _folders.Add(folder);
                }
            }

            expanded = HashInventory.Expand(list, _skippedLinks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            expanded = new List<HashInventory.Item>();
        }

        foreach (var entry in expanded)
        {
            var full = Path.GetFullPath(entry.FullPath);
            if (Items.Any(i => string.Equals(i.FullPath, full, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var info = new FileInfo(full);
            Items.Add(new QuickHashItem
            {
                FileName = entry.DisplayName,
                FullPath = info.FullName,
                Size = info.Length,
                SizeText = FormatBytes(info.Length),
                ModifiedUtc = info.LastWriteTimeUtc,
                ModifiedText = info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                StatusText = _localization.Get("quick_hash_pending")
            });
        }

        NotifyListChanged();
    }

    /// <summary>Arranca el cálculo de lo que falte. Es la única puerta de entrada.</summary>
    [RelayCommand(CanExecute = nameof(CanCalculate))]
    private Task CalculateAsync() => PumpAsync();

    /// <summary>Corta el lote en curso. Lo ya calculado se conserva.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>
    /// Calcula, de a un archivo por vez, todo lo que falte según los algoritmos seleccionados.
    /// El bucle vuelve a barrer la lista después de cada archivo, así que los que llegan mientras
    /// se está calculando (multi-selección) entran solos, sin necesidad de reiniciar el lote.
    /// </summary>
    private async Task PumpAsync()
    {
        if (_pumping)
        {
            return;
        }

        if (BuildSelectedAlgorithms().Count == 0)
        {
            StatusMessage = _localization.Get("error_no_algorithms");
            return;
        }

        _pumping = true;
        _cts = new CancellationTokenSource();
        IsProcessing = true;
        ProgressPercent = 0;

        try
        {
            while (true)
            {
                var algorithms = BuildSelectedAlgorithms();

                var pending = Items.FirstOrDefault(i => !i.HasError && IsMissingAny(i, algorithms));
                if (pending == null)
                {
                    StatusMessage = _localization.Get("hash_success");
                    ProgressDetail = string.Empty;
                    ProgressPercent = 100;
                    break;
                }

                // Foto del avance antes de este archivo: la barra global se mueve dentro del
                // tramo que le corresponde, a medida que se leen sus bytes.
                var total = Items.Count;
                var completed = Items.Count(i => i.HasError || !IsMissingAny(i, algorithms));

                StatusMessage = _localization.Get("processing");
                ProgressDetail = string.Format(
                    CultureInfo.InvariantCulture,
                    _localization.Get("quick_hash_progress"),
                    pending.FileName,
                    completed + 1,
                    total);

                pending.IsProcessing = true;
                pending.ProgressPercent = 0;
                pending.StatusText = string.Format(
                    CultureInfo.InvariantCulture,
                    _localization.Get("quick_hash_item_progress"),
                    0);

                // Progress<T> se construye en el hilo de UI, así que sus avisos vuelven solos
                // al Dispatcher aunque el hash corra en el pool de hilos.
                var item = pending;
                var itemCompleted = completed;
                var itemTotal = total;
                var progress = new Progress<double>(fraction =>
                {
                    var percent = Math.Clamp(fraction * 100, 0, 100);
                    item.ProgressPercent = percent;
                    item.StatusText = string.Format(
                        CultureInfo.InvariantCulture,
                        _localization.Get("quick_hash_item_progress"),
                        (int)percent);

                    ProgressPercent = itemTotal == 0
                        ? 0
                        : 100.0 * (itemCompleted + fraction) / itemTotal;
                });

                try
                {
                    var result = await _hashService.ComputeHashesAsync(
                        pending.FullPath, algorithms, progress, _cts.Token);

                    foreach (var (algorithm, hash) in result.Hashes)
                    {
                        pending.Hashes[algorithm] = hash;
                    }
                    pending.StatusText = string.Empty;
                }
                catch (OperationCanceledException)
                {
                    // Cancelación pedida por el operador: el archivo vuelve a quedar pendiente.
                    pending.IsProcessing = false;
                    pending.ProgressPercent = 0;
                    pending.StatusText = _localization.Get("quick_hash_pending");
                    StatusMessage = _localization.Get("quick_hash_cancelled");
                    ProgressDetail = string.Empty;
                    break;
                }
                catch (Exception ex)
                {
                    // Un archivo bloqueado o borrado no puede tumbar el lote: se marca y se sigue.
                    pending.HasError = true;
                    pending.Omission = HashInventory.Unreadable(pending.FullPath, pending.FileName, ex);
                    pending.StatusText = string.Empty;
                    pending.ErrorText = string.Format(
                        CultureInfo.InvariantCulture,
                        _localization.Get("quick_hash_unreadable"),
                        ex.Message);
                }

                pending.IsProcessing = false;
                pending.ProgressPercent = 0;
                RefreshLines(pending);
                OnPropertyChanged(nameof(HasResults));

                var done = Items.Count(i => i.HasError || !IsMissingAny(i, algorithms));
                ProgressPercent = Items.Count == 0 ? 0 : 100.0 * done / Items.Count;
            }
        }
        finally
        {
            _pumping = false;
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
            NotifyListChanged();
            UpdateFolderDigests();
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

    /// <summary>
    /// Al (des)marcar un algoritmo se re-proyectan las líneas ya calculadas —desmarcar y volver
    /// a marcar no relee el disco— y se reevalúa si queda algo pendiente. Nunca se dispara el
    /// cálculo solo: eso lo decide el operador.
    /// </summary>
    private void OnAlgorithmToggled()
    {
        RefreshAllLines();
        NotifyListChanged();
        if (!IsProcessing)
        {
            UpdateFolderDigests();
        }

        if (!IsProcessing)
        {
            StatusMessage = BuildSelectedAlgorithms().Count == 0
                ? _localization.Get("error_no_algorithms")
                : _localization.Get("quick_hash_choose");
        }
    }

    partial void OnUseMd5Changed(bool value) => OnAlgorithmToggled();
    partial void OnUseSha1Changed(bool value) => OnAlgorithmToggled();
    partial void OnUseSha256Changed(bool value) => OnAlgorithmToggled();
    partial void OnUseSha512Changed(bool value) => OnAlgorithmToggled();

    partial void OnIsProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditAlgorithms));
        OnPropertyChanged(nameof(CanCalculate));
        CalculateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Refresca lo que depende de la lista y del estado del lote.</summary>
    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CanCalculate));
        CalculateCommand.NotifyCanExecuteChanged();
    }

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
                // Lo que no se pudo leer va como no leído, con el código de Windows; antes figuraba como una fila
                // sin hash, sin decir que había fallado.
                FileResults = Items.Where(i => !i.HasError).Select(i => new HashResult
                {
                    FilePath = i.FileName,
                    SourcePath = i.FullPath,
                    FileSize = i.Size,
                    ModifiedUtc = i.ModifiedUtc,
                    Hashes = algorithms
                        .Where(a => i.Hashes.ContainsKey(a))
                        .ToDictionary(a => a, a => i.Hashes[a])
                }).ToList(),
                OmittedFiles = Items.Where(i => i.Omission != null).Select(i => i.Omission!).Concat(_skippedLinks).ToList(),
                FolderDigests = FolderDigests.Select(f => f.Digest).ToList()
            };

            var content = _reportService.GenerateReport(data, _localization.CurrentLanguage);
            await _reportService.SaveReportAsync(content, outputPath);

            // Como Hash Batch y la CLI: el informe sale con su .sha256, que es lo que permite verificarlo después.
            var reportHash = await _hashService.ComputeHashesAsync(
                outputPath, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None);
            await File.WriteAllTextAsync(outputPath + ".sha256",
                $"{reportHash.Hashes[HashAlgorithmType.SHA256]}  {Path.GetFileName(outputPath)}");

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

        foreach (var folder in FolderDigests)
        {
            sb.AppendLine($"{_localization.Get("hash_folder_heading")}: {folder.ToPlainText()}");
            sb.AppendLine();
        }

        foreach (var item in Items)
        {
            sb.AppendLine(item.FileName);
            sb.AppendLine($"  {LabelPath,-10}: {item.FullPath}");
            sb.AppendLine($"  {LabelSize,-10}: {item.SizeText}");
            sb.AppendLine($"  {LabelModified,-10}: {item.ModifiedText}");

            if (item.HasError)
            {
                sb.AppendLine($"  {item.ErrorText}");
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
