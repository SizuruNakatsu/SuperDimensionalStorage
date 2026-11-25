# SDS.Core · 规则

> 定位：本模块「必须遵守什么」的可检查清单。规则正文以《ModuleRules.zh-CN.md》6.3、五与《ModuleInterfaces.zh-CN.md》§三为准，本文只落检查项。

## 一、禁止符号（与 `CheckAssemblyBoundaries.ps1` §8 一致）
`SDS.Core/**/*.cs` 中不得出现下列符号（注释行 `//` 开头不参与检查）：

| 类别 | 禁止符号 |
|---|---|
| 补丁 / 行为 | `Harmony`、`JobDriver`、`Toil`、`ThinkTree`、`WorkGiver` |
| 存储 / 搜索 | `SlotGroup`、`Reachability`、`ListerThings`、`GenClosest`、`ResourceCounter` |
| 生成 / 落点 / 枚举 | `IHaulDestination`、`GenPlace`、`GenSpawn`、`Find.`、`.mapPawns` |
| UI / 跨模块 | `Window`、`ITab`、`Widgets.`、`SDS_ModCompat`、`SDS_Vanilla` |

检查方式：`powershell -File Tools/CheckAssemblyBoundaries.ps1`，任一行命中即非零退出。来源：《ModuleRules.zh-CN.md》6.3；脚本 §8。

## 二、唯一 `IThingHolder` 例外
- 只允许 `SDS_Ledger.cs` 内出现 `IThingHolder`：`private sealed class SDS_PreservedHolder`。
- `ParentHolder` 返回 `null`；`GetChildHolders` 必须为空；`GetDirectlyHeldThings` 只返回 `preservedOwner`；不公开 `PreservedOwner`，不做 `Scribe_Deep`。

检查方式：脚本 §8 的文件名白名单；`LedgerStructureTests.PreservedOwner_DoesNotExposeChildren`。来源：《ModuleRules.zh-CN.md》五；《ModuleInterfaces.zh-CN.md》§3.1。

## 三、禁止写 `carryTracker` / `inventory.innerContainer`
- 禁止写 `Pawn.carryTracker`、`Pawn.inventory.innerContainer`；读取只允许 `Count` / `InnerListForReading` / `Contains`（Core 当前不用）。
- 回充退路（背包 / 手持 / 地面）一律经 `ISDS_DeliverySink`，不直接操作 Pawn 容器。

检查方式：`CheckAssemblyBoundaries.ps1` 未内建此条（待补）；代码审阅 + 全目录检索 `carryTracker|inventory.innerContainer` 应为 0 命中。来源：《ModuleRules.zh-CN.md》6.3；`SDS_ReturnUtility.DeliverFallback`。

## 四、internal 边界
- 顶层 `public` 只允许：`SDS_Core`、`ISDS_LedgerRead`、`ISDS_LedgerHost`。
- `SDS_Ledger` / `SDS_StockData` 是实现类型（internal）；其写方法全部 `internal`；其余类型一律 `internal`，不得被 Vanilla / Api / ThirdModCompat 引用；`InternalsVisibleTo` 只允许 `SDS.Rules`（P3 起，P4–P5 沿用）。
- 门面新增 `public` 方法必须先改《ModuleInterfaces.zh-CN.md》§3.2 与《ModuleResponsibilities.zh-CN.md》二。

检查方式：脚本 §4 类型级白名单 + §5 友元（仅登记 `SDS.Core → SDS.Rules`）；`CheckCompatSurface.ps1` 检查兼容层引用面。来源：《ModuleRules.zh-CN.md》五；《ModuleResponsibilities.zh-CN.md》五。

## 五、事务 / 回充 / 镜像（已迁至 SDS.Rules）

取货事务、回充与镜像取用的流程约束自 P3 起以 `SDS.Rules/Rules.zh-CN.md` §一（1.7–1.9）为权威。Core 只保留账本侧不变量：

| 规则 | 具体约束 | 检查方式 |
|---|---|---|
| 可用量 / 外部扣账 | 一律 `total − reserved` 夹 `0`；禁用 / G 关闭读侧归零；`TryDebitVirtual` 不裁剪他人预留，`Reserved > Total` 合法 | `LedgerTests.*`；`TransactionTests.Transaction_RolledBack_WhenExecuteFails`；E2 `AssertAvailableWithinTotal` |

来源：W21 / W22 / W27；W21 / W22 / W27 正文均以 `SDS.Rules/Rules.zh-CN.md` §一为权威。

## 六、回充 null 规则（已迁至 SDS.Rules）

回充先回账本、`map == null` / `pawn == null` / `fallback == None` 的处置口径，自 P3 起以 `SDS.Rules/Rules.zh-CN.md` §1.8 为权威；`ReturnTests` 已随 P5 测试拆分迁入 `SDS.Rules.Tests`。

## 七、镜像取用三步（已迁至 SDS.Rules）

镜像取用的扣账 → 物理转换 → 回补顺序与计划字段约束，自 P3 起以 `SDS.Rules/Rules.zh-CN.md` §1.9 为权威。

## 八、补料（已迁至 SDS.Rules）
补料选 def、`required` / `fromOwn` / `shortfall`、预留与自有池扣减，以及医药门 `extraAllows` 的注入口径，已在 P2 随 `SDS_BillSupplyPlanner` 迁至 `SDS.Rules/Rules.zh-CN.md`。Core 不再定义补料规则，只保留账本能力。

## 九、静态清理（Core 0 项 + Rules 4 项）

`SDS_Core.RegisterAll()` 只登记 Core 自检提供者，不登记静态清理项。判定层 / 流程层的 4 项清理由 `SDS_Rules.RegisterAll()` 登记：

| 序号 | 清理项 | 清理内容 |
|---|---|---|
| 1 | `SDS_EndpointContractCache.Reset` | 端点契约版本时钟 |
| 2 | `SDS_ProducedThingMarker.Reset` | 刚物化来源标记 |
| 3 | `SDS_ImplantUtilityImpl.ResetChipCache` | 芯片等级缓存 |
| 4 | `SDS_StructureRegistry.Clear` | 当前存档的结构事实（核心 / 锚点 / 容量装置） |

回收账本随 C1 迁到 Vanilla，其 `Reset` 由 `SDS_Vanilla.RegisterAll` 登记。

`ResetAll` 只清进程级静态缓存，不清当前存档的 `SDS_Ledger`；进程级常量（Def 查询结果、Provider 实例）不登记。
`SDS_StockRules.rebuildCache` / `gearRebuildCache`（Rules）属 Def 查询结果：键为 `ThingDef`，值只依赖 def 静态数据与 `ignoreArt` 字典选择；`ForceWeaponApparelVirtual` 只决定走哪本缓存，因此不登记。

检查方式：审阅两处 `RegisterAll`；`SDS.Rules.Tests/Support/RulesTestKit.ResetRulesStatics` 清结构注册表与端点 / 标记 / 芯片缓存；`CoreHardeningTests.Structures_ResetAll_ClearsAvailabilityAndCapacity`；启动日志「登记 N / 清理 M / 失败 K」。来源：《ModuleRules.zh-CN.md》三（E1）；《ModuleResponsibilities.zh-CN.md》五。

## 十、存档节点规则
`SDS_Ledger.ExposeData` 节点名与 LookMode 冻结，逐字为：

```csharp
Scribe_Values.Look(ref dataVersion, "dataVersion", 1);
Scribe_Collections.Look(ref stockEntries, "stockEntries", LookMode.Deep);
Scribe_Collections.Look(ref forbiddenScratch, "forbiddenKeys", LookMode.Deep);
Scribe_Collections.Look(ref reservations, "reservations", LookMode.Deep);
Scribe_Collections.Look(ref preservedScratch, "preservedThings", LookMode.Deep);
```

- `dataVersion` 缺节点时固定回落到 `1`，不得用 `CurrentDataVersion` 作默认值；否则未来升版会把无版本字段的旧档误判成新版而跳过迁移。

- 列表字段在进入保存 / 读取分支前必须补空并记 `存档/旧档字段补齐`（Warn）；缺节点导致 `null` 是正常旧档。
- 保存只写 `HasIdentity` 且数量 > 0 的条目；读档先 `TryResolve`，解析失败按未解析保留。
- `preservedThings` 是 `LookMode.Deep` 中转列表，读档后交回 `preservedOwner`；不得对 owner 本身 `Scribe_Deep`。
- 读档时账本只做结构性清理（`PruneMalformedReservations`）；预留终老由 Vanilla 的 `SDS_GameComponent_Grid.FinalizeInit` 调 `SDS_Core.AuditReservations(ownerAlive)`，不得在读档时重建账本。
- **C8 迁移（2026-11-15）**：`sdsReclaim` 自本版起由 `SDS_GameComponent_Grid` 独占读写（与本账本节点同级）；账本不读也不写该节点。旧档同名节点也在同一位置，因此组件的单一读路径同时兼容新旧档；若账本也读，会被两个 `ExposeData` 各读一次而产生重复回收条目。

检查方式：`LedgerStructureTests.ExposeData_UsesLockedNodesAndLookModes`；代码审阅 `ExposeData`。来源：《ModuleInterfaces.zh-CN.md》§3.6；《ModuleRules.zh-CN.md》五、三（W26）。

## 十一、规则锚点（权威归属以《RuleIndex.zh-CN.md》为准）

按《DesignPrinciples.zh-CN.md》§二，权威文档以《RuleIndex.zh-CN.md》「权威文档」列为唯一仲裁源，本节只登记本模块锚点，不复述规则正文。
唯一以本节为权威的是 W34（迁移机制）；W26 以 `SDS.Vanilla/Rules.zh-CN.md` §十为权威，E1 / E2 以《DiagnosticsCoverage.zh-CN.md》§二·M 为权威；判定层 / 流程层规则（W21 / W22 / W27 / W33 / W35 / W49 / W56 / R15 / R17）自 P3–P4 起以 `SDS.Rules/Rules.zh-CN.md` §一为权威，本节不复述。

| ID | 权威文档（规则索引） | 本模块锚点 | 说明 |
|---|---|---|---|
| W26 | `SDS.Vanilla/Rules.zh-CN.md` §十 | `SDS_Ledger.ExposeData` | 字段补齐 + `TryResolve` + 未解析保留 |
| W34 | 本文 §十一 | `SDS_Ledger.ConvertPreservedGearToVirtual(Func<Thing,bool>)`（迁移机制）+ `SDS_StockRules.CanMigratePreservedGearToVirtual`（Rules 判据） | 迁移专用入账，绕过容量 / 筛选 / 可用性门 |
| E1 | 《DiagnosticsCoverage.zh-CN.md》§二·M | `SDS_Core.RegisterAll` 0 项清理 + `SDS_Rules.RegisterAll` 4 项清理 | 见Rules.zh-CN.md 九 |
| E2 | 《DiagnosticsCoverage.zh-CN.md》§二·M | 调用 `SDS_Diag.Assert*`（实现位于 Diag） | `AvailableWithinTotal` / `TransactionStage` / `TransactionTerminal` |

## 十二、补充口径（仍有效、无独立规则 ID）

> 本节只保留账本侧补充口径；流程 / 判定类 12.3–12.25、12.28、12.32、12.33 已迁至 `SDS.Rules/Rules.zh-CN.md` §五，本节不再复述。

### 12.1 读档后事务处置
> 事务状态机与回充实现自 P3 起在 `SDS.Rules`；本节只保留账本侧口径（预留快照、按归属 id 释放、判活委托），流程约束以 `SDS.Rules/Rules.zh-CN.md` §1.7–1.9 为准。
- 只有预留、无实物：直接释放预留。
- 已物化且实物仍在：优先回充账本；回充失败则落地交付。
- 已销毁：不回加，绝不按 def 重建；被第三方移走：视为已交付。
- Job / 对话框消失：释放预留并回充，不留悬空事务。
- 执行器异常：`Begin` 成功后抛出的一切异常都必须 `Rollback`；已物化实物按通道回充，不得把预留留到下次读档。
- 预留终老：id ≤ 0（负数 = Instant 事务残留，0 = 旧档 / 写坏的归属）一律清理；正 id 由 `SDS_Core.AuditReservations(Func<int,bool> ownerAlive)` 以调用方传入的判活委托复核，返回 false 即释放（判活事实在 Vanilla 侧组合 `ISDS_ReclaimWorld.JobExists` 与 `Pawn.Destroyed`）。读档后（Vanilla `FinalizeInit`）与运行期每 250 tick 各执行一次；账本只提供预留快照与按归属 id 释放，不读 Job / Pawn 存活语义。

### 12.2 `SDS_ReclaimOutcome` 语义与合并
- 三个取值：`Abandoned`（放弃）、`Consumed`（已消费）、`Returned`（已回充）。
- 同一物多次回收结果按 `Abandoned > Consumed > Returned` 合并。
- 硬性约束：回收结果只用于回收侧记账，不得反向驱动事务终态。

### 12.10 存活真身与计数
- 存活判据唯一在 §12.31：未销毁且内容完整（`SDS_StockData.HasLiveContent`）；不满足者按僵尸引用处理。
- 僵尸引用不计入 `Total`。
- 计数去重唯一在 `SDS_Ledger.EnsurePreservedIndex`：同一引用只计一次，同一 `thingIDNumber` 只计一次；`GetPreservedCount` / `PreservedSnapshot` / `AppendPreservedThings` 同源。

### 12.16 目的地候选与排序（实现已迁 SDS.Rules）
候选枚举、排序与平局口径的规则正文已随 `SDS_EndpointResolver` 迁至 `SDS.Rules/Rules.zh-CN.md`；Core 只保留结构事实与端点版本时钟。

### 12.26 读侧不变量 R1–R14（仍适用）
- R1 读只读；R2 同候选 + 同投影 = 同有效格 / 距离 / 存储状态；R3 排序键条件化（priority → distance → tieBreaker → thingIDNumber）；R4 排序距离用最近访问点、不掺可达，走位另取最近可达点；R5 源可达 = 任一访问点，目的地可达 = 格 / 落脚格，不得互换；R6 `Available = Total − Reserved`，key 级只是闸门，Execute 消费预留；R7 Total 读账本 / `preservedOwner` 存活真身，不读镜像 `stackCount`；R8 快照版本化，`CanReach` / `ReservationVersion` 不进快照；R9 快照不是预留，执行前事务复检；R10 候选按 `Thing` 引用去重；R11 holder 读深度必须显式（单层 `GetDirectlyHeldThings` vs 递归 `GetChildHolders`）；R12 存档只存可验证状态，读档首 tick 审计、不按 def 重建；R13 `Unresolved` 保留、供给归零、永不删除；数量与 vitals（HP / 腐烂）一起保留，def 回来自动恢复；R14 展示聚合键永不进入读路径，行 → 真实键唯一解析 `TryNormalizeRowKey`。

### 12.27 同一真身单一所有者（写入与读档）
- 同一真身同一时刻只能有一个所有者：`preservedOwner` / 端点 `inputBuffer` / 地图 / 容器四者互斥。
- 入账所有权转移只有一份实现：`SDS_Ownership.TryTransferTo`（spawned 先入容器、成功后再 `DeSpawn`，失败零副作用；held 走原版 `TryTransferToContainer`）；**禁止直接置空 `holdingOwner`**，禁止在入口各自 `Remove` 后再由别处补 `TryAdd`。
- `ISDS_LedgerRead.IsPreservedThingId(int)` 是唯一的 ID 级判定口；读档时端点缓冲、小人背包 / 手持等普通容器统一用它剔除“与存活保真真身同 thingID”的重复副本，不 Split、不 Destroy、不改数量。
- 装备 / 衣物槽不自动摘除（避免改变原版“穿着中”的表现），命中只告警，需人工处理。
- 账本自身读档按 `thingIDNumber` 去重后入列；该判定只服务读档去重与审计，不得用于正常写入路径的所有权裁决（正常写入走 `SDS_Ownership`）。

### 12.29 派生索引与容量口径
- `keysByDef`（虚拟键按 def）、`preservedByKey` / `preservedKeysByInnerDef`（保真按键 / 内层 def）、`categoryKeys`（容量品类集合）都是 `SDS_Ledger` 的派生索引，只由 `StockVersion` 变化后的首次查询重建。
- 保真派生索引（`preservedByKey` / `preservedKeysByInnerDef` / `categoryKeys`）按「引用 + `thingIDNumber`」去重（同一 ID 只算一个真身）；`GetPreservedCount` / `PreservedSnapshot` / `AppendPreservedThings` 必须同源，禁止 UI / 适配器自行去重或另算总量。
- def 级查询有两套既定语义，不得互相“统一”：`GetAvailableCountForDef` / 品类容量按**真身 def**（缩装 = `InnerThing.def`）；`LedgerTotalForDef`、资源条注入与 `SDS_API.GetPreservedAvailableCount(ThingDef)` 按**外壳 def**（与原版 `ThingsOfDef` 对齐）。改语义按 L2 评估。
- 读侧只允许通过这些索引查询：`CopyKeysForDef` / `CopyPreservedKeysForDef` / `GetPreservedCount` / `UsedCategoryCount` / `HasCategory`；不得在 UI / 适配器 / 兼容层另建第二份缓存。`UsedCategoryCount` / `HasCategory` 属 `SDS_Ledger` internal 索引，不进入 `ISDS_LedgerRead`。
- `UsedCategoryCount` 是容量口径的唯一来源；`HasCategory` 是容量“该品类已存在”的唯一判定。二者共用同一份 `categoryKeys`（保真侧已按引用 + thingID 去重，判据唯一在 `AppendLivePreservedCategories`）。
- 索引重建只发生在库存或保真真身集合变化之后，属于 O(K+P) 的一次性成本，不得退化为每次查询全扫。
- 品类分组开关（`separateByQuality` / `separateByStuff`）切换时必须调用 `NotifyCapacitySettingsChanged()` 丢弃索引；索引不得跨口径复用。

### 12.30 实物 → 虚拟的唯一原子实现

- 「摘出原容器 → 入账 → 销毁」三步只在 `SDS_Ledger.TryConvertThingToVirtual` 实现；存入（`SDS_Rules.TryDeposit`）与回充（`SDS_ReturnUtility.ReturnVirtual`）都只调它，禁止各入口再写一遍。
- 销毁未生效（`!thing.Destroyed`，含 `Destroy()` 抛异常）必须回滚入账并回挂原容器；回滚量夹到当前存量，不得把同键其它存量一并扣掉。
- 例外：保真通道入账不销毁实物，走 `TryDepositPreserved`；W34 存量迁移是独立的迁移专用入账，不属于本条。

### 12.31 存活保真真身的唯一判据

- 「存活」= 未销毁 **且** 内容完整（`SDS_StockData.HasLiveContent`：尸体带内层、缩装带内层物），唯一实现为 `SDS_Ledger.IsLivePreserved`。
- 必须同源的口径：`GetPreservedCount` / `PreservedSnapshot` / `AppendPreservedThings` / `EnsurePreservedIndex` / `EnsureCategoryKeys` / `IsPreservedThingId` / `Tick` / `AppendPreservedInvariantReport` / `AppendLedgerInvariantReport`。
- `IsPreserved(Thing)` 只回答「是否由本账本持有」，不判内容是否可用；不得用它替代存活判据。
- 运行期同一实物只允许一个所有者：引用已在 `preservedOwner`，或存在同 `thingID` 的存活真身时，存入 / 回充 / 待恢复重试一律幂等拒绝（唯一判定 `SDS_Ledger.IsPreservedIdentityOccupied`），不得复制、不得转通道、不得落地。
- 通用虚拟链路（`TryConvertThingToVirtual`）不得接管账本已持有的真身；保真 → 虚拟只允许 W34 专用迁移（`ConvertPreservedGearToVirtual`），避免绕过 W34 的开关 / 预留 / 品类门禁。
- 巡检（250 tick）必须移除「已销毁」与「内容失效」两类真身；内容失效的移除记 `Warn`。

### 12.34 禁用集合独立于库存

- 品类禁用是玩家意图，不随库存清零而丢失：读档不再裁剪「无库存」的禁用键。
- 只有玩家显式解除禁用（`SetForbidden(false)`）才从集合移除；诊断 / 容量 / 可用量口径不受影响。

### 12.35 诊断与未解析键

- 未解析 def 条目按 R13「保留、供给归零、永不删除」处理：不参与 `total − reserved` 比对，单独输出「未解析 N 条」统计；HP / 腐烂（`vitals`）同样保留——`RotProgressIllegal` 对未解析 def 只做 NaN / 无穷 / `< -1` 结构校验，`MergeVitals` 允许在未知 def 下合并。
- 保真自检同时检查：所有者一致、thingID 唯一、内容完整、读档待恢复项为 0。
- 完整账本自检 `AppendLedgerInvariantReport` 不依赖宿主事实，检查 stock / vitals / `keysByDef` / `preservedByKey` / `preservedKeysByInnerDef` / `categoryKeys` / 预留结构 / 待恢复 / `dataVersion`；由 `SDS_CoreDiagnostics.AppendReport` 调用。
- 独立校验边界：`SDS.Core.Tests` 只引用 Contracts / Diag / Core，覆盖账本随机数量守恒、不变量、未解析 vitals 往返、容量去重与预留终老；规则 / 流程 / Scribe 往返测试在 `SDS.Rules.Tests`；`SDS.Tests` 只做跨模块集成。

### 12.36 Core 的输入边界（去模块耦合）

- Core 不直接引用任何 SDS 模块，也不读宿主槽位；以下事实由 Rules / Vanilla 以查询字段 / 计划字段 / 委托消费：
  - 端点优先级：`SDS_EndpointQuery.priority`（工厂代填，`default` 查询调用 `ApplyHostPriority()`）。
  - 镜像物理：`SDS_WithdrawPlan.physical`（Rules 的 `TryTakeMirror` 显式传入）。
  - 医药门：`AllowsDef(..., extraAllows)` 与规划方法的 `extraAllows` 由 Vanilla / Api 组合 `SDS_MedicalGate.Allows`。
  - 第三方保真事实：`SDS_Rules.SetExternalPreserveProvider(Func<Thing,bool>)` 由 Bootstrap 接线；Rules 只持有委托。
- `Ports` / `DepositFilter` / `Delivery` 均由 `SDS.Rules` 消费（P2–P4 已迁出）；Core 的输入只剩账本方法参数与 `SDS_StockData` 数据。
- 外部输入只允许「加严」Core 判据（如额外要求保真），不得放宽通道 / 可用量 / 容量规则。

### 12.37 回收归属 Vanilla（C1 / C8）

- 回收账本（`SDS_ReclaimRegistry`）与 Job / Lord / 远征队编成事实整体归 `SDS.Vanilla`；Core 不再持有回收条目、不再读 `ISDS_ReclaimWorld`。
- Core 只保留一项通用能力：
  - `AuditReservations(Func<int,bool> ownerAlive)`：按归属 id 释放失效预留，判活由调用方传入。
- 回充（`ReturnThing` / `ReturnVirtualCount` / `ReturnThings`）归 `SDS.Rules` 的 `SDS_ReturnUtility`，Core 不提供；失败按 `Ground` / `PawnInventory` 退路交付，绝不吞物品。
- 存档：`sdsReclaim` 完全由 `SDS_GameComponent_Grid` 读写，Core 不读不写（C8）。
- 回收巡检（`Sweep`）由 `SDS_GameComponent_Grid.GameComponentTick` 每 250 tick 驱动；`SDS_Ledger.Tick` 不再驱动回收。
