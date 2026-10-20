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
using ForZip.Core.Shell;

namespace ForZip.GUI.ViewModels;

/// <summary>
/// Ventana "Verificar" del menú contextual: el veredicto de cada archivo elegido, sin abrir ForZip entero. Cada
/// archivo es un <see cref="VerifyReportViewModel"/> propio, así que el veredicto, el detalle y el informe son los
/// mismos que en la pantalla Verificar.
/// </summary>
/// <remarks>
/// Arranca sola: verificar solo lee y no cambia nada. Si el paquete o el sobre tiene contraseña, la ficha ofrece
/// el campo y "Verificar de nuevo". Los archivos se verifican de a uno, en el orden en que llegaron.
/// </remarks>
public partial class ShellVerifyViewModel : ViewModelBase
{
    private readonly Func<VerifyReportViewModel> _newVerify;
    private readonly ILogService _logService;

    /// <summary>Evita dos bombas de verificación a la vez (ver <see cref="PumpAsync"/>).</summary>
    private bool _pumping;

    public ShellVerifyViewModel(Func<VerifyReportViewModel> newVerify, ILogService logService,
                                ILocalizationService localization) : base(localization)
    {
        _newVerify = newVerify;
        _logService = logService;
    }

    public ObservableCollection<ShellVerifyItem> Items { get; } = new();

    public string Heading => _localization.Get("shell_verify_heading");
    public string ButtonClose => _localization.Get("quick_hash_close");
    public bool IsElevated { get; } = Environment.IsPrivilegedProcess;
    public string LabelElevated => _localization.Get("status_running_elevated_tip");

    public string Summary => string.Format(_localization.Get("shell_verify_summary"),
        Items.Count, Items.Count(i => i.Verify.HasResult));

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(ButtonClose));
        OnPropertyChanged(nameof(LabelElevated));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Suma los archivos de una invocación del menú. Uno que ya está en la ventana no se repite.</summary>
    public void AddRequest(ShellRequest request)
    {
        foreach (var path in request.Paths)
        {
            var target = VerifyReportViewModel.ResolveVerifyTarget(Path.GetFullPath(path));
            if (Items.Any(i => string.Equals(i.Verify.ReportFilePath, target, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var verify = _newVerify();
            verify.ReportFilePath = target;
            var item = new ShellVerifyItem(verify, _localization) { IsQueued = true };
            verify.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
            Items.Add(item);
        }

        // Con varios archivos el detalle de cada uno arranca cerrado: si no, el primero tapa a los demás.
        foreach (var item in Items)
        {
            item.IsDetailExpanded = Items.Count == 1;
        }

        _logService.Info(string.Format(_localization.Get("log_shell_action"), request.Verb, request.Paths.Count));
        OnPropertyChanged(nameof(Summary));
        _ = PumpAsync();
    }

    /// <summary>"Verificar de nuevo", con la contraseña escrita.</summary>
    [RelayCommand]
    private void Retry(ShellVerifyItem item)
    {
        if (item.IsQueued || item.IsRunning)
        {
            return;
        }

        item.IsQueued = true;
        _ = PumpAsync();
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
                next.IsQueued = false;
                next.IsRunning = true;
                try
                {
                    await next.Verify.VerifyCommand.ExecuteAsync(null);
                }
                finally
                {
                    next.IsRunning = false;
                }
            }
        }
        finally
        {
            _pumping = false;
            OnPropertyChanged(nameof(Summary));
        }
    }
}

/// <summary>Un archivo de la ventana "Verificar": su verificación y lo que la ficha muestra.</summary>
public partial class ShellVerifyItem : ObservableObject
{
    private readonly ILocalizationService _localization;

    public ShellVerifyItem(VerifyReportViewModel verify, ILocalizationService localization)
    {
        Verify = verify;
        _localization = localization;
        Verify.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ShowPassword));
    }

    /// <summary>
    /// El campo de contraseña, solo donde sirve: en un sobre (con ella se verifica también el contenido) y en un
    /// paquete que no se pudo verificar entero. En uno íntegro o comprometido no hay nada más que decidir.
    /// </summary>
    public bool ShowPassword => Verify.IsSealedInput
        || (Verify.IsManifestInput && Verify.HasResult && !Verify.IsValid && !Verify.IsInvalid)
        || (Verify.IsArchiveInput && Verify.HasResult && Verify.TestNeedsPassword);

    public VerifyReportViewModel Verify { get; }
    public string FileName => Path.GetFileName(Verify.ReportFilePath);
    public string FullPath => Verify.ReportFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsBusy))]
    private bool _isQueued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsBusy))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isDetailExpanded;

    public bool IsBusy => IsQueued || IsRunning;

    public string StatusText => IsRunning
        ? _localization.Get("verify_running")
        : IsQueued ? _localization.Get("shell_extract_waiting") : string.Empty;

    public string LabelDetail => _localization.Get("shell_verify_detail");
    public string ButtonRetry => _localization.Get("shell_verify_retry");
}
