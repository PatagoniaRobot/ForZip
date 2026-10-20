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
using ForZip.Core.Sealing;
using ForZip.Core.Services;

namespace ForZip.GUI.ViewModels;

public enum VerificationStatus
{
    NotRun,
    Valid,
    Invalid,
    BadFormat,

    /// <summary>
    /// No se detectó manipulación, pero tampoco se pudo comprobar todo: el contenido no se pudo leer, o un
    /// sobre sin recibo (sin él no se acredita que sea el que se selló).
    /// </summary>
    Inconclusive
}

public partial class VerifyReportViewModel : ObservableObject
{
    private readonly IReportService _reportService;
    private readonly IHashService _hashService;
    private readonly IVerificationService _verificationService;
    private readonly ISealedVerificationService _sealedVerificationService;
    private readonly ILocalizationService _localization;
    private readonly IConfigService _configService;
    private readonly IZipService _zipService;

    [ObservableProperty]
    private string _reportFilePath = string.Empty;

    [ObservableProperty]
    private VerificationStatus _status = VerificationStatus.NotRun;

    [ObservableProperty]
    private string _detailsText = string.Empty;

    /// <summary>
    /// Contraseña del sobre o del paquete. En un sobre, vacía significa verificación pública: el nivel 1 no la
    /// necesita y es el que se lleva al expediente. En un paquete ZIP o 7z cifrado hace falta para releer el
    /// contenido; sin ella la verificación queda incompleta.
    /// </summary>
    [ObservableProperty]
    private string _sealedPassword = string.Empty;

    /// <summary>El archivo cargado es un sobre sellado o su recibo.</summary>
    [ObservableProperty]
    private bool _isSealedInput;

    /// <summary>El archivo cargado es el manifiesto de un paquete (o su firma).</summary>
    [ObservableProperty]
    private bool _isManifestInput;

    /// <summary>
    /// El manifiesto de la última verificación de un paquete, para "Guardar informe…". Solo se ofrece después de
    /// verificar: el informe deja asentado el veredicto, y sin verificar no habría qué asentar.
    /// </summary>
    private ForensicManifest? _verifiedManifest;
    private string? _verifiedManifestName;
    private string? _verifiedManifestPath;
    private DateTimeOffset _verifiedAt;

    [ObservableProperty]
    private bool _canSaveReport;

    /// <summary>Dónde quedó el informe guardado, o por qué no se pudo guardar.</summary>
    [ObservableProperty]
    private string _reportSavedNote = string.Empty;

    /// <summary>
    /// Un archivo comprimido sin manifiesto (ZIP, 7z, RAR, TAR, ISO, un autoextraíble): se prueba contra sus propios
    /// controles. Antes respondía "no hay contra qué verificarlo" y no probaba nada.
    /// </summary>
    [ObservableProperty]
    private bool _isArchiveInput;

    /// <summary>Cómo terminó la última prueba de un archivo comprimido, o null si no se probó ninguno.</summary>
    private ArchiveTestOutcome? _testOutcome;

    /// <summary>La última prueba comprobó que la contraseña es incorrecta (RAR5), no solo que no abre.</summary>
    private bool _passwordVerifiedWrong;

    /// <summary>La prueba no pudo abrir el archivo por la contraseña: la ventana del menú muestra el campo.</summary>
    public bool TestNeedsPassword => _testOutcome is ArchiveTestOutcome.PasswordRequired or ArchiveTestOutcome.WrongPassword;

    private static bool IsTestableArchive(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && ZipService.IdentifyArchive(path) != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>El campo de contraseña tiene sentido: sobre, recibo, manifiesto de un paquete o archivo comprimido.</summary>
    public bool IsPasswordInputVisible => IsSealedInput || IsManifestInput || IsArchiveInput;

    public VerifyReportViewModel(
        IReportService reportService,
        IHashService hashService,
        IVerificationService verificationService,
        ISealedVerificationService sealedVerificationService,
        ILocalizationService localization,
        IConfigService configService,
        IZipService zipService)
    {
        _zipService = zipService;
        _configService = configService;
        _reportService = reportService;
        _hashService = hashService;
        _verificationService = verificationService;
        _sealedVerificationService = sealedVerificationService;
        _localization = localization;

        // Esta vista no hereda de ViewModelBase: sin esto, sus textos quedaban en el idioma con que se abrió.
        _localization.LanguageChanged += () => OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Un sobre y su recibo se reconocen por la extensión, para poder ofrecer el campo de
    /// contraseña solo cuando tiene sentido.
    /// </summary>
    partial void OnReportFilePathChanged(string value)
    {
        // La contraseña es de un paquete: con otro archivo se borra. Quedaba la del paquete anterior y se
        // probaba contra el nuevo sin que el operador la hubiera puesto para él.
        SealedPassword = string.Empty;
        IsPasswordVisible = false;
        ForgetVerifiedManifest();

        IsSealedInput = IsSealedPath(value);
        IsManifestInput = IsManifestPath(value);
        IsArchiveInput = !IsSealedInput && !IsManifestInput && IsTestableArchive(value);
        OnPropertyChanged(nameof(IsPasswordInputVisible));
        OnPropertyChanged(nameof(LabelSealedPassword));
        OnPropertyChanged(nameof(LabelSealedPasswordHint));
        OnPropertyChanged(nameof(LabelBadgeValid));
        OnPropertyChanged(nameof(LabelBadgeInvalid));
    }

    private static bool IsManifestPath(string path) =>
        path.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(ManifestSuffix + ".p7s", StringComparison.OrdinalIgnoreCase);

    private const string ManifestSuffix = ".manifest.json";

    private static bool IsSealedPath(string path) =>
        path.EndsWith(SealedContainerFormat.SealedExtension, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(ReceiptService.ReceiptSuffix, StringComparison.OrdinalIgnoreCase);

    public string LabelDropZone => _localization.Get("drag_drop_report");
    public string LabelSealedPassword => _localization.Get(IsSealedInput ? "sealed_password_label" : "package_password_label");
    public string LabelSealedPasswordHint => _localization.Get(IsSealedInput ? "sealed_password_hint" : "package_password_hint");
    public string LabelVerifyButton => _localization.Get("verify");
    public string Title => _localization.Get("verify").ToUpperInvariant();
    public string LabelInputFile => _localization.Get("verify_input_file");
    public string ButtonBrowse => _localization.Get("unzip_browse");
    public string LabelPickerTitle => _localization.Get("verify_picker_title");
    public string LabelPickerForZip => _localization.Get("verify_picker_forzip");
    public string LabelPickerAll => _localization.Get("picker_all_files");
    public string LabelPickerArchives => _localization.Get("verify_picker_archives");
    public string TooltipTogglePassword => _localization.Get("tooltip_toggle_password");
    public string LabelSaveReport => _localization.Get("save_report");
    public string LabelSaveReportHint => _localization.Get("save_report_verify_hint");
    public string PickerSaveReportTitle => _localization.Get("picker_save_report_title");
    public string PickerReportHtml => _localization.Get("picker_report_html");
    public string PickerReportTxt => _localization.Get("picker_report_txt");

    /// <summary>Nombre sugerido para el informe: el del paquete verificado, terminado en ".informe.html".</summary>
    // Otro nombre que el del empaquetado: es otro documento (lleva el veredicto) y no tiene por qué pisarlo.
    public string SuggestedReportFileName => PackageOutput.FreeFileName(ReportFolder, ReportBaseFileName);

    /// <summary>El nombre del informe sin el "(n)" que le agrega ForZip si ya existe.</summary>
    public string ReportBaseFileName =>
        (_verifiedManifest?.ZipFileName ?? "ForZip") + _localization.Get("report_verify_file_suffix");

    /// <summary>Carpeta del paquete verificado: el diálogo de guardar el informe abre ahí.</summary>
    public string? ReportFolder => Path.GetDirectoryName(_verifiedManifestPath);

    private void ForgetVerifiedManifest()
    {
        _verifiedManifest = null;
        _verifiedManifestName = null;
        _verifiedManifestPath = null;
        CanSaveReport = false;
        ReportSavedNote = string.Empty;
        SavedReportPath = null;
    }

    /// <summary>El último informe guardado: "Abrir carpeta" lo muestra seleccionado en el Explorador.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedReport))]
    private string? _savedReportPath;

    public bool HasSavedReport => !string.IsNullOrEmpty(SavedReportPath);
    public string LabelOpenFolder => _localization.Get("open_folder");
    public string LabelClear => _localization.Get("verify_clear");

    [RelayCommand]
    private void OpenSavedReportFolder()
    {
        if (string.IsNullOrEmpty(SavedReportPath) || !File.Exists(SavedReportPath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SavedReportPath}\"");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ReportSavedNote = string.Format(_localization.Get("error_io"), ex.Message);
        }
    }

    /// <summary>
    /// "Limpiar": la pantalla vuelve a como estaba al abrirla. Sin esto, al elegir otro archivo seguía a la vista el
    /// veredicto del anterior, y se podía tomar por el del nuevo.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        if (VerifyCommand.IsRunning)
        {
            return;
        }

        ReportFilePath = string.Empty;
        ForgetVerifiedManifest();
        _fileMissing = false;
        _passwordMissing = false;
        _testOutcome = null;
        _passwordVerifiedWrong = false;
        OnPropertyChanged(nameof(TestNeedsPassword));
        DetailsText = string.Empty;
        Status = VerificationStatus.NotRun;
    }

    /// <summary>
    /// "Guardar informe…": el informe del paquete verificado, armado desde su manifiesto, con el veredicto y el
    /// detalle de esta verificación al final.
    /// </summary>
    /// <param name="shownFileName">El nombre que propuso el diálogo: si el operador no lo tocó pero cambió de tipo
    /// o de carpeta, el "(n)" se recalcula ahí (ver <see cref="PackageOutput.RetargetFileSuggestion"/>).</param>
    public async Task SaveReportAsync(string path, string? shownFileName = null)
    {
        if (_verifiedManifest == null || _verifiedManifestName == null)
        {
            return;
        }

        if (shownFileName != null && !string.IsNullOrEmpty(ReportFolder))
        {
            path = PackageOutput.RetargetFileSuggestion(path, Path.Combine(ReportFolder, shownFileName),
                Path.Combine(ReportFolder, ReportBaseFileName));
        }

        try
        {
            var data = _reportService.FromManifest(_verifiedManifest, _verifiedManifestName);
            data.Verification = new ReportVerification
            {
                VerifiedAt = _verifiedAt,
                Verdict = CurrentVerdictTitle + " — " + VerdictLead,
                Outcome = Status switch
                {
                    VerificationStatus.Valid => VerificationOutcome.Intact,
                    VerificationStatus.Invalid => VerificationOutcome.Compromised,
                    _ => VerificationOutcome.Incomplete
                },
                Details = DetailsText
            };
            var language = _localization.CurrentLanguage;
            var content = await Task.Run(() => _reportService.GenerateReportFor(path, data, language));
            await _reportService.SaveReportAsync(content, path);
            ReportSavedNote = string.Format(_localization.Get("result_report_saved"), path);
            SavedReportPath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportSavedNote = string.Format(_localization.Get("error_io"), ex.Message);
        }
    }

    /// <summary>El título del veredicto que está en pantalla.</summary>
    private string CurrentVerdictTitle => Status switch
    {
        VerificationStatus.Valid => LabelBadgeValid,
        VerificationStatus.Invalid => LabelBadgeInvalid,
        VerificationStatus.Inconclusive => LabelBadgeInconclusive,
        _ => LabelBadgeBadFormat
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordCharDisplay))]
    private bool _isPasswordVisible;

    public char PasswordCharDisplay => IsPasswordVisible ? '\0' : '•';

    /// <summary>
    /// Lo que se arrastra, se elige o llega del menú contextual se lleva a lo que ForZip sabe verificar: la firma
    /// desacoplada a su manifiesto, y un paquete (o cualquiera de sus volúmenes, no solo el .001) a su
    /// <c>.manifest.json</c>. Sobres, recibos, informes y manifiestos se usan tal cual; lo demás también, y la
    /// verificación responde que no hay contra qué verificarlo.
    /// </summary>
    public static string ResolveVerifyTarget(string path)
    {
        if (path.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase))
        {
            var signedFile = path[..^".p7s".Length];
            if (File.Exists(signedFile))
            {
                return signedFile;
            }
        }

        if (IsSealedPath(path) || IsManifestPath(path) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var manifest = SplitArchive.GetBasePath(path) + ManifestSuffix;
        return File.Exists(manifest) ? manifest : path;
    }

    public bool IsValid => Status == VerificationStatus.Valid;
    public bool IsInvalid => Status == VerificationStatus.Invalid;
    public bool IsBadFormat => Status == VerificationStatus.BadFormat;
    public bool IsInconclusive => Status == VerificationStatus.Inconclusive;
    public bool HasResult => Status != VerificationStatus.NotRun;

    // El cartel nombra lo que se verificó: un informe, la evidencia de un paquete o un sobre (como la CLI).
    public string LabelBadgeValid => _localization.Get(
        IsSealedInput ? "verify_badge_valid_sealed" : IsManifestInput ? "verify_badge_valid_package"
        : IsArchiveInput ? "verify_badge_valid_tested" : "verify_badge_valid");
    public string LabelBadgeInvalid => _localization.Get(
        IsSealedInput ? "verify_badge_invalid_sealed" : IsManifestInput ? "verify_badge_invalid_package"
        : IsArchiveInput ? "verify_badge_invalid_tested" : "verify_badge_invalid");
    public string LabelBadgeBadFormat => _localization.Get(
        _fileMissing ? "verify_badge_not_found" : _passwordMissing ? "verify_badge_password_needed" : "verify_badge_bad_format");

    /// <summary>El último intento fue con una ruta vacía o inexistente: no es un problema de formato.</summary>
    private bool _fileMissing;

    /// <summary>
    /// El manifiesto registra contraseña y no se escribió ninguna: no se verificó nada. Sin esto salía
    /// "verificación incompleta" con un "lo más probable es que falte la contraseña", cuando el manifiesto lo sabe.
    /// </summary>
    private bool _passwordMissing;
    public string LabelBadgeInconclusive => _localization.Get("verify_badge_inconclusive");

    // Pantalla del diseño 2026-10-17: título, y debajo del veredicto una frase que dice qué significa.
    public string Heading => _localization.Get("verify_heading");
    public string Subtitle => _localization.Get("verify_subtitle");
    public string VerdictLead => _localization.Get(Status switch
    {
        // Un archivo probado sin manifiesto: el veredicto dice hasta dónde llega, que no es lo de un paquete.
        VerificationStatus.Valid when _testOutcome != null => "verify_lead_valid_tested",
        VerificationStatus.Invalid when _testOutcome != null => "verify_lead_invalid_tested",
        VerificationStatus.Inconclusive when _testOutcome == ArchiveTestOutcome.NoChecks => "verify_lead_nochecks_tested",
        VerificationStatus.Inconclusive when _testOutcome == ArchiveTestOutcome.WrongPassword && _passwordVerifiedWrong
            => "verify_lead_wrong_password_tested",
        VerificationStatus.Inconclusive when _testOutcome != null => "verify_lead_unreadable_tested",
        VerificationStatus.Valid => "verify_lead_valid",
        VerificationStatus.Invalid => "verify_lead_invalid",
        VerificationStatus.Inconclusive => "verify_lead_inconclusive",
        _ => _fileMissing ? "verify_lead_not_found" : _passwordMissing ? "verify_lead_password_needed" : "verify_lead_bad_format",
    });

    partial void OnStatusChanged(VerificationStatus value)
    {
        OnPropertyChanged(nameof(VerdictLead));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsInvalid));
        OnPropertyChanged(nameof(IsBadFormat));
        OnPropertyChanged(nameof(LabelBadgeBadFormat));
        OnPropertyChanged(nameof(IsInconclusive));
        OnPropertyChanged(nameof(HasResult));
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        _passwordMissing = false;
        _testOutcome = null;
        _passwordVerifiedWrong = false;
        OnPropertyChanged(nameof(TestNeedsPassword));
        // Sin archivo no hay formato que juzgar: antes una ruta mal escrita salía como "FORMATO INVÁLIDO".
        _fileMissing =string.IsNullOrWhiteSpace(ReportFilePath) || !File.Exists(ReportFilePath);
        if (_fileMissing)
        {
            Status = VerificationStatus.NotRun;
            Status = VerificationStatus.BadFormat;
            DetailsText = string.IsNullOrWhiteSpace(ReportFilePath)
                ? _localization.Get("verify_no_file")
                : string.Format(_localization.Get("verify_file_not_found"), ReportFilePath);
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

        // Un paquete con su manifiesto al lado se verifica contra el manifiesto, también si se escribió su ruta a mano
        // (al arrastrarlo ya pasaba): probarlo solo diría menos de lo que se puede decir.
        if (!IsManifestPath(ReportFilePath) && ResolveVerifyTarget(ReportFilePath) is var target
            && IsManifestPath(target) && !string.Equals(target, ReportFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var password = SealedPassword;
            ReportFilePath = target;
            SealedPassword = password;
        }

        Status = VerificationStatus.NotRun;
        DetailsText = _localization.Get("verify_running");
        ForgetVerifiedManifest();

        // Sobre sellado o recibo: verificación de dos niveles, resuelta por la contraseña.
        if (IsSealedPath(ReportFilePath))
        {
            await VerifySealedAsync();
            return;
        }

        // Si el archivo es un manifiesto, verificamos la evidencia (re-hash del ZIP)
        if (ReportFilePath.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            await VerifyArchiveAsync();
            return;
        }

        // Un archivo comprimido sin manifiesto (de terceros, o un paquete que perdió el suyo): se prueba contra sus
        // propios controles, en memoria, como 7z t. Un autoextraíble se lee, nunca se ejecuta.
        if (IsTestableArchive(ReportFilePath))
        {
            await TestArchiveAsync();
            return;
        }

        // Lo que no es paquete, informe, sobre ni archivo comprimido: no hay contra qué verificarlo. Antes caía en la
        // verificación de informe y salía el rojo "NO VERIFICADO", como si estuviera alterado.
        if (!ReportFilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            Status = VerificationStatus.Inconclusive;
            DetailsText = string.Format(_localization.Get("verify_nothing_to_verify"), Path.GetFileName(ReportFilePath));
            return;
        }

        // Informe: contra su .sha256. Sin él (o ilegible) la verificación queda incompleta, no es una alteración.
        var (integrity, details) = _reportService.CheckReport(ReportFilePath);
        DetailsText = details;
        Status = integrity switch
        {
            ReportIntegrity.Valid => VerificationStatus.Valid,
            ReportIntegrity.Altered => VerificationStatus.Invalid,
            _ => VerificationStatus.Inconclusive
        };
    }

    private async Task VerifyArchiveAsync()
    {
        try
        {
            // La contraseña solo hace falta si el paquete se cifró: sin ella no se puede releer el contenido.
            var password = string.IsNullOrEmpty(SealedPassword) ? null : SealedPassword;
            if (password == null && ManifestRecordsPassword(ReportFilePath))
            {
                _passwordMissing = true;
                Status = VerificationStatus.BadFormat;
                DetailsText = _localization.Get("verify_password_needed");
                return;
            }

            var result = await _verificationService.VerifyArchiveAsync(
                ReportFilePath, null, password, null, CancellationToken.None);

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

            // Con volúmenes faltantes el contenido no se leyó: un resumen "0 OK, 0 alterados" diría lo contrario.
            if (result.MissingVolumes.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(_localization.Get("verify_summary"),
                    result.OkCount, result.AlteredCount, result.MissingCount, result.ExtraCount));
            }
            if (result.UnverifiedCount > 0)
            {
                sb.AppendLine(string.Format(_localization.Get("verify_unverified_line"), result.UnverifiedCount));
            }
            if (result.ZipHashMatches.HasValue)
            {
                sb.AppendLine(string.Format(_localization.Get(result.ArchiveFormat == ArchiveFormat.SevenZ ? "verify_zip_hash_line_7z" : "verify_zip_hash_line"),
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
                AppendSignerIdentity(sb, sig);
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

            AppendContentError(sb, result);

            DetailsText = sb.ToString();

            // Distinguir manipulación detectada de una verificación que no pudo completarse.
            Status = result.IsIntact
                ? VerificationStatus.Valid
                : result.TamperDetected
                    ? VerificationStatus.Invalid
                    : VerificationStatus.Inconclusive; // sin manipulación detectada, pero sin poder completar

            // Cualquiera sea el veredicto, el informe lo asienta: uno que dice "comprometida" también va al expediente.
            _verifiedManifest = _verificationService.ParseManifest(await File.ReadAllTextAsync(ReportFilePath));
            _verifiedManifestName = Path.GetFileName(ReportFilePath);
            _verifiedManifestPath = ReportFilePath;
            _verifiedAt = DateTimeOffset.Now;
            CanSaveReport = true;
        }
        catch (Exception ex)
        {
            Status = VerificationStatus.Inconclusive;
            DetailsText = string.Format(_localization.Get("verify_archive_error"), ex.Message);
        }
    }

    /// <summary>
    /// Prueba un archivo comprimido sin manifiesto. El veredicto dice hasta dónde llega: "sin errores" según sus propios
    /// controles, nunca "íntegra", que solo puede decirlo un manifiesto o una huella anotada aparte.
    /// </summary>
    private async Task TestArchiveAsync()
    {
        DetailsText = _localization.Get("verify_running_test");
        var running = DetailsText;
        var entryProgress = new Progress<string>(name =>
        {
            if (Status == VerificationStatus.NotRun)
            {
                DetailsText = running + Environment.NewLine + name;
            }
        });

        ArchiveTestResult result;
        try
        {
            var password = string.IsNullOrEmpty(SealedPassword) ? null : SealedPassword;
            result = await _zipService.TestAsync(ReportFilePath, password, null, CancellationToken.None, entryProgress);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result = new ArchiveTestResult { Outcome = ArchiveTestOutcome.Unreadable, Error = ex.Message };
        }

        DetailsText = BuildTestDetails(result);
        _testOutcome = result.Outcome;
        _passwordVerifiedWrong = result.PasswordVerifiedWrong;
        _passwordMissing = result.Outcome == ArchiveTestOutcome.PasswordRequired;
        OnPropertyChanged(nameof(TestNeedsPassword));
        OnPropertyChanged(nameof(LabelBadgeBadFormat));
        Status = result.Outcome switch
        {
            ArchiveTestOutcome.NoErrors => VerificationStatus.Valid,
            ArchiveTestOutcome.Damaged => VerificationStatus.Invalid,
            ArchiveTestOutcome.PasswordRequired => VerificationStatus.BadFormat,
            _ => VerificationStatus.Inconclusive
        };
        OnPropertyChanged(nameof(VerdictLead));
    }

    private string BuildTestDetails(ArchiveTestResult result)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(result.Format))
        {
            sb.AppendLine(string.Format(_localization.Get("verify_test_format"), result.Format));
        }

        if (result.IsSelfExtracting)
        {
            sb.AppendLine(_localization.Get("verify_test_sfx"));
        }

        if (sb.Length > 0)
        {
            sb.AppendLine();
        }

        switch (result.Outcome)
        {
            case ArchiveTestOutcome.PasswordRequired:
                sb.AppendLine(_localization.Get("verify_test_password_needed"));
                break;
            case ArchiveTestOutcome.WrongPassword:
                sb.AppendLine(_localization.Get(result.PasswordVerifiedWrong
                    ? "verify_test_wrong_password_certain" : "verify_test_wrong_password"));
                break;
            case ArchiveTestOutcome.Unreadable:
                sb.AppendLine(string.Format(_localization.Get("verify_test_error"), result.Error));
                break;
            default:
                foreach (var entry in result.Entries)
                {
                    sb.AppendLine($"{_localization.Get("verify_tag_ok")} {entry.Name}");
                }

                foreach (var damaged in result.Damaged)
                {
                    sb.AppendLine($"{_localization.Get("verify_tag_damaged")} {damaged.Name} — {damaged.Reason}");
                }

                if (result.Error != null)
                {
                    sb.AppendLine(string.Format(_localization.Get("verify_test_error"), result.Error));
                }

                sb.AppendLine();
                sb.AppendLine(string.Format(_localization.Get("verify_test_summary"), result.FilesOk, result.Damaged.Count));
                if (result.Skipped.Count > 0)
                {
                    sb.AppendLine(string.Format(_localization.Get("verify_test_skipped"), result.Skipped.Count));
                }

                if (result.Outcome == ArchiveTestOutcome.NoChecks)
                {
                    sb.AppendLine(_localization.Get("verify_test_nochecks"));
                }

                break;
        }

        if (result.ArchiveFiles.Count > 0)
        {
            sb.AppendLine();
            var culture = _localization.CurrentLanguage == "es"
                ? System.Globalization.CultureInfo.GetCultureInfo("es-AR")
                : System.Globalization.CultureInfo.GetCultureInfo("en-US");
            foreach (var (fileName, size, sha256) in result.ArchiveFiles)
            {
                sb.AppendLine(string.Format(_localization.Get("verify_test_hash"), fileName, size.ToString("N0", culture) + " B", sha256));
            }

            // Solo cuando salió bien: con un archivo dañado, "está sano según sus controles" se contradiría.
            if (result.Outcome == ArchiveTestOutcome.NoErrors)
            {
                sb.AppendLine();
                sb.AppendLine(_localization.Get("verify_test_note"));
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// El manifiesto dice que el paquete se cifró. Si no se puede leer, false: que lo informe la verificación.
    /// </summary>
    private bool ManifestRecordsPassword(string manifestPath)
    {
        try
        {
            return _verificationService.ParseManifest(File.ReadAllText(manifestPath))?.HasPassword == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string StatusTag(Core.Models.FileVerificationStatus status) => status switch
    {
        Core.Models.FileVerificationStatus.Ok => _localization.Get("verify_tag_ok"),
        Core.Models.FileVerificationStatus.Altered => _localization.Get("verify_tag_altered"),
        Core.Models.FileVerificationStatus.Missing => _localization.Get("verify_tag_missing"),
        Core.Models.FileVerificationStatus.Extra => _localization.Get("verify_tag_extra"),
        Core.Models.FileVerificationStatus.Unverified => _localization.Get("verify_tag_unverified"),
        _ => "[?]"
    };

    /// <summary>
    /// Verifica un sobre sellado en sus dos niveles. Sin contraseña se responde la pregunta
    /// que va al expediente —si el sobre es el que se selló y quién lo firmó— sin abrir nada;
    /// con contraseña se agrega el veredicto por archivo contra el manifiesto interno.
    /// </summary>
    private async Task VerifySealedAsync()
    {
        var password = string.IsNullOrEmpty(SealedPassword) ? null : SealedPassword;

        SealedVerificationResult result;
        try
        {
            result = await _sealedVerificationService.VerifyAsync(
                ReportFilePath, password, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = VerificationStatus.BadFormat;
            DetailsText = ex.Message;
            return;
        }

        var details = new System.Text.StringBuilder();

        if (!result.FormatValid)
        {
            Status = VerificationStatus.BadFormat;
            DetailsText = result.FormatError ?? _localization.Get("verify_bad_format");
            return;
        }

        details.AppendLine(string.Format(_localization.Get("sealed_package_id"), result.PackageId));
        details.AppendLine(string.Format(
            _localization.Get("sealed_at"), result.SealedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss")));
        details.AppendLine(string.Format(_localization.Get("sealed_sha256"), result.SealedSha256));
        details.AppendLine();

        if (result.ReceiptPresent)
        {
            details.AppendLine(FormatMatch("sealed_hash_match", result.SealedHashMatches));
            details.AppendLine(FormatMatch("sealed_size_match", result.SealedSizeMatches));
            details.AppendLine(FormatMatch("sealed_id_match", result.PackageIdMatches));
        }
        else
        {
            details.AppendLine(result.ReceiptError);
        }

        AppendSignature(details, result.Signature);

        if (result.Content != null)
        {
            details.AppendLine();
            details.AppendLine(string.Format(
                _localization.Get("verify_summary"),
                result.Content.OkCount, result.Content.AlteredCount,
                result.Content.MissingCount, result.Content.ExtraCount));
            AppendContentError(details, result.Content);

            // Paquete intacto que no se pudo leer con la clave del sobre: se cifró con otra.
            if (result.Content is { ContentVerificationError: not null, ZipHashMatches: true })
            {
                details.AppendLine(_localization.Get("sealed_inner_password_hint"));
            }
        }
        else if (result.ContentError != null)
        {
            details.AppendLine();
            details.AppendLine(result.ContentError);
        }
        else if (password == null)
        {
            details.AppendLine();
            details.AppendLine(_localization.Get("sealed_public_only"));
        }

        if (result.UndeletedTempPath != null)
        {
            details.AppendLine(string.Format(_localization.Get("result_temp_left"), result.UndeletedTempPath));
        }

        DetailsText = details.ToString().TrimEnd();
        // Sin recibo solo se comprobó que es un sobre bien formado (y, con contraseña, que el contenido
        // coincide con su manifiesto), no que sea el que se selló: eso no se presenta como verificado. Y
        // una contraseña que no abre el sobre deja la verificación incompleta, no prueba una alteración: el
        // rojo es solo para lo que se comprobó alterado, igual que en la CLI.
        Status = result.IsIntact
            ? result.ReceiptPresent ? VerificationStatus.Valid : VerificationStatus.Inconclusive
            : result.TamperDetected ? VerificationStatus.Invalid : VerificationStatus.Inconclusive;
    }

    private string FormatMatch(string key, bool? matches)
    {
        var verdict = matches switch
        {
            true => _localization.Get("verify_match"),
            false => _localization.Get("verify_mismatch"),
            _ => "-"
        };

        return string.Format(_localization.Get(key), verdict);
    }

    /// <summary>
    /// Huella del certificado firmante y si está entre los firmantes de confianza. El nombre del certificado lo
    /// elige quien lo crea; la huella es lo que lo identifica, y es lo único con lo que se puede comprobar quién
    /// firmó (contra la lista de Ajustes, o dictándola con el perito por otro medio).
    /// </summary>
    /// <summary>
    /// Motivo por el que no se pudo leer el contenido y qué dice el hash global, que se compara aparte: intacto
    /// (falla la contraseña) o alterado.
    /// </summary>
    private void AppendContentError(System.Text.StringBuilder sb, ArchiveVerificationResult result)
    {
        if (result.ContentVerificationError == null)
        {
            return;
        }

        sb.AppendLine();
        if (result.MissingVolumes.Count > 0)
        {
            sb.AppendLine(string.Format(_localization.Get("verify_volumes_missing"),
                result.MissingVolumes.Count, string.Join(", ", result.MissingVolumes)));
            return;
        }

        sb.AppendLine(string.Format(_localization.Get("verify_content_error"), result.ContentVerificationError));
        sb.AppendLine(_localization.Get(result.ZipHashMatches switch
        {
            true => "verify_content_error_hint_intact",
            false => "verify_content_error_hint_altered",
            null => "verify_content_error_hint"
        }));
        if (result.Volumes is { Count: > 0 })
        {
            sb.AppendLine(_localization.Get("verify_content_error_hint_volumes"));
        }
    }

    private void AppendSignerIdentity(System.Text.StringBuilder details, SignatureInfo signature)
    {
        if (string.IsNullOrEmpty(signature.SignerThumbprint))
        {
            return;
        }

        // Con la misma sangría que "Firmante:", porque es parte de ese dato.
        details.AppendLine("  " + string.Format(_localization.Get("verify_signer_thumbprint"), SignerTrust.Format(signature.SignerThumbprint)));

        // Con la firma inválida el certificado no firmó este archivo: que esté en la lista no significa nada.
        if (!signature.Valid)
        {
            details.AppendLine("  " + _localization.Get("verify_signer_invalid_signature"));
            return;
        }

        List<TrustedSigner> trusted;
        try
        {
            trusted = _configService.Load().TrustedSigners;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            trusted = new List<TrustedSigner>();
        }

        var (level, name) = SignerTrust.Evaluate(signature.SignerThumbprint, trusted);
        details.AppendLine("  " + level switch
        {
            SignerTrustLevel.Trusted => string.Format(_localization.Get("verify_signer_trusted"), name),
            SignerTrustLevel.Unknown => _localization.Get("verify_signer_unknown"),
            _ => _localization.Get("verify_signer_no_list")
        });
    }

    private void AppendSignature(System.Text.StringBuilder details, SignatureInfo? signature)
    {
        if (signature is not { Present: true })
        {
            details.AppendLine(_localization.Get("sealed_receipt_unsigned"));
            return;
        }

        details.AppendLine(string.Format(
            _localization.Get("verify_signature_line"),
            _localization.Get(signature.Valid ? "verify_sig_valid" : "verify_sig_invalid")));

        if (!string.IsNullOrEmpty(signature.SignerSubject))
        {
            details.AppendLine(string.Format(_localization.Get("verify_signer"), signature.SignerSubject));
        }

        AppendSignerIdentity(details, signature);

        if (signature.TimestampUtc.HasValue)
        {
            details.AppendLine(string.Format(
                _localization.Get("verify_ts_line"),
                signature.TimestampUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                signature.TimestampValid == true
                    ? _localization.Get("verify_sig_valid")
                    : _localization.Get("verify_sig_invalid")));
        }
    }
}
