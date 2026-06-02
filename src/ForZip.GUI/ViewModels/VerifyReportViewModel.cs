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
    private readonly IVerificationService _verificationService;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private string _reportFilePath = string.Empty;

    [ObservableProperty]
    private VerificationStatus _status = VerificationStatus.NotRun;

    [ObservableProperty]
    private string _detailsText = string.Empty;

    public VerifyReportViewModel(
        IReportService reportService,
        IHashService hashService,
        IVerificationService verificationService,
        ILocalizationService localization)
    {
        _reportService = reportService;
        _hashService = hashService;
        _verificationService = verificationService;
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

        // Si el archivo es un manifiesto, verificamos la evidencia (re-hash del ZIP)
        if (ReportFilePath.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            await VerifyArchiveAsync();
            return;
        }

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

    private async Task VerifyArchiveAsync()
    {
        try
        {
            var result = await _verificationService.VerifyArchiveAsync(
                ReportFilePath, null, null, null, CancellationToken.None);

            var sb = new System.Text.StringBuilder();
            foreach (var entry in result.Entries)
            {
                var tag = entry.Status switch
                {
                    Core.Models.FileVerificationStatus.Ok => "[OK]",
                    Core.Models.FileVerificationStatus.Altered => "[ALTERADO]",
                    Core.Models.FileVerificationStatus.Missing => "[FALTANTE]",
                    Core.Models.FileVerificationStatus.Extra => "[AÑADIDO]",
                    _ => "[?]"
                };
                sb.AppendLine($"{tag} {entry.EntryName}");
            }

            sb.AppendLine();
            sb.AppendLine($"Resumen: {result.OkCount} OK, {result.AlteredCount} alterados, " +
                          $"{result.MissingCount} faltantes, {result.ExtraCount} añadidos.");
            if (result.ZipHashMatches.HasValue)
            {
                sb.AppendLine($"Hash global del ZIP: {(result.ZipHashMatches.Value ? "coincide" : "NO coincide")}.");
            }

            if (result.Signature is { Present: true } sig)
            {
                sb.AppendLine($"Firma digital: {(sig.Valid ? "VÁLIDA" : "INVÁLIDA")}");
                if (!string.IsNullOrEmpty(sig.SignerSubject))
                {
                    sb.AppendLine($"  Firmante: {sig.SignerSubject}");
                }
                if (sig.SignedAtUtc.HasValue)
                {
                    sb.AppendLine($"  Fecha de firma (UTC): {sig.SignedAtUtc.Value:yyyy-MM-ddTHH:mm:ssZ}");
                }
            }

            DetailsText = sb.ToString();
            Status = result.IsIntact ? VerificationStatus.Valid : VerificationStatus.Invalid;
        }
        catch (Exception ex)
        {
            Status = VerificationStatus.BadFormat;
            DetailsText = $"Error al verificar la evidencia: {ex.Message}";
        }
    }
}
