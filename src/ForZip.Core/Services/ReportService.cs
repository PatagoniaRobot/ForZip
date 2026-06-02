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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

public class ReportService : IReportService
{
    private const string NewLine = "\r\n";
    private const int SeparatorWidth = 80;
    private const int LabelWidth = 13;
    private const int NumColumnWidth = 4;
    private const int FileColumnWidth = 41;
    private const int SizeColumnWidth = 13;

    // Sufijo del archivo de hash externo que acompaña al informe (cadena de integridad)
    private const string Sha256Suffix = ".sha256";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashAlgorithmType[] AlgorithmDisplayOrder =
    {
        HashAlgorithmType.MD5,
        HashAlgorithmType.SHA1,
        HashAlgorithmType.SHA256,
        HashAlgorithmType.SHA512
    };

    private readonly ILocalizationService _localization;

    public ReportService(ILocalizationService localization)
    {
        _localization = localization;
    }

    public string GenerateReport(ReportData data, string language)
    {
        // Cambio temporal de idioma para construir todas las cadenas en el idioma del informe
        var previousLanguage = _localization.CurrentLanguage;
        try
        {
            _localization.SetLanguage(language);
            return BuildReport(data, language);
        }
        finally
        {
            _localization.SetLanguage(previousLanguage);
        }
    }

    public async Task SaveReportAsync(string content, string outputPath)
    {
        // UTF-8 con BOM (3 bytes 0xEF 0xBB 0xBF) requerido por el formato forense
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(outputPath, content, encoding);
    }

    public (bool isValid, string details) VerifyReport(string reportPath)
    {
        if (!File.Exists(reportPath))
        {
            return (false, _localization.Get("verify_report_not_found"));
        }

        var sidecarPath = reportPath + Sha256Suffix;
        if (!File.Exists(sidecarPath))
        {
            return (false, _localization.Get("verify_no_sidecar"));
        }

        string sidecarContent;
        try
        {
            sidecarContent = File.ReadAllText(sidecarPath);
        }
        catch (IOException ex)
        {
            return (false, $"{_localization.Get("verify_sidecar_read_error")} {ex.Message}");
        }

        var expected = ExtractSha256(sidecarContent);
        if (expected == null)
        {
            return (false, _localization.Get("verify_sidecar_bad_format"));
        }

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant();

        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return (true, _localization.Get("verify_valid"));
        }

        var template = _localization.Get("verify_hash_mismatch");
        return (false, string.Format(CultureInfo.InvariantCulture, template, expected, actual));
    }

    public string GenerateManifestJson(ReportData data)
    {
        var manifest = new ForensicManifest
        {
            ForZipVersion = data.ForZipVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Operator = data.Operator,
            CaseNumber = data.CaseNumber,
            CaseDescription = data.CaseDescription,
            Court = data.Court,
            Operation = data.Operation,
            CompressionLevel = data.CompressionLevel,
            HasPassword = data.HasPassword,
            Algorithms = AlgorithmDisplayOrder.Where(data.Algorithms.Contains).ToList(),
            ZipFileName = data.ZipFilePath != null ? Path.GetFileName(data.ZipFilePath) : null,
            ZipFileSize = data.ZipFileSize,
            ZipSha256 = data.ZipHash,
            Files = data.FileResults.Select(f => new ManifestFileEntry
            {
                EntryName = f.FilePath,
                SourcePath = f.SourcePath,
                Size = f.FileSize,
                ModifiedUtc = f.ModifiedUtc,
                Hashes = new Dictionary<HashAlgorithmType, string>(f.Hashes)
            }).ToList()
        };

        return JsonSerializer.Serialize(manifest, ManifestJsonOptions);
    }

    /// <summary>Extrae el primer hash SHA-256 (64 hex) que aparezca en el texto, en minúsculas.</summary>
    private static string? ExtractSha256(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (var token in content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 64 && token.All(Uri.IsHexDigit))
            {
                return token.ToLowerInvariant();
            }
        }

        return null;
    }

    private string BuildReport(ReportData data, string language)
    {
        var culture = GetCulture(language);
        var sb = new StringBuilder();

        AppendHeader(sb, data);
        AppendOperatorSection(sb, data);
        AppendCaseSection(sb, data);
        AppendEnvironmentSection(sb, data);
        AppendOperationParametersSection(sb, data);
        AppendFilesSection(sb, data, culture);
        AppendFileMetadataSection(sb, data);

        if (data.Operation == OperationType.Compression && data.ZipFilePath != null)
        {
            AppendZipHashSection(sb, data, culture);
        }

        AppendDisclaimer(sb);

        return sb.ToString();
    }

    private void AppendHeader(StringBuilder sb, ReportData data)
    {
        sb.Append('=', SeparatorWidth).Append(NewLine);

        var titleTemplate = _localization.Get("report_title");
        sb.Append("  ").Append(string.Format(CultureInfo.InvariantCulture, titleTemplate, data.ForZipVersion)).Append(NewLine);
        sb.Append("  ").Append(_localization.Get("report_subtitle")).Append(NewLine);
        sb.Append("  https://github.com/patagoniarobot/forzip").Append(NewLine);

        sb.Append('=', SeparatorWidth).Append(NewLine);
        sb.Append(NewLine);
    }

    private void AppendOperatorSection(StringBuilder sb, ReportData data)
    {
        var op = data.Operator;
        if (op == null)
        {
            return;
        }

        // Si todos los campos están vacíos, omitimos la sección entera
        var fields = new (string key, string? value)[]
        {
            ("report_op_name", op.Name),
            ("report_op_title", op.Title),
            ("report_op_org", op.Organization),
            ("report_op_email", op.Email),
            ("report_op_phone", op.Phone)
        };

        if (fields.All(f => string.IsNullOrWhiteSpace(f.value)))
        {
            return;
        }

        sb.Append(_localization.Get("report_operator")).Append(NewLine);
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendField(sb, _localization.Get(key), value);
            }
        }
        sb.Append(NewLine);
    }

    private void AppendCaseSection(StringBuilder sb, ReportData data)
    {
        var fields = new (string key, string? value)[]
        {
            ("report_case_number", data.CaseNumber),
            ("report_case_desc", data.CaseDescription),
            ("report_case_court", data.Court)
        };

        if (fields.All(f => string.IsNullOrWhiteSpace(f.value)))
        {
            return;
        }

        sb.Append(_localization.Get("report_case")).Append(NewLine);
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendField(sb, _localization.Get(key), value);
            }
        }
        sb.Append(NewLine);
    }

    private void AppendEnvironmentSection(StringBuilder sb, ReportData data)
    {
        sb.Append(_localization.Get("report_env")).Append(NewLine);

        AppendField(sb, _localization.Get("report_env_datetime"), DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        AppendField(sb, _localization.Get("report_env_os"), Environment.OSVersion.VersionString);
        AppendField(sb, _localization.Get("report_env_hostname"), Environment.MachineName);
        AppendField(sb, _localization.Get("report_env_user"), Environment.UserName);
        AppendField(sb, _localization.Get("report_env_version"), $"ForZip {data.ForZipVersion}");

        sb.Append(NewLine);
    }

    private void AppendOperationParametersSection(StringBuilder sb, ReportData data)
    {
        sb.Append(_localization.Get("report_params")).Append(NewLine);

        var operationLabel = data.Operation == OperationType.Compression
            ? _localization.Get("report_op_compression")
            : _localization.Get("report_op_hash_batch");
        AppendField(sb, _localization.Get("report_param_operation"), operationLabel);

        if (data.Operation == OperationType.Compression)
        {
            var levelLabel = $"{data.CompressionLevel} ({_localization.Get($"report_level_{data.CompressionLevel}")})";
            AppendField(sb, _localization.Get("report_param_level"), levelLabel);

            var encryptionLabel = data.HasPassword
                ? _localization.Get("report_encryption_aes")
                : _localization.Get("report_encryption_none");
            AppendField(sb, _localization.Get("report_param_encryption"), encryptionLabel);
        }

        if (data.Algorithms.Count > 0)
        {
            var algos = string.Join(", ", AlgorithmDisplayOrder
                .Where(data.Algorithms.Contains)
                .Select(GetAlgorithmDisplayName));
            AppendField(sb, _localization.Get("report_param_algorithms"), algos);
        }

        if (data.Operation == OperationType.Compression && !string.IsNullOrEmpty(data.ZipFilePath))
        {
            AppendField(sb, _localization.Get("report_param_zipfile"), data.ZipFilePath!);
        }

        sb.Append(NewLine);
    }

    private void AppendFilesSection(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        var sectionHeader = string.Format(
            CultureInfo.InvariantCulture,
            _localization.Get("report_files"),
            data.FileResults.Count);
        sb.Append(sectionHeader).Append(NewLine);
        sb.Append(NewLine);

        // Una tabla por algoritmo, en orden canónico (MD5 → SHA-1 → SHA-256 → SHA-512)
        foreach (var algo in AlgorithmDisplayOrder)
        {
            if (!data.Algorithms.Contains(algo))
            {
                continue;
            }

            AppendFileTable(sb, data.FileResults, algo, culture);
            sb.Append(NewLine);
        }

        var totalBytes = data.FileResults.Sum(f => f.FileSize);
        AppendField(sb, _localization.Get("total_files"), data.FileResults.Count.ToString(culture));
        AppendField(sb, _localization.Get("total_size"), FormatBytesWithUnit(totalBytes, culture));
        sb.Append(NewLine);
    }

    private void AppendFileTable(StringBuilder sb, List<HashResult> files, HashAlgorithmType algorithm, CultureInfo culture)
    {
        var hashWidth = GetHashHexWidth(algorithm);
        var algoName = GetAlgorithmDisplayName(algorithm);

        // Encabezado de tabla
        sb.Append("  ");
        sb.Append(_localization.Get("report_hash_col_no").PadRight(NumColumnWidth));
        sb.Append("  ");
        sb.Append(_localization.Get("report_file_col").PadRight(FileColumnWidth));
        sb.Append("  ");
        sb.Append(_localization.Get("report_size_col").PadRight(SizeColumnWidth));
        sb.Append("  ");
        sb.Append(algoName);
        sb.Append(NewLine);

        // Línea de guiones
        sb.Append("  ");
        sb.Append(new string('-', NumColumnWidth));
        sb.Append("  ");
        sb.Append(new string('-', FileColumnWidth));
        sb.Append("  ");
        sb.Append(new string('-', SizeColumnWidth));
        sb.Append("  ");
        sb.Append(new string('-', hashWidth));
        sb.Append(NewLine);

        // Filas de datos
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var hash = file.Hashes.TryGetValue(algorithm, out var h) ? h : string.Empty;
            var sizeText = $"{file.FileSize.ToString("N0", culture)} B";

            sb.Append("  ");
            sb.Append((i + 1).ToString(culture).PadLeft(NumColumnWidth));
            sb.Append("  ");
            sb.Append((file.FilePath ?? string.Empty).PadRight(FileColumnWidth));
            sb.Append("  ");
            sb.Append(sizeText.PadLeft(SizeColumnWidth));
            sb.Append("  ");
            sb.Append(hash);
            sb.Append(NewLine);
        }
    }

    private void AppendFileMetadataSection(StringBuilder sb, ReportData data)
    {
        // Solo si hay metadatos de cadena de custodia que documentar
        var hasMetadata = data.FileResults.Any(
            f => !string.IsNullOrEmpty(f.SourcePath) || f.ModifiedUtc.HasValue);
        if (!hasMetadata)
        {
            return;
        }

        sb.Append(_localization.Get("report_sources")).Append(NewLine);

        for (int i = 0; i < data.FileResults.Count; i++)
        {
            var file = data.FileResults[i];
            sb.Append("  [").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("] ")
              .Append(file.FilePath).Append(NewLine);

            if (!string.IsNullOrEmpty(file.SourcePath))
            {
                AppendField(sb, "  " + _localization.Get("report_src_origin"), file.SourcePath!);
            }

            if (file.ModifiedUtc.HasValue)
            {
                var modified = file.ModifiedUtc.Value.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                AppendField(sb, "  " + _localization.Get("report_src_modified"), modified);
            }
        }

        sb.Append(NewLine);
    }

    private void AppendZipHashSection(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append(_localization.Get("report_zip_hash")).Append(NewLine);

        var zipName = Path.GetFileName(data.ZipFilePath ?? string.Empty);
        AppendField(sb, _localization.Get("report_global_file"), zipName);

        if (data.ZipFileSize.HasValue)
        {
            AppendField(sb, _localization.Get("report_global_size"), FormatBytesWithUnit(data.ZipFileSize.Value, culture));
        }

        if (!string.IsNullOrEmpty(data.ZipHash))
        {
            AppendField(sb, "SHA-256", data.ZipHash!);
        }

        sb.Append(NewLine);
    }

    private void AppendDisclaimer(StringBuilder sb)
    {
        sb.Append('=', SeparatorWidth).Append(NewLine);
        sb.Append("  ").Append(_localization.Get("report_disclaimer_title")).Append(NewLine);

        var disclaimerLines = _localization.Get("report_disclaimer_text").Split('\n');
        foreach (var line in disclaimerLines)
        {
            var cleanLine = line.TrimEnd('\r');
            sb.Append("  ").Append(cleanLine).Append(NewLine);
        }

        sb.Append('=', SeparatorWidth).Append(NewLine);
        sb.Append(NewLine);
    }

    private static void AppendField(StringBuilder sb, string label, string value)
    {
        sb.Append("  ").Append(label.PadRight(LabelWidth)).Append(": ").Append(value).Append(NewLine);
    }

    private static CultureInfo GetCulture(string language)
    {
        // es-AR usa punto como separador de miles y coma como decimal (formato latinoamericano)
        return language == "es"
            ? CultureInfo.GetCultureInfo("es-AR")
            : CultureInfo.GetCultureInfo("en-US");
    }

    private static string FormatBytesWithUnit(long bytes, CultureInfo culture)
    {
        var bytesPart = $"{bytes.ToString("N0", culture)} B";
        if (bytes < 1024L * 1024)
        {
            return bytesPart;
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            var mb = bytes / 1048576.0;
            return $"{bytesPart} ({mb.ToString("N2", culture)} MB)";
        }

        var gb = bytes / (1024.0 * 1024 * 1024);
        return $"{bytesPart} ({gb.ToString("N2", culture)} GB)";
    }

    private static string GetAlgorithmDisplayName(HashAlgorithmType algorithm)
    {
        return algorithm switch
        {
            HashAlgorithmType.MD5 => "MD5",
            HashAlgorithmType.SHA1 => "SHA-1",
            HashAlgorithmType.SHA256 => "SHA-256",
            HashAlgorithmType.SHA512 => "SHA-512",
            _ => algorithm.ToString()
        };
    }

    private static int GetHashHexWidth(HashAlgorithmType algorithm)
    {
        return algorithm switch
        {
            HashAlgorithmType.MD5 => 32,
            HashAlgorithmType.SHA1 => 40,
            HashAlgorithmType.SHA256 => 64,
            HashAlgorithmType.SHA512 => 128,
            _ => 64
        };
    }
}
