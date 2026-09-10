$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.nuget\packages'
$env:APPDATA = Join-Path $repositoryRoot '.appdata'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

New-Item -ItemType Directory -Force -Path (Join-Path $env:APPDATA 'NuGet') | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'NuGet.Config') `
    -Destination (Join-Path $env:APPDATA 'NuGet\NuGet.Config') `
    -Force

& $dotnet restore (Join-Path $repositoryRoot 'SnipTranslate.slnx') `
    --configfile (Join-Path $repositoryRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build (Join-Path $repositoryRoot 'SnipTranslate.slnx') `
    --configuration Debug `
    --no-restore
exit $LASTEXITCODE

