param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet-cli-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:MSBuildEnableWorkloadResolver = "false"
$env:NUGET_PACKAGES = Join-Path $repoRoot ".nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repoRoot ".nuget\http-cache"
$env:TEMP = Join-Path $repoRoot ".nuget\temp"
$env:TMP = $env:TEMP

New-Item -ItemType Directory -Force $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $env:TEMP | Out-Null

Push-Location $repoRoot
try {
    if (-not $NoRestore) {
        & $dotnet restore .\FotoLibraryCleaner.sln --configfile .\NuGet.Config
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    & $dotnet build .\FotoLibraryCleaner.sln --no-restore
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
