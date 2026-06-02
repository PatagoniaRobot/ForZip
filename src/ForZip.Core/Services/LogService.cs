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

    private readonly string _logFilePath;
    private readonly object _fileLock = new();

    /// <summary>
    /// Despachador opcional al hilo de UI. La GUI lo asigna con
    /// <c>Dispatcher.UIThread.Post</c>; en CLI/tests queda nulo y se escribe directo.
    /// Evita corromper el binding de Avalonia al loguear desde hilos de fondo.
    /// </summary>
    public Action<Action>? UiDispatcher { get; set; }

    public LogService()
    {
        Entries = new ObservableCollection<LogEntry>();

        var baseDir = AppContext.BaseDirectory;
        var logDir = Path.Combine(baseDir, LogDirectory);
        Directory.CreateDirectory(logDir);

        var fileName = $"ForZip_{DateTime.Now:yyyyMMdd}.log";
        _logFilePath = Path.Combine(logDir, fileName);
    }

    public ObservableCollection<LogEntry> Entries { get; }

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);

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

            // Una sola escritura serializada evita intercalado entre hilos
            lock (_fileLock)
            {
                RotateIfNeeded();
                File.AppendAllText(_logFilePath, line);
            }
        }
        catch
        {
            // Fallo silencioso en escritura a archivo para no romper la app
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_logFilePath);
        if (!info.Exists || info.Length < MaxLogFileBytes)
        {
            return;
        }

        var archived = $"{_logFilePath}.{DateTime.Now:HHmmss}.bak";
        try
        {
            File.Move(_logFilePath, archived);
        }
        catch (IOException)
        {
            // Si no se puede rotar (archivo bloqueado), se sigue escribiendo en el actual
        }
    }
}
