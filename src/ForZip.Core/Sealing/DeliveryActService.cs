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

using System.Globalization;
using System.Text;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Sealing;

/// <summary>
/// Genera el acta de entrega de clave. Es papel: monoespaciado, 80 columnas, sin nada que
/// dependa de una pantalla.
/// </summary>
/// <remarks>
/// El acta va a estar en el expediente dentro de años, cuando nadie del equipo actual esté
/// disponible para explicar nada, así que dice en texto llano cómo se abre el sobre y con
/// qué versión se selló, y lleva el SHA-256 para que el receptor pueda comprobar que el
/// sobre que le llegó es el mismo que figura en el papel que firmó.
/// </remarks>
public class DeliveryActService : IDeliveryActService
{
    private const string NewLine = "\r\n";
    private const int SeparatorWidth = 80;
    // Una más que "Fecha de recepción", la etiqueta más larga del acta, para que todas
    // conserven al menos un espacio antes de los dos puntos.
    private const int LabelWidth = 19;
    private const int PasswordGroupSize = 4;

    /// <summary>Sufijo del archivo del acta.</summary>
    public const string ActSuffix = ".acta.txt";

    private readonly ILocalizationService _localization;

    public DeliveryActService(ILocalizationService localization)
    {
        _localization = localization;
    }

    /// <inheritdoc />
    public string Generate(DeliveryActData data, string language)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Mismo criterio que el informe forense: se cambia el idioma para construir todas las
        // cadenas del documento y se restaura el anterior al salir.
        var previousLanguage = _localization.CurrentLanguage;
        try
        {
            _localization.SetLanguage(language);
            return Build(data);
        }
        finally
        {
            _localization.SetLanguage(previousLanguage);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string content, string outputPath)
    {
        // UTF-8 con BOM, igual que el informe forense.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(outputPath, content, encoding).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string FormatPasswordForTranscription(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(password.Length + (password.Length / PasswordGroupSize));
        for (var i = 0; i < password.Length; i++)
        {
            if (i > 0 && i % PasswordGroupSize == 0)
            {
                sb.Append(' ');
            }

            sb.Append(password[i]);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GetSuggestedFileName(Guid packageId) =>
        SealedContainerFormat.BuildBaseName(packageId) + ActSuffix;

    private string Build(DeliveryActData data)
    {
        var sb = new StringBuilder();

        AppendHeader(sb, data);
        AppendPackageSection(sb, data);
        AppendCaseSection(sb, data);
        AppendOperatorSection(sb, data);
        AppendPasswordBlock(sb, data);
        AppendOpeningInstructions(sb, data);
        AppendCloudWarning(sb);
        AppendReceiptSection(sb);

        return sb.ToString();
    }

    private void AppendHeader(StringBuilder sb, DeliveryActData data)
    {
        sb.Append('=', SeparatorWidth).Append(NewLine);
        sb.Append("  ").Append(Format("act_title", data.ForZipVersion)).Append(NewLine);
        sb.Append("  ").Append(_localization.Get("act_subtitle")).Append(NewLine);
        sb.Append('=', SeparatorWidth).Append(NewLine);
        sb.Append(NewLine);
    }

    private void AppendPackageSection(StringBuilder sb, DeliveryActData data)
    {
        AppendSectionTitle(sb, "act_section_package");
        AppendField(sb, _localization.Get("act_package_id"), SealedContainerFormat.BuildBaseName(data.PackageId));
        AppendField(sb, _localization.Get("act_package_file"), data.SealedFileName);
        AppendField(sb, _localization.Get("act_package_sealed_at"), FormatTimestamp(data.SealedAtUtc));
        sb.Append(NewLine);
        AppendWrapped(sb, _localization.Get("act_package_sha256"));
        sb.Append("  ").Append(data.SealedSha256).Append(NewLine);
        sb.Append(NewLine);
    }

    private void AppendCaseSection(StringBuilder sb, DeliveryActData data)
    {
        AppendSectionTitle(sb, "report_case");
        AppendField(sb, _localization.Get("report_case_number"), data.CaseNumber ?? string.Empty);
        AppendField(sb, _localization.Get("report_case_desc"), data.CaseDescription ?? string.Empty);
        AppendField(sb, _localization.Get("report_case_court"), data.Court ?? string.Empty);
        sb.Append(NewLine);
    }

    private void AppendOperatorSection(StringBuilder sb, DeliveryActData data)
    {
        var op = data.Operator ?? new OperatorInfo();

        AppendSectionTitle(sb, "act_section_operator");
        AppendField(sb, _localization.Get("report_op_name"), op.Name ?? string.Empty);
        AppendField(sb, _localization.Get("report_op_title"), op.Title ?? string.Empty);
        AppendField(sb, _localization.Get("report_op_org"), op.Organization ?? string.Empty);
        sb.Append(NewLine);
    }

    /// <summary>
    /// La contraseña va en un bloque destacado y agrupada de a cuatro: este papel se
    /// transcribe a mano, y una clave de 32 caracteres corridos es un error esperando a
    /// suceder.
    /// </summary>
    private void AppendPasswordBlock(StringBuilder sb, DeliveryActData data)
    {
        sb.Append('*', SeparatorWidth).Append(NewLine);
        sb.Append("  ").Append(_localization.Get("act_password_title")).Append(NewLine);
        sb.Append(NewLine);
        // Primero tal cual, para copiar y pegar; debajo, en grupos de 4 para dictarla o copiarla a mano. Solo con los
        // grupos, quien la transcribía con los espacios se preguntaba si iban (pruebas del 2026-10-12, V3).
        sb.Append("      ").Append(data.Password).Append(NewLine);
        sb.Append(NewLine);
        sb.Append("      ").Append(_localization.Get("act_password_grouped_label")).Append(NewLine);
        sb.Append("      ").Append(FormatPasswordForTranscription(data.Password)).Append(NewLine);
        sb.Append(NewLine);
        AppendWrapped(sb, _localization.Get("act_password_note"));
        sb.Append('*', SeparatorWidth).Append(NewLine);
        sb.Append(NewLine);
    }

    private void AppendOpeningInstructions(StringBuilder sb, DeliveryActData data)
    {
        AppendSectionTitle(sb, "act_section_open");
        // El hash es el de la página embebida en esta versión, que es exactamente la copia que
        // ForZip dejó junto al sobre: el receptor la coteja contra el papel.
        AppendWrapped(sb, Format("act_open_text", data.ForZipVersion, EnvelopeOpenerPage.Sha256));
        sb.Append(NewLine);
    }

    private void AppendCloudWarning(StringBuilder sb)
    {
        sb.Append('-', SeparatorWidth).Append(NewLine);
        sb.Append("  ").Append(_localization.Get("act_warning_title")).Append(NewLine);
        sb.Append('-', SeparatorWidth).Append(NewLine);
        AppendWrapped(sb, _localization.Get("act_warning_text"));
        sb.Append(NewLine);
    }

    private void AppendReceiptSection(StringBuilder sb)
    {
        AppendSectionTitle(sb, "act_section_receipt");
        AppendWrapped(sb, _localization.Get("act_receipt_text"));
        sb.Append(NewLine);

        foreach (var key in new[] { "act_receipt_date", "act_receipt_name", "act_receipt_id", "act_receipt_signature" })
        {
            sb.Append("  ").Append(_localization.Get(key).PadRight(LabelWidth)).Append(": ")
              .Append('_', SeparatorWidth - LabelWidth - 6).Append(NewLine);
            sb.Append(NewLine);
        }

        sb.Append('=', SeparatorWidth).Append(NewLine);
    }

    private void AppendSectionTitle(StringBuilder sb, string key)
    {
        sb.Append("  ").Append(_localization.Get(key)).Append(NewLine);
        sb.Append("  ").Append('-', SeparatorWidth - 4).Append(NewLine);
    }

    private static void AppendField(StringBuilder sb, string label, string value)
    {
        sb.Append("  ").Append(label.PadRight(LabelWidth)).Append(": ").Append(value).Append(NewLine);
    }

    /// <summary>
    /// Vuelca un texto de varias líneas respetando los saltos de la cadena traducida y
    /// sangrando cada línea, que es como lo hace el informe forense.
    /// </summary>
    private static void AppendWrapped(StringBuilder sb, string text)
    {
        foreach (var line in text.Split('\n'))
        {
            // Las líneas en blanco no llevan sangría: es papel, no deja espacios colgando.
            var clean = line.TrimEnd('\r');
            sb.Append(clean.Length == 0 ? string.Empty : "  ").Append(clean).Append(NewLine);
        }
    }

    private string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, _localization.Get(key), args);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
