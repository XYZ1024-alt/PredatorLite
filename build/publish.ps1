[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "publish\win-x64",
    [switch]$AllowArtifactsOutput,
    [System.IO.FileStream]$OutputLock
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-output.ps1")

$ownsOutputLock = $null -eq $OutputLock
if ($ownsOutputLock) {
    $OutputLock = Enter-PredatorLiteOutputLock -RepositoryRoot $repositoryRoot
}
else {
    Assert-PredatorLiteOutputLock -RepositoryRoot $repositoryRoot -OutputLock $OutputLock
}

$publishFailure = $null
$cleanupFailure = $null
$destination = $null
try {
    $project = Join-Path $repositoryRoot "src\PredatorLite.App\PredatorLite.App.csproj"
    $allowedRoots = @("publish")
    if ($AllowArtifactsOutput) {
        $allowedRoots += "artifacts\installer\work"
    }
    $destination = Assert-SafeRepositoryOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Destination (Join-Path $repositoryRoot $OutputPath) `
        -AllowedRelativeRoots $allowedRoots

    Remove-DirectoryWithRetry -Path $destination

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

    $debugIdentityLayout = Join-Path $destination "AppX"
    Remove-DirectoryWithRetry -Path $debugIdentityLayout

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
}
catch {
    $publishFailure = $_
    if ($null -ne $destination) {
        try {
            Remove-DirectoryWithRetry -Path $destination
        }
        catch {
            $cleanupFailure = $_
        }
    }
}
finally {
    if ($ownsOutputLock -and $null -ne $OutputLock) {
        $OutputLock.Dispose()
    }
}

if ($null -ne $cleanupFailure) {
    throw "$($publishFailure.Exception.Message) Output cleanup also failed: $($cleanupFailure.Exception.Message)"
}
if ($null -ne $publishFailure) {
    throw $publishFailure
}
