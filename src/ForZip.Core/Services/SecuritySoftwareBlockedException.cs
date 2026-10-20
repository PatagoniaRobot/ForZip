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

namespace ForZip.Core.Services;

/// <summary>
/// Etapa de una operación de ForZip en la que el software de seguridad negó el acceso.
/// </summary>
/// <remarks>
/// La etapa no es un detalle técnico: decide qué quedó en disco. Un bloqueo leyendo la
/// evidencia o escribiendo el paquete deja un paquete que no existe; uno al releerlo deja un
/// paquete completo al que solo le falta la verificación.
/// </remarks>
public enum SecurityBlockStage
{
    ReadingEvidence,
    WritingPackage,
    RereadingPackage,
    Sealing
}

/// <summary>
/// El antivirus o el EDR impidió acceder a un archivo concreto en una etapa concreta. Envuelve
/// la excepción original de Windows para que la bitácora pueda decir dónde y sobre qué ocurrió,
/// en lugar de un genérico "se abortó".
/// </summary>
public class SecuritySoftwareBlockedException : IOException
{
    public SecuritySoftwareBlockedException(SecurityBlockStage stage, string path, Exception inner)
        : base(inner.Message, inner)
    {
        Stage = stage;
        Path = path;
        NativeErrorCode = SecuritySoftwareInterference.GetBlockingErrorCode(inner) ?? 0;
        HResult = inner.HResult;
    }

    public SecurityBlockStage Stage { get; }

    /// <summary>Ruta del archivo al que se negó el acceso.</summary>
    public string Path { get; }

    /// <summary>225 (amenaza detectada, acceso denegado) o 226 (archivo eliminado o en cuarentena).</summary>
    public int NativeErrorCode { get; }

    /// <summary>
    /// Envuelve <paramref name="exception"/> si es un bloqueo del software de seguridad que
    /// todavía no tiene etapa asignada; si ya la tiene, conserva la original, que es la más
    /// precisa. Devuelve null cuando no se trata de un bloqueo.
    /// </summary>
    public static SecuritySoftwareBlockedException? Wrap(SecurityBlockStage stage, string path, Exception exception)
    {
        if (exception is SecuritySoftwareBlockedException existing) return existing;
        return SecuritySoftwareInterference.IsBlocked(exception)
            ? new SecuritySoftwareBlockedException(stage, path, exception)
            : null;
    }
}
