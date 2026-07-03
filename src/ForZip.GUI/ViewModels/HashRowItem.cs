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

using CommunityToolkit.Mvvm.ComponentModel;

namespace ForZip.GUI.ViewModels;

public partial class HashRowItem : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private string _md5 = string.Empty;

    [ObservableProperty]
    private string _sha1 = string.Empty;

    [ObservableProperty]
    private string _sha256 = string.Empty;

    [ObservableProperty]
    private string _sha512 = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;
}
