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

using System.Globalization;
using System.Text.Json;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

public class ConfigService : IConfigService
{
    private const string FileName = "config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;

    public ConfigService() : this(AppContext.BaseDirectory)
    {
    }

    // Constructor que recibe el directorio base permite aislar tests usando carpetas temporales
    public ConfigService(string baseDirectory)
    {
        _configPath = Path.Combine(baseDirectory, FileName);
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            return config ?? new AppConfig();
        }
        catch (JsonException)
        {
            // JSON corrupto: respaldar el archivo original antes de usar defaults
            BackupCorruptedFile();
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(_configPath, json);
    }

    private void BackupCorruptedFile()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var backupPath = $"{_configPath}.backup_{timestamp}";
            File.Copy(_configPath, backupPath, overwrite: true);
        }
        catch (IOException)
        {
            // No bloquear el arranque si el respaldo falla; el config se reemplazará por defaults
        }
    }
}
