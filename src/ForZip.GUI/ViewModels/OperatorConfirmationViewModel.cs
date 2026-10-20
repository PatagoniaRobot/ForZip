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
using ForZip.Core.Services;
using ForZip.GUI.Services;

namespace ForZip.GUI.ViewModels;

public partial class OperatorConfirmationViewModel : ObservableObject
{
    /// <summary>Muestra en claro la contraseña del certificado (el ojo, como en Empaquetar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CertificatePasswordChar))]
    private bool _isCertificatePasswordVisible;

    public char CertificatePasswordChar => IsCertificatePasswordVisible ? '\0' : '●';

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
    private string _caseNumber = string.Empty;

    [ObservableProperty]
    private string _caseDescription = string.Empty;

    [ObservableProperty]
    private string _court = string.Empty;

    [ObservableProperty]
    private bool _generateExternalHash = true;

    [ObservableProperty]
    private bool _signManifest;

    [ObservableProperty]
    private string _certificatePath = string.Empty;

    [ObservableProperty]
    private string _certificatePassword = string.Empty;

    [ObservableProperty]
    private bool _requestTimestamp = true;

    [ObservableProperty]
    private string _timestampUrl = string.Empty;

    /// <summary>Si el diálogo ofrece firmar (no en Hash Batch, que no firma su informe).</summary>
    public bool OfferSigning { get; init; } = true;

    /// <summary>
    /// Si el diálogo ofrece el .sha256 del informe: solo Hash Batch, que deja su informe al lado. Empaquetar ya no
    /// escribe informe, y ese .sha256 lo recalcula cualquiera que edite el informe.
    /// </summary>
    public bool OfferExternalHash { get; init; }

    /// <summary>Por qué el certificado elegido no sirve; vacío si no hay problema.</summary>
    [ObservableProperty]
    private string _certificateError = string.Empty;

    // Cambiar el certificado o su contraseña invalida el aviso anterior.
    partial void OnCertificatePathChanged(string value) => CertificateError = string.Empty;

    partial void OnCertificatePasswordChanged(string value) => CertificateError = string.Empty;

    /// <summary>
    /// Si se pidió firmar, prueba el certificado antes de cerrar el diálogo: una contraseña mal tipeada aparecía
    /// recién al firmar, y en el perfil Reservado tiraba el sobre ya hecho. Devuelve false si no sirve.
    /// </summary>
    public bool TryValidateCertificate(string errorPrefix)
    {
        if (!SignManifest || string.IsNullOrWhiteSpace(CertificatePath))
        {
            return true;
        }

        try
        {
            SignatureService.EnsureUsableCertificate(
                CertificatePath, string.IsNullOrEmpty(CertificatePassword) ? null : CertificatePassword);
            return true;
        }
        catch (SigningCertificateException ex)
        {
            CertificateError = $"{errorPrefix} {ex.Message}";
            return false;
        }
    }

    public OperatorConfirmationViewModel(OperatorInfo info, string? defaultTimestampUrl = null)
    {
        Name = info.Name ?? string.Empty;
        Title = info.Title ?? string.Empty;
        Organization = info.Organization ?? string.Empty;
        Email = info.Email ?? string.Empty;
        Phone = info.Phone ?? string.Empty;
        TimestampUrl = defaultTimestampUrl ?? string.Empty;
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
        CaseNumber = string.IsNullOrWhiteSpace(CaseNumber) ? null : CaseNumber,
        CaseDescription = string.IsNullOrWhiteSpace(CaseDescription) ? null : CaseDescription,
        Court = string.IsNullOrWhiteSpace(Court) ? null : Court,
        GenerateExternalHash = OfferExternalHash && GenerateExternalHash,
        SignManifest = SignManifest && !string.IsNullOrWhiteSpace(CertificatePath),
        CertificatePath = string.IsNullOrWhiteSpace(CertificatePath) ? null : CertificatePath,
        CertificatePassword = string.IsNullOrEmpty(CertificatePassword) ? null : CertificatePassword,
        TimestampUrl = SignManifest && RequestTimestamp && !string.IsNullOrWhiteSpace(TimestampUrl)
            ? TimestampUrl.Trim()
            : null
    };

    [RelayCommand]
    private void Confirm()
    {
        // El diálogo se cerrará devolviendo el resultado
    }
}
