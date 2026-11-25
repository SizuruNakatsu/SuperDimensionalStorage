> 定位：本文件只登记 SDS.Api 的消费面、签名索引与对外能力边界；签名权威见 `SDS_API*.cs`。规则见 [Rules.zh-CN.md](Rules.zh-CN.md)，历史决策与坑见 [DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md)。

# SDS.Api 接口

## 一、五轴 public 签名（A）

签名与成员语义的唯一权威：`Source/SDS.Api/SDS_API.cs`（`Version = 4`）。五轴属于 `SDS_API.Version` 契约（《ModuleResponsibilities.zh-CN.md》七）；本文不抄写签名。

## 二、适配器桥 public 签名（A）

签名与成员语义的唯一权威：`Source/SDS.Api/SDS_API_Adapters.cs`。桥为 public，但不进入五轴 `Version` 承诺（《ModuleInterfaces.zh-CN.md》五）。

桥方法按组登记：端点 / 可用性、账本只读、展示 / 镜像、来源 / 通道、Job 派活、按 def 批量取、补料规划、装载 / 回充。方法名与登记见《ModuleInterfaces.zh-CN.md》五（`CheckCompatSurface.ps1` 读取的同一张表），本文不重复抄写。

## 三、消费的 Contracts 类型（B）

以下类型均在 `SDS.Api` 源码中实际引用（grep 核实）。

| 组 | 类型 | 用途 / 定义处 |
|---|---|---|
| 调用与结果 | `SDS_ApiCaller`、`SDS_ApiResultCode`、`SDS_ApiStockInfo`、`SDS_ReadVersions`、`SDS_SupplyFailure` | 调用方上下文 / 结果码 / 只读快照 / 版本快照 / 失败分类（`SDS_API.cs`、`SDS_Withdraw.cs`） |
| 库存 | `SDS_StockKey`、`SDS_StockChannel` | 品类键 / 虚拟·保真通道（`SDS_Stock.cs`） |
| 取货 | `SDS_WithdrawMode`、`SDS_WithdrawQuery`、`SDS_WithdrawPlan` | 交付模式 / 查询 / 已提交计划（`SDS_Withdraw.cs`） |
| 端点 | `SDS_EndpointOp`、`SDS_EndpointQuery`、`SDS_EndpointDecision` | 端点语义 / 查询 DTO / 决议结果（`SDS_EndpointContract.cs`） |
| 宿主接口 | `ISDS_Port`、`ISDS_PortProvider`、`ISDS_MirrorQuery`、`ISDS_DisplayQuery`、`ISDS_DepositJobs`、`ISDS_TransportLoadSink`、`SDS_Host`、`SDS_MedicalGate` | Api 直接消费的宿主事实与医药门（`SDS_Host.cs`） |
| 设置 | `SDS_Config` | 只读 W33 开关，供兼容层缓存失效判定（`IsForceWeaponApparelVirtual`） |
| Job / 回充 | `SDS_JobDefOf`、`SDS_ReturnFallback` | `DepositToGrid` 判定 / 回充退路（`SDS_CaravanTransport.cs`、`SDS_Search.cs`） |

基础类型只来自 RimWorld / Verse：`Thing`、`ThingDef`、`ThingCount`、`Pawn`、`Map`、`IntVec3`、`Faction`、`ForbidUtility`、`Job`、`IngredientCount`、`Bill`、`RecipeDef`、`ThingFilter`、`ThingWithComps`、`TransferableOneWay`、`Find`。

## 四、消费的 SDS_Host 槽（B）

`SDS_Host` 共 11 个槽（《ModuleResponsibilities.zh-CN.md》三）；Api 直接读取其中 5 个。

| 槽 | 使用点 | 用途 |
|---|---|---|
| `MirrorQuery` | `VisibleThingsOfDef`、`CaptureReadVersions`、`IsMirror` | 可见实物 / 镜像版本 / 镜像身份 |
| `Ports` | `TryDepositAt`、`HasUsableBuildingEndpointWithin`、`IsDepositEndpoint`、`IsGridUsable` | 端口句柄、候选、本图可用性 |
| `DepositJobs` | `TryTakeDepositCandidate`、`TryMakeDepositJob`、`TryMakeDepositJobFromInventory`、`IsManualDepositAvailable` | Job 型存入门面 |
| `Display` | `IsDisplayThing`、`TryGetDisplayKey` | 展示物身份 |
| `TransportLoad` | `InjectTransportLoadCandidates` | 第三方装载清单注入 |

未被 Api 直接读取：`DepositFilter`、`Delivery`、`ReclaimWorld`；`MedicalCare` 只经 `SDS_MedicalGate` 间接取值，`MirrorPhysical` / `Priority` 以入参 / 查询字段形式消费。

## 五、消费的 Core / Diag 成员（B）

| 类型 | 成员 | 使用点 |
|---|---|---|
| `SDS_Core` | `Available`、`Ledger`、`EndpointVersion` | 全局可用性 / 账本 / 端点版本 |
| `SDS_Core` | `CanDeposit`、`TryDeposit` | 存 |
| `SDS_Rules` | `SelectEndpoint` | 找 |
| `SDS_Core` | `TryWithdraw`、`TryTakeMirror`、`TryWithdrawPreservedExact` | 取 |
| `SDS_Core` | `TryGetStockKey`、`RequiresPreservedStorage`、`TryFindWithdrawableKey`、`GetAvailableCountForDef` | 通道 / 可取出键 / def 级总可用量 |
| `SDS_Rules` | `AllowsDef`、`TryPlanSlot`、`TryPlanAllowedDefSupply`、`NotifyDeposited` | 补料规划 |
| `SDS_Core` | `ReturnThing`、`IsProducedMarked`、`MarkProduced` | 回充 / 来源标记 |
| `ISDS_LedgerRead` | `IsForbidden`、`GetCount`、`GetPreservedCount`、`CopyKeys`、`AppendPreservedThings`、`IsPreserved` | 账本只读 |
| `ISDS_LedgerRead` | `StockVersion`、`ReservationVersion`、`FilterVersion` | 读版本快照 |
| `SDS_Availability` | `GetAvailableCount`、`GetFailure`、`ClampTakeToAvailable` | 统一可用量 / 失败口径与取用夹取（W56） |
| `SDS_ImplantUtility` | `IsPortOperational` | Pawn 芯片端口可用性 |
| `SDS_Diag` | `ErrorOnce` | 异常只记一次；Api 不调用 `Fail` |

## 六、调用关系

```text
SDS.ThirdModCompat（Sdk 工具类 / 9 个适配器）
        │  只经 SDS_API（五轴 + 适配器桥）
        ▼
SDS.Api（SDS_API.cs 五轴 / SDS_API_Adapters.cs 桥）
        │  只转发
        ├─→ SDS.Core（SDS_Core / SDS_Ledger / SDS_Availability / SDS_ImplantUtility）
        ├─→ SDS.Host（Ports / MirrorQuery / DepositJobs / Display / TransportLoad）
        └─→ SDS.Diag（ErrorOnce）
```

外部第三方可与 `SDS.ThirdModCompat` 一样调用五轴；适配器桥按现状公开，但不属于 `Version` 承诺（《ModuleInterfaces.zh-CN.md》五、本文「八、第三方支持」）。

## 七、原版支持

### 7.1 与原版的关系：零补丁、零行为

- Api 的补丁数为 0：112 个特性补丁类全部在 Vanilla，9 个第三方适配器的动态补丁在 ThirdModCompat（《ModuleRules.zh-CN.md》七、八；《PatchInventory.zh-CN.md》）。
- Api 依赖的不是原版行为本身，而是原版对象上的**事实字段**与**判定函数**；真正的原版行为仍由 Vanilla 执行：Api 不直接调 `ListerThings`、`GenClosest`、`JobDriver`、`ThingOwner`、`GenPlace`；「看」转发 `SDS_Host.MirrorQuery`，「找」转发 `SDS_Rules.SelectEndpoint`，「取 / 存」转发 Core 的事务与入账入口。

### 7.2 五轴调用门用到的原版判定

| 判定 | 源码位置 | 用到的原版成员 | 作用 |
|---|---|---|---|
| 玩家阵营 | `IsPlayerFaction`、`IsPlayerPawn` | `Faction.OfPlayerSilentFail` | 存入 / 取出 / 找取物点要求玩家阵营；null 或异常返回 false |
| Pawn 有效性 | `IsPlayerPawn`、`TryWithdraw`、`TryWithdrawFromMirror` | `Pawn.Spawned`、`Pawn.Dead`、`Pawn.Map` | 拒绝未生成、已死亡或无地图的 Pawn |
| 实物所有权 | `IsOwnedByCaller` | `Thing.Faction`、`Thing.SpawnedOrAnyParentSpawned`、`Thing.SpawnedParentOrMe`、根 `Pawn.Faction` / `Thing.Faction` | 调用方、实物、根持有者必须同阵营；无主物品放行 |
| 禁止状态 | `ValidateDepositCall` | `ForbidUtility.IsForbidden(thing, caller.faction)` | 被禁止的实物拒绝存入 |
| 地图同一性 | `ValidateDepositCall`、`TryWithdrawFromMirror` | `Thing.MapHeld`、端点 `MapHeld`、`Pawn.Map` 与 `mirror.Map` | 实物与端点必须同图；从镜像取出时 Pawn 必须与镜像同图 |
| 当前 tick | `CurrentTick` | `Find.TickManager.TicksGame` | 写入 `SDS_ReadVersions.tick`，供只读快照一致性比较 |
| 参考格 | `TryFindDepositEndpoint` | `Thing.PositionHeld`、`Map.Center`、`IntVec3.IsValid` | 非 Pawn 存入时确定距离参考格 |

端点所有权另外由 `IsEndpointOwnedBy` 判定：端点的 `Thing.Faction` 为 null 或等于调用方阵营时才放行（`SDS_API.cs`）。

7 步顺序与「取出只含第 2、3 步」的检查清单见 [Rules.zh-CN.md](Rules.zh-CN.md) §二；存入任一步失败即返回对应 `SDS_ApiResultCode`，不再进入 Core 的端点选择或入账。

### 7.3 适配器桥额外用到的原版事实

| 事实 | 源码位置 | 原版成员 | 边界 |
|---|---|---|---|
| 端点身份与可用性 | `IsDepositEndpoint`、`TryDepositAt` | `SDS_Host.Ports.TryGetPort` + `ISDS_Port.Operational` | 经宿主接口取 Vanilla 事实；两处共用私有 `TryGetOperationalPort`，不直接引用建筑类型 |
| 本图可用性 | `IsGridUsable`、`HasUsableBuildingEndpointWithin` | `SDS_Host.Ports.IsLocalUsable` | 同上 |
| 半径可达 | `HasUsableBuildingEndpointWithin` | `Map.reachability.CanReach`、`LocalTargetInfo`、`PathEndMode`、`TraverseParms.For(TraverseMode.PassDoors)`、`IntVec3.InBounds` | 只读可达查询，不执行寻路或移动；按地图取事实，不是 `Find.*` |
| 展示物 / 镜像身份 | `IsDisplayThing`、`TryGetDisplayKey`、`IsMirror` | `SDS_Host.Display` / `SDS_Host.MirrorQuery` | 只回答身份，不登记、不改镜像 |
| 取物格 | `TryGetWithdrawCell` | `ISDS_Port.AccessCell` | 只读交互格，不移动 Pawn |
| 手动存入可用 | `IsManualDepositAvailable` | `SDS_Host.DepositJobs.IsManualDepositAvailable` | 转发 Vanilla 判定，不新增门槛 |

其中 `HasUsableBuildingEndpointWithin` 是 Api 里唯一接触原版可达性的方法；它只在适配器桥中，供补料 / 半径判定使用，五轴本身不调用。

### 7.4 为什么这些判定留在 Api

| 理由 | 说明 | 来源 |
|---|---|---|
| Core 的硬红线 | Core 禁止 `Find.*`、`mapPawns`、寻路、`SlotGroup` 等原版行为入口，这些判定因此不能下沉 Core；Api 自身只允许 `Find.TickManager` 这一处 `Find.*` | 《ModuleRules.zh-CN.md》6.3；《ModuleResponsibilities.zh-CN.md》七 |
| Core 只接事实 DTO | Core 收 `SDS_EndpointQuery`，端点事实由 `ISDS_Port` / `ISDS_PortProvider` 提供；它没有「玩家阵营」这个概念 | 《ModuleInterfaces.zh-CN.md》3.3 |
| 调用门是 API 边界规则 | 旧契约明确「所有权 / 阵营 / 禁止检查由门面补齐，不能只依赖底层 `grid.TryDeposit` / `TryWithdraw`」，并且不复用 `HaulAIUtility.PawnCanAutomaticallyHaulFast` | `PublicAPI.zh-CN.md` §3.4 |
| Core 可脱离游戏环境 | `SDS_Ledger` 构造不依赖 `Game` / `GameComponent`；把玩家政策塞进 Core 会破坏这层可测试性 | 《ModuleInterfaces.zh-CN.md》3.1；《SDS.Core/DesignDecisions.zh-CN.md》三 |
| Vanilla 只提供事实与执行 | Vanilla 不负责第三方调用方的合法性；若 Vanilla 判阵营，Api 就要反向依赖 Vanilla，违反《ModuleRules.zh-CN.md》6.5 的引用方向 | 《ModuleRules.zh-CN.md》6.4、6.5；《ModuleResponsibilities.zh-CN.md》七 |
| 同图判定比较的是调用输入 | `ValidateDepositCall` 比的是 `thing.MapHeld` 与调用方传入的 `map` / 端点 `MapHeld`，属于入参校验，不是存储规则 | `SDS_API.cs` |
| tick 属于读模型版本 | `SDS_ReadVersions` 的 `tick` 要与镜像 / 端点 / 账本版本一起取齐；回收巡检的 tick 由 Vanilla 侧 `ISDS_ReclaimWorld.CurrentTick` 提供（Core 不读该槽位），消费方不同 | 《ModuleInterfaces.zh-CN.md》1.1、1.2 |

### 7.5 边界与已知差异

- 玩家阵营判定仍有两处写法：五轴 `TryFindWithdrawPoint` 走 `IsPlayerPawn`（`Faction.OfPlayerSilentFail`），适配器桥 `TryGetWithdrawCell` 仍直接比较 `Faction.OfPlayer`；M2 只收敛取物点决议，未改变两处调用门（待补）。
- 当前 Api 源码未出现 `Claimable` / `ClaimableBy`；所有权只按 `Thing.Faction` 与根持有者阵营判定。`SDS_ApiCaller.claimOwner` 只被赋值，不参与判定（`SDS.Contracts/SDS_API.cs`）。
- 存入的同图判定只比较实物与 `map` / 端点；未显式比较 `caller.pawn.Map`。这与 `PublicAPI.zh-CN.md` §3.4「调用方、实物、端点在同一地图」的表述存在差异（待补）。
- 尸体按实物阵营判定，不追查 `InnerPawn`（`PublicAPI.zh-CN.md` §3.4）；这避免把尸体内部的原版 Pawn 当成第二个所有权来源。
- 「看」「读」是只读轴，不要求玩家阵营；只有会改变状态或涉及可达的入口才过玩家门（`PublicAPI.zh-CN.md` §3.4）。
- 非 Pawn 存入选端点使用 `DepositForNonPawn`：无自身端口、无 `requireReach`，距离按参考格计算（`PublicAPI.zh-CN.md` §3.7）。
- `TryWithdraw` 不接收 `Map`，只校验 Pawn 有效性；`TryWithdrawFromMirror` 才显式比较 Pawn 与镜像地图。
- 原版类型只在方法体内出现，不进入 Contracts 的 DTO；契约字段仍以《ModuleInterfaces.zh-CN.md》一为准。

## 八、第三方支持

### 8.1 两个入口

五轴（看 / 读 / 找 / 取 / 存）属于 `SDS_API.Version` 契约，面向外部第三方与兼容层（《ModuleResponsibilities.zh-CN.md》七）；适配器桥 public 但不进入契约，运行时消费者只有 `SDS.ThirdModCompat`（测试由 `SDS.Tests` 覆盖），登记见《ModuleInterfaces.zh-CN.md》五。对外 public 类型只有 `SDS_API` 与 Contracts 的公共类型（《ModuleResponsibilities.zh-CN.md》二）。

### 8.2 看 / 读

| 轴 | 能做什么 | 不能做什么 | 返回与失败形态 |
|---|---|---|---|
| 看 `VisibleThingsOfDef` | 列出某地图上原版枚举可见的某 def 实物（含镜像 / 代理） | 不补 holder 树 / `ThingGrid` / Region；不给数量；不物化、不扣账 | `IReadOnlyList<Thing>`；map / def 为空或异常返回空列表，并记一次 `ErrorOnce` |
| 读 `ReadStock` | 给某键的总数、可用量、禁用、是否解析、供给失败原因、只读版本 | 不建事务、不预留、不扣账；不暴露候选投影 / 排序 | `SDS_ApiStockInfo`，看 `result` 字段 |
| 读 `ReadStockFor` | 按实物问 Core 要通道与键，再读该键 | 不自行判断虚拟 / 保真；不复制 `RequiresPreservedStorage` | 同上；实物无法解析时返回 `ReadStock(null)`，`resolved = false` |
| 读 `CaptureReadVersions` | 一次取齐 tick、镜像、端点、库存、预留、筛选版本 | 不是数据快照，不能据此读写库存 | `SDS_ReadVersions`；异常返回全 0 的 default |

### 8.3 找 / 取 / 存

| 轴 | 能做什么 | 不能做什么 | 返回与主要结果码 |
|---|---|---|---|
| 找 `TryFindDepositEndpoint` | 按非 Pawn 模式选一个可用存入端点 | 不预留、不入账；不考虑可达与自身端口 | `bool` + 端点 `Thing` + 码：`NoEndpoint` / `NotOwnedByCaller` / `NotPlayerFaction` / `GridUnavailable` |
| 找 `TryFindWithdrawPoint` | 为玩家 Pawn 找自身端口或最近取物点，并告知是否自身端口 | 只读，不预留、不取货 | `bool` + 端点 + `self` + 码：`NoEndpoint` / `InvalidArgument` |
| 取 `TryWithdraw` | 按键把虚拟库存物化成真实 `Thing` | 只处理虚拟通道；保真键、`VirtualHandoff`、无效 Pawn 均拒绝；不交付 | `List<Thing>` + 码：`NoStock` / `TransactionFailed` / `UnresolvedDef` 等 |
| 取 `TryWithdrawFromMirror` | 从指定镜像取出实物 | 不交付；镜像本身不区分保真；Pawn 与镜像必须同图 | 单个 `Thing` + 码：`NotWithdrawable` / `TransactionFailed` |
| 取 `TryWithdrawPreservedExact` | 按调用方给出的精确真身计划取出保真库存 | 不替调用方选真身；不按 def 批量取（批量在桥里）；禁用品类直接拒绝 | `List<Thing>` + 码：`Forbidden` / `TransactionFailed` / `InvalidArgument` |
| 存 `TryDepositNow` | 选端点并立即入账单件 | 不批量、不造 Job、不 SplitOff 一半 | `bool` + `accepted` + 码：`NoEndpoint` / `DepositFailed` / `EndpointUnavailable` 等 |
| 存 `TryDepositAt` | 向已知端点原子入账单件 | 端点必须是本 Mod 可用端口；不交付 | 同上 |

补充事实：`TryFindWithdrawPoint` 的结果用于走位与交付位置；`TryWithdraw` 只按 key 物化，不接收端点参数。

### 8.4 `SDS_ApiResultCode` 的语义

16 个码的枚举成员清单见《SDS.Contracts/Interfaces.zh-CN.md》A.1；下表只记 API 边界语义。

| 码 | 含义 |
|---|---|
| `Ok` | 成功 |
| `InvalidArgument` | 参数非法：空值、数量非正、虚拟轴收到保真键、`VirtualHandoff`、Pawn 无效、存入跨图 |
| `GridUnavailable` | 全局账本不可用（`SDS_Rules.Available == false`），或 Job 型宿主未注入 |
| `NoEndpoint` | 没有选出可用端点 / 取物点 |
| `EndpointUnavailable` | 已知端点已销毁、不是端口或不可运行 |
| `NotDepositable` / `DepositFailed` | Core 判定不可存 / 入账失败或接收量为 0 |
| `Forbidden` | 实物被禁止，或供给失败原因为 `Forbidden` |
| `NotOwnedByCaller` | 实物或端点不归调用方阵营 |
| `NotPlayerFaction` | 调用方阵营不是玩家阵营 |
| `NoStock` / `UnresolvedDef` / `RebuildFailed` | 供给失败原因 `Insufficient` / `UnresolvedDef` / `RebuildFailed` 的映射 |
| `NotWithdrawable` | 镜像取出参数非法或目标不可取 |
| `TransactionFailed` | Core 事务失败、返回空结果，或按 def 批量取不足整批回退 |
| `InternalError` | 捕获到异常，统一记一次 `ErrorOnce`，异常不外泄 |

### 8.5 适配器桥的用途与边界

方法名与签名见《ModuleInterfaces.zh-CN.md》五（桥面唯一登记处）。

| 类别 | 用途 | 边界 |
|---|---|---|
| 端点 / 可用性 | 判断端点是否存在、本图是否可用、Pawn 端口与手动存入是否可用、取物格 | 只读；不选端点、不派活 |
| 账本只读 | 可用量、键、禁用、保真真身、按 def 找可取出键、配方槽位判定 | 不建事务、不预留；可用量统一走 `SDS_Availability` |
| 展示 / 镜像 | 判断镜像 / 展示物身份，反查展示键 | 只读；不登记、不改形态 |
| 来源 / 通道 | 来源排除标记、存入 Job 判定、def 级通道判定 | 标记只给刚物化实物；通道判定转发 Core |
| Job 派活 | 候选闸门、造存入 Job、背包卸载 Job | 只转 Vanilla 唯一存入服务，不新增派活路径 |
| 按 def 批量取 | 虚拟按 key 逐键组合；保真按真身计划（支持叠堆计数）；不足整批回充 | 不新增排序 / 扣账规则；失败不留半截 |
| 补料规划 | 槽位判定、选 def、缺口计算、存入反馈、整备 Job 取量登记 | 缺口只在 `SDS_BillSupplyPlanner`；注册器只登记数值 |
| 回充 | 保真回充、虚拟回充（`landOnFailure` 控制失败是否按 Ground 落地） | 只转 `ReturnThing`；先回账本，失败按 `Ground` 落地且 `cell` 为失败落地首选格；`landOnFailure=false` 时保持原状 |
| 装载 | 第三方自建装载清单注入 | 只注入展示物；不取货、不预留、不扣账（W50） |

桥上的「按 def 批量取」只是同一事务原语与精确真身计划的逐键组合，加上整批回充；不构成第二套容量、排序或扣账规则（《ModuleInterfaces.zh-CN.md》五）。

### 8.6 三条边界

**第三方只 soft-depend**

- 外部第三方不应静态引用 `SDS.Api.dll`，而以反射探测 `SDS_API.Version` 后再调用。
- `Version = 4` 是程序集改名的兼容性声明：五轴语义不变，但引用路径变了，第三方须重新 soft-depend（《ModuleResponsibilities.zh-CN.md》七；`PublicAPI.zh-CN.md` §3.8）。
- 仓库内静态引用 `SDS.Api` 的是 `SDS.ThirdModCompat` 与 `SDS.Bootstrap`（《ModuleRules.zh-CN.md》四）；生产代码中直接调用五轴或适配器桥的只有 `SDS.ThirdModCompat`。

**只经 Api，不得直连 Core / Vanilla（E6）**

- 兼容层只允许经 `SDS.Api` / `SDS.Contracts` / `SDS.Diag`；`CheckCompatSurface.ps1` 会拒绝 Core / Vanilla 引用（《ModuleRules.zh-CN.md》三、十）。
- `InternalsVisibleTo` 只登记 `SDS.Core → SDS.Rules`，第三方在物理上也看不到 Core 的 internal 类型（《ModuleRules.zh-CN.md》五）。
- 目标 Mod 只允许反射探测，不得硬引用其程序集（《ModuleRules.zh-CN.md》6.6）。

**每个第三方入口只有一条写接管路径（E7）**

- 只读投影可以与适配器翻译共存；但同一入口不得同时存在两条扣账、派活或回滚路径（`PublicAPI.zh-CN.md` §4.3）。
- 典型分工：Job 型入口只走 Job 派活桥；直接取货只走 `TryWithdraw`；目的地发现只走 `TryFindDepositEndpoint` 且投递仍走原版容器契约；装载清单只走注入桥（`PublicAPI.zh-CN.md` §4.11）。
- 一旦适配器自己算可用量、缺口或回滚顺序，就违反 E7 的单写路径约束，也绕过了 E6 限定的兼容边界（《ModuleRules.zh-CN.md》6.6）。

### 8.7 最小调用流程：取货

1. soft-depend 探测 `SDS_API.Version`，确认门面存在。
2. 找端点：玩家 Pawn 调 `TryFindWithdrawPoint` 得到取物点与是否自身端口；该结果只用于走位 / 交付位置。
3. 读库存：`TryGetStockKey(thing)` + `ReadStockFor(thing)`，或直接 `ReadStock(key)`；按 def 查询可走桥上的 `TryFindWithdrawableKey` / `GetAvailableCountForDef`。
   - def 级有两套既定语义：桥上的 `TryFindWithdrawableKey` / `GetAvailableCountForDef` 走生产 / 取用口径（禁用归零，保真按**真身 def**，缩装 = 内层 def）；`GetPreservedAvailableCount(ThingDef)` 为与原版 `ThingsOfDef` 对齐按**外壳 def** 计。两者不得互换。
4. 取货：虚拟通道调 `TryWithdraw`；保真通道可用 `AppendPreservedThings` 自行组成精确计划后调 `TryWithdrawPreservedExact`，或直接用桥上的 `TryWithdrawPreservedByDef`；目标是镜像时调 `TryWithdrawFromMirror`。
5. 自行交付：用原版 `carryTracker` / `inventory` / `ThingOwner` / 落地工具把返回的 `Thing` 交给目标；Api 不代交付。
6. 失败或中断：调 `TryReturnVirtual` / `TryReturnPreserved` 回充；按 def 批量取在不足时会把已取出的部分整批回充，不会留下半截。
7. 结果码：`Ok` 继续；`NoStock` / `NoEndpoint` 等正常拒绝静默处理；`InternalError` 已由 Api 记过一次，不要重复刷日志。

### 8.8 接入检查表

- 只引用 `SDS.Api` / `SDS.Contracts` / `SDS.Diag`，未引用 Core / Vanilla 的实现类型；也未把 `SDS.Api.dll` 当作硬依赖随包分发（以 soft-depend 探测 `Version`）。
- 只经五轴或已登记的适配器桥调用，未直接读 `SDS_Ledger` / `SDS_Core` / `SDS_Host`。
- 未在适配器内计算可用量、缺口、优先级、容量或回滚顺序；每个第三方入口只有一条写路径，没有同时依赖门面与自建 Harmony 写补丁。
- 目标 Mod 只用反射探测，未静态引用其程序集。
- 日志只用 `SDS_Diag.ErrorOnce` / `Warn` / 报告中心，未调用 `SDS_Diag.Fail`。
- 若使用了适配器桥，接受它不进入 `Version` 承诺、可随适配器调整。
