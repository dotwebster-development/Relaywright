[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$InstallRoot = "C:\Relaywright\Test",

    [string]$ServiceName = "RelaywrightTest",

    [string]$DisplayName = "Relaywright Test",

    [string]$EnvironmentName = "Production",

    [string]$Urls = "https://*:5443",

    [string]$HealthUrl = "https://127.0.0.1:5443/health",

    [string]$DataDirectory = "",

    [string]$BootstrapUserName = "admin",

    [string]$BootstrapEmail = "admin@localhost",

    [string]$BootstrapPassword = "",

    [string]$HttpsCertificatePath = "",

    [string]$HttpsCertificatePassword = "",

    [switch]$GenerateSelfSignedCertificate,

    [string]$CertificateDnsName = "localhost",

    [switch]$ConfigureFirewall,

    [string]$FirewallRulePrefix = "Relaywright Test",

    [string]$FirewallRemoteAddress = "LocalSubnet",

    [string]$FirewallProfiles = "Any",

    [string]$FirewallSmtpPorts = "2525",

    [int]$HealthTimeoutSeconds = 90
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
        throw "This script must run as Administrator because it creates and updates a Windows service."
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
        [int]$TimeoutSeconds = 30
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

function Invoke-HealthCheck {
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $null
    }

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        $output = & $curl.Source --ssl-no-revoke --insecure --silent --show-error --fail --max-time 10 $Url 2>&1
        if ($LASTEXITCODE -eq 0) {
            return [pscustomobject]@{
                StatusCode = 200
                Content = ($output -join [Environment]::NewLine)
            }
        }

        throw "curl.exe failed with exit code ${LASTEXITCODE}: $($output -join ' ')"
    }

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return Invoke-WebRequest -Uri $Url -UseBasicParsing -SkipCertificateCheck -TimeoutSec 10
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    try {
        return Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10
    }
    finally {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback
    }
}

function Write-ServiceDiagnostics {
    param([string]$Name)

    Write-Host "==> Service diagnostics"
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service) {
        Write-Host "Service status: $($service.Status)"
    }
    else {
        Write-Host "Service '$Name' was not found."
    }

    $serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    if (Test-Path $serviceRegistryPath) {
        $imagePath = (Get-ItemProperty -Path $serviceRegistryPath -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
        Write-Host "ImagePath: $imagePath"
    }

    $candidateSources = @($Name, "Relaywright.Web", ".NET Runtime", "Application Error")
    foreach ($source in $candidateSources) {
        try {
            $events = Get-EventLog -LogName Application -Source $source -Newest 10 -ErrorAction Stop
            if ($events) {
                Write-Host "Recent Application events from '$source':"
                foreach ($event in $events) {
                    Write-Host "[$($event.TimeGenerated)] $($event.EntryType) $($event.Message)"
                }
            }
        }
        catch {
        }
    }
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
            continue
        }

        if ($trimmed -match "^https://") {
            $ports.Add(443)
            continue
        }

        if ($trimmed -match "^http://") {
            $ports.Add(80)
        }
    }

    return $ports | Sort-Object -Unique
}

function Get-TcpPortsFromList {
    param([string]$PortList)

    $ports = New-Object System.Collections.Generic.List[int]
    foreach ($part in ($PortList -split "[,;\s]+")) {
        $trimmed = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        $port = 0
        if (-not [int]::TryParse($trimmed, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
            throw "Firewall port '$trimmed' is not a valid TCP port."
        }

        $ports.Add($port)
    }

    return $ports | Sort-Object -Unique
}

function Get-AdminWebListenerSettings {
    param([string]$Directory)

    $listenerPath = Join-Path $Directory "admin-web-listener.json"
    if (-not (Test-Path $listenerPath)) {
        return $null
    }

    $settings = Get-Content -Path $listenerPath -Raw | ConvertFrom-Json
    $httpsPort = [int]$settings.httpsPort
    $httpPort = [int]$settings.httpPort
    $enableHttp = [bool]$settings.enableHttp

    if ($httpsPort -lt 1 -or $httpsPort -gt 65535) {
        throw "Configured admin HTTPS port '$httpsPort' is not valid."
    }

    if ($enableHttp) {
        if ($httpPort -lt 1 -or $httpPort -gt 65535) {
            throw "Configured admin HTTP port '$httpPort' is not valid."
        }

        if ($httpPort -eq $httpsPort) {
            throw "Configured admin HTTP and HTTPS ports must be different."
        }
    }

    $configuredUrls = @("https://*:$httpsPort")
    if ($enableHttp) {
        $configuredUrls += "http://*:$httpPort"
    }

    return [pscustomobject]@{
        HttpsPort = $httpsPort
        EnableHttp = $enableHttp
        HttpPort = $httpPort
        Urls = ($configuredUrls -join ";")
        HealthUrl = "https://127.0.0.1:$httpsPort/health"
    }
}

function Set-RelaywrightFirewallRules {
    param(
        [string]$RulePrefix,
        [string]$ProgramPath,
        [string]$UrlList,
        [string]$SmtpPorts,
        [string]$RemoteAddress,
        [string]$Profiles
    )

    if (-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        throw "Windows Firewall cmdlets are unavailable. Install or enable the NetSecurity PowerShell module."
    }

    $adminPorts = @(Get-TcpPortsFromUrls -UrlList $UrlList)
    $relayPorts = @(Get-TcpPortsFromList -PortList $SmtpPorts)

    Write-Step "Configuring Windows Firewall rules"
    Get-NetFirewallRule -Group $RulePrefix -ErrorAction SilentlyContinue | Remove-NetFirewallRule

    foreach ($port in $adminPorts) {
        New-NetFirewallRule `
            -DisplayName "$RulePrefix Admin TCP $port" `
            -Group $RulePrefix `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $port `
            -Program $ProgramPath `
            -Profile $Profiles `
            -RemoteAddress $RemoteAddress `
            -Description "Relaywright admin UI test deployment port $port." | Out-Null

        Write-Host "Opened admin TCP port $port for $RemoteAddress."
    }

    foreach ($port in $relayPorts) {
        New-NetFirewallRule `
            -DisplayName "$RulePrefix SMTP TCP $port" `
            -Group $RulePrefix `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $port `
            -Program $ProgramPath `
            -Profile $Profiles `
            -RemoteAddress $RemoteAddress `
            -Description "Relaywright SMTP relay test deployment port $port." | Out-Null

        Write-Host "Opened SMTP TCP port $port for $RemoteAddress."
    }
}

function Ensure-TestCertificate {
    param(
        [string]$Path,
        [string]$Password,
        [string]$DnsName,
        [string]$Service
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = Join-Path $InstallRoot "certs\relaywright-test.pfx"
    }

    if ([string]::IsNullOrWhiteSpace($Password)) {
        $Password = Get-ServiceEnvironmentValue `
            -Name $Service `
            -VariableName "ASPNETCORE_Kestrel__Certificates__Default__Password"
    }

    if ((Test-Path $Path) -and -not [string]::IsNullOrWhiteSpace($Password)) {
        return @{
            Path = $Path
            Password = $Password
        }
    }

    if (-not $GenerateSelfSignedCertificate) {
        throw "Production-like HTTPS requires a certificate. Provide HttpsCertificatePath and HttpsCertificatePassword, or pass -GenerateSelfSignedCertificate for test VMs."
    }

    Write-Step "Creating self-signed HTTPS certificate for test VM"
    $certificateDirectory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($certificateDirectory)) {
        New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
    }

    $Password = New-RandomPassword
    $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
    $dnsNames = @($DnsName)
    if (-not [string]::IsNullOrWhiteSpace($env:COMPUTERNAME) -and $dnsNames -notcontains $env:COMPUTERNAME) {
        $dnsNames += $env:COMPUTERNAME
    }

    $certificate = New-SelfSignedCertificate `
        -DnsName $dnsNames `
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

    return @{
        Path = $Path
        Password = $Password
    }
}

Assert-Administrator

if ([string]::IsNullOrWhiteSpace($BootstrapPassword) -and -not [string]::IsNullOrWhiteSpace($env:RELAYWRIGHT_BOOTSTRAP_PASSWORD)) {
    $BootstrapPassword = $env:RELAYWRIGHT_BOOTSTRAP_PASSWORD
}

if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
    $DataDirectory = Join-Path $InstallRoot "App_Data"
}

$adminWebListenerSettings = Get-AdminWebListenerSettings -Directory $DataDirectory
if ($adminWebListenerSettings) {
    Write-Step "Using persisted admin web listener settings"
    $Urls = $adminWebListenerSettings.Urls
    $HealthUrl = $adminWebListenerSettings.HealthUrl
    Write-Host "Admin URLs: $Urls"
    Write-Host "Health URL: $HealthUrl"
}

$resolvedPackagePath = Resolve-Path -Path $PackagePath
$releasesRoot = Join-Path $InstallRoot "releases"
$releaseName = Get-Date -Format "yyyyMMdd-HHmmss"
$releasePath = Join-Path $releasesRoot $releaseName
$stagingRoot = Join-Path $InstallRoot "staging"

Write-Step "Preparing directories"
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releasesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

if (Test-Path $releasePath) {
    Remove-Item -LiteralPath $releasePath -Recurse -Force
}

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

if ($EnvironmentName -ne "Development" -and $Urls -match "https://") {
    $certificate = Ensure-TestCertificate `
        -Path $HttpsCertificatePath `
        -Password $HttpsCertificatePassword `
        -DnsName $CertificateDnsName `
        -Service $ServiceName
    $HttpsCertificatePath = $certificate.Path
    $HttpsCertificatePassword = $certificate.Password
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne "Stopped") {
    Write-Step "Stopping service $ServiceName"
    Stop-Service -Name $ServiceName -Force
    Wait-ServiceStatus -Name $ServiceName -Status "Stopped" -TimeoutSeconds 45
}

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
    "ASPNETCORE_URLS=$Urls",
    "Storage__DataDirectory=$DataDirectory",
    "BootstrapAdmin__UserName=$BootstrapUserName",
    "BootstrapAdmin__Email=$BootstrapEmail"
)

if (-not [string]::IsNullOrWhiteSpace($BootstrapPassword)) {
    $serviceEnvironment += "BootstrapAdmin__Password=$BootstrapPassword"
}

if (-not [string]::IsNullOrWhiteSpace($HttpsCertificatePath)) {
    $serviceEnvironment += "ASPNETCORE_Kestrel__Certificates__Default__Path=$HttpsCertificatePath"
}

if (-not [string]::IsNullOrWhiteSpace($HttpsCertificatePassword)) {
    $serviceEnvironment += "ASPNETCORE_Kestrel__Certificates__Default__Password=$HttpsCertificatePassword"
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
        -UrlList $Urls `
        -SmtpPorts $FirewallSmtpPorts `
        -RemoteAddress $FirewallRemoteAddress `
        -Profiles $FirewallProfiles
}

Write-Step "Starting service $ServiceName"
Start-Service -Name $ServiceName
Wait-ServiceStatus -Name $ServiceName -Status "Running" -TimeoutSeconds 45

if (-not [string]::IsNullOrWhiteSpace($HealthUrl)) {
    Write-Step "Waiting for health check $HealthUrl"
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    $lastError = $null
    do {
        try {
            $response = Invoke-HealthCheck -Url $HealthUrl
            if ($response.StatusCode -eq 200 -and $response.Content -match '"status"\s*:\s*"ok"') {
                Write-Step "Health check passed"
                $lastError = $null
                break
            }

            $lastError = "Unexpected health response: HTTP $($response.StatusCode) $($response.Content)"
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    if ($lastError) {
        Write-ServiceDiagnostics -Name $ServiceName
        throw "Health check did not pass within $HealthTimeoutSeconds seconds. Last error: $lastError"
    }
}

Write-Step "Pruning old releases"
Get-ChildItem -Path $releasesRoot -Directory |
    Sort-Object CreationTime -Descending |
    Select-Object -Skip 5 |
    Remove-Item -Recurse -Force

Write-Step "Relaywright Windows test deployment complete"
Write-Host "Service: $ServiceName"
Write-Host "Release: $releasePath"
Write-Host "Data: $DataDirectory"
Write-Host "Urls: $Urls"
