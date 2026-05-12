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
    private bool _isProcessing;

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

        LoadDefaults();
    }

    public string Title => _localization.Get("hash_batch").ToUpper();
    public string LabelDropZone => _localization.Get("drag_drop_hash");
    public string LabelAlgorithms => _localization.Get("hash_algorithms");
    public string ButtonCalculate => _localization.Get("calculate");
    public string ButtonExport => _localization.Get("export_report");
    public string ButtonCancel => _localization.Get("cancel");
    public string ButtonClear => _localization.Get("clear_all");
    public string ButtonSelectFiles => _localization.Get("selected_files") + "...";

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
        OnPropertyChanged(nameof(StatusMessage));
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

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (Rows.Any(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fi = new FileInfo(path);
            Rows.Add(new HashRowItem
            {
                Index = Rows.Count + 1,
                FileName = fi.Name,
                FullPath = fi.FullName,
                Size = fi.Length,
                StatusText = _localization.Get("ready")
            });
        }
        AllHashesComputed = false;
    }

    [RelayCommand]
    private void RemoveRow(HashRowItem item)
    {
        Rows.Remove(item);
        ReindexRows();
    }

    [RelayCommand]
    private void ClearAll()
    {
        Rows.Clear();
        AllHashesComputed = false;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ComputeHashesAsync(CancellationToken ct)
    {
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

        _logService.Info($"Iniciando cálculo de hashes para {Rows.Count} archivos...");
        IsProcessing = true;
        AllHashesComputed = false;
        StatusMessage = _localization.Get("processing");
        ProgressPercent = 0;

        try
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var row = Rows[i];
                row.StatusText = _localization.Get("processing");

                _logService.Info($"Procesando: {row.FileName}");
                var result = await _hashService.ComputeHashesAsync(row.FullPath, algorithms, null, ct);
                ApplyResult(row, result);
                row.StatusText = string.Empty;

                ProgressPercent = 100.0 * (i + 1) / Rows.Count;
                ProgressDetail = $"{i + 1} / {Rows.Count}";
            }

            AllHashesComputed = true;
            StatusMessage = _localization.Get("hash_success");
            _logService.Success("Cálculo de hashes finalizado correctamente.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning("Cálculo de hashes cancelado por el usuario.");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
            _logService.Error($"Error en Hash Batch: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportReportAsync(string outputPath)
    {
        _logService.Info("Solicitando confirmación del operador para exportar informe...");
        var config = _configService.Load();
        var confirmedResult = await _operatorDialog.ConfirmOperatorAsync(config.Operator);

        if (confirmedResult == null)
        {
            _logService.Warning("Exportación de informe cancelada por el usuario.");
            return;
        }

        var algorithms = BuildSelectedAlgorithms();
        var data = new ReportData
        {
            Operator = confirmedResult.Operator,
            Operation = OperationType.HashBatch,
            Algorithms = algorithms,
            FileResults = Rows.Select(r => new HashResult
            {
                FilePath = r.FileName,
                FileSize = r.Size,
                Hashes = ExtractHashes(r, algorithms)
            }).ToList()
        };

        var content = _reportService.GenerateReport(data, _localization.CurrentLanguage);
        await _reportService.SaveReportAsync(content, outputPath);
        _logService.Success($"Informe de hashes exportado exitosamente: {outputPath}");

        if (confirmedResult.GenerateExternalHash)
        {
            var reportHash = _hashService.ComputeSha256(content);
            var hashFilePath = outputPath + ".sha256";
            await File.WriteAllTextAsync(hashFilePath, $"{reportHash}  {Path.GetFileName(outputPath)}");
            _logService.Info($"Archivo de integridad generado: {Path.GetFileName(hashFilePath)}");
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

    private static void ApplyResult(HashRowItem row, HashResult result)
    {
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
