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

using System.Collections.ObjectModel;
using ForZip.Core.Interfaces;

namespace ForZip.Core.Services;

public class LogService : ILogService
{
    private const string LogDirectory = "Logs";

    // Rotación: al superar este tamaño, el .log actual se archiva con marca temporal
    private const long MaxLogFileBytes = 5 * 1024 * 1024;

    /// <summary>Carpeta de la bitácora; null si no se pudo crear (se sigue sin bitácora en disco).</summary>
    private readonly string? _logDirectory;
    private readonly object _fileLock = new();

    /// <summary>
    /// Un archivo por día, según la fecha de cada línea: antes se fijaba al arrancar y, pasada la medianoche, se
    /// seguía escribiendo en el del día anterior.
    /// </summary>
    private string LogFilePathFor(DateTime timestamp) =>
        Path.Combine(_logDirectory!, $"ForZip_{timestamp:yyyyMMdd}.log");

    /// <summary>
    /// Despachador opcional al hilo de UI. La GUI lo asigna con
    /// <c>Dispatcher.UIThread.Post</c>; en CLI/tests queda nulo y se escribe directo.
    /// Evita corromper el binding de Avalonia al loguear desde hilos de fondo.
    /// </summary>
    public Action<Action>? UiDispatcher { get; set; }

    public LogService()
    {
        Entries = new ObservableCollection<LogEntry>();

        // Si la carpeta no se puede crear, ForZip arranca igual: la bitácora queda en pantalla. Antes la excepción
        // cerraba la aplicación al iniciar.
        try
        {
            var logDir = Path.Combine(AppDataLocation.Directory, LogDirectory);
            Directory.CreateDirectory(logDir);
            _logDirectory = logDir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logDirectory = null;
        }
    }

    public ObservableCollection<LogEntry> Entries { get; }

    /// <inheritdoc />
    public bool Redacted { get; set; }

    /// <inheritdoc />
    public string RedactedMarker { get; set; } = "[omitido: perfil Reservado]";

    public void Log(LogLevel level, string message) => Log(level, message, null);

    /// <inheritdoc />
    public void Log(LogLevel level, string message, string? evidenceDetail)
    {
        // En modo de redacción el detalle no se acorta ni se enmascara: no se escribe. Una
        // ruta parcial en la bitácora de una causa reservada sigue siendo una ruta.
        var text = Redacted || string.IsNullOrEmpty(evidenceDetail)
            ? message
            : $"{message} {evidenceDetail}";

        var entry = new LogEntry(DateTime.Now, level, text);

        // Mutar la ObservableCollection en el hilo de UI cuando hay despachador
        if (UiDispatcher != null)
        {
            UiDispatcher(() => Entries.Add(entry));
        }
        else
        {
            Entries.Add(entry);
        }

        WriteToFile(entry);
    }

    public void Clear()
    {
        if (UiDispatcher != null)
        {
            UiDispatcher(() => Entries.Clear());
        }
        else
        {
            Entries.Clear();
        }
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level.ToString().ToUpper()}] {entry.Message}{Environment.NewLine}";

            if (_logDirectory == null)
            {
                return;
            }

            // Una sola escritura serializada evita intercalado entre hilos
            lock (_fileLock)
            {
                var path = LogFilePathFor(entry.Timestamp);
                RotateIfNeeded(path);
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Fallo silencioso en escritura a archivo para no romper la app
        }
    }

    private static void RotateIfNeeded(string logFilePath)
    {
        var info = new FileInfo(logFilePath);
        if (!info.Exists || info.Length < MaxLogFileBytes)
        {
            return;
        }

        var archived = $"{logFilePath}.{DateTime.Now:HHmmss}.bak";
        try
        {
            File.Move(logFilePath, archived);
        }
        catch (IOException)
        {
            // Si no se puede rotar (archivo bloqueado), se sigue escribiendo en el actual
        }
    }
}
