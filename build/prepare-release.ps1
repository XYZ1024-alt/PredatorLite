[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-output.ps1")

if ($Configuration -ne "Release") {
    throw "Public release packaging must use the Release configuration."
}

$buildPropertiesPath = Join-Path $repositoryRoot "Directory.Build.props"
[xml]$buildProperties = Get-Content -LiteralPath $buildPropertiesPath
$projectVersion = [string]($buildProperties.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Release Version must use three numeric components, for example 1.0.0."
}
if ($Version -ne $projectVersion) {
    throw "Release Version $Version does not match Directory.Build.props version $projectVersion."
}

$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$installerScript = Join-Path $PSScriptRoot "build-installer.ps1"
$portableDirectory = Join-Path $repositoryRoot "publish\win-x64"
$installerDirectory = Join-Path $repositoryRoot "publish\installer"
$releaseDirectory = Join-Path $repositoryRoot "publish\release"
$portableZipName = "PredatorLite-$Version-win-x64-portable.zip"
$installerName = "PredatorLite-Setup-$Version-win-x64.exe"
$expectedAssetNames = @(
    $portableZipName,
    "$portableZipName.sha256",
    $installerName,
    "$installerName.sha256"
)

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ($sha256.ComputeHash($stream) | ForEach-Object { $_.ToString("x2") }) -join ""
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

function Write-Sha256File {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $hashPath = "$Path.sha256"
    $hash = Get-Sha256Hex -Path $Path
    $contents = "$hash  $([System.IO.Path]::GetFileName($Path))`r`n"
    [System.IO.File]::WriteAllText($hashPath, $contents, [System.Text.Encoding]::ASCII)
    return $hashPath
}

function Assert-NoAuthenticodeCertificateTable {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if ($peReader.PEHeaders.PEHeader.CertificateTableDirectory.Size -ne 0) {
            throw "Public release file contains an Authenticode certificate table: $Path"
        }
    }
    finally {
        $peReader.Dispose()
        $stream.Dispose()
    }
}

try {
    Assert-SafeRepositoryOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Destination (Join-Path $releaseDirectory "output") `
        -AllowedRelativeRoots @("publish") | Out-Null

    Remove-DirectoryWithRetry -Path $releaseDirectory
    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

    & $publishScript `
        -Configuration $Configuration `
        -OutputPath "publish\win-x64" `
        -ReadyToRun:$true
    if ($LASTEXITCODE -ne 0) {
        throw "PredatorLite portable publish failed with exit code $LASTEXITCODE"
    }

    & $installerScript `
        -Configuration $Configuration `
        -Version $Version `
        -SkipSigning `
        -PublicRelease
    if ($LASTEXITCODE -ne 0) {
        throw "PredatorLite installer build failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $portableDirectory -PathType Container)) {
        throw "Portable publish directory was not created: $portableDirectory"
    }
    $installerPath = Join-Path $installerDirectory $installerName
    $installerHashPath = "$installerPath.sha256"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $installerHashPath -PathType Leaf)) {
        throw "Public installer assets were not created in $installerDirectory."
    }

    $ownedBinaries = @(
        "PredatorLite.exe",
        "PredatorLite.dll",
        "PredatorLite.Core.dll",
        "PredatorLite.Platform.Windows.dll",
        "PredatorLite.FanGuard.exe",
        "PredatorLite.FanGuard.dll",
        "PredatorLite.ElevatedHelper.exe",
        "PredatorLite.ElevatedHelper.dll"
    )
    foreach ($relativePath in $ownedBinaries) {
        $binaryPath = Join-Path $portableDirectory $relativePath
        if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
            throw "Portable release is missing first-party binary: $relativePath"
        }
        Assert-NoAuthenticodeCertificateTable -Path $binaryPath
    }
    $forbiddenPortableEntries = @(
        Get-ChildItem -LiteralPath $portableDirectory -File -Recurse |
            Where-Object { $_.Name -match '(?i)unsigned|test-only|test-signed' }
    )
    if ($forbiddenPortableEntries.Count -ne 0) {
        throw "Portable release contains a test-only file name: $($forbiddenPortableEntries.Name -join ', ')"
    }
    Assert-NoAuthenticodeCertificateTable -Path $installerPath

    $portableZipPath = Join-Path $releaseDirectory $portableZipName
    Compress-Archive `
        -Path (Join-Path $portableDirectory "*") `
        -DestinationPath $portableZipPath `
        -CompressionLevel Optimal `
        -Force
    $portableHashPath = Write-Sha256File -Path $portableZipPath

    $releaseInstallerPath = Join-Path $releaseDirectory $installerName
    $releaseInstallerHashPath = Join-Path $releaseDirectory "$installerName.sha256"
    Copy-Item -LiteralPath $installerPath -Destination $releaseInstallerPath -Force
    Copy-Item -LiteralPath $installerHashPath -Destination $releaseInstallerHashPath -Force

    $releaseAssetPaths = @(
        $portableZipPath,
        $portableHashPath,
        $releaseInstallerPath,
        $releaseInstallerHashPath
    )
    foreach ($assetPath in $releaseAssetPaths) {
        if ([System.IO.Path]::GetFileName($assetPath) -match '(?i)unsigned|test-only|test-signed') {
            throw "Public release asset has a test-only name: $assetPath"
        }
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            throw "Public release asset is missing: $assetPath"
        }
    }

    foreach ($assetPath in @($portableZipPath, $releaseInstallerPath)) {
        $hashPath = "$assetPath.sha256"
        $expected = "$(Get-Sha256Hex -Path $assetPath)  $([System.IO.Path]::GetFileName($assetPath))`r`n"
        $actual = [System.IO.File]::ReadAllText($hashPath, [System.Text.Encoding]::ASCII)
        if ($actual -ne $expected) {
            throw "SHA-256 sidecar does not match $assetPath."
        }
    }

    $entries = @(Get-ChildItem -LiteralPath $releaseDirectory -Force)
    $unexpectedEntries = @(
        $entries | Where-Object { $_.PSIsContainer -or $_.Name -notin $expectedAssetNames }
    )
    if ($entries.Count -ne $expectedAssetNames.Count -or $unexpectedEntries.Count -ne 0) {
        throw "Public release directory must contain only the four expected assets."
    }

    Write-Host "Release assets prepared in $releaseDirectory"
    foreach ($assetPath in $releaseAssetPaths) {
        Write-Host ("  {0,12:N0}  {1}" -f (Get-Item -LiteralPath $assetPath).Length, [System.IO.Path]::GetFileName($assetPath))
    }
}
catch {
    $releaseFailure = $_
    try {
        Remove-DirectoryWithRetry -Path $releaseDirectory
    }
    catch {
        throw "$($releaseFailure.Exception.Message) Release output cleanup also failed: $($_.Exception.Message)"
    }
    throw $releaseFailure
}
