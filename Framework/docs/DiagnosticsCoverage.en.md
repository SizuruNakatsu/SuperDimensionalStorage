# SuperDimensional Storage · Diagnostics Coverage

> Status: **P0 + P1 implemented, with the log surface consolidated per the user's 2026-10-21 request**:
> only "exceptions / data consistency (Error)", "one-shot self-healing (Warning)", and "one-shot statistics output" are kept;
> **normal operation (gate rejections, decision yields, no stock, unreachable, …) never writes logs**.
> This document is the single authority on diagnostics criteria.

---

## 0. Judgment Criteria

For a **real failure** to be locatable, a log must contain at least:

1. **Module + reason code**: e.g. `Mirror/drop point full`, `Take/rollback failed (single item, fallback placement attempted)`;
2. **Runtime context**: `map`, `pawn`, `thing` (id + def + count), `job` (def + id), `reservation`;
3. **Result semantics**: abandoned this attempt / rolled back / exception;
4. Exceptions additionally carry a stack trace; the same reason is throttled over time.

---

## 1. Current State Overview (measured from source)

| Metric | Value | Notes |
|---|---|---|
| Harmony patch classes | 112 | Feature patch entry points covering vanilla APIs |
| Registry entries | 29 | 19 guards + 10 projection owners (`SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge`) |
| Total patch points | 141 | 112 feature classes + 29 registry entries |
| TargetMethod text-key dedup | 138 | Output by `CheckPatchInventory.ps1 -Report`; text keys, not reflection-level; the gap to 141 comes from multiple classes hooking the same method |
| `catch (Exception)` | 307 | patch entry points + Take / Mirror / Preserved bridge / Output install execution-path guards (including executor exception rollback, preserved withdrawal exception whole-batch rollback, mirror physical conversion, capacity notice, destroy exception after a physical deposit is posted) + drop-cell WorkGiver runtime hook points + outfitting take-quantity Provider guards + third-party self-built loading manifest injection + destroy return / takeover reservation exceptions + compatibility injection gates + VC Vehicle Framework vehicle cargo bay loading / gatekeeping + Use command provider enumeration + W58 / W59 / W61 new seams + W62 / W63 drop-point folding and placement-into-cell posting + load-time collection / remapping / immediate timing / loading row rebuild / display thing re-sealing guards + W60 mirror Job target consolidation + invalid mirror replace-with-new guard |
| `SDS_Diag.ErrorOnce` | 251 | Exceptions: message + call site + exception type (one-shot); includes call sites in SDS.Tests / SDS.Core.Tests / SDS.Rules.Tests |
| `SDS_Diag.Fail` (Error) | 93 | Data consistency / execution-time anomalies: rollback failure, rollback-after-exception failure, preserved withdrawal exception / rollback transfer failure, virtual posting channel mismatch / taking over ledger holdings, deduction / reservation over limit, mirror converter missing / insufficient reservation, preserved-into-container failure on load, mirror physical conversion, mirror creation failure, projection deduction failure, loading save-load binding insufficient, mid-loading save-load preserved plan rebuild failure, destroy deduction / takeover reservation failure, destroy exception after posting, ownership re-attachment failure, caravan formation display thing identity not unique / preserved bucket not uniquely resolvable, etc. |
| `SDS_Diag.Warn` (Warning) | 32 | One-shot self-healing: inspection removes invalidated true bodies from the contents, deduction exceeds the ledger balance, reservation under-consumed, vault registration self-heal, old-save field backfill, load-time input buffer residue cleanup, preserved bridge return-to-store failure, load-time cleanup of invalidated preserved physical things (corpse without an inner pawn / minified thing without an inner thing; vanilla checks would null-ref), load-time cleanup of duplicated preserved copies (dedup by thingID), load-time discovery of a preserved true body still in wear / equipment slots (warning only), loading job-assignment snapshot missing (called at two sites: transport pod / map portal), physical thing already consumed at reclaim, mirror pickup point not within endpoint range (W54), mirror Job target invalidation consolidation (two sites: runtime removal / load-time key invalidation; end / dequeue first), ledger already empty / insufficient at posting rollback (external consumption got there first), runtime duplicate deposit / same-ID return conflict / load-time duplicate copies pending restore, load-time normalization of spoilage-progress orphan values (reset to -1), etc.; each reason is logged at most once for the whole run. 2026-11-16: the drop-point and return-to-store warnings of preserved / virtual supply offers were consolidated into the single point `SDS_OfferBridge.RollbackOrWarn`, call sites −2 |
| Routine work logs (Message) | **0** | Gate rejections / decision yields / no stock / unreachable / inventory full: **not output** |
| One-shot statistics output | 8 kinds | patch count, third-party hook points, availability, endpoint counts, preserved cleanup entries, Use command provider sources, drop-cell WorkGiver hook count, forced virtualization conversion count (see §3) |

> Counting scope (2026-10-28): `SuperDimensionalStorage/Source/**/*.cs`, excluding `obj` / `bin`, counting non-comment lines only; `SDS_Diag.*` counts call sites only, not definitions. Cross-checked against the counts reconciled by `CheckPatchInventory.ps1` §1.
>
> Difference from `SDS.Diag/Interfaces.en.md` §2: this table includes call sites in `SDS.Tests` / `SDS.Core.Tests` / `SDS.Rules.Tests`; the 92 / 34 / 139 in Interfaces §2 (Core + Vanilla's Fail / Warn / ErrorOnce) are production code, excluding tests and Diag itself. The two scopes differ and do not conflict.

### Why routine work logs were removed

`Log.Message` is not free: it still performs string formatting, log file writing, and log buffering; even with 600-tick throttling,
events like gate rejections keep producing logs and noise when the map has many candidates, drowning out real diagnostics.
Therefore, per the user's request: **normal fallbacks are not logged**.

### Performance design

- high-frequency paths (`CommonGates`, `TryDecideCore`, the `SplitOff` / `TryStartCarry` / `SpawnSetup` mirror branches)
  contain **no log calls at all**, not even the overhead of a boolean check;
- `Fail` is called only on real-failure branches: one dictionary lookup + one integer comparison (600-tick throttle, with one extra occurrence line at the 10th / 100th / 1000th time);
- `Warn` performs a single `HashSet.Add`, at most one entry for the whole run;
- the context string `SDS_Diag.Ctx(...)` is built only in branches where `Fail`/`Warn` actually output.

### Log level conventions

| Level | Purpose | Examples |
|---|---|---|
| `Log.Error` (Fail) | Data consistency / execution-time anomalies | `Take/rollback failed (single item, fallback placement attempted)`, `Take/rollback fallback placement failed`, `Mirror/drop point full`, `Take/executor exception` |
| `Log.Warning` (Warn) | One-shot self-healing / old-save upgrades / one-shot reclaim anomalies | `Vault/registration self-heal`, `Save/old-save fields backfilled`, `Physical thing already consumed at reclaim` |
| Not output | Normal operation and fallbacks | gate rejections, decision yields, no stock, unreachable, inventory full, maps without endpoints |

---

## 2. Module-by-Module Coverage

Legend: ✅ has diagnostics; ⏸ intentionally silent (normal operation).

### A. Startup / patch application

| Entry | Status | Notes |
|---|---|---|
| `SDS_Mod.ApplyHarmonyPatches` | ✅ | Failures raise an Error per class; one-shot output of the `applied/declared` counts; the full list is printed **only on failure** |
| `SDS_CellOnlyWorkGiverPatches.Apply` | ✅ | After Def loading completes, enumerates all WorkGivers at runtime; one-shot output of `JobOnThing N / HasJobOnThing M`; also hooks `Toils_Haul.PlaceHauledThingInCell` for placement-into-cell posting (W63); a single failed hook point → `ErrorOnce` + a Warning with the failure list; enumeration / application exceptions → `ErrorOnce`; silently waits for the next callback when Defs are not ready; the self-check outputs the `W63 rewrite / probe / yield Job / yield probe` counts |
| `SDS_ModCompat_PUAH.TryApply` | ✅ | Registers hook points one by one (`SDS_CompatPatchReport`), one-shot output of `A/B hook points in effect`; on failure → a Warning listing "hook point name (type not found / method not found / application exception)" + the list of hooks in effect; silent when not installed; installed but not detected → Warning. Application-phase exceptions are additionally logged as `Third-party compatibility hook point application failed (skipped): hook point name` (ErrorOnce; since 2026-10-27 the hook application logic shares `SDS_CompatTools.TryPatch` with the other adapters) |
| `SDS_ModCompat_CE.TryApply` | ✅ | Same as above (same `SDS_CompatPatchReport`): 15 hook points registered one by one as A/B; when `Resolve()` finds key types incomplete, outputs `0 hook points in effect` plus the presence/absence of the four types. Since 2026-10-27 the application logic shares `SDS_CompatTools.TryPatch` with the other adapters (priority uniformly `Low`), and the application-exception reason is unified as `Third-party compatibility hook point application failed (skipped): hook point name` |
| Manipulator Beam Emitter (`SDS_ModCompat_ManipulatorBeam.TryApply`) | ✅ | Same as above: 3 hook points registered one by one as A/B (counted as B when the `BeamTransfer` constructor is missing); the three runtime translations each carry an `ErrorOnce` (`Manipulator Beam Emitter compatibility (container type gate / container scan table / endpoint source selection) failed`); normal rejections (not this Mod's endpoint) are fully silent |
| CeleTech Shuttle Extension (`SDS_ModCompat_CeleTechShuttle.TryApply`) | ✅ | Same as above: 1 hook point registered as A/B; silent when not installed; missing type / method → Warning noting "loading manifest injection not in effect" (the shuttle list cannot see the Hyperdimensional stock); injection exception → `CeleTech Shuttle Extension compatibility (loading manifest injection) failed` (ErrorOnce); `TransportersInGroup` being null while the host has not started loading is a normal state (falls back to the host itself, no log); no endpoint / no stock / unresolved are normal fallbacks, silent |
| Milira Raising: The One Entwined With You (`SDS_ModCompat_MiliraGift.TryApply`) | ✅ | 1 hook point registered as A/B; silent when not installed; missing `JobDriver_IdealReceiveGift` type or `CompIdealGift` field → do not hook and give the reason; materialize / copy / drop-point exceptions are logged as `Milira Raising compatibility (gift endpoint materialization) failed (ignored, original behavior unaffected)` (ErrorOnce); `CompIdealGift` missing on the mirror or true body is logged as `Milira Raising compatibility (gift endpoint materialization): target comp missing (returned to the ledger, no takeover)` (ErrorOnce); normal non-mirror / no endpoint / no stock non-takeover cases are never logged |

### B. Visibility / mirrors

| Entry | Status | reason |
|---|---|---|
| `SyncAll` / `SyncIfDirty` | ✅ exceptions + real failures | `Mirror/vault drop point unavailable` (Error) |
| `SyncDirtyKeys` | ✅ | `Mirror/vault missing` (Error) |
| `SpawnMirror` | ✅ | `Mirror/drop point full`, `Mirror/category not rebuildable`, `Mirror/stack limit invalid` (Error) |
| `OnMirrorSpawned` (`Thing.SpawnSetup` patch) | ✅ guard wraps mirrors only | `Mirror/drop point initialization exception` |
| `EnsureOn` / `FindVaultCell` | ✅ | `Vault/Def missing`, `Vault/no free cell on the map`, `Vault/creation failed`, `Vault/placement failed` |
| `HandleNoAnchor` / `RemoveOn` (W37) | ✅ | `Vault/withdrawal failed` (ErrorOnce); normal retention during the grace period is not logged |
| `RepairEdificeRegistration` | ✅ one-shot | `Vault/registration self-heal`, `Vault/registration skipped` (Warn) |
| `TryRelocate` | ✅ | `Vault/relocation no free cell` (Warn), `Vault/relocation failed` (Error), `Vault/move back to the original cell failed` (ErrorOnce) |
| Structure despawn withdrawal (vault `DeSpawn` / `MapDeiniter`) | ✅ | `Mirror/structure withdrawal` (one-shot Warn; includes reason + count, logged only when `count > 0`), `Mirror/withdrawal failed` (Error), `Mirror/structure withdrawal exception` (ErrorOnce, an entry-point fallback that does not block despawning); zero writes to the ledger — nothing is lost |
| Projection owners (Take / TryDrop / TryTransfer) | ✅ | `Projection/take-out failed`, `Projection/drop-to-ground*`, `Projection/transfer*`, `Projection/target*` |
| No endpoint on this map / no drop point near the endpoint | ⏸ | Normal state, no log; W37: if a structural endpoint building exists, keep the vault; otherwise remove it after a 250-tick grace period |
| Room / observation / tantrum / arson filters | ✅ pre-existing | `ErrorOnce` (pure decision logic, no logging need) |
| Steal target / steal value filters (S15) | ⏸ | Normal filtering: mirrors / the vault read as unstealable (value 0) in `StealAIUtility`, no log |
| Mirror pathing / pick-up (W54) | ✅ one-shot | When a non-chip pawn is outside the access point's hit range, taking is refused: `Mirror pickup point not within endpoint range (take refused)` (Warn, each reason logged only once); pathing resolution failure ends the Job as target-unreachable; PUAH candidates / job assignment and the pawn owner's `ThingOwner` entries silently reject mirrors (normal filtering, no log) |

### C. Read-side criteria / counts / reachability

| Entry | Status | Notes |
|---|---|---|
| `ResourceCounter` / wealth / reachability patches | ✅ pre-existing exception logs | Explaining the numeric sources is an optional debug switch, not implemented |
| `ExposeData` old-save fallback | ✅ one-shot | `Save/old-save fields backfilled` (lists the backfilled fields, Warn) |
| `EnsureStorageSettings` | ✅ one-shot | `Save/vault filter backfilled` (Warn) |
| `MirrorExposureOnResourceCounter` returns 0 | ⏸ | Normal path with heavy traffic |

### D. Search / reachability

| Entry | Status | Notes |
|---|---|---|
| The four `GenClosest` adapters / `Reachability` | ✅ pre-existing exception logs | Per-candidate filtering reasons are not recorded |
| Zone BFS boundary | ⏸ | Documented; not added at runtime |
| `ListerHaulables.ShouldBeHaulable` (W52 / W55) | ⏸ | Mirror outflow and the admission / exclusion of top-priority candidates are normal filtering, no log |

### E. Take

| Entry | Status | reason |
|---|---|---|
| `SDS_WithdrawTransaction.Begin*` (SDS.Rules) | ✅ parameter/internal errors only | `Take/invalid argument`, `Take/invalid transaction argument`, `Take/invalid plan`, `Take/takeover*` (Error) |
| Insufficient stock / reservation races / insufficient preserved quantity | ⏸ | Normal fallbacks, **no log** |
| `ExecuteVirtual` / `ExecutePreserved` / `ExecutePreservedExact` / `ExecuteMirror` | ✅ internal channel errors only | `Take/channel mismatch` (Error) |
| `Deliver` / `Rollback` | ✅ | `Take/reservation release failed`, `Take/rollback reservation release failed` (**Error**) |
| `RollbackMaterialized` (W27, per-channel item-by-item rollback) | ✅ | A single-item failure logs `Take/rollback failed (single item, fallback placement attempted)` (Error, with `index` + `reservation`); if the fallback also fails, `Take/rollback fallback placement failed` is logged (Error); the remaining items continue to roll back |
| `SDS_ReturnUtility` (SDS.Rules: return / preserved rollback / virtual rollback) | ✅ internal errors only | A return failure dropping to the ground is a normal fallback and does not spam; direct ledger write failures are logged by the caller as `Caravan formation/virtual rollback failed` and `Caravan formation/preserved rollback failed` |
| `SDS_ReclaimRegistry` (reclaim / inspection / save-load audit) | ✅ | `Physical thing already consumed at reclaim (not added back to the ledger)` (Warn, one-shot) |
| Job cleanup / target return (`Patch_SDS_ReclaimJobTargets`) | ✅ exception fallback | `Job cleanup failed (ignored, vanilla behavior unaffected)`, `Job target return failed (ignored, physical thing left on the map)` (ErrorOnce); successful discard registrations and normal returns are not logged |
| `SDS_WithdrawExecutor.*` (SDS.Rules) | ✅ guards on all entries | `Take/executor exception` (Error, exceptions not leaked to vanilla) |
| `Thing.SplitOff` mirror branch | ✅ guard | `Take/SplitOff exception` (Error) |
| `TryStartCarry` mirror branch | ✅ guard | `Take/pick-up exception` (Error) |
| `Thing.TryAbsorbStack` mirror branch | ✅ guard | `Take/merge-withdrawal exception` (Error); a mirror absorbing others is a normal rejection, no log |
| `Thing.Destroy` mirror branch (W19) | ✅ fail-closed + one-shot | `External destruction of a mirror refused (W19 fail-closed)` (`Log.WarningOnce`, dedup by thingID); internal cleanup calls `Unmark` first, no log |
| `Toils_Haul.ErrorCheckForCarry` mirror branch (W58) | ✅ exception fallback | `Take-quantity intent recording failed (handed back to vanilla with a 1-item fallback)`, `Take-quantity intent backfill failed (kept vanilla's 1-item fallback)` (ErrorOnce); normal quantity top-up / keeping 1 when there is no reservation are not logged |
| Consumption-point materialization (W59: Wear / Equip / UseItem / Ingest) | ✅ exception fallback | `Mirror consumption-point materialization failed (wear / equip / use item / ingest; handed back to vanilla's failure condition)` (ErrorOnce); normal materialization and non-takeover due to no endpoint / no stock are not logged |
| Mirror reservation admission (W61) | ✅ exception fallback | `Mirror reservation admission (CanReserve / CanReserveStack) failed (handed back to vanilla's decision)` (ErrorOnce); rejection when G is off is a normal fallback, no log |
| Mirror Job target remapping and invalidation consolidation (W60) | ✅ silent recovery + one-shot consolidation | Normal remapping is silent; only slots that still point at the old mirror (already `Destroyed` after load-time cleanup) or that were invalidated to null are handled; when the Job has already advanced (the slot has been swapped to a live thing such as the true body), it is skipped, not overwritten, not ended; for same-key invalid mirror replacement, a new mirror is built first and retargeted precisely, silent on success; on new-mirror / drop-point failure or a replacement exception, falls back to closing the old target (the exception logs `Mirror Job target replacement failed (fell back to closing the old target)`, ErrorOnce); when the key is invalidated, the current Job is ended / queued jobs dequeued / queue slots removed first, then the target is dropped: at runtime logs `Mirror Job target invalidation consolidation (end/dequeue first, then remove the mirror)`, on load logs `Mirror Job target invalidation consolidation (load-time key no longer has a mirror; end/dequeue first)` (Warn, fixed-reason dedup, count / slot placed into context); under no circumstances is `LocalTargetInfo.Invalid` written; Jobs that no longer exist are skipped as normal completion |
| Load-time loading row rebuild (W48) | ✅ silent recovery | Normal rebuild by category key / empty-row reuse is not logged; when the container does not exist, the stock is empty, or 250 ticks are exceeded, the row is dropped without spamming errors |
| Take JobDriver (loading / manifest preservation) | ✅ | `Take/manifest*`, `Loading/take materialization failed`, `Loading/delivery target invalidated` |
| Supply failure reasons (A1) | ⏸ / ✅ | `UnresolvedDef` / `Forbidden` / `Insufficient` are normal fallbacks, no log; `RebuildFailed` falls under `Take/executor exception` (Error) |

### F. Store

| Entry | Status | reason |
|---|---|---|
| The three job-assignment exit patches | ✅ exceptions | Exceptions go through `ErrorOnce` |
| `CommonGates` (13 gates) | ⏸ | Normal fallbacks, **no log** (pure decision logic) |
| `TryDecideCore` / `TryParseVanillaDestination` | ⏸ | Normal yields, **no log** |
| `BuildJob` | ✅ internal errors only | `Store/missing JobDef` (Error); capacity / multi-item port limits are normal fallbacks, not logged |
| `JobDriver_SDS_DepositToGrid` | ✅ consistency risks only | `Store/failed to enter the inventory`, `Store/failed to leave the inventory`, `Store/ledger posting failed`, `Store/carried goods dropped to the ground` (Error/Warn); empty queue / invalid endpoint / unreachable / inventory full / endpoint rejection are normal fallbacks, not logged |
| Endpoint `InputBuffer` | ✅ pre-existing | `Endpoint input buffer digitization failed` |
| Endpoint `InputBuffer` vanilla pre-checks (`GetCountCanAccept` / `CanAcceptAnyOf`) | ⏸ | Normal rejections are not logged; shares the `Accepts` hard gate with `TryAdd` (2026-10-26) |
| Vanilla and third-party "cell-only drop target" empty-container dispatch (W63) | ⏸ normal folding / yielding; ✅ exceptions | Enumerates all WorkGiver implementations at runtime (`SDS_CellOnlyWorkGiverPatches`), folding drop points per W63 first; when the drop point cannot be resolved, yields per vanilla's "no empty storage space", which is a normal fallback and not logged; decision / probe / hook exceptions log `ErrorOnce` only (exceptions not leaked; a single failed hook does not affect the rest); the startup log outputs `JobOnThing N / HasJobOnThing M` and the failure list. The type-specialized hook points for vanilla `EmptyWasteContainer` / `EmptyEggBox` have been removed and are covered uniformly by the runtime enumeration; reliquaries are folded uniformly by `Pawn_JobTracker.TryTakeOrderedJob`. |
| Priority outflow (W52) | ⏸ normal decisions silent | Setting off / top priority enabled / no strictly higher-priority destination / destination does not accept / available quantity 0: not entered as a candidate or hauling skipped, no log; only exceptions go through `ErrorOnce` |

### G. Trading / caravan formation / loading

| Entry | Status | reason |
|---|---|---|
| Trade delivery | ✅ pre-existing | Available-quantity races already have `ErrorOnce`; UI display filtering is not logged |
| Caravan formation / transport pod / map portal injection | ✅ | `Caravan formation/take materialization failed`, `Caravan formation/preserved no available plan`, `Caravan formation/preserved materialization failed`, `Caravan formation/virtual rollback failed`, `Caravan formation/preserved rollback failed` |
| Loading JobDriver | ✅ | `Loading/take materialization failed`, `Loading/delivery target invalidated`, `Loading/pod-loading identity binding failed` (pod-loading normalization throws, `SDS_Diag.ErrorOnce`, handed back to vanilla) |
| Loading job-assignment row selection (W38, `TrySelectLoadRow`) | ⏸ intentionally silent | An unserviceable row (bucket already drained / entirely planned by other Jobs / available quantity 0 for that key / no pickup point) is a normal fallback: skip the row and continue scanning the next one, no log; it is forbidden to treat "unserviceable" as HasJob=true (otherwise vanilla reports `yielded no actual job`) |
| Loading job-assignment snapshot (W42) | ✅ one-shot | JobOn without a same-tick snapshot → `Loading/job assignment snapshot missing (no reassignment this tick)` (`SDS_Diag.Warn`, once for the whole run), vanilla's result kept as is; the next HasJob re-registers; normal hits are not logged |
| Loading plan validity (W43) | ⏸ intentionally silent | Recycling stale plans during row selection is normal cleanup; queued Jobs (`jobQueue`) still count as valid occupancy and are likewise not logged; plan release on `TryMakePreToilReservations` failure is not logged (already covered by `Take/pickup point not reservable`); plans are not registered while the drop-cell guard probe is running, and not logged either |
| Dialog refresh scope (W44) | ⏸ intentionally silent | Only clears display thing identity registrations, never touches job-assignment state; containers do not affect each other, which is normal behavior |
| Preserved bucket identity (W45) | ⏸ intentionally silent | Same-bucket reuse / new instances across containers are both normal paths; the decision only looks at true-body overlap, no log |
| Loading available quantity / carry capacity (W46) | ⏸ intentionally silent | Available quantity 0 after reservation, or carry capacity ≤ 0 (no job assignment this trip) → the row is naturally skipped, a normal fallback |
| Mid-loading save-load identity rebuild (W47) | ✅ one-shot | Reservation key available → a successful plan rebuild is the normal path, no log; no plan can be rebuilt → handed back to the existing `Loading/take materialization failed` (`SDS_Diag.Fail`), no separate reason |
| Loading row save-load recovery (W48) | ✅ one-shot | Rebuilding a map portal pure-Hyperdimensional row by shortfall after vanilla dropped it is normal recovery, no log; insufficient binding still goes through `Loading/load-time binding insufficient` (`SDS_Diag.Fail`, 600-tick throttle) |
| In-transit materialization marker (W49) | ⏸ intentionally silent | Revoking the marker on return / ground placement is normal finalization; after revocation the physical thing returns to the scope of auto-deposit / auto-withdrawal, no log. Ground placement after a vanilla hauling Job fails on its own is covered by the marker's lifetime (known boundary) |
| Loading row save-load rebuild (`transportLoadRequests`) | ✅ one-shot | Container not found / row already cleared → skipped (the vanilla manifest no longer exists, normal). **A preserved row cannot bind to any bucket** → `Loading/load-time binding insufficient` (`SDS_Diag.Fail`, 600-tick throttle; W39) |
| Display thing identity resolution (`KeyOfDisplay`) | ⏸ intentionally silent | Bucket present → the bucket key; no bucket → the key sealed by the factory; neither → falls back to the virtual projection `FromThing`. The fallback only appears when display things are crafted bypassing the factory, and does not spam logs (avoiding per-frame noise); if it ever appears, the factory should be fixed rather than adding logs |
| ProjectE instant transmutation (`SDS_ModCompat_EquivalentExchange`) | ✅ guard + A/B report | Swapping display things for true bodies / removing preserved rows is normal translation, no log; `ProjectE compatibility (instant transmutation pre-check) failed`, `(instant transmutation return) failed`, `(zone transmutation filter) failed` (ErrorOnce); missing hook points go through the unified Warning of `SDS_CompatPatchReport`; hook application uniformly goes through `SDS_CompatTools.TryPatch` (the `TryAccept` prefix explicitly `Priority.High`, the rest `Low`), with the application-exception reason `Third-party compatibility hook point application failed (skipped): hook point name` |
| UF Light Industries Furniture fully automatic printer (`SDS_ModCompat_UFRecipes`) | ✅ guard + A/B report | Only supplements the **preserved channel** (corpses / minified things / genes) into the printer container; virtual categories are left to its own vanilla ingredient pulling. Failure to draw is a normal fallback, no log; exceptions log `UF Light Industries Furniture compatibility (fully automatic preserved supply) failed` (ErrorOnce); missing hook points go through the unified Warning |

### H. Lifecycle / saves / settings

| Entry | Status | Notes |
|---|---|---|
| `ExposeData` old-save field backfill | ✅ one-shot | `Save/old-save fields backfilled` |
| Load-time endpoint buffer residue cleanup | ✅ one-shot | `Save/input buffer residue cleaned` (Warn, with counts; only cleans duplicate copies / destroyed / zero stacks, never touches real stock) |
| Load-time preserved duplicate copy cleanup (Core) | ✅ one-shot | `Load cleaned duplicated preserved copies` (Warn, with counts; the ledger dedups by thingID, dropping duplicate references only — no Split / no Destroy / no count change) |
| Load-time preserved true body still in wear / equipment slots | ✅ one-shot | `Load found a preserved true body still in wear/equipment slots (not auto-removed)` (Warn; not auto-removed, to avoid changing vanilla's "being worn" presentation; requires manual handling) |
| Load-time unresolved entries (A1) | ✅ one-shot summary | `Save/unresolved defs (accounts retained; auto-recovers once the def is fixed)` (Warn; entry count + item count + details, no per-entry spam) |
| Unresolved retry | ⏸ | Normal flow (the player clicks "retry resolution"); the result surfaces only through UI prompts |
| `SDS_StaticState.ResetAll` (mirrors / projections / endpoints / reclaim / offer / trade / transport / caravan formation / Combat Extended and Milira Expanded - Future Technology short-term caches / physical thing markers / store job assignment / per-map storage proxy / resource bar cache / manifest count cache / chip cache) | ⏸ | Normal flow; the **sole** cleanup entry for new saves / loading, see §2·M |
| Settings clamp | ⏸ | Normal clamping |
| Weapon/apparel forced virtualization (W33/W34) | ✅ one-shot | On success outputs `[SuperDimensional Storage][Forced virtualization] Conversion complete: N items` (a player-initiated action, one line only); on exception `Forced virtualization/conversion failed` (`SDS_Diag.Fail`, throttled); refused while trade / caravan formation / loading windows are not closed (normal fallback, no log); skipped reservations / protected items are normal fallbacks, not listed item by item |

### I. Use command

| Entry | Status | Notes |
|---|---|---|
| Provider enumeration (inclusion / exclusion) | ✅ one-shot | `[Use command] Right-click menu item sources: N providers included / M classes in the exclusion table` |
| Gate 0 (clicking a building / ground / item) | ✅ one-shot | `Use.NotPawn` (no withdrawal, no deduction) |
| Gate 0 (mechanoids / animals / non-player factions) | ✅ one-shot | `Use.NotEligible` |
| Gate 0 (no chip / chip unavailable) | ✅ one-shot | `Use.NoPort` |
| Gate 1 (category not applicable) | ✅ one-shot | `Use.NotUsable` |
| Gate 2 (vanilla grayed-out reason) | ✅ one-shot | `Use.Blocked`, with the vanilla grayed-out text attached |
| Non-player withdrawal gate (S18, setting enabled) | ⏸ normal rejections silent | Known non-player Pawns are skipped directly at the search candidate supplement / reachability / Job materialization / carrying / apparel-equip inventory receiving / endpoint withdrawal entries; unknown actors keep vanilla behavior |
| ④ Task-type JobDef | ✅ one-shot | `Use.TaskJob`, with the JobDef name attached; cancelled + returned |
| ④ Unknown JobDef | ✅ one-shot (`Log.WarningOnce`) | `Use.UnknownJob`, handled as a Use |
| ④ Tasks without a JobDef | ✅ one-shot (`Log.WarningOnce`) | `Created a task without a JobDef; handled as an unknown Use`, keeping the ground placement and revoking the in-transit marker (W49) |
| Return failure | ✅ one-shot | `Use.RefundFailed`, the item stays on the ground |
| Placement failure | ✅ one-shot | `Use.NoPlace` (already returned) |

### J. Preserved consumption bridge (Plan A)

| Entry | Status | reason |
|---|---|---|
| `HealthAIUtility.FindBestMedicine` / `FoodUtility.TryFindBestFoodSourceFor` / `JobGiver_PickUpOpportunisticWeapon` / `JobGiver_OptimizeApparel` | ✅ exception fallback | `Preserved bridge/medicine supply failed`, `Preserved bridge/food supply failed`, `Preserved bridge/weapon supply failed`, `Preserved bridge/apparel supply failed` (ErrorOnce, exceptions not leaked) |
| Drop point / return to store | ✅ self-healing warnings | `Preserved bridge/drop point failed (physical thing on the ground, not returned to store)`, `Preserved bridge/virtual drop point failed (physical thing on the ground, not returned to store)`, `Preserved bridge/timed-out return to store failed (physical thing on the ground)`, `Preserved bridge/return to store failed after losing its map (physical thing on the ground)` (Warn, one-shot) |
| No candidates / no endpoint / cooling down / category has reservations | ⏸ | Normal fallbacks, **no log** |
| Multi-slot bill shortfall materialization (W51, `Patch_SDS_PreservedBillIngredients`) | ⏸ normal fallbacks silent; ✅ ErrorOnce | When slot-by-slot materialization into the workbench interaction cell fails, the whole batch is returned to the ledger and handed back to vanilla, with no extra logging; a re-run of the vanilla search that still fails is likewise a normal fallback; exceptions uniformly go through the existing `Preserved ingredient shortfall fill failed (ignored, vanilla behavior unaffected)` |

### K. Output install (minified things / install blueprints)

| Entry | Status | reason |
|---|---|---|
| Existing install blueprint → cancel the old blueprint and rebuild | ⏸ intentionally silent | Same semantics as vanilla `Designator_Install`, not an anomaly; `questTags` get no special-casing |
| Install placer cancel (right-click / ESC) | ⏸ intentionally silent | No withdrawal, no deduction (W31), a normal cancel |
| Install drop point invalid / stock already withdrawn | ⏸ | The ghost is marked red and vanilla explains the reason (`CanPlaceInstallAt`); no log |
| Blueprint creation throws | ✅ ErrorOnce | `Output/install failed (materials rolled back)` (includes half-built blueprint cleanup + the true body returned to the ledger) |
| Half-built blueprint cleanup failure | ✅ ErrorOnce | `Output/install half-built blueprint cleanup failed (skipped)` |

### L. Public API facade (phases 1–5 implemented)

| Entry | Status | Notes |
|---|---|---|
| `SDS_API` See / Read | ⏸ normal rejections silent; ✅ ErrorOnce | Read-only, no faction/map gates; exceptions log `PublicAPI/see stock exception`, `PublicAPI/read* exception`, `PublicAPI/availability* exception` |
| `SDS_API` Find | ⏸ normal rejections silent; ✅ ErrorOnce | Non-player / no endpoint only return `SDS_ApiResultCode`; exceptions log `PublicAPI/store/find endpoint exception`, `PublicAPI/take/find pickup point exception` |
| `SDS_API` Store | ⏸ normal rejections silent; ✅ ErrorOnce | Ownership / faction / forbidden / filter / capacity / cross-map failures only return `SDS_ApiResultCode`; exceptions log `PublicAPI/store/* exception` and return `InternalError` |
| `SDS_API` Take | ⏸ normal rejections silent; ✅ ErrorOnce | Supply failures go through the `SDS_SupplyFailure` mapping; `VirtualHandoff` / non-player / cross-map mirror return `InvalidArgument`; exceptions log `PublicAPI/take/* exception` and return `InternalError` |
| `SDS_API` ingredient restock planning (S16) | ⏸ normal rejections silent; ✅ ErrorOnce | Slot not accepting / no available def only returns `false`; exceptions log `PublicAPI/restock slot decision exception`, `PublicAPI/restock planning exception`, `PublicAPI/def-list restock planning exception`, `PublicAPI/restock store feedback exception` |
| Adapter translation layer | ✅ phase 4 migrated, phase 5 Manipulator Beam Emitter / CeleTech Shuttle Extension integrated; ⏸ normal translation silent | Pick Up And Haul / Combat Extended / ProjectE / UF Light Industries Furniture / Milira Expanded - Future Technology only call the `SDS_API` public five axes / public helpers / `internal` adapter bridge, and no longer call internal services such as `SDS_DepositService` / `SDS_WithdrawExecutor` / `SDS_GameComponent_Grid` directly; Manipulator Beam Emitter only checks endpoint identity (`IsDepositEndpoint`) and endpoint acceptance (`IHaulDestination.Accepts`), with delivery going through the vanilla container contract (the endpoint `InputBuffer`); CeleTech Shuttle Extension only calls the internal manifest injection bridge (`InjectTransportLoadCandidates`). API internal exceptions go through `PublicAPI/* exception`, while the adapters' own hook-point exceptions still go through the original compatibility logs |

> Read-only / rollback exceptions of the adapter facade (PublicAPI.en.md §2·6) also uniformly go through `PublicAPI/* exception`; normal rejections are silent.

> Phases 4 and 5 (Manipulator Beam Emitter / CeleTech Shuttle Extension) are complete; after phase 6 passes live acceptance, merge the corresponding items of this section into §2 F / G, and update §1 according to actual source statistics.

### M. Runtime static state / invariant self-check (2026-10-27)

| Entry | Status | Notes |
|---|---|---|
| `SDS_StaticState.ResetAll` | ⏸ normal flow | **E1**: the **sole** cleanup entry for new saves / loading (called from the `SDS_GameComponent_Grid` constructor). **Rule**: any static cache that "must not survive across saves" must be registered here; scattered cleanup at individual entries is forbidden; process-level constants (reflection `FieldInfo`, Provider instances) must not be placed here. Outputs one line with the cleanup item count when Developer mode starts (currently 17 static items = Core 0 + Rules 4 + Vanilla 13; enabled adapters add 2 dynamic items: CE / Milira). |
| Registered list (completed 2026-10-27) | ⏸ | **Short-term caches keyed by ID / absolute tick** (when switching saves within the same process, IDs overlap + `TicksGame` can regress → a stale `until` may far exceed the new save's current tick, causing long-lived misjudgments): `SDS_ModCompat_CE` (reload cooldown / priority), `SDS_ModCompat_MiliraExpanded` (equipment restock throttling), `SDS_ProducedThingMarker` (source-exclusion marker for freshly materialized physical things), `SDS_DepositDispatchLedger` (in-transit + failure backoff, keyed by `pawnID<<32|thingID`). **Caches keyed by Map / physical thing references** (only references are dropped; physical things are never destroyed): `SDS_MirrorHaulDestination` (per-map storage proxy), `SDS_Exposure` (resource bar refresh cache + reused buffers), `SDS_ReadOnlyUI` (manifest count cache, including the old Grid and `List<Thing>`), `SDS_ImplantUtility.ResetChipCache` (chip level cache, sharing the implementation with `NotifyChipStateChanged`). `Building_SDS_Vault`'s `vaults` and W37's `noAnchorSince` are cleaned via `SDS_MirrorService.Reset` → `ResetStatics`, also belonging to item 1. |
| Intentionally not registered | ⏸ | `SDS_ModCompat_MiliraExpanded.canStoreCache`: its key is a `ThingDef`, stable across saves, so it does not belong to "must not survive" and is not placed in `ResetAll`; but since W33 its values depend on the "weapon/apparel forced virtualization" switch, and when the switch changes `SDS_ModCompat_MiliraExpanded.ClearStoreCache()` must be called (`SDS_Mod.ApplySettings` and the confirm callback are wired in). W51's `Patch_SDS_PreservedBillIngredients.rerunning` (a re-entry guard, always reset by the in-call `try/finally`) and `findBillIngredients` (a process-level reflection `MethodInfo`) likewise belong to the process-level constants / transient values that stay out of `ResetAll`. |
| Display thing caches (`displayCache` / `displayKeys` / `bucketDisplayCache`) | ⏸ | `SDS_OutputUtility.ResetDisplayCaches` only clears references and **never destroys** display things (consistent with the existing discipline). |
| `SDS_Invariants` (development-time assertions) | ✅ DEBUG builds + Developer mode only | **E2**: `Available ∈ [0, Total]` — does **not** assert `Available == Total − Reserved`: after external destruction `Reserved > Total` is legal, with the available quantity clamped to 0; a transaction must be in `Reserved` to Execute; after `Delivered / RolledBack` it must not advance again. Only logs `Error`, never throws. |
| `SDS_SelfCheck.RunLedgerChecks` (Developer menu "SuperDimensional Storage → Self-check: ledger / reservations / mirrors"; auto-runs once on the first tick after loading a save in Developer mode) | ✅ manual / automatic (DevMode) | Read-only checks: virtual / preserved available quantity = `max(0, total − reserved)`; registered mirrors must still be live physical things on this map (no count check — mirror syncing has a throttle window); W35 per-item tracked types must be judged preserved; the W37 vault must be alive and belong to this map, vault cells must not belong to a Zone / foreign SlotGroup, and anchors must not reference destroyed / other-map objects. Outputs `Self-check complete: N items checked, M failed`. |
| `CheckPatchInventory.ps1` (build time, not runtime) | See ModuleRules.en.md §10 | Two-way reconciliation between the patch inventory and the source: feature classes 112 + registry 29 = patch points 141, dynamic rows (including the VC Vehicle Framework vehicle cargo bay loading / projection / gatekeeping 3 hook points), baseline 29 rows (19+10, 28 distinct TargetKeys), special cases 3; TargetMethod text-key dedup 138. The script's own registry is not repeated here. |

---

## 3. One-Shot Output List (kept)

1. `Harmony patch classes application complete: A/B = applied/declared count` (on failure, the failure list and the list of hooks in effect are appended);
2. Third-party hook points: Pick Up And Haul / Combat Extended / UF Light Industries Furniture / Glitterworld Destroyer 5 - Mechanoid Addon / Manipulator Beam Emitter / Milira Expanded - Future Technology / CeleTech Shuttle Extension / ProjectE — one **A/B** line per **detected** adapter (`hook points in effect: applied/expected`); on failure this becomes a Warning listing "hook point name (reason)" and the list of hooks in effect, item by item; silent when not installed (installed but the type/method cannot be detected must Warn);
3. `Hyperdimensional Space availability: available/unavailable`;
4. `Total endpoints / building endpoints / colonist endpoints / mechanoid endpoints` (total and available counts);
5. Preserved channel cleanup entry count (output only when something was cleaned);
6. "Use command" right-click menu item sources: inclusion / exclusion counts + the list of classes not matched by the exclusion table (once at startup);
7. Drop-cell WorkGiver guards: `JobOnThing N / HasJobOnThing M (enumerated X WorkGiverDefs)` + `W63 rewrite / probe / yield Job / yield probe` counts; the failure list is appended when hook points fail (W63 unified version, once at startup).
8. Weapon/apparel forced virtualization: `[Forced virtualization] Conversion complete: N items` (once after the player confirms the switch, one line only).

---

## 4. Log Conventions (actual format)

```
[SuperDimensional Storage][Mirror/drop point full] map=0 cell=(120, 0, 171) max=1 items=37 def=GravlitePanel (call site: SuperDimensionalStorage.SDS_MirrorService.SpawnMirror)
```

- **Error**: data consistency / execution anomalies; one entry per reason per 600 ticks, with the occurrence count appended at the 10th / 100th / 1000th time;
- **Warning**: one-shot self-healing (one entry per reason for the whole run);
- **Context**: `map=<uniqueID> pawn=<LabelShort>#<id> thing=<defName>#<id>[x count] job=<defName>#<loadID>`;
- **Call site**: full class name + method name (stack captured only when outputting);
- Exceptions: `ErrorOnce` keeps "message + call site + exception type" for one-shot dedup.

---

## 5. Intentionally Silent

- All gate rejections and decision yields (normal operation);
- No stock / reservation races / unreachable / inventory full / endpoint rejection (normal fallbacks);
- Maps without endpoints (normal state);
- Medical bill ingredient restock rejected by vanilla's medicine tier (S17 normal fallback: `IsUsableIngredient` rejects directly + the fallback does not restock, with vanilla reporting `NoMedicineMatchingCategory`);
- Cancelling the old blueprint during output install (same semantics as vanilla `Designator_Install`, a normal replacement);
- Install placer right-click / ESC cancel (no withdrawal, no deduction); an invalid drop point is only a ghost-preview rejection (W31);
- Display-layer-only catches, CE reflection-detection optional paths, read-only short paths in resource statistics, load-time cleanup of old mirrors, settings clamping.

---

## 6. Acceptance Scenarios

| # | Scenario | Expectation |
|---|---|---|
| 1 | Normal operation (hauling, storing, successful takes) | **No logs at all** |
| 2 | Workbench products go straight into Hyperdimensional Space | Success, no logs |
| 3 | Vault cell occupied by a building / edifice slot cleared | `Mirror/drop point full` (Error) + `Vault/registration self-heal` (one-shot) |
| 4 | Exception during the rollback phase | `Take/rollback failed (single item, fallback placement attempted)` (Error, with index and reservation); if the fallback also fails, `Take/rollback fallback placement failed` is logged; the remaining items still roll back per channel |
| 5 | The take executor throws | `Take/executor exception` (Error, exceptions not leaked) |
| 6 | Old save missing fields | `Save/old-save fields backfilled fields=…` (one-shot Warning) |
| 7 | Startup | `Harmony patch classes application complete: A/B = 112/112.` + `Mirror guard registry: A/B = 19/19` + `Projection owner bridge: A/B = 10/10` (all successful) + availability + endpoint statistics |
| 8 | Output install with an existing registered blueprint (including quest-bound things) | The old blueprint is cancelled and only the new one remains; no logs |
| 9 | Blueprint creation throws during output install | `Output/install failed (materials rolled back)` (ErrorOnce) + items returned to store + no half-built blueprint |
| 10 | Gravship map switch / whole-map unload | `Mirror/structure withdrawal` (first entry, reason + count); ledger counts unchanged; the new map's vault is spawned and mirrors rebuilt from the ledger; **no** `already spawned` red text |
| 11 | Selecting an object with "cancel growing / cancel construction / clear priority work" gizmos and pressing C | Only the vanilla command executes; the Hyperdimensional side panel does not pop up (same-key gizmos take precedence) |
| 12 | Build mode (designator selected / Architect tab open), pressing C / Q / E | Vanilla cancel-designator / rotate-left / rotate-right work as usual; side panel, Output, and Use do not trigger |
| 13 | Pressing C while `Targeter` is selecting a target or a text field has focus | The side panel does not open; the key is handed to vanilla |
| 14 | No vanilla occupancy at all, pressing C / Q / E | Side panel toggle / Output / Use execute per the original logic |
| 15 | Developer mode startup / new save / loading | `[SuperDimensional Storage][State] Runtime static state cleaned: E1 registered 18 items.` (18 static; adapter dynamic items counted separately) |
| 16 | Switching saves within the same process (save A → the older save B), a pawn with the same `thingIDNumber` in B | Does not inherit A's reload cooldown / reload priority / restock throttling / job-assignment backoff; re-judged from B's ledger on the first tick |
| 17 | "Output" of an installable minified item (same quality/material within the row) | No quantity dialog; goes straight into the vanilla install placer (ghost preview, Q / E rotation); after right-click or ESC cancel, stock counts are unchanged and there are no logs |
| 18 | Core miniaturized / moved to a new position / deconstructed (W37) | The old cell is freed for building; the vault is near the new anchor; when there is no structural endpoint and no pawn with an available chip for more than 250 ticks, the vault is removed; ledger / wealth unchanged; rotation and pawn walking do not relocate the vault |

---

## 7. Optional Debug Capabilities (not implemented)

If per-entry reasons for "why it did not take over" are ever needed, they should be built as a **default-off** debug switch (not always-on logs):
when enabled, output per reason `Store/gate:*`, `Store/priority yield`, `Endpoint/own port offline`, etc.,
with zero overhead when disabled. Not provided in the current version.

---

## 8. Manual Acceptance Matrix (Take / Delivery)

> Matrix axes: See / Read / Find / Take / Store × virtual / preserved × Pawn / non-Pawn × save-load × multi-map × concurrency. The following expands the Take / Delivery dimension.

| Dimension | Checkpoint | Expected result |
|---|---|---|
| Hand | Hauling, bill ingredients, etc. taken into the hand | The true body enters the hand; the mirror remains the original Job target; no early ground placement, no extra source markers |
| Inventory | Store-type Jobs carried via the inventory | Items stay in the inventory; when the Job suspends or ends, items are not spat onto the ground |
| Inventory | A materialized physical thing carried into a third party's inventory | Treated as delivered / abandoned: no rollback, no adding back to the ledger by count, no retrieval; the Job fails and diagnostics are logged |
| Container | Vanilla container delivery (check acceptance first, then swap in the true body) | On container rejection: rollback without deduction; on success the true body == the exact materialized one, no substitution by category |
| Container | Endpoint used as a container (write-through) | The delivery return value is backfilled with the actually accepted quantity; subsequent release and callbacks proceed normally |
| Equipment | Equipping one item | A single-item owner takes only 1 true body; the whole stack must not be equipped at once; goes through the container contract, no default ground placement |
| Apparel | Wearing | The final gate refuses mirrors into the worn list; only 1 true body is taken; body part / bioencoded / wearability checks preserved |
| Casket | Corpses / preserved items into caskets | The true body is delivered as is; casket rejection goes through preserved rollback (unaffected by filters), never dropping the true body on the ground |
| Transport pod | Pod loading | The true body first enters the hand, then is handed to the vanilla container; loading rows are located by true-body identity, not a same-category fallback |
| Transport pod | Loading row save-load recovery | Display thing references do not survive across saves; rows are rebuilt by identity + shortfall; insufficient binding logs diagnostics only once |
| Trading | Deal settlement | Re-checked against the available quantity at the moment of the deal, then settled instantly; multiple rows on the same key share one budget, decremented row by row; insufficiency fails the whole deal — never a partial delivery |
| Caravan formation | Departure settlement | Virtual bucket members that cannot be taken in and preserved items falling back each go through the corresponding rollback channel; the difference stays in the ledger |
| Preserved | Preserved withdrawal / delivery | Reserved by true-body reference, delivered as is, not rebuilt, not mirrored; read-only availability goes through the unified query |
| CE | Ammo withdrawal / reloading | Requests are first clamped to the available quantity, then made at full amount; if stock changes during execution, the whole request fails and is handed back to CE's native branch |
| Interruption | Job suspension / termination | Suspension keeps the reservation; release happens only on real completion; undelivered portions roll back precisely — no duplication, no loss |
| Exception | Executor materialization throws | After a successful `Begin`, the executor must `Rollback`; already materialized physical things return per channel; reservations must not survive to the next save-load |
| Interruption | Interrupted while carrying | Items in the carry slot drop per vanilla: no posting, no item loss; inventory items still follow the suspension rules |
| Save-load | Load recovery | Reservation not yet materialized → release the reservation; materialized and the physical thing still exists → return first, drop on failure; already destroyed → not added back to the ledger |
| Save-load | Reservation audit | `id ≤ 0` (negative = Instant residue, 0 = illegal attribution) is always cleaned; positive ids are re-checked with the liveness delegate passed in by Vanilla (combining `ISDS_ReclaimWorld.JobExists` and `Pawn.Destroyed`), released on false; Vanilla triggers this at `FinalizeInit` and on a 250-tick cycle, leaving no permanently held quantity |
| Save-load | Invalid references | Entries with only a key and a count but no actual thing are never conjured back into the ledger |
| Insufficient capacity | Judged before transaction creation | If burden / CE capacity cannot fit it, refuse creation or let the player split the order — never force it into the inventory |
| Multi-key partial shortfall | Reservation phase | Writes happen uniformly only after all keys validate; any shortfall releases all reservations made — no partial holdings |
| Multi-key partial shortfall | Deduction phase | If a key fails to deduct, the already-deducted portion is returned and the whole request fails — no "steel taken, components not obtained, yet continuing" |
| Multi-trip | One Job with multiple take trips | Multiple trips = multiple transactions: each trip reserves / deducts / rolls back independently; the same quantity is deducted from the ledger only once |
