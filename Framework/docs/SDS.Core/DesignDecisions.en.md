# SDS.Core · Design Decisions (why it is done this way / pitfalls hit)

> Positioning: records the differences and pitfalls concerning this module in the old implementation (frozen, kept only as a historical archive), plus the conclusions on "why the migration adapted Vanilla adjudication this way".
> This document answers only "why" and defines no new rules; checkable items are in [Rules.en.md](Rules.en.md) §1–§10, and the authoritative text of the rule IDs is in [Rules.en.md](Rules.en.md) §11.
> Maintaining this module requires no further reading of the old implementation; the reasons and conclusions are fully preserved here.

## 1. Historical Decisions and Pitfalls

### 1.1 Old save nodes and fields

The old ledger was written entirely in `SDS_GameComponent_Grid.ExposeData`; the new framework splits it by ownership into "Core nodes" and "Vanilla nodes",
**with all node names and LookModes unchanged** (node definition summary in ModuleInterfaces.en.md §8).

| Node | Field / type | LookMode | New owner |
|---|---|---|---|
| `dataVersion` | `int`, old `CurrentDataVersion = 1` | `Value` | Core `SDS_Ledger` |
| `stockEntries` | `List<SDS_StockEntry>` | `Deep` | Core |
| `forbiddenKeys` | `List<SDS_StockKey>` | `Deep` | Core |
| `reservations` | `List<SDS_Reservation>` | `Deep` | Core |
| `preservedThings` | `List<Thing>` (load-time staging; at runtime `preservedOwner` is authoritative) | `Deep` | Core |
| `sdsReclaim` | `List<SDS_ReclaimSave>` | `Deep` | Vanilla (C8: both read and write are owned by `SDS_GameComponent_Grid`; Core neither reads nor writes) |
| `depositFilter` | `ThingFilter` | `Deep` | Vanilla |
| `coreStoragePriority` | `int`, old-save default `-1` | `Value` | Vanilla |
| `introNoticeShown` | `bool` | `Value` | Vanilla |
| `sidebarUI` | `SDS_SidebarUIState` | `Deep` | Vanilla |
| `sdsMirrors` | `List<Thing>` | `Reference` | Vanilla |
| `transportLoadRequests` | `List<SDS_TransportLoadSave>` | `Deep` | Vanilla |

Old `SDS_ReclaimSave` fields: `policy`, `jobLoadID`, `ownerLoadID`, `tick`, `hand`, `things` (`Reference`),
`counts` (`Value`); the old `policy` defaulted to `JobTarget`. The old `sdsProtectedOutputs` / `MapComponent_SDS_ProtectedOutputs` were retired on 2026-11-16
per SDS.Vanilla/CompatibilityRetirement.en.md (serving only saves from before 2.4 / 2026-10-26 21:54) and are no longer merged into the reclaim ledger.
From C8 on, the holding, serialization, and load-time auditing of the reclaim ledger are all in Vanilla; `sdsReclaim` sits as a sibling of this ledger's nodes,
read and written exclusively by `SDS_GameComponent_Grid`; Core's `SDS_Ledger` neither reads nor writes that node.

### 1.2 Design-document provenance of the rule IDs

Rule ID provenance: W21 / W22 / W26 / W27, W33 / W34 / W35, S16, R15 / R17 are in RuleIndex.en.md and [Rules.en.md](Rules.en.md) §11;
E1 / E2 are in DiagnosticsCoverage.en.md §M.

## 2. Old Implementation → New Implementation: Differences and Pitfalls

| # | Difference | Pitfall / note |
|---|---|---|
| 1 | Rule classes renamed and merged: old `SDS_DepositRules` (`SDS_Stock.cs`) + key factory + vitals + materialization → a single `SDS_StockRules` (after P4 split into Core `SDS_StockData` + Rules `SDS_StockRules`) | Old comments and rule-ID anchors say `SDS_DepositRules`; the anchors of `CheckRuleIds.ps1` have been renamed — do not reintroduce the old name when editing code |
| 2 | The ledger was split out of `GameComponent` | Old `SDS_GameComponent_Grid : GameComponent, IThingHolder`; the new `SDS_Ledger` has a private holder. Any old pattern of "treating the grid as a holder" no longer works |
| 3 | Reservation audit timing: the old `reclaimAuditPending` deferred it to the first tick; since 2026-11-15, Vanilla's `SDS_GameComponent_Grid` calls `SDS_Core.AuditReservations(ownerAlive)` in `FinalizeInit` and on the 250-tick cycle, and the ledger's `ExposeData` does only structural cleanup | The liveness fact is passed in by the caller as a `Func<int,bool>`; Core no longer reads `ISDS_ReclaimWorld`; when the liveness delegate is null, positive-id reservations are kept untouched — a "silent skip" rather than an error. The host must finish injection before the ledger is constructed (ModuleInterfaces.en.md §4.2) |
| 4 | Vitals computation returned home | Old `SDS_StockVitals.CaptureHitPoints/CaptureRotProgress` hung on the DTO; now it lives only in `SDS_StockData` (`SDS_StockRules` before P4), with Contracts keeping only the fields. Vanilla must not compute weighted averages itself |
| 5 | Merging preserved entries with the same key was deleted | The old `TryMergePreserved` was not migrated; `preservedByKey` / `preservedMembers` remain as lazy derived indexes (see §5.4), and one key can still map to multiple `Thing`s — do not assume "one key, one thing" |
| 6 | Display-thing registration changed from owner reference counting to `key → Thing` | The old Trade / Caravan / Transport three owners could share one display thing; the new table is single-valued per key, with later registration overwriting the earlier value, and multi-owner semantics are guaranteed by Vanilla. That table moved to Vanilla's `SDS_OutputUtility` along with the display-thing factory; Core keeps only the source marker |
| 7 | Resolver split | Old `SDS_WithdrawResolver.Decide` / `ResolveMode` were in Core; since P2, `SelectEndpoint` is in `SDS.Rules`, with mode and priority save values in Vanilla. Api / Vanilla compose the plan; Rules does not re-select |
| 8 | The three endpoint cache tables were deleted | The old per-map registry, 30-tick reachability cache, distance cache, and the 10-cell reference-radius approximation were all deleted; new candidates come from `ISDS_PortProvider`, and distance is computed live by `RankDistanceSquared`. `SDS_EndpointContractCache` keeps only the version clock |
| 9 | The preserved transaction entry narrowed | Old `ExecutePreserved(Predicate<Thing> validator)` was deleted, keeping only `ExecutePreservedExact(List<ThingCount>)`; `SDS_Ledger.TryWithdrawPreserved` was deleted (no callers in the facade or internals) |
| 10 | The mirror external-consumption path | Old `TryConsumeMirror` (destroy-confirmation debit, no reservation) moved into `SDS_Rules.TryConsumeMirror` (P3); `TryTakeMirror` remains only for pickup and must not substitute for "destroy confirmation" |
| 11 | Return fallbacks no longer write the Pawn directly | Old `Return` directly did `inventory.innerContainer.TryAdd` / `carryTracker.innerContainer.TryAdd`; now everything goes through `ISDS_DeliverySink`, and Core is forbidden from writing those two containers |
| 12 | The `SDS_WithdrawSave` type disappeared | The save node remains; at load the ledger calls only `PruneMalformedReservations`; load-time auditing of reclaim entries (`SDS_ReclaimRegistry.AuditRestored`) went to Vanilla with C1, and the reservation audit is triggered by the host via `SDS_Core.AuditReservations(ownerAlive)` — do not restore the old class |
| 13 | Job-target auto-return moved into Vanilla | `SDS_ReclaimRegistry.TrackJobTarget` / `TrackHand` / `ReclaimJobTarget` / `ReclaimHand` / `ForgetJobTarget` are now called by Vanilla directly (no longer through the `SDS_Core` facade after C1); `Patch_SDS_ReclaimJobTargets` drops the registration on success and returns ground targets and carried items on failure / interruption (W27) |
| 14 | 2026-11-15: the reservation-audit policy moved out of the ledger | The ledger originally read `Pawn.Destroyed/Map` directly and asked `ISDS_ReclaimWorld.JobExists`, effectively baking Job / Pawn liveness semantics into the ledger. Now the ledger provides only `CopyReservations` + `ReleaseReservation(ownerId)`, with liveness testing and release timing in `SDS_Core.AuditReservations(Func<int,bool> ownerAlive)` (the liveness delegate is provided by Vanilla, composing `JobExists` + `Pawn.Destroyed`); `jobLoadID ≤ 0` is always invalid. Side effect: load no longer kills in-transit reservations of pawns that are "alive but off-map" by mistake (judged only by `Destroyed`) |

## 3. Design Trade-offs

> This section collects the previously standalone "implementation rationale" conclusions: why only Core keeps a copy of the rules, why the ledger uses a private owner, and why internal is enforced.
> Old-implementation differences are in §2 of this document and are not repeated here.

### 3.1 Why Core is the sole rule center

- Basis: DesignPrinciples.en.md §2 Principle 3 — logical uniqueness = a rule defined once + every entry point calling it; at the documentation level, the same rule may be defined only once.
  At the code level, ModuleRules.en.md §1 explicitly does not pursue uniqueness of the concrete function, only that every entry point ultimately lands on the same implementation.
- In the old implementation all types shared one assembly, so rules could be called directly by any entry point — and re-copied by any new entry point; after the refactor, the ledger / quantity semantics have exactly one implementation in Core, and gameplay rules exactly one in `SDS.Rules` (implementation locations are in each README's "Responsibilities" table).
- The concrete costs of not converging:
  - Quantity-semantics forks: the old `SDS_Availability` comment already stated "the total-count semantics are given only by `grid.GetAvailableCount(key)`"; once Vanilla or a third party computed `total − reserved` separately, availability, trade budgets, and loading clamps would contradict one another.
  - Shortfall-calculation forks: S16 requires that def selection, required / fromOwn / shortfall, and reservations be implemented only in `SDS_BillSupplyPlanner` (in `SDS.Rules` since P2); adapters provide facts only.
  - Priority forks: PublicAPI.en.md §4.2 forbids adapters from deciding destination priority, filtering, capacity, or ties on their own, and forbids maintaining stock caches or shadow counts.
  - Write-path forks: ModuleRules.en.md 6.4 / 6.6 forbid Vanilla from "inventing CRUD rules" and ThirdModCompat from "adding debit / job-assignment / rollback rules" respectively.
- Conclusion: Core (ledger / quantity semantics) and `SDS.Rules` (gameplay / semantic adjudication) are the only positions where Vanilla and third parties can share the same semantics; this is also the prerequisite for `CheckAssemblyBoundaries.ps1`'s type-level whitelist check (ModuleRules.en.md §10).

### 3.2 Why the ledger uses a private `ThingOwner` instead of the GameComponent implementing `IThingHolder` itself

- Old implementation: `SDS_GameComponent_Grid : GameComponent, IThingHolder`, with `GetDirectlyHeldThings()` returning `preservedOwner` directly
  (old `SDS_GameComponent_Grid`, lines 18 and 206-225). Any holder-tree traversal could reach the mutable preserved-things container.
- New implementation: `SDS_Ledger` holds `private readonly ThingOwner<Thing> preservedOwner`, and the owner is a private nested `SDS_PreservedHolder`;
  `ParentHolder` is null and `GetChildHolders` is always empty (`SDS_Ledger.SDS_PreservedHolder`; ModuleInterfaces.en.md §3.1).
- Visibility: `GameComponent` is a public type; if it implemented `IThingHolder`, `GetDirectlyHeldThings` would become a public mutable entry; ModuleRules.en.md §5 requires `SDS_Ledger` not to expose `PreservedOwner`.
- Holder tree: `GetChildHolders` is empty, so corpse `InnerPawn` and minified-furniture `InnerThing` are never enumerated recursively into the holder tree (end of ModuleInterfaces.en.md §3.1); the save form is in Rules.en.md §10.
- Lifecycle: the ledger constructs without parameters and depends on neither `Game` nor `GameComponent`; `SDS_GameComponent_Grid` handles only creation, `ExposeData`, `Tick`, and `Attach` (ModuleInterfaces.en.md §4.1), so Core can be unit-tested outside the game environment.

### 3.3 Why internal is enforced

- The external `public` surface is only `SDS_Core` + the 2 ledger interfaces (README §4): `SDS_Core` / `ISDS_LedgerRead` / `ISDS_LedgerHost`; `SDS_Ledger` and `SDS_StockData` themselves are internal implementation types. All other types (e.g., `SDS_Ownership`) are internal;
  Vanilla / Api / ThirdModCompat must not reference them directly (ModuleRules.en.md 6.4, 6.6). Endpoint resolution and bill supply (P2), the flow layer (P3), and the adjudication layer with the public facades `SDS_Availability` / `SDS_ImplantUtility` (P4) are all in `SDS.Rules`; the reclaim ledger `SDS_ReclaimRegistry` moved to Vanilla with C1.
- `InternalsVisibleTo` grants only `SDS.Rules` (since P3; ModuleRules.en.md §5); Vanilla / Api / ThirdModCompat still reach things only through the `SDS_Core` / `SDS_Rules` facades or `SDS_API` forwarding.
- Benefit: internal types can be refactored freely without breaking external contracts; `SDS.Core.Tests` tests only the ledger (Contracts / Diag / Core), rule / flow tests live in `SDS.Rules.Tests`, and `SDS.Tests` does cross-module integration only.

### 3.4 Key trade-offs

| Trade-off | Concrete manifestation | Cost / boundary |
|---|---|---|
| Reservations are scoped per channel | Virtual reserves by key count; preserved reserves by exact true-body plan | Preserved cannot be blocked by virtual reservations (`SDS_Ledger.TryReserve` / `TryReservePreservedPlan`; ModuleInterfaces.en.md §3.1) |
| Priorities do not cache ordering results | The caller reads settings and writes `SDS_EndpointQuery.priority`; Core recomputes the ordering from the query values every time | Endpoint ordering recomputed each time, in exchange for immediate effect (`SDS_EndpointResolver.cs`; ModuleInterfaces.en.md §3.3) |

Since P3, the checkable entries for the four items "Deliver releases only the reservation", "partial success does not roll back", "the three steps of mirror withdrawal", and "return to the ledger first" are in `SDS.Rules/Rules.en.md` §1.7–1.9; not repeated here.

### 3.5 Two semantics clarified after convergence

- Version semantics split: `SDS_EndpointResolver.Version` serves only the internal reachability cache (internal); `SDS_EndpointContractCache.Version` is the distance-cache clock, exposed by `SDS_Rules.EndpointVersion` (ModuleInterfaces.en.md §3.3).
- The preserved withdrawal entry narrowed: the old `TryWithdrawPreservedWhere` candidate traversal became the exact plan of `SDS_Ledger.TryWithdrawPreservedPlan`; if a planned true body is no longer in the container, the whole entry fails and rolls back.

## 4. Explanatory Conclusions from the Old Design Documents (paraphrased)

1. **Why the virtual channel guarantees only def + stuff + quality + stack-average HP / rot**: under the B1 blacklist regime, things are virtual by default; only internally held items,
   def-level identity state, and per-instance state are preserved; the four categories (crafted goods / raw materials / food / chunks) are unconditionally virtual, prioritizing count and performance.
   Source: [Rules.en.md](Rules.en.md) §12.14, §11.
2. **Why W35 takes precedence over the four-category default virtualization**: research progress / notes / random thresholds / cooldowns are all per-instance state; after virtualized `Destroy`,
   `MakeThing` rebuilds only a default instance and progress is wiped; the criterion consumes only `ThingDef.IsStudiable`, and no per-mod / defName list is maintained.
   Source: [Rules.en.md](Rules.en.md) §11 W35.
3. **Why W33 does not directly change the B1 defaults**: almost all ordinary guns / melee weapons carry `CompProperties_Art`, and almost all ordinary apparel carries
   `colorGenerator`; deleting these two outright would change new deposits in every save. W33 confines the reduction to "switch on + weapons/apparel",
   and when off, B1 and old-save behavior are completely unchanged. Source: [Rules.en.md](Rules.en.md) §11 W33.
4. **Why stock migration goes through a dedicated booking**: W34 takes true bodies only from `preservedOwner`, books them bypassing `CanDeposit`'s capacity /
   filter / global availability gates, then `Destroy`s; ownership groups with transaction reservations are skipped, turning the switch off does not backtrack, and nothing is converted silently at load.
   Source: [Rules.en.md](Rules.en.md) §11 W34.
5. **Why rollback must be per channel**: the generic `Return` is subject to the deposit filter and category capacity — a player changing the filter inside the rollback window would drop preserved true bodies on the ground;
   and after B1, the def of an old virtual entry may already have entered the preserved blacklist, so back-deriving via `RequiresPreservedStorage(thing)` would put it in the wrong channel.
   The channel can come only from the transaction Execute path (`materializedPreserved`). Source: `SDS.Rules/Rules.en.md` §1.5 W27.
6. **Why `Reserved` may temporarily exceed `Total`**: external destruction of mirrors / things is an accomplished fact and cannot retroactively alter others' reservations;
   availability still follows `max(0, Total − Reserved)`, and other in-flight transactions fail whole-entry per the full-amount semantics and release their reservations. Source: [Rules.en.md](Rules.en.md) §11 W22.
7. **Why Vanilla pickup unifies only the "take layer"**: Vanilla withdrawal is four layers — search → take → deliver → lifecycle — each with an independent contract;
   the search layer must not materialize / debit, delivery forms come in Hand / Container / SpawnedTarget / VirtualHandoff, and cannot be unified.
8. **Why preserved does not participate in Vanilla search**: preserved true bodies do not spawn and are absent from `ListerThings`, `GenClosest`, and the holder tree;
   they can only go through dedicated bridges (DoBill with preserved ingredients, corpses into coffins, installing minified things, etc.). Source: `SDS.Vanilla/Rules.en.md` §9; S1 / S5 / S7.
9. **Why "found" does not mean "taken"**: search results are snapshots only; execution must re-verify through the pickup transaction (S6 / S7);
   the search layer neither reserves nor debits.
10. **Why the "Use" command goes only through the own port and a fixed count of 1**: gate 0 requires the target pawn's `IsPortOperational`, and building endpoints do not participate;
    failing during adjudication returns to the ledger; after handing to Vanilla's `action`, output is treated as "output to the ground" and not reclaimed; task-type JobDefs are canceled and returned.
    Source: `SDS.Vanilla/Rules.en.md` §12.5; D1 / D3.
11. **Why E2 asserts only `[0, Total]` and not `Total − Reserved`**: after external destruction, `Reserved > Total` is legal,
    and availability clamps to 0; asserting `Available == Total − Reserved` would misreport. Source: DiagnosticsCoverage.en.md §M;
    `SDS_Invariants.AvailableWithinTotal`.
12. **Why the API's pickup returns only `Thing`**: on success, the ledger debit is complete and the thing materialized, and delivery is completed by the caller; on failure, the ledger and the rollback-able things
    return to their pre-call state. Source: PublicAPI.en.md §3.5.

The old implementation is henceforth a historical archive only; maintaining this module requires no further reading of it.

## 5. Supplementary Decisions and Pitfalls

### 5.1 The cost of W33
The art `taleRef` is unrecoverable after virtualization, and shield charge gets refilled for free; both costs are trade-offs accepted deliberately, not omissions.

### 5.2 The boundary of W34
Conversion skips only ownership groups that still have reservations; and the conversion must run while the trading window is closed, to avoid reading and writing the same true bodies as an in-progress trade.

### 5.3 Preserved deposit ordering and acceptance
Ownership transfer goes only through `SDS_Ownership.TryTransferTo` (spawned: `TryAdd` first, then `DeSpawn`; held: `TryTransferToContainer`), with zero side effects on failure, no Split, and no falling back to ground placement. The acceptance criterion is "item counts unchanged across a save-load round trip".

### 5.4 Derived indexes and caches
- The old derived indexes (the preserved indexes by key / def, `keysByDef`, the capacity category set) were restored as lazy indexes inside `SDS_Ledger`: rebuilt only by the first query after a `StockVersion` change, and queries no longer do full scans.
- The old distance cache and endpoint cache tables remain unrestored; distance is computed live.
- Endpoint selection does no reachability pre-check across all candidates: reachability is tested once, only on the current winning candidate; this is the 2.5.2 behavior, and the prerequisite for not degrading into a pathfinding storm across 27 endpoints.

### 5.5 Why endpoint reachability must be tested lazily
- Reachability is Vanilla `reachability.CanReach` (region pathfinding), not pure arithmetic; pre-checking all candidates would amplify one endpoint selection into "endpoint count" pathfinding runs.
- Correct order: static gates → ordering → test reachability only on the winning candidate → if unreachable, mark it and take the next. Own port and `actor == null` are not tested.
- Reachability results are not cached: gates / danger / maps change too fast; the old version also cached only candidate handles and distances, never pathfinding results.

### 5.6 The int upper bound of reservation merging (known boundary, registered in the 2026-11-16 review)
- A reservation entry's `count` is an int: the single reservation-creation path has an `int.MaxValue` check (`TryReserveCore` / `TryReservePreservedPlan`, preferring whole-entry failure over silent truncation); but cross-call merging under the same owner and key (`AddReservation` adding directly) has no cumulative check — two legal reservations each ≤ int.MaxValue can merge-overflow to negative once the ledger count (a long) reaches about 3 billion.
- Unreachable in realistic play: a single deposit is ≤ int.MaxValue and reservations are per-Job; the backstop is `AppendLedgerInvariantReport`'s "reservation count not positive" check, availability clamps to 0 per W22, so the stock is never debited negative.
- Switching to a long is a quantity-semantics change (L2) and is not done for now.
