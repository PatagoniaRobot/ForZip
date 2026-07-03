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

using System.IO.Pipes;

namespace ForZip.GUI.Services;

/// <summary>
/// Coordinación de instancia única vía Mutex + named pipe, por usuario. La primera instancia
/// "gana" el mutex y levanta un servidor de pipe; las siguientes le reenvían sus argumentos
/// (verbo + ruta del menú contextual) y terminan, de modo que una multi-selección se acumula
/// en una sola ventana en lugar de abrir varias.
/// <para/>
/// Es totalmente tolerante a fallos: si algo del mecanismo falla, se comporta como una app
/// normal multi-ventana (nunca impide el arranque).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _serverCts;

    public bool IsFirstInstance { get; }

    public SingleInstance(string appId)
    {
        // Espacio por usuario: distintas sesiones/usuarios no se interfieren.
        var scope = $"{appId}.{Environment.UserName}";
        _pipeName = $"{scope}.pipe";

        try
        {
            _mutex = new Mutex(initiallyOwned: true, $@"Local\{scope}.mutex", out var createdNew);
            IsFirstInstance = createdNew;
        }
        catch
        {
            // Sin mutex utilizable: tratar como instancia normal (no bloquear el arranque).
            _mutex = null;
            IsFirstInstance = true;
        }
    }

    /// <summary>Reenvía los argumentos a la instancia primaria. Devuelve <c>true</c> si se entregaron.</summary>
    public bool TrySendArgs(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            foreach (var arg in args)
            {
                // Un argumento por línea; las rutas no contienen saltos de línea.
                writer.WriteLine(arg);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Inicia (solo en la instancia primaria) el servidor que escucha reenvíos. <paramref name="onArgs"/>
    /// se invoca en un hilo de fondo por cada conexión; el llamador debe marshalar a la UI.
    /// </summary>
    public void StartServer(Action<string[]> onArgs)
    {
        if (!IsFirstInstance)
        {
            return;
        }

        _serverCts = new CancellationTokenSource();
        var ct = _serverCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        _pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    using var reader = new StreamReader(server);
                    var lines = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                    {
                        lines.Add(line);
                    }

                    if (lines.Count > 0)
                    {
                        onArgs(lines.ToArray());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Conexión fallida o malformada: seguir escuchando.
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        try { _serverCts?.Cancel(); } catch { /* ignore */ }
        try { _mutex?.ReleaseMutex(); } catch { /* no éramos dueños */ }
        _mutex?.Dispose();
    }
}
