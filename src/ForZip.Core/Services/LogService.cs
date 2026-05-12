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
    private readonly string _logFilePath;

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
        
        // Ejecutar en el hilo de UI si fuera necesario, pero ObservableCollection 
        // suele requerir despacho manual en Avalonia si se actualiza desde hilos de fondo.
        // Por ahora lo hacemos directo, el ViewModel se encargará si hace falta.
        Entries.Add(entry);
        
        WriteToFile(entry);
    }

    public void Clear()
    {
        Entries.Clear();
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level.ToString().ToUpper()}] {entry.Message}{Environment.NewLine}";
            File.AppendAllText(_logFilePath, line);
        }
        catch
        {
            // Fallo silencioso en escritura a archivo para no romper la app
        }
    }
}
