# 接口

> 定位：A 节是本模块对外签名，逐条照抄 `超维存储/Source/SDS.Contracts/` 现有源码；B 节是反向清单，记录其余 6 个程序集实际消费了本模块哪些类型（2026-11-12 按源码全文检索核实）。其后「原版支持」「第三方支持」两节分别记本模块与原版类型的接触面、第三方可用的公开接入口；签名以 A 节为准，规则见 `Rules.zh-CN.md`。

## A. 本模块对外签名

### A.1 枚举

```csharp
public enum SDS_StockChannel { Virtual, Preserved }
public enum SDS_WithdrawMode { Hand, Container, SpawnedTarget, VirtualHandoff }
public enum SDS_SupplyFailure { None, UnresolvedDef, Forbidden, Insufficient, RebuildFailed }
public enum SDS_WithdrawEndState { None, Reserved, Materialized, Delivered, RolledBack }
public enum SDS_ReclaimOutcome { None, Returned, Consumed, Abandoned }
public enum SDS_EndpointOp { Deposit, Withdraw }
public enum SDS_StructureKind { Core, Anchor, CapacityArray }
public enum SDS_ReclaimPolicy { JobTarget, ProtectedOutput }
public enum SDS_ReturnFallback { None, Ground, PawnInventory, PawnCarry }
public enum SDS_ApiResultCode
{ Ok, InvalidArgument, GridUnavailable, NoEndpoint, EndpointUnavailable, NotDepositable, Forbidden, NotOwnedByCaller, NotPlayerFaction, NoStock, UnresolvedDef, RebuildFailed, NotWithdrawable, DepositFailed, TransactionFailed, InternalError }
```

### A.2 品类与账本 DTO

```csharp
public class SDS_StockKey : IExposable, IEquatable<SDS_StockKey>
{
    public ThingDef thingDef; public ThingDef stuff; public int qualityInt = -1;
    public string defName; public string stuffName; public bool preserved;
    public SDS_StockKey(); public SDS_StockKey(ThingDef thingDef, ThingDef stuff, int qualityInt);
    public SDS_StockKey(ThingDef thingDef, ThingDef stuff, int qualityInt, bool preserved);
    public string EffectiveDefName { get; } public string EffectiveStuffName { get; } public bool Resolved { get; } public bool HasIdentity { get; }
    public void EnsureNames(); public bool TryResolve(); public bool SameAs(SDS_StockKey other);
    public bool Equals(SDS_StockKey other); public override int GetHashCode();
    public static int CompareIdentity(SDS_StockKey a, SDS_StockKey b); public void ExposeData();
}

public class SDS_StockEntry : IExposable
{
    public SDS_StockKey key; public long count;
    public float avgHitPoints = -1f; public float avgRotProgress = -1f;
    public SDS_StockEntry(); public SDS_StockEntry(SDS_StockKey key, long count); public void ExposeData();
}

public sealed class SDS_StockVitals { public float hitPoints = -1f; public float rotProgress = -1f; }
public class SDS_Reservation : IExposable
{ public int jobLoadID; public SDS_StockKey key; public int count; public Pawn pawn; public void ExposeData(); }
```

### A.3 事务、回收与版本 DTO

```csharp
public sealed class SDS_ReclaimSave : IExposable
{
    public SDS_ReclaimPolicy policy; public int jobLoadID; public int ownerLoadID; public int tick; public bool hand;
    public List<Thing> things = new List<Thing>(); public List<int> counts = new List<int>(); public void ExposeData();
}

public class SDS_WithdrawDeliveryPolicyDef : Def
{ public string jobDefName; public string driverType; public SDS_WithdrawMode mode = SDS_WithdrawMode.Hand; }

public sealed class SDS_WithdrawQuery
{ public SDS_StockKey key; public int quantity; public bool preserved; }

public sealed class SDS_WithdrawPlan
{ public SDS_WithdrawQuery query; public ISDS_Port endpoint; public SDS_WithdrawMode mode; public ISDS_MirrorPhysical physical; }

public struct SDS_ReadVersions
{ public int tick; public int mirror; public int endpoint; public int stock; public int reservation; public int filter; }
```

### A.4 端点与结构 DTO

```csharp
public struct SDS_StructureFact
{ public Thing Thing; public Map Map; public SDS_StructureKind Kind; public bool Operational; }
// 品类容量口径由 Core 现读 SDS_Config 计算，事实只回答「结构是否存在 / 是否可用」。

public struct SDS_EndpointQuery
{
    public SDS_EndpointOp op; public Map map; public Pawn actor; public IntVec3 reference;
    public Thing thing; public SDS_StockKey key; public Danger danger;
    public bool requireReach; public bool allowSelf; public bool requireStock;
    public bool allowProducedMarked;
    public int priority;
    public void ApplyHostPriority();
    public static SDS_EndpointQuery Deposit(Thing thing, Pawn actor, bool allowProducedMarked = false);
    public static SDS_EndpointQuery DepositForNonPawn(Thing thing, Map map, IntVec3 reference);
    public static SDS_EndpointQuery WithdrawPoint(Pawn actor);
}

public struct SDS_EndpointDecision
{ public bool allowed; public ISDS_Port port; public int priority; public float distanceSquared; public void Reset(); }
```

### A.5 API 边界 DTO

```csharp
public struct SDS_ApiCaller
{
    public Faction faction; public Pawn pawn; public Thing claimOwner; public bool IsNonPawn { get; }
    public static SDS_ApiCaller FromPawn(Pawn pawn);
    public static SDS_ApiCaller FromFaction(Faction faction, Thing claimOwner = null);
}

public struct SDS_ApiStockInfo
{
    public SDS_StockKey key; public long total; public long available;
    public bool forbidden; public bool resolved; public SDS_SupplyFailure failure;
    public SDS_ReadVersions versions; public SDS_ApiResultCode result;
}
```

### A.6 端口与宿主接口

```csharp
public interface ISDS_Port
{
    Thing Thing { get; } Pawn Pawn { get; } Map Map { get; } bool IsSelf { get; } bool Operational { get; }
    IntVec3 AccessCell { get; } PathEndMode AccessMode { get; }
    bool CanReach(Pawn actor); float RankDistanceSquared(IntVec3 reference);
}

public interface ISDS_PortProvider
{
    void AppendCandidates(in SDS_EndpointQuery query, List<ISDS_Port> into);
    bool TryGetPort(Thing thing, out ISDS_Port port); bool TryGetSelfPort(Pawn pawn, out ISDS_Port port);
    bool IsLocalUsable(Map map);
}

public interface ISDS_DepositFilter { bool Allows(Thing thing); }
public interface ISDS_PrioritySource { int Priority { get; } bool IgnorePriority { get; } }
public interface ISDS_DeliverySink
{ bool TryPlace(Thing thing, Map map, IntVec3 cell); bool TryGiveToPawn(Thing thing, Pawn pawn, bool preferCarry); }
public interface ISDS_MirrorQuery
{ IReadOnlyList<Thing> VisibleThingsOfDef(Map map, ThingDef def); bool IsMirror(Thing thing); int Version { get; } }
public interface ISDS_DisplayQuery { bool IsDisplay(Thing thing); bool TryGetDisplayKey(Thing thing, out SDS_StockKey key); }

public interface ISDS_DepositJobs
{
    bool CanTakeCandidate(Pawn pawn, Thing thing, bool forced); Job TryMakeJob(Pawn pawn, Thing thing, bool forced, Job vanillaJob, bool multi);
    Job TryMakeFromInventory(Pawn pawn, IList<Thing> items); bool IsManualDepositAvailable(Pawn pawn);
}

public interface ISDS_TransportLoadSink { bool InjectTransportLoadCandidates(ThingWithComps host, List<TransferableOneWay> transferables); }
public interface ISDS_MirrorPhysical
{ bool TryTakePhysical(SDS_StockKey key, Thing mirror, int count, out Thing made); bool TryTakeWholePhysical(SDS_StockKey key, Thing mirror, out Thing made); }

public interface ISDS_MedicalCarePolicy { bool Allows(ThingDef medicine, Thing billGiver); }

/// <summary>医药门唯一取值口径（Vanilla / Api 调用；Core 不调用）。</summary>
public static class SDS_MedicalGate { public static bool Allows(Thing billGiver, ThingDef def); }
public interface ISDS_ReclaimWorld
{ int CurrentTick { get; } bool JobExists(int jobLoadID); bool LordExists(int lordLoadID); void RemoveFromCaravan(int lordLoadID, Thing thing); }
public interface ISDS_DiagnosticProvider { string Name { get; } void AppendReport(List<string> lines); }
public interface ISDSCompatAdapter { string Name { get; } string TargetPackageId { get; } bool IsTargetLoaded { get; } void Apply(); }
```

> **已删除的扩展位（2026-11-14，L2）**：`ISDS_DeliverySink.TryDeliverToContainer`、`ISDS_MirrorQuery.TryGetMirrorFor`、`ISDS_DepositFilter.Version`、`ISDS_PrioritySource.Version`、`ISDS_PortProvider.HasUsableBEndpointOn` 无生产消费方，已按《DesignPrinciples.zh-CN.md》§三 L2 删除；`SDS_EndpointQuery.danger` 是旧框架遗留字段，工厂会写入但判定不读取，随旧契约保留；`SDS_SupplyFailure.RebuildFailed` 为预留值（`GetFailure` 当前不产出），保留映射。

### A.7 SDS_Host 与纯注册表

```csharp
public static class SDS_Host
{
    public static ISDS_PortProvider Ports; public static ISDS_DepositFilter DepositFilter;
    public static ISDS_PrioritySource Priority; public static ISDS_DeliverySink Delivery;
    public static ISDS_MirrorQuery MirrorQuery; public static ISDS_MirrorPhysical MirrorPhysical;
    public static ISDS_MedicalCarePolicy MedicalCare; public static ISDS_ReclaimWorld ReclaimWorld;
    public static ISDS_DisplayQuery Display; public static ISDS_DepositJobs DepositJobs;
    public static ISDS_TransportLoadSink TransportLoad;
    public static void Install(SDS_HostSlots slots);
}

public static class SDS_PreserveHooks
{
    public static int Count { get; } public static bool Register(Func<Thing, bool> probe);
    public static bool RequiresPreserved(Thing thing); public static void Reset();
}

public static class SDS_CompatGates
{
    public static bool RegisterTransportLoadInjection(Func<Dialog_LoadTransporters, bool> gate);
    public static bool AllowsTransportLoadInjection(Dialog_LoadTransporters dialog);
    public static void Reset();
}
```

### A.8 DefOf、设置与存档 DTO

```csharp
public static class SDS_JobDefOf
{
    public const string WithdrawForTransportLoadName = "SDS_WithdrawForTransportLoad";
    public const string DepositToGridName = "SDS_DepositToGrid"; public const string WithdrawPreservedForBillName = "SDS_WithdrawPreservedForBill";
    public static JobDef WithdrawForTransportLoad { get; } public static JobDef DepositToGrid { get; } public static JobDef WithdrawPreservedForBill { get; }
}

public static class SDS_HediffDefOf
{ public const string ChipAName = "SDS_ImplantHediffA"; public const string ChipBName = "SDS_ImplantHediffB"; public static HediffDef ChipA { get; } public static HediffDef ChipB { get; } }

public class SDS_Settings : ModSettings
{
    public int gridCapacity, capacityPerArray; public float corePower, arrayPower, portPower;
    public bool excludeGridFromWealth, playerFactionOnlyAccess, allowPriorityOutflow, highestStoragePriority, forceWeaponApparelVirtual;
    public int endpointStoragePriority; public bool separateByQuality, separateByStuff;
    public override void ExposeData(); public void ResetToDefaults(); public void ClampAll();
}

public static class SDS_Config
{
    public static SDS_Settings Current;
    public static int GridCapacity { get; } public static int CapacityPerArray { get; } public static bool SeparateByQuality { get; } public static bool SeparateByStuff { get; }
    public static bool IgnorePriorityMode { get; } public static bool ForceWeaponApparelVirtual { get; } public static bool ExcludeGridFromWealth { get; } public static bool PlayerFactionOnlyAccess { get; } public static bool AllowPriorityOutflow { get; } public static StoragePriority CoreStoragePriority { get; }
    public static int ClampGridCapacity(int value); public static int ClampCapacityPerArray(int value);
    public static int Clamp(int value, int min, int max); public static float Clamp(float value, float min, float max);
}

public sealed class SDS_TransportLoadSave : IExposable
{ public int containerThingID, rowIndex = -1; public string defName, stuffName; public int qualityInt = -1; public bool preserved; public int units; public void ExposeData(); }

public class SDS_SidebarUIState : IExposable
{
    public List<string> collapsed = new List<string>(); public Vector2 scroll = Vector2.zero; public bool hasSelection; public SDS_StockKey selectedKey; public bool selectedPreserved;
    public void ExposeData(); public void Normalize();
}
```

`SDS_Settings` 的字段默认值与 `SDS_Config` 常量（容量 30 / 10–90，稳固装置 30 / 10–90，耗电 1000 / 0–2000、1000 / 0–2000、10 / 0–100，三个 Def 名）见源码 `SDS_Mod.cs`；本文件只列签名。

## B. 其余 6 个程序集实际消费的类型

| 消费方 | 实际引用的 Contracts 类型（源码全文检索） |
|---|---|
| `SDS.Diag` | `SDS_StockKey`、`SDS_WithdrawEndState`、`ISDS_DiagnosticProvider` |
| `SDS.Core` | 品类与账本：`SDS_StockKey`、`SDS_StockEntry`、`SDS_StockVitals`、`SDS_Reservation`、`SDS_StockChannel`；事务与回收：`SDS_WithdrawMode`、`SDS_WithdrawQuery`、`SDS_WithdrawPlan`、`SDS_WithdrawEndState`、`SDS_SupplyFailure`、`SDS_ReturnFallback`；端点与结构：`SDS_EndpointOp`、`SDS_EndpointQuery`、`SDS_EndpointDecision`、`SDS_StructureKind`、`SDS_StructureFact`；设置与 DefOf：`SDS_Config`、`SDS_HediffDefOf`；注册表：`SDS_PreserveHooks`；宿主：`SDS_Host`、`ISDS_Port`、`ISDS_PortProvider`、`ISDS_DepositFilter`、`ISDS_DeliverySink`、`ISDS_MirrorPhysical`、`ISDS_DiagnosticProvider` |
| `SDS.Api` | `SDS_StockKey`、`SDS_StockChannel`、`SDS_WithdrawMode`、`SDS_SupplyFailure`、`SDS_WithdrawQuery`、`SDS_WithdrawPlan`、`SDS_ReadVersions`、`SDS_EndpointOp`、`SDS_EndpointQuery`、`SDS_EndpointDecision`、`SDS_ApiCaller`、`SDS_ApiResultCode`、`SDS_ApiStockInfo`、`SDS_JobDefOf`、`SDS_ReturnFallback`、`SDS_Host`、`ISDS_Port`、`ISDS_PortProvider`、`ISDS_MirrorQuery`、`ISDS_DisplayQuery`、`ISDS_DepositJobs`、`ISDS_TransportLoadSink` |
| `SDS.Vanilla` | 品类与账本：`SDS_StockKey`、`SDS_StockEntry`、`SDS_StockChannel`；事务与回收：`SDS_WithdrawMode`、`SDS_SupplyFailure`、`SDS_WithdrawDeliveryPolicyDef`、`SDS_WithdrawQuery`、`SDS_WithdrawPlan`、`SDS_ReadVersions`、`SDS_ReturnFallback`、`SDS_ReclaimSave`、`SDS_ReclaimPolicy`、`SDS_ReclaimOutcome`；端点与结构：`SDS_EndpointOp`、`SDS_EndpointQuery`、`SDS_EndpointDecision`、`SDS_StructureKind`、`SDS_StructureFact`；设置与 DefOf：`SDS_Config`、`SDS_JobDefOf`、`SDS_HediffDefOf`；注册表与存档 DTO：`SDS_CompatGates`、`SDS_TransportLoadSave`、`SDS_SidebarUIState`；宿主：`SDS_Host` 与除 `ISDSCompatAdapter` 外的 13 个宿主接口（含 `ISDS_Port`、`ISDS_DiagnosticProvider`） |
| `SDS.ThirdModCompat` | `SDS_StockKey`、`SDS_WithdrawMode`、`SDS_ApiCaller`、`SDS_CompatGates`、`ISDSCompatAdapter` |
| `SDS.Bootstrap` | `SDS_Settings`、`SDS_Config`、`SDS_Host`、`ISDSCompatAdapter` |

未出现在表中的类型不被对应程序集直接引用；例如 `SDS_StockVitals` / `SDS_Reservation` 只由 Core 使用，`SDS_ReclaimSave` / `SDS_ReclaimPolicy` / `SDS_ReclaimOutcome` 只由 Vanilla 使用（C1/C8 后 Core 不再认识任何回收 DTO），`SDS_ApiStockInfo` 只由 Api 使用，`SDS_TransportLoadSave` / `SDS_SidebarUIState` 只由 Vanilla 使用。

## 调用关系

- Bootstrap 的 `SDS_HostInstaller` 调用 `SDS_Host.Install(SDS_HostSlots)`，把 11 个 Vanilla 实现注入静态槽位，是唯一允许的注入方。
- Bootstrap 的 `SDS_Mod` 在启动时把 `SDS_Settings` 实例写入 `SDS_Config.Current`，Core / Vanilla 之后只读。
- Core 只通过 `SDS_Host` 的 `Ports` / `DepositFilter` / `Delivery` 三个槽位取外部事实；优先级 / 镜像物理 / 医疗门改由调用方以查询字段 / 计划字段 / 委托注入，`ReclaimWorld` 归 Vanilla 内部使用。
- Vanilla 实现 `SDS_Host` 的 11 个槽位、`ISDS_Port` 与 `ISDS_DiagnosticProvider`，同时读取 `SDS_Config`、`SDS_JobDefOf`、`SDS_HediffDefOf` 与存档 DTO。
- Api 用 `SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` 作为边界形状，向 ThirdModCompat 暴露 `SDS_API`，并消费端点 / 事务 DTO。
- ThirdModCompat 只实现 `ISDSCompatAdapter` 并调用 `SDS_API`，把 `SDS_StockKey` / `SDS_WithdrawMode` / `SDS_ApiCaller` 当作翻译类型。
- Diag 只在断言签名里使用 `SDS_StockKey` / `SDS_WithdrawEndState`，并收 `ISDS_DiagnosticProvider` 报告。
- Tests 引用全部模块，用假实现填充 `SDS_Host` 的 11 个槽位后直接驱动 Core，不启动游戏。

## 原版支持

> 定位：本模块与原版 RimWorld / Verse 的接触面——不打补丁、不调行为，但契约字段直接引用原版类型与序列化语义。允许 / 禁止的判定以《ModuleRules.zh-CN.md》6.1 为唯一权威，本节只记现状与边界。

### 一、本程序集与原版的关系

- 补丁数为 0：112 个特性补丁类全在 Vanilla，9 个第三方适配器的动态补丁在 ThirdModCompat（《ModuleRules.zh-CN.md》七、八）。
- 可引用的原版面只有 RimWorld / Verse / UnityEngine 数据类型，不含任何行为入口。
- 不引用 `SDS_Diag`；缺失与异常只走 `Log.ErrorOnce`（`SDS_CaravanTransport.cs`、`SDS_Implants.cs` 现实现）。
- 本节只解释类型语义，不定义任何玩法规则。

### 二、契约里出现的原版类型与用途

| 原版类型 | 在 Contracts 的位置 | 依赖的原版语义 | 契约边界 |
|---|---|---|---|
| `ModSettings` | `SDS_Settings` | 原版设置容器的 `ExposeData` + `Scribe_Values` | 只存数据；设置 UI 归 Bootstrap，Bootstrap 只写数据（《ModuleInterfaces.zh-CN.md》1.3） |
| `Def` | `SDS_WithdrawDeliveryPolicyDef` | XML 定义、`DefDatabase` 枚举 | 类型在 Contracts，解析器在 Vanilla（`SDS.Vanilla/SDS_JobTargets.cs`），契约不读 Def 库 |
| `DefDatabase<T>` | `SDS_JobDefOf`、`SDS_HediffDefOf`、`SDS_StockKey.TryResolve` | `GetNamedSilentFail` 返回 null、不抛异常；`AllDefsListForReading` 由使用方调用 | 每次访问现查，不做静态缓存（《ModuleInterfaces.zh-CN.md》1.3） |
| `BackCompatibility` | `SDS_StockKey.TryResolve` | 原版 def 改名兼容链 `BackCompatibleDefName` | 兼容链抛异常时回退按原名解析，不影响条目保留 |
| `IExposable` / `Scribe` | 全部存档 DTO（`SDS_StockKey` / `SDS_StockEntry` / `SDS_Reservation` / `SDS_ReclaimSave` / `SDS_TransportLoadSave` / `SDS_SidebarUIState` / `SDS_Settings`） | `Scribe_Values` / `Scribe_Deep` / `Scribe_References` / `Scribe_Collections` 与 `LookMode`；`PostLoadInit` 兜底 | 节点名与 `LookMode` 属存档锁定清单（《ModuleInterfaces.zh-CN.md》八），不得随手改名 |
| `Thing` | `SDS_StructureFact`、`SDS_ReclaimSave`、`SDS_PreserveHooks`、`ISDS_*` 入参 | 原版可识别对象；`LookMode.Reference` 由原版引用表恢复 | 只持身份与引用，不接管生命周期、不做 `Spawn` / `Destroy` |
| `ThingDef` | `SDS_StockKey.thingDef` / `stuff`、`ISDS_MedicalCarePolicy.Allows` | def 是品类身份；材质须 `IsStuff` | 未解析是合法状态，条目保留为 Unresolved（`Resolved` / `HasIdentity`） |
| `ThingWithComps` + `TransferableOneWay` | `ISDS_TransportLoadSink` | 原版装载清单的宿主与行形状 | 只注入候选行，不取货、不扣账、不改装载 Job（W50，《RuleIndex.zh-CN.md》） |
| `Job` / `JobDef` | `ISDS_DepositJobs` | `JobDef.driverClass` 是原版驱动类型 | 只提供「造 Job」，不改 Job / Toil |
| `HediffDef` | `SDS_HediffDefOf` | 芯片植入状态的原版标识 | 缺失返回 null，只记一次错误（`SDS_Implants.cs`） |
| `Pawn` / `Map` / `Faction` | `SDS_ApiCaller`、`ISDS_Port`、`ISDS_*` 参数 | 所有权、阵营、地图与可达性的事实载体 | 只携带，判定在 Core / Api 边界 |
| `IntVec3` / `PathEndMode` / `Danger` / `StoragePriority` / `Vector2` | `SDS_EndpointQuery`、`ISDS_Port`、`SDS_Settings`、`SDS_SidebarUIState` | 原版几何、走位模式、危险度、存储优先级、UI 滚动量 | 纯数据，不触发寻路或行为 |

#### 2.1 刻意不出现的原版类型

- `ThingFilter`：筛选值是存档级的状态（`SDS_VanillaState.depositFilter`，`LookMode.Deep`，见《ModuleInterfaces.zh-CN.md》4.1），契约只保留无规则的事实查询 `ISDS_DepositFilter.Allows(Thing)`（《ModuleInterfaces.zh-CN.md》1.2）。筛选语义与 UI 都在 Vanilla。
- `IngredientCount`：配方槽位与缺口计算属 S16 的唯一规则实现 `SDS_BillSupplyPlanner`，签名是 `SDS.Rules` 的 `internal`（《ModuleInterfaces.zh-CN.md》3.5），因此不进入 Contracts 的公共面。

### 三、枚举边界

- 当前引用的原版枚举：`Danger`（端点查询）、`PathEndMode`（端口访问模式）、`StoragePriority`（设置与优先级读取口），均属数据枚举。
- 当前未出现的枚举：`WorkGiverDef` / `ThinkTreeDef` / `DutyDef` 等只驱动 AI、Job、工作分配的行为性枚举。
- 自有枚举全部是状态、通道或结果码（`SDS_StockChannel`、`SDS_EndpointOp`、`SDS_WithdrawMode`、`SDS_SupplyFailure`、`SDS_WithdrawEndState`、`SDS_ReclaimPolicy`、`SDS_ReclaimOutcome`、`SDS_ReturnFallback`、`SDS_StructureKind`、`SDS_ApiResultCode`），只描述数据状态，不决定流程。

### 四、序列化约束

完整节点名与 LookMode 属存档锁定清单，见《ModuleInterfaces.zh-CN.md》八；锁定口径与检查方式见 `Rules.zh-CN.md` C6。本节只记语义要点：

- 单写 + 旧节点只读：`SDS_StockKey` 保存只写 `defName` / `stuffName`（2026-11-16 起停写旧节点）；旧节点 `thingDef` / `stuff` 仅用于读档兜底，新字段为空才回退（W26 只承诺旧档 → 新版）。
- 名字兜底：`defName` / `stuffName` 无条件落盘；def 缺失时条目保留为 Unresolved，等 def 回来再 `TryResolve`。
- `SDS_StockVitals` 不落盘，只做运行期 DTO（《ModuleResponsibilities.zh-CN.md》三）；`SDS_ReclaimSave` / `SDS_SidebarUIState` 的读档兜底在 `PostLoadInit`。

### 五、缺失与降级

降级口径见 `Rules.zh-CN.md` C8，本节只补原版特有事实：

- `BackCompatibility.BackCompatibleDefName` 可能抛异常；`SDS_StockKey.TryResolve` 捕获后按原名 `GetNamedSilentFail`，这层兜底不能删。
- 待补：`SDS_StockKey` 对 `BackCompatibility` 之外的原版兼容钩子是否有额外依赖，当前源码未见。

## 第三方支持

> 定位：第三方只依赖本程序集（加 `SDS.Api` 门面）即可使用的接入口、各自消费方与边界。签名见 A 节，规则见 `Rules.zh-CN.md`。

### 一、依赖方向

- `SDS.ThirdModCompat` 编译期只允许引用 Contracts、Diag、Api 与 Harmony；目标 Mod 只反射探测，不静态引用（《ModuleRules.zh-CN.md》四）。
- 第三方以 soft-depend（反射探测 `SDS_API.Version`）接入，不硬引用本 Mod 内部实现；当前版本 4，程序集改名与「须重新 soft-depend」的兼容声明见 `Rules.zh-CN.md` 规则正文（E5）。
- 兼容层只经 Api / Contracts / Diag（E6）；每个第三方入口只有一条写接管路径（E7）。
- 本程序集是对所有模块与第三方都开放的共享面：公共类型、接口、DefOf、设置、`SDS_Host`。

### 二、四个公开接入口

#### 2.1 `ISDSCompatAdapter`：适配器登记契约

- 定义在 Contracts（`SDS_Host.cs`），成员：`Name` / `TargetPackageId` / `IsTargetLoaded` / `Apply`（《ModuleInterfaces.zh-CN.md》1.2）。
- 实现位置：本仓库 9 个适配器都在 `SDS.ThirdModCompat` 单程序集内，`Apply` 内各自创建唯一 Harmony 实例（《ModuleRules.zh-CN.md》6.6、八）。
- 扫描方式：Bootstrap 的 `SDS_CompatLoader` 反射枚举 `AppDomain` 已加载程序集里的实现，不引用适配器程序集（`超维存储/Source/SDS.Bootstrap/SDS_CompatLoader.cs`）。
- 扫描约束（源码可核实）：
  - 需要 public 无参构造（`Activator.CreateInstance`）；构造失败记 `ErrorOnce` 并跳过。
  - `Name` 为空时回退 `type.FullName`；`Name`（即 Harmony ID）重复时记 Error 并跳过后注册者（《ModuleRules.zh-CN.md》八）。
  - `IsTargetLoaded` 为 false 时不调用 `Apply`；`Apply` 前先登记，异常不重试，避免形成第二条写路径。
- 幂等：可在 `LongEventHandler.ExecuteWhenFinished` 再扫一次（《ModuleRules.zh-CN.md》九 第 4 步）。
- 目标 packageId 与 Harmony ID 对照见《ModuleResponsibilities.zh-CN.md》八，此处不复述。

#### 2.2 `SDS_PreserveHooks`：保真事实探针

- 纯注册表：`Register(Func<Thing, bool>)`，空或重复注册返回 false；`RequiresPreserved(Thing)` 任一探针为真即要求保真（`SDS_PreserveHooks.cs`）。
- 消费方：Core 的 `SDS_StockRules.RequiresPreservedStorage(Thing)` 末尾（《ModuleInterfaces.zh-CN.md》1.3；`超维存储/Source/SDS.Core/SDS_StockRules.cs`）。
- 边界：只登记「该实物必须保真」的事实，不扣账、不派活、不建立第二条写路径；单个探针异常按探针名 `Log.ErrorOnce` 一次后跳过。
- 生命周期：注册项是进程级常量，不登记 `SDS_StaticState`；`Reset` 仅供测试与重载场景（《ModuleInterfaces.zh-CN.md》1.3）。

#### 2.3 `SDS_JobFuelCount`（2026-11-16 已删除）

- 原为第三方整备 Job 的剩余需求量注册表；方案乙落地后数量缺失统一由 W58（`Toils_Haul.ErrorCheckForCarry` + 预留意图）处理，不再需要第三方注册。
- 现契约：任何调用方都不得再依赖该类型或 `SDS_API.RegisterJobFuelCountProvider`；数量口径见《RuleIndex.zh-CN.md》W30 / W58。

#### 2.4 `SDS_API` 的 caller / result DTO

| 类型 | 用途 | 可核实的约束 |
|---|---|---|
| `SDS_ApiCaller` | 调用方上下文：`faction` / `pawn` / `claimOwner`，`FromPawn` / `FromFaction` 工厂 | 会改状态或涉及可达性的调用必须携带；无 Pawn 时 `IsNonPawn` 为 true（`设计方案/PublicAPI.zh-CN.md` 3.4） |
| `SDS_ApiResultCode` | 16 个结构化结果码，从 `Ok` 到 `InternalError` | 异常统一 `InternalError`；正常拒绝不写日志（`设计方案/PublicAPI.zh-CN.md` 3.6） |
| `SDS_ApiStockInfo` | 只读快照：`total` / `available` / `forbidden` / `resolved` / `failure` / `versions` / `result` | 不建事务、不预留、不扣账（《ModuleInterfaces.zh-CN.md》1.1） |
| `SDS_ReadVersions` | `tick` / `mirror` / `endpoint` / `stock` / `reservation` / `filter` | 调用方据此判定只读缓存失效，不要自行拼版本 |

以上类型全部在 Contracts，第三方只需要 Contracts 与 `SDS.Api` 两个程序集的公共面即可完成只读取数与结果判定。

### 三、Core 不消费的 3 个宿主槽

`SDS_Host` 共 11 个槽：Core 只直接消费 `Ports` / `DepositFilter` / `Delivery`；`Priority` / `MirrorPhysical` / `MedicalCare` 改由调用方以查询字段 / 计划字段 / 委托注入，`ReclaimWorld` 归 Vanilla 内部使用，`MirrorQuery` / `Display` / `DepositJobs` / `TransportLoad` 服务 Api 适配器桥（11 槽全表见《ModuleInterfaces.zh-CN.md》一，此处不复述）。

| 槽 | 接口 | Vanilla 实现 | 转发目标 | Core 是否消费 |
|---|---|---|---|---|
| `Display` | `ISDS_DisplayQuery` | `SDS_DisplayAdapter` | `SDS_OutputUtility.TryGetDisplayKey` | 否，Api 适配器桥用 |
| `DepositJobs` | `ISDS_DepositJobs` | `SDS_DepositJobsAdapter` | `SDS_DepositService` / `SDS_ChipManualDeposit` | 否，Api 适配器桥用 |
| `TransportLoad` | `ISDS_TransportLoadSink` | `SDS_TransportLoadAdapter` | `SDS_TransportVirtual.InjectIntoTransporters` | 否，Api 适配器桥用 |

- 实现归属见《ModuleResponsibilities.zh-CN.md》六；三者都是 public 无参构造、构造时不读存档（《ModuleInterfaces.zh-CN.md》4.2）。
- 这 3 个槽是第三方入口的专用预留：Job 型存入（PUAH 等）、展示物身份识别、自建装载清单注入都不需要 Core 参与决策，只经 Api 转发（《ModuleInterfaces.zh-CN.md》五）。

### 四、接入检查清单

- 不硬引用目标 Mod 程序集；类型 / 方法 / 字段用 `AccessTools.TypeByName` 或反射探测（《ModuleRules.zh-CN.md》6.6）。
- 只调 `SDS_API`（五轴 + 适配器桥）与 Contracts 公共类型；不引用 Core / Vanilla 的实现类（《ModuleRules.zh-CN.md》6.6、《ModuleInterfaces.zh-CN.md》五）。
- 适配器只做翻译：不选端点、不算缺口、不维护影子库存、不改第三方 Job / Toil（`设计方案/PublicAPI.zh-CN.md` 4.2）。
- 不调用 `SDS_Diag.Fail`；异常只用 `ErrorOnce` / `Warn` / 报告中心（《ModuleRules.zh-CN.md》6.6）。
- 适配器名称与 Harmony ID 一致且全局唯一；目标未装时返回 `IsTargetLoaded = false`（《ModuleRules.zh-CN.md》八）。
- 写操作只经 Api，禁止「门面 + Harmony 补丁」双触发（E7，《ModuleRules.zh-CN.md》三）。

### 五、待补

- 外部第三方（非本仓库适配器）的实际采用记录：待补。`设计方案/PublicAPI.zh-CN.md` 的采用证据列目前标注为「待补」。
- 外部第三方自行实现 `ISDSCompatAdapter` 的兼容承诺：扫描器不做来源过滤（枚举所有已加载程序集），但目前只验证过本仓库 9 个适配器，属待定边界。
