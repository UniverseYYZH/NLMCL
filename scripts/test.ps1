param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

# net48 + SDK-style projects: the VSTest xunit adapter fails to load.
# Use the xunit console runner so the result is stable in local runs and CI.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dn = $env:NLMCL_DOTNET
if (-not $dn) {
    $dn = Join-Path $env:LOCALAPPDATA 'dsh-dotnet\dotnet.exe'
}
if (-not (Test-Path $dn)) {
    $dn = 'E:\NLMCL-tools\dotnet\dotnet.exe'
}
if (-not (Test-Path $dn)) {
    $dn = 'dotnet'
}

& $dn build (Join-Path $root 'NLMCL.sln') -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$runner = Join-Path $env:USERPROFILE ".nuget\packages\xunit.runner.console\2.9.2\tools\net472\xunit.console.exe"
if (-not (Test-Path $runner)) { throw "xunit console runner not found: $runner (run restore first)." }

$dll = Join-Path $root "tests\NLMCL.Tests\bin\$Configuration\net48\NLMCL.Tests.dll"
& $runner $dll -nologo -parallel none
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
