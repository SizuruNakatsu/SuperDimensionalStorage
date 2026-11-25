> Positioning: preserves the historical decisions of "why it was done this way" from the SDS.Api refactor, the differences between the old and new implementations, the pitfalls hit along the way, and the design trade-offs, so that maintenance can be understood in one read; the old implementation is archived only as textual description, and this directory no longer depends on any old implementation snapshot. Rule IDs and normative sentences are in [Rules.en.md](Rules.en.md) §4.

# SDS.Api · Design Decisions (why it was done this way / pitfalls hit)

## 1. Historical Decisions and Pitfalls

### 1.1 Evolution of `Version`

The `Version` history of the old `SDS_API.cs`: 1 → 2 (removed entries with no adopters) → 3 (5 public helpers demoted to internal) → new 4 (assembly renamed, five-axis signatures and semantics unchanged) (PublicAPI.en.md §2.5, §3.8).

### 1.2 Conclusions of the old document (paraphrased item by item)

| Old document section | Conclusion (paraphrased) | New location |
|---|---|---|
| §1.1 | The facade is not a second implementation; every method must land on the unique implementation, with no separate ordering, filtering, capacity, priority, transaction, or deduction logic written | Rules.en.md API-2, API-6 |
| §2 | Five-axis responsibilities and return shapes: See = a read-only `Thing` list; Read = `SDS_ApiStockInfo`; Find = a `Thing` + failure reason; Take = real `Thing`s, delivery completed by the caller; Store = the actually accepted amount + failure reason | README.en.md §2; Interfaces.en.md §1 |
| §3.1 | Thin facade: calls exactly one corresponding service; combines only within "the necessary steps of the same transaction" (e.g., crediting the ledger immediately after selecting the endpoint) | `TryDepositNow` first `TryFindDepositEndpoint`, then `TryDepositAt` |
| §3.4 | Caller context: entries that change state or involve reservations serve only the player faction; deposits require the same map, and the endpoint's faction must be empty or equal the caller's; ownership / forbidding are filled in by the facade | `ValidateDepositCall` / `ValidateWithdrawCall` |
| §3.5 | Transaction postconditions: a successful take = deducted and materialized, with delivery completed by the caller; a successful store = the physical thing has been consumed; on failure everything is left as-is with the caller | Interfaces.en.md 8.3; the `TryReturn*` bridge |
| §3.6 | Failure codes: state-changing calls return `SDS_ApiResultCode`; read-only snapshots use `result`; exceptions uniformly yield `InternalError` / empty set / `default` and record one `ErrorOnce`; normal rejections write no logs | private `Fail(...)` / `MapSupplyFailure` |
| §3.7 | Non-pawn mode is a parameter branch of the same resolver (`actor == null`, `requireReach = false`, `allowSelf = false`, distance computed from `reference`), not a second resolver | `SDS_EndpointQuery.DepositForNonPawn` |
| §3.8 | `Version` is capability probing and a compatibility declaration; E5: a Version change is a compatibility declaration | Rules.en.md API-7 |
| §2.6 | The adapter bridge is a `partial` of the same `SDS_API`, not a second API; the old version was entirely `internal` and outside the `Version` public contract | Interfaces.en.md §2; ModuleInterfaces.en.md §5 |
| §4.3 / §4.5 | E7: each third-party entry has exactly one write-takeover path; E6: the compatibility layer may use only Api / Contracts / Diag | Rules.en.md API-6; ModuleRules.en.md 6.6 |
| §4.10 | The call chain is fixed as "adapter → `SDS_API` method → unique implementation"; adapters must not compute capacity / priority / shortfall themselves | Interfaces.en.md §6 |
| §6 | Acceptance includes: calling only the unique implementation, returning vanilla-recognizable objects, the facade filling in ownership / faction / forbidding, exceptions never leaking, unused entries deleted and the Version bumped | Rules.en.md §1 |

The authoritative definition of the five-axis rules is the §2 table header of PublicAPI.en.md (gameplay semantics are in `SDS.Vanilla/Rules.en.md` and `SDS.Core/Rules.en.md` respectively); this module keeps only the "caller gate + forwarding" conclusions, and gameplay semantics do not migrate back.

## 2. Old Implementation → New Implementation: Differences and Pitfalls

| Difference | Old implementation | New implementation | Maintenance note |
|---|---|---|---|
| Bridge visibility | All of `SDS_API_Adapters.cs` was `internal`, in the same assembly as the compatibility layer | All changed to `public` | Only one cross-assembly friend is registered, `SDS.Core → SDS.Rules`; ThirdModCompat still cannot see Api / Core internals, and the bridge is listed separately |
| Whether the bridge belongs to the contract | The old document stated explicitly "`internal`, no commitment" | public, but still outside the `Version` commitment | External use of the bridge is at the user's own risk; adding or removing bridge methods does not bump `Version` |
| Old direct calls to internal implementations | Read directly `SDS_GameComponent_Grid`, `SDS_DepositRules`, `SDS_WithdrawExecutor`, `SDS_EndpointResolver`, `SDS_OutputUtility`, `SDS_MirrorRegistry`, `SDS_ReturnUtility`, `SDS_DepositService` | Goes only through public members of `SDS_Core` / `SDS_Rules` and `SDS_Host` slots | Any change that references Core / Rules internals again crosses the boundary (ModuleRules.en.md 6.5) |
| `ReadStockFor` channel determination | Routed itself with `SDS_DepositRules.RequiresPreservedStorage(thing)`, then `SDS_StockKey.FromPreservedThing` / `FromThing` | Calls only `SDS_Rules.TryGetStockKey` | Duplicating the routing produces a second set of channel judgments; especially prone to divergence when W33 / W35 change |
| `TryWithdraw` with a preserved key | Preserved keys were routed to `SDS_WithdrawExecutor.TryWithdrawPreserved`; the same entry could withdraw preserved | Explicitly rejects `key.preserved` and returns `InvalidArgument`; preserved goes only through `TryWithdrawPreservedExact` | Old callers passing a preserved key must switch to the exact true-body plan after upgrading |
| `SDS_ApiCaller.claimOwner` | The field exists, assigned by `FromPawn` / `FromFaction`, unused in judgments | Same as the old implementation (`SDS.Contracts/SDS_API.cs`) | Assigned only, never used in judgments; do not infer from this that concurrent claims are implemented |
| Player-faction judgment spelling | `TryFindWithdrawPoint`, `ValidateDepositCall`, `ValidateWithdrawCall`, and `TryGetWithdrawCell` all wrote `Faction.OfPlayer` directly | Most are consolidated into `IsPlayerFaction` (`Faction.OfPlayerSilentFail`, returning false on exception), but the bridge's `TryGetWithdrawCell` still writes `actor.Faction != Faction.OfPlayer`; M2 only shared the withdrawal-point resolution and did not change the caller gates | The two spellings are inconsistent; when changing faction judgments, check both places at once to avoid differing silent-failure behavior |
| `ReadStock` when G is off | With `grid == null` it returned early with `GridUnavailable` + `Insufficient`, without filling the key's `forbidden` / `total` / failure reason | As long as the ledger exists it fills `total` / `forbidden`; `result = GridUnavailable`; `available` still comes from `SDS_Availability`; `failure` is fetched by key, and an empty key yields `UnresolvedDef` | The test `ReadStock_GridUnavailable_ZeroesAvailableKeepsTotal` pins down "total is preserved, available is zeroed, unresolved takes priority" |
| Bridge return placement | `TryReturnPreserved(Thing, Map, IntVec3)`; `TryReturnVirtual(Thing, Map, IntVec3, bool landOnFailure)` | `TryReturnPreserved(Thing, Map, IntVec3)`; `TryReturnVirtual` passes `cell` into Core, and `landOnFailure` decides whether failures land on the Ground | Placement priority: `cell` (valid and in bounds) → under the pawn → `map.Center`; the ledger returns by the physical thing's key and accepts no key parameter |
| Mirror withdrawal mode / pawn | `TryWithdrawFromMirror` passed `mode` / `caller.pawn` through to the executor | `SDS_Rules.TryTakeMirror(mirror, quantity, out taken)` fixes `Hand` with no pawn | Behavior is equivalent: the API already rejects `VirtualHandoff` and validates pawn-map identity; the registration gap has been documented here |
| G-off gate at the take / store entries | Taking failed deep inside the transaction and returned `TransactionFailed` | `ValidateWithdrawCall` / `ValidateDepositCall` return `GridUnavailable` at the top | Aligned with R17, with a more precise failure code; a result-code behavior change, recorded here |
| Bridge method additions and removals | No `TryGetStockKey` / `IsDepositEndpoint`; had 8 first-party entry points (`*ForEntry`, etc.) | Adds the above 2; all 8 first-party entry points are deleted | Vanilla already calls Core directly; do not bring `*ForEntry` back as a second entry path |
| Endpoint query implementation | `SDS_EndpointResolver.TryFindBestForNonPawn` + `SDS_EndpointHandle`; finding a withdrawal point went through `SDS_OutputUtility.FindWithdrawPoint` | `SDS_Rules.SelectEndpoint(SDS_EndpointQuery...)` + `ISDS_Port` | The new bridge `HasUsableBuildingEndpointWithin` still reads `map.reachability` directly (a vanilla fact) but does no ordering itself |
| Version | `Version = 3` | `Version = 4` | Only because of the assembly rename; five-axis signatures and semantics are unchanged, and third parties must re-establish their soft dependency |

The old implementation is historical archive only from here on; maintaining this module requires no further reading of it.

## 3. Design Trade-offs

> The former ImplementationReasons.md has been merged into this section; its table duplicating the "old file location / visibility comparison" is no longer kept.

### 3.1 Why a separate facade is needed

| Reason | Explanation | Source |
|---|---|---|
| Third parties cannot reach Core internals | Core exposes to Vanilla / Api / Bootstrap only `SDS_Core` and `ISDS_LedgerRead` / `ISDS_LedgerHost` (the ledger read-only surface and lifecycle surface); `SDS_Availability` / `SDS_ImplantUtility` are in Rules, `SDS_Ledger` and the remaining types are internal; `InternalsVisibleTo` is only `SDS.Core → SDS.Rules` | ModuleResponsibilities.en.md §2, §5; ModuleRules.en.md §5 |
| Assembly boundary changes do not change semantics | Before the refactor it was the single assembly `SuperDimensionalStorage.dll`; now it is split into 7 assemblies; five-axis method names and semantics stay unchanged, and `SDS_API.Version = 4` is only due to the assembly rename | ModuleResponsibilities.en.md §7 |
| Turning "who may call what" into a verifiable whitelist | `CheckAssemblyBoundaries.ps1` performs type-level public whitelist and reference-graph checks; `CheckCompatSurface.ps1` allows only Api / Contracts / Diag | ModuleRules.en.md §10 |
| The caller gate is written once | Player faction, ownership, forbidding, and same-map are judged only in the Api; Vanilla and third parties share the same set of entries, with no duplication in their own modules | ModuleResponsibilities.en.md §7 |
| Adding third parties does not change Core | New adapters go only through the Api and Contracts; Core knows no specific mod and adds no defName branches | ModuleRules.en.md 6.6; ModuleResponsibilities.en.md §8 |

The Api's engineering constraints are therefore limited to three: reference only Contracts / Diag / Core; referencing Vanilla / Harmony / ThirdModCompat is forbidden; writing rule, UI, deduction, or reservation implementations is forbidden — only forward to Core, and where necessary take vanilla facts via `SDS_Host` (ModuleRules.en.md 6.5).

### 3.2 What the facade does not do

- No candidate discovery: endpoint candidate enumeration lives in Vanilla's `ISDS_PortProvider.AppendCandidates`; the Api calls only `SDS_Rules.SelectEndpoint`.
- No delivery: ground placement / entering inventory / entering containers are handled by the Vanilla implementation of `ISDS_DeliverySink`; the Api does not touch `GenPlace` / `ThingOwner`.
- Installs no patches, writes no UI, caches no stock snapshots; `VisibleThingsOfDef` and `ReadStock` read fresh on every call.
- Defines no rule IDs and adds no rule documents; gameplay criteria reference only the capability documents pointed to by PublicAPI.en.md.

### 3.3 Why the Api implements no rules

| Rule | Unique implementation | What the Api actually does | Source |
|---|---|---|---|
| Available count = total − reservations | `SDS_Availability.GetAvailableCount` | `ReadStock` and the bridge's `GetAvailableCount` only forward, without subtracting `reserved` themselves | ModuleInterfaces.en.md 3.2; ModuleResponsibilities.en.md §7 |
| Virtual / preserved channel determination | `SDS_Rules.TryGetStockKey` / `SDS_Rules.RequiresPreservedStorage` | `ReadStockFor` asks Core for the key first; the bridge's `RequiresPreservedStorage` forwards directly, without duplicating the def branching | ModuleResponsibilities.en.md §7; ModuleInterfaces.en.md 3.2 |
| Bill supply shortfall | `SDS_BillSupplyPlanner` (`SDS.Rules` internal, migrated in P2) | `TryPlanBillSupplySlot` / `TryPlanAllowedDefSupply` forward inputs and outputs only, without computing `required` / `fromOwn` / `shortfall` in the Api | ModuleInterfaces.en.md 3.5; PublicAPI.en.md §4.9 |
| Endpoint ordering / capacity / filtering | `SDS_EndpointResolver` (`SDS.Rules`) / `SDS_StockRules` / `SDS_WithdrawTransaction` (`SDS.Rules`) | Find, Take, and Store only assemble query DTOs and forward to `SDS_Rules.SelectEndpoint` / `TryWithdraw` / `TryDeposit` | ModuleRules.en.md 6.5; ModuleResponsibilities.en.md §7 |
| Failure-reason criteria | `SDS_Availability.GetFailure` | The Api only maps internal reasons into `SDS_ApiResultCode`; normal rejections write no logs | PublicAPI.en.md §3.6 |

Once the Api duplicates any one of these, a second copy of quantities, a second set of channel judgments, or a second shortfall implementation appears; this is exactly the divergence Principle 3 and E7 forbid (DesignPrinciples.en.md §2; ModuleRules.en.md §3).

Taking Read as an example: when the global switch is off, `ReadStock` only sets `result` to `GridUnavailable`; `total` still comes from the ledger and `available` still comes from `SDS_Availability` (the R17 criteria). If the Api zeroed the available count itself, it would diverge from Core's read-side criteria, and the numbers third parties see would also disagree with the UI and jobs.

### 3.4 Key trade-offs

| Trade-off | Concrete manifestation | Cost / boundary | Source |
|---|---|---|---|
| The five axes return only `Thing`s, no delivery | `TryWithdraw` returns the materialized `List<Thing>`, `TryWithdrawFromMirror` returns a single `Thing`; `SDS_Rules.TryWithdraw` / `TryTakeMirror` do not call `ISDS_DeliverySink` | Delivery must be completed by the caller per the vanilla contract; on failure a return requires calling `TryReturnVirtual` / `TryReturnPreserved`; the Api provides no end-to-end transaction | ModuleInterfaces.en.md 1.2 item 3; PublicAPI.en.md §3.5 |
| Caller gate and rule gate separation | The Api judges only player faction, ownership, forbidding, and same-map; stock, capacity, filtering, ordering, and transactions are all decided by Core | The criteria of the two places must stay in sync; e.g., the Api's ownership judgment does not reuse `HaulAIUtility.PawnCanAutomaticallyHaulFast`, and both must be checked together when changing | ModuleResponsibilities.en.md §7; PublicAPI.en.md §3.4 |
| `Version` is a compatibility declaration | `Version = 4` only means the assembly was renamed and third parties must re-establish their soft dependency; five-axis semantics are unchanged | The adapter bridge is outside the `Version` commitment; adding or removing bridge methods does not trigger a version change, and external direct use of the bridge is at the user's own risk | ModuleResponsibilities.en.md §7; PublicAPI.en.md §3.8 |
| The adapter bridge is listed separately and is not part of the five-axis commitment | Bridge methods are public, only for cross-assembly use; the only production-code consumer is `SDS.ThirdModCompat` (`SDS.Tests` calls it only in tests) | The public surface grows, and the whitelist must explicitly acknowledge these methods; every bridge method may only forward and must not grow new rules | ModuleInterfaces.en.md §5; ModuleRules.en.md §5 |
| `Find.TickManager` is the sole `Find.*` exception | Only `CurrentTick()` reads `Find.TickManager.TicksGame`, falling back to 0 on a null reference, for the `tick` field of `CaptureReadVersions` | The tick is not taken via `SDS_Host`; the reclaim inspection's tick is provided by the Vanilla-side `ISDS_ReclaimWorld.CurrentTick` (after C1 Core no longer reads that slot), so the two sources differ (whether to unify them is pending) | ModuleResponsibilities.en.md §7; ModuleRules.en.md 6.3; ModuleInterfaces.en.md 1.2 |

The most easily underestimated is "returning only `Thing`s": it explicitly pushes the delivery responsibility onto the caller, in exchange for the Api not needing to know `carryTracker`, `inventory`, `ThingOwner`, or ground-placement utilities. The side effect is that the caller must return things itself on failure; the Api provides only the two return entries `TryReturnVirtual` (`landOnFailure` controls whether things land) / `TryReturnPreserved`, and does not decide the rollback order for the caller.

Making the adapter bridge public is the direct cost of the assembly split: in the old structure the compatibility layer and `SDS_API` shared one assembly, where `internal` sufficed; after the split, without opening `InternalsVisibleTo`, the bridge methods had to be promoted to public. To limit the impact, the bridge is explicitly excluded from the five-axis contract and registered category by category in ModuleInterfaces.en.md §5.

### 3.5 What does not belong to the Api

| Content | Belongs to | Note |
|---|---|---|
| Candidate enumeration, reachability, distance, priority | Core `SDS_EndpointResolver` + Vanilla `ISDS_PortProvider` | The Api only assembles `SDS_EndpointQuery` |
| Deduction, reservations, transactions, rollback | Rules `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor` | The Api stores no transactions and holds no reservations across calls |
| Delivery, pathfinding, ground placement, entering containers | Vanilla `ISDS_DeliverySink` / JobDriver / UI | The Api only returns physical things |
| Patches, buildings, UI, JobDrivers | Vanilla | The Api's patch count is 0 |
| Logging implementation and report center | Diag | The Api calls only `SDS_Diag.ErrorOnce` |
| Startup order and host assembly | Bootstrap | The Api provides only the `Version` constant |

## 4. Pending Items

- File division: `SDS_API.cs` handles the five axes, caller gates, `Version`, and tick; `SDS_API_Adapters.cs` handles the adapter bridge; both are `partial` of `SDS_API`.
- Evidence of external third-party adoption of the five axes is still empty (PublicAPI.en.md §2.7); the current state of `SDS_ApiCaller.claimOwner` is in Interfaces.en.md 7.5.
- New bridge methods must update ModuleInterfaces.en.md §5 and the type-level whitelist at the same time, otherwise `CheckAssemblyBoundaries.ps1` records them as leaks.
- Any impulse of the form "first compute the available count / shortfall / priority" should go back to Core; the Api is allowed only forwarding and result-code mapping.
