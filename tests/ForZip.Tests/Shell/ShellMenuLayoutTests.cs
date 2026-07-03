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
        Hash: "Calcular hash",
        Extract: "Extraer…",
        ExtractHere: "Extraer aquí",
        ExtractTo: "Extraer a subcarpeta",
        Verify: "Verificar");

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

        foreach (var root in ShellMenuLayout.RootSubKeys)
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
}
