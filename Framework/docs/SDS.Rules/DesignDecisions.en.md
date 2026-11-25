# SDS.Rules · Design Decisions

> Positioning: Records "why Rules was split out", "why the dependency direction is Rules → Core", and "how the migration avoids dual writes".
> This file answers only why and defines no rules; the rule body is in [Rules.en.md](Rules.en.md), and checkable items are in each module's `Rules.en.md`.

## 1. Why the Rules Were Split Out of Core

- Goal: Core does only the ledger, ensuring **data reliability** and **consistent quantity semantics**; rules (whom to take from, how much to take, which channel to use, how to roll back) become their own module so each can be tested independently.
- Problem with the old state: `SDS_Core` carried the ledger, endpoint resolution, supply planning, chip ports, in-transit marking, and transaction flow all at once, with a public surface of 70+ members; "ledger bugs" and "rule bugs" could not be located separately in tests and logs.
- After the split:
  - `SDS.Core`: ledger, reservations, `total − reserved`, saves, ledger self-checks; does not read `SDS_Host` and does not reference Rules.
  - `SDS.Rules`: gameplay / semantic rules and processes; the sole rules facade `SDS_Rules`.
  - `SDS.Vanilla`: keeps only the wiring constraints and implementation of "how to hook into vanilla".

## 2. Why Rules Depends on Core, Not the Other Way Around

- Rules must read the ledger and write reservations, so Rules → Core is the natural direction.
- The reverse (Core → Rules) would make Core hold rule references, breaking the "pure ledger" and forming a cycle when removing the old facades.
- Therefore **no Core → Rules forwarders exist**; each phase of P2–P4 moves a group of rules wholesale and switches consumers in the same step, letting the compiler guarantee nothing is missed.
- The physical boundary of ledger writes: `SDS_Ledger`'s write methods are **internal**, granted by Core via `InternalsVisibleTo("SDS.Rules")` to Rules only; Vanilla / Api cannot bypass Rules to write the ledger directly (pure data switches such as `SetForbidden` excepted). No standalone `ISDS_LedgerWrite` interface exists.

## 3. Migration Order

| Phase | What moves first | Reason |
|---|---|---|
| P1 | Project / public surface / documentation skeleton | First verify the project graph, script registration, and public surface baseline without touching semantics |
| P2 | Endpoint resolution, bill supply | Depends least on ledger writes; the chip was deferred to P4 because it depends on structure facts, and in-transit marking to P3 because Core's returns still called `Unmark` |
| P3 | Process layer: transactions, executors, returns, in-transit marking | The process layer depends on the decision layer, but Rules already referenced Core, so it could move first; after the move Core no longer holds the transaction state machine or return destinations |
| P4 | Decision layer: channels, capacity, availability, structure facts, chip | The decision layer moves last, narrowing Core to a pure ledger |

## 4. What Is Not Done

- Vanilla's patch / Job / UI constraints are not moved into Rules: that would force Rules to reference Harmony / UI, breaking Guideline 1 / 2 and the dependency matrix.
- Old entry points such as `SDS_Core.SelectEndpoint` are not kept for compatibility: rules are defined exactly once, and old entry points are deleted in the same migration phase.
- Save nodes and fields are not modified in this split: preserved true bodies remain held by Core, so old-save readability is unaffected; type-level "zero RimWorld" is a separate later L3.

## 5. P2 Actual Migration Record

- Migrated in: `SDS_EndpointResolver` (`Select` / `TryEvaluateStaticGate` / `IsBetter`), `SDS_BillSupplyPlanner` (all entry points).
- Not migrated in, and not copied:
  - `SDS_ImplantUtility`: ChipA of `IsChipOperational` needs Core's `MapHasOperationalCore` (structure facts), so it moved together with the P4 structure migration.
  - `SDS_ProducedThingMarker`: during P2, Core's `SDS_ReturnUtility` still called `Unmark` (W49), so it moved together with the P3 process layer.
  - `SDS_EndpointContractCache`: only the endpoint version clock, not a decision; stayed in Core for now, with its ownership re-evaluated during the P4 structure migration.
- Public surface changes: `SDS_Core` deleted 5 rule facades and added the `HasLiveContent` data criterion; `SDS_Rules` added 5 rule facades. `SDS_API` signatures unchanged, only the forwarding target changed to `SDS_Rules`.
- Tests: `EndpointTests` / `BillMedicalGateTests` moved to `SDS.Rules.Tests`; Core tests at the time referenced only Contracts / Diag / Core.

## 6. P3 Actual Migration Record

- Files migrated in: `SDS_WithdrawTransaction`, `SDS_WithdrawExecutor`, `SDS_ReturnUtility`, `SDS_ProducedThingMarker`; added `SDS_RulesImpl` (19 process facade implementations + `RegisterAll`).
- `SDS_Rules` public surface additions: `CanDeposit` / `TryDeposit` / `TryWithdraw` / `TryWithdrawPreservedExact` / `TryWithdrawPreservedExactAdopted` / `TryWithdrawReservedForJob` / `TryReserveForJob` / `TryReservePreservedForJob` / `ReleaseJobReservation` / `TryTakeMirror` / `TryConsumeMirror` / `ConvertPreservedGearToVirtual` / `ReturnThing` (two overloads) / `ReturnVirtualCount` / `ReturnThings` / `IsProducedMarked` / `MarkProduced` / `UnmarkProduced` / `RegisterAll`.
- `SDS_Core` deleted the above 19 facades in sync; `FindWithdrawableKey` narrowed from private to internal (for Rules key selection); `RegisterAll` dropped from 4 items to 3, with in-transit marker cleanup registered by `SDS_Rules.RegisterAll`.
- Added `SDS.Core/Properties/AssemblyInfo.cs`: `InternalsVisibleTo("SDS.Rules")`; registered in `CheckAssemblyBoundaries.ps1` §5.
- Consumers switched in the same phase: Api / Vanilla / Bootstrap / tests all switched to `SDS_Rules`; test references were split by module in P5.
- Why the process layer moved first: the process layer's boundaries (the transaction state machine, return destinations, the in-transit marker lifecycle) are the most likely to drift between Core/Rules; fixing "who orchestrates" first meant that when P4 moved the decision layer, only the callee was replaced and the call direction never changed.

## 7. P4 Actual Migration Record

- Files migrated in: `SDS_StockRules`, `SDS_Availability`, `SDS_StructureRegistry`, `SDS_ImplantUtility`, `SDS_EndpointContractCache`; the public classes `SDS_Availability` / `SDS_ImplantUtility` moved into `SDS.Rules_API.cs` as well.
- Added `SDS_StockData` (Core, internal): key construction / category membership keys / basic storability criteria / stack-average state / materialization / state write-back. The ledger depends only on this data primitive and no longer on rule decisions.
- `SDS_RulesImpl` gained the decision layer implementation: `Available` / `TypeCapacity` / `EndpointVersion` / `TryGetStockKey` / `RequiresPreservedStorage` / `IsLegacyUnrebuildableDef` / `HasLiveContent` / `CategoryKeyOf` / `TryFind*` / `GetAvailableCountForDef` / `SetExternalPreserveProvider` / structure registration and endpoint version / `MapHasOperationalCore` / `FindWithdrawableKey`.
- W34 separates mechanism from criteria: `SDS_Ledger.ConvertPreservedGearToVirtual(Func<Thing,bool>)` does only the ledger mechanism; "which items may migrate" is injected by Rules' `SDS_StockRules.CanMigratePreservedGearToVirtual`, and Core never references Rules in reverse.
- Static cleanup: `SDS_Core.RegisterAll` registers only Core self-checks; the 4 cleanup items and `SDS_RulesDiagnostics` are registered by `SDS_Rules.RegisterAll`. Core diagnostics keep only virtual availability / true-body invalidation / ledger invariants.
- `SDS_Core`'s public surface narrowed to: `Ledger` / `CreateLedger` / `Attach` / `UsedCapacity` / `SetForbidden` / `ApplyQuality` / `ApplyVitals` / `NotifyDepositFilterChanged` / `NotifyCapacitySettingsChanged` / `AuditReservations` / `RegisterAll`.
- Tests (P4 record): `CoreTestKit` added a Rules reference to keep existing cases running; P5 split them into the Core / Rules test projects (see Section 8).

## 8. P5 Wrap-Up Record

- Test project split: `SDS.Core.Tests` references only Contracts / Diag / Core and keeps `ConservationTests` / `InvariantTests`; rule / process tests (`TransactionTests`, `ReservationTests`, `ReturnTests`, `MigrationTests`, `LedgerTests`, `IdentityDedupTests`, `ScribeRoundTripTests`, `CoreHardeningTests`, `LedgerStructureTests`) moved into `SDS.Rules.Tests`.
- Added `SDS.Rules.Tests/Support/RulesTestKit.cs`: `RulesTestEnv` wires host slots, external preserved delegates, structure facts, and test ports on top of the ledger environment; static cleanup clears the Rules caches directly via reflection. The test fake hosts (`Fakes`) are owned by this project and reused by `SDS.Tests` via source linking; `SDS.Core.Tests` keeps only `CoreTestKit` / `TestAssembly`.
- `CoreTestEnv` narrowed to "settings + create and attach a ledger" and no longer touches Rules; `SDS.Tests` reuses the integration environment by linking `RulesTestKit.cs`.
- `SDS.Rules.Tests` added a `0Harmony` test reference (test output only, for the mirror / relic probe cases); script §3 was updated to allow it in sync.
- Core deleted the two leftover forwarders `SDS_CoreImpl.ApplyQuality` / `ApplyVitals`; the public facade forwards to `SDS_StockData` directly.
- Public surface baseline regenerated: Core 32 entries / Rules 53 entries; the `SDS_API` five axes (See/Read/Find/Take/Store) and `Version` unchanged.
- Review cleanup (2026-11-16): deleted the `SDS_Rules.HasLiveContent` public facade (used only internally by bill supply; now calls Core's `SDS_StockData.HasLiveContent`); deleted the two never-read fields `SDS_WithdrawQuery.referenceCell` / `preferredMode`; `SDS_WithdrawPlan.endpoint` kept (read when Vanilla's loading selection builds a Job via `SDS_LoadSelection`). Public surface baseline after cleanup: Contracts 301 entries / Rules 52 entries.
