# SDS.ThirdModCompat 接口

> 定位：本模块的程序集内签名、9 个适配器身份、`SDS_API` 调用清单、Contracts 依赖，以及「原版支持」「第三方支持」两节适配事实。规则见《Rules.zh-CN.md》；签名以 `超维存储/Source/SDS.ThirdModCompat/` 源码为准。

## A. Sdk 工具类签名

5 个工具类与 9 个适配器在源码中均为 `internal`：本程序集公开面为 0（《ModuleResponsibilities.zh-CN.md》二），Bootstrap 靠反射发现，测试经登记的 `InternalsVisibleTo("SDS.Tests")` 直引。

### A.1 `SDS_CompatTools`（`SDS_CompatServices.cs`）

```csharp
internal static class SDS_CompatTools
{
    public static MethodInfo Method(Type type, string name, params Type[] args);
    public static PropertyInfo Property(Type type, string name);
    public static MethodInfo PropertyGetter(Type type, string name);
    public static FieldInfo Field(Type type, string name);
    public static bool TryPatch(Harmony harmony, SDS_CompatPatchReport report, Type ownerType,
        string hook, MethodInfo target, string prefixName, string postfixName, string missingReason,
        string finalizerName = null, int priority = Priority.Low, int finalizerPriority = Priority.Low);
    public static bool GetBool(PropertyInfo prop, object instance);
}
```

`TryPatch` 是动态挂点唯一入口：`target == null` → `Missing`；应用异常 → `Missing` + `ErrorOnce`；成功 → `Applied`。未指定 Prefix / Postfix / Finalizer 也记 `Missing`。

### A.2 `SDS_CompatEndpoint`

```csharp
internal static class SDS_CompatEndpoint
{
    public static bool HasAnyEndpoint(Map map);
    public static bool HasUsableBuildingEndpointWithin(Map map, IntVec3 root, float radius);
}
```

### A.3 `SDS_CompatStock`

```csharp
internal static class SDS_CompatStock
{
    public static long CountAvailable(ThingDef def);
    public static bool TryWithdrawVirtualByDef(ThingDef def, int count, Map map, out List<Thing> made);
    public static bool TryWithdrawPreservedByDef(ThingDef def, int count, Map map, out List<Thing> made);
    public static void ReturnPreserved(List<Thing> things, Map map);
}
```

两个 `TryWithdraw*ByDef` 内部用 `SDS_ApiCaller.FromFaction(Faction.OfPlayerSilentFail)`；`CountAvailable` 是「虚拟 + 保真」合计，调用方不得再叠加保真量；`ReturnPreserved` 逐件调 `SDS_API.TryReturnPreserved(..., IntVec3.Invalid)`，跳过 null / 已销毁。

### A.4 `SDS_CompatBillSupply`

```csharp
internal static class SDS_CompatBillSupply
{
    public static bool TrySupplyPreservedBill(Bill bill, Map map, IntVec3 rootCell,
        ThingOwner destination, out List<Thing> made);
}
```

只补保真通道；候选 = 目标容器已有实物 + `AppendPreservedThings`，选择复用原版 `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`，取出走 `TryWithdrawPreservedExact`。

### A.5 `SDS_CompatMirrorSeam`（`SDS_CompatServices.cs`）

```csharp
internal static class SDS_CompatMirrorSeam
{
    public static bool TryMaterializeForReservation(JobDriver driver, TargetIndex ind, int count,
        Type compType, FieldInfo[] fields, string logLabel);
}
```

第三方 Job 预约前的端点物化唯一实现：镜像目标 → 最近可达端点访问格 → 取货 → 复制 comp 字段 → `GenPlace` 落点 → 改写 target；失败回账或不接管。只经 `SDS_API`，不改 W54 / Core；第二个同形态适配器只传描述符，不复制本实现。

## B. 9 个适配器

`Name` / `TargetPackageId` / `HarmonyId` 均取自适配器常量；`ISDSResettable` 取自类型声明。`HarmonyId` 契约见《ModuleRules.zh-CN.md》八；`packageId` 与挂点数见 §F.1。

| 适配器 | `Name`（实参） | `TargetPackageId`（实参） | `ISDSResettable` |
|---|---|---|---|
| `SDS_CompatAdapter_PUAH` | `Pick Up And Haul` | `Mehni.PickUpAndHaul` | 否 |
| `SDS_CompatAdapter_CE` | `Combat Extended` | `CETeam.CombatExtended` | 是（清冷却 / 优先级缓存） |
| `SDS_CompatAdapter_UFRecipes` | `联合轻工家具拓展-UF Light Industries Furniture` | `KindSeal.UFLI` | 否 |
| `SDS_CompatAdapter_ManipulatorBeam` | `牵引光束搬运器Manipulator Beam Emitter` | `natsuki.manipulatorbeam` | 否 |
| `SDS_CompatAdapter_MiliraExpanded` | `米莉拉扩展-未来科技Milira Expanded - Future Technology` | `XiyueYM.MiliraExpandedXY` | 是（清设备节流） |
| `SDS_CompatAdapter_CeleTechShuttle` | `天工模块化穿梭机 CeleTech Shuttle Extension` | `LongRange.CeleTech.ShuttleExtension` | 否 |
| `SDS_CompatAdapter_EquivalentExchange` | `ProjectE 等价交换` | `Natsuki.ProjectE` | 否 |
| `SDS_CompatAdapter_Versatran` | `VC载具框架 VersatranZeroOne` | `VersatranZeroOne.serval` | 否 |
| `SDS_CompatAdapter_MiliraGift` | `边缘米莉拉养成：与你相连之人` | `FunnyFlandre.milira.theoneentwinedwithyou` | 否 |

### B.1 每个适配器调用的 `SDS_API` 方法

本表是各适配器 `SDS_API` 入口的适配器级汇总；§F.2 按挂点给出同一组入口的对应关系。 “间接”指经 `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply` 转发；直接调用可用 `grep SDS_API\.` 核实，间接项再按括注的工具类追一层。

| 适配器 | `SDS_API` 方法 | 直接 / 间接 |
|---|---|---|
| PUAH | `IsProducedPendingConsumption`、`TryTakeDepositCandidate`、`IsDepositJob`、`TryMakeDepositJob`、`IsPawnPortOperational`、`TryMakeDepositJobFromInventory` | 直接 |
| CE | `IsManualDepositAvailable`、`IsGridUsable`、`IsGridAvailable`、`TryGetWithdrawCell`、`TryFindWithdrawableKey`、`ClampTakeToAvailable`、`TryWithdraw`、`MarkProducedPendingConsumption`、`TryReturnVirtual`（`landOnFailure: false`）、`IsMirror` | 直接 |
| UFRecipes | `HasUsableBuildingEndpointWithin`（经 `SDS_CompatEndpoint`）；`IsGridAvailable`、`AppendPreservedThings`、`IsLivePreservedThing`、`IsBillIngredientAllowed`、`TryGetStockKey`、`IsStockForbidden`、`GetAvailableCount`、`TryWithdrawPreservedExact`、`TryReturnPreserved`（经 `SDS_CompatBillSupply` / `SDS_CompatStock`） | 间接 |
| ManipulatorBeam | `IsDepositEndpoint`、`TryFindDepositEndpoint` | 直接 |
| MiliraExpanded | `TryPlanBillSupplySlot`、`TryPlanAllowedDefSupply`、`NotifyBillSupplyDeposited`、`RequiresPreservedStorage`、`TryReturnPreserved`、`TryGetStockKey`、`TryReturnVirtual`；`HasAnyEndpoint`（经 `SDS_CompatEndpoint`）、`GetAvailableCountForDef`、`TryWithdrawVirtualByDef`、`TryWithdrawPreservedByDef`（经 `SDS_CompatStock`） | 直接 + 间接 |
| CeleTechShuttle | `InjectTransportLoadCandidates` | 直接 |
| EquivalentExchange | `IsDisplayThing`、`TryGetDisplayKey`、`GetAvailableCount`、`ClampTakeToAvailable`、`TryWithdraw`、`TryGetStockKey`、`TryReturnVirtual`、`IsMirror` | 直接 |
| Versatran | `IsGridUsable`、`IsMirror`、`TryGetStockKey`、`ReadStock`、`GetAvailableCount`、`ClampTakeToAvailable`、`IsStockForbidden`、`AppendPreservedThings`、`IsLivePreservedThing`、`TryWithdraw`、`TryWithdrawPreservedExact`、`TryReturnVirtual`、`TryReturnPreserved` | 直接 |
| MiliraGift | `IsMirror`、`IsGridUsable`、`HasAnyEndpoint`、`TryGetWithdrawCell`、`TryWithdrawFromMirror`、`TryReturnVirtual` | 直接（经 `SDS_CompatMirrorSeam`） |

## C. 消费的 Contracts 类型

| 类型 | 用途 | 使用方 |
|---|---|---|
| `ISDSCompatAdapter` | 适配器契约（`Name` / `TargetPackageId` / `IsTargetLoaded` / `Apply`） | 9 个适配器、`SDS_CompatLoader` |
| `SDS_StockKey` | 账本键；`TryGetStockKey` / `TryFindWithdrawableKey` 的输出 | CE、UFRecipes、Milira、ProjectE、`SDS_CompatBillSupply` |
| `SDS_WithdrawMode` | 取货交付模式；本模块只用 `Hand` / `Container` | CE、ProjectE、`SDS_CompatBillSupply` |
| `SDS_PreserveHooks` | 保真事实探针注册表；CE 适配器 `Apply` 时登记 `HasLoadedAmmoState`（W33） | `SDS_CompatAdapter_CE` |
| `SDS_CompatGates` | 兼容闸门注册表；EE 适配器 `Apply` 时登记装载清单注入闸门，Vanilla 注入前消费 | `SDS_CompatAdapter_EquivalentExchange` |
| `SDS_ApiCaller` | 调用方上下文；`FromPawn` / `FromFaction` | CE、ManipulatorBeam、ProjectE、`SDS_CompatStock`、`SDS_CompatBillSupply` |
| `SDS_ApiResultCode` | API 边界失败码；本模块以 `out _` 丢弃，只按 bool 结果分流 | 全部走 `SDS_API` 的方法 |

`SDS_ApiResultCode` 在本模块源码中没有显式类型引用（全部 `out _`）；`SDS_ApiStockInfo` / `SDS_ReadVersions` / `SDS_SupplyFailure` 也没有直接消费（待补：若后续需要区分失败原因再引入）。

### C.1 `SDS_ApiResultCode` 语义摘要

| 值 | 语义 |
|---|---|
| `Ok` | 成功 |
| `InvalidArgument` | 参数非法；`VirtualHandoff`、非玩家阵营、跨图镜像等 |
| `GridUnavailable` | 账本不可用（G 关闭 / 未 `Attach`） |
| `NoEndpoint` / `EndpointUnavailable` | 无候选端点 / 选中端点不可用 |
| `NotDepositable` | 物品不可存入（品类 / 筛选 / 容量 / 状态） |
| `Forbidden` | 品类或物品被禁用 |
| `NotOwnedByCaller` / `NotPlayerFaction` | 所有权 / 阵营门拒绝 |
| `NoStock` | 虚拟库存不足 |
| `UnresolvedDef` | def 无法解析 |
| `RebuildFailed` | 保真 / 重建计划失败 |
| `NotWithdrawable` | 物品不可取出 |
| `DepositFailed` | 存入执行失败 |
| `TransactionFailed` | 事务失败 |
| `InternalError` | 内部异常；已记 `公共API/*异常`，不外泄异常 |

口径来源：《ModuleInterfaces.zh-CN.md》一.1.1；`PublicAPI.zh-CN.md` §3.6（`SDS_SupplyFailure` → `UnresolvedDef` / `Forbidden` / `NoStock` / `RebuildFailed`）。

## D. 调用关系

```text
SDS.Bootstrap.SDS_CompatLoader.Scan（反射枚举已加载程序集）
        │  目标未装 → 跳过；名称重复 → ErrorOnce + 跳过
        ▼
ISDSCompatAdapter.Apply（每个适配器 new Harmony(HarmonyId)）
        │  SDS_CompatTools.TryPatch → 目标 Mod 方法 / 两处原版方法
        ▼
SDS_CompatEndpoint / SDS_CompatStock / SDS_CompatBillSupply（可选）
        ▼
SDS_API（五轴 + 适配器桥）→ SDS.Core（账本 / 事务 / 规则唯一实现）
```

- Bootstrap 不静态引用本程序集；只认 Contracts 的 `ISDSCompatAdapter`。
- 适配器不直接引用 Core / Vanilla；`SDS_API` 是唯一跨程序集写路径。
- 目标 Mod 只出现在反射字符串与 `Apply` 的挂点参数里。

## E. 原版支持

> 本模块「不补原版」：补丁只打在第三方 Mod 的方法上（或第三方方法触发的原版方法上），共用原版基础设施，但不新增原版行为。只列能从源码确认的原版类型与方法；无法确认的写「（待补）」。

### E.1 结论：本模块不补原版

- 112 个特性补丁类与 29 个注册表条目全部在 `SDS.Vanilla`（《ModuleRules.zh-CN.md》七）；本模块没有任何 `[HarmonyPatch]` 类，9 个适配器都是运行期 `harmony.Patch`。
- 9 个适配器的目标方法都是第三方类型。只有两处挂到原版方法，且都由对应适配器在目标 Mod 已加载时才安装：

| 适配器 | 原版目标方法 | 安装前提 | 实际生效条件 | 对原版行为的影响 |
|---|---|---|---|---|
| `SDS_CompatAdapter_CE` | `Pawn_JobTracker.JobTrackerTick()` | CE 已加载（`Resolve()` 成功） | 玩家阵营 + 征兆 + `CurJobDef == JobDefOf.AttackStatic` + 手持 CE 弹匣武器且 `CurMagCount == 0` + 本图网格可用 | 只补弹并派 CE 自己的 `ReloadWeapon` Job，不改原方法语义 |
| `SDS_CompatAdapter_EquivalentExchange` | `Dialog_LoadTransporters.TryAccept()` | ProjectE 已加载 | 清单里每个 `CompTransporter.parent.def.defName` 都是 `EE_TransmutationTable` / `EE_EmcInterface` | Prefix 把展示物换成真身、Finalizer 回充；其它装载对话框完全不碰 |

- 除这两处，本模块只「调用」原版方法（例如 `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`），不替换原版返回值、不新增原版类型、不注册原版 Def。
- 两处原版挂点在目标 Mod 未装时不会被安装（`SDS_CompatLoader` 不调用 `Apply`），因此对纯原版玩家为零影响。

### E.2 共用的原版基础设施

| 原版类型 / API | 用途 | 使用方 |
|---|---|---|
| `Harmony` / `HarmonyMethod` / `Priority` | 动态挂点 | 全部 9 个适配器（经 `SDS_CompatTools.TryPatch`） |
| `Thing` / `ThingDef` / `ThingWithComps` / `ThingComp` | 物品、组件、组件属性 | PUAH、CE、ManipulatorBeam、Milira、MiliraGift、CeleTech、ProjectE |
| `ThingOwner`（`TryAdd` / `TryAddOrTransfer` / `Contains` / `Remove` / 遍历） | 原版容器与背包 | CE（`pawn.inventory.innerContainer`）、UFRecipes（配料落点） |
| `Job` / `JobDef` / `JobDefOf` / `JobMaker` / `TargetIndex` / `JobTag` | Job 语义与派活 | PUAH、CE、Milira、MiliraGift |
| `Bill` / `Bill_Production` / `RecipeDef` / `IngredientCount` / `ThingCount` / `ThingFilter` | 配方与账单 | UFRecipes、Milira |
| `TransferableOneWay` / `CompTransporter` / `Dialog_LoadTransporters` | 装载清单与装载对话框 | CeleTech、ProjectE |
| `IHaulDestination` / `ListerHaulables` / `Map.listerBuildings` / `MapComponent` | 搬运目的地发现 | ManipulatorBeam |
| `Building` / `Building_Turret` / `Map` / `IntVec3` / `Pawn` / `Faction` | 世界事实 | CE、UFRecipes、ManipulatorBeam、Milira、ProjectE |
| `GenPlace.TryPlaceThing` / `GenClosest.ClosestThingReachable` / `GenTicks.TicksGame` | 放置、搜索、当前 tick | CE、Milira、MiliraGift |
| `Map.reservationManager.OnlyReservationsForJobDef` | 只读预留查询 | Milira |

### E.3 逐个适配器触碰的原版类型 / 方法

| 适配器 | 触碰的原版类型 / 方法 | 语义 | 读 / 写 |
|---|---|---|---|
| PUAH | `Verse.AI.Job`（`ref __result`）、`Pawn_JobTracker.curJob`、`Pawn_JobQueue.EnqueueFirst`、`JobTag.Misc`、`Job.TryMakePreToilReservations`、`Pawn.AllComps` | 搬运 Job：`job.def.defName == "HaulToInventory"` 时识别为 PUAH 自身的携带循环 | 读 + 替换 Job 返回值 |
| CE | `Pawn_JobTracker.JobTrackerTick`（挂点）、`Pawn_JobTracker.pawn`（反射读）、`JobDefOf.AttackStatic`、`JobDefOf.TakeInventory`、`JobMaker.MakeJob`、`Pawn.equipment.Primary`、`Pawn.inventory.innerContainer`、`GenPlace.TryPlaceThing`、`GenClosest.ClosestThingReachable`、`ThingRequest.ForDef`、`PathEndMode.ClosestTouch`、`TraverseParms.For`、`Building_Turret`、`TargetIndex` | 弹药与装填：把弹药物化到地面或塞进背包，再交回 CE 自己的换弹链 | 读 + 写原版容器 / 地图 |
| UFRecipes | `Bill`、`RecipeDef` / `IngredientCount`（经 `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`）、`ThingCount`、`ThingOwner`、`Building.InteractionCell`、`Bill.ingredientSearchRadius` | 配方配料：只在第三方打印机自己拉料失败后补保真真身 | 读 + 向原版容器写入 |
| ManipulatorBeam | `Map.listerBuildings.allBuildingsColonist`、`Map.listerHaulables.ThingsPotentiallyNeedingHauling()`、`IHaulDestination.Accepts`、`MapComponent.map`、`Thing.Position.Fogged`、`Building` | 搬运束的目的地发现：候选仍是原版待搬运地面物，收不收问端点自己 | 只读；投递交原版容器契约 |
| Milira | `Bill_Production`、`RecipeDef`、`IngredientCount`、`ThingFilter`（尸体筛选）、`ThingDef.IsCorpse`、`JobDefOf.DoBill`、`Map.reservationManager.OnlyReservationsForJobDef`、`ThingComp.props`、`Thing.parent`、`GenTicks.TicksGame` | 生产 / 分解 / 生命精华订单的补料事实 | 只读；存入走米莉拉自己的网络 |
| MiliraGift | `JobDriver.TryMakePreToilReservations`（挂点）、`JobDriver.pawn` / `.job`、`Job.GetTarget` / `SetTarget`、`TargetIndex.A`、`GenPlace.TryPlaceThing`、`ThingWithComps.AllComps`、`CompIdealGift` 的 `Crafted / Prepared / Sent / Note / Author / CrafterName / Year`（反射读写） | 送礼：预约前把镜像目标换成端点访问格上的真身 | 读写 comp 字段；不改第三方 Job 结构 |
| CeleTech | `ThingWithComps`、`TransferableOneWay`、`CompTransporter`（清单所属装载链，适配器只读） | 穿梭机装载清单：只追加展示物行 | 只写清单列表（运行期） |
| ProjectE | `Dialog_LoadTransporters.TryAccept`（挂点）、`Dialog_LoadTransporters.transferables` / `transporters` / `map`（反射访问）、`TransferableOneWay.things` / `CountToTransfer` / `AdjustTo`、`CompTransporter.parent`、`Map`、`IntVec3` | 等价交换：转化前换真身，未消耗的回充，区域扫描剔除镜像 | 读写清单行（运行期，不落盘） |

逐项要点：

- PUAH：不改 PUAH 的 Job 结构，只在 `HasJobOnThing` / `JobOnThing` 的返回值上做取舍；卸载派发点接管后 `return false` 跳过 PUAH 自己的入队。
- CE：识别「CE 找不找得到弹药」用 CE 自己的 `FindBestAmmo` / `TurretRegistry`；原版侧只用 `GenPlace` 落地、`ThingOwner.TryAdd` 进背包、`GenClosest` 兜底找已 spawn 的实物。另在 `Apply` 时把 `SDS_ModCompat_CE.HasLoadedAmmoState` 登记到 `SDS_PreserveHooks`（W33）：弹匣内有弹的武器由 Core 统一分流到保真通道，探针只回答事实，不扣账 / 不派活。
- UFRecipes：只补保真通道；虚拟品类由对方自己的 `SlotGroup` / `IHaulSource` 拉料看到金库镜像，不在适配器里再补一套。
- ManipulatorBeam：容器类型闸门与扫描表只放宽「目的地可见性」，投递仍由对方通用分支的 `IHaulDestination.Accepts` + `ThingOwner.TryAdd` 完成。
- Milira：`Map.reservationManager.OnlyReservationsForJobDef(..., JobDefOf.DoBill, true)` 只用于判断「原料正被账单占用」；存入调用的是米莉拉 `GameComponent_VirtualStorageNetwork.TryDeposit`，不是原版 `ThingOwner`。
- CeleTech：不写 `CompTransporter.leftToLoad`，不改 `WorkGiver_LoadTransporters` / `LoadTransportersJobUtility` / `HaulToTransporter`，装载仍走原版那条链（W50）。
- ProjectE：唯一写原版结构的位置是转化对话框 `transferables` 的行内 `things`（替换展示物为真身、清理空行），属于对话框运行期数据，不进入存档。

### E.4 判定口径：为什么这两处不算「补原版」

- 安装条件受第三方约束：两处挂点都由对应适配器的 `Apply` 安装，`SDS_CompatLoader` 在目标 Mod 未加载时不会调用 `Apply`，纯原版环境里这两处补丁不存在。
- 作用域受第三方状态约束：CE 的后缀要求「手持 CE 弹匣武器 + CE 空匣状态」，EE 的前缀要求「清单里每个运输器都是 EE 转化台 / EMC 接口」，不满足即原样放行。
- 不改变原方法的语义契约：CE 只补物品来源并派 CE 自己的 Job；EE 只把展示物换成真身、失败回充，不改变「成交即消耗」的原逻辑。
- 不新增原版类型、Def、WorkGiver、JobDriver、ThinkNode；原版侧的行为改动仍只由 `SDS.Vanilla` 的 112 个补丁类承担。

### E.5 与原版补丁的分工

| 事项 | 归属 | 说明 |
|---|---|---|
| 112 个特性补丁类 + 29 个注册表条目 | `SDS.Vanilla` | `SDS_PatchInstaller` 只扫 Vanilla 程序集 |
| 第三方动态挂点 | `SDS.ThirdModCompat` | 不计入 112；随目标 Mod 是否存在而变化 |
| 原版 DoBill / 搬运 / 装载对话框注入 | `SDS.Vanilla` | 例如 `Patch_SDS_TransporterAddVirtual`、`Patch_SDS_PreservedBillIngredients` |
| ProjectE 的装载清单注入闸门方法 `AllowInjection` | `SDS.ThirdModCompat` | 定义在 `SDS_ModCompat_EquivalentExchange`；当前源码中未见调用点（待补） |

### E.6 已知边界（待补）

- `MatterDecomposer_ThingComp` / `LifeEssenceExtractor_ThingComp` 继承自哪个原版类型：适配器只按字符串类型名探测，未直接引用其基类（待补）。
- CE 的 `FindBestAmmo(Pawn, AmmoDef)` 内部仍会走原版 `GenClosest`；与 `SDS.Vanilla` 距离投影叠加时的实机表现需 A/B 验证（待补）。
- `Pawn_JobTracker.JobTrackerTick` / `Dialog_LoadTransporters.TryAccept` 的签名随 RimWorld 版本变化后的适配（待补）。

## F. 第三方支持

> 逐个适配器说明目标 Mod、触发方法、接管方式、`SDS_API` 入口与特殊注意。§B.1 是同一事实的适配器级汇总（含直接 / 间接分类），本节保留挂点级对应关系。

### F.1 总览

packageId 以适配器常量与本表为准（原稿所引《ModuleResponsibilities.zh-CN.md》八已无此表）；Harmony ID 见《ModuleRules.zh-CN.md》八；适配器类型、`Name` 实参与 `ISDSResettable` 见 §B。

| 适配器 | 目标 Mod | packageId | Harmony ID | 挂点数 | 静态清理 |
|---|---|---|---|---|---:|---|
| `SDS_CompatAdapter_PUAH` | Pick Up And Haul | `Mehni.PickUpAndHaul` | `sizuru.sds.compat.puah` | 3 | 否 |
| `SDS_CompatAdapter_CE` | Combat Extended | `CETeam.CombatExtended` | `sizuru.sds.compat.ce` | 15 | 是 |
| `SDS_CompatAdapter_UFRecipes` | 联合轻工家具拓展-UF Light Industries Furniture | `KindSeal.UFLI` | `sizuru.sds.compat.ufrecipes` | 1 | 否 |
| `SDS_CompatAdapter_ManipulatorBeam` | 牵引光束搬运器Manipulator Beam Emitter | `natsuki.manipulatorbeam` | `sizuru.sds.compat.manipulatorbeam` | 3 | 否 |
| `SDS_CompatAdapter_MiliraExpanded` | 米莉拉扩展-未来科技Milira Expanded - Future Technology | `XiyueYM.MiliraExpandedXY` | `sizuru.sds.compat.miliraexpanded` | 6 | 是 |
| `SDS_CompatAdapter_CeleTechShuttle` | 天工模块化穿梭机 CeleTech Shuttle Extension | `LongRange.CeleTech.ShuttleExtension` | `sizuru.sds.compat.celetechshuttle` | 1 | 否 |
| `SDS_CompatAdapter_EquivalentExchange` | ProjectE 等价交换 | `Natsuki.ProjectE` | `sizuru.sds.compat.equivalentexchange` | 2 | 否 |
| `SDS_CompatAdapter_Versatran` | VC载具框架 VersatranZeroOne | `VersatranZeroOne.serval` | `sizuru.sds.compat.versatran` | 3 | 否 |
| `SDS_CompatAdapter_MiliraGift` | 边缘米莉拉养成：与你相连之人 | `FunnyFlandre.milira.theoneentwinedwithyou` | `sizuru.sds.compat.miliragift` | 1 | 否 |

### F.2 逐个适配器

#### F.2.1 Pick Up And Haul

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `PickUpAndHaul.WorkGiver_HaulToInventory.HasJobOnThing(Pawn,Thing,bool)` | Postfix 写 `__result`：在途待消费物 → false；否则原 false 时问候选闸门 | `IsProducedPendingConsumption` / `TryTakeDepositCandidate` | 不通过则逐字节维持 PUAH 判定 |
| `PickUpAndHaul.WorkGiver_HaulToInventory.JobOnThing(Pawn,Thing,bool)` | Postfix 替换返回值：命中则换成多件存入 Job | `IsDepositJob` / `TryMakeDepositJob`（`multi: true`） | `curJob.def.defName == "HaulToInventory"` 时让出；已是存入 Job 时让出 |
| `PickUpAndHaul.PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(Pawn,bool)` | Prefix：派 Job 并入队后 `return false` 跳过 PUAH 入队；否则 `return true` | `IsPawnPortOperational` / `TryMakeDepositJobFromInventory` | 预留失败必须交回 PUAH，不能吞掉这次卸载 |

- 静态缓存：`hauledCompType` / `hauledSetGetter` 首次反射结果（进程级常量，未注册 `SDS_StaticState`）；`new List<Thing>(set)` 只是只读快照，不写回 `CompHauledToInventory`。
- 一条写路径：取货行程由本 Mod 的存入 Job 接管；PUAH 取货驱动内部 `EnqueueFirst` 那趟有意不接管（源码注释「方案 B」）。
- 不二次扣账：`HasJobOnThing` 只写同 tick 决议缓存，`JobOnThing` 消费同一决议，扣账仍只发生一次。

#### F.2.2 Combat Extended

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `WorkGiver_ReloadTurret.HasJobOnThing` / `.JobOnThing(Pawn,Thing,bool)` | HasJob 放行候选；JobOn 前置补货 + 后置兜底直构 CE Job | `IsManualDepositAvailable` / `IsGridUsable` / `TryGetWithdrawCell` / `TryWithdraw` / `MarkProducedPendingConsumption` | CE 在地图上找得到弹药时一律不插手 |
| `WorkGiver_ReloadAutoLoader.HasJobOnThing` / `.JobOnThing(Pawn,Thing,bool)` | 与炮塔同构 | 同上 | 自动装弹器只在类型可解析时挂 |
| `JobGiver_ManTurretsNearSelfCE.FindAmmoForTurret` / `JobGiver_ManTurretsNearPointCE.FindAmmoForTurret(Pawn,Thing)` | Postfix 换 `__result`：`GenPlace` 落地一件真货 | `TryFindWithdrawableKey` / `TryWithdraw` | 只补玩家阵营，防止第三方小人抽玩家库存 |
| `JobGiver_CheckReload.GetPriority(Pawn)` / `.TryGiveJob(Pawn)` | GetPriority 抬优先级；TryGiveJob 前置补弹 + 后置兜底 | `IsManualDepositAvailable` / `TryFindWithdrawableKey` | 两处必须成对补，否则 TryGiveJob 不会被调用 |
| `Pawn_JobTracker.JobTrackerTick()` | Postfix：空匣时补弹并派 CE 自己的 `ReloadWeapon` | `IsManualDepositAvailable` / `IsGridUsable` | 只在征兆 + `AttackStatic` + 空匣；按 `thingIDNumber` 做 250 tick 冷却 |
| `JobGiver_DefenderReloadTurret.TryGiveJob(Pawn)` | Prefix 补弹后放行 CE 原体 | 同上 | 不跳过原方法 |
| `CompAmmoUser.TryPickupAmmo()` / `CompAmmoUser.TryPrepareShot()` | Postfix / Prefix | `TryTakeForCe`（内部 `TryWithdraw`） | 不补 `CompInventory.AmmoCountOfDef`：纯查询会污染武器面板 |
| `JobGiverUtils_Reload.FindBestAmmo(Pawn, AmmoDef)` | Postfix：镜像换成真实最近弹药，找不到返回 null | `IsMirror` | 只读翻译，不在判定阶段物化 |
| `JobDriver_ReloadTurret` / `JobDriver_ReloadAutoLoader.TryMakePreToilReservations(bool)` | Prefix：`targetA` / `targetB` 是镜像时 `__result = false` 并跳过 | `IsMirror` | 宁可这次不装填，也不复制物品 |

- 静态缓存：`checkCooldownUntil` / `priorityCacheUntil` / `priorityCacheValue` 按 `thingIDNumber` 存绝对 tick，经 `SDS_CompatAdapter_CE.ResetStatics` 登记 `ISDSResettable`；`pendingAmmo` / `pendingAmmoTarget` / `pendingAmmoDef` 是前置补货与兜底腿之间的一次性传值槽。
- 只读快照：CE 的 `TurretRegistry` / `CompAmmoUser` / `CompInventory` 成员全部经反射读，不写 CE 状态；唯一写 CE 的地方是 `CompInventory.UpdateInventory()`，目的是让对方看见刚进背包的弹。
- 不二次扣账：取货只有 `TryTakeForCe` 一处（`TryFindWithdrawableKey` 夹取 + `TryWithdraw` 一次）；落地腿打 `MarkProducedPendingConsumption`，背包腿不打。

#### F.2.3 联合轻工家具拓展-UF Light Industries Furniture

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `UFRecipes.AutoSequenceUtil.TryPullIngredientsFromCandidates(Bill,List<Thing>,Map,ThingOwner,out bool)` | Postfix：对方返回 false 时补保真配料，补上改 `__result = true` | 经 `SDS_CompatBillSupply`：`AppendPreservedThings` / `IsLivePreservedThing` / `IsBillIngredientAllowed` / `TryGetStockKey` / `IsStockForbidden` / `GetAvailableCount` / `TryWithdrawPreservedExact` / `TryReturnPreserved` | 只补保真通道，虚拟品类交给对方自己的拉料 |
| 端点前置 | 调用前先问半径内是否有可用建筑端点 | `SDS_CompatEndpoint.HasUsableBuildingEndpointWithin` | 半径语义取 `bill.ingredientSearchRadius` |

- 选择不自己写：复用原版 `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`，适配器只负责候选与取出。
- 保真真身不做 `SplitOff`：只接受 `stackCount == 1` 的真身；`ThingOwner.TryAddOrTransfer` 失败会先 `Remove` 再从网格回充。
- 候选集合去重：目标容器已有实物与保真真身用 `HashSet<Thing>` 去重，避免同一件被计两次。
- 不二次扣账：一次 `TryWithdrawPreservedExact`；不足整批则回充并返回 false，不留半截。

#### F.2.4 牵引光束搬运器Manipulator Beam Emitter

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `ManipulatorBeam.BeamFeatureSettings.IsSpecialContainerTypeEnabled(Thing)` | Postfix：端点为 true | `IsDepositEndpoint` | 只放宽类型闸门，不改开关 / 占用 / 入队判定 |
| `ManipulatorBeam.MB_BeamScanCacheMapComponent.SpecialContainers`（getter） | Postfix：追加本图殖民者建筑里的端点 | `IsDepositEndpoint` | 幂等；列表由对方 30 tick 重建，追加项随之失效再补 |
| `ManipulatorBeam.BeamSpecialTransferUtility.TryFindSpecialContainerTransfer(op, containerThing, haulValidator, excludedDestinations, out transfer)` | Prefix：构造 `BeamTransfer(Thing, IntVec3, Thing, int)` 写入 `__args[4]`，同时写 `__result` 并跳过原方法 | `IsDepositEndpoint` + 端点 `IHaulDestination.Accepts` | 下标 4 硬编码；签名不符即报挂点缺失；牵引光束已固定目的地，不拿「找」的全局就近结果比对本端点 |

- 静态缓存：`transferCtor` 在探测到对方类型后缓存一次。
- 只读快照：候选集用原版 `ListerHaulables.ThingsPotentiallyNeedingHauling()`，收不收问端点自己的 `IHaulDestination.Accepts`（本 Mod 的存入硬门：全局可用 / 端点可用 / 筛选 / 品类容量 / 来源排除标记）。
- 投递零接管：对方通用分支就是原版容器契约，适配器不调存入门面，因此不存在第二条入账路径。

#### F.2.5 米莉拉扩展-未来科技Milira Expanded - Future Technology

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `CompVirtualProductionDevice.CompTick()` | Prefix：30 tick 节流，按链接台账单补料 | `TryPlanBillSupplySlot` / `NotifyBillSupplyDeposited` | `Operational` 为假直接返回 |
| `GameComponent_VirtualStorageNetwork.TryProcessAutomatedRecipeCycle(CompVirtualProductionDevice,Bill_Production,int)` | Prefix：账单体补料 / 订单体按 id 补料 | `TryPlanBillSupplySlot` + `SDS_CompatStock` | 不跳过原方法 |
| `GameComponent_VirtualStorageNetwork.TryProcessManualRecomposeBill(CompVirtualProductionDevice,Bill_Production)` | Prefix：账单补料 | 同上 | 只补缺口，对方逻辑不变 |
| `GameComponent_VirtualStorageNetwork.TryProcessRecomposeOrder(CompVirtualProductionDevice,int)` | Prefix：订单补料 | `TryPlanAllowedDefSupply` | 允许 def 列表 + 尸体筛选由对方提供 |
| `GameComponent_VirtualStorageNetwork.TryProcessDecomposeOrder(MatterDecomposer_ThingComp)` | Prefix：分解订单补料 | `TryPlanAllowedDefSupply` | 无 `recipeDef`，走 def 列表分支 |
| `GameComponent_VirtualStorageNetwork.TryProcessLifeEssenceOrder(LifeEssenceExtractor_ThingComp)` | Prefix：生命精华订单补料 | `TryPlanAllowedDefSupply` | 同上 |

- 自有库存优先：对方网络可用量与 `CanStoreAnyDef` / `CanDepositDef` / `IngredientReserveFor` 全部经反射读，作为 `ownCount` / `extraAllows` / `reserveProvider` 事实传入。
- 静态缓存：`nextTickByDevice` 登记 `ISDSResettable`；`canStoreCache` 的键是 `ThingDef`、值依赖 W33 开关，`CanStoreDef` 内按开关自失效，`Reset()` 同时清缓存。
- 回充：存入失败或部分失败按通道回充——保真 `TryReturnPreserved`，虚拟 `TryReturnVirtual`。
- 不接手动 `WorkGiver_DoBill` 路径（由 `SDS.Vanilla` 的 DoBill 桥负责），不与 Core 的补料规则重叠。

#### F.2.6 天工模块化穿梭机 CeleTech Shuttle Extension

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `CeleTech.ShuttleExtension.ModularShuttle.Cargo.CargoTransferFilter.BuildCargoTransferables` | Postfix：向清单追加展示物 | `InjectTransportLoadCandidates`（W50 内部桥） | 只读投影：不写 `leftToLoad`、不派活、不扣账 |

- 用 `Building_ModularShuttle` 作为 marker 区分「未安装」与「装了但内部类型改名」；`BuildCargoTransferables` 找不到时只记挂点缺失。
- 宿主尚未开始装载时原版 `CompTransporter.TransportersInGroup` 返回 null（`leftToLoad` 还没建立），**不是挂点失败**：内部桥按「回退为宿主自身这一台」继续注入只读投影行，与对方 `ResolveTransporters` 的「组为空则退回首台」口径一致；否则表现为「不带人进驾驶舱就看不到超维库存」（2026-11-13 实机定位）。
- 无静态缓存；取货 / 预留 / 入舱 / 存档仍由既有原版挂点与唯一执行器负责。

#### F.2.7 ProjectE 等价交换

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `Dialog_LoadTransporters.TryAccept()` | Prefix（`Priority.High`）+ Finalizer：展示物换真身；未消耗的回充 | `IsDisplayThing` / `TryGetDisplayKey` / `GetAvailableCount` / `TryWithdraw` / `TryGetStockKey` / `TryReturnVirtual` | 只在转化台 / EMC 接口对话框生效；保真展示物直接摘掉 |
| `EquivalentExchange.TransmutationUtility.GetTransmutableThingsInArea(Map,IntVec3,int)` | Postfix：剔除镜像 | `IsMirror` | 镜像代表容器内容物，本来就不在格上 |

- 必须 `Priority.High`：对方自己的 Prefix 会接管 `TryAccept` 并跳过原版，低优先级 Prefix 在它返回 false 后不会执行。
- 数量双重夹取：先取 `CountToTransfer − 行内真实单位数`，再用 `GetAvailableCount` + `ClampTakeToAvailable`（W56 唯一实现）夹一次；取不出来就摘掉展示物（fail-closed）。
- Finalizer 只回充「未 `Spawned` 且 `holdingOwner == null`」的真身；其余视为已被对方销毁，避免二次记账。
- `hookReady = acceptPatched && transferablesField != null && transportersField != null`；未就绪时提供 `AllowInjection(Dialog_LoadTransporters)` 闸门，由适配器登记到 `SDS_CompatGates`，Vanilla 注入前消费（未登记放行、异常 fail-closed）。

#### F.2.8 VC载具框架 VersatranZeroOne

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `Window_VersatranCargo.ExecuteInstantLoad()` | Prefix：把镜像 / 保真行摘出原版循环；虚拟 `TryWithdraw`、保真 `TryWithdrawPreservedExact`，真身交车辆 `TryAddOrTransferThingToCargoStrict`；Postfix 刷新 | `IsGridUsable` / `IsMirror` / `TryGetStockKey` / `ReadStock` / `IsStockForbidden` / `AppendPreservedThings` / `IsLivePreservedThing` / `TryWithdraw` / `TryWithdrawPreservedExact` / `TryReturnVirtual` / `TryReturnPreserved` | 真实地图物品仍走车辆原逻辑；超维行只有一条 API 写路径 |
| `Window_VersatranCargo.RefreshItems()` | Postfix：镜像行 `MaxCount = available`；追加保真真身行 | `ReadStock` / `GetAvailableCount` / `AppendPreservedThings` / `IsStockForbidden` | 只读投影；保真行只接受 `stackCount == 1` 的真身；不建第二套数量 |
| `CompVersatranTransporter.CanThingBeStoredInCargo(Thing)` | Prefix：镜像 / 存活保真真身返回 false | `IsMirror` / `IsLivePreservedThing` | 守门；适配器自己的取货产物正常放行 |

- 不新增公开面 / 存档字段 / 规则 ID；虚拟行沿用镜像、保真行沿用真身，都是框架已有表示。
- 不依赖 `Thing.SplitOff` / `ThingOwner` 兜底；虚拟 / 保真都走五轴取货。
- 保真行 `CountToTransfer` 每次 `RefreshItems` 重建后归零（UI 细节）；虚拟行计数沿用车辆自己的 `RestoreTransferStates`。

#### F.2.9 边缘米莉拉养成：与你相连之人

| 触发方法 | 接管方式 | SDS_API 入口 | 特殊注意 |
|---|---|---|---|
| `MiliraTheOneEntwinedWithYou.Gifts.JobDriver_IdealReceiveGift.TryMakePreToilReservations(bool)` | Prefix：`targetA` 是镜像时，调 `SDS_CompatMirrorSeam.TryMaterializeForReservation` 在最近可达端点访问格物化 1 件、复制 `CompIdealGift` 七个字段、改写 `targetA`；无论是否接管都返回 true，原版预约继续执行 | `IsMirror` / `IsGridUsable` / `HasAnyEndpoint` / `TryGetWithdrawCell` / `TryWithdrawFromMirror` / `TryReturnVirtual`（全在 `SDS_CompatMirrorSeam`） | 不改 W54 / Core；字段缺失时不挂并在报告里给原因；中途取消时真身留在地面，不复制不丢失 |

- 静态缓存：`CompIdealGift` 类型与 7 个 `FieldInfo` 在 `TryApply` 时一次性解析，属进程常量，不登记 `SDS_StaticState`。
- 一条写路径：取货只经 `TryWithdrawFromMirror`；适配器不碰账本、不做位置判定、不选端点。
- 不接管：非镜像 / 无端点 / 无库存 / 非玩家 / 落点失败，一律交回原版预约与失败条件。

### F.3 接入新第三方 Mod 的步骤

1. 实现 `ISDSCompatAdapter`（`SDS.Contracts`，签名见《ModuleInterfaces.zh-CN.md》一.1.2）：`Name`（与 Harmony ID 一致）、`TargetPackageId`、`IsTargetLoaded`（只按类型名探测）、`Apply()`。`Apply` 必须自建 `applied` 幂等标记，并 `new Harmony(HarmonyId)`。
2. 用 `SDS_CompatTools` 探测类型：`Method` / `Property` / `PropertyGetter` / `Field`，一次 `Resolve()` 缓存结果；挂点统一走 `SDS_CompatTools.TryPatch`，禁止自建 `HarmonyMethod`，禁止硬编码参数下标而不校验签名。
3. 只经 `SDS_API` 读写：五轴 + 适配器桥（《ModuleInterfaces.zh-CN.md》五）。不算缺口、不选端点、不扣账、不建事务；能复用 `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply` 时不新写助手，新助手自身也不得含规则。
4. 如有按 `thingIDNumber` 的跨档静态缓存，实现 `ISDSResettable` 并在 `Apply` 成功后 `SDS_StaticState.Register(this)`（《ModuleInterfaces.zh-CN.md》二.2）；进程级反射常量不登记。
5. 报告中心登记：`SDS_CompatReportHub.Register(name, packageId, detected, report, note?, reason?)`，挂点用 `SDS_CompatPatchReport.Applied` / `Missing` 逐条记录；全部失败路径只 `ErrorOnce` / `Warn`，禁止 `SDS_Diag.Fail`。
6. 跑校验：`SDS.Tests/Compat/CompatAdapterTests.cs`（9 个适配器恰好齐全、`Name` / packageId / HarmonyId 唯一且与契约一致、目标缺失不 Apply、CE 与 Milira 声明清理、源码不出现 `SDS_Diag.Fail`）；`Tools/CheckCompatSurface.ps1`（入口归属 / 调用链 / 兼容层调用图）；`Tools/CheckPatchInventory.ps1`（112 口径不变）。同步登记本文件 §B、§F.1、§F.2 与《PatchInventory.zh-CN.md》动态节。

### F.4 登记与校验

| 位置 | 内容 |
|---|---|
| `SDS_CompatReportHub`（`SDS.Diag`） | 适配器级报告：名称、packageId、是否检测到、挂点 Applied / Missing、备注 |
| `SDS_StaticState`（`SDS.Diag`） | CE、Milira 各 1 项；`Apply` 成功后注册，`ResetAll` 时清理 |
| `CompatAdapterTests` | 身份与唯一性、目标缺失不 Apply、静态清理声明、源码不调 `SDS_Diag.Fail`、`SDS_API` 桥转发事实 |
| `CheckCompatSurface.ps1` | 每个 `SDS_ModCompat_*.cs` 有入口行；门面方法可分类；不得引用 Core / Vanilla |
| `CheckPatchInventory.ps1` | 第三方挂点不计入 Vanilla 112；只做动态挂点白名单 |

## 来源

- 规则：`Framework/docs/ModuleRules.zh-CN.md` 6.6 / 七 / 八；`ModuleResponsibilities.zh-CN.md` 二；`ModuleInterfaces.zh-CN.md` 一.1.1 / 一.1.2 / 二.2 / 五 / 六。
- 代码：`超维存储/Source/SDS.ThirdModCompat/` 全部源码（`SDS_CompatServices.cs`、9 个 `SDS_CompatAdapter_*.cs`、`SDS_ModCompat_*.cs`）；`SDS.Bootstrap/SDS_CompatLoader.cs`。
- 测试与脚本：`SDS.Tests/Compat/CompatAdapterTests.cs`；`超维存储/Source/Tools/Check*.ps1`。
- 旧实现对照：《PublicAPI.zh-CN.md》（已迁至本框架）二·六 / §3.6 / §4.8 / §4.10 / §4.11；`SDS_ModCompat_*.cs` 的 `TryApply` 挂点定义。
