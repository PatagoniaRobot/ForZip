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

using System.Text.Json;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

/// <summary>
/// Consulta la última publicación de ForZip en GitHub. Es la única conexión de red que la
/// aplicación inicia por su cuenta, y solo si el operador la pidió: no envía ningún dato del
/// equipo ni de la evidencia, únicamente hace una petición GET a un recurso público.
/// <para/>
/// Deliberadamente no descarga ni instala nada. Ver <see cref="UpdateCheckResult"/>.
/// </summary>
public class UpdateService : IUpdateService
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/PatagoniaRobot/ForZip/releases/latest";

    private const string ReleasesPage =
        "https://github.com/PatagoniaRobot/ForZip/releases";

    private static readonly HttpClient DefaultHttpClient = CreateDefaultClient();

    private readonly HttpClient _httpClient;

    /// <param name="httpClient">
    /// Solo para pruebas. En producción se usa un cliente compartido, ya configurado con los
    /// encabezados que exige la API.
    /// </param>
    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? DefaultHttpClient;
    }

    public string ReleasesPageUrl => ReleasesPage;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                return Failed($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseLatestRelease(json);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelación pedida por quien llama: se propaga. Un vencimiento del propio
            // HttpClient, en cambio, llega hasta el catch general y se informa como fallo.
            throw;
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }
    }

    /// <summary>
    /// Interpreta la respuesta de la API. Público y estático para poder probar el análisis del
    /// documento sin depender de la red.
    /// </summary>
    public static UpdateCheckResult ParseLatestRelease(string json)
    {
        string? tag;
        string? notes;
        string? url;
        IReadOnlyList<UpdateAsset> assets;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failed("Respuesta inesperada del servidor.");
            }

            tag = ReadString(root, "tag_name");
            notes = ReadString(root, "body");
            url = ReadString(root, "html_url");
            assets = ParseAssets(root);
        }
        catch (JsonException ex)
        {
            return Failed(ex.Message);
        }

        if (!VersionComparer.TryParse(tag, out var latest))
        {
            return Failed("La publicación no declara una versión reconocible.");
        }

        var latestText = latest.ToString(3);
        var isNewer = VersionComparer.IsNewer(tag, AppInfo.Version);

        return new UpdateCheckResult
        {
            Status = isNewer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
            CurrentVersion = AppInfo.Version,
            LatestVersion = latestText,
            ReleaseNotes = string.IsNullOrWhiteSpace(notes) ? null : notes!.Trim(),
            ReleaseUrl = string.IsNullOrWhiteSpace(url) ? ReleasesPage : url,
            Assets = assets
        };
    }

    /// <summary>Lee el arreglo <c>assets</c> de la publicación, salteando entradas incompletas.</summary>
    private static IReadOnlyList<UpdateAsset> ParseAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<UpdateAsset>();
        }

        var assets = new List<UpdateAsset>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(item, "name");
            var downloadUrl = ReadString(item, "browser_download_url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            long size = 0;
            if (item.TryGetProperty("size", out var sizeElement) &&
                sizeElement.ValueKind == JsonValueKind.Number)
            {
                sizeElement.TryGetInt64(out size);
            }

            assets.Add(new UpdateAsset { Name = name!, DownloadUrl = downloadUrl!, Size = size });
        }

        return assets;
    }

    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateAsset asset,
        UpdateAsset? checksumAsset,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        // Sin huella publicada no se instala nada. Es la única defensa contra que una cuenta
        // comprometida —o una descarga manipulada en tránsito— reemplace el binario de un perito.
        if (checksumAsset == null)
        {
            return new UpdateDownloadResult
            {
                Status = UpdateDownloadStatus.ChecksumMissing,
                ErrorMessage = $"La publicación no adjunta {asset.Name}.sha256."
            };
        }

        string? expected;
        try
        {
            var sidecar = await _httpClient.GetStringAsync(checksumAsset.DownloadUrl, ct);
            expected = Sha256Sidecar.Extract(sidecar);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DownloadFailed($"No se pudo descargar la huella publicada: {ex.Message}");
        }

        if (expected == null)
        {
            return new UpdateDownloadResult
            {
                Status = UpdateDownloadStatus.ChecksumMissing,
                ErrorMessage = $"El archivo {checksumAsset.Name} no contiene un SHA-256 legible."
            };
        }

        try
        {
            await DownloadToFileAsync(asset, destinationPath, progress, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDelete(destinationPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(destinationPath);
            return DownloadFailed(ex.Message);
        }

        var actual = await ComputeSha256Async(destinationPath, ct);

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            // Se borra: un binario que no verifica no debe quedar dando vueltas en el disco
            // esperando que alguien lo ejecute por error.
            TryDelete(destinationPath);
            return new UpdateDownloadResult
            {
                Status = UpdateDownloadStatus.ChecksumMismatch,
                ExpectedSha256 = expected,
                ActualSha256 = actual
            };
        }

        return new UpdateDownloadResult
        {
            Status = UpdateDownloadStatus.Verified,
            FilePath = destinationPath,
            ExpectedSha256 = expected,
            ActualSha256 = actual
        };
    }

    private async Task DownloadToFileAsync(
        UpdateAsset asset, string destinationPath, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(
            asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long copied = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;

            if (progress != null && total > 0)
            {
                progress.Report(100.0 * copied / total);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
        catch
        {
            // Un temporal que no se puede borrar no debe convertir un fallo en una excepción.
        }
    }

    private static UpdateDownloadResult DownloadFailed(string message) => new()
    {
        Status = UpdateDownloadStatus.DownloadFailed,
        ErrorMessage = message
    };

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static UpdateCheckResult Failed(string message) => new()
    {
        Status = UpdateCheckStatus.Failed,
        CurrentVersion = AppInfo.Version,
        ReleaseUrl = ReleasesPage,
        ErrorMessage = message
    };

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient
        {
            // Corto a propósito: en una red que bloquea la salida, el chequeo tiene que
            // rendirse rápido en vez de dejar la interfaz esperando.
            Timeout = TimeSpan.FromSeconds(15)
        };

        // La API de GitHub responde 403 a las peticiones sin User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ForZip/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
