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
    string CompressReserved,
    string Hash,
    string Extract,
    string ExtractHere,
    string ExtractTo,
    string Verify,
    string VerifySealed,
    string OpenSealed,
    string SealedFileType,
    string? VerifyVolume = null);

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
        @"Software\Classes\SystemFileAssociations\.7z\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.rar\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.tar\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.gz\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.tgz\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.bz2\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.xz\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.txz\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.iso\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.p7s\shell\ForZip",
        @"Software\Classes\SystemFileAssociations\.fzs\shell\ForZip",
        ReceiptRoot,
        VolumeVerifyRoot,
        SealedProgIdKey
    };

    /// <summary>
    /// Tipo de archivo propio para los sobres: ícono, nombre y acción de doble clic. Es todo
    /// nuestro, así que se borra entero al desregistrar.
    /// </summary>
    public const string SealedProgId = "ForZip.SealedEnvelope";

    private const string SealedProgIdKey = @"Software\Classes\" + SealedProgId;

    /// <summary>
    /// Clave de la extensión <c>.fzs</c>. No va en <see cref="RootSubKeys"/>: puede tener datos de
    /// otros programas, así que la integración agrega y quita solo lo suyo, con cuidado.
    /// </summary>
    public const string SealedExtensionKey = @"Software\Classes\.fzs";

    /// <summary>Subclave donde se declara que ForZip sabe abrir <c>.fzs</c>, sin imponerse.</summary>
    public const string SealedOpenWithKey = SealedExtensionKey + @"\OpenWithProgids";

    /// <summary>
    /// El recibo termina en <c>.json</c>, y Windows asocia menús por extensión: sin filtro, la
    /// opción aparecería en cualquier JSON. <c>AppliesTo</c> la limita a los recibos.
    /// </summary>
    private const string ReceiptRoot = @"Software\Classes\SystemFileAssociations\.json\shell\ForZip";

    /// <summary>
    /// Filtro (sintaxis de búsqueda de Windows) que deja la opción solo en los recibos y en los manifiestos. Los dos
    /// se verifican: el recibo, contra su sobre; el manifiesto, contra su paquete. En otro JSON no aparece.
    /// </summary>
    public const string ReceiptAppliesTo =
        "System.FileName:~\"*.receipt.json\" OR System.FileName:~\"*.manifest.json\"";

    /// <summary>
    /// Volúmenes de un paquete partido desde el segundo (<c>caso.zip.002</c>, <c>caso.7z.013</c>…). El <c>.001</c>
    /// tiene su propio menú; los demás no tienen extensión registrable, así que la opción cuelga de <c>*</c> con
    /// un filtro. Es un ítem suelto ("Verificar con ForZip") y no un segundo submenú "ForZip" al lado del general.
    /// </summary>
    private const string VolumeVerifyRoot = @"Software\Classes\*\shell\ForZipVerifyVolume";

    /// <summary>Filtro positivo: si Windows no lo entendiera, la opción no aparece y el resto del menú no cambia.</summary>
    public const string VolumeAppliesTo =
        "System.FileName:~\"*.zip.0??\" OR System.FileName:~\"*.7z.0??\"";

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
            // Va al lado de "Empaquetar" para el error más probable: olvidarse de cambiar el
            // perfil antes de subir algo a la nube.
            ("02compressreserved", labels.CompressReserved, ShellVerb.CompressReserved),
            ("03hash", labels.Hash, ShellVerb.Hash)
        };

        // Items para un archivo ZIP, un 7z o un volumen .001 (acciones de archivo)
        var archive = new (string id, string label, ShellVerb verb)[]
        {
            ("01extracthere", labels.ExtractHere, ShellVerb.ExtractHere),
            ("02extractto", labels.ExtractTo, ShellVerb.ExtractTo),
            ("03extract", labels.Extract, ShellVerb.Extract),
            ("04verify", labels.Verify, ShellVerb.Verify),
            ("05hash", labels.Hash, ShellVerb.Hash)
        };

        // Formatos de terceros (RAR, TAR, ISO…): extraer y hashear. "Verificar" no: no traen manifiesto, y la
        // opción llevaba a un veredicto que no tenía contra qué comparar.
        var thirdParty = archive.Where(item => item.verb != ShellVerb.Verify).ToArray();

        // Fondo de carpeta: solo comprimir el contenido (usa %V, la ruta de la carpeta)
        var background = new (string id, string label, ShellVerb verb)[]
        {
            ("01compress", labels.Compress, ShellVerb.Compress),
            ("02compressreserved", labels.CompressReserved, ShellVerb.CompressReserved)
        };

        // Firma desacoplada (.p7s): verificar el manifiesto que acompaña
        var signature = new (string id, string label, ShellVerb verb)[]
        {
            ("01verify", labels.Verify, ShellVerb.Verify)
        };

        // Sobre sellado (.fzs). "Abrir sobre…" lleva a la pantalla Extraer con el sobre cargado:
        // la contraseña y el destino se eligen adentro, así que abrir sigue siendo un acto
        // deliberado dentro de la aplicación. Nada se descifra desde el menú.
        var sealedEnvelope = new (string id, string label, ShellVerb verb)[]
        {
            ("01open", labels.OpenSealed, ShellVerb.OpenEnvelope),
            ("02verify", labels.VerifySealed, ShellVerb.Verify)
        };

        // Recibo público y manifiesto: solo verificar. Para abrir hace falta el sobre o el paquete. Un solo ítem para
        // los dos (Verificar decide por el archivo): el filtro de la clave padre es lo que se sabe que Windows respeta.
        var receipt = new (string id, string label, ShellVerb verb)[]
        {
            ("01verify", labels.Verify, ShellVerb.Verify)
        };

        AddCascade(entries, @"Software\Classes\*\shell\ForZip", "%1", general, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\Directory\shell\ForZip", "%1", general, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\Directory\Background\shell\ForZip", "%V", background, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.zip\shell\ForZip", "%1", archive, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.001\shell\ForZip", "%1", archive, exePath, labels.Menu);
        // Bajo SystemFileAssociations el menú convive con el de 7-Zip y no toca la asociación del .7z. El 7z puede
        // ser un paquete de ForZip (con manifiesto); los demás son siempre de terceros.
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.7z\shell\ForZip", "%1", archive, exePath, labels.Menu);
        foreach (var ext in new[] { ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".txz", ".iso" })
        {
            AddCascade(entries, $@"Software\Classes\SystemFileAssociations\{ext}\shell\ForZip", "%1", thirdParty, exePath, labels.Menu);
        }
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.p7s\shell\ForZip", "%1", signature, exePath, labels.Menu);
        AddCascade(entries, @"Software\Classes\SystemFileAssociations\.fzs\shell\ForZip", "%1", sealedEnvelope, exePath, labels.Menu);

        // Recibo público (.receipt.json): verificar el sobre sin necesidad de tenerlo a mano;
        // ForZip encuentra la pareja. Quien tiene el expediente suele tener el recibo.
        AddCascade(entries, ReceiptRoot, "%1", receipt, exePath, labels.Menu);
        entries.Add(new ShellRegistryEntry(ReceiptRoot, "AppliesTo", ReceiptAppliesTo));

        // Volúmenes desde el .002: Verificar resuelve el manifiesto del juego y nombra los volúmenes que falten.
        var icon = $"{exePath},0";
        entries.Add(new ShellRegistryEntry(VolumeVerifyRoot, "MUIVerb", labels.VerifyVolume ?? $"{labels.Menu}: {labels.Verify}"));
        entries.Add(new ShellRegistryEntry(VolumeVerifyRoot, "Icon", icon));
        entries.Add(new ShellRegistryEntry(VolumeVerifyRoot, "AppliesTo", VolumeAppliesTo));
        entries.Add(new ShellRegistryEntry($@"{VolumeVerifyRoot}\command", null,
            $"\"{exePath}\" {ShellArgs.VerbToken(ShellVerb.Verify)} \"%1\""));

        // Tipo de archivo del sobre: ícono y doble clic. El doble clic hace lo mismo que
        // "Abrir sobre…": carga el sobre en ForZip y no descifra nada.
        entries.Add(new ShellRegistryEntry(SealedProgIdKey, null, labels.SealedFileType));
        entries.Add(new ShellRegistryEntry($@"{SealedProgIdKey}\DefaultIcon", null, $"{exePath},0"));
        entries.Add(new ShellRegistryEntry($@"{SealedProgIdKey}\shell", null, "open"));
        entries.Add(new ShellRegistryEntry($@"{SealedProgIdKey}\shell\open", null, labels.OpenSealed));
        entries.Add(new ShellRegistryEntry($@"{SealedProgIdKey}\shell\open\command", null,
            $"\"{exePath}\" {ShellArgs.VerbToken(ShellVerb.OpenEnvelope)} \"%1\""));

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
