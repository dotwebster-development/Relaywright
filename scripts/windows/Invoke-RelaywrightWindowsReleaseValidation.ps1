[CmdletBinding()]
param(
    [ValidateSet("clean-installer", "update-package", "full-release", "cleanup-only")]
    [string]$Mode = "clean-installer",
    [string]$Version = "1.0.0-rc.5",
    [string]$FromVersion = "",
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$GitHubToken = $env:GITHUB_TOKEN,
    [string]$ArtifactsDirectory = (Join-Path $PWD "artifacts\windows-release-validation"),
    [string]$InstallerServiceName = "Relaywright",
    [string]$InstallerInstallRoot = "$env:ProgramFiles\Relaywright",
    [string]$InstallerDataDirectory = "$env:ProgramData\Relaywright",
    [string]$InstallerFirewallGroup = "Relaywright",
    [string]$UpdateServiceName = "RelaywrightReleaseValidation",
    [string]$UpdateInstallRoot = "C:\Relaywright\ReleaseValidation",
    [string]$UpdateDataDirectory = "C:\Relaywright\ReleaseValidation\App_Data",
    [string]$UpdateFirewallGroup = "Relaywright Release Validation"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$InstallerHttpsPort = 5443
$InstallerHttpPort = 5080
$InstallerSmtpPort = 25
$UpdateHttpsPort = 5543
$UpdateHttpPort = 5580
$UpdateSmtpPort = 2526
$InstallScriptPath = Join-Path $PSScriptRoot "Install-Relaywright.ps1"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message"
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this validation from an elevated PowerShell session or administrator GitHub Actions runner service."
    }
}

function Initialize-ArtifactsDirectory {
    New-Item -ItemType Directory -Path $ArtifactsDirectory -Force | Out-Null
}

function Write-ArtifactText {
    param(
        [string]$Name,
        [string]$Content
    )

    Initialize-ArtifactsDirectory
    $path = Join-Path $ArtifactsDirectory $Name
    Set-Content -LiteralPath $path -Value $Content -Encoding UTF8
}

function Write-ArtifactJson {
    param(
        [string]$Name,
        [object]$Value
    )

    Initialize-ArtifactsDirectory
    $json = $Value | ConvertTo-Json -Depth 8
    Write-ArtifactText -Name $Name -Content $json
}

function Normalize-Version {
    param([string]$Value)

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        return $trimmed.Substring(1)
    }

    return $trimmed
}

function Get-ReleaseTag {
    param([string]$Value)
    return "v$(Normalize-Version -Value $Value)"
}

function Get-GitHubHeaders {
    param([string]$Accept = "application/vnd.github+json")

    $headers = @{
        "Accept" = $Accept
        "User-Agent" = "RelaywrightWindowsReleaseValidation"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
        $headers["Authorization"] = "Bearer $GitHubToken"
    }

    return $headers
}

function Get-GitHubRelease {
    param([string]$ReleaseVersion)

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw "Repository is required. Pass -Repository owner/name or set GITHUB_REPOSITORY."
    }

    $tag = Get-ReleaseTag -Value $ReleaseVersion
    $uri = "https://api.github.com/repos/$Repository/releases/tags/$tag"
    Write-Step "Reading GitHub release $tag from $Repository"
    return Invoke-RestMethod -Uri $uri -Headers (Get-GitHubHeaders)
}

function Save-ReleaseAsset {
    param(
        [string]$ReleaseVersion,
        [string]$AssetName,
        [string]$DestinationPath
    )

    $release = Get-GitHubRelease -ReleaseVersion $ReleaseVersion
    $asset = @($release.assets | Where-Object { $_.name -eq $AssetName }) | Select-Object -First 1
    if (-not $asset) {
        $available = @($release.assets | ForEach-Object { $_.name }) -join ", "
        throw "Release asset '$AssetName' was not found on $(Get-ReleaseTag -Value $ReleaseVersion). Available assets: $available"
    }

    $destinationDirectory = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Write-Step "Downloading release asset $AssetName"
    Invoke-WebRequest `
        -Uri $asset.url `
        -Headers (Get-GitHubHeaders -Accept "application/octet-stream") `
        -OutFile $DestinationPath `
        -UseBasicParsing

    return $DestinationPath
}

function Assert-Checksum {
    param(
        [string]$ChecksumsPath,
        [string]$FilePath
    )

    $fileName = Split-Path -Leaf $FilePath
    $pattern = "^(?<hash>[a-fA-F0-9]{64})\s+\*?$([regex]::Escape($fileName))$"
    $match = $null
    foreach ($line in Get-Content -LiteralPath $ChecksumsPath) {
        if ($line -match $pattern) {
            $match = $Matches["hash"].ToUpperInvariant()
            break
        }
    }

    if (-not $match) {
        throw "Checksum entry for '$fileName' was not found in '$ChecksumsPath'."
    }

    $actual = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $match) {
        throw "Checksum mismatch for '$fileName'. Expected $match; got $actual."
    }

    Write-Step "Checksum verified for $fileName"
}

function Save-ReleaseAssetSet {
    param(
        [string]$ReleaseVersion,
        [string[]]$AssetNames
    )

    $versionName = Normalize-Version -Value $ReleaseVersion
    $downloadDirectory = Join-Path $ArtifactsDirectory "downloads\$versionName"
    New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

    $checksumsPath = Join-Path $downloadDirectory "SHA256SUMS.txt"
    Save-ReleaseAsset -ReleaseVersion $ReleaseVersion -AssetName "SHA256SUMS.txt" -DestinationPath $checksumsPath | Out-Null

    $paths = @{}
    foreach ($assetName in $AssetNames) {
        $assetPath = Join-Path $downloadDirectory $assetName
        Save-ReleaseAsset -ReleaseVersion $ReleaseVersion -AssetName $assetName -DestinationPath $assetPath | Out-Null
        Assert-Checksum -ChecksumsPath $checksumsPath -FilePath $assetPath
        $paths[$assetName] = $assetPath
    }

    return $paths
}

function Wait-ServiceStatus {
    param(
        [string]$Name,
        [string]$Status,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($service -and $service.Status.ToString() -eq $Status) {
            return
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    throw "Service '$Name' did not reach status '$Status' within $TimeoutSeconds seconds."
}

function Stop-ValidationService {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Stopped") {
        Write-Step "Stopping service $Name"
        Stop-Service -Name $Name -Force
        Wait-ServiceStatus -Name $Name -Status "Stopped"
    }
}

function Remove-ValidationService {
    param([string]$Name)

    Stop-ValidationService -Name $Name
    if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
        Write-Step "Deleting service $Name"
        & sc.exe delete $Name | Out-Host
        Start-Sleep -Seconds 2
    }
}

function Remove-FirewallGroup {
    param([string]$GroupName)

    if (-not (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
        return
    }

    $rules = @(Get-NetFirewallRule -Group $GroupName -ErrorAction SilentlyContinue)
    if ($rules.Count -gt 0) {
        Write-Step "Removing Windows Firewall rules in group $GroupName"
        $rules | Remove-NetFirewallRule
    }
}

function Get-FullPath {
    param([string]$Path)
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Remove-KnownDirectory {
    param(
        [string]$Path,
        [string[]]$AllowedPaths
    )

    $candidate = Get-FullPath -Path $Path
    $allowed = @($AllowedPaths | ForEach-Object { Get-FullPath -Path $_ })
    if ($allowed -notcontains $candidate) {
        throw "Refusing to remove unexpected directory '$Path'. Allowed paths: $($allowed -join ', ')"
    }

    if (Test-Path -LiteralPath $candidate) {
        Write-Step "Removing directory $candidate"
        Remove-Item -LiteralPath $candidate -Recurse -Force
    }
}

function Invoke-InnoUninstallerIfPresent {
    $uninstallerPath = Join-Path $InstallerInstallRoot "unins000.exe"
    if (-not (Test-Path -LiteralPath $uninstallerPath)) {
        return
    }

    $logPath = Join-Path $ArtifactsDirectory "inno-uninstall.log"
    Write-Step "Running Inno uninstaller"
    $arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$logPath`""
    $process = Start-Process -FilePath $uninstallerPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        Write-Warning "Inno uninstaller exited with code $($process.ExitCode). Continuing with hard cleanup."
    }
}

function Invoke-ReleaseValidationCleanup {
    Write-Step "Cleaning Relaywright validation state"
    Invoke-InnoUninstallerIfPresent

    Remove-ValidationService -Name $InstallerServiceName
    Remove-ValidationService -Name $UpdateServiceName
    Remove-FirewallGroup -GroupName $InstallerFirewallGroup
    Remove-FirewallGroup -GroupName $UpdateFirewallGroup

    $allowedPaths = @(
        $InstallerInstallRoot,
        $InstallerDataDirectory,
        $UpdateInstallRoot,
        $UpdateDataDirectory
    )

    Remove-KnownDirectory -Path $InstallerInstallRoot -AllowedPaths $allowedPaths
    Remove-KnownDirectory -Path $InstallerDataDirectory -AllowedPaths $allowedPaths
    Remove-KnownDirectory -Path $UpdateDataDirectory -AllowedPaths $allowedPaths
    Remove-KnownDirectory -Path $UpdateInstallRoot -AllowedPaths $allowedPaths

    Assert-CleanupComplete
}

function Assert-CleanupComplete {
    foreach ($serviceName in @($InstallerServiceName, $UpdateServiceName)) {
        if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
            throw "Cleanup failed because service '$serviceName' still exists."
        }
    }

    foreach ($path in @($InstallerInstallRoot, $InstallerDataDirectory, $UpdateInstallRoot, $UpdateDataDirectory)) {
        if (Test-Path -LiteralPath $path) {
            throw "Cleanup failed because '$path' still exists."
        }
    }

    if (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue) {
        foreach ($groupName in @($InstallerFirewallGroup, $UpdateFirewallGroup)) {
            if (Get-NetFirewallRule -Group $groupName -ErrorAction SilentlyContinue) {
                throw "Cleanup failed because firewall group '$groupName' still has rules."
            }
        }
    }
}

function Invoke-InsecureWebRequest {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 15
    )

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        $output = & $curl.Source --ssl-no-revoke --insecure --silent --show-error --fail --max-time $TimeoutSeconds $Url 2>&1
        if ($LASTEXITCODE -eq 0) {
            return [pscustomobject]@{
                StatusCode = 200
                Content = ($output -join [Environment]::NewLine)
            }
        }

        throw "curl.exe failed with exit code ${LASTEXITCODE}: $($output -join ' ')"
    }

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return Invoke-WebRequest -Uri $Url -UseBasicParsing -SkipCertificateCheck -TimeoutSec $TimeoutSeconds
    }

    $previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    try {
        return Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSeconds
    }
    finally {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback
    }
}

function Assert-Health {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 120
    )

    Write-Step "Waiting for health check $Url"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $response = Invoke-InsecureWebRequest -Url $Url -TimeoutSeconds 10
            $content = [string]$response.Content
            if ($content -match '"status"\s*:\s*"ok"') {
                Write-ArtifactText -Name "health.txt" -Content $content
                return
            }

            $lastError = "Unexpected health response: $content"
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    throw "Health check did not pass within $TimeoutSeconds seconds. Last error: $lastError"
}

function Assert-TcpPortClosed {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds = 1000
    )

    $client = New-Object Net.Sockets.TcpClient
    try {
        $asyncResult = $client.BeginConnect($HostName, $Port, $null, $null)
        if ($asyncResult.AsyncWaitHandle.WaitOne($TimeoutMilliseconds, $false)) {
            $client.EndConnect($asyncResult)
            throw "TCP port $HostName`:$Port is open, but it should be closed."
        }
    }
    catch [Net.Sockets.SocketException] {
        return
    }
    finally {
        $client.Close()
    }
}

function Assert-SetupPage {
    param([string]$Url)

    Write-Step "Checking first-run setup page $Url"
    $response = Invoke-InsecureWebRequest -Url $Url -TimeoutSeconds 15
    $content = [string]$response.Content
    Write-ArtifactText -Name "setup-page.html" -Content $content

    if ($response.StatusCode -ne 200 -or $content -notmatch "Set Up Relaywright") {
        throw "First-run setup page was not reachable or did not render the expected setup content."
    }
}

function Assert-PathExists {
    param(
        [string]$Path,
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Name was not found at '$Path'."
    }
}

function Assert-DataLayout {
    param([string]$DataDirectory)

    Assert-PathExists -Path $DataDirectory -Name "Data directory"
    Assert-PathExists -Path (Join-Path $DataDirectory "relay.db") -Name "SQLite database"
    Assert-PathExists -Path (Join-Path $DataDirectory "spool") -Name "Spool directory"
    Assert-PathExists -Path (Join-Path $DataDirectory "keys") -Name "Data Protection key directory"
    Assert-PathExists -Path (Join-Path $DataDirectory "backups") -Name "Backup directory"
    Assert-PathExists -Path (Join-Path $DataDirectory "certs") -Name "Certificate data directory"
}

function Assert-FirewallRules {
    param(
        [string]$GroupName,
        [int[]]$ExpectedPorts,
        [int[]]$ForbiddenPorts = @()
    )

    if (-not (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
        throw "Windows Firewall cmdlets are unavailable. Cannot validate firewall defaults."
    }

    $rules = @(Get-NetFirewallRule -Group $GroupName -ErrorAction SilentlyContinue)
    if ($rules.Count -eq 0) {
        throw "No Windows Firewall rules were found in group '$GroupName'."
    }

    $ports = New-Object System.Collections.Generic.List[int]
    foreach ($rule in $rules) {
        $portFilter = $rule | Get-NetFirewallPortFilter
        foreach ($localPort in @($portFilter.LocalPort)) {
            if ($localPort -match "^\d+$") {
                $ports.Add([int]$localPort)
            }
        }

        $addressFilter = $rule | Get-NetFirewallAddressFilter
        foreach ($remoteAddress in @($addressFilter.RemoteAddress)) {
            if ($remoteAddress -eq "Any") {
                throw "Firewall rule '$($rule.DisplayName)' exposes Relaywright to remote address Any."
            }
        }
    }

    $uniquePorts = @($ports | Sort-Object -Unique)
    foreach ($expectedPort in $ExpectedPorts) {
        if ($uniquePorts -notcontains $expectedPort) {
            throw "Firewall group '$GroupName' does not contain expected TCP port $expectedPort. Ports: $($uniquePorts -join ', ')"
        }
    }

    foreach ($forbiddenPort in $ForbiddenPorts) {
        if ($uniquePorts -contains $forbiddenPort) {
            throw "Firewall group '$GroupName' unexpectedly contains TCP port $forbiddenPort."
        }
    }
}

function ConvertTo-PowerShellSingleQuotedString {
    param([string]$Value)
    return "'$($Value.Replace("'", "''"))'"
}

function Invoke-InstallerScriptReplay {
    $scriptPath = Join-Path $InstallerInstallRoot "tools\Install-Relaywright.ps1"
    $packagePath = Join-Path $InstallerInstallRoot "package"
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Write-ArtifactText -Name "installer-script-replay.txt" -Content "Install script was not found at '$scriptPath'."
        return
    }

    if (-not (Test-Path -LiteralPath $packagePath)) {
        Write-ArtifactText -Name "installer-script-replay.txt" -Content "Package directory was not found at '$packagePath'."
        return
    }

    $stdoutPath = Join-Path $ArtifactsDirectory "installer-script-replay.stdout.log"
    $stderrPath = Join-Path $ArtifactsDirectory "installer-script-replay.stderr.log"
    $exitCodePath = Join-Path $ArtifactsDirectory "installer-script-replay-exit-code.txt"

    $command = @(
        "&",
        (ConvertTo-PowerShellSingleQuotedString -Value $scriptPath),
        "-PackagePath",
        (ConvertTo-PowerShellSingleQuotedString -Value $packagePath),
        "-InstallRoot",
        (ConvertTo-PowerShellSingleQuotedString -Value $InstallerInstallRoot),
        "-DataDirectory",
        (ConvertTo-PowerShellSingleQuotedString -Value $InstallerDataDirectory),
        "-ServiceName",
        (ConvertTo-PowerShellSingleQuotedString -Value $InstallerServiceName),
        "-DisplayName",
        (ConvertTo-PowerShellSingleQuotedString -Value $InstallerServiceName),
        "-HttpsPort",
        "$InstallerHttpsPort",
        "-HttpPort",
        "$InstallerHttpPort",
        "-SmtpPort",
        "$InstallerSmtpPort",
        "-FirewallRulePrefix",
        (ConvertTo-PowerShellSingleQuotedString -Value $InstallerFirewallGroup),
        "-FirewallRemoteAddress",
        "'LocalSubnet'",
        "-BootstrapUserName",
        "'admin'",
        "-BootstrapEmail",
        "'admin@localhost'",
        "-GenerateSelfSignedCertificate:`$true",
        "-NonInteractive",
        "-ConfigureFirewall"
    ) -join " "

    Write-Step "Replaying embedded install script for diagnostics"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command $command > $stdoutPath 2> $stderrPath
    Set-Content -LiteralPath $exitCodePath -Value ([string]$LASTEXITCODE) -Encoding ASCII
}

function Get-FirewallSnapshot {
    param([string]$GroupName)

    if (-not (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
        return @()
    }

    $snapshot = @()
    foreach ($rule in @(Get-NetFirewallRule -Group $GroupName -ErrorAction SilentlyContinue)) {
        $portFilter = $rule | Get-NetFirewallPortFilter
        $addressFilter = $rule | Get-NetFirewallAddressFilter
        $snapshot += [pscustomobject]@{
            DisplayName = $rule.DisplayName
            Enabled = $rule.Enabled
            Direction = $rule.Direction
            Action = $rule.Action
            Profile = $rule.Profile
            Protocol = $portFilter.Protocol
            LocalPort = @($portFilter.LocalPort) -join ","
            RemoteAddress = @($addressFilter.RemoteAddress) -join ","
        }
    }

    return $snapshot
}

function Save-Diagnostics {
    param([string]$Suffix)

    $serviceSnapshot = @()
    foreach ($serviceName in @($InstallerServiceName, $UpdateServiceName)) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service) {
            $serviceSnapshot += [pscustomobject]@{
                Name = $service.Name
                DisplayName = $service.DisplayName
                Status = $service.Status.ToString()
                StartType = $service.StartType.ToString()
            }
        }
    }

    Write-ArtifactJson -Name "services-$Suffix.json" -Value $serviceSnapshot
    Write-ArtifactJson -Name "firewall-$Suffix.json" -Value @(
        [pscustomobject]@{
            Group = $InstallerFirewallGroup
            Rules = @(Get-FirewallSnapshot -GroupName $InstallerFirewallGroup)
        },
        [pscustomobject]@{
            Group = $UpdateFirewallGroup
            Rules = @(Get-FirewallSnapshot -GroupName $UpdateFirewallGroup)
        }
    )
}

function Invoke-CleanInstallerValidation {
    $versionName = Normalize-Version -Value $Version
    $installerAsset = "Relaywright-$versionName-windows-x64-installer.exe"
    $assets = Save-ReleaseAssetSet -ReleaseVersion $Version -AssetNames @($installerAsset)
    $installerPath = $assets[$installerAsset]

    Invoke-ReleaseValidationCleanup

    $installerLogPath = Join-Path $ArtifactsDirectory "inno-install.log"
    Write-Step "Running Windows installer $installerAsset"
    $arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$installerLogPath`""
    $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Relaywright installer exited with code $($process.ExitCode)."
    }

    try {
        Wait-ServiceStatus -Name $InstallerServiceName -Status "Running" -TimeoutSeconds 60
    }
    catch {
        Invoke-InstallerScriptReplay
        throw
    }

    Assert-Health -Url "https://127.0.0.1:$InstallerHttpsPort/health"
    Assert-TcpPortClosed -HostName "127.0.0.1" -Port $InstallerHttpPort
    Assert-SetupPage -Url "https://127.0.0.1:$InstallerHttpsPort/Account/Setup"
    Assert-DataLayout -DataDirectory $InstallerDataDirectory
    Assert-FirewallRules `
        -GroupName $InstallerFirewallGroup `
        -ExpectedPorts @($InstallerHttpsPort, $InstallerSmtpPort) `
        -ForbiddenPorts @($InstallerHttpPort)
}

function New-ValidationPassword {
    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 12)
    return "ReleaseTest42$suffix"
}

function Invoke-PackageInstall {
    param(
        [string]$PackagePath,
        [switch]$Update
    )

    if (-not (Test-Path -LiteralPath $InstallScriptPath)) {
        throw "Install script was not found at '$InstallScriptPath'."
    }

    $arguments = @{
        PackagePath = $PackagePath
        InstallRoot = $UpdateInstallRoot
        DataDirectory = $UpdateDataDirectory
        ServiceName = $UpdateServiceName
        DisplayName = "Relaywright Release Validation"
        EnvironmentName = "Production"
        HttpsPort = $UpdateHttpsPort
        HttpPort = $UpdateHttpPort
        SmtpPort = $UpdateSmtpPort
        FirewallRulePrefix = $UpdateFirewallGroup
        FirewallRemoteAddress = "LocalSubnet"
        FirewallProfiles = "Any"
        BootstrapUserName = "release-validator"
        BootstrapEmail = "release-validator@localhost"
        BootstrapPassword = (New-ValidationPassword)
        HealthTimeoutSeconds = 120
    }

    if ($Update) {
        & $InstallScriptPath @arguments -GenerateSelfSignedCertificate:$true -ConfigureFirewall -Update -NonInteractive
    }
    else {
        & $InstallScriptPath @arguments -GenerateSelfSignedCertificate:$true -ConfigureFirewall -NonInteractive
    }
}

function Initialize-UpdatePreservationMarkers {
    New-Item -ItemType Directory -Path (Join-Path $UpdateDataDirectory "spool\release-validation") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $UpdateDataDirectory "backups") -Force | Out-Null

    $spoolMarker = Join-Path $UpdateDataDirectory "spool\release-validation\preserve.eml"
    $backupMarker = Join-Path $UpdateDataDirectory "backups\release-validation-preserve.txt"
    Set-Content -LiteralPath $spoolMarker -Value "Subject: Relaywright release validation`r`n`r`nPreserve this spool marker across update." -Encoding ASCII
    Set-Content -LiteralPath $backupMarker -Value "Relaywright release validation backup marker." -Encoding ASCII

    $listenerConfig = @{
        httpsPort = $UpdateHttpsPort
        enableHttp = $false
        httpPort = $UpdateHttpPort
        updatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $UpdateDataDirectory "admin-web-listener.json") -Value $listenerConfig -Encoding UTF8
}

function Get-FileFingerprint {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        Path = $Path
        Length = $item.Length
        Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

function Get-UpdateDataFingerprint {
    $keyFiles = @()
    $keysPath = Join-Path $UpdateDataDirectory "keys"
    if (Test-Path -LiteralPath $keysPath) {
        foreach ($file in @(Get-ChildItem -LiteralPath $keysPath -File -Filter "*.xml" -ErrorAction SilentlyContinue)) {
            $keyFiles += Get-FileFingerprint -Path $file.FullName
        }
    }

    return [pscustomobject]@{
        DatabaseExists = Test-Path -LiteralPath (Join-Path $UpdateDataDirectory "relay.db")
        SpoolMarker = Get-FileFingerprint -Path (Join-Path $UpdateDataDirectory "spool\release-validation\preserve.eml")
        BackupMarker = Get-FileFingerprint -Path (Join-Path $UpdateDataDirectory "backups\release-validation-preserve.txt")
        ListenerConfig = Get-FileFingerprint -Path (Join-Path $UpdateDataDirectory "admin-web-listener.json")
        KeyFiles = $keyFiles
    }
}

function Assert-SameFingerprint {
    param(
        [object]$Before,
        [object]$After,
        [string]$Name
    )

    if (-not $Before -or -not $After) {
        throw "$Name was not present before and after update."
    }

    if ($Before.Hash -ne $After.Hash -or $Before.Length -ne $After.Length) {
        throw "$Name changed during update."
    }
}

function Assert-UpdatePreservedData {
    param(
        [object]$Before,
        [object]$After
    )

    if (-not $After.DatabaseExists) {
        throw "Database was not present after update."
    }

    Assert-SameFingerprint -Before $Before.SpoolMarker -After $After.SpoolMarker -Name "Spool preservation marker"
    Assert-SameFingerprint -Before $Before.BackupMarker -After $After.BackupMarker -Name "Backup preservation marker"
    Assert-SameFingerprint -Before $Before.ListenerConfig -After $After.ListenerConfig -Name "Admin web listener configuration"

    foreach ($beforeKey in @($Before.KeyFiles)) {
        $afterKey = @($After.KeyFiles | Where-Object { $_.Path -eq $beforeKey.Path }) | Select-Object -First 1
        Assert-SameFingerprint -Before $beforeKey -After $afterKey -Name "Data Protection key $($beforeKey.Path)"
    }
}

function Invoke-UpdatePackageValidation {
    if ([string]::IsNullOrWhiteSpace($FromVersion)) {
        throw "from_version is required when mode is update-package or full-release."
    }

    $fromVersionName = Normalize-Version -Value $FromVersion
    $toVersionName = Normalize-Version -Value $Version
    $fromAsset = "relaywright-$fromVersionName-windows-x64.zip"
    $toAsset = "relaywright-$toVersionName-windows-x64.zip"

    $fromAssets = Save-ReleaseAssetSet -ReleaseVersion $FromVersion -AssetNames @($fromAsset)
    $toAssets = Save-ReleaseAssetSet -ReleaseVersion $Version -AssetNames @($toAsset)

    Invoke-ReleaseValidationCleanup

    Write-Step "Installing baseline package $fromVersionName"
    Invoke-PackageInstall -PackagePath $fromAssets[$fromAsset]
    Wait-ServiceStatus -Name $UpdateServiceName -Status "Running" -TimeoutSeconds 60
    Assert-Health -Url "https://127.0.0.1:$UpdateHttpsPort/health"
    Invoke-InsecureWebRequest -Url "https://127.0.0.1:$UpdateHttpsPort/Account/Login" -TimeoutSeconds 15 | Out-Null
    Assert-DataLayout -DataDirectory $UpdateDataDirectory
    Assert-FirewallRules -GroupName $UpdateFirewallGroup -ExpectedPorts @($UpdateHttpsPort, $UpdateSmtpPort) -ForbiddenPorts @($UpdateHttpPort)

    Initialize-UpdatePreservationMarkers
    $before = Get-UpdateDataFingerprint
    Write-ArtifactJson -Name "update-fingerprint-before.json" -Value $before

    Write-Step "Updating package to $toVersionName"
    Invoke-PackageInstall -PackagePath $toAssets[$toAsset] -Update
    Wait-ServiceStatus -Name $UpdateServiceName -Status "Running" -TimeoutSeconds 60
    Assert-Health -Url "https://127.0.0.1:$UpdateHttpsPort/health"
    Assert-DataLayout -DataDirectory $UpdateDataDirectory
    Assert-FirewallRules -GroupName $UpdateFirewallGroup -ExpectedPorts @($UpdateHttpsPort, $UpdateSmtpPort) -ForbiddenPorts @($UpdateHttpPort)

    $after = Get-UpdateDataFingerprint
    Write-ArtifactJson -Name "update-fingerprint-after.json" -Value $after
    Assert-UpdatePreservedData -Before $before -After $after
}

function Invoke-FullReleaseValidation {
    Write-Step "Running full Windows release validation: update, cleanup, clean installer"
    Invoke-UpdatePackageValidation
    Invoke-ReleaseValidationCleanup
    Invoke-CleanInstallerValidation
}

Initialize-ArtifactsDirectory
Write-ArtifactJson -Name "validation-input.json" -Value ([pscustomobject]@{
    Mode = $Mode
    Version = $Version
    FromVersion = $FromVersion
    Repository = $Repository
    InstallerInstallRoot = $InstallerInstallRoot
    InstallerDataDirectory = $InstallerDataDirectory
    UpdateInstallRoot = $UpdateInstallRoot
    UpdateDataDirectory = $UpdateDataDirectory
})

$succeeded = $false
$failure = $null
try {
    Assert-Administrator
    switch ($Mode) {
        "clean-installer" { Invoke-CleanInstallerValidation }
        "update-package" { Invoke-UpdatePackageValidation }
        "full-release" { Invoke-FullReleaseValidation }
        "cleanup-only" { Invoke-ReleaseValidationCleanup }
    }

    $succeeded = $true
}
catch {
    $failure = $_
    Write-ArtifactText -Name "failure.txt" -Content ($_.Exception.ToString())
    Save-Diagnostics -Suffix "failure"
}

if ($succeeded -and $Mode -ne "cleanup-only") {
    try {
        Invoke-ReleaseValidationCleanup
    }
    catch {
        $failure = $_
        Write-ArtifactText -Name "cleanup-failure.txt" -Content ($_.Exception.ToString())
    }
}

Save-Diagnostics -Suffix "final"

if ($failure) {
    throw $failure
}

Write-Step "Windows release validation completed successfully"
