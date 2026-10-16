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
dotnet build "$PSScriptRoot\STS.slnx" -m:1 -c $Configuration --nologo

# 先留住构建结果，再做收尾 —— 收尾的成败不影响构建的退出码
$buildExitCode = $LASTEXITCODE

# 收掉常驻的编译服务器（VBCSCompiler）。MSBUILDDISABLENODEREUSE 只管 MSBuild 的 worker 节点，
# 编译器服务器是另一条常驻进程（实测构建后留 1 个），这里一起收掉。
# 代价：下一次构建要重新起编译器服务器（约一两秒）；换来的是构建结束后进程表干净。
#
# 注意（本机实测 2026-10-11）：`dotnet build-server shutdown` 在这里**关不掉** VBCSCompiler
# （报"未能关闭 VB/C# 编译器服务器：shutdown 命令失败"，MSBuild 服务器则能关掉），
# 所以优雅关闭之后再补一次强停。强停 Roslyn 编译器服务器是安全的：它只是缓存，
# 下一次构建会自己起新的。唯一代价是——若此刻别处正在编译，那个构建要重来。
try {
    dotnet build-server shutdown | Out-Null
}
catch {
    Write-Host "优雅关闭编译服务器失败（不影响构建结果）：$_"
}

$leftover = Get-Process -Name VBCSCompiler -ErrorAction SilentlyContinue
if ($leftover) {
    Write-Host "优雅关闭未生效，强停编译器服务器（PID $($leftover.Id -join ', ')）"
    $leftover | Stop-Process -Force
}

exit $buildExitCode
