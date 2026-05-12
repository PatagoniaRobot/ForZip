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
    public async Task VerifyReport_ReturnsDisabled()
    {
        var data = BuildFullReportData();
        var report = _service.GenerateReport(data, "es");
        var path = Path.Combine(_workDir, "report.txt");

        await _service.SaveReportAsync(report, path);
        var (isValid, details) = _service.VerifyReport(path);

        Assert.False(isValid);
        Assert.Contains("deshabilitada", details);
    }

    [Fact]
    public async Task VerifyReport_AnyFile_ReturnsDisabled()
    {
        var path = Path.Combine(_workDir, "any_file.txt");
        await File.WriteAllTextAsync(path, "contenido cualquiera", Encoding.UTF8);

        var (isValid, details) = _service.VerifyReport(path);

        Assert.False(isValid);
        Assert.Contains("deshabilitada", details);
    }

    [Fact]
    public async Task VerifyReport_BadFormat_ReturnsDisabled()
    {
        var path = Path.Combine(_workDir, "bad.txt");
        await File.WriteAllTextAsync(path, "este archivo no es un informe ForZip", Encoding.UTF8);

        var (isValid, details) = _service.VerifyReport(path);

        Assert.False(isValid);
        Assert.Contains("deshabilitada", details);
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
