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

namespace ForZip.Core.Models;

/// <summary>
/// Dónde y cuándo se empaquetó: el equipo, el sistema, el usuario de Windows y la hora local con su huso. Se toma
/// al empaquetar y viaja en el manifiesto (esquema 1.4), porque el informe se arma a pedido y puede armarse otro
/// día en otra PC: tomarlos en ese momento mostraría el equipo equivocado.
/// </summary>
public class PackagingEnvironment
{
    public DateTimeOffset LocalTime { get; set; }
    public string? OsVersion { get; set; }
    public string? MachineName { get; set; }
    public string? UserName { get; set; }

    public static PackagingEnvironment Capture() => new()
    {
        LocalTime = DateTimeOffset.Now,
        OsVersion = Environment.OSVersion.VersionString,
        MachineName = Environment.MachineName,
        UserName = Environment.UserName
    };
}

/// <summary>
/// La verificación que se hizo antes de armar el informe a pedido: el informe la deja asentada para que el papel
/// diga qué se comprobó, cuándo y dónde.
/// </summary>
public class ReportVerification
{
    public DateTimeOffset VerifiedAt { get; set; } = DateTimeOffset.Now;
    public string? MachineName { get; set; } = Environment.MachineName;
    public string? UserName { get; set; } = Environment.UserName;

    /// <summary>El veredicto con las palabras de la pantalla ("Íntegra", "Evidencia comprometida"…).</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>El veredicto como dato, para presentarlo (en el HTML, el color del recuadro).</summary>
    public VerificationOutcome Outcome { get; set; } = VerificationOutcome.Incomplete;

    /// <summary>El detalle de la verificación tal como se mostró.</summary>
    public string? Details { get; set; }
}

/// <summary>Los tres veredictos de una verificación de paquete.</summary>
public enum VerificationOutcome
{
    Intact,
    Compromised,

    /// <summary>Sin alteraciones detectadas, pero sin poder verificar todo.</summary>
    Incomplete
}
