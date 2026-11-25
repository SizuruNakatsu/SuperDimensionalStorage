# Interfaces

> Positioning: Section A is this module's external signature, copied item by item from the current source under `SuperDimensionalStorage/Source/SDS.Contracts/`; Section B is a reverse inventory recording which types of this module the other 6 assemblies actually consume (verified on 2026-11-12 by full-text source search). The two sections after it, "Vanilla support" and "Third-party support", record respectively this module's contact surface with vanilla types and the public entry points available to third parties; signatures follow Section A as the authority, and the rules are in `Rules.en.md`.

## A. This module's external signatures

### A.1 Enums

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

### A.2 Category and ledger DTOs

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

### A.3 Transaction, reclaim, and version DTOs

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

### A.4 Endpoint and structure DTOs

```csharp
public struct SDS_StructureFact
{ public Thing Thing; public Map Map; public SDS_StructureKind Kind; public bool Operational; }
// Category capacity is computed by Core reading SDS_Config at query time; the fact only answers "does the structure exist / is it operational".

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

### A.5 API boundary DTOs

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

### A.6 Ports and host interfaces

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

/// <summary>Sole evaluation basis of the medical care gate (called by Vanilla / Api; Core does not call it).</summary>
public static class SDS_MedicalGate { public static bool Allows(Thing billGiver, ThingDef def); }
public interface ISDS_ReclaimWorld
{ int CurrentTick { get; } bool JobExists(int jobLoadID); bool LordExists(int lordLoadID); void RemoveFromCaravan(int lordLoadID, Thing thing); }
public interface ISDS_DiagnosticProvider { string Name { get; } void AppendReport(List<string> lines); }
public interface ISDSCompatAdapter { string Name { get; } string TargetPackageId { get; } bool IsTargetLoaded { get; } void Apply(); }
```

> **Removed extension points (2026-11-14, L2)**: `ISDS_DeliverySink.TryDeliverToContainer`, `ISDS_MirrorQuery.TryGetMirrorFor`, `ISDS_DepositFilter.Version`, `ISDS_PrioritySource.Version`, and `ISDS_PortProvider.HasUsableBEndpointOn` had no production consumers and were removed per DesignPrinciples.en.md §3 L2; `SDS_EndpointQuery.danger` is a leftover field from the old framework — the factories still write it but decisions never read it, and it is kept along with the old contract; `SDS_SupplyFailure.RebuildFailed` is a reserved value (`GetFailure` does not currently produce it), and its mapping is kept.

### A.7 SDS_Host and the pure registries

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

### A.8 DefOf, settings, and save DTOs

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

The default field values of `SDS_Settings` and the `SDS_Config` constants (capacity 30 / 10–90, stabilizer 30 / 10–90, power draw 1000 / 0–2000, 1000 / 0–2000, 10 / 0–100, and the three Def names) are in the source `SDS_Mod.cs`; this file lists signatures only.

## B. Types actually consumed by the other 6 assemblies

| Consumer | Contracts types actually referenced (full-text source search) |
|---|---|
| `SDS.Diag` | `SDS_StockKey`, `SDS_WithdrawEndState`, `ISDS_DiagnosticProvider` |
| `SDS.Core` | Categories and ledger: `SDS_StockKey`, `SDS_StockEntry`, `SDS_StockVitals`, `SDS_Reservation`, `SDS_StockChannel`; transactions and reclaim: `SDS_WithdrawMode`, `SDS_WithdrawQuery`, `SDS_WithdrawPlan`, `SDS_WithdrawEndState`, `SDS_SupplyFailure`, `SDS_ReturnFallback`; endpoints and structure: `SDS_EndpointOp`, `SDS_EndpointQuery`, `SDS_EndpointDecision`, `SDS_StructureKind`, `SDS_StructureFact`; settings and DefOf: `SDS_Config`, `SDS_HediffDefOf`; registries: `SDS_PreserveHooks`; host: `SDS_Host`, `ISDS_Port`, `ISDS_PortProvider`, `ISDS_DepositFilter`, `ISDS_DeliverySink`, `ISDS_MirrorPhysical`, `ISDS_DiagnosticProvider` |
| `SDS.Api` | `SDS_StockKey`, `SDS_StockChannel`, `SDS_WithdrawMode`, `SDS_SupplyFailure`, `SDS_WithdrawQuery`, `SDS_WithdrawPlan`, `SDS_ReadVersions`, `SDS_EndpointOp`, `SDS_EndpointQuery`, `SDS_EndpointDecision`, `SDS_ApiCaller`, `SDS_ApiResultCode`, `SDS_ApiStockInfo`, `SDS_JobDefOf`, `SDS_ReturnFallback`, `SDS_Host`, `ISDS_Port`, `ISDS_PortProvider`, `ISDS_MirrorQuery`, `ISDS_DisplayQuery`, `ISDS_DepositJobs`, `ISDS_TransportLoadSink` |
| `SDS.Vanilla` | Categories and ledger: `SDS_StockKey`, `SDS_StockEntry`, `SDS_StockChannel`; transactions and reclaim: `SDS_WithdrawMode`, `SDS_SupplyFailure`, `SDS_WithdrawDeliveryPolicyDef`, `SDS_WithdrawQuery`, `SDS_WithdrawPlan`, `SDS_ReadVersions`, `SDS_ReturnFallback`, `SDS_ReclaimSave`, `SDS_ReclaimPolicy`, `SDS_ReclaimOutcome`; endpoints and structure: `SDS_EndpointOp`, `SDS_EndpointQuery`, `SDS_EndpointDecision`, `SDS_StructureKind`, `SDS_StructureFact`; settings and DefOf: `SDS_Config`, `SDS_JobDefOf`, `SDS_HediffDefOf`; registries and save DTOs: `SDS_CompatGates`, `SDS_TransportLoadSave`, `SDS_SidebarUIState`; host: `SDS_Host` and the 13 host interfaces other than `ISDSCompatAdapter` (including `ISDS_Port`, `ISDS_DiagnosticProvider`) |
| `SDS.ThirdModCompat` | `SDS_StockKey`, `SDS_WithdrawMode`, `SDS_ApiCaller`, `SDS_CompatGates`, `ISDSCompatAdapter` |
| `SDS.Bootstrap` | `SDS_Settings`, `SDS_Config`, `SDS_Host`, `ISDSCompatAdapter` |

Types not present in the table are not directly referenced by the corresponding assembly; for example, `SDS_StockVitals` / `SDS_Reservation` are used only by Core, `SDS_ReclaimSave` / `SDS_ReclaimPolicy` / `SDS_ReclaimOutcome` only by Vanilla (after C1/C8, Core no longer knows any reclaim DTO), `SDS_ApiStockInfo` only by Api, and `SDS_TransportLoadSave` / `SDS_SidebarUIState` only by Vanilla.

## Call relationships

- Bootstrap's `SDS_HostInstaller` calls `SDS_Host.Install(SDS_HostSlots)` to inject the 11 Vanilla implementations into the static slots; it is the only permitted injector.
- Bootstrap's `SDS_Mod` writes the `SDS_Settings` instance into `SDS_Config.Current` at startup; Core / Vanilla only read it afterwards.
- Core obtains external facts only through the `Ports` / `DepositFilter` / `Delivery` slots of `SDS_Host`; priority / mirror physical access / the medical care gate are instead injected by callers as query fields / plan fields / delegates, and `ReclaimWorld` is for Vanilla's internal use.
- Vanilla implements the 11 slots of `SDS_Host`, `ISDS_Port`, and `ISDS_DiagnosticProvider`, while also reading `SDS_Config`, `SDS_JobDefOf`, `SDS_HediffDefOf`, and the save DTOs.
- Api uses `SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` as its boundary shapes, exposes `SDS_API` to ThirdModCompat, and consumes the endpoint / transaction DTOs.
- ThirdModCompat only implements `ISDSCompatAdapter` and calls `SDS_API`, treating `SDS_StockKey` / `SDS_WithdrawMode` / `SDS_ApiCaller` as translation types.
- Diag uses `SDS_StockKey` / `SDS_WithdrawEndState` only in assertion signatures and collects `ISDS_DiagnosticProvider` reports.
- Tests reference all modules; after filling the 11 slots of `SDS_Host` with fake implementations, they drive Core directly without launching the game.

## Vanilla support

> Positioning: this module's contact surface with vanilla RimWorld / Verse — no patches, no behavior calls, but contract fields directly reference vanilla types and serialization semantics. Allow / forbid judgments take ModuleRules.en.md 6.1 as the sole authority; this section records only the current state and boundaries.

### 1. This assembly's relationship with vanilla

- The patch count is 0: all 112 feature patch classes are in Vanilla, and the dynamic patches of the 9 third-party adapters are in ThirdModCompat (ModuleRules.en.md §7, §8).
- The referencable vanilla surface is only RimWorld / Verse / UnityEngine data types, with no behavior entry points.
- It does not reference `SDS_Diag`; missing defs and exceptions go only through `Log.ErrorOnce` (current implementations in `SDS_CaravanTransport.cs`, `SDS_Implants.cs`).
- This section only explains type semantics and defines no gameplay rules.

### 2. Vanilla types appearing in the contracts and their uses

| Vanilla type | Where it appears in Contracts | Vanilla semantics relied upon | Contract boundary |
|---|---|---|---|
| `ModSettings` | `SDS_Settings` | The vanilla settings container's `ExposeData` + `Scribe_Values` | Stores data only; the settings UI belongs to Bootstrap, and Bootstrap only writes data (ModuleInterfaces.en.md 1.3) |
| `Def` | `SDS_WithdrawDeliveryPolicyDef` | XML definitions, `DefDatabase` enumeration | The type is in Contracts, the resolver in Vanilla (`SDS.Vanilla/SDS_JobTargets.cs`); the contract does not read the Def database |
| `DefDatabase<T>` | `SDS_JobDefOf`, `SDS_HediffDefOf`, `SDS_StockKey.TryResolve` | `GetNamedSilentFail` returns null without throwing; `AllDefsListForReading` is called by the user | Looked up live on every access, no static caching (ModuleInterfaces.en.md 1.3) |
| `BackCompatibility` | `SDS_StockKey.TryResolve` | The vanilla def-rename compatibility chain `BackCompatibleDefName` | When the compatibility chain throws, fall back to resolving by the original name; entry retention is unaffected |
| `IExposable` / `Scribe` | All save DTOs (`SDS_StockKey` / `SDS_StockEntry` / `SDS_Reservation` / `SDS_ReclaimSave` / `SDS_TransportLoadSave` / `SDS_SidebarUIState` / `SDS_Settings`) | `Scribe_Values` / `Scribe_Deep` / `Scribe_References` / `Scribe_Collections` and `LookMode`; `PostLoadInit` as the fallback | Node names and `LookMode` belong to the save-locked inventory (ModuleInterfaces.en.md §8) and must not be renamed casually |
| `Thing` | `SDS_StructureFact`, `SDS_ReclaimSave`, `SDS_PreserveHooks`, `ISDS_*` parameters | Vanilla-referencable objects; `LookMode.Reference` is restored by the vanilla reference table | Holds identity and references only; does not take over lifecycles and never does `Spawn` / `Destroy` |
| `ThingDef` | `SDS_StockKey.thingDef` / `stuff`, `ISDS_MedicalCarePolicy.Allows` | The def is the category identity; stuff must be `IsStuff` | Unresolved is a legal state; the entry is kept as Unresolved (`Resolved` / `HasIdentity`) |
| `ThingWithComps` + `TransferableOneWay` | `ISDS_TransportLoadSink` | The host and row shape of the vanilla loading list | Only injects candidate rows; it does not pick up goods, deduct the ledger, or modify loading jobs (W50, RuleIndex.en.md) |
| `Job` / `JobDef` | `ISDS_DepositJobs` | `JobDef.driverClass` is the vanilla driver type | Only provides "make a Job"; does not modify Job / Toil |
| `HediffDef` | `SDS_HediffDefOf` | The vanilla identifier of chip implant state | Returns null when missing and logs the error once (`SDS_Implants.cs`) |
| `Pawn` / `Map` / `Faction` | `SDS_ApiCaller`, `ISDS_Port`, `ISDS_*` parameters | Fact carriers for ownership, faction, map, and reachability | Carried only; judgment happens at the Core / Api boundary |
| `IntVec3` / `PathEndMode` / `Danger` / `StoragePriority` / `Vector2` | `SDS_EndpointQuery`, `ISDS_Port`, `SDS_Settings`, `SDS_SidebarUIState` | Vanilla geometry, pathing modes, danger levels, storage priority, and UI scroll amounts | Pure data; triggers no pathfinding or behavior |

#### 2.1 Vanilla types deliberately absent

- `ThingFilter`: the filter value is save-level state (`SDS_VanillaState.depositFilter`, `LookMode.Deep`, see ModuleInterfaces.en.md 4.1); the contract keeps only the rule-free fact query `ISDS_DepositFilter.Allows(Thing)` (ModuleInterfaces.en.md 1.2). Filter semantics and UI are both in Vanilla.
- `IngredientCount`: recipe slot and shortfall computation belongs to S16's single rule implementation `SDS_BillSupplyPlanner`, whose signature is `internal` to `SDS.Rules` (ModuleInterfaces.en.md 3.5), so it does not enter Contracts' public surface.

### 3. Enum boundaries

- Vanilla enums currently referenced: `Danger` (endpoint queries), `PathEndMode` (port access modes), `StoragePriority` (settings and the priority read accessor) — all data enums.
- Enums currently absent: behavior enums such as `WorkGiverDef` / `ThinkTreeDef` / `DutyDef` that only drive AI, Jobs, and work assignment.
- All first-party enums are states, channels, or result codes (`SDS_StockChannel`, `SDS_EndpointOp`, `SDS_WithdrawMode`, `SDS_SupplyFailure`, `SDS_WithdrawEndState`, `SDS_ReclaimPolicy`, `SDS_ReclaimOutcome`, `SDS_ReturnFallback`, `SDS_StructureKind`, `SDS_ApiResultCode`); they describe data states only and never decide the flow.

### 4. Serialization constraints

The full node names and LookMode values belong to the save-locked inventory, see ModuleInterfaces.en.md §8; the locking basis and checking method are in `Rules.en.md` C6. This section records only the semantic essentials:

- Single write + read-only legacy nodes: saving `SDS_StockKey` writes only `defName` / `stuffName` (legacy nodes stopped being written as of 2026-11-16); the legacy nodes `thingDef` / `stuff` serve only as a save-loading fallback and are used only when the new fields are empty (W26 promises only old save → new version).
- Name fallback: `defName` / `stuffName` are persisted unconditionally; when a def is missing, the entry is kept as Unresolved until the def returns and `TryResolve` runs.
- `SDS_StockVitals` is never persisted and serves only as a runtime DTO (ModuleResponsibilities.en.md §3); the save-loading fallback for `SDS_ReclaimSave` / `SDS_SidebarUIState` is `PostLoadInit`.

### 5. Missing content and degradation

The degradation basis is in `Rules.en.md` C8; this section adds only vanilla-specific facts:

- `BackCompatibility.BackCompatibleDefName` may throw; `SDS_StockKey.TryResolve` catches that and falls back to `GetNamedSilentFail` with the original name — this fallback layer must not be removed.
- To be filled in: whether `SDS_StockKey` has any further dependencies on vanilla compatibility hooks beyond `BackCompatibility`; none are visible in the current source.

## Third-party support

> Positioning: the entry points third parties can use by depending only on this assembly (plus the `SDS.Api` facade), their respective consumers, and their boundaries. Signatures are in Section A; rules are in `Rules.en.md`.

### 1. Dependency direction

- `SDS.ThirdModCompat` may reference only Contracts, Diag, Api, and Harmony at compile time; target mods are probed only via reflection, never statically referenced (ModuleRules.en.md §4).
- Third parties integrate as soft-dependencies (probing `SDS_API.Version` via reflection) and never hard-reference this mod's internals; the current version is 4. The assembly rename and the "must re-soft-depend" compatibility statement are in the normative rule text of `Rules.en.md` (E5).
- The compatibility layer goes only through Api / Contracts / Diag (E6); each third-party entry point has exactly one write take-over path (E7).
- This assembly is the shared surface open to all modules and third parties: public types, interfaces, DefOf, settings, `SDS_Host`.

### 2. The four public entry points

#### 2.1 `ISDSCompatAdapter`: the adapter registration contract

- Defined in Contracts (`SDS_Host.cs`); members: `Name` / `TargetPackageId` / `IsTargetLoaded` / `Apply` (ModuleInterfaces.en.md 1.2).
- Implementation location: all 9 adapters of this repository live inside the single `SDS.ThirdModCompat` assembly, and each `Apply` creates its own unique Harmony instance (ModuleRules.en.md 6.6, §8).
- Scan method: Bootstrap's `SDS_CompatLoader` uses reflection to enumerate implementations among the assemblies loaded in the `AppDomain`, without referencing adapter assemblies (`SuperDimensionalStorage/Source/SDS.Bootstrap/SDS_CompatLoader.cs`).
- Scan constraints (verifiable in source):
  - A public parameterless constructor is required (`Activator.CreateInstance`); on construction failure, an `ErrorOnce` is logged and the adapter is skipped.
  - When `Name` is empty, it falls back to `type.FullName`; when `Name` (i.e. the Harmony ID) is duplicated, an Error is logged and the later registrant is skipped (ModuleRules.en.md §8).
  - When `IsTargetLoaded` is false, `Apply` is not called; registration happens before `Apply`, and exceptions are not retried, to avoid forming a second write path.
- Idempotence: the scan may run once more inside `LongEventHandler.ExecuteWhenFinished` (ModuleRules.en.md §9, step 4).
- The mapping of target packageIds to Harmony IDs is in ModuleResponsibilities.en.md §8 and is not restated here.

#### 2.2 `SDS_PreserveHooks`: preserved-fact probes

- A pure registry: `Register(Func<Thing, bool>)` returns false for a null or duplicate probe; `RequiresPreserved(Thing)` requires preservation as soon as any probe is true (`SDS_PreserveHooks.cs`).
- Consumer: the tail of Core's `SDS_StockRules.RequiresPreservedStorage(Thing)` (ModuleInterfaces.en.md 1.3; `SuperDimensionalStorage/Source/SDS.Core/SDS_StockRules.cs`).
- Boundary: it registers only the fact that "this physical thing must be preserved"; it does not deduct the ledger, assign jobs, or build a second write path; if a single probe throws, it is logged once via `Log.ErrorOnce` under the probe's name and then skipped.
- Lifecycle: registrations are process-level constants and are not registered with `SDS_StaticState`; `Reset` exists only for tests and reload scenarios (ModuleInterfaces.en.md 1.3).

#### 2.3 `SDS_JobFuelCount` (removed on 2026-11-16)

- Formerly a registry of remaining requirement counts for third-party prep jobs; after Plan B landed, missing counts are handled uniformly by W58 (`Toils_Haul.ErrorCheckForCarry` + reservation intent), so third-party registration is no longer needed.
- Current contract: no caller may depend on this type or on `SDS_API.RegisterJobFuelCountProvider` any longer; the count basis is in RuleIndex.en.md W30 / W58.

#### 2.4 The `SDS_API` caller / result DTOs

| Type | Purpose | Verifiable constraints |
|---|---|---|
| `SDS_ApiCaller` | Caller context: `faction` / `pawn` / `claimOwner`, with the `FromPawn` / `FromFaction` factories | Calls that mutate state or involve reachability must carry one; `IsNonPawn` is true when there is no Pawn (DesignProposals/PublicAPI.en.md 3.4) |
| `SDS_ApiResultCode` | 16 structured result codes, from `Ok` to `InternalError` | Exceptions uniformly map to `InternalError`; normal rejections write no log (DesignProposals/PublicAPI.en.md 3.6) |
| `SDS_ApiStockInfo` | Read-only snapshot: `total` / `available` / `forbidden` / `resolved` / `failure` / `versions` / `result` | Builds no transaction, makes no reservation, deducts no ledger (ModuleInterfaces.en.md 1.1) |
| `SDS_ReadVersions` | `tick` / `mirror` / `endpoint` / `stock` / `reservation` / `filter` | Callers use it to judge read-only cache invalidation; never assemble versions yourself |

All of these types live in Contracts; a third party needs only the public surfaces of the Contracts and `SDS.Api` assemblies to do read-only data retrieval and result judgment.

### 3. The 3 host slots Core does not consume

`SDS_Host` has 11 slots in total: Core directly consumes only `Ports` / `DepositFilter` / `Delivery`; `Priority` / `MirrorPhysical` / `MedicalCare` are instead injected by callers as query fields / plan fields / delegates; `ReclaimWorld` is for Vanilla's internal use; and `MirrorQuery` / `Display` / `DepositJobs` / `TransportLoad` serve the Api adapter bridge (the full 11-slot table is in ModuleInterfaces.en.md §1 and is not restated here).

| Slot | Interface | Vanilla implementation | Forwarding target | Consumed by Core |
|---|---|---|---|---|
| `Display` | `ISDS_DisplayQuery` | `SDS_DisplayAdapter` | `SDS_OutputUtility.TryGetDisplayKey` | No — used by the Api adapter bridge |
| `DepositJobs` | `ISDS_DepositJobs` | `SDS_DepositJobsAdapter` | `SDS_DepositService` / `SDS_ChipManualDeposit` | No — used by the Api adapter bridge |
| `TransportLoad` | `ISDS_TransportLoadSink` | `SDS_TransportLoadAdapter` | `SDS_TransportVirtual.InjectIntoTransporters` | No — used by the Api adapter bridge |

- Implementation ownership is in ModuleResponsibilities.en.md §6; all three have public parameterless constructors and read no save data at construction (ModuleInterfaces.en.md 4.2).
- These 3 slots are a reservation dedicated to third-party entry points: job-style deposits (PUAH etc.), display thing identity recognition, and injection of self-built loading lists need no Core decision-making and are forwarded only through Api (ModuleInterfaces.en.md §5).

### 4. Integration checklist

- Never hard-reference target mod assemblies; resolve types / methods / fields with `AccessTools.TypeByName` or reflection probes (ModuleRules.en.md 6.6).
- Call only `SDS_API` (the five axes (See/Read/Find/Take/Store) + the adapter bridge) and Contracts public types; never reference Core / Vanilla implementation classes (ModuleRules.en.md 6.6, ModuleInterfaces.en.md §5).
- Adapters only translate: they do not choose endpoints, compute shortfalls, maintain shadow stock, or alter third-party Job / Toil (DesignProposals/PublicAPI.en.md 4.2).
- Never call `SDS_Diag.Fail`; exceptions use only `ErrorOnce` / `Warn` / the report center (ModuleRules.en.md 6.6).
- Adapter names equal their Harmony IDs and are globally unique; when the target is not installed, return `IsTargetLoaded = false` (ModuleRules.en.md §8).
- Writes go only through Api; "facade + Harmony patch" double triggering is forbidden (E7, ModuleRules.en.md §3).

### 5. To be filled in

- Actual adoption records of external third parties (adapters not from this repository): to be filled in. The adoption-evidence column of DesignProposals/PublicAPI.en.md is currently marked "to be filled in".
- The compatibility commitment for external third parties implementing `ISDSCompatAdapter` themselves: the scanner does not filter by origin (it enumerates all loaded assemblies), but so far only this repository's 9 adapters have been verified — a boundary yet to be decided.
