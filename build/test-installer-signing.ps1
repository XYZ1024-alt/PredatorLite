[CmdletBinding()]
param(
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "release-output.ps1")

$buildScript = Join-Path $PSScriptRoot "build-installer.ps1"
$testId = [Guid]::NewGuid().ToString("N")
$installDirectory = Join-Path $repositoryRoot "artifacts\installer\signed-install-smoke-$testId"
$testOutputDirectory = Join-Path $repositoryRoot "artifacts\installer\test-signed"
$unsignedOutputDirectory = Join-Path $repositoryRoot "artifacts\installer\unsigned"
$releaseOutputDirectory = Join-Path $repositoryRoot "publish"
$releaseSentinelPath = Join-Path $releaseOutputDirectory ".installer-signing-test-$testId"
$workRoot = Join-Path $repositoryRoot "artifacts\installer\work"
$stagingRoot = Join-Path $repositoryRoot "artifacts\installer\staging"
$quarantineRoot = Join-Path $repositoryRoot "artifacts\installer\quarantine"
$uninstallRegistryPath = "Software\Microsoft\Windows\CurrentVersion\Uninstall\{C45ADA29-884C-471B-BBE4-7EC74A6E151C}_is1"
$runRegistryPath = "Software\Microsoft\Windows\CurrentVersion\Run"
$testLockPath = Join-Path $repositoryRoot "obj\predatorlite-installer-signing-test.lock"
$rootCertificateSubject = "CN=PredatorLite Installer Test Root $testId"
$leafCertificateSubject = "CN=PredatorLite Installer Test Leaf $testId"
$rootKeyName = "PredatorLite-Installer-Test-Root-$testId"
$leafKeyName = "PredatorLite-Installer-Test-Leaf-$testId"
$cspProviderName = "Microsoft Enhanced RSA and AES Cryptographic Provider"
$cspProviderType = 24

function Invoke-NativeCommand {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FilePath failed with exit code $LASTEXITCODE" }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try { return ($sha256.ComputeHash($stream) | ForEach-Object { $_.ToString("x2") }) -join "" }
    finally { $sha256.Dispose(); $stream.Dispose() }
}

function Get-DirectoryFingerprint {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return "<absent>" }
    $root = [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
    $prefixLength = $root.Length + 1
    return @(
        Get-ChildItem -LiteralPath $root -File -Force -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                "$($_.FullName.Substring($prefixLength))|$($_.Length)|$(Get-Sha256Hex -Path $_.FullName)"
            }
    ) -join "`n"
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
    throw "Windows SDK SignTool was not found."
}

function New-TestRsaProviderParameters {
    param(
        [Parameter(Mandatory)][string]$Name,
        [switch]$UseExistingKey)

    $parameters = [System.Security.Cryptography.CspParameters]::new(
        $cspProviderType, $cspProviderName, $Name)
    $parameters.KeyNumber = [int][System.Security.Cryptography.KeyNumber]::Signature
    $parameters.Flags = [System.Security.Cryptography.CspProviderFlags]::NoPrompt
    if ($UseExistingKey) {
        $parameters.Flags = $parameters.Flags -bor [System.Security.Cryptography.CspProviderFlags]::UseExistingKey
    }
    return $parameters
}

function New-TestRsaProvider {
    param([Parameter(Mandatory)][string]$Name)

    $parameters = New-TestRsaProviderParameters -Name $Name
    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(2048, $parameters)
    $rsa.PersistKeyInCsp = $true
    return $rsa
}

function Remove-TestRsaKey {
    param([Parameter(Mandatory)][string]$Name)

    try {
        $parameters = New-TestRsaProviderParameters -Name $Name -UseExistingKey
        $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new($parameters)
    }
    catch [System.Security.Cryptography.CryptographicException] {
        if ($_.Exception.HResult -eq -2146893802) {
            return
        }
        throw
    }
    try {
        $rsa.PersistKeyInCsp = $false
    }
    finally {
        $rsa.Dispose()
    }
}

function Test-TestRsaKeyExists {
    param([Parameter(Mandatory)][string]$Name)

    $rsa = $null
    try {
        $parameters = New-TestRsaProviderParameters -Name $Name -UseExistingKey
        $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new($parameters)
        return $true
    }
    catch [System.Security.Cryptography.CryptographicException] {
        if ($_.Exception.HResult -eq -2146893802) {
            return $false
        }
        throw
    }
    finally {
        if ($null -ne $rsa) {
            $rsa.Dispose()
        }
    }
}

function Add-CertificateToStore {
    param(
        [Parameter(Mandatory)][string]$StoreName,
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        $StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $store.Add($Certificate)
    }
    finally { $store.Close() }
}

function Remove-CertificateFromStore {
    param([Parameter(Mandatory)][string]$StoreName, [string]$Thumbprint)
    if ([string]::IsNullOrWhiteSpace($Thumbprint)) { return }
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        $StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        foreach ($match in @($store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint })) {
            $store.Remove($match)
        }
    }
    finally { $store.Close() }
}

function Test-CertificateInStore {
    param([Parameter(Mandatory)][string]$StoreName, [Parameter(Mandatory)][string]$Thumbprint)
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        $StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        return [bool]($store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1)
    }
    finally { $store.Close() }
}

function Remove-TestRegistryState {
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runRegistryPath, $true)
    try { if ($null -ne $runKey) { $runKey.DeleteValue("PredatorLite", $false) } }
    finally { if ($null -ne $runKey) { $runKey.Dispose() } }
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($uninstallRegistryPath, $false)
}

function Assert-ExpectedFailure {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][string]$MessagePattern,
        [Parameter(Mandatory)][string]$Description)
    $observed = $false
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike $MessagePattern) { throw }
        $observed = $true
    }
    if (-not $observed) { throw "$Description unexpectedly succeeded." }
}

function Assert-InstallerScratchEmpty {
    foreach ($path in @($workRoot, $stagingRoot, $quarantineRoot)) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $entry = Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1
        if ($entry) { throw "Installer scratch output remains at $($entry.FullName)." }
    }
    $candidate = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "artifacts\installer") -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like ".*-candidate-*" } |
        Select-Object -First 1
    if ($candidate) { throw "Installer promotion candidate remains at $($candidate.FullName)." }
}

function Enter-TestLock {
    $lockDirectory = Split-Path -Parent $testLockPath
    if (Test-Path -LiteralPath $lockDirectory) {
        $item = Get-Item -LiteralPath $lockDirectory -Force
        if (-not $item.PSIsContainer -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Installer test lock directory is not a normal repository directory: $lockDirectory"
        }
    }
    else { New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null }
    try {
        return [System.IO.File]::Open(
            $testLockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] { throw "Another PredatorLite installer-signing test is already running." }
}

$testLock = Enter-TestLock
try {
$uninstallKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallRegistryPath)
$runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runRegistryPath)
try {
    $hasStartupEntry = $null -ne $runKey -and $null -ne $runKey.GetValue("PredatorLite")
    $runningProcess = Get-Process PredatorLite, PredatorLite.FanGuard, PredatorLite.ElevatedHelper -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $uninstallKey -or $hasStartupEntry -or $null -ne $runningProcess) {
        throw "Installer signing tests require no existing PredatorLite installation, startup entry, or running process."
    }
}
finally {
    if ($null -ne $uninstallKey) { $uninstallKey.Dispose() }
    if ($null -ne $runKey) { $runKey.Dispose() }
}

foreach ($path in @($installDirectory, $testOutputDirectory, $unsignedOutputDirectory)) {
    if (Test-Path -LiteralPath $path) { throw "Installer signing tests refuse to replace pre-existing test output at $path." }
}
Assert-InstallerScratchEmpty
$releaseOutputExisted = Test-Path -LiteralPath $releaseOutputDirectory -PathType Container
$initialReleaseFingerprint = Get-DirectoryFingerprint -Path $releaseOutputDirectory

[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Build.props")
$version = [string]($buildProperties.Project.PropertyGroup.Version | Select-Object -First 1)
$setupPath = Join-Path $testOutputDirectory "PredatorLite-Setup-$version-win-x64-test-signed.exe"
$unsignedSetupPath = Join-Path $unsignedOutputDirectory "PredatorLite-Setup-$version-win-x64-unsigned.exe"
Write-Host "Resolving Windows SDK SignTool..."
$signTool = Resolve-SignTool
Write-Host "Using SignTool: $signTool"
$rootRsa = $null
$leafRsa = $null
$rootCertificate = $null
$rootPublicCertificate = $null
$leafCertificate = $null
$rootThumbprint = $null
$leafThumbprint = $null
$testFailure = $null
$testSucceeded = $false
$cleanupFailures = [System.Collections.Generic.List[string]]::new()

try {
    Write-Host "Creating temporary installer-signing test certificates..."
    $rootRsa = New-TestRsaProvider -Name $rootKeyName
    Write-Host "Created temporary root CSP key."
    $leafRsa = New-TestRsaProvider -Name $leafKeyName
    Write-Host "Created temporary leaf CSP key."

    $rootRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $rootCertificateSubject, $rootRsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
    $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor
                [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign, $true))
    $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($rootRequest.PublicKey, $false))
    $rootCertificate = $rootRequest.CreateSelfSigned((Get-Date).AddMinutes(-5), (Get-Date).AddDays(1))
    Write-Host "Created temporary root certificate."
    $rootPublicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $rootCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $rootThumbprint = $rootPublicCertificate.Thumbprint

    $leafRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $leafCertificateSubject, $leafRsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $leafRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $leafUsages = [System.Security.Cryptography.OidCollection]::new()
    $null = $leafUsages.Add([System.Security.Cryptography.Oid]::new("1.3.6.1.5.5.7.3.3"))
    $leafRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($leafUsages, $true))
    $leafRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
    $leafRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($leafRequest.PublicKey, $false))
    $serialNumber = [byte[]]::new(16)
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($serialNumber) }
    finally { $random.Dispose() }
    $serialNumber[0] = $serialNumber[0] -band 0x7f
    $leafPublicCertificate = $leafRequest.Create(
        $rootCertificate, (Get-Date).AddMinutes(-5), (Get-Date).AddHours(12), $serialNumber)
    try {
        $leafCertificate = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey(
            $leafPublicCertificate, $leafRsa)
    }
    finally { $leafPublicCertificate.Dispose() }
    $leafThumbprint = $leafCertificate.Thumbprint
    Write-Host "Created temporary leaf code-signing certificate."
    Add-CertificateToStore -StoreName "My" -Certificate $leafCertificate
    Write-Host "Stored temporary leaf certificate in CurrentUser\My."

    Write-Host "Checking missing-certificate production rejection..."
    Assert-ExpectedFailure `
        -Action { & $buildScript -CertificateThumbprint "0000000000000000000000000000000000000000" -TimestampUrl $TimestampUrl } `
        -MessagePattern "*was not found in CurrentUser\My*" -Description "Missing-certificate production signing"
    if ((Get-DirectoryFingerprint -Path $releaseOutputDirectory) -ne $initialReleaseFingerprint) {
        throw "Missing-certificate rejection modified production output."
    }

    Write-Host "Checking signed Debug production rejection..."
    Assert-ExpectedFailure `
        -Action { & $buildScript -Configuration Debug -CertificateThumbprint $leafThumbprint -TimestampUrl $TimestampUrl } `
        -MessagePattern "*Signed installers must use the Release configuration*" -Description "Signed Debug production build"
    if ((Get-DirectoryFingerprint -Path $releaseOutputDirectory) -ne $initialReleaseFingerprint) {
        throw "Signed Debug rejection modified production output."
    }

    Write-Host "Checking untrusted-certificate production rejection..."
    Assert-ExpectedFailure `
        -Action { & $buildScript -CertificateThumbprint $leafThumbprint -TimestampUrl $TimestampUrl } `
        -MessagePattern "*is not trusted on this build machine*" -Description "Untrusted certificate production signing"
    if ((Get-DirectoryFingerprint -Path $releaseOutputDirectory) -ne $initialReleaseFingerprint) {
        throw "Untrusted-certificate rejection modified production output."
    }

    Add-CertificateToStore -StoreName "Root" -Certificate $rootPublicCertificate
    Write-Host "Checking private-root production rejection..."
    Assert-ExpectedFailure `
        -Action { & $buildScript -CertificateThumbprint $leafThumbprint -TimestampUrl $TimestampUrl } `
        -MessagePattern "*does not chain to a Windows public AuthRoot certificate*" -Description "Private-root production signing"
    if ((Get-DirectoryFingerprint -Path $releaseOutputDirectory) -ne $initialReleaseFingerprint) {
        throw "Private-root rejection modified production output."
    }

    Write-Host "Production certificate rejection gates passed."
    $sentinelContents = "PredatorLite unsigned isolation $testId"
    New-Item -ItemType Directory -Path $releaseOutputDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText($releaseSentinelPath, $sentinelContents, [System.Text.Encoding]::ASCII)
    & $buildScript -SkipSigning
    if ([System.IO.File]::ReadAllText($releaseSentinelPath, [System.Text.Encoding]::ASCII) -ne $sentinelContents) {
        throw "Unsigned installer build modified existing publish output."
    }
    if (-not (Test-Path -LiteralPath $unsignedSetupPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath "$unsignedSetupPath.sha256" -PathType Leaf)) {
        throw "Unsigned installer build did not stay under artifacts."
    }
    Assert-ExpectedFailure `
        -Action { Invoke-NativeCommand $signTool @("verify", "/pa", "/all", "/tw", $unsignedSetupPath) } `
        -MessagePattern "*failed with exit code*" -Description "Unsigned Setup signature verification"
    Remove-DirectoryWithRetry -Path $unsignedOutputDirectory
    Remove-Item -LiteralPath $releaseSentinelPath -Force
    if (-not $releaseOutputExisted -and @(Get-ChildItem -LiteralPath $releaseOutputDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $releaseOutputDirectory -Force
    }
    if ((Get-DirectoryFingerprint -Path $releaseOutputDirectory) -ne $initialReleaseFingerprint) {
        throw "Unsigned installer build did not restore existing publish output."
    }

    Assert-ExpectedFailure `
        -Action { & $buildScript -TestSigning -CertificateThumbprint $leafThumbprint -TimestampUrl "http://127.0.0.1:1" } `
        -MessagePattern "*failed with exit code*" -Description "Timestamp failure"
    if (Test-Path -LiteralPath $testOutputDirectory) { throw "Timestamp failure left test-signed output." }
    Assert-InstallerScratchEmpty

    $env:PREDATORLITE_TEST_CORRUPT_BEFORE_VERIFICATION = "1"
    try {
        Assert-ExpectedFailure `
            -Action { & $buildScript -TestSigning -CertificateThumbprint $leafThumbprint -TimestampUrl $TimestampUrl } `
            -MessagePattern "*failed with exit code*" -Description "Signature-verification failure"
    }
    finally { Remove-Item Env:PREDATORLITE_TEST_CORRUPT_BEFORE_VERIFICATION -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $testOutputDirectory) { throw "Signature-verification failure left test-signed output." }
    Assert-InstallerScratchEmpty

    $env:PREDATORLITE_TEST_FAIL_DURING_PROMOTION = "1"
    try {
        Assert-ExpectedFailure `
            -Action { & $buildScript -TestSigning -CertificateThumbprint $leafThumbprint -TimestampUrl $TimestampUrl } `
            -MessagePattern "*Injected installer failure during artifact promotion*" -Description "Promotion failure injection"
    }
    finally { Remove-Item Env:PREDATORLITE_TEST_FAIL_DURING_PROMOTION -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $testOutputDirectory) { throw "Promotion failure left test-signed output." }
    Assert-InstallerScratchEmpty

    & $buildScript -TestSigning -CertificateThumbprint $leafThumbprint -TimestampUrl $TimestampUrl
    Invoke-NativeCommand $signTool @("verify", "/pa", "/all", "/tw", "/sha1", $leafThumbprint, $setupPath)
    Assert-ExpectedFailure `
        -Action { Invoke-NativeCommand $signTool @("verify", "/pa", "/all", "/tw", "/sha1", $rootThumbprint, $setupPath) } `
        -MessagePattern "*failed with exit code*" -Description "Mismatched signer verification"

    Remove-DirectoryWithRetry -Path $installDirectory
    $installer = Start-Process -FilePath $setupPath -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOICONS", "/DIR=`"$installDirectory`"") -PassThru -Wait
    if ($installer.ExitCode -ne 0) { throw "Test-signed installer exited with code $($installer.ExitCode)" }

    $uninstallKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallRegistryPath)
    try { if ($null -eq $uninstallKey) { throw "Test-signed install did not create uninstall registration." } }
    finally { if ($null -ne $uninstallKey) { $uninstallKey.Dispose() } }

    $signedFiles = @(
        "PredatorLite.exe", "PredatorLite.dll", "PredatorLite.Core.dll",
        "PredatorLite.Platform.Windows.dll", "PredatorLite.FanGuard.exe", "PredatorLite.FanGuard.dll",
        "PredatorLite.ElevatedHelper.exe", "PredatorLite.ElevatedHelper.dll", "unins000.exe")
    foreach ($relativePath in $signedFiles) {
        Invoke-NativeCommand $signTool @(
            "verify", "/pa", "/all", "/tw", "/sha1", $leafThumbprint, (Join-Path $installDirectory $relativePath))
    }

    $runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runRegistryPath, $true)
    try {
        $runKey.SetValue("PredatorLite", "`"$(Join-Path $installDirectory 'PredatorLite.exe')`" --background",
            [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally { $runKey.Dispose() }

    $uninstaller = Start-Process -FilePath (Join-Path $installDirectory "unins000.exe") `
        -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -PassThru -Wait
    if ($uninstaller.ExitCode -ne 0) { throw "Test-signed uninstaller exited with code $($uninstaller.ExitCode)" }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Test-Path -LiteralPath $installDirectory) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (Test-Path -LiteralPath $installDirectory) { throw "Test-signed uninstall left the install directory behind." }

    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runRegistryPath)
    $uninstallKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallRegistryPath)
    try {
        if ($null -ne $runKey -and $null -ne $runKey.GetValue("PredatorLite")) {
            throw "Test-signed uninstall left the PredatorLite startup value behind."
        }
        if ($null -ne $uninstallKey) { throw "Test-signed uninstall left uninstall registration behind." }
    }
    finally {
        if ($null -ne $runKey) { $runKey.Dispose() }
        if ($null -ne $uninstallKey) { $uninstallKey.Dispose() }
    }
    if ((Get-DirectoryFingerprint -Path $releaseOutputDirectory) -ne $initialReleaseFingerprint) {
        throw "Test signing modified existing publish output."
    }
    $testSucceeded = $true
}
catch { $testFailure = $_ }
finally {
    foreach ($certificateStore in @(
        @{ StoreName = "Root"; Thumbprint = $rootThumbprint },
        @{ StoreName = "My"; Thumbprint = $leafThumbprint })) {
        try {
            Remove-CertificateFromStore -StoreName $certificateStore.StoreName -Thumbprint $certificateStore.Thumbprint
        }
        catch { $cleanupFailures.Add("Certificate cleanup failed: $($_.Exception.Message)") }
    }

    try {
        if (Test-Path -LiteralPath (Join-Path $installDirectory "unins000.exe")) {
            $cleanupUninstaller = Start-Process -FilePath (Join-Path $installDirectory "unins000.exe") `
                -ArgumentList @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -PassThru -Wait -ErrorAction SilentlyContinue
            if ($null -ne $cleanupUninstaller -and $cleanupUninstaller.ExitCode -ne 0) {
                $cleanupFailures.Add("Fallback uninstaller exited with code $($cleanupUninstaller.ExitCode).")
            }
        }
    }
    catch { $cleanupFailures.Add("Fallback uninstall failed: $($_.Exception.Message)") }

    try { Remove-TestRegistryState }
    catch { $cleanupFailures.Add("Registry cleanup failed: $($_.Exception.Message)") }

    foreach ($path in @($installDirectory, $testOutputDirectory, $unsignedOutputDirectory)) {
        try { Remove-DirectoryWithRetry -Path $path }
        catch { $cleanupFailures.Add("Directory cleanup failed for '$path': $($_.Exception.Message)") }
    }
    try {
        Remove-Item -LiteralPath $releaseSentinelPath -Force -ErrorAction SilentlyContinue
        if (-not $releaseOutputExisted -and
            (Test-Path -LiteralPath $releaseOutputDirectory -PathType Container) -and
            @(Get-ChildItem -LiteralPath $releaseOutputDirectory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $releaseOutputDirectory -Force
        }
    }
    catch { $cleanupFailures.Add("Publish sentinel cleanup failed: $($_.Exception.Message)") }

    foreach ($disposable in @($leafCertificate, $rootPublicCertificate, $rootCertificate, $leafRsa, $rootRsa)) {
        if ($null -eq $disposable) { continue }
        try { $disposable.Dispose() }
        catch { $cleanupFailures.Add("Cryptographic object cleanup failed: $($_.Exception.Message)") }
    }
    foreach ($keyName in @($leafKeyName, $rootKeyName)) {
        try { Remove-TestRsaKey -Name $keyName }
        catch { $cleanupFailures.Add("CSP key cleanup failed for '$keyName': $($_.Exception.Message)") }
    }
}

if ($null -ne $rootThumbprint -and (Test-CertificateInStore -StoreName "Root" -Thumbprint $rootThumbprint)) {
    $cleanupFailures.Add("The test root certificate remains trusted in CurrentUser\Root.")
}
if ($null -ne $leafThumbprint -and (Test-CertificateInStore -StoreName "My" -Thumbprint $leafThumbprint)) {
    $cleanupFailures.Add("The test leaf certificate remains in CurrentUser\My.")
}
foreach ($keyName in @($rootKeyName, $leafKeyName)) {
    if (Test-TestRsaKeyExists -Name $keyName) {
        $cleanupFailures.Add("The persisted test key remains in the user key store: $keyName")
    }
}
try { Assert-InstallerScratchEmpty }
catch { $cleanupFailures.Add($_.Exception.Message) }
try {
    if ((Get-DirectoryFingerprint -Path $releaseOutputDirectory) -ne $initialReleaseFingerprint) {
        throw "Publish output changed during installer-signing tests."
    }
}
catch { $cleanupFailures.Add($_.Exception.Message) }

if ($cleanupFailures.Count -gt 0) {
    $cleanupMessage = $cleanupFailures -join "; "
    if ($null -ne $testFailure) { throw "$($testFailure.Exception.Message) Cleanup also failed: $cleanupMessage" }
    throw "Installer signing test cleanup failed: $cleanupMessage"
}
if ($null -ne $testFailure) { throw $testFailure }
if (-not $testSucceeded) { throw "Installer test-signing pipeline did not complete." }
Write-Host "Installer test-signing pipeline passed."
}
finally {
    $testLock.Dispose()
}
