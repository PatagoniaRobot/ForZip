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
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ForZip.Core.Interfaces;
using ForZip.GUI.Services;
using ForZip.Core.Models;

namespace ForZip.GUI.ViewModels;

public record CompressionLevelItem(int Level, string Name);

public partial class ZipViewModel : ViewModelBase
{
    private List<CompressionLevelItem> _availableLevels = new();
    public List<CompressionLevelItem> AvailableLevels 
    { 
        get => _availableLevels;
        private set => SetProperty(ref _availableLevels, value);
    }

    private readonly IZipService _zipService;
    private readonly IHashService _hashService;
    private readonly IPasswordService _passwordService;
    private readonly IReportService _reportService;
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly IOperatorDialogService _operatorDialog;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private CompressionLevelItem _selectedCompression = null!;

    [ObservableProperty]
    private string _password = string.Empty;

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
    private string _elapsedTime = "00:00:00";

    [ObservableProperty]
    private string _remainingTime = "--:--:--";

    [ObservableProperty]
    private bool _isEncryptionEnabled;

    [ObservableProperty]
    private bool _isPasswordVisible;

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordCharDisplay));
    }

    public ZipViewModel(
        IZipService zipService,
        IHashService hashService,
        IPasswordService passwordService,
        IReportService reportService,
        ILocalizationService localization,
        IConfigService configService,
        ILogService logService,
        IOperatorDialogService operatorDialog) : base(localization)
    {
        _zipService = zipService;
        _hashService = hashService;
        _passwordService = passwordService;
        _reportService = reportService;
        _configService = configService;
        _logService = logService;
        _operatorDialog = operatorDialog;

        Files = new ObservableCollection<SelectedFileItem>();
        Files.CollectionChanged += (s, e) => OnPropertyChanged(nameof(LabelFilesCount));
        RefreshAvailableLevels();
        LoadDefaults();
    }

    public string LabelFilesCount => $"{_localization.Get("selected_files")} ({Files.Count})";

    private void RefreshAvailableLevels()
    {
        var currentLevel = SelectedCompression?.Level ?? 5;
        AvailableLevels = new List<CompressionLevelItem>
        {
            new(0, $"0 - {_localization.Get("report_level_0")}"),
            new(1, $"1 - {_localization.Get("report_level_1")}"),
            new(3, $"3 - {_localization.Get("report_level_3")}"),
            new(5, $"5 - {_localization.Get("report_level_5")}"),
            new(7, $"7 - {_localization.Get("report_level_7")}"),
            new(9, $"9 - {_localization.Get("report_level_9")}")
        };
        SelectedCompression = AvailableLevels.First(l => l.Level == currentLevel);
    }

    public string Title => _localization.Get("compress").ToUpper();
    public string LabelDropZone => _localization.Get("drag_drop_files");
    public string LabelFiles => _localization.Get("selected_files");
    public string LabelOptions => _localization.Get("settings_defaults");
    public string LabelCompression => _localization.Get("compression_level");
    public string LabelHashes => _localization.Get("hash_algorithms");
    public string LabelOutput => _localization.Get("output_file");
    public string ButtonPack => _localization.Get("compress");
    public string ButtonCancel => _localization.Get("cancel");
    public string ButtonClear => _localization.Get("clear_all");

    protected override void OnLanguageChanged()
    {
        RefreshAvailableLevels();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LabelDropZone));
        OnPropertyChanged(nameof(LabelFiles));
        OnPropertyChanged(nameof(LabelFilesCount));
        OnPropertyChanged(nameof(LabelOptions));
        OnPropertyChanged(nameof(LabelCompression));
        OnPropertyChanged(nameof(LabelHashes));
        OnPropertyChanged(nameof(LabelOutput));
        OnPropertyChanged(nameof(ButtonPack));
        OnPropertyChanged(nameof(ButtonCancel));
        OnPropertyChanged(nameof(ButtonClear));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void LoadDefaults()
    {
        var config = _configService.Load();
        SelectedCompression = AvailableLevels.FirstOrDefault(l => l.Level == config.DefaultCompressionLevel) ?? AvailableLevels[3];
        OutputPath = config.DefaultOutputDirectory ?? string.Empty;
        
        UseMd5 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.MD5);
        UseSha1 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA1);
        UseSha256 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA256);
        UseSha512 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA512);
    }

    public ObservableCollection<SelectedFileItem> Files { get; } = new();

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Files.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                Files.Add(new SelectedFileItem
                {
                    FullPath = fi.FullName,
                    DisplayName = fi.Name,
                    Size = fi.Length,
                    IsFolder = false
                });
            }
            else if (Directory.Exists(path))
            {
                var dirFiles = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList();
                long total = 0;
                foreach (var f in dirFiles)
                {
                    total += new FileInfo(f).Length;
                }

                Files.Add(new SelectedFileItem
                {
                    FullPath = path,
                    DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) + Path.DirectorySeparatorChar,
                    Size = total,
                    IsFolder = true,
                    FolderFileCount = dirFiles.Count
                });
            }
        }
        UpdateOutputPath();
    }

    private void UpdateOutputPath()
    {
        if (Files.Count > 0 && string.IsNullOrEmpty(OutputPath))
        {
            var first = Files[0].FullPath;
            var dir = Path.GetDirectoryName(first) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = "Evidencia";
            OutputPath = Path.Combine(dir, name + ".zip");
        }
    }

    [RelayCommand]
    private void RemoveFile(SelectedFileItem item)
    {
        Files.Remove(item);
    }

    [RelayCommand]
    private void ClearAll()
    {
        Files.Clear();
        OutputPath = string.Empty;
        Password = string.Empty;
        IsEncryptionEnabled = false;
        IsPasswordVisible = false;
        StatusMessage = string.Empty;
        ProgressPercent = 0;
        ProgressDetail = string.Empty;
        LoadDefaults();
    }

    [RelayCommand]
    private async Task CopyPasswordAsync()
    {
        if (string.IsNullOrEmpty(Password)) return;
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(Password);
                _logService.Info("Contraseña copiada al portapapeles.");
            }
        }
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        Password = _passwordService.GeneratePassword(new PasswordOptions { Length = 16 });
        IsPasswordVisible = false;
        OnPropertyChanged(nameof(PasswordCharDisplay));
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    public char PasswordCharDisplay => IsPasswordVisible ? '\0' : '•';

    private string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CompressAsync(CancellationToken ct)
    {
        if (Files.Count == 0) return;

        _logService.Info($"Iniciando empaquetado: {Path.GetFileName(OutputPath)}");
        _logService.Info($"- Archivos origen: {Files.Count}");
        _logService.Info($"- Nivel: {SelectedCompression.Name}");
        _logService.Info($"- Cifrado: {(IsEncryptionEnabled && !string.IsNullOrEmpty(Password) ? "AES-256" : "No")}");

        IsProcessing = true;
        StatusMessage = _localization.Get("processing");
        ProgressPercent = 0;
        var startTime = DateTime.Now;


        var algorithms = BuildSelectedAlgorithms();
        var hasHashing = algorithms.Count > 0;

        var progressHandler = new Progress<(long processed, long total)>(p =>
        {
            var percent = p.total > 0 ? (double)p.processed / p.total * 100 : 0;
            ProgressPercent = percent;

            var elapsed = DateTime.Now - startTime;
            ElapsedTime = elapsed.ToString(@"hh\:mm\:ss");

            if (percent > 1 && elapsed.TotalSeconds > 2)
            {
                var totalEstimated = TimeSpan.FromSeconds(elapsed.TotalSeconds / (percent / 100));
                var remaining = totalEstimated - elapsed;
                RemainingTime = remaining.TotalSeconds > 0 ? remaining.ToString(@"hh\:mm\:ss") : "00:00:00";
            }

            // Mapeo de bytes para el label: p.total es totalWork (2x si hay hashing)
            var realSize = hasHashing ? p.total / 2 : p.total;
            long displayProcessed;
            
            if (hasHashing)
            {
                if (p.processed <= realSize) displayProcessed = p.processed; // Hashing
                else displayProcessed = p.processed - realSize; // Packing
            }
            else
            {
                displayProcessed = p.processed;
            }

            ProgressDetail = $"{percent:F1}% — {FormatSize(displayProcessed)} de {FormatSize(realSize)}";
        });

        try
        {
            var options = new ZipOptions
            {
                SourcePaths = Files.Select(f => f.FullPath).ToList(),
                OutputPath = OutputPath,
                CompressionLevel = SelectedCompression.Level,
                Password = IsEncryptionEnabled ? Password : null,
                HashAlgorithms = algorithms
            };

            var hashResults = await _zipService.CompressAsync(options, progressHandler, ct);

            // Genera informe forense automáticamente si se solicitaron hashes
            if (algorithms.Count > 0 && hashResults.Count > 0)
            {
                _logService.Info("Solicitando confirmación del operador para el informe forense...");
                var config = _configService.Load();
                var confirmedResult = await _operatorDialog.ConfirmOperatorAsync(config.Operator);
                
                if (confirmedResult != null)
                {
                    await GenerateAccompanyingReportAsync(options, hashResults, confirmedResult);
                    _logService.Success("Informe forense generado exitosamente.");
                }
                else
                {
                    _logService.Warning("El usuario canceló el diálogo de operador. El informe no fue generado.");
                }
            }

            ProgressPercent = 100;
            StatusMessage = _localization.Get("zip_success");
            _logService.Success($"Archivo ZIP creado exitosamente: {options.OutputPath}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning("Operación de compresión cancelada por el usuario.");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
            _logService.Error($"Error durante la compresión: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private HashSet<HashAlgorithmType> BuildSelectedAlgorithms()
    {
        var set = new HashSet<HashAlgorithmType>();
        if (UseMd5) set.Add(HashAlgorithmType.MD5);
        if (UseSha1) set.Add(HashAlgorithmType.SHA1);
        if (UseSha256) set.Add(HashAlgorithmType.SHA256);
        if (UseSha512) set.Add(HashAlgorithmType.SHA512);
        return set;
    }

    private async Task GenerateAccompanyingReportAsync(ZipOptions options, List<HashResult> hashResults, OperatorConfirmationResult result)
    {
        var zipHashResult = await _hashService.ComputeHashesAsync(
            options.OutputPath, 
            new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, 
            null, 
            CancellationToken.None);

        var data = new ReportData
        {
            Operator = result.Operator,
            Operation = OperationType.Compression,
            CompressionLevel = options.CompressionLevel,
            HasPassword = !string.IsNullOrEmpty(options.Password),
            Algorithms = options.HashAlgorithms,
            ZipFilePath = options.OutputPath,
            ZipFileSize = new FileInfo(options.OutputPath).Length,
            ZipHash = zipHashResult.Hashes[HashAlgorithmType.SHA256],
            FileResults = hashResults
        };

        var content = _reportService.GenerateReport(data, _localization.CurrentLanguage);
        var reportPath = Path.ChangeExtension(options.OutputPath, ".report.txt");
        await _reportService.SaveReportAsync(content, reportPath);

        if (result.GenerateExternalHash)
        {
            var reportHashResult = await _hashService.ComputeHashesAsync(
                reportPath, 
                new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, 
                null, 
                CancellationToken.None);
                
            var reportHash = reportHashResult.Hashes[HashAlgorithmType.SHA256];
            var hashFilePath = reportPath + ".sha256";
            await File.WriteAllTextAsync(hashFilePath, $"{reportHash}  {Path.GetFileName(reportPath)}");
            _logService.Info($"Archivo de integridad generado: {Path.GetFileName(hashFilePath)}");
        }
    }
}
