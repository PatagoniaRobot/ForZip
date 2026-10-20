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
using ForZip.Core.Services;

namespace ForZip.Core.Sealing;

/// <summary>
/// Arma el contenedor interno con los artefactos del paquete, lo sella y emite el recibo.
/// </summary>
/// <remarks>
/// El contenedor interno hay que materializarlo: el tamaño de la carga útil se declara en el
/// header del sobre y por lo tanto debe conocerse antes de empezar a cifrar. Por eso se
/// construye en un temporal y por eso se verifica el espacio disponible <b>antes</b> de
/// empezar, en lugar de fallar a mitad de un paquete de decenas de GB.
/// </remarks>
public class SealedPackageBuilder : ISealedPackageBuilder
{
    // Margen sobre el tamaño estimado: el contenedor interno agrega la estructura del ZIP y
    // el sobre agrega header y tags. Un 5 % cubre de sobra ambas cosas.
    private const double SpaceMargin = 1.05;

    private const string SigningSuffix = ".p7s";
    private const string WorkingPrefix = ".forzip-sellando-";
    private const string WorkingSuffix = ".tmp";
    private const string InnerFileName = "inner.zip";

    private readonly ISealingService _sealingService;
    private readonly IReceiptService _receiptService;

    public SealedPackageBuilder(ISealingService sealingService, IReceiptService receiptService)
    {
        _sealingService = sealingService;
        _receiptService = receiptService;
    }

    /// <inheritdoc />
    public async Task<SealedPackageResult> BuildAsync(
        SealedPackageOptions options,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ArtifactPaths.Count == 0)
        {
            throw new ArgumentException("No hay artefactos que sellar.", nameof(options));
        }

        if (string.IsNullOrEmpty(options.Password))
        {
            throw new ArgumentException("El sobre necesita una contraseña.", nameof(options));
        }

        foreach (var path in options.ArtifactPaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("No se encontró un artefacto del paquete.", path);
            }
        }

        // El certificado se prueba antes de cifrar nada: una contraseña de PFX mal tipeada se descubría al firmar
        // el recibo, con el sobre ya hecho, y se perdía todo el trabajo.
        if (!string.IsNullOrEmpty(options.SigningCertificatePath))
        {
            SignatureService.EnsureUsableCertificate(options.SigningCertificatePath, options.SigningCertificatePassword);
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var payloadEstimate = options.ArtifactPaths.Sum(p => new FileInfo(p).Length);
        var required = (long)(payloadEstimate * SpaceMargin);

        // El contenedor temporal y el sobre conviven hasta el final: si están en el mismo disco, ese disco
        // tiene que alojar los dos.
        if (SameVolume(Path.GetTempPath(), options.OutputDirectory))
        {
            EnsureSpace(options.OutputDirectory, required * 2, "el contenedor temporal y el sobre sellado");
        }
        else
        {
            EnsureSpace(Path.GetTempPath(), required, "el contenedor temporal");
            EnsureSpace(options.OutputDirectory, required, "el sobre sellado");
        }

        var result = new SealedPackageResult();
        var workspace = TempWorkspace.Create();
        var workingPath = Path.Combine(
            options.OutputDirectory, WorkingPrefix + Guid.NewGuid().ToString("N") + WorkingSuffix);
        string? sealedPath = null;
        string? receiptPath = null;

        try
        {
            var innerPath = workspace.Combine(InnerFileName);
            await using (var inner = new FileStream(innerPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await SealedPayloadContainer.CreateAsync(options.ArtifactPaths, inner, ct).ConfigureAwait(false);
            }

            // Se sella con un nombre provisional en la carpeta de destino y recién después se
            // renombra: el nombre definitivo sale del identificador aleatorio, que nace acá
            // adentro, y renombrar dentro del mismo volumen no copia nada.
            // El avance cubre las dos pasadas, sellar y abrir de prueba, como una sola: cada una es la mitad. Antes
            // la barra llegaba al 100 % y volvía a empezar.
            var header = await _sealingService
                .SealFileAsync(innerPath, workingPath, options.Password, HalfProgress(progress, second: false), ct)
                .ConfigureAwait(false);

            sealedPath = Path.Combine(
                options.OutputDirectory, header.BaseName + SealedContainerFormat.SealedExtension);

            File.Move(workingPath, sealedPath, overwrite: false);

            // Antes de entregarlo —y de que quien llama borre los archivos en claro, que después son la única
            // copia—, el sobre recién escrito se abre de prueba con la misma contraseña: cada bloque tiene que
            // autenticarse leyéndolo del archivo, ya forzado al disco al terminar de sellar (el sistema puede
            // servir la lectura desde su caché; lo que sí se descarta es un error en el camino de escritura).
            // Un fallo de escritura deja un sobre que nadie podría abrir.
            await VerifyWrittenEnvelopeAsync(sealedPath, options.Password, HalfProgress(progress, second: true), ct)
                .ConfigureAwait(false);

            result.Header = header;
            result.SealedPath = sealedPath;

            var receipt = await _receiptService.CreateAsync(sealedPath, header, null, ct).ConfigureAwait(false);
            result.Receipt = receipt;
            receiptPath = _receiptService.GetReceiptPathFor(sealedPath);
            result.ReceiptPath = await _receiptService
                .WriteAsync(receipt, receiptPath, ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(options.SigningCertificatePath))
            {
                await SignReceiptAsync(result, options, ct).ConfigureAwait(false);
            }

            // La página para quien recibe va junto al sobre y al recibo. Si no se puede escribir
            // no se deshace un sobre ya sellado: se abre igual con ForZip, y la ruta nula avisa.
            try
            {
                result.OpenerPagePath = await EnvelopeOpenerPage
                    .WriteToAsync(options.OutputDirectory, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.OpenerPagePath = null;
            }

            return result;
        }
        catch
        {
            // Nunca dejar un sobre a medio escribir con el nombre provisional, ni uno que no se completó o no
            // pasó la prueba con el nombre definitivo: los archivos en claro siguen ahí.
            TryDelete(workingPath);
            if (sealedPath != null)
            {
                TryDelete(sealedPath);
            }

            // Ni un recibo (o su firma a medias) que nombre a un sobre que ya no existe.
            if (receiptPath != null)
            {
                TryDelete(receiptPath);
                TryDelete(receiptPath + SigningSuffix);
            }

            throw;
        }
        finally
        {
            workspace.Dispose();
            result.UndeletedTempPath = workspace.UndeletedPath;
        }
    }

    private async Task VerifyWrittenEnvelopeAsync(
        string sealedPath, string password, IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct)
    {
        try
        {
            await using var written = new FileStream(sealedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await _sealingService.UnsealAsync(written, Stream.Null, password, progress, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SealedFormatException or SealedPasswordException or SealedIntegrityException)
        {
            throw new IOException(
                "El sobre recién escrito no pasó la prueba de apertura (" + ex.Message + "). No se entrega: " +
                "los archivos del paquete siguen en claro y sin cambios.", ex);
        }
    }

    /// <summary>Lleva el avance de una de las dos pasadas a su mitad de la escala total.</summary>
    private static IProgress<(long bytesProcessed, long totalBytes)>? HalfProgress(
        IProgress<(long bytesProcessed, long totalBytes)>? progress, bool second)
    {
        if (progress == null)
        {
            return null;
        }

        return new SynchronousProgress(p =>
            progress.Report(((second ? p.totalBytes : 0) + p.bytesProcessed, p.totalBytes * 2)));
    }

    /// <summary>Reenvía en el acto, sin pasar por un contexto de sincronización (el que llama ya tiene el suyo).</summary>
    private sealed class SynchronousProgress(Action<(long bytesProcessed, long totalBytes)> report)
        : IProgress<(long bytesProcessed, long totalBytes)>
    {
        public void Report((long bytesProcessed, long totalBytes) value) => report(value);
    }

    private static bool SameVolume(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Firma el recibo. Que la TSA no responda es una advertencia, no un error: la firma CMS
    /// igual quedó escrita, y así se comporta la firma del manifiesto desde 1.2.0.
    /// </summary>
    private async Task SignReceiptAsync(SealedPackageResult result, SealedPackageOptions options, CancellationToken ct)
    {
        try
        {
            await _receiptService.SignAsync(
                result.ReceiptPath,
                options.SigningCertificatePath!,
                options.SigningCertificatePassword,
                options.TimestampUrl,
                ct).ConfigureAwait(false);
        }
        catch (TimestampUnavailableException ex)
        {
            result.TimestampWarning = ex.Message;
        }

        result.ReceiptSignaturePath = result.ReceiptPath + SigningSuffix;
    }

    /// <summary>
    /// Comprueba el espacio libre antes de empezar. Si el volumen no se puede consultar
    /// —una ruta UNC, por ejemplo— no se bloquea la operación: se prefiere intentarlo a
    /// negarse por no poder medir.
    /// </summary>
    private static void EnsureSpace(string path, long required, string what)
    {
        long available;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            available = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (available < required)
        {
            throw new IOException(
                $"No hay espacio suficiente para {what} en '{path}': " +
                $"hacen falta {FormatSize(required)} y hay {FormatSize(available)} disponibles.");
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
