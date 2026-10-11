# 本机测试入口，与 build.ps1 同样先固定 DOTNET_CLI_HOME（原因见 build.ps1 头部注释）。
#
# 用法：pwsh -NoProfile -File .\test.ps1
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = 'E:\AiStudy\CodeX\.dotnetcli'

# 同上（原因见 build.ps1 注释）：不留后台 MSBuild 节点
$env:MSBUILDDISABLENODEREUSE = '1'

Write-Host "DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME"
dotnet test "$PSScriptRoot\ANX.slnx" -m:1 --nologo
exit $LASTEXITCODE
