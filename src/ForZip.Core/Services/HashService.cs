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

using System.Security.Cryptography;
using System.Text;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

public class HashService : IHashService
{
    // 64 KB ofrece buen equilibrio entre rendimiento de I/O y uso de memoria
    private const int BufferSize = 65536;

    public async Task<HashResult> ComputeHashesAsync(
        string filePath,
        HashSet<HashAlgorithmType> algorithms,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (algorithms == null || algorithms.Count == 0)
        {
            throw new ArgumentException("Debe seleccionar al menos un algoritmo.", nameof(algorithms));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Archivo no encontrado.", filePath);
        }

        ct.ThrowIfCancellationRequested();

        var fileInfo = new FileInfo(filePath);
        var totalSize = fileInfo.Length;

        await using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        var hashes = await ComputeHashesCoreAsync(fs, algorithms, totalSize, progress, ct).ConfigureAwait(false);

        // Ruta completa y fecha de modificación, tomadas antes de leer: el informe identifica el archivo por su
        // ubicación y no solo por el nombre (dos homónimos en carpetas distintas eran indistinguibles).
        return new HashResult
        {
            FilePath = filePath,
            SourcePath = fileInfo.FullName,
            ModifiedUtc = fileInfo.LastWriteTimeUtc,
            FileSize = totalSize,
            Hashes = hashes
        };
    }

    public Task<Dictionary<HashAlgorithmType, string>> ComputeHashesAsync(
        Stream stream,
        HashSet<HashAlgorithmType> algorithms,
        CancellationToken ct)
    {
        if (algorithms == null || algorithms.Count == 0)
        {
            throw new ArgumentException("Debe seleccionar al menos un algoritmo.", nameof(algorithms));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        // Sin tamaño conocido no podemos reportar progreso fraccional
        return ComputeHashesCoreAsync(stream, algorithms, totalSize: -1, progress: null, ct);
    }

    /// <inheritdoc />
    public Task<Dictionary<HashAlgorithmType, string>> ComputeHashesAsync(
        Stream stream,
        HashSet<HashAlgorithmType> algorithms,
        long totalSize,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (algorithms == null || algorithms.Count == 0)
        {
            throw new ArgumentException("Debe seleccionar al menos un algoritmo.", nameof(algorithms));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        return ComputeHashesCoreAsync(stream, algorithms, totalSize, progress, ct);
    }

    private static async Task<Dictionary<HashAlgorithmType, string>> ComputeHashesCoreAsync(
        Stream stream,
        HashSet<HashAlgorithmType> algorithms,
        long totalSize,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        // Una instancia de algoritmo por cada elemento solicitado
        var hashers = new Dictionary<HashAlgorithmType, HashAlgorithm>();
        try
        {
            foreach (var algo in algorithms)
            {
                hashers[algo] = CreateHashAlgorithm(algo);
            }

            var buffer = new byte[BufferSize];
            long totalRead = 0;

            // Con archivos grandes el bucle da miles de vueltas por segundo. Reportar en cada
            // una inundaría el hilo de UI, así que solo se avisa cuando cambia el por mil.
            var lastPermille = -1;

            int bytesRead;
            // ConfigureAwait(false) es lo que mantiene el cálculo fuera del hilo de UI: sin él,
            // cada continuación vuelve al Dispatcher y el TransformBlock se come el hilo que
            // debería estar pintando la ventana (síntoma: ventana en blanco hasta terminar).
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();

                // Alimentar el mismo bloque a todos los algoritmos para evitar relecturas
                foreach (var hasher in hashers.Values)
                {
                    hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                totalRead += bytesRead;
                if (totalSize > 0 && progress != null)
                {
                    var permille = (int)(totalRead * 1000 / totalSize);
                    if (permille != lastPermille)
                    {
                        lastPermille = permille;
                        progress.Report((double)totalRead / totalSize);
                    }
                }
            }

            // TransformFinalBlock necesario aunque el stream esté vacío
            foreach (var hasher in hashers.Values)
            {
                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }

            var hashes = new Dictionary<HashAlgorithmType, string>();
            foreach (var (algo, hasher) in hashers)
            {
                hashes[algo] = ToLowerHex(hasher.Hash!);
            }

            // Cierre explícito del progreso: el último bloque leído rara vez cae justo en el
            // 100 %, y un archivo vacío no entra siquiera al bucle.
            progress?.Report(1.0);

            return hashes;
        }
        finally
        {
            foreach (var hasher in hashers.Values)
            {
                hasher.Dispose();
            }
        }
    }

    public async Task<List<HashResult>> ComputeHashesBatchAsync(
        IReadOnlyList<string> filePaths,
        HashSet<HashAlgorithmType> algorithms,
        int maxDegreeOfParallelism,
        CancellationToken ct)
    {
        if (filePaths == null)
        {
            throw new ArgumentNullException(nameof(filePaths));
        }

        var results = new HashResult[filePaths.Count];
        var options = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = maxDegreeOfParallelism <= 0
                ? Environment.ProcessorCount
                : maxDegreeOfParallelism
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, filePaths.Count), options, async (i, token) =>
        {
            results[i] = await ComputeHashesAsync(filePaths[i], algorithms, null, token);
        });

        return results.ToList();
    }

    public string ComputeSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return ToLowerHex(hash);
    }

    private static HashAlgorithm CreateHashAlgorithm(HashAlgorithmType type)
    {
        return type switch
        {
            HashAlgorithmType.MD5 => MD5.Create(),
            HashAlgorithmType.SHA1 => SHA1.Create(),
            HashAlgorithmType.SHA256 => SHA256.Create(),
            HashAlgorithmType.SHA512 => SHA512.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Algoritmo no soportado.")
        };
    }

    private static string ToLowerHex(byte[] hash)
    {
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
