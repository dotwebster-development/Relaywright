[CmdletBinding()]
param(
    [string]$PackagePath = "",
    [string]$InstallRoot = "$env:ProgramFiles\Relaywright",
    [string]$DataDirectory = "$env:ProgramData\Relaywright",
    [string]$ServiceName = "Relaywright",
    [string]$DisplayName = "Relaywright",
    [string]$EnvironmentName = "Production",
    [int]$HttpsPort = 5443,
    [switch]$EnableHttp,
    [int]$HttpPort = 5080,
    [int]$SmtpPort = 25,
    [switch]$ConfigureFirewall,
    [string]$FirewallRulePrefix = "Relaywright",
    [string]$FirewallRemoteAddress = "Any",
    [string]$FirewallProfiles = "Any",
    [bool]$GenerateSelfSignedCertificate = $true,
    [string]$CertificateDnsName = "localhost",
    [string]$HttpsCertificatePath = "",
    [string]$HttpsCertificatePassword = "",
    [string]$BootstrapUserName = "admin",
    [string]$BootstrapEmail = "admin@localhost",
    [string]$BootstrapPassword = "",
    [int]$HealthTimeoutSeconds = 90,
    [switch]$Update,
    [switch]$Uninstall,
    [switch]$RemoveData,
    [switch]$NonInteractive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message"
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from an elevated PowerShell session."
    }
}

function Read-Default {
    param(
        [string]$Prompt,
        [string]$Default
    )

    if ($NonInteractive) {
        return $Default
    }

    $value = Read-Host "$Prompt [$Default]"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value.Trim()
}

function Read-YesNo {
    param(
        [string]$Prompt,
        [bool]$Default
    )

    if ($NonInteractive) {
        return $Default
    }

    $suffix = if ($Default) { "Y/n" } else { "y/N" }
    $value = Read-Host "$Prompt [$suffix]"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value.Trim().StartsWith("y", [StringComparison]::OrdinalIgnoreCase)
}

function Assert-Port {
    param(
        [int]$Port,
        [string]$Name
    )

    if ($Port -lt 1 -or $Port -gt 65535) {
        throw "$Name port '$Port' is not a valid TCP port."
    }
}

function New-RandomPassword {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $rng.Dispose()
    }
}

function Get-ServiceEnvironmentValue {
    param(
        [string]$Name,
        [string]$VariableName
    )

    $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    if (-not (Test-Path $registryPath)) {
        return $null
    }

    $environment = (Get-ItemProperty -Path $registryPath -Name Environment -ErrorAction SilentlyContinue).Environment
    foreach ($entry in @($environment)) {
        if ($entry -like "$VariableName=*") {
            return $entry.Substring($VariableName.Length + 1)
        }
    }

    return $null
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

function Get-Urls {
    Assert-Port -Port $HttpsPort -Name "HTTPS"
    $urls = @("https://*:$HttpsPort")
    if ($EnableHttp) {
        Assert-Port -Port $HttpPort -Name "HTTP"
        if ($HttpPort -eq $HttpsPort) {
            throw "HTTP and HTTPS ports must be different."
        }

        $urls += "http://*:$HttpPort"
    }

    return $urls -join ";"
}

function Get-TcpPortsFromUrls {
    param([string]$UrlList)

    $ports = New-Object System.Collections.Generic.List[int]
    foreach ($url in ($UrlList -split "[;,]")) {
        $trimmed = $url.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($trimmed -match "^[a-zA-Z][a-zA-Z0-9+.-]*://.*:(\d+)(?:/|$)") {
            $ports.Add([int]$Matches[1])
        }
        elseif ($trimmed -match "^https://") {
            $ports.Add(443)
        }
        elseif ($trimmed -match "^http://") {
            $ports.Add(80)
        }
    }

    return $ports | Sort-Object -Unique
}

function Remove-RelaywrightFirewallRules {
    param([string]$RulePrefix)

    if (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue) {
        Get-NetFirewallRule -Group $RulePrefix -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    }
}

function Set-RelaywrightFirewallRules {
    param(
        [string]$RulePrefix,
        [string]$ProgramPath,
        [string]$UrlList,
        [int]$RelayPort,
        [string]$RemoteAddress,
        [string]$Profiles
    )

    if (-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        throw "Windows Firewall cmdlets are unavailable. Install or enable the NetSecurity PowerShell module."
    }

    Assert-Port -Port $RelayPort -Name "SMTP"
    $adminPorts = @(Get-TcpPortsFromUrls -UrlList $UrlList)
    $ports = @($adminPorts + $RelayPort | Sort-Object -Unique)

    Write-Step "Configuring Windows Firewall rules"
    Remove-RelaywrightFirewallRules -RulePrefix $RulePrefix

    foreach ($port in $ports) {
        New-NetFirewallRule `
            -DisplayName "$RulePrefix TCP $port" `
            -Group $RulePrefix `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $port `
            -Program $ProgramPath `
            -Profile $Profiles `
            -RemoteAddress $RemoteAddress `
            -Description "Relaywright TCP port $port." | Out-Null

        Write-Host "Opened TCP port $port for $RemoteAddress."
    }
}

function Ensure-HttpsCertificate {
    param(
        [string]$Path,
        [string]$Password,
        [string]$DnsName,
        [string]$Service
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = Join-Path $InstallRoot "certs\relaywright.pfx"
    }

    if ([string]::IsNullOrWhiteSpace($Password)) {
        $Password = Get-ServiceEnvironmentValue `
            -Name $Service `
            -VariableName "ASPNETCORE_Kestrel__Certificates__Default__Password"
    }

    if ((Test-Path $Path) -and -not [string]::IsNullOrWhiteSpace($Password)) {
        return @{ Path = $Path; Password = $Password }
    }

    if (-not $GenerateSelfSignedCertificate) {
        throw "HTTPS requires a certificate. Provide HttpsCertificatePath and HttpsCertificatePassword, or allow self-signed certificate generation."
    }

    Write-Step "Creating self-signed HTTPS certificate"
    $certificateDirectory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($certificateDirectory)) {
        New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
    }

    $Password = New-RandomPassword
    $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
    $dnsNames = @($DnsName, "localhost")
    if (-not [string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) {
        $dnsNames += $env:COMPUTERNAME
    }

    $certificate = New-SelfSignedCertificate `
        -DnsName ($dnsNames | Sort-Object -Unique) `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -NotAfter (Get-Date).AddYears(2) `
        -KeyExportPolicy Exportable

    try {
        Export-PfxCertificate `
            -Cert "Cert:\LocalMachine\My\$($certificate.Thumbprint)" `
            -FilePath $Path `
            -Password $securePassword `
            -Force | Out-Null
    }
    finally {
        Remove-Item -Path "Cert:\LocalMachine\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }

    return @{ Path = $Path; Password = $Password }
}

function Invoke-HealthCheck {
    param([string]$Url)

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        $output = & $curl.Source --ssl-no-revoke --insecure --silent --show-error --fail --max-time 10 $Url 2>&1
        if ($LASTEXITCODE -eq 0) {
            return ($output -join [Environment]::NewLine)
        }

        throw "curl.exe failed with exit code ${LASTEXITCODE}: $($output -join ' ')"
    }

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return (Invoke-WebRequest -Uri $Url -UseBasicParsing -SkipCertificateCheck -TimeoutSec 10).Content
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    try {
        return (Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10).Content
    }
    finally {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback
    }
}

function Stop-RelaywrightService {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Stopped") {
        Write-Step "Stopping service $Name"
        Stop-Service -Name $Name -Force
        Wait-ServiceStatus -Name $Name -Status "Stopped"
    }
}

function Uninstall-Relaywright {
    Assert-Administrator
    Stop-RelaywrightService -Name $ServiceName
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Step "Deleting service $ServiceName"
        & sc.exe delete $ServiceName | Out-Host
    }

    Remove-RelaywrightFirewallRules -RulePrefix $FirewallRulePrefix

    if ($RemoveData) {
        Write-Step "Removing installation and data directories"
        if (Test-Path $InstallRoot) {
            Remove-Item -LiteralPath $InstallRoot -Recurse -Force
        }

        if (Test-Path $DataDirectory) {
            Remove-Item -LiteralPath $DataDirectory -Recurse -Force
        }
    }
    else {
        Write-Step "Removing installed binaries and preserving data"
        if (Test-Path $InstallRoot) {
            Remove-Item -LiteralPath $InstallRoot -Recurse -Force
        }
    }
}

if (-not $NonInteractive -and -not $Uninstall) {
    $InstallRoot = Read-Default -Prompt "Install directory" -Default $InstallRoot
    $DataDirectory = Read-Default -Prompt "Data directory" -Default $DataDirectory
    $HttpsPort = [int](Read-Default -Prompt "Admin HTTPS port" -Default ([string]$HttpsPort))
    if (Read-YesNo -Prompt "Enable admin HTTP listener" -Default ([bool]$EnableHttp)) {
        $EnableHttp = $true
        $HttpPort = [int](Read-Default -Prompt "Admin HTTP port" -Default ([string]$HttpPort))
    }
    else {
        $EnableHttp = $false
    }

    $SmtpPort = [int](Read-Default -Prompt "SMTP listener firewall port" -Default ([string]$SmtpPort))
    if (Read-YesNo -Prompt "Configure Windows Firewall" -Default ([bool]$ConfigureFirewall)) {
        $ConfigureFirewall = $true
        $FirewallRemoteAddress = Read-Default -Prompt "Firewall remote address" -Default $FirewallRemoteAddress
    }
    else {
        $ConfigureFirewall = $false
    }

    $GenerateSelfSignedCertificate = Read-YesNo -Prompt "Generate self-signed HTTPS certificate if needed" -Default $GenerateSelfSignedCertificate
}

if ($Uninstall) {
    Uninstall-Relaywright
    return
}

Assert-Administrator

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    throw "PackagePath is required for install or update."
}

$urls = Get-Urls
$healthUrl = "https://127.0.0.1:$HttpsPort/health"
$resolvedPackagePath = Resolve-Path -Path $PackagePath
$releaseName = Get-Date -Format "yyyyMMdd-HHmmss"
$releasesRoot = Join-Path $InstallRoot "releases"
$releasePath = Join-Path $releasesRoot $releaseName

Write-Step "Preparing directories"
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releasesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null

if ((Get-Item $resolvedPackagePath).PSIsContainer) {
    Write-Step "Copying package directory to release $releaseName"
    Copy-Item -Path (Join-Path $resolvedPackagePath "*") -Destination $releasePath -Recurse -Force
}
else {
    Write-Step "Expanding package archive to release $releaseName"
    Expand-Archive -Path $resolvedPackagePath -DestinationPath $releasePath -Force
}

$exePath = Join-Path $releasePath "Relaywright.Web.exe"
if (-not (Test-Path $exePath)) {
    throw "Relaywright.Web.exe was not found in package path '$PackagePath'."
}

$certificate = Ensure-HttpsCertificate `
    -Path $HttpsCertificatePath `
    -Password $HttpsCertificatePassword `
    -DnsName $CertificateDnsName `
    -Service $ServiceName
$HttpsCertificatePath = $certificate.Path
$HttpsCertificatePassword = $certificate.Password

Stop-RelaywrightService -Name $ServiceName

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$binaryPath = "`"$exePath`""
if ($existingService) {
    Write-Step "Updating service $ServiceName"
    & sc.exe config $ServiceName binPath= $binaryPath DisplayName= $DisplayName start= auto | Out-Host
}
else {
    Write-Step "Creating service $ServiceName"
    New-Service `
        -Name $ServiceName `
        -DisplayName $DisplayName `
        -BinaryPathName $binaryPath `
        -StartupType Automatic | Out-Null
}

& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/""/60000 | Out-Host

$serviceEnvironment = @(
    "ASPNETCORE_ENVIRONMENT=$EnvironmentName",
    "ASPNETCORE_URLS=$urls",
    "Storage__DataDirectory=$DataDirectory",
    "BootstrapAdmin__UserName=$BootstrapUserName",
    "BootstrapAdmin__Email=$BootstrapEmail",
    "ASPNETCORE_Kestrel__Certificates__Default__Path=$HttpsCertificatePath",
    "ASPNETCORE_Kestrel__Certificates__Default__Password=$HttpsCertificatePassword"
)

if (-not [string]::IsNullOrWhiteSpace($BootstrapPassword)) {
    $serviceEnvironment += "BootstrapAdmin__Password=$BootstrapPassword"
}

Write-Step "Writing service environment"
$serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty `
    -Path $serviceRegistryPath `
    -Name Environment `
    -Value $serviceEnvironment `
    -PropertyType MultiString `
    -Force | Out-Null

if ($ConfigureFirewall) {
    Set-RelaywrightFirewallRules `
        -RulePrefix $FirewallRulePrefix `
        -ProgramPath $exePath `
        -UrlList $urls `
        -RelayPort $SmtpPort `
        -RemoteAddress $FirewallRemoteAddress `
        -Profiles $FirewallProfiles
}

Write-Step "Starting service $ServiceName"
Start-Service -Name $ServiceName
Wait-ServiceStatus -Name $ServiceName -Status "Running"

Write-Step "Waiting for health check $healthUrl"
$deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
$lastError = $null
do {
    try {
        $response = Invoke-HealthCheck -Url $healthUrl
        if ($response -match '"status"\s*:\s*"ok"') {
            $lastError = $null
            break
        }

        $lastError = "Unexpected health response: $response"
    }
    catch {
        $lastError = $_.Exception.Message
    }

    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)

if ($lastError) {
    throw "Health check did not pass within $HealthTimeoutSeconds seconds. Last error: $lastError"
}

Write-Step "Pruning old releases"
Get-ChildItem -Path $releasesRoot -Directory |
    Sort-Object CreationTime -Descending |
    Select-Object -Skip 5 |
    Remove-Item -Recurse -Force

Write-Step "Relaywright installation complete"
Write-Host "Service: $ServiceName"
Write-Host "Install root: $InstallRoot"
Write-Host "Data: $DataDirectory"
Write-Host "Urls: $urls"
