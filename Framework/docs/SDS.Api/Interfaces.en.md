> Positioning: this file only registers the consumption surface, signature index, and external capability boundary of SDS.Api; the signature authority is `SDS_API*.cs`. Rules are in [Rules.en.md](Rules.en.md), and historical decisions and pitfalls are in [DesignDecisions.en.md](DesignDecisions.en.md).

# SDS.Api Interfaces

## 1. Five-Axis public Signatures (A)

The sole authority for signatures and member semantics: `Source/SDS.Api/SDS_API.cs` (`Version = 4`). The five axes belong to the `SDS_API.Version` contract (ModuleResponsibilities.en.md §7); this document does not transcribe the signatures.

## 2. Adapter Bridge public Signatures (A)

The sole authority for signatures and member semantics: `Source/SDS.Api/SDS_API_Adapters.cs`. The bridge is public, but not covered by the five-axis `Version` commitment (ModuleInterfaces.en.md §5).

Bridge methods are registered by group: endpoint / availability, ledger read-only, display / mirror, source / channel, job assignment, withdraw by def in bulk, supply planning, loading / return. Method names and registration are in ModuleInterfaces.en.md §5 (the same table read by `CheckCompatSurface.ps1`); this document does not transcribe them again.

## 3. Consumed Contracts Types (B)

The following types are all actually referenced in `SDS.Api` source code (verified by grep).

| Group | Types | Purpose / defined in |
|---|---|---|
| Calls and results | `SDS_ApiCaller`, `SDS_ApiResultCode`, `SDS_ApiStockInfo`, `SDS_ReadVersions`, `SDS_SupplyFailure` | caller context / result codes / read-only snapshot / version snapshot / failure classification (`SDS_API.cs`, `SDS_Withdraw.cs`) |
| Stock | `SDS_StockKey`, `SDS_StockChannel` | category keys / virtual · preserved channels (`SDS_Stock.cs`) |
| Withdrawal | `SDS_WithdrawMode`, `SDS_WithdrawQuery`, `SDS_WithdrawPlan` | delivery mode / query / committed plan (`SDS_Withdraw.cs`) |
| Endpoints | `SDS_EndpointOp`, `SDS_EndpointQuery`, `SDS_EndpointDecision` | endpoint semantics / query DTO / decision result (`SDS_EndpointContract.cs`) |
| Host interfaces | `ISDS_Port`, `ISDS_PortProvider`, `ISDS_MirrorQuery`, `ISDS_DisplayQuery`, `ISDS_DepositJobs`, `ISDS_TransportLoadSink`, `SDS_Host`, `SDS_MedicalGate` | host facts consumed directly by the Api, and the medical gate (`SDS_Host.cs`) |
| Settings | `SDS_Config` | read-only W33 switch, used for the compatibility layer's cache-invalidation judgment (`IsForceWeaponApparelVirtual`) |
| Job / return | `SDS_JobDefOf`, `SDS_ReturnFallback` | `DepositToGrid` judgment / return fallback (`SDS_CaravanTransport.cs`, `SDS_Search.cs`) |

Base types come only from RimWorld / Verse: `Thing`, `ThingDef`, `ThingCount`, `Pawn`, `Map`, `IntVec3`, `Faction`, `ForbidUtility`, `Job`, `IngredientCount`, `Bill`, `RecipeDef`, `ThingFilter`, `ThingWithComps`, `TransferableOneWay`, `Find`.

## 4. Consumed `SDS_Host` Slots (B)

`SDS_Host` has 11 slots in total (ModuleResponsibilities.en.md §3); the Api directly reads 5 of them.

| Slot | Used at | Purpose |
|---|---|---|
| `MirrorQuery` | `VisibleThingsOfDef`, `CaptureReadVersions`, `IsMirror` | visible physical things / mirror versions / mirror identity |
| `Ports` | `TryDepositAt`, `HasUsableBuildingEndpointWithin`, `IsDepositEndpoint`, `IsGridUsable` | port handles, candidates, usability on this map |
| `DepositJobs` | `TryTakeDepositCandidate`, `TryMakeDepositJob`, `TryMakeDepositJobFromInventory`, `IsManualDepositAvailable` | the job-based deposit facade |
| `Display` | `IsDisplayThing`, `TryGetDisplayKey` | display thing identity |
| `TransportLoad` | `InjectTransportLoadCandidates` | third-party load manifest injection |

Not read directly by the Api: `DepositFilter`, `Delivery`, `ReclaimWorld`; `MedicalCare` is read only indirectly through `SDS_MedicalGate`, and `MirrorPhysical` / `Priority` are consumed as input parameters / query fields.

## 5. Consumed Core / Diag Members (B)

| Type | Members | Used at |
|---|---|---|
| `SDS_Core` | `Available`, `Ledger`, `EndpointVersion` | global availability / ledger / endpoint version |
| `SDS_Core` | `CanDeposit`, `TryDeposit` | Store |
| `SDS_Rules` | `SelectEndpoint` | Find |
| `SDS_Core` | `TryWithdraw`, `TryTakeMirror`, `TryWithdrawPreservedExact` | Take |
| `SDS_Core` | `TryGetStockKey`, `RequiresPreservedStorage`, `TryFindWithdrawableKey`, `GetAvailableCountForDef` | channels / withdrawable keys / def-level total availability |
| `SDS_Rules` | `AllowsDef`, `TryPlanSlot`, `TryPlanAllowedDefSupply`, `NotifyDeposited` | supply planning |
| `SDS_Core` | `ReturnThing`, `IsProducedMarked`, `MarkProduced` | returns / source markers |
| `ISDS_LedgerRead` | `IsForbidden`, `GetCount`, `GetPreservedCount`, `CopyKeys`, `AppendPreservedThings`, `IsPreserved` | ledger read-only |
| `ISDS_LedgerRead` | `StockVersion`, `ReservationVersion`, `FilterVersion` | reading version snapshots |
| `SDS_Availability` | `GetAvailableCount`, `GetFailure`, `ClampTakeToAvailable` | unified available counts / failure criteria and take clamping (W56) |
| `SDS_ImplantUtility` | `IsPortOperational` | pawn implant port availability |
| `SDS_Diag` | `ErrorOnce` | exceptions are logged once; the Api does not call `Fail` |

## 6. Call Relationships

```text
SDS.ThirdModCompat (Sdk utility classes / 9 adapters)
        │  only via SDS_API (five axes + adapter bridge)
        ▼
SDS.Api (SDS_API.cs five axes / SDS_API_Adapters.cs bridge)
        │  only forwards
        ├─→ SDS.Core (SDS_Core / SDS_Ledger / SDS_Availability / SDS_ImplantUtility)
        ├─→ SDS.Host (Ports / MirrorQuery / DepositJobs / Display / TransportLoad)
        └─→ SDS.Diag (ErrorOnce)
```

External third parties can call the five axes just like `SDS.ThirdModCompat`; the adapter bridge is exposed as-is, but is not part of the `Version` commitment (ModuleInterfaces.en.md §5, and section 8 "Third-Party Support" of this document).

## 7. Vanilla Support

### 7.1 Relationship with vanilla: zero patches, zero behavior

- The Api's patch count is 0: all 112 feature patch classes are in Vanilla, and the dynamic patches of the 9 third-party adapters are in ThirdModCompat (ModuleRules.en.md §7, §8; PatchInventory.en.md).
- What the Api depends on is not vanilla behavior itself but **fact fields** and **judgment functions** on vanilla objects; the real vanilla behavior is still executed by Vanilla: the Api does not directly call `ListerThings`, `GenClosest`, `JobDriver`, `ThingOwner`, or `GenPlace`; See forwards to `SDS_Host.MirrorQuery`, Find forwards to `SDS_Rules.SelectEndpoint`, and Take / Store forwards to Core's transaction and ledger-crediting entry points.

### 7.2 Vanilla judgments used by the five-axis caller gate

| Judgment | Source location | Vanilla members used | Effect |
|---|---|---|---|
| Player faction | `IsPlayerFaction`, `IsPlayerPawn` | `Faction.OfPlayerSilentFail` | Deposit / withdraw / find-withdrawal-point require the player faction; returns false on null or exception |
| Pawn validity | `IsPlayerPawn`, `TryWithdraw`, `TryWithdrawFromMirror` | `Pawn.Spawned`, `Pawn.Dead`, `Pawn.Map` | Rejects pawns that are not spawned, are dead, or have no map |
| Physical thing ownership | `IsOwnedByCaller` | `Thing.Faction`, `Thing.SpawnedOrAnyParentSpawned`, `Thing.SpawnedParentOrMe`, root `Pawn.Faction` / `Thing.Faction` | Caller, thing, and root holder must be of the same faction; unowned things are allowed through |
| Forbidden state | `ValidateDepositCall` | `ForbidUtility.IsForbidden(thing, caller.faction)` | Forbidden physical things are rejected for deposit |
| Map identity | `ValidateDepositCall`, `TryWithdrawFromMirror` | `Thing.MapHeld`, endpoint `MapHeld`, `Pawn.Map` and `mirror.Map` | The thing and the endpoint must be on the same map; when withdrawing from a mirror, the pawn must be on the same map as the mirror |
| Current tick | `CurrentTick` | `Find.TickManager.TicksGame` | Written into `SDS_ReadVersions.tick` for read-only snapshot consistency comparison |
| Reference cell | `TryFindDepositEndpoint` | `Thing.PositionHeld`, `Map.Center`, `IntVec3.IsValid` | Determines the distance reference cell for non-pawn deposits |

Endpoint ownership is additionally decided by `IsEndpointOwnedBy`: it is allowed through only when the endpoint's `Thing.Faction` is null or equals the caller's faction (`SDS_API.cs`).

The 7-step order and the checklist ("withdrawal contains only steps 2 and 3") are in [Rules.en.md](Rules.en.md) §2; if any deposit step fails, the corresponding `SDS_ApiResultCode` is returned, without proceeding to Core's endpoint selection or ledger crediting.

### 7.3 Additional vanilla facts used by the adapter bridge

| Fact | Source location | Vanilla members | Boundary |
|---|---|---|---|
| Endpoint identity and availability | `IsDepositEndpoint`, `TryDepositAt` | `SDS_Host.Ports.TryGetPort` + `ISDS_Port.Operational` | Vanilla facts are taken via host interfaces; both places share the private `TryGetOperationalPort` and do not reference building types directly |
| Usability on this map | `IsGridUsable`, `HasUsableBuildingEndpointWithin` | `SDS_Host.Ports.IsLocalUsable` | Same as above |
| Radius reachability | `HasUsableBuildingEndpointWithin` | `Map.reachability.CanReach`, `LocalTargetInfo`, `PathEndMode`, `TraverseParms.For(TraverseMode.PassDoors)`, `IntVec3.InBounds` | A read-only reachability query; performs no pathfinding or movement; facts are taken per map, not via `Find.*` |
| Display thing / mirror identity | `IsDisplayThing`, `TryGetDisplayKey`, `IsMirror` | `SDS_Host.Display` / `SDS_Host.MirrorQuery` | Answers identity only; no registration, no mirror changes |
| Withdrawal cell | `TryGetWithdrawCell` | `ISDS_Port.AccessCell` | A read-only interaction cell; does not move the pawn |
| Manual deposit availability | `IsManualDepositAvailable` | `SDS_Host.DepositJobs.IsManualDepositAvailable` | Forwards the Vanilla judgment; adds no new gates |

Of these, `HasUsableBuildingEndpointWithin` is the only Api method that touches vanilla reachability; it exists only in the adapter bridge, used for supply / radius judgments, and the five axes themselves do not call it.

### 7.4 Why these judgments stay in the Api

| Reason | Explanation | Source |
|---|---|---|
| Core's hard red lines | Core forbids `Find.*`, `mapPawns`, pathfinding, `SlotGroup`, and other vanilla-behavior entry points, so these judgments cannot sink into Core; the Api itself allows only one `Find.*` — `Find.TickManager` | ModuleRules.en.md 6.3; ModuleResponsibilities.en.md §7 |
| Core accepts only fact DTOs | Core receives `SDS_EndpointQuery`; endpoint facts are provided by `ISDS_Port` / `ISDS_PortProvider`; it has no concept of "player faction" | ModuleInterfaces.en.md 3.3 |
| The caller gate is an API boundary rule | The old contract states explicitly that "ownership / faction / forbid checks are filled in by the facade and cannot rely solely on the underlying `grid.TryDeposit` / `TryWithdraw`", and `HaulAIUtility.PawnCanAutomaticallyHaulFast` is not reused | PublicAPI.en.md §3.4 |
| Core can run outside the game environment | `SDS_Ledger` construction does not depend on `Game` / `GameComponent`; stuffing player policy into Core would break this testability | ModuleInterfaces.en.md 3.1; SDS.Core/DesignDecisions.en.md §3 |
| Vanilla only provides facts and execution | Vanilla is not responsible for the legitimacy of third-party callers; if Vanilla judged factions, the Api would depend on Vanilla in reverse, violating the reference direction of ModuleRules.en.md 6.5 | ModuleRules.en.md 6.4, 6.5; ModuleResponsibilities.en.md §7 |
| The same-map judgment compares call inputs | `ValidateDepositCall` compares `thing.MapHeld` with the `map` / endpoint `MapHeld` passed in by the caller — input validation, not a storage rule | `SDS_API.cs` |
| The tick belongs to the read-model version | The `tick` of `SDS_ReadVersions` must be fetched together with the mirror / endpoint / ledger versions; the reclaim inspection's tick is provided by the Vanilla-side `ISDS_ReclaimWorld.CurrentTick` (Core does not read that slot), and the consumers differ | ModuleInterfaces.en.md 1.1, 1.2 |

### 7.5 Boundaries and known differences

- The player-faction judgment still has two spellings: the five-axis `TryFindWithdrawPoint` goes through `IsPlayerPawn` (`Faction.OfPlayerSilentFail`), while the adapter bridge's `TryGetWithdrawCell` still compares `Faction.OfPlayer` directly; M2 only consolidated the withdrawal-point resolution and did not change either caller gate (pending).
- The current Api source contains no `Claimable` / `ClaimableBy`; ownership is judged only by `Thing.Faction` and the root holder's faction. `SDS_ApiCaller.claimOwner` is only assigned, never used in judgments (`SDS.Contracts/SDS_API.cs`).
- The deposit same-map judgment compares only the thing with `map` / the endpoint; `caller.pawn.Map` is not compared explicitly. This differs from the PublicAPI.en.md §3.4 wording "caller, thing, and endpoint are on the same map" (pending).
- Corpses are judged by the physical thing's faction, without inspecting `InnerPawn` (PublicAPI.en.md §3.4); this avoids treating the vanilla pawn inside a corpse as a second ownership source.
- See and Read are read-only axes and do not require the player faction; only entry points that change state or involve reachability pass the player gate (PublicAPI.en.md §3.4).
- Non-pawn deposits select endpoints with `DepositForNonPawn`: no own port, no `requireReach`, distance computed from the reference cell (PublicAPI.en.md §3.7).
- `TryWithdraw` takes no `Map` and validates only pawn validity; `TryWithdrawFromMirror` is where the pawn and mirror maps are compared explicitly.
- Vanilla types appear only inside method bodies and never enter Contracts DTOs; contract fields remain governed by ModuleInterfaces.en.md §1.

## 8. Third-Party Support

### 8.1 The two entries

The five axes (See / Read / Find / Take / Store) belong to the `SDS_API.Version` contract, aimed at external third parties and the compatibility layer (ModuleResponsibilities.en.md §7); the adapter bridge is public but outside the contract, with `SDS.ThirdModCompat` as its only runtime consumer (tests are covered by `SDS.Tests`); registration is in ModuleInterfaces.en.md §5. The externally visible public types are only `SDS_API` and the public types of Contracts (ModuleResponsibilities.en.md §2).

### 8.2 See / Read

| Axis | What it can do | What it cannot do | Return and failure shape |
|---|---|---|---|
| See `VisibleThingsOfDef` | Lists physical things of a def that vanilla enumeration can see on a given map (including mirrors / proxies) | Does not supplement the holder tree / `ThingGrid` / Regions; gives no counts; does not materialize or deduct | `IReadOnlyList<Thing>`; an empty list is returned when map / def is empty or on exception, and one `ErrorOnce` is recorded |
| Read `ReadStock` | For a given key: total, available count, forbidden state, whether resolved, supply failure reason, read-only versions | Creates no transactions, makes no reservations, deducts nothing; exposes no candidate projection / ordering | `SDS_ApiStockInfo`; look at the `result` field |
| Read `ReadStockFor` | Asks Core for the channel and key of a physical thing, then reads that key | Does not judge virtual / preserved itself; does not duplicate `RequiresPreservedStorage` | Same as above; when the thing cannot be resolved it returns `ReadStock(null)` with `resolved = false` |
| Read `CaptureReadVersions` | Fetches tick, mirror, endpoint, stock, reservation, and filter versions in one pass | Is not a data snapshot and must not be used to read or write stock | `SDS_ReadVersions`; on exception an all-zero default is returned |

### 8.3 Find / Take / Store

| Axis | What it can do | What it cannot do | Return and main result codes |
|---|---|---|---|
| Find `TryFindDepositEndpoint` | Selects a usable deposit endpoint in non-pawn mode | Makes no reservation, credits nothing; ignores reachability and own ports | `bool` + endpoint `Thing` + code: `NoEndpoint` / `NotOwnedByCaller` / `NotPlayerFaction` / `GridUnavailable` |
| Find `TryFindWithdrawPoint` | Finds a player pawn's own port or the nearest withdrawal point and reports whether it is the own port | Read-only; no reservations, no taking | `bool` + endpoint + `self` + code: `NoEndpoint` / `InvalidArgument` |
| Take `TryWithdraw` | Materializes virtual stock into real `Thing`s by key | Handles the virtual channel only; preserved keys, `VirtualHandoff`, and invalid pawns are all rejected; does not deliver | `List<Thing>` + code: `NoStock` / `TransactionFailed` / `UnresolvedDef`, etc. |
| Take `TryWithdrawFromMirror` | Withdraws a physical thing from the specified mirror | Does not deliver; mirrors themselves make no preserved distinction; the pawn and mirror must be on the same map | A single `Thing` + code: `NotWithdrawable` / `TransactionFailed` |
| Take `TryWithdrawPreservedExact` | Withdraws preserved stock per the exact true-body plan given by the caller | Does not pick true bodies for the caller; no withdraw by def in bulk (bulk lives in the bridge); forbidden categories are rejected outright | `List<Thing>` + code: `Forbidden` / `TransactionFailed` / `InvalidArgument` |
| Store `TryDepositNow` | Selects an endpoint and immediately credits a single item | No bulk, no job creation, no half-stack SplitOff | `bool` + `accepted` + code: `NoEndpoint` / `DepositFailed` / `EndpointUnavailable`, etc. |
| Store `TryDepositAt` | Atomically credits a single item to a known endpoint | The endpoint must be a usable port of this mod; does not deliver | Same as above |

Additional facts: the result of `TryFindWithdrawPoint` is used for movement and delivery positioning; `TryWithdraw` materializes by key only and takes no endpoint parameter.

### 8.4 Semantics of `SDS_ApiResultCode`

The enumerated member list of the 16 codes is in SDS.Contracts/Interfaces.en.md A.1; the table below records only the API boundary semantics.

| Code | Meaning |
|---|---|
| `Ok` | Success |
| `InvalidArgument` | Invalid arguments: empty values, non-positive quantities, a preserved key given to a virtual axis, `VirtualHandoff`, an invalid pawn, or a deposit across maps |
| `GridUnavailable` | The global ledger is unavailable (`SDS_Rules.Available == false`), or the job-based host is not injected |
| `NoEndpoint` | No usable endpoint / withdrawal point was selected |
| `EndpointUnavailable` | The known endpoint is destroyed, is not a port, or is not operational |
| `NotDepositable` / `DepositFailed` | Core judged the thing not depositable / the ledger crediting failed or the accepted amount was 0 |
| `Forbidden` | The physical thing is forbidden, or the supply failure reason is `Forbidden` |
| `NotOwnedByCaller` | The thing or endpoint does not belong to the caller's faction |
| `NotPlayerFaction` | The caller's faction is not the player faction |
| `NoStock` / `UnresolvedDef` / `RebuildFailed` | Mappings of the supply failure reasons `Insufficient` / `UnresolvedDef` / `RebuildFailed` |
| `NotWithdrawable` | Invalid mirror-withdrawal parameters or a non-withdrawable target |
| `TransactionFailed` | The Core transaction failed, returned an empty result, or a bulk withdraw by def fell short of a whole batch and rolled back |
| `InternalError` | An exception was caught; one `ErrorOnce` is recorded uniformly, and the exception is never leaked |

### 8.5 Purpose and boundaries of the adapter bridge

Method names and signatures are in ModuleInterfaces.en.md §5 (the sole registry of the bridge surface).

| Category | Purpose | Boundary |
|---|---|---|
| Endpoint / availability | Whether an endpoint exists, whether it is usable on this map, whether the pawn's port and manual deposit are available, and the withdrawal cell | Read-only; does not select endpoints or assign jobs |
| Ledger read-only | Available counts, keys, forbidding, preserved true bodies, withdrawable keys by def, recipe slot judgment | Creates no transactions, makes no reservations; available counts uniformly go through `SDS_Availability` |
| Display / mirror | Judging mirror / display thing identity, reverse-looking-up the display key | Read-only; no registration, no form changes |
| Source / channel | Source-exclusion markers, deposit-job determination, def-level channel determination | Markers apply only to just-materialized physical things; channel determinations are forwarded to Core |
| Job assignment | Candidate gate, building deposit jobs, inventory unload job | Forwards only to Vanilla's sole deposit service; adds no new job-assignment paths |
| Withdraw by def in bulk | Virtual is combined key by key; preserved follows the true-body plan (supports stacked counting); a shortfall rolls back the whole batch | Adds no new ordering / deduction rules; on failure nothing is left half-done |
| Supply planning | Slot judgment, def selection, shortfall computation, deposit feedback, restock-job take-amount registration | The shortfall lives only in `SDS_BillSupplyPlanner`; the registrar records only numbers |
| Return | Preserved returns, virtual returns (`landOnFailure` controls whether failures land on the `Ground`) | Forwards to `ReturnThing` only; the return is credited to the ledger first; on failure the thing lands on the `Ground` and `cell` is the preferred landing cell for that failure; with `landOnFailure=false` the thing is left as-is |
| Loading | Injection of third-party-built load manifests | Injects display things only; no taking, no reservations, no deduction (W50) |

The bridge's "withdraw by def in bulk" is only a per-key combination of the same transaction primitive and the exact true-body plan, plus a whole-batch return; it does not constitute a second set of capacity, ordering, or deduction rules (ModuleInterfaces.en.md §5).

### 8.6 The three boundaries

**Third parties use a soft dependency only**

- External third parties should not statically reference `SDS.Api.dll`; instead, probe `SDS_API.Version` via reflection before calling.
- `Version = 4` is the compatibility declaration for the assembly rename: the five-axis semantics are unchanged, but the reference path changed, so third parties must re-establish their soft dependency (ModuleResponsibilities.en.md §7; PublicAPI.en.md §3.8).
- Within the repository, `SDS.ThirdModCompat` and `SDS.Bootstrap` statically reference `SDS.Api` (ModuleRules.en.md §4); in production code only `SDS.ThirdModCompat` calls the five axes or the adapter bridge directly.

**Only via the Api; no direct connections to Core / Vanilla (E6)**

- The compatibility layer may go only through `SDS.Api` / `SDS.Contracts` / `SDS.Diag`; `CheckCompatSurface.ps1` rejects Core / Vanilla references (ModuleRules.en.md §3, §10).
- `InternalsVisibleTo` registers only `SDS.Core → SDS.Rules`; third parties also cannot physically see Core's internal types (ModuleRules.en.md §5).
- Target mods may only be probed via reflection; their assemblies must not be hard-referenced (ModuleRules.en.md 6.6).

**Each third-party entry has exactly one write-takeover path (E7)**

- Read-only projections may coexist with adapter translation; but two deduction, job-assignment, or rollback paths must not coexist on the same entry (PublicAPI.en.md §4.3).
- Typical division: job-based entries go only through the job-assignment bridge; direct taking goes only through `TryWithdraw`; destination discovery goes only through `TryFindDepositEndpoint` with delivery still following the vanilla container contract; load manifests go only through the injection bridge (PublicAPI.en.md §4.11).
- Once an adapter computes available counts, shortfalls, or rollback order itself, it violates E7's single-write-path constraint and bypasses the compatibility boundary defined by E6 (ModuleRules.en.md 6.6).

### 8.7 Minimal call flow: taking

1. Probe `SDS_API.Version` via a soft dependency to confirm the facade exists.
2. Find an endpoint: a player pawn calls `TryFindWithdrawPoint` to get the withdrawal point and whether it is the own port; the result is used only for movement / delivery positioning.
3. Read stock: `TryGetStockKey(thing)` + `ReadStockFor(thing)`, or directly `ReadStock(key)`; def-level queries can use the bridge's `TryFindWithdrawableKey` / `GetAvailableCountForDef`.
   - Def level has two established semantics: the bridge's `TryFindWithdrawableKey` / `GetAvailableCountForDef` use the production / take criteria (forbidden counts as zero, preserved by the **true body def**, minified = the inner def); `GetPreservedAvailableCount(ThingDef)` counts by the **outer shell def**, aligned with vanilla `ThingsOfDef`. The two must not be interchanged.
4. Take: for the virtual channel call `TryWithdraw`; for the preserved channel, compose the exact plan yourself with `AppendPreservedThings` and call `TryWithdrawPreservedExact`, or directly use the bridge's `TryWithdrawPreservedByDef`; when the target is a mirror call `TryWithdrawFromMirror`.
5. Deliver yourself: hand the returned `Thing` to the target with vanilla `carryTracker` / `inventory` / `ThingOwner` / ground-placement utilities; the Api does not deliver on the caller's behalf.
6. On failure or interruption: call `TryReturnVirtual` / `TryReturnPreserved` to return; a bulk withdraw by def that falls short returns the already-taken portion as a whole batch and never leaves a partial take.
7. Result codes: `Ok` means continue; handle normal rejections like `NoStock` / `NoEndpoint` silently; `InternalError` has already been logged once by the Api — do not spam the log again.

### 8.8 Integration checklist

- Reference only `SDS.Api` / `SDS.Contracts` / `SDS.Diag`, with no references to Core / Vanilla implementation types; do not ship `SDS.Api.dll` as a hard dependency either (probe `Version` via a soft dependency).
- Call only via the five axes or the registered adapter bridge, without directly reading `SDS_Ledger` / `SDS_Core` / `SDS_Host`.
- Available counts, shortfalls, priorities, capacity, or rollback order are not computed inside adapters; each third-party entry has exactly one write path, with no simultaneous reliance on both the facade and self-built Harmony write patches.
- Target mods are only probed via reflection, with no static reference to their assemblies.
- Logging uses only `SDS_Diag.ErrorOnce` / `Warn` / the report center, without calling `SDS_Diag.Fail`.
- If the adapter bridge is used, accept that it is not covered by the `Version` commitment and may change along with the adapters.
