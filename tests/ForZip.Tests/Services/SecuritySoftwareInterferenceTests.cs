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
using ForZip.Core.Services;

namespace ForZip.Tests.Services;

public class SecuritySoftwareInterferenceTests
{
    [Theory]
    [InlineData(SecuritySoftwareInterference.VirusInfectedError)]
    [InlineData(SecuritySoftwareInterference.VirusDeletedError)]
    public void IsBlocked_WindowsVirusErrorInIOException_ReturnsTrue(int errorCode)
    {
        var exception = new IOException("Bloqueado por el sistema.", HResultFromWin32(errorCode));

        Assert.True(SecuritySoftwareInterference.IsBlocked(exception));
    }

    [Fact]
    public void IsBlocked_WindowsVirusErrorWrappedByLibrary_ReturnsTrue()
    {
        var native = new Win32Exception(SecuritySoftwareInterference.VirusInfectedError);
        var wrapped = new InvalidOperationException("La biblioteca no pudo continuar.", native);

        Assert.True(SecuritySoftwareInterference.IsBlocked(wrapped));
    }

    [Fact]
    public void IsBlocked_OrdinaryIoError_ReturnsFalse()
    {
        var exception = new IOException("El disco está lleno.", HResultFromWin32(112));

        Assert.False(SecuritySoftwareInterference.IsBlocked(exception));
    }

    [Fact]
    public void GetBlockingErrorCode_DistinguishesInfectedFromDeleted()
    {
        Assert.Equal(225, SecuritySoftwareInterference.GetBlockingErrorCode(
            new IOException("x", HResultFromWin32(225))));
        Assert.Equal(226, SecuritySoftwareInterference.GetBlockingErrorCode(
            new IOException("x", HResultFromWin32(226))));
        Assert.Null(SecuritySoftwareInterference.GetBlockingErrorCode(
            new IOException("x", HResultFromWin32(112))));
    }

    [Fact]
    public void Wrap_BlockedException_KeepsStagePathAndWindowsCode()
    {
        var native = new IOException("Contiene un virus.", HResultFromWin32(225));

        var blocked = SecuritySoftwareBlockedException.Wrap(
            SecurityBlockStage.ReadingEvidence, @"C:\caso\adjunto.html", native);

        Assert.NotNull(blocked);
        Assert.Equal(SecurityBlockStage.ReadingEvidence, blocked!.Stage);
        Assert.Equal(@"C:\caso\adjunto.html", blocked.Path);
        Assert.Equal(225, blocked.NativeErrorCode);
        Assert.Same(native, blocked.InnerException);
        Assert.True(SecuritySoftwareInterference.IsBlocked(blocked));
    }

    [Fact]
    public void Wrap_OrdinaryError_ReturnsNull()
    {
        var ordinary = new IOException("El disco está lleno.", HResultFromWin32(112));

        Assert.Null(SecuritySoftwareBlockedException.Wrap(SecurityBlockStage.WritingPackage, "x", ordinary));
    }

    [Fact]
    public void Wrap_AlreadyWrapped_KeepsTheFirstStage()
    {
        // La etapa más interna es la más precisa: una capa de afuera no debe pisarla.
        var inner = new SecuritySoftwareBlockedException(
            SecurityBlockStage.ReadingEvidence, "evidencia.html",
            new IOException("x", HResultFromWin32(225)));

        var rewrapped = SecuritySoftwareBlockedException.Wrap(SecurityBlockStage.WritingPackage, "salida.zip", inner);

        Assert.Same(inner, rewrapped);
    }

    [Theory]
    [InlineData(SecurityBlockStage.ReadingEvidence, 225, "error_security_blocked_read")]
    [InlineData(SecurityBlockStage.WritingPackage, 225, "error_security_blocked_write")]
    [InlineData(SecurityBlockStage.RereadingPackage, 225, "error_security_blocked_reread")]
    [InlineData(SecurityBlockStage.RereadingPackage, 226, "error_security_blocked_quarantined")]
    [InlineData(SecurityBlockStage.Sealing, 225, "error_security_blocked_seal")]
    public void Describe_MessageDependsOnStage_AndLogKeepsFileAndCode(
        SecurityBlockStage stage, int errorCode, string expectedKey)
    {
        var localization = new LocalizationService();
        var blocked = new SecuritySoftwareBlockedException(
            stage, @"D:\salida\caso.zip", new IOException("Mensaje de Windows.", HResultFromWin32(errorCode)));

        var (message, _, detail) = SecuritySoftwareInterference.Describe(blocked, localization);

        Assert.Equal(string.Format(localization.Get(expectedKey), @"D:\salida\caso.zip"), message);
        Assert.Contains(@"D:\salida\caso.zip", detail);
        Assert.Contains(errorCode.ToString(), detail);
        Assert.Contains("Mensaje de Windows.", detail);
    }

    private static int HResultFromWin32(int errorCode) =>
        unchecked((int)(0x80070000u | (uint)errorCode));
}
