[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "publish\win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\PredatorLite.App\PredatorLite.App.csproj"
$destination = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "publish")) + [System.IO.Path]::DirectorySeparatorChar
if (-not $destination.StartsWith($publishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must resolve inside $publishRoot"
}

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

$publishArguments = @(
    "publish",
    $project,
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "false",
    "--output", $destination,
    "--nologo",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

& dotnet $publishArguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $destination -Recurse -Filter "*.pdb" -File |
    Remove-Item -Force

$licenseSource = Join-Path $repositoryRoot "licenses"
$licenseFiles = @(
    Get-ChildItem -LiteralPath $licenseSource -File |
        ForEach-Object { "licenses\$($_.Name)" }
)
if ($licenseFiles.Count -eq 0) {
    throw "No third-party license files were found in $licenseSource"
}

$requiredFiles = @(
    "PredatorLite.exe",
    "PredatorLite.dll",
    "PredatorLite.deps.json",
    "PredatorLite.runtimeconfig.json",
    "PredatorLite.pri",
    "App.xbf",
    "MainWindow.xbf",
    "OsdWindow.xbf",
    "Resources\Strings.enUS.xbf",
    "Resources\Strings.zhCN.xbf",
    "Resources\Theme.xbf",
    "Views\MainShell.xbf",
    "Views\HomePage.xbf",
    "Views\CoolingPage.xbf",
    "Views\LightingPage.xbf",
    "Views\MonitorPage.xbf",
    "Views\OsdContent.xbf",
    "Views\SettingsPage.xbf",
    "Views\TrayIconView.xbf",
    "PredatorLite.FanGuard.exe",
    "PredatorLite.FanGuard.dll",
    "PredatorLite.FanGuard.deps.json",
    "PredatorLite.FanGuard.runtimeconfig.json",
    "PredatorLite.ElevatedHelper.exe",
    "PredatorLite.ElevatedHelper.dll",
    "PredatorLite.ElevatedHelper.deps.json",
    "PredatorLite.ElevatedHelper.runtimeconfig.json",
    "Microsoft.WindowsAppRuntime.Bootstrap.dll",
    "Microsoft.WindowsAppRuntime.Bootstrap.Net.dll",
    "Assets\PredatorLiteFluent.ico",
    "Assets\PredatorLiteFluent.png",
    "LICENSE",
    "THIRD-PARTY-NOTICES.md"
) + $licenseFiles
$missingFiles = $requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $destination $_) -PathType Leaf)
}
if ($missingFiles.Count -gt 0) {
    throw "Published output is incomplete. Missing: $($missingFiles -join ', ')"
}

Write-Host "PredatorLite published to $destination"
