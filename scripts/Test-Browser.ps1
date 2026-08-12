[CmdletBinding()]
param(
    [ValidateSet("msedge", "chrome", "chromium")]
    [string] $BrowserChannel = "msedge",
    [switch] $InstallChromium
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "tests\Relaywright.Web.BrowserTests\Relaywright.Web.BrowserTests.csproj"

dotnet build $project
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($InstallChromium) {
    $installer = Join-Path $repositoryRoot "tests\Relaywright.Web.BrowserTests\bin\Debug\net10.0\playwright.ps1"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer install chromium
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($BrowserChannel -eq "chromium") {
    dotnet test $project --no-build -- Playwright.BrowserName=chromium
}
else {
    dotnet test $project --no-build -- Playwright.BrowserName=chromium Playwright.LaunchOptions.Channel=$BrowserChannel
}
exit $LASTEXITCODE
