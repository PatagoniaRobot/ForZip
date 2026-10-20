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

using ForZip.Core.Interfaces;
using ForZip.Core.Services;

namespace ForZip.GUI.ViewModels;

/// <summary>La huella de una carpeta, como la muestran Hash Batch y el hash rápido.</summary>
public sealed class FolderDigestItem
{
    public FolderDigestItem(FolderDigest digest, ILocalizationService localization, Func<long, string> formatSize)
    {
        Digest = digest;
        Name = Path.GetFileName(digest.FolderPath) is { Length: > 0 } name ? name : digest.FolderPath;
        FolderPath = digest.FolderPath;
        Summary = string.Format(localization.Get("hash_folder_summary"), digest.FileCount, formatSize(digest.TotalSize));
        LabelForZip = localization.Get("hash_folder_forzip");
        LabelSevenZip = localization.Get("hash_folder_7zip");
        LabelCopy = localization.Get("hash_folder_copy");
        ForZip = digest.ForZipSha256 ?? string.Empty;
        SevenZip = digest.SevenZipDataSha256 ?? string.Empty;

        var notes = new List<string>();
        if (!digest.IsComplete)
        {
            notes.Add(string.Format(localization.Get("hash_folder_incomplete"), digest.UnreadableCount));
        }

        if (digest.LinksNotFollowed > 0)
        {
            notes.Add(string.Format(localization.Get("hash_folder_links"), digest.LinksNotFollowed));
        }

        Note = string.Join(" ", notes);
    }

    public FolderDigest Digest { get; }
    public string Name { get; }
    public string FolderPath { get; }
    public string Summary { get; }
    public string LabelForZip { get; }
    public string LabelSevenZip { get; }
    public string LabelCopy { get; }
    public string ForZip { get; }
    public string SevenZip { get; }
    public string Note { get; }
    public bool HasDigest => Digest.IsComplete;
    public bool HasNote => Note.Length > 0;

    /// <summary>Texto para el portapapeles y para el volcado del hash rápido.</summary>
    public string ToPlainText() => HasDigest
        ? $"{FolderPath}{Environment.NewLine}  {Summary}{Environment.NewLine}  {LabelForZip}: {ForZip}{Environment.NewLine}  {LabelSevenZip}: {SevenZip}"
          + (HasNote ? $"{Environment.NewLine}  {Note}" : string.Empty)
        : $"{FolderPath}{Environment.NewLine}  {Summary}{Environment.NewLine}  {Note}";
}
