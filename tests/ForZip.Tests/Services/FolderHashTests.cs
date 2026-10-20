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
using System.Security.Cryptography;
using System.Text;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>Huella de carpeta: la de ForZip (con nombres) y la "SHA256 for data" de 7-Zip.</summary>
public class FolderHashTests : IDisposable
{
    private readonly string _workDir;

    public FolderHashTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"forzip_folder_{Guid.NewGuid():N}");
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

    private static string Sha(string text) => Sha(Encoding.UTF8.GetBytes(text));
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public void SevenZipDataSum_MatchesValuesMeasuredAgainst7Zip()
    {
        // Medidos con 7z h -scrcSHA256 (7-Zip 24.08) sobre carpetas con esos contenidos, 2026-10-19.
        Assert.Equal("d4953cf731629d8a991ae47b9ec527d6c77d57989660369c504bda8a142378da-00000000",
            FolderHash.SevenZipDataSum(new[] { Sha("hola"), Sha("chau") }));
        Assert.Equal("b221d9dbb083a7f33428d7c2a3c3198ae925614d70210e28716ccaa7cd4ddb79-00000000",
            FolderHash.SevenZipDataSum(new[] { Sha("hola") }));
    }

    [Fact]
    public void SevenZipDataSum_CountsTheOverflows()
    {
        var max = new string('f', 64);
        // (2^256 - 1) * 2 = 2^256 + (2^256 - 2): un desborde, y el resto en little-endian empieza con fe.
        Assert.Equal("fe" + new string('f', 62) + "-00000001", FolderHash.SevenZipDataSum(new[] { max, max }));
    }

    [Fact]
    public void ForZipDigest_IsTheSha256OfTheSortedSha256sumListing()
    {
        var files = new[] { ("sub/b.txt", Sha("chau")), ("a.txt", Sha("hola")), ("Z.txt", Sha("z")) };
        // Por bytes UTF-8: "Z" (0x5A) va antes que "a" (0x61), y "a.txt" antes que "sub/b.txt".
        var listing = $"{Sha("z")}  Z.txt\n{Sha("hola")}  a.txt\n{Sha("chau")}  sub/b.txt\n";

        Assert.Equal(Sha(listing), FolderHash.ForZipDigest(files));
        Assert.Equal(FolderHash.ForZipDigest(files), FolderHash.ForZipDigest(files.Reverse()));
    }

    [Fact]
    public void ForZipDigest_MatchesTheExampleInTheSpecification()
    {
        // Documentation/Huella_de_Carpeta_v1.md, sección 2.
        Assert.Equal("398320c92b527d0e43a1633bb80fae02320c298fd91ecbd1eb33e52326b0b12b",
            FolderHash.ForZipDigest(new[] { ("a.txt", Sha("hola")), ("sub/b.txt", Sha("chau")) }));
    }

    [Fact]
    public void ForZipDigest_ChangesWithANameOrAMove_TheSevenZipValueDoesNot()
    {
        var original = new[] { ("a.txt", Sha("hola")), ("b.txt", Sha("chau")) };
        var renamed = new[] { ("a2.txt", Sha("hola")), ("b.txt", Sha("chau")) };
        var moved = new[] { ("sub/a.txt", Sha("hola")), ("b.txt", Sha("chau")) };

        Assert.NotEqual(FolderHash.ForZipDigest(original), FolderHash.ForZipDigest(renamed));
        Assert.NotEqual(FolderHash.ForZipDigest(original), FolderHash.ForZipDigest(moved));
        Assert.Equal(FolderHash.SevenZipDataSum(original.Select(f => f.Item2)),
                     FolderHash.SevenZipDataSum(renamed.Select(f => f.Item2)));
    }

    [Fact]
    public void Compute_OneDigestPerFolder_NoneWhenAFileCouldNotBeRead()
    {
        var caso = Path.Combine(_workDir, "caso");
        Directory.CreateDirectory(Path.Combine(caso, "sub"));
        var a = Path.Combine(caso, "a.txt");
        var b = Path.Combine(caso, "sub", "b.txt");
        var loose = Path.Combine(_workDir, "suelto.txt");
        File.WriteAllText(a, "hola");
        File.WriteAllText(b, "chau");
        File.WriteAllText(loose, "x");

        var ok = FolderHash.Compute(new[] { caso, loose },
            new[] { (a, 4L, (string?)Sha("hola")), (b, 4L, (string?)Sha("chau")), (loose, 1L, (string?)Sha("x")) },
            Array.Empty<OmittedFile>());

        var digest = Assert.Single(ok); // el archivo suelto no tiene huella de carpeta
        Assert.Equal(2, digest.FileCount);
        Assert.Equal(8, digest.TotalSize);
        Assert.Equal(FolderHash.ForZipDigest(new[] { ("a.txt", Sha("hola")), ("sub/b.txt", Sha("chau")) }), digest.ForZipSha256);
        Assert.Equal("d4953cf731629d8a991ae47b9ec527d6c77d57989660369c504bda8a142378da-00000000", digest.SevenZipDataSha256);

        var broken = Assert.Single(FolderHash.Compute(new[] { caso },
            new[] { (a, 4L, (string?)Sha("hola")), (b, 4L, (string?)null) }, Array.Empty<OmittedFile>()));
        Assert.False(broken.IsComplete);
        Assert.Equal(1, broken.UnreadableCount);
        Assert.Null(broken.ForZipSha256);
        Assert.Null(broken.SevenZipDataSha256);
    }

    [Sealing.PythonExtractorFact]
    public async Task ReferenceScript_GivesTheSameTwoValues()
    {
        // El script publicado (Tools/huella_carpeta.py) es lo que permite recalcular la huella sin ForZip: tiene que dar
        // exactamente lo mismo, también con acentos, mayúsculas y subcarpetas, que es donde un orden distinto se nota.
        var caso = Path.Combine(_workDir, "py");
        Directory.CreateDirectory(Path.Combine(caso, "Sub", "ñ"));
        var files = new Dictionary<string, byte[]>
        {
            ["acta.pdf"] = RandomNumberGenerator.GetBytes(5_000),
            ["Zeta.txt"] = Encoding.UTF8.GetBytes("z"),
            ["Sub/ñ/declaración.txt"] = Encoding.UTF8.GetBytes("año"),
            ["Sub/a b.txt"] = [],
            ["éxito.txt"] = Encoding.UTF8.GetBytes("ok")
        };
        foreach (var (rel, content) in files)
        {
            File.WriteAllBytes(Path.Combine(caso, rel.Replace('/', Path.DirectorySeparatorChar)), content);
        }

        var psi = new ProcessStartInfo(Sealing.PythonExtractorFactAttribute.Path!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Tools", "huella_carpeta.py"));
        psi.ArgumentList.Add(caso);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);

        var expectedForZip = FolderHash.ForZipDigest(files.Select(f => (f.Key, Sha(f.Value))));
        var expected7z = FolderHash.SevenZipDataSum(files.Values.Select(Sha));
        Assert.Contains($"Huella ForZip : {expectedForZip}", output);
        Assert.Contains($"7-Zip (datos) : {expected7z}", output);
    }

    [Requires7zFact]
    public async Task SevenZipDataSum_EqualsWhat7ZipPrintsForARealFolder()
    {
        var caso = Path.Combine(_workDir, "real");
        Directory.CreateDirectory(Path.Combine(caso, "sub", "más"));
        var files = new Dictionary<string, byte[]>
        {
            ["acta.pdf"] = RandomNumberGenerator.GetBytes(70_000),
            ["sub/foto.jpg"] = RandomNumberGenerator.GetBytes(12_345),
            ["sub/más/vacío.txt"] = [],
            ["ñandú.txt"] = Encoding.UTF8.GetBytes("año 2026")
        };
        foreach (var (rel, content) in files)
        {
            File.WriteAllBytes(Path.Combine(caso, rel.Replace('/', Path.DirectorySeparatorChar)), content);
        }

        var psi = new ProcessStartInfo(Requires7zFactAttribute.Find7z()!)
        {
            ArgumentList = { "h", "-scrcSHA256", caso },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        var line = output.Split('\n').Single(l => l.StartsWith("SHA256 for data:", StringComparison.Ordinal));
        var expected = line["SHA256 for data:".Length..].Trim();
        Assert.Equal(expected, FolderHash.SevenZipDataSum(files.Values.Select(Sha)));
    }
}
