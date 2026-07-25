[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$CertificateThumbprint = $env:PREDATORLITE_SIGNING_THUMBPRINT,
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStore = "CurrentUser",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipSigning,
    [switch]$TestSigning
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-output.ps1")

$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$installerScript = Join-Path $PSScriptRoot "installer\PredatorLite.iss"
$invocationId = [guid]::NewGuid().ToString("N")
$installerWorkDirectory = Join-Path $repositoryRoot "artifacts\installer\work\$invocationId"
$publishOutputPath = "artifacts\installer\work\$invocationId\win-x64"
$publishDirectory = Join-Path $repositoryRoot $publishOutputPath
$compilerOutputDirectory = Join-Path $installerWorkDirectory "setup"
$releaseInstallerDirectory = Join-Path $repositoryRoot "publish\installer"
$unsignedInstallerDirectory = Join-Path $repositoryRoot "artifacts\installer\unsigned"
$testSignedInstallerDirectory = Join-Path $repositoryRoot "artifacts\installer\test-signed"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [int]$RetryCount = 0,
        [int]$RetryDelaySeconds = 2
    )

    for ($attempt = 0; $attempt -le $RetryCount; $attempt++) {
        & $FilePath @Arguments
        if ($LASTEXITCODE -eq 0) {
            return
        }
        if ($attempt -lt $RetryCount) {
            Start-Sleep -Seconds $RetryDelaySeconds
        }
    }

    throw "$FilePath failed with exit code $LASTEXITCODE after $($RetryCount + 1) attempt(s)"
}

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
    $versionDirectories = @(
        Get-ChildItem -LiteralPath $kitsBin -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
            Sort-Object { [version]$_.Name } -Descending
    )
    foreach ($directory in $versionDirectories) {
        $candidate = Join-Path $directory.FullName "x64\signtool.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    throw "Windows SDK SignTool was not found. Install the Windows 10/11 SDK or set SIGNTOOL_PATH."
}

function Test-WindowsPublicAuthRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Thumbprint
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        "AuthRoot",
        [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        $matches = $store.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
            $Thumbprint,
            $false)
        return $matches.Count -gt 0
    }
    finally {
        $store.Close()
    }
}

function Get-CodeSigningCertificate {
    param(
        [Parameter(Mandatory)]
        [string]$Thumbprint,
        [Parameter(Mandatory)]
        [string]$StoreLocation,
        [Parameter(Mandatory)]
        [bool]$RequirePublicTrust
    )

    $normalizedThumbprint = $Thumbprint.Replace(" ", "").ToUpperInvariant()
    $location = [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocation
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new("My", $location)

    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        $matches = $store.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
            $normalizedThumbprint,
            $false)
        $certificate = if ($matches.Count -gt 0) { $matches[0] } else { $null }

        if (-not $certificate) {
            throw "Certificate $normalizedThumbprint was not found in $StoreLocation\My."
        }
        if (-not $certificate.HasPrivateKey) {
            throw "Certificate $normalizedThumbprint does not have an accessible private key."
        }

        $codeSigningOid = "1.3.6.1.5.5.7.3.3"
        $hasCodeSigningEku = $false
        foreach ($extension in $certificate.Extensions) {
            if ($extension -isnot [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
                continue
            }
            foreach ($usage in $extension.EnhancedKeyUsages) {
                if ($usage.Value -eq $codeSigningOid) {
                    $hasCodeSigningEku = $true
                    break
                }
            }
            if ($hasCodeSigningEku) {
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
        if ($RequirePublicTrust -and $certificate.Subject -eq $certificate.Issuer) {
            throw "Certificate $normalizedThumbprint is self-signed and cannot be used for a public release."
        }

        $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
        $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        $chain.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(5)
        $rootThumbprint = $null
        try {
            if (-not $chain.Build($certificate)) {
                $statuses = $chain.ChainStatus | ForEach-Object { $_.StatusInformation.Trim() }
                throw "Certificate $normalizedThumbprint is not trusted on this build machine: $($statuses -join '; ')"
            }
            if ($chain.ChainElements.Count -eq 0) {
                throw "Certificate $normalizedThumbprint did not produce a trust chain."
            }
            $rootThumbprint = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate.Thumbprint
        }
        finally {
            $chain.Dispose()
        }

        if ($RequirePublicTrust -and -not (Test-WindowsPublicAuthRoot -Thumbprint $rootThumbprint)) {
            throw "Certificate $normalizedThumbprint does not chain to a Windows public AuthRoot certificate."
        }
        if ($RequirePublicTrust) {
            $revocationChain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
            $revocationChain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
            $revocationChain.ChainPolicy.RevocationFlag = [System.Security.Cryptography.X509Certificates.X509RevocationFlag]::ExcludeRoot
            $revocationChain.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(15)
            try {
                if (-not $revocationChain.Build($certificate)) {
                    $statuses = $revocationChain.ChainStatus | ForEach-Object { $_.StatusInformation.Trim() }
                    throw "Certificate $normalizedThumbprint failed online revocation validation: $($statuses -join '; ')"
                }
            }
            finally {
                $revocationChain.Dispose()
            }
        }

        return $certificate
    }
    finally {
        $store.Close()
    }
}

if ($SkipSigning -and $TestSigning) {
    throw "SkipSigning and TestSigning cannot be used together."
}

$signingEnabled = -not $SkipSigning
$productionBuild = $signingEnabled -and -not $TestSigning
if ($signingEnabled -and $Configuration -ne "Release") {
    throw "Signed installers must use the Release configuration."
}

$artifactDirectory = if ($SkipSigning) {
    $unsignedInstallerDirectory
}
elseif ($TestSigning) {
    $testSignedInstallerDirectory
}
else {
    $releaseInstallerDirectory
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
if ($signingEnabled) {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "An Authenticode certificate is required. Pass -CertificateThumbprint or set PREDATORLITE_SIGNING_THUMBPRINT. Use -SkipSigning only for unsigned local testing."
    }
    $signTool = Resolve-SignTool
    $certificate = Get-CodeSigningCertificate `
        -Thumbprint $CertificateThumbprint `
        -StoreLocation $CertificateStore `
        -RequirePublicTrust:$productionBuild
}

$outputSuffix = if ($SkipSigning) { "-unsigned" } elseif ($TestSigning) { "-test-signed" } else { "" }
$setupFileName = "PredatorLite-Setup-$Version-win-x64$outputSuffix.exe"
$stagedSetupPath = Join-Path $compilerOutputDirectory $setupFileName
$stagedHashPath = "$stagedSetupPath.sha256"
$setupPath = Join-Path $artifactDirectory $setupFileName
$hashPath = "$setupPath.sha256"
$outputLock = $null
$promotionCandidateDirectory = $null
$promotionStarted = $false
$promotionAccepted = $false
$operationFailure = $null
$cleanupFailures = [System.Collections.Generic.List[string]]::new()

try {
    $outputLock = Enter-PredatorLiteOutputLock -RepositoryRoot $repositoryRoot

    Assert-SafeRepositoryOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Destination $publishDirectory `
        -AllowedRelativeRoots @("artifacts\installer\work") | Out-Null
    $artifactAllowedRoots = if ($productionBuild) { @("publish") } else { @("artifacts\installer") }
    Assert-SafeRepositoryOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Destination (Join-Path $artifactDirectory "output") `
        -AllowedRelativeRoots $artifactAllowedRoots | Out-Null

    try {
        if (-not $productionBuild) {
            Remove-DirectoryWithRetry -Path $artifactDirectory
        }
        Remove-DirectoryWithRetry -Path $installerWorkDirectory
        New-Item -ItemType Directory -Path $installerWorkDirectory -Force | Out-Null

        & $publishScript `
            -Configuration $Configuration `
            -OutputPath $publishOutputPath `
            -AllowArtifactsOutput `
            -OutputLock $outputLock
        if ($LASTEXITCODE -ne 0) {
            throw "PredatorLite publish failed with exit code $LASTEXITCODE"
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
        $publishPrefixLength = $publishDirectory.TrimEnd([char[]]@('\', '/')).Length + 1
        $actualOwnedBinaries = @(
            Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
                Where-Object {
                    $_.Extension -in @(".exe", ".dll") -and
                    $_.VersionInfo.CompanyName -eq "PredatorLite contributors"
                } |
                ForEach-Object { $_.FullName.Substring($publishPrefixLength) }
        )
        $ownedBinaryDrift = @(
            Compare-Object `
                -ReferenceObject @($ownedBinaries | Sort-Object) `
                -DifferenceObject @($actualOwnedBinaries | Sort-Object)
        )
        if ($ownedBinaryDrift.Count -ne 0) {
            throw "The first-party signing allowlist does not match the published first-party binaries: $($ownedBinaryDrift.InputObject -join ', ')"
        }

        if ($signingEnabled) {
            $storeSwitch = if ($CertificateStore -eq "LocalMachine") { @("/sm") } else { @() }
            foreach ($relativePath in $ownedBinaries) {
                $binaryPath = Join-Path $publishDirectory $relativePath
                $signArguments = @("sign") + $storeSwitch + @(
                    "/sha1", $certificate.Thumbprint,
                    "/fd", "SHA256",
                    "/td", "SHA256",
                    "/tr", $TimestampUrl,
                    "/d", "PredatorLite",
                    "/du", "https://github.com/XYZ1024-alt/PredatorLite",
                    $binaryPath
                )
                Invoke-NativeCommand `
                    -FilePath $signTool `
                    -Arguments $signArguments `
                    -RetryCount 2
                if ($TestSigning -and
                    $env:PREDATORLITE_TEST_CORRUPT_BEFORE_VERIFICATION -eq "1" -and
                    $relativePath -eq $ownedBinaries[0]) {
                    $corruptionStream = [System.IO.File]::Open(
                        $binaryPath,
                        [System.IO.FileMode]::Append,
                        [System.IO.FileAccess]::Write,
                        [System.IO.FileShare]::None)
                    try {
                        $corruptionStream.WriteByte(0)
                    }
                    finally {
                        $corruptionStream.Dispose()
                    }
                }
                Invoke-NativeCommand `
                    -FilePath $signTool `
                    -Arguments @(
                        "verify", "/pa", "/all", "/tw", "/sha1", $certificate.Thumbprint, $binaryPath)
            }
        }

        New-Item -ItemType Directory -Path $compilerOutputDirectory -Force | Out-Null
        $compilerArguments = @(
            "/DAppVersion=$Version",
            "/DOutputSuffix=$outputSuffix",
            "/DPayloadDirectory=$publishDirectory",
            "/O$compilerOutputDirectory"
        )
        if ($signingEnabled) {
            $storeOption = if ($CertificateStore -eq "LocalMachine") { " /sm" } else { "" }
            $innoSignCommand = "`$q$signTool`$q sign$storeOption /sha1 $($certificate.Thumbprint) /fd SHA256 /td SHA256 /tr $TimestampUrl /d `$qPredatorLite`$q /du https://github.com/XYZ1024-alt/PredatorLite `$f"
            $compilerArguments += "/DSignInstaller"
            $compilerArguments += "/SPredatorLiteSign=$innoSignCommand"
        }
        $compilerArguments += $installerScript
        Invoke-NativeCommand -FilePath $innoCompiler -Arguments $compilerArguments

        if (-not (Test-Path -LiteralPath $stagedSetupPath -PathType Leaf)) {
            throw "Inno Setup did not create the expected installer: $stagedSetupPath"
        }
        $maximumInstallerBytes = 25MB
        $installerBytes = (Get-Item -LiteralPath $stagedSetupPath).Length
        if ($installerBytes -gt $maximumInstallerBytes) {
            Write-Host "Largest installer payload files:"
            Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
                Sort-Object Length -Descending |
                Select-Object -First 15 |
                ForEach-Object {
                    $relativePath = [System.IO.Path]::GetRelativePath(
                        $publishDirectory,
                        $_.FullName)
                    Write-Host ("  {0,12:N0}  {1}" -f $_.Length, $relativePath)
                }
            throw "Installer exceeds the $maximumInstallerBytes byte budget: $installerBytes bytes"
        }
        if ($signingEnabled) {
            Invoke-NativeCommand `
                -FilePath $signTool `
                -Arguments @(
                    "verify", "/pa", "/all", "/tw", "/sha1", $certificate.Thumbprint, $stagedSetupPath)
        }

        $hash = Get-Sha256Hex -Path $stagedSetupPath
        $expectedHashContents = "$hash  $setupFileName`r`n"
        [System.IO.File]::WriteAllText($stagedHashPath, $expectedHashContents, [System.Text.Encoding]::ASCII)

        $stagedEntries = @(Get-ChildItem -LiteralPath $compilerOutputDirectory -Force)
        $expectedStagedNames = @($setupFileName, "$setupFileName.sha256")
        $unexpectedStagedEntries = @(
            $stagedEntries | Where-Object { $_.PSIsContainer -or $_.Name -notin $expectedStagedNames }
        )
        if ($stagedEntries.Count -ne 2 -or $unexpectedStagedEntries.Count -ne 0) {
            throw "Installer staging contains files other than the verified Setup executable and checksum."
        }

        if ($TestSigning -and $env:PREDATORLITE_TEST_FAIL_BEFORE_PROMOTION -eq "1") {
            throw "Injected installer failure before artifact promotion."
        }

        Remove-DirectoryWithRetry -Path $publishDirectory
        $artifactParent = Split-Path -Parent $artifactDirectory
        $artifactLeaf = Split-Path -Leaf $artifactDirectory
        New-Item -ItemType Directory -Path $artifactParent -Force | Out-Null
        $promotionCandidateDirectory = Join-Path $artifactParent ".$artifactLeaf-candidate-$invocationId"
        Assert-SafeRepositoryOutputPath `
            -RepositoryRoot $repositoryRoot `
            -Destination (Join-Path $promotionCandidateDirectory "output") `
            -AllowedRelativeRoots $artifactAllowedRoots | Out-Null
        Remove-DirectoryWithRetry -Path $promotionCandidateDirectory
        Move-Item -LiteralPath $compilerOutputDirectory -Destination $promotionCandidateDirectory

        $promotionStarted = $true
        Remove-DirectoryWithRetry -Path $artifactDirectory
        if ($TestSigning -and $env:PREDATORLITE_TEST_FAIL_DURING_PROMOTION -eq "1") {
            throw "Injected installer failure during artifact promotion."
        }
        Move-Item -LiteralPath $promotionCandidateDirectory -Destination $artifactDirectory

        if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
            throw "Installer promotion did not produce the expected final artifacts."
        }
        if ((Get-Sha256Hex -Path $setupPath) -ne $hash -or
            [System.IO.File]::ReadAllText($hashPath, [System.Text.Encoding]::ASCII) -ne $expectedHashContents) {
            throw "Promoted installer artifacts failed checksum verification."
        }
        if ($signingEnabled) {
            Invoke-NativeCommand `
                -FilePath $signTool `
                -Arguments @(
                    "verify", "/pa", "/all", "/tw", "/sha1", $certificate.Thumbprint, $setupPath)
        }
        $promotionAccepted = $true
    }
    catch {
        $operationFailure = $_
        if (($promotionStarted -and -not $promotionAccepted) -or -not $productionBuild) {
            try {
                Remove-DirectoryWithRetry -Path $artifactDirectory
            }
            catch {
                $cleanupFailures.Add("Failed to remove output '$artifactDirectory': $($_.Exception.Message)")
            }
        }
    }

    foreach ($cleanupPath in @($promotionCandidateDirectory, $installerWorkDirectory)) {
        if ([string]::IsNullOrWhiteSpace($cleanupPath)) {
            continue
        }
        try {
            Remove-DirectoryWithRetry -Path $cleanupPath
        }
        catch {
            $cleanupFailures.Add("Failed to remove work path '$cleanupPath': $($_.Exception.Message)")
        }
    }

    if ($cleanupFailures.Count -gt 0 -and $null -eq $operationFailure) {
        try {
            Remove-DirectoryWithRetry -Path $artifactDirectory
        }
        catch {
            $cleanupFailures.Add("Failed to remove output after cleanup failure: $($_.Exception.Message)")
        }
    }
}
finally {
    if ($null -ne $outputLock) {
        $outputLock.Dispose()
    }
    if ($null -ne $certificate) {
        $certificate.Dispose()
    }
}

if ($cleanupFailures.Count -gt 0) {
    $cleanupMessage = $cleanupFailures -join "; "
    if ($null -ne $operationFailure) {
        throw "$($operationFailure.Exception.Message) Cleanup also failed: $cleanupMessage"
    }
    throw "Installer cleanup failed: $cleanupMessage"
}
if ($null -ne $operationFailure) {
    throw $operationFailure
}

if ($SkipSigning) {
    Write-Warning "Created an unsigned installer for test use only. Do not attach it to a GitHub Release or present it as a production release."
}
elseif ($TestSigning) {
    Write-Warning "Created a test-signed installer outside publish. Do not publish it."
}
Write-Host "Installer: $setupPath"
Write-Host "SHA256:    $hashPath"
