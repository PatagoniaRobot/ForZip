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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;

namespace ForZip.GUI.ViewModels;

public partial class UnzipViewModel : ViewModelBase
{
    private readonly IZipService _zipService;
    private readonly IConfigService _configService;
    private readonly ILogService _logService;

    [ObservableProperty]
    private string _zipFilePath = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressDetail = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public UnzipViewModel(
        IZipService zipService,
        ILocalizationService localization,
        IConfigService configService,
        ILogService logService) : base(localization)
    {
        _zipService = zipService;
        _configService = configService;
        _logService = logService;

        LoadDefaults();
    }

    public string Title => _localization.Get("extract").ToUpper();
    public string LabelZipPath => _localization.Get("input_file");
    public string LabelOutputDir => _localization.Get("destination_folder");
    public string LabelPassword => _localization.Get("password_optional");
    public string LabelDropZone => _localization.Get("drag_drop_zip");
    public string ButtonExtract => _localization.Get("extract");
    public string ButtonCancel => _localization.Get("cancel");

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LabelZipPath));
        OnPropertyChanged(nameof(LabelOutputDir));
        OnPropertyChanged(nameof(LabelPassword));
        OnPropertyChanged(nameof(LabelDropZone));
        OnPropertyChanged(nameof(ButtonExtract));
        OnPropertyChanged(nameof(ButtonCancel));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void LoadDefaults()
    {
        var config = _configService.Load();
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            OutputDirectory = config.DefaultOutputDirectory ?? string.Empty;
        }
    }

    public void SetZipPath(string path)
    {
        ZipFilePath = path;
        // Sugerencia: misma carpeta + subcarpeta con el nombre del ZIP
        if (!string.IsNullOrEmpty(path) && string.IsNullOrEmpty(OutputDirectory))
        {
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            OutputDirectory = Path.Combine(dir, name);
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DecompressAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ZipFilePath) || string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = _localization.Get("error_no_files");
            return;
        }

        _logService.Info($"Iniciando extracción: {Path.GetFileName(ZipFilePath)}");
        _logService.Info($"- Destino: {OutputDirectory}");
        _logService.Info($"- Cifrado: {(string.IsNullOrEmpty(Password) ? "No" : "AES-256")}");

        IsProcessing = true;
        StatusMessage = _localization.Get("processing");
        ProgressPercent = 0;

        try
        {
            var progress = new Progress<(long processed, long total)>(p =>
            {
                if (p.total > 0)
                {
                    ProgressPercent = 100.0 * p.processed / p.total;
                    ProgressDetail = $"{p.processed:N0} / {p.total:N0} B";
                }
            });

            await _zipService.DecompressAsync(
                ZipFilePath, OutputDirectory,
                string.IsNullOrEmpty(Password) ? null : Password,
                progress, ct);

            ProgressPercent = 100;
            StatusMessage = _localization.Get("unzip_success");
            _logService.Success($"Extracción finalizada exitosamente en: {OutputDirectory}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning("Extracción cancelada por el usuario. Limpiando archivos parciales...");
        }
        catch (Exception ex)
        {
            // SharpZipLib lanza ZipException con mensaje genérico cuando la contraseña es inválida
            if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("contraseña", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = _localization.Get("error_wrong_password");
                _logService.Error("Error: Contraseña incorrecta.");
            }
            else
            {
                StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
                _logService.Error($"Error durante la extracción: {ex.Message}");
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
