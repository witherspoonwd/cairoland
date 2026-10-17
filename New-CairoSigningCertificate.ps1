<#
.SYNOPSIS
    Creates the code signing certificate that Build-Installer.ps1 signs CairoDesktop.UIAccess.exe with, and
    trusts it on this computer. Run it once per computer that builds or runs the installer.

.DESCRIPTION
    Windows only grants UI access (uiAccess="true") to an exe signed by a certificate that chains to the
    computer's Trusted Root store. This script:

    1. Creates a self-signed code signing certificate in your user store (Cert:\CurrentUser\My), with a
       private key that can't be exported. Reuses it if it already exists.
    2. Adds the public certificate to the computer's Trusted Root and Trusted Publishers stores. This step
       asks for administrator approval (UAC).

    Anything signed with this certificate is trusted for code signing on this computer, so keep it to this
    user account. To undo, delete it from certlm.msc (Trusted Root Certification Authorities and Trusted
    Publishers) and certmgr.msc (Personal).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\New-CairoSigningCertificate.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Build-Installer.ps1 finds the certificate by this subject
$subject = 'CN=Cairo Local UIAccess Signing'

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($cert) {
    Write-Host "Using the existing certificate $($cert.Thumbprint) (expires $($cert.NotAfter.ToShortDateString()))"
}
else {
    Write-Host 'Creating the signing certificate...'
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(10)
    Write-Host "Created $($cert.Thumbprint)"
}

$trusted = (Test-Path "Cert:\LocalMachine\Root\$($cert.Thumbprint)") -and (Test-Path "Cert:\LocalMachine\TrustedPublisher\$($cert.Thumbprint)")
if ($trusted) {
    Write-Host 'The certificate is already trusted on this computer.' -ForegroundColor Green
    return
}

# Only the public part goes to the machine stores
$cerPath = Join-Path $env:TEMP "CairoUIAccessSigning-$($cert.Thumbprint).cer"
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

try {
    $script = @"
`$ErrorActionPreference = 'Stop'
Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))

    Write-Host 'Trusting the certificate on this computer (approve the administrator prompt)...'
    try {
        $process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden `
            -ArgumentList '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded
    }
    catch {
        Write-Error "The administrator prompt was cancelled, so the certificate is not trusted yet. Run this script again."
    }

    if ($process.ExitCode -ne 0 -or -not (Test-Path "Cert:\LocalMachine\Root\$($cert.Thumbprint)")) {
        Write-Error "Unable to add the certificate to the computer's stores (exit code $($process.ExitCode))."
    }
}
finally {
    Remove-Item -LiteralPath $cerPath -ErrorAction SilentlyContinue
}

Write-Host 'The certificate is trusted. Build-Installer.ps1 will now sign the UI access build.' -ForegroundColor Green
