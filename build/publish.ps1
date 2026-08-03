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

function Assert-ValidAuthenticodeSignature {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate) {
        throw "Published dependency does not have a valid Authenticode signature: $Path ($($signature.Status))"
    }
}

function Assert-X64BinaryLayout {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $unsupportedDirectories = @(
        Get-ChildItem -LiteralPath $Path -Recurse -Directory |
            Where-Object { $_.Name -match '^(arm|arm64|arm64ec|x86)$' }
    )
    if ($unsupportedDirectories.Count -ne 0) {
        throw "Published output contains unsupported architecture directories: $($unsupportedDirectories.FullName -join ', ')"
    }

    foreach ($binary in Get-ChildItem -LiteralPath $Path -Recurse -File |
        Where-Object { $_.Extension -in @('.exe', '.dll') }) {
        $stream = [System.IO.File]::OpenRead($binary.FullName)
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $headers = $reader.PEHeaders
            $machine = $headers.CoffHeader.Machine
            $corHeader = $headers.CorHeader
            if ($null -eq $corHeader) {
                if ($machine -ne [System.Reflection.PortableExecutable.Machine]::Amd64) {
                    throw "Native binary is not AMD64: $($binary.FullName) ($machine)"
                }
                continue
            }

            $requires32Bit = ([int]$corHeader.Flags -band
                [int][System.Reflection.PortableExecutable.CorFlags]::Requires32Bit) -ne 0
            if ($requires32Bit) {
                throw "Managed binary requires 32-bit execution: $($binary.FullName)"
            }
            if ($machine.ToString().StartsWith('Arm', [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Managed binary targets an ARM architecture: $($binary.FullName) ($machine)"
            }
            $ilOnly = ([int]$corHeader.Flags -band
                [int][System.Reflection.PortableExecutable.CorFlags]::ILOnly) -ne 0
            if (-not $ilOnly -and $machine -ne [System.Reflection.PortableExecutable.Machine]::Amd64) {
                throw "Mixed-mode managed binary is not AMD64: $($binary.FullName) ($machine)"
            }
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }
    }
}

function Assert-DependencyAssetsAbsent {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string[]]$PackagePrefixes,
        [Parameter(Mandatory)]
        [string[]]$FileNames
    )

    $configuration = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    $unexpectedPackages = @(
        $configuration.libraries.Keys | Where-Object {
            $name = $_
            $PackagePrefixes | Where-Object {
                $name.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase)
            }
        }
    )
    if ($unexpectedPackages.Count -ne 0) {
        throw "Dependency manifest contains removed packages: $($unexpectedPackages -join ', ')"
    }

    $unexpectedAssets = [System.Collections.Generic.List[string]]::new()
    foreach ($target in $configuration.targets.Values) {
        foreach ($library in $target.Values) {
            foreach ($assetKind in @('runtime', 'native', 'runtimeTargets')) {
                if (-not $library.Contains($assetKind)) {
                    continue
                }
                foreach ($assetPath in $library[$assetKind].Keys) {
                    $leafName = [System.IO.Path]::GetFileName($assetPath)
                    if ($leafName -in $FileNames) {
                        $unexpectedAssets.Add($assetPath)
                    }
                }
            }
        }
    }
    if ($unexpectedAssets.Count -ne 0) {
        throw "Dependency manifest contains excluded local assets: $($unexpectedAssets -join ', ')"
    }
}

function Write-LargestPublishedFiles {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Write-Host "Largest published files:"
    Get-ChildItem -LiteralPath $Path -Recurse -File |
        Sort-Object Length -Descending |
        Select-Object -First 15 |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($Path, $_.FullName)
            Write-Host ("  {0,12:N0}  {1}" -f $_.Length, $relativePath)
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

    foreach ($bootstrapperFile in @(
        "Microsoft.WindowsAppRuntime.Bootstrap.dll",
        "Microsoft.WindowsAppRuntime.Bootstrap.Net.dll")) {
        Assert-ValidAuthenticodeSignature -Path (Join-Path $destination $bootstrapperFile)
    }

    $unexpectedDevelopmentFiles = @(
        Get-ChildItem -LiteralPath $destination -Recurse -File |
            Where-Object { $_.Extension -in @(".pdb", ".xaml") }
    )
    if ($unexpectedDevelopmentFiles.Count -ne 0) {
        throw "Published output contains development-only files: $($unexpectedDevelopmentFiles.FullName -join ', ')"
    }

    $removedRuntimeFiles = @(
        "Dia2Lib.dll",
        "DirectML.dll",
        "KernelTraceControl.dll",
        "KernelTraceControl.Win61.dll",
        "Microsoft.Diagnostics.FastSerialization.dll",
        "Microsoft.Diagnostics.NETCore.Client.dll",
        "Microsoft.Diagnostics.Tracing.TraceEvent.dll",
        "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
        "Microsoft.Extensions.DependencyInjection.dll",
        "Microsoft.Extensions.Logging.Abstractions.dll",
        "Microsoft.Extensions.Logging.dll",
        "Microsoft.Extensions.Options.dll",
        "Microsoft.Extensions.Primitives.dll",
        "Microsoft.Windows.AI.MachineLearning.dll",
        "msdia140.dll",
        "onnxruntime.dll",
        "TraceReloggerLib.dll"
    )
    $unexpectedRemovedRuntimeFiles = @(
        Get-ChildItem -LiteralPath $destination -Recurse -File |
            Where-Object { $_.Name -in $removedRuntimeFiles }
    )
    if ($unexpectedRemovedRuntimeFiles.Count -ne 0) {
        throw "Published output contains removed runtime files: $($unexpectedRemovedRuntimeFiles.FullName -join ', ')"
    }

    Assert-X64BinaryLayout -Path $destination
    Assert-DependencyAssetsAbsent `
        -Path (Join-Path $destination "PredatorLite.deps.json") `
        -PackagePrefixes @(
            "Microsoft.Diagnostics.NETCore.Client/",
            "Microsoft.Diagnostics.Tracing.TraceEvent/",
            "Microsoft.Extensions.DependencyInjection/",
            "Microsoft.Extensions.DependencyInjection.Abstractions/",
            "Microsoft.Extensions.Logging/",
            "Microsoft.Extensions.Logging.Abstractions/",
            "Microsoft.Extensions.Options/",
            "Microsoft.Extensions.Primitives/") `
        -FileNames @(
            "DirectML.dll",
            "Microsoft.Windows.AI.MachineLearning.dll",
            "onnxruntime.dll")

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
        "PredatorLite.ElevatedHelper.dll",
        "CommunityToolkit.Mvvm.dll",
        "H.NotifyIcon.dll",
        "H.NotifyIcon.WinUI.dll",
        "Microsoft.WinUI.dll"
    )
    $deferredIlAssemblies = @(
        "BlackSharp.Core.dll",
        "DiskInfoToolkit.dll",
        "HidSharp.dll",
        "LibreHardwareMonitorLib.dll",
        "Microsoft.Graphics.Imaging.Projection.dll",
        "Microsoft.ML.OnnxRuntime.dll",
        "Microsoft.Windows.AI.ContentSafety.Projection.dll",
        "Microsoft.Windows.AI.Foundation.Projection.dll",
        "Microsoft.Windows.AI.Imaging.Projection.dll",
        "Microsoft.Windows.AI.MachineLearning.Projection.dll",
        "Microsoft.Windows.AI.Projection.dll",
        "Microsoft.Windows.AI.Text.Projection.dll",
        "Microsoft.Windows.AI.Video.Projection.dll",
        "Microsoft.Windows.Widgets.Projection.dll",
        "Microsoft.WindowsAppRuntime.Bootstrap.Net.dll",
        "Mono.Posix.NETStandard.dll",
        "RAMSPDToolkit-NDD.dll",
        "System.IO.Ports.dll",
        "System.Numerics.Tensors.dll"
    )
    if ($ReadyToRun) {
        $notReadyToRun = @($readyToRunAssemblies | Where-Object {
            -not (Test-ReadyToRunImage -Path (Join-Path $destination $_))
        })
        if ($notReadyToRun.Count -ne 0) {
            throw "ReadyToRun was requested but these startup assemblies lack a managed native header: $($notReadyToRun -join ', ')"
        }

        $unexpectedDeferredReadyToRun = @($deferredIlAssemblies | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $destination $_) -PathType Leaf) -or
                (Test-ReadyToRunImage -Path (Join-Path $destination $_))
        })
        if ($unexpectedDeferredReadyToRun.Count -ne 0) {
            throw "Deferred assemblies must remain IL in the balanced layout: $($unexpectedDeferredReadyToRun -join ', ')"
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
    $maximumBytes = if ($ReadyToRun) { 80MB } else { 65MB }
    if ($totalBytes -gt $maximumBytes) {
        Write-LargestPublishedFiles -Path $destination
        throw "Published $layoutKind layout exceeds the $maximumBytes byte budget: $totalBytes bytes"
    }
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
