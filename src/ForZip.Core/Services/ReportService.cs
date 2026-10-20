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

public partial class ReportService : IReportService
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

    private static readonly HashAlgorithmType[] AlgorithmDisplayOrder = HashAlgorithmNames.DisplayOrder;

    private readonly ILocalizationService _localization;

    public ReportService(ILocalizationService localization)
    {
        _localization = localization;
    }

    public string GenerateReport(ReportData data, string language)
    {
        // Los textos se piden en el idioma del informe sin tocar el de la aplicación. Antes se cambiaba el idioma
        // global y se restauraba al final: cada cambio avisaba a todas las vistas, y en Empaquetar el informe se
        // arma en un hilo de fondo, así que el aviso llegaba a la interfaz desde fuera de su hilo.
        var previous = _reportLanguage.Value;
        try
        {
            _reportLanguage.Value = language;
            return BuildReport(data, language);
        }
        finally
        {
            _reportLanguage.Value = previous;
        }
    }

    /// <summary>Idioma del informe que se está armando en este flujo (null fuera de GenerateReport).</summary>
    private readonly AsyncLocal<string?> _reportLanguage = new();

    /// <summary>Texto en el idioma del informe en curso, o en el de la aplicación si no hay informe en curso.</summary>
    private string L(string key) => _reportLanguage.Value is { } language
        ? _localization.Get(key, language)
        : _localization.Get(key);

    public async Task SaveReportAsync(string content, string outputPath)
    {
        // UTF-8 con BOM (3 bytes 0xEF 0xBB 0xBF) requerido por el formato forense
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(outputPath, content, encoding);
    }

    public (bool isValid, string details) VerifyReport(string reportPath)
    {
        var (status, details) = CheckReport(reportPath);
        return (status == ReportIntegrity.Valid, details);
    }

    public (ReportIntegrity status, string details) CheckReport(string reportPath)
    {
        if (!File.Exists(reportPath))
        {
            return (ReportIntegrity.Unverifiable, L("verify_report_not_found"));
        }

        var sidecarPath = reportPath + Sha256Suffix;
        if (!File.Exists(sidecarPath))
        {
            return (ReportIntegrity.Unverifiable, L("verify_no_sidecar"));
        }

        string sidecarContent;
        try
        {
            sidecarContent = File.ReadAllText(sidecarPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (ReportIntegrity.Unverifiable, $"{L("verify_sidecar_read_error")} {ex.Message}");
        }

        var expected = ExtractSha256(sidecarContent);
        if (expected == null)
        {
            return (ReportIntegrity.Unverifiable, L("verify_sidecar_bad_format"));
        }

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant();

        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return (ReportIntegrity.Valid, L("verify_valid"));
        }

        var template = L("verify_hash_mismatch");
        return (ReportIntegrity.Altered, string.Format(CultureInfo.InvariantCulture, template, expected, actual));
    }

    public string GenerateManifestJson(ReportData data)
    {
        var manifest = new ForensicManifest
        {
            // 1.2 agrega OmittedFiles y 1.3 ArchiveFormat. Un ZIP completo sigue declarando 1.1 y sale
            // idéntico al de versiones anteriores; solo lo que usa un campo nuevo declara su esquema.
            // 1.4 agrega Environment (equipo, sistema, usuario y hora del empaquetado), que antes solo quedaba en
            // el informe: desde que el informe se arma a pedido, el manifiesto es lo único que lo conserva.
            FormatVersion = data.Environment != null ? "1.4"
                : data.ArchiveFormat == ArchiveFormat.SevenZ ? "1.3"
                : data.OmittedFiles.Count > 0 ? "1.2" : "1.1",
            ArchiveFormat = data.ArchiveFormat == ArchiveFormat.SevenZ ? "7z" : null,
            ForZipVersion = data.ForZipVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Operator = data.Operator,
            CaseNumber = data.CaseNumber,
            CaseDescription = data.CaseDescription,
            Court = data.Court,
            Environment = data.Environment,
            Operation = data.Operation,
            CompressionLevel = data.CompressionLevel,
            HasPassword = data.HasPassword,
            Algorithms = AlgorithmDisplayOrder.Where(data.Algorithms.Contains).ToList(),
            ZipFileName = data.ZipFilePath != null ? Path.GetFileName(data.ZipFilePath) : null,
            ZipFileSize = data.ZipFileSize,
            ZipSha256 = data.ZipHash,
            IsSplit = data.Volumes.Count > 0,
            Volumes = data.Volumes.Count > 0
                ? data.Volumes.Select(v => new ManifestVolume
                {
                    FileName = v.FileName,
                    Size = v.Size,
                    Sha256 = v.Sha256
                }).ToList()
                : null,
            Files = data.FileResults.Select(f => new ManifestFileEntry
            {
                EntryName = f.FilePath,
                SourcePath = f.SourcePath,
                Size = f.FileSize,
                ModifiedUtc = f.ModifiedUtc,
                Hashes = new Dictionary<HashAlgorithmType, string>(f.Hashes)
            }).ToList(),
            OmittedFiles = data.OmittedFiles.Count > 0
                ? data.OmittedFiles.Select(o => new ManifestOmittedFile
                {
                    EntryName = o.EntryName,
                    SourcePath = o.SourcePath,
                    Size = o.Size,
                    ModifiedUtc = o.ModifiedUtc,
                    Reason = ManifestReason(o.Reason),
                    WindowsErrorCode = o.WindowsErrorCode,
                    SystemMessage = o.SystemMessage,
                    LinkTarget = o.LinkTarget,
                    SeenAtUtc = o.SeenAtUtc,
                    RecordedAtUtc = o.RecordedAtUtc
                }).ToList()
                : null
        };

        return JsonSerializer.Serialize(manifest, ManifestJsonOptions);
    }

    public ReportData FromManifest(ForensicManifest manifest, string manifestFileName) => new()
    {
        Operator = manifest.Operator,
        CaseNumber = manifest.CaseNumber,
        CaseDescription = manifest.CaseDescription,
        Court = manifest.Court,
        Operation = manifest.Operation,
        CompressionLevel = manifest.CompressionLevel,
        HasPassword = manifest.HasPassword,
        Algorithms = manifest.Algorithms.ToHashSet(),
        ZipFilePath = manifest.ZipFileName,
        ArchiveFormat = string.Equals(manifest.ArchiveFormat, "7z", StringComparison.OrdinalIgnoreCase)
            ? ArchiveFormat.SevenZ : ArchiveFormat.Zip,
        ZipFileSize = manifest.ZipFileSize,
        ZipHash = manifest.ZipSha256,
        FileResults = manifest.Files.Select(f => new HashResult
        {
            FilePath = f.EntryName,
            SourcePath = f.SourcePath,
            FileSize = f.Size,
            ModifiedUtc = f.ModifiedUtc,
            Hashes = new Dictionary<HashAlgorithmType, string>(f.Hashes)
        }).ToList(),
        Volumes = manifest.Volumes?.Select(v => new VolumeInfo { FileName = v.FileName, Size = v.Size, Sha256 = v.Sha256 })
            .ToList() ?? new List<VolumeInfo>(),
        OmittedFiles = manifest.OmittedFiles?.Select(o => new OmittedFile
        {
            Reason = ReasonFromManifest(o.Reason),
            EntryName = o.EntryName,
            SourcePath = o.SourcePath,
            Size = o.Size,
            ModifiedUtc = o.ModifiedUtc,
            WindowsErrorCode = o.WindowsErrorCode,
            SystemMessage = o.SystemMessage,
            LinkTarget = o.LinkTarget,
            SeenAtUtc = o.SeenAtUtc,
            RecordedAtUtc = o.RecordedAtUtc
        }).ToList() ?? new List<OmittedFile>(),
        ForZipVersion = manifest.ForZipVersion,
        // El del manifiesto, nunca el de esta PC: si es anterior al 1.4 queda null y el informe lo dice.
        Environment = manifest.Environment,
        SourceManifestName = manifestFileName
    };

    /// <summary>Extrae el primer hash SHA-256 (64 hex) que aparezca en el texto, en minúsculas.</summary>
    private static string? ExtractSha256(string content) => Sha256Sidecar.Extract(content);

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

        if (data.FolderDigests.Count > 0)
        {
            AppendFolderDigestsSection(sb, data, culture);
        }

        if (data.OmittedFiles.Count > 0)
        {
            AppendOmittedFilesSection(sb, data, culture);
        }

        if (data.Operation == OperationType.Compression && data.ZipFilePath != null)
        {
            AppendZipHashSection(sb, data, culture);
        }

        if (data.Volumes.Count > 0)
        {
            AppendVolumesSection(sb, data, culture);
        }

        if (data.Verification != null)
        {
            AppendVerificationSection(sb, data.Verification);
        }

        AppendDisclaimer(sb);

        return sb.ToString();
    }

    private void AppendHeader(StringBuilder sb, ReportData data)
    {
        sb.Append('=', SeparatorWidth).Append(NewLine);

        var titleTemplate = L("report_title");
        sb.Append("  ").Append(string.Format(CultureInfo.InvariantCulture, titleTemplate, data.ForZipVersion)).Append(NewLine);
        sb.Append("  ").Append(L("report_subtitle")).Append(NewLine);
        sb.Append("  https://github.com/patagoniarobot/forzip").Append(NewLine);

        sb.Append('=', SeparatorWidth).Append(NewLine);
        sb.Append(NewLine);

        // Armado a pedido: el papel dice de dónde salió y cuándo, para que nadie lo tome por un archivo del
        // empaquetado. La evidencia es el paquete con su manifiesto, no este texto.
        if (data.SourceManifestName != null)
        {
            sb.Append(L("report_from_manifest")).Append(NewLine);
            AppendField(sb, L("report_from_manifest_file"), data.SourceManifestName);
            AppendField(sb, L("report_from_manifest_at"), FormatLocal(DateTimeOffset.Now));
            AppendWrapped(sb, L("report_from_manifest_note"));
            sb.Append(NewLine);
        }
    }

    private void AppendVerificationSection(StringBuilder sb, ReportVerification verification)
    {
        sb.Append(L("report_verification")).Append(NewLine);
        AppendField(sb, L("report_verification_at"), FormatLocal(verification.VerifiedAt));
        if (!string.IsNullOrEmpty(verification.MachineName))
        {
            AppendField(sb, L("report_env_hostname"), verification.MachineName!);
        }

        if (!string.IsNullOrEmpty(verification.UserName))
        {
            AppendField(sb, L("report_env_user"), verification.UserName!);
        }

        AppendField(sb, L("report_verification_verdict"), verification.Verdict);
        if (!string.IsNullOrWhiteSpace(verification.Details))
        {
            sb.Append(NewLine);
            AppendWrapped(sb, verification.Details!);
        }

        sb.Append(NewLine);
    }

    /// <summary>Texto suelto con sangría, línea por línea (los saltos que trae se respetan).</summary>
    private static void AppendWrapped(StringBuilder sb, string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            sb.Append("  ").Append(line.TrimEnd()).Append(NewLine);
        }
    }

    private static string FormatLocal(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

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

        sb.Append(L("report_operator")).Append(NewLine);
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendField(sb, L(key), value);
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

        sb.Append(L("report_case")).Append(NewLine);
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendField(sb, L(key), value);
            }
        }
        sb.Append(NewLine);
    }

    private void AppendEnvironmentSection(StringBuilder sb, ReportData data)
    {
        sb.Append(L("report_env")).Append(NewLine);

        var env = data.Environment;
        if (env == null)
        {
            // Manifiesto anterior al 1.4: el entorno no se registró. Poner el de esta PC sería atribuirle el
            // empaquetado a un equipo que quizás nunca lo tuvo.
            sb.Append("  ").Append(L("report_env_unrecorded")).Append(NewLine);
        }
        else
        {
            AppendField(sb, L("report_env_datetime"), FormatLocal(env.LocalTime));
            AppendField(sb, L("report_env_os"), env.OsVersion ?? string.Empty);
            AppendField(sb, L("report_env_hostname"), env.MachineName ?? string.Empty);
            AppendField(sb, L("report_env_user"), env.UserName ?? string.Empty);
        }

        AppendField(sb, L("report_env_version"), $"ForZip {data.ForZipVersion}");

        sb.Append(NewLine);
    }

    private void AppendOperationParametersSection(StringBuilder sb, ReportData data)
    {
        sb.Append(L("report_params")).Append(NewLine);

        var operationLabel = data.Operation == OperationType.Compression
            ? L("report_op_compression")
            : L("report_op_hash_batch");
        AppendField(sb, L("report_param_operation"), operationLabel);

        if (data.Operation == OperationType.Compression)
        {
            var levelLabel = $"{data.CompressionLevel} ({L($"report_level_{data.CompressionLevel}")})";
            AppendField(sb, L("report_param_level"), levelLabel);

            var encryptionLabel = data.HasPassword
                ? L(Is7z(data) ? "report_encryption_aes_7z" : "report_encryption_aes")
                : L("report_encryption_none");
            AppendField(sb, L("report_param_encryption"), encryptionLabel);

            // Solo en un 7z: el informe de un ZIP sale idéntico al de siempre.
            if (Is7z(data))
            {
                AppendField(sb, L("report_param_format"), L("report_format_7z"));
            }
        }

        if (data.Algorithms.Count > 0)
        {
            var algos = string.Join(", ", AlgorithmDisplayOrder
                .Where(data.Algorithms.Contains)
                .Select(GetAlgorithmDisplayName));
            AppendField(sb, L("report_param_algorithms"), algos);
        }

        if (data.Operation == OperationType.Compression && !string.IsNullOrEmpty(data.ZipFilePath))
        {
            AppendField(sb, L(Is7z(data) ? "report_param_zipfile_7z" : "report_param_zipfile"), data.ZipFilePath!);
        }

        // Arriba, donde se lee primero: quien recibe el paquete tiene que saber de entrada que
        // no contiene toda la evidencia seleccionada, sin tener que llegar al final del informe.
        if (data.OmittedFiles.Count > 0)
        {
            AppendField(sb, L("report_param_completeness"),
                string.Format(CultureInfo.InvariantCulture,
                    L("report_completeness_incomplete"), data.OmittedFiles.Count));
        }

        sb.Append(NewLine);
    }

    /// <summary>
    /// Documenta los archivos que el software de seguridad no dejó leer y el fundamento de su
    /// omisión. No afirma nada sobre el resto del paquete: el antivirus solo bloquea lo que
    /// reconoce, y decir que lo demás está limpio sería una afirmación que ForZip no puede sostener.
    /// </summary>
    private void AppendOmittedFilesSection(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append(L("report_omitted")).Append(NewLine);
        sb.Append(NewLine);
        AppendField(sb, L("report_omitted_count"),
            data.OmittedFiles.Count.ToString(culture));
        sb.Append(NewLine);

        for (int i = 0; i < data.OmittedFiles.Count; i++)
        {
            var file = data.OmittedFiles[i];
            sb.Append("  [").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("] ")
              .Append(file.EntryName).Append(NewLine);
            AppendField(sb, "  " + L("report_src_origin"), file.SourcePath);
            AppendField(sb, "  " + L("report_global_size"), FormatBytesWithUnit(file.Size, culture));
            AppendField(sb, "  " + L("report_src_modified"),
                file.ModifiedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            AppendField(sb, "  " + L("report_omitted_reason"),
                L(ReasonKey(file.Reason)));
            if (file.SeenAtUtc.HasValue)
            {
                AppendField(sb, "  " + L("report_omitted_seen"), FormatUtc(file.SeenAtUtc.Value));
            }

            if (file.WindowsErrorCode.HasValue)
            {
                AppendField(sb, "  " + L("report_omitted_code"),
                    file.WindowsErrorCode.Value.ToString(CultureInfo.InvariantCulture) + " (" +
                    WindowsErrorName(file.WindowsErrorCode.Value) + ")");
            }

            if (!string.IsNullOrEmpty(file.SystemMessage))
            {
                AppendField(sb, "  " + L("report_omitted_message"), file.SystemMessage);
            }

            if (!string.IsNullOrEmpty(file.LinkTarget))
            {
                AppendField(sb, "  " + L("report_omitted_link_target"), file.LinkTarget);
            }

            AppendField(sb, "  " + L("report_omitted_time"), FormatUtc(file.RecordedAtUtc));
        }

        // El fundamento se arma con los párrafos de los motivos que efectivamente aparecen, y
        // cierra siempre con la base normativa y el alcance de lo que la sección afirma.
        sb.Append(NewLine);
        sb.Append("  ").Append(L("report_omitted_basis_title")).Append(NewLine);
        var paragraphs = OmissionBasisKeys(data);
        for (int p = 0; p < paragraphs.Count; p++)
        {
            if (p > 0)
            {
                sb.Append(NewLine);
            }

            foreach (var line in L(paragraphs[p]).Split('\n'))
            {
                sb.Append("  ").Append(line.TrimEnd('\r')).Append(NewLine);
            }
        }

        sb.Append(NewLine);
    }

    /// <summary>
    /// Los párrafos del fundamento de las omisiones: uno por cada motivo que aparece, y siempre al final la base
    /// normativa y el alcance de lo que la sección afirma.
    /// </summary>
    private static List<string> OmissionBasisKeys(ReportData data)
    {
        var paragraphs = new List<string>();
        if (data.OmittedFiles.Any(f => f.Reason == OmissionReason.SecuritySoftwareBlocked))
        {
            paragraphs.Add("report_omitted_basis_blocked");
        }

        if (data.OmittedFiles.Any(f => f.Reason is OmissionReason.MissingAtPackaging or OmissionReason.VanishedDuringPackaging))
        {
            paragraphs.Add("report_omitted_basis_missing");
        }

        if (data.OmittedFiles.Any(f => f.Reason == OmissionReason.LinkNotFollowed))
        {
            paragraphs.Add("report_omitted_basis_link");
        }

        if (data.OmittedFiles.Any(f => f.Reason == OmissionReason.Unreadable))
        {
            paragraphs.Add("report_omitted_basis_unreadable");
        }

        if (data.OmittedFiles.Any(f => f.Reason == OmissionReason.FolderUnreadable))
        {
            paragraphs.Add("report_omitted_basis_folder");
        }

        paragraphs.Add("report_omitted_basis_common");
        return paragraphs;
    }

    private static string ReasonKey(OmissionReason reason) => reason switch
    {
        OmissionReason.SecuritySoftwareBlocked => "reason_security_blocked",
        OmissionReason.MissingAtPackaging => "reason_missing_at_packaging",
        OmissionReason.LinkNotFollowed => "reason_link_not_followed",
        OmissionReason.Unreadable => "reason_unreadable",
        OmissionReason.FolderUnreadable => "reason_folder_unreadable",
        _ => "reason_vanished_during_packaging"
    };

    private static string ManifestReason(OmissionReason reason) => reason switch
    {
        OmissionReason.SecuritySoftwareBlocked => "security_software_blocked",
        OmissionReason.MissingAtPackaging => "missing_at_packaging",
        OmissionReason.LinkNotFollowed => "link_not_followed",
        OmissionReason.Unreadable => "unreadable",
        OmissionReason.FolderUnreadable => "folder_unreadable",
        _ => "vanished_during_packaging"
    };

    private static OmissionReason ReasonFromManifest(string reason) => reason switch
    {
        "security_software_blocked" => OmissionReason.SecuritySoftwareBlocked,
        "missing_at_packaging" => OmissionReason.MissingAtPackaging,
        "link_not_followed" => OmissionReason.LinkNotFollowed,
        "unreadable" => OmissionReason.Unreadable,
        "folder_unreadable" => OmissionReason.FolderUnreadable,
        _ => OmissionReason.VanishedDuringPackaging
    };

    private static string WindowsErrorName(int code) => code switch
    {
        SecuritySoftwareInterference.VirusInfectedError => "ERROR_VIRUS_INFECTED",
        SecuritySoftwareInterference.VirusDeletedError => "ERROR_VIRUS_DELETED",
        2 => "ERROR_FILE_NOT_FOUND",
        3 => "ERROR_PATH_NOT_FOUND",
        _ => "Win32"
    };

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private void AppendFilesSection(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        var sectionHeader = string.Format(
            CultureInfo.InvariantCulture,
            L("report_files"),
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
        AppendField(sb, L("total_files"), data.FileResults.Count.ToString(culture));
        AppendField(sb, L("total_size"), FormatBytesWithUnit(totalBytes, culture));
        sb.Append(NewLine);
    }

    private void AppendFileTable(StringBuilder sb, List<HashResult> files, HashAlgorithmType algorithm, CultureInfo culture)
    {
        var hashWidth = GetHashHexWidth(algorithm);
        var algoName = GetAlgorithmDisplayName(algorithm);

        // Encabezado de tabla
        sb.Append("  ");
        sb.Append(L("report_hash_col_no").PadRight(NumColumnWidth));
        sb.Append("  ");
        sb.Append(L("report_file_col").PadRight(FileColumnWidth));
        sb.Append("  ");
        sb.Append(L("report_size_col").PadRight(SizeColumnWidth));
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

            var name = file.FilePath ?? string.Empty;
            sb.Append("  ");
            sb.Append((i + 1).ToString(culture).PadLeft(NumColumnWidth));
            sb.Append("  ");
            if (name.Length > FileColumnWidth)
            {
                // Un nombre largo va completo en su línea (en un informe forense no se recorta) y el tamaño y el
                // hash siguen debajo, en sus columnas. Antes empujaba el resto de la fila y la tabla se desalineaba.
                sb.Append(name).Append(NewLine);
                sb.Append("  ").Append(' ', NumColumnWidth).Append("  ").Append(' ', FileColumnWidth);
            }
            else
            {
                sb.Append(name.PadRight(FileColumnWidth));
            }

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

        sb.Append(L("report_sources")).Append(NewLine);

        for (int i = 0; i < data.FileResults.Count; i++)
        {
            var file = data.FileResults[i];
            sb.Append("  [").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("] ")
              .Append(file.FilePath).Append(NewLine);

            if (!string.IsNullOrEmpty(file.SourcePath))
            {
                AppendField(sb, "  " + L("report_src_origin"), file.SourcePath!);
            }

            if (file.ModifiedUtc.HasValue)
            {
                var modified = file.ModifiedUtc.Value.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                AppendField(sb, "  " + L("report_src_modified"), modified);
            }
        }

        sb.Append(NewLine);
    }

    private static bool Is7z(ReportData data) => data.ArchiveFormat == ArchiveFormat.SevenZ;

    private void AppendZipHashSection(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append(L(Is7z(data) ? "report_zip_hash_7z" : "report_zip_hash")).Append(NewLine);

        var zipName = Path.GetFileName(data.ZipFilePath ?? string.Empty);
        AppendField(sb, L("report_global_file"), zipName);

        if (data.ZipFileSize.HasValue)
        {
            AppendField(sb, L("report_global_size"), FormatBytesWithUnit(data.ZipFileSize.Value, culture));
        }

        if (!string.IsNullOrEmpty(data.ZipHash))
        {
            AppendField(sb, "SHA-256", data.ZipHash!);
        }

        sb.Append(NewLine);
    }

    private void AppendFolderDigestsSection(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append(L("report_folder_hashes")).Append(NewLine);
        for (int i = 0; i < data.FolderDigests.Count; i++)
        {
            var folder = data.FolderDigests[i];
            sb.Append("  [").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("] ")
              .Append(folder.FolderPath).Append(NewLine);
            AppendField(sb, "  " + L("report_folder_files"), folder.FileCount.ToString(culture));
            AppendField(sb, "  " + L("report_global_size"), FormatBytesWithUnit(folder.TotalSize, culture));
            if (folder.IsComplete)
            {
                AppendField(sb, "  " + L("report_folder_forzip"), folder.ForZipSha256!);
                AppendField(sb, "  " + L("report_folder_7zip"), folder.SevenZipDataSha256!);
            }
            else
            {
                sb.Append("    ").Append(string.Format(culture, L("report_folder_incomplete"), folder.UnreadableCount)).Append(NewLine);
            }

            if (folder.LinksNotFollowed > 0)
            {
                sb.Append("    ").Append(string.Format(culture, L("report_folder_links"), folder.LinksNotFollowed)).Append(NewLine);
            }
        }

        sb.Append(NewLine);
        foreach (var line in L("report_folder_note").Split('\n'))
        {
            sb.Append("  ").Append(line.TrimEnd('\r')).Append(NewLine);
        }

        sb.Append(NewLine);
    }

    private void AppendVolumesSection(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append(L("report_volumes")).Append(NewLine);

        AppendField(sb, L("report_volumes_count"),
            data.Volumes.Count.ToString(culture));
        sb.Append(NewLine);

        for (int i = 0; i < data.Volumes.Count; i++)
        {
            var vol = data.Volumes[i];
            sb.Append("  [").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("] ")
              .Append(vol.FileName).Append(NewLine);
            AppendField(sb, "  " + L("report_global_size"),
                FormatBytesWithUnit(vol.Size, culture));
            AppendField(sb, "  SHA-256", vol.Sha256);
        }

        sb.Append(NewLine);
    }

    private void AppendDisclaimer(StringBuilder sb)
    {
        sb.Append('=', SeparatorWidth).Append(NewLine);
        sb.Append("  ").Append(L("report_disclaimer_title")).Append(NewLine);

        var disclaimerLines = L("report_disclaimer_text").Split('\n');
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
        => HashAlgorithmNames.Display(algorithm);

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
