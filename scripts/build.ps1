param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dn = Join-Path $env:LOCALAPPDATA 'dsh-dotnet\dotnet.exe'
if (-not (Test-Path $dn)) { $dn = 'dotnet' }

& $dn build (Join-Path $root 'NLMCL.sln') -c $Configuration --nologo -v m
if ($LASTEXITCODE -ne 0) { throw "构建失败（$Configuration）。" }

Write-Host ''
Write-Host "输出：$root\src\NLMCL.App\bin\$Configuration\net48\NLMCL.exe" -ForegroundColor Green
