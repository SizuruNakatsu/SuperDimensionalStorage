# SDS.Core · Interfaces

> Positioning: the **consumption surface** and signature index of this module. The sole authority for signatures and member semantics is this module's `SDS.Core_API.cs` (E8); this document does not transcribe signatures.
> The authority for type definitions is ModuleInterfaces.en.md §3; where they conflict, the source code prevails.

## Section A: Signature Index

| Type | Declaration site (sole authority) | Purpose |
|---|---|---|
| `SDS_Core` | `Source/SDS.Core/SDS.Core_API.cs` | public facade: ledger lifecycle, used category count, forbid switch, state write-back |
| `ISDS_LedgerRead` / `ISDS_LedgerHost` | same as above | public ledger read surface / lifecycle surface |
| Remaining implementation classes (`SDS_Ledger` / `SDS_StockData` / `SDS_Ownership` etc.) | each `SDS_*.cs` | internal, not part of the external surface (ModuleRules §5) |

The adjudication layer's public surface `SDS_Availability` / `SDS_ImplantUtility`, plus `SDS_StockRules` / `SDS_StructureRegistry` / `SDS_EndpointContractCache`, have lived in `SDS.Rules` since P4; this module keeps only the ledger and the data primitives.

Member-level semantics (preconditions / post-failure state / side effects / ordering) are written in the XML comments of `SDS.Core_API.cs`; when changing a signature or semantics, that file is the single edit point.

### A.0 P3 / P4 Migration Checklist (historical record)

The following members have been provided by `SDS_Rules` since P3 / P4 and deleted from this module; this file keeps only their ledger-primitive descriptions:

| Migrated out | Current entry point |
|---|---|
| P3: `CanDeposit` / `TryDeposit` / `TryWithdraw*` / `TryReserve*` / `ReleaseJobReservation` / `TryTakeMirror` / `TryConsumeMirror` / `ConvertPreservedGearToVirtual` / `Return*` / `*Produced*` | `SDS_Rules.*` |
| P4: `Available` / `TypeCapacity` / `EndpointVersion` / `TryGetStockKey` / `RequiresPreservedStorage` / `IsLegacyUnrebuildableDef` / `CategoryKeyOf` / `TryFindWithdrawableKey` / `TryFindTradableKey` / `GetAvailableCountForDef` / `SetExternalPreserveProvider` | `SDS_Rules.*` |
| P4: `RegisterStructure` / `UnregisterStructure` / `ReconcileStructures` / `NotifyEndpointChanged` | `SDS_Rules.*` |
| P4: `SDS_Availability` / `SDS_ImplantUtility` | the same-named public facades in `SDS.Rules` |

### A.1 Read / write ports added this round (2026-11-12)

> A.1–A.3 are kept as historical records; members already migrated out in P3 / P4 follow A.0, and the current entry points live in `SDS.Rules`.

| Method | Semantics | Consumers |
|---|---|---|
| `TryFindWithdrawableKey(ThingDef, out, out)` | The sole selection rule for the production / withdrawal semantics: exact key first → largest availability → preserved matching the same semantics; forbidden categories count as zero | Vanilla loading / Api compat bridge / CE compat |
| `TryFindTradableKey(ThingDef, out, out)` | Trading / donation / launching semantics (R15): shares the same implementation and selection order as the previous row, but is not zeroed by category forbidding | Vanilla trade beacon / donation |
| `GetAvailableCountForDef(ThingDef)` | Sum of the virtual + preserved availability for the same def | Vanilla loading / Api compat bridge |
| `CategoryKeyOf(SDS_StockKey)` | Display aggregation key that zeroes quality / stuff per settings (sole implementation) | Vanilla sidebar grouping |
| `IsLegacyUnrebuildableDef(ThingDef)` | Legacy unrebuildable blacklist for the old semantics (corpses / minified / unfinished / gene carriers) | Vanilla default deposit filter |
| `CopyKeysForDef(ThingDef, List)` / `CopyPreservedKeysForDef(ThingDef, List)` | Category key index by def for virtual / preserved; the sole entry point for read-side by-def queries | Vanilla loading / Api compat bridge |
| `NotifyCapacitySettingsChanged()` | Drops the ledger's derived indexes when the category grouping semantics change | Bootstrap settings page |
| `IsPreservedThingId(int)` | Determines by `thingID` whether a "live, content-complete" preserved true body already exists; the sole ID-level test for save-load buffer deduplication and for runtime duplicate deposit / return identity gating | Vanilla endpoint buffer cleanup; `SDS_Ledger.IsPreservedIdentityOccupied` |
| `SetForbidden(SDS_StockKey, bool)` | The sole write entry for category forbidding; advances `StockVersion` on change | Vanilla sidebar lock toggle |
| `ReconcileStructures(List<SDS_StructureFact>)` | Reconciles structure registration against the live fact set — fills in, removes, and corrects; advances the endpoint version only when something changed | Vanilla 250-tick structure reconciliation |
| `TryConsumeMirror(Thing, out int)` | W22 external destruction confirmation: debits only, no reservation, debits against the current amount when short, and passes even at 0 | Vanilla mirror destruction |
| `ReturnVirtualCount(SDS_StockKey, int)` | Pure count return when destruction did not take effect | Vanilla mirror destruction Finalizer |
| `ReturnThing(..., SDS_ReturnFallback, IntVec3 cell)` | Return placement: `cell` applies only to the Ground fallback; invalid / out-of-bounds falls back to beneath the pawn / map center | Api return bridge |
| `TryWithdrawPreservedExactAdopted(int, List<ThingCount>, out, mode)` | Exact preserved withdrawal after taking over a Job's reservation; on failure it does not release the reservation | Vanilla loading Job |
| `AuditReservations(Func<int,bool> ownerAlive)` | Reservation audit: Core does not know Jobs / Lords; the liveness delegate is passed in by Vanilla | Vanilla save shell |

`HasLiveContent` once served as a `SDS_Rules` public facade; after the 2026-11-16 review it was pulled back in as an internal primitive of `SDS_StockData`, shared by Core and Rules as friends, and no longer appears on the public surface.

`SDS_StructureRegistry.Clear` is used both for new-save static cleanup (item 4 of `SDS_Rules.RegisterAll`) and for test isolation (invoked via reflection by `SDS.Rules.Tests/Support/RulesTestKit`).

`UsedCategoryCount` / `HasCategory` exist only in `SDS_Ledger` (internal derived indexes) as of 2026-11-14 and no longer enter `ISDS_LedgerRead`; the module-surface entry points for capacity semantics are `SDS_Core.UsedCapacity` / `SDS_Rules.TypeCapacity`.

### A.2 Semantics changed this round (2026-11-15)

| Method | Semantics | Consumers |
|---|---|---|
| `TryDeposit` / `ReturnThing(Virtual)` | Always goes through the ledger's `TryConvertThingToVirtual` (detach → book → destroy); if destruction does not take effect or throws, the whole entry rolls back — never keep one copy in the ledger and one in the world. When the ledger already holds the same thing: a **same-reference** return succeeds idempotently, without destroying / without placing; a **different instance with the same thingID** is rejected on return and not placed (sole test: `ClassifyPreservedIdentity`) | Vanilla deposit / return |
| `TryWithdrawPreservedExact` / `TryReservePreservedForJob` | Plan entries must be ≤ the true body's current item count; before execution, validate by key that `Σplan ≤ reservations of this transaction`; any short entry rolls back the whole entry (W21 full-amount), never silently under-delivering | Vanilla / Api |
| `TryWithdrawReservedForJob` / `TryWithdrawPreservedExactAdopted` | Exceptions on the takeover path only return the materialized things (`ReclaimMaterializedWithoutRelease`) and do not release the reservation; the reservation is cleaned up by the Job's `FinishAction` | Vanilla Job |
| `AuditReservations(Func<int,bool> ownerAlive)` | The ledger no longer tests liveness: it only provides a reservation snapshot and release by owner id; `id ≤ 0` is always invalid; the liveness delegate is passed in by Vanilla (`JobExists` + `Pawn.Destroyed`); called by Vanilla in `FinalizeInit` and on the 250-tick cycle | Vanilla save shell |

### A.3 De-module-coupling (2026-11-15)

| Method / input | Semantics | Consumers |
|---|---|---|
| `SetExternalPreserveProvider(Func<Thing,bool>)` | Third-party "must be preserved" facts are injected by the composition root; Core holds only the delegate and does not reference `SDS_PreserveHooks`; only tightening is allowed | Bootstrap, once at startup |
| `SDS_EndpointQuery.priority` | Endpoint ordering priority is carried by the query; Core does not read `SDS_Host.Priority`; factory methods fill it in, and `default` queries use `ApplyHostPriority()` | `SDS.Rules` / Api / Vanilla |
| `TryTakeMirror(Thing, int, ISDS_MirrorPhysical, out Thing)` | The physical converter is injected by the caller (`SDS_WithdrawPlan.physical`); Core does not read `SDS_Host.MirrorPhysical` | Vanilla mirrors / Api |


## Section B: Contracts Consumed by This Module

### B.1 Contracts types and members

| Source file | Type / member (signatures per Contracts source) | Core usage |
|---|---|---|
| `SDS_Stock.cs` | `public class SDS_StockKey : IExposable, IEquatable<SDS_StockKey>`; fields `thingDef` / `stuff` / `qualityInt` / `defName` / `stuffName` / `preserved`; `Resolved` / `HasIdentity` / `EffectiveDefName` / `SameAs` / `TryResolve()` / `EnsureNames()` / `static CompareIdentity(a, b)` | Ledger keys / comparison / resolution |
| `SDS_Stock.cs` | `SDS_StockEntry`: `SDS_StockKey key` / `long count` / `float avgHitPoints` / `float avgRotProgress`; `SDS_StockVitals`: `float hitPoints` / `float rotProgress`; `SDS_Reservation`: `int jobLoadID` / `SDS_StockKey key` / `int count` / `Pawn pawn`; `SDS_StockChannel { Virtual, Preserved }` | Snapshots / save / reservations / channels |
| `SDS_EndpointContract.cs` | `SDS_StructureKind`, `SDS_StructureFact` (endpoint query / resolution DTOs are consumed by `SDS.Rules`) | Structure facts |
| `SDS_Withdraw.cs` | `SDS_WithdrawMode`, `SDS_SupplyFailure`, `SDS_WithdrawEndState`, `SDS_WithdrawQuery`, `SDS_WithdrawPlan` (with `ISDS_MirrorPhysical physical`) | State machine / withdrawal plans |
| `SDS_Search.cs` | `SDS_ReturnFallback { None, Ground, PawnInventory, PawnCarry }` | Return fallbacks |
| `SDS_Host.cs` | `ISDS_DepositFilter`, `ISDS_DeliverySink`, `ISDS_MirrorPhysical` (as parameter types only), `ISDS_DiagnosticProvider` | Host facts / parameter types (`ISDS_Port` / `ISDS_PortProvider` are consumed by `SDS.Rules`) |
| `SDS_Host.cs` | `SDS_MedicalGate.Allows(Thing billGiver, ThingDef def)` | The medical gate of Vanilla / Api (Core does not call it) |
| `SDS_PreserveHooks.cs` | `RequiresPreserved(Thing)`; wired in by Bootstrap via `SetExternalPreserveProvider` | Composition-root wiring |
| `SDS_Mod.cs` / `SDS_Implants.cs` | `SDS_Config.GridCapacity` / `SeparateByQuality` / `SeparateByStuff` / `ForceWeaponApparelVirtual`; `SDS_HediffDefOf.ChipA` / `ChipB` | Capacity / key granularity / chips |

Contact points with Vanilla data types are in D.1.
`SDS_StockKey.ExposeData` is implemented by Contracts; Core only calls `EnsureNames` / `TryResolve`.

### B.2 Consumed host slots (Core directly consumes 0; all historical use sites have moved to SDS.Rules)

| Slot | Interface | Core use site | Members called |
|---|---|---|---|
| `Ports` | `ISDS_PortProvider` | `SDS.Rules / SDS_EndpointResolver.Select` (migrated out of Core in P2) | `IsLocalUsable(Map)`, `AppendCandidates(in SDS_EndpointQuery, List<ISDS_Port>)` |
| `DepositFilter` | `ISDS_DepositFilter` | `SDS_StockRules.CanDeposit` of `SDS.Rules` (migrated in P4) | `Allows(Thing)` |
| `Delivery` | `ISDS_DeliverySink` | `SDS_ReturnUtility.DeliverFallback` of `SDS.Rules` (migrated in P3) | `TryPlace(Thing, Map, IntVec3)`, `TryGiveToPawn(Thing, Pawn, bool)` |

Not consumed: `Priority` / `MirrorPhysical` / `MedicalCare` / `ReclaimWorld` / `MirrorQuery` / `Display` / `DepositJobs` / `TransportLoad`.
Of these, `Priority`, `MirrorPhysical`, and `MedicalCare` are now injected by the caller as query fields / plan fields / delegates (see A.3); `ReclaimWorld` went to Vanilla with C1.
Diag side: `SDS_Diag.Fail` / `Warn` / `ErrorOnce` / `Ctx` / `Assert*`; `SDS_StaticState.Register`; `SDS_SelfCheck.Register`.

## Section C: Call Relationships

| Direction | Target | Entry points |
|---|---|---|
| Who calls Core | `SDS.Vanilla` | `SDS_Core` ledger lifecycle / used category count / forbid switch / state write-back; `ISDS_LedgerHost` (construction / `ExposeData` / `Tick`); endpoints, supply, structures, availability, transactions, returns, and in-transit markers now call `SDS_Rules` |
| Who calls Core | `SDS.Api` | `SDS_Core` ledger lifecycle / forbid switch / state write-back; `ISDS_LedgerRead` read-only; everything else forwards to `SDS_Rules` |
| Who calls Core | `SDS.Bootstrap` / `SDS.Tests` / `SDS.Core.Tests` / `SDS.Rules.Tests` | `SDS_Core.RegisterAll`; the W34 migration now calls `SDS_Rules.ConvertPreservedGearToVirtual`; `SDS.Core.Tests` tests only the ledger — rule / flow tests live in `SDS.Rules.Tests` |
| Who Core calls | Data primitives | `SDS_StockData` (keys / materialization / vitals); `SDS_Config`; external facts injected as parameters |
| Who Core calls | Ledger / reclaim | `SDS_Ledger`; returns and the reclaim ledger belong to Rules / Vanilla |
| Who Core calls | Diagnostics | `SDS_Diag`; `SDS_SelfCheck` is called only by `RegisterAll` |

## Section D: Vanilla Support

> This section records only Core's contact points with Vanilla types, the behavior layers it does not depend on, and known gaps; the rule text is in [Rules.en.md](Rules.en.md), and host slots are in B.2.

### D.1 Vanilla types / members depended on

| Type / member | What Core uses it for | Code location |
|---|---|---|
| `Thing.def` / `Stuff` | Generating category keys | `SDS_StockData.cs` |
| `Thing.stackCount` | Quantity semantics, reservations, reclaim counting, `ThingCount` plan validation | `SDS_Ledger.cs`; (reclaim counting is in Vanilla `SDS_ReclaimRegistry.cs`) |
| `Thing.Destroyed` / `Spawned` / `holdingOwner` | Liveness criteria; ownership transfer goes only through `SDS_Ownership.TryTransferTo` (directly clearing `holdingOwner` is forbidden); reclaim sweeps (Vanilla side) | `SDS_Ownership`, `SDS_Ledger.TryDepositPreserved`; (`SDS_ReclaimRegistry.Sweep` is in Vanilla) |
| `Thing.SplitOff` / `Destroy` | Splitting preserved stacks for withdrawal; consuming temporary instances after a virtual booking succeeds | `SDS_Ledger.TakePreservedUnits`; for the return side see `SDS.Rules/SDS_ReturnUtility.cs` |
| `ThingMaker.MakeThing` | Rebuilding unspawned things from category keys (data construction, not spawning behavior) | `SDS_StockData.MakeThing` |
| `Map` | The map a structure fact belongs to (Rules); the ledger itself holds no map | `SDS.Rules/SDS_StructureRegistry.cs` |
| `Pawn` | Reservation ownership | `SDS_Reservation.pawn`; chip ports are in `SDS.Rules` |
| `ThingOwner<Thing>` | The sole holding container for preserved true bodies; `TryAdd` / `Remove` / `Contains` / counting | `SDS_Ledger.preservedOwner` |
| `ThingCount` | Exact preserved withdrawal plans (`Thing` + count; Core does only ledger-side validation) | `SDS.Rules/SDS_WithdrawExecutor.cs` |
| `Corpse` / `ThingDef.IsCorpse` | Legacy unrebuildable types of the preserved channel; corpse filtering for supply | `SDS.Rules/SDS_StockRules.cs` |
| `GenTicks.TicksGame` | Lifetime clock for just-materialized source markers (1-day fallback, W49) | `SDS.Rules/SDS_ProducedThingMarker.cs` |

Read-only capability bits (likewise data semantics, producing no behavior):

- `ThingDef.IsStudiable` (W35 per-item tracking), `ThingDef.EverHaulable`, `ThingDef.stackLimit`, `ThingDef.useHitPoints`.
- `CompQuality` / `CompRottable` / `CompArt` / `Apparel.WornByCorpse`: reading state or writing back the stack-average state.
- `ReliquaryUtility.IsRelic`: relic probe, degraded to "not a relic" when the host is unavailable (`SDS_StockRules.IsRelicSafe`).
- The two chip Hediffs of `SDS_HediffDefOf` and `Pawn.health.hediffSet.hediffs`: port state queries.

### D.2 Contact points with Vanilla adjudication (not rule definitions)

| Rule point | Vanilla adjudication | Corresponding implementation (after P4) |
|---|---|---|
| Storeability | `category == Item && !IsUnfinishedDef` | `CanStoreDef` / `CanStore` of `SDS.Core/SDS_StockData.cs` (Rules calls them as a friend) |
| Capacity | Virtual and preserved share one category-count pool; beyond it, new categories are rejected | `SDS_Rules.TypeCapacity` (base value + Stabilizer bonus, both read live from `SDS_Config`) and `SDS_Core.UsedCapacity` (deduplicated by `CategoryKeyOf`) |
| Unresolved entries | Supply counts as zero, the entry is kept, reusable once the def returns | `SDS_StockKey.Resolved` + `SDS_Availability.GetFailure` (Rules) returning `UnresolvedDef` + `SDS_Ledger.UnresolvedSnapshot` (Core) |
| The three gates of the Use command | Own port operational, category not forbidden, thing category allowed to "use" | `SDS_ImplantUtility.IsPortOperational` and `RequiresPreservedStorage` of `SDS_Rules`; `IsForbidden` (Core); gating order and UI are in Vanilla |

The semantics of channel splitting, availability, external destruction, rollback channels, and medical candidates are defined in [Rules.en.md](Rules.en.md) §5–§8 and §11, and `SDS.Rules/Rules.en.md` §1; this section does not repeat them.

### D.3 Behavior layers not depended on

- Does not reference or call: Harmony, `Find.*`, `ListerThings` / `ThingRequestGroup`, `GenClosest` / `Reachability`, `Job` behavior (only `job.loadID` appears, as a reservation key), `SlotGroup` / `IHaulDestination`, `GenPlace` / `GenSpawn`, `ResourceCounter`. The checkable forbidden-symbol list is in [Rules.en.md](Rules.en.md) §1.
- Conclusion: Core's behavior surface is zero, and the replacement points are the 8 host fact interfaces (E.1); this is also the prerequisite that makes the forbidden-symbol checks of `CheckAssemblyBoundaries.ps1` executable.

### D.4 Why it can be verified outside the game environment

- `SDS_Ledger` constructs without parameters, and `SDS_Core.Attach` only assigns references; neither the ledger nor the data primitives depend on `Game`.
- The return failure path depends only on the return value of `ISDS_DeliverySink` (in `SDS.Rules` since P3); it never triggers `GenPlace` / `GenSpawn`.
- Endpoint resolution, supply, the adjudication layer, and the flow layer have moved to `SDS.Rules`; see `SDS.Rules/Interfaces.en.md` for their out-of-game verification.

### D.5 Known gaps and boundaries

- Unresolved-entry semantics: `Snapshot` does **not** skip unresolved entries; consumers filter by `entry.key.thingDef == null` / `key.Resolved`; `UnresolvedSnapshot()` is only for diagnostics and old-save verification (the Vanilla usage window displays them separately). The 2026-11-16 review confirmed that every `Snapshot(0)` call site in `SDS.Vanilla` explicitly filters unresolved keys.
- Capacity granularity: `UsedCapacity` is the category count after deduplication by `CategoryKeyOf` (def + stuff + quality + channel); multiple items / multiple true bodies of the same category occupy a single slot. "Ownership groups" are not an independent capacity dimension; see `SDS.Rules/Rules.en.md` §12.24 and this file §12.29.
- Test coverage: `SDS.Core.Tests` covers `ConservationTests` (random virtual count conservation), `InvariantTests` (E2 / SelfCheck entry points), `UnresolvedVitalsTests` (vitals round-trip for unresolved defs), and `LedgerCoreTests` (capacity thingID deduplication, preserved reservation / withdrawal, reservation audit, forbid persistence); ledger structure / reservation / save round-trips live in `SDS.Rules.Tests/Core` (`LedgerStructureTests`, `ReservationTests`, `ScribeRoundTripTests`, etc.), and cross-module integration lives in `SDS.Tests`. The rule-side cases for preserved / reservations are still driven through `RulesTestEnv`, layered separately from Core's pure-ledger cases.
- Core still needs Vanilla `Thing` instances to participate in `ThingOwner` and `SplitOff`, and cannot fully leave the RimWorld assemblies; "outside the game environment" means not starting `Game`, not loading a map, and not installing Harmony.
- `ReliquaryUtility.IsRelic`, `ModsConfig`, `DefDatabase` are host-environment probes; on failure they are handled with degraded values and do not block Core adjudication.
- Any newly added Vanilla behavior call should first be evaluated for conversion into a host fact interface; the only current exceptions are the read-only capability bits above and `ThingMaker.MakeThing` (ModuleRules.en.md 6.3).

## Section E: Third-Party Support

> This section records only the call surface and registration points by which third parties enter Core; checkable constraints are in [Rules.en.md](Rules.en.md) §4 and §8.

### E.1 Host fact interfaces and Vanilla implementations

| Interface | Fact provided | Vanilla implementation |
|---|---|---|
| `ISDS_PortProvider` | Candidate endpoints, on-map availability, own port | `SDS_EndpointProvider` |
| `ISDS_DepositFilter` | Global deposit filter, filter version | `SDS_VanillaServices` |
| `ISDS_PrioritySource` | Storage priority, whether to ignore priority, version | `SDS_VanillaServices` |
| `ISDS_DeliverySink` | Placing, giving to inventory, giving to container | `SDS_VanillaServices` |
| `ISDS_MirrorQuery` | Mirror visibility, version, `IsMirror` (Core does not call it directly today) | `SDS_MirrorService` |
| `ISDS_MirrorPhysical` | Mirror physical conversion (partial / whole stack) | `SDS_MirrorPhysicalOps` |
| `ISDS_MedicalCarePolicy` | Whether a medical care level allows a given drug | `SDS_MedicalCareAdapter` |
| `ISDS_ReclaimWorld` | Current tick, Job / Lord existence, leaving the group | `SDS_ReclaimWorldAdapter` |

Core-side consumption points are in B.2 (0 host slots consumed directly; the historical `DepositFilter` / `Delivery` / `Ports` moved to `SDS.Rules` with P2–P4); `Priority` / `MirrorPhysical` / `MedicalCare` are injected by the caller as query fields / plan fields / delegates, and `ReclaimWorld` / `MirrorQuery` / `Display` / `DepositJobs` / `TransportLoad` are used by Vanilla or Api.
Interfaces return only facts, never rules: for example, `ISDS_Port` answers only availability, reachability, and squared distance; ordering is still decided by `SDS_EndpointResolver.IsBetter` of `SDS.Rules` (ModuleInterfaces.en.md §3.3).
Injection timing: step 2 of Bootstrap startup (ModuleRules.en.md §9); for the behavior when any interface is null, see ModuleInterfaces.en.md §1.2.

### E.2 The third-party call surface

- The third-party-visible surface is only the `SDS_API` five axes (See / Read / Find / Take / Store) and the adapter bridges; adapter bridges only compose and never add ordering / capacity / debit / job-assignment rules. Core does not distinguish caller identity — Vanilla and third parties share the same contract.
- Each third-party entry point may have only one write takeover path; read-only projections may coexist with adapter translation.
- Adapter responsibility is limited to "reading third-party state / translating parameters / calling facades / translating results"; the moment "which one, how many, is it enough, what order, what rollback order" appears, it is a rule and must be escalated to Core.
- Target mods may only be probed via reflection; hard-referencing their assemblies is forbidden; logging uses only `SDS_Diag.ErrorOnce` / `Warn`, never `SDS_Diag.Fail`.
- Result: Core faces only the fact interfaces above and a set of facade methods; adding a third-party mod requires no change to Core and no new branch.

### E.3 `SDS_PreserveHooks`: third-party registration of preserved facts

- Location: the pure registry `SDS_PreserveHooks` in Contracts (ModuleInterfaces.en.md §1.3).
- Registered content: one `Func<Thing, bool>` probe answering "must this thing keep its true body"; null or duplicate registrations return false.
- Consumption point: at the end of `SDS_StockRules.RequiresPreservedStorage(Thing)` of `SDS.Rules`, alongside the B1 blacklist, `IsProtectedThing`, and `HasInstanceNonKeyState` (same file, located by symbol).
- Isolation: an exception in a single probe is logged once via `Log.ErrorOnce` and that probe is skipped, without affecting the other probes or Core's channel split (the actual implementation uses a fixed message and ID — see `SDS_PreserveHooks.cs`).
- Boundary: a probe provides facts only — no debiting, no job assignment, no second write path; registrations are process-level constants and are not registered into `SDS_StaticState`.
- Significance: when a third party adds an item type that "must be preserved", it only registers the test; no defName / mod list needs to be added inside Core.

### E.4 `SDS_BillSupplyPlanner`: the sole outlet for shortfall calculation (migrated to SDS.Rules)

- Location: internal in `SDS.Rules` since P2; for the S16 rule text and interface see `SDS.Rules/Rules.en.md` and `SDS.Rules/Interfaces.en.md`.
- Core keeps only the ledger capability and no longer defines supply rules; the live-content test is unified in Core's `SDS_StockData.HasLiveContent`, which Rules calls directly as a friend; the `SDS_Rules` public facade is gone.

### E.5 `SDS_JobFuelCount` (deleted on 2026-11-16)

- Was a Contracts pure registry registering the remaining requirement count of outfitting Jobs by `JobDef.driverClass`.
- After Plan B landed, W58 (missing counts are filled per reservation intent) covers it uniformly; Core no longer provides any per-recipe count-registration entry point.

### E.6 Why third parties never call Core internal types directly

- Physically unreachable and auditable: `InternalsVisibleTo` grants only `SDS.Rules` (since P3, registered in `CheckAssemblyBoundaries.ps1` §5); `CheckCompatSurface.ps1` allows only Api / Contracts / Diag and forbids Core / Vanilla; conclusion in [Rules.en.md](Rules.en.md) §4.
- Benefit: internal types can change signatures without breaking third parties; the external contract is managed solely by the `SDS_API` Version; failure semantics are unified as `SDS_ApiResultCode` and read-only snapshots, and exceptions never leak out.
