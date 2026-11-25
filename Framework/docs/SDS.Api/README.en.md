> Positioning: SDS.Api is the sole external facade of `SuperDimensionalStorage`: it translates caller context into queries Core can understand, then translates the results back into structured result codes; it does not define a second set of rules.

# SDS.Api (External Five-Axis Facade)

## 1. Positioning

- Assembly `SDS.Api.dll`, namespace uniformly `SuperDimensionalStorage`; the only public type is `SDS_API` (ModuleResponsibilities.en.md §2).
- Aimed at external third parties and the in-repo `SDS.ThirdModCompat`; vanilla behavior, patches, and UI are not in this module (ModuleResponsibilities.en.md §1).
- There is no `InternalsVisibleTo` across assemblies; utilities the compatibility layer needs hang off the same `SDS_API` as public methods, known separately as the "adapter bridge" (ModuleInterfaces.en.md §5).

## 2. Responsibilities

| Axis / surface | Methods | Forwards to |
|---|---|---|
| See | `VisibleThingsOfDef` | `SDS_Host.MirrorQuery` |
| Read | `ReadStock` / `ReadStockFor` / `CaptureReadVersions` | `SDS_Core.Ledger` (`ISDS_LedgerRead`), `SDS_Availability`, `SDS_Rules.TryGetStockKey`, `SDS_Host` versions |
| Find | `TryFindDepositEndpoint` / `TryFindWithdrawPoint` | `SDS_Rules.SelectEndpoint` |
| Take | `TryWithdraw` (two overloads) / `TryWithdrawFromMirror` / `TryWithdrawPreservedExact` | `SDS_Rules.TryWithdraw` / `TryTakeMirror` / `TryWithdrawPreservedExact` |
| Store | `TryDepositNow` / `TryDepositAt` | `SDS_Rules.TryDeposit` |
| Adapter bridge | endpoints, ledger read-only, display / mirror, source channels, job assignment, withdraw by def in bulk, supply planning, loading, returns | the unique implementations in `SDS_Core` / `SDS_Rules` / `SDS_Host` |

No UI, no pathfinding, no delivery, no ground placement, no entering containers, no building JobDrivers; no caches or shadow counts are maintained — every call reads fresh (PublicAPI.en.md §3.1).

The iron rules of this module are in [Rules.en.md](Rules.en.md).

## 3. public Entry Points

- The only public type is `SDS_API`: the five axes (See/Read/Find/Take/Store) in `SDS_API.cs`, the adapter bridge in `SDS_API_Adapters.cs` (both `partial` of the same `SDS_API`); the constant `Version = 4`.
- Public types visible externally in Contracts: `SDS_ApiCaller`, `SDS_ApiResultCode`, `SDS_ApiStockInfo`, `SDS_ReadVersions`, `SDS_StockKey`, `SDS_WithdrawMode`.
- Five-axis signatures, bridge surface categories, forwarding targets, and the consumed Contracts / `SDS_Host` slots are in [Interfaces.en.md](Interfaces.en.md); the public method list of the bridge is registered uniquely in ModuleInterfaces.en.md §5.
- Although the adapter bridge is public, it is not covered by the `Version` commitment; its only runtime consumer is `SDS.ThirdModCompat`.

## 4. Dependencies and Artifact

| Item | Content |
|---|---|
| Project references | `SDS.Contracts`, `SDS.Diag`, `SDS.Core` (`SDS.Api.csproj`) |
| Forbidden references | `SDS.Vanilla`, `0Harmony`, `SDS.ThirdModCompat` (ModuleRules.en.md 6.5) |
| Artifact | `SDS.Api.dll` |
| Patch count | 0 (ModuleRules.en.md §7) |
| Static state registration | none; Api / Sdk utility classes / Bootstrap total 0 items (ModuleResponsibilities.en.md §4) |

## 5. Document Map

| Document | Content |
|---|---|
| [Interfaces.en.md](Interfaces.en.md) | public signature index, consumed Contracts / `SDS_Host` / Core members, call relationships, vanilla support, third-party support |
| [Rules.en.md](Rules.en.md) | checkable rules of this module, the caller-gate order and how it is checked |
| [DesignDecisions.en.md](DesignDecisions.en.md) | historical decisions and pitfalls, differences between the old and new implementations, design trade-offs, pending items (the complete "why it was done this way") |
| [../ModuleRules.en.md](../ModuleRules.en.md) | global hard constraints, dependency matrix, per-module prohibitions |

Reading this directory is enough to start work; ModuleRules.en.md is the only additional read.
