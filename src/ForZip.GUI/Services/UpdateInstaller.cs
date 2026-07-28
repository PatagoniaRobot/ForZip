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

namespace ForZip.GUI.Services;

/// <summary>
/// Reemplazo del ejecutable en caliente, sin proceso auxiliar ni script suelto.
/// <para/>
/// Windows no deja sobrescribir un <c>.exe</c> en ejecución, pero sí <b>renombrarlo</b>. Sobre
/// eso se apoya toda la maniobra: el binario en uso se aparta a <c>&lt;nombre&gt;.old</c>, el
/// nuevo ocupa su lugar, se relanza y el proceso viejo termina. Al siguiente arranque, ya libre,
/// se borra el <c>.old</c>; mientras tanto queda como vuelta atrás si la versión nueva falla.
/// <para/>
/// Evitar un ayudante externo no es solo elegancia: un ejecutable que lanza otro proceso para
/// reemplazar binarios es exactamente el patrón que los antivirus marcan.
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private const string BackupSuffix = ".old";

    private readonly string _currentExePath;

    public UpdateInstaller()
    {
        // ProcessPath apunta al apphost real, no al .dll: es el archivo que hay que reemplazar.
        _currentExePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "ForZip.exe");
    }

    public string CurrentExeName => Path.GetFileName(_currentExePath);

    public string InstallDirectory => Path.GetDirectoryName(_currentExePath) ?? AppContext.BaseDirectory;

    private string BackupPath => _currentExePath + BackupSuffix;

    public bool CanInstall()
    {
        try
        {
            // Sondeo real: los permisos declarados no siempre coinciden con lo que el medio
            // permite (pendrive protegido contra escritura, unidad de red, carpeta de solo lectura).
            var probe = Path.Combine(InstallDirectory, $".forzip-write-probe-{Guid.NewGuid():N}");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
            }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public InstallOutcome ReplaceAndRelaunch(string newExePath)
    {
        if (!File.Exists(newExePath))
        {
            return new InstallOutcome(false, $"No se encontró el archivo descargado: {newExePath}");
        }

        // Un .old de un intento anterior bloquearía el renombrado.
        try
        {
            if (File.Exists(BackupPath))
            {
                File.Delete(BackupPath);
            }
        }
        catch
        {
            // Si no se puede borrar (todavía bloqueado), el File.Move de abajo avisará.
        }

        var moved = false;
        try
        {
            File.Move(_currentExePath, BackupPath);
            moved = true;

            File.Copy(newExePath, _currentExePath);
        }
        catch (Exception ex)
        {
            // Vuelta atrás: si alcanzamos a apartar el binario pero no pudimos poner el nuevo,
            // hay que dejar el ejecutable donde estaba o la instalación queda destruida.
            if (moved)
            {
                try
                {
                    if (File.Exists(_currentExePath))
                    {
                        File.Delete(_currentExePath);
                    }
                    File.Move(BackupPath, _currentExePath);
                }
                catch
                {
                    return new InstallOutcome(false,
                        $"{ex.Message} — además falló la vuelta atrás; el ejecutable anterior quedó en {BackupPath}");
                }
            }

            return new InstallOutcome(false, ex.Message);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _currentExePath,
                UseShellExecute = true,
                WorkingDirectory = InstallDirectory
            });
        }
        catch (Exception ex)
        {
            // El binario nuevo ya está puesto y verificado; solo falló el relanzamiento.
            return new InstallOutcome(false, $"Se instaló la versión nueva, pero no se pudo reabrir ForZip: {ex.Message}");
        }

        return new InstallOutcome(true, null);
    }

    public void CleanupPreviousVersion()
    {
        try
        {
            if (File.Exists(BackupPath))
            {
                File.Delete(BackupPath);
            }
        }
        catch
        {
            // Si el proceso anterior todavía no soltó el archivo, se intenta en el próximo arranque.
        }
    }
}
