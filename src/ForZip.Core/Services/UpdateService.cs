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
            ReleaseUrl = string.IsNullOrWhiteSpace(url) ? ReleasesPage : url
        };
    }

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
