# SDS.Vanilla 接口

> 定位：只登记本模块对外的 public 签名、本模块消费的 Contracts / `SDS_Host` / `SDS_Core` 面，以及调用关系。
> 完整全局契约见《ModuleInterfaces.zh-CN.md》；必须遵守的约束见《ModuleRules.zh-CN.md》与 [Rules.zh-CN.md](Rules.zh-CN.md)。签名以 `超维存储/Source/SDS.Vanilla/` 源码为准。

## 一、实现形态与存档契约（A 节）

> 模块间公开面只有 `SDS_Vanilla`（`SDS.Vanilla_API.cs`，E8）；其余类型要么是引擎面（XML / Scribe），要么是 internal 实现，本节保留它们作为维护索引。

### 1.1 存档壳与初始化

```csharp
public class SDS_GameComponent_Grid : GameComponent
{
    public static SDS_GameComponent_Grid Instance { get; private set; }
    public ISDS_LedgerHost Ledger { get; private set; }
    internal SDS_VanillaState State { get; private set; }
    public SDS_GameComponent_Grid(Game game);
    public override void ExposeData();
    public override void FinalizeInit();
    public override void GameComponentTick();
}

public static class SDS_Vanilla
{
    // 签名与成员语义的唯一权威：SDS.Vanilla_API.cs（E8）；本节只列索引
    public const string HotkeyActionOpen / HotkeyActionOutput / HotkeyActionUse;
    public static SDS_HostSlots CreateHostSlots();      // 启动第 2 步：装配 11 个宿主槽位
    public static int InstallPatches();                 // 启动第 3 步：安装特性补丁类
    public static int ApplyRuntimeHooks();              // 启动第 6 步：W63 落点守卫
    public static int PatchAppliedCount { get; }        // 启动报告：实际安装数
    public static int PatchDeclaredCount { get; }       // 启动报告：声明数
    public static void RegisterAll();                   // 启动第 5 步：静态清理 + 自检 provider
    public static bool HasGrid { get; }                 // 存档壳是否已建立
    public static void ReportUseProvidersOnce();        // 启动第 7 步：使用命令 provider 来源与排除表自检
    // 设置页 / 热键：NotifySettingsPageDrawn / HotkeyCapturing / HotkeyCaptureNote /
    // HotkeyCaptureAction / BeginHotkeyCapture / CancelHotkeyCapture / GetHotkeyLabel /
    // ClearHotkey / ResetHotkeys / ApplyStoragePriorityToEndpoints
}

internal static class SDS_PatchInstallerImpl { /* 实现，不构成对外面 */ }
```

`SDS_GameComponent_Grid.GameComponentTick` 的运行期职责（按源码顺序）：账本 Tick（`SDS_Ledger.Tick`，每 tick；内部每 250 tick 做一次保真 owner 巡检）→ Vanilla 状态 Tick（`SDS_VanillaState.Tick`：镜像同步 30 tick 节流、W60 首个 Tick 尝试后按 30 tick 重试、W48 30 tick 重试、250 tick 清废弃地图缓存；W60 在 `FinalizeInit` 另有一次立即重映射）→ 读档诊断（`TryLogLoadDiagnostics`，地图就绪后一次）→ 首档通知（`TrySendIntroNotice`，每档一次）→ **预留终老审计（250 tick，调 `SDS_Core.AuditReservations`）** → 回收账本巡检（250 tick，`SDS_ReclaimRegistry.Sweep`）→ 30 tick 块（地图签名检测 `CheckMapSignature` + 库存变化提醒 `TickStockNudges`）→ 结构对账（`SweepRegistries`，每 tick 入口、内部 250 tick 门，末尾校正端点优先级）。库存提醒自身再去抖：财富重算 300 tick、服装提醒 600 tick。读档后的首次预留审计在 `FinalizeInit`（地图 spawn 之后）完成，账本 `ExposeData` 不判活。

### 1.2 宿主实现

《ModuleResponsibilities.zh-CN.md》六把 11 个宿主槽位全部划归本模块；源码为 9 个实现类（`SDS_VanillaServices` 同时实现 3 个槽位）。
F2 起 9 个实现类全部 `internal`，由 `SDS_Vanilla.CreateHostSlots()` 装配为 `SDS_HostSlots` 交给 Bootstrap；下列声明仅作维护索引。

```csharp
internal sealed class SDS_EndpointProvider : ISDS_PortProvider
{ public void AppendCandidates(in SDS_EndpointQuery query, List<ISDS_Port> into); public bool TryGetPort(Thing thing, out ISDS_Port port); public bool TryGetSelfPort(Pawn pawn, out ISDS_Port port); public bool IsLocalUsable(Map map); }  // HasUsableBEndpointOn 已收缩为 private

internal sealed class SDS_VanillaServices : ISDS_DepositFilter, ISDS_PrioritySource, ISDS_DeliverySink
{ public bool Allows(Thing thing); public int Priority { get; } public bool IgnorePriority { get; } public bool TryPlace(Thing thing, Map map, IntVec3 cell); public bool TryGiveToPawn(Thing thing, Pawn pawn, bool preferCarry); }

internal sealed class SDS_MirrorService : ISDS_MirrorQuery
{ public static int Version { get; private set; } public static void Reset(); public static void NotifyStockChanged(); public IReadOnlyList<Thing> VisibleThingsOfDef(Map map, ThingDef def); public bool IsMirror(Thing thing); public static void CollectMirrors(List<Thing> outList); public static void CleanupLoadedMirrors(List<Thing> loaded); public static bool HasAnyMirrorOn(Map map); public static bool AnyMirrorInWorld { get; } public static void CopyMirrorsOn(Map map, List<Thing> outList); public static void CopyMirrorKeys(Map map, List<SDS_StockKey> outList); public static void CopyMirrorsForDef(Map map, ThingDef def, List<Thing> outList); public static bool TryGetMirror(Map map, SDS_StockKey key, out Thing mirror); public static long MirrorExposureOnResourceCounter(Map map, SDS_StockKey key); public static void NotifyVaultRelocated(Map map); public static float MirrorWealthOn(Map map); public static void SyncIfDirty(); public static void SyncAll(); public static void OnMirrorSpawned(Thing thing); public static void EvacuateForStructureChange(Map map, string reason); }

internal sealed class SDS_MirrorPhysicalOps : ISDS_MirrorPhysical
{ public bool TryTakePhysical(SDS_StockKey key, Thing mirror, int count, out Thing made); public bool TryTakeWholePhysical(SDS_StockKey key, Thing mirror, out Thing made); }

internal sealed class SDS_MedicalCareAdapter : ISDS_MedicalCarePolicy
{ public bool Allows(ThingDef medicine, Thing billGiver); }

internal sealed class SDS_ReclaimWorldAdapter : ISDS_ReclaimWorld
{ public int CurrentTick { get; } public bool JobExists(int jobLoadID); public bool LordExists(int lordLoadID); public void RemoveFromCaravan(int lordLoadID, Thing thing); }

internal sealed class SDS_DisplayAdapter : ISDS_DisplayQuery
{ public bool IsDisplay(Thing thing); public bool TryGetDisplayKey(Thing thing, out SDS_StockKey key); }

internal sealed class SDS_DepositJobsAdapter : ISDS_DepositJobs
{ public bool CanTakeCandidate(Pawn pawn, Thing thing, bool forced); public Job TryMakeJob(Pawn pawn, Thing thing, bool forced, Job vanillaJob, bool multi); public Job TryMakeFromInventory(Pawn pawn, IList<Thing> items); public bool IsManualDepositAvailable(Pawn pawn); }

internal sealed class SDS_TransportLoadAdapter : ISDS_TransportLoadSink
{ public bool InjectTransportLoadCandidates(ThingWithComps host, List<TransferableOneWay> transferables); }
```

### 1.3 建筑

```csharp
public class Building_SDS_Core : Building_WorkTable, ISDS_StorageEndpoint, IBillGiver
{ public Building_SDS_Core(); public bool Operational { get; } bool IBillGiver.CurrentlyUsableForBills(); bool IBillGiver.UsableForBillsAfterFueling(); public ThingOwner InputBuffer { get; } public bool HaulDestinationEnabled { get; } public bool Accepts(Thing thing); public int SpaceRemainingFor(ThingDef stuff); public bool StorageTabVisible { get; } public StorageSettings GetStoreSettings(); public StorageSettings GetParentStoreSettings(); public void Notify_SettingsChanged(); public ThingOwner GetDirectlyHeldThings(); public void GetChildHolders(List<IThingHolder> outChildren); public override void SpawnSetup(Map map, bool respawningAfterLoad); public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish); public override void Destroy(DestroyMode mode = DestroyMode.Vanish); public override void ExposeData(); public override string GetInspectString(); internal bool SyncStoragePriority(); }

public class Building_SDS_Port : Building, ISDS_StorageEndpoint
{ public Building_SDS_Port(); public bool Operational { get; } public ThingOwner InputBuffer { get; } public bool HaulDestinationEnabled { get; } public bool Accepts(Thing thing); public int SpaceRemainingFor(ThingDef stuff); public bool StorageTabVisible { get; } public StorageSettings GetStoreSettings(); public StorageSettings GetParentStoreSettings(); public void Notify_SettingsChanged(); public ThingOwner GetDirectlyHeldThings(); public void GetChildHolders(List<IThingHolder> outChildren); public override void SpawnSetup(Map map, bool respawningAfterLoad); public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish); public override void Destroy(DestroyMode mode = DestroyMode.Vanish); public override void ExposeData(); public override string GetInspectString(); internal bool SyncStoragePriority(); }

public class Building_SDS_CapacityArray : Building
{ public bool Operational { get; } public override void SpawnSetup(Map map, bool respawningAfterLoad); public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish); public override void Destroy(DestroyMode mode = DestroyMode.Vanish); public override string GetInspectString(); }

public class Building_SDS_Vault : Building_Storage, IHaulDestination, IStoreSettingsParent, IHaulSource
{ public const string DefName = "SDS_HiddenVault"; bool IHaulDestination.HaulDestinationEnabled { get; } bool IStoreSettingsParent.StorageTabVisible { get; } public ThingOwner GetDirectlyHeldThings(); public void GetChildHolders(List<IThingHolder> outChildren); public bool HaulSourceEnabled { get; } public override void ExposeData(); public override void SpawnSetup(Map map, bool respawningAfterLoad); public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish); }
```

### 1.4 缓冲、代理物、JobDriver、Comp、旧档壳

```csharp
public class SDS_EndpointBuffer : ThingOwner<Thing>
{ public SDS_EndpointBuffer(IThingHolder owner); public override bool TryAdd(Thing item, bool canMergeWithExistingStacks); public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks); public override int GetCountCanAccept(Thing item, bool canMergeWithExistingStacks = true); }

public class SDS_Proxy_Thing : Thing, ISDS_ProxyThing
{ public Thing ResolveReal(int count); /* 重写：SplitOff / CanStackWith / TryAbsorbStack / PreApplyDamage / Kill / Print / DrawGUIOverlay / GetInspectString / GetGizmos / GetFloatMenuOptions */ }
public class SDS_Proxy_ThingWithComps : ThingWithComps, ISDS_ProxyThing { /* 同一重写集合 */ }
public class SDS_Proxy_Apparel : Apparel, ISDS_ProxyThing { /* 同一重写集合 */ }

public class JobDriver_SDS_DepositToGrid : JobDriver { }
public class JobDriver_SDS_WithdrawFromGrid : JobDriver { }         // A=取物点，B=工作目标（装载时为容器）
public class JobDriver_SDS_WithdrawPreservedForBill : JobDriver { } // A=取物点，B=账单给予者

public class SDS_CompUsable : CompUsable { }
public class SDS_CompProperties_InstallChip : CompProperties_UseEffectInstallImplant
{ public HediffDef replacedHediffDef; public SDS_CompProperties_InstallChip(); }
public class SDS_CompInstallChip : CompUseEffect_InstallImplant
{ public SDS_CompProperties_InstallChip ChipProps { get; } public override AcceptanceReport CanBeUsedBy(Pawn p); public override void DoEffect(Pawn user); }
internal class CompProperties_SDS_DrawExtraOverlay : CompProperties { public GraphicData graphicDataExtra; }
internal class Comp_SDS_DrawExtraOverlay : ThingComp { public CompProperties_SDS_DrawExtraOverlay Props { get; } public override void PostDraw(); }
```

### 1.5 `SDS_VanillaState` 字段表（internal，存档节点契约）

| 字段 | 类型 | 节点 | LookMode | 说明 |
|---|---|---|---|---|
| `depositFilter` | `ThingFilter` | `depositFilter` | `Deep` | 全局唯一存入筛选；读档后用原回调重建 |
| `coreStoragePriority` | `int` | `coreStoragePriority` | `Value` | -1 = 未设置，回退 `SDS_Config.CoreStoragePriority` |
| `introNoticeShown` | `bool` | `introNoticeShown` | `Value` | 首档操作指南信只发一次 |
| `sidebarUI` | `SDS_SidebarUIState` | `sidebarUI` | `Deep` | 侧边栏折叠 / 滚动 / 选中行 |
| `mirrorScratch` | `List<Thing>` | `sdsMirrors` | `Reference` | 只作读档迁移；读后销毁并按账本重建 |
| `transportLoadScratch` | `List<SDS_TransportLoadSave>` | `transportLoadRequests` | `Deep` | W48：纯展示物装载行按「容器 thingID + 行号 + 品类键 + 欠量」落盘，读档后重建 |
| `mirrorJobTargets` | `List<SDS_MirrorJobTargetSave>` | `sdsMirrorJobTargets` | `Deep` | W60：读档后把 Job 的镜像目标按「loadID + 槽位 + 品类键」换到新镜像；同 key 无效镜像替换先建新镜像并按精确引用改指，再移除旧镜像（新建 / 落点 / 换新失败回退关闭）；仅处理仍指向旧镜像（读档清理后已 `Destroyed`）或已失效为 null 的槽，Job 已推进时跳过；键失效时先结束当前 Job / 排队出队 / 移除队列槽，再丢弃目标，不写 `LocalTargetInfo.Invalid`；旧档无节点视为空 |

运行期派生（不落盘）：`endpointCaches`（建筑端点按 `EndpointVersion` 缓存）、`filterVersion`、`tickCounter`；W60 三元组 `mirrorJobTargetRemapPending` / `mirrorJobTargetRemapDeadline` / `mirrorJobTargetRemapNextTick`（`FinalizeInit` 先调 `TryRemapMirrorJobTargetsImmediately` 立即尝试一次，失败才留到 Tick）；W48 三元组 `transportLoadRestorePending` / `transportLoadRestoreDeadline` / `transportLoadRestoreNextTick`。静态 `noStructures` / `staleMaps` 只服务「无锚点宽限」判定。`priorityVersion` 不存在。

## 二、消费面（B 节）

### 2.1 Contracts 类型与成员

| 类别 | 类型 / 成员 | 用途 |
|---|---|---|
| 账本 DTO | `SDS_StockKey`（`thingDef` / `stuff` 为运行期 Def 引用，非存档节点名；存档只写 `defName` / `stuffName` / `qualityInt` / `preserved`）、`SDS_StockEntry`、`SDS_StockVitals` | 镜像键、展示物键、快照与财富计算 |
| 端点 DTO | `SDS_EndpointQuery`、`SDS_EndpointDecision`、`SDS_EndpointOp` | 存入 / 取出决议 |
| 取货 DTO | `SDS_WithdrawQuery`、`SDS_WithdrawPlan`、`SDS_WithdrawMode`、`SDS_StockChannel`、`SDS_ReturnFallback`、`SDS_SupplyFailure` | 事务入口与回充 |
| API 快照 | `SDS_ReadVersions` | 读侧缓存失效判定 |
| 存档 DTO | `SDS_SidebarUIState`、`SDS_TransportLoadSave`、`SDS_ReclaimSave`（`sdsReclaim` 节点）、`SDS_ReclaimPolicy`、`SDS_ReclaimOutcome` | UI 状态、装载恢复、回收账本 |
| 结构 DTO | `SDS_StructureFact`、`SDS_StructureKind` | 核心 / 锚点 / 稳固装置登记 |
| 宿主接口 | `ISDS_Port`、`ISDS_PortProvider`、`ISDS_DepositFilter`、`ISDS_PrioritySource`、`ISDS_DeliverySink`、`ISDS_MirrorQuery`、`ISDS_MirrorPhysical`、`ISDS_MedicalCarePolicy`、`ISDS_ReclaimWorld`、`ISDS_DisplayQuery`、`ISDS_DepositJobs`、`ISDS_TransportLoadSink`、`SDS_HostSlots`、`SDS_MedicalGate` | 实现或调用；`SDS_HostSlots` 是 `SDS_Vanilla.CreateHostSlots()` 的返回 DTO；医药门经 `SDS_MedicalGate` 取值 |
| DefOf | `SDS_JobDefOf`（`DepositToGrid` 等）、`SDS_HediffDefOf`（`ChipA` / `ChipB`） | Job 与芯片判定 |
| 设置 | `SDS_Settings`（只读）、`SDS_Config`（`Current` / `Clamp` / 容量 / 电力 / 优先级 / 分流开关）、`SDS_WithdrawDeliveryPolicyDef` | 运行期读取 |
| 诊断 / 注册 | `SDS_Host`、`SDS_StaticState`、`SDS_SelfCheck`、`SDS_Diag`、`SDS_CompatGates`（装载注入闸门） | 注入、清理、自检、日志、兼容闸门消费 |

`SDS_JobDefOf` / `SDS_HediffDefOf` 由 Contracts 提供；`SDS_KeyBindingDefOf` 属本模块内部实现。

### 2.2 `SDS_Host` 槽位

| 槽位 | 本模块角色 | 实现 / 消费点 |
|---|---|---|
| `Ports` | 提供 + 消费 | `SDS_EndpointProvider`；`SDS_AccessPointResolver`、`SDS_Exposure`、`SDS_EndpointUtility` 读取 |
| `DepositFilter` | 提供 | `SDS_VanillaServices`（Core 消费） |
| `Priority` | 提供 + 消费 | `SDS_VanillaServices`；`SDS_EndpointProvider` / `SDS_Exposure` 读取 |
| `Delivery` | 提供 | `SDS_VanillaServices`（Core 回充消费） |
| `MirrorQuery` | 提供 + 消费 | `SDS_MirrorService`；`SDS_JobTargets`、`SDS_Search`、交易 / 编成读取 |
| `MirrorPhysical` | 提供 | `SDS_MirrorPhysicalOps`；由 Vanilla / Api 经 `TryTakeMirror(..., physical, ...)` 传入（Core 不读槽） |
| `MedicalCare` | 提供 | `SDS_MedicalCareAdapter`；补料侧由 Vanilla / Api 经 `SDS_MedicalGate` 取值（Core 不读） |
| `ReclaimWorld` | 提供 + 消费 | `SDS_ReclaimWorldAdapter`；回收账本 `SDS_ReclaimRegistry`（Job / Lord / 编成事实） |
| `Display` | 提供 | `SDS_DisplayAdapter`（Api 适配器桥） |
| `DepositJobs` | 提供 | `SDS_DepositJobsAdapter`（Api 适配器桥） |
| `TransportLoad` | 提供 | `SDS_TransportLoadAdapter`（Api 适配器桥） |

### 2.3 `SDS_Core` / `SDS_Rules` 门面调用（按用途）

| 用途 | 调用的公开方法 |
|---|---|
| 生命周期 / 读取 | `SDS_Core.Attach`、`Ledger`、`UsedCapacity`；`SDS_Rules.Available`、`EndpointVersion`、`TypeCapacity`、`TryGetStockKey` |
| 存入（Rules） | `SDS_Rules.CanDeposit`、`TryDeposit`、`IsProducedMarked`、`MarkProduced`；`SDS_Core.NotifyDepositFilterChanged` |
| 端点 / 结构 | `SDS_Rules.SelectEndpoint`；`SDS_Rules.NotifyEndpointChanged`、`RegisterStructure`、`UnregisterStructure`、`ReconcileStructures` |
| 取货 / 预留（Rules） | `SDS_Rules.TryWithdraw`、`TryWithdrawPreservedExact`、`TryWithdrawPreservedExactAdopted`、`TryReserveForJob`、`TryReservePreservedForJob`、`TryGetJobReservation`、`ReleaseJobReservation`、`TryWithdrawReservedForJob`、`TryTakeMirror`、`TryConsumeMirror` |
| 回充（Rules） | `SDS_Rules.ReturnThing`、`ReturnVirtualCount`、`ReturnThings` |
| 回收（本模块自有，不经 Core 门面） | `SDS_ReclaimRegistry.RegisterProtectedOutput` / `TrackJobTarget` / `TrackHand` / `ForgetJobTarget` / `ReclaimJobTarget` / `ReclaimHand`（C1 起回收账本整体归 Vanilla） |
| 迁移 | 已收口到 `SDS_Rules.ConvertPreservedGearToVirtual`（Bootstrap 直接调用；本模块不再暴露） |
| 状态写回 / 医疗判据 | `SDS_Core.ApplyQuality`、`ApplyVitals`、`SetForbidden`；医疗门改经 Contracts 的 `SDS_MedicalGate.Allows` |
| 只读口径 | `SDS_Availability.GetAvailableCount`、`GetFailure`、`GetTradableCount`；`SDS_Rules.GetAvailableCountForDef`、`TryFindWithdrawableKey`、`CategoryKeyOf`、`IsLegacyUnrebuildableDef`；`SDS_ImplantUtility` 的芯片查询与 `NotifyChipStateChanged` |

## 三、调用关系

```text
Bootstrap → SDS_Host.Install(SDS_HostSlots) → 本模块 9 个宿主实现（进程级，无参构造）
Bootstrap → SDS_PatchInstaller.Install()
    ├─ 112 个 [HarmonyPatch] 类（只扫 SDS.Vanilla）
    ├─ SDS_MirrorGuardRegistry（19 条）
    └─ SDS_ProjectionOwnerBridge（10 条）

原版入口 / 第三方桥 → 本模块补丁 / 适配器 / 服务
    → SDS_Rules 门面（决定 / 流程） → SDS_Ledger（账本，经 Core 友元）
    → SDS_Core 门面（账本生命周期 / 数据写回）
    → SDS_Host 槽位（事实回读：Ports / Priority / MirrorQuery）
    → 原版 API（Spawn / DeSpawn / Job / UI）

SDS_GameComponent_Grid.ExposeData → SDS_Ledger.ExposeData（Core 节点）
                                  → SDS_VanillaState.ExposeData（本模块节点）
```

## 四、原版支持

> 按「原版机制 → 接在哪」登记本模块与 RimWorld / Verse 的接触面；小节标题后括号保留原《原版支持.md》的节号，便于对照旧引用。规则语义见 [Rules.zh-CN.md](Rules.zh-CN.md)，全局约束见《ModuleRules.zh-CN.md》。

### 4.0 原版流程接入总览

> 按玩家可见的原版流程归类；同一流程只在一处实现，其余入口只转发。括号内为下文小节号。

| 原版流程 | 原版入口（按调用顺序） | 本 Mod 的接法 | 唯一实现 | 详见 |
|---|---|---|---|---|
| 自动搬运入库 | `WorkGiver_Haul.JobOnThing` → `HaulAIUtility.HaulToStorageJob`；`JobGiver_Haul.TryGiveJob`；产物 / 机会搬运 → `HaulToCellStorageJob` / `HaulToContainerJob` | Postfix 低优先级调 `SDS_DepositService`，端点赢过原版最佳目的地才改派 `SDS_DepositToGrid`；否则保留原版 Job | `SDS_DestinationResolver` + `SDS_DepositService` | 4.5 |
| 手动存入 | 右键「存入超维空间」、小人「存入背包」gizmo、第三方玩家 UI | 统一走 `SDS_DepositService.TryResolveManualDeposit`（`forced` + `AcceptsForManual`）；即时或 Job 只是交付方式不同 | `SDS_DepositService` / `SDS_ChipManualDeposit` | 4.5 / 4.7 |
| 交付执行 | `JobDriver_SDS_DepositToGrid` → 端点 `inputBuffer` → `SDS_Rules.TryDeposit` | 入缓冲后同 tick 数字化；失败落地并退避；不跨 tick 吞货 | `SDS_EndpointBuffer` / `SDS_EndpointUtility` | 4.1 / 4.5 |
| 从超维取物参与作业 | `GenClosest` 四适配器、`Reachability.CanReach`、`Toils_Goto.GotoThing`、`Pawn_JobTracker.StartJob`、`Toils_Haul.ErrorCheckForCarry`、四个 `JobDriver_*.Notify_Starting`、`Thing.SplitOff` / `TryAbsorbStack` / `ThingOwner.TryAdd` | 搜索 / 可达把镜像折算到访问点；Job 目标默认 Hand 不物化，消费点（W59）、数量缺失（W58）、拆分 / 携带 / 吸收（W5 / W20）、容器接收（W2）时换真身 | `SDS_SearchCore` / `SDS_AccessPointResolver` / `SDS_JobTargetAdapter` / `SDS_MirrorConsumeSeam` / `SDS_MirrorPhysicalOps` | 4.6 / 4.9 |
| 运输舱 / 传送门装载 | `Dialog_LoadTransporters` / `Dialog_EnterPortal` / `TransporterUtility.AllSendableItems` / `LoadTransportersJobUtility` / `EnterPortalUtility` / `SubtractFromToLoadList` | 对话框注入展示物行；`HasJob` 登记同 tick 快照、`JobOn` 消费快照并改派 `SDS_WithdrawFromGrid`；取到手上后交回原版 `HaulToTransporter` / `HaulToPortal` | `SDS_TransportVirtual` + `JobDriver_SDS_WithdrawFromGrid` | 4.8 |
| 远行队编成 | `Dialog_FormCaravan` 四个阶段 + `LordMaker.MakeNewLord` | 注入展示物行；出发前数量快照校验；正式组队时按身份把展示物换成真身 | `SDS_CaravanVirtual` | 4.8 |
| 交易 / 发射 / 远程买卖 | `TradeDeal.Reset` / `TryExecute` / `Tradeable.ResolveTrade` / `Dialog_Trade.Close` / `TradeUtility` 信标两入口 / 聚落与商队买卖 | 注入展示物；成交前只读校验；交收时展示物换等量真身；信标发射从账本补缺口；远程买入直接入账 | `SDS_TradeVirtual` / `SDS_BeaconDisplay` / `SDS_RemoteTrade` | 4.13 |
| 账单与医疗 | `WorkGiver_DoBill` 四入口（`IsUsableIngredient` / `TryFindBestBillIngredients` / `TryFindBestFixedIngredients` / `TryStartNewDoBillJob`） | 原版失败后先虚拟镜像、再保真真身；多槽 W51 物化后同 tick 重跑原版；纯保真原料改派取货 Job | `SDS_VirtualBillFallback` / `SDS_PreservedBillDispatch` | 4.14 |
| 专用原版决策 | `HealthAIUtility.FindBestMedicine`、`FoodUtility.TryFindBestFoodSourceFor`、武器 / 衣物 JobGiver | 原版返回「没有」时物化 1 件保真真身并落点；超时按来源回库 | `SDS_OfferBridge` + `SDS_PreservedOffer` | 4.15 |
| 基因 / 胚胎 / 棺材 / 安装 | `Building_GeneAssembler.GetGenepacks`、`Dialog_SelectXenogerm`、`Building_GrowthVat`、`Building_Casket.TryAcceptThing`、`GenConstruct.PlaceBlueprintForInstall` | 在原版 UI / 容器入口先把保真真身物化，再交原版流程 | `SDS_PreservedUsage` | 4.14 |
| 读数（资源条 / 财富 / 清单 / 可及性） | `ResourceCounter`、`WealthWatcher`、`RecipeWorkerCounter`、`Alert_NeedWarmClothes`、`PlayerItemAccessibilityUtility` | 只补「账本目标量 − 原版已数到的物理量」的差额；口径唯一在 `SDS_Exposure` / `SDS_Selection` / `SDS_ReadOnlyUI` | 同左 | 4.3 / 4.4 |
| 世界 / AI 视角 | 房间四 API、`PawnObserver`、发脾气、纵火、偷窃、可研究、可搬运、可合并、可卖 / 可发射、编成原始清单 | 镜像与金库统一隐藏 / 排除；只有显式设置（W52 / W55）才重新进入搬运候选 | `SDS_MirrorGuardRegistry` / `Patch_Mirrors` | 4.1 / 4.2 / 4.11 |
| 存档与生命周期 | `GameComponent` 构造 / `ExposeData` / `FinalizeInit` / `Tick`；建筑与 Pawn 的 `SpawnSetup` / `DeSpawn` | 账本 + Vanilla 状态 + 回收账本三类节点；读档后先重建镜像再审计 | `SDS_GameComponent_Grid` / `SDS_VanillaState` | 4.10 |
| UI 与热键 | 侧边栏、输出 / 使用对话框、存入筛选 ITab、播放设置按钮、`WindowStack` 热键 | 本 Mod 窗口 + 唯一入口转发；不新建原版窗口类 | `Window_SuperSpace` / `Window_SDS_OutputDialog` / `SDS_Hotkeys` | 4.7 |

### 4.1 容器与派活契约：IHaulDestination / IStoreSettingsParent / IThingHolder / IHaulEnroute / IHaulSource（原 §一）

- **原版机制**：`IHaulDestination` 决定 Thing 是否参与非 SlotGroup 目的地搜索（`Accepts` 是收货闸门，`HaulDestinationEnabled` 决定是否可见）；`IStoreSettingsParent` 提供 `StorageSettings` 与优先级，供原版排序；`IThingHolder` 的 `GetDirectlyHeldThings` 是容器直接内容、`GetChildHolders` 是递归子树；`IHaulEnroute.SpaceRemainingFor` 供在途预留；`IHaulSource` 让 `WorkGiver_DoBill` 等入口沿 holder 递归读取内容。
- **接在哪**：`ISDS_StorageEndpoint` 同时继承这四个接口，由 `Building_SDS_Core` / `Building_SDS_Port` 实现（`SDS_Buildings.cs`）。端点绝不能实现 `ISlotGroupParent`：一旦实现，原版 haul 的容器分支永不成立（同文件接口注释）。
- **写穿语义**：`SDS_EndpointBuffer.TryAdd` 与 `GetCountCanAccept` 共用同一个 `Accepts` 硬门；成功入缓冲后只处理这一件，失败 `EjectToMap`，不跨 tick 吞货（`SDS_EndpointBuffer.cs`）。`CanAcceptNow` 在读档期（`Scribe.mode != Inactive`）与非 `Playing` 状态一律放行存档残留，刷写只在 `Playing` + `Inactive` 下发生。
- **金库**：`Building_SDS_Vault` 实现 `IHaulDestination` + `IStoreSettingsParent` + `IHaulSource`，`HaulDestinationEnabled=false`、直接内容为空，投影挂在 `GetChildHolders`；这样既是只计数的 `SlotGroup`，又不会在直接内容枚举里重复计库存（`SDS_Buildings.cs`）。
- **仓库代理口径（W53）**：镜像的存储事实只由每图 `SDS_MirrorHaulDestination` 代理提供——`Patch_SDS_MirrorHaulDestination` 让 `CurrentHaulDestinationOf(镜像)` 返回代理（`Accepts` 恒真、`HaulDestinationEnabled=false`、优先级在 `GetStoreSettings` 中同步存档级值），从而 `IsInAnyStorage` / `IsInValidStorage` 为真、`CurrentStoragePriorityOf` 取存档级优先级。金库自身的 `StorageSettings` 不参与该判定，不得另写一份。
- **镜像不可存入（W53）**：`SDS_EndpointUtility.IsDepositable` 是唯一判据（非镜像 + `SDS_Rules.CanDeposit`）；端点 `IHaulDestination.Accepts`、`SDS_DepositService` 的派活闸门与手动存入全部经它，镜像一律拒绝。否则 W52 的流出 Job 会被存入竞争改写、端点也会被当成镜像的流出目的地。
- **镜像落点**：镜像本身不单独选格，位置由隐藏金库提供；`Building_SDS_Vault.FindVaultCell` 只接受「图内、未迷雾、可站立、无建筑、无 `Zone`、无 `SlotGroup`」的格，并在落子前查 `GetMaxItemsAllowedInCell > GetItemCount`，否则 `ScheduleRetry`（`SDS_Buildings.cs`）。
- **区域例外**：`IsFreeCell` 只排 Zone 与 SlotGroup、不排 Region，因此金库 / 镜像可以落在区域内；区域内落点仍需通过其余校验。
- **直接内容枚举**：`GetAllThings(lookInHaulSources: true)` 只读 holder 的单层直接内容；`ApparelSource` 把金库当空衣物源，避免投影被当成可穿物。
- **仓库代理语义**：`Accepts` 恒真、`HaulDestinationEnabled = false`、优先级跟随全局设置、`Position` 恒为 `Map.Center`（仅占位，不参与距离与落点判定）。
- **端点候选枚举**：`SDS_EndpointProvider.AppendCandidates` 只做「建筑事实 + Pawn 芯片事实」枚举，不在枚举期判 `Operational`；可用性由 Core 的硬门实时判定。建筑列表按 `EndpointVersion` 缓存，Pawn 每 tick 实时扫；自身端口最后加入（`allowSelf` 且本人端口可用），按参考点实时算距离；折叠为自身由 W62 在端点胜出后完成。

### 4.2 Map.GetChildHolders 去重（原 §二）

- **原版机制**：`Map.GetChildHolders` 枚举 `listerThings.ThingsInGroup(ThingHolder)` 等来源；`ThingOwnerUtility.GetAllThingsRecursively(map, ...)` 会同时读 `listerThings` 与 Map 的子 holder。
- **接在哪**：核心、锚点、金库各自挂着镜像投影，带芯片小人也会挂 `SDS_ProjectionHolder`；原样递归会把同一批镜像算多次。`Patch_SDS_MapChildHoldersExposure` 对 `Map.GetChildHolders` 做 Postfix，摘掉 `ISDS_StorageEndpoint` 与 `Building_SDS_Vault`；存在镜像时把带可用芯片的 Pawn 替换成 `SDS_MapLevelHolder`，保留原 Pawn 的库存 / 搬运 / 装备子 holder，只过滤投影（`Patch_VanillaSeams.cs`）。
- **边界**：保真真身不在这里暴露；其财富由 `Patch_SDS_WealthNormalize` 按图补加。

### 4.3 ResourceCounter.UpdateResourceCounts 资源条（原 §三）

- **原版机制**：资源条按 `SlotGroup.HeldThings` 汇总 `CountAsResource` 的实物，结果缓存在 `countedAmounts`，`ResourceCounterTick` 周期性刷新。
- **接在哪**：镜像每图每品类只有 1 个，`stackCount` 代表整品类库存，保真真身不 spawn，只读物理计数会看不到账本量。`Patch_SDS_ResourceCounterWithGrid` 对 `UpdateResourceCounts` 做 Postfix，按 `SDS_Exposure.InjectedOnCounter` 补差额；`Patch_SDS_ResourceCounterFastRefresh` 对 `ResourceCounterTick` 做 Prefix，只在 30 tick 节流内触发一次刷新，不改原版结果（`Patch_VanillaSeams.cs`）。
- **口径归属**：该图是否暴露库存由 `SDS_Exposure.MapExposesGrid`（G + 本图可用端点）决定；补丁只消费 `SDS_Exposure`，不自行推导「该加多少」（`SDS_Exposure.cs`）。
- **校正公式**：`CounterAdjust = (暴露 ? 账本量 : 0) − 物理曝光`；物理曝光来自镜像的实际堆量，所以原版已计入的那一份会先被扣回。镜像存在但原版没数到（迷雾 / 不新鲜）时目标按 0 计，不用账本兜底。def 级汇总 `InjectedOnCounter` = 该 def 全部虚拟键的校正量 + 仅存在于镜像表的键的校正量 + 暴露时的保真数量。
- **快速刷新**：`TryBeginCounterRefresh` 只在「非 `now % 204 == 0`（原版本 tick 会整表重算）+ 镜像 / 端点 / 库存 / 预留 / 筛选五个版本有变化 + 距上次 ≥ 30 tick + 本图暴露」时放行；否则交回原版 `ResourceCounterTick`。

### 4.4 WealthWatcher.CalculateWealthItems 财富（原 §四）

- **原版机制**：`WealthWatcher.CalculateWealthItems` 通过 `ThingOwnerUtility.GetAllThingsRecursively(map, HaulableEver, allowUnreal:false, WealthItemsFilter)` 统计地图物品财富。
- **接在哪**：镜像 spawn 在金库格会被计入、保真真身不 spawn 会被漏计、设置「不计入殖民地财富」时还要扣掉所有图的镜像财富。`Patch_SDS_WealthNormalize` 对 `CalculateWealthItems` 做 Postfix，调用 `SDS_Exposure.WealthAdjust`，结果夹到 0；`Patch_SDS_WealthItemsFilterProjection` 处理 `WealthItemsFilter` 对 `SDS_MapLevelHolder` / 投影的过滤（`Patch_VanillaSeams.cs`、`Patch_Mirrors.cs`）。
- **校正公式**：`WealthAdjust` 返回三条互斥分支——① `excludeGridFromWealth` 开启：`−本图镜像财富`（所有图都不加保真）；② 本图不暴露或不是规范图：`−本图镜像财富`；③ 规范图：`账本虚拟财富 + 保真财富 − 本图实际镜像财富`。结果在补丁里夹到 0。镜像财富只算已 spawn、非迷雾、`EverHaulable` 的镜像堆。
- **规范图**：`CanonicalExposureMap` 优先玩家主基地，否则第一张 `MapExposesGrid` 的图；同一份库存只在规范图计一次，避免多殖民地重复。
- **holder 过滤**：`WealthItemsFilter` 对 `SDS_MapLevelHolder` 复刻原版 Pawn 口径（玩家阵营且非任务暂住者），对 `SDS_ProjectionHolder` 恒放行（投影已由规范化扣减 / 补加处理）。

### 4.5 HaulAIUtility 三个入口与 WorkGiver_Haul / JobGiver_Haul（原 §五）

- **原版机制**：`HaulAIUtility.HaulToStorageJob` 是「物品 → 仓储」的唯一函数级 Job 工厂，`WorkGiver_Haul`、`JobGiver_Haul`、KAU 紧急搬运、`HaulFromSource` 浮窗、`Toils_Haul.StoreThingJob`、`JobDriver_Reading` 都经它；`HaulToCellStorageJob` 被账单产出（`Toils_Recipe`）与机会搬运（`Pawn_JobTracker.TryOpportunisticJob`）直调；`HaulToContainerJob` 被容器分支与机会搬运直调。
- **接在哪**：`Patch_SDS_HaulToStorageJob`、`Patch_SDS_CarriedProductToGrid`、`Patch_SDS_HaulToContainerJob` 均为 Postfix + `Priority.Low`，统一调用 `SDS_DepositService`；端点不赢则保留原版结果。`Patch_SDS_HaulGeneralToGrid` 只在结果已经是 `DepositToGrid` 时回填 `workGiverDef`，不二次决议；`Patch_SDS_JobGiverHaul` 只在原版 `__result == null` 时补派（`Patch_VanillaSeams.cs`；`SDS_Destinations.cs`）。
- **边界**：只消费落点格的 WorkGiver 由 W63 运行期枚举统一折叠落点（`AccessCellOf`，不可站时兜底建筑格，不保证可走；落点不可达则退让），失败才退让；形状判定唯一实现在 `SDS_CellOnlyJobShape`（`Patch_VanillaSeams.cs`）。端点是 non-`SlotGroup` 目的地，格子身份由 W63 在 Job 层合成（`job.targetC`），不实现 `ISlotGroupParent`。
- **目的地白名单**：储存格、料斗、发酵桶、工作台、棺材、运输舱等功能性目的地一律不接管；未知容器 / 格也不接管，每 def 只记一次诊断。分类唯一实现在 `SDS_DestinationClassifier`（端点 / `ISlotGroupParent` 仓储组 / 功能性 / 未知）。
- **派活硬门**：`CommonGates` = 玩家阵营、已 spawn、同图、`CanDepositThing`、`CanReserveAndReach`（强制用 `Danger.Deadly`）；自动路径再加 `AutoOnlyGates`（非禁止、无产出标记、不在最优储存（W55 开启时例外）、`PawnCanAutomaticallyHaulFast`、无在途 / 无失败冷却）。多件收集上限 8 件、半径 12 格、超重即停。
- **分件口径**：整堆优先；背包装不下时按 `pawn.inventory.innerContainer.GetCountCanAccept(item, false)` 切分，`canMerge = false`；入包失败 / 部分失败按剩余量落地并记失败退避（不再另算 CE 背包容量）。
- **部分存入回执**：只存下 X 时回执「已存 X、剩余 Y 在地面」，玩家可再次右键续存。
- **手动存入唯一决议（W57）**：右键「存入超维空间」与「存入背包」共用 `SDS_DepositService.TryResolveManualDeposit`（`SDS_SelfDepositFold.TryResolveManual`：端口 → `AcceptsForManual`）；即时 / Job 只是交付方式不同，判定不得分叉。自动搬运 / 回充只用 `AcceptsForAuto`；新增手动入口必须调用该决议，不得再传 `allowProducedMarked` 字面量。
- **所有权转移与读档归一化**：入账所有权转移只在 Core 的 `SDS_Ownership.TryTransferTo`；驱动器不再预摘背包。读档时 `SDS_EndpointUtility.PruneLoadedOwnership(ThingOwner, string)` 是唯一清理实现，端点缓冲与小人背包 / 手持共用 `IsPreservedThingId` 判据（由 `SDS_GameComponent_Grid.FinalizeInit` 触发），只移除重复引用，不 Split / Destroy / 改数量。
- **账单产出**：产出先落在 `carryTracker`，由 `TakeCarriedIntoInventory` 入账；打断后落地，因此「挂起不吐货」不适用。
- **优先级流出（W52）**：设置开启且「最高优先级」关闭时，`Patch_SDS_MirrorNotHaulable` 不再强制排除镜像；由原版 `ListerHaulables.ShouldBeHaulable` / `StoreUtility.TryFindBestBetterStorageFor` 只在严格更高优先级且允许进入的目的地存在时生成搬运 Job。`SDS_MirrorService` 每 30 tick 刷新镜像 haulable 状态；`SDS_MirrorCarry` 按 `total − reserved` 夹取，目的地格 / 容器与镜像 Job 预留仍走原版。该判据的「当前优先级」由仓库代理（W53）提供；镜像不可存入任何端点（W53），端点不能成为流出目的地。
- **最高优先级（W55）**：同一个 `ShouldBeHaulable` 挂点里，`SDS_DepositService.IsGridAlwaysBetter` 让「原版判定已在最优储存」的可存实物重新进入候选；`AutoOnlyGates` 对同一判据忽略 `IsInValidBestStorage` 拒绝；`SDS_DestinationResolver.TryDecideCore` 的存入决议跳过与原版目的地的优先级比较，直接采用超维端点（随后按 W62 折叠自身）。判据要求已 spawn、非镜像、未禁止、无产出标记、`CanDeposit` 且本图有可用端点；owner 型容器内容物不 spawn，不在覆盖内；设置关闭时零行为变化。

### 4.6 GenClosest 搜索四适配器（原 §六）

- **原版机制**：`GenClosest.ClosestThing_Global`、`ClosestThing_Global_Reachable`、`ClosestThingReachable`（先区域 BFS，必要时全局）、`ClosestThing_Regionwise_ReachablePrioritized`（区域 + 优先级）是四类对外重载。
- **接在哪**：镜像按真实 def spawn 在金库格，区域 BFS 只会扫到金库所在区域，而镜像的实际交互位置是端点访问点。两个 Global 重载由 `Patch_SDS_ClosestGlobal` / `Patch_SDS_ClosestGlobalReachable` 做 Prefix 完整重算；两个区域重载由 `Patch_SDS_ClosestReachable` / `Patch_SDS_ClosestRegionwisePrioritized` 做 Postfix 补镜像候选；可达性由 `Patch_SDS_ReachabilityMirror` 改写为「任一访问点可达」。距离折算所需的访问点集合由 `SDS_AccessPoints.cs` 提供：镜像的排序距离走最近访问点，非镜像仍走原版 `PositionHeld` 距离（`SDS_Search.cs`、`SDS_AccessPoints.cs`）。
- **边界**：Postfix 无法区分 Region / Global 阶段，按 Global 语义与原版 best 二选一，属于有意偏差；`RegionwiseBFSWorker` 不单独打补丁。
- **规则**：S2 / S4 / S9 / S10 / S12a / S12b 六条正文见 [Rules.zh-CN.md](Rules.zh-CN.md) §九。
- **`lookInHaulSources`**：只覆盖区域阶段，且只读单层直接内容；全局回退不传该参数，避免把子树计入候选。
- **非玩家取用门（S18）**：设置开启时，`TryReplaceWithMirror`、`FindGlobal` 的 `lookInHaulSources` 分支、`Patch_SDS_ReachabilityMirror` 与 `SDS_AccessPointResolver.TryGetNearestReachableAccess` 对已知非玩家 Pawn 直接跳过；未知 actor 保持原版口径。
- **区域成员判定**：`ThingFromRegionListerReachable` 只回答「是否属于该区域」，`Touch` 恒真，不承担可达性与距离过滤。
- **候选池口径**：`customGlobalSearchSet` 只取镜像；`singleDef` 覆盖全品类；结果按 `Thing` 去重；未解析到供给的键按 0 计。
- **访问点集合**：访问点 = 本图所有可用端点（核心、锚点、带芯片小人、机械体）的 `AccessCell` + `AccessMode`，只保留图内格；60 tick 重建，结构 / 芯片变化或 Reset 立即失效。
- **取货点唯一规则**：`TryGetNearestReachableAccess` 在「自身端口可用且起点就是本人所在格」时直接返回自身端口（距离 0，含可达，不进缓存），否则取最近可达建筑访问点；`TryGetNearestReachable` 只转发这条规则。镜像走位、Job 物化、保真 / 虚拟供给桥（`SDS_OfferBridge.TryGetAccess`）与 `CanReachViaAccess` 共用，不得各自另写。
- **镜像真身不是取货点（W54）**：`Patch_SDS_MirrorTargetGoto` 对镜像目标解析不到可用端点时按目标不可达结束 Job（不回落到真身坐标）；`SDS_MirrorCarry` 对没有自身端口的小人要求本人位于访问点命中范围（水平距离平方 ≤ 2）才允许取货；存入执行器的取货循环跳过镜像、不按真身坐标走位。`SDS_Transmission` 拒绝镜像进入小人所属 owner（背包 / 装备 / 衣物）——上手只能走 `SDS_MirrorCarry`；PUAH 适配器的候选 / 派活对镜像直接拒绝，卸货清单中的镜像引用按 `SDS_StockKey.SameAs` 映射回背包真身，避免统一决议让出后由 PUAH 自己把货送进核心 / 容器。

### 4.7 UI 面：FloatMenuOptionProvider / ITab / Dialog（原 §七）

- **原版机制**：`FloatMenuOptionProvider` 为右键菜单提供选项；`ITab` 绘制选中物的检视页；`Dialog_Trade`、`Dialog_FormCaravan`、`Dialog_LoadTransporters`、`Dialog_EnterPortal` 各自构建 `Tradeable` / `TransferableOneWay` 清单。
- **接在哪**：虚拟库存只在账本中、保真真身不 spawn，界面必须用展示物让玩家「看得到、选得了、算质量」，同时不替换原版对话框流程。`SDS_ITab_DepositFilter.cs`、`Window_SuperSpace.cs`、`Window_SDS_OutputDialog.cs` 分别提供存入筛选页、侧边栏、输出 / 使用 / 安装对话框；`SDS_UI.cs` 提供输出 / 安装入口、只读清单缓存与工作文案；`SDS_Use.cs` 在「使用」命令里镜像 `FloatMenuMakerMap.GetProviderOptions` 的单目标分支，只读取 `FloatMenuOption.action == null` 与 `Label`，不自己构造使用 Job。
- **挂点清单**：`FloatMenuMakerMap.GetOptions`（只追加「存入超维空间」一条，不改写原菜单，`SDS_Implants.cs`）、`Pawn.GetGizmos`（追加「存入背包」gizmo）、`PlaySettings.DoPlaySettingsGlobalControls`（播放设置「超维」按钮）、`WindowStack.HandleEventsHighPriority`（热键：C 开关 / Q 输出 / E 使用 / 中键确认）、`RecipeWorkerCounter.CountProducts`（清单产物计数）、`Alert_NeedWarmClothes.FreeWarmClothesSetsCount`（寒冷预警）、`JobDriver.GetReport` 与 `JobDriver_HaulToContainer.GetReport`（当前工作文案）。全部为 Postfix / 单点 Prefix，异常静默。
- **热键口径**：仅在 `Playing`、有当前地图、非世界视图、无搜索框 / 输入窗口时响应；建造模式、Gizmo 热键、Targeter 瞄准、焦点控件会让位。设置页捕获只写 `KeyPrefs` A 槽并清空 B 槽。
- **边界**：交易 / 编成 / 装载对话框只注入展示物行，不新建窗口类；侧边栏「使用」复用 `Window_SuperSpace`，不另开窗口。
- **provider 排除表**：共 33 类排除项；`WorkGivers` 必须排除（否则穿衣会被判成存回），`FromThing` 必须纳入；使用类 provider 优先排序。
- **targeting 与理由**：targeting 阶段不做过滤；三档理由只在唯一处产生；仅 `members > 1` 时弹记录。
- **保真行**：保真行不得按 `selectedPreserved` 一刀切，必须逐行判定。
- **只读通道提示**：侧边栏保真行保留 `(保真)` 行尾标记，虚拟 / 保真行的悬浮提示分别说明该通道保留与不保留的信息；存入筛选页底部固定显示只读图例；输出 / 使用 / 安装对话框的记录按钮与确认按钮悬浮提示附带通道说明。提示只消费 `SDS_StockKey.preserved`，不参与分流、不写账本、不新增设置或存档字段。

### 4.8 TransferableOneWay 装载面（原 §八）

- **原版机制**：`TransferableOneWay` 是远行队编成与运输舱 / 传送门装载共用的行结构，携带 `things`、`CountToTransfer` 与 `TransferAsOne` 分组；交易使用 `Tradeable`。对话框每次重建清单。
- **接在哪**：账本条目和保真真身不能作为原版清单里的真实可搬物，必须注入展示物行，并在出发 / 装载时用真身替换、读档时按身份与欠量重建行。
- **编成挂点**：`Dialog_FormCaravan.CalculateAndRecacheTransferables`（前缀清理上一轮展示物）、`AddItemsToTransferables`（Postfix 注入虚拟 + 保真桶展示物）、`TryFormAndSendCaravan`（出发瞬间 `AnyVirtualNeeded` + `ValidateQuantities` 校验，失败拒绝出发）、`DebugTryFormCaravanInstantly`（含超维行时拒绝调试瞬间编成）、`PostClose`（非选路线阶段清理）、`LordMaker.MakeNewLord`（`LordJob_FormAndSendCaravan` 时按身份把展示物换成真身）（`Patch_CaravanTransport.cs`、`SDS_CaravanTransport.cs`）。
- **编成差额（W25 编成腿）**：`deficit = CountToTransfer − RealCount`，`RealCount` 只累加 `holdingOwner == null` 且非展示物的实物；`have = GetAvailableCount(虚拟键) + GetAvailableCount(保真键)`，`have < deficit` 即拒绝。物化先进编成成员背包（自身端口可用时），收不下的再落端点并登记受保护输出。
- **装载挂点**：`TransporterUtility.AllSendableItems`（任务穿梭机 `CompShuttle.requiredItems` 点名品类，虚拟 + 保真桶，保真排除缩装）、`Dialog_LoadTransporters.AddItemsToTransferables`（受 `SDS_CompatGates.AllowsTransportLoadInjection` 闸门）、`Dialog_EnterPortal.AddItemsToTransferables`、`LoadTransportersJobUtility.HasJobOnTransporter` / `JobOnTransporter`、`EnterPortalUtility.HasJobOnPortal` / `JobOnPortal`、`CompTransporter.SubtractFromToLoadList`、`MapPortal.SubtractFromToLoadList`（`Patch_TransportLoad.cs`、`SDS_TransportVirtual.cs`）。
- **装载流程**：`HasJob` 选行并登记同 tick 快照（虚拟键 / 保真 plan / 端点）→ `JobOn` 只消费快照并生成 `SDS_WithdrawFromGrid`（A = 取物点，B = 容器）→ 驱动器首 toil 预留 → 走到访问点 → 物化到 `carryTracker` → 交回原版 `HaulToTransporter` / `HaulToPortal` 带着货走到舱 / 门并投入。`JobOn` 在快照缺失时不重新求值（含 `FindThingToLoad`），原样保留原版结果并一次性 `Warn`；下一次 `HasJob` 会重新生成快照（W42）。
- **装载差额（W25 装载腿）**：`deficit = CountToTransfer − 在途量`；在途量只统计原版 `HaulToTransporter` / `HaulToPortal` 且能映射到本行的 Job（本 Mod 自己的待派活不计入）。单趟夹取 `ClampLoadTake` = `ClampCarry(deficit, MaxStackSpaceEver)` 再 `ClampTakeToAvailable`（可用量只经 `SDS_Availability`）；携带上限 ≤ 0 时返回 0、本趟不派活；保真分支再受展示物可代表单位 `max(1, display.stackCount)` 上限。
- **装舱记账（W40）**：`SubtractFromToLoadList` Prefix 按「真身 → 原版装载行」定位唯一行：① 行内已含该真身；② 展示物登记桶包含该真身；③ 封存键相等且该行无登记桶（同键多行返回 null，交原版）。命中后把真身放回该行 `things`，扣数 / 移除行 / 完成消息仍由原版执行；运输舱、传送门与穿梭机共用同一身份规则。
- **存档面**：装载行欠量快照写入 `SDS_VanillaState.transportLoadScratch`（节点 `transportLoadRequests`，`LookMode.Deep`）；只有「整行都是展示物且无桶或同属一桶」的纯展示物行会被记录，展示物引用不跨存档（《ModuleInterfaces.zh-CN.md》4.1）。读档后先立即尝试一次 W48 行恢复，其后 250 tick 内每 30 tick 重试：优先复用原 `rowIndex` 的空行，其次任一空行，最后新建行；`ForceTo(units)`。驱动器若发现运行期计划为空且账本仍有保真预留，则按 W47 从容器内唯一桶重建计划；找不到唯一桶或不足额即 fail-closed。
- **边界**：第三方自建装载清单走 W50 注入桥，见 §5.1。

### 4.9 物化语义：CompRottable / ThingOwner / Thing.SplitOff（原 §九）

- **原版机制**：`ThingOwner` 是容器 / 背包 / 运输舱 / 棺材 / 基因库的统一入口（`TryAdd`、`TryAddOrTransfer`、`TryTransferToContainer`、`GetCountCanAccept`）；`Thing.SplitOff` 产出指定数量的新件；`CompRottable` 保存腐烂进度；`Thing.Destroy` 是不可逆消费。
- **接在哪**：镜像是整品类聚合堆，部分取必须产出带原 `HitPoints` / `RotProgress` 的真实件，整堆取必须把镜像本体转成真身，所有容器写入都必须把镜像换成真身且只换一次。`SDS_MirrorPhysicalOps.TryTakePhysical` 用 `SDS_OutputUtility.MakeDisplayThing` 建件、改 `stackCount`、写回 HP / 腐烂、`MarkChanged`；`TryTakeWholePhysical` 做 `ForgetMirror` / `Unmark` / 恢复 `CompRottable.disabled` / `DeSpawnOrDeselect`。`SDS_Transmission` 的 `Patch_SDS_OwnerTryAdd` / `Patch_SDS_OwnerTryAddOrTransfer` 在容器入口把镜像换成真身；`SDS_EndpointBuffer` 在缓冲入口先过 `Accepts`（`SDS_Mirrors.cs`、`SDS_Transmission.cs`、`SDS_EndpointBuffer.cs`；《ModuleInterfaces.zh-CN.md》4.3）。
- **W51 多槽配方的保真缺口**：原版 `WorkGiver_DoBill.TryFindBestBillIngredients` 只从地图实物里选料，而 `TryFindBestBillIngredientsInSet` 要求**配方所有槽位**都能从传入候选里满足；保真真身不在图上，单通道候选永远补不齐（典型：复活机械体 = 1 尸体 + N 钢）。唯一做法是：原版搜索失败后，按原版返回的 `missingIngredients` 逐槽从保真通道取真身，物化到 `SDS_VirtualBillFallback.RootCell`（= 建筑有交互格时的 `InteractionCell`，否则台位格；源码注释里的 `IngredientPlaceCellsInOrder[0]` 只是语义说明，代码不读该属性），然后在同一 tick 重跑原版搜索；随后由原版自己搬运、`DepositHauledThingInContainer` 入容器。件数 = `Math.Max(1, IngredientCount.CountRequiredOfFor(...))` 再夹到该真身 `stackCount`。**不打来源排除标记**：物化实例在同一 tick 就被原版 Job 接手；若玩家随后取消清单，尸体留在地上并可被端点自动吸回（自愈）。物化要求 `billGiver is Building` 且 `HasReachableEndpoint(pawn)`（**不套用 `ingredientSearchRadius`**）；任一件落不下即把本批已落地的收回账本。禁止直接写工作台 / 培育器容器，禁止复活已删除的批量预留 / 批量物化机制。实现锚点：`SDS_PreservedBill.cs`（`TryMaterializePreservedGaps` + `Patch_SDS_PreservedBillIngredients` 重入守卫）。
- **投影不是真实容器**：`TryAdd` / `GetCountCanAccept` 对投影恒 false / 0；`Remove` 超过 `stackLimit` 直接返回 false；`Contains` 不保证 false——首次读取前内部列表为空，任何触发 `EnsureFresh` 的读取（`Count` / 枚举 / `IndexOf` / `Remove`）之后会命中镜像引用，第三方必须改用 `Take` / `SplitOff`。
- **非玩家接收门（S18）**：`SDS_Transmission` 的 `TryAdd` / `TryAddOrTransfer` 在镜像入容器前按 `OwnerPawn` 判阵营；装备 / 衣物 / 背包属于已知 Pawn owner，设置开启时直接拒绝。
- **S18 的无 actor 残余**：`Thing.SplitOff` / `TryAbsorbStack` / 通用 `ThingOwner` 没有 Pawn 上下文，不能直接判阵营；它们依赖搜索 / 可达 / Job 门先阻断，第三方绕过这些门直接操作镜像不在保证范围内。

### 4.10 GameComponent 生命周期与存档时机（原 §十）

- **原版机制**：`GameComponent` 每个 `Game` 构造一次，随存档 `ExposeData`、读档后 `FinalizeInit`、每 tick `GameComponentTick`。本模块自 2026-11-16 起不再有 `MapComponent`（旧受保护输出壳已退役），所有存档状态集中在唯一的 `SDS_GameComponent_Grid`（《ModuleInterfaces.zh-CN.md》4.1、4.4）。
- **接在哪**：账本必须挂在 `Game` 上而不是 Mod 静态字段，才能随存档保存、读档恢复、换档清理。`SDS_GameComponent_Grid` 构造顺序固定为 `SDS_StaticState.ResetAll()` → `SDS_Core.CreateLedger()` → `new SDS_VanillaState()` → 设置 `Instance` → `SDS_Core.Attach(Ledger)`（`SDS_GameComponent_Grid.cs`）。
- **ExposeData 节点**：`Ledger.ExposeData()`（Core 账本节点）+ `State.ExposeData()`（本模块 7 个节点）+ `sdsReclaim`（组件级回收账本，`Deep`）。储蓄时先 `SDS_ReclaimRegistry.ExportTo` 到中转列表再写；`PostLoadInit` 立即 `ImportFrom`，审计推到 `FinalizeInit`。不兜底重建账本、不重挂 `SDS_Core`。
- **FinalizeInit 顺序**：`State.CleanupLoadedMirrors()`（地图 spawn 后销毁旧镜像）→ `BeginMirrorJobTargetRemap()`（W60）→ `TryRemapMirrorJobTargetsImmediately()`（立即同步重建 + 重映射，失败保留到 Tick）→ `BeginTransportLoadRestore()`（W48）→ `TryRestoreTransportLoadRowsImmediately()`（立即尝试一次 W48 行恢复，供 W47 解析容器桶）→ `SDS_TransportVirtual.RebindLoadedDisplayKeys()`（给容器行内持久化展示物补封存）→ 小人背包 / 手持重复副本修剪 + 穿戴保真告警 → `sdsReclaim` 兜底再导入 → `SDS_ReclaimRegistry.AuditRestored()` → `SDS_Core.AuditReservations(ReclaimOwnerAlive)` → 置载入诊断 pending / 记录地图签名 → `SDS_OutputCommand.SyncForbiddenToPreserved()` → 输出一次载入诊断。
- **Tick 顺序**：见 §1.1；核心是「账本 → 状态 → 读档诊断 → 首档通知 → 预留审计 / 回收巡检（250）→ 地图签名 + 库存提醒（30）→ 结构对账（250，末尾校正端点优先级）」。
- **读档诊断 / 首档通知**：`TryLogLoadDiagnostics` 在地图就绪后跑一次 B 类报数（开发者模式再跑完整只读自检）；`TrySendIntroNotice` 每档一次 `PositiveEvent`，随 `introNoticeShown` 保存。
- **Holder 边界**：`SDS_GameComponent_Grid` 不实现 `IThingHolder`，保真真身由 Core 私有 owner 持有；`Map.GetChildHolders` 只遍历 `map.components`，因此移除 `IThingHolder` 对 Map 级枚举无影响（《ModuleInterfaces.zh-CN.md》4.1）。
- **旧档壳已退役（2026-11-16）**：`MapComponent_SDS_ProtectedOutputs` 与 `sdsProtectedOutputs` 只服务 2.4（2026-10-26 21:54）之前的档，已按 [CompatibilityRetirement.zh-CN.md](CompatibilityRetirement.zh-CN.md) 删除；旧档该节点不再读取，新档只写 `sdsReclaim`。
- **回收账本（C1/C8）**：`SDS_ReclaimRegistry` 与 `sdsReclaim` 节点都在本模块；`SDS_GameComponent_Grid` 是唯一读写点（节点与本账本同级，旧档同名节点走同一读路径）：`PostLoadInit` → `ImportFrom`；`FinalizeInit` 兜底再导入 → `AuditRestored`（Job 目标 / 手上料审计 + 受保护输出巡检）。`FinalizeInit` 与 250 tick 审计以 `SDS_Core.AuditReservations(ReclaimOwnerAlive)` 传判活委托，每 250 tick 调 `SDS_ReclaimRegistry.Sweep`（只巡检受保护输出）。

### 4.11 工作类型（WorkTypeDef / WorkGiver）为何不改（原 §十一）

- **原版机制**：`WorkTypeDef` 定义工作类别与优先级，`WorkGiverDef` 把 WorkGiver 挂到工作类型上，ThinkTree 的 `JobGiver` 负责给小人派活。
- **接在哪**：本程序集不新增 `WorkTypeDef` / `WorkGiverDef`；只在既有 `WorkGiver_Haul`、`JobGiver_Haul`、`HaulAIUtility` 与各 WorkGiver 的 `JobOnThing` / `HasJobOnThing` 上做 Postfix。新增的只是 `SDS_DepositToGrid` 等 JobDef 与对应 JobDriver（《ModuleResponsibilities.zh-CN.md》六；`PatchInventory.zh-CN.md` 三·六）。W63 折叠 / 退让版见 4.5 边界。
- **边界**：完全不经过 `WorkGiver.JobOnThing` / `HasJobOnThing` 的自定义派发不在覆盖内，需单独立项；自带 Job 工厂且不调用 `HaulAIUtility` 的第三方搬运器，函数级出口也覆盖不到。

### 4.12 已知边界（原 §十二）

- 局部 `ThingGrid` / `GenRadial` 半径搜索不折算访问点距离；`RegionwiseBFSWorker` 未单独补丁。
- **原子燃料**：`FindEnoughReservableThings` 没有访问点兜底；`FindFixedIngredientCount` 仍走区域 BFS，只能部分覆盖。
- **顺路合并**：`Toils_Haul` 的顺路合并可能选中镜像，从而把 Job 改道到端点取货，属预期路径而非漏派。
- **行进中的远行队**：`Map == null` 且没有实体端口，只能编成装车或远程交易，不能走端点存取。
- **DoBill 原料距离**：DoBill 的区域 BFS 与 `TryFindBestIngredientsInSet` 都用 `thing.Position`（金库格）算距离 / 半径，镜像可能因金库超出 `ingredientSearchRadius` 被排除或排后。
- **装载差额与编成不同式**：编成用 `CountToTransfer − 未被 holder 持有的实物量`；装载用 `CountToTransfer − 在途量`（在途 = 原版 `HaulToTransporter` / `HaulToPortal` 的 `initialCount`）。W25 明确把两者定义为不同口径、不得互换：装载不以地图实物量扣减，因为原版只在找不到可装实物时才让超维空间接管。
- **装载多身份不 fail-closed**：编成侧一行出现多个虚拟键 / 多个保真桶会 `Fail` 并整行归零；装载选行侧同一行有多个虚拟键时取第一个可用键、多个保真桶时任选一个能整趟取到的桶，不归零（编成与装载的这条差异已分别登记在 [Rules.zh-CN.md](Rules.zh-CN.md) §十二.6）。
- **W47 读档重建**：运行期计划不跨存档；驱动器首个 toil 若计划为空且账本仍有保真预留，按预留键从容器内唯一桶用 `TakePlan` 重建计划，登记为运行时计划后沿用原预留与物化路径；找不到唯一桶或不足额时 `SDS_Diag.Fail` + fail-closed，库存与预留均不动。`FinalizeInit` 会立即尝试一次 W48 行恢复，缩短等待窗口。
- **快照缺失时 fail-closed**：`JobOnTransporter` / `JobOnPortal` 只在同一 tick 的 `HasJob` 快照命中时改派；缺失时不调用 `FindThingToLoad` / `TrySelectLoadRow`，原样保留原版结果并一次性 `Warn`，由下一 tick 的 `HasJob` 重新登记。第三方绕过 `HasJob` 直接调 `JobOn` 时不会使用超维库存。

### 4.13 交易 / 发射 / 远程买卖

- **原版机制**：`TradeDeal.Reset` 构建 `Tradeable` 清单；`TryExecute` 统一成交；`Tradeable.ResolveTrade` 逐行交收；`Dialog_Trade.Close` 收尾。发射走 `TradeUtility.AllLaunchableThingsForTrade`（读）与 `LaunchThingsOfType`（写）；远程买卖走 `Settlement_TraderTracker` / `Caravan_TraderTracker` 的 `ColonyThingsWillingToBuy` 与 `GiveSoldThingToPlayer`。
- **挂点**：`TradeDeal.Reset`（Prefix 清理 + Postfix 注入）、`TradeDeal.TryExecute`（Prefix 校验）、`Tradeable.ResolveTrade`（Prefix 低优先级替换）、`Dialog_Trade.Close`（Postfix 清理）、`TradeUtility.AllLaunchableThingsForTrade`（Postfix 追加）、`TradeUtility.LaunchThingsOfType`（Prefix 扣缺口）、`Settlement_TraderTracker` / `Caravan_TraderTracker` 的 `ColonyThingsWillingToBuy`（Postfix 追加）与 `GiveSoldThingToPlayer`（Prefix 入账）（`Patch_Trade.cs`）。
- **清单注入**：虚拟腿遍历 `Snapshot(0)`，跳过 `key.preserved`，要求 `GetTradableCount > 0` 且 `PlayerSellableNow`，`stackCount = ClampTakeToAvailable(int.MaxValue, available)`，再按 `room = int.MaxValue − 已持有` 夹一次；保真腿按 `SDS_PreservedBuckets.Build(..., tradeGrouping:true, tradableBudget:true)` 每桶一个展示物，缩装单独成桶。只有交易面板的 `BuildTradePreservedDisplays` 会写展示物 / 桶登记表；信标的 `BuildPreservedDisplays` 只读构建。
- **成交校验（读侧，R15 / R16）**：只遍历 `ActionToDo == PlayerSells` 的行，按原版消费顺序逐件消费 `thingsColony`；预算来自 `GetTradableCount`（total − reserved，禁用不过滤），保真行额外用 `SDS_PreservedBuckets.TakePlan` 做可行性试算。任一行不足 → 弹「库存已变化」并取消整笔交易；校验不预留、不扣账。
- **交收（写侧）**：`ResolveTrade` 只在本行确实消费了展示物时才替换：按原版顺序把展示物换成等量真身——虚拟走 `SDS_Rules.TryWithdraw`（`SpawnedTarget` 事务），保真按登记桶 `TakePlan` 后 `TryWithdrawPreservedExact`。替换异常时把已取出的真身按通道回滚并跳过该行，绝不把展示物交给商人。
- **信标发射**：读侧仅在 `trader == null`、本图有通电通讯信标且本图暴露账本时追加虚拟与保真展示物（保真排除缩装）；写侧 `LaunchThingsOfType` 在地图实物不足时从账本扣 `debt − onMap`，扣到多少减多少。带 `TradeShip` 的调用不进入该扣账分支。
- **远程买入**：要求芯片 B、玩家阵营、跨图科技 `SDS_AdvancedPrecisionSuperStorage` 完成、`SDS_Rules.Available`。买入货 `SplitOff` → `PreTraded` → `SDS_Rules.TryDeposit`（`SDS_DirectDepositPort`）；成功则抑制原版交付，失败恢复拆分并把货交回原版。
- **收尾**：`Reset()` / `Dialog_Trade.Close` 只解除展示物与桶引用，不销毁实物；展示物实例按键 / 按桶全局缓存，跨窗口共享 `stackCount`。

### 4.14 账单 / 医疗 / 基因 / 培育舱 / 棺材 / 安装

- **原版机制**：`WorkGiver_DoBill` 先 `IsUsableIngredient` 过滤候选，再 `TryFindBestBillIngredients` 选料（失败后可走 `TryFindBestFixedIngredients`），最后 `TryStartNewDoBillJob` 生成 Job；基因、异种胚、培育舱、棺材、安装各走自己的 UI / 容器入口。
- **医疗门（S17）**：`Patch_SDS_MedicalIngredientGate` 只拦「药品 + `Bill_Medical` + 患者是 Pawn」，判定唯一在 `SDS_MedicalGate.Allows`（底层 `SDS_MedicalCareAdapter` 调原版 `GetMedicalCareCategory(...).AllowsMedicine`）；非药品 / 非 Pawn billGiver 恒放行。虚拟腿、保真腿、W51 与 `FindBestMedicine` 全部经同一门。
- **DoBill 选料顺序**：原版成功 → 不改；原版失败 → ① 虚拟腿 `SDS_VirtualBillFallback.TrySupplyVirtualIngredients`（候选只取本图镜像，要求相对 `RootCell` 在 `ingredientSearchRadius` 内、有可达端点、`SDS_MedicalGate` / `IsFixedOrAllowedIngredient` 通过，反射原版 `TryFindBestBillIngredientsInSet`）；② 保真腿 `TrySupplyPreservedIngredients`（候选来自账本真身，**不套搜索半径**、只要求有可达端点，多件计划还受总重量 ≤ 携带力约束）；③ W51 `TryMaterializePreservedGaps`（仅 `Building` 工作台；逐槽物化到交互格；同 tick 由 `rerunning` 守卫重跑原版搜索，失败则回滚本批）。
- **固定配料**：`TryFindBestFixedIngredients` 只有虚拟腿，复用原版 `_NoMixHelper` 探针与同一 `RootCell` / 半径判据。
- **纯保真 Job**：`TryStartNewDoBillJob` Postfix 只在「结果仍是 `DoBill` 且 `targetQueueB` 全部是未 spawn 的保真真身」时改成 `WithdrawPreservedForBill`（A = 端点，B = 工作台）；没有可达端点返回 null，不留坏 Job。
- **保真取货驱动器**：先预留取货点、工作台与交互格（失败 → `nextTickToSearchForIngredients = now + 60` 退避），再按队列预约保真 plan，走到端点后 `TryWithdrawPreservedExact` + `MarkProduced`，第一件进手、其余进背包，最后交回原版 `DoBill`（清空退避）。任何失败都把已取出的真身按保真通道回充，绝不吞物品。
- **基因包**：`Dialog_CreateXenogerm` 构造时登记当前装配器；`Building_GeneAssembler.GetGenepacks` Postfix 每次开窗只注入一次，把保真 `Genepack` 摘出账本并 `TryAdd` 进有空位的 `CompGenepackContainer`；容器拒收即 `ReturnThing(Preserved, Ground)`。
- **异种胚**：`Recipe_ImplantXenogerm.AvailableOnNow` 在库存有保真 Xenogerm 且可达端点时置可选；`Dialog_SelectXenogerm` 构造时把账本真身追加进私有列表；`Xenogerm.SetTargetPawn` Prefix 在玩家确认后物化真身，失败提示并阻断原版。
- **培育舱**：`Building_GrowthVat.GetGizmos` 追加「从超维空间植入胚胎」（无电置灰），`SelectEmbryo` Prefix 把选中的保真胚胎物化到交互格后交原版。
- **棺材 / 坟墓**：`Building_Casket.TryAcceptThing` Prefix 先摘出账本，Finalizer 在原版拒收或异常时按保真通道回充。
- **安装蓝图**：`GenConstruct.PlaceBlueprintForInstall` Prefix 把保真缩装物物化、取消同 innerThing 旧蓝图，再以真身继续；安装面板路径由 `SDS_InstallBlueprintGuard` 统一取消 / 回滚（W32）。
- **在途标记的分工**：凡物化后要在图上等待未来原版派活的路径（异种胚、培育舱胚胎、安装缩装、保真装载物化）都打 `MarkProduced`；W51 的物化在同一 tick 就被原版 Job 接手，虚拟装载物化也直接交原版装舱，因此这两条明确**不打**标记，两类路径不得互换。

### 4.15 保真消费桥（药品 / 食物 / 武器 / 衣物）

- **原版机制**：`HealthAIUtility.FindBestMedicine`、`FoodUtility.TryFindBestFoodSourceFor`、`JobGiver_PickUpOpportunisticWeapon.TryGiveJob`、`JobGiver_OptimizeApparel.TryGiveJob` 在「地图上没有可用物」时返回失败 / null；`StoreUtility.IsInAnyStorage` 决定实物是否被视为已入储。
- **挂点**：上述四个决策入口各一个 Postfix（低优先级），加 `StoreUtility.IsInAnyStorage` Postfix（`SDS_PreservedOffer.cs`）。全部只在原版返回「没有」时接管，原版有结果时不改。
- **物化与生命周期**：保真腿 `SDS_PreservedOffer.TryOffer` 逐件筛账本真身（可解析、非禁用、可用量 > 0、同 key 无预留），按评分取 1 件 → `TryWithdrawPreservedExact` → `SDS_OfferBridge.TryPlace` 落到最近可达访问点；独立落地件打 `MarkProduced`、解禁、冻结 `CompRottable` 并登记 offer。落点并入已有同类堆时只记冷却，不跟踪、不打标。
- **超时与回库**：offer 默认存活 6000 tick；药品 / 武器 / 衣物传 6000，食物传 2500；`TickIfDue` 每 60 tick 扫一次。到期、失图或已销毁时恢复腐烂并按来源通道 `RollbackOrWarn` 回库（失败只 `Warn`）；被拿起 / 穿 / 装 / 进容器视为已领取。同 Pawn + 同 def 冷却 600 tick；offer 表上限 512，超出时该次物化不登记、不冻结、不超时回收。
- **虚拟腿**：只有武器桥在原版无武器时先试保真、再 `SDS_OfferBridge.TryOfferVirtual` 试虚拟（同样 6000 tick）；其余四类只走保真腿。offer 表、落点、冷却、腐烂冻结只有一份，`SDS_PreservedOffer` 不得自建第二份。

## 五、第三方支持

> 定位：本模块为第三方提供的三个专用宿主实现、各自转发到哪、以及 Vanilla 与 `SDS.ThirdModCompat` 的隔离边界。规则见《ModuleRules.zh-CN.md》6.4 / 6.6 与 E6 / E7；宿主槽位角色见 §2.2。

### 5.1 三个专用宿主实现

| 宿主接口 | Vanilla 实现 | 转发到 | 第三方为什么需要 |
|---|---|---|---|
| `ISDS_DisplayQuery` | `SDS_DisplayAdapter` | `SDS_OutputUtility.TryGetDisplayKey` | 展示物身份在工厂创建时封存（读档容器行补封存）；第三方不能用展示物反推通道 |
| `ISDS_DepositJobs` | `SDS_DepositJobsAdapter` | `SDS_DepositService` / `SDS_ChipManualDeposit` | Job 型搬运 Mod 只应转发唯一存入服务，不另建目的地决议 |
| `ISDS_TransportLoadSink` | `SDS_TransportLoadAdapter` | `SDS_TransportVirtual.InjectIntoTransporters` | 自建装载界面的 Mod 只能在清单构建处注入展示物行（W50） |

- **`SDS_DisplayAdapter`（展示物身份封存）**：`IsDisplay(Thing)` 与 `TryGetDisplayKey(Thing, out SDS_StockKey)` 都转到 `SDS_OutputUtility.TryGetDisplayKey`，后者查展示物工厂写入的 `displayKeys` 表（`SDS_CompatHosts.cs`、`SDS_OutputUtility.cs`）。展示物是投影，不复制颜色 / 风格 / 任务标签 / 燃料 / 充能等实例状态；用 `SDS_StockKey.FromAnyThing(display)` 反推通道会把保真物判成虚拟，表现为「清单看得到、装载 / 编成 / 交易全说没有」。身份只在 `MakeDisplayThing` / `MakePreservedDisplayThingFrom` 创建时封存；读档后容器行内未封存的持久化展示物由 `SDS_TransportVirtual.RebindLoadedDisplayKeys` 补封存（唯一保真桶优先，其次虚拟键）。唯一判据是 `SDS_OutputUtility.IsDisplay`，唯一解析入口是 `KeyOfDisplay(display, preservedBucket)`。第三方只能问「这是不是展示物、它的键是什么」，不能自行构造展示物，也不能用 `RequiresPreservedStorage(display)` 判断通道。
- **`SDS_DepositJobsAdapter`（Job 型搬运只转发唯一存入服务）**：`CanTakeCandidate` → `SDS_DepositService.CanTakeCandidate`；`TryMakeJob` → `SDS_DepositService.TryMake`；`TryMakeFromInventory` → `SDS_DepositService.TryMakeFromInventory`；`IsManualDepositAvailable` → `SDS_ChipManualDeposit.PawnCanDeposit`（`SDS_CompatHosts.cs`）。Pick Up And Haul 这类 Mod 有自己的 WorkGiver / Job 工厂、不经过 `HaulAIUtility`，桥只让它调用同一套 `SDS_DepositService`，候选闸门、目的地决议与背包卸载派活都留在唯一实现里。`CanTakeCandidate` 只回答「端点是否赢过原版最佳目的地」，`TryMakeJob` / `TryMakeFromInventory` 只产出 Job；第三方不得在适配器内再改 `targetB`、再算数量或复制目的地排序（《PublicAPI.zh-CN.md》4.9、4.10）。
- **`SDS_TransportLoadAdapter`（自建装载清单注入，W50）**：先检查 `host`、`host.Map`、`CompTransporter` 与 `TransportersInGroup`，再调用 `SDS_TransportVirtual.InjectIntoTransporters(transferables, transporters, host.Map)`；运输舱组为空时退回首台自身继续注入，不返回 false（`SDS_CompatHosts.cs`）。不经过原版 `Dialog_LoadTransporters` 的第三方装载界面（当前登记的是天工模块化穿梭机 CeleTech Shuttle Extension）没有原版注入点，只能在它自己的「清单构建」返回值处注入展示物行；注入必须调用唯一实现，不能由适配器取货、预留、扣账或另算数量。该桥只注入 `TransferableOneWay` 行，不决定选行、不取货、不扣账；派活 / 入舱 / 读档仍共用 W38–W48 的统一路径。展示物 `stackCount` 是全库存聚合量而非原版 `stackLimit` 物理堆，第三方若在装载前按整堆质量做准入，预检会比原版更严（《RuleIndex.zh-CN.md》W50）。

### 5.2 为什么桥定义在 Contracts、实现在 Vanilla

- 展示物工厂与 `displayKeys` 身份表在 Vanilla（`SDS_OutputUtility`）；Core 不再保留展示物登记注册表，且禁止 UI / `Find` / 原版行为入口，无法承担工厂与解析。
- 存入派活与装载注入都是原版执行面：Contracts 只声明 `ISDS_DepositJobs` / `ISDS_TransportLoadSink` 的接口形状，真正创建 `Job`、写 `TransferableOneWay`、读 `CompTransporter` 的实现只能在 Vanilla。
- ThirdModCompat 禁止引用 Vanilla、Api 只做门面转发不实现，因此「需要 Vanilla 内部能力、又要给兼容层用」的桥只能定义在 Contracts、实现在 Vanilla、由 Bootstrap 注入，再经 `SDS_API` 的内部适配器桥暴露。
- 这三个槽位不进入 Core 的事实轴：`Display` / `DepositJobs` / `TransportLoad` 只服务 Api 适配器桥，Core 不消费。

### 5.3 隔离与唯一写路径

- Vanilla 与 `SDS.ThirdModCompat` 互不引用，依赖矩阵中两者互不相连；Vanilla 只有一个 Harmony ID `sizuru.sds.vanilla`，9 个适配器、目标 packageId 与各自 Harmony ID 全部在 ThirdModCompat。审计：`CheckAssemblyBoundaries.ps1`（引用图 + 类型级 public 白名单）、`CheckCompatSurface.ps1`（只允许 Api / Contracts / Diag）。
- Vanilla 的可编译可见面很窄：`InternalsVisibleTo` 只登记 `SDS.Core → SDS.Rules` 与 `SDS.Vanilla → SDS.Tests`，实现类绝大多数 internal；public 类型面向 Bootstrap 宿主装配、Defs XML 与存档，不是对外契约。
- 写路径只有一条（E7）：同一第三方入口要么调用 `SDS_API`、要么由适配器翻译，不得两条同时生效；只读投影可与适配器翻译共存。绕过 `SDS_API` 会绕过 Api 的调用方门（玩家阵营、所有权、禁止、同地图），可能把非玩家阵营或跨地图的物品写进账本；若同时用 Harmony / 反射直调 Vanilla 服务又保留自己的派活，会出现两次扣账、两次预留或两条回滚顺序；且这些调用不在 `SDS_API.Version` 的兼容承诺内，Vanilla 实现可随 Core 契约调整而重构（《PublicAPI.zh-CN.md》3.4、3.8、4.3、4.11）。

### 5.4 无法接入或只能部分接入的第三方类型

- **自带存储组 / 自建 SlotGroup 的 Mod**：不能作为 SDS 端点接入——这类存储组通常以 `ISlotGroupParent` 暴露，而 `ISDS_StorageEndpoint` 明确禁止实现它（否则原版 haul 的容器分支永不成立，`SDS_Buildings.cs` 接口注释），两者在原版契约上互斥。也没有第三方存储组注册入口：Version 2 已删除 `SDS_API.RegisterWarehouseContainer`（无内部调用、无第三方采用记录）。仍可共存：第三方存储组按原版 `IHaulDestination` / `SlotGroup` 参与 `TryFindBestBetterStorageFor`，与端点并列竞争，但 SDS 虚拟库存不会进入它的组；该 Mod 若只遍历 `AllGroupsListInPriorityOrder` 或使用硬编码容器表，则看不到端点。契约层结论，当前无实际接入实例登记。
- **自带 Job 工厂、不经过 HaulAIUtility 的搬运 Mod**：函数级出口只覆盖 `HaulToStorageJob` / `HaulToCellStorageJob` / `HaulToContainerJob`；自带 Job 工厂且不调用这些函数的不在覆盖内，需该 Mod 自己的 `SDS_ModCompat_*` 适配器走 `ISDS_DepositJobs` 桥，Vanilla 不为单个 Mod 增加目的地特例。当前仅登记 Pick Up And Haul 的 Job 型接入。
- **自带装载界面 / 自定义清单的 Mod**：只有在其「清单构建」返回值处存在注入点，才能用 `ISDS_TransportLoadSink`（W50）；若内部直接构造装载 Job 且不返回 `TransferableOneWay` 清单，则没有注入点。可接入时也只注入展示物行，不接管装载 Job、不扣账、不预留；预检偏严属已知边界。
- **自带展示物 / 身份体系的 Mod**：展示物身份只在 `SDS_OutputUtility` 工厂创建时封存（读档时容器行内持久化展示物由 `SDS_TransportVirtual.RebindLoadedDisplayKeys` 补封存），第三方自行构造的展示物只能走 `FromThing` 兜底、无法保证通道正确；正确做法是查询 `ISDS_DisplayQuery`，或在它自己的清单里使用 Vanilla 工厂产生的展示物。
- **目标 Mod 未安装 / 未加载**：适配器由 Bootstrap 第 4 步扫描 `ISDSCompatAdapter`，只有目标已加载才 `Apply`；未安装静默、不产生挂点，未加载时不会应用任何第三方补丁。

## 六、补充接触面（原版机制细节）

- **保真消费桥时序**：offer 存活上限默认 6000 tick（药品 / 武器 / 衣物），食物为 2500 tick；`TickIfDue` 每 60 tick 扫描；同 Pawn + 同 def 冷却 600 tick。不存在独立的「回充等待」计时器：到期 / 失图 / 已销毁时直接按来源回库，回库失败只 `Warn`。
- **桥的边界**：落点被合并后不再跟踪；同类已有预留时整类跳过，不重复物化。
- **物化后处理**：物化实例立即打标记、`SetForbidden(false)`、冻结 `CompRottable`，避免落地即腐烂或被禁。
- **使用命令时序**：同一次 UI 回调内完成物化 → 判定 → 回充，帧末无残留，不新增存档字段。
