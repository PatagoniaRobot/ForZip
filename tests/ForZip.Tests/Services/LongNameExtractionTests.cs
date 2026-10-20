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

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ForZip.Core.Models;
using ForZip.Core.Services;
using PrZip;
using Xunit;

namespace ForZip.Tests.Services;

/// <summary>
/// Un nombre de más de 255 caracteres (un reporte de Autopsy con tags, por ejemplo) no entra en NTFS. Antes la
/// extracción entera se cortaba ahí y se borraba lo ya extraído; ahora se extrae todo y ese nombre, acortado,
/// queda entre los renombrados con su nombre original completo.
/// </summary>
public class LongNameExtractionTests : IDisposable
{
    private static readonly string LongName = "Reports/tags/" + new string('x', 300) + ".html";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"forzip_largo_{Guid.NewGuid():N}");
    private readonly ZipService _zip = new(new HashService());

    public LongNameExtractionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly string[] Entries = ["antes.txt", LongName, "despues.txt"];

    private static byte[] Content(string name) => Encoding.UTF8.GetBytes("contenido de " + name[^Math.Min(12, name.Length)..]);

    private string MakeZip()
    {
        string path = Path.Combine(_dir, "reporte.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in Entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(Content(name));
        }

        return path;
    }

    private string Make7z()
    {
        string path = Path.Combine(_dir, "reporte.7z");
        using var writer = Writer7z.Create(path);
        foreach (string name in Entries)
        {
            writer.Add(new WriterEntry7z { Name = name }, new MemoryStream(Content(name)));
        }

        writer.Finish();
        return path;
    }

    private string MakeTar()
    {
        string path = Path.Combine(_dir, "reporte.tar");
        using var fs = File.Create(path);
        using var tar = new TarWriter(fs, TarEntryFormat.Pax);
        foreach (string name in Entries)
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Content(name)) });
        }

        return path;
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("tar")]
    public async Task NameLongerThanNtfsAllows_EverythingIsExtracted_AndTheNameIsDocumented(string format)
    {
        string archive = format switch { "zip" => MakeZip(), "7z" => Make7z(), _ => MakeTar() };
        string output = Path.Combine(_dir, "salida");

        var result = await _zip.DecompressAsync(archive, output, null, null, CancellationToken.None);

        Assert.Equal(3, result.FilesExtracted);
        Assert.True(result.IsComplete);
        var renamed = Assert.Single(result.Renamed);
        Assert.Equal(LongName, renamed.OriginalName);
        Assert.Equal(RenameReason.TooLong, renamed.Reason);
        Assert.EndsWith(".html", renamed.SavedAs, StringComparison.Ordinal);

        string saved = Path.Combine(output, renamed.SavedAs.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Path.GetFileName(saved).Length <= 255);
        Assert.Equal(Content(LongName), File.ReadAllBytes(saved));
        Assert.True(File.Exists(Path.Combine(output, "antes.txt")));
        Assert.True(File.Exists(Path.Combine(output, "despues.txt")));
    }

    [Fact]
    public async Task PathLongerThanExplorerOpens_IsExtracted_AndListed()
    {
        // Cada carpeta entra en NTFS, pero la ruta completa pasa de 259: el Explorador no la abre.
        string deep = string.Join("/", Enumerable.Range(1, 4).Select(i => $"Tag_{i}_" + new string('t', 70))) + "/reporte.html";
        string archive = Path.Combine(_dir, "profundo.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "corto.txt", deep })
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(Content(name));
            }
        }

        string output = Path.Combine(_dir, "salida_profunda");
        var result = await _zip.DecompressAsync(archive, output, null, null, CancellationToken.None);

        Assert.Equal(2, result.FilesExtracted);
        Assert.True(result.IsComplete);
        Assert.Equal(new[] { deep }, result.LongPaths);
        Assert.True(Path.Combine(output, deep).Length > ExtractionResult.ExplorerMaxPath);
    }
}

