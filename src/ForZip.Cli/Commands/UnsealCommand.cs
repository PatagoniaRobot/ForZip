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

using ForZip.Core.Interfaces;
using ForZip.Core.Sealing;

namespace ForZip.Cli.Commands;

/// <summary>
/// <c>forzip unseal</c>: abre un sobre <c>.fzs</c> y vuelca el paquete que tiene adentro.
/// </summary>
public class UnsealCommand
{
    private readonly ISealedPackageOpener _opener;
    private readonly ICloudSyncDetector _cloudSyncDetector;

    public UnsealCommand(ISealedPackageOpener opener, ICloudSyncDetector cloudSyncDetector)
    {
        _opener = opener;
        _cloudSyncDetector = cloudSyncDetector;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var input = parser.GetOption("-i", "--input");
        var output = parser.GetOption("-o", "--output");

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
        {
            Console.WriteLine("Uso: forzip unseal -i <archivo.fzs> -o <carpeta-destino> [-p <clave>]");
            Console.WriteLine();
            Console.WriteLine("  Si se omite -p, ForZip pide la contraseña por consola sin mostrarla.");
            Console.WriteLine("  La carpeta de destino tiene que no existir o estar vacía: abrir un sobre");
            Console.WriteLine("  nunca sobrescribe archivos. Al terminar se verifica el contenido contra el");
            Console.WriteLine("  manifiesto que trae adentro.");
            Console.WriteLine("  No extraiga el contenido en una carpeta sincronizada con Drive, OneDrive");
            Console.WriteLine("  o Dropbox: se subiría en claro y automáticamente. ForZip lo comprueba y");
            Console.WriteLine("  aborta; --allow-cloud-folder lo permite igual.");
            return 1;
        }

        if (!File.Exists(input))
        {
            WriteError($"No se encontró el sobre: {input}");
            return 1;
        }

        var password = parser.GetOption("-p", "--password");
        if (string.IsNullOrEmpty(password))
        {
            password = ConsolePassword.Read("Contraseña del sobre: ");
        }

        if (string.IsNullOrEmpty(password))
        {
            WriteError("El sobre no se puede abrir sin contraseña.");
            return 1;
        }

        // El destino de extracción es el punto de fuga más verosímil de todo el perfil:
        // el contenido sale en claro y, en una carpeta sincronizada, sube solo.
        if (!SealedArtifacts.EnsureNotCloudSynced(
                _cloudSyncDetector, output,
                parser.HasOption("--allow-cloud-folder", "--allow-cloud-folder"),
                blocking: true))
        {
            return 1;
        }

        SealedOpenResult result;
        try
        {
            Console.WriteLine($"Abriendo el sobre: {input}");
            result = await _opener.OpenAsync(
                input, output, password, SealedArtifacts.CreateProgress(), CancellationToken.None);
        }
        catch (SealedPasswordException ex)
        {
            Console.WriteLine();
            WriteError(ex.Message);
            return 3;
        }
        catch (SealedIntegrityException ex)
        {
            Console.WriteLine();
            WriteError($"SOBRE ALTERADO: {ex.Message}");
            return 3;
        }
        catch (SealedFormatException ex)
        {
            Console.WriteLine();
            WriteError(ex.Message);
            return 3;
        }
        catch (IOException ex)
        {
            Console.WriteLine();
            WriteError(ex.Message);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"  Identificador : {result.Header.BaseName}");
        Console.WriteLine($"  Sellado (UTC) : {result.Header.SealedAtUtc:yyyy-MM-dd HH:mm:ss}");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Sobre abierto: {result.ExtractedFiles.Count} archivo(s) en {result.OutputDirectory}");
        Console.ResetColor();
        foreach (var file in result.ExtractedFiles)
        {
            Console.WriteLine($"  - {Path.GetFileName(file)}");
        }

        var exitCode = ReportVerification(result);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("El contenido quedó en claro en disco. No lo deje en una carpeta sincronizada");
        Console.WriteLine("con Drive, OneDrive o Dropbox.");
        if (result.UndeletedTempPath != null)
        {
            Console.WriteLine($"Aviso: no se pudo borrar el temporal '{result.UndeletedTempPath}'. Bórrelo a mano.");
        }
        Console.ResetColor();

        return exitCode;
    }

    /// <summary>
    /// Informa el veredicto contra el manifiesto. Un contenido que no coincide sale con código 3,
    /// el mismo que un sobre alterado: para un script, las dos cosas significan "no confiar".
    /// </summary>
    private static int ReportVerification(SealedOpenResult result)
    {
        if (result.HasNoManifest)
        {
            Console.WriteLine("Verificación: el sobre no trae un manifiesto forense; no hubo contra qué verificar.");
            return 0;
        }

        if (result.Verification == null)
        {
            WriteError($"No se pudo verificar el contenido: {result.VerificationError}");
            return 3;
        }

        var v = result.Verification;
        if (v.IsIntact)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[✓] Verificación: EVIDENCIA ÍNTEGRA ({v.OkCount} archivo(s) coinciden con el manifiesto).");
            Console.ResetColor();
            return 0;
        }

        if (!v.TamperDetected)
        {
            // Nada alterado, pero no se pudo verificar todo (archivos sin hash, contenido ilegible): no es lo mismo
            // que una alteración, y el código de salida es el de verificación incompleta, como en verify.
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[!] Verificación INCOMPLETA: no se detectaron alteraciones, pero el contenido no pudo verificarse por completo " +
                              $"({v.OkCount} OK, {v.UnverifiedCount} sin hash en el manifiesto).");
            if (!string.IsNullOrEmpty(v.ContentVerificationError))
            {
                Console.WriteLine($"    {v.ContentVerificationError}");
            }

            Console.ResetColor();
            return 2;
        }

        WriteError($"Verificación: EVIDENCIA ALTERADA ({v.AlteredCount} alterado(s), {v.MissingCount} faltante(s), {v.ExtraCount} añadido(s)).");
        if (!string.IsNullOrEmpty(v.ContentVerificationError))
        {
            Console.WriteLine($"  {v.ContentVerificationError}");
        }

        return 3;
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[X] {message}");
        Console.ResetColor();
    }
}
