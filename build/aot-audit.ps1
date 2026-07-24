[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$auditRoot = Join-Path $repositoryRoot "artifacts\aot-audit"
$publishDirectory = Join-Path $auditRoot "app"
$logPath = Join-Path $auditRoot "publish.log"
$reportPath = Join-Path $auditRoot "report.json"

if (Test-Path -LiteralPath $auditRoot) {
    Remove-Item -LiteralPath $auditRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $auditRoot -Force | Out-Null

$project = Join-Path $repositoryRoot "src\PredatorLite.App\PredatorLite.App.csproj"
$arguments = @(
    "publish",
    $project,
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $publishDirectory,
    "--nologo",
    "-p:ArtifactsPath=$(Join-Path $auditRoot 'build')",
    "-p:PublishAot=true",
    "-p:PublishTrimmed=true",
    "-p:TrimMode=full",
    "-p:WindowsAppSDKSelfContained=false",
    "-p:SkipCompanionBuildCopy=true",
    "-p:PublishReadyToRun=false"
)

$output = @(& dotnet $arguments 2>&1 | ForEach-Object { $_.ToString() })
$exitCode = $LASTEXITCODE
[System.IO.File]::WriteAllLines($logPath, $output, [System.Text.UTF8Encoding]::new($false))
$output | ForEach-Object { Write-Host $_ }

$diagnostics = @($output | Where-Object {
    $_ -match '(?i)(warning|error)\s+(IL\d+|WMC\d+|CsWinRT\d+|MSB\d+)' -or
    $_ -match '^\s*ILC:'
})
$requiredResources = @(
    "PredatorLite.pri",
    "App.xbf",
    "MainWindow.xbf",
    "OsdWindow.xbf",
    "Resources\Strings.enUS.xbf",
    "Resources\Strings.zhCN.xbf",
    "Resources\Theme.xbf",
    "Views\CoolingPage.xbf",
    "Views\HomePage.xbf",
    "Views\LightingPage.xbf",
    "Views\MainShell.xbf",
    "Views\MonitorPage.xbf",
    "Views\OsdContent.xbf",
    "Views\SettingsPage.xbf",
    "Views\TrayIconView.xbf"
)
$missingResources = @($requiredResources | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $publishDirectory $_) -PathType Leaf)
})

$eligibleForRegressionTesting = $exitCode -eq 0 -and
    $diagnostics.Count -eq 0 -and
    $missingResources.Count -eq 0
$status = if ($eligibleForRegressionTesting) {
    "compile-audit-passed-manual-regression-required"
}
else {
    "blocked"
}
$report = [ordered]@{
    schemaVersion = 1
    capturedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    status = $status
    publishExitCode = $exitCode
    zeroWarning = $diagnostics.Count -eq 0
    resourcesComplete = $missingResources.Count -eq 0
    diagnostics = $diagnostics
    missingResources = $missingResources
    productionPublishChanged = $false
    promotionRequirements = @(
        "zero trim, AOT, CsWinRT, and XAML warnings",
        "complete PRI and XBF output",
        "all automated tests",
        "full WinUI, hardware-write, FanGuard, helper, tray, activation, and installer regression matrix"
    )
}
[System.IO.File]::WriteAllText(
    $reportPath,
    ($report | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

if (-not $eligibleForRegressionTesting) {
    throw "Native AOT remains blocked. See $reportPath and $logPath."
}

Write-Host "Native AOT compile audit passed. Production promotion still requires the full documented regression matrix."
