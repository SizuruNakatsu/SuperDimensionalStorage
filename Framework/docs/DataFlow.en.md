# Data Flow (Main Flow)

> Positioning: this document only answers "where data comes from, who it passes through, where it lands, and who consumes it", for quickly untangling the data shapes and flows between modules.
> **Defines no rules**: criteria are in RuleIndex.en.md and each module's `Rules.en.md`; fields / signatures are in each module's `*_API.cs`, `Interfaces.en.md`, and ModuleInterfaces.en.md; startup order is in ModuleRules.en.md §9; patch hook points are in PatchInventory.en.md. On conflict, those documents win.

## 1. The Overall Chain

```text
                            writes
on-map physical things ─────deposit entry─────▶ SDS_Ledger (single source of truth)
                                    │ virtual channel: SDS_StockEntry (key / count / stack-average state)
                                    │ preserved channel: true-body Things (held by the Core-private owner)
                                    │
              ┌─────────────────────┼──────────────────────┐
              │ read-only accounting│ derived visibility   │ transactions (the only ledger writers)
              ▼                     ▼                      ▼
      SDS_Rules / SDS_Availability      SDS_MirrorService      SDS_Rules (flow layer)
      (available / tradable counts)     (per-map mirrors / display things)   (reservation → materialization → delivery)
              │                     │                      │
              ▼                     ▼                      ▼
       UI / trading / donations /   vanilla enumeration / jobs / loading   physical things → hands / containers / ground
       bills / resource summaries
```

Four main chains: **① physical things → ledger, ② ledger → read-only derivatives, ③ ledger → physical things, ④ saves ↔ runtime**.
The ledger is modified only by "deposits" and "withdraw transactions"; read-only accounting and derived visibility flow out one-way.

## 2. Deposits: On-Map Physical Things → Ledger

1. Entry points: the hauling-digitization patch, right-click / gizmo, third-party deposit jobs, and the trading-purchase funnel all converge on `SDS_Rules.TryDeposit` (third parties go through the `SDS_API` bridge).
2. Endpoints: regular entries first go through `SDS_Rules.SelectEndpoint(SDS_EndpointQuery)` (candidates and availability come from `SDS_Host.Ports`, implemented by Vanilla; moved out of Core in P2); entries like trading purchases where the goods are already inside the system use direct ports and skip endpoint selection.
3. Channel split (criteria in `SDS.Rules/Rules.en.md` §1 and `SDS.Core/Rules.en.md` §5):
   - **Preserved**: the true body enters the Core-private owner as-is and no longer exists physically on the map;
   - **Virtual**: the physical thing is destroyed and booked as `SDS_StockKey` + unit count + stack-average state (HP / rot).
4. Result: ledger counts and versions change → `StockVersion` advances → mirrors, display things, and exposure rebuild on the next pass.

```text
Thing ──(key, units, vitals)──▶ SDS_StockEntry ──▶ SDS_Ledger
Thing ──(preserved)───────────▶ Core-private owner (true body)
```

## 3. Visibility: Ledger → Read-Only Derivatives

- **Availability accounting**: `SDS_Availability` is the single source of available counts / tradable counts / failure classifications; Api, UI, trading, donations, and bill supply all read it and nothing else.
- **Physical mirrors**: `SDS_MirrorService.SyncIfDirty` is driven periodically by `SDS_VanillaState.Tick` (period and invalidation criteria in `SDS.Vanilla/Rules.en.md`); a version change triggers a rebuild on the next sync pass, and with no change it returns in O(1). Rebuilding only does "ledger snapshot → per-map mirror `Thing`s (add / remove / adjust counts)". The mirror table is a **derived cache**; no flow may ever read it back as a ledger.
- **Display things**: trading, loading, and bill lists use display things built by `SDS_OutputUtility` from a key; their identity is the `SDS_StockKey` sealed at creation, not the physical thing itself.
- **Exposure**: `SDS_Exposure` is the single producer of visibility increments (that implementation is the only source of the accounting); the resource-readout / wealth / reachability patches only consume these increments and never derive their own.

```text
SDS_StockEntry ──▶ mirror Things (one per map) ──▶ Listers / enumeration / job targets
              ──▶ display Things ────────────────▶ trading / loading / bill candidates
              ──▶ numeric increments ────────────▶ ResourceCounter / WealthWatcher / reachability
```

## 4. Withdrawals: Ledger → Physical Things

1. The caller supplies a `SDS_WithdrawPlan` (key, count, mode, endpoint); on the job path, `SDS_Rules.TryReserveForJob` runs first, then `BeginJobAdopt`.
2. `SDS_WithdrawTransaction.Begin` reserves (the virtual and preserved channels each keep their own reservation); state becomes `Reserved`.
3. Materialize (all three exits share the same transaction):
   - virtual: `TryWithdrawVirtual` debits + `SDS_StockRules.MakeThing` builds the physical thing from the key and stack-average state;
   - preserved: `ExecutePreservedExact` withdraws according to the exact true-body plan;
   - mirror: debit first, then the caller-injected `ISDS_MirrorPhysical` (`SDS_WithdrawPlan.physical`) converts the mirror into a true body (on failure it is credited back); Rules does not read `SDS_Host.MirrorPhysical`.
4. State becomes `Materialized`; completed delivery goes through `Deliver()` (which only releases the reservation), while hauling / delivery is done by the caller via `SDS_Host.Delivery` (hands / containers / ground); ending without delivery calls `Rollback()` to return per channel (the virtual / preserved fallbacks).

```text
SDS_StockKey + count ──▶ SDS_WithdrawPlan ──▶ transaction (reserved) ──▶ List<Thing> ──▶ Delivery
```

The authority on the state machine and failure semantics: `SDS.Rules/Interfaces.en.md` and `SDS.Rules/Rules.en.md` (W21 / W27).

## 5. Vanilla Entry-Point Wiring

| Scenario | Read side (ledger → vanilla) | Write side (vanilla → ledger) |
|---|---|---|
| Local / remote trading, gifting | inject display things into the `Tradeable` list (`SDS_TradeVirtual.Inject`) | the sell side funnels through `SwapDisplaysForReal`, swapping display things for equal quantities of true bodies; purchases are booked directly via `TryTakeBoughtIntoGrid` |
| Trade beacons / donations / launches | `TradeUtility.AllLaunchableThingsForTrade` appends display things | `TradeUtility.LaunchThingsOfType` fills shortfalls from the ledger, then hands back to vanilla |
| Transport pod / map portal loading | inject display things and shortfall rows into the `TransferableOneWay` list | shortfalls land in `SDS_TransportLoadSave`; once the withdrawal job materializes, hand off to vanilla loading |
| Bills / work assignment | `SDS_BillIngredientQuery` reads availability to fill missing ingredients; def choice / shortfall sizing is decided by the `SDS_Rules` supply planner | the supply plan is handed to a job to fetch the ingredients; the deposit job then books them |
| Hauling / manual deposits | — | digitized at the moment of delivery; explicit deposits via right-click / gizmo |
| Resource summaries / wealth / UI | read-only exposure and availability | — |

The full list of entry points and hook points is in PublicAPI.en.md and PatchInventory.en.md; this document does not repeat it.

## 6. Third-Party Adapters (One-Way)

```text
Bootstrap scans for ISDSCompatAdapter (reflection, no static references)
        └─▶ adapter Apply ──▶ SDS_API bridge ──▶ SDS_Rules (withdraw / deposit / return) / SDS_Core (read-only / structure)
third-party mod containers / recipes / UI ──▶ adapter callbacks (facts only)
```

- Third-party inventories serve only as a source of facts (S16); adapters never debit on their own and never build a second write path (E7).
- Target mods are only probed via reflection; reverse reads (display / job assignment) also go through the `SDS_API` bridge and never touch Core implementation.

## 7. Saves ↔ Runtime

- **Write**: `SDS_GameComponent_Grid.ExposeData` → `SDS_Ledger.ExposeData` (ledger) + `SDS_VanillaState.ExposeData` (runtime shell) + `sdsReclaim` (the reclaim ledger, a component-level node); node names and LookMode are in ModuleInterfaces.en.md §8. Mirrors / display things / exposure are all derived caches; nothing is written to disk except the mirror shells kept for migration.
- **Read**: the fixed component-construction order is ModuleRules.en.md §9 step 7; `ExposeData` only fills content and never rebuilds → `FinalizeInit` emits a single post-load count report.
- **Restore**: structural facts are re-registered by building `SpawnSetup`; the mirror table is rebuilt from the ledger by `SyncAll`; unresolved keys keep `defName` / `stuffName` and wait for the next resolution pass; the reclaim ledger is imported via `sdsReclaim` (the old `sdsProtectedOutputs` was retired on 2026-11-16, see SDS.Vanilla/CompatibilityRetirement.en.md).

```text
save XML ◀──▶ SDS_GameComponent_Grid (shell) ◀──▶ SDS_Ledger + SDS_VanillaState
                                    derived caches (mirrors / display things / exposure) are rebuilt only from the ledger
```

## 8. Settings

```text
settings page ──▶ SDS_Settings (ModSettings, persisted to disk) ──▶ ClampAll
       ──▶ SDS_SettingsApplier: building def power + on-map CompPowerTrader recalculation
                              + endpoint priority notifications + wealth recalculation
```

Settings only affect building def / instance consumption accounting and visibility; they never write the ledger.

## 9. Cross-Flow Invariants

- The ledger is the single source of truth; mirrors, display things, exposure, and resource readouts are all one-way derivatives and never write back.
- Transactions / reservations are the only "in-flight" state: reservations are not released before delivery, and returns only use the channel-specific paths.
- Third parties and the outward API go only through Contracts contracts and the `SDS_Core` facade, never touching implementation types.
