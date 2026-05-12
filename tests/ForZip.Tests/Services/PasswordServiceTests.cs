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
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class PasswordServiceTests
{
    private const string AmbiguousChars = "0OoIl1|";

    [Fact]
    public void GeneratePassword_AllClasses_ContainsAllClasses()
    {
        var service = new PasswordService();
        var options = new PasswordOptions
        {
            Length = 16,
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeDigits = true,
            IncludeSymbols = true
        };

        var password = service.GeneratePassword(options);

        Assert.Contains(password, c => char.IsUpper(c));
        Assert.Contains(password, c => char.IsLower(c));
        Assert.Contains(password, c => char.IsDigit(c));
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void GeneratePassword_OnlyDigits_ContainsOnlyDigits()
    {
        var service = new PasswordService();
        var options = new PasswordOptions
        {
            Length = 16,
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeDigits = true,
            IncludeSymbols = false
        };

        var password = service.GeneratePassword(options);

        Assert.All(password, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void GeneratePassword_ExcludeAmbiguous_DoesNotContainAmbiguous()
    {
        var service = new PasswordService();
        var options = new PasswordOptions
        {
            Length = 64,
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeDigits = true,
            IncludeSymbols = true,
            ExcludeAmbiguous = true
        };

        for (int trial = 0; trial < 50; trial++)
        {
            var password = service.GeneratePassword(options);
            foreach (var ambiguous in AmbiguousChars)
            {
                Assert.DoesNotContain(ambiguous.ToString(), password);
            }
        }
    }

    [Fact]
    public void GeneratePassword_CorrectLength_ReturnsRequestedLength()
    {
        var service = new PasswordService();
        var options = new PasswordOptions { Length = 24 };

        var password = service.GeneratePassword(options);

        Assert.Equal(24, password.Length);
    }

    [Fact]
    public void GeneratePassword_LengthTooShort_ThrowsArgumentException()
    {
        var service = new PasswordService();
        var options = new PasswordOptions { Length = 5 };

        Assert.Throws<ArgumentException>(() => service.GeneratePassword(options));
    }

    [Fact]
    public void GeneratePassword_LengthTooLong_ThrowsArgumentException()
    {
        var service = new PasswordService();
        var options = new PasswordOptions { Length = 200 };

        Assert.Throws<ArgumentException>(() => service.GeneratePassword(options));
    }

    [Fact]
    public void GeneratePassword_NoClassesEnabled_ThrowsArgumentException()
    {
        var service = new PasswordService();
        var options = new PasswordOptions
        {
            Length = 16,
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeDigits = false,
            IncludeSymbols = false
        };

        Assert.Throws<ArgumentException>(() => service.GeneratePassword(options));
    }

    [Fact]
    public void CalculateEntropy_KnownValues_ReturnsExpectedBits()
    {
        var service = new PasswordService();
        var options = new PasswordOptions
        {
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeDigits = true,
            IncludeSymbols = true,
            ExcludeAmbiguous = false
        };

        var entropy = service.CalculateEntropy(options, 12);

        // Pool 94 (26+26+10+32) → 12 * log2(94) ≈ 78.66
        Assert.Equal(12 * Math.Log2(94), entropy, 5);
    }

    [Fact]
    public void GeneratePassword_1000Passwords_AllMeetConstraints()
    {
        var service = new PasswordService();
        var options = new PasswordOptions
        {
            Length = 20,
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeDigits = true,
            IncludeSymbols = true
        };

        for (int i = 0; i < 1000; i++)
        {
            var password = service.GeneratePassword(options);

            Assert.Equal(20, password.Length);
            Assert.Contains(password, c => char.IsUpper(c));
            Assert.Contains(password, c => char.IsLower(c));
            Assert.Contains(password, c => char.IsDigit(c));
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        }
    }
}
