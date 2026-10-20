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

    // La ventana principal y la del hash rápido son procesos distintos y comparten este archivo: un acceso que choca
    // con el del otro se reintenta un momento en vez de cerrar la aplicación.
    private const int Attempts = 10;
    private const int RetryDelayMs = 50;

    private readonly string _configPath;

    /// <summary>Configuración de solo lectura al lado del ejecutable, cuando los datos van a otra carpeta.</summary>
    private readonly string? _readOnlyFallbackPath;

    public ConfigService() : this(AppDataLocation.Directory)
    {
        var besideExe = Path.Combine(AppDataLocation.ExecutableDirectory, FileName);
        if (!string.Equals(Path.GetFullPath(besideExe), Path.GetFullPath(_configPath), StringComparison.OrdinalIgnoreCase))
        {
            _readOnlyFallbackPath = besideExe;
        }
    }

    // Constructor que recibe el directorio base permite aislar tests usando carpetas temporales
    public ConfigService(string baseDirectory)
    {
        _configPath = Path.Combine(baseDirectory, FileName);
    }

    public AppConfig Load()
    {
        // Instalado donde no se puede escribir, la primera vez vale la configuración que vino con el ejecutable.
        var path = !File.Exists(_configPath) && _readOnlyFallbackPath != null && File.Exists(_readOnlyFallbackPath)
            ? _readOnlyFallbackPath
            : _configPath;
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        try
        {
            var json = WithRetry(() => File.ReadAllText(path));
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

        // Se escribe aparte y se reemplaza de una vez: quien lee nunca ve un archivo a medio escribir.
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var temp = _configPath + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, json);
        try
        {
            WithRetry(() =>
            {
                File.Move(temp, _configPath, overwrite: true);
                return true;
            });
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static T WithRetry<T>(Func<T> action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            // "Acceso denegado" también: es lo que da Windows mientras el otro proceso termina de reemplazar el archivo.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                Thread.Sleep(RetryDelayMs * attempt);
            }
        }
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
