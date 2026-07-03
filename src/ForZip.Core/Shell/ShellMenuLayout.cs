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

namespace ForZip.Core.Shell;

/// <summary>Etiquetas (localizables) que se muestran en el menú contextual.</summary>
public sealed record ShellMenuLabels(
    string Menu,
    string Compress,
    string Hash,
    string Extract,
    string ExtractHere,
    string ExtractTo,
    string Verify);

/// <summary>Una entrada de registro a crear: subclave (relativa a HKCU), nombre de valor
/// (<c>null</c> = valor por defecto) y dato.</summary>
public sealed record ShellRegistryEntry(string SubKey, string? ValueName, string Value);

/// <summary>
/// Describe, como datos puros y sin dependencia de plataforma, el menú contextual en cascada
/// de ForZip (estilo 7-Zip/WinRAR) bajo <c>HKCU\Software\Classes</c>. La I/O real del registro
/// vive en la GUI (Windows); esto permite testear la forma exacta de las claves y comandos.
/// <para/>
/// El submenú en cascada se logra solo con registro (Win7+): la clave padre lleva
/// <c>MUIVerb</c> + <c>SubCommands=""</c> y los items cuelgan de su subclave <c>shell</c>.
/// </summary>
public static class ShellMenuLayout
{
    /// <summary>Clave (bajo HKCU) donde se guarda la ruta del .exe registrado, como marcador.</summary>
    public const string MarkerKey = @"Software\ForZip";
    public const string MarkerValueName = "ShellIntegrationPath";

    /// <summary>
    /// Valor (bajo <see cref="MarkerKey"/>) que registra que la oferta de integración de
    /// primer uso ya se mostró en esta PC (se pregunta una sola vez por equipo).
    /// </summary>
    public const string OfferShownValueName = "ShellOfferShown";

    /// <summary>Subárboles que se crean al registrar y se borran al desregistrar (en bloque).</summary>
    public static IReadOnlyList<string> RootSubKeys { get; } = new[]
    {
        @"Software\Classes\*\shell\ForZip",
        @"Software\Classes\Directory\shell\ForZip",
        @"Software\Classes\Directory\Background\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.zip\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.001\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.p7s\shell\ForZip"
    };

    /// <summary>
    /// Genera todas las entradas de registro para el <paramref name="exePath"/> dado.
    /// Cada scope (archivos, carpetas, fondo de carpeta, .zip, .001) recibe su propio menú
    /// con los items que tienen sentido en ese contexto.
    /// </summary>
    public static IReadOnlyList<ShellRegistryEntry> Build(string exePath, ShellMenuLabels labels)
    {
        var entries = new List<ShellRegistryEntry>();

        // Items para archivos y carpetas normales
        var general = new (string id, string label, ShellVerb verb)[]
        {
            ("01compress", labels.Compress, ShellVerb.Compress),
            ("02hash", labels.Hash, ShellVerb.Hash)
        };

        // Items para un archivo ZIP o un volumen .001 (acciones de archivo)
        var archive = new (string id, string label, ShellVerb verb)[]
        {
            ("01extracthere", labels.ExtractHere, ShellVerb.ExtractHere),
            ("02extractto", labels.ExtractTo, ShellVerb.ExtractTo),
            ("03extract", labels.Extract, ShellVerb.Extract),
            ("04verify", labels.Verify, ShellVerb.Verify),
            ("05hash", labels.Hash, ShellVerb.Hash)
        };

        // Fondo de carpeta: solo comprimir el contenido (usa %V, la ruta de la carpeta)
        var background = new (string id, string label, ShellVerb verb)[]
        {
            ("01compress", labels.Compress, ShellVerb.Compress)
        };

        // Firma desacoplada (.p7s): verificar el manifiesto que acompaña
        var signature = new (string id, string label, ShellVerb verb)[]
        {
            ("01verify", labels.Verify, ShellVerb.Verify)
        };

        AddCascade(entries, @"Software\Classes\*\shell\ForZip", "%1", general, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\Directory\shell\ForZip", "%1", general, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\Directory\Background\shell\ForZip", "%V", background, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.zip\shell\ForZip", "%1", archive, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.001\shell\ForZip", "%1", archive, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.p7s\shell\ForZip", "%1", signature, exePath, labels.Menu);

        return entries;
    }

    private static void AddCascade(
        List<ShellRegistryEntry> entries,
        string root,
        string placeholder,
        (string id, string label, ShellVerb verb)[] children,
        string exePath,
        string menuLabel)
    {
        var icon = $"{exePath},0";

        entries.Add(new ShellRegistryEntry(root, "MUIVerb", menuLabel));
        entries.Add(new ShellRegistryEntry(root, "Icon", icon));
        entries.Add(new ShellRegistryEntry(root, "SubCommands", string.Empty));

        foreach (var (id, label, verb) in children)
        {
            var childKey = $@"{root}\shell\{id}";
            entries.Add(new ShellRegistryEntry(childKey, null, label));
            entries.Add(new ShellRegistryEntry(childKey, "Icon", icon));

            var command = $"\"{exePath}\" {ShellArgs.VerbToken(verb)} \"{placeholder}\"";
            entries.Add(new ShellRegistryEntry($@"{childKey}\command", null, command));
        }
    }
}
