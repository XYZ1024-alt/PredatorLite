[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$CertificateThumbprint = $env:PREDATORLITE_SIGNING_THUMBPRINT,
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStore = "CurrentUser",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipSigning
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$installerScript = Join-Path $PSScriptRoot "installer\PredatorLite.iss"
$publishDirectory = Join-Path $repositoryRoot "publish\win-x64"
$releaseInstallerDirectory = Join-Path $repositoryRoot "publish\installer"
$unsignedInstallerDirectory = Join-Path $repositoryRoot "artifacts\installer\unsigned"
$artifactDirectory = if ($SkipSigning) { $unsignedInstallerDirectory } else { $releaseInstallerDirectory }

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE"
    }
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha256.ComputeHash($stream)
        return ($bytes | ForEach-Object { $_.ToString("x2") }) -join ""
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

function Resolve-InnoCompiler {
    $candidates = @(
        $env:INNO_SETUP_COMPILER,
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $compiler) {
        throw "Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup or set INNO_SETUP_COMPILER."
    }

    return [System.IO.Path]::GetFullPath($compiler)
}

function Resolve-SignTool {
    if ($env:SIGNTOOL_PATH -and (Test-Path -LiteralPath $env:SIGNTOOL_PATH -PathType Leaf)) {
        return [System.IO.Path]::GetFullPath($env:SIGNTOOL_PATH)
    }

    $kitsBin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $signTool = Get-ChildItem -LiteralPath $kitsBin -Filter "signtool.exe" -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" } |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1

    if (-not $signTool) {
        throw "Windows SDK SignTool was not found. Install the Windows 10/11 SDK or set SIGNTOOL_PATH."
    }

    return $signTool.FullName
}

function Get-CodeSigningCertificate {
    param(
        [Parameter(Mandatory)]
        [string]$Thumbprint,
        [Parameter(Mandatory)]
        [string]$StoreLocation
    )

    $normalizedThumbprint = $Thumbprint.Replace(" ", "").ToUpperInvariant()
    $location = [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocation
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new("My", $location)

    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        $certificate = $store.Certificates |
            Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
            Select-Object -First 1

        if (-not $certificate) {
            throw "Certificate $normalizedThumbprint was not found in $StoreLocation\My."
        }

        if (-not $certificate.HasPrivateKey) {
            throw "Certificate $normalizedThumbprint does not have an accessible private key."
        }

        $codeSigningOid = "1.3.6.1.5.5.7.3.3"
        $hasCodeSigningEku = $false
        foreach ($extension in $certificate.Extensions) {
            if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] -and
                $extension.EnhancedKeyUsages.ObjectId.Value -contains $codeSigningOid) {
                $hasCodeSigningEku = $true
                break
            }
        }

        if (-not $hasCodeSigningEku) {
            throw "Certificate $normalizedThumbprint is not valid for Code Signing."
        }

        $now = Get-Date
        if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
            throw "Certificate $normalizedThumbprint is outside its validity period."
        }

        if ($certificate.Subject -eq $certificate.Issuer) {
            throw "Certificate $normalizedThumbprint is self-signed and cannot be used for a public release."
        }

        $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
        $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        try {
            if (-not $chain.Build($certificate)) {
                $statuses = $chain.ChainStatus | ForEach-Object { $_.StatusInformation.Trim() }
                throw "Certificate $normalizedThumbprint is not trusted: $($statuses -join '; ')"
            }
        }
        finally {
            $chain.Dispose()
        }

        return $certificate
    }
    finally {
        $store.Close()
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Build.props")
    $Version = [string]($buildProperties.Project.PropertyGroup.Version | Select-Object -First 1)
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Installer Version must use three numeric components, for example 0.1.0."
}

$timestampUri = $null
if (-not [System.Uri]::TryCreate($TimestampUrl, [System.UriKind]::Absolute, [ref]$timestampUri) -or
    $timestampUri.Scheme -notin @("http", "https") -or
    -not [string]::IsNullOrEmpty($timestampUri.UserInfo) -or
    -not [string]::IsNullOrEmpty($timestampUri.Query) -or
    -not [string]::IsNullOrEmpty($timestampUri.Fragment)) {
    throw "TimestampUrl must be a plain absolute HTTP or HTTPS URL without credentials, query, or fragment."
}
$TimestampUrl = $timestampUri.AbsoluteUri

$innoCompiler = Resolve-InnoCompiler
$signTool = $null
$certificate = $null
if (-not $SkipSigning) {
    if ($Configuration -ne "Release") {
        throw "Signed installers must use the Release configuration."
    }

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "A trusted Authenticode certificate is required. Pass -CertificateThumbprint or set PREDATORLITE_SIGNING_THUMBPRINT. Use -SkipSigning only for local installer testing."
    }

    $signTool = Resolve-SignTool
    $certificate = Get-CodeSigningCertificate -Thumbprint $CertificateThumbprint -StoreLocation $CertificateStore
}

if (Test-Path -LiteralPath $artifactDirectory) {
    Remove-Item -LiteralPath $artifactDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

& $publishScript -Configuration $Configuration -OutputPath "publish\win-x64"
if ($LASTEXITCODE -ne 0) {
    throw "PredatorLite publish failed with exit code $LASTEXITCODE"
}

if (-not $SkipSigning) {
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

    $storeSwitch = if ($CertificateStore -eq "LocalMachine") { @("/sm") } else { @() }
    foreach ($relativePath in $ownedBinaries) {
        $binaryPath = Join-Path $publishDirectory $relativePath
        if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
            throw "Published binary is missing: $binaryPath"
        }

        $arguments = @("sign") + $storeSwitch + @(
            "/sha1", $certificate.Thumbprint,
            "/fd", "SHA256",
            "/td", "SHA256",
            "/tr", $TimestampUrl,
            "/d", "PredatorLite",
            "/du", "https://github.com/XYZ1024-alt/PredatorLite",
            $binaryPath
        )
        Invoke-NativeCommand -FilePath $signTool -Arguments $arguments
        Invoke-NativeCommand -FilePath $signTool -Arguments @("verify", "/pa", "/all", "/tw", $binaryPath)
    }
}

$outputSuffix = if ($SkipSigning) { "-unsigned" } else { "" }
$setupPath = Join-Path $artifactDirectory "PredatorLite-Setup-$Version-win-x64$outputSuffix.exe"

$compilerArguments = @(
    "/DAppVersion=$Version",
    "/DOutputSuffix=$outputSuffix",
    "/O$artifactDirectory"
)
if (-not $SkipSigning) {
    $storeOption = if ($CertificateStore -eq "LocalMachine") { " /sm" } else { "" }
    $innoSignCommand = "`$q$signTool`$q sign$storeOption /sha1 $($certificate.Thumbprint) /fd SHA256 /td SHA256 /tr $TimestampUrl /d `$qPredatorLite`$q /du https://github.com/XYZ1024-alt/PredatorLite `$f"
    $compilerArguments += "/DSignInstaller"
    $compilerArguments += "/SPredatorLiteSign=$innoSignCommand"
}
$compilerArguments += $installerScript
Invoke-NativeCommand -FilePath $innoCompiler -Arguments $compilerArguments

if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Inno Setup did not create the expected installer: $setupPath"
}

if (-not $SkipSigning) {
    Invoke-NativeCommand -FilePath $signTool -Arguments @("verify", "/pa", "/all", "/tw", $setupPath)
}

$hash = Get-Sha256Hex -Path $setupPath
$hashPath = "$setupPath.sha256"
[System.IO.File]::WriteAllText($hashPath, "$hash  $([System.IO.Path]::GetFileName($setupPath))`r`n", [System.Text.Encoding]::ASCII)

if ($SkipSigning) {
    Write-Warning "Created an unsigned installer for local testing only. Do not publish it."
}
Write-Host "Installer: $setupPath"
Write-Host "SHA256:    $hashPath"
