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

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class Sha256SidecarTests
{
    private const string Hash = "31f4fc1b423f162e66cf8ede190cb80e11e2b2fc4c4eadc6b0bef44064f12c9c";

    [Theory]
    [InlineData(Hash + "  ForZip.exe")]
    [InlineData(Hash + " *ForZip.exe")]
    [InlineData(Hash)]
    [InlineData(Hash + "  ForZip.exe\n")]
    [InlineData("\r\n" + Hash + "\tForZip.exe\r\n")]
    public void Extract_AcceptedForms_ReturnsTheHash(string content)
    {
        Assert.Equal(Hash, Sha256Sidecar.Extract(content));
    }

    [Fact]
    public void Extract_UppercaseHash_IsNormalisedToLowercase()
    {
        Assert.Equal(Hash, Sha256Sidecar.Extract(Hash.ToUpperInvariant() + "  ForZip.exe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no hay ningun hash aca")]
    [InlineData("abc123")]
    public void Extract_WithoutAValidHash_ReturnsNull(string? content)
    {
        Assert.Null(Sha256Sidecar.Extract(content));
    }

    [Fact]
    public void Extract_TooShortHexToken_IsRejected()
    {
        // 63 caracteres: un hash truncado no debe pasar por válido.
        Assert.Null(Sha256Sidecar.Extract(Hash[..63] + "  ForZip.exe"));
    }
}

public class UpdateAssetSelectionTests
{
    private static UpdateCheckResult ResultWith(params string[] assetNames) => new()
    {
        Status = UpdateCheckStatus.UpdateAvailable,
        CurrentVersion = "1.3.0",
        LatestVersion = "1.4.0",
        Assets = assetNames
            .Select(n => new UpdateAsset { Name = n, DownloadUrl = $"https://example.invalid/{n}", Size = 10 })
            .ToList()
    };

    [Fact]
    public void FindAsset_MatchesTheRunningExecutableByName()
    {
        var result = ResultWith("ForZip.exe", "ForZip.Cli.exe");

        Assert.Equal("ForZip.exe", result.FindAsset("ForZip.exe")!.Name);
        Assert.Equal("ForZip.Cli.exe", result.FindAsset("ForZip.Cli.exe")!.Name);
    }

    [Fact]
    public void FindAsset_IsCaseInsensitive()
    {
        Assert.NotNull(ResultWith("ForZip.exe").FindAsset("forzip.EXE"));
    }

    [Fact]
    public void FindAsset_RenamedAssets_ReturnsNullInsteadOfGuessing()
    {
        // La v1.1.0 publicaba "ForZip.GUI.exe". Un ejecutable llamado ForZip.exe no debe
        // aceptar ese binario solo porque sea el unico adjunto.
        Assert.Null(ResultWith("ForZip.GUI.exe").FindAsset("ForZip.exe"));
    }

    [Fact]
    public void FindChecksumFor_ReturnsTheSidecarAsset()
    {
        var result = ResultWith("ForZip.exe", "ForZip.exe.sha256");

        Assert.Equal("ForZip.exe.sha256", result.FindChecksumFor("ForZip.exe")!.Name);
    }

    [Fact]
    public void FindChecksumFor_WithoutSidecar_ReturnsNull()
    {
        Assert.Null(ResultWith("ForZip.exe").FindChecksumFor("ForZip.exe"));
    }

    [Fact]
    public void ParseLatestRelease_ReadsAssetsFromTheRelease()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tag_name"] = "v99.0.0",
            ["assets"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "ForZip.exe",
                    ["browser_download_url"] = "https://example.invalid/ForZip.exe",
                    ["size"] = 96089353L
                },
                new Dictionary<string, object?>
                {
                    ["name"] = "ForZip.exe.sha256",
                    ["browser_download_url"] = "https://example.invalid/ForZip.exe.sha256",
                    ["size"] = 76L
                },
                // Entrada incompleta: debe saltearse sin romper el resto.
                new Dictionary<string, object?> { ["name"] = "roto.exe" }
            }
        });

        var result = UpdateService.ParseLatestRelease(json);

        Assert.Equal(2, result.Assets.Count);
        Assert.Equal(96089353L, result.FindAsset("ForZip.exe")!.Size);
        Assert.NotNull(result.FindChecksumFor("ForZip.exe"));
    }
}

public class UpdateDownloadAndVerifyTests : IDisposable
{
    private readonly string _tempDir;

    public UpdateDownloadAndVerifyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "forzip_update_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* limpieza best-effort */ }
    }

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("contenido del ejecutable falso");

    private static string PayloadHash =>
        Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes;

        public RouteHandler(Dictionary<string, Func<HttpResponseMessage>> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            return Task.FromResult(_routes.TryGetValue(url, out var make)
                ? make()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string BinaryUrl = "https://example.invalid/ForZip.exe";
    private const string SidecarUrl = "https://example.invalid/ForZip.exe.sha256";

    private static readonly UpdateAsset BinaryAsset =
        new() { Name = "ForZip.exe", DownloadUrl = BinaryUrl, Size = 30 };

    private static readonly UpdateAsset SidecarAsset =
        new() { Name = "ForZip.exe.sha256", DownloadUrl = SidecarUrl, Size = 76 };

    private static UpdateService ServiceWith(string sidecarBody, byte[]? binary = null)
    {
        var handler = new RouteHandler(new Dictionary<string, Func<HttpResponseMessage>>
        {
            [SidecarUrl] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sidecarBody)
            },
            [BinaryUrl] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(binary ?? Payload)
            }
        });

        return new UpdateService(new HttpClient(handler));
    }

    private string Destination => Path.Combine(_tempDir, "ForZip.exe");

    [Fact]
    public async Task DownloadAndVerify_MatchingChecksum_IsVerifiedAndKeepsTheFile()
    {
        var service = ServiceWith($"{PayloadHash}  ForZip.exe");

        var result = await service.DownloadAndVerifyAsync(
            BinaryAsset, SidecarAsset, Destination, null, CancellationToken.None);

        Assert.True(result.IsVerified);
        Assert.Equal(UpdateDownloadStatus.Verified, result.Status);
        Assert.Equal(PayloadHash, result.ActualSha256);
        Assert.True(File.Exists(Destination));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task DownloadAndVerify_TamperedBinary_IsRejectedAndTheFileIsDeleted()
    {
        // El sidecar anuncia el hash del contenido legítimo, pero el servidor entrega otra cosa:
        // es exactamente el escenario de una descarga manipulada.
        var service = ServiceWith($"{PayloadHash}  ForZip.exe", Encoding.UTF8.GetBytes("binario hostil"));

        var result = await service.DownloadAndVerifyAsync(
            BinaryAsset, SidecarAsset, Destination, null, CancellationToken.None);

        Assert.False(result.IsVerified);
        Assert.Equal(UpdateDownloadStatus.ChecksumMismatch, result.Status);
        Assert.Equal(PayloadHash, result.ExpectedSha256);
        Assert.NotEqual(result.ExpectedSha256, result.ActualSha256);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task DownloadAndVerify_NoChecksumAsset_RefusesWithoutDownloading()
    {
        var service = ServiceWith($"{PayloadHash}  ForZip.exe");

        var result = await service.DownloadAndVerifyAsync(
            BinaryAsset, null, Destination, null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.ChecksumMissing, result.Status);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task DownloadAndVerify_UnreadableSidecar_RefusesToInstall()
    {
        var service = ServiceWith("esto no contiene ningun hash");

        var result = await service.DownloadAndVerifyAsync(
            BinaryAsset, SidecarAsset, Destination, null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.ChecksumMissing, result.Status);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task DownloadAndVerify_BinaryUnreachable_ReportsFailureAndLeavesNothingBehind()
    {
        var handler = new RouteHandler(new Dictionary<string, Func<HttpResponseMessage>>
        {
            [SidecarUrl] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{PayloadHash}  ForZip.exe")
            }
            // El binario no está enrutado: responde 404.
        });
        var service = new UpdateService(new HttpClient(handler));

        var result = await service.DownloadAndVerifyAsync(
            BinaryAsset, SidecarAsset, Destination, null, CancellationToken.None);

        Assert.Equal(UpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task DownloadAndVerify_ReportsProgress()
    {
        var service = ServiceWith($"{PayloadHash}  ForZip.exe");
        var reported = new List<double>();

        await service.DownloadAndVerifyAsync(
            BinaryAsset, SidecarAsset, Destination,
            new Progress<double>(p => { lock (reported) { reported.Add(p); } }),
            CancellationToken.None);

        // Progress<T> despacha de forma asincrónica; damos margen para que lleguen los avisos.
        for (int i = 0; i < 20 && reported.Count == 0; i++)
        {
            await Task.Delay(25);
        }

        lock (reported)
        {
            Assert.NotEmpty(reported);
            Assert.All(reported, p => Assert.InRange(p, 0, 100));
        }
    }
}
