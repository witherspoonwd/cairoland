<#
.SYNOPSIS
    Builds the Cairo installer locally, the same way CI does, into artifacts\installer (git-ignored).

.DESCRIPTION
    1. Publishes the app with the chosen publish profile (Release, self-contained for .NET builds).
       Adds CairoDesktop.UIAccess.exe: a copy of CairoDesktop.exe whose manifest asks for UI access, signed
       with the certificate from New-CairoSigningCertificate.ps1. CairoDesktop.exe hands off to it at startup,
       which lets Cairo manage windows running as administrator.
    2. Stages a copy of the Installer folder under artifacts\installer\staging, stamping the version and
       pointing it at the published files. The tracked .nsi files are not modified.
    3. Compiles it with NSIS (makensis) and puts the setup exe in artifacts\installer.

    Requires NSIS. Install it with -InstallNsis (uses winget) or from https://nsis.sourceforge.io.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Build-Installer.ps1

.EXAMPLE
    .\Build-Installer.ps1 -PublishProfile x64_net480 -InstallNsis
#>
[CmdletBinding()]
param(
    # Matches the CI build matrix and Installer\CairoShell_<profile>.nsi.
    [ValidateSet('x64', 'x86', 'x64_net480', 'x86_net480', 'arm64')]
    [string]$PublishProfile = 'x64',

    # Install NSIS with winget if makensis can't be found.
    [switch]$InstallNsis,

    # Reuse the existing publish output instead of publishing again.
    [switch]$NoPublish,

    # Leave out CairoDesktop.UIAccess.exe, so Cairo can't manage windows running as administrator.
    [switch]$NoUIAccess
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$appDir = Join-Path $repoRoot 'Cairo Desktop\Cairo Desktop'
$installerDir = Join-Path $repoRoot 'Installer'
$outputDir = Join-Path $repoRoot 'artifacts\installer'
$stagingDir = Join-Path $outputDir 'staging'

# Same platform per profile as .github\workflows\build.yml
$platforms = @{ 'x64' = 'x64'; 'x86' = 'AnyCPU'; 'x64_net480' = 'x64'; 'x86_net480' = 'AnyCPU'; 'arm64' = 'ARM64' }

function Find-MakeNsis {
    $command = Get-Command makensis -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($root) {
            $candidate = Join-Path $root 'NSIS\makensis.exe'
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }

    return $null
}

function Get-SigningCertificate {
    # created by New-CairoSigningCertificate.ps1
    $subject = 'CN=Cairo Local UIAccess Signing'
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
        Where-Object { Test-Path "Cert:\LocalMachine\Root\$($_.Thumbprint)" } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        Write-Error "No trusted signing certificate found. Run .\New-CairoSigningCertificate.ps1 once, or build with -NoUIAccess."
    }

    return $cert
}

# Replaces an exe's application manifest (RT_MANIFEST, ID 1) in place
function Set-ExeManifest([string]$path, [string]$manifest) {
    if (-not ('CairoBuild.ResourceEditor' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CairoBuild
{
    public static class ResourceEditor
    {
        private static readonly IntPtr RT_MANIFEST = new IntPtr(24);
        private static readonly IntPtr MANIFEST_ID = new IntPtr(1);
        private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;

        private delegate bool EnumResLangProc(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr param);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumResLangProc proc, IntPtr param);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string fileName, bool deleteExisting);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr update, bool discard);

        public static List<ushort> GetManifestLanguages(string path)
        {
            List<ushort> languages = new List<ushort>();
            IntPtr module = LoadLibraryEx(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (module == IntPtr.Zero) throw new Win32Exception();
            try
            {
                EnumResourceLanguages(module, RT_MANIFEST, MANIFEST_ID, (m, t, n, language, p) => { languages.Add(language); return true; }, IntPtr.Zero);
            }
            finally
            {
                FreeLibrary(module);
            }
            return languages;
        }

        public static void ReplaceManifest(string path, byte[] manifest)
        {
            List<ushort> languages = GetManifestLanguages(path);
            IntPtr update = BeginUpdateResource(path, false);
            if (update == IntPtr.Zero) throw new Win32Exception();

            // remove every existing language so Windows can't pick up the old manifest
            foreach (ushort language in languages)
            {
                if (!UpdateResource(update, RT_MANIFEST, MANIFEST_ID, language, null, 0))
                {
                    int error = Marshal.GetLastWin32Error();
                    EndUpdateResource(update, true);
                    throw new Win32Exception(error);
                }
            }

            ushort newLanguage = languages.Count > 0 ? languages[0] : (ushort)0;
            if (!UpdateResource(update, RT_MANIFEST, MANIFEST_ID, newLanguage, manifest, (uint)manifest.Length))
            {
                int error = Marshal.GetLastWin32Error();
                EndUpdateResource(update, true);
                throw new Win32Exception(error);
            }

            if (!EndUpdateResource(update, false)) throw new Win32Exception();
        }
    }
}
'@
    }

    [CairoBuild.ResourceEditor]::ReplaceManifest($path, [Text.Encoding]::UTF8.GetBytes($manifest))
}

function New-UIAccessCopy([string]$exe, $cert) {
    $directory = Split-Path -Parent $exe
    $copy = Join-Path $directory 'CairoDesktop.UIAccess.exe'

    # For .NET builds CairoDesktop.exe is the app host, which loads CairoDesktop.dll whatever its own name is.
    # For .NET Framework builds it's the app itself, and needs its config under the new name.
    Copy-Item -LiteralPath $exe -Destination $copy -Force
    $config = "$exe.config"
    if (Test-Path $config) {
        Copy-Item -LiteralPath $config -Destination "$copy.config" -Force
    }

    $manifest = [IO.File]::ReadAllText((Join-Path $appDir 'app.manifest'))
    if ($manifest -notmatch 'uiAccess="false"') {
        Write-Error 'app.manifest has no uiAccess="false" to switch on.'
    }
    Set-ExeManifest $copy ($manifest -replace 'uiAccess="false"', 'uiAccess="true"')

    $signature = Set-AuthenticodeSignature -FilePath $copy -Certificate $cert -HashAlgorithm SHA256
    if ($signature.Status -ne 'Valid') {
        Write-Error "Signing $copy failed: $($signature.Status) $($signature.StatusMessage)"
    }

    Write-Host "Added $copy (UI access, signed by $($cert.Thumbprint))"
}

function Get-XmlValue([string]$path, [string]$name) {
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    $node = $xml.Project.PropertyGroup | ForEach-Object { $_.$name } | Where-Object { $_ } | Select-Object -First 1
    if (-not $node) {
        throw "$name not found in $path"
    }
    return $node
}

# Find NSIS first so a missing tool fails fast
$makensis = Find-MakeNsis
if (-not $makensis -and $InstallNsis) {
    Write-Host 'Installing NSIS with winget...'
    winget install --id NSIS.NSIS --exact --accept-source-agreements --accept-package-agreements
    $makensis = Find-MakeNsis
}

if (-not $makensis) {
    Write-Error "NSIS (makensis.exe) was not found. Re-run with -InstallNsis, or run 'winget install NSIS.NSIS'."
}

Write-Host "Using NSIS: $makensis"

# Find the certificate before publishing, so a missing one fails fast too
$signingCert = $null
if (-not $NoUIAccess) {
    $signingCert = Get-SigningCertificate
    Write-Host "Signing with: $($signingCert.Thumbprint)"
}

# Publish settings come from the publish profile
$pubxml = Join-Path $appDir "Properties\PublishProfiles\$PublishProfile.pubxml"
$framework = Get-XmlValue $pubxml 'TargetFramework'
$publishDir = Join-Path $appDir (Get-XmlValue $pubxml 'PublishDir')
$platform = $platforms[$PublishProfile]

if (-not $NoPublish) {
    # start clean so files from removed projects can't end up in the installer
    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    Write-Host "Publishing $PublishProfile ($framework, $platform)..."
    Push-Location $appDir
    try {
        & dotnet publish -p:PublishProfile=$PublishProfile -p:Platform=$platform -f $framework -nologo
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Publish failed (exit code $LASTEXITCODE)."
        }
    }
    finally {
        Pop-Location
    }
}

$exe = Join-Path $publishDir 'CairoDesktop.exe'
if (-not (Test-Path $exe)) {
    Write-Error "Published CairoDesktop.exe not found at $exe."
}

$uiAccessCopy = Join-Path $publishDir 'CairoDesktop.UIAccess.exe'
if ($NoUIAccess) {
    # don't let a copy from an earlier -NoPublish run slip into the installer
    Remove-Item -LiteralPath $uiAccessCopy, "$uiAccessCopy.config" -ErrorAction SilentlyContinue
}
else {
    New-UIAccessCopy $exe $signingCert
}

# Nerdbank.GitVersioning stamps the file version; CI uses its major.minor.build ("SimpleVersion")
$fileVersion = [Version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion
$version = "$($fileVersion.Major).$($fileVersion.Minor).$($fileVersion.Build)"
Write-Host "Version: $version"

# Stage a patched copy of the installer scripts
if (Test-Path $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
Copy-Item -Path (Join-Path $installerDir '*') -Destination $stagingDir -Recurse

$mainScript = Join-Path $stagingDir 'CairoShell.nsi'
$text = [IO.File]::ReadAllText($mainScript)
$text = $text.Replace('BUILD_VERSION', $version)
# the script refers to the published files relative to the Installer folder; point it at the real location
$text = $text.Replace('"..\Cairo Desktop\', '"' + $repoRoot + '\Cairo Desktop\')
[IO.File]::WriteAllText($mainScript, $text, (New-Object Text.UTF8Encoding($true)))

# Compile
$profileScript = Join-Path $stagingDir "CairoShell_$PublishProfile.nsi"
Write-Host "Compiling $profileScript..."
# the Shell Launcher scripts (Enable-, Disable-CairoShellLauncher.ps1, ...) live in the repo root
& $makensis /V2 "/DSETUPSCRIPTS=$repoRoot\" $profileScript
if ($LASTEXITCODE -ne 0) {
    Write-Error "makensis failed (exit code $LASTEXITCODE)."
}

# Move the result next to the staging folder, named with the version and commit
$built = Get-ChildItem -LiteralPath $stagingDir -Filter '*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $built) {
    Write-Error 'makensis succeeded but no installer exe was produced.'
}

$commit = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
$suffix = if ($commit) { "$version-$commit" } else { $version }
$destination = Join-Path $outputDir "$($built.BaseName)-$suffix.exe"
Move-Item -LiteralPath $built.FullName -Destination $destination -Force
Remove-Item -LiteralPath $stagingDir -Recurse -Force

Write-Host ''
Write-Host "Installer: $destination" -ForegroundColor Green
