using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ForZip.Core.Services;
using ICSharpCode.SharpZipLib.BZip2;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Evidencia que llega en TAR, TAR.GZ, BZ2 o ISO: se extrae con las mismas garantías que un ZIP (nada
/// fuera del destino, nada a medias si falla) y lo que no sale tal cual queda documentado en el resultado.
/// </summary>
public class OtherFormatsExtractionTests : IDisposable
{
    private readonly string _workDir;
    private readonly ZipService _service = new(new HashService());

    public OtherFormatsExtractionTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_formatos_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void AddFile(TarWriter tar, string name, string content)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
        };
        tar.WriteEntry(entry);
    }

    /// <summary>Un .tar.gz con lo que suele traer la evidencia de terceros, incluido lo peligroso.</summary>
    private string MakeHostileTarGz()
    {
        string path = Path.Combine(_workDir, "evidencia.tar.gz");
        using (var fs = File.Create(path))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "caso/"));
            AddFile(tar, "caso/acta.txt", "acta original");
            AddFile(tar, "caso/doble.txt", "primera versión");
            AddFile(tar, "caso/doble.txt", "segunda versión, agregada después");
            AddFile(tar, "/etc/passwd_falso", "ruta absoluta");
            AddFile(tar, "../fuera.txt", "intento de salir del destino");
            AddFile(tar, "caso/CON.txt", "nombre reservado en Windows");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "caso/enlace") { LinkName = "acta.txt" });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Fifo, "caso/tuberia"));
        }

        return path;
    }

    [Fact]
    public async Task HostileTarGz_StaysInsideTheDestination_AndEverythingIsDocumented()
    {
        string archive = MakeHostileTarGz();
        string dest = Path.Combine(_workDir, "salida");

        var result = await _service.DecompressAsync(archive, dest, null, null, CancellationToken.None);

        Assert.Equal("tar.gz", result.Format);
        Assert.Equal(6, result.FilesExtracted);
        Assert.False(File.Exists(Path.Combine(_workDir, "fuera.txt")), "se escribió fuera del destino");

        // Todo lo escrito está dentro del destino.
        var written = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).ToList();
        Assert.All(written, p => Assert.StartsWith(Path.GetFullPath(dest), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase));

        // Las dos versiones del mismo nombre se conservan.
        Assert.Equal("primera versión", File.ReadAllText(Path.Combine(dest, "caso", "doble.txt")));
        Assert.Equal("segunda versión, agregada después", File.ReadAllText(Path.Combine(dest, "caso", "doble~2.txt")));

        // Lo renombrado queda con su nombre original.
        var renamed = result.Renamed.Select(r => r.OriginalName).ToList();
        Assert.Contains("/etc/passwd_falso", renamed);
        Assert.Contains("../fuera.txt", renamed);
        Assert.Contains("caso/CON.txt", renamed);
        Assert.Contains("caso/doble.txt", renamed);

        // Enlace y FIFO no se crean, se documentan.
        var link = Assert.Single(result.Skipped, s => s.Name == "caso/enlace");
        Assert.Equal("SymbolicLink", link.Kind);
        Assert.Equal("acta.txt", link.LinkTarget);
        Assert.Contains(result.Skipped, s => s.Name == "caso/tuberia" && s.Kind == "Other");
        Assert.False(File.Exists(Path.Combine(dest, "caso", "enlace")));
    }

    [Fact]
    public async Task TarGz_WithAlteredCrc_FailsAndLeavesNothing()
    {
        string archive = MakeHostileTarGz();
        byte[] data = File.ReadAllBytes(archive);
        data[^8] ^= 0x01; // CRC32 del GZip: los datos comprimidos quedan intactos
        File.WriteAllBytes(archive, data);
        string dest = Path.Combine(_workDir, "salida_crc");

        await Assert.ThrowsAnyAsync<Exception>(() => _service.DecompressAsync(archive, dest, null, null, CancellationToken.None));
        Assert.Empty(Directory.Exists(dest) ? Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task SingleBz2_IsExtracted_AndRecognizedByItsSignature()
    {
        // Texto muy repetitivo a propósito: SharpZipLib escribe el bloque "randomised" (como bzip2 0.9.0).
        byte[] content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("declaración del testigo; ", 2000)));
        string archive = Path.Combine(_workDir, "declaracion.txt.bz2");
        // Compresor de otra implementación (SharpZipLib): el códec BZip2 del motor es interno.
        using (var fs = File.Create(archive))
        using (var bz = new BZip2OutputStream(fs, 9))
        {
            bz.Write(content);
        }

        // Con otra extensión también: manda la firma.
        string disguised = Path.Combine(_workDir, "adjunto.dat");
        File.Copy(archive, disguised);
        string dest = Path.Combine(_workDir, "salida_bz2");

        var result = await _service.DecompressAsync(disguised, dest, null, null, CancellationToken.None);
        Assert.Equal("bz2", result.Format);
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(dest, "adjunto.dat.out")));
    }

    [Fact]
    public async Task Iso_FromBsdtar_IsExtractedWithItsTree()
    {
        string bsdtar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
        if (!File.Exists(bsdtar))
        {
            return; // sin bsdtar (Windows 10 1803+ lo trae) no hay con qué fabricar la imagen
        }

        string src = Path.Combine(_workDir, "iso_src");
        Directory.CreateDirectory(Path.Combine(src, "fotos", "2026"));
        File.WriteAllText(Path.Combine(src, "informe pericial.txt"), "contenido del informe");
        File.WriteAllBytes(Path.Combine(src, "fotos", "2026", "imagen.bin"), Enumerable.Range(0, 50_000).Select(i => (byte)i).ToArray());
        string iso = Path.Combine(_workDir, "disco.iso");
        using (var proc = Process.Start(new ProcessStartInfo(bsdtar, ["--format", "iso9660", "-cf", iso, "."])
               { WorkingDirectory = src, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })!)
        {
            proc.StandardError.ReadToEnd();
            proc.WaitForExit(60_000);
            Assert.Equal(0, proc.ExitCode);
        }

        string dest = Path.Combine(_workDir, "salida_iso");
        var result = await _service.DecompressAsync(iso, dest, null, null, CancellationToken.None);

        Assert.StartsWith("iso", result.Format, StringComparison.Ordinal);
        Assert.Equal("contenido del informe", File.ReadAllText(Path.Combine(dest, "informe pericial.txt")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(src, "fotos", "2026", "imagen.bin")),
                     File.ReadAllBytes(Path.Combine(dest, "fotos", "2026", "imagen.bin")));
    }

    private static string RarFixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rar", name);

    [Fact]
    public async Task Rar_ComentarioYFlujosNtfs_QuedanDocumentados()
    {
        // RAR4 de Rar.exe 6.24 con -z (comentario en Unicode) y -os (flujos NTFS).
        string dest = Path.Combine(_workDir, "salida_rar4");
        var result = await _service.DecompressAsync(RarFixture("cmt_v4.rar"), dest, null, null, CancellationToken.None);

        Assert.Equal("rar", result.Format);
        Assert.Equal(2, result.FilesExtracted);
        Assert.Equal(new ForZip.Core.Models.CommentInfo("Causa 1234/2026 - perito: Señal ñandú\r\n", null), result.ArchiveComment);

        var zone = Assert.Single(result.Skipped, s => s.Name == "datos/foto.jpg:Zone.Identifier");
        Assert.Equal("AlternateStream", zone.Kind);
        Assert.Equal(68, zone.Size);
        Assert.Contains(result.Skipped, s => s.Name == "acta.txt:oculto" && s.Size == 780);

        // Los flujos no se escriben: ni como archivo aparte ni como flujo del archivo extraído.
        Assert.Single(Directory.GetFiles(dest, "*", SearchOption.AllDirectories), f => f.EndsWith("acta.txt", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(dest, "datos", "foto.jpg:Zone.Identifier")));
    }

    [Fact]
    public async Task Rar20_ComentarioDeArchivo_QuedaDocumentado()
    {
        // RAR 2.90: comentario comprimido en la cabecera principal y comentario propio de acta.txt (cf).
        string dest = Path.Combine(_workDir, "salida_rar20");
        var result = await _service.DecompressAsync(RarFixture("cmt_v20.rar"), dest, null, null, CancellationToken.None);

        Assert.Equal("Causa 1234/2026 - perito: Senal\r\n", result.ArchiveComment?.Text);
        var acta = Assert.Single(result.EntryComments);
        Assert.Equal("acta.txt", acta.Name);
        Assert.Equal("Comentario del archivo acta\n", acta.Comment.Text);
        Assert.Equal(2, result.FilesExtracted);
    }

    // Extraer acepta una parte .r00/.r01 (esquema de nombres viejo). Desde cualquier parte
    // se extrae el juego entero, igual que desde el .rar.
    [Theory]
    [InlineData("vol_v4_viejo.rar")]
    [InlineData("vol_v4_viejo.r01")]
    public async Task RarVolumenesEsquemaViejo_SeExtraenDesdeCualquierParte(string part)
    {
        string dest = Path.Combine(_workDir, "salida_viejo_" + Path.GetExtension(part).TrimStart('.'));
        var result = await _service.DecompressAsync(RarFixture(part), dest, null, null, CancellationToken.None);

        Assert.Equal("rar", result.Format);
        Assert.True(result.IsComplete);
        Assert.True(result.FilesExtracted > 0);
    }

    // Un autoextraíble se reconoce por la firma RAR adentro del ejecutable (no se ejecuta), y un .exe
    // cualquiera no.
    [Fact]
    public async Task RarAutoextraible_SeReconocePorElContenido()
    {
        Assert.Equal("rar", PrZip.Formats.ArchiveReader.DetectFormat(RarFixture("m0_v5_sfx.exe")));

        var plainExe = Path.Combine(_workDir, "programa.exe");
        var mz = new byte[8192];
        mz[0] = (byte)'M';
        mz[1] = (byte)'Z';
        await File.WriteAllBytesAsync(plainExe, mz);
        Assert.Null(PrZip.Formats.ArchiveReader.DetectFormat(plainExe));

        string dest = Path.Combine(_workDir, "salida_sfx");
        var result = await _service.DecompressAsync(RarFixture("m0_v5_sfx.exe"), dest, null, null, CancellationToken.None);
        Assert.Equal("rar", result.Format);
        Assert.True(result.FilesExtracted > 0);
    }

    // Revisión de lectores 2026-10-13, hallazgo 8: una bomba de descompresión llenaba el disco de destino.

    [Fact]
    public void SpaceGuard_StopsWhenLessThanTheMinimumIsLeft()
    {
        var guard = new ExtractionSpaceGuard(_workDir, () => 500L << 20);
        guard.Account(ExtractionSpaceGuard.CheckEvery - 1); // todavía no consulta
        var ex = Assert.Throws<ExtractionSpaceException>(() => guard.Account(1));
        Assert.Contains("500 MB libres", ex.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<IOException>(ex);
    }

    [Fact]
    public void SpaceGuard_GoesOnWithEnoughSpaceOrWhenItCannotTell()
    {
        var plenty = new ExtractionSpaceGuard(_workDir, () => 5L << 30);
        var unknown = new ExtractionSpaceGuard(_workDir, () => null);
        for (int i = 0; i < 4; i++)
        {
            plenty.Account(ExtractionSpaceGuard.CheckEvery);
            unknown.Account(ExtractionSpaceGuard.CheckEvery);
        }
    }
}
