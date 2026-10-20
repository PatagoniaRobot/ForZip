// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ForZip.Core.Sealing;
using ForZip.Core.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ForZip.Cli.Commands;

public class ZipCommand
{
    private readonly IZipService _zipService;
    private readonly IReportService _reportService;
    private readonly IHashService _hashService;
    private readonly ISignatureService _signatureService;
    private readonly ISealedPackageBuilder _sealedPackageBuilder;
    private readonly IPasswordService _passwordService;
    private readonly IDeliveryActService _actService;
    private readonly ILocalizationService _localization;

    public ZipCommand(
        IZipService zipService,
        IReportService reportService,
        IHashService hashService,
        ISignatureService signatureService,
        ISealedPackageBuilder sealedPackageBuilder,
        IPasswordService passwordService,
        IDeliveryActService actService,
        ILocalizationService localization)
    {
        _zipService = zipService;
        _reportService = reportService;
        _hashService = hashService;
        _signatureService = signatureService;
        _sealedPackageBuilder = sealedPackageBuilder;
        _passwordService = passwordService;
        _actService = actService;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        // -i se puede repetir: varios archivos y carpetas en un solo paquete, como la selección múltiple de la GUI.
        var inputs = parser.GetOptionValues("-i", "--input");
        var output = parser.GetOption("-o", "--output");
        var password = parser.GetOption("-p", "--password");
        var levelStr = parser.GetOption("-l", "--level") ?? "5";
        var hashStr = parser.GetOption("--hash", "--hashes");
        var reportFile = parser.GetOption("--report", "--report-file");

        // Datos de cadena de custodia y preferencias (paridad con la GUI)
        var operatorName = parser.GetOption("--operator", "--operator-name");
        var caseNumber = parser.GetOption("--case", "--case-number");
        var court = parser.GetOption("--court", "--court");
        var lang = parser.GetOption("--lang", "--language") ?? _localization.CurrentLanguage;
        var signCert = parser.GetOption("--sign-cert", "--sign-cert");
        var signCertPassword = parser.GetOption("--sign-cert-password", "--sign-cert-password");
        var timestampUrl = parser.GetOption("--timestamp-url", "--tsa");
        var splitStr = parser.GetOption("--split", "--volume-size");
        var formatStr = parser.GetOption("--format", "--format");

        if (inputs.Count == 0 || inputs.Any(string.IsNullOrEmpty))
        {
            Console.WriteLine("Uso: forzip zip -i <origen> [-i <otro origen> ...] [-o <paquete>] [opciones]");
            return 1;
        }

        var repeatedInput = inputs.GroupBy(i => Path.TrimEndingDirectorySeparator(Path.GetFullPath(i)), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (repeatedInput != null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: el origen '{repeatedInput.Key}' se indicó más de una vez. No se hizo nada.");
            Console.ResetColor();
            return 1;
        }

        // Formato: --format manda; si no está, lo dice la extensión de -o (caso.7z → 7z); si no, ZIP.
        ArchiveFormat format;
        try
        {
            format = formatStr != null
                ? ParseFormat(formatStr)
                : output != null ? ArchiveFormats.FromOutputPath(output) : ArchiveFormat.Zip;
        }
        catch (ArgumentException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        if (string.IsNullOrEmpty(output) && inputs.Count > 1)
        {
            // Con varios orígenes no hay un nombre obvio para el paquete: lo elige el operador, como en la GUI.
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: con varios orígenes indique el paquete con -o. No se hizo nada.");
            Console.ResetColor();
            return 1;
        }

        if (string.IsNullOrEmpty(output))
        {
            // Al lado del origen y con su nombre. Antes, con una barra final ("-i C:\caso\") la carpeta se
            // calculaba sin quitarla y el paquete quedaba dentro de la evidencia.
            output = PackageOutput.DefaultFor(inputs[0], ArchiveFormats.Extension(format));
            if (output == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: el origen es la raíz de una unidad; indique dónde dejar el paquete con -o.");
                Console.ResetColor();
                return 1;
            }
        }

        int level = int.TryParse(levelStr, out var l) ? l : 5;
        if (!CliOptions.TryParseAlgorithms(hashStr, out var algorithms, out var algoError))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {algoError}");
            Console.ResetColor();
            return 1;
        }
        long? splitSize = ParseSize(splitStr);

        // Perfil de confidencialidad. Es un eje aparte del resto de las opciones: al elegir
        // Reservado, el perfil gobierna cifrado, contraseña y manifiesto,
        // y no se negocian de a una. Una configuración de doce casillas sobre evidencia
        // sensible es un error silencioso esperando a suceder.
        var profile = ParseProfile(parser.GetOption("--profile", "--profile"));
        var actaPath = parser.GetOption("--acta", "--act");
        var generatedPassword = false;

        if (profile == ConfidentialityProfile.Reserved && format == ArchiveFormat.SevenZ)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: el perfil Reservado empaqueta siempre en ZIP, que es lo que lleva el sobre .fzs");
            Console.WriteLine("       (y lo que saben abrir fzs_extract.py y Open_FZS.html). Use -o con .zip o quite --format 7z.");
            Console.ResetColor();
            return 1;
        }

        if (profile == ConfidentialityProfile.Reserved)
        {
            if (string.IsNullOrEmpty(actaPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: el perfil Reservado exige --acta <ruta>. La contraseña se genera sola y");
                Console.WriteLine("       solo queda escrita en el acta: sin ella el paquete es irrecuperable.");
                Console.ResetColor();
                return 1;
            }

            // Se comprueba antes de empaquetar: el acta se escribe al final, después de todo el trabajo.
            if (!SealedArtifacts.CheckActaPath(actaPath))
            {
                return 1;
            }

            if (string.IsNullOrEmpty(password))
            {
                password = _passwordService.GeneratePassword(SealedPasswordPolicy.BuildOptions());
                generatedPassword = true;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Aviso: el perfil Reservado usa contraseña generada. Con --password la clave la");
                Console.WriteLine("       eligió una persona, que es exactamente la vulnerabilidad que el perfil evita.");
                Console.ResetColor();
            }

            // Un informe en texto quedaría en claro al lado del sobre, con los datos de la causa: justo lo que el
            // perfil existe para evitar. Quien abre el sobre lo arma con verify --write-report.
            if (!string.IsNullOrEmpty(reportFile))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: el perfil Reservado no escribe informe (--report): quedaría en claro fuera del sobre.");
                Console.WriteLine("       Quien abra el sobre lo arma después con: forzip verify -m <manifiesto> --write-report <informe.txt>");
                Console.ResetColor();
                return 1;
            }

            // El sobre necesita el manifiesto adentro: es la contraparte del recibo.
            algorithms.Add(HashAlgorithmType.SHA256);
        }

        // El certificado se prueba antes de empaquetar: una contraseña de PFX mal tipeada aparecía recién al firmar,
        // con el paquete hecho (y, en Reservado, al firmar el recibo, con el sobre hecho y descartado).
        if (!string.IsNullOrEmpty(signCert))
        {
            try
            {
                SignatureService.EnsureUsableCertificate(signCert, signCertPassword);
            }
            catch (SigningCertificateException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("       No se empaquetó nada.");
                Console.ResetColor();
                return 1;
            }
        }

        // Nunca encima de un paquete anterior (ni de su informe): se avisa antes de empezar. El núcleo igual lo
        // impide para el paquete y sus volúmenes.
        if (PackageOutput.IsFolder(output))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: -o indica una carpeta ({output}), no un paquete. No se hizo nada.");
            Console.WriteLine("       Agregue el nombre del archivo, por ejemplo -o carpeta\\caso.zip.");
            Console.ResetColor();
            return 1;
        }

        if (PackageOutput.FindExisting(output, reportFile) is { Count: > 0 } existing)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: ya existe un paquete o informe con ese nombre; ForZip no sobrescribe:");
            foreach (var path in existing)
            {
                Console.WriteLine($"  - {path}");
            }
            Console.WriteLine("       Elija otro nombre con -o (y --report), o mueva los existentes.");
            Console.ResetColor();
            return 1;
        }

        // El manifiesto es el registro del empaquetado: sale con --hash (o con --report, que se arma con sus datos).
        // Sin ninguno de los dos, los datos de custodia y la firma no quedan en ningún lado. Antes se aceptaban y se
        // descartaban en silencio.
        var recordPackage = algorithms.Count > 0 || !string.IsNullOrEmpty(reportFile);
        if (!recordPackage)
        {
            var unused = new[]
            {
                (operatorName, "--operator"), (caseNumber, "--case"), (court, "--court"),
                (signCert, "--sign-cert"), (timestampUrl, "--timestamp-url")
            }.Where(o => !string.IsNullOrEmpty(o.Item1)).Select(o => o.Item2).ToList();
            if (unused.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Aviso: sin --hash no se genera manifiesto, así que {string.Join(", ", unused)} " +
                                  (unused.Count == 1 ? "no queda registrado" : "no quedan registrados") + " en ningún lado.");
                Console.WriteLine("       Agregue --hash sha256 para dejar constancia.");
                Console.ResetColor();
            }
        }

        // La carpeta de salida se crea si no existe, como hace unzip. Dentro de la evidencia no: ahí el núcleo se
        // niega a empaquetar y quedaría una carpeta vacía nueva en el origen.
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)
            && PackageOutput.FindSourceContaining(inputs, output) == null)
        {
            Directory.CreateDirectory(outputDir);
        }

        var reportDir = string.IsNullOrEmpty(reportFile) ? null : Path.GetDirectoryName(Path.GetFullPath(reportFile));
        if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir)
            && PackageOutput.FindSourceContaining(inputs, reportFile!) == null)
        {
            Directory.CreateDirectory(reportDir);
        }

        var options = new ZipOptions
        {
            SourcePaths = inputs,
            OutputPath = output,
            CompressionLevel = level,
            Password = password,
            HashAlgorithms = algorithms,
            SplitSize = splitSize,
            Format = format
        };

        Console.WriteLine($"Comprimiendo: {string.Join(", ", inputs)} -> {output}");
        if (format == ArchiveFormat.SevenZ) Console.WriteLine("Formato: 7z (LZMA2, cada archivo por separado, todos los núcleos).");
        if (!string.IsNullOrEmpty(password))
        {
            Console.WriteLine(format == ArchiveFormat.SevenZ
                ? "Cifrado: AES-256 activado (contenido y nombres de archivo)."
                : "Cifrado: AES-256 activado.");
        }
        if (splitSize.HasValue) Console.WriteLine($"División en volúmenes de {FormatSize(splitSize.Value)} (.001, .002, …).");
        
        var sw = Stopwatch.StartNew();
        var progress = new Progress<(long processed, long total)>(p =>
        {
            if (p.total > 0)
            {
                var percent = (int)(100.0 * p.processed / p.total);
                Console.Write($"\rProgreso: [{new string('#', percent / 5)}{new string('-', 20 - percent / 5)}] {percent}% ");
            }
        });

        var result = await _zipService.CompressAsync(options, progress, CancellationToken.None);
        sw.Stop();

        Console.WriteLine($"\n¡Éxito! Tiempo: {sw.Elapsed.TotalSeconds:F2}s");
        if (result.IsSplit)
        {
            Console.WriteLine($"Generados {result.Volumes.Count} volúmenes: " +
                              $"{result.Volumes[0].FileName} … {result.Volumes[^1].FileName}");
        }

        PrintOmittedFiles(result);

        if (recordPackage)
        {
            if (algorithms.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Aviso: sin --hash no se registran hashes por archivo; la verificación de contenido no será posible.");
                Console.ResetColor();
            }

            // Hash global y tamaño del ZIP lógico (concatenación de volúmenes si está dividido)
            var (zipHash, zipSize) = await ComputeLogicalZipHashAsync(output, result);

            var data = new ReportData
            {
                Operator = string.IsNullOrWhiteSpace(operatorName) ? null : new OperatorInfo { Name = operatorName },
                CaseNumber = caseNumber,
                Court = court,
                Operation = OperationType.Compression,
                CompressionLevel = level,
                HasPassword = !string.IsNullOrEmpty(password),
                Algorithms = algorithms,
                // Ruta completa, como en la GUI: "pk/caso.zip" en un informe no dice dónde estaba el paquete.
                ZipFilePath = Path.GetFullPath(output),
                ArchiveFormat = format,
                ZipFileSize = zipSize,
                ZipHash = zipHash,
                Volumes = result.Volumes.ToList(),
                FileResults = result.FileHashes,
                OmittedFiles = result.OmittedFiles
            };

            // Manifiesto JSON (fuente de verdad para verificación automática), junto al ZIP
            var manifestPath = output + ".manifest.json";
            await File.WriteAllTextAsync(manifestPath, _reportService.GenerateManifestJson(data));
            Console.WriteLine($"Manifiesto forense generado: {manifestPath}");

            // Firma digital del manifiesto (CMS/PKCS#7) con el certificado del operador,
            // con sello de tiempo RFC 3161 opcional (--timestamp-url)
            if (!string.IsNullOrEmpty(signCert))
            {
                try
                {
                    await _signatureService.SignAsync(manifestPath, signCert, signCertPassword, timestampUrl, CancellationToken.None);
                    Console.WriteLine($"Manifiesto firmado digitalmente: {manifestPath}.p7s");
                    if (!string.IsNullOrEmpty(timestampUrl))
                    {
                        Console.WriteLine($"Sello de tiempo RFC 3161 incluido (TSA: {timestampUrl}).");
                    }
                }
                catch (TimestampUnavailableException ex)
                {
                    Console.WriteLine($"Manifiesto firmado digitalmente: {manifestPath}.p7s");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Aviso: la firma se generó SIN sello de tiempo. {ex.Message}");
                    Console.ResetColor();
                }
            }

            // Informe en texto, solo si se pidió. Sin .sha256: lo recalcula cualquiera que edite el informe, y daba
            // una protección que no tenía. Lo que prueba la integridad es la huella del paquete en la planilla de
            // custodia (o la firma del manifiesto).
            if (!string.IsNullOrEmpty(reportFile))
            {
                // .html o .htm: el informe como página imprimible; cualquier otra extensión, el texto de siempre.
                var content = _reportService.GenerateReportFor(reportFile, data, lang);
                await _reportService.SaveReportAsync(content, reportFile);
                Console.WriteLine($"Informe forense generado: {reportFile}");
            }

            // En Reservado lo que va a la planilla es la huella del sobre, que informa el sellado (y el acta).
            if (profile != ConfidentialityProfile.Reserved)
            {
                Console.WriteLine($"Huella SHA-256 del paquete (anótela en la planilla de cadena de custodia): {zipHash}");
            }
        }

        // Código 3: el paquete se generó, pero sin todo lo seleccionado. Un script que solo mire
        // "distinto de cero" no puede confundirlo con un éxito pleno.
        var completeExitCode = result.OmittedFiles.Count > 0 ? IncompleteExitCode : 0;

        if (profile == ConfidentialityProfile.Reserved)
        {
            var sealExitCode = await SealPackageAsync(parser, output, password!, actaPath!, generatedPassword);
            return sealExitCode != 0 ? sealExitCode : completeExitCode;
        }

        return completeExitCode;
    }

    /// <summary>Código de salida de un paquete generado con archivos no incluidos.</summary>
    public const int IncompleteExitCode = 3;

    /// <summary>
    /// Lista en consola los archivos que el antivirus no dejó leer, con ruta y código de Windows.
    /// </summary>
    private void PrintOmittedFiles(CompressionResult result)
    {
        if (result.OmittedFiles.Count == 0)
        {
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(string.Format(_localization.Get("cli_omitted_header"), result.OmittedFiles.Count));
        foreach (var omitted in result.OmittedFiles)
        {
            var reason = _localization.Get(omitted.Reason switch
            {
                OmissionReason.SecuritySoftwareBlocked => "reason_security_blocked",
                OmissionReason.MissingAtPackaging => "reason_missing_at_packaging",
                OmissionReason.LinkNotFollowed => "reason_link_not_followed",
                OmissionReason.Unreadable => "reason_unreadable",
                OmissionReason.FolderUnreadable => "reason_folder_unreadable",
                _ => "reason_vanished_during_packaging"
            });
            var code = omitted.WindowsErrorCode.HasValue ? $", Windows {omitted.WindowsErrorCode}" : string.Empty;
            Console.WriteLine($"  - {omitted.SourcePath} ({reason}{code})");
        }
        Console.WriteLine(_localization.Get("cli_omitted_footer"));
        Console.ResetColor();
    }

    /// <summary>
    /// Paso 2 del perfil Reservado: encierra el paquete recién generado —el ZIP, el manifiesto
    /// y su firma— en el sobre, emite el recibo y borra lo que quedó en claro. A la nube sube
    /// solo el <c>.fzs</c>.
    /// </summary>
    private async Task<int> SealPackageAsync(
        CommandParser parser,
        string output,
        string password,
        string actaPath,
        bool generatedPassword)
    {
        var artifacts = CollectPackageArtifacts(output);

        Console.WriteLine();
        Console.WriteLine($"Sellando el paquete ({artifacts.Count} archivo(s)) en perfil Reservado:");
        foreach (var artifact in artifacts)
        {
            Console.WriteLine($"  - {Path.GetFileName(artifact)}");
        }

        var options = new SealedPackageOptions
        {
            ArtifactPaths = artifacts,
            OutputDirectory = Path.GetDirectoryName(Path.GetFullPath(output)) ?? Directory.GetCurrentDirectory(),
            Password = password,
            SigningCertificatePath = parser.GetOption("--sign-cert", "--sign-cert"),
            SigningCertificatePassword = parser.GetOption("--sign-cert-password", "--sign-cert-password"),
            TimestampUrl = parser.GetOption("--timestamp-url", "--tsa")
        };

        SealedPackageResult sealedResult;
        try
        {
            sealedResult = await _sealedPackageBuilder.BuildAsync(
                options, SealedArtifacts.CreateProgress(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Sin sobre no hay acta, y la contraseña generada solo iba a quedar en el acta: el ZIP cifrado ya no
            // lo puede abrir nadie, y el manifiesto quedaba en claro con los datos de la causa. Se
            // descarta todo lo del paso 1; la evidencia original no se toca y se puede volver a empaquetar.
            Console.WriteLine();
            DiscardUnsealedPackage(artifacts);

            if (SecuritySoftwareBlockedException.Wrap(SecurityBlockStage.Sealing, options.OutputDirectory, ex) is { } blocked)
            {
                // Sube a Program, que lo informa con etapa, archivo y código igual que la GUI.
                throw blocked;
            }

            if (ex is not (IOException or SigningCertificateException))
            {
                throw;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 2;
        }

        Console.WriteLine();
        SealedArtifacts.ReportResult(sealedResult);

        var actData = new DeliveryActData
        {
            PackageId = sealedResult.Header.PackageId,
            SealedFileName = Path.GetFileName(sealedResult.SealedPath),
            SealedSha256 = sealedResult.Receipt.SealedSha256,
            SealedAtUtc = sealedResult.Header.SealedAtUtc,
            Password = password,
            CaseNumber = parser.GetOption("--case", "--case-number"),
            CaseDescription = parser.GetOption("--desc", "--description"),
            Court = parser.GetOption("--court", "--court"),
            Operator = BuildActOperator(parser)
        };

        var language = parser.GetOption("--lang", "--language") ?? _localization.CurrentLanguage;
        await _actService.SaveAsync(_actService.Generate(actData, language), actaPath);

        Console.WriteLine($"Acta de entrega generada: {actaPath}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        if (generatedPassword)
        {
            Console.WriteLine("La contraseña se generó y quedó escrita SOLO en el acta. No se muestra por consola");
            Console.WriteLine("ni se guarda en ningún otro lado: imprímala, entréguela en mano bajo firma y borre");
            Console.WriteLine("el archivo del acta.");
        }
        Console.ResetColor();

        // Los artefactos del paso 1 no deben quedar en disco fuera del sobre.
        SealedArtifacts.DeleteAll(artifacts);
        Console.WriteLine("Artefactos intermedios eliminados: fuera del sobre no queda nada en claro.");

        return 0;
    }

    /// <summary>
    /// Perfil Reservado sin sobre: borra el paquete intermedio y sus sidecar, y lo dice. Nada de eso es la
    /// evidencia original, que sigue intacta.
    /// </summary>
    private static void DiscardUnsealedPackage(List<string> artifacts)
    {
        SealedArtifacts.DeleteAll(artifacts);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("El sellado no se completó: se descartó el paquete intermedio (ZIP y manifiesto),");
        Console.WriteLine("que sin el acta nadie podría abrir. La evidencia original no se tocó: vuelva a empaquetar.");
        Console.ResetColor();
    }

    /// <summary>Reúne el ZIP (o sus volúmenes), el manifiesto y su firma.</summary>
    private static List<string> CollectPackageArtifacts(string output)
    {
        var artifacts = new List<string>();

        artifacts.AddRange(SplitArchive.ResolveSegments(Path.GetFullPath(output)).Where(File.Exists));

        var manifestPath = output + ".manifest.json";
        foreach (var candidate in new[] { manifestPath, manifestPath + ".p7s" })
        {
            if (File.Exists(candidate))
            {
                artifacts.Add(Path.GetFullPath(candidate));
            }
        }

        return artifacts;
    }

    private static OperatorInfo? BuildActOperator(CommandParser parser)
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

    /// <summary>
    /// Interpreta <c>--profile</c>. Se aceptan las dos formas, en español y en inglés, porque
    /// la interfaz habla en los dos idiomas y el operador escribe la que tiene a mano.
    /// </summary>
    private static ArchiveFormat ParseFormat(string value) => value.Trim().ToLowerInvariant() switch
    {
        "zip" => ArchiveFormat.Zip,
        "7z" or "7zip" or "7-zip" => ArchiveFormat.SevenZ,
        _ => throw new ArgumentException($"Formato desconocido: '{value}'. Use 'zip' o '7z'.")
    };

    private static ConfidentialityProfile ParseProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ConfidentialityProfile.Standard;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "reserved" or "reservado" => ConfidentialityProfile.Reserved,
            "standard" or "estandar" or "estándar" => ConfidentialityProfile.Standard,
            _ => throw new ArgumentException($"Perfil desconocido: '{value}'. Use 'standard' o 'reserved'.")
        };
    }

    /// <summary>
    /// Calcula el SHA-256 y el tamaño del ZIP lógico. Para un archivo dividido, hashea la
    /// concatenación de los volúmenes (que ya no existe como archivo único en disco).
    /// </summary>
    private async Task<(string hash, long size)> ComputeLogicalZipHashAsync(string output, CompressionResult result)
    {
        try
        {
            return await ComputeLogicalZipHashCoreAsync(output, result);
        }
        catch (Exception ex) when (SecuritySoftwareBlockedException.Wrap(
            SecurityBlockStage.RereadingPackage, output, ex) is { } blocked && blocked != ex)
        {
            // Releer el ZIP terminado hace que el antivirus lo revise por dentro; el paquete ya existe.
            throw blocked;
        }
    }

    private async Task<(string hash, long size)> ComputeLogicalZipHashCoreAsync(string output, CompressionResult result)
    {
        if (result.IsSplit)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(output)) ?? string.Empty;
            var segments = result.Volumes.Select(v => Path.Combine(dir, v.FileName)).ToList();
            var size = result.Volumes.Sum(v => v.Size);

            await using var logical = new ConcatenatedReadStream(segments);
            var hashes = await _hashService.ComputeHashesAsync(
                logical, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, CancellationToken.None);
            return (hashes[HashAlgorithmType.SHA256], size);
        }

        var single = await _hashService.ComputeHashesAsync(
            output, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, null, CancellationToken.None);
        return (single.Hashes[HashAlgorithmType.SHA256], new FileInfo(output).Length);
    }

    /// <summary>
    /// Parsea un tamaño con sufijo opcional: <c>700M</c>, <c>4096MB</c>, <c>1.5G</c>, <c>100K</c>,
    /// o un número de bytes a secas. Devuelve <c>null</c> si no se especificó.
    /// </summary>
    private static long? ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var m = Regex.Match(text.Trim(), @"^(\d+(?:[.,]\d+)?)\s*(k|kb|m|mb|g|gb|b)?$", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            throw new ArgumentException($"Tamaño de volumen inválido: '{text}'. Ejemplos: 700M, 4096MB, 1.5G, 100K.");
        }

        var value = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        long multiplier = m.Groups[2].Value.ToLowerInvariant() switch
        {
            "k" or "kb" => 1024L,
            "m" or "mb" => 1024L * 1024,
            "g" or "gb" => 1024L * 1024 * 1024,
            _ => 1L
        };

        return (long)(value * multiplier);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double n = bytes;
        int u = 0;
        while (n >= 1024 && u < units.Length - 1)
        {
            n /= 1024;
            u++;
        }
        return $"{n.ToString("0.##", CultureInfo.InvariantCulture)} {units[u]}";
    }
}
