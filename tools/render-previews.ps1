<#
.SYNOPSIS
Draws the preview images the Widgets Board shows in its + dialog, from the cards' own code.

.DESCRIPTION
The + dialog shows a 300 x 304 picture of each card at medium size, with separate pictures for light and dark
mode. Hand-drawn pictures drift away from the real cards as the cards change, so this draws them the way the
board does:

  1. A Debug build of Dev Pane writes each card's real template and data, filled from made-up sample data in
     src\DevPane.Widgets\Previews. No real account, repository or number ever appears in a preview.
  2. Each card is rendered the way the Widgets Board renders it: the same Adaptive Cards libraries, the same host
     configs, the same card stylesheet and the same theme colors. All of them are read from the board's install on
     this PC rather than copied into this repository, so the previews follow whatever version of the board Windows
     has.
  3. Microsoft Edge, running headless, saves each card as a PNG with transparent rounded corners, the way the
     picker's guidelines ask for.

Run it after changing how a card looks, and commit the images it writes.

.PARAMETER OutputFolder
Where the images go. Defaults to src\DevPane.Widgets\Assets\Widgets, which the package manifest points at.

.PARAMETER SkipBuild
Uses the existing Debug build instead of building first.

.PARAMETER KeepWork
Leaves the generated pages in the temp folder and prints where, for opening one in a browser to see why a preview
looks wrong.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\render-previews.ps1
#>
param(
    [string] $OutputFolder,

    [switch] $SkipBuild,

    [switch] $KeepWork
)

$ErrorActionPreference = 'Stop'

function Fail([string] $message) {
    Write-Host ''
    Write-Host $message -ForegroundColor Red
    Write-Host ''
    exit 1
}

# Undoes JavaScript string escapes: the stylesheet is stored in the board's script as a quoted string.
function ConvertFrom-JavaScriptString([string] $text) {
    [regex]::Replace($text, '\\(.)', {
        param($match)
        switch -CaseSensitive ($match.Groups[1].Value) {
            'n' { "`n" }
            't' { "`t" }
            default { $match.Groups[1].Value }
        }
    })
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\DevPane.Widgets'
$manifestPath = Join-Path $project 'Package.appxmanifest'
if (-not $OutputFolder) {
    $OutputFolder = Join-Path $project 'Assets\Widgets'
}

# The medium card's size. The board's stylesheet asks for it as a placeholder.
$cardWidth = 300
$cardHeight = 304

# The board computes accent colors from the user's Windows accent color, so its tables leave them out. These are
# Fluent's values for the default blue accent, for the stylesheet rules that use them.
$accentDefaults = @{
    light = @{
        accentFillDefault = 'rgba(0, 95, 184, 1)'; accentFillSecondary = 'rgba(0, 95, 184, 0.9)'
        accentFillTertiary = 'rgba(0, 95, 184, 0.8)'; accentTextPrimary = 'rgba(0, 62, 146, 1)'
        accentTextSecondary = 'rgba(0, 26, 104, 1)'; accentDisabled = 'rgba(0, 0, 0, 0.2169)'
        textOnAccentPrimary = 'rgba(255, 255, 255, 1)'
    }
    dark = @{
        accentFillDefault = 'rgba(96, 205, 255, 1)'; accentFillSecondary = 'rgba(96, 205, 255, 0.9)'
        accentFillTertiary = 'rgba(96, 205, 255, 0.8)'; accentTextPrimary = 'rgba(153, 235, 255, 1)'
        accentTextSecondary = 'rgba(153, 235, 255, 1)'; accentDisabled = 'rgba(255, 255, 255, 0.1581)'
        textOnAccentPrimary = 'rgba(0, 0, 0, 1)'
    }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('devpane-previews-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $work, (Join-Path $work 'lib'), (Join-Path $work 'pages') | Out-Null

try {
    # --- 1. Each card's template and data, from the cards' own code ---------------------------------------------
    if (-not $SkipBuild) {
        Write-Host 'Building Dev Pane (Debug)...'
        dotnet build (Join-Path $root 'DevPane.slnx') -p:Platform=x64 -c Debug -nologo -v:quiet
        if ($LASTEXITCODE -ne 0) { Fail 'Build failed.' }
    }

    $exe = Get-ChildItem (Join-Path $project 'bin\x64\Debug') -Recurse -Filter 'DevPane.Widgets.exe' | Select-Object -First 1
    if (-not $exe) { Fail 'No Debug build found. Run without -SkipBuild.' }

    $cardData = Join-Path $work 'cards'
    Write-Host 'Writing each card from sample data...'
    # The provider is a windowed app, so PowerShell wouldn't wait for it without Start-Process -Wait.
    $process = Start-Process -FilePath $exe.FullName -ArgumentList 'render-previews', "`"$cardData`"" -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        $details = Join-Path $cardData 'error.txt'
        Fail ("The render-previews command failed." + $(if (Test-Path $details) { "`n`n" + (Get-Content $details -Raw) } else { '' }))
    }

    # --- 2. How the board renders a card -----------------------------------------------------------------------
    $board = Get-AppxPackage -Name 'MicrosoftWindows.Client.WebExperience' | Select-Object -First 1
    if (-not $board) { Fail 'The Widgets Board (Windows Web Experience Pack) is not installed, and the previews are drawn with its renderer.' }

    $web = Join-Path $board.InstallLocation 'Dashboard\WebContent'
    $modules = Join-Path $web 'node_modules'
    $libraries = [ordered]@{
        'adaptive-expressions.js'     = 'adaptive-expressions\lib\browser.js'
        'markdown-it.js'              = 'markdown-it\dist\markdown-it.min.js'
        'adaptivecards.js'            = 'adaptivecards\dist\adaptivecards.min.js'
        'adaptivecards-templating.js' = 'adaptivecards-templating\dist\adaptivecards-templating.min.js'
        'adaptivecards.css'           = 'adaptivecards\dist\adaptivecards.css'
    }
    foreach ($entry in $libraries.GetEnumerator()) {
        $source = Join-Path $modules $entry.Value
        if (-not (Test-Path -LiteralPath $source)) { Fail "The Widgets Board no longer ships $($entry.Value), which this script renders with." }
        Copy-Item -LiteralPath $source -Destination (Join-Path $work "lib\$($entry.Key)")
    }

    # The rest lives inside the board's own scripts, whose file names change with every release, so each piece is
    # found by its shape:
    #   - two host configs, stored as JSON.parse('...') literals: one with light text for dark mode, one with dark
    #     text for light mode;
    #   - the stylesheet the board adds to every card, which makes the Adaptive Card itself the card: its size,
    #     8px corners, 48px of room for the header, and the background;
    #   - the light and dark theme tables that fill the stylesheet's color placeholders.
    $hostConfigs = @{}
    $stylesheet = $null
    $themes = @{}
    foreach ($script in Get-ChildItem (Join-Path $web 'wwwroot') -Recurse -Filter '*.js' -File) {
        $text = [IO.File]::ReadAllText($script.FullName)

        foreach ($match in [regex]::Matches($text, "JSON\.parse\('(\{""supportsInteractivity"".*?)'\)")) {
            $json = ConvertFrom-JavaScriptString $match.Groups[1].Value
            try { $config = $json | ConvertFrom-Json } catch { continue }
            $foreground = $config.containerStyles.default.foregroundColors.default.default
            if (-not $foreground) { continue }

            # #AARRGGBB: light text means the config is for dark mode.
            $theme = if ([Convert]::ToInt32($foreground.Substring($foreground.Length - 6, 2), 16) -gt 128) { 'dark' } else { 'light' }
            if (-not $hostConfigs.ContainsKey($theme)) { $hostConfigs[$theme] = $json }
        }

        if (-not $stylesheet) {
            $match = [regex]::Match($text, "'(\\n\s*\.ac-container\.ac-adaptiveCard\\n\s*\{\\n\s*height: size\.acContainer\.height.*?)(?<!\\)'\s*;", 'Singleline')
            if ($match.Success) { $stylesheet = ConvertFrom-JavaScriptString $match.Groups[1].Value }
        }

        foreach ($match in [regex]::Matches($text, 'cardBackgroundSecondary\s*:\s*"')) {
            # From the key back to the '{' that opens its table, then forward to the matching '}'.
            $depth = 0; $start = $match.Index
            for ($k = $match.Index; $k -ge 0; $k--) {
                if ($text[$k] -eq '}') { $depth++ } elseif ($text[$k] -eq '{') { if ($depth -eq 0) { $start = $k; break }; $depth-- }
            }
            $depth = 0; $end = $start
            for ($k = $start; $k -lt $text.Length; $k++) {
                if ($text[$k] -eq '{') { $depth++ } elseif ($text[$k] -eq '}') { $depth--; if ($depth -eq 0) { $end = $k; break } }
            }

            $colors = @{}
            foreach ($pair in [regex]::Matches($text.Substring($start, $end - $start + 1), '(\w+)\s*:\s*"([^"]*)"')) {
                $colors[$pair.Groups[1].Value] = $pair.Groups[2].Value
            }
            if ($colors['colorScheme'] -in 'light', 'dark' -and -not $themes.ContainsKey($colors['colorScheme'])) {
                $themes[$colors['colorScheme']] = $colors
            }
        }
    }

    foreach ($theme in 'dark', 'light') {
        if (-not $hostConfigs.ContainsKey($theme)) { Fail "Couldn't find the Widgets Board's $theme host config in its scripts." }
        if (-not $themes.ContainsKey($theme)) { Fail "Couldn't find the Widgets Board's $theme theme colors in its scripts." }
    }
    if (-not $stylesheet) { Fail "Couldn't find the stylesheet the Widgets Board adds to each card." }

    # The stylesheet with its placeholders filled the way the board fills them, once per theme.
    $cardStyles = @{}
    foreach ($theme in 'dark', 'light') {
        $colors = $themes[$theme]
        $filled = [regex]::Replace($stylesheet, 'theme\.customSemanticColors\.([A-Za-z]+)', {
            param($match)
            $name = $match.Groups[1].Value
            if ($colors[$name]) { $colors[$name] } elseif ($accentDefaults[$theme][$name]) { $accentDefaults[$theme][$name] } else { 'transparent' }
        })
        $cardStyles[$theme] = $filled.Replace('size.acContainer.height', "${cardHeight}px").Replace('size.acContainer.width', "${cardWidth}px")
    }

    # --- 3. One page per card and theme, saved as a PNG by headless Edge -----------------------------------------
    [xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
    $definitions = @{}
    foreach ($definition in $manifest.SelectNodes("//*[local-name()='Definition']")) {
        $icons = @{}
        foreach ($theme in 'dark', 'light') {
            $mode = if ($theme -eq 'dark') { 'DarkMode' } else { 'LightMode' }
            $icon = $definition.SelectSingleNode("*[local-name()='ThemeResources']/*[local-name()='$mode']/*[local-name()='Icons']/*[local-name()='Icon']")
            if (-not $icon) {
                $icon = $definition.SelectSingleNode("*[local-name()='ThemeResources']/*[local-name()='Icons']/*[local-name()='Icon']")
            }
            $icons[$theme] = Join-Path $project $icon.Path
        }
        $definitions[$definition.Id] = @{ Name = $definition.DisplayName; Icons = $icons }
    }

    $edge = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe' -ErrorAction SilentlyContinue).'(default)'
    if (-not $edge) { $edge = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe' }
    if (-not (Test-Path -LiteralPath $edge)) { Fail 'Microsoft Edge not found. The previews are saved with Edge running headless.' }

    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
    $written = 0
    foreach ($file in Get-ChildItem $cardData -Filter '*.json') {
        $payload = Get-Content -LiteralPath $file.FullName -Raw
        $card = $payload | ConvertFrom-Json
        $id = $card.definition
        $theme = $card.theme
        if (-not $definitions.ContainsKey($id)) { Fail "The manifest has no definition for $id." }

        $icon = 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($definitions[$id].Icons[$theme]))
        $name = [Net.WebUtility]::HtmlEncode($definitions[$id].Name)
        $text = $themes[$theme]['textPrimary']

        # The board draws the header itself, over the 48px the stylesheet leaves at the top of the card. Its position
        # was measured from the board at 100% scaling: a 16px icon 16px in, the title beside it, the menu on the right.
        # JSON is valid JavaScript; "</" is escaped so a string can't end the script tag early.
        $html = @"
<!doctype html>
<html><head><meta charset="utf-8">
<link rel="stylesheet" href="../lib/adaptivecards.css">
<style>
$($cardStyles[$theme])
</style>
<style>
  html, body { margin: 0; padding: 0; background: transparent; }
  body { position: relative; width: ${cardWidth}px; height: ${cardHeight}px; overflow: hidden; }
  .ac-container.ac-adaptiveCard { overflow: hidden; }
  .header { position: absolute; z-index: 1; left: 0; top: 0; width: ${cardWidth}px; height: 48px; }
  .icon { position: absolute; left: 16px; top: 17px; width: 16px; height: 16px; }
  .title { position: absolute; left: 37px; top: 14px; line-height: 20px; font: 14px 'Segoe UI Variable Text', 'Segoe UI', sans-serif; color: $text; white-space: nowrap; }
  .more { position: absolute; right: 19px; top: 22px; width: 14px; height: 4px; }
</style>
<script src="../lib/adaptive-expressions.js"></script>
<script src="../lib/markdown-it.js"></script>
<script src="../lib/adaptivecards.js"></script>
<script src="../lib/adaptivecards-templating.js"></script>
</head><body>
<div class="header">
  <img class="icon" src="$icon" alt="">
  <div class="title">$name</div>
  <svg class="more" viewBox="0 0 14 4"><circle cx="2" cy="2" r="1.4" fill="$text"/><circle cx="7" cy="2" r="1.4" fill="$text"/><circle cx="12" cy="2" r="1.4" fill="$text"/></svg>
</div>
<script>
  const hostConfig = $($hostConfigs[$theme].Replace('</', '<\/'));
  const card = $($payload.Replace('</', '<\/'));
  const expanded = new ACData.Template(card.template).expand({ `$root: card.data });
  const adaptiveCard = new AdaptiveCards.AdaptiveCard();
  adaptiveCard.hostConfig = new AdaptiveCards.HostConfig(hostConfig);
  adaptiveCard.parse(expanded);
  document.body.appendChild(adaptiveCard.render());
</script>
</body></html>
"@
        $page = Join-Path $work "pages\$id.$theme.html"
        [IO.File]::WriteAllText($page, $html, [Text.UTF8Encoding]::new($false))

        $suffix = if ($theme -eq 'dark') { 'Dark' } else { 'Light' }
        $image = Join-Path $OutputFolder "${id}_Screenshot_$suffix.png"
        Remove-Item -LiteralPath $image -ErrorAction SilentlyContinue

        $arguments = @(
            '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check',
            '--force-device-scale-factor=1', '--default-background-color=00000000', "--window-size=$cardWidth,$cardHeight",
            # Grayscale text antialiasing, as the board draws it, rather than ClearType's colored fringes.
            '--disable-lcd-text',
            # Images in the card are data URIs that decode after load; this lets them finish before the capture.
            '--virtual-time-budget=4000',
            # A profile of its own, so a running copy of Edge doesn't take the request.
            "--user-data-dir=`"$(Join-Path $work 'edge')`"",
            "--screenshot=`"$image`"",
            ('"file:///' + $page.Replace('\', '/') + '"')
        )
        $edgeProcess = Start-Process -FilePath $edge -ArgumentList $arguments -PassThru -WindowStyle Hidden
        if (-not $edgeProcess.WaitForExit(60000)) { $edgeProcess.Kill(); Fail "Edge timed out drawing $id ($theme)." }
        if (-not (Test-Path -LiteralPath $image)) { Fail "Edge didn't save $image." }

        Write-Host ("  {0,-20} {1}" -f $id, $theme)
        $written++
    }

    Write-Host ''
    Write-Host "Wrote $written previews to $OutputFolder."
}
finally {
    if ($KeepWork) {
        Write-Host "Pages kept in $work"
    }
    else {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
