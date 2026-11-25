# SDS.Diag 接口

> 定位：本文只登记本模块的消费面与调用事实；签名与成员语义的唯一权威是 `SDS.Diag_API.cs`。规则见《Rules.zh-CN.md》，历史取舍见《DesignDecisions.zh-CN.md》。
> 源码路径相对 `超维存储/Source/`，行号以当前源码为准。

## 一、公共契约（A 节）

### 1.1 `SDS_Diag` 公共方法

签名与成员语义的唯一权威：`Source/SDS.Diag/SDS_Diag_API.cs`（XML 注释）。下表合并「允许调用方」与「实现口径」。

| 方法 | 允许调用方 | 键 / 节流与语义 | 落点 | 来源 |
|---|---|---|---|---|
| `ErrorOnce` | 所有层（含 ThirdModCompat） | key = 消息 + **真实调用点** + 异常类型全名；同一组合只完整记录一次，第 10 / 100 / 1000 次补一条 Warning | `EmitError` | 30–55 行 |
| `Warn` | 所有层（含 ThirdModCompat） | 可自愈异常情况；按 reason 进程内只一次（`HashSet`），不做时间节流 | `EmitWarning` | 68–77 行 |
| `Fail` | Core / Rules / Vanilla；ThirdModCompat 不得调用 | 行为回退（Error 级）；按 reason，600 tick 内一条；第 10 / 100 / 1000 次补记 | `EmitError` | 57–66、139–164 行 |
| `Ctx` | 所有层，只在失败分支调用 | 输出 `map= pawn= thing= job= extra` | — | 80–114 行 |
| `AssertAvailableWithinTotal` | Core / Vanilla；`[Conditional("DEBUG")]` | 只记 Error，不抛异常；实现见 internal `SDS_Invariants` | `SDS_Invariants` | 32 行 |
| `AssertTransactionStage` | 同上 | 事务阶段断言 | `SDS_Invariants` | 48 行 |
| `AssertTransactionTerminal` | 同上 | 事务终态断言 | `SDS_Invariants` | 65 行 |
| `EmitError` / `EmitWarning` / `EmitInfo` | `internal` | `try` 吞掉写入异常，`Log.*` 放在 `NoInlining` 内核方法 | `Verse.Log` | 189–223 行 |

没有 `SDS_Diag.Message`；普通信息直接 `Log.Message`。E2 的实际实现在 internal `SDS_Invariants`。

### 1.2 `ISDSResettable` / `SDS_StaticState`

签名与成员语义的唯一权威：`SDS.Diag_API.cs`。`Register` 忽略空值与重复实例；`ResetAll` 逐项 try/catch 并返回清理成功项数。

`ResetAll` 语义（`SDS_StaticState.cs` 47–68）：

| 点 | 行为 |
|---|---|
| 快照 | 先 `items.ToArray()`，清理过程中增删不影响本轮 |
| 单项异常 | 记 `SDS_Diag.ErrorOnce("运行期静态状态清理失败（已跳过）", e)`，继续下一项 |
| 返回值 | 实际 `ResetStatics` 未抛异常的项数 |
| 登记表 | 不清空；重新登记只发生在下一次启动 |
| 日志 | 每次输出 `[超维存储][状态] 登记 N / 清理 M / 失败 K` |

### 1.3 `SDS_SelfCheck` 公共方法

签名唯一权威：`SDS.Diag_API.cs`。

| 方法 | 语义 / 顺序 |
|---|---|
| `Count` | 已登记 provider 数 |
| `Register` | 空值与重复实例忽略 |
| `Reset` | 清空 provider 表（当前仅测试使用） |
| `RunLedgerChecks` | 开发者菜单入口，等价 `Run(true)` |
| `Run` | 先输出 E2 状态行；失败行始终 Error；统计行仅在 `verbose` 或无失败时输出；其余行仅 `verbose` 输出 |
| `RunLoadReport` | 只输出 provider 标记为 `[载入]` 的行；实现会先 `Collect()` 全部 provider，`[载入]` 过滤只影响输出、不减少检查计算 |
| `Collect`（internal） | 逐个调用 provider，单个异常记 `ErrorOnce` 后继续；自 2026-11-14 起不进入公开面 |

provider 行约定（`SDS_SelfCheck.cs` 7–11）：

| 前缀 | 语义 | 输出级别 |
|---|---|---|
| `[失败]` | 检查失败 | `EmitError` |
| `[统计]` | 统计行 | `EmitInfo`；`verbose=false` 且无失败时省略 |
| `[载入]` | 进入存档报数 | 仅 `RunLoadReport` 输出，去掉前缀后 `EmitInfo` |
| 其它 | 普通信息 | `EmitInfo`；仅 `verbose` 输出 |

### 1.4 报告中心与 DTO

签名、字段与语义的唯一权威：`Source/SDS.Diag/SDS_Diag_API.cs`。

约束：DTO 只含 `string` / `int` / `bool` / `List<string>`；`Register` 值拷贝；`Snapshot` 深拷贝，调用方改副本不影响 hub。`SDS_CompatPatchReport.Missing(hook, reason)` 记「`hook → reason`」。

## 二、调用方（B 节，grep 核实）

消费的 Contracts：`ISDS_DiagnosticProvider`（定义）、`SDS_StockKey` / `SDS_WithdrawEndState`（断言参数）。

统计口径：`超维存储/Source/**/*.cs`，排除 `obj` / `bin`、`SDS.Diag` 自身与测试；只计非注释行。下表计数为 2026-11-15 的 grep 快照（C1 回收账本迁入 Vanilla 后重测；P3–P5 流程层 / 判定层文件已迁至 `SDS.Rules`，Core / Rules 的分布见下方说明），权威值以脚本输出为准。

| 程序集 | 调用内容 | 调用行数 | 代表位置 |
|---|---|---|---|
| Contracts | 定义 `ISDS_DiagnosticProvider`；不引用 Diag | 定义 1 | `SDS_Host.cs:119` |
| Core | 账本侧诊断（`Fail` / `Warn` / `ErrorOnce` / `Ctx`）；`StaticState.Register` 0；`SelfCheck.Register` 1 | 账本自检 | `SDS_Core.cs`、`SDS_Ledger.cs` |
| Rules | 判定层 / 流程层诊断（`Fail` / `Warn` / `ErrorOnce` / `Ctx`）；`StaticState.Register` 4；`SelfCheck.Register` 1 | 待重测 | `SDS_WithdrawTransaction.cs`、`SDS_WithdrawExecutor.cs`、`SDS_ReturnUtility.cs`、`SDS_ProducedThingMarker.cs`、`SDS_StockRules.cs`、`SDS_Availability.cs` |
| Vanilla | `Fail` 49、`Warn` 18、`ErrorOnce` 130、`Ctx` 65；`StaticState.Register` 14；`ResetAll` 1；`SelfCheck.Register` 1；`SelfCheck.Run` 2；`RunLoadReport` 1 | 281 | `SDS_Vanilla.cs:44–60`、`SDS_GameComponent_Grid.cs`；`SDS_ReclaimRegistry.cs`、`SDS_Mirrors.cs`、`SDS_Buildings.cs` |
| Api | `ErrorOnce` 25；不调用 `Fail` / `Warn` / 断言 | 25 | `SDS_API.cs`、`SDS_API_Adapters.cs` |
| ThirdModCompat | `ErrorOnce` 60；`CompatReportHub.Register` 26；`StaticState.Register` 2；实现 `ISDSResettable` 2 | 90 | `SDS_ModCompat_CE.cs`、`SDS_CompatAdapter_CE.cs:52`、`SDS_CompatAdapter_MiliraExpanded.cs:52` |
| Bootstrap | `ErrorOnce` 8；`SelfCheck.Run(false)` 1；`SelfCheck.Count` 2；`StaticState.RegisteredCount` 2；`CompatReportHub.LogAll` 1、`Has` 1、`Register` 1 | 16 | `SDS_Startup.cs:64,103,106,111–112`、`SDS_CompatLoader.cs:166,170` |

补充事实：

- `SDS_SelfCheck.Collect`（internal）只被同文件的 `Run` / `RunLoadReport` 调用；`RunLedgerChecks` 只作为开发者菜单入口；`Reset` 仅测试调用。
- `SDS_CompatReportHub.Snapshot` 当前只有测试调用；生产读侧是 Bootstrap 的 `LogAll`。
- `ISDSResettable` 的实现：Core 的 `SDS_ResetAction`、Vanilla 的私有 `ResetAction`、CE / Milira 两个适配器。
- `ISDS_DiagnosticProvider` 的实现：Core `SDS_CoreDiagnostics`、Vanilla `SDS_VanillaDiagnostics`。
- `SDS_SelfCheck.Register` 只在 `SDS_Core.RegisterAll` 与 `SDS_Vanilla.RegisterAll` 各一次。

## 三、调用关系

```text
Bootstrap ──(读计数 / LogAll / Run(false))──→ SDS.Diag
    │
Core ──(Fail / Warn / ErrorOnce / Ctx / Assert* / Register)──→ SDS.Diag
    │
Vanilla ──(Fail / Warn / ErrorOnce / Ctx / Register / ResetAll / Run)──→ SDS.Diag
    │
Api ──(ErrorOnce)──→ SDS.Diag
    │
ThirdModCompat ──(ErrorOnce / ReportHub.Register / StaticState.Register)──→ SDS.Diag
Contracts ──(定义 ISDS_DiagnosticProvider)──→ 由 SDS_SelfCheck 消费
```

启动期注册顺序：Bootstrap 第 5 步调用 `SDS_Core.RegisterAll()` → `SDS_Vanilla.RegisterAll()`，随后读取 `SDS_StaticState.RegisteredCount` 与 `SDS_SelfCheck.Count`；第 7 步调 `SDS_CompatReportHub.LogAll()` → `SDS_SelfCheck.Run(false)`。读档期顺序：`SDS_GameComponent_Grid` 构造 → `ResetAll`；`FinalizeInit` → `RunLoadReport`（开发者模式再 `Run(false)`）。

启动期没有 `SDS_GameComponent_Grid`：`SDS_VanillaDiagnostics` 仍先跑不依赖地图的 W63，再因 grid 缺失跳过镜像 / 金库 / 载入报数；Core 的 W35 与 E2 状态照常输出。

无反向调用：Diag 的程序集引用只有 Contracts，不引用任何 `SDS.*` 实现。

## 四、原版支持

> 源码路径相对 `超维存储/Source/`。规则与签名见《Rules.zh-CN.md》《ModuleInterfaces.zh-CN.md》，本节只记交互事实。

### 4.1 与原版 API 的交互面

| 原版 API | 用途 | 位置 | 生效条件 |
|---|---|---|---|
| `Verse.Log.Error` | `ErrorOnce` / `Fail` 的落点 | `SDS_Diag.EmitError` → `EmitErrorCore` | 总是（写入失败被吞） |
| `Verse.Log.Warning` | `Warn`、重复错误里程碑、自检失败汇总 | `SDS_Diag.EmitWarning` | 总是 |
| `Verse.Log.Message` | 自检、载入报数、静态清理状态行 | `SDS_Diag.EmitInfo` | 总是 |
| `Prefs.DevMode` | 断言运行时开关 | `SDS.Diag/SDS_Invariants.cs` 三个实现 | DEBUG 构建内 |
| `Prefs.DevMode` | 读档后跑一次完整只读自检 | `SDS.Vanilla/SDS_GameComponent_Grid.cs` 66–79 | 进入存档且地图已生成 |
| `[Conditional("DEBUG")]` | 剥离断言调用点 | `SDS_Diag.Assert*`、`SDS_Invariants.*` | Release 编译 |
| `GenTicks.TicksGame` | `Fail` 的 600 tick 节流时钟 | `SDS_Diag.ShouldLogFail` | 总是 |
| `LudeonTK.DebugAction` | 开发者菜单手动自检入口 | `SDS_SelfCheck.RunLedgerChecks` | 开发者模式 |
| `GameComponent` 构造函数 | 触发 `SDS_StaticState.ResetAll` | `SDS.Vanilla/SDS_GameComponent_Grid.cs` 24–30 行 | 新档 / 读档 |
| `GameComponent.FinalizeInit` | 进入存档后一次性报数 | 同文件 41–47 行 | 进入存档 |

Contracts 不引用 Diag，因此 Contracts 侧直接用原版 `Log.ErrorOnce(string, int 固定 key)`：宿主 11 个 null 检查（`SDS.Contracts/SDS_Host.cs` 179–189）、JobDef 缺失（`SDS.Contracts/SDS_CaravanTransport.cs` 20–48）、HediffDef 缺失（`SDS.Contracts/SDS_Implants.cs` 19–33）、保真探针异常（`SDS.Contracts/SDS_PreserveHooks.cs` 51）。

### 4.2 `[Conditional("DEBUG")]` 与开发者模式

- 条件特性同时标在 `SDS_Diag` 三个公共断言入口与 internal `SDS_Invariants` 三个实现上；Release 时调用点整条语句（含参数求值）被移除，不是「实现体为空」（`SDS.Diag/SDS_Diag.cs` 74–93；`SDS_Invariants.cs` 32 / 48 / 65）。公共入口保证调用方剥离，实现侧保证内部单独调用同样为空。
- 三个断言实现开头检查 `Prefs.DevMode`，关闭时直接返回，不拼消息串（`SDS_Invariants.cs` 32–78）。
- `SDS_SelfCheck.Run` 无条件先输出 E2 状态行；`SDS_Invariants.StatusText` 由 `#if DEBUG` + `Prefs.DevMode` 给出三态：生效 / DEBUG 但未开开发者模式 / Release 未编译（走 `#else` 分支）。
- 与旧实现的行为差异：旧 `ResetAll` 只在开发者模式打清理行，新实现每次 `ResetAll` 都输出「登记 N / 清理 M / 失败 K」，便于玩家报告里核对清理项数（`SDS.Diag/SDS_StaticState.cs` 66）。

### 4.3 GameComponent 时机

- 原版 `Game.FillComponents` 用 `Activator.CreateInstance(type, this)` 构造组件，因此构造函数必须保留 `(Game)` 形参（`SDS.Vanilla/SDS_GameComponent_Grid.cs` 20–22）。
- 构造顺序固定：`ResetAll` → `SDS_Core.CreateLedger()` → `new SDS_VanillaState()` → `Instance = this` → `SDS_Core.Attach`（同文件构造函数）。
- 时机约束：构造时 `Find.Maps` 可能为空，`ResetAll` 只能清进程级静态缓存，不能依赖地图或存档；`ExposeData` 只序列化内容，不兜底重建账本（同文件 32–39）。
- 报数延后：`FinalizeInit` 只置 pending，等首个非空 tick 才真正输出（同文件 41–79）。
- `Fail` 的 `GenTicks.TicksGame` 计时在换到更旧存档时会回退，换档前必须先清静态缓存，见《Rules.zh-CN.md》二；`ResetAll` 只清静态缓存，不清当前存档的 `SDS_Ledger` / `SDS_VanillaState`（《ModuleInterfaces.zh-CN.md》二·2.2）。

### 4.4 Harmony 与补丁计数的归属

Diag 不安装补丁、不引用 Harmony（D2）；补丁计数由 Vanilla `SDS_PatchInstaller`、镜像守卫 / 投影 owner 桥注册表、9 个适配器各自提供，Diag 只提供 `SDS_CompatPatchReport` 载体与 `SDS_CompatReportHub.LogAll` 出口，不统计补丁数，也不创建 Harmony 实例。

### 4.5 待补

- 运行期 `DeclaredCount` 是否等于《ModuleResponsibilities.zh-CN.md》六的 112（需实机启动报告核对）。
- 开发者菜单条目在非开发者模式下的可见性（需实机确认）。

## 五、第三方支持

> 规则与签名见《Rules.zh-CN.md》六·6.6、八，《ModuleInterfaces.zh-CN.md》二、四、八，本节不复述。

### 5.1 Diag 对第三方暴露的契约面

| 类型 | 角色 | 适配器可做 / 不可做 |
|---|---|---|
| `SDS_CompatPatchReport` | 单个适配器的挂点 A/B 记录 | 可 `Applied` / `Missing`；不负责输出 |
| `SDS_CompatAdapterReport` | 单个适配器的只读快照（值拷贝） | 只读；不持有内部列表 |
| `SDS_CompatReportHub` | 唯一登记与输出出口 | 可 `Register` / `Has`；`LogAll` 由 Bootstrap 调 |
| `SDS_StaticState` / `ISDSResettable` | 运行期静态缓存清理登记 | 可 `Register` |
| `SDS_Diag.ErrorOnce` / `Warn` | 异常与一次性自愈 | 可调用 |
| `SDS_Diag.Fail` | 数据一致性 Error | 禁止调用 |

### 5.2 适配器如何把结果写进报告中心

以 Combat Extended 为例（`SDS.ThirdModCompat/SDS_ModCompat_CE.cs` 130–208）：

1. 被调用但关键类型解析不完整：`Register(name, packageId, detected, null, null, reason)`，即 `report = null` + `detectedButBroken`，启动报告输出 `A/B = 0/0` 并给出原因。目标 Mod 未装时 `Apply` 根本不会被调用，因而不登记、完全静默（`SDS.Bootstrap/SDS_CompatLoader.cs` 138–142）。
2. 目标已装：新建 `SDS_CompatPatchReport(适配器名)`，逐挂点经 `SDS_CompatTools.TryPatch` 记 `Applied(挂点名)` 或 `Missing(挂点名, 原因)`（`SDS.ThirdModCompat/SDS_CompatServices.cs` 84–124）。
3. 收尾：`SDS_CompatReportHub.Register(name, packageId, true, report, remark)`。
4. 整体异常：catch 内再 `Register(..., null, ..., "应用异常 XxxException")` 并 `SDS_Diag.ErrorOnce`（同文件 203–208）。

输出只由 `LogAll` 决定：未检测到的适配器完全静默；已检测到输出一行 A/B（`【category】name`）。`declaredCount == 0` 再分两种：`report == null` → Warning + `detectedButBroken` / 「关键成员缺失」；`report != null` 但 0 挂点 → 中性 Message（空报告不等于破损）。有失效挂点时追加一条明细 Warning，带 `【category】name`、失败名单与已生效名单（`SDS.Diag/SDS_CompatReports.cs`）。

适配器若在 `Apply` 中直接抛异常、没来得及登记，Bootstrap 的 `RegisterBrokenReport` 会补一条 0/0 记录，避免启动报告漏项（`SDS.Bootstrap/SDS_CompatLoader.cs` 155–171）。

### 5.3 为什么 Bootstrap 只从 Diag 读 A/B

- 依据：《ModuleRules.zh-CN.md》六·6.7 禁止 Bootstrap 静态引用 ThirdModCompat，靠反射扫描；《ModuleResponsibilities.zh-CN.md》九 规定适配器 A/B 结果从 Diag 读。
- 实现：`SDS_CompatLoader` 反射枚举 `ISDSCompatAdapter` 实现，校验名称唯一（重复记 `ErrorOnce` 并跳过后注册者），目标已加载才 `Apply`；报告由适配器自己写进 Hub。
- Bootstrap 只做两件事：读自己的扫描计数（`AppliedCount` / `KnownCount`）与调用 `SDS_CompatReportHub.LogAll()`（`SDS.Bootstrap/SDS_Startup.cs` 101–114）。其编译引用里没有 `SDS.ThirdModCompat`；全部适配器未装时启动路径不变，Diag 是唯一数据通道。

### 5.4 适配器重置静态缓存时如何登记

| 适配器 | 登记项数 | 登记时机 | 清理内容 |
|---|---:|---|---|
| `SDS_CompatAdapter_CE` | 1 | `Apply` 成功后 `SDS_StaticState.Register(this)` | 转发 `SDS_ModCompat_CE.Reset()` |
| `SDS_CompatAdapter_MiliraExpanded` | 1 | 同上 | 转发 `SDS_ModCompat_MiliraExpanded.Reset()` |
| 其余 6 个适配器 | 0 | — | 无跨档静态缓存（待补：逐个复核） |

- 两个适配器类直接实现 `ISDSResettable`，`ResetStatics` 只做转发（`SDS.ThirdModCompat/SDS_CompatAdapter_CE.cs` 9–58、`SDS_CompatAdapter_MiliraExpanded.cs` 9–58）；测试 `SDS.Tests/Compat/CompatAdapterTests` 断言两者可赋值给 `ISDSResettable`。
- 登记发生在 `Apply` 成功之后，不是类构造时；目标未装就不 `Apply`，也不登记，所以登记总数会随实际启用的适配器变化（`SDS.Bootstrap/SDS_CompatLoader.cs` 120–160）。
- 登记的是进程级短时缓存（按 ID / 绝对 tick 的换弹冷却与补料节流），不是当前存档的账本实例；条目归属见《ModuleResponsibilities.zh-CN.md》四。

### 5.5 `Fail` 禁用面

- 语义差别：`Fail` 是数据一致性 / 执行期异常级，带 600 tick 节流并进 Error 通道。第三方挂点缺失、目标类型改名、签名不匹配只是「没有接管」，不属于本 Mod 的数据错误；写 Error 会污染真正的故障定位。
- 替代写法：
  - 挂点缺失 / 签名不匹配 / 应用异常 → `SDS_CompatPatchReport.Missing`，最终汇总成一条 Warning；
  - 适配器运行期异常 → `SDS_Diag.ErrorOnce`（同一调用点 + 异常类型只完整记一次）；
  - 可自愈的一次性情况 → `SDS_Diag.Warn`（每 reason 进程级一次）。
- 校验：见《Rules.zh-CN.md》D4 与 §三 检查命令（`DiagCallerTests.Fail_OnlyCalledFromImplementationModules`、`CompatAdapterTests.CompatSources_DoNotCallDiagFail`）。
- 可见性后果：第三方只能通过 A/B 行与逐挂点 Warning 表达「未生效」，不能把「无法接管」升级成 Error。目标 Mod 未装时保持静默，装了但探测不到必须 Warning。

### 5.6 线程与生命周期

- 报告只在主线程写：`Apply` 由 Bootstrap 主线程调用；Hub 内部 `List` 无锁，写入侧靠这一约束保证安全。
- 读侧不持有内部列表：生产路径 Bootstrap 直接调 `LogAll()` 就地输出；需要数据时用 `Snapshot()` 深拷贝。
- `SDS_CompatReportHub.Reset()` 当前只在测试中调用；`Apply` 由适配器类的静态 `applied` 保证进程内幂等，报告不会重复累加（`SDS.ThirdModCompat/SDS_CompatAdapter_CE.cs` 16、44–53；`SDS.Tests/Diag/CompatReportTests.cs` 38）。
- 适配器的 `applied` 是静态 bool，跨存档不复位，因此适配器不参与「换档重新 Apply」，只参与静态缓存清理（待补：是否需要随读档复位）。

### 5.7 待补

- 其余 6 个适配器是否确实没有跨档静态缓存（逐个复核）。
- 各适配器 `remark` 文案的统一口径。
