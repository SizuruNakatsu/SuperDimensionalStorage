> 定位：保留 SDS.Api 重构中「为什么这么做」的历史决策、旧实现与新实现之间的差异、踩过的坑与设计取舍，供维护时一次读懂；旧实现只以文字描述留档，本目录不再依赖任何旧实现快照。规则 ID 与规范句见 [Rules.zh-CN.md](Rules.zh-CN.md) §四。

# SDS.Api · 设计决策（为什么这么做 / 踩过的坑）

## 一、历史决策与坑

### 1.1 `Version` 演进

旧 `SDS_API.cs` 的 `Version` 历史：1 → 2（删除无采用入口）→ 3（5 个公共辅助降为 internal）→ 新 4（程序集改名，五轴签名与语义不变）（`PublicAPI.zh-CN.md` §二·五、§3.8）。

### 1.2 旧文档结论（逐条转述）

| 旧文档章节 | 结论（转述） | 新落点 |
|---|---|---|
| §一.1 | 门面不是第二实现；每个方法必须落到唯一实现，不另写排序、筛选、容量、优先级、事务或扣账 | 《Rules.zh-CN.md》API-2、API-6 |
| §二 | 五轴职责与返回形态：看 = 只读 `Thing` 列表；读 = `SDS_ApiStockInfo`；找 = `Thing` + 失败原因；取 = 真实 `Thing`，交付由调用方完成；存 = 实际接收量 + 失败原因 | 《README.zh-CN.md》二；《Interfaces.zh-CN.md》一 |
| §3.1 | 薄门面：只调用一个对应服务；仅在「同一事务的必要步骤」内组合（如选端点后立即入账） | `TryDepositNow` 先 `TryFindDepositEndpoint` 再 `TryDepositAt` |
| §3.4 | 调用方上下文：会改变状态或涉及预留的入口只服务玩家阵营；存入要求同地图、端点阵营为空或等于调用方；所有权 / 禁止由门面补齐 | `ValidateDepositCall` / `ValidateWithdrawCall` |
| §3.5 | 事务后置条件：取成功 = 已扣账并物化、交付由调用方完成；存成功 = 实物已消费；失败原样留在调用方 | 《Interfaces.zh-CN.md》8.3；`TryReturn*` 桥 |
| §3.6 | 失败码：状态型返回 `SDS_ApiResultCode`；只读快照用 `result`；异常统一 `InternalError` / 空集 / `default` 并记一次 `ErrorOnce`；正常拒绝不写日志 | 私有 `Fail(...)` / `MapSupplyFailure` |
| §3.7 | 非 Pawn 模式是同一解析器的参数分支（`actor == null`、`requireReach = false`、`allowSelf = false`、按 `reference` 算距离），不是第二套 resolver | `SDS_EndpointQuery.DepositForNonPawn` |
| §3.8 | `Version` 是能力探测与兼容性声明；E5：Version 变更即兼容性声明 | 《Rules.zh-CN.md》API-7 |
| §二·六 | 适配器桥是同一 `SDS_API` 的 `partial`，不是第二个 API；旧版全为 `internal`，不属于 `Version` 公共契约 | 《Interfaces.zh-CN.md》二；《ModuleInterfaces.zh-CN.md》五 |
| §4.3 / §4.5 | E7：每个第三方入口只有一条写接管路径；E6：兼容层只允许 Api / Contracts / Diag | 《Rules.zh-CN.md》API-6；《ModuleRules.zh-CN.md》6.6 |
| §4.10 | 调用链固定为「适配器 → `SDS_API` 方法 → 唯一实现」；适配器不得自算容量 / 优先级 / 缺口 | 《Interfaces.zh-CN.md》六 |
| §六 | 验收包含：只调用唯一实现、返回原版可识别对象、门面补齐所有权 / 阵营 / 禁止、异常不外泄、未采用入口删除并 bump Version | 《Rules.zh-CN.md》一 |

五轴规则的权威定义见 `PublicAPI.zh-CN.md` §二表头（玩法语义分别见 `SDS.Vanilla/Rules.zh-CN.md` 与 `SDS.Core/Rules.zh-CN.md`）；本模块只保留「调用门 + 转发」结论，玩法语义不回迁。

## 二、旧实现 → 新实现的差异与坑

| 差异 | 旧实现 | 新实现 | 维护提示 |
|---|---|---|---|
| 桥的可见性 | `SDS_API_Adapters.cs` 全部 `internal`，与兼容层同程序集 | 全部改 `public` | 跨程序集只登记了 `SDS.Core → SDS.Rules` 一个友元；ThirdModCompat 仍看不到 Api / Core internal，桥已单列 |
| 桥是否属于契约 | 旧文档明确「`internal`，不承诺」 | public，但仍不进 `Version` 承诺 | 外部直接使用桥自担风险；增删桥不 bump `Version` |
| 旧直调内部实现 | 直接读 `SDS_GameComponent_Grid`、`SDS_DepositRules`、`SDS_WithdrawExecutor`、`SDS_EndpointResolver`、`SDS_OutputUtility`、`SDS_MirrorRegistry`、`SDS_ReturnUtility`、`SDS_DepositService` | 只经 `SDS_Core` / `SDS_Rules` 公开成员与 `SDS_Host` 槽 | 任何重新引用 Core / Rules internal 的改动都越界（《ModuleRules.zh-CN.md》6.5） |
| `ReadStockFor` 通道判定 | 自己用 `SDS_DepositRules.RequiresPreservedStorage(thing)` 分流，再 `SDS_StockKey.FromPreservedThing` / `FromThing` | 只调 `SDS_Rules.TryGetStockKey` | 复制分流会产生第二套通道判断；W33 / W35 变更时尤其容易分叉 |
| `TryWithdraw` 对保真键 | 保真键会转到 `SDS_WithdrawExecutor.TryWithdrawPreserved`，同一入口可出保真 | 显式拒绝 `key.preserved`，返回 `InvalidArgument`；保真只走 `TryWithdrawPreservedExact` | 旧调用方若传保真键，升级后必须改走精确真身计划 |
| `SDS_ApiCaller.claimOwner` | 字段存在，`FromPawn` / `FromFaction` 赋值，判定不使用 | 同左（`SDS.Contracts/SDS_API.cs`） | 只赋值、不参与判定；不要据此推断并发 claim 已实现 |
| 玩家阵营判定写法 | `TryFindWithdrawPoint`、`ValidateDepositCall`、`ValidateWithdrawCall`、`TryGetWithdrawCell` 均直接写 `Faction.OfPlayer` | 多数收口为 `IsPlayerFaction`（`Faction.OfPlayerSilentFail`，异常返回 false），但桥 `TryGetWithdrawCell` 仍写 `actor.Faction != Faction.OfPlayer`；M2 只共用取物点决议，未改调用门 | 两处写法不一致；改动阵营判定时必须同时检查，避免静默失败行为不同 |
| G 关闭时的 `ReadStock` | `grid == null` 时提前返回 `GridUnavailable` + `Insufficient`，不填 key 的 `forbidden` / `total` / 失败原因 | 只要账本存在就填 `total` / `forbidden`；`result = GridUnavailable`；`available` 仍取 `SDS_Availability`；`failure` 按 key 取，key 为空给 `UnresolvedDef` | 测试 `ReadStock_GridUnavailable_ZeroesAvailableKeepsTotal` 固定了「total 保留、available 归零、未解析优先」 |
| 桥回充落点 | `TryReturnPreserved(Thing, Map, IntVec3)`；`TryReturnVirtual(Thing, Map, IntVec3, bool landOnFailure)` | `TryReturnPreserved(Thing, Map, IntVec3)`；`TryReturnVirtual` 的 `cell` 传入 Core，`landOnFailure` 决定失败是否按 Ground 落地 | 落点优先级：`cell`（有效且在界内）→ pawn 脚下 → `map.Center`；账本按实物键回充，不接受 key 参数 |
| 镜像取出的 mode / pawn | `TryWithdrawFromMirror` 把 `mode` / `caller.pawn` 透传给执行器 | `SDS_Rules.TryTakeMirror(mirror, quantity, out taken)` 固定 `Hand`、无 pawn | 行为等价：API 已拒绝 `VirtualHandoff` 并校验 pawn 同图；登记缺口已在此补记 |
| 取 / 存入口的 G 关闭门 | 取货在事务深处失败返回 `TransactionFailed` | `ValidateWithdrawCall` / `ValidateDepositCall` 顶部返回 `GridUnavailable` | R17 对齐，失败码更精确；属于结果码行为变更，已在此登记 |
| 桥方法增减 | 无 `TryGetStockKey` / `IsDepositEndpoint`；有 8 个本体入口（`*ForEntry` 等） | 新增上述 2 个；8 个本体入口全部删除 | Vanilla 已直接调 Core；不要把 `*ForEntry` 搬回来当第二条入口 |
| 端点查询实现 | `SDS_EndpointResolver.TryFindBestForNonPawn` + `SDS_EndpointHandle`；找取物点走 `SDS_OutputUtility.FindWithdrawPoint` | `SDS_Rules.SelectEndpoint(SDS_EndpointQuery...)` + `ISDS_Port` | 新桥 `HasUsableBuildingEndpointWithin` 仍直接读 `map.reachability`（原版事实），但不自行排序 |
| Version | `Version = 3` | `Version = 4` | 只因程序集改名；五轴签名与语义不变，第三方须重新 soft-depend |

旧实现自此仅作历史存档，维护本模块无需再读。

## 三、设计取舍

> 原《实现原因.md》已并入本节；其中与「旧文件落点 / 可见性对照」重复的表不再保留。

### 3.1 为什么需要独立门面

| 理由 | 说明 | 来源 |
|---|---|---|
| 第三方够不到 Core internal | Core 对 Vanilla / Api / Bootstrap 只公开 `SDS_Core`、`ISDS_LedgerRead` / `ISDS_LedgerHost`（账本只读面与生命周期面）；`SDS_Availability` / `SDS_ImplantUtility` 在 Rules，`SDS_Ledger` 与其余类型 internal；`InternalsVisibleTo` 仅 `SDS.Core → SDS.Rules` | 《ModuleResponsibilities.zh-CN.md》二、五；《ModuleRules.zh-CN.md》五 |
| 程序集边界变化不改语义 | 重构前是单程序集 `SuperDimensionalStorage.dll`，现在拆成 7 个程序集；五轴方法名与语义保持不变，`SDS_API.Version = 4` 只因程序集改名 | 《ModuleResponsibilities.zh-CN.md》七 |
| 把「谁能调什么」变成可校验白名单 | `CheckAssemblyBoundaries.ps1` 做类型级 public 白名单与引用图检查；`CheckCompatSurface.ps1` 只允许 Api / Contracts / Diag | 《ModuleRules.zh-CN.md》十 |
| 调用门只写一次 | 玩家阵营、所有权、禁止、同地图只在 Api 判定；Vanilla 与第三方共用同一套入口，不在各自模块复制 | 《ModuleResponsibilities.zh-CN.md》七 |
| 新增第三方不改 Core | 新适配器只经 Api 与 Contracts；Core 不认识任何具体 Mod，也不新增 defName 分支 | 《ModuleRules.zh-CN.md》6.6；《ModuleResponsibilities.zh-CN.md》八 |

Api 的工程约束因此被限定为三条：只引用 Contracts / Diag / Core；禁止引用 Vanilla / Harmony / ThirdModCompat；禁止写规则、UI、扣账或预留实现，只转发 Core，必要时经 `SDS_Host` 取 Vanilla 事实（《ModuleRules.zh-CN.md》6.5）。

### 3.2 门面不承担的事

- 不做候选发现：端点候选枚举在 Vanilla 的 `ISDS_PortProvider.AppendCandidates`，Api 只调 `SDS_Rules.SelectEndpoint`。
- 不做交付：落地 / 进背包 / 进容器由 `ISDS_DeliverySink` 的 Vanilla 实现负责，Api 不碰 `GenPlace` / `ThingOwner`。
- 不装补丁、不写 UI、不缓存库存快照；`VisibleThingsOfDef` 与 `ReadStock` 每次现读。
- 不定义规则 ID，也不新增规则文档；玩法口径只引用《PublicAPI.zh-CN.md》指向的能力文档。

### 3.3 为什么 Api 不做规则

| 规则 | 唯一实现 | Api 的实际行为 | 来源 |
|---|---|---|---|
| 可用量 = 总量 − 预留 | `SDS_Availability.GetAvailableCount` | `ReadStock` 与桥上的 `GetAvailableCount` 只转发，不自己减 `reserved` | 《ModuleInterfaces.zh-CN.md》3.2；《ModuleResponsibilities.zh-CN.md》七 |
| 虚拟 / 保真通道判定 | `SDS_Rules.TryGetStockKey` / `SDS_Rules.RequiresPreservedStorage` | `ReadStockFor` 先问 Core 要键；桥上的 `RequiresPreservedStorage` 直接转发，不复制 def 分支 | 《ModuleResponsibilities.zh-CN.md》七；《ModuleInterfaces.zh-CN.md》3.2 |
| 账单补料缺口 | `SDS_BillSupplyPlanner`（`SDS.Rules` internal，P2 迁入） | `TryPlanBillSupplySlot` / `TryPlanAllowedDefSupply` 只转发入参与出参，不在 Api 算 `required` / `fromOwn` / `shortfall` | 《ModuleInterfaces.zh-CN.md》3.5；《PublicAPI.zh-CN.md》 §4.9 |
| 端点排序 / 容量 / 筛选 | `SDS_EndpointResolver`（`SDS.Rules`） / `SDS_StockRules` / `SDS_WithdrawTransaction`（`SDS.Rules`） | 「找」「取」「存」只组装查询 DTO 并转发 `SDS_Rules.SelectEndpoint` / `TryWithdraw` / `TryDeposit` | 《ModuleRules.zh-CN.md》6.5；《ModuleResponsibilities.zh-CN.md》七 |
| 失败原因口径 | `SDS_Availability.GetFailure` | Api 只把内部原因映射成 `SDS_ApiResultCode`，正常拒绝不写日志 | 《PublicAPI.zh-CN.md》 §3.6 |

一旦 Api 复制其中任何一条，就会出现第二份数量、第二套通道判断或第二个缺口实现；这正是准则三与 E7 要禁止的分叉（《DesignPrinciples.zh-CN.md》§二；《ModuleRules.zh-CN.md》三）。

以读为例：`ReadStock` 在全局开关关闭时只把 `result` 置为 `GridUnavailable`，`total` 仍按账本给出，`available` 仍取 `SDS_Availability`（R17 口径）。如果 Api 自己把可用量归零，就会与 Core 的读侧口径分叉，第三方看到的数字也将与 UI、Job 不一致。

### 3.4 关键取舍

| 取舍 | 具体表现 | 代价 / 边界 | 来源 |
|---|---|---|---|
| 五轴只返回 `Thing`，不交付 | `TryWithdraw` 返回已物化的 `List<Thing>`，`TryWithdrawFromMirror` 返回单个 `Thing`；`SDS_Rules.TryWithdraw` / `TryTakeMirror` 不调 `ISDS_DeliverySink` | 交付必须由调用方按原版契约完成；失败回充要再调 `TryReturnVirtual` / `TryReturnPreserved`，Api 不提供端到端事务 | 《ModuleInterfaces.zh-CN.md》1.2 第 3 条；`PublicAPI.zh-CN.md` §3.5 |
| 调用门与规则门分离 | Api 只判玩家阵营、所有权、禁止、同地图；库存、容量、筛选、排序、事务全部由 Core 决定 | 两处口径必须同步；例如 Api 的所有权判定不复用 `HaulAIUtility.PawnCanAutomaticallyHaulFast`，改动时要一起检查 | 《ModuleResponsibilities.zh-CN.md》七；`PublicAPI.zh-CN.md` §3.4 |
| `Version` 是兼容性声明 | `Version = 4` 只表示程序集改名、第三方须重新 soft-depend；五轴语义不变 | 适配器桥不进入 `Version` 承诺；桥的方法增删不触发版本号变化，外部若直接使用桥则自担风险 | 《ModuleResponsibilities.zh-CN.md》七；`PublicAPI.zh-CN.md` §3.8 |
| 适配器桥单列且不承诺五轴 | 桥方法 public，只为跨程序集；生产代码中的消费者只有 `SDS.ThirdModCompat`（`SDS.Tests` 仅测试调用） | public 面变大，白名单必须显式承认这些方法；每个桥方法只能转发，不能长出新规则 | 《ModuleInterfaces.zh-CN.md》五；《ModuleRules.zh-CN.md》五 |
| `Find.TickManager` 是唯一 `Find.*` 例外 | 只有 `CurrentTick()` 读 `Find.TickManager.TicksGame`，空引用回退 0，供 `CaptureReadVersions` 的 `tick` 字段 | tick 不通过 `SDS_Host` 取；回收巡检的 tick 由 Vanilla 侧 `ISDS_ReclaimWorld.CurrentTick` 提供（C1 后 Core 不再读该槽位），两处来源不同（是否统一待补） | 《ModuleResponsibilities.zh-CN.md》七；《ModuleRules.zh-CN.md》6.3；《ModuleInterfaces.zh-CN.md》1.2 |

最容易被低估的是「只返回 `Thing`」：它把交付责任显式推给调用方，换来 Api 不需要认识 `carryTracker`、`inventory`、`ThingOwner` 或落地工具。副作用是调用方必须在失败时自行回充；Api 只提供 `TryReturnVirtual`（`landOnFailure` 控制是否落地）/ `TryReturnPreserved` 两个回充入口，不替调用方决定回滚顺序。

适配器桥的 public 化是程序集拆分的直接代价：旧结构里兼容层与 `SDS_API` 同处一个程序集，`internal` 就够用；拆分后若不开 `InternalsVisibleTo`，就只能把桥方法提升为 public。为限制影响面，桥被明确排除在五轴契约之外，并由《ModuleInterfaces.zh-CN.md》五逐类登记。

### 3.5 不属于 Api 的部分

| 内容 | 归属 | 说明 |
|---|---|---|
| 候选枚举、可达、距离、优先级 | Core `SDS_EndpointResolver` + Vanilla `ISDS_PortProvider` | Api 只组装 `SDS_EndpointQuery` |
| 扣账、预留、事务、回滚 | Rules `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor` | Api 不保存事务，也不跨调用持有预留 |
| 交付、寻路、落地、进容器 | Vanilla `ISDS_DeliverySink` / JobDriver / UI | Api 只返回实物 |
| 补丁、建筑、UI、JobDriver | Vanilla | Api 补丁数为 0 |
| 日志实现与报告中心 | Diag | Api 只调 `SDS_Diag.ErrorOnce` |
| 启动顺序与宿主装配 | Bootstrap | Api 只提供 `Version` 常量 |

## 四、待补

- 文件分工：`SDS_API.cs` 承担五轴、调用门、`Version`、tick；`SDS_API_Adapters.cs` 承担适配器桥，两者都是 `SDS_API` 的 `partial`。
- 外部第三方对五轴的采用证据仍为空（`PublicAPI.zh-CN.md` §二·七）；`SDS_ApiCaller.claimOwner` 的现状见《Interfaces.zh-CN.md》7.5。
- 新增桥方法必须同时更新《ModuleInterfaces.zh-CN.md》五与类型级白名单，否则 `CheckAssemblyBoundaries.ps1` 会把它记为泄漏。
- 任何「先算一下可用量 / 缺口 / 优先级」的冲动都应回到 Core；Api 只允许转发与结果码映射。
