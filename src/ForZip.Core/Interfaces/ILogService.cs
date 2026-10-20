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

namespace ForZip.Core.Interfaces;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Success
}

public record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    /// <summary>Para la consola de la GUI: un error se pinta entero, no solo la palabra del nivel.</summary>
    public bool IsError => Level == LogLevel.Error;

    /// <summary>Ídem para las advertencias.</summary>
    public bool IsWarning => Level == LogLevel.Warning;
}

public interface ILogService
{
    ObservableCollection<LogEntry> Entries { get; }

    /// <summary>
    /// Modo de redacción del perfil Reservado: mientras está activo, la bitácora no registra
    /// nada derivado de la evidencia.
    /// </summary>
    /// <remarks>
    /// Es un modo, no un filtro de texto a posteriori: lo que se descarta es el argumento
    /// <c>evidenceDetail</c> de cada llamada, así que quien escribe una entrada tiene que
    /// decidir explícitamente qué parte es sensible. Filtrar la cadena ya armada solo
    /// serviría hasta el primer mensaje que el filtro no previó.
    /// </remarks>
    bool Redacted { get; set; }

    /// <summary>
    /// Lo que ocupa el lugar del dato en una línea cuya plantilla termina en él ("- Destino: {0}") cuando la
    /// bitácora está redactada. Sin esto quedaba "finalizada exitosamente en:" con nada después, que parecía una
    /// falla y no decía que el perfil Reservado estaba activo.
    /// </summary>
    string RedactedMarker { get; set; }

    void Log(LogLevel level, string message);

    /// <summary>
    /// Registra un mensaje separando la parte segura de la derivada de la evidencia (nombres
    /// de archivo, rutas de origen, número de causa, datos del operador). Con
    /// <see cref="Redacted"/> activo, <paramref name="evidenceDetail"/> se descarta entero.
    /// </summary>
    void Log(LogLevel level, string message, string? evidenceDetail);

    void Info(string message) => Log(LogLevel.Info, message);
    void Warning(string message) => Log(LogLevel.Warning, message);
    void Error(string message) => Log(LogLevel.Error, message);
    void Success(string message) => Log(LogLevel.Success, message);

    void Info(string message, string? evidenceDetail) => Log(LogLevel.Info, message, evidenceDetail);
    void Warning(string message, string? evidenceDetail) => Log(LogLevel.Warning, message, evidenceDetail);
    void Error(string message, string? evidenceDetail) => Log(LogLevel.Error, message, evidenceDetail);
    void Success(string message, string? evidenceDetail) => Log(LogLevel.Success, message, evidenceDetail);

    void Clear();
}
