[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Expand-ZipArchiveSafely {
    param(
        [Parameter(Mandatory)]
        [string]$ArchivePath,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    $destinationRoot = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\', '/') +
        [System.IO.Path]::DirectorySeparatorChar
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryPath = [System.IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            if (-not $entryPath.StartsWith(
                    $destinationRoot,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Archive entry escapes the extraction directory: $($entry.FullName)"
            }

            if ([string]::IsNullOrEmpty($entry.Name)) {
                [System.IO.Directory]::CreateDirectory($entryPath) | Out-Null
                continue
            }

            [System.IO.Directory]::CreateDirectory(
                [System.IO.Path]::GetDirectoryName($entryPath)) | Out-Null
            $input = $entry.Open()
            $output = [System.IO.File]::Open(
                $entryPath,
                [System.IO.FileMode]::Create,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Find-MakeAppx {
    $command = Get-Command "MakeAppx.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $windowsKitsBin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $windowsKitsBin -PathType Container)) {
        throw "Windows SDK MakeAppx.exe was not found. Install the Windows 11 SDK packaging tools."
    }

    $candidates = @(
        Get-ChildItem -LiteralPath $windowsKitsBin -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\MakeAppx.exe" } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    )
    if ($candidates.Count -eq 0) {
        throw "Windows SDK MakeAppx.exe was not found. Install the Windows 11 SDK packaging tools."
    }

    return $candidates[0]
}

function Get-RequiredNode {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode]$Node,
        [Parameter(Mandatory)]
        [string]$XPath,
        [Parameter(Mandatory)]
        [System.Xml.XmlNamespaceManager]$NamespaceManager,
        [Parameter(Mandatory)]
        [string]$Description
    )

    $result = $Node.SelectSingleNode($XPath, $NamespaceManager)
    if ($null -eq $result) {
        throw "Package manifest is missing $Description."
    }
    return $result
}

if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "ExpectedVersion must use four numeric components with a zero fourth component."
}

$packageItem = Get-Item -LiteralPath $PackagePath -ErrorAction Stop
if ($packageItem.PSIsContainer) {
    throw "PackagePath must reference a .msixupload or .msix file."
}
$extension = $packageItem.Extension.ToLowerInvariant()
if ($extension -notin @(".msixupload", ".msix")) {
    throw "PackagePath must reference a .msixupload or .msix file."
}

$validationRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    "PredatorLite-StoreValidation-" + [Guid]::NewGuid().ToString("N"))
$uploadDirectory = Join-Path $validationRoot "upload"
$unpackDirectory = Join-Path $validationRoot "package"

try {
    [System.IO.Directory]::CreateDirectory($validationRoot) | Out-Null
    $msixPath = $packageItem.FullName
    if ($extension -eq ".msixupload") {
        Expand-ZipArchiveSafely -ArchivePath $packageItem.FullName -Destination $uploadDirectory
        $msixFiles = @(Get-ChildItem -LiteralPath $uploadDirectory -Recurse -File -Filter "*.msix")
        if ($msixFiles.Count -ne 1) {
            throw ".msixupload must contain exactly one x64 MSIX; found $($msixFiles.Count)."
        }
        $msixPath = $msixFiles[0].FullName
    }

    $makeAppx = Find-MakeAppx
    $makeAppxOutput = & $makeAppx unpack /p $msixPath /d $unpackDirectory /o 2>&1
    $makeAppxExitCode = $LASTEXITCODE
    if ($makeAppxExitCode -ne 0) {
        throw "MakeAppx unpack failed with exit code $makeAppxExitCode.`n$(
            $makeAppxOutput -join [Environment]::NewLine)"
    }

    $manifestPath = Join-Path $unpackDirectory "AppxManifest.xml"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Unpacked package does not contain AppxManifest.xml."
    }

    [xml]$manifest = Get-Content -LiteralPath $manifestPath
    $namespaces = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaces.AddNamespace("f", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $namespaces.AddNamespace("desktop", "http://schemas.microsoft.com/appx/manifest/desktop/windows10")
    $namespaces.AddNamespace("rescap", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")
    $namespaces.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
    $namespaces.AddNamespace("uap5", "http://schemas.microsoft.com/appx/manifest/uap/windows10/5")

    $identity = Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Identity" `
        -NamespaceManager $namespaces `
        -Description "Identity"

    $associationPath = Join-Path $repositoryRoot "src\PredatorLite.Package\Package.StoreAssociation.xml"
    [xml]$association = Get-Content -LiteralPath $associationPath
    $associationNamespaces = [System.Xml.XmlNamespaceManager]::new($association.NameTable)
    $associationNamespaces.AddNamespace(
        "s",
        "http://schemas.microsoft.com/appx/2010/storeassociation")
    $associationPublisher = $association.SelectSingleNode(
        "/s:StoreAssociation/s:Publisher",
        $associationNamespaces).InnerText
    $associationPublisherDisplayName = $association.SelectSingleNode(
        "/s:StoreAssociation/s:PublisherDisplayName",
        $associationNamespaces).InnerText
    $mainPackageIdentityName = $association.SelectSingleNode(
        "/s:StoreAssociation/s:ProductReservedInfo/s:MainPackageIdentityName",
        $associationNamespaces).InnerText
    $separatorIndex = $mainPackageIdentityName.LastIndexOf('_')
    if ($separatorIndex -le 0) {
        throw "Package.StoreAssociation.xml contains an invalid MainPackageIdentityName."
    }
    $associationIdentityName = $mainPackageIdentityName.Substring(0, $separatorIndex)

    if ($identity.GetAttribute("Name") -cne $associationIdentityName) {
        throw "Package Identity Name does not match Package.StoreAssociation.xml."
    }
    if ($identity.GetAttribute("Publisher") -cne $associationPublisher) {
        throw "Package Identity Publisher does not match Package.StoreAssociation.xml."
    }
    if ($identity.GetAttribute("Version") -ne $ExpectedVersion) {
        throw "Package version $($identity.GetAttribute('Version')) does not match $ExpectedVersion."
    }
    if ($identity.GetAttribute("ProcessorArchitecture") -ne "x64") {
        throw "Package architecture must be x64; found $($identity.GetAttribute('ProcessorArchitecture'))."
    }

    $publisherDisplayName = Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Properties/f:PublisherDisplayName" `
        -NamespaceManager $namespaces `
        -Description "PublisherDisplayName"
    if ($publisherDisplayName.InnerText -cne $associationPublisherDisplayName) {
        throw "Package PublisherDisplayName does not match Package.StoreAssociation.xml."
    }

    $targetDeviceFamily = Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Dependencies/f:TargetDeviceFamily[@Name='Windows.Desktop']" `
        -NamespaceManager $namespaces `
        -Description "Windows.Desktop TargetDeviceFamily"
    if ($targetDeviceFamily.GetAttribute("MinVersion") -ne "10.0.26100.0") {
        throw "Windows.Desktop MinVersion must be 10.0.26100.0."
    }

    Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Capabilities/rescap:Capability[@Name='runFullTrust']" `
        -NamespaceManager $namespaces `
        -Description "runFullTrust capability" | Out-Null
    if ($null -ne $manifest.SelectSingleNode(
            "/f:Package/f:Capabilities/rescap:Capability[@Name='allowElevation']",
            $namespaces)) {
        throw "Store package must not declare allowElevation."
    }

    if ($null -ne $manifest.SelectSingleNode(
            "/f:Package/f:Applications/f:Application/f:Extensions/desktop:Extension[@Category='windows.fullTrustProcess']",
            $namespaces)) {
        throw "Store package must not declare a fullTrustProcess helper extension."
    }

    Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Applications/f:Application[@Executable='PredatorLite.exe' and @EntryPoint='Windows.FullTrustApplication']" `
        -NamespaceManager $namespaces `
        -Description "PredatorLite full-trust application" | Out-Null

    $startupExtension = Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Applications/f:Application/f:Extensions/uap5:Extension[@Category='windows.startupTask' and @Executable='PredatorLite.exe' and @EntryPoint='Windows.FullTrustApplication']" `
        -NamespaceManager $namespaces `
        -Description "PredatorLite startupTask extension"
    Get-RequiredNode `
        -Node $startupExtension `
        -XPath "uap5:StartupTask[@TaskId='PredatorLiteStartup' and @Enabled='false']" `
        -NamespaceManager $namespaces `
        -Description "PredatorLiteStartup declaration" | Out-Null

    $dependencyNames = @(
        $manifest.SelectNodes(
            "/f:Package/f:Dependencies/f:PackageDependency",
            $namespaces) |
            ForEach-Object { $_.GetAttribute("Name") }
    )
    if (-not ($dependencyNames | Where-Object {
                $_.StartsWith(
                    "Microsoft.WindowsAppRuntime.",
                    [System.StringComparison]::OrdinalIgnoreCase)
            })) {
        throw "Package is missing a Windows App Runtime framework dependency."
    }
    if (-not ($dependencyNames | Where-Object {
                $_.StartsWith(
                    "Microsoft.VCLibs.",
                    [System.StringComparison]::OrdinalIgnoreCase)
            })) {
        throw "Package is missing a VCLibs framework dependency."
    }

    $visualElements = Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Applications/f:Application/uap:VisualElements" `
        -NamespaceManager $namespaces `
        -Description "VisualElements"
    $defaultTile = Get-RequiredNode `
        -Node $visualElements `
        -XPath "uap:DefaultTile" `
        -NamespaceManager $namespaces `
        -Description "DefaultTile"
    $splashScreen = Get-RequiredNode `
        -Node $visualElements `
        -XPath "uap:SplashScreen" `
        -NamespaceManager $namespaces `
        -Description "SplashScreen"
    $propertiesLogo = Get-RequiredNode `
        -Node $manifest `
        -XPath "/f:Package/f:Properties/f:Logo" `
        -NamespaceManager $namespaces `
        -Description "Store logo"

    $assetReferences = @(
        $visualElements.GetAttribute("Square44x44Logo"),
        $visualElements.GetAttribute("Square150x150Logo"),
        $defaultTile.GetAttribute("Square71x71Logo"),
        $defaultTile.GetAttribute("Square310x310Logo"),
        $defaultTile.GetAttribute("Wide310x150Logo"),
        $splashScreen.GetAttribute("Image"),
        [string]$propertiesLogo.InnerText
    )
    $expectedAssetReferences = @(
        "Assets\Square44x44Logo.png",
        "Assets\Square150x150Logo.png",
        "Assets\SmallTile.png",
        "Assets\LargeTile.png",
        "Assets\Wide310x150Logo.png",
        "Assets\SplashScreen.png",
        "Assets\StoreLogo.png"
    )
    foreach ($expectedAsset in $expectedAssetReferences) {
        if ($expectedAsset -notin $assetReferences) {
            throw "Package manifest does not reference visual asset $expectedAsset."
        }
        $assetPath = Join-Path $unpackDirectory ($expectedAsset.Replace('\', [System.IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            throw "Package is missing visual asset $expectedAsset."
        }
    }

    $requiredRootFiles = @(
        "PredatorLite.exe",
        "PredatorLite.dll",
        "PredatorLite.deps.json",
        "PredatorLite.runtimeconfig.json",
        "PredatorLite.FanGuard.exe",
        "PredatorLite.FanGuard.dll",
        "PredatorLite.FanGuard.deps.json",
        "PredatorLite.FanGuard.runtimeconfig.json",
        "coreclr.dll",
        "hostfxr.dll",
        "System.Private.CoreLib.dll"
    )
    foreach ($requiredFile in $requiredRootFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $unpackDirectory $requiredFile) -PathType Leaf)) {
            throw "Package root is missing $requiredFile."
        }
    }

    $helperFiles = @(
        Get-ChildItem -LiteralPath $unpackDirectory -Recurse -File |
            Where-Object {
                $_.Name.StartsWith(
                    "PredatorLite.ElevatedHelper",
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
    )
    if ($helperFiles.Count -ne 0) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $unpackDirectory,
            $helperFiles[0].FullName)
        throw "Store package contains a forbidden ElevatedHelper payload: $relativePath"
    }

    $unsafeStagingEntry = Get-ChildItem -LiteralPath $unpackDirectory -Recurse -Force |
        Where-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($unpackDirectory, $_.FullName)
            $relativePath -match '(^|[\\/])(staging|fallback)([\\/]|$)'
        } |
        Select-Object -First 1
    if ($unsafeStagingEntry) {
        throw "Package contains a forbidden helper staging or fallback path: $($unsafeStagingEntry.FullName)"
    }

    Write-Host "Validated Store package $($packageItem.Name) as version $ExpectedVersion x64."
}
finally {
    if (Test-Path -LiteralPath $validationRoot) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}
