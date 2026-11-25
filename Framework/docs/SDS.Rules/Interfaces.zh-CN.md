# SDS.Rules · 接口

> 定位：本模块的消费面与签名索引。签名与成员语义的唯一权威是 `SDS.Rules_API.cs`；本文不抄写签名。
> **当前状态（P5，迁移完成）**：`SDS_Rules` 承载端点决议、账单补料、判定层与流程层；`SDS_Availability` / `SDS_ImplantUtility` 也位于本程序集。

## A 节　签名索引

| 类型 | 声明处（唯一权威） | 用途 |
|---|---|---|
| `SDS_Rules` | `Source/SDS.Rules/SDS.Rules_API.cs` | 规则模块唯一门面；P2 / P3 / P4 成员见 A.1 / A.2 / A.3 |

### A.1 P2 门面成员

| 方法 | 口径 | 消费方 |
|---|---|---|
| `SelectEndpoint(in SDS_EndpointQuery)` | 候选 → 静态门 → 优先级 / 距离 / 自身排序 → 只对胜出候选判可达 | Api / Vanilla |
| `AllowsDef(...)` | 配方槽 / def 是否接受；医药门由 `extraAllows` 注入 | Api 补料桥 / Vanilla |
| `TryPlanSlot(...)` | 单槽补料规划；只产出计划，不改账本 | Api 补料桥 |
| `TryPlanAllowedDefSupply(...)` | def 列表补料规划；与单槽共用分配策略 | Api 补料桥 |
| `NotifyDeposited(pool, def, amount)` | 只更新调用方自有池计数 | Api 补料桥 |

### A.2 P3 门面成员（流程层）

| 方法 | 口径 | 消费方 |
|---|---|---|
| `CanDeposit(Thing)` / `TryDeposit(Thing, ISDS_Port, out int)` | 存入可行性 / 落账；虚拟通道实转虚 | Api / Vanilla |
| `TryWithdraw(...)` / `TryWithdrawPreservedExact(...)` / `TryWithdrawPreservedExactAdopted(...)` / `TryWithdrawReservedForJob(...)` | 取货事务与 Job 接管（W21 / W24） | Api / Vanilla |
| `TryReserveForJob(...)` / `TryReservePreservedForJob(...)` / `ReleaseJobReservation(int)` | Job 预留生命周期 | Vanilla |
| `TryTakeMirror(...)` / `TryConsumeMirror(Thing, out int)` | 镜像取出 / 外部销毁确认（W22） | Api / Vanilla |
| `ConvertPreservedGearToVirtual()` | W34 玩家确认后的存量迁移 | Bootstrap |
| `ReturnThing(...)`（两重载）/ `ReturnVirtualCount(...)` / `ReturnThings(...)` | 回充与失败退路（W27 / W49） | Api / Vanilla |
| `IsProducedMarked` / `MarkProduced` / `UnmarkProduced` | 在途物化标记（W49） | Api / Vanilla |
| `RegisterAll()` | 登记 4 个静态清理项与 Rules 自检提供者（E1） | Bootstrap |

### A.3 P4 门面成员（判定层）

| 成员 | 口径 | 消费方 |
|---|---|---|
| `Available` / `TypeCapacity` | 总开关 G / 品类容量上限（结构注册表） | Api / Vanilla / Rules |
| `EndpointVersion` | 端点契约版本时钟 | Api / Vanilla |
| `TryGetStockKey` / `RequiresPreservedStorage` / `IsLegacyUnrebuildableDef` / `CategoryKeyOf` | 通道 / 键 / 品类归属判据 | Api / Vanilla / Rules |
| `TryFindWithdrawableKey` / `TryFindTradableKey` / `GetAvailableCountForDef` | 按 def 选键 / 总量（R15） | Api / Vanilla |
| `SetExternalPreserveProvider` | 第三方保真事实注入 | Bootstrap |
| `RegisterStructure` / `UnregisterStructure` / `ReconcileStructures` / `NotifyEndpointChanged` | 结构事实 / 端点版本 | Vanilla / Bootstrap |
| `SDS_Availability.*` | 可用量 / 失败分类 / 夹取（W22 / W56 / R15 / R17） | Api / Vanilla |
| `SDS_ImplantUtility.*` | 芯片端口事实 | Api / Vanilla |

## B 节　本模块消费的契约

P2–P4 实际消费：

| 来源 | 类型 / 成员 | 用途 |
|---|---|---|
| Contracts `SDS_EndpointContract.cs` | `SDS_EndpointOp`、`SDS_EndpointQuery`、`SDS_EndpointDecision`、`SDS_StructureFact`、`SDS_StructureKind` | 端点决议 / 结构事实 |
| Contracts `SDS_Host.cs` | `ISDS_Port`、`ISDS_PortProvider`、`ISDS_DeliverySink`、`SDS_HediffDefOf` | 候选枚举 / 存入端口 / 回充退路 / 芯片 def |
| Contracts `SDS_Stock.cs` | `SDS_StockKey`、`SDS_StockChannel`、`SDS_StockVitals`、`SDS_StockEntry` | 取货键 / 回充通道 / 事务状态写回 / 自检快照 |
| Contracts `SDS_Withdraw.cs` | `SDS_WithdrawPlan`、`SDS_WithdrawQuery`、`SDS_WithdrawMode`、`SDS_WithdrawEndState`、`SDS_SupplyFailure` | 取货事务 / 计划 / 供给失败分类 |
| Contracts `SDS_Search.cs` | `SDS_ReturnFallback` | 回充退路 |
| Contracts `SDS_Mod.cs` | `SDS_Config`（容量 / 键粒度 / 强制虚拟化开关） | 结构容量 / 通道判定 |
| RimWorld / Verse | `Bill`、`RecipeDef`、`IngredientCount`、`ThingFilter`、`ThingDef`、`ThingCount`、`Thing`、`Pawn`、`Map`、`IntVec3` | 补料槽位判定 / 事务 / 回充 |
| Core（public） | `SDS_Core.UsedCapacity`；`ISDS_LedgerRead` / `ISDS_LedgerHost` | 容量门 / 账本只读 |
| Core（internal，Core → Rules 友元） | `SDS_Ledger`、`SDS_CoreImpl.Ledger`、`SDS_StockData` | 账本读写 / 键构造 / 物化数据原语 |

P5 已完成：判定层实现与 `SDS_Availability` / `SDS_ImplantUtility` 公开面均在本程序集；Core 只保留账本与 `SDS_StockData`。

## C 节　调用关系

| 方向 | 对象 | 入口 |
|---|---|---|
| 谁调 Rules | `SDS.Api` | 端点 / 补料桥 / 取存 / 回充 / 判定层转发 |
| 谁调 Rules | `SDS.Vanilla` | 端点决议、结构事实、芯片、可用量、取货事务、Job 预留、镜像、回充、在途标记 |
| 谁调 Rules | `SDS.Bootstrap` | `RegisterAll`；`SetExternalPreserveProvider`；W34 确认后的 `ConvertPreservedGearToVirtual` |
| Rules 调谁 | `SDS.Core` | 账本读写（`SDS_Ledger`）、已用品类数、键构造 / 物化数据原语（`SDS_StockData`） |
| Rules 调谁 | Contracts 宿主槽位 | `Ports`（端点候选）、`DepositFilter`（可存）、`Delivery`（回充退路）；`Priority` / `MirrorPhysical` / `MedicalCare` 只经入参 |
| Rules 不调 | `SDS.Vanilla`、`SDS.Api`、Harmony | 依赖方向禁止 |

## D 节　原版支持

已迁入的原版接触点：

- `SDS_EndpointResolver`：只读 `ISDS_Port` 的五个事实（可用 / 自身 / 可达 / 距离平方 / `Thing`（平局 thingID）），不直接寻路。
- `SDS_BillSupplyPlanner`：`Bill` / `RecipeDef` / `IngredientCount` / `ThingFilter` 的槽位判定与尸体过滤；不读工作台 / Job。
- `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor`：只经 `ThingCount` / `Thing` / `Pawn` / `Map` 与账本交互；不寻路、不派 Job、不写 UI。
- `SDS_ReturnUtility`：回充退路只经 `ISDS_DeliverySink` 与 `Thing` 的原版操作；不调用 `GenPlace` / `GenSpawn`。
- `SDS_ProducedThingMarker`：只读 `GenTicks.TicksGame` 作寿命时钟。
- `SDS_StockRules`：只读 `ThingDef` 能力位 / 组件与 `Thing` 实例状态；不寻路、不派活。
- `SDS_ImplantUtility`：只读 `Pawn.health` / 阵营 / 地图与 `SDS_HediffDefOf`。
- `SDS_StructureRegistry`：只读 `Thing` 引用 / 地图 / `SDS_Config`。

## E 节　第三方支持

- 第三方不直接引用 Rules；只经 `SDS_API` 五轴与适配器桥。
- Rules 不认识具体 Mod，也不新增 defName 分支；外部事实只以入参 / 委托进入。
