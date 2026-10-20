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

namespace ForZip.Core.Sealing;

/// <summary>
/// Emite y lee el recibo público del sobre sellado. La firma se delega en
/// <see cref="ISignatureService"/> y el hash en <see cref="IHashService"/>: el recibo no
/// duplica criptografía que ya está implementada y probada.
/// </summary>
public class ReceiptService : IReceiptService
{
    /// <summary>Sufijo del recibo público.</summary>
    public const string ReceiptSuffix = ".receipt.json";

    private readonly IHashService _hashService;
    private readonly ISignatureService _signatureService;

    public ReceiptService(IHashService hashService, ISignatureService signatureService)
    {
        _hashService = hashService;
        _signatureService = signatureService;
    }

    /// <inheritdoc />
    public async Task<Receipt> CreateAsync(
        string sealedPath,
        SealedHeader header,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(header);

        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
        var hash = await _hashService.ComputeHashesAsync(sealedPath, algorithms, progress, ct).ConfigureAwait(false);

        return Receipt.Create(
            header,
            Path.GetFileName(sealedPath),
            hash.Hashes[HashAlgorithmType.SHA256],
            new FileInfo(sealedPath).Length);
    }

    /// <inheritdoc />
    public async Task<string> WriteAsync(Receipt receipt, string receiptPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        await File.WriteAllTextAsync(receiptPath, receipt.ToJson(), SealedContainerFormat.HeaderEncoding, ct)
            .ConfigureAwait(false);
        return receiptPath;
    }

    /// <inheritdoc />
    public async Task<Receipt> ReadAsync(string receiptPath, CancellationToken ct)
    {
        if (!File.Exists(receiptPath))
        {
            throw new FileNotFoundException("No se encontró el recibo del sobre.", receiptPath);
        }

        var json = await File.ReadAllTextAsync(receiptPath, ct).ConfigureAwait(false);
        return Receipt.Parse(json);
    }

    /// <inheritdoc />
    public Task SignAsync(string receiptPath, string pfxPath, string? pfxPassword, string? tsaUrl, CancellationToken ct) =>
        _signatureService.SignAsync(receiptPath, pfxPath, pfxPassword, tsaUrl, ct);

    /// <inheritdoc />
    public string GetReceiptPathFor(string sealedPath)
    {
        var directory = Path.GetDirectoryName(sealedPath) ?? string.Empty;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(sealedPath) + ReceiptSuffix);
    }

    /// <inheritdoc />
    public string GetSealedPathFor(string receiptPath)
    {
        var fileName = Path.GetFileName(receiptPath);
        if (!fileName.EndsWith(ReceiptSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La ruta indicada no es un recibo de ForZip.", nameof(receiptPath));
        }

        var directory = Path.GetDirectoryName(receiptPath) ?? string.Empty;
        var baseName = fileName[..^ReceiptSuffix.Length];
        return Path.Combine(directory, baseName + SealedContainerFormat.SealedExtension);
    }
}
