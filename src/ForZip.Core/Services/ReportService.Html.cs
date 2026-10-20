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
using ForZip.Core.Models;

namespace ForZip.Core.Services;

/// <summary>
/// El informe forense como página HTML imprimible. Sale de los mismos datos y con los mismos textos que el informe en
/// texto; cambia solo la presentación, para que un juez o un fiscal lo abra con doble clic y lo imprima.
/// </summary>
/// <remarks>
/// Es una presentación, no la prueba: no se firma ni se verifica (eso es el manifiesto, y la huella del paquete
/// anotada en la planilla de custodia). Por eso tampoco puede ejecutar nada: sin scripts, sin recursos externos y con
/// una política de seguridad que lo prohíbe aunque alguien los agregue después. Una página que ejecuta código puede
/// mostrar en pantalla algo distinto de lo que contiene; esta muestra lo que tiene escrito.
/// </remarks>
public partial class ReportService
{
    public string GenerateReportFor(string path, ReportData data, string language) =>
        IsHtmlPath(path) ? GenerateReportHtml(data, language) : GenerateReport(data, language);

    /// <summary>La ruta pide el informe en HTML (<c>.html</c> o <c>.htm</c>).</summary>
    public static bool IsHtmlPath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    public string GenerateReportHtml(ReportData data, string language)
    {
        // Como en el texto: los textos se piden en el idioma del informe sin tocar el de la aplicación.
        var previous = _reportLanguage.Value;
        try
        {
            _reportLanguage.Value = language;
            return BuildReportHtml(data, language);
        }
        finally
        {
            _reportLanguage.Value = previous;
        }
    }

    private const string HtmlStyle = """
        :root { color-scheme: light; }
        * { box-sizing: border-box; }
        body { margin: 0; background: #f4f5f7; color: #1b1f24; font: 14px/1.5 "Segoe UI", system-ui, -apple-system, sans-serif; }
        main { max-width: 900px; margin: 24px auto; background: #fff; padding: 40px 48px; border: 1px solid #d9dde3; border-radius: 6px; }
        header { border-bottom: 3px solid #1b1f24; padding-bottom: 12px; margin-bottom: 20px; }
        h1 { font-size: 22px; margin: 0 0 4px; letter-spacing: .2px; }
        .sub { color: #555d68; margin: 0; }
        h2 { font-size: 13px; letter-spacing: .6px; text-transform: uppercase; color: #3a4350; border-bottom: 1px solid #d9dde3;
             padding-bottom: 4px; margin: 28px 0 10px; break-after: avoid; }
        h3 { font-size: 13px; margin: 16px 0 6px; }
        table { border-collapse: collapse; width: 100%; }
        table.kv th { text-align: left; font-weight: 600; color: #3a4350; width: 190px; padding: 3px 12px 3px 0; vertical-align: top; }
        table.kv td { padding: 3px 0; vertical-align: top; }
        table.grid th { text-align: left; font-weight: 600; background: #eef0f3; border: 1px solid #d9dde3; padding: 5px 8px; }
        table.grid td { border: 1px solid #d9dde3; padding: 5px 8px; vertical-align: top; }
        table.grid td.num { text-align: right; white-space: nowrap; }
        tr { break-inside: avoid; }
        .mono { font-family: Consolas, "Cascadia Mono", "Courier New", monospace; font-size: 12.5px; overflow-wrap: anywhere; }
        td.hash { word-break: break-all; }
        .nw { white-space: nowrap; }
        col.n { width: 44px; } col.file { width: 30%; } col.size { width: 90px; } col.date { width: 150px; }
        .key { border: 2px solid #1b1f24; border-radius: 6px; padding: 14px 18px; margin: 18px 0; break-inside: avoid; }
        .key .label { font-weight: 700; margin: 0 0 6px; }
        .key .hash { font-family: Consolas, "Cascadia Mono", "Courier New", monospace; font-size: 15px; font-weight: 600; word-break: break-all; margin: 4px 0; }
        .key .hint { color: #555d68; margin: 6px 0 0; }
        .verdict { border-radius: 6px; padding: 14px 18px; margin: 18px 0; border: 2px solid; break-inside: avoid; }
        .verdict.ok { border-color: #1e7b45; background: #ecf7f0; }
        .verdict.bad { border-color: #b3261e; background: #fbeceb; }
        .verdict.doubt { border-color: #a86200; background: #fdf4e6; }
        .verdict p { margin: 2px 0; }
        .verdict .title { font-size: 16px; font-weight: 700; }
        .note { background: #f4f5f7; border-left: 4px solid #8a94a3; padding: 10px 14px; margin: 14px 0; color: #3a4350; }
        .warn { color: #b3261e; font-weight: 600; }
        pre { white-space: pre-wrap; word-break: break-all; background: #f4f5f7; border: 1px solid #d9dde3; padding: 10px 12px;
              font: 12.5px/1.45 Consolas, "Cascadia Mono", "Courier New", monospace; }
        footer { margin-top: 32px; border-top: 1px solid #d9dde3; padding-top: 10px; color: #555d68; font-size: 12px; }
        @page { size: A4; margin: 16mm 14mm; }
        @media print {
          body { background: #fff; font-size: 11pt; }
          main { margin: 0; padding: 0; border: 0; max-width: none; }
          /* Una SHA-256 entera por renglón en A4: partida en dos se coteja mal contra la planilla. */
          table.grid td.mono { font-size: 7.6pt; }
          table.grid th, table.grid td { padding: 3px 5px; }
          col.file { width: 21%; } col.size { width: 64px; } col.n { width: 34px; }
          .verdict.ok, .verdict.bad, .verdict.doubt { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
        }
        """;

    // El logo de ForZip para la pestaña del navegador, como data: URI. Sin el recurso, la página sale sin ícono.
    private static readonly Lazy<string?> IconDataUri = new(() =>
    {
        using var stream = typeof(ReportService).Assembly.GetManifestResourceStream("ForZip.Core.Resources.ForZip_logo_32x32.png");
        if (stream == null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
    });

    private string BuildReportHtml(ReportData data, string language)
    {
        var culture = GetCulture(language);
        var sb = new StringBuilder();
        var title = string.Format(CultureInfo.InvariantCulture, L("report_title"), data.ForZipVersion);

        sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(language == "es" ? "es" : "en").Append("\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        // Ni scripts ni nada de afuera: solo el estilo que viene escrito acá y el ícono que viaja adentro.
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<meta name=\"generator\" content=\"ForZip ").Append(H(data.ForZipVersion)).Append("\">\n");
        sb.Append("<title>").Append(H(title)).Append("</title>\n");
        if (IconDataUri.Value is { } icon)
        {
            sb.Append("<link rel=\"icon\" type=\"image/png\" href=\"").Append(icon).Append("\">\n");
        }
        sb.Append("<style>\n").Append(HtmlStyle).Append("</style>\n</head>\n<body>\n<main>\n");

        sb.Append("<header>\n<h1>").Append(H(title)).Append("</h1>\n");
        sb.Append("<p class=\"sub\">").Append(H(L("report_subtitle"))).Append(" · https://github.com/patagoniarobot/forzip</p>\n");
        sb.Append("</header>\n");

        HtmlSourceNote(sb, data);
        HtmlVerdict(sb, data.Verification);
        HtmlPackageKey(sb, data, culture);

        HtmlOperator(sb, data);
        HtmlCase(sb, data);
        HtmlEnvironment(sb, data);
        HtmlParameters(sb, data);
        HtmlFiles(sb, data, culture);
        if (data.FolderDigests.Count > 0)
        {
            HtmlFolderDigests(sb, data, culture);
        }

        HtmlSources(sb, data);
        if (data.OmittedFiles.Count > 0)
        {
            HtmlOmitted(sb, data, culture);
        }

        if (data.Volumes.Count > 0)
        {
            HtmlVolumes(sb, data, culture);
        }

        if (data.Verification != null)
        {
            HtmlVerificationDetail(sb, data.Verification);
        }

        sb.Append("<footer>\n<p><strong>").Append(H(L("report_disclaimer_title"))).Append("</strong></p>\n");
        Paragraphs(sb, L("report_disclaimer_text"));
        sb.Append("</footer>\n</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Qué es este papel: de dónde salió (si se armó a pedido) y que no es la prueba.</summary>
    private void HtmlSourceNote(StringBuilder sb, ReportData data)
    {
        sb.Append("<div class=\"note\">\n");
        if (data.SourceManifestName != null)
        {
            sb.Append("<p><strong>").Append(H(L("report_from_manifest"))).Append("</strong> · ")
              .Append(H(L("report_from_manifest_file"))).Append(": <span class=\"mono\">").Append(H(data.SourceManifestName))
              .Append("</span> · ").Append(H(L("report_from_manifest_at"))).Append(": <span class=\"nw\">")
              .Append(H(FormatLocal(DateTimeOffset.Now))).Append("</span></p>\n");
        }

        Paragraphs(sb, L("report_html_note"));
        sb.Append("</div>\n");
    }

    private void HtmlVerdict(StringBuilder sb, ReportVerification? verification)
    {
        if (verification == null)
        {
            return;
        }

        var kind = verification.Outcome switch
        {
            VerificationOutcome.Intact => "ok",
            VerificationOutcome.Compromised => "bad",
            _ => "doubt"
        };
        sb.Append("<div class=\"verdict ").Append(kind).Append("\">\n");
        sb.Append("<p>").Append(H(L("report_verification"))).Append(" · <span class=\"nw\">")
          .Append(H(FormatLocal(verification.VerifiedAt))).Append("</span>");
        if (!string.IsNullOrEmpty(verification.MachineName))
        {
            sb.Append(" · ").Append(H(L("report_env_hostname"))).Append(": ").Append(H(verification.MachineName!));
        }

        if (!string.IsNullOrEmpty(verification.UserName))
        {
            sb.Append(" · ").Append(H(L("report_env_user"))).Append(": ").Append(H(verification.UserName!));
        }

        sb.Append("</p>\n<p class=\"title\">").Append(H(verification.Verdict)).Append("</p>\n</div>\n");
    }

    /// <summary>La huella del paquete, arriba y destacada: es el dato que se coteja con la planilla de custodia.</summary>
    private void HtmlPackageKey(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        if (data.Operation != OperationType.Compression || data.ZipFilePath == null)
        {
            return;
        }

        sb.Append("<section class=\"key\">\n");
        sb.Append("<p class=\"label\">").Append(H(L(Is7z(data) ? "report_zip_hash_7z" : "report_zip_hash"))).Append("</p>\n");
        sb.Append("<table class=\"kv\">\n");
        Row(sb, L("report_global_file"), Path.GetFileName(data.ZipFilePath), mono: true);
        if (data.ZipFileSize.HasValue)
        {
            Row(sb, L("report_global_size"), FormatBytesWithUnit(data.ZipFileSize.Value, culture));
        }

        sb.Append("</table>\n");
        if (!string.IsNullOrEmpty(data.ZipHash))
        {
            sb.Append("<p class=\"hash\">SHA-256: ").Append(H(data.ZipHash!)).Append("</p>\n");
        }

        sb.Append("<p class=\"hint\">").Append(H(L("report_html_custody"))).Append("</p>\n");
        sb.Append("</section>\n");
    }

    private void HtmlOperator(StringBuilder sb, ReportData data)
    {
        var op = data.Operator;
        if (op == null)
        {
            return;
        }

        KvSection(sb, "report_operator", new (string, string?)[]
        {
            ("report_op_name", op.Name), ("report_op_title", op.Title), ("report_op_org", op.Organization),
            ("report_op_email", op.Email), ("report_op_phone", op.Phone)
        });
    }

    private void HtmlCase(StringBuilder sb, ReportData data) =>
        KvSection(sb, "report_case", new (string, string?)[]
        {
            ("report_case_number", data.CaseNumber), ("report_case_desc", data.CaseDescription),
            ("report_case_court", data.Court)
        });

    private void HtmlEnvironment(StringBuilder sb, ReportData data)
    {
        sb.Append("<h2>").Append(H(L("report_env"))).Append("</h2>\n");
        var env = data.Environment;
        if (env == null)
        {
            // Como en el texto: sin entorno registrado no se pone el de esta PC.
            sb.Append("<p>").Append(H(L("report_env_unrecorded"))).Append("</p>\n");
        }

        sb.Append("<table class=\"kv\">\n");
        if (env != null)
        {
            Row(sb, L("report_env_datetime"), FormatLocal(env.LocalTime));
            Row(sb, L("report_env_os"), env.OsVersion ?? string.Empty);
            Row(sb, L("report_env_hostname"), env.MachineName ?? string.Empty);
            Row(sb, L("report_env_user"), env.UserName ?? string.Empty);
        }

        Row(sb, L("report_env_version"), $"ForZip {data.ForZipVersion}");
        sb.Append("</table>\n");
    }

    private void HtmlParameters(StringBuilder sb, ReportData data)
    {
        sb.Append("<h2>").Append(H(L("report_params"))).Append("</h2>\n<table class=\"kv\">\n");
        Row(sb, L("report_param_operation"),
            L(data.Operation == OperationType.Compression ? "report_op_compression" : "report_op_hash_batch"));

        if (data.Operation == OperationType.Compression)
        {
            Row(sb, L("report_param_level"), $"{data.CompressionLevel} ({L($"report_level_{data.CompressionLevel}")})");
            Row(sb, L("report_param_encryption"), data.HasPassword
                ? L(Is7z(data) ? "report_encryption_aes_7z" : "report_encryption_aes")
                : L("report_encryption_none"));
            if (Is7z(data))
            {
                Row(sb, L("report_param_format"), L("report_format_7z"));
            }
        }

        if (data.Algorithms.Count > 0)
        {
            Row(sb, L("report_param_algorithms"),
                string.Join(", ", AlgorithmDisplayOrder.Where(data.Algorithms.Contains).Select(GetAlgorithmDisplayName)));
        }

        if (data.Operation == OperationType.Compression && !string.IsNullOrEmpty(data.ZipFilePath))
        {
            Row(sb, L(Is7z(data) ? "report_param_zipfile_7z" : "report_param_zipfile"), data.ZipFilePath!, mono: true);
        }

        if (data.OmittedFiles.Count > 0)
        {
            sb.Append("<tr><th>").Append(H(L("report_param_completeness"))).Append("</th><td class=\"warn\">")
              .Append(H(string.Format(CultureInfo.InvariantCulture, L("report_completeness_incomplete"), data.OmittedFiles.Count)))
              .Append("</td></tr>\n");
        }

        sb.Append("</table>\n");
    }

    private void HtmlFiles(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append("<h2>").Append(H(string.Format(CultureInfo.InvariantCulture, L("report_files"), data.FileResults.Count)))
          .Append("</h2>\n");

        // Una tabla por algoritmo, como en el texto: así cada hash entra entero en su columna.
        foreach (var algo in AlgorithmDisplayOrder.Where(data.Algorithms.Contains))
        {
            sb.Append("<h3>").Append(H(GetAlgorithmDisplayName(algo))).Append("</h3>\n");
            sb.Append("<table class=\"grid\">\n<colgroup><col class=\"n\"><col class=\"file\"><col class=\"size\"><col></colgroup>\n")
              .Append("<tr><th>").Append(H(L("report_hash_col_no"))).Append("</th><th>")
              .Append(H(L("report_file_col"))).Append("</th><th>").Append(H(L("report_size_col"))).Append("</th><th>")
              .Append(H(GetAlgorithmDisplayName(algo))).Append("</th></tr>\n");
            for (int i = 0; i < data.FileResults.Count; i++)
            {
                var file = data.FileResults[i];
                var hash = file.Hashes.TryGetValue(algo, out var h) ? h : string.Empty;
                sb.Append("<tr><td class=\"num\">").Append(i + 1).Append("</td><td class=\"mono\">").Append(H(file.FilePath ?? string.Empty))
                  .Append("</td><td class=\"num\">").Append(H($"{file.FileSize.ToString("N0", culture)} B"))
                  .Append("</td><td class=\"mono hash\">").Append(H(hash)).Append("</td></tr>\n");
            }

            sb.Append("</table>\n");
        }

        sb.Append("<table class=\"kv\">\n");
        Row(sb, L("total_files"), data.FileResults.Count.ToString(culture));
        Row(sb, L("total_size"), FormatBytesWithUnit(data.FileResults.Sum(f => f.FileSize), culture));
        sb.Append("</table>\n");
    }

    private void HtmlSources(StringBuilder sb, ReportData data)
    {
        if (!data.FileResults.Any(f => !string.IsNullOrEmpty(f.SourcePath) || f.ModifiedUtc.HasValue))
        {
            return;
        }

        sb.Append("<h2>").Append(H(L("report_sources"))).Append("</h2>\n");
        sb.Append("<table class=\"grid\">\n<colgroup><col class=\"n\"><col class=\"file\"><col><col class=\"date\"></colgroup>\n")
          .Append("<tr><th>").Append(H(L("report_hash_col_no"))).Append("</th><th>")
          .Append(H(L("report_file_col"))).Append("</th><th>").Append(H(L("report_src_origin"))).Append("</th><th>")
          .Append(H(L("report_src_modified"))).Append("</th></tr>\n");
        for (int i = 0; i < data.FileResults.Count; i++)
        {
            var file = data.FileResults[i];
            sb.Append("<tr><td class=\"num\">").Append(i + 1).Append("</td><td class=\"mono\">").Append(H(file.FilePath ?? string.Empty))
              .Append("</td><td class=\"mono\">").Append(H(file.SourcePath ?? string.Empty)).Append("</td><td class=\"num\">")
              .Append(file.ModifiedUtc.HasValue ? H(FormatUtc(file.ModifiedUtc.Value)) : string.Empty).Append("</td></tr>\n");
        }

        sb.Append("</table>\n");
    }

    private void HtmlOmitted(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append("<h2>").Append(H(L("report_omitted"))).Append("</h2>\n");
        sb.Append("<p class=\"warn\">").Append(H(L("report_omitted_count"))).Append(": ")
          .Append(data.OmittedFiles.Count.ToString(culture)).Append("</p>\n");

        for (int i = 0; i < data.OmittedFiles.Count; i++)
        {
            var file = data.OmittedFiles[i];
            sb.Append("<h3>[").Append(i + 1).Append("] <span class=\"mono\">").Append(H(file.EntryName)).Append("</span></h3>\n");
            sb.Append("<table class=\"kv\">\n");
            Row(sb, L("report_src_origin"), file.SourcePath, mono: true);
            Row(sb, L("report_global_size"), FormatBytesWithUnit(file.Size, culture));
            Row(sb, L("report_src_modified"), FormatUtc(file.ModifiedUtc));
            Row(sb, L("report_omitted_reason"), L(ReasonKey(file.Reason)));
            if (file.SeenAtUtc.HasValue)
            {
                Row(sb, L("report_omitted_seen"), FormatUtc(file.SeenAtUtc.Value));
            }

            if (file.WindowsErrorCode.HasValue)
            {
                Row(sb, L("report_omitted_code"), file.WindowsErrorCode.Value.ToString(CultureInfo.InvariantCulture) + " (" +
                    WindowsErrorName(file.WindowsErrorCode.Value) + ")");
            }

            if (!string.IsNullOrEmpty(file.SystemMessage))
            {
                Row(sb, L("report_omitted_message"), file.SystemMessage);
            }

            if (!string.IsNullOrEmpty(file.LinkTarget))
            {
                Row(sb, L("report_omitted_link_target"), file.LinkTarget, mono: true);
            }

            Row(sb, L("report_omitted_time"), FormatUtc(file.RecordedAtUtc));
            sb.Append("</table>\n");
        }

        sb.Append("<h3>").Append(H(L("report_omitted_basis_title"))).Append("</h3>\n");
        foreach (var key in OmissionBasisKeys(data))
        {
            Paragraphs(sb, L(key));
        }
    }

    private void HtmlFolderDigests(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append("<h2>").Append(H(L("report_folder_hashes"))).Append("</h2>\n");
        for (int i = 0; i < data.FolderDigests.Count; i++)
        {
            var folder = data.FolderDigests[i];
            sb.Append("<h3>[").Append(i + 1).Append("] <span class=\"mono\">").Append(H(folder.FolderPath)).Append("</span></h3>\n");
            sb.Append("<table class=\"kv\">\n");
            Row(sb, L("report_folder_files"), folder.FileCount.ToString(culture));
            Row(sb, L("report_global_size"), FormatBytesWithUnit(folder.TotalSize, culture));
            if (folder.IsComplete)
            {
                Row(sb, L("report_folder_forzip"), folder.ForZipSha256!, mono: true);
                Row(sb, L("report_folder_7zip"), folder.SevenZipDataSha256!, mono: true);
            }

            sb.Append("</table>\n");
            if (!folder.IsComplete)
            {
                sb.Append("<p class=\"warn\">").Append(H(string.Format(culture, L("report_folder_incomplete"), folder.UnreadableCount))).Append("</p>\n");
            }

            if (folder.LinksNotFollowed > 0)
            {
                sb.Append("<p class=\"warn\">").Append(H(string.Format(culture, L("report_folder_links"), folder.LinksNotFollowed))).Append("</p>\n");
            }
        }

        Paragraphs(sb, L("report_folder_note"));
    }

    private void HtmlVolumes(StringBuilder sb, ReportData data, CultureInfo culture)
    {
        sb.Append("<h2>").Append(H(L("report_volumes"))).Append("</h2>\n");
        sb.Append("<table class=\"grid\">\n<tr><th>").Append(H(L("report_hash_col_no"))).Append("</th><th>")
          .Append(H(L("report_file_col"))).Append("</th><th>").Append(H(L("report_global_size"))).Append("</th><th>SHA-256</th></tr>\n");
        for (int i = 0; i < data.Volumes.Count; i++)
        {
            var vol = data.Volumes[i];
            sb.Append("<tr><td class=\"num\">").Append(i + 1).Append("</td><td class=\"mono\">").Append(H(vol.FileName))
              .Append("</td><td class=\"num\">").Append(H(FormatBytesWithUnit(vol.Size, culture)))
              .Append("</td><td class=\"mono hash\">").Append(H(vol.Sha256)).Append("</td></tr>\n");
        }

        sb.Append("</table>\n");
    }

    private void HtmlVerificationDetail(StringBuilder sb, ReportVerification verification)
    {
        sb.Append("<h2>").Append(H(L("report_verification"))).Append("</h2>\n<table class=\"kv\">\n");
        Row(sb, L("report_verification_at"), FormatLocal(verification.VerifiedAt));
        if (!string.IsNullOrEmpty(verification.MachineName))
        {
            Row(sb, L("report_env_hostname"), verification.MachineName!);
        }

        if (!string.IsNullOrEmpty(verification.UserName))
        {
            Row(sb, L("report_env_user"), verification.UserName!);
        }

        Row(sb, L("report_verification_verdict"), verification.Verdict);
        sb.Append("</table>\n");
        if (!string.IsNullOrWhiteSpace(verification.Details))
        {
            sb.Append("<pre>").Append(H(verification.Details!.Replace("\r\n", "\n").Trim())).Append("</pre>\n");
        }
    }

    private void KvSection(StringBuilder sb, string titleKey, (string key, string? value)[] fields)
    {
        if (fields.All(f => string.IsNullOrWhiteSpace(f.value)))
        {
            return;
        }

        sb.Append("<h2>").Append(H(L(titleKey))).Append("</h2>\n<table class=\"kv\">\n");
        foreach (var (key, value) in fields.Where(f => !string.IsNullOrWhiteSpace(f.value)))
        {
            Row(sb, L(key), value!);
        }

        sb.Append("</table>\n");
    }

    private static void Row(StringBuilder sb, string label, string value, bool mono = false)
    {
        sb.Append("<tr><th>").Append(H(label)).Append("</th><td").Append(mono ? " class=\"mono\"" : string.Empty).Append('>')
          .Append(H(value)).Append("</td></tr>\n");
    }

    /// <summary>
    /// Un texto traducido como párrafos. Los textos del informe vienen cortados a 80 columnas para el .txt: un salto
    /// simple es solo el corte de línea, uno doble separa párrafos.
    /// </summary>
    private static void Paragraphs(StringBuilder sb, string text)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n"))
        {
            var joined = string.Join(' ', paragraph.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            if (joined.Length > 0)
            {
                sb.Append("<p>").Append(H(joined)).Append("</p>\n");
            }
        }
    }

    /// <summary>
    /// Todo dato va escapado: un nombre de archivo o de causa con "&lt;" es texto, no marcado. Solo los cinco caracteres
    /// que significan algo en HTML: WebUtility.HtmlEncode pasaba también los acentos a entidades numéricas, y el
    /// archivo dejaba de leerse como texto ("P&amp;#233;rez").
    /// </summary>
    private static string H(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");
}
