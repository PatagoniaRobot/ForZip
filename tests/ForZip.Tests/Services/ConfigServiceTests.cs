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

public class ConfigServiceTests : IDisposable
{
    private readonly string _workDir;

    public ConfigServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
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
            // best-effort
        }
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var service = new ConfigService(_workDir);

        var config = service.Load();

        Assert.Equal("es", config.Language);
        Assert.Equal("dark", config.Theme);
        Assert.Equal(5, config.DefaultCompressionLevel);
        Assert.Single(config.DefaultHashAlgorithms);
        Assert.Contains(HashAlgorithmType.SHA256, config.DefaultHashAlgorithms);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_PreservesAllFields()
    {
        var service = new ConfigService(_workDir);
        var saved = new AppConfig
        {
            Language = "en",
            Theme = "light",
            DefaultCompressionLevel = 9,
            DefaultHashAlgorithms = new HashSet<HashAlgorithmType>
            {
                HashAlgorithmType.MD5,
                HashAlgorithmType.SHA512
            },
            DefaultOutputDirectory = @"C:\Output",
            Operator = new OperatorInfo
            {
                Name = "Tester",
                Email = "tester@example.com"
            }
        };

        service.Save(saved);
        var loaded = service.Load();

        Assert.Equal(saved.Language, loaded.Language);
        Assert.Equal(saved.Theme, loaded.Theme);
        Assert.Equal(saved.DefaultCompressionLevel, loaded.DefaultCompressionLevel);
        Assert.Equal(saved.DefaultHashAlgorithms, loaded.DefaultHashAlgorithms);
        Assert.Equal(saved.DefaultOutputDirectory, loaded.DefaultOutputDirectory);
        Assert.Equal(saved.Operator.Name, loaded.Operator.Name);
        Assert.Equal(saved.Operator.Email, loaded.Operator.Email);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndCreatesBackup()
    {
        var configPath = Path.Combine(_workDir, "config.json");
        File.WriteAllText(configPath, "{ esto no es JSON válido");

        var service = new ConfigService(_workDir);
        var config = service.Load();

        Assert.Equal("es", config.Language);
        Assert.Equal("dark", config.Theme);

        var backups = Directory.GetFiles(_workDir, "config.json.backup_*");
        Assert.NotEmpty(backups);
    }

    [Fact]
    public void Save_CreatesJsonFile()
    {
        var service = new ConfigService(_workDir);
        var config = new AppConfig { Language = "en" };

        service.Save(config);

        var configPath = Path.Combine(_workDir, "config.json");
        Assert.True(File.Exists(configPath));
        var content = File.ReadAllText(configPath);
        Assert.Contains("\"Language\"", content);
        Assert.Contains("\"en\"", content);
    }
}
