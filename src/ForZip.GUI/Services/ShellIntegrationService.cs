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

using ForZip.Core.Interfaces;
using ForZip.Core.Shell;
using Microsoft.Win32;

namespace ForZip.GUI.Services;

/// <summary>
/// Integración portable con el menú contextual de Windows vía registro <c>HKCU</c>: no requiere
/// admin, no usa instalador y es totalmente reversible. Las etiquetas se toman del idioma actual.
/// En plataformas que no son Windows, todas las operaciones son no-op (o lanzan en Register).
/// </summary>
public sealed class ShellIntegrationService : IShellIntegrationService
{
    private readonly ILocalizationService _localization;

    public ShellIntegrationService(ILocalizationService localization)
    {
        _localization = localization;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public string CurrentExePath => Environment.ProcessPath ?? string.Empty;

    public bool IsRegistered() => GetRegisteredPath() != null;

    public string? GetRegisteredPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.MarkerKey);
        return key?.GetValue(ShellMenuLayout.MarkerValueName) as string;
    }

    public void Register()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "La integración con el menú contextual solo está disponible en Windows.");
        }

        var exe = CurrentExePath;
        if (string.IsNullOrEmpty(exe))
        {
            throw new InvalidOperationException("No se pudo determinar la ruta del ejecutable de ForZip.");
        }

        // Reescribimos desde cero (borrar + crear) para que un re-registro corrija una ruta
        // obsoleta si el .exe portable se movió de carpeta.
        Unregister();

        foreach (var entry in ShellMenuLayout.Build(exe, BuildLabels()))
        {
            using var key = Registry.CurrentUser.CreateSubKey(entry.SubKey);
            key.SetValue(entry.ValueName ?? string.Empty, entry.Value, RegistryValueKind.String);
        }

        using var marker = Registry.CurrentUser.CreateSubKey(ShellMenuLayout.MarkerKey);
        marker.SetValue(ShellMenuLayout.MarkerValueName, exe, RegistryValueKind.String);
    }

    public void Unregister()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var root in ShellMenuLayout.RootSubKeys)
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }

        using var marker = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.MarkerKey, writable: true);
        marker?.DeleteValue(ShellMenuLayout.MarkerValueName, throwOnMissingValue: false);
    }

    public bool WasOfferShown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true; // en otras plataformas nunca ofrecemos
        }

        using var key = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.MarkerKey);
        return key?.GetValue(ShellMenuLayout.OfferShownValueName) != null;
    }

    public void MarkOfferShown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(ShellMenuLayout.MarkerKey);
        key.SetValue(ShellMenuLayout.OfferShownValueName, 1, RegistryValueKind.DWord);
    }

    private ShellMenuLabels BuildLabels() => new(
        Menu: "ForZip",
        Compress: _localization.Get("shell_compress"),
        Hash: _localization.Get("shell_hash"),
        Extract: _localization.Get("shell_extract"),
        ExtractHere: _localization.Get("shell_extract_here"),
        ExtractTo: _localization.Get("shell_extract_to"),
        Verify: _localization.Get("shell_verify"));
}
