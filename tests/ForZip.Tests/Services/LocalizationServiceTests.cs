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

using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class LocalizationServiceTests
{
    [Fact]
    public void Get_ExistingKeyInSpanish_ReturnsSpanishValue()
    {
        var service = new LocalizationService();
        service.SetLanguage("es");

        var value = service.Get("cancel");

        Assert.Equal("Cancelar", value);
    }

    [Fact]
    public void Get_ExistingKeyInEnglish_ReturnsEnglishValue()
    {
        var service = new LocalizationService();
        service.SetLanguage("en");

        var value = service.Get("cancel");

        Assert.Equal("Cancel", value);
    }

    [Fact]
    public void Get_NonExistentKey_ReturnsKeyInBrackets()
    {
        var service = new LocalizationService();

        var value = service.Get("clave_inexistente");

        Assert.Equal("[clave_inexistente]", value);
    }

    [Fact]
    public void SetLanguage_SwitchesToEnglish_GetReturnsEnglish()
    {
        var service = new LocalizationService();
        service.SetLanguage("es");
        var spanish = service.Get("compress");

        service.SetLanguage("en");
        var english = service.Get("compress");

        Assert.Equal("Comprimir", spanish);
        Assert.Equal("Compress", english);
    }

    [Fact]
    public void SetLanguage_SwitchesToSpanish_GetReturnsSpanish()
    {
        var service = new LocalizationService();
        service.SetLanguage("en");
        var english = service.Get("settings");

        service.SetLanguage("es");
        var spanish = service.Get("settings");

        Assert.Equal("Settings", english);
        Assert.Equal("Ajustes", spanish);
    }
}
