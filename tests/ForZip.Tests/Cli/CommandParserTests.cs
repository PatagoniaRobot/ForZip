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

using ForZip.Cli;
using Xunit;

namespace ForZip.Tests.Cli;

public class CommandParserTests
{
    [Theory]
    [InlineData("-p")]
    [InlineData("--password")]
    [InlineData("--sign-cert-password")]
    [InlineData("--cert-password")]
    public void Password_StartingWithDash_IsTakenAsTheValue(string option)
    {
        // Las contraseñas generadas incluyen '-': una que empezaba con él se tomaba como otra opción y el sobre no
        // se podía abrir con la contraseña del acta.
        var parser = new CommandParser(new[] { "unseal", "-i", "x.fzs", option, "-aB3$xK9", "-o", "destino" });

        Assert.Equal("-aB3$xK9", parser.GetOption(option, option));
        Assert.Equal("destino", parser.GetOption("-o", "--output"));
    }

    [Fact]
    public void OtherOption_FollowedByAnOption_HasNoValue()
    {
        var parser = new CommandParser(new[] { "zip", "--delete-sources", "-o", "salida" });

        Assert.Equal(string.Empty, parser.GetOption("--delete-sources", "--delete-sources"));
        Assert.Equal("salida", parser.GetOption("-o", "--output"));
    }

    [Theory]
    [InlineData("zip", "-i", "caso", "-o", "caso.zip", "--split")]
    [InlineData("zip", "-i", "caso", "--split", "--hash", "sha256")]
    [InlineData("zip", "-i", "caso", "-o")]
    public void OptionThatNeedsAValue_WithoutIt_IsReported(params string[] args)
    {
        // Hasta 2.0.0, "--split" sin valor se ignoraba en silencio y el paquete salía sin dividir.
        var parser = new CommandParser(args);

        var missing = CliOptions.MissingValue(parser.OptionsWithoutValue);

        Assert.Single(missing);
        Assert.Contains(missing[0], new[] { "--split", "-o" });
    }

    [Fact]
    public void Flags_WithoutValue_AreNotReported()
    {
        var parser = new CommandParser(new[] { "seal", "-i", "caso", "--gen-password", "--delete-sources", "--allow-cloud-folder" });

        Assert.Empty(CliOptions.MissingValue(parser.OptionsWithoutValue));
    }

    [Fact]
    public void EveryFlag_IsAnOptionOfSomeCommand()
    {
        // Un interruptor mal escrito en la lista dejaría pasar sin valor una opción que lo necesita.
        Assert.All(CliOptions.Flags, f => Assert.Contains(f, CliOptions.All));
    }
}
