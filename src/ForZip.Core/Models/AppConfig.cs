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

namespace ForZip.Core.Models;

public class AppConfig
{
    public string Language { get; set; } = "es";
    public string Theme { get; set; } = "dark";
    public int DefaultCompressionLevel { get; set; } = 5;
    public HashSet<HashAlgorithmType> DefaultHashAlgorithms { get; set; } = new() { HashAlgorithmType.SHA256 };
    public string? DefaultOutputDirectory { get; set; }
    public OperatorInfo Operator { get; set; } = new();

    /// <summary>Vista Comprimir en modo simple (guiado, con valores forenses recomendados).</summary>
    public bool SimpleMode { get; set; } = true;

    /// <summary>
    /// Perfil de confidencialidad del empaquetado. Es un eje <b>ortogonal</b> a
    /// <see cref="SimpleMode"/>: aquel decide cuántos controles se ven, este decide qué
    /// artefactos se producen y qué información queda expuesta. Son campos separados a
    /// propósito y no deben fusionarse en un solo selector.
    /// </summary>
    public ConfidentialityProfile Profile { get; set; } = ConfidentialityProfile.Standard;

    /// <summary>
    /// Formato elegido en el modo avanzado. El modo simple y el perfil Reservado empaquetan siempre en
    /// ZIP: el simple aplica los valores recomendados y el Reservado mete el ZIP en el sobre.
    /// </summary>
    public ArchiveFormat PreferredFormat { get; set; } = ArchiveFormat.Zip;

    /// <summary>
    /// El aviso del perfil Estándar sobre nombres y sidecar en claro ya se mostró en esta
    /// sesión. No se persiste: una advertencia que aparece siempre se vuelve invisible, pero
    /// una que no vuelve nunca más deja de advertir a quien cambió de máquina.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool StandardProfileNoticeShown { get; set; }

    /// <summary>
    /// TSA (RFC 3161) usada para el sellado de tiempo de la firma del manifiesto.
    /// </summary>
    public string TimestampServerUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>
    /// Consultar al iniciar si hay una versión más reciente publicada. Desactivado por defecto:
    /// ForZip no debe iniciar tráfico de red que el operador no haya pedido, y en un laboratorio
    /// aislado la consulta no haría más que fallar.
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = false;

    /// <summary>
    /// Versión desde la que se acaba de actualizar. La escribe el actualizador antes de
    /// relanzar y la consume el arranque siguiente para mostrar qué cambió; después se limpia.
    /// </summary>
    public string? UpdatedFromVersion { get; set; }

    /// <summary>
    /// Notas de la publicación instalada, guardadas antes de reiniciar para poder mostrarlas
    /// aunque el equipo ya no tenga conexión.
    /// </summary>
    public string? UpdatedReleaseNotes { get; set; }

    /// <summary>
    /// Certificados de quienes firman manifiestos y recibos en los que el operador confía, por su huella SHA-256.
    /// Con la lista se puede decir "firmado por alguien conocido"; sin ella, solo que la firma es válida.
    /// </summary>
    public List<TrustedSigner> TrustedSigners { get; set; } = new();
}

/// <summary>Un firmante de confianza: la huella SHA-256 de su certificado y un nombre para reconocerlo.</summary>
public sealed class TrustedSigner
{
    public string Thumbprint { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
