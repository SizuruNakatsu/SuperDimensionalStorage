# SDS.Core · Rules

> Positioning: the checkable checklist of "what this module must obey". The rule text takes ModuleRules.en.md 6.3 and §5, and ModuleInterfaces.en.md §3, as authoritative; this document only records the check items.

## 1. Forbidden Symbols (consistent with `CheckAssemblyBoundaries.ps1` §8)
The following symbols must not appear in `SDS.Core/**/*.cs` (comment lines starting with `//` are excluded from the check):

| Category | Forbidden symbols |
|---|---|
| Patches / behavior | `Harmony`, `JobDriver`, `Toil`, `ThinkTree`, `WorkGiver` |
| Storage / search | `SlotGroup`, `Reachability`, `ListerThings`, `GenClosest`, `ResourceCounter` |
| Spawning / placement / enumeration | `IHaulDestination`, `GenPlace`, `GenSpawn`, `Find.`, `.mapPawns` |
| UI / cross-module | `Window`, `ITab`, `Widgets.`, `SDS_ModCompat`, `SDS_Vanilla` |

How to check: `powershell -File Tools/CheckAssemblyBoundaries.ps1`; any single-line hit exits non-zero. Source: ModuleRules.en.md 6.3; script §8.

## 2. The Sole `IThingHolder` Exception
- `IThingHolder` may appear only inside `SDS_Ledger.cs`: `private sealed class SDS_PreservedHolder`.
- `ParentHolder` returns `null`; `GetChildHolders` must be empty; `GetDirectlyHeldThings` returns only `preservedOwner`; `PreservedOwner` is not exposed, and no `Scribe_Deep` is used.

How to check: the script §8 filename whitelist; `LedgerStructureTests.PreservedOwner_DoesNotExposeChildren`. Source: ModuleRules.en.md §5; ModuleInterfaces.en.md §3.1.

## 3. Writing `carryTracker` / `inventory.innerContainer` Is Forbidden
- Writing `Pawn.carryTracker` and `Pawn.inventory.innerContainer` is forbidden; reads allow only `Count` / `InnerListForReading` / `Contains` (Core currently uses none).
- Return fallbacks (inventory / carried / ground) always go through `ISDS_DeliverySink`; Pawn containers are never manipulated directly.

How to check: `CheckAssemblyBoundaries.ps1` does not build in this check yet (to be added); code review plus a full-directory search for `carryTracker|inventory.innerContainer` should yield 0 hits. Source: ModuleRules.en.md 6.3; `SDS_ReturnUtility.DeliverFallback`.

## 4. internal Boundary
- Top-level `public` is allowed only for: `SDS_Core`, `ISDS_LedgerRead`, `ISDS_LedgerHost`.
- `SDS_Ledger` / `SDS_StockData` are implementation types (internal); all their write methods are `internal`; every other type is `internal` and must not be referenced by Vanilla / Api / ThirdModCompat; `InternalsVisibleTo` grants only `SDS.Rules` (since P3; carried through P4–P5).
- Before a facade adds a `public` method, ModuleInterfaces.en.md §3.2 and ModuleResponsibilities.en.md §2 must be updated first.

How to check: script §4 type-level whitelist + §5 friend grants (only `SDS.Core → SDS.Rules` is registered); `CheckCompatSurface.ps1` checks the compat-layer reference surface. Source: ModuleRules.en.md §5; ModuleResponsibilities.en.md §5.

## 5. Transactions / Returns / Mirrors (migrated to SDS.Rules)

Since P3, the flow constraints for pickup transactions, returns, and mirror withdrawal take `SDS.Rules/Rules.en.md` §1 (1.7–1.9) as authoritative. Core keeps only the ledger-side invariants:

| Rule | Concrete constraint | How to check |
|---|---|---|
| Availability / external debits | Always `total − reserved` clamped to `0`; forbidden categories, or G off, read as zero on the read side; `TryDebitVirtual` does not trim others' reservations, and `Reserved > Total` is legal | `LedgerTests.*`; `TransactionTests.Transaction_RolledBack_WhenExecuteFails`; E2 `AssertAvailableWithinTotal` |

Source: W21 / W22 / W27; the text of W21 / W22 / W27 takes `SDS.Rules/Rules.en.md` §1 as authoritative.

## 6. Return null Rules (migrated to SDS.Rules)

Since P3, the handling semantics for "return to the ledger first" and for `map == null` / `pawn == null` / `fallback == None` take `SDS.Rules/Rules.en.md` §1.8 as authoritative; `ReturnTests` moved to `SDS.Rules.Tests` with the P5 test split.

## 7. The Three Steps of Mirror Withdrawal (migrated to SDS.Rules)

Since P3, the debit → physical conversion → restock order and the plan-field constraints of mirror withdrawal take `SDS.Rules/Rules.en.md` §1.9 as authoritative.

## 8. Supply (migrated to SDS.Rules)
Since P2, supply def selection, `required` / `fromOwn` / `shortfall`, reservation and own-pool debits, and the injection semantics of the medical gate's `extraAllows` have moved with `SDS_BillSupplyPlanner` into `SDS.Rules/Rules.en.md`. Core no longer defines supply rules and keeps only the ledger capability.

## 9. Static Cleanup (0 Core items + 4 Rules items)

`SDS_Core.RegisterAll()` registers only Core's self-check provider, not static cleanup items. The 4 cleanup items of the adjudication / flow layers are registered by `SDS_Rules.RegisterAll()`:

| # | Cleanup item | Cleanup content |
|---|---|---|
| 1 | `SDS_EndpointContractCache.Reset` | The endpoint contract version clock |
| 2 | `SDS_ProducedThingMarker.Reset` | Just-materialized source markers |
| 3 | `SDS_ImplantUtilityImpl.ResetChipCache` | The chip level cache |
| 4 | `SDS_StructureRegistry.Clear` | The current save's structure facts (core / anchor / capacity device) |

The reclaim ledger went to Vanilla with C1, and its `Reset` is registered by `SDS_Vanilla.RegisterAll`.

`ResetAll` clears only process-level static caches, not the current save's `SDS_Ledger`; process-level constants (Def query results, Provider instances) are not registered.
`SDS_StockRules.rebuildCache` / `gearRebuildCache` (Rules) are Def query results: the keys are `ThingDef`, and the values depend only on static def data and the `ignoreArt` dictionary choice; `ForceWeaponApparelVirtual` only decides which of the two caches is used, so they are not registered.

How to check: review both `RegisterAll`s; `SDS.Rules.Tests/Support/RulesTestKit.ResetRulesStatics` clears the structure registry and the endpoint / marker / chip caches; `CoreHardeningTests.Structures_ResetAll_ClearsAvailabilityAndCapacity`; the startup log "登记 N / 清理 M / 失败 K". Source: ModuleRules.en.md §3 (E1); ModuleResponsibilities.en.md §5.

## 10. Save-Node Rules
The node names and LookModes of `SDS_Ledger.ExposeData` are frozen, verbatim:

```csharp
Scribe_Values.Look(ref dataVersion, "dataVersion", 1);
Scribe_Collections.Look(ref stockEntries, "stockEntries", LookMode.Deep);
Scribe_Collections.Look(ref forbiddenScratch, "forbiddenKeys", LookMode.Deep);
Scribe_Collections.Look(ref reservations, "reservations", LookMode.Deep);
Scribe_Collections.Look(ref preservedScratch, "preservedThings", LookMode.Deep);
```

- When the `dataVersion` node is missing, fall back fixedly to `1`; `CurrentDataVersion` must not be used as the default — otherwise a future version bump would misjudge old saves without a version field as new and skip migration.

- List fields must be null-filled before entering the save / load branch and log `存档/旧档字段补齐` (Warn); a missing node producing `null` is a normal old save.
- Saving writes only entries with `HasIdentity` and a count > 0; on load, `TryResolve` runs first, and entries that fail to resolve are kept as unresolved.
- `preservedThings` is a `LookMode.Deep` staging list; after load it is handed back to `preservedOwner`; never `Scribe_Deep` the owner itself.
- On load the ledger performs only structural cleanup (`PruneMalformedReservations`); the reservation audit is triggered by Vanilla's `SDS_GameComponent_Grid.FinalizeInit` calling `SDS_Core.AuditReservations(ownerAlive)`; the ledger must not be rebuilt during load.
- **C8 migration (2026-11-15)**: as of this version, `sdsReclaim` is read and written exclusively by `SDS_GameComponent_Grid` (a sibling of this ledger's nodes); the ledger neither reads nor writes that node. The old save's same-named node lives in the same place, so the component's single read path covers both old and new saves; if the ledger also read it, the two `ExposeData` passes would each read it once and produce duplicate reclaim entries.

How to check: `LedgerStructureTests.ExposeData_UsesLockedNodesAndLookModes`; code review of `ExposeData`. Source: ModuleInterfaces.en.md §3.6; ModuleRules.en.md §5, §3 (W26).

## 11. Rule Anchors (authority per RuleIndex.en.md)

Per DesignPrinciples.en.md §2, for authority documents the "authoritative document" column of RuleIndex.en.md is the sole arbiter; this section only registers this module's anchors and does not restate rule text.
The only rule whose authority is this section is W34 (the migration mechanism); W26 takes `SDS.Vanilla/Rules.en.md` §10 as authoritative, E1 / E2 take DiagnosticsCoverage.en.md §2·M as authoritative; adjudication-layer / flow-layer rules (W21 / W22 / W27 / W33 / W35 / W49 / W56 / R15 / R17) take `SDS.Rules/Rules.en.md` §1 as authoritative since P3–P4, and this section does not restate them.

| ID | Authoritative document (rule index) | Anchor in this module | Description |
|---|---|---|---|
| W26 | `SDS.Vanilla/Rules.en.md` §10 | `SDS_Ledger.ExposeData` | Field fill-in + `TryResolve` + keep unresolved |
| W34 | This document §11 | `SDS_Ledger.ConvertPreservedGearToVirtual(Func<Thing,bool>)` (migration mechanism) + `SDS_StockRules.CanMigratePreservedGearToVirtual` (Rules criteria) | Migration-only booking, bypassing the capacity / filter / availability gates |
| E1 | DiagnosticsCoverage.en.md §2·M | `SDS_Core.RegisterAll` 0 cleanup items + `SDS_Rules.RegisterAll` 4 cleanup items | See Rules.en.md §9 |
| E2 | DiagnosticsCoverage.en.md §2·M | Calls `SDS_Diag.Assert*` (implementation lives in Diag) | `AvailableWithinTotal` / `TransactionStage` / `TransactionTerminal` |

## 12. Supplementary Semantics (still in force, no standalone rule IDs)

> This section keeps only ledger-side supplementary semantics; flow / adjudication items 12.3–12.25, 12.28, 12.32, 12.33 have moved to `SDS.Rules/Rules.en.md` §5 and are not restated here.

### 12.1 Post-load transaction disposition
> The transaction state machine and the return implementation have lived in `SDS.Rules` since P3; this section keeps only the ledger-side semantics (reservation snapshot, release by owner id, liveness delegate); for flow constraints see `SDS.Rules/Rules.en.md` §1.7–1.9.
- Reservation only, no things: release the reservation directly.
- Materialized and the things still exist: prefer returning them to the ledger; if the return fails, deliver on the ground.
- Destroyed: do not add back, never rebuild by def; moved away by a third party: treat as delivered.
- Job / dialog gone: release the reservation and return, leaving no dangling transactions.
- Executor exception: every exception thrown after a successful `Begin` must `Rollback`; materialized things are returned per channel; reservations must not be left to the next load.
- Reservation audit: `id ≤ 0` (negative = residue of an Instant transaction, 0 = old save / corrupted owner) is always cleaned up; positive ids are re-checked by `SDS_Core.AuditReservations(Func<int,bool> ownerAlive)` with the caller-supplied liveness delegate, and returning false releases the reservation (liveness facts are composed on the Vanilla side from `ISDS_ReclaimWorld.JobExists` and `Pawn.Destroyed`). Executed once after load (Vanilla `FinalizeInit`) and once per 250 ticks at runtime; the ledger provides only a reservation snapshot and release by owner id, and does not read Job / Pawn liveness semantics.

### 12.2 `SDS_ReclaimOutcome` semantics and merging
- Three values: `Abandoned` (abandoned), `Consumed` (consumed), `Returned` (returned).
- Multiple reclaim results for the same thing merge by `Abandoned > Consumed > Returned`.
- Hard constraint: reclaim results serve only reclaim-side bookkeeping and must never drive transaction terminal states in reverse.

### 12.10 Live true bodies and counting
- The sole liveness test is §12.31: not destroyed and content complete (`SDS_StockData.HasLiveContent`); anything failing it is handled as a zombie reference.
- Zombie references do not count into `Total`.
- Count deduplication lives solely in `SDS_Ledger.EnsurePreservedIndex`: the same reference counts once, and the same `thingIDNumber` counts once; `GetPreservedCount` / `PreservedSnapshot` / `AppendPreservedThings` share the same source.

### 12.16 Destination candidates and ordering (implementation migrated to SDS.Rules)
The rule text of candidate enumeration, ordering, and tie-breaking has moved with `SDS_EndpointResolver` into `SDS.Rules/Rules.en.md`; Core keeps only structure facts and the endpoint version clock.

### 12.26 Read-side invariants R1–R14 (still in force)
- R1 reads are read-only; R2 same candidates + same projection = same valid cell / distance / storage state; R3 conditional ordering keys (priority → distance → tieBreaker → thingIDNumber); R4 ordering distance uses the nearest access point and does not mix in reachability — walking takes the nearest reachable point separately; R5 source reachable = any access point, destination reachable = cell / standing cell, never swapped; R6 `Available = Total − Reserved`, with the key level only a gate and `Execute` consuming the reservation; R7 Total reads the ledger / live true bodies in `preservedOwner`, never mirror `stackCount`; R8 snapshots are versioned, and `CanReach` / `ReservationVersion` do not enter snapshots; R9 a snapshot is not a reservation — transactions re-verify before execution; R10 candidates deduplicate by `Thing` reference; R11 holder read depth must be explicit (single-level `GetDirectlyHeldThings` vs recursive `GetChildHolders`); R12 saves store only verifiable state — audit on the first tick after load, never rebuild by def; R13 `Unresolved` is kept, supply counts zero, never deleted; counts and vitals (HP / rot) are kept together, auto-restoring when the def returns; R14 display aggregation keys never enter the read path — a row resolves to real keys only via `TryNormalizeRowKey`.

### 12.27 One owner per true body (writes and load)
- At any moment a true body has exactly one owner: `preservedOwner` / endpoint `inputBuffer` / map / container are mutually exclusive, four-way.
- Ownership transfer on booking has exactly one implementation: `SDS_Ownership.TryTransferTo` (spawned: into the container first, then `DeSpawn` after success; zero side effects on failure; held goes through Vanilla `TryTransferToContainer`); **directly clearing `holdingOwner` is forbidden**, and so is each entry doing its own `Remove` with a `TryAdd` patched in elsewhere afterwards.
- `ISDS_LedgerRead.IsPreservedThingId(int)` is the sole ID-level test; at load time, ordinary containers such as endpoint buffers and pawn inventory / carried items uniformly use it to drop duplicate copies "with the same thingID as a live preserved true body" — no Split, no Destroy, no count changes.
- Equipment / apparel slots are not auto-removed (to avoid changing the Vanilla "worn" presentation); a hit only logs a warning and needs manual handling.
- The ledger's own load deduplicates by `thingIDNumber` before enqueueing; that test serves only load-time deduplication and audit, and must not be used for ownership adjudication on the normal write path (normal writes go through `SDS_Ownership`).

### 12.29 Derived indexes and capacity semantics
- `keysByDef` (virtual keys by def), `preservedByKey` / `preservedKeysByInnerDef` (preserved by key / inner def), and `categoryKeys` (the capacity category set) are all derived indexes of `SDS_Ledger`, rebuilt only by the first query after a `StockVersion` change.
- Preserved derived indexes (`preservedByKey` / `preservedKeysByInnerDef` / `categoryKeys`) deduplicate by "reference + `thingIDNumber`" (one true body per ID); `GetPreservedCount` / `PreservedSnapshot` / `AppendPreservedThings` must share the same source; UI / adapters are forbidden from deduplicating on their own or computing totals separately.
- Def-level queries have two established semantics that must not be "unified" with each other: `GetAvailableCountForDef` and category capacity go by the **true body's def** (minified = `InnerThing.def`); `LedgerTotalForDef`, the resource bar injection, and `SDS_API.GetPreservedAvailableCount(ThingDef)` go by the **shell def** (aligned with Vanilla `ThingsOfDef`). Changing semantics is evaluated per L2.
- The read side may query only through these indexes: `CopyKeysForDef` / `CopyPreservedKeysForDef` / `GetPreservedCount` / `UsedCategoryCount` / `HasCategory`; UI / adapters / compat layers must not build a second cache. `UsedCategoryCount` / `HasCategory` are internal indexes of `SDS_Ledger` and do not enter `ISDS_LedgerRead`.
- `UsedCategoryCount` is the sole source of the capacity semantics; `HasCategory` is the sole test for "this category already exists" in capacity. Both share the same `categoryKeys` (the preserved side is deduplicated by reference + thingID, with the sole test in `AppendLivePreservedCategories`).
- Index rebuilds happen only after the stock or the preserved-true-body set changes — a one-time O(K+P) cost; they must not degrade into a full scan per query.
- When the category grouping switches (`separateByQuality` / `separateByStuff`) toggle, `NotifyCapacitySettingsChanged()` must be called to drop the indexes; indexes must not be reused across semantics.

### 12.30 The sole atomic implementation of thing → virtual

- The three steps "detach from the original container → book → destroy" are implemented only in `SDS_Ledger.TryConvertThingToVirtual`; deposit (`SDS_Rules.TryDeposit`) and return (`SDS_ReturnUtility.ReturnVirtual`) both call only it, and every entry point is forbidden from writing it again.
- If destruction did not take effect (`!thing.Destroyed`, including `Destroy()` throwing), the booking must be rolled back and the thing re-attached to the original container; the rollback amount is clamped to the current stock — other stock under the same key must not be debited along with it.
- Exception: booking into the preserved channel does not destroy the thing — it goes through `TryDepositPreserved`; the W34 stock migration is a separate migration-only booking and is not covered by this item.

### 12.31 The sole test for a live preserved true body

- "Live" = not destroyed **and** content complete (`SDS_StockData.HasLiveContent`: corpse with inner pawn, minified with inner thing); the sole implementation is `SDS_Ledger.IsLivePreserved`.
- Semantics that must share the same source: `GetPreservedCount` / `PreservedSnapshot` / `AppendPreservedThings` / `EnsurePreservedIndex` / `EnsureCategoryKeys` / `IsPreservedThingId` / `Tick` / `AppendPreservedInvariantReport` / `AppendLedgerInvariantReport`.
- `IsPreserved(Thing)` answers only "is it held by this ledger" and does not test whether the content is usable; it must not replace the liveness test.
- At runtime a thing may have only one owner: when the reference is already in `preservedOwner`, or a live true body with the same `thingID` exists, deposit / return / pending-restore retries are idempotently rejected (sole test: `SDS_Ledger.IsPreservedIdentityOccupied`) — no copying, no channel conversion, no placing on the ground.
- The generic virtual path (`TryConvertThingToVirtual`) must not take over true bodies already held by the ledger; preserved → virtual is allowed only via the W34 dedicated migration (`ConvertPreservedGearToVirtual`), to avoid bypassing the W34 switch / reservation / category gates.
- The sweep (250 ticks) must remove both "destroyed" and "content-invalid" true bodies; removing a content-invalid one logs a `Warn`.

### 12.34 The forbidden set is independent of the stock

- Category forbidding is player intent and is not lost when the stock hits zero: load no longer trims forbidden keys with "no stock".
- Only an explicit player un-forbid (`SetForbidden(false)`) removes a key from the set; diagnostics / capacity / availability semantics are unaffected.

### 12.35 Diagnostics and unresolved keys

- Unresolved def entries follow R13 "keep, supply zero, never delete": they do not participate in the `total − reserved` comparison and are reported separately as "unresolved N entries"; HP / rot (`vitals`) are likewise kept — `RotProgressIllegal` performs only structural validation (NaN / infinity / `< -1`) for unresolved defs, and `MergeVitals` allows merging under an unknown def.
- The preserved self-check also verifies: owner consistency, thingID uniqueness, content completeness, and 0 pending-restore entries after load.
- The full ledger self-check `AppendLedgerInvariantReport` depends on no host facts; it checks stock / vitals / `keysByDef` / `preservedByKey` / `preservedKeysByInnerDef` / `categoryKeys` / reservation structure / pending restores / `dataVersion`; it is called by `SDS_CoreDiagnostics.AppendReport`.
- Independent verification boundary: `SDS.Core.Tests` references only Contracts / Diag / Core and covers the ledger's random count conservation, invariants, unresolved vitals round-trips, capacity deduplication, and the reservation audit; rule / flow / Scribe round-trip tests live in `SDS.Rules.Tests`; `SDS.Tests` does cross-module integration only.

### 12.36 Core's input boundary (de-module-coupling)

- Core does not directly reference any SDS module and reads no host slots; the following facts are consumed by Rules / Vanilla as query fields / plan fields / delegates:
  - Endpoint priority: `SDS_EndpointQuery.priority` (filled in by factory methods; `default` queries call `ApplyHostPriority()`).
  - Mirror physical: `SDS_WithdrawPlan.physical` (passed explicitly by Rules' `TryTakeMirror`).
  - Medical gate: `AllowsDef(..., extraAllows)` and the planning methods' `extraAllows` are composed by Vanilla / Api from `SDS_MedicalGate.Allows`.
  - Third-party preserved facts: `SDS_Rules.SetExternalPreserveProvider(Func<Thing,bool>)` is wired by Bootstrap; Rules holds only the delegate.
- `Ports` / `DepositFilter` / `Delivery` are all consumed by `SDS.Rules` (migrated out in P2–P4); Core's inputs are down to the ledger method parameters and `SDS_StockData` data.
- External input may only "tighten" Core's criteria (e.g., additionally requiring preservation); loosening the channel / availability / capacity rules is not allowed.

### 12.37 Reclaim belongs to Vanilla (C1 / C8)

- The reclaim ledger (`SDS_ReclaimRegistry`) and the Job / Lord / caravan-composition facts belong to `SDS.Vanilla` as a whole; Core no longer holds reclaim entries and no longer reads `ISDS_ReclaimWorld`.
- Core keeps exactly one generic capability:
  - `AuditReservations(Func<int,bool> ownerAlive)`: releases stale reservations by owner id, with liveness supplied by the caller.
- Returns (`ReturnThing` / `ReturnVirtualCount` / `ReturnThings`) belong to `SDS_ReturnUtility` of `SDS.Rules`, and Core does not provide them; on failure they deliver via the `Ground` / `PawnInventory` fallbacks and never swallow items.
- Save: `sdsReclaim` is read and written entirely by `SDS_GameComponent_Grid`; Core neither reads nor writes it (C8).
- The reclaim sweep (`Sweep`) is driven every 250 ticks by `SDS_GameComponent_Grid.GameComponentTick`; `SDS_Ledger.Tick` no longer drives reclaim.
