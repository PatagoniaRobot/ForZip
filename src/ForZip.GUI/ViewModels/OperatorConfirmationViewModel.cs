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
using CommunityToolkit.Mvvm.Input;
using ForZip.Core.Models;
using ForZip.GUI.Services;

namespace ForZip.GUI.ViewModels;

public partial class OperatorConfirmationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _organization = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private bool _generateExternalHash = true;

    [ObservableProperty]
    private bool _signManifest;

    [ObservableProperty]
    private string _certificatePath = string.Empty;

    [ObservableProperty]
    private string _certificatePassword = string.Empty;

    public OperatorConfirmationViewModel(OperatorInfo info)
    {
        Name = info.Name ?? string.Empty;
        Title = info.Title ?? string.Empty;
        Organization = info.Organization ?? string.Empty;
        Email = info.Email ?? string.Empty;
        Phone = info.Phone ?? string.Empty;
    }

    public OperatorConfirmationResult GetResult() => new()
    {
        Operator = new OperatorInfo
        {
            Name = string.IsNullOrWhiteSpace(Name) ? null : Name,
            Title = string.IsNullOrWhiteSpace(Title) ? null : Title,
            Organization = string.IsNullOrWhiteSpace(Organization) ? null : Organization,
            Email = string.IsNullOrWhiteSpace(Email) ? null : Email,
            Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone
        },
        GenerateExternalHash = GenerateExternalHash,
        SignManifest = SignManifest && !string.IsNullOrWhiteSpace(CertificatePath),
        CertificatePath = string.IsNullOrWhiteSpace(CertificatePath) ? null : CertificatePath,
        CertificatePassword = string.IsNullOrEmpty(CertificatePassword) ? null : CertificatePassword
    };

    [RelayCommand]
    private void Confirm()
    {
        // El diálogo se cerrará devolviendo el resultado
    }
}
