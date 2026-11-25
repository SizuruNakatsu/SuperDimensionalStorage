# SDS.Core · 设计决策（为什么这么做 / 踩过的坑）

> 定位：记录旧实现（已冻结、只作历史存档）中与本模块有关的差异、坑，以及迁移时「为什么这样适配原版判定」的结论。
> 本文只回答「为什么」，不定义新规则；可检查条目见 [Rules.zh-CN.md](Rules.zh-CN.md) §一–§十，规则 ID 的权威正文见 [Rules.zh-CN.md](Rules.zh-CN.md) §十一。
> 维护本模块无需再读旧实现，原因与结论已完整留存于此。

## 一、历史决策与坑

### 1.1 旧存档节点与字段

旧账本全部写在 `SDS_GameComponent_Grid.ExposeData`；新框架按归属拆成「Core 节点」与「Vanilla 节点」，
**节点名与 LookMode 全部不变**（节点定义汇总见《ModuleInterfaces.zh-CN.md》八）。

| 节点 | 字段 / 类型 | LookMode | 新归属 |
|---|---|---|---|
| `dataVersion` | `int`，旧 `CurrentDataVersion = 1` | `Value` | Core `SDS_Ledger` |
| `stockEntries` | `List<SDS_StockEntry>` | `Deep` | Core |
| `forbiddenKeys` | `List<SDS_StockKey>` | `Deep` | Core |
| `reservations` | `List<SDS_Reservation>` | `Deep` | Core |
| `preservedThings` | `List<Thing>`（读档中转，运行期以 `preservedOwner` 为准） | `Deep` | Core |
| `sdsReclaim` | `List<SDS_ReclaimSave>` | `Deep` | Vanilla（C8：读写都由 `SDS_GameComponent_Grid` 负责；Core 不读不写） |
| `depositFilter` | `ThingFilter` | `Deep` | Vanilla |
| `coreStoragePriority` | `int`，旧档缺省 `-1` | `Value` | Vanilla |
| `introNoticeShown` | `bool` | `Value` | Vanilla |
| `sidebarUI` | `SDS_SidebarUIState` | `Deep` | Vanilla |
| `sdsMirrors` | `List<Thing>` | `Reference` | Vanilla |
| `transportLoadRequests` | `List<SDS_TransportLoadSave>` | `Deep` | Vanilla |

旧 `SDS_ReclaimSave` 字段：`policy`、`jobLoadID`、`ownerLoadID`、`tick`、`hand`、`things`（`Reference`）、
`counts`（`Value`）；旧 `policy` 缺省按 `JobTarget` 处理。旧 `sdsProtectedOutputs` / `MapComponent_SDS_ProtectedOutputs` 已于 2026-11-16
按《SDS.Vanilla/CompatibilityRetirement.zh-CN.md》退役（只服务 2.4 / 2026-10-26 21:54 之前的档），不再并入回收账本。
C8 起回收账本的持有、序列化、读档审计都在 Vanilla；`sdsReclaim` 与本账本节点同级，
由 `SDS_GameComponent_Grid` 独占读写，Core 的 `SDS_Ledger` 不读也不写该节点。

### 1.2 规则 ID 的设计文档出处

规则 ID 出处：W21 / W22 / W26 / W27、W33 / W34 / W35、S16、R15 / R17 见《RuleIndex.zh-CN.md》与 [Rules.zh-CN.md](Rules.zh-CN.md) §十一；
E1 / E2 见《DiagnosticsCoverage.zh-CN.md》§M。

## 二、旧实现 → 新实现的差异与坑

| # | 差异 | 坑 / 注意 |
|---|---|---|
| 1 | 规则类改名并合并：旧 `SDS_DepositRules`（`SDS_Stock.cs`）+ 键工厂 + vitals + 物化 → 单一 `SDS_StockRules`（P4 后拆为 Core `SDS_StockData` + Rules `SDS_StockRules`） | 旧注释与规则 ID 锚点写的是 `SDS_DepositRules`；`CheckRuleIds.ps1` 的锚点已换名，改代码时不要把旧名引回来 |
| 2 | 账本从 `GameComponent` 拆出 | 旧 `SDS_GameComponent_Grid : GameComponent, IThingHolder`；新 `SDS_Ledger` 私有 holder。任何「把 grid 当 holder」的旧写法都失效 |
| 3 | 预留审计时机：旧 `reclaimAuditPending` 推迟到首个 tick；2026-11-15 起改由 Vanilla `SDS_GameComponent_Grid` 在 `FinalizeInit` 与 250 tick 周期调 `SDS_Core.AuditReservations(ownerAlive)`，账本 `ExposeData` 只做结构性清理 | 判活事实由调用方以 `Func<int,bool>` 传入，Core 不再读 `ISDS_ReclaimWorld`；判活委托为 null 时正 id 预留保留不动，表现为「静默跳过」而不会报错。宿主必须在账本构造前完成注入（《ModuleInterfaces.zh-CN.md》§4.2） |
| 4 | vitals 计算归位 | 旧 `SDS_StockVitals.CaptureHitPoints/CaptureRotProgress` 挂在 DTO 上；新只在 `SDS_StockData`（P4 前为 `SDS_StockRules`），Contracts 只留字段。Vanilla 不得自行算加权平均 |
| 5 | 保真同键合并删除 | 旧 `TryMergePreserved` 不迁入；`preservedByKey` / `preservedMembers` 作为惰性派生索引保留（见 §5.4），同一 key 仍可对应多个 `Thing`，不要假设「一键一件」 |
| 6 | 展示物登记从 owner 引用计数改为 `key → Thing` | 旧 Trade / Caravan / Transport 三个 owner 可共享同一展示物；新表按 key 单值，后注册覆盖前值，多 owner 语义由 Vanilla 自行保证。该表已随展示物工厂归 Vanilla `SDS_OutputUtility`，Core 只保留来源标记 |
| 7 | 决议器拆分 | 旧 `SDS_WithdrawResolver.Decide` / `ResolveMode` 在 Core；P2 起 `SelectEndpoint` 在 `SDS.Rules`，模式与优先级存档值在 Vanilla。Api / Vanilla 组合 plan，Rules 不重选 |
| 8 | 端点三张缓存表删除 | 旧每图注册表、30 tick 可达缓存、距离缓存、10 格参考半径近似全部删除；新候选由 `ISDS_PortProvider` 提供，距离由 `RankDistanceSquared` 现算。`SDS_EndpointContractCache` 只保留版本时钟 |
| 9 | 保真事务入口收窄 | 旧 `ExecutePreserved(Predicate<Thing> validator)` 删除，只保留 `ExecutePreservedExact(List<ThingCount>)`；`SDS_Ledger.TryWithdrawPreserved` 已删除（门面与内部均无调用） |
| 10 | 镜像外部消费路径 | 旧 `TryConsumeMirror`（`Destroy` 确认扣账、不预留）已迁入 `SDS_Rules.TryConsumeMirror`（P3）；`TryTakeMirror` 仍只用于取货，不得代替「销毁确认」 |
| 11 | 回充退路不再直接写 Pawn | 旧 `Return` 直接 `inventory.innerContainer.TryAdd` / `carryTracker.innerContainer.TryAdd`；新一律经 `ISDS_DeliverySink`，Core 禁止写这两个容器 |
| 12 | `SDS_WithdrawSave` 类型消失 | 存档节点仍在；读档时账本只调 `PruneMalformedReservations`；回收条目的读档审计（`SDS_ReclaimRegistry.AuditRestored`）随 C1 归 Vanilla，预留终老由宿主经 `SDS_Core.AuditReservations(ownerAlive)` 触发，不要恢复旧类 |
| 13 | Job 目标自动回充已迁入 Vanilla | `SDS_ReclaimRegistry.TrackJobTarget` / `TrackHand` / `ReclaimJobTarget` / `ReclaimHand` / `ForgetJobTarget` 现由 Vanilla 直接调用（C1 后不再经 `SDS_Core` 门面）；`Patch_SDS_ReclaimJobTargets` 在成功时丢弃登记、失败 / 打断时回充地面目标与手上料（W27） |
| 14 | 2026-11-15：预留终老策略移出账本 | 账本原先直接读 `Pawn.Destroyed/Map` 并问 `ISDS_ReclaimWorld.JobExists`，等于把 Job / Pawn 存活语义写进账本。现改为账本只提供 `CopyReservations` + `ReleaseReservation(ownerId)`，判活与释放时机在 `SDS_Core.AuditReservations(Func<int,bool> ownerAlive)`（判活委托由 Vanilla 提供，组合 `JobExists` + `Pawn.Destroyed`）；`jobLoadID ≤ 0` 一律非法。副作用：读档不再误杀「活着但不在图上」的小人的在途预留（只按 `Destroyed` 判） |

## 三、设计取舍

> 本节收录原先独立成篇的「实现原因」结论：为什么规则只剩 Core 一份、账本为什么用私有 owner、为什么强制 internal。
> 旧实现差异见本文 §二，本节不重复。

### 3.1 为什么 Core 是唯一规则中心

- 依据：《DesignPrinciples.zh-CN.md》§二 准则三——逻辑唯一 = 规则定义一次 + 所有入口调用它；文档层面同一规则只允许定义一次。
  代码层面《ModuleRules.zh-CN.md》一 明确不追求具体函数唯一，要求所有入口最终落到同一实现。
- 旧实现的所有类型同处一个程序集，规则可被任何入口直接调用，也可被任何新入口重新抄写一遍；重构后账本 / 数量口径只剩 Core 一份实现，玩法规则只剩 `SDS.Rules` 一份实现（实现落点见各自 README「职责」表）。
- 不收敛的具体代价：
  - 数量口径分叉：旧 `SDS_Availability` 注释已写明「总量口径只由 `grid.GetAvailableCount(key)` 一处给出」，一旦 Vanilla 或第三方另算 `total − reserved`，可用量、交易预算、装载夹取会互相矛盾。
  - 缺口计算分叉：S16 要求选 def、required / fromOwn / shortfall 与预留只在 `SDS_BillSupplyPlanner` 实现（P2 起在 `SDS.Rules`）；适配器只提供事实。
  - 优先级分叉：`PublicAPI.zh-CN.md` §4.2 禁止适配器自行决定目的地优先级、筛选、容量、平局，也禁止维护库存缓存或影子数量。
  - 写路径分叉：《ModuleRules.zh-CN.md》6.4 / 6.6 分别禁止 Vanilla「发明 CRUD 规则」与 ThirdModCompat「新增扣账 / 派活 / 回滚规则」。
- 结论：Core（账本 / 数量口径）与 `SDS.Rules`（玩法 / 口径判定）是唯一能让 Vanilla 与第三方共享同一口径的位置；这也是 `CheckAssemblyBoundaries.ps1` 做类型级白名单检查的前提（《ModuleRules.zh-CN.md》十）。

### 3.2 账本为什么用私有 `ThingOwner`，而不是 GameComponent 自己实现 `IThingHolder`

- 旧实现：`SDS_GameComponent_Grid : GameComponent, IThingHolder`，`GetDirectlyHeldThings()` 直接返回 `preservedOwner`
  （旧 `SDS_GameComponent_Grid` 第 18、206-225 行）。任何 holder 树遍历都能拿到可变的保真实物容器。
- 新实现：`SDS_Ledger` 持有 `private readonly ThingOwner<Thing> preservedOwner`，owner 是一个 private nested `SDS_PreservedHolder`；
  `ParentHolder` 为 null，`GetChildHolders` 恒为空（`SDS_Ledger.SDS_PreservedHolder`；《ModuleInterfaces.zh-CN.md》§3.1）。
- 可见面：`GameComponent` 是 public 类型，若它实现 `IThingHolder`，`GetDirectlyHeldThings` 就成为公开的可变入口；《ModuleRules.zh-CN.md》五要求 `SDS_Ledger` 不公开 `PreservedOwner`。
- holder 树：`GetChildHolders` 为空，尸体 `InnerPawn`、缩装家具 `InnerThing` 不会被递归枚举到 holder 树（《ModuleInterfaces.zh-CN.md》§3.1 末段）；存档形态见 Rules.zh-CN.md 十。
- 生命周期：账本构造无参，不依赖 `Game` / `GameComponent`；`SDS_GameComponent_Grid` 只负责创建、`ExposeData`、`Tick`、`Attach`（《ModuleInterfaces.zh-CN.md》§4.1），因此 Core 可脱离游戏环境单测。

### 3.3 为什么强制 internal

- 对外 `public` 只有 `SDS_Core` + 2 个账本接口（README §四）：`SDS_Core` / `ISDS_LedgerRead` / `ISDS_LedgerHost`；`SDS_Ledger` 与 `SDS_StockData` 本身是 internal 实现类型。其余类型（`SDS_Ownership` 等）全部 internal；
  Vanilla / Api / ThirdModCompat 不得直接引用（《ModuleRules.zh-CN.md》6.4、6.6）。端点决议与账单补料（P2）、流程层（P3）、判定层与公开门面 `SDS_Availability` / `SDS_ImplantUtility`（P4）均已在 `SDS.Rules`；回收账本 `SDS_ReclaimRegistry` 已随 C1 迁到 Vanilla。
- `InternalsVisibleTo` 只授予 `SDS.Rules`（P3 起；《ModuleRules.zh-CN.md》五）；Vanilla / Api / ThirdModCompat 仍只能经 `SDS_Core` / `SDS_Rules` 门面或 `SDS_API` 转发。
- 收益：internal 类型可自由重构而不破坏外部契约；`SDS.Core.Tests` 只测账本（Contracts / Diag / Core），规则 / 流程测试在 `SDS.Rules.Tests`，`SDS.Tests` 只做跨模块集成。

### 3.4 关键取舍

| 取舍 | 具体表现 | 代价 / 边界 |
|---|---|---|
| 预留按通道分口径 | 虚拟按 key 计数，保真按精确真身计划预留 | 保真不能用虚拟预留去挡（`SDS_Ledger.TryReserve` / `TryReservePreservedPlan`；《ModuleInterfaces.zh-CN.md》§3.1） |
| 优先级不缓存排序结果 | 由调用方读设置后写入 `SDS_EndpointQuery.priority`，Core 每次按查询值重算排序 | 端点排序每次重算，换取即时生效（`SDS_EndpointResolver.cs`；《ModuleInterfaces.zh-CN.md》§3.3） |

`Deliver` 只释放预留、部分成功不回滚、镜像取用三步、回充先回账本四项的可检查条目自 P3 起见 `SDS.Rules/Rules.zh-CN.md` §1.7–1.9，本节不重复。

### 3.5 收敛后的两处口径说明

- 版本语义拆开：`SDS_EndpointResolver.Version` 只服务内部可达缓存（internal）；`SDS_EndpointContractCache.Version` 才是距离缓存时钟，由 `SDS_Rules.EndpointVersion` 暴露（《ModuleInterfaces.zh-CN.md》§3.3）。
- 保真取用入口收窄：旧的 `TryWithdrawPreservedWhere` 候选遍历，改为 `SDS_Ledger.TryWithdrawPreservedPlan` 的精确计划；计划中的真身若已不在容器内，整笔失败并回滚。

## 四、旧设计文档的解释性结论（转述）

1. **为什么虚拟通道只保证 def + stuff + quality + 堆平均 HP / 腐烂**：B1 黑名单制下默认虚拟，只有内部持有物、
   def 级身份状态、实例状态才保真；四类（制成品 / 原材料 / 食物 / 块堆）无条件虚拟，以数量与性能优先。
   来源：[Rules.zh-CN.md](Rules.zh-CN.md) §12.14、§十一。
2. **为什么 W35 要优先于四类默认虚拟**：研究进度 / 笔记 / 随机阈值 / 冷却都是逐件实例状态，虚拟化 `Destroy`
   后 `MakeThing` 只会重建默认实例，进度清空；判据只消费 `ThingDef.IsStudiable`，不按 mod / defName 维护名单。
   来源：[Rules.zh-CN.md](Rules.zh-CN.md) §十一 W35。
3. **为什么 W33 不直接改 B1 默认值**：普通枪械 / 近战几乎都带 `CompProperties_Art`，普通衣物几乎都带
   `colorGenerator`；直接删这两项会改变所有存档的新存入。W33 把缩减限定在「开关开启 + 武器/衣物」，
   关闭时 B1 与旧档行为完全不变。来源：[Rules.zh-CN.md](Rules.zh-CN.md) §十一 W33。
4. **为什么存量迁移要单独走专用入账**：W34 只允许从 `preservedOwner` 取真身，入账不经 `CanDeposit` 的容量 /
   筛选 / 全局可用性门，再 `Destroy`；有事务预留的归属组跳过，关闭开关不回溯、不在读档时静默转换。
   来源：[Rules.zh-CN.md](Rules.zh-CN.md) §十一 W34。
5. **为什么回滚必须按通道**：通用 `Return` 受存入筛选 / 品类容量支配，玩家在回滚窗口改筛选会把保真真身丢到地上；
   且 B1 之后旧虚拟条目对应的 def 可能已进保真黑名单，按 `RequiresPreservedStorage(thing)` 反推会塞错通道。
   通道只能来自事务 Execute 路径（`materializedPreserved`）。来源：`SDS.Rules/Rules.zh-CN.md` §1.5 W27。
6. **为什么 `Reserved` 可以暂时大于 `Total`**：镜像 / 实物的外部销毁是既成事实，不能回改别人的预留；
   可用量仍按 `max(0, Total − Reserved)`，其它瞬时事务按足额口径整笔失败并释放预留。来源：[Rules.zh-CN.md](Rules.zh-CN.md) §十一 W22。
7. **为什么原版取货只统一「拿取层」**：原版取用是搜索 → 拿取 → 交付 → 生命周期四层，各有独立契约；
   搜索层不得物化 / 扣账，交付形态有 Hand / Container / SpawnedTarget / VirtualHandoff 四种，无法统一。
8. **为什么保真不参与原版搜索**：保真真身不 spawn，不在 `ListerThings`、`GenClosest` 与 holder 树；
   只能走专用桥（保真原料 DoBill、尸体入棺、缩装安装等）。来源：`SDS.Vanilla/Rules.zh-CN.md` §九；S1 / S5 / S7。
9. **为什么「找到」不等于「拿到」**：搜索结果只是快照，执行时必须由取货事务重新校验（S6 / S7）；
   搜索层不预留、不扣账。
10. **为什么「使用」命令只走自身端口、固定 1 件**：门控 0 要求目标小人 `IsPortOperational`，建筑端点不参与；
    判定阶段失败回充，交给原版 `action` 后按「输出到地上」处理、不再回收；任务类 JobDef 取消并回充。
    来源：`SDS.Vanilla/Rules.zh-CN.md` §十二.5；D1 / D3。
11. **为什么 E2 只断言 `[0, Total]`，不断言 `Total − Reserved`**：外部销毁后 `Reserved > Total` 合法，
    此时可用量夹 0；断言 `Available == Total − Reserved` 会误报。来源：《DiagnosticsCoverage.zh-CN.md》§M；
    `SDS_Invariants.AvailableWithinTotal`。
12. **为什么 API 的取货只返回 `Thing`**：成功即已完成账本扣减并物化，交付由调用方完成；失败时账本与可回滚实物
    回到调用前状态。来源：《PublicAPI.zh-CN.md》§3.5。

旧实现自此仅作历史存档，维护本模块无需再读。

## 五、补充决策与坑

### 5.1 W33 的代价
艺术 `taleRef` 在虚拟化后不可恢复，护盾充能会被免费补满；两项代价都是有意接受的取舍，不是遗漏。

### 5.2 W34 的边界
转换只跳过仍有预留的归属组；且转换必须在关闭交易窗口时进行，避免与进行中的交易读写同一批真身。

### 5.3 保真入库的顺序与验收
所有权转移只经 `SDS_Ownership.TryTransferTo`（spawned 先 `TryAdd` 再 `DeSpawn`；held 走 `TryTransferToContainer`），失败零副作用，不做 Split、不回退落地。验收口径是「存读档前后件数不变」。

### 5.4 派生索引与缓存
- 旧派生索引（按 key / def 的保真索引、`keysByDef`、容量品类集合）已恢复为 `SDS_Ledger` 内的惰性索引：只在 `StockVersion` 变化后的首次查询重建，查询不再全量扫描。
- 旧距离缓存与端点缓存表仍不恢复，距离现算。
- 端点选择不做全候选可达预判：可达性只在「当前胜出候选」上判一次；这是 2.5.2 的行为，也是 27 个端点下不退化成绩寻路风暴的前提。

### 5.5 端点可达性为什么必须惰性判
- 可达性是原版 `reachability.CanReach`（区域寻路），不是纯算术；对全部候选预判会把一次端点选择放大到「端点数」次寻路。
- 正确顺序：静态门 → 排序 → 只对胜出候选判可达 → 不可达标记后取下一位。自身端口与 `actor == null` 不判。
- 不缓存可达结果：门 / 危险度 / 地图变化太快；旧版也只缓存候选句柄与距离，不缓存寻路结果。

### 5.6 预留合并的 int 上界（已知边界，2026-11-16 复审登记）
- 预留条目 `count` 为 int：单次建预留路径有 `int.MaxValue` 检查（`TryReserveCore` / `TryReservePreservedPlan`，宁可整笔失败也不静默截断）；但同归属同 key 的跨调用合并（`AddReservation` 直接累加）没有累计检查——两次各 ≤ int.MaxValue 的合法预留，在账面 ≥ 约 30 亿件（账面为 long）时可合并溢出为负。
- 现实玩法不可达：单笔存入 ≤ int.MaxValue 且预留按 Job 粒度；兜底是 `AppendLedgerInvariantReport` 的「预留非正数量」检查，可用量按 W22 夹 0，不会把库存扣成负值。
- 改成 long 口径属数量口径变更（L2），当前不做。
