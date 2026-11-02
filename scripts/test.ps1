param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

# net48 + SDK 风格项目下 VSTest 加载 xunit 适配器会失败，
# 因此统一走 xunit 自带控制台 runner，行为稳定且可进 CI。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dn = Join-Path $env:LOCALAPPDATA 'dsh-dotnet\dotnet.exe'
if (-not (Test-Path $dn)) { $dn = 'dotnet' }

& $dn build (Join-Path $root 'NLMCL.sln') -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }

$runner = Join-Path $env:USERPROFILE ".nuget\packages\xunit.runner.console\2.9.2\tools\net472\xunit.console.exe"
if (-not (Test-Path $runner)) { throw "找不到 xunit 控制台 runner：$runner（先执行一次 restore）。" }

$dll = Join-Path $root "tests\NLMCL.Tests\bin\$Configuration\net48\NLMCL.Tests.dll"
& $runner $dll -nologo -parallel none
if ($LASTEXITCODE -ne 0) { throw "测试失败（退出码 $LASTEXITCODE）。" }
