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

# 关掉 MSBuild 的「节点复用」。
# 默认行为是构建结束后 worker 节点留在后台等下一次构建（空闲 15 分钟才自退），
# 实测连跑几次 build/test 就攒下 19 个 MSBuild + 1 个 VBCSCompiler、合计约 1.9 GB 内存，
# 而且它们会一直占着输出 DLL，导致应用在运行中时构建失败（MSB3021/MSB3027）。
# 关掉之后：构建结束即刻回收，代价是每次多花一点启动开销。
$env:MSBUILDDISABLENODEREUSE = '1'

Write-Host "DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME"
dotnet build "$PSScriptRoot\ANX.slnx" -m:1 -c $Configuration --nologo
exit $LASTEXITCODE
