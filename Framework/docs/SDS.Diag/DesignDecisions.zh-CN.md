# SDS.Diag · 设计决策（为什么这么做 / 踩过的坑）

> 定位：记录本模块「为什么这么做」、历史决策与迁移踩过的坑；新代码以 `超维存储/Source/SDS.Diag` 为准。规则与检查方式见《Rules.zh-CN.md》，签名与调用点见《Interfaces.zh-CN.md》，诊断口径的唯一权威是《DiagnosticsCoverage.zh-CN.md》。

## 一、诊断口径的历史决策与坑

《DiagnosticsCoverage.zh-CN.md》是诊断口径的唯一权威，方法如下：

| 项 | 内容 | 来源 |
|---|---|---|
| 判定标准 | 真故障日志至少含：模块 + reason 码、运行时上下文（map / pawn / thing / job / reservation）、结果语义（放弃 / 已回滚 / 异常）、异常堆栈；同一 reason 节流 | §零 |
| 计数口径 | `超维存储/Source/**/*.cs`，排除 `obj` / `bin`，只计非注释行；`SDS_Diag.*` 只计调用点、不计定义；由 `CheckPatchInventory.ps1` §一 对账 | §一 注 |
| 分级 | Error = 数据一致性 / 执行异常；Warning = 一次性自愈；不输出 = 正常工作与回退 | §一「日志级别约定」 |
| 分类方式 | 按入口分 A–M 组（启动 / 补丁、镜像、行为、取货、交易、装载、投影、API、兼容、静态状态等），每行标「有诊断 / 有意静默」并写具体 reason | §二 |
| 静默原则 | 闸门拒绝、决议让出、无库存、不可达、背包满、无端点地图、纯显示 catch 一律不写日志 | §五 |
| 结论 | 日志面收敛为「异常 + 一次性自愈 + 一次性统计」；「为什么没接管」的逐条原因留作默认关闭的调试开关，未实现 | §五、§七 |

本模块只承接「出口」与「静态状态 / 断言」两段；各入口的具体 reason 码仍归各模块。

## 二、旧实现 → 新实现的差异与坑

| # | 决策 / 坑 | 结论与来源 |
|---|---|---|
| 1 | `ErrorOnce` 自建 key，不直接调用 `Verse.Log.ErrorOnce` | 旧实现注释明确：key 必须包含「消息 + 调用点 + 异常类型」，避免不同位置的同类错误被一并静默；同时需要第 10 / 100 / 1000 次补记（`SDS_Mod.cs:932–935, 943–949`）。公共门面与 Impl 入口必须 `NoInlining`，跳帧按「门面 + Impl」两层计；新增代理层必须同步复核（`DiagCallerTests.ErrorOnce_KeyUsesRealCallSite`）。与 `Verse.Log.ErrorOnce(string, int)` 在「永久静默 / 仅整数 key」上的逐行差异（待补） |
| 2 | 没有 `SDS_Diag.Message` | 普通信息不需要去重、节流与上下文；门面化会诱导把正常工作写进日志（`SDS_Mod.cs:984–986`、`DiagnosticsCoverage.zh-CN.md` §一「为什么取消普通工作日志」） |
| 3 | `ResetAll` 返回 `int` 而不是 `bool` | 启动日志要输出「登记 N / 清理 M / 失败 K」，`bool` 无法表达部分成功（旧 `SDS_StaticState.cs:18–51`；新 `SDS_StaticState.cs:41–62`） |
| 4 | 断言签名只用 Contracts 基础类型 | Diag 看不到 Core 的 `SDS_WithdrawTransaction`；压成 `id + 阶段枚举` 后 Core 可调用且不产生反向引用，代价是只能报 id（`SDS_Invariants.cs:39–67`） |
| 5 | `[Conditional("DEBUG")]` 标两处 | 只标 internal 实现，Release 仍保留公共调用与参数求值；公共门面与实现都标，调用点才整条剥离（`SDS.Diag_API.cs:44–58`、`SDS_Invariants.cs:32,48,65`） |
| 6 | `Emit*` 吞掉写入异常，`Log.*` 放 `NoInlining` 内核 | 无宿主（单测 / 早期加载）时日志不可用；JIT 解析 `Assembly-CSharp` 失败也必须落在 `try` 内，不能让诊断反过来中断业务（`SDS_Diag.cs:185–223`） |
| 7 | 报告在 `Register` 时值拷贝 | 旧 Hub 持有适配器报告引用，拆开后 DTO 必须 `public` 且 Bootstrap 不能持有活对象；读侧再 `Snapshot` 深拷贝（`SDS_CompatReports.cs:50–82`） |
| 8 | 登记表不随 `ResetAll` 清空 | 重新登记只发生在下一次启动；`ResetAll` 只清进程级静态缓存，不清 `SDS_Ledger` / `SDS_VanillaState` 实例（`SDS_StaticState.cs:37–40`；《ModuleInterfaces.zh-CN.md》二·2.2） |
| 9 | 旧 18 项 → 新动态项数 | 旧 `ResetAll` 直接调用 18 个静态类；当前由 Rules 4 + Vanilla 13 + 已启用适配器 2（CE / Milira）构成，启动「登记 N」会随实际启用变化（`SDS_StaticState.cs`；《ModuleResponsibilities.zh-CN.md》四） |
| 10 | 旧 `SDS_SelfCheck` 硬编码检查 | 拆开后 Diag 不允许引用 Core / Vanilla，检查只能下放为 provider；Diag 只保留收集、分级、落日志（§五） |
| 11 | `SDS_CompatReportHub.Reset()` 新实现仅测试调用（旧 `SDS_ModCompat.TryApply` 每次应用前会调用） | 新流程由 `SDS_CompatLoader` 的 `attempted` / `Has` 保证幂等，当前无需生产 Reset；是否需要随读档清空仍未定（`CompatReportTests.cs:38`）（待补） |
| 12 | `SDS_Diag` 自身缓存未登记 `ResetAll` | `seen` / `fails` / `warned` 无 `Reset`；换档 tick 回退时 `Fail` 的 `nextTick` 可能长期静默（`SDS_Diag.cs:26–28,116–137`）。是否接受或补登记（待补） |
| 13 | 旧自检的 `MaxReported = 20` | 新实现把截断下放到各 provider：Vanilla 仍为 20，Core / Rules 不设上限；Diag 不再统一截断（`SDS_SelfCheck.cs`；`SDS_VanillaDiagnostics.cs:15`；`SDS_Core.cs` / `SDS_RulesImpl.cs`） |

## 三、设计取舍

> 原「实现原因」文档并入本节。逐条「坑」与迁移代价见第二章，这里只保留独立成程序集的理由与文档分工。

| 能力 | 独立理由 | 证据 |
|---|---|---|
| 日志门面 | reason 码、去重 key、节流窗口与上下文格式只能有一份实现；复制到多个程序集会让同一故障出现两套格式与两套计数 | `SDS.Diag/SDS_Diag.cs`；《DiagnosticsCoverage.zh-CN.md》§四 |
| 开发期断言 | 断言要被各模块调用，又不能让 Diag 反向引用 Core；只有独立程序集能把「调用点剥离 + 实现唯一」同时做到 | `SDS.Diag/SDS_Invariants.cs`；E2（《RuleIndex.zh-CN.md》） |
| 自检入口 | 检查内容分散在 Core / Vanilla，出口必须唯一：失败 / 统计 / 载入三类前缀、失败汇总、单 provider 异常不中断 | `SDS.Diag/SDS_SelfCheck.cs`；《DiagnosticsCoverage.zh-CN.md》§M |
| 静态状态注册制 | 旧实现直接调用 Core / Vanilla / Compat 的 `Reset`，拆分后 Diag 不允许引用它们，只能由模块登记 `ISDSResettable` | 旧 `SDS_StaticState.cs` 22–45 行；新 `SDS.Diag/SDS_StaticState.cs` |
| 适配器报告中心 | 第三方适配器只允许写 Diag；报告类型又必须跨程序集可读，不能留 `internal` | 《ModuleResponsibilities.zh-CN.md》四；`SDS.Diag/SDS_CompatReports.cs` |
| 依赖图 | Diag 只引用 Contracts，其余模块单向指向 Diag；日志若留在各模块，只能复制实现或让模块互相引用，都会破坏单向依赖 | 《ModuleRules.zh-CN.md》四 |

`SDS.Diag.csproj` 只有一条项目引用 `SDS.Contracts`；未开启 `SdsHarmonyRef`（`Directory.Build.props` 15、35–38），因此不引用 `0Harmony`。

- 依赖方向、禁止项、`Fail` 调用方限制只在《ModuleRules.zh-CN.md》四、六·6.2 定义；本节只解释原因，不新增规则。
- 类型与签名只在《ModuleInterfaces.zh-CN.md》二·2.1、二·2.2 定义；断言与自检口径以《DiagnosticsCoverage.zh-CN.md》§M 为唯一权威，E1 / E2 的规范句在《RuleIndex.zh-CN.md》。
- 行号与测试名以当前源码为准，代码变动后同步，不另设第二份清单。

## 四、待补

待补：各 provider 检查项与《DiagnosticsCoverage.zh-CN.md》§M 的逐条映射。

其余零散未决项见第二章表格中标注「待补」的各行。
