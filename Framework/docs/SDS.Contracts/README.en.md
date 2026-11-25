# SDS.Contracts

One-line positioning: the pure data-contract assembly shared by all modules of SuperDimensional Storage; it declares only types, interfaces, DefOf, settings, and host injection points, and contains no behavior implementation.

## Responsibilities

| # | Responsibility |
|---|---|
| 1 | Cross-module data: DTOs and enums such as category keys / ledger entries / reservations / reclaims / loading rows / sidebar state. |
| 2 | Value types for endpoint queries, decisions, and structure facts. |
| 3 | The interfaces corresponding to the 11 host injection slots, plus `ISDS_Port`, `ISDS_DiagnosticProvider`, `ISDSCompatAdapter`, and `SDS_Host`. |
| 4 | Settings data `SDS_Settings` and the read-only accessor `SDS_Config`. |
| 5 | `SDS_JobDefOf` / `SDS_HediffDefOf` and the pure registries `SDS_PreserveHooks` / `SDS_CompatGates`. |
| 6 | Frozen save node names, LookMode values, and cross-module public signatures. |

Out of scope: it implements no host interface and performs no runtime rule evaluation or computation (weighted averaging, sorting, capacity, filtering, transactions, returns, and reclaim belong to Core / Vanilla); it installs no Harmony, writes no UI, does no pathfinding, and defines no behavior-layer Job / Toil / ThinkTree / WorkGiver / Duty; it provides no settings page (the settings UI belongs to Bootstrap; this module supplies only data and clamping); it holds no ledger or static state (runtime state belongs to Core / Vanilla).

## Dependencies and artifacts

| Item | Content |
|---|---|
| Assembly references | No `ProjectReference`; only `Assembly-CSharp` (RimWorld / Verse) and `UnityStubs` (UnityEngine); see `Directory.Build.props` |
| Artifact | `SDS.Contracts.dll`, output to `SuperDimensionalStorage/Assemblies` |
| Tests | `SDS.Tests` may reference all modules, including this assembly |

## Public entry points (type names only)

- Categories and ledger: `SDS_StockKey`, `SDS_StockEntry`, `SDS_StockVitals`, `SDS_Reservation`, `SDS_StockChannel`
- Transactions and reclaim: `SDS_WithdrawMode`, `SDS_WithdrawQuery`, `SDS_WithdrawPlan`, `SDS_WithdrawEndState`, `SDS_SupplyFailure`, `SDS_ReclaimPolicy`, `SDS_ReclaimOutcome`, `SDS_ReclaimSave`, `SDS_ReturnFallback`, `SDS_WithdrawDeliveryPolicyDef`, `SDS_ReadVersions`
- Endpoints and structure: `SDS_EndpointOp`, `SDS_EndpointQuery`, `SDS_EndpointDecision`, `SDS_StructureKind`, `SDS_StructureFact`
- Host and ports: `ISDS_Port`, `ISDS_PortProvider`, `ISDS_DepositFilter`, `ISDS_PrioritySource`, `ISDS_DeliverySink`, `ISDS_MirrorQuery`, `ISDS_MirrorPhysical`, `ISDS_MedicalCarePolicy`, `ISDS_ReclaimWorld`, `ISDS_DisplayQuery`, `ISDS_DepositJobs`, `ISDS_TransportLoadSink`, `ISDS_DiagnosticProvider`, `ISDSCompatAdapter`, `SDS_Host`
- API boundary: `SDS_ApiCaller`, `SDS_ApiResultCode`, `SDS_ApiStockInfo`
- Settings: `SDS_Settings`, `SDS_Config`
- DefOf and pure registries: `SDS_JobDefOf`, `SDS_HediffDefOf`, `SDS_PreserveHooks`, `SDS_CompatGates`
- Save DTOs: `SDS_TransportLoadSave`, `SDS_SidebarUIState`

Field-level signatures take Section A of `Interfaces.en.md` as the sole authority; this page lists type names only.

## Iron rules

See `Rules.en.md` C1–C8; this page does not restate them.

## Document map

| File | One line |
|---|---|
| `README.en.md` | This page: positioning, responsibilities, dependencies and artifacts, and the public entry-point index. |
| `Interfaces.en.md` | Section A external signatures (the sole signature authority), Section B consumption surface of the other 6 assemblies and call relationships, vanilla support, third-party support. |
| `Rules.en.md` | The C1–C8 rules belonging to this module alone, each executable and checkable, plus the migrated normative rule text (authoritative). |
| `DesignDecisions.en.md` | Why it is done this way: historical decisions and pitfalls, what the migration removed / added (with reasons), design trade-offs of a pure data assembly, and historical notes on legacy save nodes and fields. |

Once you have read this directory you can start work; the only additional read is ModuleRules.en.md (the single global page)
