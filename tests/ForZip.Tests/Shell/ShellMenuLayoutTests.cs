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

using ForZip.Core.Shell;
using Xunit;

namespace ForZip.Tests.Shell;

public class ShellMenuLayoutTests
{
    private static readonly ShellMenuLabels Labels = new(
        Menu: "ForZip",
        Compress: "Comprimir",
        CompressReserved: "Empaquetar en sobre reservado",
        Hash: "Calcular hash",
        Extract: "Extraer…",
        ExtractHere: "Extraer aquí",
        ExtractTo: "Extraer a subcarpeta",
        Verify: "Verificar",
        VerifySealed: "Verificar sobre",
        OpenSealed: "Abrir sobre…",
        SealedFileType: "Sobre sellado ForZip");

    private const string Exe = @"C:\Tools\ForZip\ForZip.exe";

    [Fact]
    public void Build_CommandsQuoteExeAndUsePlaceholder()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);

        // El comando de comprimir sobre archivos usa %1 y cita el ejecutable
        var compressCmd = entries.Single(e =>
            e.SubKey == @"Software\Classes\*\shell\ForZip\shell\01compress\command");
        Assert.Equal($"\"{Exe}\" compress \"%1\"", compressCmd.Value);

        // El fondo de carpeta usa %V (ruta de la carpeta), no %1
        var bgCmd = entries.Single(e =>
            e.SubKey == @"Software\Classes\Directory\Background\shell\ForZip\shell\01compress\command");
        Assert.Equal($"\"{Exe}\" compress \"%V\"", bgCmd.Value);
    }

    [Fact]
    public void Build_ArchiveScope_HasExtractAndVerify()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);

        var zipCommands = entries
            .Where(e => e.SubKey.StartsWith(@"Software\Classes\SystemFileAssociations\.zip\shell\ForZip\shell\")
                        && e.SubKey.EndsWith(@"\command"))
            .Select(e => e.Value)
            .ToList();

        Assert.Contains(zipCommands, v => v.Contains(" extract-here "));
        Assert.Contains(zipCommands, v => v.Contains(" verify "));
        Assert.Contains(zipCommands, v => v.Contains(" extract "));
    }

    [Fact]
    public void Build_EveryCascadeRoot_HasSubCommandsMarker()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);

        foreach (var root in ShellMenuLayout.RootSubKeys.Where(r => r.EndsWith(@"\shell\ForZip", StringComparison.Ordinal)))
        {
            Assert.Contains(entries, e =>
                e.SubKey == root && e.ValueName == "SubCommands" && e.Value == string.Empty);
            Assert.Contains(entries, e =>
                e.SubKey == root && e.ValueName == "MUIVerb" && e.Value == "ForZip");
        }
    }

    [Fact]
    public void Build_P7sScope_HasOnlyVerify()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);

        var p7sCommands = entries
            .Where(e => e.SubKey.StartsWith(@"Software\Classes\SystemFileAssociations\.p7s\shell\ForZip\shell\")
                        && e.SubKey.EndsWith(@"\command"))
            .Select(e => e.Value)
            .ToList();

        var verifyCmd = Assert.Single(p7sCommands);
        Assert.Equal($"\"{Exe}\" verify \"%1\"", verifyCmd);
    }

    [Fact]
    public void Build_AllCommandLines_AreParseableBack()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);

        var commands = entries.Where(e => e.SubKey.EndsWith(@"\command")).ToList();
        Assert.NotEmpty(commands);

        foreach (var cmd in commands)
        {
            // El token de verbo del comando debe ser interpretable por el parser de arranque.
            // Comando: "exe" <verbo> "%1"  → el segundo token es el verbo.
            var verbToken = cmd.Value.Split('"', System.StringSplitOptions.RemoveEmptyEntries)[1].Trim();
            var req = ShellArgs.Parse(new[] { verbToken, @"C:\x" });
            Assert.NotNull(req);
            Assert.NotEqual(ShellVerb.None, req!.Verb);
        }
    }

    [Fact]
    public void Build_ArchivosYCarpetas_OfrecenEmpaquetarEnSobreReservado()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);

        foreach (var root in new[]
        {
            @"Software\Classes\*\shell\ForZip",
            @"Software\Classes\Directory\shell\ForZip",
            @"Software\Classes\Directory\Background\shell\ForZip"
        })
        {
            var item = entries.Single(e => e.SubKey == root + @"\shell\02compressreserved" && e.ValueName == null);
            Assert.Equal("Empaquetar en sobre reservado", item.Value);
            Assert.Contains(entries, e => e.SubKey == root + @"\shell\02compressreserved\command"
                                          && e.Value.Contains(" compress-reserved "));
        }
    }

    [Fact]
    public void Build_ReciboYManifiesto_OfrecenVerificarSoloEnEsosJson()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);
        const string root = @"Software\Classes\SystemFileAssociations\.json\shell\ForZip";

        // Sin el filtro, "Verificar" aparecería en cualquier JSON del equipo. El manifiesto se sumó en las pruebas del
        // 2026-10-12: sobre él solo se ofrecían Empaquetar y Ensobrar.
        Assert.Contains(entries, e => e.SubKey == root && e.ValueName == "AppliesTo"
                                      && e.Value == "System.FileName:~\"*.receipt.json\" OR System.FileName:~\"*.manifest.json\"");
        var command = Assert.Single(entries, e => e.SubKey.StartsWith(root + @"\shell\") && e.SubKey.EndsWith(@"\command"));
        Assert.Equal($"\"{Exe}\" verify \"%1\"", command.Value);
        Assert.Contains(root, ShellMenuLayout.RootSubKeys);
    }

    [Fact]
    public void Build_VolumenesDesdeElSegundo_OfrecenVerificar_ConFiltroPositivo()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);
        const string root = @"Software\Classes\*\shell\ForZipVerifyVolume";

        // Un ítem suelto, no un segundo submenú "ForZip": el general sigue intacto y sin filtro.
        Assert.Contains(entries, e => e.SubKey == root && e.ValueName == "AppliesTo"
                                      && e.Value == "System.FileName:~\"*.zip.0??\" OR System.FileName:~\"*.7z.0??\"");
        Assert.Contains(entries, e => e.SubKey == root && e.ValueName == "MUIVerb" && e.Value == "ForZip: Verificar");
        Assert.Contains(entries, e => e.SubKey == root + @"\command" && e.Value == $"\"{Exe}\" verify \"%1\"");
        Assert.DoesNotContain(entries, e => e.SubKey == @"Software\Classes\*\shell\ForZip" && e.ValueName == "AppliesTo");
        Assert.Contains(root, ShellMenuLayout.RootSubKeys);
    }

    [Fact]
    public void Build_SobreSellado_OfreceAbrirYVerificar_SinDescifrarDesdeElMenu()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);
        const string root = @"Software\Classes\SystemFileAssociations\.fzs\shell\ForZip\shell\";

        var labels = entries
            .Where(e => e.SubKey.StartsWith(root, StringComparison.Ordinal) && e.ValueName == null
                        && !e.SubKey.EndsWith(@"\command", StringComparison.Ordinal))
            .Select(e => e.Value)
            .ToList();
        Assert.Equal(new[] { "Abrir sobre…", "Verificar sobre" }, labels);

        // "Abrir sobre…" solo carga el sobre en ForZip: el comando no lleva contraseña ni
        // destino, que se eligen adentro de la aplicación.
        var open = entries.Single(e => e.SubKey == root + @"01open\command");
        Assert.Equal($"\"{Exe}\" open-envelope \"%1\"", open.Value);
    }

    [Fact]
    public void Build_TipoDeArchivoDelSobre_TieneIconoYDobleClicQueSoloCarga()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);
        const string progId = @"Software\Classes\ForZip.SealedEnvelope";

        Assert.Contains(entries, e => e.SubKey == progId && e.ValueName == null && e.Value == "Sobre sellado ForZip");
        Assert.Contains(entries, e => e.SubKey == progId + @"\DefaultIcon" && e.Value == $"{Exe},0");
        Assert.Contains(entries, e => e.SubKey == progId + @"\shell\open\command"
                                      && e.Value == $"\"{Exe}\" open-envelope \"%1\"");
        Assert.Contains(progId, ShellMenuLayout.RootSubKeys);
    }

    [Fact]
    public void Build_LaExtensionFzs_NoSeBorraEnBloque()
    {
        // La clave .fzs puede tener datos de otros programas: la integración agrega y quita
        // solo lo suyo, así que no puede estar entre las claves que se borran enteras.
        Assert.DoesNotContain(ShellMenuLayout.RootSubKeys,
            r => r.Equals(ShellMenuLayout.SealedExtensionKey, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ShellMenuLayout.Build(Exe, Labels),
            e => e.SubKey.StartsWith(ShellMenuLayout.SealedExtensionKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_Recibo_NoOfreceAbrir()
    {
        var entries = ShellMenuLayout.Build(Exe, Labels);
        Assert.DoesNotContain(entries, e =>
            e.SubKey.StartsWith(@"Software\Classes\SystemFileAssociations\.json\", StringComparison.Ordinal)
            && e.Value.Contains("open-envelope"));
    }
}
