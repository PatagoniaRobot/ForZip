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

using Xunit;

namespace ForZip.Tests;

/// <summary>
/// [Fact] que se saltea si no está el 7z.exe oficial. Antes esos tests terminaban con un return y figuraban como
/// aprobados sin haber probado nada; un salteado se ve en el resumen ("Omitido"). Con OFFICIAL_7Z_EXE=none se
/// simula una máquina sin 7-Zip.
/// </summary>
public sealed class Requires7zFactAttribute : FactAttribute
{
    public Requires7zFactAttribute()
    {
        if (Find7z() == null)
        {
            Skip = "Necesita el 7z.exe oficial (Program Files u OFFICIAL_7Z_EXE) para comparar contra 7-Zip.";
        }
    }

    public static string? Find7z()
    {
        string? env = Environment.GetEnvironmentVariable("OFFICIAL_7Z_EXE");
        if (string.Equals(env, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidates = new[]
        {
            env,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe")
        };
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
    }
}
