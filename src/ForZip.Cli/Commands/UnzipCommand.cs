// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Cli.Commands;

public class UnzipCommand
{
    private readonly IZipService _zipService;
    private readonly ILocalizationService _localization;

    public UnzipCommand(IZipService zipService, ILocalizationService localization)
    {
        _zipService = zipService;
        _localization = localization;
    }

    public async Task<int> ExecuteAsync(CommandParser parser)
    {
        var input = parser.GetOption("-i", "--input");
        var output = parser.GetOption("-o", "--output");
        var password = parser.GetOption("-p", "--password");

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
        {
            Console.WriteLine("Uso: forzip unzip -i <archivo> -o <outputDir> [-p <password>]");
            Console.WriteLine("     ZIP, 7z, RAR, TAR, TAR.GZ/BZ2/XZ, GZ, BZ2, XZ o ISO (se reconoce por la firma).");
            return 1;
        }

        Console.WriteLine($"Extrayendo: {input} -> {output}");

        var progress = new Progress<(long processed, long total)>(p =>
        {
            if (p.total > 0)
            {
                var percent = (int)(100.0 * p.processed / p.total);
                Console.Write($"\rProgreso: [{new string('#', percent / 5)}{new string('-', 20 - percent / 5)}] {percent}% ");
            }
        });

        var result = await _zipService.DecompressAsync(input, output, password, progress, CancellationToken.None);

        if (result.IsComplete)
        {
            Console.WriteLine($"\n¡Extracción completada exitosamente! Formato: {result.Format}. Archivos: {result.FilesExtracted}.");
        }
        else
        {
            // Lo extraído está verificado; lo dañado no quedó en el destino. El código de salida lo distingue de un
            // éxito pleno, como el de un paquete incompleto al empaquetar.
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nEXTRACCIÓN INCOMPLETA. Formato: {result.Format}. Extraídos y verificados: {result.FilesExtracted}. " +
                              $"Dañados, NO extraídos: {result.Damaged.Count}.");
            foreach (var d in result.Damaged)
            {
                Console.WriteLine($"  DAÑADO: '{d.Name}'" + (d.ExpectedSize is { } size ? $" ({size:N0} bytes)" : "") + $" — {d.Reason}");
            }

            Console.ResetColor();
        }
        if (result.Renamed.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Renombrados ({result.Renamed.Count}): se guardaron con otro nombre; el motivo va en cada uno.");
            foreach (var r in result.Renamed)
            {
                Console.WriteLine($"  RENOMBRADO: '{r.OriginalName}' -> '{r.SavedAs}' ({ReasonText(r.Reason)})");
            }

            Console.ResetColor();
        }

        if (result.LongPaths.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Rutas largas ({result.LongPaths.Count}): pasan de {ExtractionResult.ExplorerMaxPath} caracteres. ForZip los extrajo bien, " +
                              "pero el Explorador de Windows los muestra y no los abre con doble clic (sin avisar), y muchos programas " +
                              "tampoco. Arrastrarlos al programa sí funciona; o vuelva a extraer en una carpeta de nombre corto, " +
                              "cerca de la raíz del disco, si las carpetas de adentro del archivo lo permiten.");
            foreach (var p in result.LongPaths)
            {
                Console.WriteLine($"  RUTA LARGA: '{p}'");
            }

            Console.ResetColor();
        }

        if (result.Skipped.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No creados ({result.Skipped.Count}): enlaces, dispositivos y flujos NTFS no se materializan, quedan documentados.");
            foreach (var s in result.Skipped)
            {
                Console.WriteLine($"  OMITIDO ({s.Kind}): '{s.Name}'" + (s.Size is { } size ? $" ({size:N0} bytes)" : "") +
                    (s.LinkTarget != null ? $" -> '{s.LinkTarget}'" : ""));
            }

            Console.ResetColor();
        }

        PrintComment("Comentario del archivo", result.ArchiveComment);
        foreach (var c in result.EntryComments)
        {
            PrintComment($"Comentario de '{c.Name}'", c.Comment);
        }

        return result.IsComplete ? 0 : ZipCommand.IncompleteExitCode;
    }

    /// <summary>
    /// Muestra un comentario. Los caracteres de control (salvo saltos de línea y tabulaciones) se ven como '?':
    /// un comentario puede traer secuencias de escape que alteren la terminal.
    /// </summary>
    private static string ReasonText(RenameReason reason) => reason switch
    {
        RenameReason.TooLong => "nombre de más de 255 caracteres",
        RenameReason.Repeated => "repetido dentro del archivo",
        RenameReason.AlreadyInDestination => "ya existía en el destino; no se pisa",
        RenameReason.FolderIsFile => "una carpeta del camino ya existía como archivo",
        _ => "nombre peligroso o inválido en Windows",
    };

    private static void PrintComment(string title, CommentInfo? comment)
    {
        if (comment == null)
        {
            return;
        }

        if (comment.Text == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{title}: no se pudo leer ({comment.Problem}).");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"{title}:");
        Console.WriteLine(CommentText.ForDisplay(comment.Text));
    }
}
