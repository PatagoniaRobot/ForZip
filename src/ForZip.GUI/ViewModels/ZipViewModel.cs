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
using ForZip.Core.Sealing;
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
    private readonly ISealedPackageBuilder _sealedPackageBuilder;
    private readonly IDeliveryActService _actService;
    private readonly ICloudSyncDetector _cloudSyncDetector;

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
    [NotifyPropertyChangedFor(nameof(CanStartCompress))]
    private bool _isProcessing;

    /// <summary>
    /// Se está recorriendo una carpeta recién elegida. Va en segundo plano: con la raíz de una unidad tardaba
    /// segundos y la ventana quedaba "No responde". Mientras dure, Empaquetar espera: la foto de la selección
    /// tiene que estar completa antes de empezar.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCompress))]
    private bool _isLoadingSelection;

    public bool CanStartCompress => !IsProcessing && !IsLoadingSelection;

    /// <summary>Una carga a la vez, en el orden en que llegan (el menú contextual reenvía de a una ruta).</summary>
    private readonly SemaphoreSlim _selectionGate = new(1, 1);

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

    /// <summary>7z elegido por el operador, en cualquiera de los dos modos (ver <see cref="EffectiveFormat"/>).</summary>
    [ObservableProperty]
    private bool _is7zFormat;

    /// <summary>
    /// Formato con el que se empaqueta de verdad: 7z si se eligió y fuera del perfil Reservado, que mete el
    /// ZIP en el sobre .fzs (es lo que leen fzs_extract.py y Open_FZS.html).
    /// </summary>
    public ArchiveFormat EffectiveFormat =>
        Is7zFormat && !IsReservedProfile ? ArchiveFormat.SevenZ : ArchiveFormat.Zip;

    public bool IsZipFormat
    {
        get => !Is7zFormat;
        set => Is7zFormat = !value;
    }

    partial void OnIs7zFormatChanged(bool value)
    {
        OnPropertyChanged(nameof(IsZipFormat));
        OnPropertyChanged(nameof(FormatHint));
        SyncOutputExtension();

        // El perfil Reservado apaga el 7z mientras dura, pero eso no es una elección del operador: no se guarda.
        if (_formatChangeFromProfile)
        {
            return;
        }

        try
        {
            var config = _configService.Load();
            var format = value ? ArchiveFormat.SevenZ : ArchiveFormat.Zip;
            if (config.PreferredFormat != format)
            {
                config.PreferredFormat = format;
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

    /// <summary>
    /// Ajusta la extensión de la salida al formato vigente (caso.zip ↔ caso.7z) si la ruta termina en
    /// una de las dos; cualquier otro nombre que haya puesto el operador se respeta.
    /// </summary>
    private void SyncOutputExtension()
    {
        OnPropertyChanged(nameof(EffectiveFormat));
        if (string.IsNullOrEmpty(OutputPath))
        {
            return;
        }

        var wanted = ArchiveFormats.Extension(EffectiveFormat);
        var current = Path.GetExtension(OutputPath);
        if ((current.Equals(".zip", StringComparison.OrdinalIgnoreCase) || current.Equals(".7z", StringComparison.OrdinalIgnoreCase))
            && !current.Equals(wanted, StringComparison.OrdinalIgnoreCase))
        {
            OutputPath = Path.ChangeExtension(OutputPath, wanted);
        }
    }

    /// <summary>
    /// Modo simple (guiado): oculta las opciones técnicas y aplica los valores forenses
    /// recomendados (SHA-256 + informe + manifiesto). Persiste en la configuración.
    /// </summary>
    [ObservableProperty]
    private bool _isSimpleMode = true;

    /// <summary>
    /// Perfil Reservado: el paquete completo termina dentro de un sobre <c>.fzs</c> opaco y
    /// solo salen de la máquina el sobre y su recibo.
    /// </summary>
    /// <remarks>
    /// Es un eje aparte de <see cref="IsSimpleMode"/>: se combinan, no se excluyen. Y es una
    /// sola decisión, no una grilla de casillas: sobre material sensible, doce opciones
    /// independientes son un error silencioso esperando a suceder, porque si el operador
    /// desmarca la equivocada no hay señal hasta que el archivo ya está subido.
    /// </remarks>
    [ObservableProperty]
    private bool _isReservedProfile;

    /// <summary>Aviso del perfil Estándar (§7.4). Se muestra una sola vez por sesión.</summary>
    [ObservableProperty]
    private bool _isStandardNoticeVisible;

    /// <summary>Advertencia de carpeta sincronizada sobre el destino elegido, si la hay.</summary>
    [ObservableProperty]
    private string _cloudSyncWarning = string.Empty;

    // ---- Resultado del perfil Reservado ----

    [ObservableProperty]
    private bool _isSealedResultVisible;

    [ObservableProperty]
    private string _deliveryActText = string.Empty;

    private string _sealedPackageId = string.Empty;

    /// <summary>
    /// Los controles que el perfil gobierna quedan deshabilitados pero visibles, con el
    /// tooltip que explica por qué. Ocultarlos dejaría al operador sin saber qué cambió.
    /// </summary>
    public bool AreProfileGovernedControlsEnabled => !IsReservedProfile;

    // ---- Pantalla de resultado (post-empaquetado) ----

    [ObservableProperty]
    private bool _isResultVisible;

    [ObservableProperty]
    private string _resultZipHash = string.Empty;

    [ObservableProperty]
    private string _resultNote = string.Empty;

    /// <summary>
    /// Los datos del último empaquetado, para "Guardar informe…": el informe ya no se escribe solo junto al paquete
    /// (el que se escribía y su .sha256 aparentaban una protección que no tenían), se arma cuando el operador lo pide.
    /// </summary>
    private ReportData? _lastReportData;

    /// <summary>
    /// Hay informe para guardar: el empaquetado registró sus datos y no es un sobre. En Reservado no se ofrece:
    /// el informe dejaría en claro, fuera del sobre, lo que el perfil existe para encerrar; quien abre el sobre lo
    /// arma desde Verificar.
    /// </summary>
    [ObservableProperty]
    private bool _canSaveReport;

    /// <summary>
    /// Aviso de paquete incompleto. Va aparte de <see cref="ResultNote"/> porque esa nota la
    /// pisan otros avisos (sello de tiempo, acta guardada) y este no se puede perder.
    /// </summary>
    [ObservableProperty]
    private string _resultIncompleteNote = string.Empty;

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

    partial void OnOutputPathChanged(string value) => CheckCloudSyncDestination();

    partial void OnIsSimpleModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonPack));
        SyncOutputExtension();

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

    /// <summary>
    /// Al entrar al perfil Reservado se aplican de una vez todos los controles que el perfil
    /// gobierna (§7.2) y se activa la redacción de la bitácora; al salir se avisa una sola vez
    /// qué queda expuesto en el perfil Estándar (§7.4).
    /// </summary>
    partial void OnIsReservedProfileChanged(bool value)
    {
        OnPropertyChanged(nameof(AreProfileGovernedControlsEnabled));
        OnPropertyChanged(nameof(ProfileHint));
        OnPropertyChanged(nameof(FormatHint));
        OnPropertyChanged(nameof(ButtonPack));
        SyncOutputExtension();

        // La bitácora deja de registrar nombres y rutas antes de que se escriba la primera
        // entrada del empaquetado, no después.
        _logService.Redacted = value;

        if (value)
        {
            IsStandardNoticeVisible = false;
            SetFormatFromProfile(false); // el sobre lleva un ZIP
            ApplyReservedDefaults();
            _logService.Info(_localization.Get("log_profile_reserved"));
        }
        else
        {
            // Al salir del Reservado vuelve el formato que el operador había elegido.
            SetFormatFromProfile(LoadPreferredFormat() == ArchiveFormat.SevenZ);

            // La entrada al Reservado quedaba registrada y la salida no: leyendo la bitácora no se sabía desde
            // cuándo volvían a registrarse nombres y rutas.
            _logService.Info(_localization.Get("log_profile_standard"));

            if (!_standardNoticeShownThisSession)
            {
                _standardNoticeShownThisSession = true;
                IsStandardNoticeVisible = true;
            }
        }

        if (!_profileIsOneShot)
        {
            PersistProfile(value);
        }
    }

    private bool _standardNoticeShownThisSession;

    /// <summary>El formato lo está cambiando el perfil, no el operador: no se guarda como preferencia.</summary>
    private bool _formatChangeFromProfile;

    private void SetFormatFromProfile(bool exe7z)
    {
        _formatChangeFromProfile = true;
        try
        {
            Is7zFormat = exe7z;
        }
        finally
        {
            _formatChangeFromProfile = false;
        }
    }

    private ArchiveFormat LoadPreferredFormat()
    {
        try
        {
            return _configService.Load().PreferredFormat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ArchiveFormat.Zip;
        }
    }

    /// <summary>
    /// La contraseña vigente ya se usó en un paquete del perfil Reservado. La siguiente tiene que ser otra: con la
    /// misma, el acta de un sobre abría también el siguiente.
    /// </summary>
    private bool _reservedPasswordUsed;

    /// <summary>
    /// Lo que el perfil Reservado fija para cada paquete: cifrado, SHA-256 (hace falta para el informe y el
    /// manifiesto que van en el sobre) y una contraseña recién generada.
    /// </summary>
    /// <remarks>
    /// Se aplica al entrar al perfil y cada vez que la contraseña anterior ya se usó o se borró. Antes se
    /// aplicaba solo al cambiar de perfil: con Reservado como perfil guardado, "Nuevo trabajo" vaciaba la
    /// contraseña y apagaba el cifrado sin que nada los repusiera, y el paquete siguiente salía sin cifrar.
    /// </remarks>
    private void ApplyReservedDefaults()
    {
        IsEncryptionEnabled = true;
        UseSha256 = true;

        // Contraseña generada y distinta por paquete, siempre. El campo manual queda
        // deshabilitado: una clave elegida por una persona, o derivada del número de
        // causa, es la vulnerabilidad real contra este material.
        Password = _passwordService.GeneratePassword(SealedPasswordPolicy.BuildOptions());
        _reservedPasswordUsed = false;
    }

    /// <summary>
    /// Verdadero mientras el perfil vigente vino de "Empaquetar en sobre reservado" del menú
    /// contextual: vale para ese paquete y no cambia el perfil predeterminado. Si el operador
    /// cambia el perfil a mano, esa elección sí se guarda, como siempre.
    /// </summary>
    private bool _profileIsOneShot;

    /// <summary>Aplica el perfil Reservado para este paquete sin guardarlo como predeterminado.</summary>
    public void UseReservedProfileOnce()
    {
        _profileIsOneShot = true;
        try
        {
            IsReservedProfile = true;
        }
        finally
        {
            _profileIsOneShot = false;
        }
    }

    private void PersistProfile(bool reserved)
    {
        try
        {
            var config = _configService.Load();
            var profile = reserved ? ConfidentialityProfile.Reserved : ConfidentialityProfile.Standard;
            if (config.Profile != profile)
            {
                config.Profile = profile;
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

    /// <summary>Descarta el aviso del perfil Estándar. No vuelve a aparecer en esta sesión.</summary>
    [RelayCommand]
    private void DismissStandardNotice() => IsStandardNoticeVisible = false;

    public ZipViewModel(
        IZipService zipService,
        IHashService hashService,
        IPasswordService passwordService,
        IReportService reportService,
        ISignatureService signatureService,
        ILocalizationService localization,
        IConfigService configService,
        ILogService logService,
        IOperatorDialogService operatorDialog,
        ISealedPackageBuilder sealedPackageBuilder,
        IDeliveryActService actService,
        ICloudSyncDetector cloudSyncDetector) : base(localization)
    {
        _zipService = zipService;
        _hashService = hashService;
        _passwordService = passwordService;
        _reportService = reportService;
        _signatureService = signatureService;
        _configService = configService;
        _logService = logService;
        _operatorDialog = operatorDialog;
        _sealedPackageBuilder = sealedPackageBuilder;
        _actService = actService;
        _cloudSyncDetector = cloudSyncDetector;

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
    /// <summary>Encabezados de las dos columnas del formulario.</summary>
    public string LabelColumnEvidence => _localization.Get("column_evidence");
    public string LabelColumnConfiguration => _localization.Get("column_configuration");
    public string LabelOptions => _localization.Get("zip_technical_options");
    public string LabelCompression => _localization.Get("compression_level");
    public string LabelHashes => _localization.Get("hash_algorithms");
    public string LabelOutput => _localization.Get("output_file");
    public string LabelSplit => _localization.Get("split_into_volumes");
    public string LabelFormat => _localization.Get("archive_format");
    public string LabelFormatZip => _localization.Get("archive_format_zip");
    public string LabelFormat7z => _localization.Get("archive_format_7z");
    // En Reservado el selector queda deshabilitado: el texto de abajo dice por qué, en vez de solo el tooltip.
    public string FormatHint => _localization.Get(IsReservedProfile ? "archive_format_reserved_hint"
        : Is7zFormat ? "archive_format_7z_hint" : "archive_format_zip_hint");
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
    public string LabelResultHashHint => _localization.Get("result_zip_hash_custody");
    public string LabelSaveReport => _localization.Get("save_report");
    public string LabelSaveReportHint => _localization.Get("save_report_hint");
    public string PickerSaveReportTitle => _localization.Get("picker_save_report_title");
    public string PickerReportHtml => _localization.Get("picker_report_html");
    public string PickerReportTxt => _localization.Get("picker_report_txt");
    public string LabelOpenFolder => _localization.Get("open_folder");
    public string LabelCopyHash => _localization.Get("copy_hash");
    public string LabelNewJob => _localization.Get("new_job");

    // Perfil de confidencialidad
    public string LabelProfile => _localization.Get("label_profile");
    public string LabelProfileStandard => _localization.Get("profile_standard_option");
    public string LabelProfileReserved => _localization.Get("profile_reserved_option");
    public string ProfileHint => _localization.Get(IsReservedProfile ? "profile_reserved_hint" : "profile_standard_hint");
    public string LabelStandardNotice => _localization.Get("profile_standard_notice");
    public string LabelDismissNotice => _localization.Get("dismiss_notice");
    public string TooltipProfileLocked => _localization.Get("profile_locked_tooltip");

    // Acta de entrega
    public string LabelDeliveryAct => _localization.Get("label_delivery_act");
    public string DeliveryActHint => _localization.Get("delivery_act_hint");
    public string LabelSaveAct => _localization.Get("save_act");
    public string LabelCopyAct => _localization.Get("copy_act");
    public string LabelActSaveWarning => _localization.Get("act_save_warning");
    public string LabelIrrecoverableWarning => _localization.Get("sealed_irrecoverable_warning");

    // Pantalla en tres pasos (diseño 2026-10-17).
    public string Heading => _localization.Get("zip_heading");
    public string Subtitle => _localization.Get("zip_subtitle");
    public string LabelStepWhat => _localization.Get("zip_step_what");
    public string LabelStepProtect => _localization.Get("zip_step_protect");
    public string LabelStepWhere => _localization.Get("zip_step_where");
    public string LabelProfileStandardName => _localization.Get("profile_standard");
    public string LabelProfileReservedName => _localization.Get("profile_reserved");
    public string ProfileStandardDesc => _localization.Get("profile_standard_card");
    public string ProfileReservedDesc => _localization.Get("profile_reserved_card");
    public string OutputHint => _localization.Get("zip_output_hint");

    protected override void OnLanguageChanged()
    {
        RefreshAvailableLevels();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(LabelStepWhat));
        OnPropertyChanged(nameof(LabelStepProtect));
        OnPropertyChanged(nameof(LabelStepWhere));
        OnPropertyChanged(nameof(LabelProfileStandardName));
        OnPropertyChanged(nameof(LabelProfileReservedName));
        OnPropertyChanged(nameof(ProfileStandardDesc));
        OnPropertyChanged(nameof(ProfileReservedDesc));
        OnPropertyChanged(nameof(OutputHint));
        OnPropertyChanged(nameof(LabelDropZone));
        OnPropertyChanged(nameof(LabelFiles));
        OnPropertyChanged(nameof(LabelFilesCount));
        OnPropertyChanged(nameof(LabelColumnEvidence));
        OnPropertyChanged(nameof(LabelColumnConfiguration));
        OnPropertyChanged(nameof(LabelOptions));
        OnPropertyChanged(nameof(LabelCompression));
        OnPropertyChanged(nameof(LabelHashes));
        OnPropertyChanged(nameof(LabelOutput));
        OnPropertyChanged(nameof(LabelSplit));
        OnPropertyChanged(nameof(LabelFormat));
        OnPropertyChanged(nameof(LabelFormatZip));
        OnPropertyChanged(nameof(LabelFormat7z));
        OnPropertyChanged(nameof(FormatHint));
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
        OnPropertyChanged(nameof(LabelResultHashHint));
        OnPropertyChanged(nameof(LabelSaveReport));
        OnPropertyChanged(nameof(LabelSaveReportHint));
        OnPropertyChanged(nameof(LabelNewJob));
        OnPropertyChanged(nameof(LabelProfile));
        OnPropertyChanged(nameof(LabelProfileStandard));
        OnPropertyChanged(nameof(LabelProfileReserved));
        OnPropertyChanged(nameof(ProfileHint));
        OnPropertyChanged(nameof(LabelStandardNotice));
        OnPropertyChanged(nameof(LabelDismissNotice));
        OnPropertyChanged(nameof(TooltipProfileLocked));
        OnPropertyChanged(nameof(LabelDeliveryAct));
        OnPropertyChanged(nameof(DeliveryActHint));
        OnPropertyChanged(nameof(LabelSaveAct));
        OnPropertyChanged(nameof(LabelCopyAct));
        OnPropertyChanged(nameof(LabelActSaveWarning));
        OnPropertyChanged(nameof(LabelIrrecoverableWarning));
    }

    private void LoadDefaults()
    {
        var config = _configService.Load();
        SelectedCompression = AvailableLevels.FirstOrDefault(l => l.Level == config.DefaultCompressionLevel) ?? AvailableLevels[3];
        // La carpeta de salida por defecto de Ajustes es una carpeta, no la ruta del paquete: se usa al
        // sugerir el nombre (UpdateOutputPath). Ponerla en OutputPath dejaba empaquetar "sobre" una carpeta.
        _defaultOutputDirectory = config.DefaultOutputDirectory;
        OutputPath = string.Empty;
        IsSimpleMode = config.SimpleMode;
        IsReservedProfile = config.Profile == ConfidentialityProfile.Reserved;
        // El perfil Reservado fija ZIP: la preferencia de 7z no se restaura dentro de él.
        SetFormatFromProfile(config.PreferredFormat == ArchiveFormat.SevenZ && !IsReservedProfile);

        UseMd5 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.MD5);
        UseSha1 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA1);
        UseSha256 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA256);
        UseSha512 = config.DefaultHashAlgorithms.Contains(HashAlgorithmType.SHA512);
    }

    private string? _defaultOutputDirectory;

    public ObservableCollection<SelectedFileItem> Files { get; } = new();

    public void AddPaths(IEnumerable<string> paths) => _ = AddPathsAsync(paths.ToList());

    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        // Al cargar evidencia nueva se abandona la pantalla de resultado anterior
        IsResultVisible = false;

        await _selectionGate.WaitAsync();
        try
        {
            foreach (var path in paths)
            {
                if (Files.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Lo que se ve ahora queda registrado con su hora: si algo falta al empaquetar —por
                // ejemplo, porque el antivirus lo puso en cuarentena mientras tanto—, se documenta.
                var seenAt = DateTimeOffset.UtcNow;
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    Files.Add(new SelectedFileItem
                    {
                        FullPath = fi.FullName,
                        DisplayName = fi.Name,
                        Size = fi.Length,
                        IsFolder = false,
                        Snapshot = new[] { new SelectedSourceFile(fi.FullName, fi.Length, fi.LastWriteTimeUtc, seenAt) }
                    });
                }
                else if (Directory.Exists(path))
                {
                    IsLoadingSelection = true;
                    StatusMessage = string.Format(_localization.Get("selection_loading"),
                        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path);

                    // El mismo recorrido que el empaquetado: sin entrar en enlaces ni junctions.
                    // Una carpeta que Windows no deja recorrer (System Volume Information en la raíz de una unidad) no
                    // impide elegir el resto: el empaquetado la documenta como omitida en el informe y el manifiesto.
                    var snapshot = await Task.Run(() => EvidenceTree.EnumerateFiles(path, null, new List<EvidenceTree.UnreadableFolder>())
                        .Select(f => new FileInfo(f))
                        .Select(fi => new SelectedSourceFile(fi.FullName, fi.Length, fi.LastWriteTimeUtc, seenAt))
                        .ToList());

                    Files.Add(new SelectedFileItem
                    {
                        FullPath = path,
                        // La raíz de una unidad no tiene nombre de carpeta: se muestra "D:\" y no un "\" suelto.
                        DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } folderName
                            ? folderName + Path.DirectorySeparatorChar
                            : Path.GetFullPath(path),
                        Size = snapshot.Sum(f => f.Size),
                        IsFolder = true,
                        FolderFileCount = snapshot.Count,
                        Snapshot = snapshot
                    });
                    StatusMessage = string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            // La carga corre suelta (AddPaths no espera): lo que falle se dice acá o se perdería en silencio.
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_selection_error"), ex.Message);
        }
        finally
        {
            IsLoadingSelection = false;
            _selectionGate.Release();
        }

        UpdateOutputPath();
    }

    /// <summary>
    /// Sugiere la salida: al lado del primer origen (o en la carpeta por defecto de Ajustes), con su nombre, y
    /// libre: si ya hay un paquete así, "caso (2).zip". Con la raíz de una unidad no se sugiere nada.
    /// </summary>
    private void UpdateOutputPath()
    {
        if (Files.Count == 0 || !string.IsNullOrEmpty(OutputPath))
        {
            return;
        }

        var suggested = PackageOutput.DefaultFor(Files[0].FullPath, ArchiveFormats.Extension(EffectiveFormat));
        if (suggested == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_defaultOutputDirectory) && Directory.Exists(_defaultOutputDirectory))
        {
            suggested = Path.Combine(_defaultOutputDirectory, Path.GetFileName(suggested));
        }

        OutputPath = PackageOutput.SuggestFree(suggested);
        _suggestedDesiredPath = suggested;
        _suggestedOutputPath = OutputPath;
    }

    // Lo último que propuso ForZip: el nombre deseado y el libre que quedó (con "(n)" si chocaba).
    private string? _suggestedDesiredPath;
    private string? _suggestedOutputPath;

    /// <summary>
    /// La salida elegida en el diálogo de guardar, que mostraba <paramref name="shownPath"/>. Si el operador solo
    /// cambió de carpeta, el nombre libre se recalcula en la carpeta nueva (ver
    /// <see cref="PackageOutput.RelocateSuggestion"/>).
    /// </summary>
    public void SetPickedOutputPath(string pickedPath, string? shownPath)
    {
        // El nombre que quiere el operador: el de la propuesta de ForZip sin su "(n)", o el que dejó escrito él.
        var desired = SameIgnoringExtension(shownPath, _suggestedOutputPath) ? _suggestedDesiredPath : shownPath;
        OutputPath = PackageOutput.RelocateSuggestion(pickedPath, shownPath, desired);

        bool nameUntouched = !string.IsNullOrEmpty(shownPath) && string.Equals(
            Path.GetFileNameWithoutExtension(pickedPath), Path.GetFileNameWithoutExtension(shownPath),
            StringComparison.OrdinalIgnoreCase);
        _suggestedDesiredPath = nameUntouched ? desired : OutputPath;
        _suggestedOutputPath = OutputPath;
    }

    private static bool SameIgnoringExtension(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(Path.ChangeExtension(Path.GetFullPath(a), null), Path.ChangeExtension(Path.GetFullPath(b), null),
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Antes de empezar: la salida no puede caer dentro de lo seleccionado ni encima de un paquete anterior. Devuelve el mensaje para el operador, o null si se puede seguir.
    /// </summary>
    private string? CheckOutputBeforeStart()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            return _localization.Get("error_output_missing");
        }

        if (PackageOutput.FindSourceContaining(Files.Select(f => f.FullPath), OutputPath) != null)
        {
            return _localization.Get("error_output_inside_source");
        }

        if (PackageOutput.IsFolder(OutputPath))
        {
            return _localization.Get("error_output_is_folder");
        }

        var existing = PackageOutput.FindExisting(OutputPath);
        return existing.Count > 0
            ? string.Format(_localization.Get("error_output_exists"), Path.GetFileName(existing[0]))
            : null;
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
        ResultIncompleteNote = string.Empty;
        _resultPrimaryPath = string.Empty;
        _lastReportData = null;
        CanSaveReport = false;
        LoadDefaults();

        // Si el perfil guardado ya era Reservado, LoadDefaults no dispara el cambio de perfil: lo que el perfil fija
        // (y la contraseña que se acaba de borrar) se repone acá.
        if (IsReservedProfile)
        {
            ApplyReservedDefaults();
        }
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
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_open_folder_error"), ex.Message);
        }
    }

    /// <summary>
    /// "Guardar informe…": arma el informe del último empaquetado y lo escribe donde eligió el operador. No lleva
    /// .sha256: lo que prueba la integridad es la huella del paquete anotada en la planilla de custodia.
    /// </summary>
    /// <param name="shownFileName">El nombre que propuso el diálogo: si el operador no lo tocó pero cambió de tipo
    /// o de carpeta, el "(n)" se recalcula ahí (ver <see cref="PackageOutput.RetargetFileSuggestion"/>).</param>
    public async Task SaveReportAsync(string path, string? shownFileName = null)
    {
        if (_lastReportData == null)
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
            var data = _lastReportData;
            var language = _localization.CurrentLanguage;
            // Sobre miles de archivos armar el texto es trabajo de CPU: en el hilo de UI la ventana deja de repintar.
            // HTML o texto según la extensión que eligió el operador en el diálogo.
            var content = await Task.Run(() => _reportService.GenerateReportFor(path, data, language));
            await _reportService.SaveReportAsync(content, path);
            _logService.Success(_localization.Get("log_report_saved"),
                string.Format(_localization.Get("log_detail_file"), Path.GetFileName(path)));
            ResultNote = string.Format(_localization.Get("result_report_saved"), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_report_save_error"), ex.Message);
            ResultNote = string.Format(_localization.Get("error_io"), ex.Message);
        }
    }

    /// <summary>Carpeta del paquete: el diálogo de guardar el informe abre ahí.</summary>
    public string? ReportFolder => _resultPrimaryPath is { Length: > 0 } p ? Path.GetDirectoryName(p) : null;

    /// <summary>Nombre sugerido para el informe: el del paquete, terminado en ".informe.html".</summary>
    public string SuggestedReportFileName => PackageOutput.FreeFileName(ReportFolder, ReportBaseFileName);

    /// <summary>El nombre del informe sin el "(n)" que le agrega ForZip si ya existe.</summary>
    public string ReportBaseFileName =>
        Path.GetFileName(_resultPrimaryPath is { Length: > 0 } p ? SplitArchive.GetBasePath(p) : "ForZip")
        + _localization.Get("report_file_suffix");

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
        // El perfil manda también acá. En Reservado la clave se regenera con la política del
        // sobre —32 caracteres, con el piso de 24 que la especificación fija—: un botón que la
        // bajara al largo común dejaría el sobre por debajo de su propio mínimo declarado, en
        // silencio y sin que nada avise hasta que el paquete ya está sellado.
        var options = IsReservedProfile
            ? SealedPasswordPolicy.BuildOptions()
            : new PasswordOptions { Length = PasswordOptions.DefaultLength };

        Password = _passwordService.GeneratePassword(options);
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
        if (Files.Count == 0 || IsLoadingSelection) return;

        if (CheckOutputBeforeStart() is { } outputProblem)
        {
            StatusMessage = outputProblem;
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_output_rejected"), outputProblem);
            return;
        }

        // Una contraseña por paquete: si la vigente ya se usó (o se perdió), se genera otra antes de empezar. Se
        // da por usada recién cuando cifró algo que quedó: un sobre, o restos del paso 1 que no se pudieron borrar
        // (ver el finally). Marcarla al empezar cambiaba la contraseña en silencio después de cancelar el diálogo
        // del operador, y la que el operador ya había copiado de la pantalla no abría el sobre siguiente.
        var reservedRun = IsReservedProfile;
        var runOutputPath = OutputPath;
        if (reservedRun && (_reservedPasswordUsed || string.IsNullOrEmpty(Password) || !IsEncryptionEnabled || !UseSha256))
        {
            ApplyReservedDefaults();
        }

        _logService.Info(
            _localization.Get("log_zip_start"),
            string.Format(_localization.Get("log_detail_output"), Path.GetFileName(OutputPath)));
        _logService.Info(string.Format(_localization.Get("log_zip_sources"), Files.Count));
        _logService.Info(string.Format(_localization.Get("log_zip_level"), SelectedCompression.Name));
        if (EffectiveFormat == ArchiveFormat.SevenZ)
        {
            _logService.Info(_localization.Get("log_format_7z"));
        }
        _logService.Info(string.Format(_localization.Get("log_encryption"),
            IsEncryptionEnabled && !string.IsNullOrEmpty(Password) ? "AES-256" : _localization.Get("log_no")));

        IsProcessing = true;
        StatusMessage = _localization.Get("processing");
        ProgressPercent = 0;
        IsResultVisible = false;
        ResultArtifacts.Clear();
        ResultZipHash = string.Empty;
        ResultNote = string.Empty;
        ResultIncompleteNote = string.Empty;
        _lastReportData = null;
        CanSaveReport = false;
        var startTime = DateTime.Now;
        _operationStartedAt = startTime;

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
                SplitSize = IsSimpleMode ? null : ResolveSplitSize(),
                SelectionSnapshot = Files.SelectMany(f => f.Snapshot).ToList(),
                Format = EffectiveFormat
            };

            var compression = await _zipService.CompressAsync(options, progressHandler, ct);

            if (compression.IsSplit)
            {
                _logService.Info(string.Format(_localization.Get("log_zip_split"),
                    compression.Volumes.Count, compression.Volumes.Count.ToString("D3")));
            }

            AddArchiveArtifacts(options, compression);
            ReportOmittedFiles(compression);

            // Con hashes, el diálogo del operador y el manifiesto (con su firma, si se pidió). El informe en texto
            // ya no sale solo: se arma a pedido desde la pantalla de resultado o desde Verificar.
            var manifestGenerated = false;
            if (algorithms.Count > 0 && compression.FileHashes.Count > 0)
            {
                SetPhase("phase_report");
                _logService.Info(_localization.Get("log_operator_confirm"));
                var config = _configService.Load();
                var confirmedResult = await _operatorDialog.ConfirmOperatorAsync(config.Operator, config.TimestampServerUrl);

                _lastOperatorConfirmation = confirmedResult;

                if (confirmedResult != null)
                {
                    await GeneratePackageRecordAsync(options, compression, confirmedResult);
                    manifestGenerated = true;
                    _logService.Success(_localization.Get("log_report_generated"));
                }
                else
                {
                    ResultNote = _localization.Get("result_no_report");
                    _logService.Warning(_localization.Get("log_report_skipped"));
                }
            }

            ProgressPercent = 100;
            StatusMessage = _localization.Get(
                compression.OmittedFiles.Count > 0 ? "zip_success_incomplete" : "zip_success");
            IsResultVisible = true;

            // En Reservado sin manifiesto el paquete se descarta enseguida: anunciarlo como creado contradecía la línea
            // siguiente de la bitácora ("No se selló… se descartó el paquete intermedio").
            if (!IsReservedProfile || manifestGenerated)
            {
                _logService.Success(
                    _localization.Get("log_zip_done"),
                    string.Format(_localization.Get("log_detail_path"), options.OutputPath));
            }

            if (IsReservedProfile)
            {
                if (manifestGenerated)
                {
                    await SealPackageAsync(options, ct);
                }
                else
                {
                    // El sobre lleva el manifiesto: es la contraparte del recibo y lo único contra lo que se puede
                    // verificar el contenido al abrirlo. Sin él no se sella (como en la CLI).
                    DiscardUnsealedPackage(CollectPackageArtifacts(options.OutputPath), "seal_needs_report");
                }
            }
            else
            {
                CanSaveReport = _lastReportData != null;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localization.Get("operation_cancelled");
            _logService.Warning(_localization.Get("log_zip_cancelled"));
        }
        catch (SecuritySoftwareBlockedException blocked)
        {
            // ForZip no analiza contenido: Windows o el EDR negó el acceso. La etapa dice qué
            // quedó en disco, y la bitácora guarda archivo y código para reconstruirlo después.
            var (message, logSummary, logDetail) = SecuritySoftwareInterference.Describe(blocked, _localization);
            StatusMessage = message;
            _logService.Error(logSummary, logDetail);

            // Bloqueado al releerlo, el paquete existe y está completo. Anunciar solo un error
            // hacía creer al operador que no había quedado nada.
            if (blocked.Stage == SecurityBlockStage.RereadingPackage
                && blocked.NativeErrorCode == SecuritySoftwareInterference.VirusInfectedError)
            {
                ResultNote = message;
                IsResultVisible = true;
            }
        }
        catch (Exception ex) when (SecuritySoftwareInterference.IsBlocked(ex))
        {
            // Bloqueo en un punto que no lo envuelve con su etapa: al menos se lo nombra.
            StatusMessage = _localization.Get("error_security_software_blocked");
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_zip_error"), ex.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message);
            _logService.LogEvidence(LogLevel.Error, _localization.Get("log_zip_error"), ex.Message);
        }
        finally
        {
            if (reservedRun && !_reservedPasswordUsed && PackageLeftOnDisk(runOutputPath))
            {
                // Quedó en disco algo cifrado con esta contraseña: el paquete siguiente lleva otra.
                _reservedPasswordUsed = true;
            }

            IsProcessing = false;
        }
    }

    /// <summary>
    /// Deja constancia de cada archivo que el antivirus no dejó leer: una línea de bitácora por
    /// archivo, con ruta y código, y el aviso de paquete incompleto en la pantalla de resultado.
    /// </summary>
    private void ReportOmittedFiles(CompressionResult compression)
    {
        if (compression.OmittedFiles.Count == 0)
        {
            return;
        }

        foreach (var omitted in compression.OmittedFiles)
        {
            var summaryKey = omitted.Reason switch
            {
                OmissionReason.SecuritySoftwareBlocked => "log_omitted_blocked",
                OmissionReason.MissingAtPackaging => "log_omitted_missing",
                OmissionReason.LinkNotFollowed => "log_omitted_link",
                OmissionReason.FolderUnreadable => "log_omitted_folder",
                _ => "log_omitted_vanished"
            };
            var detail = omitted.WindowsErrorCode.HasValue
                ? string.Format(_localization.Get("log_security_blocked_detail"),
                    omitted.SourcePath, omitted.WindowsErrorCode, omitted.SystemMessage)
                : string.Format(_localization.Get("log_detail_path"), omitted.SourcePath);
            _logService.Warning(_localization.Get(summaryKey), detail);
        }

        // En pantalla van los primeros nombres; la lista completa está en el informe y el manifiesto.
        const int shown = 5;
        var names = string.Join(", ", compression.OmittedFiles.Take(shown).Select(o => o.EntryName));
        if (compression.OmittedFiles.Count > shown)
        {
            names += " " + string.Format(_localization.Get("result_incomplete_more"),
                compression.OmittedFiles.Count - shown);
        }

        ResultIncompleteNote = string.Format(_localization.Get("result_incomplete_note"),
            compression.OmittedFiles.Count, names);
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
        try
        {
            return await ComputeLogicalZipHashCoreAsync(outputPath, compression);
        }
        catch (Exception ex) when (SecuritySoftwareBlockedException.Wrap(
            SecurityBlockStage.RereadingPackage, outputPath, ex) is { } blocked && blocked != ex)
        {
            // Es el único paso que vuelve a abrir el ZIP terminado, y con eso el antivirus lo
            // revisa por dentro. El paquete ya está completo: hay que decirlo con esa etapa.
            throw blocked;
        }
    }

    private async Task<(string hash, long size)> ComputeLogicalZipHashCoreAsync(string outputPath, CompressionResult compression)
    {
        // Este es el paso caro de la etapa del informe: relee el paquete entero. Sin progreso
        // la ventana se queda muda varios minutos sobre evidencia pesada y parece colgada.
        var progress = BuildPhaseProgress("phase_zip_hash");

        if (compression.IsSplit)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? string.Empty;
            var segments = compression.Volumes.Select(v => Path.Combine(dir, v.FileName)).ToList();
            var size = compression.Volumes.Sum(v => v.Size);

            await using var logical = new ConcatenatedReadStream(segments);
            var hashes = await _hashService.ComputeHashesAsync(
                logical, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
                size, progress, CancellationToken.None);
            return (hashes[HashAlgorithmType.SHA256], size);
        }

        var single = await _hashService.ComputeHashesAsync(
            outputPath, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
            progress, CancellationToken.None);
        return (single.Hashes[HashAlgorithmType.SHA256], new FileInfo(outputPath).Length);
    }

    /// <summary>
    /// Marca el comienzo de una etapa posterior al empaquetado y deja la barra en cero.
    /// </summary>
    /// <remarks>
    /// Las etapas del informe no tienen un total común contra el cual medir —una relee el
    /// paquete, otra escribe un archivo, otra habla con una TSA—, así que cada una reporta su
    /// propio avance desde cero en lugar de fingir un porcentaje global que no significa nada.
    /// </remarks>
    private void SetPhase(string phaseKey)
    {
        _currentPhase = _localization.Get(phaseKey);
        ProgressPercent = 0;
        ProgressDetail = _currentPhase;
        StatusMessage = _currentPhase;

        // La estimación de la etapa anterior no vale para esta: dejarla puesta diría un número
        // falso, y en una herramienta forense un dato de más es peor que un dato de menos.
        RemainingTime = "--:--:--";
        UpdateElapsed();
    }

    /// <summary>
    /// Refresca el reloj de la operación. Sin esto el contador queda congelado en cuanto
    /// termina el empaquetado, que es exactamente la señal de "se colgó" que hay que evitar.
    /// </summary>
    private void UpdateElapsed()
    {
        ElapsedTime = (DateTime.Now - _operationStartedAt).ToString(@"hh\:mm\:ss");
    }

    private DateTime _operationStartedAt = DateTime.Now;

    /// <summary>
    /// Progreso de una etapa que sí sabe cuánto le falta, expresado en fracción (0 a 1).
    /// </summary>
    private IProgress<double> BuildPhaseProgress(string phaseKey)
    {
        SetPhase(phaseKey);
        var label = _currentPhase;

        return new Progress<double>(fraction =>
        {
            var percent = Math.Clamp(fraction, 0, 1) * 100;
            ProgressPercent = percent;
            ProgressDetail = string.Format(_localization.Get("phase_detail_percent"), label, percent);
            UpdateElapsed();
        });
    }

    private string _currentPhase = string.Empty;

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
                "IconPackage", volumesLabel, _localization.Get("result_volumes_desc"), _resultPrimaryPath));
        }
        else
        {
            _resultPrimaryPath = options.OutputPath;
            ResultArtifacts.Add(new ResultArtifactItem(
                "IconPackage", Path.GetFileName(options.OutputPath), _localization.Get("result_zip_desc"), options.OutputPath));
        }
    }

    /// <summary>
    /// Lo que queda registrado del empaquetado: la huella del paquete, el manifiesto y su firma. Los datos quedan en
    /// memoria para "Guardar informe…".
    /// </summary>
    private async Task GeneratePackageRecordAsync(ZipOptions options, CompressionResult compression, OperatorConfirmationResult result)
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
            ArchiveFormat = options.Format,
            ZipFileSize = zipSize,
            ZipHash = zipHash,
            Volumes = compression.Volumes.ToList(),
            FileResults = compression.FileHashes,
            OmittedFiles = compression.OmittedFiles
        };

        _lastReportData = data;

        // Manifiesto JSON (fuente de verdad legible por máquina para verificación automática)
        SetPhase("phase_manifest");
        var manifestPath = options.OutputPath + ".manifest.json";
        var manifestJson = await Task.Run(() => _reportService.GenerateManifestJson(data));
        await File.WriteAllTextAsync(manifestPath, manifestJson);
        _logService.Info(
            _localization.Get("log_manifest_generated"),
            string.Format(_localization.Get("log_detail_file"), Path.GetFileName(manifestPath)));
        ResultArtifacts.Add(new ResultArtifactItem(
            "IconFile", Path.GetFileName(manifestPath), _localization.Get("result_manifest_desc"), manifestPath));

        // Firma digital del manifiesto (CMS/PKCS#7) si el operador la solicitó,
        // con sello de tiempo RFC 3161 opcional
        if (result.SignManifest && !string.IsNullOrWhiteSpace(result.CertificatePath))
        {
            // Con sello de tiempo esto sale a la red y puede tardar: hay que decirlo.
            SetPhase("phase_signature");
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
                    "IconVerify", Path.GetFileName(signaturePath),
                    _localization.Get(sealed_ ? "result_signature_ts_desc" : "result_signature_desc"),
                    signaturePath));
            }
            catch (TimestampUnavailableException ex)
            {
                // La firma sí se escribió; solo faltó el sello de tiempo
                _logService.LogEvidence(LogLevel.Warning, _localization.Get("log_timestamp_missing"), ex.Message);
                ResultArtifacts.Add(new ResultArtifactItem(
                    "IconVerify", Path.GetFileName(signaturePath),
                    _localization.Get("result_signature_desc"), signaturePath));
                ResultNote = _localization.Get("result_timestamp_failed");
            }
            catch (Exception ex)
            {
                _logService.LogEvidence(LogLevel.Error, _localization.Get("log_sign_error"), ex.Message);
            }
        }
    }

    private OperatorConfirmationResult? _lastOperatorConfirmation;

    /// <summary>
    /// Paso 2 del perfil Reservado: encierra el paquete recién generado en el sobre, emite el
    /// recibo, prepara el acta y borra de disco todo lo que quedó en claro.
    /// </summary>
    private async Task SealPackageAsync(ZipOptions options, CancellationToken ct)
    {
        var artifacts = CollectPackageArtifacts(options.OutputPath);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))
            ?? Directory.GetCurrentDirectory();

        _logService.Info(string.Format(_localization.Get("log_seal_start"), artifacts.Count));
        StatusMessage = _localization.Get("sealing_in_progress");

        var sealOptions = new SealedPackageOptions
        {
            ArtifactPaths = artifacts,
            OutputDirectory = outputDirectory,
            Password = options.Password ?? string.Empty,
            SigningCertificatePath = _lastOperatorConfirmation?.SignManifest == true
                ? _lastOperatorConfirmation.CertificatePath
                : null,
            SigningCertificatePassword = _lastOperatorConfirmation?.CertificatePassword,
            TimestampUrl = _lastOperatorConfirmation?.TimestampUrl
        };

        try
        {
            // Sellar un paquete grande lleva minutos (cifrar y después abrirlo de prueba): con avance, no parece colgado.
            var phase = BuildPhaseProgress("phase_seal");
            var sealProgress = new Progress<(long processed, long total)>(p =>
            {
                if (p.total > 0)
                {
                    phase.Report((double)p.processed / p.total);
                }
            });
            var result = await _sealedPackageBuilder.BuildAsync(sealOptions, sealProgress, ct);
            _reservedPasswordUsed = true;

            _sealedPackageId = result.Header.BaseName;
            _logService.Success(string.Format(_localization.Get("log_seal_done"), _sealedPackageId));

            // La pantalla de resultado se rearma: los artefactos del paso 1 ya no existen, y
            // seguir mostrándolos sería decirle al operador que suba archivos que no están.
            ResultArtifacts.Clear();
            _resultPrimaryPath = result.SealedPath;

            ResultArtifacts.Add(new ResultArtifactItem(
                "IconEnvelope", Path.GetFileName(result.SealedPath),
                _localization.Get("result_sealed_desc"), result.SealedPath));
            ResultArtifacts.Add(new ResultArtifactItem(
                "IconFile", Path.GetFileName(result.ReceiptPath),
                _localization.Get("result_receipt_desc"), result.ReceiptPath));

            if (result.ReceiptSignaturePath != null)
            {
                ResultArtifacts.Add(new ResultArtifactItem(
                    "IconVerify", Path.GetFileName(result.ReceiptSignaturePath),
                    _localization.Get("result_receipt_signature_desc"), result.ReceiptSignaturePath));
            }

            if (result.OpenerPagePath != null)
            {
                ResultArtifacts.Add(new ResultArtifactItem(
                    "IconFile", Path.GetFileName(result.OpenerPagePath),
                    _localization.Get("result_opener_desc"), result.OpenerPagePath));
            }
            else
            {
                _logService.Warning(_localization.Get("result_opener_missing"));
            }

            ResultZipHash = result.Receipt.SealedSha256;
            DeliveryActText = BuildDeliveryAct(result, options.Password ?? string.Empty);
            IsSealedResultVisible = true;

            if (result.TimestampWarning != null)
            {
                ResultNote = _localization.Get("result_timestamp_failed");
            }

            if (result.UndeletedTempPath != null)
            {
                ResultNote = string.Format(_localization.Get("result_temp_left"), result.UndeletedTempPath);
            }

            DeletePackageArtifacts(artifacts);
            StatusMessage = _localization.Get("seal_success");
            // Con el sobre hecho, un informe fuera de él dejaría en claro lo que el perfil encierra.
            _lastReportData = null;
        }
        catch (OperationCanceledException)
        {
            DiscardUnsealedPackage(artifacts, "seal_discarded");
            throw;
        }
        catch (Exception ex) when (SecuritySoftwareBlockedException.Wrap(
            SecurityBlockStage.Sealing, outputDirectory, ex) is { } blocked)
        {
            // El sellador no dice qué archivo tocaba; se nombra la carpeta del paquete.
            var (message, logSummary, logDetail) = SecuritySoftwareInterference.Describe(blocked, _localization);
            _logService.Error(logSummary, logDetail);
            DiscardUnsealedPackage(artifacts, "seal_discarded");
            StatusMessage = message + Environment.NewLine + StatusMessage;
        }
        catch (Exception ex)
        {
            // Cualquier falla, no solo de E/S: un certificado que no abre, por ejemplo, también dejaba el paso 1
            // en disco. El detalle va a la bitácora como evidencia (puede nombrar archivos).
            _logService.Error(_localization.Get("log_seal_error"), ex.Message);
            DiscardUnsealedPackage(artifacts, "seal_discarded");
            StatusMessage = string.Format(_localization.Get("error_io"), ex.Message) + Environment.NewLine + StatusMessage;
        }
    }

    /// <summary>
    /// Perfil Reservado sin sobre: se borra el paquete intermedio (ZIP, informe, manifiesto). Quedaba en disco con
    /// los datos de la causa en claro y cifrado con una contraseña que solo iba a estar en el acta. La evidencia
    /// original no se toca: se puede volver a empaquetar.
    /// </summary>
    private void DiscardUnsealedPackage(List<string> artifacts, string reasonKey)
    {
        DeletePackageArtifacts(artifacts);
        ResultArtifacts.Clear();
        ResultZipHash = string.Empty;
        _resultPrimaryPath = string.Empty;
        IsResultVisible = false;
        StatusMessage = _localization.Get(reasonKey);
        _logService.Warning(_localization.Get(reasonKey));
    }

    /// <summary>Verdadero si del paso 1 quedó algo en disco (el ZIP, sus volúmenes o un sidecar).</summary>
    private static bool PackageLeftOnDisk(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return false;
        }

        try
        {
            return CollectPackageArtifacts(outputPath).Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Sin poder mirar, lo prudente es no reusar la contraseña.
            return true;
        }
    }

    /// <summary>Reúne el ZIP (o sus volúmenes), el manifiesto y su firma, si se generaron.</summary>
    private static List<string> CollectPackageArtifacts(string outputPath)
    {
        var artifacts = new List<string>();
        artifacts.AddRange(SplitArchive.ResolveSegments(Path.GetFullPath(outputPath)).Where(File.Exists));

        var manifestPath = outputPath + ".manifest.json";

        foreach (var candidate in new[] { manifestPath, manifestPath + ".p7s" })
        {
            if (File.Exists(candidate))
            {
                artifacts.Add(Path.GetFullPath(candidate));
            }
        }

        return artifacts;
    }

    /// <summary>
    /// Borra los artefactos que ya quedaron encerrados en el sobre: fuera de él no debe quedar
    /// nada en claro.
    /// </summary>
    private void DeletePackageArtifacts(IEnumerable<string> artifacts)
    {
        foreach (var path in artifacts)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logService.Warning(_localization.Get("log_cleanup_failed"), path);
            }
        }
    }

    private string BuildDeliveryAct(SealedPackageResult result, string password)
    {
        var data = new DeliveryActData
        {
            PackageId = result.Header.PackageId,
            SealedFileName = Path.GetFileName(result.SealedPath),
            SealedSha256 = result.Receipt.SealedSha256,
            SealedAtUtc = result.Header.SealedAtUtc,
            Password = password,
            CaseNumber = _lastOperatorConfirmation?.CaseNumber,
            CaseDescription = _lastOperatorConfirmation?.CaseDescription,
            Court = _lastOperatorConfirmation?.Court,
            Operator = _lastOperatorConfirmation?.Operator
        };

        return _actService.Generate(data, _localization.CurrentLanguage);
    }

    /// <summary>
    /// Guarda el acta en la ruta que el operador eligió explícitamente. Nunca se escribe sola
    /// junto al sobre: lleva la contraseña en claro.
    /// </summary>
    public async Task SaveDeliveryActAsync(string path)
    {
        await _actService.SaveAsync(DeliveryActText, path);
        ResultNote = _localization.Get("result_act_saved");
        _logService.Warning(_localization.Get("log_act_saved"));
    }

    /// <summary>Nombre sugerido para el acta, derivado del identificador del paquete.</summary>
    public string SuggestedActFileName => string.IsNullOrEmpty(_sealedPackageId)
        ? "acta.txt"
        : _sealedPackageId + DeliveryActService.ActSuffix;

    /// <summary>
    /// Revisa el destino elegido contra las carpetas sincronizadas conocidas. La detección es
    /// heurística: ante la duda avisa, porque un falso positivo cuesta un clic y un falso
    /// negativo cuesta la evidencia.
    /// </summary>
    public void CheckCloudSyncDestination()
    {
        var target = string.IsNullOrEmpty(OutputPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(OutputPath));

        var match = _cloudSyncDetector.Detect(target);
        CloudSyncWarning = match == null
            ? string.Empty
            : string.Format(_localization.Get("cloud_sync_warning"), match.Provider, match.RootPath);
    }

    /// <summary>
    /// Copia el acta al portapapeles. Es la vía que no toca el disco: el acta lleva la
    /// contraseña en claro y su destino natural es el papel, no un archivo.
    /// </summary>
    [RelayCommand]
    private async Task CopyDeliveryActAsync()
    {
        if (string.IsNullOrEmpty(DeliveryActText))
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(DeliveryActText);
                _logService.Warning(_localization.Get("log_act_copied"));
            }
        }
    }
}
