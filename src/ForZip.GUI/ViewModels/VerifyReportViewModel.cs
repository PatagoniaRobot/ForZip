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
using ForZip.Core.Models;

namespace ForZip.GUI.ViewModels;

public enum VerificationStatus
{
    NotRun,
    Valid,
    Invalid,
    BadFormat
}

public partial class VerifyReportViewModel : ObservableObject
{
    private readonly IReportService _reportService;
    private readonly IHashService _hashService;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private string _reportFilePath = string.Empty;

    [ObservableProperty]
    private VerificationStatus _status = VerificationStatus.NotRun;

    [ObservableProperty]
    private string _detailsText = string.Empty;

    public VerifyReportViewModel(IReportService reportService, IHashService hashService, ILocalizationService localization)
    {
        _reportService = reportService;
        _hashService = hashService;
        _localization = localization;
    }

    public bool IsValid => Status == VerificationStatus.Valid;
    public bool IsInvalid => Status == VerificationStatus.Invalid;
    public bool IsBadFormat => Status == VerificationStatus.BadFormat;
    public bool HasResult => Status != VerificationStatus.NotRun;

    partial void OnStatusChanged(VerificationStatus value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsInvalid));
        OnPropertyChanged(nameof(IsBadFormat));
        OnPropertyChanged(nameof(HasResult));
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (string.IsNullOrWhiteSpace(ReportFilePath) || !File.Exists(ReportFilePath))
        {
            Status = VerificationStatus.BadFormat;
            DetailsText = _localization.Get("verify_bad_format");
            return;
        }

        Status = VerificationStatus.NotRun;
        DetailsText = "Verificando integridad...";

        // Intentar verificación externa (.sha256)
        var hashFilePath = ReportFilePath + ".sha256";
        if (File.Exists(hashFilePath))
        {
            try
            {
                var hashFileContent = await File.ReadAllTextAsync(hashFilePath);
                var match = System.Text.RegularExpressions.Regex.Match(hashFileContent, "^([0-9a-fA-F]{64})");
                if (match.Success)
                {
                    var expectedHash = match.Groups[1].Value.ToLowerInvariant();
                    var reportHashResult = await _hashService.ComputeHashesAsync(
                        ReportFilePath, 
                        new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, 
                        null, 
                        CancellationToken.None);
                    
                    var actualHash = reportHashResult.Hashes[HashAlgorithmType.SHA256];

                    if (string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                    {
                        Status = VerificationStatus.Valid;
                        DetailsText = "Verificación exitosa mediante archivo de hash externo (.sha256).";
                        return;
                    }
                    else
                    {
                        Status = VerificationStatus.Invalid;
                        DetailsText = $"DISCREPANCIA DE HASH:\nEsperado: {expectedHash}\nObtenido: {actualHash}";
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Status = VerificationStatus.BadFormat;
                DetailsText = $"Error al leer el archivo de hash: {ex.Message}";
                return;
            }
        }

        // Si no hay hash externo, el ReportService ahora devuelve que no puede verificar internamente
        var (isValid, details) = _reportService.VerifyReport(ReportFilePath);
        DetailsText = details;

        if (isValid)
        {
            Status = VerificationStatus.Valid;
        }
        else
        {
            Status = VerificationStatus.Invalid;
        }
    }
}
