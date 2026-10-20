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

using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ForZip.Core.Sealing;

/// <summary>
/// Recibo público del sobre sellado. Es el único artefacto del perfil Reservado que puede
/// circular en claro: se adjunta a un oficio, se manda por correo o se incorpora al
/// expediente sin exponer absolutamente nada del contenido.
/// </summary>
/// <remarks>
/// <b>El recibo prueba integridad y autoría; el sobre guarda el contenido.</b> Son dos
/// artefactos separados, y por eso su contenido está cerrado por especificación: nada de
/// nombres de archivo, rutas, cantidad de archivos, hashes por archivo, número de causa,
/// descripción, dependencia judicial, operador ni fechas de los archivos originales.
/// <para>
/// El <see cref="SealedSha256"/> es el hash <i>del sobre</i>, no del contenido: no habilita
/// ninguna comparación contra bases de hashes de material conocido. Esa es exactamente la
/// propiedad que se busca.
/// </para>
/// </remarks>
public sealed class Receipt
{
    /// <summary>Versión del recibo que esta compilación emite y sabe leer.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Valor del campo <c>profile</c> para un paquete sellado.</summary>
    public const string ReservedProfile = "Reservado";

    // Mismo formato de fecha que el header del sobre: ISO 8601 UTC con precisión de segundo.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    // Orden y nombre exactos de los campos admitidos. Cualquier campo fuera de esta lista
    // vuelve inválido el recibo: es la defensa contra que una versión futura filtre datos
    // del contenido por agregar un campo "inofensivo".
    private static readonly string[] AllowedFields =
    {
        "receiptVersion",
        "packageId",
        "sealedFileName",
        "sealedSha256",
        "sealedSize",
        "sealedAtUtc",
        "forZipVersion",
        "formatVersion",
        "profile"
    };

    public Receipt(
        int receiptVersion,
        Guid packageId,
        string sealedFileName,
        string sealedSha256,
        long sealedSize,
        DateTimeOffset sealedAtUtc,
        string forZipVersion,
        int formatVersion,
        string profile)
    {
        ReceiptVersion = receiptVersion;
        PackageId = packageId;
        SealedFileName = sealedFileName;
        SealedSha256 = sealedSha256;
        SealedSize = sealedSize;
        SealedAtUtc = sealedAtUtc;
        ForZipVersion = forZipVersion;
        FormatVersion = formatVersion;
        Profile = profile;
    }

    /// <summary>Versión del formato del recibo.</summary>
    public int ReceiptVersion { get; }

    /// <summary>Identificador aleatorio del paquete, el mismo que declara el sobre.</summary>
    public Guid PackageId { get; }

    /// <summary>Nombre del archivo sellado. Se deriva del identificador, nunca de la causa.</summary>
    public string SealedFileName { get; }

    /// <summary>SHA-256 del <c>.fzs</c> completo, en hexadecimal minúscula.</summary>
    public string SealedSha256 { get; }

    /// <summary>Tamaño del <c>.fzs</c> en bytes.</summary>
    public long SealedSize { get; }

    /// <summary>Fecha de sellado en UTC.</summary>
    public DateTimeOffset SealedAtUtc { get; }

    /// <summary>Versión de ForZip que produjo el sobre.</summary>
    public string ForZipVersion { get; }

    /// <summary>Versión del formato del contenedor sellado.</summary>
    public int FormatVersion { get; }

    /// <summary>Perfil de confidencialidad del paquete.</summary>
    public string Profile { get; }

    /// <summary>
    /// Arma el recibo de un sobre ya escrito en disco, a partir de su header y de la huella
    /// del archivo completo.
    /// </summary>
    public static Receipt Create(SealedHeader header, string sealedFileName, string sealedSha256, long sealedSize)
    {
        ArgumentNullException.ThrowIfNull(header);

        return new Receipt(
            CurrentVersion,
            header.PackageId,
            sealedFileName,
            sealedSha256,
            sealedSize,
            header.SealedAtUtc,
            AppInfo.Version,
            SealedContainerFormat.FormatVersion,
            ReservedProfile);
    }

    /// <summary>
    /// Serializa el recibo con sangría, para que sea legible en el expediente. La firma CMS
    /// cubre los bytes del archivo tal como quedan escritos, así que el formato es libre.
    /// </summary>
    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        var options = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("receiptVersion", ReceiptVersion);
            writer.WriteString("packageId", PackageId.ToString("D"));
            writer.WriteString("sealedFileName", SealedFileName);
            writer.WriteString("sealedSha256", SealedSha256);
            writer.WriteNumber("sealedSize", SealedSize);
            writer.WriteString("sealedAtUtc", SealedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteString("forZipVersion", ForZipVersion);
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WriteString("profile", Profile);
            writer.WriteEndObject();
        }

        return SealedContainerFormat.HeaderEncoding.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Interpreta un recibo. Rechaza cualquier campo que no esté en la especificación: un
    /// recibo con datos de más no es un recibo válido.
    /// </summary>
    /// <exception cref="SealedFormatException">El recibo está malformado o lleva campos ajenos.</exception>
    public static Receipt Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SealedFormatException("El recibo está malformado.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SealedFormatException("El recibo está malformado.");
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!AllowedFields.Contains(property.Name, StringComparer.Ordinal))
                {
                    throw new SealedFormatException(
                        $"El recibo declara el campo '{property.Name}', que no forma parte de la especificación.");
                }
            }

            var receiptVersion = ReadInt32(root, "receiptVersion");
            if (receiptVersion != CurrentVersion)
            {
                throw new SealedFormatException(
                    $"El recibo usa la versión {receiptVersion} y esta versión de ForZip solo lee la {CurrentVersion}.");
            }

            var sealedSha256 = ReadString(root, "sealedSha256");
            if (sealedSha256.Length != 64 || !sealedSha256.All(Uri.IsHexDigit))
            {
                throw new SealedFormatException("El recibo declara un SHA-256 inválido.");
            }

            var sealedSize = ReadInt64(root, "sealedSize");
            if (sealedSize < 0)
            {
                throw new SealedFormatException("El recibo declara un tamaño inválido.");
            }

            return new Receipt(
                receiptVersion,
                ReadGuid(root, "packageId"),
                ReadString(root, "sealedFileName"),
                sealedSha256.ToLowerInvariant(),
                sealedSize,
                ReadTimestamp(root, "sealedAtUtc"),
                ReadString(root, "forZipVersion"),
                ReadInt32(root, "formatVersion"),
                ReadString(root, "profile"));
        }
    }

    private static string ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new SealedFormatException($"Falta el campo '{name}' del recibo o es inválido.");
        }

        return value.GetString()!;
    }

    private static int ReadInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed))
        {
            throw new SealedFormatException($"Falta el campo '{name}' del recibo o es inválido.");
        }

        return parsed;
    }

    private static long ReadInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var parsed))
        {
            throw new SealedFormatException($"Falta el campo '{name}' del recibo o es inválido.");
        }

        return parsed;
    }

    private static Guid ReadGuid(JsonElement parent, string name)
    {
        if (!Guid.TryParseExact(ReadString(parent, name), "D", out var parsed))
        {
            throw new SealedFormatException($"El campo '{name}' del recibo es inválido.");
        }

        return parsed;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement parent, string name)
    {
        if (!DateTimeOffset.TryParseExact(
                ReadString(parent, name),
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new SealedFormatException($"El campo '{name}' del recibo es inválido.");
        }

        return parsed;
    }
}
