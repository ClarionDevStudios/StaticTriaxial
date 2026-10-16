# StaticTriaxial（STS）—— 抗剪强度试验软件（静三轴法）

C# / WPF 上位机软件：**串口通讯**，协议按《全自动土工项目通讯协议 V1.7》
（全自动土工项目系列；三轴仪为 STM32 主控、PC 控制模式）。
目标链路：**联机 → 采集 / 控制 → 判定 → 落库 → 报表 → 界面**。

本工程 2026-10-16 自 AsphaltNext（沥青综合性能试验软件，ANX）**保留全史**生成：

- 程序集前缀 / 命名空间 `ANX.` → `STS.`；
- 沥青方法（弯曲 / 马歇尔）、GR-3（UDP）协议、沥青报表与对照文档已清出源码树
  （git 历史里都还在，随时可查）；
- 串口通道按 IAP 的 `SerialChannel` 适配而来（同为本机自研代码，按本工程 `ICommChannel` 契约重写对外面）。

## 结构

```
src\STS.Core           领域与契约（无外部引用）
src\STS.Protocol       土工协议层（骨架占位：GeoDeviceController；帧层 FA FB FC + CRC-8 待实现）
src\STS.Communication  通道实现（串口已就位；调试模拟器仅 Debug 构建）
src\STS.Ui             WPF 外壳与通用页
src\STS.Host           宿主（唯一引用 methods\ 的工程）
tests\                 xUnit
methods\               方法插件（STS.Method.ShearStrength：骨架占位）
tools\                 开发工具（表定义 → 建表 SQL 导出）
docs\                  约定与参考（docs\参考\ 内为 ANX 原件：界面约定 / 数据表约定 / 接班说明）
```

**依赖铁律**：Core 不引用任何业务工程；Ui 只认 Core 的接口；**Host 是唯一引用 `methods\*` 的工程**；
方法工程可引用 Core、不得引用 Ui。

## 构建（本机约定）

直接跑仓库里的脚本，别手敲 `dotnet`：

```powershell
pwsh -NoProfile -File .\build.ps1                 # 也可以 -Configuration Release
pwsh -NoProfile -File .\test.ps1
```

三个坑（都实测过）：① `DOTNET_CLI_HOME` 必须设（脚本已设）；② 一律 `-m:1`；
③ NuGet 走工程内 `NuGet.config`（离线源）。模拟器只在 Debug 构建里存在（`Directory.Build.targets` 的 `SIMULATOR` 宏）。

## 当前状态（骨架）

- 已就位：改名与清理完成、串口通道（含单元测试）、外壳可启动、方法目录挂 `STS.Method.ShearStrength` 骨架。
- 进行中（本周）：土工协议帧层（V1.7，含"体积变化量上传"独立小块）→ 模拟下位机 → 全链路 → 真机验证（待设备）。

## 对照口径

现役 Delphi 土工三轴软件（`E:\Work\土工三轴软件\土工三轴软件通讯重构版`）是"标准答案"：
判定与报表照现役实物逐格抄；协议按《全自动土工项目通讯协议 V1.7》实现，
并与现役拆包（`uAnalyzeDataController.pas`）逐行对照，差异记入 `docs\对照\`。
