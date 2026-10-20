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

        RegisterSealedExtension();

        using var marker = Registry.CurrentUser.CreateSubKey(ShellMenuLayout.MarkerKey);
        marker.SetValue(ShellMenuLayout.MarkerValueName, exe, RegistryValueKind.String);

        NotifyAssociationsChanged();
    }

    /// <summary>
    /// Asocia <c>.fzs</c> al tipo de archivo de ForZip sin imponerse: se declara siempre en
    /// OpenWithProgids, pero se toma el valor por defecto de la extensión solo si nadie lo
    /// reclamó antes. Si el usuario eligió otro programa, Windows respeta esa elección.
    /// </summary>
    private static void RegisterSealedExtension()
    {
        using var extension = Registry.CurrentUser.CreateSubKey(ShellMenuLayout.SealedExtensionKey);
        if (string.IsNullOrEmpty(extension.GetValue(string.Empty) as string))
        {
            extension.SetValue(string.Empty, ShellMenuLayout.SealedProgId, RegistryValueKind.String);
        }

        using var openWith = Registry.CurrentUser.CreateSubKey(ShellMenuLayout.SealedOpenWithKey);
        openWith.SetValue(ShellMenuLayout.SealedProgId, string.Empty, RegistryValueKind.String);
    }

    /// <summary>
    /// Retira de <c>.fzs</c> solo lo que puso ForZip. La clave se borra únicamente si queda
    /// vacía: puede tener datos de otros programas.
    /// </summary>
    private static void UnregisterSealedExtension()
    {
        using (var openWith = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.SealedOpenWithKey, writable: true))
        {
            openWith?.DeleteValue(ShellMenuLayout.SealedProgId, throwOnMissingValue: false);
        }

        using (var openWith = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.SealedOpenWithKey))
        {
            if (openWith != null && openWith.ValueCount == 0 && openWith.SubKeyCount == 0)
            {
                openWith.Dispose();
                Registry.CurrentUser.DeleteSubKey(ShellMenuLayout.SealedOpenWithKey, throwOnMissingSubKey: false);
            }
        }

        using (var extension = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.SealedExtensionKey, writable: true))
        {
            if (extension == null)
            {
                return;
            }

            if (string.Equals(extension.GetValue(string.Empty) as string, ShellMenuLayout.SealedProgId, StringComparison.Ordinal))
            {
                extension.DeleteValue(string.Empty, throwOnMissingValue: false);
            }
        }

        using (var extension = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.SealedExtensionKey))
        {
            if (extension != null && extension.ValueCount == 0 && extension.SubKeyCount == 0)
            {
                extension.Dispose();
                Registry.CurrentUser.DeleteSubKey(ShellMenuLayout.SealedExtensionKey, throwOnMissingSubKey: false);
            }
        }
    }

    /// <summary>
    /// Avisa al Explorador que cambiaron las asociaciones, para que muestre el ícono nuevo
    /// del sobre sin reiniciar la sesión.
    /// </summary>
    private static void NotifyAssociationsChanged()
    {
        const int SHCNE_ASSOCCHANGED = 0x08000000;
        const uint SHCNF_IDLIST = 0x0000;
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

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

        UnregisterSealedExtension();

        using (var marker = Registry.CurrentUser.OpenSubKey(ShellMenuLayout.MarkerKey, writable: true))
        {
            marker?.DeleteValue(ShellMenuLayout.MarkerValueName, throwOnMissingValue: false);
        }

        NotifyAssociationsChanged();
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
        CompressReserved: _localization.Get("shell_compress_reserved"),
        Hash: _localization.Get("shell_hash"),
        Extract: _localization.Get("shell_extract"),
        ExtractHere: _localization.Get("shell_extract_here"),
        ExtractTo: _localization.Get("shell_extract_to"),
        Verify: _localization.Get("shell_verify"),
        VerifySealed: _localization.Get("shell_verify_sealed"),
        OpenSealed: _localization.Get("shell_open_sealed"),
        SealedFileType: _localization.Get("shell_sealed_file_type"),
        VerifyVolume: _localization.Get("shell_verify_volume"));
}
