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

using System.Security.Cryptography;
using ForZip.Core.Models;
using ForZip.Core.Services;
using Xunit;

namespace ForZip.Tests.Services;

public class HashServiceTests
{
    private static string SampleFilePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "sample.txt");

    [Fact]
    public async Task ComputeHashesAsync_KnownFile_ReturnsCorrectSha256()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        // Verificación cruzada: hashear los bytes del archivo con la API directa de .NET
        var fileBytes = await File.ReadAllBytesAsync(SampleFilePath);
        var expected = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
        Assert.Equal(expected, result.Hashes[HashAlgorithmType.SHA256]);
    }

    [Fact]
    public async Task ComputeHashesAsync_KnownFile_ReturnsCorrectMd5()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.MD5 };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        var fileBytes = await File.ReadAllBytesAsync(SampleFilePath);
        var expected = Convert.ToHexString(MD5.HashData(fileBytes)).ToLowerInvariant();
        Assert.Equal(expected, result.Hashes[HashAlgorithmType.MD5]);
    }

    [Fact]
    public async Task ComputeHashesAsync_AllAlgorithms_ReturnsAllFour()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType>
        {
            HashAlgorithmType.MD5,
            HashAlgorithmType.SHA1,
            HashAlgorithmType.SHA256,
            HashAlgorithmType.SHA512
        };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        Assert.Equal(4, result.Hashes.Count);
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.MD5));
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA1));
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA256));
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA512));
    }

    [Fact]
    public async Task ComputeHashesAsync_OnlySha256_ReturnsOnlySha256()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };

        var result = await service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None);

        Assert.Single(result.Hashes);
        Assert.True(result.Hashes.ContainsKey(HashAlgorithmType.SHA256));
    }

    [Fact]
    public async Task ComputeHashesAsync_EmptyFile_ReturnsValidHashes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, Array.Empty<byte>());
            var service = new HashService();
            var algorithms = new HashSet<HashAlgorithmType>
            {
                HashAlgorithmType.MD5,
                HashAlgorithmType.SHA256
            };

            var result = await service.ComputeHashesAsync(tempFile, algorithms, null, CancellationToken.None);

            // Hashes conocidos del contenido vacío
            Assert.Equal(0, result.FileSize);
            Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", result.Hashes[HashAlgorithmType.MD5]);
            Assert.Equal(
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                result.Hashes[HashAlgorithmType.SHA256]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeHashesAsync_NoAlgorithms_ThrowsArgumentException()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComputeHashesAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
        var missingPath = Path.Combine(Path.GetTempPath(), $"forzip_missing_{Guid.NewGuid():N}.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ComputeHashesAsync(missingPath, algorithms, null, CancellationToken.None));
    }

    [Fact]
    public async Task ComputeHashesAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ComputeHashesAsync(SampleFilePath, algorithms, null, cts.Token));
    }

    [Fact]
    public void ComputeSha256_KnownString_ReturnsExpectedHash()
    {
        var service = new HashService();

        var hash = service.ComputeSha256("test");

        Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", hash);
    }

    // ---------------------------------------------------------------------
    //  Progreso y uso del hilo llamador
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ComputeHashesAsync_ReportsProgress_MonotonicAndEndsAtOne()
    {
        var tempFile = CreateTempFile(8 * 1024 * 1024);
        try
        {
            var service = new HashService();
            var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
            var reports = new List<double>();
            var progress = new SynchronousProgress(reports.Add);

            await service.ComputeHashesAsync(tempFile, algorithms, progress, CancellationToken.None);

            Assert.NotEmpty(reports);
            Assert.All(reports, r => Assert.InRange(r, 0.0, 1.0));
            // El avance nunca retrocede y el último aviso cierra en 1.0
            for (var i = 1; i < reports.Count; i++)
            {
                Assert.True(reports[i] >= reports[i - 1], $"El progreso retrocedió en el aviso {i}.");
            }
            Assert.Equal(1.0, reports[^1], 6);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeHashesAsync_SobreStreamConTamanoConocido_ReportaProgreso()
    {
        // Es el caso del ZIP dividido en volúmenes: el archivo lógico no existe en disco, así
        // que el hash se calcula sobre un stream y el tamaño hay que pasarlo aparte. Sin esta
        // sobrecarga la etapa del informe avanzaba sin decir nada y parecía colgada.
        var datos = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(datos);

        var service = new HashService();
        var reports = new List<double>();
        var progress = new SynchronousProgress(reports.Add);

        await using var stream = new MemoryStream(datos);
        var hashes = await service.ComputeHashesAsync(
            stream,
            new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 },
            datos.Length,
            progress,
            CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.InRange(r, 0.0, 1.0));
        Assert.Equal(1.0, reports[^1], 6);

        // Y el hash es el mismo que sin progreso: el aviso no toca el cálculo.
        await using var otra = new MemoryStream(datos);
        var sinProgreso = await service.ComputeHashesAsync(
            otra, new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 }, CancellationToken.None);

        Assert.Equal(sinProgreso[HashAlgorithmType.SHA256], hashes[HashAlgorithmType.SHA256]);
    }

    [Fact]
    public async Task ComputeHashesAsync_ThrottlesProgress_AtMostOneReportPerPermille()
    {
        // A 64 KB por vuelta, 8 MiB daría 128 avisos; con archivos grandes serían decenas de
        // miles y el hilo de UI no da abasto. El acumulador se reporta por milésima.
        var tempFile = CreateTempFile(8 * 1024 * 1024);
        try
        {
            var service = new HashService();
            var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
            var count = 0;
            var progress = new SynchronousProgress(_ => Interlocked.Increment(ref count));

            await service.ComputeHashesAsync(tempFile, algorithms, progress, CancellationToken.None);

            // 1000 milésimas más el aviso final de cierre
            Assert.InRange(count, 1, 1001);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComputeHashesAsync_EmptyFile_ReportsCompletion()
    {
        var tempFile = CreateTempFile(0);
        try
        {
            var service = new HashService();
            var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };
            var reports = new List<double>();
            var progress = new SynchronousProgress(reports.Add);

            await service.ComputeHashesAsync(tempFile, algorithms, progress, CancellationToken.None);

            // Un archivo vacío no entra al bucle de lectura, pero igual tiene que cerrar la barra
            Assert.Equal(1.0, Assert.Single(reports), 6);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Regresión de la "ventana invisible": si el cálculo captura el contexto de sincronización
    /// del llamador, cada bloque de 64 KB devuelve el TransformBlock al hilo de UI y la ventana
    /// de hash rápido queda en blanco hasta terminar. El hash no debe usar el contexto del
    /// llamador para nada: sin ConfigureAwait(false) este test se cuelga y el Join falla.
    /// </summary>
    [Fact]
    public void ComputeHashesAsync_DoesNotUseCallerSynchronizationContext()
    {
        var service = new HashService();
        var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };

        var postCount = -1;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var context = new CountingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                // El bloqueo es deliberado: es la trampa que detecta la captura de contexto.
                // El hilo es propio y el Join de abajo acota la espera, así que no cuelga la suite.
#pragma warning disable xUnit1031
                service.ComputeHashesAsync(SampleFilePath, algorithms, null, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore xUnit1031
                postCount = context.PostCount;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.IsBackground = true;
        thread.Start();

        Assert.True(
            thread.Join(TimeSpan.FromSeconds(30)),
            "El cálculo se bloqueó esperando al contexto del llamador (falta ConfigureAwait(false)).");
        Assert.Null(failure);
        Assert.Equal(0, postCount);
    }

    private static string CreateTempFile(int sizeBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"forzip_hash_{Guid.NewGuid():N}.bin");
        var content = new byte[sizeBytes];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>
    /// <see cref="Progress{T}"/> entrega los avisos de forma diferida, lo que haría inestables
    /// los conteos. Esta implementación invoca en el mismo hilo que reporta.
    /// </summary>
    private sealed class SynchronousProgress : IProgress<double>
    {
        private readonly Action<double> _onReport;

        public SynchronousProgress(Action<double> onReport) => _onReport = onReport;

        public void Report(double value) => _onReport(value);
    }

    /// <summary>Cuenta los reenvíos al contexto del llamador, que deben ser cero.</summary>
    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => _postCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            base.Post(d, state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            base.Send(d, state);
        }
    }
}
