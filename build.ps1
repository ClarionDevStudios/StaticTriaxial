# 本机构建入口。
#
# 为什么要包一层：`DOTNET_CLI_HOME` 不只影响模板引擎缓存，它**同时决定 NuGet 的全局包目录**
# （不设 → C:\Users\<用户>\.nuget\packages；设了 → $DOTNET_CLI_HOME\.nuget\packages）。
# 而本工程 FlexCel 7.26.0 依赖的 SkiaSharp 3.119.2 只存在于后者，工程 NuGet.config 又用
# <clear /> 清掉了 nuget.org —— 忘了设这个变量就会在还原阶段撞 NU1103。
#
# 用法：pwsh -NoProfile -File .\build.ps1 [-Configuration Release]
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = 'E:\AiStudy\CodeX\.dotnetcli'

Write-Host "DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME"
dotnet build "$PSScriptRoot\ANX.slnx" -m:1 -c $Configuration --nologo
exit $LASTEXITCODE
