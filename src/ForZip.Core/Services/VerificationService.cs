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
using System.Text.Json;
using System.Text.Json.Serialization;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;
using ICSharpCode.SharpZipLib.Zip;

namespace ForZip.Core.Services;

public class VerificationService : IVerificationService
{
    private const int BufferSize = 65536;

    // Orden de preferencia: el algoritmo más fuerte disponible se usa para verificar
    private static readonly HashAlgorithmType[] PreferredAlgorithms =
    {
        HashAlgorithmType.SHA512,
        HashAlgorithmType.SHA256,
        HashAlgorithmType.SHA1,
        HashAlgorithmType.MD5
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IHashService _hashService;
    private readonly ISignatureService _signatureService;

    public VerificationService(IHashService hashService, ISignatureService signatureService)
    {
        _hashService = hashService;
        _signatureService = signatureService;
    }

    public ForensicManifest ParseManifest(string json)
    {
        var manifest = JsonSerializer.Deserialize<ForensicManifest>(json, JsonOptions);
        return manifest ?? throw new InvalidOperationException("El manifiesto JSON está vacío o es inválido.");
    }

    public async Task<ArchiveVerificationResult> VerifyArchiveAsync(
        string manifestPath,
        string? zipPathOverride,
        string? password,
        IProgress<(long bytesProcessed, long totalBytes)>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Manifiesto no encontrado.", manifestPath);
        }

        var manifest = ParseManifest(await File.ReadAllTextAsync(manifestPath, ct));

        // Verificación de la firma digital del manifiesto (si existe un .p7s junto a él)
        var signature = _signatureService.IsSignaturePresent(manifestPath)
            ? _signatureService.Verify(manifestPath)
            : null;

        var zipPath = ResolveZipPath(manifestPath, zipPathOverride, manifest);
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Archivo ZIP no encontrado.", zipPath);
        }

        var result = new ArchiveVerificationResult { Signature = signature };

        // Índice de entradas esperadas según el manifiesto
        var expected = manifest.Files.ToDictionary(f => f.EntryName, StringComparer.Ordinal);
        var matched = new HashSet<string>(StringComparer.Ordinal);

        long totalBytes = manifest.Files.Sum(f => f.Size);
        long processed = 0;

        using (var zipFile = new ZipFile(zipPath))
        {
            if (!string.IsNullOrEmpty(password))
            {
                zipFile.Password = password;
            }

            foreach (ZipEntry entry in zipFile)
            {
                ct.ThrowIfCancellationRequested();
                if (!entry.IsFile)
                {
                    continue;
                }

                if (!expected.TryGetValue(entry.Name, out var manifestEntry))
                {
                    // Está en el ZIP pero no en el manifiesto: añadido a posteriori
                    result.Entries.Add(new FileVerificationEntry(entry.Name, FileVerificationStatus.Extra));
                    continue;
                }

                matched.Add(entry.Name);

                var algo = PickAlgorithm(manifestEntry.Hashes);
                if (algo == null)
                {
                    // El manifiesto no registró hashes para este archivo: solo se constata presencia
                    result.Entries.Add(new FileVerificationEntry(entry.Name, FileVerificationStatus.Ok));
                    processed += manifestEntry.Size;
                    progress?.Report((processed, totalBytes));
                    continue;
                }

                string actual;
                using (var entryStream = zipFile.GetInputStream(entry))
                {
                    var hashes = await _hashService.ComputeHashesAsync(
                        entryStream,
                        new HashSet<HashAlgorithmType> { algo.Value },
                        ct);
                    actual = hashes[algo.Value];
                }

                var expectedHash = manifestEntry.Hashes[algo.Value];
                var status = string.Equals(expectedHash, actual, StringComparison.OrdinalIgnoreCase)
                    ? FileVerificationStatus.Ok
                    : FileVerificationStatus.Altered;

                result.Entries.Add(new FileVerificationEntry(entry.Name, status, expectedHash, actual));

                processed += manifestEntry.Size;
                progress?.Report((processed, totalBytes));
            }
        }

        // Entradas del manifiesto que no aparecieron en el ZIP
        foreach (var f in manifest.Files)
        {
            if (!matched.Contains(f.EntryName))
            {
                result.Entries.Add(new FileVerificationEntry(f.EntryName, FileVerificationStatus.Missing));
            }
        }

        // Verificación del hash global del ZIP, si el manifiesto lo registró
        if (!string.IsNullOrEmpty(manifest.ZipSha256))
        {
            await using var fs = new FileStream(
                zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            var zipHashes = await _hashService.ComputeHashesAsync(
                fs, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, ct);
            result.ZipHashMatches = string.Equals(
                zipHashes[HashAlgorithmType.SHA256], manifest.ZipSha256, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string ResolveZipPath(string manifestPath, string? zipPathOverride, ForensicManifest manifest)
    {
        if (!string.IsNullOrEmpty(zipPathOverride))
        {
            return zipPathOverride;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? string.Empty;

        if (!string.IsNullOrEmpty(manifest.ZipFileName))
        {
            return Path.Combine(dir, manifest.ZipFileName);
        }

        // Convención: <zip>.manifest.json → quitar el sufijo para obtener el ZIP
        var name = Path.GetFileName(manifestPath);
        const string suffix = ".manifest.json";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(dir, name[..^suffix.Length]);
        }

        throw new InvalidOperationException(
            "No se pudo determinar la ruta del ZIP a partir del manifiesto. Especifíquela explícitamente.");
    }

    private static HashAlgorithmType? PickAlgorithm(Dictionary<HashAlgorithmType, string> hashes)
    {
        foreach (var algo in PreferredAlgorithms)
        {
            if (hashes.ContainsKey(algo) && !string.IsNullOrEmpty(hashes[algo]))
            {
                return algo;
            }
        }
        return null;
    }
}
