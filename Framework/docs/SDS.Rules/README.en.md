# SDS.Rules

> Positioning: This directory is the complete maintenance documentation for the `SDS.Rules` assembly.
> **Current status (P5, migration complete)**: Endpoint resolution, bill supply, the decision layer (channels / capacity / availability / structure / chip), and the process layer (deposit / withdraw / return / in-transit marking) all live in Rules; `SDS.Core` keeps only the ledger and data primitives. During the migration there was no dual writing and no forwarding (Guideline 3).

## 1. Target Responsibilities

Rules is "the single definition point of this Mod's gameplay and semantic rules"; it produces only decisions and processes, holds no ledger, and executes no vanilla behavior:

| Rule facet | Implementation | Status |
|---|---|---|
| Whose inventory to draw from | `SDS_EndpointResolver` | P2 |
| Bill supply selection / shortfall size | `SDS_BillSupplyPlanner` | P2 |
| Reservation → materialization → delivery / rollback | `SDS_WithdrawTransaction`, `SDS_WithdrawExecutor` | P3 |
| Returns and failure fallbacks | `SDS_ReturnUtility` | P3 |
| In-transit materialization marking | `SDS_ProducedThingMarker` | P3 |
| Deposit / withdraw / Job reservation facade | `SDS_RulesImpl` | P3 |
| Whether the chip port is operational | `SDS_ImplantUtility` | P4 |
| Item → channel / key, capacity, storability | `SDS_StockRules` (data primitives in Core's `SDS_StockData`) | P4 |
| Availability / tradable count / clamping | `SDS_Availability` | P4 |
| Structure facts, global switch G, capacity cap | `SDS_StructureRegistry` | P4 |
| Endpoint contract version clock | `SDS_EndpointContractCache` | P4 |

## 2. Explicitly Out of Scope

- Holds no ledger data and does no quantity arithmetic: the ledger, reservations, saves, and `total − reserved` are in `SDS.Core`.
- No patches, no Job dispatching, no UI writing, no vanilla contact: those live in `SDS.Vanilla`.
- No external five axes (See/Read/Find/Take/Store): third parties go only through `SDS.Api`.

## 3. Dependencies and Artifact

- Compile-time references: `SDS.Contracts`, `SDS.Diag`, `SDS.Core` (`SDS.Rules.csproj`; ModuleRules.en.md §4).
- Artifact: `SDS.Rules.dll`, output to `超维存储/Assemblies`; namespace unified as `SuperDimensionalStorage`.

## 4. public Entrypoints

`SDS_Rules`: the sole rules / process facade (P2 endpoints and bill supply; P3 deposit / withdraw / Job reservation / mirror / return / in-transit marking; P4 the decision layer and structure / endpoint versions).
`SDS_Availability`: the single authoritative semantics for availability / failure classification / clamping (W22 / W56 / R15 / R17).
`SDS_ImplantUtility`: chip port facts.
All other rule implementation classes are `internal`.

Any top-level `public` outside the whitelist is treated as a leak ([Rules.en.md](Rules.en.md); `CheckAssemblyBoundaries.ps1` §4; ModuleRules.en.md §5).

## 5. Migration Status

| Phase | Content | Core-side change |
|---|---|---|
| P1 | Project / public surface / documentation skeleton | Unchanged |
| P2 | Endpoint resolution and bill supply moved in | Deleted 5 rule facades; added `HasLiveContent` (later removed from the public facade during review and folded internally into `SDS_StockData`) |
| P3 | Process layer moved in (transaction / executor / return / in-transit marking) | Deleted 19 process facades; `FindWithdrawableKey` made internal; added `InternalsVisibleTo("SDS.Rules")` |
| P4 | Decision layer moved in (channels / capacity / availability / structure / chip) | Deleted 16 decision facades and the public classes `SDS_Availability` / `SDS_ImplantUtility`; Core keeps only the ledger + `SDS_StockData`; `RegisterAll` registers self-checks only |

## 6. Document Map

| Document | Content |
|---|---|
| [Interfaces.en.md](Interfaces.en.md) | Signature index, consumed contracts, call relationships, vanilla / third-party contact surface |
| [Rules.en.md](Rules.en.md) | Rule body (migrated in P2–P4; P5 wrap-up complete) |
| [DesignDecisions.en.md](DesignDecisions.en.md) | Why Rules was split out, why Rules depends on Core, migration trade-offs |
