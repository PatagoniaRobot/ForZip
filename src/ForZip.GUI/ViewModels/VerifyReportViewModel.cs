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

        // Si arrastran la firma desacoplada, verificamos el manifiesto que acompaña
        if (ReportFilePath.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase))
        {
            var signedFile = ReportFilePath[..^".p7s".Length];
            if (File.Exists(signedFile))
            {
                ReportFilePath = signedFile;
            }
        }

        Status = VerificationStatus.NotRun;
        DetailsText = _localization.Get("verify_running");

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
                        DetailsText = _localization.Get("verify_external_ok");
                        return;
                    }
                    else
                    {
                        Status = VerificationStatus.Invalid;
                        DetailsText = string.Format(_localization.Get("verify_hash_mismatch_details"), expectedHash, actualHash);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Status = VerificationStatus.BadFormat;
                DetailsText = string.Format(_localization.Get("verify_hash_read_error"), ex.Message);
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

            // Veredicto por volumen (archivos divididos .001/.002/…), si aplica
            if (result.Volumes is { Count: > 0 })
            {
                sb.AppendLine(_localization.Get("verify_volumes_header"));
                foreach (var vol in result.Volumes)
                {
                    sb.AppendLine($"{StatusTag(vol.Status)} {vol.FileName}");
                }
                sb.AppendLine();
            }

            foreach (var entry in result.Entries)
            {
                sb.AppendLine($"{StatusTag(entry.Status)} {entry.EntryName}");
            }

            sb.AppendLine();
            sb.AppendLine(string.Format(_localization.Get("verify_summary"),
                result.OkCount, result.AlteredCount, result.MissingCount, result.ExtraCount));
            if (result.ZipHashMatches.HasValue)
            {
                sb.AppendLine(string.Format(_localization.Get("verify_zip_hash_line"),
                    _localization.Get(result.ZipHashMatches.Value ? "verify_match" : "verify_mismatch")));
            }

            if (result.Signature is { Present: true } sig)
            {
                sb.AppendLine(string.Format(_localization.Get("verify_signature_line"),
                    _localization.Get(sig.Valid ? "verify_sig_valid" : "verify_sig_invalid")));
                if (!string.IsNullOrEmpty(sig.SignerSubject))
                {
                    sb.AppendLine(string.Format(_localization.Get("verify_signer"), sig.SignerSubject));
                }
                if (sig.SignedAtUtc.HasValue)
                {
                    sb.AppendLine(string.Format(_localization.Get("verify_signed_at"),
                        sig.SignedAtUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                }
                if (sig.TimestampUtc.HasValue)
                {
                    sb.AppendLine(string.Format(_localization.Get("verify_ts_line"),
                        sig.TimestampUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        _localization.Get(sig.TimestampValid == true ? "verify_ts_valid" : "verify_ts_invalid")));
                    if (!string.IsNullOrEmpty(sig.TimestampAuthority))
                    {
                        sb.AppendLine(string.Format(_localization.Get("verify_tsa_line"), sig.TimestampAuthority));
                    }
                }
                else if (sig.TimestampValid == false)
                {
                    sb.AppendLine(_localization.Get("verify_ts_corrupt"));
                }
                else
                {
                    sb.AppendLine(_localization.Get("verify_ts_absent"));
                }
            }

            if (result.ContentVerificationError != null)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(_localization.Get("verify_content_error"), result.ContentVerificationError));
                sb.AppendLine(_localization.Get("verify_content_error_hint"));
            }

            DetailsText = sb.ToString();

            // Distinguir manipulación detectada de una verificación que no pudo completarse.
            var tamperDetected = result.AlteredCount > 0 || result.MissingCount > 0 || result.ExtraCount > 0 ||
                                 result.HasVolumeProblems || result.ZipHashMatches == false ||
                                 result.Signature is { Valid: false } ||
                                 result.Signature is { TimestampValid: false };

            Status = result.IsIntact
                ? VerificationStatus.Valid
                : tamperDetected
                    ? VerificationStatus.Invalid
                    : VerificationStatus.BadFormat; // verificación incompleta (sin manipulación detectada)
        }
        catch (Exception ex)
        {
            Status = VerificationStatus.BadFormat;
            DetailsText = string.Format(_localization.Get("verify_archive_error"), ex.Message);
        }
    }

    private string StatusTag(Core.Models.FileVerificationStatus status) => status switch
    {
        Core.Models.FileVerificationStatus.Ok => _localization.Get("verify_tag_ok"),
        Core.Models.FileVerificationStatus.Altered => _localization.Get("verify_tag_altered"),
        Core.Models.FileVerificationStatus.Missing => _localization.Get("verify_tag_missing"),
        Core.Models.FileVerificationStatus.Extra => _localization.Get("verify_tag_extra"),
        _ => "[?]"
    };
}
