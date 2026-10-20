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
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;

namespace ForZip.Cli.Commands;

/// <summary>
/// <c>forzip seal</c>: encierra un paquete forense ya generado en un sobre <c>.fzs</c> y
/// emite el recibo público.
/// </summary>
public class SealCommand
{
    private readonly ISealedPackageBuilder _builder;
    private readonly IPasswordService _passwordService;
    private readonly IDeliveryActService _actService;
    private readonly ICloudSyncDetector _cloudSyncDetector;
    private readonly ILocalizationService _localization;

    public SealCommand(
        ISealedPackageBuilder builder,
        IPasswordService passwordService,
        IDeliveryActService actService,
        ICloudSyncDetector cloudSyncDetector,
        ILocalizationService localization)
    {
        _builder = builder;
        _passwordService = passwordService;
        _actService = actService;
        _cloudSyncDetector = cloudSyncDetector;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var input = parser.GetOption("-i", "--input");
        var outputDir = parser.GetOption("-o", "--output");

        if (string.IsNullOrEmpty(input))
        {
            ShowUsage();
            return 1;
        }

        var artifacts = SealedArtifacts.Collect(input);
        if (artifacts.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: no se encontró nada que sellar en '{input}'.");
            Console.ResetColor();
            return 1;
        }

        outputDir = SealedArtifacts.ResolveOutputDirectory(outputDir, input);

        // Sobre el destino del sobre el aviso no bloquea: el .fzs es opaco y subirlo es
        // justamente para lo que se hizo. Lo que no debería es convivir con los originales.
        SealedArtifacts.EnsureNotCloudSynced(
            _cloudSyncDetector, outputDir,
            parser.HasOption("--allow-cloud-folder", "--allow-cloud-folder"),
            blocking: false);

        var password = ResolvePassword(parser, out var generated);
        if (string.IsNullOrEmpty(password))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: el sobre necesita una contraseña.");
            Console.ResetColor();
            return 1;
        }

        var actaPath = parser.GetOption("--acta", "--act");
        if (generated && string.IsNullOrEmpty(actaPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: con --gen-password hay que indicar --acta <ruta>, o la contraseña generada");
            Console.WriteLine("       se pierde y el sobre queda inabrible. No hay recuperación de clave.");
            Console.ResetColor();
            return 1;
        }

        if (!SealedArtifacts.CheckActaPath(actaPath))
        {
            return 1;
        }

        Console.WriteLine($"Sellando {artifacts.Count} archivo(s) en un sobre ForZip:");
        foreach (var artifact in artifacts)
        {
            Console.WriteLine($"  - {Path.GetFileName(artifact)}");
        }

        var skippedFolders = SealedArtifacts.SkippedSubfolders(input);
        if (skippedFolders.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Aviso: {skippedFolders.Count} subcarpeta(s) NO entran en el sobre (solo los archivos de la raíz):");
            foreach (var folder in skippedFolders)
            {
                Console.WriteLine($"  - {Path.GetFileName(folder)}{Path.DirectorySeparatorChar}");
            }
            Console.ResetColor();
        }

        var options = new SealedPackageOptions
        {
            ArtifactPaths = artifacts,
            OutputDirectory = outputDir,
            Password = password,
            SigningCertificatePath = parser.GetOption("--sign-cert", "--sign-cert"),
            SigningCertificatePassword = parser.GetOption("--sign-cert-password", "--sign-cert-password"),
            TimestampUrl = parser.GetOption("--timestamp-url", "--tsa")
        };

        var progress = SealedArtifacts.CreateProgress();
        SealedPackageResult result;
        try
        {
            result = await _builder.BuildAsync(options, progress, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or SigningCertificateException)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 2;
        }

        Console.WriteLine();
        SealedArtifacts.ReportResult(result);

        if (!string.IsNullOrEmpty(actaPath))
        {
            await WriteActAsync(parser, result, password, actaPath);
        }

        if (parser.HasOption("--delete-sources", "--delete-sources"))
        {
            SealedArtifacts.DeleteAll(artifacts);
            Console.WriteLine("Artefactos originales eliminados: fuera del sobre no queda nada en claro.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Aviso: los artefactos originales siguen en disco, en claro. Use --delete-sources");
            Console.WriteLine("       para que solo quede el sobre.");
            Console.ResetColor();
        }

        return 0;
    }

    /// <summary>
    /// Resuelve la contraseña por orden de preferencia: generada, la indicada en la línea de
    /// comandos, o pedida por consola con el eco apagado.
    /// </summary>
    private string ResolvePassword(CommandParser parser, out bool generated)
    {
        generated = false;

        if (parser.HasOption("--gen-password", "--gen-password"))
        {
            generated = true;
            var lengthText = parser.GetOption("-n", "--length");
            int? length = int.TryParse(lengthText, out var parsed) ? parsed : null;

            return _passwordService.GeneratePassword(SealedPasswordPolicy.BuildOptions(length));
        }

        var provided = parser.GetOption("-p", "--password");
        if (!string.IsNullOrEmpty(provided))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Aviso: --password queda registrado en el historial del shell. Omítalo para que");
            Console.WriteLine("       ForZip la pida por consola sin mostrarla.");
            Console.ResetColor();
            return provided;
        }

        return ConsolePassword.ReadTwice("Contraseña del sobre: ", "Repita la contraseña: ");
    }

    private async Task WriteActAsync(CommandParser parser, SealedPackageResult result, string password, string actaPath)
    {
        var data = new DeliveryActData
        {
            PackageId = result.Header.PackageId,
            SealedFileName = Path.GetFileName(result.SealedPath),
            SealedSha256 = result.Receipt.SealedSha256,
            SealedAtUtc = result.Header.SealedAtUtc,
            Password = password,
            CaseNumber = parser.GetOption("--case", "--case-number"),
            CaseDescription = parser.GetOption("--desc", "--description"),
            Court = parser.GetOption("--court", "--court"),
            Operator = BuildOperator(parser)
        };

        var language = parser.GetOption("--lang", "--language") ?? _localization.CurrentLanguage;
        await _actService.SaveAsync(_actService.Generate(data, language), actaPath);

        Console.WriteLine($"Acta de entrega generada: {actaPath}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("       El acta contiene la contraseña en claro. Imprímala, entréguela en mano bajo");
        Console.WriteLine("       firma y borre el archivo: no debe quedar junto al sobre ni subirse a ningún lado.");
        Console.ResetColor();
    }

    private static OperatorInfo? BuildOperator(CommandParser parser)
    {
        var name = parser.GetOption("--operator", "--operator-name");
        var title = parser.GetOption("--operator-title", "--operator-title");
        var organization = parser.GetOption("--operator-org", "--operator-org");

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(organization))
        {
            return null;
        }

        return new OperatorInfo { Name = name, Title = title, Organization = organization };
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Uso: forzip seal -i <archivo-o-carpeta> [-o <carpeta-destino>] [opciones]");
        Console.WriteLine();
        Console.WriteLine("  -i <ruta>              Paquete a sellar: una carpeta (entra todo lo que hay adentro)");
        Console.WriteLine("                         o un archivo (entran también sus sidecar y volúmenes).");
        Console.WriteLine("  -o <carpeta>           Carpeta de destino. El nombre del sobre lo decide ForZip:");
        Console.WriteLine("                         se deriva de un identificador aleatorio, nunca de la causa.");
        Console.WriteLine("  --gen-password         Genera la contraseña (requiere --acta para no perderla).");
        Console.WriteLine("  -p, --password <clave> Contraseña explícita. Queda en el historial del shell:");
        Console.WriteLine("                         si se omite, ForZip la pide por consola sin mostrarla.");
        Console.WriteLine("  -n, --length <n>       Longitud de la contraseña generada (por defecto 32, mínimo 24).");
        Console.WriteLine("  --acta <ruta>          Escribe el acta de entrega de clave en esa ruta.");
        Console.WriteLine("  --sign-cert <pfx>      Firma el recibo con un certificado X.509.");
        Console.WriteLine("  --sign-cert-password   Contraseña del PFX, si la tiene.");
        Console.WriteLine("  --timestamp-url <url>  Sella la firma del recibo con una TSA RFC 3161.");
        Console.WriteLine("  --delete-sources       Borra los artefactos originales al terminar.");
        Console.WriteLine("  --allow-cloud-folder   Sigue adelante aunque el destino esté sincronizado con la nube.");
        Console.WriteLine("  --case / --desc / --court / --operator   Datos para el acta.");
    }
}
