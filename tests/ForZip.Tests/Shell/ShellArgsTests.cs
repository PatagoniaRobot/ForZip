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

public class ShellArgsTests
{
    [Theory]
    [InlineData("compress", ShellVerb.Compress)]
    [InlineData("zip", ShellVerb.Compress)]
    [InlineData("extract", ShellVerb.Extract)]
    [InlineData("extract-here", ShellVerb.ExtractHere)]
    [InlineData("extract-to", ShellVerb.ExtractTo)]
    [InlineData("hash", ShellVerb.Hash)]
    [InlineData("verify", ShellVerb.Verify)]
    public void Parse_KnownVerb_ExtractsVerbAndPaths(string token, ShellVerb expected)
    {
        var req = ShellArgs.Parse(new[] { token, @"C:\ev\a.zip" });

        Assert.NotNull(req);
        Assert.Equal(expected, req!.Verb);
        Assert.Equal(new[] { @"C:\ev\a.zip" }, req.Paths);
    }

    [Fact]
    public void Parse_NoArgs_ReturnsNull()
    {
        Assert.Null(ShellArgs.Parse(null));
        Assert.Null(ShellArgs.Parse(System.Array.Empty<string>()));
    }

    [Fact]
    public void Parse_BarePaths_DefaultsToCompress()
    {
        var req = ShellArgs.Parse(new[] { @"C:\ev\a.txt", @"C:\ev\b.txt" });

        Assert.NotNull(req);
        Assert.Equal(ShellVerb.Compress, req!.Verb);
        Assert.Equal(2, req.Paths.Count);
    }

    [Fact]
    public void Parse_VerbWithoutPaths_ReturnsNull()
    {
        Assert.Null(ShellArgs.Parse(new[] { "extract" }));
    }

    [Fact]
    public void VerbToken_RoundTripsThroughParse()
    {
        foreach (ShellVerb verb in System.Enum.GetValues<ShellVerb>())
        {
            if (verb == ShellVerb.None) continue;
            var token = ShellArgs.VerbToken(verb);
            var req = ShellArgs.Parse(new[] { token, @"C:\x" });
            Assert.Equal(verb, req!.Verb);
        }
    }
}
