# SuperDimensionalStorage · Public API Contract

> Positioning: this document is the sole authority on the **public API contract** (the five axes (See/Read/Find/Take/Store) + adapter specification + derived registries).
> This document **does not define** the concrete rules for See / Read / Find / Take / Store; rule IDs and normative sentences are in RuleIndex.en.md, and rule bodies are in its "Authoritative Document" column.
>
> Relationships:
> - **Vanilla contracts first**: `IHaulDestination` / `IThingHolder` / `IHaulEnroute` / `ListerThings` / Region / `ThingOwner` remain the main path;
> - **The public API is a facade**: it only forwards to the existing single implementation and adds no rules;
> - **Adapters translate**: they only do inbound/outbound conversion — they never decide destinations, never debit the ledger, and never change third-party work logic;
> - **Goal**: make third parties treat this mod's stock as a vanilla peer, instead of treating this mod as a special system.
>
> **Implementation status (2026-11-16)**: the five-axis facade and all 9 adapters are in place; `SDS_API.Version = 4`. For the gaps and boundaries of the two thin adapters (Manipulator Beam Emitter / CeleTech Shuttle), see [SDS.ThirdModCompat/Interfaces.en.md](SDS.ThirdModCompat/Interfaces.en.md).

---

## 1. General rules

1. **A facade, not a second implementation**
   Every public API method must land on the single implementation pointed to by the corresponding capability document; the API itself must not write its own sorting, filtering, capacity, priority, transaction, or ledger-debit logic.

2. **Vanilla-recognizable**
   Countable / takeable stock representations must be vanilla-recognizable `Thing`s / existing proxies (`SDS_Proxy_*`); read-only scalar snapshots (such as `SDS_ApiStockInfo`) and opaque handles may exist, but must not replace `Thing` in vanilla enumeration, reservations, or `ThingOwner` operations.

3. **One representation**
   The same piece of stock may have only one visible representation in the same context (mirror / projection / real materialization); the facade must not create a second count or a second takeable object.

4. **Adapters only translate**
   Adapters may translate third-party events into facade calls and translate facade results back into the shape third parties expect; they must not copy rules from inside the facade.

5. **Vanilla contracts first**
   Whatever can be solved through `IHaulDestination` / `ListerThings` / Region / `ThingOwner` does not go through the public API; the public API only covers scenarios "the vanilla contracts cannot reach" (non-Pawn, non-Job, non-standard containers).

6. **Non-goals**
   The public API is not a second stock system, nor a second hauling framework; it does not provide the full scheduling of "find source + reserve + carry + re-select en route + batch transactions".

---

## 2. The five-axis facade

| Axis | Facade responsibility (implemented subset) | Rule authority | Return shape |
|---|---|---|---|
| **See** | the `listerThings` per-def view (including mirrors/proxies); holder trees / ThingGrid / Region still go through the vanilla APIs | RuleIndex.en.md S-series | read-only `Thing` list |
| **Read** | stock availability snapshot (total/available/disabled/failed) + read-only versions; candidate projection/reachability/filtering/sorting remain internal | RuleIndex.en.md R-series | `SDS_ApiStockInfo` + `SDS_ReadVersions` |
| **Find** | non-Pawn deposit endpoint selection, Pawn pickup-point selection; item search still goes through vanilla `GenClosest` | RuleIndex.en.md S-series | `Thing` + failure reason |
| **Take** | transaction-level materialization; returns a real `Thing`, delivery is completed by the caller | RuleIndex.en.md W-series | `List<Thing>` / `Thing` + failure reason |
| **Store** | single-item atomic ledger deposit; ownership/faction/forbidden/map gates sit at the API boundary | RuleIndex.en.md W-series | actually accepted amount + failure reason |

> The facade exposes only "entry + result"; concrete filtering, sorting, reservations, and failure policies follow the authoritative documents pointed to by RuleIndex.en.md and are not restated in this file.

### 2.5 Implemented five-axis methods

| Axis | Method | Description |
|---|---|---|
| Version | `SDS_API.Version` | currently `4`; third-party soft-dependency probing |
| See | `SDS_API.VisibleThingsOfDef(Map, ThingDef)` | returns the `Thing`s visible to vanilla enumeration on that map (including mirrors/proxies); read-only |
| Read | `SDS_API.ReadStock(SDS_StockKey)` / `ReadStockFor(Thing)` | total / available / disabled / supply failure reasons; read-only; results carry `SDS_ReadVersions` |
| Read | `SDS_API.CaptureReadVersions()` | captures a read-only version snapshot so callers can detect cache invalidation |
| Find | `SDS_API.TryFindDepositEndpoint(...)` | non-Pawn deposit endpoint selection; reuses the same Gate + Ranker |
| Find | `SDS_API.TryFindWithdrawPoint(Pawn, ...)` | Pawn pickup-point selection (own port / nearest endpoint); read-only |
| Store | `SDS_API.TryDepositNow(...)` / `TryDepositAt(...)` | single-item atomic ledger deposit; player faction + same map + ownership/forbidden gates |
| Take | `SDS_API.TryWithdraw(...)` | transaction-level materialization; returns a real `Thing`, delivery is completed by the caller |
| Take | `SDS_API.TryWithdrawFromMirror(..., SDS_ApiCaller, ...)` | withdraws a real materialized result from a mirror; player faction + same map |
| Take | `SDS_API.TryWithdrawPreservedExact(..., SDS_ApiCaller, ...)` | exact true-body withdrawal through the preserved channel; player faction |
| Type | `SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` | caller context / structured result codes / read-only stock snapshot (with `result`, `versions`) |

> When G is off (no usable core in the world), `ReadStock` returns `GridUnavailable` with `available = 0` and `failure = Insufficient`, while `total` is still reported per the ledger (R17).

**Version history (contract impact only)**

- Version 2 (2026-10-27): removed the never-adopted `HasAvailable` / `GetAvailableCount(ThingDef)` / `RegisterWarehouseContainer`; `ReadStock` returns `GridUnavailable` when G is off (R17).
- Version 3 (2026-10-28): the 5 public helpers exited the five-axis contract (`GetAvailableCount` / `GetPreservedAvailableCount` / `IsMirror` / `TryGetDisplayKey` / `TryGetWithdrawCell`).
- Version 4 (2026-11-12): the adapter bridge moved to public methods on `SDS_API` when ThirdModCompat was consolidated into a single assembly; the bridge surface is not part of the five-axis contract; registration is in ModuleInterfaces.en.md §5.

### 2.6 Adapter bridge

> `SDS_API_Adapters.cs` is a `partial` file of the same `SDS_API`, not a second API.
> Bridge methods are public (there is no `InternalsVisibleTo` across assemblies) but are **not part of the five-axis contract** and are not covered by the `SDS_API.Version` promise; the public caller overload of `TryWithdraw` lives in `SDS_API.cs`.

#### 2.6.1 Adapter bridge

> The sole registry for the bridge surface is ModuleInterfaces.en.md §5 (`CheckCompatSurface.ps1` reads method names from there); this section does not repeat the method list.

> `TryWithdrawVirtualByDef` / `TryWithdrawPreservedByDef` only compose the same transaction primitive key by key, adding no priority / filtering / debit rules.

> **W33 (2026-11-04)**: the channel results of `RequiresPreservedStorage` / `ReadStockFor(Thing)` vary with the
> "force-virtualize weapons/apparel" toggle (ordinary weapons/apparel may return virtual keys); the query still only forwards
> to `SDS_DepositRules`, adding no second rule set. It belongs to the adapter bridge and carries no external promise; adapters must follow the channel indicated by the query result.

> **W35 (2026-11-07)**: the **return value set of `RequiresPreservedStorage(def)` expands** (signature unchanged) —
> items where `ThingDef.IsStudiable` is true always return "preserved", and that criterion consumes only vanilla capability bits, listing no mods / defNames / components.
> The query still only forwards to `SDS_DepositRules`, adding no second rule set; callers must follow the channel indicated by the result and must not cache old results for reuse across versions.

#### 2.6.2 Public helpers outside the five axes

| Method | Description |
|---|---|
| `TryWithdraw(key, count, mode, SDS_ApiCaller, ...)` | non-Pawn / adapter overload; player faction + current map (when there is no Pawn); located in `SDS_API.cs` |

> Bridge methods (including the original 5 public helpers) only forward to the existing single implementation; they add no stock representations, sorting, or debit rules; third parties should use the five-axis entries of §2.5.

### 2.7 Public surface inventory (derived)

> The sole authority remains §2.5 / §2.6; this table registers only the five-axis contract's "public member ↔ category ↔ caller ↔ adoption evidence" for `CheckCompatSurface.ps1` reconciliation.
> Bridge methods (public but not part of the five-axis contract) are registered in ModuleInterfaces.en.md §5 and are not in this table; public types (`SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` / `SDS_ReadVersions` / `SDS_StockKey` / `SDS_WithdrawMode`) are manually reviewed per §2.5.

| Member | Category | Doc section | In-mod callers | External adoption evidence |
|---|---|---|---|---|
| `SDS_API.Version` | Contract | 2.5 | — (third-party probing) | pending |
| `SDS_API.VisibleThingsOfDef` | Contract | 2.5 | none | pending |
| `SDS_API.ReadStock` | Contract | 2.5 | none | pending |
| `SDS_API.ReadStockFor` | Contract | 2.5 | none | pending |
| `SDS_API.CaptureReadVersions` | Contract | 2.5 | none | pending |
| `SDS_API.TryFindDepositEndpoint` | Contract | 2.5 | none (kept as an external contract; the Manipulator Beam has a fixed destination and judges via the endpoint's `Accepts`) | pending |
| `SDS_API.TryFindWithdrawPoint` | Contract | 2.5 | none | pending |
| `SDS_API.TryDepositNow` | Contract | 2.5 | none | pending |
| `SDS_API.TryDepositAt` | Contract | 2.5 | none | pending |
| `SDS_API.TryWithdraw` | Contract | 2.5 / 2.6.2 | `SDS_ModCompat_CE`, `SDS_ModCompat_EquivalentExchange` | pending (two overloads merged into one row) |
| `SDS_API.TryWithdrawFromMirror` | Contract | 2.5 | none | pending |
| `SDS_API.TryWithdrawPreservedExact` | Contract | 2.5 | `SDS_CompatServices` | pending |

---

## 3. Contract rules

### 3.1 Thin facade

- Each API method may call only one corresponding single service; a second workflow must not be composed at the API layer.
- The only permitted composition is "necessary steps within the same transaction" (for example, depositing to the ledger immediately after selecting an endpoint), and it must reuse the same set of gates and sorting.

### 3.2 Vanilla-recognizable

- Objects returned externally must be `Thing`s / proxies that third parties can use in the vanilla way; stock entries that "only SDS understands" must not be returned.
- If a handle must be returned, it may only be used to call the SDS API again; it cannot replace a `Thing` in vanilla enumeration, reservations, or `ThingOwner` operations.

### 3.3 One representation

- The same piece of stock may have only one visible representation in the same context; representations returned by the facade must match the mirror visibility criteria (see [SDS.Vanilla/Rules.en.md](SDS.Vanilla/Rules.en.md) §11).
- "One copy in the mirror + one copy in the API result" must never both be countable or takeable by third parties at the same time.
- Unresolved entries, category-disabled entries, and invisible entries must not appear among candidates through the API.

### 3.4 Caller context

- Every API that mutates state or involves reachability must carry caller context: `Pawn` / `Faction` / `claimOwner` (building or mech).
- APIs that mutate state or involve reservations serve the **player faction** only; read-only queries are unrestricted. `TryFindWithdrawPoint` also requires the player faction (it is normally used for subsequent pickup).
- Callers without a Pawn must explicitly declare "non-Pawn mode"; that mode has no own port, no AllowedArea, and no Pawn reservations; distance is computed only from the reference cell.
- Deposits require **caller, physical item, and endpoint on the same map**; the endpoint's faction must be null or equal to the caller's faction.
- Mirror withdrawal: with a Pawn, the Pawn and the mirror must be on the same map; without a Pawn, the mirror must be on the current map (to guarantee the rollback map is correct).
- Preserved exact withdrawal: with a Pawn, its map is used for rollback; without a Pawn, the current map is used.
- Ownership / faction / forbidden checks are completed by the facade; they cannot rely only on the underlying `grid.TryDeposit` / `TryWithdraw`.
  Current implementation: `thing.Faction` and the root holder's faction must equal the caller's; ownerless items pass; forbidden items are rejected outright; corpses are judged by the physical item's faction without chasing `InnerPawn`.
  This is an API-boundary rule and does not reuse `HaulAIUtility.PawnCanAutomaticallyHaulFast`; when either place's criteria change, both must be checked in sync.

### 3.5 Transaction postconditions

- **Take**: on success, the item has completed its ledger debit and been materialized into a real `Thing`, and **delivery is completed by the caller**; on failure, the ledger and the rollbackable physical item return to their pre-call state.
- **Store**: on success, the physical item has been consumed (destroyed on the virtual channel / handed over on the preserved channel); on failure, the physical item stays with the caller intact — never SplitOff in half.
- `Thing` references held by the caller may become invalid after success; the contract must state clearly how "reference invalidation" and "re-resolution" work.

### 3.6 Failure codes

- State-changing facade methods return `SDS_ApiResultCode`; read-only snapshots (`SDS_ApiStockInfo`) use the `result` field; the list-shaped read-only call (`VisibleThingsOfDef`) returns an empty set on exception and records one diagnostic.
- `SDS_ApiResultCode` is an **API boundary code**; it corresponds to internal verdicts (`SDS_SupplyFailure`, endpoint-stage conclusions) but does not expose internal enums directly:
  - `SDS_SupplyFailure` → `UnresolvedDef` / `Forbidden` / `NoStock` / `RebuildFailed` (`RebuildFailed` is a reserved mapping with no current producer);
  - endpoint stage → `EndpointUnavailable` / `NotDepositable` / `DepositFailed`.
- Exceptions uniformly return `InternalError` (or an empty set / default) and record one `公共API/*异常` (`SDS_Diag.ErrorOnce`); exceptions are never leaked to third parties.
- Normal rejections (unreachable / out of stock / filter-rejected / capacity full / endpoint offline / non-player faction / cross-map) write no logs.

### 3.7 Non-Pawn mode

- Non-Pawn mode is a **parameter branch** of the same resolver `TryFindBestCore` (`actor == null`), not a second resolver:
  - `actor == null`;
  - `requireReach == false`;
  - `allowSelf == false`;
  - distance is computed from the `reference` cell;
  - priority / filtering / capacity still go through the same set of gates and sorting.
- Endpoint enumeration uses the same registry (only building endpoints are taken; Pawn ports are not).
- `SDS_API` additionally passes in a faction filter; this belongs to the API boundary and does not change Ranker rules.

### 3.8 Versioning and dependencies

- Provide `SDS_API.Version` or an equivalent capability probe; third parties should integrate via a soft dependency (reflection probing).
- Adding or modifying facade methods must update this document and note the compatibility impact.
- **E5 (2026-10-27)**: **a Version change is a compatibility declaration in itself; without external adoption you may break directly; with adoption, keep thin forwarding for at least one major version.**
  - "External adoption" is judged by: at least one public third-party reference / compat adapter / user report exists; without evidence, it counts as no adoption.
  - Thin forwarding only translates and must not copy rules (Principle 3); the old entry must internally call the current single implementation.
  - Any Version change must note its compatibility impact in §2.5 (see the Version 2 record below).
- **Version 2 (2026-10-27) compatibility impact**: removed three old entries that this mod did not use and for which no third-party adoption was recorded; the five-axis facade signatures are unchanged; `ReadStock` now returns `GridUnavailable` with `available = 0` when G is off (no usable core), while `total` is still reported per the ledger (R17).
- **Version 3 / 4 compatibility impact**: the original 5 public helpers exited the five-axis contract and, in Version 4, stay public as the adapter bridge (serving only this mod's compat layer); the five-axis public signatures are unchanged.

### 3.9 Threading and lifetime

- All APIs are called only on the RimWorld main thread, in in-game tick context.
- Returned temporary views / handles are "valid only for the current call" by default, unless explicitly marked as holdable across ticks.

---

## 4. Adapter specification

### 4.1 Inbound / outbound

- **Inbound**: third-party queries / actions / events → facade calls.
- **Outbound**: facade results / failure codes → the shape third parties expect (`Thing`, counts, Jobs, fallback reasons).

### 4.2 Prohibitions

Adapters must not:

1. decide destination priority, filtering, capacity, or tie-breaking on their own;
2. maintain their own stock cache or shadow counts;
3. rewrite third-party Job / Toil work logic;
4. bypass the facade and call internal paths of `SDS_GameComponent_Grid` / `SDS_EndpointHandle` directly;
5. walk both the "facade" and the "SDS Harmony patch" routes at the same time.

### 4.3 Dual triggering

- A single third-party entry may have only one takeover path: either it calls the facade, or the compat layer translates; both must never be active at the same time. **E7**: read-only projections may coexist with adapter translation, but there must be no second write path for debits / job assignment / rollback.
- Facade calls must carry an idempotency marker or caller context to avoid duplicate ledger deposits / duplicate job assignment.

### 4.4 Non-cooperative mods

- For third parties that do not call the facade, keep going through "vanilla contracts + recorded boundaries"; do not add destination special cases in the core for a single mod.
- If compatibility is needed, prefer an independent, toggleable thin adapter; adapters only translate and do not copy rules.
- **No polling reconciliation**: never build scheduled scan / comparison channels for artifacts created by third-party mods themselves — aligning through polling would form a second debit channel; only explicit facade calls and registered hooks (§4.8) are recognized.

### 4.5 The correct definition of "everything goes through the public API"

- It means: **all adapters' business rules converge onto the public API**;
- It does not mean: abolishing adapters, forcing third parties to reroute, or letting the public API replace the vanilla contracts.
- **Layering (2026-10-28 step 4)**: external entries (patches / UI / JobDrivers / third-party adapters) go only through this document's public facade or the adapter bridge in ModuleInterfaces.en.md §5; the service layer (`SDS_Trade` / `SDS_CaravanTransport` / `SDS_Preserved*`) may call the single executor directly but must not sort / debit / select endpoints on its own.
- **Compat layer boundary (2026-10-28)**: `SDS_ModCompat*` may reference only `SDS_API`, the public types of `SDS.Contracts` (`SDS_StockKey` / `SDS_WithdrawMode` / `SDS_ApiCaller` / `SDS_ApiResultCode` / `SDS_ApiStockInfo` / `SDS_ReadVersions` / `SDS_EETransmuteState`), the `SDS_Compat*` infrastructure itself, and `SDS_Diag`; it must not reference internal implementations such as `SDS_GameComponent_Grid` / `SDS_DepositService` / `SDS_WithdrawExecutor` / `SDS_MirrorRegistry` / `SDS_ChipManualDeposit`. Verification: `CheckPatchInventory.ps1`'s `[兼容直调]` (allowlist) + `CheckCompatSurface.ps1` (§2.7 public surface / §4.8 entry ownership / call graph). **E6**: this boundary is an engineering constraint, not a gameplay rule.

### 4.6 Extended registration for use-bills (goal, not implemented)

- Registering provider type names / def predicates is an entry extension that is not yet implemented; there is currently no public method.
- If implemented, it must uniformly go through the versioned `SDS_API` entries defined in this document; exclusion tables must not be privately defined inside adapters.

### 4.7 File ownership (mandatory)

- **All third-party mod adapters live only in `Source/SDS.ThirdModCompat/SDS_ModCompat_*.cs`** (single assembly, one file per source): class names are `SDS_CompatAdapter_*`; `SDS_CompatServices.cs` holds the shared Sdk utility classes (`SDS_CompatTools` / `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply`); hook A/B report DTOs and the Hub belong to `SDS.Diag` (`SDS.Diag_API.cs`, implemented in `SDS_CompatReports.cs`); everything else is one file per "source" (Pick Up And Haul / Combat Extended / UF Light Industries Furniture / Manipulator Beam Emitter / Milira Expanded - Future Technology / CeleTech Shuttle Extension / ProjectE Equivalent Exchange / VC Vehicle Framework VersatranZeroOne). They must not be scattered into other directories.
- A single third-party mod's adaptation goes in its own `SDS_ModCompat_<Name>.cs` (create the file if it does not exist); it must not be mixed into another mod's file, and adapter files with other prefixes must not be created.
- This constraint only governs "where adapters live" and does not change the translation-responsibility boundary of §4.

### 4.8 Compat entry ownership (derived)

> Sole purpose: make "every third-party entry has exactly one write takeover path" (§4.3) auditable.
> The machine verification granularity of `CheckCompatSurface.ps1` is "file + symbol"; true hook-level dual triggering still requires manual review.
> Entry keys must not repeat within the same table; every `SDS_ModCompat_*.cs` has at least one row.

| Third party | Entry key | Ownership | Source anchor | Allowed calls |
|---|---|---|---|---|
| Pick Up And Haul | `PUAH.HasJobOnThing` | Adapter | `SDS_ModCompat_PUAH.cs: HasJobOnThing` | ModuleInterfaces.en.md §5 Job bridge |
| Pick Up And Haul | `PUAH.JobOnThing` | Adapter | `SDS_ModCompat_PUAH.cs: JobOnThing` | ModuleInterfaces.en.md §5 Job bridge |
| Pick Up And Haul | `PUAH.UnloadDispatch` | Adapter | `SDS_ModCompat_PUAH.cs: UnloadDispatchPrefix` | ModuleInterfaces.en.md §5 Job bridge |
| Combat Extended | `CE.ReloadTurret` | Adapter | `SDS_ModCompat_CE.cs: WorkGiver_ReloadTurret, ReloadTurretHasJobPostfix, ReloadTurretJobPrefix` | public `TryWithdraw` + bridge |
| Combat Extended | `CE.ReloadAutoLoader` | Adapter | `SDS_ModCompat_CE.cs: WorkGiver_ReloadAutoLoader, ReloadAutoLoaderHasJobPostfix, ReloadAutoLoaderJobPrefix` | public `TryWithdraw` + bridge |
| Combat Extended | `CE.ManTurrets` | Adapter | `SDS_ModCompat_CE.cs: JobGiver_ManTurretsNearSelfCE, FindAmmoForTurret` | public `TryWithdraw` + bridge |
| Combat Extended | `CE.CheckReload` | Adapter | `SDS_ModCompat_CE.cs: JobGiver_CheckReload, JobTrackerTick` | public `TryWithdraw` + bridge |
| Combat Extended | `CE.DefenderReload` | Adapter | `SDS_ModCompat_CE.cs: JobGiver_DefenderReloadTurret` | public `TryWithdraw` + bridge |
| Combat Extended | `CE.PickupAmmo` | Adapter | `SDS_ModCompat_CE.cs: TryPickupAmmo` | public `TryWithdraw` + bridge |
| Combat Extended | `CE.PrepareShot` | Adapter | `SDS_ModCompat_CE.cs: TryPrepareShot` | public `TryWithdraw` + bridge |
| Combat Extended | `CE.FindBestAmmo` | Adapter (read-only translation, no duplicate writes with the core search patch) | `SDS_ModCompat_CE.cs: JobGiverUtils_Reload, FindBestAmmo` | `IsMirror` / real-thing filtering |
| Combat Extended | `CE.ReloadDriverGuard` | Adapter (driver fallback) | `SDS_ModCompat_CE.cs: JobDriver_ReloadTurret, JobDriver_ReloadAutoLoader` | `IsMirror` |
| UF Light Industries Furniture | `UF.PullIngredients` | Adapter + shared bill-supply service | `SDS_ModCompat_UFRecipes.cs: TryPullIngredientsFromCandidates` | `SDS_CompatBillSupply` |
| Manipulator Beam Emitter | `Beam.SpecialContainerType` | Adapter | `SDS_ModCompat_ManipulatorBeam.cs: IsSpecialContainerTypeEnabled` | public `IsDepositEndpoint` |
| Manipulator Beam Emitter | `Beam.ScanCache` | Adapter | `SDS_ModCompat_ManipulatorBeam.cs: SpecialContainers` | public `IsDepositEndpoint` |
| Manipulator Beam Emitter | `Beam.Transfer` | Adapter | `SDS_ModCompat_ManipulatorBeam.cs: TryFindSpecialContainerTransfer` | public `IsDepositEndpoint` + endpoint `IHaulDestination.Accepts` (vanilla contract; the destination is already fixed by the other mod, so the "Find" result is not used for comparison) |
| Milira Expanded - Future Technology | `Milira.AutoProduction` | Adapter + shared bill-supply service | `SDS_ModCompat_MiliraExpanded.cs: CompVirtualProductionDevice, CompTick` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| Milira Expanded - Future Technology | `Milira.AutomatedRecipeCycle` | Adapter + shared bill-supply service | `SDS_ModCompat_MiliraExpanded.cs: TryProcessAutomatedRecipeCycle` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| Milira Expanded - Future Technology | `Milira.ManualRecompose` | Adapter + shared bill-supply service | `SDS_ModCompat_MiliraExpanded.cs: TryProcessManualRecomposeBill` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| Milira Expanded - Future Technology | `Milira.RecomposeOrder` | Adapter + shared bill-supply service | `SDS_ModCompat_MiliraExpanded.cs: TryProcessRecomposeOrder` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| Milira Expanded - Future Technology | `Milira.DecomposeOrder` | Adapter + shared bill-supply service | `SDS_ModCompat_MiliraExpanded.cs: TryProcessDecomposeOrder` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| Milira Expanded - Future Technology | `Milira.LifeEssenceOrder` | Adapter + shared bill-supply service | `SDS_ModCompat_MiliraExpanded.cs: TryProcessLifeEssenceOrder` | `SDS_API.TryPlanBillSupplySlot` + `SDS_CompatStock` |
| CeleTech Shuttle Extension | `CeleTechShuttle.BuildCargoTransferables` | Adapter (read-only projection, manifest injection) | `SDS_ModCompat_CeleTechShuttle.cs: TryApply, BuildCargoTransferablesPostfix` | bridge `SDS_API.InjectTransportLoadCandidates`; no queue writes / no job assignment / no ledger debits |
| ProjectE Equivalent Exchange | `EE.TransmuteAccept` | Adapter | `SDS_ModCompat_EquivalentExchange.cs: TryAcceptPrefix, TryAcceptFinalizer` | `TryGetDisplayKey` / `TryWithdraw` + `TryReturnVirtual` |
| VC Vehicle Framework VersatranZeroOne | `Versatran.CargoInstantLoad` | Adapter (write translation) | `SDS_ModCompat_Versatran.cs: LoadPrefix, LoadPostfix` | `SDS_API.TryWithdraw` / `TryWithdrawPreservedExact` + `TryReturnVirtual` / `TryReturnPreserved`; cargo loading uses the vehicle's own `TryAddOrTransferThingToCargoStrict` |
| VC Vehicle Framework VersatranZeroOne | `Versatran.CargoRefresh` | Adapter (read-only projection) | `SDS_ModCompat_Versatran.cs: RefreshPostfix` | `SDS_API.ReadStock` / `GetAvailableCount` / `AppendPreservedThings` / `IsLivePreservedThing` / `IsStockForbidden` |
| VC Vehicle Framework VersatranZeroOne | `Versatran.MirrorGuard` | Adapter (read-only gatekeeping) | `SDS_ModCompat_Versatran.cs: CanStorePrefix` | `SDS_API.IsMirror` / `IsLivePreservedThing` |
| Edge Milira Raising: The One Connected to You | `MiliraGift.ReceiveGift` | Adapter | `SDS_ModCompat_MiliraGift.cs: TryMakeReservationsPrefix` | public `IsMirror` / `IsGridUsable` / `HasAnyEndpoint` / `TryGetWithdrawCell` / `TryWithdrawFromMirror` / `TryReturnVirtual` (via `SDS_CompatMirrorSeam`) |

### 4.9 Adapter rule classification (derived)

> Criterion: adapters may only "read third-party state / translate parameters / call the facade / translate results"; any judgment such as "which one, how many, is it enough, ordering, rollback order" is a rule and must be pushed up to the single implementation.
> This table records only classification conclusions; the rules themselves remain governed by the corresponding capability documents.

| Adapter / infrastructure | Code location | Verdict | Single rule / notes |
|---|---|---|---|
| `SDS_BillSupplyPlanner` | `SDS.Rules/SDS_BillSupplyPlanner.cs` (adapter queries in `SDS_BillIngredientQuery.cs`) | **Rule implementation** | S16: choose def / required / fromOwn / shortfall / reserve once per def; the recipe-slot and def-list entries share the `TryPlanFromCandidates` core |
| `SDS_ModCompat_PUAH` | `SDS_ModCompat_PUAH.cs` | Pure translation | goes only through the ModuleInterfaces.en.md §5 Job bridge |
| `SDS_ModCompat_CE.TryTakeForCe` | `SDS_ModCompat_CE.cs` | Translation + composition | available amounts come from `SDS_API.TryFindWithdrawableKey`; capacity / priority are not recomputed |
| `SDS_CompatBillSupply` | `SDS_CompatServices.cs` | Translation + reuse of vanilla algorithms | only filters preserved candidates + calls vanilla `TryFindBestBillIngredientsInSet`; slot eligibility asks `SDS_API.IsBillIngredientAllowed` |
| `SDS_ModCompat_ManipulatorBeam` | `SDS_ModCompat_ManipulatorBeam.cs` | Pure translation | uses public `IsDepositEndpoint` only to judge endpoint identity + the endpoint's `IHaulDestination.Accepts` to judge acceptance (the Manipulator Beam has a fixed destination) |
| `SDS_ModCompat_MiliraExpanded` | `SDS_ModCompat_MiliraExpanded.cs` | Translation + execution | only provides own amount / externally available amount / depositability / per-cycle consumption (translation of third-party formulas) and executes pickup / deposit; planning asks `SDS_API.TryPlanBillSupplySlot` (recipes) / `SDS_API.TryPlanAllowedDefSupply` (recipe-less orders) |
| `SDS_ModCompat_CeleTechShuttle` | `SDS_ModCompat_CeleTechShuttle.cs` | Pure translation (read-only injection) | only forwards the single injection implementation through the bridge `SDS_API.InjectTransportLoadCandidates`; selects no rows, takes no items, debits nothing (W50) |
| `SDS_ModCompat_Versatran` | `SDS_ModCompat_Versatran.cs` | Translation + execution | read-only projection: mirror row cap = `ReadStock.available`, preserved true-body rows come from `AppendPreservedThings`; virtual goes through `TryWithdraw`, preserved through `TryWithdrawPreservedExact`; cargo loading uses the vehicle's own method, with no custom capacity / priority / channel |
| `SDS_ModCompat_EquivalentExchange.ConvertRows` | `SDS_ModCompat_EquivalentExchange.cs` | Transaction composition | only calls `SDS_API.GetAvailableCount` / `TryWithdraw` / `TryReturnVirtual`; no custom rollback order |
| `SDS_CompatMirrorSeam` | `SDS_CompatServices.cs` | Translation + execution (shared implementation) | the single implementation of endpoint materialization before third-party Job reservations: fetch endpoint cell / take items / copy comp fields / drop cell / rewrite target / re-credit the ledger on failure; selects no endpoints, computes no availability, defines no rules |
| `SDS_ModCompat_MiliraGift` | `SDS_ModCompat_MiliraGift.cs` | Pure descriptor + hook | only declares `JobDriver_IdealReceiveGift`'s type, fields, and take-1-item; all materialization behavior lives in `SDS_CompatMirrorSeam` |
| `SDS_CompatIngredientRule` | deleted | — | rules pushed up to S16; no second rule outlet is kept |

> Third-party formulas (e.g. Milira's "how many items one cycle consumes") only produce the `need` input and count as parameter translation; target amounts, def selection, shortfalls, and reservations are still decided by `SDS_BillSupplyPlanner` (S16).

### 4.10 Call chains (derived)

> Sole purpose: pin down "entry → adapter → facade method → single implementation" so adapters cannot grow a second implementation again.
> Machine verification: `CheckCompatSurface.ps1` verifies that files exist, that facade methods are classifiable within `SDS_API`, and that single-implementation class names exist in source.
> From Version 4 on, `GetAvailableCount` / `GetPreservedAvailableCount` / `IsMirror` / `TryGetDisplayKey` / `TryGetWithdrawCell` among the calls below belong to the adapter bridge in ModuleInterfaces.en.md §5 (public, but not part of the five-axis contract).

| Adapter | Entry group | Facade methods | Single implementation |
|---|---|---|---|
| `SDS_ModCompat_PUAH.cs` | Pick Up And Haul Job / unloading | `SDS_API.TryTakeDepositCandidate`, `SDS_API.TryMakeDepositJob`, `SDS_API.TryMakeDepositJobFromInventory`, `SDS_API.IsDepositJob`, `SDS_API.IsPawnPortOperational`, `SDS_API.IsProducedPendingConsumption` | `SDS_DepositService`, `SDS_JobDefOf`, `SDS_ProducedThingMarker`, `SDS_ImplantUtility` |
| `SDS_ModCompat_CE.cs` | Combat Extended reloading / turrets / ammo pickup | `SDS_API.IsGridAvailable`, `SDS_API.IsGridUsable`, `SDS_API.IsManualDepositAvailable`, `SDS_API.IsMirror`, `SDS_API.MarkProducedPendingConsumption`, `SDS_API.TryFindWithdrawableKey`, `SDS_API.TryGetWithdrawCell`, `SDS_API.TryReturnVirtual` (`landOnFailure: false`), `SDS_API.TryWithdraw` | `SDS_WithdrawExecutor`, `SDS_Availability`, `SDS_Core`, `SDS_ProducedThingMarker`, `SDS_ReturnUtility`, `SDS_ChipManualDeposit` |
| `SDS_ModCompat_UFRecipes.cs` | UF Light Industries Furniture fully automatic printer | `SDS_API.AppendPreservedThings`, `SDS_API.IsBillIngredientAllowed`, `SDS_API.IsGridAvailable`, `SDS_API.IsLivePreservedThing`, `SDS_API.IsStockForbidden`, `SDS_API.GetAvailableCount`, `SDS_API.TryWithdrawPreservedExact` | `SDS_BillSupplyPlanner`, `SDS_WithdrawExecutor`, `SDS_PreservedBuckets`, vanilla `TryFindBestBillIngredientsInSet` |
| `SDS_ModCompat_ManipulatorBeam.cs` | Manipulator Beam Emitter special containers | `SDS_API.IsDepositEndpoint` | `SDS_EndpointHandle`, endpoint `IHaulDestination.Accepts` |
| `SDS_ModCompat_MiliraExpanded.cs` | Milira Expanded - Future Technology automatic / manual / orders / decomposition | `SDS_API.TryPlanBillSupplySlot`, `SDS_API.TryPlanAllowedDefSupply`, `SDS_API.NotifyBillSupplyDeposited`, `SDS_API.RequiresPreservedStorage`, `SDS_API.TryReturnPreserved`, `SDS_API.TryReturnVirtual`, `SDS_API.GetAvailableCountForDef`, `SDS_API.TryWithdrawVirtualByDef`, `SDS_API.TryWithdrawPreservedByDef` | `SDS_BillSupplyPlanner`, `SDS_WithdrawExecutor`, `SDS_ReturnUtility` |
| `SDS_ModCompat_CeleTechShuttle.cs` | CeleTech Shuttle Extension cargo manifest injection | `SDS_API.InjectTransportLoadCandidates` | `SDS_TransportVirtual` |
| `SDS_ModCompat_EquivalentExchange.cs` | ProjectE Equivalent Exchange transmutation dialog | `SDS_API.GetAvailableCount`, `SDS_API.IsDisplayThing`, `SDS_API.IsMirror`, `SDS_API.TryGetDisplayKey`, `SDS_API.TryReturnVirtual`, `SDS_API.TryWithdraw` | `SDS_WithdrawExecutor`, `SDS_ReturnUtility` |
| `SDS_ModCompat_Versatran.cs` | VC Vehicle Framework vehicle cargo loading | `SDS_API.IsGridUsable`, `SDS_API.IsMirror`, `SDS_API.TryGetStockKey`, `SDS_API.ReadStock`, `SDS_API.GetAvailableCount`, `SDS_API.IsStockForbidden`, `SDS_API.AppendPreservedThings`, `SDS_API.IsLivePreservedThing`, `SDS_API.TryWithdraw`, `SDS_API.TryWithdrawPreservedExact`, `SDS_API.TryReturnVirtual`, `SDS_API.TryReturnPreserved` | `SDS_WithdrawExecutor`, `SDS_Availability`, `SDS_ReturnUtility` |
| `SDS_ModCompat_MiliraGift.cs` | Edge Milira Raising: The One Connected to You gift receiving | `SDS_API.IsMirror`, `SDS_API.IsGridUsable`, `SDS_API.HasAnyEndpoint`, `SDS_API.TryGetWithdrawCell`, `SDS_API.TryWithdrawFromMirror`, `SDS_API.TryReturnVirtual` | `SDS_CompatMirrorSeam`, `SDS_WithdrawExecutor`, `SDS_ReturnUtility` |

### 4.11 Dual-trigger audit (derived)

> Verdict: at most one "write" path per entry; read-only projections + adapter translation may coexist. Item-by-item conclusions are in the table below.
> Machine verification: `CheckCompatSurface.ps1` verifies that entry keys are all in §4.8 and every adapter is in §4.11; semantics still need manual review.

| Entry | Core path | Adapter path | Conclusion |
|---|---|---|---|
| `PUAH.HasJobOnThing` / `PUAH.JobOnThing` / `PUAH.UnloadDispatch` | `Patch_SDS_JobGiverHaul` / `Patch_SDS_HaulGeneralToGrid` go through the vanilla WorkGiver | `SDS_ModCompat_PUAH` goes through Pick Up And Haul's own WorkGiver / unload entry | different work givers; a Pawn takes only one Job per tick; takeover happens only when storing in place is possible, otherwise it yields |
| `CE.FindBestAmmo` | `Patch_SDS_Closest*` does only mirror distance projection (read-only) | `FindBestAmmoMirrorFilterPostfix` swaps mirrors for the nearest real ammo | allowed: core read-only, adapter translates; no second pickup-debit path exists |
| `CE.ReloadTurret` / `CE.ReloadAutoLoader` / `CE.ManTurrets` / `CE.CheckReload` / `CE.DefenderReload` / `CE.PickupAmmo` / `CE.PrepareShot` / `CE.ReloadDriverGuard` | no same-named core write patch | `SDS_ModCompat_CE` uses paired `HasJobOnThing + JobOnThing` or `GetPriority + TryGiveJob`; the driver fallback only rejects mirror targets | each chain has only Combat Extended's own write outlet; when Combat Extended can find a real thing, the adapter does not intervene |
| `UF.PullIngredients` | `Patch_SDS_PreservedBillIngredients` / `Patch_SDS_VirtualFixedIngredients` hook only `WorkGiver_DoBill` | `SDS_CompatBillSupply` goes through UF Light Industries Furniture's own ingredient-pull entry | different entries; that entry does not pass through `WorkGiver_DoBill` |
| `GD.DeckReinforceCount` | `Patch_SDS_JobMirrorTargets` only rewrites mirror targets at StartJob (the write path remains the unified executor) | registers only a value provider; does not directly take items / debit / write Jobs | allowed: the adapter only supplies values; withdrawal amounts, materialization, and debits still have only one path |
| `Beam.SpecialContainerType` / `Beam.ScanCache` / `Beam.Transfer` | no core write patch | only calls `SDS_API.IsDepositEndpoint` + the endpoint's `IHaulDestination.Accepts`; delivery goes through the vanilla container contract | does not call the Store facade, avoiding a second ledger-deposit path |
| `Milira.AutoProduction` / `Milira.AutomatedRecipeCycle` / `Milira.ManualRecompose` / `Milira.RecomposeOrder` / `Milira.DecomposeOrder` / `Milira.LifeEssenceOrder` | the core DoBill bridge does not connect to the Milira Expanded - Future Technology network | entries share `SDS_BillSupplyPlanner` + their own pools / reservations | entries share the same planner; the same shortfall is not re-resupplied within the same tick |
| `CeleTechShuttle.BuildCargoTransferables` | no same-named core write patch (the core only touches the vanilla dialog manifest / job assignment / boarding layers) | only injects display things (read-only projection); the write path remains the unified loading Job | allowed: read projection + adapter translation; no second pickup / debit path exists |
| `EE.TransmuteAccept` | `Patch_SDS_TransporterAddVirtual` only injects display things (UI read-only) | `TryAcceptPrefix / TryAcceptFinalizer` swap display things for true bodies and return on failure | the core does not write the ledger; the only write path is ProjectE Equivalent Exchange transmutation + the adapter's return |
| `Versatran.CargoInstantLoad` | no same-named core write patch (the vehicle's custom loading UI) | `SDS_ModCompat_Versatran` first pulls mirror / preserved rows out of the vanilla loop; virtual goes through `TryWithdraw`, preserved through `TryWithdrawPreservedExact` | allowed: Hyperdimensional rows have only one API write path; the vanilla loop never sees mirror / preserved true bodies; real map items still follow the vehicle's original logic |
| `Versatran.CargoRefresh` | no core write patch (read-only projection) | `RefreshPostfix` changes `MaxCount = available` and appends preserved true-body rows | allowed: read-only projection; no debits / no job assignment |
| `Versatran.MirrorGuard` | no core write patch | `CanStorePrefix` rejects mirrors / live preserved true bodies from entering vehicle containers | allowed: read-only gatekeeping; adds no write path |
| `MiliraGift.ReceiveGift` | no same-named core write patch (W54 only translates mirror-target search / reachability / carrying) | the adapter materializes 1 item during the third-party driver's reservation pre-step and rewrites targetA; pickup goes only through `TryWithdrawFromMirror` | allowed: the same entry has only one write path; the adapter never touches the ledger and makes no position judgments |

---

## 5. Known boundaries of the thin adapters (non-contract)

> This section only records why the two thin adapters for "non-cooperative mods" are wired the way they are; it defines no rules. For adapter hooks and call lists, see [SDS.ThirdModCompat/Interfaces.en.md](SDS.ThirdModCompat/Interfaces.en.md).

### 5.1 The two thin adapters (implemented)

17. Manipulator Beam Emitter (`natsuki.manipulatorbeam`): an independent, toggleable thin adapter that only translates "find destination / deliver" into Find / Deposit; it does not change the other mod's work logic (in place since 2026-10-26).
    - Gap: its destinations are only `SlotGroup` plus a closed whitelist of special container types; it neither walks vanilla non-cell containers nor asks generic `IHaulDestination`, so this mod's endpoints are invisible to it.
    - Translation: pass the type gate + append endpoints to the container scan table + describe "ground item → endpoint" using its `BeamTransfer` shape; the candidate set uses vanilla `ListerHaulables`, endpoint identity asks `IsDepositEndpoint`, and acceptance asks only the endpoint's `IHaulDestination.Accepts` (this mod's hard deposit gate). The Manipulator Beam has a fixed destination, so the "Find" facade's global nearest result is no longer compared against that endpoint — otherwise, as long as any nearer chip pawn or other endpoint is nearby, all building endpoints get judged "no work" at once (2026-11-12 in-game symptom: endpoints completely unrecognized).
    - **Zero takeover of delivery**: its own generic branch (`TryGetInnerInteractableThingOwner` + `IHaulDestination.Accepts` + `ThingOwner.TryAdd`) is exactly the vanilla container contract, and the endpoint's `InputBuffer` catches it directly → "buffer → digitized ledger deposit".
    - The adapter judges only "endpoint identity + whether the endpoint accepts" and does not call the Store facade: delivery is already covered by the vanilla container contract (§1.5 vanilla contracts first); going through `TryDepositAt` again would become a second ledger-deposit path.
    - Disabling: it follows the Manipulator Beam Emitter's own "special containers" master switch (effective when any sub-switch is on); no SDS-specific switch is added.
17a (2026-11-11): CeleTech Shuttle Extension (`LongRange.CeleTech.ShuttleExtension`): an independent thin adapter that only injects display things after its **cargo-manifest building function** returns; it does not change its loading / refrigeration / launch logic.
    - Gap: it builds its own loading dialog and read model and does not go through vanilla `Dialog_LoadTransporters`; this mod's stock has no map-side physical items, so without an injection point in the manifest it is completely invisible.
    - Translation: one Postfix (`CargoTransferFilter.BuildCargoTransferables`) → bridge `SDS_API.InjectTransportLoadCandidates` → the single injection implementation `SDS_TransportVirtual.InjectIntoTransporters`; pickup / job assignment / boarding / save-loading remain the responsibility of the existing vanilla hooks and the single executor (W50).
    - Known boundary: the display thing is an "aggregate stack of the entire stock"; if the other mod has a pre-load admission check based on whole-stack mass (refrigeration admission), the manifest-stage precheck is stricter than vanilla; the adapter does not clamp counts — the final authority is the other mod's "start loading" recomputation by the selected count.
18. If integration is impossible without changing the other mod's logic, keep the recorded boundaries and add no special cases to the core.

---

## 6. Acceptance checklist

- [x] Do facade methods call only the corresponding single implementation, with no second sorting / filtering / debit logic?
- [x] Are returned objects vanilla-recognizable (`Thing` / proxy / read-only snapshot)?
- [x] Does the same stock have only one visible representation, matching the RuleIndex.en.md S-series criteria?
- [x] Does non-Pawn mode reuse the same resolver instead of a second resolver?
- [x] Do Take / Store expose only transaction-level interfaces with clearly stated postconditions?
- [x] Are ownership / faction / forbidden checks completed by the facade?
- [x] Does it serve the player faction only and reject cross-map deposits / cross-faction endpoints?
- [x] Do public API exceptions uniformly return `InternalError` / an empty set / `default`, never leaking to third parties?
- [x] Have unadopted old entries been deleted with a `Version` bump (Version 2)?
- [x] Does the bridge surface carry forwarding only, staying outside the five-axis contract (Version 4)?
- [x] Do adapters only translate, copying no rules and changing no third-party Job/Toil?
- [x] Is there only one takeover path, with no facade + Harmony dual triggering? (Static checks: Pick Up And Haul keeps the `DepositToGrid` early-out; Combat Extended / ProjectE Equivalent Exchange / UF Light Industries Furniture go straight through the facade; in-game verification items are in DiagnosticsCoverage.en.md)
- [x] Do all normal rejections write no logs, with exceptions uniformly `InternalError` / empty set / `default` + one `SDS_Diag.ErrorOnce`?
- [x] Are version / capability probes provided, with integration as a soft dependency?
