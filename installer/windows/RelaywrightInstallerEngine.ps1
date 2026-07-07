[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,

    [ValidateSet("Install")]
    [string]$Operation = "Install"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-InstallerConfig {
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        throw "Installer configuration was not found at '$ConfigPath'."
    }

    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
}

$config = Read-InstallerConfig
$logDirectory = Join-Path ([string]$config.DataDirectory) "logs"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory ("installer-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

function Write-InstallerLog {
    param([string]$Message)

    $line = "{0} {1}" -f (Get-Date).ToUniversalTime().ToString("O"), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $Message
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run the Relaywright installer as Administrator."
    }
}

function ConvertTo-BooleanValue {
    param(
        [object]$Value,
        [string]$Name
    )

    if ($Value -is [bool]) {
        return [bool]$Value
    }

    $text = ([string]$Value).Trim()
    if ($text.Equals("true", [StringComparison]::OrdinalIgnoreCase) -or $text -eq "1") {
        return $true
    }

    if ($text.Equals("false", [StringComparison]::OrdinalIgnoreCase) -or $text -eq "0" -or $text.Length -eq 0) {
        return $false
    }

    throw "$Name must be true or false."
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

function Assert-PortAvailable {
    param(
        [int]$Port,
        [string]$Name
    )

    Assert-Port -Port $Port -Name $Name
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, $Port)
    try {
        $listener.Start()
    }
    catch {
        throw "$Name port $Port is not available. Stop the service using that port or choose another port."
    }
    finally {
        $listener.Stop()
    }
}

function Test-TcpReachable {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds = 3000
    )

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $result = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $result.AsyncWaitHandle.WaitOne($TimeoutMilliseconds, $false)) {
            throw "Timed out"
        }

        $client.EndConnect($result)
    }
    catch {
        throw "Database endpoint $HostName`:$Port is not reachable."
    }
    finally {
        $client.Close()
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

function Normalize-DatabaseProvider {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "Sqlite"
    }

    switch ($Value.Trim().ToLowerInvariant()) {
        "sqlite" { return "Sqlite" }
        "sqlserver" { return "SqlServer" }
        "mssql" { return "SqlServer" }
        "mysql" { return "MySql" }
        default { throw "Unsupported database provider '$Value'. Use Sqlite, SqlServer, or MySql." }
    }
}

function New-DatabaseConnectionString {
    param([object]$Config)

    $provider = Normalize-DatabaseProvider -Value ([string]$Config.DatabaseProvider)
    if ($provider -eq "Sqlite") {
        return ""
    }

    $server = ([string]$Config.DatabaseServer).Trim()
    $database = ([string]$Config.DatabaseName).Trim()
    $user = ([string]$Config.DatabaseUser).Trim()
    $password = [string]$Config.DatabasePassword
    $port = [int]$Config.DatabasePort

    if ([string]::IsNullOrWhiteSpace($server)) {
        throw "$provider requires a database server name."
    }

    if ([string]::IsNullOrWhiteSpace($database)) {
        throw "$provider requires a database name."
    }

    if ([string]::IsNullOrWhiteSpace($user)) {
        throw "$provider requires a database user name."
    }

    if ([string]::IsNullOrWhiteSpace($password)) {
        throw "$provider requires a database password."
    }

    if ($provider -eq "SqlServer") {
        return "Server=$server,$port;Database=$database;User Id=$user;Password=$password;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Application Name=Relaywright"
    }

    return "server=$server;port=$port;database=$database;user=$user;password=$password;SslMode=Preferred;AllowPublicKeyRetrieval=True;Application Name=Relaywright"
}

function Get-Urls {
    param(
        [int]$HttpsPort,
        [bool]$EnableHttp,
        [int]$HttpPort
    )

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
        [string]$RemoteAddress
    )

    if (-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        throw "Windows Firewall cmdlets are unavailable. Install or enable the NetSecurity PowerShell module."
    }

    Assert-Port -Port $RelayPort -Name "SMTP"
    $adminPorts = @(Get-TcpPortsFromUrls -UrlList $UrlList)
    $ports = @($adminPorts + $RelayPort | Sort-Object -Unique)

    Write-InstallerLog "Configuring Windows Firewall rules for $RemoteAddress."
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
            -Profile Any `
            -RemoteAddress $RemoteAddress `
            -Description "Relaywright TCP port $port." | Out-Null

        Write-InstallerLog "Opened TCP port $port for $RemoteAddress."
    }
}

function Ensure-HttpsCertificate {
    param(
        [string]$DataDirectory,
        [string]$Service
    )

    $path = Join-Path $DataDirectory "certs\relaywright.pfx"
    $password = Get-ServiceEnvironmentValue `
        -Name $Service `
        -VariableName "ASPNETCORE_Kestrel__Certificates__Default__Password"

    if ((Test-Path $path) -and -not [string]::IsNullOrWhiteSpace($password)) {
        Write-InstallerLog "Reusing existing HTTPS certificate."
        return @{ Path = $path; Password = $password }
    }

    Write-InstallerLog "Creating self-signed HTTPS certificate."
    $certificateDirectory = Split-Path -Parent $path
    New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null

    $password = New-RandomPassword
    $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
    $dnsNames = @("localhost")
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
            -FilePath $path `
            -Password $securePassword `
            -Force | Out-Null
    }
    finally {
        Remove-Item -Path "Cert:\LocalMachine\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }

    return @{ Path = $path; Password = $password }
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

function Stop-RelaywrightService {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Stopped") {
        Write-InstallerLog "Stopping service $Name."
        Stop-Service -Name $Name -Force
        Wait-ServiceStatus -Name $Name -Status "Stopped"
    }
}

function Get-ServiceSnapshot {
    param([string]$Name)

    $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $service -or -not (Test-Path $registryPath)) {
        return $null
    }

    $properties = Get-ItemProperty -Path $registryPath
    return [pscustomobject]@{
        Name = $Name
        ImagePath = [string]$properties.ImagePath
        Environment = @($properties.Environment)
        DisplayName = [string]$properties.DisplayName
        WasRunning = $service.Status -eq "Running"
    }
}

function Restore-ServiceSnapshot {
    param(
        [object]$Snapshot,
        [string]$ServiceName
    )

    if (-not $Snapshot) {
        return
    }

    Write-InstallerLog "Rolling back service $ServiceName to the previous release."
    Stop-RelaywrightService -Name $ServiceName
    & sc.exe config $ServiceName binPath= $Snapshot.ImagePath DisplayName= $Snapshot.DisplayName start= auto | Out-Host

    $serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    New-ItemProperty `
        -Path $serviceRegistryPath `
        -Name Environment `
        -Value @($Snapshot.Environment) `
        -PropertyType MultiString `
        -Force | Out-Null

    if ($Snapshot.WasRunning) {
        Start-Service -Name $ServiceName
        Wait-ServiceStatus -Name $ServiceName -Status "Running" -TimeoutSeconds 60
    }
}

function Assert-Preflight {
    param(
        [object]$Config,
        [bool]$ServiceExists
    )

    Write-InstallerLog "Running installer preflight checks."
    Assert-Administrator

    New-Item -ItemType Directory -Path ([string]$Config.DataDirectory) -Force | Out-Null
    $probePath = Join-Path ([string]$Config.DataDirectory) ("installer-write-{0}.tmp" -f [Guid]::NewGuid().ToString("N"))
    Set-Content -LiteralPath $probePath -Value "ok" -Encoding ASCII
    Remove-Item -LiteralPath $probePath -Force

    $configureFirewall = ConvertTo-BooleanValue -Value $Config.ConfigureFirewall -Name "ConfigureFirewall"
    if ($configureFirewall -and -not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        throw "Windows Firewall cmdlets are unavailable. Install or enable the NetSecurity PowerShell module."
    }

    $provider = Normalize-DatabaseProvider -Value ([string]$Config.DatabaseProvider)
    if ($provider -ne "Sqlite") {
        Test-TcpReachable -HostName ([string]$Config.DatabaseServer) -Port ([int]$Config.DatabasePort)
    }

    if (-not $ServiceExists) {
        Assert-PortAvailable -Port ([int]$Config.HttpsPort) -Name "HTTPS"
        if (ConvertTo-BooleanValue -Value $Config.EnableHttp -Name "EnableHttp") {
            Assert-PortAvailable -Port ([int]$Config.HttpPort) -Name "HTTP"
        }

        Assert-PortAvailable -Port ([int]$Config.SmtpPort) -Name "SMTP"
    }
}

function Copy-PackageToRelease {
    param(
        [string]$PackagePath,
        [string]$ReleasePath
    )

    if (-not (Test-Path -LiteralPath $PackagePath)) {
        throw "Package path '$PackagePath' was not found."
    }

    New-Item -ItemType Directory -Path $ReleasePath -Force | Out-Null
    Write-InstallerLog "Copying package to release $ReleasePath."
    Copy-Item -Path (Join-Path $PackagePath "*") -Destination $ReleasePath -Recurse -Force
}

function Prune-OldReleases {
    param([string]$ReleasesRoot)

    Write-InstallerLog "Pruning old releases."
    Get-ChildItem -Path $ReleasesRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object CreationTime -Descending |
        Select-Object -Skip 5 |
        Remove-Item -Recurse -Force
}

function Remove-LegacyInstalledTools {
    param([string]$InstallRoot)

    $toolsPath = Join-Path $InstallRoot "tools"
    if (-not (Test-Path -LiteralPath $toolsPath)) {
        return
    }

    Write-InstallerLog "Removing legacy installed tools directory."
    Remove-Item -LiteralPath $toolsPath -Recurse -Force
}

if ($Operation -ne "Install") {
    throw "Unsupported installer operation '$Operation'."
}

$serviceName = "Relaywright"
$displayName = "Relaywright - SMTP relay gateway"
$firewallGroup = "Relaywright"
$provider = Normalize-DatabaseProvider -Value ([string]$config.DatabaseProvider)
$existingSnapshot = Get-ServiceSnapshot -Name $serviceName
$serviceExists = $null -ne $existingSnapshot
$existingProvider = Normalize-DatabaseProvider -Value (Get-ServiceEnvironmentValue -Name $serviceName -VariableName "Database__Provider")
$enableHttp = ConvertTo-BooleanValue -Value $config.EnableHttp -Name "EnableHttp"
$configureFirewall = ConvertTo-BooleanValue -Value $config.ConfigureFirewall -Name "ConfigureFirewall"
$urls = Get-Urls -HttpsPort ([int]$config.HttpsPort) -EnableHttp $enableHttp -HttpPort ([int]$config.HttpPort)
$healthUrl = "https://127.0.0.1:$([int]$config.HttpsPort)/health"

Write-InstallerLog "Relaywright installer started. Version=$($config.Version); Provider=$provider; Database=$($config.DatabaseName); InstallRoot=$($config.InstallRoot); DataDirectory=$($config.DataDirectory); ServiceExists=$serviceExists."

if ($serviceExists -and $existingProvider -ne $provider) {
    throw "Existing Relaywright service uses database provider '$existingProvider'. In-place provider changes to '$provider' are blocked."
}

Assert-Preflight -Config $config -ServiceExists $serviceExists

$databaseConnectionString = New-DatabaseConnectionString -Config $config
$installRoot = [string]$config.InstallRoot
$dataDirectory = [string]$config.DataDirectory
$releasesRoot = Join-Path $installRoot "releases"
$releaseName = Get-Date -Format "yyyyMMdd-HHmmss"
$releasePath = Join-Path $releasesRoot $releaseName

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releasesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $dataDirectory "spool") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $dataDirectory "keys") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $dataDirectory "backups") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $dataDirectory "certs") -Force | Out-Null

Copy-PackageToRelease -PackagePath ([string]$config.PackagePath) -ReleasePath $releasePath
$exePath = Join-Path $releasePath "Relaywright.Web.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Relaywright.Web.exe was not found in '$releasePath'."
}

$certificate = Ensure-HttpsCertificate -DataDirectory $dataDirectory -Service $serviceName

try {
    Stop-RelaywrightService -Name $serviceName
    Assert-PortAvailable -Port ([int]$config.HttpsPort) -Name "HTTPS"
    if ($enableHttp) {
        Assert-PortAvailable -Port ([int]$config.HttpPort) -Name "HTTP"
    }

    Assert-PortAvailable -Port ([int]$config.SmtpPort) -Name "SMTP"

    $binaryPath = "`"$exePath`""
    if ($serviceExists) {
        Write-InstallerLog "Updating Windows service $serviceName."
        & sc.exe config $serviceName binPath= $binaryPath DisplayName= $displayName start= auto | Out-Host
    }
    else {
        Write-InstallerLog "Creating Windows service $serviceName."
        New-Service `
            -Name $serviceName `
            -DisplayName $displayName `
            -BinaryPathName $binaryPath `
            -StartupType Automatic | Out-Null
    }

    & sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/""/60000 | Out-Host

    $serviceEnvironment = @(
        "ASPNETCORE_ENVIRONMENT=Production",
        "ASPNETCORE_URLS=$urls",
        "Storage__DataDirectory=$dataDirectory",
        "Database__Provider=$provider",
        "Database__ConnectionString=$databaseConnectionString",
        "BootstrapAdmin__UserName=$($config.BootstrapUserName)",
        "BootstrapAdmin__Email=$($config.BootstrapEmail)",
        "ASPNETCORE_Kestrel__Certificates__Default__Path=$($certificate.Path)",
        "ASPNETCORE_Kestrel__Certificates__Default__Password=$($certificate.Password)"
    )

    if (-not [string]::IsNullOrWhiteSpace([string]$config.BootstrapPassword)) {
        $serviceEnvironment += "BootstrapAdmin__Password=$($config.BootstrapPassword)"
    }

    Write-InstallerLog "Writing service environment."
    $serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
    New-ItemProperty `
        -Path $serviceRegistryPath `
        -Name Environment `
        -Value $serviceEnvironment `
        -PropertyType MultiString `
        -Force | Out-Null

    if ($configureFirewall) {
        Set-RelaywrightFirewallRules `
            -RulePrefix $firewallGroup `
            -ProgramPath $exePath `
            -UrlList $urls `
            -RelayPort ([int]$config.SmtpPort) `
            -RemoteAddress ([string]$config.FirewallRemoteAddress)
    }

    Write-InstallerLog "Starting service $serviceName."
    Start-Service -Name $serviceName
    Wait-ServiceStatus -Name $serviceName -Status "Running" -TimeoutSeconds 60

    Write-InstallerLog "Waiting for health check $healthUrl."
    $deadline = (Get-Date).AddSeconds(120)
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
        throw "Health check did not pass within 120 seconds. Last error: $lastError"
    }

    Remove-LegacyInstalledTools -InstallRoot $installRoot
    Prune-OldReleases -ReleasesRoot $releasesRoot
    Write-InstallerLog "Relaywright installation complete. Urls=$urls; Log=$logPath."
}
catch {
    Write-InstallerLog "Installation failed: $($_.Exception.Message)"
    if ($serviceExists) {
        try {
            Restore-ServiceSnapshot -Snapshot $existingSnapshot -ServiceName $serviceName
            Write-InstallerLog "Rollback completed."
        }
        catch {
            Write-InstallerLog "Rollback failed: $($_.Exception.Message)"
        }
    }

    throw
}
