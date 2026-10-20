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

using System.Diagnostics;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Prueba de archivos comprimidos sin manifiesto y autoextraíbles 7z (desde la 2.0.0: verificar el
/// autoextraíble sin ejecutarlo, y probar cualquier archivo comprimido que llegue de terceros).
/// </summary>
public class ArchiveTestTests : IDisposable
{
    private readonly string _workDir;
    private readonly ZipService _zipService = new(new HashService());

    public ArchiveTestTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // Limpieza best-effort
        }
    }

    private string BuildSource()
    {
        var root = Path.Combine(_workDir, "caso");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "acta.txt"), "MARCADOR_UNICO_DEL_ACTA " + new string('x', 5000), Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "sub", "foto.txt"), "otra cosa", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(root, "vacio.bin"), []);
        return root;
    }

    private async Task<string> PackAsync(string source, string output, ArchiveFormat format, string? password = null, int level = 5)
    {
        await _zipService.CompressAsync(new ZipOptions
        {
            SourcePaths = new List<string> { source },
            OutputPath = output,
            CompressionLevel = level,
            Password = password,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
            Format = format
        }, null, CancellationToken.None);
        return output;
    }

    /// <summary>Un "ejecutable" de mentira: "MZ", ruido y, si se pide, el 7z pegado detrás, como un autoextraíble.</summary>
    private string MakeExe(string name, byte[]? appended, bool decoySignature = false)
    {
        var stub = new byte[300_000];
        new Random(11).NextBytes(stub);
        stub[0] = (byte)'M';
        stub[1] = (byte)'Z';
        if (decoySignature)
        {
            // Los seis bytes de la firma 7z dentro del programa, sin un encabezado válido detrás: no deben confundir.
            new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }.CopyTo(stub, 1000);
        }

        var path = Path.Combine(_workDir, name);
        File.WriteAllBytes(path, appended == null ? stub : [.. stub, .. appended]);
        return path;
    }

    [Fact]
    public async Task Zip_Sound_NoErrors_WithItsFingerprint()
    {
        var zip = await PackAsync(BuildSource(), Path.Combine(_workDir, "caso.zip"), ArchiveFormat.Zip);

        var result = await _zipService.TestAsync(zip, null, null, CancellationToken.None);

        Assert.Equal(ArchiveTestOutcome.NoErrors, result.Outcome);
        Assert.Equal("ZIP", result.Format);
        Assert.False(result.IsSelfExtracting);
        Assert.Equal(3, result.FilesOk);
        Assert.Empty(result.Damaged);
        var (fileName, size, sha256) = Assert.Single(result.ArchiveFiles);
        Assert.Equal("caso.zip", fileName);
        Assert.Equal(new FileInfo(zip).Length, size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(), sha256);
    }

    [Fact]
    public async Task Zip_ByteChangedInContent_Damaged_AndTheRestStillTested()
    {
        var zip = await PackAsync(BuildSource(), Path.Combine(_workDir, "caso.zip"), ArchiveFormat.Zip, level: 0);
        var bytes = File.ReadAllBytes(zip);
        int at = bytes.AsSpan().IndexOf("MARCADOR_UNICO"u8);
        Assert.True(at > 0);
        bytes[at + 3] ^= 0x20;
        File.WriteAllBytes(zip, bytes);

        var result = await _zipService.TestAsync(zip, null, null, CancellationToken.None);

        Assert.Equal(ArchiveTestOutcome.Damaged, result.Outcome);
        Assert.Contains(result.Damaged, d => d.Name.EndsWith("acta.txt"));
        Assert.Equal(2, result.FilesOk);
    }

    [Fact]
    public async Task SevenZ_Encrypted_AsksForThePassword_AndTellsAWrongOne()
    {
        var archive = await PackAsync(BuildSource(), Path.Combine(_workDir, "caso.7z"), ArchiveFormat.SevenZ, "Clave-Buena-1");

        var none = await _zipService.TestAsync(archive, null, null, CancellationToken.None);
        var wrong = await _zipService.TestAsync(archive, "otra", null, CancellationToken.None);
        var right = await _zipService.TestAsync(archive, "Clave-Buena-1", null, CancellationToken.None);

        Assert.Equal(ArchiveTestOutcome.PasswordRequired, none.Outcome);
        Assert.Equal(ArchiveTestOutcome.WrongPassword, wrong.Outcome);
        Assert.Equal(ArchiveTestOutcome.NoErrors, right.Outcome);
        Assert.Equal(3, right.FilesOk);
        // Sin la contraseña igual se informa la huella: es del archivo, no del contenido.
        Assert.Single(none.ArchiveFiles);
    }

    [Fact]
    public async Task SevenZ_SelfExtracting_IsReadNotRun_TestedAndExtracted()
    {
        var source = BuildSource();
        var archive = await PackAsync(source, Path.Combine(_workDir, "caso.7z"), ArchiveFormat.SevenZ, "Clave-Buena-1");
        var exe = MakeExe("caso.exe", File.ReadAllBytes(archive), decoySignature: true);

        Assert.True(ArchiveFormats.Is7zSelfExtracting(exe));
        Assert.Equal(ArchiveFormat.SevenZ, ArchiveFormats.Detect(exe));
        Assert.Equal("7z", ZipService.IdentifyArchive(exe));

        var result = await _zipService.TestAsync(exe, "Clave-Buena-1", null, CancellationToken.None);
        Assert.Equal(ArchiveTestOutcome.NoErrors, result.Outcome);
        Assert.True(result.IsSelfExtracting);
        Assert.Equal(3, result.FilesOk);

        var output = Path.Combine(_workDir, "extraido");
        var extraction = await _zipService.DecompressAsync(exe, output, "Clave-Buena-1", null, CancellationToken.None);
        Assert.Equal(3, extraction.FilesExtracted);
        Assert.Equal(File.ReadAllBytes(Path.Combine(source, "acta.txt")),
                     File.ReadAllBytes(Path.Combine(output, "caso", "acta.txt")));
    }

    [Fact]
    public async Task WrongPassword_IsCertainOnlyWhereTheFormatKeepsACheck()
    {
        // En RAR5 se dice "contraseña incorrecta", porque el formato guarda un valor de chequeo. En
        // RAR 4 y en 7z una contraseña mala y un archivo dañado no se distinguen, y así se sigue diciendo.
        string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rar", name);
        var sevenZ = await PackAsync(BuildSource(), Path.Combine(_workDir, "caso.7z"), ArchiveFormat.SevenZ, "Clave-Buena-1");

        var rar5 = await _zipService.TestAsync(Fixture("m3_v5_pass.rar"), "otra", null, CancellationToken.None);
        var rar4 = await _zipService.TestAsync(Fixture("m3_v4_pass.rar"), "otra", null, CancellationToken.None);
        var seven = await _zipService.TestAsync(sevenZ, "otra", null, CancellationToken.None);
        var rar5Right = await _zipService.TestAsync(Fixture("m3_v5_pass.rar"), "Clave123", null, CancellationToken.None);

        Assert.Equal(ArchiveTestOutcome.WrongPassword, rar5.Outcome);
        Assert.True(rar5.PasswordVerifiedWrong);
        Assert.Equal(ArchiveTestOutcome.WrongPassword, rar4.Outcome);
        Assert.False(rar4.PasswordVerifiedWrong);
        Assert.Equal(ArchiveTestOutcome.WrongPassword, seven.Outcome);
        Assert.False(seven.PasswordVerifiedWrong);
        Assert.Equal(ArchiveTestOutcome.NoErrors, rar5Right.Outcome);
    }

    [Fact]
    public async Task FailedExtraction_LeavesNoFoldersItCreated()
    {
        // Cancelar dejaba "grande (3)" vacía. Lo mismo con una contraseña mala.
        var zip = await PackAsync(BuildSource(), Path.Combine(_workDir, "caso.zip"), ArchiveFormat.Zip, "Clave-Buena-1");
        var nuevo = Path.Combine(_workDir, "nuevo");
        var destino = Path.Combine(nuevo, "sub", "caso (3)");

        await Assert.ThrowsAnyAsync<Exception>(() => _zipService.DecompressAsync(zip, destino, "otra", null, CancellationToken.None));

        Assert.False(Directory.Exists(nuevo), "quedó la carpeta que creó la extracción fallida");
    }

    [Fact]
    public async Task CancelledExtraction_LeavesNoFoldersItCreated()
    {
        var source = BuildSource();
        File.WriteAllBytes(Path.Combine(source, "sub", "grande.bin"), RandomNumberGenerator.GetBytes(3_000_000));
        var archive = await PackAsync(source, Path.Combine(_workDir, "caso.7z"), ArchiveFormat.SevenZ, level: 0);
        var destino = Path.Combine(_workDir, "grande (3)");
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _zipService.DecompressAsync(
            archive, destino, null, new CancelOnFirstReport(cts), cts.Token));

        Assert.False(Directory.Exists(destino));
    }

    [Fact]
    public async Task FailedExtraction_LeavesAnExistingDestinationAsItWas()
    {
        var zip = await PackAsync(BuildSource(), Path.Combine(_workDir, "caso.zip"), ArchiveFormat.Zip, "Clave-Buena-1");
        var destino = Path.Combine(_workDir, "ya_existia");
        Directory.CreateDirectory(destino);
        File.WriteAllText(Path.Combine(destino, "otra_evidencia.txt"), "no tocar");

        await Assert.ThrowsAnyAsync<Exception>(() => _zipService.DecompressAsync(zip, destino, "otra", null, CancellationToken.None));

        Assert.Equal(new[] { "otra_evidencia.txt" }, Directory.GetFileSystemEntries(destino).Select(Path.GetFileName));
    }

    /// <summary>Cancela apenas hay avance: la extracción ya creó su carpeta y empezó a escribir.</summary>
    private sealed class CancelOnFirstReport(CancellationTokenSource cts) : IProgress<(long, long)>
    {
        public void Report((long, long) value) => cts.Cancel();
    }

    [Fact]
    public async Task AnExecutableWithoutAnArchive_IsNotAnArchive()
    {
        var exe = MakeExe("programa.exe", null, decoySignature: true);

        Assert.False(ArchiveFormats.Is7zSelfExtracting(exe));
        Assert.Null(ZipService.IdentifyArchive(exe));
        var result = await _zipService.TestAsync(exe, null, null, CancellationToken.None);
        Assert.Equal(ArchiveTestOutcome.Unreadable, result.Outcome);
        Assert.Empty(result.ArchiveFiles);
    }

    [Fact]
    public async Task Tar_ReadInFull_ButHasNoChecks()
    {
        var source = BuildSource();
        var tar = Path.Combine(_workDir, "caso.tar");
        await TarFile.CreateFromDirectoryAsync(source, tar, includeBaseDirectory: true);

        var result = await _zipService.TestAsync(tar, null, null, CancellationToken.None);

        // Un TAR no guarda sumas de su contenido: se leyó entero, pero no se puede decir "sin errores".
        Assert.Equal(ArchiveTestOutcome.NoChecks, result.Outcome);
        Assert.Equal(3, result.FilesOk);
    }

    [Fact]
    public async Task MissingMiddleVolume_IsNamed_NotCalledDamage()
    {
        var source = BuildSource();
        File.WriteAllBytes(Path.Combine(source, "grande.bin"), RandomNumberGenerator.GetBytes(400_000));
        var basePath = Path.Combine(_workDir, "vol.7z");
        await _zipService.CompressAsync(new ZipOptions
        {
            SourcePaths = new List<string> { source },
            OutputPath = basePath,
            CompressionLevel = 0,
            SplitSize = 100_000,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
            Format = ArchiveFormat.SevenZ
        }, null, CancellationToken.None);
        File.Delete(basePath + ".002");

        var result = await _zipService.TestAsync(basePath + ".001", null, null, CancellationToken.None);

        Assert.Equal(ArchiveTestOutcome.Unreadable, result.Outcome);
        Assert.Contains("vol.7z.002", result.Error);
    }

    [Requires7zFact]
    public async Task RealSevenZipSfx_WithEncryptedNames_IsTestedWithoutRunningIt()
    {
        var sevenZip = Requires7zFactAttribute.Find7z()!;
        var sfxModule = Path.Combine(Path.GetDirectoryName(sevenZip)!, "7z.sfx");
        Assert.True(File.Exists(sfxModule), "7z.sfx no está junto a 7z.exe");

        var source = BuildSource();
        var exe = Path.Combine(_workDir, "real.exe");
        var psi = new ProcessStartInfo(sevenZip)
        {
            ArgumentList = { "a", $"-sfx{sfxModule}", exe, source, "-pClave-Real-9", "-mhe=on", "-bso0", "-bsp0" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(psi)!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        Assert.True(ArchiveFormats.Is7zSelfExtracting(exe));
        var none = await _zipService.TestAsync(exe, null, null, CancellationToken.None);
        var wrong = await _zipService.TestAsync(exe, "otra", null, CancellationToken.None);
        var right = await _zipService.TestAsync(exe, "Clave-Real-9", null, CancellationToken.None);

        Assert.Equal(ArchiveTestOutcome.PasswordRequired, none.Outcome);
        // Con los nombres cifrados de 7-Zip, la contraseña mala deja el encabezado ilegible: no es "dañado".
        Assert.Equal(ArchiveTestOutcome.WrongPassword, wrong.Outcome);
        Assert.Equal(ArchiveTestOutcome.NoErrors, right.Outcome);
        Assert.True(right.IsSelfExtracting);
        Assert.Equal(3, right.FilesOk);
    }
}
