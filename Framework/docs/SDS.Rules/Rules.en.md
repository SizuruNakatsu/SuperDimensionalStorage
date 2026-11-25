# SDS.Rules · Rules

> Positioning: The checkable checklist and rule body of "what this module must comply with".
> **Current status (P5, migration complete)**: Endpoint resolution, bill supply, decision layer, and process layer rules have all been migrated in; `SDS.Core/Rules.en.md` keeps only ledger-side rules. During migration **normative sentences remain verbatim unchanged**; only the authoritative document and code anchors change.

## 1. Process and Bill Supply Rules (P2–P3)

### 1.1 S16 Third-Party Bill Supply

- Normative sentence: third-party bill supply (recipes / network orders): the third party's own inventory comes first, and SDS fills only the shortfall; ingredient def selection, `required` / `fromOwn` / `shortfall` computation, and reservation happen exactly once, only in `SDS_BillSupplyPlanner`; the adapter only provides facts and executes withdrawal / deposit.
- Implementation: `SDS_BillSupplyPlanner` (the two entry points `TryPlanSlot` / `TryPlanAllowedDefSupply` share `TryPlanFromCandidates`; `NotifyDeposited` only does pool counting).
- Input boundary: own quantity / external availability / periodic usage / reservations enter only as delegates; the medical tier is injected by the caller merging `SDS_MedicalGate.Allows` into `extraAllows` (S17); the planner does not read `SDS_Host.MedicalCare`.
- Verification: the unique `// RULE:S16` marker via `CheckRuleSingleSource.ps1`; `SDS.Rules.Tests/BillMedicalGateTests` (injection rejected / allowed / allowed without a gate / planner rejects the ingredient).

### 1.2 Endpoint Candidates and Ordering (originally Core rules §12.16 / §12.28)

- Candidates = all available endpoints (Hyperdimensional Cores / Hyperdimensional Anchors / pawns with chips / mechanoids) + the self port; vanilla storage cells / storage containers do not enter this decision — `SDS_DestinationResolver` compares them as the opponent after an endpoint wins (§12.23).
- Ordering (between endpoints, `SDS_EndpointResolver.IsBetter`) = priority → distance → `thingIDNumber`; the self port participates by real distance and no longer enjoys self priority.
- Ordering (endpoint vs vanilla destination, `SDS_SearchRanker.PreferDestination`) = priority → distance → ties go to the endpoint → `thingIDNumber`.
- Unknown third-party containers are allowed through, never rejected for being unknown.
- The decision order is fixed: static gate (operational / self / storable / stocked) → ordering → **reachability checked only for the current winning candidate**; only if it is unreachable, fall to the next candidate.
- Pre-checking `CanReach` for every candidate one by one is forbidden; the self port is always reachable; `actor == null` performs no reachability check; reachability results are never cached.
- Implementation: `SDS_EndpointResolver`; the contract version clock is `SDS_EndpointContractCache` in this assembly.

### 1.3 S17 Medical Gate (implementation location)

- The normative sentence's authority remains in `SDS.Vanilla/Rules.en.md` §10; Rules owns only the implementation constraint "do not read the host; the caller injects `extraAllows`".
- The planner must not determine the medical tier itself, nor duplicate `SDS_MedicalGate`'s evaluation logic.

### 1.4 W21 Transaction Full-Amount Semantics

- Normative sentence: for instantaneous transactions, the execution-time full-amount semantics is fixed by the entry point: with the exact mode, any shortfall rolls back the whole transaction; with the best-effort mode, delivery matches what was actually obtained.
- Implementation: `SDS_WithdrawTransaction.Begin` / `BeginSingle` establishes reservations; the executor uses the `requireFull` passed in by the entry point to decide between "shortfall rolls back the whole transaction" and "deliver what was actually obtained".
- Verification: transaction state machine assertions; `SDS.Rules.Tests/TransactionTests` covers both the exact-failure and partial-success paths.

### 1.5 W27 Rollback by Channel

- Normative sentence: rollback must take the channel-specific fallback per transaction channel (preserved ReturnPreserved, virtual ReturnVirtual); generic Return is forbidden; per-item failures are logged and fall back to a ground drop.
- Implementation: `SDS_WithdrawTransaction.Rollback` dispatches to `SDS_ReturnUtility.ReturnPreserved` / `ReturnVirtual` by the plan's channel; no generic fallback branch exists.
- Verification: code review of the rollback path; `SDS.Rules.Tests/ReturnTests` covers returns on both channels.

### 1.6 W49 In-Transit Materialization Marker Lifecycle

- Normative sentence: the in-transit materialization marker is valid only during delivery; any return / ground drop / failure fallback initiated by this Mod must unmark it, and a physical thing must never linger under an "in-transit" identity.
- Implementation: `SDS_ProducedThingMarker` (`Mark` / `Unmark` / `IsMarked` / a 1-day `Reset` safety net); both return paths of `SDS_ReturnUtility` call `Unmark`; static cleanup is registered by `SDS_Rules.RegisterAll`.
- Verification: anchor existence via `CheckRuleIds.ps1`; code review of the return paths.

### 1.7 Transaction State Machine Constraints

| Rule | Specific constraint | Verification |
|---|---|---|
| `Deliver` only releases the reservation | No hauling, no calls to the delivery host; callable only after the caller guarantees the true body has been handed over to vanilla; `Rollback` is forbidden after `Delivered` | Code review of `Deliver`; E2 `AssertTransactionTerminal` |
| Partial success does not roll back | `TryWithdrawReservedForJob` handles only the virtual channel; returns true once materialization succeeds; Execute consumes and releases the reservation, and Vanilla calls `ReturnThing` for undelivered items | `TransactionTests.ReservedJob_PartialSuccess_ReturnsUndeliveredBack` |
| `Rollback` goes by channel | Preserved uses `ReturnPreserved`, virtual uses `ReturnVirtual`; the channel comes from the Execute path, never inferred from the def; per-item try/catch | `TransactionTests.Transaction_RolledBack_WhenExecuteFails`; review of `RollbackMaterialized` |
| Reservations only through the facade | Creation / release only via `SDS_Rules.TryReserveForJob` / `TryReservePreservedForJob` / `ReleaseJobReservation`; auditing only in `SDS_Core.AuditReservations` (the ledger provides only snapshots and release by owner id; liveness facts come from the host) | the six cases in `ReservationTests` |

### 1.8 Return Null-Handling Rules

1. Ledger first: `ReturnVirtual` → the ledger's sole atomic "physical-to-virtual" implementation `TryConvertThingToVirtual` (detach → post to ledger → destroy; if the destroy does not take effect or throws, roll back per the actual state and re-attach); `ReturnPreserved` → `TryDepositPreserved`.
2. `map == null`: no ground drop, no delivery call; log `ErrorOnce`; the physical thing stays as is.
3. `pawn == null` with fallback `PawnInventory` / `PawnCarry`: log `ErrorOnce` and treat as `None`.
4. `fallback == None`: return directly; no branch ever actively destroys the physical thing on the map — the temporary instance is consumed only after a successful return.

Verification: `ReturnTests.ReturnThing_MapNull_DoesNotFallback`, `ReturnThing_LedgerFirst_NoDelivery`. Source: ModuleInterfaces.en.md §3.4; the `SDS_ReturnUtility.DeliverFallback` comments.

### 1.9 The Three Steps of Mirror Withdrawal

Executed only in `SDS_WithdrawTransaction.ExecuteMirror`, in fixed order:
① The actual debit count = `min(quantity, mirror.stackCount)` (whole-stack conversion can deliver only the mirror stack amount, never debiting beyond it per the request); first confirm the reservation is sufficient (`GetReserved(owner, key) >= units`, same semantics as 12.33), then capture `vitals` and call `ledger.TryDebitVirtual(key, units)`;
② `SDS_WithdrawPlan.physical.TryTakeWholePhysical(key, mirror, out piece)` (whole stack) or `TryTakePhysical(key, mirror, units, out piece)` (partial);
③ Physical failure with no physical thing produced → `ledger.TryDepositVirtual(key, units, vitals)` to credit back; success → `ConsumeReservation(id, key, units)`.
The reconcile branch of failure cleanup: when the physical layer reports failure but a physical thing has been produced, or the produced count does not match the debited count, first align the ledger discrepancy to the actual count (`AlignMirrorDebit`), then register the physical thing into `materialized` and hand it to `Rollback` for whole-item return — ledger and reality must return to the pre-call state, leaving no "one on the ledger, one in the world" split.

Additional constraints: both `key` and `physical` are passed in by the transaction plan; inferring them from the mirror is forbidden, and Rules must not read `SDS_Host.MirrorPhysical` on the spot; the physical layer never touches the ledger and must not call back into the executor or `SDS_TakeService`.

Verification: code review of `ExecuteMirror`; `SDS.Rules.Tests/Core/ReviewRegressionTests.Mirror_PhysicalCountMismatch_RollsBackLedger` covers the ledger return when physical counts mismatch, while full mirror A/B is still covered by Vanilla. Source: ModuleInterfaces.en.md §3.4; ModuleResponsibilities.en.md §5.

## 2. Decision Layer Rules (P4)

### 2.1 W22 External Consumption Only Debits

- Normative sentence: external consumption only debits and never retroactively alters reservations; availability = max(0, physical − reserved), and reservations may temporarily exceed physical. The mirror's `Destroy` does not follow this semantics (W19 fail-closed).
- Implementation: `SDS_AvailabilityImpl.AvailableOf` (`RULE:W22`); `TryConsumeMirror` only debits, never reserves (no production callers today; mirror Destroy follows W19); the ledger's `GetAvailable` does not trim others' reservations.
- Verification: self-check availability range; `LedgerTests.*`; E2 `AssertAvailableWithinTotal`.

### 2.2 W33 Forced Virtualization of Weapons / Apparel

- Normative sentence: when the "force virtualization of weapons/equipment" setting is on, items with `IsWeapon` / `IsApparel` no longer stay preserved because of `CompArt`'s def-level presence and generated instances, `CompColorable.Active`, `CompStyleable.styleDef`, or `CompApparelVerbOwner_Charged`; quests, relics, personality, generated names, unique traits, biocoding, shrouds, fuel, and loading still stay preserved.
- Implementation: `SDS_StockRules.RequiresPreservedStorage`, `CanRebuildDef`, `IsProtectedThing`, `HasInstanceNonKeyState`; the switch only shrinks the exemption criteria and never adds a second channel.
- Boundary: `CompApparelVerbOwner_Charged` is "charging" and is ignored when the switch is on; "loading" means CE loaded ammo (an external `SDS_PreserveHooks` fact); Rules does not read the host.
- Verification: anchor existence via `CheckRuleIds.ps1`; `MigrationTests`.

### 2.3 W35 Per-Instance Tracking Is Always Preserved

- Normative sentence: any item with `ThingDef.IsStudiable` always goes through the preserved channel, taking precedence over the four default-virtual categories; this criterion asks only vanilla capabilities and never enumerates mods / defNames / comps; preserved true bodies do not participate in same-key merging.
- Implementation: `SDS_StockRules.IsTrackedPerInstance` (`RULE:W35`); `CanRebuildDef` vetoes per-instance tracking first; `SelfCheck` validates the invariant.
- Verification: `CheckRuleIds.ps1`; the unique `// RULE:W35` via `CheckRuleSingleSource.ps1`.

### 2.4 W56 Single Implementation of Take Clamping

- Normative sentence: take clamping (= min(demand, availability)) has exactly one implementation: `SDS_Availability.ClampTakeToAvailable(int wanted, long available)`; if either value is non-positive the result is 0 (nothing is taken this time). Every take entry point (Job materialization / loading trips / outfitting deltas / manual output / trading and launching / compatibility adapters) must call it; availability itself comes only from `GetAvailableCount` (W22) or `GetTradableCount` (R15) — never self-computed, never inlining the same formula.
- Implementation: `SDS_AvailabilityImpl.ClampTakeToAvailable` (`RULE:W56`).
- Verification: duplicate-formula scanning via `CheckRuleSingleSource.ps1`; `TripCountTests`.

### 2.5 R15 / R17 Read-Side Availability

- R15 normative sentence: trading / gifting / launching read "physical stock − transaction reservations" and are not zeroed by category disabling; production / taking is still zeroed when disabled.
- R17 normative sentence: when G is off (no operational core exists in the world), all read-side availability zeroes out; ledger data is retained, and this does not count as category disabling.
- Implementation: `SDS_AvailabilityImpl.GetTradableCount` (`RULE:R15`) / `GetAvailableCount` (`RULE:R17`); the global gate reads `SDS_RulesImpl.Available`.
- Verification: self-check availability range; `LedgerTests.*`.

## 3. Migration-Period Constraints

1. **Move, never copy**: at any moment the same rule exists in exactly one implementation file; Core deletions and Rules additions must complete within the same phase.
2. **No forwarders**: Core does not reference Rules, so no forwarding implementations of the old facades are kept; consumers switch to `SDS_Rules` in the same phase.
3. **Normative sentences unchanged**: migration changes only the "authoritative document / code anchor / sole implementation registration" entries in RuleIndex.en.md, never the normative sentences; the `// RULE:<ID>` marker moves with the implementation and remains exactly one occurrence repo-wide.
4. **Vanilla contact constraints are not migrated in**: patch hook points, Job / UI timing, and vanilla API constraints remain defined by `SDS.Vanilla/Rules.en.md`; Rules does not reference Harmony / UI / Job.
5. **Deferred dependencies are over**: the P3 process layer reads the ledger via the Core friend grant; the P4 decision layer is already inside the same assembly, Core keeps only the ledger and the `SDS_StockData` data primitives, and no cross-phase dependency remains.

## 4. Verification

- The migrated S16 / W21 / W22 / W27 / W33 / W35 / W49 / W56 / R15 / R17 are validated by `CheckRuleIds.ps1` / `CheckRuleSingleSource.ps1`; endpoint rules have no standalone ID and are covered by `SDS.Rules.Tests/EndpointTests`.
- Ledger-side rules (W26 / W34 / E1 / E2) are still registered per RuleIndex.en.md and validated in `SDS.Core/Rules.en.md`.

## 5. Supplementary Semantics (Migrated from Core Rules §12)

> The numbering below preserves the original Core rules §12 numbering for historical reference; on conflict, this file prevails.

### 12.3 Multiple Trips = Multiple Transactions
- Each trip opens its own independent transaction; transactions never suspend across trips and are never reused.
- Weight / CE capacity must be judged before `Begin`; if it cannot fit, fail outright — never open a transaction first and then force items in.


### 12.4 Ground Materialization While a Job Is Suspended but Not Terminated
- Reachable: keep using that physical thing.
- Destroyed: no credit-back, no rebuild by def.
- Unreachable: the Job fails; the physical thing stays where it is.
- Entered another pawn's inventory: give up that item, no reclaim.
- Never roll back a transaction because of displacement.


### 12.5 Triple-Reservation Exclusivity
A single thing must never hold all three reservations at once — "mirror reservation + ledger reservation + true body reservation"; holding any one of them already counts as occupied.


### 12.6 Materialization Marker Scope
- Applied only to the four landing destinations: map / hand / inventory / ground.
- Trading, outfitting, and loading do not apply markers.
- A 1-day lifetime acts as the safety net; the marker expires automatically.


### 12.7 The Write Side While the Global Switch Is Off
While the global switch is off, `Begin` / `Execute` are always rejected — it is not merely a read-side zeroing.
Deposits (`CanDeposit` / `TryDeposit`) are not constrained by the global switch: G affects only the read side and withdrawal transactions, and already stored data must be retained.


### 12.8 The Three-Layer Semantics
- `Availability` → `Plan` → `Execute`, narrowing at each layer.
- Key-level reservations are only a gate, not an authorization; authorization comes from the re-checks in `Plan` and `Execute`.
- Key selection by def has exactly one implementation (exact key → largest availability → preserved fallback); the read side splits into two tiers: production / taking uses `TryFindWithdrawableKey` (zeroed when disabled), trading / gifting / launching uses `TryFindTradableKey` (not zeroed when disabled, R15); both tiers share the same selection order and must never each get their own copy.


### 12.9 `ConsumeReservation` Is an Atomic Three Steps
`ConsumeReservation` completes its three steps atomically in fixed order: debit the ledger first, then consume the reservation; failure must never leave a half-consumed state.


### 12.11 The Ladder for `TryAdd` Failure After `SplitOff`
- The seven-step ladder: `TryAdd` → absorb → `TryPlace` → `Spawn` → record `partialRemaining` → fall back → never dangle.
- Stop at the first successful step; skipping steps is forbidden; mirrors are forbidden from `SplitOff`.


### 12.12 Collection-Level Capacity Pre-Check
After the list is built, perform one collection-level capacity pre-check; it only lowers the failure probability and does not promise zero failures — per-item writes must still tolerate failure.


### 12.13 The Tie-Breaking Table
When an endpoint and a vanilla candidate tie, the endpoint wins — this is an **intentional deviation**; vanilla picks the container only when strictly greater.


### 12.14 The Complete B1 Criteria
- Criteria = the four default-virtual categories (skipping def-level and instance-level non-key state) + the 5 def-level items + the instance-state enumeration + identity-type protected instances (`IsProtectedThing`); the four categories are not absolutely virtual — quests / relics / personality / generated names / art etc. still stay preserved.
- "Version" is not a criterion.
- When extending the list, prefer vanilla capability bits; never enumerate mods / defNames.


### 12.15 Criteria Are Not Retroactive
- Existing virtual entries are judged only by `key.preserved`; the new criteria never retroactively re-judge them.
- The virtual and preserved channels of the same def may coexist.


### 12.17 The Read Pipeline (0–6) and the Read-Only Boundary
- 0 `ReadVersions` snapshot (Map / Stock / Endpoint / Position / StorageSettings / Preserved); 1 `Enumerate(kind)` thin adapters, never unified; 2 `PureProjection` pure functions (Source: valid cells / valid distance / candidate types; Destination: valid cells / `Accepts` / priority / capacity); 3 `ReachabilityProjection`, key = map + root + traverseParms + reachVersion, TTL 30; 4 `Filter` strictly in vanilla order; 5 `Rank` conditional ordering keys; 6 `Freeze` read-only result + version snapshot — no reservation, no debit, no materialization.
- Field grouping: `Scope` (map / root / actor / faction), `Query` (kind / def / ThingRequest / filter / group / quantity / preserved), `Projection` (scope / peMode / traverseParms / maxDistance), `Policy` (validator / priorityGetter / tieBreaker); `priorityGetter` is read once per candidate and cached in Rank only, never called in Filter.
- **R1 reads are read-only**: no changes to stock, reservations, or entity ownership relations, and no writes to the trade registration table / preserved bucket mapping; a validator writing `JobFailReason` / diagnostics does not count as breaking read-only. A read session ends at Freeze; execution re-checking is a consumer contract — on re-check failure, fall back to "re-resolution"; the executor must never re-pick the source or the delivery mode.


### 12.18 Transaction Invariants (W1 / W3 / W4 / W6–W16, still in force)
- W1 A transaction has exactly one debit point, and the same quantity is debited exactly once; a Job may consist of multiple transactions, each with its own reservation, debit, and rollback.
- W3 The delivery mode is fixed at `Begin` and must not switch during execution; all transaction entry points (`Begin` / `BeginJobAdopt` / `SDS_API` withdrawal) always reject `VirtualHandoff`; the enum exists only for historical compatibility.
- W4 Failure rolls back only the not-yet-delivered quantity; never duplicate, never lose.
- W6 The search layer handles visibility only — no materialization, no debits.
- W7 Quantity settlement for trading / outfitting / loading must go through transactions; no cross-dialog reservations. Instant settlement uses each flow's own direct-posting path, not `VirtualHandoff` transactions (the enum is historical compatibility only).
- W8 While the global switch is off, `Begin` / `Execute` are always rejected.
- W9 The delivery mode is decided by the vanilla contract; forcing every Job into inventory or onto the ground is forbidden.
- W10 Multiple keys are all-or-nothing; a transaction closes once in-transaction delivery completes; multiple trips = multiple transactions — never held open across trips; never force items in when capacity falls short.
- W11 Multi-key reservations must be atomic: validate everything first, then write in one go; partial holding is not allowed.
- W12 All debits go through reservation-aware executors; scattered `TryWithdraw` calls are forbidden; the sole exception is external consumption confirmation.
- W13 Ground materialization holds precise `Thing` references in the transaction; rollback is precise to the item.
- W14 Transaction objects are never persisted; what is persisted are ledger reservations and ledger contents. After loading, the Job re-derives the transaction with `jobLoadID`; suspended reservations persist, and true termination releases them via the Job's `FinishAction` / `SDS_Core.AuditReservations`.
- W15 Scopes are Instant / Job / Session, fixed at `Begin`; Session has been merged into Instant, leaving two kinds today.
- W16 Saves store only verifiable reservations (owner id / key / count / pawn reference); preserved true bodies are persisted by the Core private owner itself; plans are rebuilt by the Job after loading and are never saved separately.


### 12.19 Multi-Key Atomic Reservation (supplementing W10/W11)
- First run one total-quantity validation over all keys; only when everything passes, write in one go; independent of traversal order, never partially held.
- If any key fails to reserve, release all keys already reserved.
- If any key's debit fails in `Execute`, return the already-debited keys and fail the whole transaction.
- "Steel reserves successfully, components fail, yet the steel is still taken" is forbidden.


### 12.20 The Four Principles of Preserved Taking and No Rebuild
- ① Act only at the exact moment vanilla explicitly takes; ② first aggregate the itemized plan into a **key-level reservation** (ledger reservations do not distinguish Things), and at execution verify per key that `Σplan ≤` reserved and verify per item that the true body is still with its owner; ③ deliver the true body as is, no rebuild; ④ if any item is not with its owner or the count falls short, roll back the whole transaction.
- `TryWithdrawPreservedPlan` detaches the exact true body from `preservedOwner` → **never rebuilds InnerPawn / InnerThing / GeneSet** → rolls back on failure.
- `ExecutePreservedExact` must aggregate planned counts by key before executing and verify per key that `GetReserved(owner, key) >= Σplan`; any shortfall fails the whole transaction with a `Fail` log — never rely solely on the implicit closure of "the same plan deriving the reservation".


### 12.21 The Three Delivery Contracts
- Hand: the true body goes into `Pawn_CarryTracker` / `Pawn_InventoryTracker` (DoBill ingredients, transport loading, preserved ingredients).
- Container: the true body enters the target container via `ThingOwner.TryAdd` (sarcophagi, transport pods, gene banks, caravan packs); equipment goes through `AddEquipment → TryAdd`, which counts as Container.
- SpawnedTarget: enabled only when explicitly registered (the escape hatch). Only this form may materialize a mirror to the endpoint during job assignment and rewrite `targetA/B/C`; currently only bottle-feeding registers it. The default Hand relies on W58 (missing count) / W59 (materialization at the consumption point) / withdrawal primitives swapping in the true body at the consumption point.


### 12.22 Read-Side Failure Code Priority
`GetFailure(key)` has a fixed order: `key == null || !Resolved` → `UnresolvedDef`; `grid == null` → `Insufficient`; `IsForbidden` → `Forbidden`; `GetAvailableCount <= 0` → `Insufficient`; otherwise `None`. That is, `UnresolvedDef` ranks highest and `Forbidden` outranks `Insufficient`; when disabled, availability returns 0, but capacity / wealth / trading are unaffected.


### 12.23 The Deposit Candidate Pool and the Self Port
- Candidate pool = vanilla `SlotGroup` storage cells + vanilla non-`SlotGroup` containers confirmed to be storage classes + all available endpoints (Hyperdimensional Cores / Hyperdimensional Anchors / pawns with chips / mechanoids) + the self port.
- The self port is an ordinary candidate participating in ordering by its real position; after endpoint resolution picks a winner, W62 folds the destination into the self port (`SDS_SelfDepositFold`, without changing the priority comparison), and once the item is in hand it is posted immediately through the single deposit entry point.


### 12.24 Capacity Ownership-Group Granularity
- Virtual and preserved share one "category count" pool; the capacity cap rejects only **new categories**, while the same (existing) category may stack without limit.
- When capacity is lowered so that `UsedCapacity > TypeCapacity`, existing stock is not evicted and existing categories are not rejected; only new categories are.
- Capacity dedupes by the `CategoryKeyOf` category key: def + stuff + quality + channel; multiple items / multiple true bodies of one category occupy a single slot.
- "Ownership group" is a historical term and not an independent capacity dimension; if slots ever need to merge across channels, define a separate L2 semantics and add `CapacityKeyOf` — `CategoryKeyOf` must not be changed (it also serves display grouping).


### 12.25 Fallback and Destination Order
- Judge acceptance before detaching the item; on failure the options are only "keep in hand / return / ground drop".
- Core does not execute destination behavior: `GenPlace` / `GenSpawn` fallbacks are allowed only in Vanilla's `SDS_PlacementUtility.TryPlace` (`Exact` / `NearPawn` / `RandomNear`, hard precondition `!Spawned`); vault / mirror destinations are exceptions. The endpoint buffer's `EjectToMap` and the withdrawal-side `ReturnToGridOrDrop` also belong to Vanilla.
- Preserved rollback goes through `ReturnPreserved` (first `TryDepositPreserved`, bypassing the global filter); virtual goes through `ReturnVirtual` / `ReturnVirtualCount`.
- Failure ground drops must not apply `SDS_ProducedThingMarker`.


### 12.28 The Endpoint Selection Decision Order (implementation migrated to SDS.Rules)
The static gate, the ordering, and the reachability-checked-only-for-the-winner sequence moved with `SDS_EndpointResolver` to `SDS.Rules/Rules.en.md`; Core no longer performs endpoint selection.


### 12.32 Quantity Conservation on Exception and Failure Paths

- Preserved withdrawal (`TryWithdrawPreservedPlan`) wraps the whole flow in try/catch: an exception must roll back the whole transaction for the already-detached true bodies, and log `Fail` (def + thingID + index).
- Preserved rollback (`RollbackPreserved`) and detachment (`TakePreservedUnits`) must check ownership transfer / removal results; failures log `Fail` — never silently lose items.
- W34 migration exceptions settle by the actual `thing.Destroyed` state: destroyed = the migration stands; not destroyed = retract the ledger entry and re-attach the true body where possible, logging `Fail` if re-attachment fails.
- On load, a true body failing to enter its container must never be dropped: it enters `pendingPreservedRestore` and is retried by every inspection pass; while not yet restored to the ledger it counts as a self-check failure item.
- Transaction materialization must register `materialized` on the first line after `TryWithdrawVirtual` succeeds (same semantics in `ExecuteVirtual` / `ExecuteVirtualReserved`); an executor adopting an existing reservation calls only `ReclaimMaterializedWithoutRelease` in its catch (return the physical thing without releasing the reservation), and the reservation is still cleaned up by the Job's `FinishAction`.


### 12.33 Quantity Floors and Reservation Consumption

- `TryDepositVirtual` accepts virtual keys only; `key.preserved == true` fails outright with a `Fail` log (preventing a preserved key from sneaking into the virtual table and then "vanishing" under other semantics).
- `DebitVirtual` never drives the ledger negative: the excess is clamped to the current amount and logged as `Warn`.
- `ConsumeReservation` returns the actually consumed count; `ExecuteVirtual` / `ExecuteVirtualReserved` must compare "materialized count == actually debited reservation"; any mismatch rolls back the whole transaction.
- Reservation entries are int: when a single key's requested total exceeds `int.MaxValue`, the whole transaction fails with a `Fail` log; silent truncation is forbidden.
- Mirror withdrawal (`ExecuteMirror`) must confirm `GetReserved(owner, key) >=` the actual debit count (`min(quantity, mirror.stackCount)`) before debiting.
- The idempotent branch of `TryReserveForJob` backfills the existing reservation quantity and never shrinks it to a later, smaller `maxStackSpace`; the caller must clamp by itself before materialization / delivery, or release and rebuild the reservation to the new space.
