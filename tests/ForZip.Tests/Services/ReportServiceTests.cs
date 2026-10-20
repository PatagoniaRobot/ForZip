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

using System.Text;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class ReportServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly ReportService _service;

    public ReportServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_report_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _service = new ReportService(new LocalizationService());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir))
            {
                Directory.Delete(_workDir, recursive: true);
            }
        }
        catch
        {
            // Limpieza best-effort
        }
    }

    [Fact]
    public void GenerateReport_WithAllData_ContainsAllSections()
    {
        var data = BuildFullReportData();

        var report = _service.GenerateReport(data, "es");

        Assert.Contains("INFORME FORENSE DE INTEGRIDAD", report);
        Assert.Contains("DATOS DEL OPERADOR", report);
        Assert.Contains("DATOS DEL CASO", report);
        Assert.Contains("INFORMACIÓN DEL ENTORNO", report);
        Assert.Contains("PARÁMETROS DE LA OPERACIÓN", report);
        Assert.Contains("ARCHIVOS PROCESADOS", report);
        Assert.Contains("HASH GLOBAL DEL ARCHIVO ZIP", report);
        Assert.Contains("DISCLAIMER", report);
    }

    [Fact]
    public void GenerateReport_LongFileName_IsKeptWholeAndTheColumnsStayAligned()
    {
        // Pendiente conocido hasta 2.0.0: un nombre de más de 41 caracteres empujaba el tamaño y el hash de su fila.
        var data = BuildFullReportData();
        var longName = "Documentos/subcarpeta_con_nombre_largo/acta_de_secuestro_numero_4471_2026.pdf";
        data.FileResults[0].FilePath = longName;

        var lines = _service.GenerateReport(data, "es").Split("\r\n");

        // El nombre aparece entero, sin recortar.
        var nameLine = Array.FindIndex(lines, l => l.Contains(longName, StringComparison.Ordinal));
        Assert.True(nameLine >= 0);

        // El hash de esa fila arranca en la misma columna que el de la fila con nombre corto.
        var longHash = data.FileResults[0].Hashes[HashAlgorithmType.SHA256];
        var shortHash = data.FileResults[1].Hashes[HashAlgorithmType.SHA256];
        var longRow = lines.First(l => l.Contains(longHash, StringComparison.Ordinal));
        var shortRow = lines.First(l => l.Contains(shortHash, StringComparison.Ordinal));
        Assert.Equal(shortRow.IndexOf(shortHash, StringComparison.Ordinal), longRow.IndexOf(longHash, StringComparison.Ordinal));
        Assert.Equal(nameLine + 1, Array.IndexOf(lines, longRow));
    }

    [Fact]
    public void GenerateReport_InAnotherLanguage_DoesNotTouchTheApplicationLanguage()
    {
        // Antes se cambiaba el idioma global y se restauraba: dos avisos a todas las vistas por informe, y en
        // Empaquetar desde un hilo de fondo.
        var localization = new LocalizationService();
        localization.SetLanguage("es");
        var changes = 0;
        localization.LanguageChanged += () => changes++;
        var service = new ReportService(localization);

        var report = service.GenerateReport(BuildFullReportData(), "en");

        Assert.Contains("FORENSIC INTEGRITY REPORT", report);
        Assert.Equal("es", localization.CurrentLanguage);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void GenerateReport_WithOmittedFiles_DeclaresIncompleteAndDocumentsGrounds()
    {
        var data = BuildFullReportData();
        data.OmittedFiles = new List<OmittedFile> { BuildOmittedFile() };

        var report = _service.GenerateReport(data, "es");

        // Arriba, en los parámetros, y con su propia sección más abajo
        Assert.Contains("INCOMPLETO — 1 archivo(s) no incluido(s)", report);
        Assert.Contains("ARCHIVOS NO INCLUIDOS", report);
        Assert.Contains("bloqueado por el software de seguridad", report);
        Assert.Contains("clasificó como amenaza", report);
        Assert.DoesNotContain("estaban en el origen cuando el operador", report);
        Assert.Contains(@"C:\Evidencia\reporte\files\adjunto_correo.txt", report);
        Assert.Contains("225 (ERROR_VIRUS_INFECTED)", report);
        Assert.Contains("El archivo contiene un virus.", report);
        Assert.Contains("ACPO", report);
        Assert.Contains("ISO/IEC 27037:2012", report);
        Assert.Contains("no certifica que el resto del paquete esté libre", report);
        Assert.True(report.IndexOf("INCOMPLETO", StringComparison.Ordinal)
                    < report.IndexOf("ARCHIVOS PROCESADOS", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateReport_FileGoneSinceSelection_StatesFactsNotCause()
    {
        var data = BuildFullReportData();
        data.OmittedFiles = new List<OmittedFile> { BuildMissingFile() };

        var report = _service.GenerateReport(data, "es");

        Assert.Contains("presente al seleccionar, ausente al empaquetar", report);
        Assert.Contains("Seleccionado", report);
        Assert.Contains("2026-10-08T22:01:20Z", report);
        // Hechos, no causa: menciona la cuarentena como posibilidad, junto con otras.
        Assert.Contains("Una causa posible", report);
        Assert.Contains("movido", report);
        // Sin código de Windows no se inventa uno, y el párrafo de bloqueo no corresponde.
        Assert.DoesNotContain("ERROR_VIRUS", report);
        Assert.DoesNotContain("clasificó como amenaza", report);
        Assert.Contains("ISO/IEC 27037:2012", report);
    }

    [Fact]
    public void GenerateReport_BothReasons_IncludesBothGrounds()
    {
        var data = BuildFullReportData();
        data.OmittedFiles = new List<OmittedFile> { BuildOmittedFile(), BuildMissingFile() };

        var report = _service.GenerateReport(data, "es");

        Assert.Contains("INCOMPLETO — 2 archivo(s) no incluido(s)", report);
        Assert.Contains("clasificó como amenaza", report);
        Assert.Contains("estaban en el origen cuando el operador", report);
    }

    [Fact]
    public void GenerateManifestJson_FileGoneSinceSelection_HasReasonAndNoWindowsCode()
    {
        var data = BuildFullReportData();
        data.OmittedFiles = new List<OmittedFile> { BuildMissingFile() };

        using var json = System.Text.Json.JsonDocument.Parse(_service.GenerateManifestJson(data));
        var omitted = Assert.Single(json.RootElement.GetProperty("OmittedFiles").EnumerateArray());

        Assert.Equal("missing_at_packaging", omitted.GetProperty("Reason").GetString());
        Assert.False(omitted.TryGetProperty("WindowsErrorCode", out _));
        Assert.True(omitted.TryGetProperty("SeenAtUtc", out _));
    }

    private static OmittedFile BuildMissingFile() => new()
    {
        Reason = OmissionReason.MissingAtPackaging,
        EntryName = "reporte/files/otro_adjunto.txt",
        SourcePath = @"C:\Evidencia\reporte\files\otro_adjunto.txt",
        Size = 68,
        ModifiedUtc = new DateTimeOffset(2026, 10, 8, 14, 53, 5, TimeSpan.Zero),
        SeenAtUtc = new DateTimeOffset(2026, 10, 8, 22, 1, 20, TimeSpan.Zero),
        RecordedAtUtc = new DateTimeOffset(2026, 10, 8, 22, 2, 5, TimeSpan.Zero)
    };

    [Fact]
    public void GenerateReport_WithoutOmittedFiles_HasNoOmissionSection()
    {
        var report = _service.GenerateReport(BuildFullReportData(), "es");

        Assert.DoesNotContain("ARCHIVOS NO INCLUIDOS", report);
        Assert.DoesNotContain("INCOMPLETO", report);
    }

    [Fact]
    public void GenerateManifestJson_WithOmittedFiles_ListsThemWithReasonAndCode()
    {
        var data = BuildFullReportData();
        data.OmittedFiles = new List<OmittedFile> { BuildOmittedFile() };

        using var json = System.Text.Json.JsonDocument.Parse(_service.GenerateManifestJson(data));
        var root = json.RootElement;

        // 1.4 desde 2.0: todo empaquetado registra su entorno; las omisiones siguen como en 1.2.
        Assert.Equal("1.4", root.GetProperty("FormatVersion").GetString());
        var omitted = Assert.Single(root.GetProperty("OmittedFiles").EnumerateArray());
        Assert.Equal("reporte/files/adjunto_correo.txt", omitted.GetProperty("EntryName").GetString());
        Assert.Equal("security_software_blocked", omitted.GetProperty("Reason").GetString());
        Assert.Equal(225, omitted.GetProperty("WindowsErrorCode").GetInt32());
    }

    [Fact]
    public void GenerateManifestJson_WithoutOmittedFiles_OmitsTheKey()
    {
        using var json = System.Text.Json.JsonDocument.Parse(_service.GenerateManifestJson(BuildFullReportData()));

        Assert.False(json.RootElement.TryGetProperty("OmittedFiles", out _));
    }

    private static OmittedFile BuildOmittedFile() => new()
    {
        EntryName = "reporte/files/adjunto_correo.txt",
        SourcePath = @"C:\Evidencia\reporte\files\adjunto_correo.txt",
        Size = 68,
        ModifiedUtc = new DateTimeOffset(2026, 10, 8, 14, 53, 5, TimeSpan.Zero),
        WindowsErrorCode = SecuritySoftwareInterference.VirusInfectedError,
        SystemMessage = "El archivo contiene un virus.",
        RecordedAtUtc = new DateTimeOffset(2026, 10, 8, 14, 53, 52, TimeSpan.Zero)
    };

    [Fact]
    public async Task VerifyReport_WithMatchingSidecar_ReturnsValid()
    {
        var data = BuildFullReportData();
        var report = _service.GenerateReport(data, "es");
        var path = Path.Combine(_workDir, "report.txt");
        await _service.SaveReportAsync(report, path);
        await WriteSidecarAsync(path);

        var (isValid, _) = _service.VerifyReport(path);

        Assert.True(isValid);
    }

    [Fact]
    public async Task VerifyReport_WithoutSidecar_ReturnsInvalid()
    {
        var data = BuildFullReportData();
        var report = _service.GenerateReport(data, "es");
        var path = Path.Combine(_workDir, "report.txt");
        await _service.SaveReportAsync(report, path);

        var (isValid, _) = _service.VerifyReport(path);

        Assert.False(isValid);
    }

    [Fact]
    public async Task VerifyReport_TamperedReport_ReturnsInvalid()
    {
        var data = BuildFullReportData();
        var report = _service.GenerateReport(data, "es");
        var path = Path.Combine(_workDir, "report.txt");
        await _service.SaveReportAsync(report, path);
        await WriteSidecarAsync(path);

        // Alterar el informe después de generar el sidecar
        await File.AppendAllTextAsync(path, "modificación maliciosa");

        var (isValid, _) = _service.VerifyReport(path);

        Assert.False(isValid);
    }

    [Fact]
    public void GenerateManifestJson_RoundTrips_AllFiles()
    {
        var data = BuildFullReportData();

        var json = _service.GenerateManifestJson(data);

        Assert.Contains("\"FormatVersion\"", json);
        Assert.Contains("contrato_2024.pdf", json);
        // Los algoritmos se serializan por nombre, no por número
        Assert.Contains("SHA256", json);
    }

    private static async Task WriteSidecarAsync(string reportPath)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(reportPath)))
            .ToLowerInvariant();
        await File.WriteAllTextAsync(reportPath + ".sha256", $"{hash}  {Path.GetFileName(reportPath)}");
    }

    [Fact]
    public void GenerateReport_InSpanish_HasSpanishHeaders()
    {
        var data = BuildFullReportData();

        var report = _service.GenerateReport(data, "es");

        Assert.Contains("INFORME FORENSE DE INTEGRIDAD", report);
        Assert.DoesNotContain("FORENSIC INTEGRITY REPORT", report);
    }

    [Fact]
    public void GenerateReport_InEnglish_HasEnglishHeaders()
    {
        var data = BuildFullReportData();

        var report = _service.GenerateReport(data, "en");

        Assert.Contains("FORENSIC INTEGRITY REPORT", report);
        Assert.DoesNotContain("INFORME FORENSE DE INTEGRIDAD", report);
    }

    [Fact]
    public void GenerateReport_HashBatchMode_OmitsZipSection()
    {
        var data = BuildFullReportData();
        data.Operation = OperationType.HashBatch;
        data.ZipFilePath = null;
        data.ZipFileSize = null;
        data.ZipHash = null;

        var report = _service.GenerateReport(data, "es");

        Assert.DoesNotContain("HASH GLOBAL", report);
    }

    [Fact]
    public void GenerateReport_EmptyOperator_OmitsOperatorSection()
    {
        var data = BuildFullReportData();
        data.Operator = null;

        var report = _service.GenerateReport(data, "es");

        Assert.DoesNotContain("DATOS DEL OPERADOR", report);
    }

    [Fact]
    public void GenerateReport_7z_NamesTheFormat_AndZipReportIsUnchanged()
    {
        var zip = BuildFullReportData();
        var zipReport = _service.GenerateReport(zip, "es");
        Assert.Contains("HASH GLOBAL DEL ARCHIVO ZIP", zipReport);
        Assert.DoesNotContain("7z", zipReport, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Formato", zipReport);

        var exe7z = BuildFullReportData();
        exe7z.ArchiveFormat = ArchiveFormat.SevenZ;
        var report = _service.GenerateReport(exe7z, "es");
        Assert.Contains("HASH GLOBAL DEL ARCHIVO 7Z", report);
        Assert.Contains("Archivo 7z", report);
        Assert.Contains("AES-256 del contenido y de los nombres de archivo", report);
        Assert.Contains("7z (LZMA2, cada archivo comprimido por separado)", report);
        Assert.DoesNotContain("ARCHIVO ZIP", report);

        var english = _service.GenerateReport(exe7z, "en");
        Assert.Contains("7Z FILE HASH", english);
        Assert.Contains("AES-256 of contents and file names", english);
    }

    // -------------------------------------------------------------------------
    //  Opción B: el informe se arma a pedido desde el manifiesto
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateManifestJson_RecordsThePackagingEnvironment()
    {
        var data = BuildFullReportData();
        data.Environment = new PackagingEnvironment
        {
            LocalTime = new DateTimeOffset(2026, 10, 17, 15, 30, 0, TimeSpan.FromHours(-3)),
            OsVersion = "Microsoft Windows NT 10.0.26100.0",
            MachineName = "PERITO-02",
            UserName = "jperez"
        };

        using var json = System.Text.Json.JsonDocument.Parse(_service.GenerateManifestJson(data));
        var env = json.RootElement.GetProperty("Environment");

        Assert.Equal("1.4", json.RootElement.GetProperty("FormatVersion").GetString());
        Assert.Equal("PERITO-02", env.GetProperty("MachineName").GetString());
        Assert.Equal("jperez", env.GetProperty("UserName").GetString());
        Assert.Equal(TimeSpan.FromHours(-3), env.GetProperty("LocalTime").GetDateTimeOffset().Offset);
    }

    [Fact]
    public void ReportFromManifest_HasTheSameDataAsTheOneMadeWhenPackaging()
    {
        var data = BuildFullReportData();
        data.Environment = new PackagingEnvironment
        {
            LocalTime = new DateTimeOffset(2026, 10, 17, 15, 30, 0, TimeSpan.FromHours(-3)),
            OsVersion = "Microsoft Windows NT 10.0.26100.0",
            MachineName = "OTRA-PC-QUE-NO-ES-ESTA",
            UserName = "jperez"
        };
        data.ZipFilePath = "caso_542_evidencia.zip";
        var manifest = new VerificationService(new HashService(), new SignatureService())
            .ParseManifest(_service.GenerateManifestJson(data));

        var fromManifest = _service.FromManifest(manifest, "caso_542_evidencia.zip.manifest.json");
        var report = _service.GenerateReport(fromManifest, "es");

        // El entorno es el del empaquetado, no el de la PC donde se arma el informe.
        Assert.Contains("OTRA-PC-QUE-NO-ES-ESTA", report);
        Assert.DoesNotContain(Environment.MachineName, report);
        Assert.Contains("2026-10-17T15:30:00-03:00", report);
        Assert.Contains("INFORME ARMADO A PEDIDO", report);
        Assert.Contains("caso_542_evidencia.zip.manifest.json", report);
        Assert.Contains("IPP-2026-00542", report);
        Assert.Contains("Juzgado Federal Nro. 2, Neuquén", report);
        Assert.Contains("Juan Carlos Pérez", report);
        Assert.Contains(data.ZipHash!, report);
        foreach (var file in data.FileResults)
        {
            Assert.Contains(file.Hashes[HashAlgorithmType.SHA256], report);
            Assert.Contains(file.Hashes[HashAlgorithmType.SHA512], report);
        }
    }

    [Fact]
    public void ReportFromAnOldManifest_SaysTheEnvironmentWasNotRecorded()
    {
        var data = BuildFullReportData();
        data.Environment = null;
        var json = _service.GenerateManifestJson(data);
        Assert.Contains("\"FormatVersion\": \"1.1\"", json);
        var manifest = new VerificationService(new HashService(), new SignatureService()).ParseManifest(json);

        var report = _service.GenerateReport(_service.FromManifest(manifest, "viejo.zip.manifest.json"), "es");

        Assert.Contains("No registrado", report);
        Assert.DoesNotContain(Environment.MachineName, report);
    }

    [Fact]
    public void ReportWithVerification_RecordsTheVerdictAndTheDetail()
    {
        var data = BuildFullReportData();
        data.Verification = new ReportVerification
        {
            VerifiedAt = new DateTimeOffset(2026, 10, 20, 10, 42, 0, TimeSpan.FromHours(-3)),
            Verdict = "Evidencia íntegra — Todo coincide.",
            Details = "[OK] Documentos/contrato_2024.pdf\nResumen: 1 OK"
        };

        var report = _service.GenerateReport(data, "es");

        var verification = report.IndexOf("VERIFICACIÓN", StringComparison.Ordinal);
        Assert.True(verification > report.IndexOf("ARCHIVOS PROCESADOS", StringComparison.Ordinal));
        Assert.True(verification < report.IndexOf("DISCLAIMER", StringComparison.Ordinal));
        Assert.Contains("2026-10-20T10:42:00-03:00", report);
        Assert.Contains("Evidencia íntegra — Todo coincide.", report);
        Assert.Contains("  [OK] Documentos/contrato_2024.pdf", report);
    }

    // -------------------------------------------------------------------------
    //  Informe en HTML imprimible
    // -------------------------------------------------------------------------

    [Fact]
    public void ReportHtml_HasTheSameDataAsTheText()
    {
        var data = BuildFullReportData();

        var html = _service.GenerateReportHtml(data, "es");

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"es\">", html);
        Assert.Contains("INFORME FORENSE DE INTEGRIDAD", html);
        Assert.Contains("IPP-2026-00542", html);
        Assert.Contains("Juan Carlos Pérez", html);
        Assert.Contains(data.ZipHash!, html);
        foreach (var file in data.FileResults)
        {
            Assert.Contains(file.Hashes[HashAlgorithmType.SHA256], html);
            Assert.Contains(file.Hashes[HashAlgorithmType.SHA512], html);
        }

        // La huella del paquete va arriba, antes de los datos del operador, con la indicación de cotejarla.
        Assert.True(html.IndexOf(data.ZipHash!, StringComparison.Ordinal) < html.IndexOf("DATOS DEL OPERADOR", StringComparison.Ordinal));
        Assert.Contains("planilla de cadena de custodia", html);
    }

    [Fact]
    public void ReportHtml_CannotRunAnything_AndEscapesEveryValue()
    {
        var data = BuildFullReportData();
        data.CaseNumber = "<script>alert('x')</script>";
        data.Court = "Juzgado \"A\" & <b>B</b>";
        data.FileResults[0].FilePath = "carpeta/<img src=x onerror=alert(1)>.pdf";

        var html = _service.GenerateReportHtml(data, "es");

        // Ni scripts ni recursos externos: la política lo prohíbe (la única imagen es el ícono que viaja adentro) y
        // nada del contenido se vuelve marcado.
        Assert.Contains("Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:\"", html);
        Assert.Contains("<link rel=\"icon\" type=\"image/png\" href=\"data:image/png;base64,iVBORw0KGgo", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>B</b>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", html);
        Assert.Contains("Juzgado &quot;A&quot; &amp; &lt;b&gt;B&lt;/b&gt;", html);
        Assert.DoesNotContain("http://", html);
        Assert.Contains("carpeta/&lt;img src=x onerror=alert(1)&gt;.pdf", html);
    }

    [Fact]
    public void ReportHtml_ShowsTheVerdictWithItsColour()
    {
        var data = BuildFullReportData();
        data.Verification = new ReportVerification
        {
            Verdict = "Evidencia comprometida — Algo cambió.",
            Outcome = VerificationOutcome.Compromised,
            Details = "[ALTERADO] Documentos/contrato_2024.pdf"
        };

        var html = _service.GenerateReportHtml(data, "es");

        Assert.Contains("class=\"verdict bad\"", html);
        Assert.Contains("Evidencia comprometida — Algo cambió.", html);
        Assert.Contains("<pre>[ALTERADO] Documentos/contrato_2024.pdf</pre>", html);
    }

    [Fact]
    public void ReportHtml_WithOmittedFiles_CarriesTheBasis()
    {
        var data = BuildFullReportData();
        data.OmittedFiles = new List<OmittedFile> { BuildOmittedFile() };

        var html = _service.GenerateReportHtml(data, "es");

        Assert.Contains("ARCHIVOS NO INCLUIDOS", html);
        Assert.Contains("reporte/files/adjunto_correo.txt", html);
        Assert.Contains("ERROR_VIRUS_INFECTED", html);
        Assert.Contains("Fundamento de la omisión", html);
    }

    [Theory]
    [InlineData("informe.html", true)]
    [InlineData("informe.HTM", true)]
    [InlineData("informe.txt", false)]
    [InlineData("informe", false)]
    public void GenerateReportFor_ChoosesTheFormatByExtension(string name, bool html)
    {
        var content = _service.GenerateReportFor(name, BuildFullReportData(), "es");

        Assert.Equal(html, content.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal));
    }

    private static ReportData BuildFullReportData()
    {
        return new ReportData
        {
            Operator = new OperatorInfo
            {
                Name = "Juan Carlos Pérez",
                Title = "Perito Informático",
                Organization = "D.A.F.I.",
                Email = "jcperez@ejemplo.com",
                Phone = "+54 299 555-1234"
            },
            CaseNumber = "IPP-2026-00542",
            CaseDescription = "Fraude informático",
            Court = "Juzgado Federal Nro. 2, Neuquén",
            Operation = OperationType.Compression,
            CompressionLevel = 5,
            HasPassword = true,
            Algorithms = new HashSet<HashAlgorithmType>
            {
                HashAlgorithmType.SHA256,
                HashAlgorithmType.SHA512
            },
            ZipFilePath = @"C:\Evidencia\caso_542_evidencia.zip",
            ZipFileSize = 3_102_445,
            ZipHash = "d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5",
            FileResults = new List<HashResult>
            {
                new()
                {
                    FilePath = "Documentos\\contrato_2024.pdf",
                    FileSize = 1_245_678,
                    Hashes = new Dictionary<HashAlgorithmType, string>
                    {
                        [HashAlgorithmType.SHA256] = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
                        [HashAlgorithmType.SHA512] = new string('a', 128)
                    }
                },
                new()
                {
                    FilePath = "Documentos\\factura_marzo.xlsx",
                    FileSize = 342_109,
                    Hashes = new Dictionary<HashAlgorithmType, string>
                    {
                        [HashAlgorithmType.SHA256] = "b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3",
                        [HashAlgorithmType.SHA512] = new string('b', 128)
                    }
                }
            }
        };
    }
}
