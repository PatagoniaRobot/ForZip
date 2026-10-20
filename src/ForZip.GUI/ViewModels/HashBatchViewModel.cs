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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;
using ForZip.GUI.Services;
using ForZip.Core.Models;
using ForZip.Core.Services;

namespace ForZip.GUI.ViewModels;

public partial class HashBatchViewModel : ViewModelBase
{
    private readonly IHashService _hashService;
    private readonly IReportService _reportService;
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly IOperatorDialogService _operatorDialog;

    [ObservableProperty]
    private bool _useMd5;

    [ObservableProperty]
    private bool _useSha1;

    [ObservableProperty]
    private bool _useSha256 = true;

    [ObservableProperty]
    private bool _useSha512;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompute))]
    private bool _isProcessing;

    /// <summary>Se está recorriendo una carpeta recién agregada (en segundo plano, para no congelar la ventana).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompute))]
    private bool _isLoadingSelection;

    public bool CanStartCompute => !IsProcessing && !IsLoadingSelection;

    /// <summary>Una carga a la vez, en el orden en que llegan.</summary>
    private readonly SemaphoreSlim _selectionGate = new(1, 1);

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressDetail = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _allHashesComputed;

    public HashBatchViewModel(
        IHashService hashService,
        IReportService reportService,
        ILocalizationService localization,
        IConfigService configService,
        ILogService logService,
        IOperatorDialogService operatorDialog) : base(localization)
    {
        _hashService = hashService;
        _reportService = reportService;
        _configService = configService;
        _logService = logService;
        _operatorDialog = operatorDialog;

        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LabelFilesInList));
        LoadDefaults();
    }

    public string Title => _localization.Get("hash_batch");
    public string LabelDropZone => _localization.Get("drag_drop_hash");
    public string LabelAlgorithms => _localization.Get("hash_algorithms");
    public string ButtonCalculate => _localization.Get("calculate");
    public string ButtonExport => _localization.Get("export_report");
    public string ButtonCancel => _localization.Get("cancel");
    public string ButtonClear => _localization.Get("clear_all");
    public string ButtonSelectFiles => _localization.Get("browse_files");
    public string ButtonSelectFolder => _localization.Get("browse_folder");
    public string LabelFilesInList => string.Format(_localization.Get("hash_files_in_list"), Rows.Count);
    public string HeaderFile => _localization.Get("hash_col_file");
    public string HeaderSize => _localization.Get("hash_col_size");
    public string HeaderHashes => _localization.Get("hash_col_hashes");
    public string PickerFilesTitle => _localization.Get("hash_picker_files_title");
    public string PickerFoldersTitle => _localization.Get("hash_picker_folders_title");

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LabelDropZone));
        OnPropertyChanged(nameof(LabelAlgorithms));
        OnPropertyChanged(nameof(ButtonCalculate));
        OnPropertyChanged(nameof(ButtonExport));
        OnPropertyChanged(nameof(ButtonCancel));
        OnPropertyChanged(nameof(ButtonClear));
        OnPropertyChanged(nameof(ButtonSelectFiles));
        OnPropertyChanged(nameof(ButtonSelectFolder));
        OnPropertyChanged(nameof(LabelFilesInList));
        OnPropertyChanged(nameof(HeaderFile));
        OnPropertyChanged(nameof(HeaderSize));
        OnPropertyChanged(nameof(HeaderHashes));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(LabelFolderHeading));
        OnPropertyChanged(nameof(LabelFolderHint));
        if (AllHashesComputed)
        {
            UpdateFolderDigests();
        }
    }

    private void LoadDefaults()
    {
        var config = _configService.Load();
        UseMd5 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.MD5);
        UseSha1 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA1);
        UseSha256 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA256);
        UseSha512 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA512);
    }

    public ObservableCollection<HashRowItem> Rows { get; } = new();

    /// <summary>Enlaces y junctions encontrados dentro de las carpetas agregadas: no se siguen, se informan.</summary>
    private readonly List<OmittedFile> _skippedLinks = new();

    /// <summary>Algoritmos con que se calculó el lote: el informe usa estos, no los que estén marcados al exportar.</summary>
    private HashSet<HashAlgorithmType> _computedAlgorithms = new();

    /// <summary>
    /// Agrega archivos y carpetas. Una carpeta aporta todo lo que tiene adentro, sin entrar en enlaces (como al
    /// empaquetar): antes las carpetas se ignoraban en silencio.
    /// </summary>
    public void AddPaths(IEnumerable<string> paths) => _ = AddPathsAsync(paths.ToList());

    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        await _selectionGate.WaitAsync();
        IsLoadingSelection = true;
        try
        {
            // El recorrido y el tamaño de cada archivo, fuera del hilo de la ventana: con una carpeta grande (o la
            // raíz de una unidad) la ventana quedaba "No responde".
            var fullRoots = paths.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).ToList();
            _addedRoots.AddRange(fullRoots);
            _partialRoots.ExceptWith(fullRoots);
            var skippedBefore = _skippedLinks.Count;
            var items = await Task.Run(() => HashInventory.Expand(paths, _skippedLinks)
                .Select(item => (Item: item, Info: new FileInfo(item.FullPath)))
                .Select(x => (x.Item, FullName: x.Info.FullName, Length: x.Info.Exists ? x.Info.Length : 0L))
                .ToList());

            // Una carpeta que Windows no dejó recorrer no corta la lista: queda en la bitácora y en el informe.
            foreach (var folder in _skippedLinks.Skip(skippedBefore).Where(o => o.Reason == OmissionReason.FolderUnreadable))
            {
                _logService.LogEvidence(LogLevel.Warning, _localization.Get("log_omitted_folder"),
                    $"{folder.SourcePath}: {folder.SystemMessage}");
            }

            foreach (var (item, fullName, length) in items)
            {
                if (Rows.Any(r => string.Equals(r.FullPath, fullName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Rows.Add(new HashRowItem
                {
                    Index = Rows.Count + 1,
                    FileName = item.DisplayName,
                    FullPath = fullName,
                    Size = length,
                    StatusText = _localization.Get("ready")
                });
            }

            AllHashesComputed = false;
        }
        catch (Exception ex)
        {
            // La carga corre suelta (AddPaths no espera): lo que falle se dice acá o se perdería en silencio.
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
        }
        finally
        {
            IsLoadingSelection = false;
            _selectionGate.Release();
        }
    }

    [RelayCommand]
    private void RemoveRow(HashRowItem item)
    {
        Rows.Remove(item);
        ReindexRows();

        // Sin ese archivo la carpeta ya no está entera: su huella diría algo que no es. El freno del informe sigue
        // contando la carpeta (_addedRoots): lo que queda adentro sigue siendo evidencia.
        foreach (var root in _addedRoots.Where(r => Directory.Exists(r) && PackageOutput.FindSourceContaining(new[] { r }, item.FullPath) != null))
        {
            _partialRoots.Add(root);
        }

        if (AllHashesComputed)
        {
            UpdateFolderDigests();
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        Rows.Clear();
        _skippedLinks.Clear();
        _addedRoots.Clear();
        _partialRoots.Clear();
        AllHashesComputed = false;
    }

    /// <summary>Carpetas de las que se sacó un archivo de la lista: ya no tienen huella de carpeta.</summary>
    private readonly HashSet<string> _partialRoots = new(StringComparer.OrdinalIgnoreCase);

    partial void OnAllHashesComputedChanged(bool value)
    {
        if (!value)
        {
            ClearFolderDigests();
        }
    }

    /// <summary>Archivos y carpetas tal como se agregaron: el informe no puede quedar adentro de ninguno.</summary>
    private readonly List<string> _addedRoots = new();

    /// <summary>
    /// Dónde abre el diálogo de exportar: al lado de lo primero que se agregó, no adentro. Sin esto abría en la
    /// última carpeta de cualquier diálogo, que solía ser justo la que se acababa de agregar.
    /// </summary>
    public string? ExportStartFolder => _addedRoots.Count == 0 ? null : Path.GetDirectoryName(_addedRoots[0]);

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ComputeHashesAsync(CancellationToken ct)
    {
        if (IsLoadingSelection)
        {
            return;
        }

        if (Rows.Count == 0)
        {
            StatusMessage = _localization.Get("error_no_files");
            return;
        }

        var algorithms = BuildSelectedAlgorithms();
        if (algorithms.Count == 0)
        {
            StatusMessage = _localization.Get("error_no_algorithms");
            return;
        }

        _logService.Info(string.Format(_localization.Get("log_hash_start"), Rows.Count));
        IsProcessing = true;
        AllHashesComputed = false;
        StatusMessage = _localization.Get("processing");
        ProgressPercent = 0;

        try
        {
            int unreadable = 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var row = Rows[i];
                row.StatusText = _localization.Get("processing");
                row.HasError = false;
                row.Omission = null;

                _logService.LogEvidence(LogLevel.Info, _localization.Get("log_hash_processing"), row.FileName);
                try
                {
                    var result = await _hashService.ComputeHashesAsync(row.FullPath, algorithms, null, ct);
                    ApplyResult(row, result);
                    row.StatusText = string.Empty;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un archivo que no se puede leer (bloqueado por el antivirus, en uso, borrado) no corta el lote,
                    // como en el hash rápido: queda marcado y va al informe como no leído, con el código de Windows.
                    unreadable++;
                    row.HasError = true;
                    row.Omission = HashInventory.Unreadable(row.FullPath, row.FileName, ex);
                    row.StatusText = string.Format(_localization.Get("hash_row_unreadable"), ex.Message);
                    _logService.LogEvidence(LogLevel.Warning, _localization.Get("log_hash_unreadable"), $"{row.FullPath}: {ex.Message}");
                }

                ProgressPercent = 100.0 * (i + 1) / Rows.Count;
                ProgressDetail = $"{i + 1} / {Rows.Count}";
            }

            _computedAlgorithms = algorithms;
            AllHashesComputed = true;
            UpdateFolderDigests();
            StatusMessage = unreadable > 0
                ? string.Format(_localization.Get("hash_success_with_errors"), Rows.Count - unreadable, unreadable)
                : _localization.Get("hash_success");
            if (unreadable > 0)
            {
                // Con archivos sin leer el lote no terminó "correctamente": la bitácora lo dice en ámbar, con cuántos.
                _logService.Warning(string.Format(_localization.Get("log_hash_done_with_errors"),
                    Rows.Count - unreadable, unreadable));
            }
            else
            {
                _logService.Success(_localization.Get("log_hash_done"));
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning(_localization.Get("log_hash_cancelled"));
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_hash_error"), ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportReportAsync(string outputPath)
    {
        // El informe y su .sha256 dentro de una carpeta hasheada se mezclan con la evidencia (y entran en el
        // próximo cálculo de esa carpeta). Se frena antes de pedir los datos del operador.
        var inside = PackageOutput.FindSourceContaining(_addedRoots, outputPath)
                     ?? PackageOutput.FindSourceContaining(_addedRoots, outputPath + ".sha256");
        if (inside != null)
        {
            StatusMessage = string.Format(_localization.Get("hash_export_inside_source"), inside);
            _logService.LogEvidence(LogLevel.Warning, _localization.Get("log_hash_export_inside_source"), outputPath);
            return;
        }

        _logService.Info(_localization.Get("log_operator_confirm"));
        var config = _configService.Load();
        var confirmedResult = await _operatorDialog.ConfirmOperatorAsync(config.Operator, offerSigning: false, offerExternalHash: true);

        if (confirmedResult == null)
        {
            _logService.Warning(_localization.Get("log_report_export_cancelled"));
            return;
        }

        // Los algoritmos con que se calculó, no los marcados ahora: si después de calcular se marcaba otro, el
        // informe salía con esa columna vacía. Y los datos de la causa que pide el diálogo, que antes se descartaban.
        var algorithms = _computedAlgorithms;
        var data = new ReportData
        {
            Operator = confirmedResult.Operator,
            CaseNumber = confirmedResult.CaseNumber,
            CaseDescription = confirmedResult.CaseDescription,
            Court = confirmedResult.Court,
            Operation = OperationType.HashBatch,
            Algorithms = algorithms,
            FileResults = Rows.Where(r => !r.HasError).Select(r => new HashResult
            {
                FilePath = r.FileName,
                SourcePath = r.FullPath,
                FileSize = r.Size,
                ModifiedUtc = r.ModifiedUtc,
                Hashes = ExtractHashes(r, algorithms)
            }).ToList(),
            OmittedFiles = Rows.Where(r => r.Omission != null).Select(r => r.Omission!).Concat(_skippedLinks).ToList(),
            FolderDigests = FolderDigests.Select(f => f.Digest).ToList()
        };

        var content = _reportService.GenerateReport(data, _localization.CurrentLanguage);
        await _reportService.SaveReportAsync(content, outputPath);
        _logService.LogEvidence(LogLevel.Success, _localization.Get("log_report_exported"), outputPath);

        if (confirmedResult.GenerateExternalHash)
        {
            // El sidecar debe hashear el ARCHIVO en disco (UTF-8 con BOM), no el string en
            // memoria: VerifyReport re-hashea los bytes del archivo, que incluyen el BOM.
            // Hashear 'content' (sin BOM) producía un sidecar que nunca validaba.
            var reportHashResult = await _hashService.ComputeHashesAsync(
                outputPath,
                new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
                null,
                CancellationToken.None);
            var reportHash = reportHashResult.Hashes[HashAlgorithmType.SHA256];
            var hashFilePath = outputPath + ".sha256";
            await File.WriteAllTextAsync(hashFilePath, $"{reportHash}  {Path.GetFileName(outputPath)}");
            _logService.LogEvidence(LogLevel.Info, _localization.Get("log_sidecar_generated"), Path.GetFileName(hashFilePath));
        }
    }

    private bool CanExportReport(string _) => AllHashesComputed && Rows.Count > 0;

    private HashSet<HashAlgorithmType> BuildSelectedAlgorithms()
    {
        var set = new HashSet<HashAlgorithmType>();
        if (UseMd5) set.Add(HashAlgorithmType.MD5);
        if (UseSha1) set.Add(HashAlgorithmType.SHA1);
        if (UseSha256) set.Add(HashAlgorithmType.SHA256);
        if (UseSha512) set.Add(HashAlgorithmType.SHA512);
        return set;
    }

    /// <summary>Huella de cada carpeta agregada entera, con los mismos SHA-256 de la lista.</summary>
    public ObservableCollection<FolderDigestItem> FolderDigests { get; } = new();

    /// <summary>Hay carpetas en la lista pero no se calculó SHA-256: la huella de carpeta no sale.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderPanel))]
    private string _folderNotice = string.Empty;

    public bool HasFolderPanel => FolderDigests.Count > 0 || FolderNotice.Length > 0;
    public bool HasFolderDigests => FolderDigests.Count > 0;
    public string LabelFolderHeading => _localization.Get("hash_folder_heading");
    public string LabelFolderHint => _localization.Get("hash_folder_hint");

    private void UpdateFolderDigests()
    {
        FolderDigests.Clear();
        FolderNotice = string.Empty;
        if (!_addedRoots.Any(Directory.Exists))
        {
            OnPropertyChanged(nameof(HasFolderPanel));
            OnPropertyChanged(nameof(HasFolderDigests));
            return;
        }

        if (!_computedAlgorithms.Contains(HashAlgorithmType.SHA256))
        {
            FolderNotice = _localization.Get("hash_folder_need_sha256");
            return;
        }

        var files = Rows.Select(r => (r.FullPath, r.Size, r.HasError ? null : (string?)r.Sha256)).ToList();
        var omitted = Rows.Where(r => r.Omission != null).Select(r => r.Omission!).Concat(_skippedLinks);
        foreach (var digest in FolderHash.Compute(_addedRoots.Where(r => !_partialRoots.Contains(r)), files, omitted))
        {
            FolderDigests.Add(new FolderDigestItem(digest, _localization, HashRowItem.FormatSize));
        }

        OnPropertyChanged(nameof(HasFolderPanel));
        OnPropertyChanged(nameof(HasFolderDigests));
    }

    private void ClearFolderDigests()
    {
        FolderDigests.Clear();
        FolderNotice = string.Empty;
        OnPropertyChanged(nameof(HasFolderPanel));
        OnPropertyChanged(nameof(HasFolderDigests));
    }

    /// <summary>Copia una huella de carpeta al portapapeles.</summary>
    [RelayCommand]
    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)
            || Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
        StatusMessage = _localization.Get("copied");
    }

    private static void ApplyResult(HashRowItem row, HashResult result)
    {
        row.ModifiedUtc = result.ModifiedUtc;
        row.Size = result.FileSize;
        if (result.Hashes.TryGetValue(HashAlgorithmType.MD5, out var md5)) row.Md5 = md5;
        if (result.Hashes.TryGetValue(HashAlgorithmType.SHA1, out var sha1)) row.Sha1 = sha1;
        if (result.Hashes.TryGetValue(HashAlgorithmType.SHA256, out var sha256)) row.Sha256 = sha256;
        if (result.Hashes.TryGetValue(HashAlgorithmType.SHA512, out var sha512)) row.Sha512 = sha512;
    }

    private static Dictionary<HashAlgorithmType, string> ExtractHashes(HashRowItem row, HashSet<HashAlgorithmType> algorithms)
    {
        var dict = new Dictionary<HashAlgorithmType, string>();
        if (algorithms.Contains(HashAlgorithmType.MD5)) dict[HashAlgorithmType.MD5] = row.Md5;
        if (algorithms.Contains(HashAlgorithmType.SHA1)) dict[HashAlgorithmType.SHA1] = row.Sha1;
        if (algorithms.Contains(HashAlgorithmType.SHA256)) dict[HashAlgorithmType.SHA256] = row.Sha256;
        if (algorithms.Contains(HashAlgorithmType.SHA512)) dict[HashAlgorithmType.SHA512] = row.Sha512;
        return dict;
    }

    private void ReindexRows()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].Index = i + 1;
        }
    }
}
