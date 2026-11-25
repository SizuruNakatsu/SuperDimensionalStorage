# SDS.ThirdModCompat Interfaces

> Role: the in-assembly signatures of this module, the identities of the 9 adapters, the `SDS_API` call inventory, the Contracts dependencies, and the adaptation facts in the "Vanilla Support" and "Third-Party Support" sections. Rules: see Rules.en.md; signatures are authoritative per the sources under `超维存储/Source/SDS.ThirdModCompat/`.

## A. Sdk Utility Class Signatures

The 5 utility classes and 9 adapters are all `internal` in source: this assembly's public surface is 0 (ModuleResponsibilities.en.md 2). Bootstrap discovers them via reflection; tests reference them directly via the registered `InternalsVisibleTo("SDS.Tests")`.

### A.1 `SDS_CompatTools` (`SDS_CompatServices.cs`)

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

`TryPatch` is the sole entry point for dynamic hook points: `target == null` → `Missing`; an exception while applying → `Missing` + `ErrorOnce`; success → `Applied`. Specifying no Prefix / Postfix / Finalizer is also recorded as `Missing`.

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

Both `TryWithdraw*ByDef` methods use `SDS_ApiCaller.FromFaction(Faction.OfPlayerSilentFail)` internally; `CountAvailable` is the combined "virtual + preserved" count, and callers must not add the preserved amount on top; `ReturnPreserved` calls `SDS_API.TryReturnPreserved(..., IntVec3.Invalid)` per thing, skipping null / destroyed ones.

### A.4 `SDS_CompatBillSupply`

```csharp
internal static class SDS_CompatBillSupply
{
    public static bool TrySupplyPreservedBill(Bill bill, Map map, IntVec3 rootCell,
        ThingOwner destination, out List<Thing> made);
}
```

Supplies the preserved channel only; candidates = physical things already in the target container + `AppendPreservedThings`, selection reuses the vanilla `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`, and withdrawal goes through `TryWithdrawPreservedExact`.

### A.5 `SDS_CompatMirrorSeam` (`SDS_CompatServices.cs`)

```csharp
internal static class SDS_CompatMirrorSeam
{
    public static bool TryMaterializeForReservation(JobDriver driver, TargetIndex ind, int count,
        Type compType, FieldInfo[] fields, string logLabel);
}
```

The sole implementation of endpoint materialization before third-party Job reservations: mirror target → nearest reachable endpoint access cell → withdraw goods → copy comp fields → place with `GenPlace` → rewrite target; on failure, return to the ledger or decline takeover. Access only via `SDS_API`, without touching W54 / Core; a second same-shape adapter only passes descriptors and must not copy this implementation.

## B. The 9 Adapters

`Name` / `TargetPackageId` / `HarmonyId` are taken from the adapter constants; `ISDSResettable` from the type declarations. The `HarmonyId` contract is in ModuleRules.en.md 8; `packageId` and hook point counts are in §F.1.

| Adapter | `Name` (argument) | `TargetPackageId` (argument) | `ISDSResettable` |
|---|---|---|---|
| `SDS_CompatAdapter_PUAH` | `Pick Up And Haul` | `Mehni.PickUpAndHaul` | No |
| `SDS_CompatAdapter_CE` | `Combat Extended` | `CETeam.CombatExtended` | Yes (clears cooldown / priority caches) |
| `SDS_CompatAdapter_UFRecipes` | `联合轻工家具拓展-UF Light Industries Furniture` | `KindSeal.UFLI` | No |
| `SDS_CompatAdapter_ManipulatorBeam` | `牵引光束搬运器Manipulator Beam Emitter` | `natsuki.manipulatorbeam` | No |
| `SDS_CompatAdapter_MiliraExpanded` | `米莉拉扩展-未来科技Milira Expanded - Future Technology` | `XiyueYM.MiliraExpandedXY` | Yes (clears device throttling) |
| `SDS_CompatAdapter_CeleTechShuttle` | `天工模块化穿梭机 CeleTech Shuttle Extension` | `LongRange.CeleTech.ShuttleExtension` | No |
| `SDS_CompatAdapter_EquivalentExchange` | `ProjectE 等价交换` | `Natsuki.ProjectE` | No |
| `SDS_CompatAdapter_Versatran` | `VC载具框架 VersatranZeroOne` | `VersatranZeroOne.serval` | No |
| `SDS_CompatAdapter_MiliraGift` | `边缘米莉拉养成：与你相连之人` | `FunnyFlandre.milira.theoneentwinedwithyou` | No |

### B.1 `SDS_API` Methods Called by Each Adapter

This table is an adapter-level summary of each adapter's `SDS_API` entry points; §F.2 maps the same set of entry points by hook point. "Indirect" means forwarded through `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply`; direct calls can be verified with `grep SDS_API\.`, and indirect entries can be traced one more level via the utility class noted in parentheses.

| Adapter | `SDS_API` methods | Direct / indirect |
|---|---|---|
| PUAH | `IsProducedPendingConsumption`, `TryTakeDepositCandidate`, `IsDepositJob`, `TryMakeDepositJob`, `IsPawnPortOperational`, `TryMakeDepositJobFromInventory` | Direct |
| CE | `IsManualDepositAvailable`, `IsGridUsable`, `IsGridAvailable`, `TryGetWithdrawCell`, `TryFindWithdrawableKey`, `ClampTakeToAvailable`, `TryWithdraw`, `MarkProducedPendingConsumption`, `TryReturnVirtual` (`landOnFailure: false`), `IsMirror` | Direct |
| UFRecipes | `HasUsableBuildingEndpointWithin` (via `SDS_CompatEndpoint`); `IsGridAvailable`, `AppendPreservedThings`, `IsLivePreservedThing`, `IsBillIngredientAllowed`, `TryGetStockKey`, `IsStockForbidden`, `GetAvailableCount`, `TryWithdrawPreservedExact`, `TryReturnPreserved` (via `SDS_CompatBillSupply` / `SDS_CompatStock`) | Indirect |
| ManipulatorBeam | `IsDepositEndpoint`, `TryFindDepositEndpoint` | Direct |
| MiliraExpanded | `TryPlanBillSupplySlot`, `TryPlanAllowedDefSupply`, `NotifyBillSupplyDeposited`, `RequiresPreservedStorage`, `TryReturnPreserved`, `TryGetStockKey`, `TryReturnVirtual`; `HasAnyEndpoint` (via `SDS_CompatEndpoint`), `GetAvailableCountForDef`, `TryWithdrawVirtualByDef`, `TryWithdrawPreservedByDef` (via `SDS_CompatStock`) | Direct + indirect |
| CeleTechShuttle | `InjectTransportLoadCandidates` | Direct |
| EquivalentExchange | `IsDisplayThing`, `TryGetDisplayKey`, `GetAvailableCount`, `ClampTakeToAvailable`, `TryWithdraw`, `TryGetStockKey`, `TryReturnVirtual`, `IsMirror` | Direct |
| Versatran | `IsGridUsable`, `IsMirror`, `TryGetStockKey`, `ReadStock`, `GetAvailableCount`, `ClampTakeToAvailable`, `IsStockForbidden`, `AppendPreservedThings`, `IsLivePreservedThing`, `TryWithdraw`, `TryWithdrawPreservedExact`, `TryReturnVirtual`, `TryReturnPreserved` | Direct |
| MiliraGift | `IsMirror`, `IsGridUsable`, `HasAnyEndpoint`, `TryGetWithdrawCell`, `TryWithdrawFromMirror`, `TryReturnVirtual` | Direct (via `SDS_CompatMirrorSeam`) |

## C. Consumed Contracts Types

| Type | Purpose | Used by |
|---|---|---|
| `ISDSCompatAdapter` | Adapter contract (`Name` / `TargetPackageId` / `IsTargetLoaded` / `Apply`) | The 9 adapters, `SDS_CompatLoader` |
| `SDS_StockKey` | Ledger key; output of `TryGetStockKey` / `TryFindWithdrawableKey` | CE, UFRecipes, Milira, ProjectE, `SDS_CompatBillSupply` |
| `SDS_WithdrawMode` | Withdrawal delivery mode; this module only uses `Hand` / `Container` | CE, ProjectE, `SDS_CompatBillSupply` |
| `SDS_PreserveHooks` | Preserved-fact probe registry; the CE adapter registers `HasLoadedAmmoState` (W33) at `Apply` time | `SDS_CompatAdapter_CE` |
| `SDS_CompatGates` | Compat gate registry; the EE adapter registers the loading-manifest injection gate at `Apply` time, consumed before the Vanilla injection | `SDS_CompatAdapter_EquivalentExchange` |
| `SDS_ApiCaller` | Caller context; `FromPawn` / `FromFaction` | CE, ManipulatorBeam, ProjectE, `SDS_CompatStock`, `SDS_CompatBillSupply` |
| `SDS_ApiResultCode` | API-boundary failure codes; this module discards them via `out _` and branches only on the bool result | All methods that go through `SDS_API` |

`SDS_ApiResultCode` has no explicit type references in this module's sources (all `out _`); `SDS_ApiStockInfo` / `SDS_ReadVersions` / `SDS_SupplyFailure` are not directly consumed either (TBD: introduce them later if failure reasons need to be distinguished).

### C.1 `SDS_ApiResultCode` Semantics Summary

| Value | Meaning |
|---|---|
| `Ok` | Success |
| `InvalidArgument` | Invalid argument; `VirtualHandoff`, non-player faction, cross-map mirror, etc. |
| `GridUnavailable` | Ledger unavailable (G disabled / not `Attach`ed) |
| `NoEndpoint` / `EndpointUnavailable` | No candidate endpoint / the selected endpoint is unusable |
| `NotDepositable` | Thing cannot be deposited (category / filter / capacity / state) |
| `Forbidden` | Category or thing is forbidden |
| `NotOwnedByCaller` / `NotPlayerFaction` | Ownership / faction gate rejection |
| `NoStock` | Insufficient virtual stock |
| `UnresolvedDef` | Def cannot be resolved |
| `RebuildFailed` | Preserved / rebuild plan failed |
| `NotWithdrawable` | Thing cannot be withdrawn |
| `DepositFailed` | Deposit execution failed |
| `TransactionFailed` | Transaction failed |
| `InternalError` | Internal exception; already logged under `公共API/*异常`, the exception itself is not leaked |

Source of these definitions: ModuleInterfaces.en.md 1.1.1; `PublicAPI.en.md` §3.6 (`SDS_SupplyFailure` → `UnresolvedDef` / `Forbidden` / `NoStock` / `RebuildFailed`).

## D. Call Relationships

```text
SDS.Bootstrap.SDS_CompatLoader.Scan (reflectively enumerates loaded assemblies)
        │  target mod absent → skip; duplicate name → ErrorOnce + skip
        ▼
ISDSCompatAdapter.Apply (each adapter does new Harmony(HarmonyId))
        │  SDS_CompatTools.TryPatch → target mod methods / two vanilla methods
        ▼
SDS_CompatEndpoint / SDS_CompatStock / SDS_CompatBillSupply (optional)
        ▼
SDS_API (the five axes (See/Read/Find/Take/Store) + the adapter bridge) → SDS.Core (the sole implementation of ledger / transactions / rules)
```

- Bootstrap does not statically reference this assembly; it only recognizes Contracts' `ISDSCompatAdapter`.
- Adapters do not reference Core / Vanilla directly; `SDS_API` is the only cross-assembly write path.
- Target mods appear only in reflection strings and in the hook point parameters of `Apply`.

## E. Vanilla Support

> This module "does not patch vanilla": patches are applied only to third-party mod methods (or vanilla methods triggered by third-party methods); it shares vanilla infrastructure but adds no vanilla behavior. Only vanilla types and methods confirmable from source are listed; unconfirmable items are marked "(TBD)".

### E.1 Conclusion: This Module Does Not Patch Vanilla

- The 112 attribute patch classes and 29 registry entries are all in `SDS.Vanilla` (ModuleRules.en.md 7); this module has no `[HarmonyPatch]` classes; all 9 adapters use runtime `harmony.Patch`.
- The target methods of the 9 adapters are all third-party types. Only two hook points attach to vanilla methods, and both are installed by their adapters only when the target mod is loaded:

| Adapter | Vanilla target method | Installation precondition | Actual activation condition | Effect on vanilla behavior |
|---|---|---|---|---|
| `SDS_CompatAdapter_CE` | `Pawn_JobTracker.JobTrackerTick()` | CE loaded (`Resolve()` succeeds) | player faction + the symptom + `CurJobDef == JobDefOf.AttackStatic` + holding a CE magazine weapon with `CurMagCount == 0` + grid usable on this map | only refills ammo and assigns CE's own `ReloadWeapon` Job; the original method's semantics are unchanged |
| `SDS_CompatAdapter_EquivalentExchange` | `Dialog_LoadTransporters.TryAccept()` | ProjectE loaded | every `CompTransporter.parent.def.defName` in the manifest is `EE_TransmutationTable` / `EE_EmcInterface` | the Prefix swaps display things for true bodies and the Finalizer returns them; other loading dialogs are not touched at all |

- Apart from these two places, this module only "calls" vanilla methods (e.g. `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`); it does not replace vanilla return values, add vanilla types, or register vanilla Defs.
- The two vanilla hook points are not installed when the target mod is absent (`SDS_CompatLoader` does not call `Apply`), so there is zero impact for pure-vanilla players.

### E.2 Shared Vanilla Infrastructure

| Vanilla type / API | Purpose | Used by |
|---|---|---|
| `Harmony` / `HarmonyMethod` / `Priority` | Dynamic hook points | All 9 adapters (via `SDS_CompatTools.TryPatch`) |
| `Thing` / `ThingDef` / `ThingWithComps` / `ThingComp` | Things, comps, comp properties | PUAH, CE, ManipulatorBeam, Milira, MiliraGift, CeleTech, ProjectE |
| `ThingOwner` (`TryAdd` / `TryAddOrTransfer` / `Contains` / `Remove` / enumeration) | Vanilla containers and inventories | CE (`pawn.inventory.innerContainer`), UFRecipes (ingredient placement target) |
| `Job` / `JobDef` / `JobDefOf` / `JobMaker` / `TargetIndex` / `JobTag` | Job semantics and job assignment | PUAH, CE, Milira, MiliraGift |
| `Bill` / `Bill_Production` / `RecipeDef` / `IngredientCount` / `ThingCount` / `ThingFilter` | Recipes and bills | UFRecipes, Milira |
| `TransferableOneWay` / `CompTransporter` / `Dialog_LoadTransporters` | Loading manifests and the loading dialog | CeleTech, ProjectE |
| `IHaulDestination` / `ListerHaulables` / `Map.listerBuildings` / `MapComponent` | Haul destination discovery | ManipulatorBeam |
| `Building` / `Building_Turret` / `Map` / `IntVec3` / `Pawn` / `Faction` | World facts | CE, UFRecipes, ManipulatorBeam, Milira, ProjectE |
| `GenPlace.TryPlaceThing` / `GenClosest.ClosestThingReachable` / `GenTicks.TicksGame` | Placement, searching, current tick | CE, Milira, MiliraGift |
| `Map.reservationManager.OnlyReservationsForJobDef` | Read-only reservation query | Milira |

### E.3 Vanilla Types / Methods Touched by Each Adapter

| Adapter | Vanilla types / methods touched | Semantics | Read / write |
|---|---|---|---|
| PUAH | `Verse.AI.Job` (`ref __result`), `Pawn_JobTracker.curJob`, `Pawn_JobQueue.EnqueueFirst`, `JobTag.Misc`, `Job.TryMakePreToilReservations`, `Pawn.AllComps` | Hauling Jobs: when `job.def.defName == "HaulToInventory"`, recognized as PUAH's own carry loop | Read + replaces the Job return value |
| CE | `Pawn_JobTracker.JobTrackerTick` (hook point), `Pawn_JobTracker.pawn` (reflection read), `JobDefOf.AttackStatic`, `JobDefOf.TakeInventory`, `JobMaker.MakeJob`, `Pawn.equipment.Primary`, `Pawn.inventory.innerContainer`, `GenPlace.TryPlaceThing`, `GenClosest.ClosestThingReachable`, `ThingRequest.ForDef`, `PathEndMode.ClosestTouch`, `TraverseParms.For`, `Building_Turret`, `TargetIndex` | Ammo and reloading: materializes ammo onto the ground or into the inventory, then hands back to CE's own reload chain | Read + writes vanilla containers / map |
| UFRecipes | `Bill`, `RecipeDef` / `IngredientCount` (via `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`), `ThingCount`, `ThingOwner`, `Building.InteractionCell`, `Bill.ingredientSearchRadius` | Recipe ingredients: supplies preserved true bodies only after the third-party printer's own ingredient pull fails | Read + writes into vanilla containers |
| ManipulatorBeam | `Map.listerBuildings.allBuildingsColonist`, `Map.listerHaulables.ThingsPotentiallyNeedingHauling()`, `IHaulDestination.Accepts`, `MapComponent.map`, `Thing.Position.Fogged`, `Building` | Beam haul destination discovery: candidates are still vanilla ground things pending hauling; acceptance is decided by the endpoint itself | Read-only; delivery is delegated to the vanilla container contract |
| Milira | `Bill_Production`, `RecipeDef`, `IngredientCount`, `ThingFilter` (corpse filter), `ThingDef.IsCorpse`, `JobDefOf.DoBill`, `Map.reservationManager.OnlyReservationsForJobDef`, `ThingComp.props`, `Thing.parent`, `GenTicks.TicksGame` | Supply-refill facts for production / decomposition / life essence orders | Read-only; deposits go through Milira's own network |
| MiliraGift | `JobDriver.TryMakePreToilReservations` (hook point), `JobDriver.pawn` / `.job`, `Job.GetTarget` / `SetTarget`, `TargetIndex.A`, `GenPlace.TryPlaceThing`, `ThingWithComps.AllComps`, `CompIdealGift`'s `Crafted / Prepared / Sent / Note / Author / CrafterName / Year` (reflection read/write) | Gift giving: before reservations, swaps the mirror target for a true body on the endpoint access cell | Reads and writes comp fields; does not alter the third-party Job structure |
| CeleTech | `ThingWithComps`, `TransferableOneWay`, `CompTransporter` (the loading chain the manifest belongs to; the adapter only reads) | Shuttle loading manifest: only appends display thing rows | Writes only the manifest list (runtime) |
| ProjectE | `Dialog_LoadTransporters.TryAccept` (hook point), `Dialog_LoadTransporters.transferables` / `transporters` / `map` (reflection access), `TransferableOneWay.things` / `CountToTransfer` / `AdjustTo`, `CompTransporter.parent`, `Map`, `IntVec3` | Equivalent exchange: swaps in true bodies before transmutation, returns unconsumed ones, excludes mirrors from area scans | Reads and writes manifest rows (runtime, never persisted) |

Key points per adapter:

- PUAH: does not alter PUAH's Job structure; it only decides on the return values of `HasJobOnThing` / `JobOnThing`. After the unload dispatch point takes over, `return false` skips PUAH's own enqueueing.
- CE: whether "CE can find ammo" is determined with CE's own `FindBestAmmo` / `TurretRegistry`; on the vanilla side it only uses `GenPlace` to place things on the ground, `ThingOwner.TryAdd` to put ammo into inventories, and `GenClosest` as a fallback to find already-spawned physical things. Additionally, at `Apply` time it registers `SDS_ModCompat_CE.HasLoadedAmmoState` into `SDS_PreserveHooks` (W33): weapons with ammo in the magazine are routed by Core uniformly to the preserved channel; the probe only answers facts, never debits the ledger / assigns jobs.
- UFRecipes: supplies the preserved channel only; for virtual categories, the other mod's own `SlotGroup` / `IHaulSource` ingredient pull already sees the vault mirror, so the adapter does not add another supply path.
- ManipulatorBeam: the container type gate and scan table only relax "destination visibility"; delivery is still completed by the other mod's generic branch via `IHaulDestination.Accepts` + `ThingOwner.TryAdd`.
- Milira: `Map.reservationManager.OnlyReservationsForJobDef(..., JobDefOf.DoBill, true)` is only used to judge whether "ingredients are currently occupied by a bill"; deposits call Milira's `GameComponent_VirtualStorageNetwork.TryDeposit`, not the vanilla `ThingOwner`.
- CeleTech: does not write `CompTransporter.leftToLoad`, does not touch `WorkGiver_LoadTransporters` / `LoadTransportersJobUtility` / `HaulToTransporter`; loading still goes through the original vanilla chain (W50).
- ProjectE: the only place it writes vanilla structures is the per-row `things` of the transmutation dialog's `transferables` (replacing display things with true bodies and cleaning up empty rows); this is dialog runtime data and never enters the save.

### E.4 Judgment Criteria: Why These Two Places Do Not Count as "Patching Vanilla"

- Installation is constrained by third-party state: both hook points are installed by their adapters' `Apply`; `SDS_CompatLoader` does not call `Apply` when the target mod is not loaded, so in a pure-vanilla environment these two patches do not exist.
- Scope is constrained by third-party state: CE's postfix requires "holding a CE magazine weapon + CE empty-magazine state"; EE's prefix requires "every transporter in the manifest is an EE transmutation table / EMC interface"; if unmet, everything passes through untouched.
- The original methods' semantic contracts are unchanged: CE only supplements item sources and assigns CE's own Jobs; EE only swaps display things for true bodies and returns them on failure, without changing the original "commit consumes" logic.
- No vanilla types, Defs, WorkGivers, JobDrivers, or ThinkNodes are added; behavioral changes on the vanilla side are still borne solely by `SDS.Vanilla`'s 112 patch classes.

### E.5 Division of Labor with Vanilla Patches

| Item | Owner | Notes |
|---|---|---|
| 112 attribute patch classes + 29 registry entries | `SDS.Vanilla` | `SDS_PatchInstaller` scans only the Vanilla assembly |
| Third-party dynamic hook points | `SDS.ThirdModCompat` | Not counted in the 112; vary with whether target mods are present |
| Vanilla DoBill / hauling / loading dialog injections | `SDS.Vanilla` | e.g. `Patch_SDS_TransporterAddVirtual`, `Patch_SDS_PreservedBillIngredients` |
| ProjectE's loading-manifest injection gate method `AllowInjection` | `SDS.ThirdModCompat` | Defined in `SDS_ModCompat_EquivalentExchange`; no call site found in current sources (TBD) |

### E.6 Known Boundaries (TBD)

- Which vanilla type `MatterDecomposer_ThingComp` / `LifeEssenceExtractor_ThingComp` inherits from: the adapters probe only by string type names and do not reference their base classes directly (TBD).
- CE's `FindBestAmmo(Pawn, AmmoDef)` still goes through the vanilla `GenClosest` internally; the combined behavior with `SDS.Vanilla`'s distance projection needs in-game A/B verification (TBD).
- Adapting to signature changes of `Pawn_JobTracker.JobTrackerTick` / `Dialog_LoadTransporters.TryAccept` across RimWorld versions (TBD).

## F. Third-Party Support

> Describes each adapter's target mod, trigger methods, takeover approach, `SDS_API` entry points, and special notes. §B.1 is an adapter-level summary of the same facts (including the direct / indirect classification); this section keeps the hook-point-level mapping.

### F.1 Overview

packageIds follow the adapter constants and this table (the table once cited in ModuleResponsibilities.en.md 8 no longer exists there); Harmony IDs: see ModuleRules.en.md 8; adapter types, `Name` arguments, and `ISDSResettable`: see §B.

| Adapter | Target mod | packageId | Harmony ID | Hook points | Static cleanup |
|---|---|---|---|---:|---|
| `SDS_CompatAdapter_PUAH` | Pick Up And Haul | `Mehni.PickUpAndHaul` | `sizuru.sds.compat.puah` | 3 | No |
| `SDS_CompatAdapter_CE` | Combat Extended | `CETeam.CombatExtended` | `sizuru.sds.compat.ce` | 15 | Yes |
| `SDS_CompatAdapter_UFRecipes` | UF Light Industries | `KindSeal.UFLI` | `sizuru.sds.compat.ufrecipes` | 1 | No |
| `SDS_CompatAdapter_ManipulatorBeam` | Manipulator Beam Emitter | `natsuki.manipulatorbeam` | `sizuru.sds.compat.manipulatorbeam` | 3 | No |
| `SDS_CompatAdapter_MiliraExpanded` | Milira Expanded | `XiyueYM.MiliraExpandedXY` | `sizuru.sds.compat.miliraexpanded` | 6 | Yes |
| `SDS_CompatAdapter_CeleTechShuttle` | CeleTech Shuttle Extension | `LongRange.CeleTech.ShuttleExtension` | `sizuru.sds.compat.celetechshuttle` | 1 | No |
| `SDS_CompatAdapter_EquivalentExchange` | ProjectE | `Natsuki.ProjectE` | `sizuru.sds.compat.equivalentexchange` | 2 | No |
| `SDS_CompatAdapter_Versatran` | VersatranZeroOne | `VersatranZeroOne.serval` | `sizuru.sds.compat.versatran` | 3 | No |
| `SDS_CompatAdapter_MiliraGift` | Milira Raising: The One Entwined With You | `FunnyFlandre.milira.theoneentwinedwithyou` | `sizuru.sds.compat.miliragift` | 1 | No |

### F.2 Adapter by Adapter

#### F.2.1 Pick Up And Haul

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `PickUpAndHaul.WorkGiver_HaulToInventory.HasJobOnThing(Pawn,Thing,bool)` | Postfix writes `__result`: things in transit pending consumption → false; otherwise, when the original is false, asks the candidate gate | `IsProducedPendingConsumption` / `TryTakeDepositCandidate` | If the gate does not pass, PUAH's verdict is preserved bit for bit |
| `PickUpAndHaul.WorkGiver_HaulToInventory.JobOnThing(Pawn,Thing,bool)` | Postfix replaces the return value: on a hit, swaps in a multi-thing deposit Job | `IsDepositJob` / `TryMakeDepositJob` (`multi: true`) | Defers when `curJob.def.defName == "HaulToInventory"`; defers when it is already a deposit Job |
| `PickUpAndHaul.PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(Pawn,bool)` | Prefix: after assigning a Job and enqueueing it, `return false` skips PUAH's enqueue; otherwise `return true` | `IsPawnPortOperational` / `TryMakeDepositJobFromInventory` | If the reservation fails it must be handed back to PUAH; this unload must not be swallowed |

- Static caches: `hauledCompType` / `hauledSetGetter` are first-reflection results (process-level constants, not registered with `SDS_StaticState`); `new List<Thing>(set)` is only a read-only snapshot and is not written back to `CompHauledToInventory`.
- One write path: the withdrawal trip is taken over by this mod's deposit Job; the `EnqueueFirst` trip inside PUAH's haul driver is deliberately not taken over (source comment "Plan B").
- No double debiting: `HasJobOnThing` only writes the same-tick decision cache, `JobOnThing` consumes the same decision, and the ledger is still debited exactly once.

#### F.2.2 Combat Extended

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `WorkGiver_ReloadTurret.HasJobOnThing` / `.JobOnThing(Pawn,Thing,bool)` | HasJob passes candidates through; JobOn does a pre-refill plus a postfix fallback that directly builds the CE Job | `IsManualDepositAvailable` / `IsGridUsable` / `TryGetWithdrawCell` / `TryWithdraw` / `MarkProducedPendingConsumption` | Never interferes when CE can find ammo on the map |
| `WorkGiver_ReloadAutoLoader.HasJobOnThing` / `.JobOnThing(Pawn,Thing,bool)` | Same structure as the turret row | Same as above | The auto-loader hook is installed only when the type is resolvable |
| `JobGiver_ManTurretsNearSelfCE.FindAmmoForTurret` / `JobGiver_ManTurretsNearPointCE.FindAmmoForTurret(Pawn,Thing)` | Postfix swaps `__result`: places one real thing on the ground via `GenPlace` | `TryFindWithdrawableKey` / `TryWithdraw` | Refills player-faction pawns only, preventing third-party pawns from drawing on player stock |
| `JobGiver_CheckReload.GetPriority(Pawn)` / `.TryGiveJob(Pawn)` | GetPriority raises the priority; TryGiveJob does a pre-refill plus a postfix fallback | `IsManualDepositAvailable` / `TryFindWithdrawableKey` | The two must be patched as a pair, otherwise TryGiveJob is never called |
| `Pawn_JobTracker.JobTrackerTick()` | Postfix: when the magazine is empty, refills ammo and assigns CE's own `ReloadWeapon` | `IsManualDepositAvailable` / `IsGridUsable` | Only on symptom + `AttackStatic` + empty magazine; a 250-tick cooldown keyed by `thingIDNumber` |
| `JobGiver_DefenderReloadTurret.TryGiveJob(Pawn)` | Prefix refills ammo, then lets CE's original logic proceed | Same as above | Does not skip the original method |
| `CompAmmoUser.TryPickupAmmo()` / `CompAmmoUser.TryPrepareShot()` | Postfix / Prefix | `TryTakeForCe` (internally `TryWithdraw`) | Does not patch `CompInventory.AmmoCountOfDef`: a pure query would pollute the weapon panel |
| `JobGiverUtils_Reload.FindBestAmmo(Pawn, AmmoDef)` | Postfix: swaps mirrors for the real nearest ammo; returns null if none found | `IsMirror` | Read-only translation; no materialization at the decision stage |
| `JobDriver_ReloadTurret` / `JobDriver_ReloadAutoLoader.TryMakePreToilReservations(bool)` | Prefix: when `targetA` / `targetB` is a mirror, sets `__result = false` and skips | `IsMirror` | Rather skip this reload than duplicate items |

- Static caches: `checkCooldownUntil` / `priorityCacheUntil` / `priorityCacheValue` store absolute ticks keyed by `thingIDNumber`, registered with `ISDSResettable` via `SDS_CompatAdapter_CE.ResetStatics`; `pendingAmmo` / `pendingAmmoTarget` / `pendingAmmoDef` are one-shot value slots between the pre-refill leg and the fallback leg.
- Read-only snapshots: CE's `TurretRegistry` / `CompAmmoUser` / `CompInventory` members are all read via reflection; CE state is not written. The only write into CE is `CompInventory.UpdateInventory()`, so the other mod can see ammo that just entered the inventory.
- No double debiting: withdrawal happens only in `TryTakeForCe` (one clamp via `TryFindWithdrawableKey` + one `TryWithdraw`); the ground leg calls `MarkProducedPendingConsumption`, the inventory leg does not.

#### F.2.3 UF Light Industries

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `UFRecipes.AutoSequenceUtil.TryPullIngredientsFromCandidates(Bill,List<Thing>,Map,ThingOwner,out bool)` | Postfix: when the other mod returns false, supplies preserved ingredients; on success sets `__result = true` | Via `SDS_CompatBillSupply`: `AppendPreservedThings` / `IsLivePreservedThing` / `IsBillIngredientAllowed` / `TryGetStockKey` / `IsStockForbidden` / `GetAvailableCount` / `TryWithdrawPreservedExact` / `TryReturnPreserved` | Supplies the preserved channel only; virtual categories are left to the other mod's own ingredient pull |
| Endpoint precondition | Before calling, first asks whether a usable building endpoint exists within the radius | `SDS_CompatEndpoint.HasUsableBuildingEndpointWithin` | Radius semantics come from `bill.ingredientSearchRadius` |

- Does not write its own selection: reuses the vanilla `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`; the adapter only provides candidates and withdrawal.
- No `SplitOff` for preserved true bodies: only true bodies with `stackCount == 1` are accepted; if `ThingOwner.TryAddOrTransfer` fails, it first `Remove`s and then returns them to the grid.
- Candidate deduplication: things already in the target container and preserved true bodies are deduplicated with a `HashSet<Thing>` so the same thing is not counted twice.
- No double debiting: one `TryWithdrawPreservedExact`; if the whole batch cannot be met, everything is returned and false is returned — no partial results.

#### F.2.4 Manipulator Beam Emitter

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `ManipulatorBeam.BeamFeatureSettings.IsSpecialContainerTypeEnabled(Thing)` | Postfix: true if an endpoint exists | `IsDepositEndpoint` | Only relaxes the type gate; does not touch the toggle / occupancy / enqueue decisions |
| `ManipulatorBeam.MB_BeamScanCacheMapComponent.SpecialContainers` (getter) | Postfix: appends endpoints among this map's colonist buildings | `IsDepositEndpoint` | Idempotent; the other mod rebuilds the list every 30 ticks, appended entries expire with it and are re-added |
| `ManipulatorBeam.BeamSpecialTransferUtility.TryFindSpecialContainerTransfer(op, containerThing, haulValidator, excludedDestinations, out transfer)` | Prefix: constructs a `BeamTransfer(Thing, IntVec3, Thing, int)` into `__args[4]`, also writes `__result` and skips the original method | `IsDepositEndpoint` + endpoint `IHaulDestination.Accepts` | Index 4 is hardcoded; a signature mismatch is reported as a missing hook point; the beam already has a fixed destination, so the global nearest result from "Find" is not compared against this endpoint |

- Static cache: `transferCtor` is cached once after the other mod's types are detected.
- Read-only snapshot: the candidate set uses the vanilla `ListerHaulables.ThingsPotentiallyNeedingHauling()`; acceptance is decided by the endpoint's own `IHaulDestination.Accepts` (this mod's hard deposit gates: globally available / endpoint available / filter / category capacity / source exclusion flag).
- Zero takeover of delivery: the other mod's generic branch is the vanilla container contract; the adapter does not call the deposit facade, so there is no second ledger-entry path.

#### F.2.5 Milira Expanded

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `CompVirtualProductionDevice.CompTick()` | Prefix: 30-tick throttling; refills the bills in the linked ledger | `TryPlanBillSupplySlot` / `NotifyBillSupplyDeposited` | Returns immediately when `Operational` is false |
| `GameComponent_VirtualStorageNetwork.TryProcessAutomatedRecipeCycle(CompVirtualProductionDevice,Bill_Production,int)` | Prefix: bill supply refill / order refill by id | `TryPlanBillSupplySlot` + `SDS_CompatStock` | Does not skip the original method |
| `GameComponent_VirtualStorageNetwork.TryProcessManualRecomposeBill(CompVirtualProductionDevice,Bill_Production)` | Prefix: bill supply refill | Same as above | Fills only the shortfall; the other mod's logic is unchanged |
| `GameComponent_VirtualStorageNetwork.TryProcessRecomposeOrder(CompVirtualProductionDevice,int)` | Prefix: order supply refill | `TryPlanAllowedDefSupply` | The allowed-def list + corpse filter are provided by the other mod |
| `GameComponent_VirtualStorageNetwork.TryProcessDecomposeOrder(MatterDecomposer_ThingComp)` | Prefix: decomposition order supply refill | `TryPlanAllowedDefSupply` | No `recipeDef`; takes the def-list branch |
| `GameComponent_VirtualStorageNetwork.TryProcessLifeEssenceOrder(LifeEssenceExtractor_ThingComp)` | Prefix: life essence order supply refill | `TryPlanAllowedDefSupply` | Same as above |

- Own stock first: the other network's available count and `CanStoreAnyDef` / `CanDepositDef` / `IngredientReserveFor` are all read via reflection and passed in as `ownCount` / `extraAllows` / `reserveProvider` facts.
- Static caches: `nextTickByDevice` is registered with `ISDSResettable`; `canStoreCache` is keyed by `ThingDef` and its values depend on the W33 toggle — `CanStoreDef` self-invalidates based on the toggle, and `Reset()` clears the cache too.
- Return: on deposit failure or partial failure, returns per channel — preserved via `TryReturnPreserved`, virtual via `TryReturnVirtual`.
- Does not hook the manual `WorkGiver_DoBill` path (handled by `SDS.Vanilla`'s DoBill bridge) and does not overlap Core's supply refill rules.

#### F.2.6 CeleTech Shuttle Extension

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `CeleTech.ShuttleExtension.ModularShuttle.Cargo.CargoTransferFilter.BuildCargoTransferables` | Postfix: appends display things to the manifest | `InjectTransportLoadCandidates` (W50 internal bridge) | Read-only projection: does not write `leftToLoad`, no job assignment, no debiting |

- Uses `Building_ModularShuttle` as a marker to distinguish "not installed" from "installed but internal types renamed"; when `BuildCargoTransferables` cannot be found, only a missing hook point is logged.
- When the host has not started loading yet, the vanilla `CompTransporter.TransportersInGroup` returns null (`leftToLoad` not built yet) — this is **not a hook point failure**: the internal bridge keeps injecting read-only projection rows under the "fall back to the host shuttle itself" rule, consistent with the other mod's `ResolveTransporters` behavior of "falling back to the first transporter when the group is empty"; otherwise it manifests as "the hyperdimensional stock is invisible unless a pawn enters the cockpit" (diagnosed in-game, 2026-11-13).
- No static caches; withdrawal / reservation / bay loading / saving are still handled by the existing vanilla hook points and the sole executor.

#### F.2.7 ProjectE

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `Dialog_LoadTransporters.TryAccept()` | Prefix (`Priority.High`) + Finalizer: swaps display things for true bodies; returns unconsumed ones | `IsDisplayThing` / `TryGetDisplayKey` / `GetAvailableCount` / `TryWithdraw` / `TryGetStockKey` / `TryReturnVirtual` | Effective only in the transmutation table / EMC interface dialog; preserved display things are removed outright |
| `EquivalentExchange.TransmutationUtility.GetTransmutableThingsInArea(Map,IntVec3,int)` | Postfix: excludes mirrors | `IsMirror` | Mirrors represent container contents and were never on the cell to begin with |

- `Priority.High` is mandatory: the other mod's own Prefix takes over `TryAccept` and skips the vanilla one; a lower-priority Prefix would not run after it returns false.
- Double count clamping: first takes `CountToTransfer − the row's real unit count`, then clamps once more with `GetAvailableCount` + `ClampTakeToAvailable` (the sole W56 implementation); if it cannot be withdrawn, the display thing is removed (fail-closed).
- The Finalizer returns only true bodies that are "not `Spawned` and have `holdingOwner == null`"; anything else is treated as already destroyed by the other mod to avoid double bookkeeping.
- `hookReady = acceptPatched && transferablesField != null && transportersField != null`; when not ready it provides an `AllowInjection(Dialog_LoadTransporters)` gate, registered by the adapter into `SDS_CompatGates` and consumed before the Vanilla injection (allowed when unregistered, fail-closed on exception).

#### F.2.8 VersatranZeroOne

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `Window_VersatranCargo.ExecuteInstantLoad()` | Prefix: pulls mirror / preserved rows out of the vanilla loop; virtual goes through `TryWithdraw`, preserved through `TryWithdrawPreservedExact`, true bodies handed to the vehicle's `TryAddOrTransferThingToCargoStrict`; Postfix refreshes | `IsGridUsable` / `IsMirror` / `TryGetStockKey` / `ReadStock` / `IsStockForbidden` / `AppendPreservedThings` / `IsLivePreservedThing` / `TryWithdraw` / `TryWithdrawPreservedExact` / `TryReturnVirtual` / `TryReturnPreserved` | Real map items still go through the vehicle's original logic; hyperdimensional rows have exactly one API write path |
| `Window_VersatranCargo.RefreshItems()` | Postfix: mirror rows get `MaxCount = available`; preserved true body rows are appended | `ReadStock` / `GetAvailableCount` / `AppendPreservedThings` / `IsStockForbidden` | Read-only projection; preserved rows accept only true bodies with `stackCount == 1`; no second counting system is built |
| `CompVersatranTransporter.CanThingBeStoredInCargo(Thing)` | Prefix: returns false for mirrors / live preserved true bodies | `IsMirror` / `IsLivePreservedThing` | Gatekeeping; the adapter's own withdrawal products pass through normally |

- No new public surface / save fields / rule IDs; virtual rows reuse mirrors and preserved rows reuse true bodies — both are representations the framework already has.
- Does not rely on `Thing.SplitOff` / `ThingOwner` fallbacks; both virtual and preserved withdrawal go through the five axes.
- Preserved rows' `CountToTransfer` is reset to zero on every `RefreshItems` rebuild (a UI detail); virtual row counts reuse the vehicle's own `RestoreTransferStates`.

#### F.2.9 Milira Raising: The One Entwined With You

| Trigger method | Takeover | SDS_API entry points | Special notes |
|---|---|---|---|
| `MiliraTheOneEntwinedWithYou.Gifts.JobDriver_IdealReceiveGift.TryMakePreToilReservations(bool)` | Prefix: when `targetA` is a mirror, calls `SDS_CompatMirrorSeam.TryMaterializeForReservation` to materialize 1 thing on the nearest reachable endpoint access cell, copy the seven `CompIdealGift` fields, and rewrite `targetA`; returns true whether or not it takes over, so the vanilla reservation still executes | `IsMirror` / `IsGridUsable` / `HasAnyEndpoint` / `TryGetWithdrawCell` / `TryWithdrawFromMirror` / `TryReturnVirtual` (all in `SDS_CompatMirrorSeam`) | Does not touch W54 / Core; when fields are missing it does not hook and reports the reason; if cancelled midway, the true body remains on the ground — not duplicated, not lost |

- Static caches: the `CompIdealGift` type and the 7 `FieldInfo`s are resolved once at `TryApply` time; they are process-level constants and are not registered with `SDS_StaticState`.
- One write path: withdrawal goes only through `TryWithdrawFromMirror`; the adapter never touches the ledger, never decides positions, and never picks endpoints.
- No takeover: non-mirror / no endpoint / no stock / not player / placement failure are all handed back to the vanilla reservation and its failure conditions.

### F.3 Steps to Onboard a New Third-Party Mod

1. Implement `ISDSCompatAdapter` (`SDS.Contracts`; signatures in ModuleInterfaces.en.md 1.1.2): `Name` (consistent with the Harmony ID), `TargetPackageId`, `IsTargetLoaded` (probes by type name only), `Apply()`. `Apply` must maintain its own `applied` idempotency flag and do `new Harmony(HarmonyId)`.
2. Probe types with `SDS_CompatTools`: `Method` / `Property` / `PropertyGetter` / `Field`, caching results after one `Resolve()`; all hook points go through `SDS_CompatTools.TryPatch` — building your own `HarmonyMethod` is forbidden, and so is hardcoding argument indexes without validating signatures.
3. Read and write only via `SDS_API`: the five axes (See/Read/Find/Take/Store) + the adapter bridge (ModuleInterfaces.en.md 5). Do not compute shortfalls, pick endpoints, debit the ledger, or create transactions; when `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply` can be reused, do not write new helpers, and new helpers must themselves contain no rules.
4. If there are cross-save static caches keyed by `thingIDNumber`, implement `ISDSResettable` and call `SDS_StaticState.Register(this)` after `Apply` succeeds (ModuleInterfaces.en.md 2.2); process-level reflection constants are not registered.
5. Register with the report hub: `SDS_CompatReportHub.Register(name, packageId, detected, report, note?, reason?)`; record each hook point with `SDS_CompatPatchReport.Applied` / `Missing`. All failure paths use only `ErrorOnce` / `Warn`; `SDS_Diag.Fail` is forbidden.
6. Run the checks: `SDS.Tests/Compat/CompatAdapterTests.cs` (exactly 9 adapters present, unique and contract-consistent `Name` / packageId / HarmonyId, no Apply when the target is missing, CE and Milira declare cleanup, no `SDS_Diag.Fail` in sources); `Tools/CheckCompatSurface.ps1` (entry point ownership / call chains / compat-layer call graph); `Tools/CheckPatchInventory.ps1` (the 112 baseline unchanged). Also register this file's §B, §F.1, and §F.2, plus the PatchInventory.en.md dynamic section, in sync.

### F.4 Registration and Verification

| Location | Content |
|---|---|
| `SDS_CompatReportHub` (`SDS.Diag`) | Adapter-level reports: name, packageId, whether detected, hook points Applied / Missing, notes |
| `SDS_StaticState` (`SDS.Diag`) | 1 entry each for CE and Milira; registered after `Apply` succeeds, cleared on `ResetAll` |
| `CompatAdapterTests` | Identity and uniqueness, no Apply when the target is missing, static cleanup declarations, no `SDS_Diag.Fail` calls in sources, `SDS_API` bridge forwarding facts |
| `CheckCompatSurface.ps1` | Every `SDS_ModCompat_*.cs` has entry lines; facade methods are classifiable; must not reference Core / Vanilla |
| `CheckPatchInventory.ps1` | Third-party hook points are not counted in the Vanilla 112; only maintains the dynamic hook point whitelist |

## Sources

- Rules: `Framework/ModuleRules.en.md` 6.6 / 7 / 8; `ModuleResponsibilities.en.md` 2; `ModuleInterfaces.en.md` 1.1.1 / 1.1.2 / 2.2 / 5 / 6.
- Code: all sources under `超维存储/Source/SDS.ThirdModCompat/` (`SDS_CompatServices.cs`, the 9 `SDS_CompatAdapter_*.cs`, `SDS_ModCompat_*.cs`); `SDS.Bootstrap/SDS_CompatLoader.cs`.
- Tests and scripts: `SDS.Tests/Compat/CompatAdapterTests.cs`; `超维存储/Source/Tools/Check*.ps1`.
- Old-implementation reference: PublicAPI.en.md (already migrated into this framework) 2·6 / §3.6 / §4.8 / §4.10 / §4.11; the `TryApply` hook point definitions in `SDS_ModCompat_*.cs`.
