[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath = "publish\win-x64",
    [bool]$ReadyToRun = $true,
    [switch]$AllowArtifactsOutput,
    [System.IO.FileStream]$OutputLock
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-output.ps1")

function Invoke-ProjectPublish {
    param(
        [Parameter(Mandatory)]
        [string]$Project,
        [Parameter(Mandatory)]
        [string]$Destination,
        [Parameter(Mandatory)]
        [bool]$UseReadyToRun
    )

    $readyToRunValue = $UseReadyToRun.ToString().ToLowerInvariant()
    $arguments = @(
        "publish",
        $Project,
        "--configuration", $Configuration,
        "--runtime", "win-x64",
        "--self-contained", "false",
        "--output", $Destination,
        "--nologo",
        "-p:PublishProfile=FrameworkDependent",
        "-p:PublishReadyToRun=$readyToRunValue",
        "-p:PublishReadyToRunShowWarnings=true",
        "-p:SkipCompanionBuildCopy=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    & dotnet $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project with exit code $LASTEXITCODE"
    }
}

function Test-ReadyToRunImage {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $corHeader = $reader.PEHeaders.CorHeader
        return $null -ne $corHeader -and $corHeader.ManagedNativeHeaderDirectory.Size -gt 0
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Assert-FrameworkDependentRuntimeConfig {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $configuration = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $runtimeOptions = $configuration.runtimeOptions
    $frameworkCount = @($runtimeOptions.frameworks).Count + @($runtimeOptions.framework).Count
    if ($frameworkCount -eq 0) {
        throw "Runtime configuration is not framework-dependent: $Path"
    }
}

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
$stagingRoot = $null
try {
    $allowedRoots = @("publish")
    if ($AllowArtifactsOutput) {
        $allowedRoots += "artifacts\installer\work"
    }
    $destination = Assert-SafeRepositoryOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Destination (Join-Path $repositoryRoot $OutputPath) `
        -AllowedRelativeRoots $allowedRoots

    $stagingRelativePath = "obj\publish-staging\$([guid]::NewGuid().ToString('N'))"
    $stagingRoot = Assert-SafeRepositoryOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Destination (Join-Path $repositoryRoot $stagingRelativePath) `
        -AllowedRelativeRoots @("obj\publish-staging")
    $appStage = Join-Path $stagingRoot "app"
    $fanGuardStage = Join-Path $stagingRoot "fan-guard"
    $helperStage = Join-Path $stagingRoot "elevated-helper"

    Remove-DirectoryWithRetry -Path $destination
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

    Invoke-ProjectPublish `
        -Project (Join-Path $repositoryRoot "src\PredatorLite.App\PredatorLite.App.csproj") `
        -Destination $appStage `
        -UseReadyToRun $ReadyToRun
    Invoke-ProjectPublish `
        -Project (Join-Path $repositoryRoot "src\PredatorLite.FanGuard\PredatorLite.FanGuard.csproj") `
        -Destination $fanGuardStage `
        -UseReadyToRun $ReadyToRun
    Invoke-ProjectPublish `
        -Project (Join-Path $repositoryRoot "src\PredatorLite.ElevatedHelper\PredatorLite.ElevatedHelper.csproj") `
        -Destination $helperStage `
        -UseReadyToRun $ReadyToRun

    $unexpectedAppCompanions = @(
        Get-ChildItem -LiteralPath $appStage -File |
            Where-Object { $_.Name -like "PredatorLite.FanGuard.*" -or
                $_.Name -like "PredatorLite.ElevatedHelper.*" }
    )
    if ($unexpectedAppCompanions.Count -ne 0) {
        throw "The app publish unexpectedly contains companion build output: $($unexpectedAppCompanions.Name -join ', ')"
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $appStage -Force |
        Copy-Item -Destination $destination -Recurse -Force

    $companionFiles = @(
        @{ Stage = $fanGuardStage; Name = "PredatorLite.FanGuard.exe" },
        @{ Stage = $fanGuardStage; Name = "PredatorLite.FanGuard.dll" },
        @{ Stage = $fanGuardStage; Name = "PredatorLite.FanGuard.deps.json" },
        @{ Stage = $fanGuardStage; Name = "PredatorLite.FanGuard.runtimeconfig.json" },
        @{ Stage = $helperStage; Name = "PredatorLite.ElevatedHelper.exe" },
        @{ Stage = $helperStage; Name = "PredatorLite.ElevatedHelper.dll" },
        @{ Stage = $helperStage; Name = "PredatorLite.ElevatedHelper.deps.json" },
        @{ Stage = $helperStage; Name = "PredatorLite.ElevatedHelper.runtimeconfig.json" }
    )
    foreach ($file in $companionFiles) {
        $source = Join-Path $file.Stage $file.Name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Companion publish is incomplete: $source"
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $destination $file.Name) -Force
    }

    Remove-DirectoryWithRetry -Path (Join-Path $destination "AppX")

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
        "PredatorLite.Core.dll",
        "PredatorLite.Platform.Windows.dll",
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
    $missingFiles = @($requiredFiles | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $destination $_) -PathType Leaf)
    })
    if ($missingFiles.Count -gt 0) {
        throw "Published output is incomplete. Missing: $($missingFiles -join ', ')"
    }

    $unexpectedDevelopmentFiles = @(
        Get-ChildItem -LiteralPath $destination -Recurse -File |
            Where-Object { $_.Extension -in @(".pdb", ".xaml") }
    )
    if ($unexpectedDevelopmentFiles.Count -ne 0) {
        throw "Published output contains development-only files: $($unexpectedDevelopmentFiles.FullName -join ', ')"
    }

    foreach ($runtimeBinary in @("coreclr.dll", "hostfxr.dll", "System.Private.CoreLib.dll")) {
        if (Test-Path -LiteralPath (Join-Path $destination $runtimeBinary) -PathType Leaf) {
            throw "Framework-dependent output unexpectedly contains $runtimeBinary"
        }
    }
    foreach ($runtimeConfig in @(
        "PredatorLite.runtimeconfig.json",
        "PredatorLite.FanGuard.runtimeconfig.json",
        "PredatorLite.ElevatedHelper.runtimeconfig.json")) {
        Assert-FrameworkDependentRuntimeConfig -Path (Join-Path $destination $runtimeConfig)
    }

    $readyToRunAssemblies = @(
        "PredatorLite.dll",
        "PredatorLite.Core.dll",
        "PredatorLite.Platform.Windows.dll",
        "PredatorLite.FanGuard.dll",
        "PredatorLite.ElevatedHelper.dll"
    )
    if ($ReadyToRun) {
        $notReadyToRun = @($readyToRunAssemblies | Where-Object {
            -not (Test-ReadyToRunImage -Path (Join-Path $destination $_))
        })
        if ($notReadyToRun.Count -ne 0) {
            throw "ReadyToRun was requested but these assemblies lack a managed native header: $($notReadyToRun -join ', ')"
        }
    }
    else {
        $unexpectedReadyToRun = @($readyToRunAssemblies | Where-Object {
            Test-ReadyToRunImage -Path (Join-Path $destination $_)
        })
        if ($unexpectedReadyToRun.Count -ne 0) {
            throw "IL output unexpectedly contains ReadyToRun assemblies: $($unexpectedReadyToRun -join ', ')"
        }
    }

    $layoutKind = if ($ReadyToRun) { "framework-dependent ReadyToRun" } else { "framework-dependent IL" }
    $totalBytes = (Get-ChildItem -LiteralPath $destination -Recurse -File |
        Measure-Object -Property Length -Sum).Sum
    Write-Host "PredatorLite $layoutKind layout published to $destination ($totalBytes bytes)"
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
    if ($null -ne $stagingRoot) {
        try {
            Remove-DirectoryWithRetry -Path $stagingRoot
        }
        catch {
            if ($null -eq $cleanupFailure) {
                $cleanupFailure = $_
            }
        }
    }
    if ($ownsOutputLock -and $null -ne $OutputLock) {
        $OutputLock.Dispose()
    }
}

if ($null -ne $cleanupFailure) {
    $message = if ($null -ne $publishFailure) {
        $publishFailure.Exception.Message
    }
    else {
        "Publish succeeded, but staging cleanup failed."
    }
    throw "$message Output cleanup also failed: $($cleanupFailure.Exception.Message)"
}
if ($null -ne $publishFailure) {
    throw $publishFailure
}
