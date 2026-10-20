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

using System.Buffers.Binary;
using System.Text;
using ForZip.Core.Sealing;
using Xunit;

namespace ForZip.Tests.Sealing;

/// <summary>
/// Tests del contenedor sellado <c>.fzs</c> (Fase A de la especificación 1.5.0). Cubren el
/// ida y vuelta, y las cuatro formas de manipulación que el formato debe detectar:
/// contraseña ajena, alteración del ciphertext, alteración del header y truncamiento.
/// </summary>
[Collection(ForZip.Tests.Sealing.SealingCollection.Name)]
public class SealedContainerTests
{
    // Los tests usan el chunk mínimo y pocas iteraciones: lo que se ejerce es el formato,
    // no el costo del KDF, que en producción es de 600.000 iteraciones.
    private const int TestChunkSize = SealedContainerFormat.MinChunkSize;
    private const int TestIterations = 1000;

    private const string Password = "clave-de-prueba-del-sobre-2026";
    private const string OtherPassword = "otra-clave-distinta-2026";

    private static SealingService CreateService() => new(TestIterations, TestChunkSize);

    // -------------------------------------------------------------------------
    //  1. Ida y vuelta
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]                          // payload vacío
    [InlineData(1)]                          // un solo byte
    [InlineData(TestChunkSize - 1)]          // menos de un chunk
    [InlineData(TestChunkSize)]              // múltiplo exacto de un chunk
    [InlineData(TestChunkSize + 1)]          // un chunk y una cola mínima
    [InlineData(TestChunkSize * 3)]          // múltiplo exacto de varios chunks
    [InlineData((TestChunkSize * 3) + 977)]  // varios chunks con cola parcial
    public async Task SealAsync_RoundTrip_DevuelveLosMismosBytes(int payloadSize)
    {
        var service = CreateService();
        var payload = BuildPayload(payloadSize);

        var sealedBytes = await SealAsync(service, payload);
        var opened = await UnsealAsync(service, sealedBytes, Password);

        Assert.Equal(payload, opened);
    }

    [Fact]
    public async Task SealAsync_ParametrosDeProduccion_RoundTripCorrecto()
    {
        // Un caso con los parámetros reales (600.000 iteraciones, chunks de 1 MiB) para que
        // ningún ajuste de los valores de test tape un problema en los de producción.
        var service = new SealingService();
        var payload = BuildPayload(4096);

        var sealedBytes = await SealAsync(service, payload);
        var opened = await UnsealAsync(service, sealedBytes, Password);

        Assert.Equal(payload, opened);
        Assert.Equal(KeyDerivation.DefaultIterations, ReadHeader(sealedBytes).Iterations);
        Assert.Equal(SealedContainerFormat.DefaultChunkSize, ReadHeader(sealedBytes).ChunkSize);
    }

    [Fact]
    public async Task SealAsync_EscribeMarcaYVersionDeFormato()
    {
        var service = CreateService();

        var sealedBytes = await SealAsync(service, BuildPayload(10));

        Assert.Equal(
            SealedContainerFormat.Magic,
            Encoding.ASCII.GetString(sealedBytes, 0, SealedContainerFormat.MagicLength));
        Assert.Equal(
            SealedContainerFormat.FormatVersion,
            BinaryPrimitives.ReadUInt16LittleEndian(sealedBytes.AsSpan(SealedContainerFormat.MagicLength)));
    }

    [Fact]
    public async Task ReadHeaderAsync_ArchivoAjeno_LanzaSealedFormatException()
    {
        var service = CreateService();
        var noEsUnSobre = Encoding.ASCII.GetBytes("PK esto es un ZIP cualquiera, no un sobre ForZip");

        await using var input = new MemoryStream(noEsUnSobre);
        await Assert.ThrowsAsync<SealedFormatException>(
            () => service.ReadHeaderAsync(input, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    //  2. Contraseña incorrecta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnsealAsync_ContrasenaIncorrecta_FallaEnElChunkCeroYNoEscribeNada()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize * 3));

        await using var input = new MemoryStream(sealedBytes);
        await using var output = new MemoryStream();

        await Assert.ThrowsAsync<SealedPasswordException>(
            () => service.UnsealAsync(input, output, OtherPassword, null, CancellationToken.None));

        // No se filtró un solo byte de la carga útil.
        Assert.Equal(0, output.Length);
    }

    // -------------------------------------------------------------------------
    //  3. Alteración del ciphertext
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnsealAsync_ByteAlteradoEnElChunkTres_FallaSoloEseChunk()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize * 5));

        // Un byte en el medio del chunk 3, contando ciphertext + tag por chunk.
        var offset = ChunkOffset(sealedBytes, 3) + (TestChunkSize / 2);
        sealedBytes[offset] ^= 0xFF;

        await using var input = new MemoryStream(sealedBytes);
        await using var output = new MemoryStream();

        var error = await Assert.ThrowsAsync<SealedIntegrityException>(
            () => service.UnsealAsync(input, output, Password, null, CancellationToken.None));

        Assert.Equal(3, error.ChunkIndex);

        // Los chunks 0, 1 y 2 sí validaron: la alteración está localizada.
        Assert.Equal(TestChunkSize * 3, output.Length);
    }

    // -------------------------------------------------------------------------
    //  4. Alteración del header
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnsealAsync_IteracionesAlteradasEnElHeader_FallaEnElChunkCero()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize * 2));

        MutateHeader(sealedBytes, "\"iterations\":1000", "\"iterations\":1001");

        await Assert.ThrowsAsync<SealedPasswordException>(
            () => UnsealAsync(service, sealedBytes, Password));
    }

    [Fact]
    public async Task UnsealAsync_SaltAlteradoEnElHeader_FallaEnElChunkCero()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize * 2));

        var salt = Convert.ToBase64String(ReadHeader(sealedBytes).Salt);
        MutateHeader(sealedBytes, salt, FlipBase64Char(salt));

        await Assert.ThrowsAsync<SealedPasswordException>(
            () => UnsealAsync(service, sealedBytes, Password));
    }

    [Fact]
    public async Task UnsealAsync_PayloadSizeAlteradoEnElHeader_FallaEnElChunkCero()
    {
        var service = CreateService();
        var payloadSize = TestChunkSize * 2;
        var sealedBytes = await SealAsync(service, BuildPayload(payloadSize));

        // Se baja el tamaño declarado sin cambiar la cantidad de chunks: el header sigue
        // siendo coherente, así que lo único que lo delata es el AAD del chunk 0.
        MutateHeader(
            sealedBytes,
            $"\"payloadSize\":{payloadSize}",
            $"\"payloadSize\":{payloadSize - 1}");

        await Assert.ThrowsAsync<SealedPasswordException>(
            () => UnsealAsync(service, sealedBytes, Password));
    }

    [Fact]
    public async Task ReadHeaderAsync_TotalChunksIncoherente_LanzaSealedFormatException()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize * 2));

        MutateHeader(sealedBytes, "\"totalChunks\":2", "\"totalChunks\":3");

        await using var input = new MemoryStream(sealedBytes);
        await Assert.ThrowsAsync<SealedFormatException>(
            () => service.ReadHeaderAsync(input, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    //  5. Truncamiento
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]                                  // falta un byte del último tag
    [InlineData(SealedContainerFormat.TagSize)]      // falta el último tag entero
    [InlineData(TestChunkSize)]                      // falta el último chunk entero
    public async Task UnsealAsync_ArchivoTruncado_LanzaSealedFormatException(int bytesRecortados)
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize * 3));

        var truncado = sealedBytes[..^bytesRecortados];

        await Assert.ThrowsAsync<SealedFormatException>(
            () => UnsealAsync(service, truncado, Password));
    }

    [Fact]
    public async Task UnsealAsync_BytesAgregadosAlFinal_LanzaSealedFormatException()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize));

        var conBasura = sealedBytes.Concat(new byte[] { 0x00, 0x01, 0x02 }).ToArray();

        await Assert.ThrowsAsync<SealedFormatException>(
            () => UnsealAsync(service, conBasura, Password));
    }

    // -------------------------------------------------------------------------
    //  6. Reordenamiento de chunks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnsealAsync_ChunksIntercambiados_DetectaLaAlteracion()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize * 4));

        // Chunks 1 y 2: ambos completos, así que ocupan bloques del mismo tamaño y el
        // intercambio no altera ninguna longitud. Solo lo delata el índice en nonce y AAD.
        var blockSize = TestChunkSize + SealedContainerFormat.TagSize;
        var first = ChunkOffset(sealedBytes, 1);
        var second = ChunkOffset(sealedBytes, 2);

        var buffer = sealedBytes[first..(first + blockSize)];
        Array.Copy(sealedBytes, second, sealedBytes, first, blockSize);
        Array.Copy(buffer, 0, sealedBytes, second, blockSize);

        var error = await Assert.ThrowsAsync<SealedIntegrityException>(
            () => UnsealAsync(service, sealedBytes, Password));

        Assert.Equal(1, error.ChunkIndex);
    }

    // -------------------------------------------------------------------------
    //  7. Unicidad de los nonces
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildNonce_TodosLosIndices_ProduceNoncesDistintos()
    {
        var nonceBase = Convert.FromHexString("0011223344556677");
        var nonce = new byte[SealedContainerFormat.NonceSize];
        var vistos = new HashSet<string>(StringComparer.Ordinal);

        for (uint index = 0; index < 20_000; index++)
        {
            SealedContainerFormat.BuildNonce(nonceBase, index, nonce);

            Assert.True(vistos.Add(Convert.ToHexString(nonce)), $"Nonce repetido en el índice {index}.");
            Assert.Equal(nonceBase, nonce[..SealedContainerFormat.NonceBaseSize]);
            Assert.Equal(index, BinaryPrimitives.ReadUInt32LittleEndian(nonce.AsSpan(SealedContainerFormat.NonceBaseSize)));
        }
    }

    [Fact]
    public void BuildAad_MarcaElUltimoChunk_YLoDistingueDelResto()
    {
        var headerHash = SealedHeader.ComputeHash("cualquier header"u8);
        var normal = new byte[SealedContainerFormat.AadSize];
        var ultimo = new byte[SealedContainerFormat.AadSize];

        SealedContainerFormat.BuildAad(headerHash, 7, isFinal: false, normal);
        SealedContainerFormat.BuildAad(headerHash, 7, isFinal: true, ultimo);

        Assert.Equal(0x00, normal[^1]);
        Assert.Equal(0x01, ultimo[^1]);
        Assert.NotEqual(Convert.ToHexString(normal), Convert.ToHexString(ultimo));
    }

    // -------------------------------------------------------------------------
    //  8. Bordes del parser
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SealAsync_PayloadVacio_ProduceUnSobreSinChunks()
    {
        var service = CreateService();

        var sealedBytes = await SealAsync(service, Array.Empty<byte>());
        var header = ReadHeader(sealedBytes);

        Assert.Equal(0, header.PayloadSize);
        Assert.Equal(0, header.TotalChunks);

        // El sobre es exactamente preámbulo + header: no hay un solo chunk detrás.
        Assert.Equal(ChunkOffset(sealedBytes, 0), sealedBytes.Length);
        Assert.Empty(await UnsealAsync(service, sealedBytes, Password));
    }

    [Fact]
    public async Task SealAsync_UnSoloByte_ProduceUnChunkDeUnByte()
    {
        var service = CreateService();

        var sealedBytes = await SealAsync(service, new byte[] { 0x5A });
        var header = ReadHeader(sealedBytes);

        Assert.Equal(1, header.PayloadSize);
        Assert.Equal(1, header.TotalChunks);
        Assert.Equal(ChunkOffset(sealedBytes, 0) + 1 + SealedContainerFormat.TagSize, sealedBytes.Length);
        Assert.Equal(new byte[] { 0x5A }, await UnsealAsync(service, sealedBytes, Password));
    }

    // -------------------------------------------------------------------------
    //  Header: el sobre no puede filtrar nada del contenido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Header_SoloDeclaraLosCamposDeLaEspecificacion()
    {
        var service = CreateService();
        var sealedBytes = await SealAsync(service, BuildPayload(TestChunkSize));

        using var json = System.Text.Json.JsonDocument.Parse(HeaderJson(sealedBytes));
        var campos = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(
            new[] { "packageId", "sealedAtUtc", "kdf", "cipher", "payloadSize", "totalChunks" },
            campos);
        Assert.Equal(
            new[] { "algo", "iterations", "saltB64" },
            json.RootElement.GetProperty("kdf").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(
            new[] { "algo", "chunkSize", "nonceBaseB64" },
            json.RootElement.GetProperty("cipher").EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Header_PackageIdEsAleatorio_YDaElNombreDeLosArtefactos()
    {
        var service = CreateService();
        var payload = BuildPayload(64);

        var primero = ReadHeader(await SealAsync(service, payload));
        var segundo = ReadHeader(await SealAsync(service, payload));

        Assert.NotEqual(primero.PackageId, segundo.PackageId);
        Assert.StartsWith(SealedContainerFormat.FileNamePrefix, primero.BaseName, StringComparison.Ordinal);
        Assert.Equal(
            SealedContainerFormat.FileNamePrefix.Length + SealedContainerFormat.FileNameIdLength,
            primero.BaseName.Length);
    }

    // -------------------------------------------------------------------------
    //  Salida a disco: nunca queda un sobre incompleto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SealFileAsync_RoundTripEnDisco_DevuelveLosMismosBytes()
    {
        var service = CreateService();
        var workDir = CreateWorkDirectory();

        try
        {
            var payloadPath = Path.Combine(workDir, "payload.bin");
            var sealedPath = Path.Combine(workDir, "sobre.fzs");
            var openedPath = Path.Combine(workDir, "abierto.bin");
            var payload = BuildPayload(TestChunkSize * 2);
            await File.WriteAllBytesAsync(payloadPath, payload);

            var header = await service.SealFileAsync(payloadPath, sealedPath, Password, null, CancellationToken.None);
            await service.UnsealFileAsync(sealedPath, openedPath, Password, null, CancellationToken.None);

            Assert.Equal(payload, await File.ReadAllBytesAsync(openedPath));
            Assert.Equal(payload.Length, header.PayloadSize);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task UnsealFileAsync_ContrasenaIncorrecta_NoDejaSalidaParcial()
    {
        var service = CreateService();
        var workDir = CreateWorkDirectory();

        try
        {
            var payloadPath = Path.Combine(workDir, "payload.bin");
            var sealedPath = Path.Combine(workDir, "sobre.fzs");
            var openedPath = Path.Combine(workDir, "abierto.bin");
            await File.WriteAllBytesAsync(payloadPath, BuildPayload(TestChunkSize * 2));

            await service.SealFileAsync(payloadPath, sealedPath, Password, null, CancellationToken.None);

            await Assert.ThrowsAsync<SealedPasswordException>(
                () => service.UnsealFileAsync(sealedPath, openedPath, OtherPassword, null, CancellationToken.None));

            Assert.False(File.Exists(openedPath), "Quedó una salida parcial en disco.");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task SealFileAsync_Cancelado_NoDejaSobreParcial()
    {
        var service = CreateService();
        var workDir = CreateWorkDirectory();

        try
        {
            var payloadPath = Path.Combine(workDir, "payload.bin");
            var sealedPath = Path.Combine(workDir, "sobre.fzs");
            await File.WriteAllBytesAsync(payloadPath, BuildPayload(TestChunkSize * 4));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.SealFileAsync(payloadPath, sealedPath, Password, null, cts.Token));

            Assert.False(File.Exists(sealedPath), "Quedó un sobre incompleto en disco.");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    //  Ayudantes
    // -------------------------------------------------------------------------

    private static string CreateWorkDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ForZipSealTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }


    private static byte[] BuildPayload(int size)
    {
        var payload = new byte[size];
        new Random(20260903).NextBytes(payload);
        return payload;
    }

    private static async Task<byte[]> SealAsync(SealingService service, byte[] payload)
    {
        await using var input = new MemoryStream(payload, writable: false);
        await using var output = new MemoryStream();

        await service.SealAsync(input, output, Password, null, CancellationToken.None);
        return output.ToArray();
    }

    private static async Task<byte[]> UnsealAsync(SealingService service, byte[] sealedBytes, string password)
    {
        await using var input = new MemoryStream(sealedBytes, writable: false);
        await using var output = new MemoryStream();

        await service.UnsealAsync(input, output, password, null, CancellationToken.None);
        return output.ToArray();
    }

    private static int HeaderLength(byte[] sealedBytes) =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(
            sealedBytes.AsSpan(SealedContainerFormat.MagicLength + sizeof(ushort)));

    private static string HeaderJson(byte[] sealedBytes) =>
        Encoding.UTF8.GetString(sealedBytes, SealedContainerFormat.PreambleLength, HeaderLength(sealedBytes));

    private static SealedHeader ReadHeader(byte[] sealedBytes) =>
        SealedHeader.Parse(sealedBytes.AsSpan(SealedContainerFormat.PreambleLength, HeaderLength(sealedBytes)));

    /// <summary>Offset del chunk indicado, asumiendo chunks completos salvo el último.</summary>
    private static int ChunkOffset(byte[] sealedBytes, int chunkIndex)
    {
        var header = ReadHeader(sealedBytes);
        return SealedContainerFormat.PreambleLength
            + HeaderLength(sealedBytes)
            + (chunkIndex * (header.ChunkSize + SealedContainerFormat.TagSize));
    }

    /// <summary>
    /// Reemplaza un fragmento del header JSON por otro de la misma longitud, sin tocar el
    /// campo <c>headerLen</c> del preámbulo.
    /// </summary>
    private static void MutateHeader(byte[] sealedBytes, string find, string replace)
    {
        Assert.Equal(find.Length, replace.Length);

        var json = HeaderJson(sealedBytes);
        var position = json.IndexOf(find, StringComparison.Ordinal);
        Assert.True(position >= 0, $"El header no contiene '{find}'.");

        var replacement = Encoding.UTF8.GetBytes(replace);
        replacement.CopyTo(sealedBytes, SealedContainerFormat.PreambleLength + position);
    }

    /// <summary>Cambia un carácter del Base64 por otro válido, para alterar un byte del salt.</summary>
    private static string FlipBase64Char(string value)
    {
        var chars = value.ToCharArray();
        chars[0] = chars[0] == 'A' ? 'B' : 'A';
        return new string(chars);
    }
}
