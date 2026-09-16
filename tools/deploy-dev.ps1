<#
.SYNOPSIS
Builds Dev Pane and installs it on this PC for testing.

.DESCRIPTION
The Widgets Board keeps files open inside an installed package's folder, so each build is copied to a fresh
folder under .deploy and installed from there. Each copy gets a higher revision number, so it installs as an
update and your pinned cards stay put. Old copies are cleaned up once nothing holds them open.

Requires Developer Mode (Settings > System > Advanced > Developer Mode).

.PARAMETER Clean
Uninstalls the previous copy first. Pinned Dev Pane cards are removed and need to be pinned again.

.PARAMETER RestartWidgets
Restarts the Widgets Board so it picks up added, removed, or renamed cards.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\deploy-dev.ps1
#>
param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $Clean,

    [switch] $RestartWidgets
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "src\DevPane.Widgets\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0"
$deployRoot = Join-Path $root '.deploy'
$layout = Join-Path $deployRoot ("$Platform-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$revisionFile = Join-Path $deployRoot 'revision.txt'

Write-Host 'Stopping the widget provider...'
Get-Process -Name 'DevPane.Widgets' -ErrorAction SilentlyContinue | Stop-Process -Force

if ($Clean) {
    Write-Host 'Uninstalling the previous copy...'
    Get-AppxPackage -Name 'DevPane' | Remove-AppxPackage
}

if ($RestartWidgets) {
    Write-Host 'Restarting the Widgets Board...'
    Get-Process -Name 'WidgetBoard', 'WidgetService' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

Write-Host 'Building...'
dotnet build (Join-Path $root 'DevPane.slnx') -p:Platform=$Platform -c $Configuration -nologo -v:minimal
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed. If the error mentions resources.pri, run again with -RestartWidgets.'
}

Write-Host 'Cleaning up old copies...'
Get-ChildItem -LiteralPath $deployRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
    }
    catch {
        # Still in use by the installed package or the Widgets Board; removed on a later run.
    }
}

Write-Host "Copying the build to $layout..."
New-Item -ItemType Directory -Force -Path $deployRoot | Out-Null
Copy-Item -LiteralPath $output -Destination $layout -Recurse

# Give this copy a higher revision than the last one, so Windows treats it as an update.
$revision = 1
if (Test-Path -LiteralPath $revisionFile) {
    $revision = [int](Get-Content -LiteralPath $revisionFile -Raw) + 1
}
Set-Content -LiteralPath $revisionFile -Value $revision

$manifestPath = Join-Path $layout 'AppxManifest.xml'
[xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
$version = [version] $manifest.Package.Identity.Version
$manifest.Package.Identity.Version = '{0}.{1}.{2}.{3}' -f $version.Major, $version.Minor, $version.Build, $revision
$manifest.Save($manifestPath)

Write-Host "Installing version $($manifest.Package.Identity.Version)..."
try {
    Add-AppxPackage -Register $manifestPath -ForceApplicationShutdown
}
catch {
    Write-Host "Couldn't install as an update ($($_.Exception.Message.Trim())). Reinstalling; pin your cards again afterwards."
    Get-AppxPackage -Name 'DevPane' | Remove-AppxPackage
    Add-AppxPackage -Register $manifestPath
}

Write-Host 'Done. Open the Widgets Board (Win + W) to see the changes.'
