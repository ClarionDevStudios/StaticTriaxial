# AsphaltNext（ANX）

沥青综合性能试验软件的 **C# / WPF 重构版**——一个**学习项目**：目的是练 C# 与工业上位机开发能力，**不替换**现役 Delphi 软件（现役继续正常维护，并扮演"标准答案"，先自己按规范/协议写、再与现役比对，差异写进 `docs\对照\`）。

## 结论来源

所有设计决策来自 2026-10-09 ~ 2026-10-10 的方案访谈（23 条共识 + 7 个页面 + 5 条默认），落库在 C# 知识库的项目卡片：
`E:\ObsidianVault\CSharp\CSharp项目\200-项目\沥青综合性能试验软件（AsphaltNext）.md`

## 构建（本机实测约定）

**直接跑仓库里的脚本，别手敲 dotnet**：

```powershell
pwsh -NoProfile -File .\build.ps1                 # 也可以 -Configuration Release
pwsh -NoProfile -File .\test.ps1
```

脚本干三件事：设 `DOTNET_CLI_HOME`、加 `-m:1`、调 `dotnet`。手敲的话要写成：

```powershell
$env:DOTNET_CLI_HOME = 'E:\AiStudy\CodeX\.dotnetcli'
dotnet build ANX.slnx -m:1
```

三个坑（都实测过，缺一个就出事）：

1. **`DOTNET_CLI_HOME` 必须设**。它不只影响模板引擎缓存（默认会往工作区外的
   `C:\Users\<用户>\.templateengine` 写，报 Access denied），**还会改变 NuGet 的全局包目录**
   （不设 → `C:\Users\<用户>\.nuget\packages`；设了 → `$DOTNET_CLI_HOME\.nuget\packages`）。
   本工程 FlexCel 7.26.0 依赖的 `SkiaSharp 3.119.2` **只存在于后者**，而工程 `NuGet.config`
   用 `<clear />` 清掉了用户级的 nuget.org 源 —— 所以漏设这个变量会在还原阶段报
   `NU1103 找不到稳定包 SkiaSharp`（看着像"源里没有"，其实是查错了目录）。
   排查第一步永远是 `dotnet nuget locals global-packages --list`，看当前真正生效的是哪个目录。
2. **一律 `-m:1`**：并行还原在本机会静默失败（0 警告 0 错误，但产物没生成）。
3. NuGet 走工程内 `NuGet.config`（`<clear />` + 本机缓存离线源 + FlexCel 零售包目录），不依赖公网。
   要补新的包：`dotnet restore ANX.slnx -m:1 --force -s https://api.nuget.org/v3/index.json`（一次联网，之后仍可离线还原）。

## 里程碑

| # | 内容 | 状态 |
| --- | --- | --- |
| M1 | 骨架可运行（外壳 + DI/日志 + 契约 + 测试链路） | **已完成（2026-10-10）** |
| M2 | 通道 + GR-3 协议（抓帧黄金用例） | **已完成（2026-10-10，真机联机通过）** |
| M3 | T0715 全链路出报告 | **已完成**（协议→解析→控制→判定→落库→报表→界面五页；仅剩真机空载试电机方向字与复位） |
| M4 | 真机验证 + 第二个方法（马歇尔） | 未开始 |

## 工程划分

```
src\ANX.Core           领域与契约（无外部引用）
src\ANX.Protocol       GR-3 两层帧编解码（CRC-8 / 命令字 / 上下行）
src\ANX.Communication  通道实现（UDP；串口待定）
src\ANX.Ui             WPF 外壳与通用页
src\ANX.Host           宿主（唯一引用 methods\ 的工程）
tests\                 xUnit（Core / Protocol / Communication 三个测试工程）
methods\               方法插件（M3 起）
docs\                  实现方案、对照表、学习笔记
```

**依赖铁律**：Core 不引用任何业务工程；Ui 只认 Core 的接口；Host 是唯一引用 `methods\*` 的工程；方法工程可引用 Core、不得引用 Ui。

## M1 验证记录（2026-10-10）

| 项 | 结果 |
| --- | --- |
| 编译 | `dotnet build ANX.slnx -m:1` → **0 警告 0 错误**（4 个工程全部生成） |
| 测试 | `dotnet test ANX.slnx -m:1` → **16/16 通过**（量纲码 5 个、试验单号 11 个） |
| 启动 | 程序启动后主窗口创建成功，窗口标题正确显示为「沥青综合性能试验软件（ANX）」 |
| 日志 | `bin\Debug\net10.0-windows\Logs\anx-20261010.log` 落盘，含「启动完成，日志目录：…」 |
| 本地化 | 启动路径里 `SetLanguage("zh-CN")` 已执行成功（资源 URI 若错会抛异常并记「启动失败」，实际没有） |

**需要人工复测的**：切页（首页 ⇄ 系统设置）、切中英、切深浅主题、点「打开日志目录」。这些是运行期交互，离线无法自动断言。

**M1 已知边界**：系统设置未持久化（`IAppSettingsService` 只有契约）；协议、控制、存储、报表四层只有契约，M2 起逐个落地。

## M2 验证记录（2026-10-10）

| 项 | 结果 |
| --- | --- |
| 编译 | `dotnet build ANX.slnx -m:1` → **0 警告 0 错误**（8 个工程） |
| 测试 | `dotnet test ANX.slnx -m:1` → **46/46 通过**（Core 16 + Protocol 28 + Communication 2） |
| CRC-8 | `Gr3Crc8.Table` 与现役 Delphi `UdpClientEx.pas` 的 `CRCTable` 字面量 **256/256 项逐项一致** |
| 帧布局 | 外层 / 内层结构与协议文档逐字节一致；A1 应答帧与文档 2.2 节举例的前 11 字节完全对上 |
| 通道 | UDP 回环 + 假设备：C0 联机 → A1 成功 → CAH 读设备 ID → A4 解析出 24 字节 ID 与设备类型 |
| **真机** | `tools\ANX.Tools.Gr3Probe` 对实体控制器（192.168.0.122:5001）**联机成功**：A0 自动推、CB 心跳回 A1 成功、CAH 读回设备 ID `1400C2009105032453235302`（类型 2 = 伺服版综合）、A3 通道配置 95 B；A2 以约 **190Hz** 推送，每个数据报打包 **5 帧**。真机帧已固化成 `Gr3LiveFrameTests` 回归用例 |

抓帧原始日志：[docs/抓帧/gr3-20261010-140424.log](docs/抓帧/gr3-20261010-140424.log)；冲突表 11 条**已全部闭环**，见 [GR-3-文档与实现冲突表](docs/对照/GR-3-文档与实现冲突表.md)。

**现场通道已确认是 UDP**（真机联机通过，本地端口 5001 绑定成功）。**M2 已知边界**：串口通道仍未做（现役串口用的是另一套 `CMD_SERIAL_*` 帧格式）；A0 / A2 / A3 上行**解析**归 M3（帧层已通）；产品级模拟器通道归 M3。

## M3 进行中：第 1 步 —— 上行解析（2026-10-10）

| 项 | 结果 |
| --- | --- |
| 编译 | 0 警告 0 错误 |
| 测试 | **50/50 通过**（Core 16 + Protocol 32 + Communication 2） |
| A0H 系统配置 | 数据域 56 B：版本 10/15/10；压力通道量程 `1000/100/300/0`、小数位 `2/2/3/0`、量纲 `1/0/3/0`；围压有效位 1 |
| A2H 实时数据 | 数据域 71 B：压力 `0/100/-1/0`（-1 = 未接通道）；机器状态 `0x12`；温度 / 端口 / 扩展引伸计均已解出 |
| A3H 通道配置 | 数据域 95 B：通道 1 → `Range=1000`、`Digital=2`、`Unit=1`(kN)，标定实值 `9900→100000`、码值 `100→10000` |
| 换算口径 | 由标定表**反推**出 `工程量 = raw / 10^Digital`（`Digital=2` 时 `9900→99.00`、`100000→1000.00`，正好等于满量程 1000 kN），已写进 `Gr3ChannelConfig.ToEngineeringValue` |

**状态位定义已落地（2026-10-10）**：协议文档 2.3 节其实写了机器状态与试验状态的位定义 ——
机器状态 Bit0 压力过载 / Bit1 位移上限位 / Bit2 位移下限位 / Bit3 电机正转（上升）运行 / Bit4 电机反转（下降）运行 / Bit5 控温；
试验状态 Bit0 试验运行中 / Bit1 试验暂停中 / Bit2 破碎点已找到 / Bit3 稳压（保持）中。
已包成 `Gr3MachineStatus` / `Gr3TestStatus` 两个 flags 枚举，并用单元测试逐位锁住（防止以后"顺手改位序"）。
于是真机抓到的 `0x12` 可以读作 **「位移上限位有效 + 电机反转（下降）运行中」**。

**仍按原始值保留的**：`PortOutputBStatus` / `PortInputAStatus` / `PortInputBStatus` —— 这三个端口的位定义文档里没有，等需要用时再确认。

**M3 剩余步骤**：② 判定算法（先按规范自己写、现役当标准答案比对）→ ③ 每方法三表 + EF Core → ④ 标签制报表（中英两套，FlexCel）→ ⑤ 试验执行页与四个页面（照现役布局）→ ⑥ 模拟器跑完整轮次并出报告。
