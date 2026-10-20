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
using System.IO.Compression;
using System.Text;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Los defectos que encontró la revisión del 2026-10-10, cada uno con la prueba que lo reproducía: ZIP
/// alterado extraído sin aviso, nombres con ':' y duplicados en un ZIP, un paquete viejo que le ganaba al
/// nuevo, la salida dentro de la evidencia, junctions seguidas y un volumen faltante sin nombrar.
/// </summary>
public class PackagingSafetyTests : IDisposable
{
    private readonly string _workDir;
    private readonly ZipService _service = new(new HashService());

    public PackagingSafetyTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_safety_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try
        {
            // Las junctions se borran como carpetas vacías, sin tocar su destino.
            foreach (var dir in Directory.EnumerateDirectories(_workDir, "*", new EnumerationOptions { RecurseSubdirectories = true }))
            {
                if (new DirectoryInfo(dir).LinkTarget != null)
                {
                    Directory.Delete(dir);
                }
            }

            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string MakeSource(string name = "caso")
    {
        var src = Path.Combine(_workDir, name);
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "acta.txt"), "acta original");
        var datos = new byte[300_000];
        new Random(1).NextBytes(datos); // no comprime: alcanza para varios volúmenes de 64 KB
        File.WriteAllBytes(Path.Combine(src, "sub", "datos.bin"), datos);
        return src;
    }

    private Task<CompressionResult> PackAsync(string source, string output, long? split = null, string? password = null) =>
        _service.CompressAsync(new ZipOptions
        {
            SourcePaths = new List<string> { source },
            OutputPath = output,
            CompressionLevel = 5,
            SplitSize = split,
            Password = password,
            HashAlgorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }
        }, null, CancellationToken.None);

    // ---------- Extracción de ZIP ----------

    [Fact]
    public async Task Zip_StoredEntryWithAlteredByte_IsSetAsideAndTheRestIsExtracted()
    {
        var zip = Path.Combine(_workDir, "alterado.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("acta.txt", CompressionLevel.NoCompression);
            using (var w = new StreamWriter(entry.Open()))
            {
                w.Write(string.Concat(Enumerable.Repeat("contenido original del acta\n", 10)));
            }

            Write(archive, "otro.txt", "este archivo está sano");
        }

        var bytes = File.ReadAllBytes(zip);
        int at = bytes.AsSpan().IndexOf("contenido original"u8);
        bytes[at] ^= 0x20;
        File.WriteAllBytes(zip, bytes);

        var dest = Path.Combine(_workDir, "salida");
        var result = await _service.DecompressAsync(zip, dest, null, null, CancellationToken.None);

        // El dañado no queda en el destino (ni con su nombre ni a medias) y queda documentado; el sano sale.
        Assert.False(result.IsComplete);
        var damaged = Assert.Single(result.Damaged);
        Assert.Equal("acta.txt", damaged.Name);
        Assert.Contains("CRC32", damaged.Reason, StringComparison.Ordinal);
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(new[] { "otro.txt" }, Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Zip_AesEntryWithAlteredByte_IsRejected()
    {
        var src = MakeSource();
        var zip = Path.Combine(_workDir, "cifrado.zip");
        await PackAsync(src, zip, password: "clave-de-prueba");

        // Un byte del medio del archivo: cae en los datos cifrados de la entrada grande.
        var bytes = File.ReadAllBytes(zip);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(zip, bytes);

        var dest = Path.Combine(_workDir, "salida");
        var result = await _service.DecompressAsync(zip, dest, "clave-de-prueba", null, CancellationToken.None);

        // El código de autenticación AES delata la entrada alterada: se aparta, las demás salen.
        var damaged = Assert.Single(result.Damaged);
        Assert.Equal("caso/sub/datos.bin", damaged.Name);
        Assert.False(File.Exists(Path.Combine(dest, "caso", "sub", "datos.bin")));
        Assert.Equal("acta original", File.ReadAllText(Path.Combine(dest, "caso", "acta.txt")));
    }

    [Fact]
    public async Task Format7z_DamagedFile_IsSetAsideAndTheRestIsExtracted()
    {
        var src = MakeSource();
        var archive = Path.Combine(_workDir, "caso.7z");
        await _service.CompressAsync(new ZipOptions
        {
            SourcePaths = new List<string> { src },
            OutputPath = archive,
            CompressionLevel = 5,
            Format = ArchiveFormat.SevenZ
        }, null, CancellationToken.None);

        // El medio del archivo cae en los datos de la entrada grande (aleatoria, no comprime).
        var bytes = File.ReadAllBytes(archive);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(archive, bytes);

        var dest = Path.Combine(_workDir, "salida7z");
        var result = await _service.DecompressAsync(archive, dest, null, null, CancellationToken.None);

        var damaged = Assert.Single(result.Damaged);
        Assert.Equal("caso/sub/datos.bin", damaged.Name);
        Assert.False(File.Exists(Path.Combine(dest, "caso", "sub", "datos.bin")));
        Assert.Equal("acta original", File.ReadAllText(Path.Combine(dest, "caso", "acta.txt")));
    }

    [Fact]
    public async Task Zip_WrongPassword_StillAbortsEverything()
    {
        // Un fallo en la primera entrada cifrada es la contraseña, no daño: no se aparta nada, se aborta.
        var src = MakeSource();
        var zip = Path.Combine(_workDir, "cifrado.zip");
        await PackAsync(src, zip, password: "clave-de-prueba");

        var dest = Path.Combine(_workDir, "salida");
        await Assert.ThrowsAnyAsync<Exception>(
            () => _service.DecompressAsync(zip, dest, "otra-clave", null, CancellationToken.None));
        // Ni archivos ni la carpeta de destino, que la creó esta extracción.
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public async Task Zip_NameWithColon_DoesNotWriteAHiddenStream()
    {
        var zip = Path.Combine(_workDir, "flujo.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "acta.txt", "acta visible");
            Write(archive, "acta.txt:oculto", "esto iba a un flujo alternativo");
        }

        var dest = Path.Combine(_workDir, "salida");
        var result = await _service.DecompressAsync(zip, dest, null, null, CancellationToken.None);

        Assert.Equal(2, result.FilesExtracted);
        var renamed = Assert.Single(result.Renamed);
        Assert.Equal("acta.txt:oculto", renamed.OriginalName);
        Assert.Equal(RenameReason.Invalid, renamed.Reason);
        Assert.Equal("acta visible", File.ReadAllText(Path.Combine(dest, "acta.txt")));
        Assert.Equal("esto iba a un flujo alternativo", File.ReadAllText(Path.Combine(dest, renamed.SavedAs)));
        Assert.False(File.Exists(Path.Combine(dest, "acta.txt") + ":oculto"), "se escribió un flujo alternativo");
    }

    [Fact]
    public async Task Zip_DuplicateNames_BothVersionsAreKeptAndDocumented()
    {
        var zip = Path.Combine(_workDir, "duplicado.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "acta.txt", "version 1");
            Write(archive, "acta.txt", "version 2, agregada");
        }

        var dest = Path.Combine(_workDir, "salida");
        var result = await _service.DecompressAsync(zip, dest, null, null, CancellationToken.None);

        Assert.Equal(2, result.FilesExtracted);
        Assert.Equal("version 1", File.ReadAllText(Path.Combine(dest, "acta.txt")));
        var second = Assert.Single(result.Renamed);
        Assert.Equal(RenameReason.Repeated, second.Reason);
        Assert.Equal("version 2, agregada", File.ReadAllText(Path.Combine(dest, second.SavedAs)));
    }

    [Fact]
    public async Task Extract_IntoAFolderWithAnExistingFile_DoesNotOverwriteIt()
    {
        var dest = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "acta.txt"), "otra evidencia, de antes");

        var zip = Path.Combine(_workDir, "nuevo.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "acta.txt", "acta del archivo nuevo");
        }

        var result = await _service.DecompressAsync(zip, dest, null, null, CancellationToken.None);

        Assert.Equal("otra evidencia, de antes", File.ReadAllText(Path.Combine(dest, "acta.txt")));
        var renamed = Assert.Single(result.Renamed);
        Assert.Equal("acta.txt", renamed.OriginalName);
        Assert.Equal(RenameReason.AlreadyInDestination, renamed.Reason);
        Assert.Equal("acta del archivo nuevo", File.ReadAllText(Path.Combine(dest, renamed.SavedAs)));
    }

    [Fact]
    public async Task Extract_FailingIntoAFolderWithAnExistingFile_LeavesThatFileAlone()
    {
        // Antes: la entrada pisaba el archivo previo y, al fallar el CRC, la limpieza lo borraba.
        var dest = Path.Combine(_workDir, "salida");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "acta.txt"), "otra evidencia, de antes");

        var zip = Path.Combine(_workDir, "alterado.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("acta.txt", CompressionLevel.NoCompression);
            using var w = new StreamWriter(entry.Open());
            w.Write("contenido original del acta");
        }

        var bytes = File.ReadAllBytes(zip);
        bytes[bytes.AsSpan().IndexOf("contenido original"u8)] ^= 0x20;
        File.WriteAllBytes(zip, bytes);

        var result = await _service.DecompressAsync(zip, dest, null, null, CancellationToken.None);
        Assert.Single(result.Damaged);
        Assert.Equal("otra evidencia, de antes", File.ReadAllText(Path.Combine(dest, "acta.txt")));
        Assert.Single(Directory.GetFiles(dest));
    }

    [Fact]
    public async Task Zip_ParentReference_StillAborts()
    {
        var zip = Path.Combine(_workDir, "slip.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "../fuera.txt", "intento de salir");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DecompressAsync(zip, Path.Combine(_workDir, "salida"), null, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_workDir, "fuera.txt")));
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var w = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        w.Write(text);
    }

    // ---------- Salida del paquete ----------

    [Fact]
    public async Task Compress_OverAnExistingPackage_IsRefusedAndTheOldOneIsKept()
    {
        var src = MakeSource();
        var zip = Path.Combine(_workDir, "p.zip");
        await PackAsync(src, zip);
        var before = File.ReadAllBytes(zip);

        await Assert.ThrowsAsync<IOException>(() => PackAsync(src, zip));
        Assert.Equal(before, File.ReadAllBytes(zip));
    }

    [Fact]
    public async Task Compress_SplitWhereASingleOneExists_IsRefused()
    {
        // El escenario de la revisión: el caso.zip viejo le ganaba a los volúmenes nuevos al verificar.
        var src = MakeSource();
        var zip = Path.Combine(_workDir, "p.zip");
        await PackAsync(src, zip);

        await Assert.ThrowsAsync<IOException>(() => PackAsync(src, zip, split: 64 * 1024));
        Assert.Empty(SplitArchive.FindVolumeFiles(zip));
    }

    [Fact]
    public async Task Compress_WhereAStaleVolumeExists_IsRefused()
    {
        var src = MakeSource();
        var zip = Path.Combine(_workDir, "p.zip");
        File.WriteAllText(zip + ".004", "volumen sobrante de una corrida anterior");

        await Assert.ThrowsAsync<IOException>(() => PackAsync(src, zip, split: 64 * 1024));
        Assert.False(File.Exists(zip + ".001"));
    }

    [Fact]
    public async Task Compress_OutputInsideTheSource_IsRefused()
    {
        var src = MakeSource();
        var zip = Path.Combine(src, "caso.zip");

        await Assert.ThrowsAsync<ArgumentException>(() => PackAsync(src, zip));
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public void DefaultOutput_WithTrailingSeparator_GoesNextToTheSourceNotInside()
    {
        var src = MakeSource();
        var expected = Path.Combine(_workDir, "caso.zip");

        Assert.Equal(expected, PackageOutput.DefaultFor(src + Path.DirectorySeparatorChar, ".zip"));
        Assert.Equal(expected, PackageOutput.DefaultFor(src, ".zip"));
        Assert.Null(PackageOutput.DefaultFor(Path.GetPathRoot(_workDir)!, ".zip"));
    }

    [Fact]
    public void OutputThatIsAFolder_IsRecognized_InsteadOfAnExistingPackageWithNoName()
    {
        // Antes el aviso era "Ya existe un paquete o informe con ese nombre ()": el nombre de una carpeta con barra
        // final es vacío.
        var folder = Path.Combine(_workDir, "Salida_C1");
        Directory.CreateDirectory(folder);

        Assert.True(PackageOutput.IsFolder(folder));
        Assert.True(PackageOutput.IsFolder(folder + Path.DirectorySeparatorChar));
        Assert.True(PackageOutput.IsFolder(Path.Combine(_workDir, "todavia_no_existe") + Path.DirectorySeparatorChar));
        Assert.False(PackageOutput.IsFolder(Path.Combine(folder, "docs.zip")));
    }

    [Fact]
    public async Task SuggestFree_SkipsAnExistingPackage()
    {
        var src = MakeSource();
        var zip = Path.Combine(_workDir, "caso.zip");
        await PackAsync(src, zip);

        Assert.Equal(Path.Combine(_workDir, "caso (2).zip"), PackageOutput.SuggestFree(zip));
    }

    [Fact]
    public void RelocateSuggestion_ForZipNumberInAnotherFolder_IsRecalculated()
    {
        var desired = Path.Combine(_workDir, "caso.7z");
        var suggested = Path.Combine(_workDir, "caso (2).7z");
        var other = Path.Combine(_workDir, "otra");
        Directory.CreateDirectory(other);

        Assert.Equal(Path.Combine(other, "caso.7z"),
            PackageOutput.RelocateSuggestion(Path.Combine(other, "caso (2).7z"), suggested, desired));

        // En la carpeta nueva también choca: el número se vuelve a calcular ahí, no se arrastra.
        File.WriteAllText(Path.Combine(other, "caso.7z"), "x");
        Assert.Equal(Path.Combine(other, "caso (2).7z"),
            PackageOutput.RelocateSuggestion(Path.Combine(other, "caso (2).7z"), suggested, desired));
    }

    [Fact]
    public void RelocateSuggestion_RespectsTheOperatorsNameAndTheSameFolder()
    {
        var desired = Path.Combine(_workDir, "caso.zip");
        var suggested = Path.Combine(_workDir, "caso (2).zip");
        var other = Path.Combine(_workDir, "otra", "caso (2).zip");
        var typed = Path.Combine(_workDir, "otra", "pericia 14.zip");

        Assert.Equal(typed, PackageOutput.RelocateSuggestion(typed, suggested, desired));
        Assert.Equal(suggested, PackageOutput.RelocateSuggestion(suggested, suggested, desired));
        // Sin "(n)" de ForZip no hay nada que corregir.
        var plain = Path.Combine(_workDir, "otra", "caso.zip");
        Assert.Equal(plain, PackageOutput.RelocateSuggestion(plain, desired, desired));
        Assert.Equal(other, PackageOutput.RelocateSuggestion(other, null, null));
    }

    [Fact]
    public void RelocateSuggestion_UntouchedNameThatCollidesInTheNewFolder_GetsItsNumberThere()
    {
        // El diálogo mostraba "caso.zip" (sin número) y el operador fue a una carpeta donde ya hay uno: antes
        // Windows preguntaba si reemplazarlo y ForZip frenaba recién al empaquetar.
        var shown = Path.Combine(_workDir, "caso.zip");
        var other = Path.Combine(_workDir, "ocupada");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "caso.zip"), "x");

        Assert.Equal(Path.Combine(other, "caso (2).zip"),
            PackageOutput.RelocateSuggestion(Path.Combine(other, "caso.zip"), shown, shown));
        // Un nombre escrito por el operador que choca se respeta: lo frena el empaquetado, no se renombra solo.
        var typed = Path.Combine(other, "caso.zip");
        Assert.Equal(typed, PackageOutput.RelocateSuggestion(typed, Path.Combine(_workDir, "pericia.zip"), Path.Combine(_workDir, "pericia.zip")));
    }

    [Fact]
    public void FreeFileName_AddsANumberOnlyWhenTheNameIsTaken()
    {
        Assert.Equal("caso.zip.informe.html", PackageOutput.FreeFileName(_workDir, "caso.zip.informe.html"));
        File.WriteAllText(Path.Combine(_workDir, "caso.zip.informe.html"), "x");
        Assert.Equal("caso.zip.informe (2).html", PackageOutput.FreeFileName(_workDir, "caso.zip.informe.html"));
        Assert.Equal("caso.zip.informe.html", PackageOutput.FreeFileName(null, "caso.zip.informe.html"));
    }

    [Fact]
    public void RetargetFileSuggestion_UntouchedNameWithAnotherType_IsRecalculatedForThatType()
    {
        // Había caso_14.zip.informe.html y (2).html; el diálogo propuso (3).html y al pasar a
        // Texto Windows dejó "(3).txt" sin que existiera ningún .txt.
        File.WriteAllText(Path.Combine(_workDir, "caso_14.zip.informe.html"), "x");
        File.WriteAllText(Path.Combine(_workDir, "caso_14.zip.informe (2).html"), "x");
        var desired = Path.Combine(_workDir, "caso_14.zip.informe.html");
        var shown = Path.Combine(_workDir, "caso_14.zip.informe (3).html");

        Assert.Equal(Path.Combine(_workDir, "caso_14.zip.informe.txt"),
            PackageOutput.RetargetFileSuggestion(Path.Combine(_workDir, "caso_14.zip.informe (3).txt"), shown, desired));

        // Con un .txt ya guardado, el siguiente recibe su propio "(2)".
        File.WriteAllText(Path.Combine(_workDir, "caso_14.zip.informe.txt"), "x");
        Assert.Equal(Path.Combine(_workDir, "caso_14.zip.informe (2).txt"),
            PackageOutput.RetargetFileSuggestion(Path.Combine(_workDir, "caso_14.zip.informe (3).txt"), shown, desired));
    }

    [Fact]
    public void RetargetFileSuggestion_RespectsTypedNamesAndUnchangedChoices()
    {
        var desired = Path.Combine(_workDir, "caso.zip.informe.html");
        var shown = Path.Combine(_workDir, "caso.zip.informe (2).html");

        // Mismo nombre, tipo y carpeta: tal cual.
        Assert.Equal(shown, PackageOutput.RetargetFileSuggestion(shown, shown, desired));
        // Un nombre escrito por el operador no se toca, aunque cambie el tipo.
        var typed = Path.Combine(_workDir, "pericia (2).txt");
        Assert.Equal(typed, PackageOutput.RetargetFileSuggestion(typed, shown, desired));
        // Otra carpeta sin tocar el nombre: el "(2)" no viaja.
        var other = Path.Combine(_workDir, "otra");
        Directory.CreateDirectory(other);
        Assert.Equal(Path.Combine(other, "caso.zip.informe.html"),
            PackageOutput.RetargetFileSuggestion(Path.Combine(other, "caso.zip.informe (2).html"), shown, desired));
        Assert.Equal(typed, PackageOutput.RetargetFileSuggestion(typed, null, null));
    }

    [Fact]
    public void FreeFolder_NumbersOnlyAnOccupiedFolder()
    {
        var desired = Path.Combine(_workDir, "evidencia");
        Assert.Equal(desired, PackageOutput.FreeFolder(desired));

        // Vacía sirve tal cual: no hay nada con qué mezclarse.
        Directory.CreateDirectory(desired);
        Assert.Equal(desired, PackageOutput.FreeFolder(desired));

        // Extraer de nuevo va a "evidencia (2)", no a archivos "~2" mezclados.
        File.WriteAllText(Path.Combine(desired, "acta.pdf"), "x");
        Assert.Equal(desired + " (2)", PackageOutput.FreeFolder(desired));
        Directory.CreateDirectory(desired + " (2)");
        File.WriteAllText(Path.Combine(desired + " (2)", "acta.pdf"), "x");
        Assert.Equal(desired + " (3)", PackageOutput.FreeFolder(desired + Path.DirectorySeparatorChar));

        // Un archivo con ese nombre también la ocupa.
        var file = Path.Combine(_workDir, "caso");
        File.WriteAllText(file, "x");
        Assert.Equal(file + " (2)", PackageOutput.FreeFolder(file));
    }

    [Fact]
    public void FreeFolder_SkipsFoldersAlreadyClaimedInTheSameSelection()
    {
        // caso.zip y caso.7z elegidos juntos: ninguna carpeta existe todavía, pero no pueden ir a la misma.
        var claimed = new List<string>();
        var desired = Path.Combine(_workDir, "caso");
        Assert.Equal(desired, PackageOutput.FreeFolder(desired, claimed));
        Assert.Equal(desired + " (2)", PackageOutput.FreeFolder(desired, claimed));
        Assert.Equal(2, claimed.Count);
    }

    [Fact]
    public void FindSourceContaining_DriveRoot_ContainsEverythingOnTheDrive()
    {
        // Con la raíz de una unidad como origen, "D:\" + "\" no coincidía con
        // nada y una salida en esa unidad no se reconocía como de adentro.
        var drive = Path.GetPathRoot(Path.GetFullPath(_workDir))!;
        var inside = Path.Combine(_workDir, "informe.txt");

        Assert.Equal(drive, PackageOutput.FindSourceContaining(new[] { drive }, inside));
        Assert.Equal(_workDir, PackageOutput.FindSourceContaining(new[] { _workDir }, inside));
        Assert.Null(PackageOutput.FindSourceContaining(new[] { Path.Combine(_workDir, "caso_14") }, inside));
    }

    // ---------- Enlaces dentro del origen ----------

    private static void MakeJunction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "no se pudo crear la junction de prueba");
    }

    [Fact]
    public async Task Compress_JunctionsInsideTheSource_AreNotFollowedAndAreDocumented()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var src = MakeSource();
        var outside = Path.Combine(_workDir, "afuera");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "ajeno.txt"), "esto no es evidencia");
        MakeJunction(Path.Combine(src, "enlace_afuera"), outside);
        MakeJunction(Path.Combine(src, "sub", "bucle"), src);

        var zip = Path.Combine(_workDir, "j.zip");
        var result = await PackAsync(src, zip);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("caso/acta.txt", names);
        Assert.DoesNotContain(names, n => n.Contains("ajeno", StringComparison.Ordinal) || n.Contains("bucle/", StringComparison.Ordinal));

        var links = result.OmittedFiles.Where(o => o.Reason == OmissionReason.LinkNotFollowed).ToList();
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.EntryName == "caso/enlace_afuera/" && l.LinkTarget!.EndsWith("afuera", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Volúmenes ----------

    [Fact]
    public async Task MissingMiddleVolume_IsNamed()
    {
        var src = MakeSource();
        var zip = Path.Combine(_workDir, "v.zip");
        var result = await PackAsync(src, zip, split: 64 * 1024);
        Assert.True(result.Volumes.Count >= 3);
        File.Delete(zip + ".002");

        var ex = Assert.Throws<FileNotFoundException>(() => SplitArchive.ResolveSegments(zip + ".001"));
        Assert.Contains("v.zip.002", ex.Message, StringComparison.Ordinal);
    }

    // ---------- Carpetas que Windows no deja recorrer ----------

    /// <summary>
    /// Niega listar la carpeta al usuario actual, como System Volume Information en la raíz de una unidad. Devuelve
    /// la acción que lo deshace (sin deshacerlo no se puede borrar la carpeta de trabajo).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Action DenyListing(string folder)
    {
        var info = new DirectoryInfo(folder);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var rule = new System.Security.AccessControl.FileSystemAccessRule(user,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        return () =>
        {
            var restore = info.GetAccessControl();
            restore.RemoveAccessRule(rule);
            info.SetAccessControl(restore);
        };
    }

    [Fact]
    public async Task Compress_UnreadableFolderInsideTheSource_IsDocumentedAndTheRestIsPackaged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Empaquetar la raíz de D:\ cortaba todo con "Access to the path
        // 'D:\System Volume Information' is denied". Ahora la carpeta queda asentada y el resto se empaqueta.
        var src = MakeSource();
        var closed = Path.Combine(src, "cerrada");
        Directory.CreateDirectory(closed);
        File.WriteAllText(Path.Combine(closed, "oculto.txt"), "no se puede listar");
        var undo = DenyListing(closed);
        try
        {
            var zip = Path.Combine(_workDir, "u.zip");
            var result = await PackAsync(src, zip);

            using (var archive = ZipFile.OpenRead(zip))
            {
                var names = archive.Entries.Select(e => e.FullName).ToList();
                Assert.Contains("caso/acta.txt", names);
                Assert.Contains("caso/sub/datos.bin", names);
                Assert.DoesNotContain(names, n => n.Contains("oculto", StringComparison.Ordinal));
            }

            var folder = Assert.Single(result.OmittedFiles, o => o.Reason == OmissionReason.FolderUnreadable);
            Assert.Equal("caso/cerrada/", folder.EntryName);
            Assert.Equal(closed, folder.SourcePath);
            Assert.Equal(5, folder.WindowsErrorCode); // ERROR_ACCESS_DENIED

            // El inventario de hashes (Hash Batch, hash rápido, forzip hash) hace lo mismo.
            var skipped = new List<OmittedFile>();
            var items = HashInventory.Expand(new[] { src }, skipped);
            Assert.Equal(2, items.Count);
            Assert.Contains(skipped, o => o.Reason == OmissionReason.FolderUnreadable && o.EntryName == "caso/cerrada/");
        }
        finally
        {
            undo();
        }
    }

    [Fact]
    public void EvidenceTree_WithoutAListForThem_UnreadableFolderStillThrows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Quien no puede documentar la omisión no la oculta: sin lista, la excepción de Windows sigue saliendo.
        var src = MakeSource();
        var closed = Path.Combine(src, "cerrada");
        Directory.CreateDirectory(closed);
        var undo = DenyListing(closed);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => EvidenceTree.EnumerateFiles(src).ToList());
        }
        finally
        {
            undo();
        }
    }
}
