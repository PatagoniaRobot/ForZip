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

using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Tests del acta de entrega de clave (§6). El acta es papel que va a estar en un
/// expediente dentro de años: lo que se verifica acá es que diga todo lo que el receptor
/// necesita y nada de lo que no debe decir.
/// </summary>
public class DeliveryActTests
{
    private const string Password = "K7mQ2xRt9Wp4Zc6Vn8Bh3Fj5Ld1Sg0A";

    private static readonly Guid PackageId = Guid.Parse("8f3a1c9e-4b21-4d7e-9a03-1c5e7f2b8d64");

    private static DeliveryActService CreateService() => new(new LocalizationService());

    private static DeliveryActData CreateData() => new()
    {
        PackageId = PackageId,
        SealedFileName = "FZS-8f3a1c9e.fzs",
        SealedSha256 = "3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b",
        SealedAtUtc = new DateTimeOffset(2026, 9, 4, 17, 22, 11, TimeSpan.Zero),
        Password = Password,
        CaseNumber = "CAUSA-4471/26",
        CaseDescription = "Peritaje de prueba",
        Court = "Juzgado de Instrucción Nro. 3",
        Operator = new OperatorInfo
        {
            Name = "Claudio Andino",
            Title = "Perito informático",
            Organization = "DAFI"
        },
        ForZipVersion = "v2.0.0"
    };

    [Fact]
    public void FormatPasswordForTranscription_AgrupaDeACuatro()
    {
        var service = CreateService();

        var formatted = service.FormatPasswordForTranscription("ABCDEFGHIJKL");

        Assert.Equal("ABCD EFGH IJKL", formatted);

        // Los espacios son ayuda de lectura: sacarlos devuelve la clave exacta.
        Assert.Equal("ABCDEFGHIJKL", formatted.Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void FormatPasswordForTranscription_LongitudNoMultiploDeCuatro_DejaElRestoAlFinal()
    {
        var service = CreateService();

        Assert.Equal("ABCD EFGH IJ", service.FormatPasswordForTranscription("ABCDEFGHIJ"));
        Assert.Equal("A", service.FormatPasswordForTranscription("A"));
        Assert.Equal(string.Empty, service.FormatPasswordForTranscription(string.Empty));
    }

    [Fact]
    public void Generate_LlevaLaClaveTranscribibleYLaHuellaDelSobre()
    {
        var data = CreateData();
        var service = CreateService();

        var acta = service.Generate(data, "es");

        Assert.Contains(service.FormatPasswordForTranscription(Password), acta, StringComparison.Ordinal);

        // La clave también va tal cual, en su propia línea, antes de la versión agrupada: con solo los grupos no
        // quedaba claro si los espacios iban (pruebas del 2026-10-12).
        var lines = acta.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var title = lines.FindIndex(l => l.Contains("CONTRASEÑA DEL PAQUETE", StringComparison.Ordinal));
        Assert.Equal(Password, lines[title + 2].Trim());
        Assert.True(lines.IndexOf(lines.First(l => l.Trim() == service.FormatPasswordForTranscription(Password))) > title + 2);

        Assert.Contains(data.SealedSha256, acta, StringComparison.Ordinal);
        Assert.Contains(data.SealedFileName, acta, StringComparison.Ordinal);
        Assert.Contains("FZS-8f3a1c9e", acta, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_LlevaLosDatosDeLaCausaYDelOperador()
    {
        var data = CreateData();

        var acta = CreateService().Generate(data, "es");

        Assert.Contains(data.CaseNumber!, acta, StringComparison.Ordinal);
        Assert.Contains(data.CaseDescription!, acta, StringComparison.Ordinal);
        Assert.Contains(data.Court!, acta, StringComparison.Ordinal);
        Assert.Contains(data.Operator!.Name!, acta, StringComparison.Ordinal);
        Assert.Contains("2026-09-04 17:22:11 UTC", acta, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_DejaEnBlancoLosCamposDelReceptor()
    {
        var acta = CreateService().Generate(CreateData(), "es");

        foreach (var etiqueta in new[] { "Fecha de recepción", "Aclaración", "DNI", "Firma" })
        {
            Assert.Contains(etiqueta, acta, StringComparison.Ordinal);
        }

        // Los campos vienen con la línea para completar a mano.
        Assert.Contains("____", acta, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ExplicaComoSeAbreElSobreConForZip()
    {
        var acta = CreateService().Generate(CreateData(), "es");

        Assert.Contains("ForZip", acta, StringComparison.Ordinal);
        Assert.Contains("v2.0.0", acta, StringComparison.Ordinal);
        Assert.Contains("github.com/patagoniarobot/forzip", acta, StringComparison.Ordinal);
        Assert.Contains("Python", acta, StringComparison.Ordinal);
        Assert.Contains(".receipt.json", acta, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_NoNombraElExtractorDescartado()
    {
        // La decisión del 2026-09-04 revirtió el ForZipOpen: el acta no puede prometerle al
        // receptor una herramienta que no se construye.
        var acta = CreateService().Generate(CreateData(), "es");

        Assert.DoesNotContain("ForZipOpen", acta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_AdvierteSobreLasCarpetasSincronizadas()
    {
        var acta = CreateService().Generate(CreateData(), "es");

        Assert.Contains("Drive", acta, StringComparison.Ordinal);
        Assert.Contains("OneDrive", acta, StringComparison.Ordinal);
        Assert.Contains("Dropbox", acta, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_AvisaQueSinLaClaveNoHayRecuperacion()
    {
        var acta = CreateService().Generate(CreateData(), "es");

        Assert.Contains("irrecuperable", acta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_EnIngles_ProduceElActaTraducidaYNoCambiaElIdiomaActual()
    {
        var localization = new LocalizationService();
        localization.SetLanguage("es");
        var service = new DeliveryActService(localization);

        var acta = service.Generate(CreateData(), "en");

        Assert.Contains("KEY HANDOVER RECORD", acta, StringComparison.Ordinal);
        Assert.Contains("PACKAGE PASSWORD", acta, StringComparison.Ordinal);
        Assert.DoesNotContain("CONTRASEÑA DEL PAQUETE", acta, StringComparison.Ordinal);

        // El idioma de la aplicación queda como estaba: el acta se emite en el idioma pedido.
        Assert.Equal("es", localization.CurrentLanguage);
    }

    [Fact]
    public void Generate_NoTieneClavesDeTraduccionSinResolver()
    {
        foreach (var language in new[] { "es", "en" })
        {
            var acta = CreateService().Generate(CreateData(), language);

            // LocalizationService devuelve "[clave]" cuando falta una traducción.
            Assert.DoesNotContain("[act_", acta, StringComparison.Ordinal);
            Assert.DoesNotContain("[report_", acta, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Generate_NoSuperaLasOchentaColumnas()
    {
        // Es papel: si una línea se pasa, la impresora la corta o la envuelve.
        foreach (var language in new[] { "es", "en" })
        {
            var acta = CreateService().Generate(CreateData(), language);

            foreach (var line in acta.Split("\r\n"))
            {
                Assert.True(line.Length <= 80, $"Línea de {line.Length} columnas en '{language}': {line}");
            }
        }
    }

    [Fact]
    public void GetSuggestedFileName_DerivaDelIdentificadorYNoDeLaCausa()
    {
        var name = CreateService().GetSuggestedFileName(PackageId);

        Assert.Equal("FZS-8f3a1c9e.acta.txt", name);
    }

    [Fact]
    public async Task SaveAsync_EscribeUtf8ConBom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forzip_acta_{Guid.NewGuid():N}.txt");
        try
        {
            var service = CreateService();
            await service.SaveAsync(service.Generate(CreateData(), "es"), path);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
