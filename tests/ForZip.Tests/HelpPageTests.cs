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

using System.Text.RegularExpressions;
using ForZip.Core;
using Xunit;

namespace ForZip.Tests;

/// <summary>
/// La guía autocontenida (<c>Help_ForZip.html</c>, la que abre "Acerca de") declara la versión
/// en el encabezado. Quedó congelada en "v0.9.0 Beta" mientras la aplicación llegaba a la 1.2,
/// porque nada obligaba a tocarla al publicar. Estas pruebas cierran esa puerta.
/// </summary>
public class HelpPageTests
{
    private static string ReadHelpPage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Help_ForZip.html");
        Assert.True(File.Exists(path), $"No se encontró la guía de ayuda en {path}.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void HelpPage_DeclaresTheCurrentVersion()
    {
        var html = ReadHelpPage();

        Assert.Contains($"<div class=\"h-line3\">{AppInfo.DisplayVersion}</div>", html);
    }

    [Fact]
    public void HelpPage_DeclaresExactlyOneVersion()
    {
        // Evita que quede una versión vieja olvidada en otro lugar del documento.
        var html = ReadHelpPage();

        var versions = Regex.Matches(html, @"v\d+\.\d+\.\d+")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        Assert.Equal(new[] { AppInfo.DisplayVersion }, versions);
    }

    [Fact]
    public void HelpPage_HasNoExternalResources()
    {
        // ForZip es portable y no hace peticiones de red: la guía tampoco debe hacerlas, o en
        // una estación aislada se degrada sola. (Tenía un <link> a Google Fonts.)
        var html = ReadHelpPage();

        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"(src|href)\s*=\s*[""']\s*https?:", RegexOptions.IgnoreCase), html);
    }
}
