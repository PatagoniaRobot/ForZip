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
using ForZip.Core.Services;

namespace ForZip.GUI.ViewModels;

public record CompressionLevelItem(int Level, string Name);

public record SplitSizeItem(string Name, long Bytes);

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
    private readonly ISignatureService _signatureService;
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

    [ObservableProperty]
    private bool _isSplitEnabled;

    /// <summary>
    /// Modo simple (guiado): oculta las opciones técnicas y aplica los valores forenses
    /// recomendados (SHA-256 + informe + manifiesto). Persiste en la configuración.
    /// </summary>
    [ObservableProperty]
    private bool _isSimpleMode = true;

    // ---- Pantalla de resultado (post-empaquetado) ----

    [ObservableProperty]
    private bool _isResultVisible;

    [ObservableProperty]
    private string _resultZipHash = string.Empty;

    [ObservableProperty]
    private string _resultNote = string.Empty;

    public ObservableCollection<ResultArtifactItem> ResultArtifacts { get; } = new();

    /// <summary>Ruta del artefacto principal (ZIP o primer volumen) para "Abrir carpeta".</summary>
    private string _resultPrimaryPath = string.Empty;

    // Tamaños comunes de volumen (estilo 7-Zip). El valor en bytes es lo que se pasa al Core.
    public List<SplitSizeItem> AvailableSplitSizes { get; } = new()
    {
        new("10 MB", 10L * 1024 * 1024),
        new("100 MB", 100L * 1024 * 1024),
        new("200 MB", 200L * 1024 * 1024),
        new("700 MB (CD)", 700L * 1024 * 1024),
        new("1 GB", 1024L * 1024 * 1024),
        new("2 GB", 2L * 1024 * 1024 * 1024),
        new("4 GB (FAT32)", 4000L * 1024 * 1024)
    };

    [ObservableProperty]
    private SplitSizeItem _selectedSplitSize = null!;

    // Si el usuario escribe un tamaño en MB, tiene prioridad sobre el preset seleccionado.
    [ObservableProperty]
    private string _customSplitMb = string.Empty;

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordCharDisplay));
    }

    partial void OnIsSimpleModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonPack));

        // Persistir la preferencia de modo (ignorando fallas de E/S en medios de solo lectura)
        try
        {
            var config = _configService.Load();
            if (config.SimpleMode != value)
            {
                config.SimpleMode = value;
                _configService.Save(config);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public ZipViewModel(
        IZipService zipService,
        IHashService hashService,
        IPasswordService passwordService,
        IReportService reportService,
        ISignatureService signatureService,
        ILocalizationService localization,
        IConfigService configService,
        ILogService logService,
        IOperatorDialogService operatorDialog) : base(localization)
    {
        _zipService = zipService;
        _hashService = hashService;
        _passwordService = passwordService;
        _reportService = reportService;
        _signatureService = signatureService;
        _configService = configService;
        _logService = logService;
        _operatorDialog = operatorDialog;

        Files = new ObservableCollection<SelectedFileItem>();
        Files.CollectionChanged += (s, e) => OnPropertyChanged(nameof(LabelFilesCount));
        SelectedSplitSize = AvailableSplitSizes[1]; // 100 MB por defecto
        RefreshAvailableLevels();
        LoadDefaults();
    }

    /// <summary>
    /// Resuelve el tamaño de volumen efectivo (bytes) según el toggle, el campo personalizado
    /// (en MB, con prioridad) y el preset seleccionado. <c>null</c> si la división está desactivada.
    /// </summary>
    private long? ResolveSplitSize()
    {
        if (!IsSplitEnabled)
        {
            return null;
        }

        if (long.TryParse(CustomSplitMb, out var mb) && mb > 0)
        {
            return mb * 1024 * 1024;
        }

        return SelectedSplitSize?.Bytes ?? AvailableSplitSizes[1].Bytes;
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
    public string LabelSplit => _localization.Get("split_into_volumes");
    public string LabelSplitSize => _localization.Get("volume_size");
    public string LabelSplitCustom => _localization.Get("custom_mb");
    public string ButtonPack => IsSimpleMode ? _localization.Get("pack_evidence") : _localization.Get("compress");
    public string ButtonCancel => _localization.Get("cancel");
    public string ButtonClear => _localization.Get("clear_all");

    // Modo simple / avanzado
    public string LabelModeSimple => _localization.Get("mode_simple");
    public string LabelModeAdvanced => _localization.Get("mode_advanced");
    public string LabelSimpleHint => _localization.Get("simple_mode_hint");

    // Textos de la vista que estaban fijos en español
    public string LabelBrowseFiles => _localization.Get("browse_files");
    public string LabelBrowseFolder => _localization.Get("browse_folder");
    public string LabelEncryption => _localization.Get("encryption_aes256");
    public string LabelPasswordPlaceholder => _localization.Get("password_placeholder");
    public string TooltipGeneratePassword => _localization.Get("tooltip_gen_password");
    public string TooltipCopyPassword => _localization.Get("tooltip_copy_clipboard");
    public string TooltipTogglePassword => _localization.Get("tooltip_toggle_password");
    public string TooltipRemoveFile => _localization.Get("remove_file");
    public string LabelElapsed => _localization.Get("elapsed");
    public string LabelRemaining => _localization.Get("remaining");
    public string PickerFilesTitle => _localization.Get("picker_files_title");
    public string PickerFoldersTitle => _localization.Get("picker_folders_title");
    public string PickerSaveZipTitle => _localization.Get("picker_save_zip_title");

    // Pantalla de resultado
    public string ResultTitle => _localization.Get("result_title");
    public string ResultSubtitle => _localization.Get("result_subtitle");
    public string LabelResultHash => _localization.Get("result_zip_hash");
    public string LabelOpenFolder => _localization.Get("open_folder");
    public string LabelCopyHash => _localization.Get("copy_hash");
    public string LabelNewJob => _localization.Get("new_job");

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
        OnPropertyChanged(nameof(LabelSplit));
        OnPropertyChanged(nameof(LabelSplitSize));
        OnPropertyChanged(nameof(LabelSplitCustom));
        OnPropertyChanged(nameof(ButtonPack));
        OnPropertyChanged(nameof(ButtonCancel));
        OnPropertyChanged(nameof(ButtonClear));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(LabelModeSimple));
        OnPropertyChanged(nameof(LabelModeAdvanced));
        OnPropertyChanged(nameof(LabelSimpleHint));
        OnPropertyChanged(nameof(LabelBrowseFiles));
        OnPropertyChanged(nameof(LabelBrowseFolder));
        OnPropertyChanged(nameof(LabelEncryption));
        OnPropertyChanged(nameof(LabelPasswordPlaceholder));
        OnPropertyChanged(nameof(TooltipGeneratePassword));
        OnPropertyChanged(nameof(TooltipCopyPassword));
        OnPropertyChanged(nameof(TooltipTogglePassword));
        OnPropertyChanged(nameof(TooltipRemoveFile));
        OnPropertyChanged(nameof(LabelElapsed));
        OnPropertyChanged(nameof(LabelRemaining));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultSubtitle));
        OnPropertyChanged(nameof(LabelResultHash));
        OnPropertyChanged(nameof(LabelOpenFolder));
        OnPropertyChanged(nameof(LabelCopyHash));
        OnPropertyChanged(nameof(LabelNewJob));
    }

    private void LoadDefaults()
    {
        var config = _configService.Load();
        SelectedCompression = AvailableLevels.FirstOrDefault(l => l.Level == config.DefaultCompressionLevel) ?? AvailableLevels[3];
        OutputPath = config.DefaultOutputDirectory ?? string.Empty;
        IsSimpleMode = config.SimpleMode;

        UseMd5 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.MD5);
        UseSha1 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA1);
        UseSha256 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA256);
        UseSha512 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA512);
    }

    public ObservableCollection<SelectedFileItem> Files { get; } = new();

    public void AddPaths(IEnumerable<string> paths)
    {
        // Al cargar evidencia nueva se abandona la pantalla de resultado anterior
        IsResultVisible = false;

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
        IsSplitEnabled = false;
        CustomSplitMb = string.Empty;
        StatusMessage = string.Empty;
        ProgressPercent = 0;
        ProgressDetail = string.Empty;
        IsResultVisible = false;
        ResultArtifacts.Clear();
        ResultZipHash = string.Empty;
        ResultNote = string.Empty;
        _resultPrimaryPath = string.Empty;
        LoadDefaults();
    }

    /// <summary>Cierra la pantalla de resultado y deja todo listo para otro empaquetado.</summary>
    [RelayCommand]
    private void NewJob() => ClearAll();

    [RelayCommand]
    private void OpenResultFolder()
    {
        if (string.IsNullOrEmpty(_resultPrimaryPath))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(_resultPrimaryPath))
            {
                // Abre el Explorador con el archivo generado ya seleccionado
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_resultPrimaryPath}\"");
                return;
            }

            var dir = Path.GetDirectoryName(_resultPrimaryPath);
            if (!string.IsNullOrEmpty(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logService.Error(string.Format(_localization.Get("log_open_folder_error"), ex.Message));
        }
    }

    [RelayCommand]
    private async Task CopyResultHashAsync()
    {
        if (string.IsNullOrEmpty(ResultZipHash)) return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(ResultZipHash);
                _logService.Info(_localization.Get("log_hash_copied"));
            }
        }
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
                _logService.Info(_localization.Get("log_password_copied"));
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

        _logService.Info(string.Format(_localization.Get("log_zip_start"), Path.GetFileName(OutputPath)));
        _logService.Info(string.Format(_localization.Get("log_zip_sources"), Files.Count));
        _logService.Info(string.Format(_localization.Get("log_zip_level"), SelectedCompression.Name));
        _logService.Info(string.Format(_localization.Get("log_encryption"),
            IsEncryptionEnabled && !string.IsNullOrEmpty(Password) ? "AES-256" : _localization.Get("log_no")));

        IsProcessing = true;
        StatusMessage = _localization.Get("processing");
        ProgressPercent = 0;
        IsResultVisible = false;
        ResultArtifacts.Clear();
        ResultZipHash = string.Empty;
        ResultNote = string.Empty;
        var startTime = DateTime.Now;

        // En modo simple se aplican los valores forenses recomendados: SHA-256 siempre
        // (habilita informe + manifiesto + verificación posterior) y sin división en volúmenes.
        var algorithms = IsSimpleMode
            ? new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
            : BuildSelectedAlgorithms();

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

            // Empaquetado y hashing ocurren en un único pase sobre la evidencia, por lo que
            // p.total es el tamaño real total (1x) y p.processed avanza una sola vez.
            ProgressDetail = $"{percent:F1}% — {FormatSize(p.processed)} de {FormatSize(p.total)}";
        });

        try
        {
            var options = new ZipOptions
            {
                SourcePaths = Files.Select(f => f.FullPath).ToList(),
                OutputPath = OutputPath,
                CompressionLevel = SelectedCompression.Level,
                Password = IsEncryptionEnabled ? Password : null,
                HashAlgorithms = algorithms,
                SplitSize = IsSimpleMode ? null : ResolveSplitSize()
            };

            var compression = await _zipService.CompressAsync(options, progressHandler, ct);

            if (compression.IsSplit)
            {
                _logService.Info(string.Format(_localization.Get("log_zip_split"),
                    compression.Volumes.Count, compression.Volumes.Count.ToString("D3")));
            }

            AddArchiveArtifacts(options, compression);

            // Genera informe forense automáticamente si se solicitaron hashes
            if (algorithms.Count > 0 && compression.FileHashes.Count > 0)
            {
                _logService.Info(_localization.Get("log_operator_confirm"));
                var config = _configService.Load();
                var confirmedResult = await _operatorDialog.ConfirmOperatorAsync(config.Operator, config.TimestampServerUrl);

                if (confirmedResult != null)
                {
                    await GenerateAccompanyingReportAsync(options, compression, confirmedResult);
                    _logService.Success(_localization.Get("log_report_generated"));
                }
                else
                {
                    ResultNote = _localization.Get("result_no_report");
                    _logService.Warning(_localization.Get("log_report_skipped"));
                }
            }

            ProgressPercent = 100;
            StatusMessage = _localization.Get("zip_success");
            IsResultVisible = true;
            _logService.Success(string.Format(_localization.Get("log_zip_done"), options.OutputPath));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning(_localization.Get("log_zip_cancelled"));
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
            _logService.Error(string.Format(_localization.Get("log_zip_error"), ex.Message));
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

    /// <summary>
    /// Calcula el SHA-256 y el tamaño del ZIP lógico. Para un archivo dividido hashea la
    /// concatenación de los volúmenes (que ya no existe como archivo único en disco).
    /// </summary>
    private async Task<(string hash, long size)> ComputeLogicalZipHashAsync(string outputPath, CompressionResult compression)
    {
        if (compression.IsSplit)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? string.Empty;
            var segments = compression.Volumes.Select(v => Path.Combine(dir, v.FileName)).ToList();
            var size = compression.Volumes.Sum(v => v.Size);

            await using var logical = new ConcatenatedReadStream(segments);
            var hashes = await _hashService.ComputeHashesAsync(
                logical, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, CancellationToken.None);
            return (hashes[HashAlgorithmType.SHA256], size);
        }

        var single = await _hashService.ComputeHashesAsync(
            outputPath, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None);
        return (single.Hashes[HashAlgorithmType.SHA256], new FileInfo(outputPath).Length);
    }

    /// <summary>
    /// Agrega a la pantalla de resultado el ZIP (o sus volúmenes) recién creado.
    /// </summary>
    private void AddArchiveArtifacts(ZipOptions options, CompressionResult compression)
    {
        if (compression.IsSplit)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)) ?? string.Empty;
            _resultPrimaryPath = Path.Combine(dir, compression.Volumes[0].FileName);

            var volumesLabel = string.Format(
                _localization.Get("result_volumes_name"),
                compression.Volumes[0].FileName, compression.Volumes.Count);
            ResultArtifacts.Add(new ResultArtifactItem(
                "📦", volumesLabel, _localization.Get("result_volumes_desc"), _resultPrimaryPath));
        }
        else
        {
            _resultPrimaryPath = options.OutputPath;
            ResultArtifacts.Add(new ResultArtifactItem(
                "📦", Path.GetFileName(options.OutputPath), _localization.Get("result_zip_desc"), options.OutputPath));
        }
    }

    private async Task GenerateAccompanyingReportAsync(ZipOptions options, CompressionResult compression, OperatorConfirmationResult result)
    {
        // Hash global y tamaño del ZIP lógico (concatenación de volúmenes si está dividido).
        var (zipHash, zipSize) = await ComputeLogicalZipHashAsync(options.OutputPath, compression);
        ResultZipHash = zipHash;

        var data = new ReportData
        {
            Operator = result.Operator,
            CaseNumber = result.CaseNumber,
            CaseDescription = result.CaseDescription,
            Court = result.Court,
            Operation = OperationType.Compression,
            CompressionLevel = options.CompressionLevel,
            HasPassword = !string.IsNullOrEmpty(options.Password),
            Algorithms = options.HashAlgorithms,
            ZipFilePath = options.OutputPath,
            ZipFileSize = zipSize,
            ZipHash = zipHash,
            Volumes = compression.Volumes.ToList(),
            FileResults = compression.FileHashes
        };

        var content = _reportService.GenerateReport(data, _localization.CurrentLanguage);
        var reportPath = Path.ChangeExtension(options.OutputPath, ".report.txt");
        await _reportService.SaveReportAsync(content, reportPath);
        ResultArtifacts.Add(new ResultArtifactItem(
            "📄", Path.GetFileName(reportPath), _localization.Get("result_report_desc"), reportPath));

        // Manifiesto JSON (fuente de verdad legible por máquina para verificación automática)
        var manifestPath = options.OutputPath + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));
        _logService.Info(string.Format(_localization.Get("log_manifest_generated"), Path.GetFileName(manifestPath)));
        ResultArtifacts.Add(new ResultArtifactItem(
            "🧾", Path.GetFileName(manifestPath), _localization.Get("result_manifest_desc"), manifestPath));

        // Firma digital del manifiesto (CMS/PKCS#7) si el operador la solicitó,
        // con sello de tiempo RFC 3161 opcional
        if (result.SignManifest && !string.IsNullOrWhiteSpace(result.CertificatePath))
        {
            var signaturePath = manifestPath + ".p7s";
            try
            {
                await _signatureService.SignAsync(
                    manifestPath, result.CertificatePath!, result.CertificatePassword,
                    result.TimestampUrl, CancellationToken.None);
                var sealed_ = !string.IsNullOrEmpty(result.TimestampUrl);
                _logService.Success(string.Format(
                    _localization.Get(sealed_ ? "log_manifest_signed_ts" : "log_manifest_signed"),
                    Path.GetFileName(signaturePath)));
                ResultArtifacts.Add(new ResultArtifactItem(
                    "🔏", Path.GetFileName(signaturePath),
                    _localization.Get(sealed_ ? "result_signature_ts_desc" : "result_signature_desc"),
                    signaturePath));
            }
            catch (TimestampUnavailableException ex)
            {
                // La firma sí se escribió; solo faltó el sello de tiempo
                _logService.Warning(string.Format(_localization.Get("log_timestamp_missing"), ex.Message));
                ResultArtifacts.Add(new ResultArtifactItem(
                    "🔏", Path.GetFileName(signaturePath),
                    _localization.Get("result_signature_desc"), signaturePath));
                ResultNote = _localization.Get("result_timestamp_failed");
            }
            catch (Exception ex)
            {
                _logService.Error(string.Format(_localization.Get("log_sign_error"), ex.Message));
            }
        }

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
            _logService.Info(string.Format(_localization.Get("log_sidecar_generated"), Path.GetFileName(hashFilePath)));
            ResultArtifacts.Add(new ResultArtifactItem(
                "#", Path.GetFileName(hashFilePath), _localization.Get("result_sidecar_desc"), hashFilePath));
        }
    }
}
