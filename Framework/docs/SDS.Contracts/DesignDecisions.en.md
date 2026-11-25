# SDS.Contracts · Design Decisions (why it is done this way / pitfalls encountered)

> Positioning: to solidify the "whys" from the old framework that are still valid and that anyone maintaining `SDS.Contracts` must know: historical decisions and pitfalls, what the migration removed / added (with reasons), why this must be a pure data assembly, and historical notes on legacy save nodes and fields. The old source code and design proposals are henceforth historical evidence only, no longer day-to-day references; executable rules are in `Rules.en.md`, signatures in `Interfaces.en.md`.

## 1. Historical decisions and pitfalls

- `SDS_StockKey` must override both `Equals` / `GetHashCode`: old comments record an incident where implementing only `SameAs` made every `new` of the same category a separate dictionary entry, splitting steel 200 and steel 300 into two separate rows — remove the overrides and it regresses.
- The `thingDef` of a preserved key is the "true body" def: minified furniture takes the def of `MinifiedThing.InnerThing`, corpses / gene carriers take themselves; the shell def must not be used as the key, otherwise all minified furniture would collapse into one category.
- Unconditional persistence of `defName` / `stuffName` is the core of W26. The legacy nodes `thingDef` / `stuff` are read-only, never written, as of 2026-11-16: W26 promises only "old save → new version" and makes no downgrade promise, so new saves no longer need to write back the legacy nodes; the read fallback must be kept until saves written before 2026-11-13 leave the support window.
- DefOf must never fall back to static caching: if the static constructor runs before Defs are loaded, null is permanently baked in and every later call gets null.
- `BackCompatibility.BackCompatibleDefName` may throw; the old implementation caught that and fell back to `GetNamedSilentFail` with the original name — do not remove this fallback layer.
- `SDS_Host` uses nullable static fields rather than constructor injection: host injection must happen before `SDS_GameComponent_Grid` is constructed, and Core cannot reference Vanilla; a null slot only makes the corresponding feature unavailable.
- `SDS_PreserveHooks` is a process-level constant; it does not vary per save and is not registered with `ResetAll`; `Reset` is for tests and reloads only.
- The two "count separately" switches in the old `SDS_Settings` affected only the category-counting basis; withdrawals still restore by quality / stuff — do not bake the switches into the key itself.
- ~~`SDS_ProtectedOutput` and `MapComponent_SDS_ProtectedOutputs`~~ were retired on 2026-11-16 per the [Compatibility Retirement List](../SDS.Vanilla/CompatibilityRetirement.en.md) (they served only saves from before 2.4 / 2026-10-26 21:54). New saves write only Vanilla's unified reclaim ledger (C1/C8); do not restore the second registration.

## 2. What the migration removed / added (with reasons)

Removed or moved out:

- `SDS_StockEntry.preserved`, `displayKey`, and the three-parameter constructor: they served UI only and were never persisted (old `SDS_Stock.cs` lines 372–405); display derivation belongs to Vanilla.
- `SDS_StockVitals.CaptureHitPoints` / `CaptureRotProgress`: weighted averaging belongs to Core's `SDS_StockRules`; Contracts keeps only the fields.
- The computed properties `SDS_WithdrawPlan.Key` / `Quantity` / `Preserved` and `SDS_ReadVersions.Capture()`: consumers read from the fields instead; save loading / version capture live in Core / Api.
- The `SDS_WithdrawDeliveryPolicy` resolver, the `SDS_WithdrawSave` audit class, and `SDS_EndpointPriorityState`: they went to Vanilla / Core / Vanilla respectively; Contracts keeps only the DTOs.
- `SDS_SidebarUIState.NormalizeAgainst(grid)`: removed; the save-load normalization of the old aggregate display keys (`CategoryKeyOf` had zeroed stuff / quality) is now done by Vanilla's `Window_SuperSpace.RebuildSelectedMembers` at rebuild time (for the same def + same channel, take the row with the smallest identity order as the representative key; clear only when none is found).
- The `SDS_Config.Settings` property (the old implementation read `SDS_Mod.Settings` directly): replaced with `SDS_Config.Current`, injected by Bootstrap.

Added or rewritten:

- Added `SDS_StockChannel` (virtual / preserved), `SDS_StructureKind` / `SDS_StructureFact`, and `SDS_PreserveHooks`.
- Added `SDS_Host`, the interfaces for the 11 host slots, and `ISDSCompatAdapter`; the concrete type `SDS_EndpointHandle` behind the old `SDS_EndpointDecision.handle` and `SDS_WithdrawPlan.endpoint` became the interface `ISDS_Port`.
- ~~`SDS_JobFuelCount`~~ was removed on 2026-11-16 (Plan B: missing counts are filled in uniformly per reservation intent by W58; third parties no longer register counts).
- A precedent of the same visibility narrowing: Core's `SDS_Ledger.CurrentDataVersion` changed from the old `public const` to `internal const`; the `SDS_JobDefOf.*Name` / `SDS_HediffDefOf.*Name` constants kept in this assembly remain `public const`, because they are stable Def names shared across assemblies.
- `SDS_JobDefOf` / `SDS_HediffDefOf` changed from one-shot `lookedUp` caches to live `DefDatabase` lookups on every access; missing defs changed from `Log.Error` to a per-name `Log.ErrorOnce`.
- The `SDS_EndpointPriorityState.NotifyChanged()` at the end of the old `SDS_Settings.ResetToDefaults` was removed; notifications belong to Vanilla / Bootstrap.
- `SDS_API.Version` was raised to 4; the old `SuperDimensionalStorage.dll` is no longer kept.

## 3. Design trade-offs

Why it must be a standalone pure data assembly:

- **Breaking compile-time dependencies between modules**: the dependency graph takes this assembly as the sole common upstream; Diag / Core / Api / Vanilla / ThirdModCompat / Bootstrap all reference only downward, with no reverse references (ModuleRules.en.md §4); modules may communicate only through Contracts' DTOs and interfaces, and must not reference other modules' `internal` classes, implementation classes, or static state (ModuleRules.en.md §2.2).
- **The cost before the split**: in the single-assembly `SuperDimensionalStorage.dll` (48 `.cs` files, see the frozen baseline in ModuleRules.en.md), data types and implementation classes shared one room, so any implementation change tugged at every consumer; once the contract stood alone, changing Core rules no longer required recompiling Vanilla.
- **Independently acceptable**: a module's completion does not presuppose the completion of other modules; the acceptance criteria are contract + independent compilation + independent tests (ModuleRules.en.md §2.1, §2.5).
- **Testable without the game startup flow**: Core does not reference Vanilla / Api / Bootstrap and depends only on Contracts and Diag; all host facts are obtained through `SDS_Host` slots or caller parameters (Core directly consumes `Ports` / `DepositFilter` / `Delivery`; priority / mirror physical access / the medical care gate are injected as query fields / plan fields / delegates), and implementation classes all have public parameterless constructors that read no `Instance` / `State` / `Find` / `Def` at construction (ModuleInterfaces.en.md 4.2). `SDS.Tests`' `CoreTestKit` fills the slots with fake implementations, injects the parameters above, and then directly does `new SDS_Ledger()` + `SDS_Core.Attach` (`SuperDimensionalStorage/Source/SDS.Tests/Core/CoreTestKit.cs`, `SDS.Tests/Fakes/`). "Without" means only: the game is not launched, no `Map` is built, and the mod startup sequence is not entered; compilation and execution still require `Assembly-CSharp` and `UnityStubs`.
- **Once the contract is stable, each module can optimize independently**: as long as DTO field names and interface members are unchanged, Core can rework its transaction implementation, Vanilla can rewrite its patches, and ThirdModCompat can swap its adapters, with no cross-recompilation (ModuleRules.en.md §2.1, §2.5); stability is backed by the save-locked inventory and the type-level allowlist of `CheckAssemblyBoundaries.ps1` (ModuleInterfaces.en.md §8, ModuleRules.en.md §5, §10); changing the save structure or the external API is L3 and requires a human decision (DesignPrinciples.en.md §3 E4).

This assembly is split into 13 files: `SDS_Stock.cs` / `SDS_Withdraw.cs` / `SDS_EndpointContract.cs` / `SDS_API.cs` / `SDS_Mod.cs` / `SDS_CaravanTransport.cs` / `SDS_Implants.cs` / `SDS_UI.cs` / `SDS_Search.cs` / `SDS_Trade.cs` / `SDS_JobTargets.cs` / `SDS_Host.cs` / `SDS_PreserveHooks.cs`; the types carried by each file are in Section A of `Interfaces.en.md`.

The old framework had no host interface layer: Core and Vanilla sat in the same assembly and called `SDS_GameComponent_Grid` / `SDS_EndpointHandle` directly; therefore `SDS_Host`, the 11 host interfaces, and `ISDSCompatAdapter` are all contracts newly added at the split (`SDS_Host.cs`).

Pure data types newly extracted at the split (the old framework had no types with these names):

| Type | Current file | Notes |
|---|---|---|
| `SDS_StockChannel` | `SDS_Stock.cs` | The old framework expressed the channel distinction through the two methods `SDS_ReturnUtility.ReturnPreserved` / `ReturnVirtual` (old `SDS_ReturnAndReclaim.cs`); the unified return facade needs an explicit channel parameter |
| `SDS_StructureKind` / `SDS_StructureFact` | `SDS_EndpointContract.cs` | The old framework registered structures through concrete methods such as `SDS_GameComponent_Grid.RegisterCore` (old `SDS_GameComponent_Grid.cs`, line 378); after the split this became value-type facts |
| `SDS_PreserveHooks` | `SDS_PreserveHooks.cs` | A pure registry for third-party "must be preserved" facts; the old framework had no counterpart |

Key trade-offs:

1. **DefOf uses properties, not `static readonly` fields.** The old framework used `lookedUp` lazy caches (old `SDS_CaravanTransport.cs` from line 3714, `SDS_Implants.cs` from line 16); after the split, Def references are used across assemblies and the timing of static construction is uncontrollable — once such a cache runs before Defs are loaded, null is baked in permanently. The current implementation instead calls `DefDatabase<T>.GetNamedSilentFail` on every access, returning null when missing and logging `Log.ErrorOnce` once per Def name (this directory's `Interfaces.en.md`; current implementations in `SDS_CaravanTransport.cs`, `SDS_Implants.cs`).
2. **`SDS_Host` uses nullable static fields, not constructor injection.** Constructor injection would bind the host creation order into Core (Core does not reference Vanilla), while host injection must complete before `SDS_GameComponent_Grid` is constructed (the ordering of ModuleRules.en.md §9, steps 2 and 7); the degradation basis for null slots is in §1 and `Rules.en.md` C5.
3. **Behavior enums are forbidden.** This assembly currently contains only pure data enums such as `Danger` / `PathEndMode` / `StoragePriority`, and none of `WorkGiverDef` / `ThinkTreeDef` / `DutyDef` (the judgment boundary is in ModuleRules.en.md 6.1 and `Rules.en.md` C2). Rationale: if the contract referenced behavior enums, the knowledge of "who assigns jobs and how things move" would be written into the upstream that all modules depend on; any vanilla behavior-table change would then force a full recompile, and it would violate "do not change the work logic vanilla exhibits" (DesignPrinciples.en.md, Corollary 2).
4. **Endpoints use the interface `ISDS_Port`, not the old concrete handle.** The old `SDS_EndpointDecision.handle` had the type `SDS_EndpointHandle` (old `SDS_EndpointContract.cs`, line 108), which is a Vanilla implementation type; Contracts must not reference implementations (ModuleRules.en.md 6.1), so it became an interface keeping only the members needed for judgment and ranking (this directory's `Interfaces.en.md`). The same trade-off applies to `SDS_WithdrawPlan.endpoint`.
5. **Slimmer DTOs: keep data only, no UI derivation or computation.** The removals are in §2 (old `SDS_StockEntry.preserved` / `displayKey`, old `SDS_StockVitals.Capture*`, etc.); weighted averaging belongs to Core's `SDS_StockRules` (ModuleResponsibilities.en.md §3). Rationale: computation is rules, and rules stay out of Contracts (ModuleRules.en.md 6.1); UI-derived fields must not become save contracts either.

From now on the old framework serves only as a historical archive; maintaining this module requires no further reading of it.

## 4. Historical notes on legacy save nodes and fields

> The nodes, default values, and save-loading fallbacks below are the historical basis for save compatibility, explaining why these fields are kept; signatures and the locking basis follow ModuleInterfaces.en.md §8, `Interfaces.en.md` A.8, and `Rules.en.md` C6.

| Type | Nodes (LookMode) | Notes |
|---|---|---|
| `SDS_StockKey` | `defName`, `stuffName` (Value); legacy nodes `thingDef`, `stuff` (Value, default `"null"`, **read-only**); `qualityInt` (Value, default -1); `preserved` (Value, default false) | Saving writes the new fields only (legacy nodes stopped being written as of 2026-11-16); when loading, the legacy nodes are used only if the new fields are empty; `TryResolve` after reading. |
| `SDS_StockEntry` | `key` (Deep), `count` (Value, 0), `avgHitPoints` (Value, -1), `avgRotProgress` (Value, -1) | -1 means the old save had no record; treat as full hit points / unrotted. |
| `SDS_Reservation` | `jobLoadID` (0), `key` (Deep), `count` (0), `pawn` (Reference) | After loading, Core audits and cleans up negative ids / empty keys / invalid pawns. |
| `SDS_ReclaimSave` | `policy` (default `JobTarget`), `jobLoadID`, `ownerLoadID`, `tick`, `hand`, `things` (Reference), `counts` (Value) | `PostLoadInit` pads the `counts` length. |
| `SDS_TransportLoadSave` | `containerThingID`, `rowIndex` (-1), `defName`, `stuffName`, `qualityInt` (-1), `preserved`, `units` | Saves no `Thing` references at all. |
| `SDS_SidebarUIState` | `collapsed` (Value), `scroll`, `hasSelection`, `selectedKey` (Deep), `selectedPreserved` | `PostLoadInit` calls `Normalize`: drops empty entries, zeroes negative scroll, re-resolves the selected key. |
| ~~`SDS_ProtectedOutput`~~ | ~~`thing` (Reference), `lordLoadID`, `createdTick`~~ | Retired (2026-11-16); new saves hold this in the unified reclaim ledger. |
| `SDS_Settings` | `gridCapacity`, `capacityPerArray`, `corePower`, `arrayPower`, `portPower`, `excludeGridFromWealth`, `highestStoragePriority`, `forceWeaponApparelVirtual`, `endpointStoragePriority`, `separateByQuality`, `separateByStuff` | All `Scribe_Values`; defaults come from `SDS_Config`. |
| `SDS_WithdrawDeliveryPolicyDef` | No `ExposeData` | Reads `jobDefName` / `driverType` / `mode` from XML. |

## 5. UI state boundaries

- The search term lives only in the window's `QuickSearchWidget`; it is never written into `SDS_SidebarUIState` and never persisted; after loading a save the search is empty.
- `collapsed` is written back at the moment of the group collapse / expand click; `scroll` is written back after each draw of the stock scroll area finishes, and when the search term changes it is zeroed first and then written back.
- `hasSelection` / `selectedKey` / `selectedPreserved` are written back immediately when a row is clicked; during a row rebuild, if the selected key has been normalized or no longer exists, they are also written back at once (the latter clears all three).
- Loading restores references only: `PostLoadInit`'s `Normalize` only drops empty entries, zeroes negative scroll, and runs `selectedKey.TryResolve()`; windows and UI objects such as rows / member tables are rebuilt when opened and are never deserialized from the save.
