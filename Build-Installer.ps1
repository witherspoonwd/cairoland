<#
.SYNOPSIS
    Builds the Cairo installer locally, the same way CI does, into artifacts\installer (git-ignored).

.DESCRIPTION
    1. Publishes the app with the chosen publish profile (Release, self-contained for .NET builds).
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
    [switch]$NoPublish
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
& $makensis /V2 $profileScript
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
