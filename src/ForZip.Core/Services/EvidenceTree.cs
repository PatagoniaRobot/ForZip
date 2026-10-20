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
/// Recorrido de una carpeta de evidencia que no entra en enlaces simbólicos ni junctions. Lo usan el
/// empaquetado y la selección de la GUI, para que lo que el operador ve al elegir sea lo que se empaqueta.
/// </summary>
/// <remarks>
/// <c>Directory.EnumerateFiles</c> con <c>AllDirectories</c> sigue los enlaces: una junction dentro del
/// origen metía en el paquete archivos de cualquier otro lugar del disco como si fueran parte de la
/// evidencia, y una junction que apunta a una carpeta padre llevaba a un bucle que terminaba en un error de
/// ruta demasiado larga. Los marcadores de OneDrive y otros archivos en la nube también son puntos de
/// reanálisis, pero no enlaces (<see cref="FileSystemInfo.LinkTarget"/> es null): esos se leen como siempre.
/// </remarks>
public static class EvidenceTree
{
    /// <summary>Un enlace encontrado dentro del origen, con su destino tal como lo declara.</summary>
    public sealed record SkippedLink(string Path, string Target, bool IsDirectory, DateTimeOffset ModifiedUtc);

    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false
    };

    /// <summary>
    /// Una carpeta que Windows no dejó recorrer (en la raíz de una unidad, <c>System Volume Information</c> le niega
    /// la lectura incluso al administrador). Con el código y el mensaje de Windows.
    /// </summary>
    public sealed record UnreadableFolder(string Path, int WindowsErrorCode, string Message, DateTimeOffset ModifiedUtc);

    /// <summary>
    /// Archivos debajo de <paramref name="root"/>, sin entrar en enlaces. Cada enlace encontrado (a archivo o a
    /// carpeta) se agrega a <paramref name="skippedLinks"/> si se da. La raíz misma se recorre aunque sea un
    /// enlace: elegirla fue una decisión explícita del operador.
    /// </summary>
    /// <param name="unreadableFolders">
    /// Si se da, una carpeta que no se puede recorrer se anota acá y el recorrido sigue con las demás. Si no, la
    /// excepción de Windows se propaga: quien no puede documentar la omisión no la oculta.
    /// </param>
    public static IEnumerable<string> EnumerateFiles(string root, List<SkippedLink>? skippedLinks = null,
        List<UnreadableFolder>? unreadableFolders = null)
    {
        foreach (var (info, isLink) in Walk(root, unreadableFolders))
        {
            if (isLink)
            {
                skippedLinks?.Add(new SkippedLink(info.FullName, info.LinkTarget ?? string.Empty,
                    info is DirectoryInfo, info.LastWriteTimeUtc));
            }
            else if (info is FileInfo)
            {
                yield return info.FullName;
            }
        }
    }

    /// <summary>Subcarpetas debajo de <paramref name="root"/> (sin la raíz), sin entrar en enlaces.</summary>
    public static IEnumerable<string> EnumerateDirectories(string root, List<UnreadableFolder>? unreadableFolders = null)
    {
        foreach (var (info, isLink) in Walk(root, unreadableFolders))
        {
            if (!isLink && info is DirectoryInfo)
            {
                yield return info.FullName;
            }
        }
    }

    /// <summary>
    /// Recorrido en anchura, como el de .NET: los elementos de cada carpeta en el orden en que los da el
    /// sistema, y las subcarpetas en cola. Un enlace se devuelve marcado y no se recorre.
    /// </summary>
    private static IEnumerable<(FileSystemInfo Info, bool IsLink)> Walk(string root, List<UnreadableFolder>? unreadableFolders)
    {
        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var dir = pending.Dequeue();
            IEnumerator<FileSystemInfo>? items = null;
            try
            {
                while (true)
                {
                    FileSystemInfo info;
                    try
                    {
                        items ??= dir.EnumerateFileSystemInfos("*", OneLevel).GetEnumerator();
                        if (!items.MoveNext())
                        {
                            break;
                        }

                        info = items.Current;
                    }
                    catch (Exception ex) when (unreadableFolders != null && ex is UnauthorizedAccessException or IOException)
                    {
                        // Lo ya devuelto de esta carpeta queda; el resto se documenta como no leído.
                        unreadableFolders.Add(new UnreadableFolder(dir.FullName, ex.HResult & 0xFFFF, ex.Message,
                            SafeModified(dir)));
                        break;
                    }

                    // El atributo viene de la enumeración y no cuesta nada; LinkTarget sí consulta el disco, así
                    // que solo se pregunta por los puntos de reanálisis.
                    bool isLink = (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget != null;
                    yield return (info, isLink);
                    if (!isLink && info is DirectoryInfo sub)
                    {
                        pending.Enqueue(sub);
                    }
                }
            }
            finally
            {
                items?.Dispose();
            }
        }
    }

    private static DateTimeOffset SafeModified(DirectoryInfo dir)
    {
        try
        {
            return dir.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return default;
        }
    }
}
