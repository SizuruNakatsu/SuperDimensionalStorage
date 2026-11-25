# SDS.Rules · Interfaces

> Positioning: This module's consumption surface and signature index. The sole authority for signatures and member semantics is `SDS.Rules_API.cs`; this document does not duplicate signatures.
> **Current status (P5, migration complete)**: `SDS_Rules` carries endpoint resolution, bill supply, the decision layer, and the process layer; `SDS_Availability` / `SDS_ImplantUtility` also live in this assembly.

## Section A: Signature Index

| Type | Declaration site (sole authority) | Purpose |
|---|---|---|
| `SDS_Rules` | `Source/SDS.Rules/SDS.Rules_API.cs` | The rules module's sole facade; P2 / P3 / P4 members in A.1 / A.2 / A.3 |

### A.1 P2 Facade Members

| Method | Semantics | Consumers |
|---|---|---|
| `SelectEndpoint(in SDS_EndpointQuery)` | Candidates → static gate → priority / distance / self ordering → reachability checked only for the winning candidate | Api / Vanilla |
| `AllowsDef(...)` | Whether a recipe slot / def is accepted; the medical gate is injected via `extraAllows` | Api bill-supply bridge / Vanilla |
| `TryPlanSlot(...)` | Single-slot bill supply planning; produces plans only, never touches the ledger | Api bill-supply bridge |
| `TryPlanAllowedDefSupply(...)` | Def-list bill supply planning; shares the allocation strategy with the single-slot variant | Api bill-supply bridge |
| `NotifyDeposited(pool, def, amount)` | Only updates the caller's own pool counter | Api bill-supply bridge |

### A.2 P3 Facade Members (Process Layer)

| Method | Semantics | Consumers |
|---|---|---|
| `CanDeposit(Thing)` / `TryDeposit(Thing, ISDS_Port, out int)` | Deposit feasibility / ledger posting; the virtual channel converts physical into virtual | Api / Vanilla |
| `TryWithdraw(...)` / `TryWithdrawPreservedExact(...)` / `TryWithdrawPreservedExactAdopted(...)` / `TryWithdrawReservedForJob(...)` | Withdraw transactions and Job adoption (W21 / W24) | Api / Vanilla |
| `TryReserveForJob(...)` / `TryReservePreservedForJob(...)` / `ReleaseJobReservation(int)` | Job reservation lifecycle | Vanilla |
| `TryTakeMirror(...)` / `TryConsumeMirror(Thing, out int)` | Mirror withdrawal / external destroy confirmation (W22) | Api / Vanilla |
| `ConvertPreservedGearToVirtual()` | W34 stock migration after player confirmation | Bootstrap |
| `ReturnThing(...)` (two overloads) / `ReturnVirtualCount(...)` / `ReturnThings(...)` | Returns and failure fallbacks (W27 / W49) | Api / Vanilla |
| `IsProducedMarked` / `MarkProduced` / `UnmarkProduced` | In-transit materialization marking (W49) | Api / Vanilla |
| `RegisterAll()` | Registers the 4 static cleanup items and the Rules self-check provider (E1) | Bootstrap |

### A.3 P4 Facade Members (Decision Layer)

| Member | Semantics | Consumers |
|---|---|---|
| `Available` / `TypeCapacity` | Global switch G / category capacity cap (structure registry) | Api / Vanilla / Rules |
| `EndpointVersion` | Endpoint contract version clock | Api / Vanilla |
| `TryGetStockKey` / `RequiresPreservedStorage` / `IsLegacyUnrebuildableDef` / `CategoryKeyOf` | Channel / key / category-membership criteria | Api / Vanilla / Rules |
| `TryFindWithdrawableKey` / `TryFindTradableKey` / `GetAvailableCountForDef` | Key selection by def / totals (R15) | Api / Vanilla |
| `SetExternalPreserveProvider` | Third-party preserved-fact injection | Bootstrap |
| `RegisterStructure` / `UnregisterStructure` / `ReconcileStructures` / `NotifyEndpointChanged` | Structure facts / endpoint version | Vanilla / Bootstrap |
| `SDS_Availability.*` | Availability / failure classification / clamping (W22 / W56 / R15 / R17) | Api / Vanilla |
| `SDS_ImplantUtility.*` | Chip port facts | Api / Vanilla |

## Section B: Contracts Consumed by This Module

Actually consumed by P2–P4:

| Source | Type / member | Purpose |
|---|---|---|
| Contracts `SDS_EndpointContract.cs` | `SDS_EndpointOp`, `SDS_EndpointQuery`, `SDS_EndpointDecision`, `SDS_StructureFact`, `SDS_StructureKind` | Endpoint resolution / structure facts |
| Contracts `SDS_Host.cs` | `ISDS_Port`, `ISDS_PortProvider`, `ISDS_DeliverySink`, `SDS_HediffDefOf` | Candidate enumeration / deposit ports / return fallbacks / chip def |
| Contracts `SDS_Stock.cs` | `SDS_StockKey`, `SDS_StockChannel`, `SDS_StockVitals`, `SDS_StockEntry` | Withdrawal keys / return channels / transaction state write-back / self-check snapshots |
| Contracts `SDS_Withdraw.cs` | `SDS_WithdrawPlan`, `SDS_WithdrawQuery`, `SDS_WithdrawMode`, `SDS_WithdrawEndState`, `SDS_SupplyFailure` | Withdraw transactions / plans / supply failure classification |
| Contracts `SDS_Search.cs` | `SDS_ReturnFallback` | Return fallback |
| Contracts `SDS_Mod.cs` | `SDS_Config` (capacity / key granularity / forced-virtualization switch) | Structure capacity / channel determination |
| RimWorld / Verse | `Bill`, `RecipeDef`, `IngredientCount`, `ThingFilter`, `ThingDef`, `ThingCount`, `Thing`, `Pawn`, `Map`, `IntVec3` | Bill supply slot determination / transactions / returns |
| Core (public) | `SDS_Core.UsedCapacity`; `ISDS_LedgerRead` / `ISDS_LedgerHost` | Capacity gate / read-only ledger access |
| Core (internal, Core → Rules friend) | `SDS_Ledger`, `SDS_CoreImpl.Ledger`, `SDS_StockData` | Ledger read/write / key construction / materialization data primitives |

P5 complete: the decision layer implementation and the public surfaces of `SDS_Availability` / `SDS_ImplantUtility` are all in this assembly; Core keeps only the ledger and `SDS_StockData`.

## Section C: Call Relationships

| Direction | Counterpart | Entry points |
|---|---|---|
| Who calls Rules | `SDS.Api` | Endpoints / bill-supply bridge / withdraw & deposit / returns / decision-layer forwarding |
| Who calls Rules | `SDS.Vanilla` | Endpoint resolution, structure facts, chip, availability, withdraw transactions, Job reservations, mirror, returns, in-transit marking |
| Who calls Rules | `SDS.Bootstrap` | `RegisterAll`; `SetExternalPreserveProvider`; `ConvertPreservedGearToVirtual` after W34 confirmation |
| Who Rules calls | `SDS.Core` | Ledger read/write (`SDS_Ledger`), used category count, key construction / materialization data primitives (`SDS_StockData`) |
| Who Rules calls | Contracts host slots | `Ports` (endpoint candidates), `DepositFilter` (storability), `Delivery` (return fallbacks); `Priority` / `MirrorPhysical` / `MedicalCare` only via input parameters |
| Who Rules does not call | `SDS.Vanilla`, `SDS.Api`, Harmony | Forbidden by the dependency direction |

## Section D: Vanilla Support

Vanilla contact points already migrated in:

- `SDS_EndpointResolver`: reads only the five facts of `ISDS_Port` (available / self / reachable / squared distance / `Thing` (tie-break thingID)); no direct pathfinding.
- `SDS_BillSupplyPlanner`: slot determination and corpse filtering via `Bill` / `RecipeDef` / `IngredientCount` / `ThingFilter`; does not read the workbench / Jobs.
- `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor`: interacts with the ledger only via `ThingCount` / `Thing` / `Pawn` / `Map`; no pathfinding, no Job dispatching, no UI writing.
- `SDS_ReturnUtility`: return fallbacks go only through `ISDS_DeliverySink` and vanilla `Thing` operations; does not call `GenPlace` / `GenSpawn`.
- `SDS_ProducedThingMarker`: reads only `GenTicks.TicksGame` as its lifetime clock.
- `SDS_StockRules`: reads only `ThingDef` capability bits / comps and `Thing` instance state; no pathfinding, no job assignment.
- `SDS_ImplantUtility`: reads only `Pawn.health` / faction / map and `SDS_HediffDefOf`.
- `SDS_StructureRegistry`: reads only `Thing` references / map / `SDS_Config`.

## Section E: Third-Party Support

- Third parties do not reference Rules directly; they go only through the `SDS_API` five axes (See/Read/Find/Take/Store) and adapter bridges.
- Rules knows no specific Mod and adds no defName branches; external facts enter only as input parameters / delegates.
