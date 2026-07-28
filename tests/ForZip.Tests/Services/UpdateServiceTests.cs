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
using System.Text.Json;
using ForZip.Core;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class VersionComparerTests
{
    [Theory]
    [InlineData("1.3.0", 1, 3, 0)]
    [InlineData("v1.3.0", 1, 3, 0)]
    [InlineData("V1.3.0", 1, 3, 0)]
    [InlineData("  v1.3.0  ", 1, 3, 0)]
    [InlineData("1.3", 1, 3, 0)]
    [InlineData("v1.4.0-beta.1", 1, 4, 0)]
    [InlineData("v1.4.0+build7", 1, 4, 0)]
    public void TryParse_AcceptedForms_ParseToExpectedNumbers(string text, int major, int minor, int build)
    {
        Assert.True(VersionComparer.TryParse(text, out var version));

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.-2.0")]
    [InlineData("1.two.0")]
    public void TryParse_Garbage_ReturnsFalse(string? text)
    {
        Assert.False(VersionComparer.TryParse(text, out _));
    }

    [Fact]
    public void IsNewer_ComparesNumericallyNotAsText()
    {
        // El caso que rompe la comparación textual: "1.10.0" < "1.9.0" alfabéticamente.
        Assert.True(VersionComparer.IsNewer("v1.10.0", "1.9.0"));
        Assert.False(VersionComparer.IsNewer("v1.9.0", "1.10.0"));
    }

    [Theory]
    [InlineData("v1.4.0", "1.3.0", true)]
    [InlineData("v1.3.1", "1.3.0", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("v1.3.0", "1.3.0", false)]
    [InlineData("v1.2.0", "1.3.0", false)]
    public void IsNewer_KnownPairs_ReturnExpected(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, VersionComparer.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("no-es-una-version", "1.3.0")]
    [InlineData("v1.4.0", "tampoco")]
    [InlineData(null, "1.3.0")]
    public void IsNewer_UnparseableInput_DoesNotAnnounceAnUpdate(string? candidate, string? current)
    {
        // Ante la duda no se anuncia nada: es peor ofrecer una actualización inexistente.
        Assert.False(VersionComparer.IsNewer(candidate, current));
    }
}

public class UpdateServiceParsingTests
{
    private static string ReleaseJson(string tag, string? body = "Notas.", string? url = "https://example.invalid/r")
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tag_name"] = tag,
            ["body"] = body,
            ["html_url"] = url
        });
    }

    [Fact]
    public void ParseLatestRelease_NewerTag_ReportsUpdateAvailable()
    {
        var json = ReleaseJson("v99.0.0", "Lo nuevo de la 99.", "https://example.invalid/99");

        var result = UpdateService.ParseLatestRelease(json);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("99.0.0", result.LatestVersion);
        Assert.Equal("Lo nuevo de la 99.", result.ReleaseNotes);
        Assert.Equal("https://example.invalid/99", result.ReleaseUrl);
        Assert.Equal(AppInfo.Version, result.CurrentVersion);
    }

    [Fact]
    public void ParseLatestRelease_SameTagAsCurrent_ReportsUpToDate()
    {
        var json = ReleaseJson($"v{AppInfo.Version}");

        var result = UpdateService.ParseLatestRelease(json);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public void ParseLatestRelease_OlderTag_ReportsUpToDate()
    {
        // Una compilación propia por delante del último release publicado no debe ofrecer
        // "actualizar" hacia atrás.
        var json = ReleaseJson("v0.1.0");

        var result = UpdateService.ParseLatestRelease(json);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public void ParseLatestRelease_MissingTag_ReportsFailed()
    {
        var result = UpdateService.ParseLatestRelease("{\"body\":\"sin etiqueta\"}");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void ParseLatestRelease_MalformedJson_ReportsFailedInsteadOfThrowing()
    {
        var result = UpdateService.ParseLatestRelease("{ esto no es json ");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public void ParseLatestRelease_EmptyBody_LeavesNotesNull()
    {
        var json = ReleaseJson("v99.0.0", "   ");

        var result = UpdateService.ParseLatestRelease(json);

        Assert.Null(result.ReleaseNotes);
    }

    [Fact]
    public void ParseLatestRelease_MissingUrl_FallsBackToReleasesPage()
    {
        var json = ReleaseJson("v99.0.0", "Notas.", null);

        var result = UpdateService.ParseLatestRelease(json);

        Assert.Equal(new UpdateService().ReleasesPageUrl, result.ReleaseUrl);
    }
}

public class UpdateServiceHttpTests
{
    /// <summary>Handler que responde lo que se le indique, sin salir a la red.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request));
    }

    private static UpdateService ServiceThat(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new HttpClient(new StubHandler(responder)));

    [Fact]
    public async Task CheckAsync_SuccessfulResponse_ReportsUpdate()
    {
        var service = ServiceThat(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"tag_name\":\"v99.0.0\",\"html_url\":\"https://example.invalid/99\"}")
        });

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("99.0.0", result.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_ErrorStatus_ReportsFailedWithTheCode()
    {
        var service = ServiceThat(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("403", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_NetworkFailure_ReportsFailedInsteadOfThrowing()
    {
        // Sin conexión, o con la salida bloqueada por la red del laboratorio: no poder
        // averiguar si hay versión nueva nunca debe interrumpir el trabajo.
        var service = ServiceThat(_ => throw new HttpRequestException("sin conexión"));

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("sin conexión", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = ServiceThat(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAsync(cts.Token));
    }
}
