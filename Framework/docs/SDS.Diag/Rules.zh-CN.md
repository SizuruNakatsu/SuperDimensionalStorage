# SDS.Diag 规则

> 定位：把《ModuleRules.zh-CN.md》六·6.2 与相关硬约束落到本模块的可检查条目。每条给出源码锚点与检查方式；没有机器校验的明确写「人工」。
> 规则正文的权威仍是《ModuleRules.zh-CN.md》；本文只做本模块的展开与核对。

## 一、规则清单

| # | 规则 | 源码锚点 | 检查方式 | 结果 |
|---|---|---|---|---|
| D1 | 禁止引用 Core / Vanilla / ThirdModCompat 的实现 | `SDS.Diag.csproj` 仅 `ProjectReference SDS.Contracts` | `Tools/CheckAssemblyBoundaries.ps1` 第 39–45 行允许引用表；越界报 `[越界引用]` | 通过 |
| D2 | 禁止 Harmony | `SDS.Diag.csproj` 无 `0Harmony`；`Directory.Build.props` 未开 `SdsHarmonyRef` | 同一脚本的 Harmony 例外表；`SDS.Tests/Diag/CompatReportTests.DiagAssembly_DoesNotReferenceHarmony` | 通过 |
| D3 | 禁止定义玩法规则 | 无品类 / 容量 / 扣账 / 派活分支；断言只读参数 | 人工审阅（无脚本；规则正文见《ModuleRules.zh-CN.md》六·6.2） | 人工 |
| D4 | `SDS_Diag.Fail` 只允许 Core / Rules / Vanilla 调用 | `SDS_Diag.cs:55`；Api / ThirdModCompat / Bootstrap 均为 0 调用 | `SDS.Tests/Diag/DiagCallerTests.Fail_OnlyCalledFromImplementationModules`；`SDS.Tests/Compat/CompatAdapterTests.CompatSources_DoNotCallDiagFail` | 通过 |
| D5 | 断言在 Release 整条剥离 | `SDS_Diag.cs:75,81,88` 与 `SDS_Invariants.cs:32,48,65` 均标 `[Conditional("DEBUG")]` | 编译期特性（无脚本）；`InvariantTests` 验证 DEBUG 下不抛异常、`StatusText` 含 E2 | 部分机器 |
| D6 | 断言只记 Error、不抛异常；且仅在开发者模式生效 | `SDS_Invariants.cs:33–45,49–62,66–78` | `SDS.Core.Tests/Core/InvariantTests.Invariants_IllegalStates_DoNotThrow` | 通过 |
| D7 | `ResetAll` 单项失败继续且返回清理成功数 | `SDS_StaticState.cs:47–68` | `SDS.Tests/Diag/StaticStateTests.ResetAll_CountsSuccesses_AndContinuesAfterException` | 通过 |
| D8 | `Register` 忽略 null 与重复实例 | `SDS_StaticState.cs:27–41` | `StaticStateTests.Register_IgnoresNullAndDuplicates` | 通过 |
| D9 | 注册点与项数：Rules 4 / Vanilla 13 / CE 与 Milira 各 1 | `SDS_RulesImpl.cs` 的 `RegisterAll`；`SDS_Vanilla.cs:40–52`；两个适配器 `Apply` 成功后 `Register(this)` | 源码 grep 计数；`VanillaContractTests.Vanilla_RegisterAll_IsIdempotent`；`CompatAdapterTests.CeAndMilira_DeclareStaticCleanup` | 通过 |
| D10 | 报告只能主线程写且 Bootstrap 读快照 | Hub 内部 `List` 无锁；写侧只有适配器 `Apply`；Bootstrap 主线程直接 `LogAll`（需取数时用 `Snapshot` 深拷贝） | 人工（线程约束）；`CompatReportTests.Hub_SnapshotIsDeepCopy` 验证深拷贝 | 人工 + 测试 |
| D11 | 报告 DTO 不含 Harmony 类型 | `SDS_CompatReports.cs` 的 DTO 只含基础类型 | `CompatReportTests.ReportDtos_OnlyPrimitiveAndStringListMembers` | 通过 |
| D12 | 没有 `SDS_Diag.Message` | `SDS_Diag.cs` 无 `public static void Message` | `DiagCallerTests.Diag_HasNoMessageFacade` | 通过 |
| D13 | 顶层 public 仅白名单 7 个类型 | `SDS_Diag`、`SDS_StaticState`、`ISDSResettable`、`SDS_SelfCheck`、`SDS_CompatReportHub`、两个 DTO | `CheckAssemblyBoundaries.ps1` 第 94–97 行 public 白名单，越界报 `[public泄漏]` | 通过 |

## 二、为什么不注册 Diag 自己的状态

`SDS_Diag` 的 `seen` / `fails` / `warned` 是进程级诊断缓存，不在 `SDS_StaticState` 的登记表内，也没有对外 `Reset`。这与 E1「跨存档不应保留的静态缓存必须登记」存在口径张力：

| 点 | 事实 | 来源 |
|---|---|---|
| 去重表 | `seen` / `fails` / `warned` 只增不减 | `SDS_Diag.cs:24–26` |
| 换档影响 | `fails[reason].nextTick` 基于 `GenTicks.TicksGame`；换到更旧存档时 tick 回退，可能长时间静默 | `SDS_Diag.cs:139–151`；旧 `SDS_StaticState.cs:34–39` 注释 |
| 当前登记 | 无 `ISDSResettable` 实现、无 `Reset` | `SDS_StaticState.cs`、`SDS_Diag.cs` |

是否需要把这三张表纳入 `ResetAll`，属于待定项；本文只登记现状，不把未决项写成规则（待补）。

## 三、检查命令

| 目的 | 命令 / 测试 |
|---|---|
| 引用图、Harmony、public 白名单 | `pwsh 超维存储/Source/Tools/CheckAssemblyBoundaries.ps1` |
| `Fail` 调用方 | `SDS.Tests/Diag/DiagCallerTests`、`SDS.Tests/Compat/CompatAdapterTests` |
| 静态清理 | `SDS.Tests/Diag/StaticStateTests` |
| 报告 DTO / 快照 / Harmony | `SDS.Tests/Diag/CompatReportTests` |
| E2 断言 | `SDS.Core.Tests/Core/InvariantTests` |

## 四、已知缺口

| 缺口 | 说明 |
|---|---|
| `CheckCompatSurface.ps1` 未实际扫描 `SDS_Diag.Fail` | 《ModuleRules.zh-CN.md》十把它列为兼容面检查项，但脚本中没有该符号规则；当前由两个测试兜底（待补脚本） |
| Release 剥离无构建级校验 | 目前靠 `[Conditional]` 特性与人工确认，没有脚本或 IL 检查（待补） |
| 注册项数无脚本对账 | Rules 4 / Vanilla 13 / 适配器 2 由源码计数与测试间接保证，没有独立对账脚本（待补） |
| 主线程写入无运行时断言 | 依赖《ModuleResponsibilities.zh-CN.md》四的调用约定，Hub 不加锁也不检测线程（待补） |

## 五、规则正文（权威）

> 以下只保留上文 D1–D13 尚未覆盖、且与本模块直接相关的规范句；完整正文与机器校验列见《RuleIndex.zh-CN.md》，D1–D13 是这些规范句的检查落点。

| ID | 规范句 | 与本模块的关系 | 当前锚点 |
|---|---|---|---|
| E6 | 兼容层只允许引用 `SDS_API`、`SDS_Compat*`、`SDS_Diag` 与公共 API 类型 | 第三方只能经 Diag 写报告 / 记日志 | `SDS_CompatReports.cs` |
| E7 | 每个第三方入口只有一个写接管路径 | 报告中心不参与补丁逻辑，只汇总 | `SDS_CompatReportHub` |
| R15 / W22 | 交易 / 捐献读「物理存量 − 事务预留」；可用量 = `max(0, 物理 − 预留)` | 自检与断言校验的可用量口径 | `SDS_Diag.AssertAvailableWithinTotal` |
| W21 | 瞬时事务执行期足额口径由入口固定 | 事务阶段断言 | `AssertTransactionStage` / `AssertTransactionTerminal` |
