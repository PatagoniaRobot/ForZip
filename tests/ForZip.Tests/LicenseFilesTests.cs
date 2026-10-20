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

using System.Xml.Linq;
using Xunit;

namespace ForZip.Tests;

/// <summary>
/// Lo que exigen las licencias al distribuir: el texto completo de Apache 2.0, el NOTICE y los avisos de cada
/// componente de terceros. Antes LICENSE traía solo el párrafo corto y NOTICE remitía a un archivo que no existía.
/// </summary>
public class LicenseFilesTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "publish.bat")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void License_IsTheFullApacheText_AndNoticeNamesTheEngineAndUnrar()
    {
        string root = RepoRoot();
        Assert.Contains("END OF TERMS AND CONDITIONS", File.ReadAllText(Path.Combine(root, "LICENSE")));
        string notice = File.ReadAllText(Path.Combine(root, "NOTICE"));
        Assert.Contains("PrZip", notice);
        Assert.Contains("UnRAR source code may be used in any software to handle RAR archives", notice);
        Assert.Contains("THIRD-PARTY-NOTICES.txt", notice);
        Assert.True(File.Exists(Path.Combine(root, "THIRD-PARTY-NOTICES.txt")));
    }

    [Fact]
    public void Licenses_TravelInsideTheExecutables_AndMatchTheFilesNextToThem()
    {
        // Quien actualiza desde la 1.4 recibe solo ForZip.exe, y la CLI puede circular sola: las licencias van adentro
        // (Acerca de → Licencias, forzip --licenses), y tienen que ser las mismas que se distribuyen sueltas.
        string root = RepoRoot();
        string combined = ForZip.Core.Services.LicenseTexts.Combined();
        var sources = new Dictionary<string, string>
        {
            ["NOTICE"] = Path.Combine(root, "NOTICE"),
            ["LICENSE"] = Path.Combine(root, "LICENSE"),
            ["THIRD-PARTY-NOTICES.txt"] = Path.Combine(root, "THIRD-PARTY-NOTICES.txt"),
            ["LICENSE-UNRAR.txt"] = Path.Combine(root, "PrZip", "src", "PrZip.Rar", "LICENSE-UNRAR.txt"),
        };

        Assert.Equal(sources.Keys, ForZip.Core.Services.LicenseTexts.Files);
        foreach (var (name, path) in sources)
        {
            Assert.Contains(File.ReadAllText(path).TrimEnd().ReplaceLineEndings(), combined.ReplaceLineEndings());
        }

        Assert.Contains("UnRAR source code may be used in any software", combined);
    }

    [Fact]
    public void EveryPackageReference_IsInTheThirdPartyNotices()
    {
        // Si se suma un paquete, su licencia tiene que sumarse al archivo de avisos que viaja con los .exe.
        string root = RepoRoot();
        string notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.txt"));
        var missing = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(p => XDocument.Load(p).Descendants("PackageReference"))
            .Select(r => (string?)r.Attribute("Include"))
            .OfType<string>()
            .Distinct()
            .Where(name => !notices.Contains(name, StringComparison.OrdinalIgnoreCase)
                           && !(name.StartsWith("Avalonia.", StringComparison.Ordinal) && notices.Contains("Avalonia UI", StringComparison.Ordinal)))
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryEmbeddedFont_IsInTheThirdPartyNotices()
    {
        // Las fuentes no son paquetes: viajan como recursos de la GUI y el test de arriba no las ve.
        string root = RepoRoot();
        string notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.txt"));
        string fonts = Path.Combine(root, "src", "ForZip.GUI", "Assets", "Fonts");
        Assert.True(Directory.Exists(fonts));
        foreach (var font in Directory.EnumerateFiles(fonts, "*.ttf"))
        {
            string name = Path.GetFileName(font);
            string family = name.StartsWith("IBMPlexMono", StringComparison.Ordinal) ? "IBM Plex Mono"
                : name.StartsWith("IBMPlexSans", StringComparison.Ordinal) ? "IBM Plex Sans"
                : name;
            Assert.True(notices.Contains(family, StringComparison.Ordinal), $"{name} no figura en THIRD-PARTY-NOTICES.txt");
        }

        Assert.Contains("SIL Open Font License", notices);
    }
}
