<#
.SYNOPSIS
Builds the package to upload to the Microsoft Store.

.DESCRIPTION
Produces a single .msixupload holding x64 and ARM64 builds, which is what Partner Center wants. The package is
left unsigned on purpose: the Store re-signs it with Microsoft's certificate after it passes certification, and
a signature of your own would only clash with that. It can't be installed by double-clicking for the same
reason, so keep using tools\deploy-dev.ps1 to test on this PC.

Before the first run, replace the REPLACEME values in src\DevPane.Widgets\Package.appxmanifest with the app
identity from Partner Center. This script stops if they're still there. docs\microsoft-store.md has the walkthrough.

Needs Visual Studio with the WinUI application development workload; the .NET SDK on its own can't build MSIX bundles.

.PARAMETER Version
Sets the package version before building, as Major.Minor.Build.0. The Store owns the fourth part, so it has to
stay 0. Every submission needs a higher version than the last one. Leave this out to build the version that's
already in the manifest.

.PARAMETER Configuration
Debug builds are for reading crash dumps, never for submission.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\pack-store.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\pack-store.ps1 -Version 1.0.1.0
#>
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string] $Version,

    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# These are all things the person running the script has to go and fix, so they're reported as plain text
# rather than as a PowerShell error with a stack trace over the instructions.
function Fail([string] $message) {
    Write-Host ''
    Write-Host $message -ForegroundColor Red
    Write-Host ''
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\DevPane.Widgets\DevPane.Widgets.csproj'
$manifestPath = Join-Path $root 'src\DevPane.Widgets\Package.appxmanifest'
$output = Join-Path $root 'AppPackages'

# Both architectures go in one bundle, so a single submission covers Intel/AMD and Arm PCs. The bundle is
# assembled by the build of the last platform listed, which builds the others itself.
$platforms = 'x64|arm64'

$manifestText = Get-Content -LiteralPath $manifestPath -Raw

if ($Version) {
    Write-Host "Setting the package version to $Version..."

    # Edited as text, not through an XmlDocument: saving the parsed document would re-indent the whole manifest
    # and bury a one-attribute change in a diff touching every line.
    $updated = [regex]::Replace($manifestText, '(<Identity\b[^>]*?\bVersion=")[^"]*(")', "`${1}$Version`${2}", 1)
    if ($updated -eq $manifestText) {
        Fail "Couldn't find the Version attribute of <Identity> in $manifestPath."
    }

    Set-Content -LiteralPath $manifestPath -Value $updated -NoNewline -Encoding utf8
    $manifestText = $updated
}

[xml] $manifest = $manifestText
$identity = $manifest.Package.Identity
$publisherDisplayName = $manifest.Package.Properties.PublisherDisplayName

# Catching this here is worth it: MakeAppx's own error for a placeholder is a schema pattern violation with a
# line number, which says nothing about Partner Center.
$placeholders = @(
    @{ Name = 'Identity/@Name'; Value = $identity.Name }
    @{ Name = 'Identity/@Publisher'; Value = $identity.Publisher }
    @{ Name = 'Properties/PublisherDisplayName'; Value = $publisherDisplayName }
) | Where-Object { $_.Value -match 'REPLACEME' }

if ($placeholders) {
    $names = ($placeholders | ForEach-Object { $_.Name }) -join ', '
    Fail @"
$manifestPath still has placeholder values in: $names

Copy the real ones from Partner Center > Dev Pane > Product management > Product identity:
  Package/Identity/@Name            <- "Package/Identity/Name"
  Package/Identity/@Publisher       <- "Package/Identity/Publisher"
  Package/Properties/PublisherDisplayName <- "Package/Properties/PublisherDisplayName"

They're case-sensitive, and Partner Center rejects the upload if they don't match exactly.
See docs\microsoft-store.md.
"@
}

$packageVersion = [version] $identity.Version
if ($packageVersion.Revision -ne 0) {
    Fail "Version $($identity.Version) doesn't end in 0. The Store reserves the fourth part of the version for itself."
}

if ($packageVersion.Major -eq 0) {
    Fail "Version $($identity.Version) starts with 0. The Store won't take a package whose first number is 0."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    Fail 'Visual Studio not found. The MSIX bundle targets ship with Visual Studio, not with the .NET SDK.'
}

$installation = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $installation) {
    Fail 'No Visual Studio installation with MSBuild. Install the WinUI application development workload.'
}

# The 64-bit MSBuild, not the 32-bit one Visual Studio puts first. The symbol packaging targets look for
# mspdbcmf.exe under bin\Host<PROCESSOR_ARCHITECTURE>, so 32-bit MSBuild sends them to a HostX86 folder that
# isn't installed and the symbol step then fails the build.
$msbuild = Join-Path $installation 'MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) {
    $msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
}

if (-not (Test-Path -LiteralPath $msbuild)) {
    Fail "MSBuild not found under $installation."
}

# Crash reports in Partner Center need a symbol package, built by mspdbcmf.exe from the C++ workload. Looking
# for it in the same place the packaging targets do: finding it elsewhere wouldn't help, because they build the
# path themselves and fail the whole build when nothing is there.
$symbols = $null -ne (Get-ChildItem -Path (Join-Path $installation 'VC\Tools\MSVC\*\bin\Host*\x64\mspdbcmf.exe') -ErrorAction SilentlyContinue | Select-Object -First 1)
if (-not $symbols) {
    Write-Warning 'mspdbcmf.exe not found, so the upload gets no symbol package and Partner Center will show no crash analytics. Add "Desktop development with C++" in the Visual Studio Installer to include it.'
}

Write-Host "Building Dev Pane $($identity.Version) for $platforms..."
Write-Host "  Identity  : $($identity.Name)"
Write-Host "  Publisher : $($identity.Publisher)"

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

# Packages from an earlier run stay in the build folders under their old names, and the bundler picks up every
# .msix it finds there. One left over from a different version or identity fails the build with a package
# family name mismatch, so they go before each run. /t:Rebuild alone doesn't remove them.
foreach ($platform in 'x64', 'arm64') {
    $binary = Join-Path $root "src\DevPane.Widgets\bin\$platform\$Configuration"
    if (Test-Path -LiteralPath $binary) {
        Get-ChildItem -LiteralPath $binary -Recurse -Include '*.msix', '*.msixbundle', '*.msixupload' -File -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $binary -Recurse -Directory -Filter 'Upload' -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    # The generated copy of the manifest. Deleting it forces the identity in Package.appxmanifest to be picked
    # up: an incremental build has been seen to keep an older one, which then fails the bundle as a mismatch.
    Get-ChildItem -Path (Join-Path $root "src\DevPane.Widgets\obj\$platform\$Configuration") -Recurse -Filter 'AppxManifest.xml' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

# ARM64 is built by the x64 build below, but its packages have to be restored first.
& $msbuild $project '/t:Restore' "/p:Configuration=$Configuration" '/p:Platform=ARM64' '-nologo' '-v:quiet'
if ($LASTEXITCODE -ne 0) { Fail 'Restore failed for ARM64.' }

& $msbuild $project '/t:Restore' "/p:Configuration=$Configuration" '/p:Platform=x64' '-nologo' '-v:quiet'
if ($LASTEXITCODE -ne 0) { Fail 'Restore failed for x64.' }

$arguments = @(
    $project
    '/t:Rebuild'                                   # A stale generated manifest would keep an old identity.
    "/p:Configuration=$Configuration"
    '/p:Platform=x64'
    '/p:GenerateAppxPackageOnBuild=true'
    '/p:AppxBundle=Always'
    "/p:AppxBundlePlatforms=$platforms"
    '/p:UapAppxPackageBuildMode=StoreUpload'       # Produces the .msixupload Partner Center asks for.
    '/p:AppxPackageSigningEnabled=false'           # The Store signs it; see the note at the top.
    "/p:AppxSymbolPackageEnabled=$($symbols.ToString().ToLowerInvariant())"
    "/p:AppxPackageDir=$output\"                   # The trailing slash matters: MSBuild joins it as-is.
    '-nologo'
    '-v:minimal'
)

& $msbuild $arguments
if ($LASTEXITCODE -ne 0) { Fail 'Build failed.' }

$upload = Get-ChildItem -LiteralPath $output -Filter '*.msixupload' -File | Select-Object -First 1
if (-not $upload) {
    Fail "The build finished but produced no .msixupload in $output."
}

Write-Host ''
Write-Host "Ready to upload: $($upload.FullName)"
Write-Host ("  {0:N1} MB, x64 and ARM64" -f ($upload.Length / 1MB))
Write-Host ''
Write-Host 'Next: Partner Center > Dev Pane > Submissions > Packages, and drop that file in.'
Write-Host 'The sideload copy next to it is for testing on another PC and is not what the Store wants.'
