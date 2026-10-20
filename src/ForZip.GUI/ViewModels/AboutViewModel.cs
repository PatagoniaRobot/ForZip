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

using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.GUI.Services;

namespace ForZip.GUI.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly IUpdateService _updateService;
    private readonly IUpdateInstaller _installer;
    private readonly IUpdateDialogService _updateDialog;
    private readonly IConfigService _configService;
    private readonly ILogService _logService;

    /// <summary>Última consulta, de donde salen los adjuntos a descargar.</summary>
    private UpdateCheckResult? _lastCheck;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateProgress;

    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string _notesHeading = string.Empty;

    /// <summary>Verdadero cuando la instalación es posible: hay adjunto y se puede escribir.</summary>
    [ObservableProperty]
    private bool _canInstallUpdate;

    public AboutViewModel(
        ILocalizationService localization,
        IUpdateService updateService,
        IUpdateInstaller installer,
        IUpdateDialogService updateDialog,
        IConfigService configService,
        ILogService logService)
    {
        _localization = localization;
        _updateService = updateService;
        _installer = installer;
        _updateDialog = updateDialog;
        _configService = configService;
        _logService = logService;
    }

    public string AppName => "ForZip";
    public string Version => Core.AppInfo.DisplayVersion;
    public string Subtitle => "Forensic ZIP Tool — Open Source";
    public string Author => "Claudio Andino";
    public string Email => "claudio@patagoniarobot.com";
    public string LicenseName => "Apache License 2.0";
    public string Initiative => "Patagonia Robot";
    public string DisclaimerText => _localization.Get("report_disclaimer_text");

    public string LabelUpdates => _localization.Get("update_section");
    public string ButtonCheckUpdates => _localization.Get("update_check");
    public string ButtonOpenRelease => _localization.Get("update_open_release");
    public string ButtonInstallUpdate => _localization.Get("update_install");
    public string LabelManualNote => _localization.Get("update_manual_note");

    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesHeading);
    public bool IsBusy => IsUpdating;

    /// <summary>
    /// Al arrancar después de un reemplazo: muestra de dónde se vino y las novedades que se
    /// guardaron antes de reiniciar, y limpia esas marcas para no repetir el aviso.
    /// </summary>
    public void ShowPostUpdateSummary(string previousVersion, string? notes)
    {
        UpdateStatusMessage = string.Format(
            CultureInfo.InvariantCulture,
            _localization.Get("update_installed"),
            previousVersion,
            Core.AppInfo.Version);

        NotesHeading = _localization.Get("update_notes_installed");
        ReleaseNotes = notes ?? string.Empty;
        IsUpdateAvailable = false;
        CanInstallUpdate = false;
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken ct)
    {
        IsCheckingUpdates = true;
        UpdateStatusMessage = _localization.Get("update_checking");
        ClearNotes();

        try
        {
            ApplyResult(await _updateService.CheckAsync(ct));
        }
        catch (OperationCanceledException)
        {
            UpdateStatusMessage = string.Empty;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    public void ApplyResult(UpdateCheckResult result)
    {
        _lastCheck = result;
        IsUpdateAvailable = result.Status == UpdateCheckStatus.UpdateAvailable;

        UpdateStatusMessage = result.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_available"),
                result.LatestVersion),

            UpdateCheckStatus.UpToDate => _localization.Get("update_up_to_date"),

            _ => string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_failed"),
                result.ErrorMessage)
        };

        if (IsUpdateAvailable)
        {
            NotesHeading = _localization.Get("update_notes");
            ReleaseNotes = result.ReleaseNotes ?? string.Empty;
        }
        else
        {
            ClearNotes();
        }

        RefreshInstallAvailability();
    }

    /// <summary>
    /// La instalación se ofrece solo si la publicación adjunta un binario que corresponde a
    /// este ejecutable y si se puede escribir donde vive. Correr desde un medio de solo lectura
    /// es un caso normal en pericia, no un error.
    /// </summary>
    private void RefreshInstallAvailability()
    {
        if (!IsUpdateAvailable || _lastCheck == null)
        {
            CanInstallUpdate = false;
            return;
        }

        var asset = _lastCheck.FindAsset(_installer.CurrentExeName);
        CanInstallUpdate = asset != null && _installer.CanInstall();

        if (asset == null)
        {
            UpdateStatusMessage += " " + string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_no_asset"),
                _installer.CurrentExeName);
        }
        else if (!CanInstallUpdate)
        {
            UpdateStatusMessage += " " + _localization.Get("update_readonly");
        }
    }

    /// <summary>
    /// Descarga, verifica, reemplaza y reinicia. El orden importa: nada se toca hasta que el
    /// binario descargado coincide con la huella publicada. Si la descarga falla a mitad de
    /// camino, la instalación en uso queda intacta.
    /// </summary>
    [RelayCommand]
    private async Task InstallUpdateAsync(CancellationToken ct)
    {
        if (_lastCheck == null || !IsUpdateAvailable)
        {
            return;
        }

        var asset = _lastCheck.FindAsset(_installer.CurrentExeName);
        if (asset == null)
        {
            return;
        }

        var checksum = _lastCheck.FindChecksumFor(_installer.CurrentExeName);
        var latest = _lastCheck.LatestVersion ?? "?";

        if (!await _updateDialog.ConfirmUpdateAsync(latest, asset.Size))
        {
            return;
        }

        IsUpdating = true;
        UpdateProgress = 0;
        UpdateStatusMessage = _localization.Get("update_downloading");

        try
        {
            var destination = Path.Combine(
                Path.GetTempPath(), $"ForZip-update-{latest}", asset.Name);

            var progress = new Progress<double>(percent => UpdateProgress = percent);
            var download = await _updateService.DownloadAndVerifyAsync(asset, checksum, destination, progress, ct);

            if (!download.IsVerified)
            {
                UpdateStatusMessage = DescribeFailure(download);
                _logService.Error(UpdateStatusMessage);
                return;
            }

            UpdateStatusMessage = _localization.Get("update_verified");
            _logService.Success(string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("log_update_verified"),
                download.ActualSha256));

            // Se guarda ANTES de reemplazar: si el proceso muere en el medio, el arranque
            // siguiente igual puede contar qué pasó.
            var config = _configService.Load();
            config.UpdatedFromVersion = Core.AppInfo.Version;
            config.UpdatedReleaseNotes = _lastCheck.ReleaseNotes;
            _configService.Save(config);

            _logService.Info(string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("log_update_installing"),
                Core.AppInfo.Version,
                latest));

            var outcome = _installer.ReplaceAndRelaunch(download.FilePath!);
            if (!outcome.Success)
            {
                // No se instaló: hay que borrar las marcas o el próximo arranque mentiría.
                config.UpdatedFromVersion = null;
                config.UpdatedReleaseNotes = null;
                _configService.Save(config);

                UpdateStatusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    _localization.Get("update_install_failed"),
                    outcome.ErrorMessage);
                _logService.Error(UpdateStatusMessage);
                return;
            }

            Shutdown();
        }
        catch (OperationCanceledException)
        {
            UpdateStatusMessage = _localization.Get("operation_cancelled");
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                _localization.Get("update_install_failed"),
                ex.Message);
            _logService.Error(UpdateStatusMessage);
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private string DescribeFailure(UpdateDownloadResult download) => download.Status switch
    {
        UpdateDownloadStatus.ChecksumMissing => string.Format(
            CultureInfo.InvariantCulture,
            _localization.Get("update_no_checksum"),
            download.ErrorMessage),

        UpdateDownloadStatus.ChecksumMismatch => string.Format(
            CultureInfo.InvariantCulture,
            _localization.Get("update_checksum_mismatch"),
            download.ExpectedSha256,
            download.ActualSha256),

        _ => string.Format(
            CultureInfo.InvariantCulture,
            _localization.Get("update_download_failed"),
            download.ErrorMessage)
    };

    private void ClearNotes()
    {
        NotesHeading = string.Empty;
        ReleaseNotes = string.Empty;
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        OpenWithDefaultApp(_lastCheck?.ReleaseUrl ?? _updateService.ReleasesPageUrl);
    }

    [RelayCommand]
    private void OpenHelp()
    {
        var helpPath = MaterializeHelp() ?? Path.Combine(AppContext.BaseDirectory, "Help_ForZip.html");
        if (File.Exists(helpPath))
        {
            OpenWithDefaultApp(helpPath);
        }
    }

    /// <summary>
    /// Escribe la ayuda que viaja dentro del ejecutable en un temporal con el número de versión y devuelve su ruta.
    /// Así se abre siempre la de esta versión: la actualización reemplaza solo el .exe, y la copia que está al lado
    /// quedaba en la versión anterior. Null si no se pudo (se usa la de al lado).
    /// </summary>
    private static string? MaterializeHelp()
    {
        try
        {
            using var resource = typeof(AboutViewModel).Assembly.GetManifestResourceStream("ForZip.Help_ForZip.html");
            if (resource == null)
            {
                return null;
            }

            var directory = Path.Combine(Path.GetTempPath(), "ForZip");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"Help_ForZip-{Core.AppInfo.Version}.html");
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                resource.CopyTo(file);
            }

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [RelayCommand]
    private void OpenLicenses()
    {
        var path = MaterializeLicenses();
        if (path != null)
        {
            OpenWithDefaultApp(path);
        }
    }

    /// <summary>
    /// Escribe en un temporal las licencias embebidas (como la ayuda: las de al lado pueden ser de la 1.4, o no estar
    /// si se actualizó desde ella) y devuelve su ruta. Null si no se pudo.
    /// </summary>
    private static string? MaterializeLicenses()
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "ForZip");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"ForZip-licencias-{Core.AppInfo.Version}.txt");
            File.WriteAllText(path, Core.Services.LicenseTexts.Combined(), new System.Text.UTF8Encoding(true));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        OpenWithDefaultApp("https://github.com/patagoniarobot/forzip");
    }

    private static void OpenWithDefaultApp(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Si el sistema no puede abrir el recurso, ignoramos silenciosamente: no es crítico
        }
    }
}
