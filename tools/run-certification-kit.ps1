<#
.SYNOPSIS
Runs the Windows App Certification Kit on the package tools\pack-store.ps1 built, and says what failed and why.

.DESCRIPTION
The kit can't be pointed at either copy of Dev Pane you'd expect:

  - The copy tools\deploy-dev.ps1 installs is a development-mode registration of a folder. The kit can't find its
    manifest, then marks every test it never ran as failed.
  - The Store package is deliberately unsigned, because the Store signs it, and to test a packaged desktop app the
    kit has to install it.

So this makes a test copy of the x64 package that Windows 11 will install unsigned: the same files, with the OID that
Windows requires of unsigned packages added to the publisher. That OID is also what keeps the copy from ever being
mistaken for, or replacing, the signed Store package or your own Dev Pane install. The copy is installed, tested and
uninstalled again. No certificate is created and nothing is added to the certificate store.

Unsigned packages that contain programs install for all users, so this needs administrator rights. Run from a normal
PowerShell, it asks once, waits for the kit to finish (a few minutes), and prints the result in the same window.

.PARAMETER Bundle
The .msixbundle to test. Defaults to the newest one tools\pack-store.ps1 left in AppPackages.

.PARAMETER LogPath
Used when the script relaunches itself with administrator rights, to hand its output back. Not for direct use.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\run-certification-kit.ps1
#>
param(
    [string] $Bundle,

    [string] $LogPath
)

$ErrorActionPreference = 'Stop'

# Unsigned packages must carry this in their publisher, or Windows won't install them.
$UnsignedPublisherOid = 'OID.2.25.311729368913984317654407730594956997722=1'

function Say([string] $text, [string] $color) {
    if ($color) { Write-Host $text -ForegroundColor $color } else { Write-Host $text }
    if ($LogPath) { Add-Content -LiteralPath $LogPath -Value $text -Encoding utf8 }
}

# Runs a native program and returns everything it printed as text. Windows PowerShell turns each line a native
# program writes to stderr into an error record, which $ErrorActionPreference = 'Stop' would make fatal; the exit
# code is what decides success here, so stderr is collected like any other output.
function Invoke-Native([scriptblock] $command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $command 2>&1 | ForEach-Object { "$_" }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdministrator = ([Security.Principal.WindowsPrincipal] $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# --- Not elevated: run again elevated, wait for it, and show its output here --------------------------------------
# Launching appcert.exe from a normal shell elevates it in a window of its own that PowerShell doesn't wait for, so
# the prompt comes straight back, the kit's messages vanish with its window, and the report on disk is an old one.
if (-not $isAdministrator) {
    $log = Join-Path ([IO.Path]::GetTempPath()) ('devpane-certification-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.log')
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-LogPath', "`"$log`"")
    if ($Bundle) { $arguments += @('-Bundle', "`"$Bundle`"") }

    Write-Host 'The certification kit needs administrator rights. Approve the prompt, then wait: the kit takes a few minutes.'
    try {
        $child = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    }
    catch {
        Write-Host ''
        Write-Host 'The administrator prompt was declined, so the kit did not run.' -ForegroundColor Red
        exit 1
    }

    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Encoding utf8 | ForEach-Object {
            $color = if ($_ -match '^\s*\[FAIL\].*optional|^\s*\[WARNING\]|^Result: WARNING') { 'Yellow' }
                elseif ($_ -match '^\s*\[(FAIL|ERROR)\]|^Result: FAIL|^Stopped:') { 'Red' }
                elseif ($_ -match '^Result: PASS') { 'Green' }
                else { $null }
            if ($color) { Write-Host $_ -ForegroundColor $color } else { Write-Host $_ }
        }
        Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Host 'The elevated run ended without writing any output.' -ForegroundColor Red
    }

    exit $child.ExitCode
}

# --- Elevated --------------------------------------------------------------------------------------------------
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('devpane-certification-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$report = Join-Path ([IO.Path]::GetTempPath()) 'devpane-wack.xml'
$testPublisher = $null
$packageName = $null
$exitCode = 1

try {
    if (-not $Bundle) {
        $Bundle = Get-ChildItem (Join-Path $root 'AppPackages') -Recurse -Filter '*.msixbundle' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $Bundle -or -not (Test-Path -LiteralPath $Bundle)) { throw 'No package to test. Run tools\pack-store.ps1 first.' }

    $appcert = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
    if (-not (Test-Path -LiteralPath $appcert)) { throw 'The Windows App Certification Kit is not installed. It comes with the Windows SDK.' }

    $makeappx = @(Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\*\x64\makeappx.exe') -ErrorAction SilentlyContinue) +
        @(Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe') -ErrorAction SilentlyContinue) |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $makeappx) { throw 'makeappx.exe not found. It comes with the Windows SDK.' }

    Say "Testing $Bundle"
    Say "  built $((Get-Item -LiteralPath $Bundle).LastWriteTime)"
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    # The kit runs on this PC, so it tests the x64 package out of the bundle.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $source = $null
    $zip = [IO.Compression.ZipFile]::OpenRead($Bundle)
    try {
        $entry = $zip.Entries | Where-Object { $_.Name -like '*_x64.msix' } | Select-Object -First 1
        if ($entry) {
            $source = Join-Path $work $entry.Name
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $source)
        }
    }
    finally {
        $zip.Dispose()
    }
    if (-not $source) { throw "$Bundle has no x64 package in it." }

    # Unpack, add the unsigned-package OID to the publisher, and pack again.
    $unpacked = Join-Path $work 'package'
    $output = Invoke-Native { & $makeappx unpack /p $source /d $unpacked /o }
    if ($LASTEXITCODE -ne 0) { throw "makeappx couldn't unpack the package:`n$($output -join "`n")" }

    $manifestPath = Join-Path $unpacked 'AppxManifest.xml'
    $manifestText = [IO.File]::ReadAllText($manifestPath)
    $identityMatch = [regex]::Match($manifestText, '<Identity\b[^>]*>')
    $packageName = [regex]::Match($identityMatch.Value, '\bName="([^"]*)"').Groups[1].Value
    $publisher = [regex]::Match($identityMatch.Value, '\bPublisher="([^"]*)"').Groups[1].Value
    $testPublisher = "$publisher, $UnsignedPublisherOid"
    $testIdentity = $identityMatch.Value.Replace("Publisher=`"$publisher`"", "Publisher=`"$testPublisher`"")
    [IO.File]::WriteAllText($manifestPath, $manifestText.Replace($identityMatch.Value, $testIdentity), [Text.UTF8Encoding]::new($false))

    # makeappx writes these itself, and won't pack a folder that already has them.
    foreach ($generated in 'AppxBlockMap.xml', 'AppxSignature.p7x', '[Content_Types].xml', 'AppxMetadata') {
        Remove-Item -LiteralPath (Join-Path $unpacked $generated) -Recurse -Force -ErrorAction SilentlyContinue
    }

    $testPackage = Join-Path $work 'DevPane-certification-test.msix'
    $output = Invoke-Native { & $makeappx pack /d $unpacked /p $testPackage /o }
    if ($LASTEXITCODE -ne 0) { throw "makeappx couldn't pack the test copy:`n$($output -join "`n")" }

    # A run that was stopped half way may have left its copy installed.
    Get-AppxPackage -AllUsers -Name $packageName | Where-Object { $_.Publisher -eq $testPublisher } |
        ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -AllUsers }

    Say 'Installing the test copy...'
    Add-AppxPackage -Path $testPackage -AllowUnsigned
    $installed = Get-AppxPackage -AllUsers -Name $packageName | Where-Object { $_.Publisher -eq $testPublisher } | Select-Object -First 1
    if (-not $installed) { throw 'The test copy reported installing, but Windows has no record of it.' }

    # A fresh report every time: reading an old one is how a run that never happened looks like a failure.
    Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
    Say "Running the certification kit on $($installed.PackageFullName). This takes a few minutes..."
    Invoke-Native { & $appcert reset } | Out-Null
    $kitOutput = Invoke-Native { & $appcert test -packagefullname $installed.PackageFullName -reportoutputpath $report }

    if (-not (Test-Path -LiteralPath $report)) {
        throw "The kit finished without writing a report. What it printed:`n$(($kitOutput | Select-Object -Last 20) -join "`n")"
    }

    [xml] $xml = Get-Content -LiteralPath $report -Raw
    $overall = $xml.REPORT.OVERALL_RESULT
    $messages = @($xml.SelectNodes('//TEST/MESSAGES/MESSAGE') | ForEach-Object { $_.TEXT })

    # When the kit can't read the package it still writes a report, failing every test with one of these. The
    # report's APP_NAME is no guide: the kit leaves it empty for packaged desktop apps even on a good run.
    $unreadable = $messages | Where-Object { $_ -match 'manifest file for this app package could not be found|path is not of a legal form' } | Select-Object -First 1
    if ($unreadable) {
        Say ''
        Say "Stopped: the kit couldn't read the package, so its failures don't describe Dev Pane."
        Say "  The kit said: $(($unreadable -replace '\s+', ' ').Trim())"
        Say "  Report: $report"
        throw 'The kit could not test the package.'
    }

    $tests = @($xml.SelectNodes('//TEST'))
    $notPassing = @($tests | Where-Object { $_.RESULT.InnerText -ne 'PASS' })
    $verdict = switch ($overall) {
        'PASS' { 'every test passed' }
        'FAIL' { 'a required test failed; certification would fail too' }
        default { 'passes; nothing below blocks certification' }
    }

    Say ''
    Say "Result: $overall - $verdict"
    Say "  $($tests.Count - $notPassing.Count) of $($tests.Count) tests passed"
    foreach ($test in $notPassing) {
        $optional = if ($test.OPTIONAL -eq 'TRUE') { "  (optional: doesn't block certification)" } else { '' }
        Say ("  [{0}] {1}{2}" -f $test.RESULT.InnerText, $test.NAME, $optional)
        foreach ($message in @($test.SelectNodes('MESSAGES/MESSAGE')) | Select-Object -First 3) {
            $text = ($message.TEXT -replace '\s+', ' ').Trim()
            if ($text) { Say ('        ' + $text.Substring(0, [Math]::Min(300, $text.Length))) }
        }
    }
    Say ''
    Say "Report: $report"

    $exitCode = if ($overall -eq 'FAIL') { 1 } else { 0 }
}
catch {
    Say ''
    Say "Stopped: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    # Always take the test copy off again, even after a failure part way through.
    if ($packageName -and $testPublisher) {
        foreach ($copy in @(Get-AppxPackage -AllUsers -Name $packageName -ErrorAction SilentlyContinue | Where-Object { $_.Publisher -eq $testPublisher })) {
            try {
                Remove-AppxPackage -Package $copy.PackageFullName -AllUsers -ErrorAction Stop
                Say 'Removed the test copy.'
            }
            catch {
                Say "Couldn't remove the test copy $($copy.PackageFullName): $($_.Exception.Message)"
            }
        }
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

exit $exitCode
