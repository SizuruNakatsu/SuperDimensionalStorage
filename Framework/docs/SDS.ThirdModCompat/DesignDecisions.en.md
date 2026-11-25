# SDS.ThirdModCompat · Design Decisions (Why It Is Done This Way / Pitfalls Hit)

> Role: records the historical decisions, pitfalls hit, and design trade-offs since this module was consolidated from the old 9 projects into a single assembly. For archival and traceability only; maintaining this module does not require revisiting old code — per-hook-point facts are authoritative in Interfaces.en.md §F.2. All figures are per source-verifiable definitions; unverifiable ones are marked "(TBD)".

## 1. Historical Decisions and Pitfalls

| Date / item | Decision or pitfall | Status |
|---|---|---|
| 2026-11-12 consolidation | The 9 dlls were merged into `SDS.ThirdModCompat.dll`; deployment adds only one file, and any adapter change advances the whole dll's version | Done |
| One implementation per assembly → name uniqueness | After losing the assembly boundary, duplicates can only be found via `Name` / Harmony ID; same-name-different-type cases are logged as Error and the later registrant is skipped | `SDS_CompatLoader` + `CompatAdapterTests` |
| Shared Harmony → per-adapter Harmony | The old `SDS_ModCompat.cs` unconditionally `TryApplyAll`ed with a single Harmony; the new `Apply` creates one per adapter depending on whether the target is loaded | Done |
| Sdk utility class visibility | The old 4 classes such as `SDS_CompatTools` were `internal` (within the Sdk assembly); after consolidation they were promoted to `public`, constrained by the public whitelist | `CheckAssemblyBoundaries.ps1` |
| `SDS_StockKey.FromThing` | Old EE / Milira derived keys directly from physical things; the new API no longer exposes that entry point and uniformly uses `SDS_API.TryGetStockKey` | Replaced |
| CE preserved probe | The old `SDS_Stock.HasInstanceNonKeyState` inlined a call to `HasLoadedAmmoState`; now `SDS_CompatAdapter_CE.Apply` calls `SDS_PreserveHooks.Register` and Core consumes it uniformly | Done |
| `TryReturnPreserved` signature | The old signature had `IntVec3`; the previous version narrowed it to `(Thing, Map)`; on 2026-11-12 it was restored to `(Thing, Map, IntVec3)`, and Milira uniformly passes `IntVec3.Invalid` | Restored |
| `AllowInjection` call chain | Old docs said it was called by `Patch_SDS_TransporterAddVirtual`; the new implementation has the adapter's `Apply` register it into `SDS_CompatGates`, consumed uniformly by the Vanilla injection patch (allowed when unregistered, fail-closed on exception) | Fixed |
| `ClearStoreCache` call sites | The old `SDS_Mod.ApplySettings` and the confirmation callback called it in two places; the new implementation makes `CanStoreDef` self-invalidate based on the toggle and clears the cache in `Reset()` | Fixed |
| EE dynamic inventory 3 rows | The old PatchInventory registered 3 rows for EE (including the `AddItemsToTransferables` injection gate); the first two rows are `TryPatch` calls and the 3rd is the gate consumption, now backed by `SDS_CompatGates` | Aligned |
| Report hub migration | The old `SDS_CompatReports.cs` lived in the compat layer; it now belongs to `SDS.Diag`, with writes allowed only on the main thread and Bootstrap reading snapshots | Done |
| Priority | Hook points uniformly use `Priority.Low`, letting third-party originals run first with adapters as fallback; EE's `TryAccept` Prefix is the exception at `High` | Done |
| Dynamic patch accounting | Third-party hook points are not counted in Vanilla's 112 attribute patch classes; they are recorded only in the PatchInventory.en.md dynamic section | `CheckPatchInventory.ps1` |

## 2. Old Implementation → New Implementation: Differences and Pitfalls

> This section keeps old-implementation facts and old/new differences; per-hook-point details, packageIds, and current notes are in Interfaces.en.md §F.1 / §F.2 and are not repeated here.

### 2.1 PUAH

Old implementation: when the endpoint wins, the whole order is replaced by this mod's multi-thing deposit Job; `HasJobOnThing` writes the same-tick decision cache, `JobOnThing` consumes only that same decision, and debiting still happens exactly once.

### 2.2 CE

Old implementation: the static caches `checkCooldownUntil` / `priorityCacheUntil` / `priorityCacheValue` stored absolute ticks keyed by `thingIDNumber`, with 1 entry registered in the old `SDS_StaticState`; the `pendingAmmo` series were one-shot value slots between the two legs.

### 2.3 UFRecipes

Old implementation: selection reused the vanilla `WorkGiver_DoBill.TryFindBestBillIngredientsInSet`; the adapter provided only candidates and withdrawal; if the whole batch could not be met, everything was returned and false was returned — no partial results.

### 2.4 GlitterworldDestroyer (Removed on 2026-11-16)

The original adapter only registered the remaining required amount; after Plan B, missing counts are handled uniformly by W58, and the adapter was deleted along with `SDS_JobFuelCount`.

### 2.5 ManipulatorBeam

Old implementation: the old version's three decision points checked `ISDS_StorageEndpoint` directly (the old core's endpoint interface); the new version uniformly asks `SDS_API.IsDepositEndpoint` instead, removing cross-layer references to Core / Vanilla implementations (E6).

### 2.6 MiliraExpanded

Old implementation: `nextTickByDevice` was registered with `ISDSResettable`; `canStoreCache` was keyed by `ThingDef` with values depending on the W33 toggle. The old source called `ClearStoreCache()` in two places — the W33 confirmation callback and `ApplySettings`; the new implementation makes `CanStoreDef` self-invalidate based on `ForceWeaponApparelVirtual`, and `Reset()` clears the cache too (E1), no longer relying on settings callbacks.

### 2.7 CeleTechShuttle

Old implementation: no static caches; admission / cold storage / loading Jobs / bay loading remain the other mod's and vanilla's responsibility.

### 2.8 EquivalentExchange

Old implementation: the old version's return used `SDS_StockKey.FromThing(thing)` to derive the key directly from the physical thing; the new version uniformly uses `SDS_API.TryGetStockKey(thing, out key)`, keeping the compat layer from depending on old internal types.

## 3. Design Trade-offs

> The former ImplementationRationale.md was merged into this section: why third-party adaptation must be independent of vanilla (`SDS.Vanilla`), why it was consolidated into a single assembly, why the Sdk utility classes must exist, and the trade-offs paid for these choices. The historical structure is covered in 3.2 and landed decisions in Chapter 1; this section does not repeat them. The binding constraints are in ModuleRules.en.md 6.6 / 7 / 8.

### 3.1 Why Third-Party Adaptation Must Be Independent of Vanilla

| Reason | Approach | Source evidence |
|---|---|---|
| Vanilla behavior must not change | All 9 adapters' hook points land on third-party types; only two hook points attach to vanilla methods, and both use "target mod loaded" as the installation precondition (see the "Vanilla Support" section E.1 of Interfaces.en.md) | `Resolve()` in `SDS_ModCompat_CE.TryApply`; `found` in `SDS_ModCompat_EquivalentExchange.TryApply` |
| Core rules must not be polluted | Adapters provide only facts (own counts / externally available counts / per-cycle usage); they do not compute shortfalls, pick endpoints, or debit the ledger | `SDS_ModCompat_MiliraExpanded` calls only `SDS_API.TryPlan*`; `SDS_CompatBillSupply` reuses the vanilla ingredient-selection algorithm |
| Zero impact when target mods are absent | `IsTargetLoaded` only probes by type name; Bootstrap does not call `Apply` when it fails, creates no Harmony instance, and generates no hook points | `SDS_CompatLoader.Process` (returns directly on `!targetLoaded`) |

- The 112 attribute patch classes are all in `SDS.Vanilla`; this module has no `[HarmonyPatch]` classes. Patch ownership: ModuleRules.en.md 7.
- If third-party translation were written into Core: S16 would gain a second implementation, endpoint ordering would gain third-party special cases, and transactions would gain a second write path — a direct violation of E7.
- The second purpose of a separate assembly is "mods that are not installed never enter compilation": `SDS.ThirdModCompat.csproj` references only `SDS.Contracts` / `SDS.Diag` / `SDS.Api` and Harmony, with no third-party dlls.

### 3.2 Why Consolidate into a Single Assembly

The historical structure was 1 `SDS.Compat.Sdk` + 8 independent `SDS.Compat.*` projects, each assembly holding exactly one `ISDSCompatAdapter`, naturally isolated by the assembly boundary. The benefits and costs after consolidation:

| Trade-off | Benefit after consolidation | Cost and mitigation |
|---|---|---|
| Deployment | `Assemblies` gains only one file, `SDS.ThirdModCompat.dll`; no more 9-dll fragmentation | None |
| Version consistency | All 9 adapters live in one dll; no "some adapters new, some old" situations | Any adapter change advances the whole dll's version |
| Type uniqueness | The old `SuperDimensionalStorage.dll` is not kept, avoiding duplicate types and `GenTypes` ambiguity (ModuleRules.en.md 4, naming rules) | Visibility checks are now done at the type level by `CheckAssemblyBoundaries.ps1` (ModuleRules.en.md 5) |
| Adapter isolation | Adapters still never call each other and share only the Sdk utility classes (ModuleRules.en.md 2.3) | The natural "one implementation per assembly" boundary is lost; name uniqueness is the guarantee instead |

How isolation is maintained within the single assembly:

- Each `Apply` has its own internal try/catch; one adapter's failure affects neither the other adapters nor `SDS.Vanilla`.
- Adapter identity changed from "assembly" to "`Name` / Harmony ID": `SDS_CompatLoader` indexes by `Name`, and same-name-different-type means `duplicates++` + `ErrorOnce` + skipping the later registrant (corresponding to item 3 of 3.4 and Chapter 1's "one implementation per assembly → name uniqueness").
- Each adapter creates its own `new Harmony(HarmonyId)` and applies on demand (for the old implementation's "one Harmony + unconditional full `TryApplyAll`", see Chapter 1).

### 3.3 Why the Sdk Utility Classes Must Exist

| Utility class | Calls only | Main users |
|---|---|---|
| `SDS_CompatTools` | Harmony + `SDS_Diag.ErrorOnce` + report DTOs | All 9 adapters |
| `SDS_CompatEndpoint` | `SDS_API` | UFRecipes, Milira |
| `SDS_CompatStock` | `SDS_API` | Milira, UFRecipes (indirect) |
| `SDS_CompatBillSupply` | `SDS_API` | UFRecipes |
| `SDS_CompatMirrorSeam` | `SDS_API` | MiliraGift (shared by same-shape adapters) |

Responsibilities and signatures: section A of Interfaces.en.md and the responsibility table in README.en.md. Why all of these classes must exist:

- Reflection probing must handle three cases — "type missing / member renamed / call throws"; if each of the 9 adapters wrote its own, inconsistent null checks and logging conventions would inevitably appear.
- `TryPatch` unifies four things — "apply / report / exception / priority" (null target → `Missing`; exception → `Missing` + `ErrorOnce`; success → `Applied`) — giving the report hub a single source of truth for hook point counts.
- Endpoints, stock, and supply refill are all write-path entries; if every adapter implemented its own, that would mean a second ordering or debiting scheme for the same thing (E6 / E7).
- Endpoint materialization before third-party reservations has exactly one implementation, `SDS_CompatMirrorSeam`; same-shape adapters pass descriptors only, avoiding duplicated withdrawal / placement / ledger return (added 2026-11-19).
- The utility classes themselves contain no rules: no shortfall computation, no job assignment, no transactions, no caching of cross-save business state (ModuleRules.en.md 6.6).

### 3.4 Trade-offs

1. No hard references to target assemblies
   - Approach: `SDS.ThirdModCompat.csproj` contains no third-party `ProjectReference`.
   - Rationale: target mods are optional; a hard reference would turn "target mod not installed" into a load failure rather than an absent feature.
   - Cost: all types and members are accessible only by string name; a rename on their side breaks it.
2. Reflection probing with `AccessTools.TypeByName`
   - Approach: each adapter first does `IsTargetLoaded`, then `Resolve()`s once inside `TryApply` and caches the `Type` / `MethodInfo`.
   - Rationale: absence, renames, and structural changes all degrade to "not found", which can be reported per hook point instead of crashing at startup.
   - Cost: no compile-time validation; only adapter tests and in-game A/B testing.
3. Name uniqueness is the only line of defense
   - Approach: after going single-assembly there is no more "one implementation per assembly"; `SDS_CompatLoader` deduplicates by `adapter.Name`, falling back to `type.FullName` when the name is empty.
   - Rationale: without an assembly boundary, duplicate implementations can only be caught by name.
   - Cost: new adapters must ensure `Name` and the Harmony ID do not clash with the existing 9; covered by the `HarmonyIds_AreUniqueAndMatchContract` test.
4. Failures are logged with `ErrorOnce` only and never block startup
   - Approach: `TryPatch` failures only report missing; instantiation / identity read / target detection / `Apply` exceptions during the scan phase are each logged once and processing continues.
   - Rationale: the compat layer is an optional enhancement; one third party's version change must not keep the whole mod from starting; `SDS_Diag.Fail` is forbidden in this module (ModuleRules.en.md 6.6).
   - Cost: features degrade silently; "detected, hook points 0/N" is visible only from the startup report and `SDS_CompatReportHub`.
5. Dynamic patches are not counted in the 112
   - Approach: all 9 adapters use runtime `harmony.Patch` and no `[HarmonyPatch]` attribute classes are written.
   - Rationale: `SDS_PatchInstaller` scans only `SDS.Vanilla`; third-party hook points vary with whether target mods are present and cannot enter a fixed baseline (ModuleRules.en.md 7).
   - Cost: the patch inventory cannot see these hook points; they are only readable from each `TryApply`'s hook point names and `SDS_CompatPatchReport`.

## 4. Boundaries (What This Module Deliberately Does Not Do)

- No references to Core / Vanilla and no calls to other adapters; anything needing cross-adapter sharing may go only into the Sdk utility classes of 3.3.
- No shortfall computation, endpoint picking, or count-policy duplication in utility classes or adapters; those rules live only in Core / Api.
- No second debiting / job assignment / rollback path; all write operations go through `SDS_API` only.
- No `SDS_Diag.Fail` calls; normal rejections write nothing to the log, exceptions get `ErrorOnce` only.
- No takeover of third-party Jobs / Toils / presentation: CE only supplements "item sources", CeleTech only injects the loading manifest, Manipulator Beam only supplements "destination discovery", and ProjectE only does boundary translation around transmutation.

## 5. Sources

- Rules: `Framework/ModuleRules.en.md` 6.6 / 7 / 8; `ModuleResponsibilities.en.md` 2; `ModuleInterfaces.en.md` 1.1.2 / 5 / 6.
- Code: `超维存储/Source/SDS.ThirdModCompat/SDS_CompatServices.cs`, the 9 `SDS_CompatAdapter_*.cs`, `SDS.ThirdModCompat.csproj`; `SDS.Bootstrap/SDS_CompatLoader.cs`.
- Old-implementation reference: Chapter 1 "Historical Decisions and Pitfalls" and Chapter 2 "Old Implementation → New Implementation: Differences and Pitfalls".
- Tests: `超维存储/Source/SDS.Tests/Compat/CompatAdapterTests.cs`.

This document is historical archival only; maintaining this module requires no revisiting of the old implementation.
