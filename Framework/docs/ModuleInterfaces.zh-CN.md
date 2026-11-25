# 模块接口

> 定位：**跨模块共享契约的登记处**。各模块的对外签名以 `SDS.*/*_API.cs` 为唯一权威；各模块 `Interfaces.zh-CN.md` 只写消费语义。本文只保留三类跨模块内容：共享契约索引、适配器桥登记（机器校验读取）、存档锁定清单。
> 必须遵守的约束见《ModuleRules.zh-CN.md》；模块分工见《ModuleResponsibilities.zh-CN.md》。

## 一、SDS.Contracts

跨模块共享类型（枚举、DTO、宿主接口、DefOf、注册表、设置）全部定义在 Contracts，是唯一允许被所有模块共同引用的语言。

签名全文 → [SDS.Contracts/Interfaces.zh-CN.md](SDS.Contracts/Interfaces.zh-CN.md)（原 1.1 枚举与 DTO、1.2 端口与宿主接口、1.3 DefOf / 注册表 / 设置）。使用方按模块各自内联其实际引用的子集：见各模块 `Interfaces.zh-CN.md` 的消费契约节（B 节 / 消费面 / 调用方）。

`SDS_Host` 的 11 个槽位与「谁提供 / 谁消费」：

| 槽位 | 提供方 | 消费方 |
|---|---|---|
| `Ports` | Vanilla | Rules（端点决议；P2 从 Core 迁出） |
| `DepositFilter` | Vanilla | Rules（`SDS_StockRules.CanDeposit`；P4 从 Core 迁出） |
| `Priority` | Vanilla | Vanilla / Api（读设置后写入 `SDS_EndpointQuery.priority`；Core 不读槽） |
| `Delivery` | Vanilla | Rules（回充 / 落地 / 进手；P3 随 `SDS_ReturnUtility` 迁入） |
| `MirrorQuery` | Vanilla | Api、Vanilla（自身暴露口径） |
| `MirrorPhysical` | Vanilla | Vanilla / Api（经 `TryTakeMirror(..., physical, ...)` 传入；Core 不读槽） |
| `MedicalCare` | Vanilla | Vanilla / Api（S17 医药门经 `SDS_MedicalGate`；Core 不读槽） |
| `ReclaimWorld` | Vanilla | Vanilla（回收账本 / Job·Lord 事实；Core 不消费） |
| `Display` | Vanilla | ThirdModCompat（经 Api 桥） |
| `DepositJobs` | Vanilla | ThirdModCompat（经 Api 桥） |
| `TransportLoad` | Vanilla | ThirdModCompat（经 Api 桥） |

> P2–P4 规则迁入 `SDS.Rules`：`Ports`（P2）、`Delivery`（P3）、`DepositFilter`（P4）均已改由 Rules 消费；Core 不读宿主槽位。

## 二、SDS.Diag

`SDS_Diag` 方法表、`ISDSResettable` / `SDS_StaticState`、报告 DTO 与 `Fail` 的调用方限制 → [SDS.Diag/Interfaces.zh-CN.md](SDS.Diag/Interfaces.zh-CN.md) 与 [SDS.Diag/Rules.zh-CN.md](SDS.Diag/Rules.zh-CN.md)（原 2.1 / 2.2）。

## 三、SDS.Core

账本、数据原语、Core 存档 → [SDS.Core/Interfaces.zh-CN.md](SDS.Core/Interfaces.zh-CN.md)（原 3.1 账本、3.6 Core 存档）；端点决议、账单补料（P2）、流程层（P3）、判定层（P4）→ [SDS.Rules/Interfaces.zh-CN.md](SDS.Rules/Interfaces.zh-CN.md)。

规则模块（P4，迁移完成）：`SDS.Rules` 已承载全部规则 / 流程与 `SDS_Availability` / `SDS_ImplantUtility`；Core 只保留账本与 `SDS_StockData`。`SDS_API` 桥签名不变，转发目标改为 Rules。

## 四、SDS.Vanilla

存档壳、宿主机实现约束、镜像物理契约、Vanilla 存档 → [SDS.Vanilla/Interfaces.zh-CN.md](SDS.Vanilla/Interfaces.zh-CN.md)（原 4.1 存档壳、4.2 宿主实现约束、4.3 镜像物理操作、4.4 Vanilla 存档）。

## 五、SDS.Api（适配器桥登记）

`SDS_API.Version = 4`。五轴签名与转发目标见 [SDS.Api/Interfaces.zh-CN.md](SDS.Api/Interfaces.zh-CN.md)。

适配器桥（public，不进入五轴契约）：跨程序集没有 `InternalsVisibleTo`，`SDS.ThirdModCompat` 需要的工具必须以 public 方法挂在 `SDS_API` 上；它们不新增排序 / 容量 / 扣账 / 派活规则。按 def 批量取只是对同一事务 / 精确真身计划原语做逐键组合与整批回充。本表是桥面的唯一登记处，`CheckCompatSurface.ps1` 从这里读取桥方法名。

| 类别 | 方法 |
|---|---|
| 端点 / 可用性 | `HasAnyEndpoint(Map)` / `HasUsableBuildingEndpointWithin(Map, IntVec3, float)` / `IsDepositEndpoint(Thing)` / `IsGridUsable(Map)` / `IsManualDepositAvailable(Pawn)` / `IsPawnPortOperational(Pawn)` / `TryGetWithdrawCell(Pawn, out IntVec3, out SDS_ApiResultCode)` |
| 账本只读 | `IsGridAvailable()` / `AppendPreservedThings(List<Thing>)` / `IsLivePreservedThing(Thing)` / `IsStockForbidden(SDS_StockKey)` / `TryGetStockKey(Thing, out SDS_StockKey)` / `GetAvailableCount(SDS_StockKey)` / `GetAvailableCountForDef(ThingDef)` / `GetPreservedAvailableCount(ThingDef)` / `TryFindWithdrawableKey(ThingDef, out SDS_StockKey, out int)` / `ClampTakeToAvailable(int, long)` / `IsForceWeaponApparelVirtual()` / `IsBillIngredientAllowed(...)` |
| 展示 / 镜像 | `IsMirror(Thing)` / `IsDisplayThing(Thing)` / `TryGetDisplayKey(Thing, out SDS_StockKey)` |
| 来源 / 通道 | `IsProducedPendingConsumption(Thing)` / `MarkProducedPendingConsumption(IList<Thing>)` / `IsDepositJob(Job)` / `RequiresPreservedStorage(ThingDef)` |
| Job 派活 | `TryTakeDepositCandidate(Pawn, Thing, bool, out SDS_ApiResultCode)` / `TryMakeDepositJob(Pawn, Thing, bool, Job, bool, out Job, out SDS_ApiResultCode)` / `TryMakeDepositJobFromInventory(Pawn, IList<Thing>, out Job, out SDS_ApiResultCode)` |
| 按 def 批量取 | `TryWithdrawVirtualByDef(...)` / `TryWithdrawPreservedByDef(...)` |
| 补料规划 | `TryPlanBillSupplySlot(...)` / `TryPlanAllowedDefSupply(...)` / `NotifyBillSupplyDeposited(...)` |
| 回充 | `TryReturnPreserved(Thing, Map, IntVec3)` / `TryReturnVirtual(Thing, Map, IntVec3, bool landOnFailure = true)` |
| 装载 | `InjectTransportLoadCandidates(ThingWithComps, List<TransferableOneWay>)`（W50） |

## 六、SDS.ThirdModCompat

Sdk 工具类签名、9 个适配器的 `Name` / `TargetPackageId` / `ISDSResettable` / `SDS_API` 调用清单 → [SDS.ThirdModCompat/Interfaces.zh-CN.md](SDS.ThirdModCompat/Interfaces.zh-CN.md)（原 6.1 / 6.2）。

## 七、SDS.Bootstrap

`SDS_Mod` 的 public 面、启动 7 步的实际调用点、11 个注入对象 → [SDS.Bootstrap/Interfaces.zh-CN.md](SDS.Bootstrap/Interfaces.zh-CN.md)（原 7.1）。启动顺序规则见《ModuleRules.zh-CN.md》九。

## 八、存档锁定清单

### 8.1 必须锁定的类型全名

```text
SDS_GameComponent_Grid
SDS_StockKey
SDS_StockEntry
SDS_Reservation
SDS_ReclaimSave
SDS_TransportLoadSave
SDS_SidebarUIState
SDS_EndpointBuffer
SDS_Settings
SDS_WithdrawDeliveryPolicyDef
SDS_MirrorJobTargetSave
```

待复核：`SDS_ProjectionOwner`（无 ExposeData，复核后决定是否移除）。

### 8.2 Defs XML 引用类型

```text
Building_SDS_Core
Building_SDS_Port
Building_SDS_CapacityArray
Building_SDS_Vault
SDS_CompUsable
SDS_CompProperties_InstallChip
SDS_CompInstallChip
SDS_Comp_SDS_DrawExtraOverlay
CompProperties_SDS_DrawExtraOverlay
SDS_Proxy_Thing
SDS_Proxy_ThingWithComps
SDS_Proxy_Apparel
JobDriver_SDS_WithdrawFromGrid
JobDriver_SDS_DepositToGrid
JobDriver_SDS_WithdrawPreservedForBill
```

### 8.3 深序列化字段与写入节点

字段、`LookMode` 与节点名的唯一权威是各模块 `Interfaces.zh-CN.md`：Core 见 [SDS.Core/Interfaces.zh-CN.md](SDS.Core/Interfaces.zh-CN.md)，Vanilla 见 [SDS.Vanilla/Interfaces.zh-CN.md](SDS.Vanilla/Interfaces.zh-CN.md)。本文只锁定「哪些类型必须可存读」，不重复字段表。
