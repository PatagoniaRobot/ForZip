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

namespace ForZip.Core.Models;

public enum HashAlgorithmType
{
    MD5,
    SHA1,
    SHA256,
    SHA512
}

public static class HashAlgorithmNames
{
    /// <summary>Orden canónico de presentación: del más débil al más fuerte.</summary>
    public static readonly HashAlgorithmType[] DisplayOrder =
    {
        HashAlgorithmType.MD5,
        HashAlgorithmType.SHA1,
        HashAlgorithmType.SHA256,
        HashAlgorithmType.SHA512
    };

    /// <summary>Nombre con guion tal como se escribe en informes y en la interfaz.</summary>
    public static string Display(HashAlgorithmType algorithm) => algorithm switch
    {
        HashAlgorithmType.MD5 => "MD5",
        HashAlgorithmType.SHA1 => "SHA-1",
        HashAlgorithmType.SHA256 => "SHA-256",
        HashAlgorithmType.SHA512 => "SHA-512",
        _ => algorithm.ToString()
    };
}
