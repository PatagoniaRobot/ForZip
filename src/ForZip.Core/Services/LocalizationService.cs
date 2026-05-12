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
using System.Reflection;
using System.Linq;
using System.Text.Json;
using ForZip.Core.Interfaces;

namespace ForZip.Core.Services;

public class LocalizationService : ILocalizationService
{
    private const string SpanishCode = "es";
    private const string EnglishCode = "en";
    private const string SpanishResource = "ForZip.Core.Resources.Strings_es.json";
    private const string EnglishResource = "ForZip.Core.Resources.Strings_en.json";

    private readonly Dictionary<string, string> _spanish;
    private readonly Dictionary<string, string> _english;
    private string _currentLanguage;

    public event Action? LanguageChanged;

    public LocalizationService()
    {
        _spanish = LoadResource(SpanishResource);
        _english = LoadResource(EnglishResource);
        _currentLanguage = DetectInitialLanguage();
    }

    public string CurrentLanguage => _currentLanguage;

    public string Get(string key)
    {
        var primary = _currentLanguage == SpanishCode ? _spanish : _english;
        if (primary.TryGetValue(key, out var value))
        {
            return value;
        }

        // Fallback al inglés si la clave no existe en el idioma actual
        if (_english.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        // Sin traducción disponible: devolver la clave entre corchetes para detectar faltantes en QA
        return $"[{key}]";
    }

    public void SetLanguage(string code)
    {
        var newLang = code == SpanishCode ? SpanishCode : EnglishCode;
        if (_currentLanguage != newLang)
        {
            _currentLanguage = newLang;
            LanguageChanged?.Invoke();
        }
    }

    public IEnumerable<string> GetAllKeys()
    {
        return _english.Keys.Union(_spanish.Keys);
    }

    private static Dictionary<string, string> LoadResource(string resourceName)
    {
        var assembly = typeof(LocalizationService).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso embebido no encontrado: {resourceName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return dict ?? new Dictionary<string, string>();
    }

    private static string DetectInitialLanguage()
    {
        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return ui == SpanishCode ? SpanishCode : EnglishCode;
    }
}
