param(
    [Parameter(Mandatory)]
    [int]$AppPid,

    [string]$OutputDirectory = "artifacts\ui"
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$results = @()
$homeLightingEffectValue = $null
$lightingZoneOneValue = $null

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw "winapp is required. Run /winui-setup, then retry this script."
}

Add-Type -AssemblyName System.Drawing -ErrorAction Stop
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PredatorLiteUiTestNativeMethods
{
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);
}
"@ -ErrorAction Stop

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
$mainHwnd = ($windows |
    Where-Object { $_.label -eq "window" } |
    Sort-Object { $_.width * $_.height } -Descending |
    Select-Object -First 1).hwnd
if (-not $mainHwnd) {
    throw "Could not resolve the PredatorLite main window."
}

function Test-Ui {
    param(
        [string]$Name,
        [scriptblock]$Script
    )

    Write-Host "RUN: $Name"
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++
            $script:results += @{ name = $Name; status = "PASS" }
            Write-Host "PASS: $Name" -ForegroundColor Green
        } else {
            $script:fail++
            $script:results += @{
                name = $Name
                status = "FAIL"
                detail = "$output"
            }
            Write-Host "FAIL: $Name" -ForegroundColor Red
        }
    } catch {
        $script:fail++
        $script:results += @{
            name = $Name
            status = "FAIL"
            detail = "$_"
        }
        Write-Host "FAIL: $Name" -ForegroundColor Red
    }
}

function Assert-WinAppSucceeded {
    param([string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Get-UiBounds {
    param([string]$Selector)

    $propertyResult = winapp ui get-property $Selector -w $mainHwnd --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading bounds for $Selector"
    $parts = @($propertyResult.properties.BoundingRectangle -split ",")
    if ($parts.Count -ne 4) {
        throw "Unexpected bounds for $Selector."
    }

    return [pscustomobject]@{
        X = [int]$parts[0]
        Y = [int]$parts[1]
        Width = [int]$parts[2]
        Height = [int]$parts[3]
    }
}

function Save-UiStateScreenshot {
    param(
        [string]$FileName,
        [string]$NavigationSelector
    )

    winapp ui screenshot -w $mainHwnd -o "$OutputDirectory\$FileName" --focus -q
    Assert-WinAppSucceeded "Capturing $FileName"
    $navigationFileName = [System.IO.Path]::GetFileNameWithoutExtension($FileName) + "-nav.png"
    winapp ui screenshot $NavigationSelector -w $mainHwnd -o "$OutputDirectory\$navigationFileName" -q
    Assert-WinAppSucceeded "Capturing $navigationFileName"
}

function Save-HoverScreenshot {
    param(
        [string]$FileName,
        [string]$Selector
    )

    $capturePath = Join-Path $OutputDirectory $FileName
    $elementFileName =
        [System.IO.Path]::GetFileNameWithoutExtension($FileName) + "-element.png"
    $elementCapturePath = Join-Path $OutputDirectory $elementFileName
    $captureJob = Start-Job -ScriptBlock {
        param(
            [string]$Window,
            [string]$Path,
            [string]$ElementSelector,
            [string]$ElementPath
        )

        Start-Sleep -Milliseconds 1000
        winapp ui screenshot -w $Window -o $Path -q
        if ($LASTEXITCODE -ne 0) {
            throw "Hover-state screenshot failed with exit code $LASTEXITCODE."
        }

        winapp ui screenshot $ElementSelector -w $Window -o $ElementPath -q
        if ($LASTEXITCODE -ne 0) {
            throw "Hover-state element screenshot failed with exit code $LASTEXITCODE."
        }
    } -ArgumentList "$mainHwnd", $capturePath, $Selector, $elementCapturePath

    try {
        winapp ui hover $Selector -w $mainHwnd --dwell-time 3500 | Out-Null
        Assert-WinAppSucceeded "Hovering $Selector"
        $captureJob | Wait-Job | Out-Null
        if ($captureJob.State -ne "Completed") {
            throw "Capturing $FileName failed: $($captureJob.ChildJobs[0].JobStateInfo.Reason)"
        }

        Receive-Job $captureJob -ErrorAction Stop | Out-Null
    }
    finally {
        Remove-Job $captureJob -Force -ErrorAction SilentlyContinue
    }

    return $elementCapturePath
}

function Assert-CaptionStateChanged {
    param(
        [string]$NormalPath,
        [string]$PointerOverPath
    )

    $normal = [System.Drawing.Bitmap]::new($NormalPath)
    $pointerOver = [System.Drawing.Bitmap]::new($PointerOverPath)
    try {
        if ($normal.Width -ne $pointerOver.Width -or
            $normal.Height -ne $pointerOver.Height) {
            throw "Caption-state screenshots have different dimensions."
        }

        $sampleX = [Math]::Max(1, [int][Math]::Floor($normal.Width * 0.2))
        $sampleY = [Math]::Max(1, [int][Math]::Floor($normal.Height * 0.5))
        $normalColor = $normal.GetPixel($sampleX, $sampleY)
        $pointerOverColor = $pointerOver.GetPixel($sampleX, $sampleY)
        $colorDistance =
            [Math]::Abs($normalColor.R - $pointerOverColor.R) +
            [Math]::Abs($normalColor.G - $pointerOverColor.G) +
            [Math]::Abs($normalColor.B - $pointerOverColor.B)
        if ($colorDistance -lt 80) {
            throw "The Close button did not visibly enter its pointer-over state."
        }
    }
    finally {
        $normal.Dispose()
        $pointerOver.Dispose()
    }
}

Test-Ui "Title bar exposes only minimize and close" {
    winapp ui wait-for "Shell.TitleBar.Minimize" -w $mainHwnd -t 3000
    Assert-WinAppSucceeded "Waiting for the title-bar minimize button"
    winapp ui wait-for "Shell.TitleBar.Close" -w $mainHwnd -t 3000
    Assert-WinAppSucceeded "Waiting for the title-bar close button"
    winapp ui wait-for "Shell.TitleBar.Maximize" -w $mainHwnd --gone -t 1000
    Assert-WinAppSucceeded "Checking that the title-bar maximize button is absent"

    $minimizeBounds = Get-UiBounds "Shell.TitleBar.Minimize"
    $closeBounds = Get-UiBounds "Shell.TitleBar.Close"
    $dpi = [PredatorLiteUiTestNativeMethods]::GetDpiForWindow([IntPtr][long]$mainHwnd)
    if ($dpi -eq 0) {
        $dpi = 96
    }

    $dpiScale = $dpi / 96.0
    $expectedWidth = [Math]::Round(46 * $dpiScale)
    $expectedHeight = [Math]::Round(40 * $dpiScale)
    $captionAspectRatio = $minimizeBounds.Width / [double]$minimizeBounds.Height
    if ([Math]::Abs($minimizeBounds.Width - $expectedWidth) -gt 1 -or
        [Math]::Abs($minimizeBounds.Height - $expectedHeight) -gt 1 -or
        $minimizeBounds.Width -ne $closeBounds.Width -or
        $minimizeBounds.Height -ne $closeBounds.Height -or
        [Math]::Abs($captionAspectRatio - (46.0 / 40.0)) -gt 0.03 -or
        $closeBounds.X -ne $minimizeBounds.X + $minimizeBounds.Width) {
        throw "Caption buttons must be adjacent, equally sized 46x40 DIP controls."
    }

    $titleBarTree = winapp ui inspect -w $mainHwnd --interactive --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Inspecting title-bar controls"
    $visibleSystemMaximize = @($titleBarTree.windows.elements | Where-Object {
        $_.automationId -eq "Maximize-Restore" -and
        -not $_.isOffscreen -and
        $_.width -gt 0 -and
        $_.height -gt 0
    })
    if ($visibleSystemMaximize.Count -gt 0) {
        throw "The system maximize caption button is still visible."
    }
}
Test-Ui "Title bar normal and close-hover states render" {
    winapp ui focus "Shell.Nav.Home" -w $mainHwnd
    Assert-WinAppSucceeded "Focusing the main window"
    winapp ui hover "Shell.Nav.Home" -w $mainHwnd
    Assert-WinAppSucceeded "Moving the pointer away from the caption buttons"
    winapp ui screenshot -w $mainHwnd -o "$OutputDirectory\00-titlebar-normal.png" --focus -q
    Assert-WinAppSucceeded "Capturing the normal title-bar state"
    $normalElementPath = Join-Path $OutputDirectory "00-titlebar-close-normal-element.png"
    winapp ui screenshot "Shell.TitleBar.Close" -w $mainHwnd -o $normalElementPath -q
    Assert-WinAppSucceeded "Capturing the normal close-button state"
    $hoverElementPath =
        Save-HoverScreenshot "00-titlebar-close-hover.png" "Shell.TitleBar.Close"
    Assert-CaptionStateChanged $normalElementPath $hoverElementPath
    winapp ui focus "Shell.Nav.Home" -w $mainHwnd
    Assert-WinAppSucceeded "Refocusing the main window"
    winapp ui hover "Shell.Nav.Home" -w $mainHwnd
    Assert-WinAppSucceeded "Clearing the close-button hover state"
}

Test-Ui "Home dashboard is visible" {
    winapp ui wait-for "Shell.Nav.Home" -a $AppPid -t 5000
    Assert-WinAppSucceeded "Waiting for Home navigation"
    winapp ui invoke "Shell.Nav.Home" -a $AppPid
    Assert-WinAppSucceeded "Opening Home"
    winapp ui wait-for "Home.Scroll" -a $AppPid -t 5000
    Assert-WinAppSucceeded "Waiting for the Home page"
    winapp ui wait-for "Home.Mode.Balanced" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for operating modes"
    winapp ui wait-for "Home.Mode.Performance" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for performance mode"
    winapp ui wait-for "Home.Gpu.Hybrid" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for GPU modes"
    winapp ui wait-for "Home.RefreshRate" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for display controls"
    winapp ui wait-for "Home.Lighting.Effect" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for lighting controls"
    winapp ui wait-for "Home.ChargeLimit" -a $AppPid -t 2000
}
Test-Ui "Home lighting effect has an initial selection" {
    $homeLightingEffectResult = winapp ui get-value "Home.Lighting.Effect" -a $AppPid --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading the Home lighting effect"
    $script:homeLightingEffectValue = "$($homeLightingEffectResult.text)".Trim()
    if ([string]::IsNullOrWhiteSpace($script:homeLightingEffectValue)) {
        throw "Home lighting effect must have a selected value on first render."
    }
}
Test-Ui "Persistent navigation exposes every section" {
    winapp ui wait-for "Shell.Nav.Home" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for Home navigation"
    winapp ui wait-for "Shell.Nav.Cooling" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for Cooling navigation"
    winapp ui wait-for "Shell.Nav.Lighting" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for Lighting navigation"
    winapp ui wait-for "Shell.Nav.Monitor" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for Monitor navigation"
    winapp ui wait-for "Shell.Nav.Settings" -a $AppPid -t 2000
}
Test-Ui "Home navigation is not a status panel" {
    winapp ui wait-for "Shell.Status" -a $AppPid --gone -t 1000
    Assert-WinAppSucceeded "Checking the removed Home status panel"
}
Test-Ui "Eco is not a manual mode tile" {
    $homeTreeJson = winapp ui inspect -a $AppPid --interactive --json 2>$null
    Assert-WinAppSucceeded "Inspecting Home controls"
    if ($homeTreeJson -match '"automationId"\s*:\s*"Home\.Mode\.Eco"') {
        throw "Home.Mode.Eco must not be visible."
    }
}
Save-UiStateScreenshot "01-home.png" "Shell.Nav.Home"

Test-Ui "Cooling navigation works" {
    winapp ui invoke "Shell.Nav.Cooling" -a $AppPid
    Assert-WinAppSucceeded "Opening Cooling"
    winapp ui wait-for "Cooling.ApplyCurve" -a $AppPid -t 3000
}
Test-Ui "CPU safety endpoint is locked" {
    winapp ui wait-for "FanCurve.Cpu.7.Temperature" -a $AppPid -p IsEnabled --value "False" -t 3000
    Assert-WinAppSucceeded "Checking CPU safety temperature"
    winapp ui wait-for "FanCurve.Cpu.7.Speed" -a $AppPid -p IsEnabled --value "False" -t 3000
}
Test-Ui "Curve validation is surfaced" {
    winapp ui wait-for "Cooling.Validation" -a $AppPid -t 2000
}
Save-UiStateScreenshot "02-cooling.png" "Shell.Nav.Cooling"

Test-Ui "Home navigation returns to dashboard" {
    winapp ui invoke "Shell.Nav.Home" -a $AppPid
    Assert-WinAppSucceeded "Opening Home"
    winapp ui wait-for "Home.Mode.Balanced" -a $AppPid -t 3000
}
Test-Ui "Lighting preserves the selected effect and switches to Static context" {
    winapp ui invoke "Shell.Nav.Lighting" -a $AppPid
    Assert-WinAppSucceeded "Opening Lighting"
    winapp ui wait-for "Lighting.Scroll" -a $AppPid -t 5000
    Assert-WinAppSucceeded "Waiting for the Lighting page"
    winapp ui wait-for "Lighting.Effect" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for the Lighting effect selector"
    $lightingEffectResult = winapp ui get-value "Lighting.Effect" -a $AppPid --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading the Lighting effect"
    $lightingEffectValue = "$($lightingEffectResult.text)".Trim()
    if ([string]::IsNullOrWhiteSpace($lightingEffectValue)) {
        throw "Lighting effect must have a selected value."
    }
    if ($lightingEffectValue -ne $script:homeLightingEffectValue) {
        throw "Home and Lighting must show the same selected effect."
    }

    winapp ui send-keys "home" --target "Lighting.Effect" -a $AppPid --via send-input
    Assert-WinAppSucceeded "Selecting the Static lighting effect"
    winapp ui wait-for "Lighting.StaticPreview" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for the Static keyboard preview"
    winapp ui wait-for "Lighting.DynamicPreview" -a $AppPid --gone -t 2000
    Assert-WinAppSucceeded "Checking that the dynamic preview is hidden"
    winapp ui wait-for "LightingZone.1" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for lighting zone 1"
    winapp ui wait-for "LightingZone.2" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for lighting zone 2"
    winapp ui wait-for "LightingZone.3" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for lighting zone 3"
    winapp ui wait-for "LightingZone.4" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for lighting zone 4"
    winapp ui wait-for "Lighting.Speed" -a $AppPid --gone -t 2000
    Assert-WinAppSucceeded "Checking that speed is hidden for Static"
    winapp ui wait-for "Lighting.Direction" -a $AppPid --gone -t 2000
    Assert-WinAppSucceeded "Checking that direction is hidden for Static"

    $zoneResult = winapp ui get-value "LightingZone.1" -a $AppPid --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading lighting zone 1"
    $script:lightingZoneOneValue = "$($zoneResult.text)".Trim()
    if ([string]::IsNullOrWhiteSpace($script:lightingZoneOneValue)) {
        throw "Lighting zone 1 must expose its label and color."
    }

    $staticColorResult = winapp ui get-value "Lighting.PrimaryColor" -a $AppPid --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading the Static logo color control"
    if ("$($staticColorResult.text)" -notmatch "^(Logo color|\u6807\u5FD7\u706F\u989C\u8272), #[0-9A-Fa-f]{6}$") {
        throw "Static color control must expose its logo context and current hex value."
    }
}
Save-UiStateScreenshot "03-lighting-static.png" "Shell.Nav.Lighting"

Test-Ui "Static lighting zone opens and cancels the color dialog" {
    winapp ui invoke "LightingZone.1" -a $AppPid
    Assert-WinAppSucceeded "Opening the lighting zone color dialog"
    winapp ui wait-for "LightingColorDialog" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for the lighting color dialog"
    winapp ui send-keys "esc" -a $AppPid --via send-input
    Assert-WinAppSucceeded "Cancelling the lighting color dialog"
    winapp ui wait-for "LightingColorDialog" -a $AppPid --gone -t 3000
    Assert-WinAppSucceeded "Waiting for the lighting color dialog to close"

    $zoneResult = winapp ui get-value "LightingZone.1" -a $AppPid --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading lighting zone 1 after cancelling"
    if ("$($zoneResult.text)".Trim() -ne $script:lightingZoneOneValue) {
        throw "Cancelling the color dialog must preserve the zone color."
    }
}

Test-Ui "Dynamic lighting uses one keyboard preview and contextual controls" {
    winapp ui send-keys "down" --target "Lighting.Effect" -a $AppPid --via send-input
    Assert-WinAppSucceeded "Selecting a dynamic lighting effect"
    winapp ui wait-for "Lighting.DynamicPreview" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for the dynamic keyboard preview"
    winapp ui wait-for "Lighting.StaticPreview" -a $AppPid --gone -t 2000
    Assert-WinAppSucceeded "Checking that the Static preview is hidden"
    winapp ui wait-for "LightingZone.1" -a $AppPid --gone -t 2000
    Assert-WinAppSucceeded "Checking that Static zones are hidden"
    winapp ui wait-for "Lighting.PrimaryColor" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for the primary color control"
    $dynamicColorResult = winapp ui get-value "Lighting.PrimaryColor" -a $AppPid --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading the dynamic primary color control"
    if ("$($dynamicColorResult.text)" -notmatch "^(Primary color|\u4E3B\u989C\u8272), #[0-9A-Fa-f]{6}$") {
        throw "Dynamic color control must expose its primary-color context and current hex value."
    }
    winapp ui wait-for "Lighting.Speed" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for dynamic speed"
    winapp ui wait-for "Lighting.Direction" -a $AppPid -t 2000
    Assert-WinAppSucceeded "Waiting for dynamic direction"
}
Save-UiStateScreenshot "03-lighting-dynamic.png" "Shell.Nav.Lighting"

Test-Ui "Returning to Static restores the four zone colors" {
    winapp ui send-keys "home" --target "Lighting.Effect" -a $AppPid --via send-input
    Assert-WinAppSucceeded "Returning to the Static lighting effect"
    winapp ui wait-for "LightingZone.1" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for lighting zone 1 to return"
    $zoneResult = winapp ui get-value "LightingZone.1" -a $AppPid --json 2>$null |
        ConvertFrom-Json -ErrorAction Stop
    Assert-WinAppSucceeded "Reading restored lighting zone 1"
    if ("$($zoneResult.text)".Trim() -ne $script:lightingZoneOneValue) {
        throw "Switching effects must preserve the Static zone colors."
    }
}

Test-Ui "Lighting omits the normal-state badge" {
    $lightingTreeJson = winapp ui inspect -a $AppPid --json --depth 12 2>$null
    Assert-WinAppSucceeded "Inspecting Lighting controls"
    if ($lightingTreeJson -match '"name"\s*:\s*"(Controllable|\\u53EF\\u63A7\\u5236)"') {
        throw "Lighting must not expose a normal-state controllable badge."
    }
}

Test-Ui "Alt+Left returns to Home" {
    winapp ui focus "Shell.Nav.Lighting" -a $AppPid
    Assert-WinAppSucceeded "Focusing the window"
    winapp ui send-keys "alt+left" -a $AppPid --via send-input
    Assert-WinAppSucceeded "Sending Alt+Left"
    winapp ui wait-for "Home.Mode.Balanced" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for Home after Alt+Left"
    winapp ui wait-for "Shell.Nav.Monitor" -a $AppPid -t 3000
}
Test-Ui "Alt+Left leaves no shortcut label" {
    $shortcutTreeJson = winapp ui inspect -a $AppPid --interactive --json 2>$null
    Assert-WinAppSucceeded "Inspecting shortcut labels"
    $shortcutTree = $shortcutTreeJson | ConvertFrom-Json -ErrorAction Stop
    $shortcutElements = @($shortcutTree.elements)
    $shortcutElements += @($shortcutTree.windows | ForEach-Object { $_.elements })
    $shortcutLabels = @($shortcutElements | Where-Object {
        $_.name -match "Alt\s*\+\s*(Left|\u2190|\u5411\u5DE6)"
    })
    if ($shortcutLabels.Count -gt 0) {
        $names = ($shortcutLabels | ForEach-Object { $_.name }) -join ", "
        throw "Unexpected shortcut label remains visible: $names"
    }
}
Save-UiStateScreenshot "04-alt-left-return.png" "Shell.Nav.Home"

Test-Ui "Monitor omits the live badge" {
    winapp ui invoke "Shell.Nav.Monitor" -a $AppPid
    Assert-WinAppSucceeded "Opening Monitor"
    winapp ui wait-for "Monitor.Scroll" -a $AppPid -t 1500
    Assert-WinAppSucceeded "Waiting for Monitor"
    winapp ui wait-for "Monitor.TelemetryState" -a $AppPid --gone -t 1000
    Assert-WinAppSucceeded "Checking the removed live badge"
}
Test-Ui "CPU package power is absent from Monitor" {
    $monitorTreeJson = winapp ui inspect -a $AppPid --json --depth 12 2>$null
    Assert-WinAppSucceeded "Inspecting Monitor controls"
    $monitorTree = $monitorTreeJson | ConvertFrom-Json -ErrorAction Stop
    $monitorElements = @($monitorTree.elements)
    $monitorElements += @($monitorTree.windows | ForEach-Object { $_.elements })
    $cpuPowerLabels = @($monitorElements | Where-Object {
        $_.name -match "^CPU (power|\u529F\u8017)$"
    })
    if ($cpuPowerLabels.Count -gt 0) {
        throw "Monitor must not expose CPU package power."
    }
}
Save-UiStateScreenshot "05-monitor.png" "Shell.Nav.Monitor"

Test-Ui "Settings controls are reachable" {
    winapp ui invoke "Shell.Nav.Settings" -a $AppPid
    Assert-WinAppSucceeded "Opening Settings"
    winapp ui wait-for "Settings.Language" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for language settings"
    winapp ui wait-for "Settings.Services" -a $AppPid -t 3000
    Assert-WinAppSucceeded "Waiting for service settings"
    winapp ui wait-for "Settings.ExportDiagnostics" -a $AppPid -t 3000
}
Test-Ui "Generic device switches are absent" {
    $settingsTreeJson = winapp ui inspect -a $AppPid --interactive --json 2>$null
    Assert-WinAppSucceeded "Inspecting Settings controls"
    if ($settingsTreeJson -match '"automationId"\s*:\s*"(Settings\.DeviceSettings|DeviceSetting\.[^"]*)"') {
        throw "Generic device settings are still exposed."
    }
}
Test-Ui "Settings action buttons are equal and ordered" {
    winapp ui scroll-into-view "Settings.ExportDiagnostics" -w $mainHwnd
    Assert-WinAppSucceeded "Scrolling to Settings actions"
    $restoreBounds = Get-UiBounds "Settings.RestoreServices"
    $disableBounds = Get-UiBounds "Settings.DisableConflicts"
    $logsBounds = Get-UiBounds "Settings.OpenLogs"
    $exportBounds = Get-UiBounds "Settings.ExportDiagnostics"
    $buttons = @($restoreBounds, $disableBounds, $logsBounds, $exportBounds)
    $widths = @($buttons | Select-Object -ExpandProperty Width -Unique)
    $heights = @($buttons | Select-Object -ExpandProperty Height -Unique)
    if ($widths.Count -ne 1 -or $heights.Count -ne 1) {
        throw "Settings action buttons must have identical dimensions."
    }
    if ($restoreBounds.X -ge $disableBounds.X) {
        throw "Restore services must appear left of Disable conflicts."
    }
    if ($logsBounds.X -ge $exportBounds.X) {
        throw "Open logs must appear left of Export diagnostics."
    }
}
Save-UiStateScreenshot "06-settings.png" "Shell.Nav.Settings"

$inspectionJson = winapp ui inspect -a $AppPid --interactive --json 2>$null
if ($LASTEXITCODE -ne 0) {
    $fail++
    $results += @{
        name = "Interactive controls have AutomationId"
        status = "FAIL"
        detail = "UI tree inspection failed with exit code $LASTEXITCODE."
    }
} else {
    try {
        $inspection = $inspectionJson | ConvertFrom-Json -ErrorAction Stop
        $allElements = @($inspection.elements)
        $allElements += @($inspection.windows | ForEach-Object { $_.elements })
        $interactive = @($allElements | Where-Object {
            $_.type -match "Button|RadioButton|TextBox|ComboBox|CheckBox|ToggleSwitch|Slider|Spinner|TabItem|ListItem" -and
            $_.name -notmatch "Minimize|Maximize|Close|System" -and
            $_.className -notmatch "PickerHost|#32770|CabinetWClass|Expander|RepeatButton|ScrollBar" -and
            $_.ancestorPath -notcontains "TitleBar" -and
            $_.ancestorPath -notcontains "ScrollBar"
        })
        if ($interactive.Count -eq 0) {
            throw "UI tree inspection returned no interactive controls."
        }

        $missingAutomationId = @($interactive | Where-Object { -not $_.automationId })
        if ($missingAutomationId.Count -gt 0) {
            $missing = ($missingAutomationId | ForEach-Object {
                "$($_.type) '$($_.name)'"
            }) -join ", "
            throw "Missing: $missing"
        }

        $pass++
        $results += @{ name = "Interactive controls have AutomationId"; status = "PASS" }
    } catch {
        $fail++
        $results += @{
            name = "Interactive controls have AutomationId"
            status = "FAIL"
            detail = "$_"
        }
    }
}

$results | ConvertTo-Json -Depth 4 | Out-File "$OutputDirectory\test-results.json"
Write-Host "`nPassed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
    Write-Host "  FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
}

if ($fail -gt 0) {
    exit 1
}

exit 0
