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

namespace ForZip.Core.Shell;

/// <summary>Acción solicitada al lanzar ForZip desde el menú contextual del Explorador.</summary>
public enum ShellVerb
{
    None,
    Compress,
    Extract,
    ExtractHere,
    ExtractTo,
    Hash,
    Verify
}

/// <summary>Petición resuelta a partir de los argumentos de línea de comandos.</summary>
public sealed record ShellRequest(ShellVerb Verb, IReadOnlyList<string> Paths);

/// <summary>
/// Vocabulario compartido entre el registro del menú contextual (que escribe las líneas de
/// comando) y el arranque de la GUI (que las interpreta). Mantener una única fuente de verdad
/// para los tokens evita que se desincronicen el registrador y el parser.
/// </summary>
public static class ShellArgs
{
    /// <summary>Token de línea de comandos para cada verbo (estable, en inglés, sin espacios).</summary>
    public static string VerbToken(ShellVerb verb) => verb switch
    {
        ShellVerb.Compress => "compress",
        ShellVerb.Extract => "extract",
        ShellVerb.ExtractHere => "extract-here",
        ShellVerb.ExtractTo => "extract-to",
        ShellVerb.Hash => "hash",
        ShellVerb.Verify => "verify",
        _ => "open"
    };

    private static ShellVerb MapToken(string token) => token.ToLowerInvariant() switch
    {
        "compress" or "zip" => ShellVerb.Compress,
        "extract" or "unzip" => ShellVerb.Extract,
        "extract-here" => ShellVerb.ExtractHere,
        "extract-to" => ShellVerb.ExtractTo,
        "hash" => ShellVerb.Hash,
        "verify" => ShellVerb.Verify,
        _ => ShellVerb.None
    };

    /// <summary>
    /// Interpreta los argumentos de arranque. Si el primero es un verbo conocido, el resto son
    /// rutas; si no hay verbo (p. ej. el usuario arrastró archivos sobre el .exe o abrió por
    /// asociación), se asume <see cref="ShellVerb.Compress"/> sobre todas las rutas.
    /// Devuelve <c>null</c> si no hay nada accionable (arranque normal de la app).
    /// </summary>
    public static ShellRequest? Parse(string[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return null;
        }

        var verb = MapToken(args[0]);

        if (verb == ShellVerb.None)
        {
            // Sin verbo explícito: tratar todos los argumentos como rutas (default: comprimir)
            var asPaths = args.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            return asPaths.Count > 0 ? new ShellRequest(ShellVerb.Compress, asPaths) : null;
        }

        var paths = args.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        return paths.Count > 0 ? new ShellRequest(verb, paths) : null;
    }
}
