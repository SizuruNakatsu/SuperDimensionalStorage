# SDS.Core

> Positioning: this directory holds the complete maintenance documentation for the `SDS.Core` assembly, self-contained: reading only this directory is enough to maintain this module; interface signatures and iron rules are all inlined here.
> The only additional required reading is ModuleRules.en.md (a single global page); when cross-module boundaries are in doubt, consult ModuleResponsibilities.en.md §2 and ModuleInterfaces.en.md §5 and §8.

## 1. Responsibilities

Core is the sole implementation of the ledger and of quantity semantics, and produces no Vanilla behavior (ModuleResponsibilities.en.md §5); gameplay / semantic adjudication and flows are defined uniquely in `SDS.Rules` (P2–P4 fully migrated out, P5 completed the wrap-up).

| Responsibility | Implementation type | Description |
|---|---|---|
| Ledger | `SDS_Ledger` | Virtual counts, preserved true bodies, reservations, forbidding, the three version numbers, save / Tick |
| Ledger data primitives | `SDS_StockData` | Key construction, category membership keys, basic storeability criteria, stack-average state, materialization, state write-back |
| Ownership transfer | `SDS_Ownership` (inside `SDS_Ledger.cs`) | The sole implementation for moving things between "map / container / ledger"; zero side effects on failure; directly modifying `holdingOwner` is forbidden |
| Ledger self-check | `SDS_CoreDiagnostics` | Virtual availability / invalid preserved true bodies / full ledger invariants |

The adjudication layer (channels / capacity / availability / structures / chips) and the flow layer (pickup transactions / returns / in-transit markers) have lived in `SDS.Rules` since P3–P4.

## 2. Explicitly Out of Scope
- Does not define gameplay / semantic rules or flows: endpoint resolution, bill supply, channel / capacity / availability, structure facts, chip ports, transactions / returns / in-transit markers are all in `SDS.Rules`.
- Does not deliver: never calls `ISDS_DeliverySink` to complete normal delivery; return fallbacks live in Rules.
- No pathfinding, no map enumeration, no job assignment, no UI, no patching (ModuleRules.en.md 6.3).
- Does not write `Pawn.carryTracker` / `inventory.innerContainer`; does not decide the delivery mode or the source of endpoint priority (mode, priority save values, and candidate enumeration all live in Vanilla, ModuleResponsibilities.en.md §5).

## 3. Dependencies and Artifact
- At compile time it references only `SDS.Contracts` and `SDS.Diag` (`SDS.Core.csproj`; ModuleRules.en.md §4); the consumption surface is in [Interfaces.en.md](Interfaces.en.md) Section B.
- Artifact: `SDS.Core.dll`, output to `超维存储/Assemblies`; the namespace is uniformly `SuperDimensionalStorage`, with no new sub-namespaces.

## 4. public Entry Points
`SDS_Core`, `ISDS_LedgerRead`, `ISDS_LedgerHost`; `SDS_Ledger` / `SDS_StockData` are internal implementation types and do not form the external surface.
After the P2–P4 migration, `SDS_Core` retains only the ledger lifecycle, the used category count, the forbid switch, state write-back, and version notifications; the endpoint / supply / flow / adjudication-layer facades all live in `SDS_Rules` (including `SDS_Availability`, `SDS_ImplantUtility`). Core's internal ledger write interface is granted to Rules only, via `InternalsVisibleTo("SDS.Rules")`.

Any top-level `public` outside the whitelist counts as a leak ([Rules.en.md](Rules.en.md) §4; `CheckAssemblyBoundaries.ps1` §4; ModuleRules.en.md §5).

## 5. Iron Rules
See the checkable entries in [Rules.en.md](Rules.en.md) and the authoritative rule ID table in [Rules.en.md](Rules.en.md) §11; in one sentence: the ledger and quantity semantics are defined exactly once, in Core, and gameplay / semantic rules and flows are defined exactly once, in `SDS.Rules` (ModuleRules.en.md §2).

## 6. Document Map

| Document | Content |
|---|---|
| [Interfaces.en.md](Interfaces.en.md) | A Signature index; B Consumed Contracts / host slots; C Call relationships; D Vanilla support; E Third-party support |
| [Rules.en.md](Rules.en.md) | Checkable iron rules: forbidden symbols, internal boundary, transactions / returns / mirrors / supply / cleanup / save |
| [DesignDecisions.en.md](DesignDecisions.en.md) | Historical decisions, pitfalls, and design trade-offs |
| ModuleRules.en.md (outside this directory, the only additional read) | Global principles, boundaries, and diagnostic requirements |

Reading this directory is enough to start work; ModuleRules.en.md is the only additional read.
