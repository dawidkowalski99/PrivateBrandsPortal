# Local SDK helper. All caches and the downloaded SDK stay outside Git.
$ErrorActionPreference = 'Stop'
$portalSdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $portalSdk)) {
    $portalSdk = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_ROOT = Split-Path $portalSdk
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
& $portalSdk @args
exit $LASTEXITCODE
