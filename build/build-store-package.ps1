[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateSet("StoreUpload", "Sideload")]
    [string]$Mode,

    [string]$CertificatePath,

    [string]$CertificatePassword
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-output.ps1")

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Sha256File {
    param(
        [Parameter(Mandatory)]
        [string]$AssetPath
    )

    $assetName = Split-Path -Leaf $AssetPath
    $hashPath = "$AssetPath.sha256"
    $hash = Get-Sha256Hex -Path $AssetPath
    [System.IO.File]::WriteAllText(
        $hashPath,
        "$hash  $assetName`r`n",
        [System.Text.Encoding]::ASCII)
    return $hashPath
}

function Promote-Directory {
    param(
        [Parameter(Mandatory)]
        [string]$Candidate,
        [Parameter(Mandatory)]
        [string]$Destination,
        [Parameter(Mandatory)]
        [System.IO.FileStream]$OutputLock
    )

    Assert-PredatorLiteOutputLock -RepositoryRoot $repositoryRoot -OutputLock $OutputLock
    $backup = "$Destination.backup-$([Guid]::NewGuid().ToString('N'))"
    $destinationMoved = $false
    try {
        if (Test-Path -LiteralPath $Destination) {
            Move-Item -LiteralPath $Destination -Destination $backup
            $destinationMoved = $true
        }

        Move-Item -LiteralPath $Candidate -Destination $Destination
    }
    catch {
        if (Test-Path -LiteralPath $Destination) {
            Remove-DirectoryWithRetry -Path $Destination
        }
        if ($destinationMoved -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $Destination
        }
        throw
    }

    if ($destinationMoved -and (Test-Path -LiteralPath $backup)) {
        Remove-DirectoryWithRetry -Path $backup
    }
}

if ($Configuration -ne "Release") {
    throw "Store packages must use the Release configuration."
}

$buildPropertiesPath = Join-Path $repositoryRoot "Directory.Build.props"
[xml]$buildProperties = Get-Content -LiteralPath $buildPropertiesPath
$projectVersion = [string](
    $buildProperties.Project.PropertyGroup.Version |
        Select-Object -First 1)
if ($Version -ne $projectVersion) {
    throw "Store Version $Version does not match Directory.Build.props version $projectVersion."
}

$releaseVersion = Resolve-PredatorLiteReleaseVersion `
    -BaseVersion $Version `
    -Channel Stable
$appxPackageVersion = $releaseVersion.AssemblyVersion
if ($appxPackageVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "Store AppxPackageVersion must have a zero fourth component: $appxPackageVersion"
}

if ($Mode -eq "StoreUpload") {
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -or
        -not [string]::IsNullOrEmpty($CertificatePassword)) {
        throw "StoreUpload does not accept a signing certificate."
    }
}
else {
    if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        throw "Sideload requires CertificatePath."
    }
    if ([string]::IsNullOrEmpty($CertificatePassword)) {
        throw "Sideload requires CertificatePassword."
    }
}

$vswherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
    throw "Visual Studio vswhere.exe was not found. Install Visual Studio with MSBuild and MSIX Packaging Tools."
}

$installationPath = [string](& $vswherePath `
    -latest `
    -products * `
    -requires Microsoft.Component.MSBuild `
    -property installationPath)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installationPath)) {
    throw "Visual Studio MSBuild was not found. Install Visual Studio with MSBuild and MSIX Packaging Tools."
}

$msbuildPath = Join-Path $installationPath "MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
    throw "Visual Studio MSBuild.exe was not found beneath $installationPath."
}

$desktopBridgeTargets = Join-Path $installationPath "MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.targets"
if (-not (Test-Path -LiteralPath $desktopBridgeTargets -PathType Leaf)) {
    throw "Windows Application Packaging Project targets are missing. Install the Visual Studio MSIX Packaging Tools component."
}

$packageProject = Join-Path $repositoryRoot "src\PredatorLite.Package\PredatorLite.Package.wapproj"
if (-not (Test-Path -LiteralPath $packageProject -PathType Leaf)) {
    throw "Store packaging project was not found: $packageProject"
}

$certificateFullPath = $null
if ($Mode -eq "Sideload") {
    $certificateItem = Get-Item -LiteralPath $CertificatePath -ErrorAction Stop
    if ($certificateItem.PSIsContainer) {
        throw "CertificatePath must reference a PFX file."
    }
    $certificateFullPath = $certificateItem.FullName
}

$outputLock = Enter-PredatorLiteOutputLock -RepositoryRoot $repositoryRoot
$buildId = [Guid]::NewGuid().ToString("N")
$workDirectory = Assert-SafeRepositoryOutputPath `
    -RepositoryRoot $repositoryRoot `
    -Destination (Join-Path $repositoryRoot "artifacts\store\work\$buildId") `
    -AllowedRelativeRoots @("artifacts\store")
$appxPackageDirectory = Join-Path $workDirectory "AppPackages"
$candidateDirectory = $null

try {
    [System.IO.Directory]::CreateDirectory($appxPackageDirectory) | Out-Null
    $manifestTemplatePath = Join-Path $repositoryRoot "src\PredatorLite.Package\Package.appxmanifest"
    $generatedManifestPath = Join-Path $workDirectory "Package.appxmanifest"
    [xml]$generatedManifest = Get-Content -LiteralPath $manifestTemplatePath
    $manifestNamespaces = [System.Xml.XmlNamespaceManager]::new($generatedManifest.NameTable)
    $manifestNamespaces.AddNamespace(
        "f",
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $manifestIdentity = $generatedManifest.SelectSingleNode(
        "/f:Package/f:Identity",
        $manifestNamespaces)
    if ($null -eq $manifestIdentity) {
        throw "Store package manifest is missing Package/Identity."
    }
    $manifestIdentity.SetAttribute("Version", $appxPackageVersion)
    $manifestWriterSettings = [System.Xml.XmlWriterSettings]::new()
    $manifestWriterSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $manifestWriterSettings.Indent = true
    $manifestWriter = [System.Xml.XmlWriter]::Create(
        $generatedManifestPath,
        $manifestWriterSettings)
    try {
        $generatedManifest.Save($manifestWriter)
    }
    finally {
        $manifestWriter.Dispose()
    }

    $appxPackageDirectoryProperty = $appxPackageDirectory.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $msbuildArguments = @(
        $packageProject,
        "/restore",
        "/t:Rebuild",
        "/m",
        "/nologo",
        "/v:minimal",
        "/p:Configuration=$Configuration",
        "/p:Platform=x64",
        "/p:RuntimeIdentifier=win-x64",
        "/p:DistributionChannel=Store",
        "/p:WindowsAppSdkBootstrapInitialize=false",
        "/p:SelfContained=true",
        "/p:WindowsAppSDKSelfContained=false",
        "/p:PublishTrimmed=false",
        "/p:PublishSingleFile=false",
        "/p:DebugType=portable",
        "/p:DebugSymbols=true",
        "/p:StorePackageManifestPath=$generatedManifestPath",
        "/p:AppxBundle=Never",
        "/p:AppxBundlePlatforms=x64",
        "/p:AppxPackageDir=$appxPackageDirectoryProperty"
    )

    if ($Mode -eq "StoreUpload") {
        $msbuildArguments += @(
            "/p:UapAppxPackageBuildMode=CI",
            "/p:AppxPackageSigningEnabled=false"
        )
    }
    else {
        $msbuildArguments += @(
            "/p:UapAppxPackageBuildMode=SideloadOnly",
            "/p:AppxPackageSigningEnabled=true",
            "/p:PackageCertificateKeyFile=$certificateFullPath",
            "/p:PackageCertificatePassword=$CertificatePassword"
        )
    }

    & $msbuildPath @msbuildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Store package build failed with exit code $LASTEXITCODE."
    }

    if ($Mode -eq "StoreUpload") {
        $uploads = @(
            Get-ChildItem -LiteralPath $workDirectory -Recurse -File -Filter "*.msixupload"
        )
        if ($uploads.Count -ne 1) {
            throw "StoreUpload must generate exactly one .msixupload; found $($uploads.Count)."
        }

        $publishRoot = Join-Path $repositoryRoot "publish"
        [System.IO.Directory]::CreateDirectory($publishRoot) | Out-Null
        $candidateDirectory = Assert-SafeRepositoryOutputPath `
            -RepositoryRoot $repositoryRoot `
            -Destination (Join-Path $publishRoot "store.candidate-$buildId") `
            -AllowedRelativeRoots @("publish")
        [System.IO.Directory]::CreateDirectory($candidateDirectory) | Out-Null

        $assetName = "PredatorLite-Store-$Version-win-x64.msixupload"
        $candidateAsset = Join-Path $candidateDirectory $assetName
        Copy-Item -LiteralPath $uploads[0].FullName -Destination $candidateAsset
        $candidateHash = Write-Sha256File -AssetPath $candidateAsset
        $entries = @(Get-ChildItem -LiteralPath $candidateDirectory -Force)
        if ($entries.Count -ne 2 -or
            -not (Test-Path -LiteralPath $candidateAsset -PathType Leaf) -or
            -not (Test-Path -LiteralPath $candidateHash -PathType Leaf)) {
            throw "Store upload candidate must contain only the upload and its SHA-256 sidecar."
        }

        $storeDirectory = Assert-SafeRepositoryOutputPath `
            -RepositoryRoot $repositoryRoot `
            -Destination (Join-Path $publishRoot "store") `
            -AllowedRelativeRoots @("publish")
        Promote-Directory `
            -Candidate $candidateDirectory `
            -Destination $storeDirectory `
            -OutputLock $outputLock
        $candidateDirectory = $null
        Write-Host "Created Store upload: $(Join-Path $storeDirectory $assetName)"
    }
    else {
        $testDirectories = @(
            Get-ChildItem -LiteralPath $workDirectory -Recurse -Directory |
                Where-Object {
                    $_.Name.EndsWith("_Test", [System.StringComparison]::OrdinalIgnoreCase) -and
                    (Test-Path -LiteralPath (Join-Path $_.FullName "Add-AppDevPackage.ps1") -PathType Leaf)
                }
        )
        if ($testDirectories.Count -ne 1) {
            throw "Sideload must generate exactly one complete _Test directory; found $($testDirectories.Count)."
        }

        $sideloadRoot = Join-Path $repositoryRoot "artifacts\store\sideload"
        [System.IO.Directory]::CreateDirectory($sideloadRoot) | Out-Null
        $targetName = "PredatorLite-Store-$Version-win-x64_Test"
        $candidateDirectory = Assert-SafeRepositoryOutputPath `
            -RepositoryRoot $repositoryRoot `
            -Destination (Join-Path $sideloadRoot "$targetName.candidate-$buildId") `
            -AllowedRelativeRoots @("artifacts\store")
        Copy-Item -LiteralPath $testDirectories[0].FullName -Destination $candidateDirectory -Recurse

        $targetDirectory = Assert-SafeRepositoryOutputPath `
            -RepositoryRoot $repositoryRoot `
            -Destination (Join-Path $sideloadRoot $targetName) `
            -AllowedRelativeRoots @("artifacts\store")
        Promote-Directory `
            -Candidate $candidateDirectory `
            -Destination $targetDirectory `
            -OutputLock $outputLock
        $candidateDirectory = $null
        Write-Host "Created Store sideload directory: $targetDirectory"
    }
}
finally {
    if ($candidateDirectory -and (Test-Path -LiteralPath $candidateDirectory)) {
        Remove-DirectoryWithRetry -Path $candidateDirectory
    }
    if (Test-Path -LiteralPath $workDirectory) {
        Remove-DirectoryWithRetry -Path $workDirectory
    }
    $outputLock.Dispose()
}
