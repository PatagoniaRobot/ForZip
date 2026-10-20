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
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using ForZip.Core.Shell;
using ForZip.GUI.Services;

namespace ForZip.GUI.ViewModels;

public partial class UnzipViewModel : ViewModelBase
{
    private readonly IZipService _zipService;
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly ISealedPackageOpener _sealedOpener;
    private readonly ICloudSyncDetector _cloudSyncDetector;
    private readonly IConfirmDialogService _confirmDialog;

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

    /// <summary>El archivo que se está extrayendo ahora, debajo de la barra (como en WinRAR o 7-Zip).</summary>
    [ObservableProperty]
    private string _progressFile = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>El mensaje de estado es un error (contraseña incorrecta, sobre alterado…): se muestra en rojo.</summary>
    [ObservableProperty]
    private bool _isStatusError;

    // Cualquier mensaje nuevo arranca neutro; los de error lo marcan con ShowError.
    partial void OnStatusMessageChanged(string value) => IsStatusError = false;

    private void ShowError(string message)
    {
        StatusMessage = message;
        IsStatusError = true;
    }

    /// <summary>
    /// La última extracción se frenó por la contraseña: el archivo está cifrado y no se dio ninguna, o la que se dio
    /// no lo abre. La ventana del menú contextual muestra el campo recién entonces.
    /// </summary>
    [ObservableProperty]
    private bool _needsPassword;

    /// <summary>Verdadero cuando lo elegido es un sobre .fzs y no un ZIP.</summary>
    [ObservableProperty]
    private bool _isSealedInput;

    /// <summary>Identificador y fecha del sobre, leídos del header en claro.</summary>
    [ObservableProperty]
    private string _sealedSummary = string.Empty;

    /// <summary>Veredicto de la verificación tras abrir un sobre (vacío hasta que se abre).</summary>
    [ObservableProperty]
    private string _sealedVerdict = string.Empty;

    [ObservableProperty]
    private bool _isSealedVerdictOk;

    public bool ShowVerdictOk => IsSealedVerdictOk && !string.IsNullOrEmpty(SealedVerdict);
    public bool ShowVerdictBad => !IsSealedVerdictOk && !string.IsNullOrEmpty(SealedVerdict);

    partial void OnSealedVerdictChanged(string value) => NotifyVerdictVisibility();

    partial void OnIsSealedVerdictOkChanged(bool value) => NotifyVerdictVisibility();

    private void NotifyVerdictVisibility()
    {
        OnPropertyChanged(nameof(ShowVerdictOk));
        OnPropertyChanged(nameof(ShowVerdictBad));
    }

    private SealedHeader? _sealedHeader;

    // ---- Resultado de la última extracción, en pantalla (diseño 2026-10-17) ----

    /// <summary>Renombres, rutas largas, dañados, omitidos y comentarios de la última extracción.</summary>
    public ObservableCollection<ExtractionObservation> Observations { get; } = new();

    /// <summary>Título de la tarjeta de resultado; vacío mientras no hay resultado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultHeading = string.Empty;

    [ObservableProperty]
    private string _resultDetail = string.Empty;

    /// <summary>"ok" (completa), "notes" (con observaciones) o "error" (con entradas dañadas).</summary>
    [ObservableProperty]
    private string _resultKind = string.Empty;

    public bool HasResult => ResultHeading.Length > 0;

    /// <summary>Más observaciones que esta cantidad se resumen en una línea que remite a la bitácora.</summary>
    private const int MaxObservationsShown = 50;

    private string _lastOutputDirectory = string.Empty;

    private void ClearResult()
    {
        Observations.Clear();
        ResultHeading = string.Empty;
        ResultDetail = string.Empty;
        ResultKind = string.Empty;
    }

    private void AddObservation(ExtractionObservation observation, ref int hidden)
    {
        if (Observations.Count < MaxObservationsShown)
        {
            Observations.Add(observation);
        }
        else
        {
            hidden++;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (string.IsNullOrEmpty(_lastOutputDirectory) || !Directory.Exists(_lastOutputDirectory))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_lastOutputDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logService.Warning(ex.Message);
        }
    }

    public string Heading => _localization.Get("unzip_heading");
    public string Subtitle => _localization.Get("unzip_subtitle");
    public string LabelDestinationHint => _localization.Get("unzip_dest_hint");
    public string LabelOpenFolder => _localization.Get("open_folder");

    public UnzipViewModel(
        IZipService zipService,
        ILocalizationService localization,
        IConfigService configService,
        ILogService logService,
        ISealedPackageOpener sealedOpener,
        ICloudSyncDetector cloudSyncDetector,
        IConfirmDialogService confirmDialog) : base(localization)
    {
        _zipService = zipService;
        _configService = configService;
        _logService = logService;
        _sealedOpener = sealedOpener;
        _cloudSyncDetector = cloudSyncDetector;
        _confirmDialog = confirmDialog;

        LoadDefaults();
    }

    public string Title => _localization.Get("extract").ToUpper();
    public string LabelZipPath => _localization.Get("input_file");
    public string LabelOutputDir => _localization.Get("destination_folder");
    public string LabelPassword => _localization.Get(IsSealedInput ? "unseal_password" : "password_optional");
    public string LabelDropZone => _localization.Get("drag_drop_zip");
    public string ButtonBrowse => _localization.Get("unzip_browse");
    public string ButtonExtract => _localization.Get(IsSealedInput ? "unseal_button" : "extract");
    public string ButtonCancel => _localization.Get("cancel");

    /// <summary>Dónde va a quedar el contenido del sobre: siempre en una subcarpeta propia.</summary>
    public string SealedDestinationHint =>
        IsSealedInput && _sealedHeader != null
            ? string.Format(_localization.Get("unseal_dest_hint"), _sealedHeader.BaseName)
            : string.Empty;

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(LabelDestinationHint));
        OnPropertyChanged(nameof(LabelOpenFolder));
        OnPropertyChanged(nameof(LabelZipPath));
        OnPropertyChanged(nameof(LabelOutputDir));
        OnPropertyChanged(nameof(LabelPassword));
        OnPropertyChanged(nameof(LabelDropZone));
        OnPropertyChanged(nameof(ButtonBrowse));
        OnPropertyChanged(nameof(ButtonExtract));
        OnPropertyChanged(nameof(ButtonCancel));
        OnPropertyChanged(nameof(SealedDestinationHint));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(TooltipTogglePassword));
        OnPropertyChanged(nameof(LabelPickerAll));
    }

    partial void OnIsSealedInputChanged(bool value)
    {
        OnPropertyChanged(nameof(LabelPassword));
        OnPropertyChanged(nameof(ButtonExtract));
        OnPropertyChanged(nameof(SealedDestinationHint));
    }

    private void LoadDefaults()
    {
        var config = _configService.Load();
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            OutputDirectory = config.DefaultOutputDirectory ?? string.Empty;
        }
    }

    public static bool IsSealedPath(string? path) =>
        !string.IsNullOrEmpty(path) &&
        path.EndsWith(SealedContainerFormat.SealedExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Volumen de un RAR con el esquema de nombres viejo (<c>caso.rar</c>, <c>caso.r00</c>, <c>caso.r01</c>…). El
    /// lector arranca solo desde el primero, así que cualquiera sirve.
    /// </summary>
    public static bool IsOldStyleRarVolume(string path) =>
        System.Text.RegularExpressions.Regex.IsMatch(Path.GetExtension(path), @"^\.r\d\d$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Un ejecutable que lleva un 7z o un RAR adentro (autoextraíble). Se reconoce por la firma, leyendo el archivo: nunca
    /// se ejecuta. Lo que no se puede leer cuenta como no reconocido.
    /// </summary>
    public static bool IsSelfExtractingArchive(string path)
    {
        try
        {
            return PrZip.Formats.ArchiveReader.DetectFormat(path) == "rar" || ArchiveFormats.Is7zSelfExtracting(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Un .exe que no es un autoextraíble 7z o RAR reconocible: no hay nada que extraer.</summary>
    private static bool IsUnrecognizedExecutable(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path)
        && !IsSelfExtractingArchive(path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordCharDisplay))]
    private bool _isPasswordVisible;

    public char PasswordCharDisplay => IsPasswordVisible ? '\0' : '•';
    public string TooltipTogglePassword => _localization.Get("tooltip_toggle_password");
    public string LabelPickerAll => _localization.Get("picker_all_files");

    /// <summary>
    /// Lo que no salió tal cual queda en la bitácora: nombres saneados, enlaces, dispositivos y flujos NTFS no
    /// creados, y los comentarios. Nombres y textos de comentario van como detalle de evidencia (la bitácora
    /// redactada los omite).
    /// </summary>
    private int ReportExtractionNotes(ExtractionResult result)
    {
        int hidden = 0;
        if (result.Damaged.Count > 0)
        {
            _logService.Error(
                string.Format(_localization.Get("log_extract_damaged"), result.Damaged.Count),
                string.Join("; ", result.Damaged.Select(d => $"'{d.Name}'" +
                    (d.ExpectedSize is { } size ? $" ({size:N0} bytes)" : "") + $": {d.Reason}")));
            foreach (var d in result.Damaged)
            {
                AddObservation(new ExtractionObservation(_localization.Get("obs_damaged"), d.Name, d.Reason, IsError: true),
                    ref hidden);
            }
        }

        if (result.Renamed.Count > 0)
        {
            _logService.Warning(
                string.Format(_localization.Get("log_extract_renamed"), result.Renamed.Count),
                string.Join("; ", result.Renamed.Select(r =>
                    $"'{r.OriginalName}' → '{r.SavedAs}' ({_localization.Get(RenameReasonKey(r.Reason))})")));
            foreach (var r in result.Renamed)
            {
                AddObservation(new ExtractionObservation(
                    string.Format(_localization.Get("obs_renamed"), _localization.Get(RenameReasonKey(r.Reason))),
                    $"{r.OriginalName} → {r.SavedAs}"), ref hidden);
            }
        }

        if (result.LongPaths.Count > 0)
        {
            _logService.Warning(
                string.Format(_localization.Get("log_extract_long_paths"), result.LongPaths.Count),
                string.Join("; ", result.LongPaths.Select(p => $"'{p}'")));
            foreach (var p in result.LongPaths)
            {
                AddObservation(new ExtractionObservation(
                    _localization.Get("obs_long_path"), p, _localization.Get("obs_long_path_advice")), ref hidden);
            }
        }

        if (result.Skipped.Count > 0)
        {
            _logService.Warning(
                string.Format(_localization.Get("log_extract_skipped"), result.Skipped.Count),
                string.Join("; ", result.Skipped.Select(s => $"'{s.Name}' ({s.Kind}" +
                    (s.Size is { } size ? $", {size:N0} bytes" : "") +
                    (s.LinkTarget != null ? " → '" + s.LinkTarget + "'" : "") + ")")));
            foreach (var s in result.Skipped)
            {
                AddObservation(new ExtractionObservation(
                    string.Format(_localization.Get("obs_skipped"), s.Kind),
                    s.Name + (s.LinkTarget != null ? " → " + s.LinkTarget : "")), ref hidden);
            }
        }


        if (result.ArchiveComment is { } comment)
        {
            if (comment.Text != null)
            {
                _logService.Info(_localization.Get("log_extract_comment"), CommentText.ForDisplay(comment.Text));
            }
            else
            {
                _logService.Warning(string.Format(_localization.Get("log_extract_comment_bad"), comment.Problem));
            }
        }

        if (result.EntryComments.Count > 0)
        {
            _logService.Info(
                string.Format(_localization.Get("log_extract_entry_comments"), result.EntryComments.Count),
                string.Join("; ", result.EntryComments.Select(c => $"'{c.Name}': " +
                    (c.Comment.Text != null ? CommentText.ForDisplay(c.Comment.Text) : $"({c.Comment.Problem})"))));
        }

        if (result.ArchiveComment is { } archiveComment)
        {
            AddObservation(new ExtractionObservation(_localization.Get("obs_comment"),
                archiveComment.Text != null ? CommentText.ForDisplay(archiveComment.Text) : archiveComment.Problem ?? string.Empty),
                ref hidden);
        }

        foreach (var c in result.EntryComments)
        {
            AddObservation(new ExtractionObservation(string.Format(_localization.Get("obs_entry_comment"), c.Name),
                c.Comment.Text != null ? CommentText.ForDisplay(c.Comment.Text) : c.Comment.Problem ?? string.Empty),
                ref hidden);
        }

        if (hidden > 0)
        {
            Observations.Add(new ExtractionObservation(
                string.Format(_localization.Get("obs_more"), hidden), string.Empty));
        }

        return Observations.Count - (hidden > 0 ? 1 : 0) + hidden;
    }

    private static string RenameReasonKey(RenameReason reason) => reason switch
    {
        RenameReason.TooLong => "rename_reason_too_long",
        RenameReason.Repeated => "rename_reason_repeated",
        RenameReason.AlreadyInDestination => "rename_reason_in_destination",
        RenameReason.FolderIsFile => "rename_reason_folder_is_file",
        _ => "rename_reason_invalid",
    };

    public void SetZipPath(string path)
    {
        ZipFilePath = path;
        ClearResult();
        SealedVerdict = string.Empty;
        StatusMessage = string.Empty;

        // La contraseña del archivo anterior no sirve para este y, si quedaba, la bitácora registraba como
        // cifrado un tar.gz o un ISO que no lo están.
        Password = string.Empty;
        IsPasswordVisible = false;

        if (IsSealedPath(path))
        {
            // Para un sobre no se propone destino: el operador lo elige siempre. Un .fzs bajado
            // de la nube suele estar en una carpeta sincronizada, y "al lado del sobre" sería
            // justo el peor lugar para dejar el contenido en claro.
            OutputDirectory = string.Empty;
            IsSealedInput = true;
            _ = LoadSealedHeaderAsync(path);
            return;
        }

        IsSealedInput = false;
        _sealedHeader = null;
        SealedSummary = string.Empty;

        if (IsUnrecognizedExecutable(path))
        {
            // Se avisa al elegirlo, no recién al extraer con un error del lector de ZIP.
            StatusMessage = _localization.Get("unzip_not_sfx");
        }

        // Sugerencia: misma carpeta + subcarpeta con el nombre del ZIP. Un destino que sugirió ForZip para el
        // archivo anterior se reemplaza (si no, el segundo archivo caía en la carpeta del primero); uno que eligió
        // o escribió el operador, o el predeterminado de la configuración, se respeta.
        if (!string.IsNullOrEmpty(path) &&
            (string.IsNullOrEmpty(OutputDirectory) || OutputDirectory == _suggestedOutputDirectory))
        {
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            OutputDirectory = PackageOutput.FreeFolder(Path.Combine(dir, SplitArchive.FolderNameFor(path)));
            _suggestedOutputDirectory = OutputDirectory;
        }
    }

    /// <summary>
    /// Destino de una acción del menú contextual: "Extraer aquí" deja el contenido en la carpeta del archivo; "Extraer
    /// en Nombre\" y "Extraer…" (como sugerencia) en una subcarpeta con su nombre. Con un volumen .001 manda el nombre
    /// lógico (caso.zip), no el del volumen. Si la subcarpeta ya tiene algo, "Nombre (2)\" (ver
    /// <see cref="PackageOutput.FreeFolder"/>); <paramref name="claimed"/> son los destinos ya tomados por otros
    /// archivos de la misma selección, que todavía no existen en disco.
    /// </summary>
    public static string DestinationFor(ShellVerb verb, string archivePath, ICollection<string>? claimed = null)
    {
        var logical = SplitArchive.GetBasePath(archivePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(logical)) ?? string.Empty;
        return verb == ShellVerb.ExtractHere
            ? dir
            : PackageOutput.FreeFolder(Path.Combine(dir, SplitArchive.FolderNameFor(archivePath)), claimed);
    }

    /// <summary>Último destino que propuso ForZip (no el operador): ver <see cref="SetZipPath"/>.</summary>
    private string? _suggestedOutputDirectory;

    private async Task LoadSealedHeaderAsync(string path)
    {
        try
        {
            _sealedHeader = await _sealedOpener.ReadHeaderAsync(path, CancellationToken.None);
            SealedSummary = string.Format(_localization.Get("unseal_summary"),
                _sealedHeader.BaseName, _sealedHeader.SealedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (Exception ex) when (ex is SealedFormatException or IOException or UnauthorizedAccessException)
        {
            _sealedHeader = null;
            SealedSummary = string.Format(_localization.Get("unseal_bad_envelope"), ex.Message);
        }

        OnPropertyChanged(nameof(SealedDestinationHint));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DecompressAsync(CancellationToken ct)
    {
        if (IsSealedInput)
        {
            await OpenSealedAsync(ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(ZipFilePath) || string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = _localization.Get("error_no_files");
            return;
        }

        if (IsUnrecognizedExecutable(ZipFilePath))
        {
            // Un .exe cualquiera (elegido con "Todos los archivos" o escrito a mano) iba al lector de ZIP y fallaba con
            // "Cannot find central directory". Nunca se ejecuta: solo se dice qué es.
            StatusMessage = _localization.Get("unzip_not_sfx");
            _logService.LogEvidence(LogLevel.Warning, _localization.Get("log_extract_not_sfx"), Path.GetFileName(ZipFilePath));
            return;
        }

        _logService.LogEvidence(LogLevel.Info, _localization.Get("log_extract_start"), Path.GetFileName(ZipFilePath));
        _logService.LogEvidence(LogLevel.Info, _localization.Get("log_extract_dest"), OutputDirectory);
        // Al extraer, el algoritmo lo decide el archivo (AES-256 en ZIP/7z/RAR5, AES-128 en RAR 3.x...): la
        // bitácora solo deja constancia de si el operador dio una contraseña. Decía "Cifrado: Sí", que afirmaba
        // del archivo algo que solo era cierto del campo.
        _logService.Info(string.Format(_localization.Get("log_extract_password"),
            _localization.Get(string.IsNullOrEmpty(Password) ? "log_password_not_given" : "log_password_given")));

        IsProcessing = true;
        NeedsPassword = false;
        StatusMessage = _localization.Get("processing");
        ProgressPercent = 0;
        ProgressDetail = string.Empty;
        ProgressFile = string.Empty;
        ClearResult();
        _lastOutputDirectory = OutputDirectory;

        try
        {
            var result = await _zipService.DecompressAsync(
                ZipFilePath, OutputDirectory,
                string.IsNullOrEmpty(Password) ? null : Password,
                BuildProgress(), ct,
                new Progress<string>(name => ProgressFile = name));

            ProgressPercent = 100;
            _logService.Info(string.Format(_localization.Get("log_extract_format"), result.Format, result.FilesExtracted));
            var notes = ReportExtractionNotes(result);
            if (result.IsComplete)
            {
                StatusMessage = string.Empty;
                ResultKind = notes > 0 ? "notes" : "ok";
                ResultHeading = notes > 0
                    ? string.Format(_localization.Get("unzip_result_notes"), notes)
                    : _localization.Get("unzip_result_ok");
                ResultDetail = string.Format(_localization.Get("unzip_result_detail"), result.FilesExtracted);
                _logService.LogEvidence(LogLevel.Success, _localization.Get("log_extract_done"), OutputDirectory);
            }
            else
            {
                // Lo extraído está verificado; lo dañado no quedó en el destino. No se presenta como un éxito.
                StatusMessage = string.Empty;
                ResultKind = "error";
                ResultHeading = _localization.Get("unzip_result_damaged");
                ResultDetail = string.Format(_localization.Get("unzip_incomplete_damaged"),
                    result.FilesExtracted, result.Damaged.Count);
                _logService.LogEvidence(LogLevel.Warning, _localization.Get("log_extract_done_incomplete"), OutputDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning(_localization.Get("log_extract_cancelled"));
        }
        catch (Exception ex)
        {
            // Archivo cifrado sin contraseña (7z, RAR): se pide la contraseña en vez de decir que es incorrecta.
            if (ex is PrZip.Exceptions.InvalidPasswordOrCorruptDataException && string.IsNullOrEmpty(Password))
            {
                NeedsPassword = true;
                StatusMessage = _localization.Get("error_password_required");
                _logService.Error(_localization.Get("error_password_required"));
            }
            // SharpZipLib lanza ZipException con mensaje genérico cuando la contraseña es inválida. Sin contraseña
            // ("No password available for AES encrypted stream") no es que esté mal: falta.
            else if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("contraseña", StringComparison.OrdinalIgnoreCase))
            {
                NeedsPassword = true;
                if (string.IsNullOrEmpty(Password))
                {
                    StatusMessage = _localization.Get("error_password_required");
                }
                else
                {
                    ShowError(_localization.Get("error_wrong_password"));
                }

                _logService.Error(StatusMessage);
            }
            else
            {
                ShowError(string.Format(_localization.Get("error_io"), ex.Message));
                _logService.LogEvidence(LogLevel.Error, _localization.Get("log_extract_error"), ex.Message);
            }
        }
        finally
        {
            // Como al abrir un sobre: la contraseña no queda en el campo después de usarla.
            Password = string.Empty;
            IsPasswordVisible = false;
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Abre un sobre: freno de carpeta sincronizada, subcarpeta propia que no pisa nada,
    /// descifrado, extracción y verificación contra el manifiesto que trae adentro.
    /// </summary>
    private async Task OpenSealedAsync(CancellationToken ct)
    {
        SealedVerdict = string.Empty;

        if (_sealedHeader == null)
        {
            StatusMessage = _localization.Get("unseal_no_header");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = _localization.Get("unseal_no_dest");
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            StatusMessage = _localization.Get("unseal_no_password");
            return;
        }

        // El destino es el punto de fuga más verosímil del perfil: el contenido sale en claro
        // y, en una carpeta sincronizada, sube solo. Se pregunta con la opción segura por defecto.
        var cloud = _cloudSyncDetector.Detect(OutputDirectory);
        if (cloud != null)
        {
            var proceed = await _confirmDialog.ConfirmAsync(
                string.Format(_localization.Get("unseal_cloud_title"), cloud.Provider),
                string.Format(_localization.Get("unseal_cloud_msg"), cloud.Provider, OutputDirectory),
                _localization.Get("unseal_cloud_details"),
                _localization.Get("unseal_cloud_accept"),
                _localization.Get("unseal_cloud_cancel"));
            if (!proceed)
            {
                StatusMessage = _localization.Get("unseal_cloud_declined");
                _logService.Warning(string.Format(_localization.Get("log_unseal_cloud_declined"), cloud.Provider));
                return;
            }

            _logService.Warning(string.Format(_localization.Get("log_unseal_cloud_accepted"), cloud.Provider));
        }

        var target = Path.Combine(OutputDirectory, _sealedHeader.BaseName);
        _logService.Info(string.Format(_localization.Get("log_unseal_start"), _sealedHeader.BaseName));

        IsProcessing = true;
        StatusMessage = _localization.Get("unseal_opening");
        ProgressPercent = 0;

        try
        {
            var result = await _sealedOpener.OpenAsync(ZipFilePath, target, Password, BuildProgress(), ct);
            ProgressPercent = 100;

            StatusMessage = string.Format(_localization.Get("unseal_done"), result.ExtractedFiles.Count, target)
                + Environment.NewLine + _localization.Get("unseal_cleartext_warning");
            if (result.UndeletedTempPath != null)
            {
                StatusMessage += Environment.NewLine
                    + string.Format(_localization.Get("unseal_temp_left"), result.UndeletedTempPath);
            }

            ReportVerdict(result);
            _logService.Success(string.Format(_localization.Get("log_unseal_done"),
                _sealedHeader.BaseName, result.ExtractedFiles.Count));
        }
        catch (SealedPasswordException)
        {
            ShowError(_localization.Get("unseal_wrong_password"));
            _logService.Error(_localization.Get("unseal_wrong_password"));
        }
        catch (SealedIntegrityException ex)
        {
            ShowError(string.Format(_localization.Get("unseal_altered"), ex.ChunkIndex));
            _logService.Error(StatusMessage);
        }
        catch (SealedFormatException ex)
        {
            ShowError(string.Format(_localization.Get("unseal_bad_envelope"), ex.Message));
            _logService.Error(StatusMessage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning(_localization.Get("log_extract_cancelled"));
        }
        catch (IOException ex) when (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            StatusMessage = string.Format(_localization.Get("unseal_dest_not_empty"), target);
            _logService.Error(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(string.Format(_localization.Get("error_io"), ex.Message));
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_extract_error"), ex.Message);
        }
        finally
        {
            // La contraseña no queda en el campo después de usarla, haya salido bien o mal.
            Password = string.Empty;
            IsProcessing = false;
        }
    }

    private void ReportVerdict(SealedOpenResult result)
    {
        if (result.HasNoManifest)
        {
            // No se verificó nada: no va en la caja verde de "íntegro" sino como nota, igual que en la CLI.
            SealedVerdict = string.Empty;
            StatusMessage += Environment.NewLine + _localization.Get("unseal_verify_none");
            _logService.Warning(_localization.Get("unseal_verify_none"));
            return;
        }

        if (result.Verification == null)
        {
            IsSealedVerdictOk = false;
            SealedVerdict = string.Format(_localization.Get("unseal_verify_error"), result.VerificationError);
            _logService.Error(SealedVerdict);
            return;
        }

        var v = result.Verification;
        IsSealedVerdictOk = v.IsIntact;
        SealedVerdict = v.IsIntact
            ? string.Format(_localization.Get("unseal_verify_ok"), v.OkCount)
            : v.TamperDetected
                ? string.Format(_localization.Get("unseal_verify_bad"), v.AlteredCount, v.MissingCount, v.ExtraCount)
                : string.Format(_localization.Get("unseal_verify_incomplete"), v.OkCount, v.UnverifiedCount);

        if (v.IsIntact)
        {
            _logService.Success(SealedVerdict);
        }
        else
        {
            _logService.Error(SealedVerdict);
        }
    }

    private Progress<(long processed, long total)> BuildProgress() => new(p =>
    {
        if (p.total > 0)
        {
            ProgressPercent = 100.0 * p.processed / p.total;
            ProgressDetail = $"{p.processed:N0} / {p.total:N0} B";
        }
    });
}
