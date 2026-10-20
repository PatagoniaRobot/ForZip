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

using System.ComponentModel;
using ForZip.Core.Interfaces;

namespace ForZip.Core.Services;

/// <summary>
/// Reconoce cuando Windows impide una operación de archivo porque el antivirus o el EDR
/// clasificó su contenido como amenaza. ForZip no inspecciona contenido ni puede eludir un
/// bloqueo del sistema operativo; distinguirlo de un error de E/S común evita atribuirle a
/// la aplicación una decisión que tomó el software de seguridad del equipo.
/// </summary>
public static class SecuritySoftwareInterference
{
    // WinError.h: ERROR_VIRUS_INFECTED y ERROR_VIRUS_DELETED.
    public const int VirusInfectedError = 225;
    public const int VirusDeletedError = 226;

    /// <summary>
    /// Indica si la excepción, o alguna excepción interna, contiene uno de los códigos con
    /// los que Windows comunica un bloqueo o una cuarentena por detección de malware.
    /// </summary>
    public static bool IsBlocked(Exception exception) => GetBlockingErrorCode(exception) != null;

    /// <summary>
    /// Devuelve el código de Windows del bloqueo (225 o 226) buscando en toda la cadena de
    /// excepciones internas, o null si la excepción no es un bloqueo del software de seguridad.
    /// </summary>
    public static int? GetBlockingErrorCode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            var errorCode = current is Win32Exception win32
                ? win32.NativeErrorCode
                : current.HResult & 0xFFFF;

            if (errorCode is VirusInfectedError or VirusDeletedError)
            {
                return errorCode;
            }
        }

        return null;
    }

    /// <summary>
    /// Arma el mensaje para el operador y las dos líneas de bitácora de un bloqueo. El mensaje
    /// depende de la etapa porque lo que quedó en disco depende de ella; la bitácora conserva
    /// archivo, código y texto original de Windows, que es lo que hace falta para reconstruir
    /// después qué pasó sin depender de la memoria de nadie.
    /// </summary>
    public static (string Message, string LogSummary, string LogDetail) Describe(
        SecuritySoftwareBlockedException blocked, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(blocked);
        ArgumentNullException.ThrowIfNull(localization);

        var (messageKey, stageKey) = blocked.Stage switch
        {
            SecurityBlockStage.ReadingEvidence => ("error_security_blocked_read", "security_stage_read"),
            SecurityBlockStage.WritingPackage => ("error_security_blocked_write", "security_stage_write"),
            SecurityBlockStage.RereadingPackage when blocked.NativeErrorCode == VirusDeletedError
                => ("error_security_blocked_quarantined", "security_stage_reread"),
            SecurityBlockStage.RereadingPackage => ("error_security_blocked_reread", "security_stage_reread"),
            _ => ("error_security_blocked_seal", "security_stage_seal")
        };

        var original = blocked.InnerException?.Message ?? blocked.Message;
        return (
            string.Format(localization.Get(messageKey), blocked.Path),
            string.Format(localization.Get("log_security_blocked"), localization.Get(stageKey)),
            string.Format(localization.Get("log_security_blocked_detail"), blocked.Path, blocked.NativeErrorCode, original));
    }
}
