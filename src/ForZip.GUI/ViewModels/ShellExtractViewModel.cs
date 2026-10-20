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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Interfaces;
using ForZip.Core.Sealing;
using ForZip.Core.Shell;

namespace ForZip.GUI.ViewModels;

/// <summary>
/// Ventana "Extraer" del menú contextual: los archivos elegidos con "Extraer aquí", "Extraer en Nombre\" o
/// "Extraer…", sin abrir ForZip entero. Cada archivo es un <see cref="UnzipViewModel"/> propio, así que extrae,
/// avisa y deja en la bitácora exactamente lo mismo que la pantalla Extraer.
/// </summary>
/// <remarks>
/// "Extraer aquí" y "Extraer en Nombre\" arrancan solos: el operador ya eligió la acción y el destino en el menú.
/// "Extraer…" espera a que confirme el destino. Los archivos se extraen de a uno, en el orden en que llegaron
/// (el Explorador lanza un proceso por archivo seleccionado y los demás reenvían su ruta a esta ventana).
/// </remarks>
public partial class ShellExtractViewModel : ViewModelBase
{
    private readonly Func<UnzipViewModel> _newUnzip;
    private readonly ILogService _logService;

    /// <summary>Evita dos bombas de extracción a la vez (ver <see cref="PumpAsync"/>).</summary>
    private bool _pumping;

    public ShellExtractViewModel(Func<UnzipViewModel> newUnzip, ILogService logService,
                                 ILocalizationService localization) : base(localization)
    {
        _newUnzip = newUnzip;
        _logService = logService;
    }

    public ObservableCollection<ShellExtractItem> Items { get; } = new();

    /// <summary>Para el diálogo de cierre, que la ventana abre por su cuenta.</summary>
    public ILocalizationService Localization => _localization;

    public string Heading => _localization.Get("shell_extract_heading");
    public string ButtonClose => _localization.Get("quick_hash_close");
    public bool IsElevated { get; } = Environment.IsPrivilegedProcess;
    public string LabelElevated => _localization.Get("status_running_elevated_tip");

    /// <summary>Hay una extracción en curso o esperando turno: cerrar la ventana la cortaría.</summary>
    public bool IsOperationRunning => Items.Any(i => i.IsQueued || i.Unzip.IsProcessing);

    public string Summary => string.Format(_localization.Get("shell_extract_summary"),
        Items.Count, Items.Count(i => i.Unzip.HasResult));

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(ButtonClose));
        OnPropertyChanged(nameof(LabelElevated));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Subcarpetas ya asignadas en esta ventana: dos archivos con el mismo nombre no van a la misma.</summary>
    private readonly List<string> _claimedDestinations = new();

    /// <summary>Suma los archivos de una invocación del menú. Uno que ya está en la ventana no se repite.</summary>
    public void AddRequest(ShellRequest request)
    {
        foreach (var path in request.Paths)
        {
            var full = Path.GetFullPath(path);
            if (Items.Any(i => string.Equals(i.ArchivePath, full, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var unzip = _newUnzip();
            var item = new ShellExtractItem(unzip, request.Verb, full, _localization);
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);

            if (full.EndsWith(SealedContainerFormat.SealedExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Llega acá solo si el sobre vino junto con otros archivos: se abre desde ForZip, con su destino.
                item.MarkUnsupported(_localization.Get("shell_extract_sealed"));
                continue;
            }

            unzip.SetZipPath(full);
            unzip.OutputDirectory = UnzipViewModel.DestinationFor(request.Verb, full, _claimedDestinations);
            if (request.Verb != ShellVerb.Extract)
            {
                Enqueue(item);
            }
        }

        _logService.Info(string.Format(_localization.Get("log_shell_action"), request.Verb, request.Paths.Count));
        OnPropertyChanged(nameof(Summary));
        _ = PumpAsync();
    }

    /// <summary>"Extraer" de una ficha: con el destino confirmado o la contraseña escrita.</summary>
    [RelayCommand]
    private void Start(ShellExtractItem item)
    {
        if (!item.CanEdit)
        {
            return;
        }

        Enqueue(item);
        _ = PumpAsync();
    }

    [RelayCommand]
    private void Cancel(ShellExtractItem item)
    {
        if (item.IsQueued)
        {
            item.IsQueued = false;
            return;
        }

        item.Unzip.DecompressCancelCommand.Execute(null);
    }

    /// <summary>Al cerrar la ventana con trabajo pendiente: lo que espera turno no arranca y lo que corre se corta.</summary>
    public void CancelAll()
    {
        foreach (var item in Items)
        {
            item.IsQueued = false;
            if (item.Unzip.IsProcessing)
            {
                item.Unzip.DecompressCancelCommand.Execute(null);
            }
        }
    }

    private static void Enqueue(ShellExtractItem item)
    {
        item.IsQueued = true;
        item.Unzip.StatusMessage = string.Empty;
    }

    private async Task PumpAsync()
    {
        if (_pumping)
        {
            return;
        }

        _pumping = true;
        try
        {
            while (Items.FirstOrDefault(i => i.IsQueued) is { } next)
            {
                // El comando pone IsProcessing antes de su primer await: la ficha pasa de "en espera" a "extrayendo"
                // sin quedar un instante editable en el medio.
                var run = next.Unzip.DecompressCommand.ExecuteAsync(null);
                next.IsQueued = false;
                await run;
            }
        }
        finally
        {
            _pumping = false;
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(IsOperationRunning));
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsOperationRunning));
    }
}

/// <summary>Un archivo de la ventana "Extraer": su extracción y lo que la ficha muestra según el momento.</summary>
public partial class ShellExtractItem : ObservableObject
{
    private readonly ILocalizationService _localization;

    public ShellExtractItem(UnzipViewModel unzip, ShellVerb verb, string archivePath, ILocalizationService localization)
    {
        Unzip = unzip;
        Verb = verb;
        ArchivePath = archivePath;
        _localization = localization;
        Unzip.PropertyChanged += (_, _) => NotifyState();
    }

    public UnzipViewModel Unzip { get; }
    public ShellVerb Verb { get; }
    public string ArchivePath { get; }
    public string FileName => Path.GetFileName(ArchivePath);

    /// <summary>Espera su turno para extraer.</summary>
    [ObservableProperty]
    private bool _isQueued;

    /// <summary>Un sobre u otra cosa que esta ventana no extrae: la ficha solo dice por qué.</summary>
    [ObservableProperty]
    private bool _isUnsupported;

    public void MarkUnsupported(string reason)
    {
        IsUnsupported = true;
        Unzip.StatusMessage = reason;
    }

    partial void OnIsQueuedChanged(bool value) => NotifyState();

    /// <summary>Ni extrayendo, ni en espera, ni terminado: el operador puede tocar destino o contraseña y extraer.</summary>
    public bool CanEdit => !IsUnsupported && !IsQueued && !Unzip.IsProcessing && !Unzip.HasResult;

    /// <summary>"Extraer…": el destino se confirma (o se cambia) antes de extraer.</summary>
    public bool ShowDestinationEditor => CanEdit && Verb == ShellVerb.Extract;

    public bool ShowDestination => !ShowDestinationEditor && !IsUnsupported;
    public bool ShowPasswordEditor => CanEdit && Unzip.NeedsPassword;
    public bool ShowCancel => IsQueued || Unzip.IsProcessing;
    public string DestinationText => "→ " + Unzip.OutputDirectory;

    public string StatusText => IsQueued ? _localization.Get("shell_extract_waiting") : Unzip.StatusMessage;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText) && !Unzip.HasResult;
    public bool IsStatusError => !IsQueued && Unzip.IsStatusError;

    public string LabelDestination => _localization.Get("destination_folder");
    // El campo aparece recién cuando el archivo resultó cifrado: ahí la contraseña ya no es "opcional".
    public string LabelPassword => _localization.Get("shell_extract_password");
    public string ButtonExtract => _localization.Get("extract");
    public string ButtonCancel => _localization.Get("cancel");

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowDestinationEditor));
        OnPropertyChanged(nameof(ShowDestination));
        OnPropertyChanged(nameof(ShowPasswordEditor));
        OnPropertyChanged(nameof(ShowCancel));
        OnPropertyChanged(nameof(DestinationText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsStatusError));
    }
}
