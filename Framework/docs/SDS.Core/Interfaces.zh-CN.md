# SDS.Core · 接口

> 定位：本模块的**消费面**与签名索引。签名与成员语义的唯一权威是本模块 `SDS.Core_API.cs`（E8）；本文不抄写签名。
> 类型定义权威见《ModuleInterfaces.zh-CN.md》§三；与之冲突时以源码为准。

## A 节　签名索引

| 类型 | 声明处（唯一权威） | 用途 |
|---|---|---|
| `SDS_Core` | `Source/SDS.Core/SDS.Core_API.cs` | public 门面：账本生命周期、已用品类数、禁用开关、状态写回 |
| `ISDS_LedgerRead` / `ISDS_LedgerHost` | 同上 | public 账本只读面 / 生命周期面 |
| 其余实现类（`SDS_Ledger` / `SDS_StockData` / `SDS_Ownership` 等） | 各 `SDS_*.cs` | internal，不构成对外面（《模块规则》五） |

判定层公开面 `SDS_Availability` / `SDS_ImplantUtility`，以及 `SDS_StockRules` / `SDS_StructureRegistry` / `SDS_EndpointContractCache` 自 P4 起位于 `SDS.Rules`；本模块只保留账本与数据原语。

成员级语义（前置条件 / 失败后状态 / 副作用 / 顺序）写在 `SDS.Core_API.cs` 的 XML 注释里；改签名或语义时以该文件为唯一编辑点。

### A.0 P3 / P4 迁出清单（历史记录）

以下成员自 P3 / P4 起改由 `SDS_Rules` 提供，本模块已删除；本文件只保留其账本原语说明：

| 已迁出 | 现入口 |
|---|---|
| P3：`CanDeposit` / `TryDeposit` / `TryWithdraw*` / `TryReserve*` / `ReleaseJobReservation` / `TryTakeMirror` / `TryConsumeMirror` / `ConvertPreservedGearToVirtual` / `Return*` / `*Produced*` | `SDS_Rules.*` |
| P4：`Available` / `TypeCapacity` / `EndpointVersion` / `TryGetStockKey` / `RequiresPreservedStorage` / `IsLegacyUnrebuildableDef` / `CategoryKeyOf` / `TryFindWithdrawableKey` / `TryFindTradableKey` / `GetAvailableCountForDef` / `SetExternalPreserveProvider` | `SDS_Rules.*` |
| P4：`RegisterStructure` / `UnregisterStructure` / `ReconcileStructures` / `NotifyEndpointChanged` | `SDS_Rules.*` |
| P4：`SDS_Availability` / `SDS_ImplantUtility` | `SDS.Rules` 的同名 public 门面 |

### A.1 本次增补的读 / 写口（2026-11-12）

> A.1–A.3 保留为历史记录；其中 P3 / P4 已迁出的成员以 A.0 为准，现行入口在 `SDS.Rules`。

| 方法 | 口径 | 消费方 |
|---|---|---|
| `TryFindWithdrawableKey(ThingDef, out, out)` | 生产 / 取用口径的唯一选择规则：精确键优先 → 可用量最大 → 保真同口径；禁用归零 | Vanilla 装载 / Api 兼容桥 / CE 兼容 |
| `TryFindTradableKey(ThingDef, out, out)` | 交易 / 捐献 / 发射口径（R15）：与上一行共用同一实现与选择顺序，但不因品类禁用归零 | Vanilla 信标 / 捐献 |
| `GetAvailableCountForDef(ThingDef)` | 同 def 的虚拟 + 保真可用量之和 | Vanilla 装载 / Api 兼容桥 |
| `CategoryKeyOf(SDS_StockKey)` | 按设置归零品质 / 材质的展示聚合键（唯一实现） | Vanilla 侧边栏分组 |
| `IsLegacyUnrebuildableDef(ThingDef)` | 旧口径不可重建黑名单（尸体 / 缩装 / 未完成 / 基因载体） | Vanilla 默认存入筛选 |
| `CopyKeysForDef(ThingDef, List)` / `CopyPreservedKeysForDef(ThingDef, List)` | 按 def 的虚拟 / 保真品类键索引；读侧按 def 查询的唯一入口 | Vanilla 装载 / Api 兼容桥 |
| `NotifyCapacitySettingsChanged()` | 品类分组口径变化时丢弃账本派生索引 | Bootstrap 设置页 |
| `IsPreservedThingId(int)` | 按 `thingID` 判定是否已有「存活且内容完整」的保真真身；读档缓冲去重与运行期重复入库 / 回充身份门的唯一 ID 级判定 | Vanilla 端点缓冲清理；`SDS_Ledger.IsPreservedIdentityOccupied` |
| `SetForbidden(SDS_StockKey, bool)` | 品类禁用唯一写入口；变化时推进 `StockVersion` | Vanilla 侧边栏锁形开关 |
| `ReconcileStructures(List<SDS_StructureFact>)` | 以活体事实集为准补齐 / 剔除 / 修正结构登记；有变化才推进端点版本 | Vanilla 250-tick 结构对账 |
| `TryConsumeMirror(Thing, out int)` | W22 外部销毁确认：只扣账、不预留、不足按现有量扣、0 也放行 | Vanilla 镜像销毁 |
| `ReturnVirtualCount(SDS_StockKey, int)` | 销毁未完成时的纯数量回充 | Vanilla 镜像销毁 Finalizer |
| `ReturnThing(..., SDS_ReturnFallback, IntVec3 cell)` | 回充落点：cell 只作用于 Ground 退路；无效 / 越界回退 pawn 脚下 / 地图中心 | Api 回充桥 |
| `TryWithdrawPreservedExactAdopted(int, List<ThingCount>, out, mode)` | 接管 Job 预留后的精确保真取出；失败不释放预留 | Vanilla 装载 Job |
| `AuditReservations(Func<int,bool> ownerAlive)` | 预留终老：Core 不认识 Job / Lord，判活委托由 Vanilla 传入 | Vanilla 存档壳 |

`HasLiveContent` 曾作为 `SDS_Rules` 公开门面；2026-11-16 复审后已内收为 `SDS_StockData` 内部原语，由 Core 与 Rules 经友元共用，不再出现在公开面。

`SDS_StructureRegistry.Clear` 同时用于新档静态清理（`SDS_Rules.RegisterAll` 第 4 项）与测试隔离（`SDS.Rules.Tests/Support/RulesTestKit` 反射调用）。

`UsedCategoryCount` / `HasCategory` 自 2026-11-14 起只存在于 `SDS_Ledger`（internal 派生索引），不再进入 `ISDS_LedgerRead`；容量口径的模块面入口是 `SDS_Core.UsedCapacity` / `SDS_Rules.TypeCapacity`。

### A.2 本次改语义的口径（2026-11-15）

| 方法 | 口径 | 消费方 |
|---|---|---|
| `TryDeposit` / `ReturnThing(Virtual)` | 一律走账本 `TryConvertThingToVirtual`（摘出 → 入账 → 销毁）；销毁未生效或抛异常即整笔回退，不出现账实各留一份。账本已持有同一实物时：**同引用**回充按幂等成功、不销毁 / 不落地；**同 thingID 的其他实例**回充拒绝且不落地（唯一判定 `ClassifyPreservedIdentity`） | Vanilla 存入 / 回充 |
| `TryWithdrawPreservedExact` / `TryReservePreservedForJob` | 计划条目必须 ≤ 该真身当前件数；执行前按 key 校验 `Σplan ≤ 本事务预留`；任一条目不足即整笔回滚（W21 足额），不静默少给 | Vanilla / Api |
| `TryWithdrawReservedForJob` / `TryWithdrawPreservedExactAdopted` | 接管路径异常只回充实物（`ReclaimMaterializedWithoutRelease`），不释放预留；预留由 Job 的 `FinishAction` 清理 | Vanilla Job |
| `AuditReservations(Func<int,bool> ownerAlive)` | 账本不再判活：只提供预留快照与按归属 id 释放；`id ≤ 0` 一律非法；判活委托由 Vanilla 传入（`JobExists` + `Pawn.Destroyed`）；由 Vanilla 在 `FinalizeInit` 与 250 tick 周期调用 | Vanilla 存档壳 |

### A.3 去模块耦合（2026-11-15）

| 方法 / 输入 | 口径 | 消费方 |
|---|---|---|
| `SetExternalPreserveProvider(Func<Thing,bool>)` | 第三方「必须保真」事实由组合根注入；Core 只持有委托，不引用 `SDS_PreserveHooks`；只允许加严 | Bootstrap 启动一次 |
| `SDS_EndpointQuery.priority` | 端点排序优先级由查询携带；Core 不读 `SDS_Host.Priority`；工厂方法代填，`default` 查询用 `ApplyHostPriority()` | `SDS.Rules` / Api / Vanilla |
| `TryTakeMirror(Thing, int, ISDS_MirrorPhysical, out Thing)` | 物理转换器由调用方注入（`SDS_WithdrawPlan.physical`）；Core 不读 `SDS_Host.MirrorPhysical` | Vanilla 镜像 / Api |


## B 节　本模块消费的契约

### B.1 Contracts 类型与成员

| 来源文件 | 类型 / 成员（签名以 Contracts 源码为准） | Core 用途 |
|---|---|---|
| `SDS_Stock.cs` | `public class SDS_StockKey : IExposable, IEquatable<SDS_StockKey>`；字段 `thingDef` / `stuff` / `qualityInt` / `defName` / `stuffName` / `preserved`；`Resolved` / `HasIdentity` / `EffectiveDefName` / `SameAs` / `TryResolve()` / `EnsureNames()` / `static CompareIdentity(a, b)` | 账本键 / 比较 / 解析 |
| `SDS_Stock.cs` | `SDS_StockEntry`：`SDS_StockKey key` / `long count` / `float avgHitPoints` / `float avgRotProgress`；`SDS_StockVitals`：`float hitPoints` / `float rotProgress`；`SDS_Reservation`：`int jobLoadID` / `SDS_StockKey key` / `int count` / `Pawn pawn`；`SDS_StockChannel { Virtual, Preserved }` | 快照 / 存档 / 预留 / 通道 |
| `SDS_EndpointContract.cs` | `SDS_StructureKind`、`SDS_StructureFact`（端点查询 / 决议 DTO 由 `SDS.Rules` 消费） | 结构事实 |
| `SDS_Withdraw.cs` | `SDS_WithdrawMode`、`SDS_SupplyFailure`、`SDS_WithdrawEndState`、`SDS_WithdrawQuery`、`SDS_WithdrawPlan`（含 `ISDS_MirrorPhysical physical`） | 状态机 / 取货计划 |
| `SDS_Search.cs` | `SDS_ReturnFallback { None, Ground, PawnInventory, PawnCarry }` | 回充退路 |
| `SDS_Host.cs` | `ISDS_DepositFilter`、`ISDS_DeliverySink`、`ISDS_MirrorPhysical`（仅作入参类型）、`ISDS_DiagnosticProvider` | 宿主事实 / 入参类型（`ISDS_Port` / `ISDS_PortProvider` 由 `SDS.Rules` 消费） |
| `SDS_Host.cs` | `SDS_MedicalGate.Allows(Thing billGiver, ThingDef def)` | Vanilla / Api 的医药门（Core 不调用） |
| `SDS_PreserveHooks.cs` | `RequiresPreserved(Thing)`；由 Bootstrap 经 `SetExternalPreserveProvider` 接入 | 组合根接线 |
| `SDS_Mod.cs` / `SDS_Implants.cs` | `SDS_Config.GridCapacity` / `SeparateByQuality` / `SeparateByStuff` / `ForceWeaponApparelVirtual`；`SDS_HediffDefOf.ChipA` / `ChipB` | 容量 / 键粒度 / 芯片 |

原版数据类型的接触点见 D.1。
`SDS_StockKey.ExposeData` 由 Contracts 实现；Core 只调用 `EnsureNames` / `TryResolve`。

### B.2 消费的宿主槽位（Core 直接消费 0 个；历史使用点均已迁 SDS.Rules）

| 槽位 | 接口 | Core 使用点 | 调用的成员 |
|---|---|---|---|
| `Ports` | `ISDS_PortProvider` | `SDS.Rules / SDS_EndpointResolver.Select`（P2 已迁出 Core） | `IsLocalUsable(Map)`、`AppendCandidates(in SDS_EndpointQuery, List<ISDS_Port>)` |
| `DepositFilter` | `ISDS_DepositFilter` | `SDS.Rules` 的 `SDS_StockRules.CanDeposit`（P4 迁出） | `Allows(Thing)` |
| `Delivery` | `ISDS_DeliverySink` | `SDS.Rules` 的 `SDS_ReturnUtility.DeliverFallback`（P3 迁出） | `TryPlace(Thing, Map, IntVec3)`、`TryGiveToPawn(Thing, Pawn, bool)` |

不消费：`Priority` / `MirrorPhysical` / `MedicalCare` / `ReclaimWorld` / `MirrorQuery` / `Display` / `DepositJobs` / `TransportLoad`。
其中 `Priority`、`MirrorPhysical`、`MedicalCare` 改由调用方以查询字段 / 计划字段 / 委托注入（见 A.3）；`ReclaimWorld` 随 C1 归 Vanilla。
Diag 侧：`SDS_Diag.Fail` / `Warn` / `ErrorOnce` / `Ctx` / `Assert*`；`SDS_StaticState.Register`；`SDS_SelfCheck.Register`。

## C 节　调用关系

| 方向 | 对象 | 入口 |
|---|---|---|
| 谁调 Core | `SDS.Vanilla` | `SDS_Core` 账本生命周期 / 已用品类数 / 禁用开关 / 状态写回；`ISDS_LedgerHost`（构造 / `ExposeData` / `Tick`）；端点、补料、结构、可用量、事务、回充、在途标记改调 `SDS_Rules` |
| 谁调 Core | `SDS.Api` | `SDS_Core` 账本生命周期 / 禁用开关 / 状态写回；`ISDS_LedgerRead` 只读；其余转发 `SDS_Rules` |
| 谁调 Core | `SDS.Bootstrap` / `SDS.Tests` / `SDS.Core.Tests` / `SDS.Rules.Tests` | `SDS_Core.RegisterAll`；W34 迁移改调 `SDS_Rules.ConvertPreservedGearToVirtual`；`SDS.Core.Tests` 只测账本，规则 / 流程测试在 `SDS.Rules.Tests` |
| Core 调谁 | 数据原语 | `SDS_StockData`（键 / 物化 / vitals）；`SDS_Config`；外部事实经参数注入 |
| Core 调谁 | 账本 / 回收 | `SDS_Ledger`；回充与回收账本归 Rules / Vanilla |
| Core 调谁 | 诊断 | `SDS_Diag`；`SDS_SelfCheck` 只由 `RegisterAll` 调用 |

## D 节　原版支持

> 本节只记 Core 与原版类型的接触点、不依赖的行为层与已知缺口；规则正文见 [Rules.zh-CN.md](Rules.zh-CN.md)，宿主槽位见 B.2。

### D.1 依赖的原版类型 / 成员

| 类型 / 成员 | Core 用它做什么 | 代码位置 |
|---|---|---|
| `Thing.def` / `Stuff` | 生成品类键 | `SDS_StockData.cs` |
| `Thing.stackCount` | 数量口径、预留、回收计数、`ThingCount` 计划校验 | `SDS_Ledger.cs`；（回收计数见 Vanilla `SDS_ReclaimRegistry.cs`） |
| `Thing.Destroyed` / `Spawned` / `holdingOwner` | 存活判据；所有权转移只经 `SDS_Ownership.TryTransferTo`（禁止直接置空 `holdingOwner`）；回收巡检（Vanilla 侧） | `SDS_Ownership`、`SDS_Ledger.TryDepositPreserved`；（`SDS_ReclaimRegistry.Sweep` 在 Vanilla） |
| `Thing.SplitOff` / `Destroy` | 保真拆堆取用；虚拟入账成功后消化临时实例 | `SDS_Ledger.TakePreservedUnits`；回充侧见 `SDS.Rules/SDS_ReturnUtility.cs` |
| `ThingMaker.MakeThing` | 按品类键重建未 spawn 的实物（数据构造，不是生成行为） | `SDS_StockData.MakeThing` |
| `Map` | 结构事实所属地图（Rules）；账本自身不持有地图 | `SDS.Rules/SDS_StructureRegistry.cs` |
| `Pawn` | 预留归属 | `SDS_Reservation.pawn`；芯片端口在 `SDS.Rules` |
| `ThingOwner<Thing>` | 保真真身的唯一持有容器；`TryAdd` / `Remove` / `Contains` / 计数 | `SDS_Ledger.preservedOwner` |
| `ThingCount` | 保真精确取用计划（`Thing` + 数量，Core 只做账本校验） | `SDS.Rules/SDS_WithdrawExecutor.cs` |
| `Corpse` / `ThingDef.IsCorpse` | 保真通道的遗留不可重建类型；补料尸体过滤 | `SDS.Rules/SDS_StockRules.cs` |
| `GenTicks.TicksGame` | 来源标记的寿命时钟（1 天兜底，W49） | `SDS.Rules/SDS_ProducedThingMarker.cs` |

只读能力位（同样属于数据语义，不产生行为）：

- `ThingDef.IsStudiable`（W35 逐件跟踪型）、`ThingDef.EverHaulable`、`ThingDef.stackLimit`、`ThingDef.useHitPoints`。
- `CompQuality` / `CompRottable` / `CompArt` / `Apparel.WornByCorpse`：读取状态或写回堆平均状态。
- `ReliquaryUtility.IsRelic`：圣物探针，宿主未就绪时降级为「不是圣物」（`SDS_StockRules.IsRelicSafe`）。
- `SDS_HediffDefOf` 的两个芯片 Hediff 与 `Pawn.health.hediffSet.hediffs`：端口状态查询。

### D.2 原版判定的接触点（非规则定义）

| 规则点 | 原版判定 | 对应实现（P4 后） |
|---|---|---|
| 可存口径 | `category == Item && !IsUnfinishedDef` | `SDS.Core/SDS_StockData.cs` 的 `CanStoreDef` / `CanStore`（Rules 经友元调用） |
| 容量 | 虚拟与保真共用一个品类数池；超出拒绝新品类 | `SDS_Rules.TypeCapacity`（基础值 + 稳固装置加成，两者都按 `SDS_Config` 现读）与 `SDS_Core.UsedCapacity`（`CategoryKeyOf` 去重） |
| 未解析条目 | 供给归零，条目保留，恢复 def 后可再用 | `SDS_StockKey.Resolved` + `SDS_Availability.GetFailure`（Rules）返回 `UnresolvedDef` + `SDS_Ledger.UnresolvedSnapshot`（Core） |
| 使用命令的三道门 | 自身端口可用、品类未禁用、物品类别可「使用」 | `SDS_Rules` 的 `SDS_ImplantUtility.IsPortOperational`、`RequiresPreservedStorage`；`IsForbidden`（Core）；门控顺序与 UI 在 Vanilla |

通道分流、可用量、外部销毁、回滚通道、医疗候选的口径已在 [Rules.zh-CN.md](Rules.zh-CN.md) §五–§八、§十一 与 `SDS.Rules/Rules.zh-CN.md` §一 定义，本节不再重复。

### D.3 不依赖的行为层

- 不引用、不调用：Harmony、`Find.*`、`ListerThings` / `ThingRequestGroup`、`GenClosest` / `Reachability`、`Job` 行为（只出现 `job.loadID` 作预留键）、`SlotGroup` / `IHaulDestination`、`GenPlace` / `GenSpawn`、`ResourceCounter`。可检查的禁止符号清单见 [Rules.zh-CN.md](Rules.zh-CN.md) §一。
- 结论：Core 的行为面为零，替换点是 8 个宿主事实接口（E.1）；这也是 `CheckAssemblyBoundaries.ps1` 禁止符号检查可执行的前提。

### D.4 为什么可以脱离游戏环境验证

- `SDS_Ledger` 构造无参，`SDS_Core.Attach` 只做引用赋值；账本与数据原语都不依赖 `Game`。
- 回充失败路径只依赖 `ISDS_DeliverySink` 的返回值（P3 起在 `SDS.Rules`）；不会触发 `GenPlace` / `GenSpawn`。
- 端点决议、补料、判定层与流程层已迁 `SDS.Rules`，其脱离游戏验证见 `SDS.Rules/Interfaces.zh-CN.md`。

### D.5 已知缺口与边界

- 未解析条目的口径：`Snapshot` **不跳过**未解析条目，由消费方按 `entry.key.thingDef == null` / `key.Resolved` 过滤；`UnresolvedSnapshot()` 只供诊断与旧档核对（Vanilla 用量窗口单独展示）。2026-11-16 复审已核对 `SDS.Vanilla` 各 `Snapshot(0)` 调用点均显式过滤未解析键。
- 容量粒度：`UsedCapacity` = `CategoryKeyOf`（def + 材质 + 品质 + 通道）去重后的品类数；同一品类多件 / 多真身只占一个名额。“归属组”不是独立容量维度；见 `SDS.Rules/Rules.zh-CN.md` §12.24 与本文件 §12.29。
- 测试覆盖：`SDS.Core.Tests` 覆盖 `ConservationTests`（随机虚拟数量守恒）、`InvariantTests`（E2 / SelfCheck 入口）、`UnresolvedVitalsTests`（未解析 def 的 vitals 往返）与 `LedgerCoreTests`（容量 thingID 去重、保真预留 / 取出、预留终老、禁用持久化）；账本结构 / 预留 / 存档往返在 `SDS.Rules.Tests/Core`（`LedgerStructureTests`、`ReservationTests`、`ScribeRoundTripTests` 等），跨模块集成在 `SDS.Tests`。保真 / 预留的规则侧用例仍经 `RulesTestEnv` 驱动，与 Core 纯账本用例分层。
- Core 仍需原版 `Thing` 实例参与 `ThingOwner` 与 `SplitOff`，无法完全脱离 RimWorld 程序集；「脱离游戏环境」指不启动 `Game` / 不加载地图 / 不装 Harmony。
- `ReliquaryUtility.IsRelic`、`ModsConfig`、`DefDatabase` 属于宿主环境探针，失败时按降级值处理，不阻塞 Core 判定。
- 任何新增的原版行为调用都应先判断能否改为宿主事实接口；当前例外只有上述只读能力位与 `ThingMaker.MakeThing`（《ModuleRules.zh-CN.md》6.3）。

## E 节　第三方支持

> 本节只记第三方进入 Core 的调用面与登记点；可检查约束见 [Rules.zh-CN.md](Rules.zh-CN.md) §四、§八。

### E.1 宿主事实接口与 Vanilla 实现

| 接口 | 提供的事实 | Vanilla 实现 |
|---|---|---|
| `ISDS_PortProvider` | 候选端点、本图可用性、自身端口 | `SDS_EndpointProvider` |
| `ISDS_DepositFilter` | 全局存入筛选、筛选版本 | `SDS_VanillaServices` |
| `ISDS_PrioritySource` | 存储优先级、是否忽略优先级、版本 | `SDS_VanillaServices` |
| `ISDS_DeliverySink` | 落地、进背包、进容器 | `SDS_VanillaServices` |
| `ISDS_MirrorQuery` | 镜像可见性、版本、`IsMirror`（Core 当前未直接调用） | `SDS_MirrorService` |
| `ISDS_MirrorPhysical` | 镜像形态转换（部分 / 整堆） | `SDS_MirrorPhysicalOps` |
| `ISDS_MedicalCarePolicy` | 医疗等级是否允许某药品 | `SDS_MedicalCareAdapter` |
| `ISDS_ReclaimWorld` | 当前 tick、Job / Lord 存在性、离队 | `SDS_ReclaimWorldAdapter` |

Core 侧消费点见 B.2（直接消费 0 个槽位；历史上的 `DepositFilter` / `Delivery` / `Ports` 已随 P2–P4 迁 `SDS.Rules`）；`Priority` / `MirrorPhysical` / `MedicalCare` 由调用方以查询字段 / 计划字段 / 委托注入，`ReclaimWorld` / `MirrorQuery` / `Display` / `DepositJobs` / `TransportLoad` 由 Vanilla 或 Api 使用。
接口只返回事实、不返回规则：例如 `ISDS_Port` 只回答可用性、可达与距离平方，排序仍由 `SDS.Rules` 的 `SDS_EndpointResolver.IsBetter` 决定（《ModuleInterfaces.zh-CN.md》§3.3）。
注入时机：Bootstrap 启动第 2 步（《ModuleRules.zh-CN.md》九）；任一接口为 null 时的行为见《ModuleInterfaces.zh-CN.md》§1.2。

### E.2 第三方的调用面

- 第三方可见面只有 `SDS_API` 五轴（看 / 读 / 找 / 取 / 存）与适配器桥；适配器桥只做组合，不新增排序 / 容量 / 扣账 / 派活规则。Core 不区分调用方身份，Vanilla 与第三方走同一套契约。
- 每个第三方入口只能有一条写接管路径；只读投影可以与适配器翻译共存。
- 适配器职责限定为「读第三方状态 / 翻译参数 / 调门面 / 翻译结果」；出现「选哪个、多少、够不够、先后顺序、回滚顺序」即属于规则，必须上收到 Core。
- 目标 Mod 只能反射探测，不得硬引用其程序集；日志只用 `SDS_Diag.ErrorOnce` / `Warn`，不得调用 `SDS_Diag.Fail`。
- 结果：Core 只面对上述事实接口与一组门面方法，增加一个第三方 Mod 不需要修改 Core，也不需要新增分支。

### E.3 `SDS_PreserveHooks`：第三方登记保真事实

- 位置：Contracts 的纯注册表 `SDS_PreserveHooks`（《ModuleInterfaces.zh-CN.md》§1.3）。
- 登记内容：一个 `Func<Thing, bool>` 探针，回答「该实物是否必须保留真身」；空或重复登记返回 false。
- 消费点：`SDS.Rules` 的 `SDS_StockRules.RequiresPreservedStorage(Thing)` 末尾，与 B1 黑名单、`IsProtectedThing`、`HasInstanceNonKeyState` 并列（同一文件，按符号定位）。
- 隔离：单个探针异常记一次 `Log.ErrorOnce` 后跳过，不影响其它探针与 Core 分流（实际实现使用固定消息与 ID，见 `SDS_PreserveHooks.cs`）。
- 边界：探针只提供事实，不扣账、不派活、不建立第二条写路径；注册项是进程级常量，不登记 `SDS_StaticState`。
- 意义：第三方新增「必须保真」的物品类型时，只需登记判定，不需要在 Core 内新增 defName / mod 名单。

### E.4 `SDS_BillSupplyPlanner`：缺口计算的唯一出口（已迁 SDS.Rules）

- 位置：P2 起在 `SDS.Rules` internal；S16 规则正文与接口见 `SDS.Rules/Rules.zh-CN.md`、`SDS.Rules/Interfaces.zh-CN.md`。
- Core 只保留账本能力，不再定义补料规则；存活内容判据统一在 Core 的 `SDS_StockData.HasLiveContent`，Rules 经友元直接调用，不再有 `SDS_Rules` 公开门面。

### E.5 `SDS_JobFuelCount`（2026-11-16 已删除）

- 原为 Contracts 纯注册表，按 `JobDef.driverClass` 注册整备 Job 剩余需求量。
- 方案乙落地后由 W58（数量缺失按预留意图补齐）统一覆盖，Core 不再提供任何配方的取量注册入口。

### E.6 为什么第三方不直接调用 Core internal 类型

- 物理上不可达且可审计：`InternalsVisibleTo` 只授予 `SDS.Rules`（P3 起，登记在 `CheckAssemblyBoundaries.ps1` §5）；`CheckCompatSurface.ps1` 只允许 Api / Contracts / Diag，禁止 Core / Vanilla；结论见 [Rules.zh-CN.md](Rules.zh-CN.md) §四。
- 收益：internal 类型可改签名而不破坏第三方，对外契约只由 `SDS_API` 的 Version 管理；失败语义统一为 `SDS_ApiResultCode` 与只读快照，异常不外泄。
