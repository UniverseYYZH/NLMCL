<#
.SYNOPSIS
    发布 NLMCL 单文件 exe。

.DESCRIPTION
    net48 不支持 `dotnet publish -p:PublishSingleFile`（那是 .NET Core 3+ 的功能），
    因此这里走 Costura.Fody：把引用的 DLL 作为资源嵌进 exe，运行时从内存加载。

    产物是一个自包含的 NLMCL.exe（约 1.5 MB），拷到任意 Windows 10/11 机器双击即可运行。
    前提是系统自带 .NET Framework 4.8（Win10 1903+ / Win11 默认都有）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\NLMCL.App\NLMCL.App.csproj'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'artifacts\publish'
}

# 优先用 harness 自带的 SDK——PATH 上的 dotnet 可能只是个没有 SDK 的宿主。
$dotnet = Join-Path $env:LOCALAPPDATA 'dsh-dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = 'dotnet'
    if (-not (Get-Command $dotnet -ErrorAction SilentlyContinue)) {
        throw '找不到 dotnet，请安装 .NET SDK 8+。'
    }
}

Write-Host "构建 $Configuration ..." -ForegroundColor Cyan
& $dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "构建失败（退出码 $LASTEXITCODE）。"
}

$built = Join-Path $root "src\NLMCL.App\bin\$Configuration\net48\NLMCL.exe"
if (-not (Test-Path $built)) {
    throw "没有找到构建产物：$built"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# 只拷贝 exe：Costura 已把依赖嵌进去了，config/pdb 都不需要。
# （已实测：把 exe 单独放进空目录能正常启动）
Copy-Item $built (Join-Path $OutputDirectory 'NLMCL.exe') -Force

$exe = Get-Item (Join-Path $OutputDirectory 'NLMCL.exe')
$sizeMb = [math]::Round($exe.Length / 1MB, 2)

Write-Host ''
Write-Host '发布完成' -ForegroundColor Green
Write-Host "  路径: $($exe.FullName)"
Write-Host "  大小: $sizeMb MB"
Write-Host ''
Write-Host '注意：exe 未做代码签名，首次运行可能触发 SmartScreen「未知发布者」提示。' -ForegroundColor Yellow
Write-Host '      选择「更多信息 → 仍要运行」即可。' -ForegroundColor Yellow
