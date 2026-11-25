# SDS.Diag

> Positioning: the diagnostics infrastructure module. It only provides the logging facade, development-time assertions, the self-check entry point, the static state registry, and the adapter report hub; it carries no gameplay rules and knows nothing about the implementation types of Core / Vanilla / ThirdModCompat.

## 1. Responsibilities

| Capability | Content | Source |
|---|---|---|
| Logging facade | `ErrorOnce` (dedup by message + call site + exception type, supplemental entries at the 10th / 100th / 1000th occurrence), `Fail` (Error level, 600-tick throttle), `Warn` (Warning level, once per reason per process), `Ctx` (unified context string) | `SDS_Diag.cs` |
| Development-time assertions | E2: availability range, transaction stage, transaction terminal state; logs Error only, never throws | `SDS_Invariants.cs` + `SDS_Diag.Assert*` |
| Self-check entry point | Collects the report lines of each module's `ISDS_DiagnosticProvider` and outputs them graded as `[FAIL]` / `[STATS]` / `[LOAD]` | `SDS_SelfCheck.cs` |
| Static state registry | `ISDSResettable` registration; `ResetAll` cleans item by item and counts | `SDS_StaticState.cs` |
| Adapter report hub | Carrier for adapter hook point A/B compatibility reports and the single output point | `SDS_CompatReports.cs` |

## 2. Not Responsible For

| Not responsible for | Belongs to |
|---|---|
| Concrete check items (ledger availability, mirror liveness, vault structure, the W63 drop-point hook points) | Core `SDS_CoreDiagnostics`, Vanilla `SDS_VanillaDiagnostics` |
| Actual cleanup actions on static caches | Each module's `Reset` / `ResetStatics` / `ResetChipCache` |
| Patch installation, patch counting, Harmony instances | Vanilla, ThirdModCompat |
| Gameplay rules, category adjudication, debit / dispatch / rollback | Core / Vanilla (this module is only called) |
| Routine work logs | No `Message` facade is provided; normal fallbacks are always silent |

## 3. Dependencies and Artifact

> The single public-surface file: `SDS.Diag_API.cs` (4 facades + `ISDSResettable` + report DTOs, one-line forwarding).
> `SDS_Diag.cs` / `SDS_StaticState.cs` / `SDS_SelfCheck.cs` / `SDS_CompatReports.cs` / `SDS_Invariants.cs` are the corresponding Impl implementation files, all `internal` (enforced by script §4.5).

| Item | Value |
|---|---|
| Project references | Only `SDS.Contracts` (`SDS.Diag.csproj`) |
| Vanilla types | `Assembly-CSharp` / `UnityStubs`, pulled in uniformly by `Directory.Build.props` |
| Harmony | Not referenced; `SdsHarmonyRef` not enabled |
| Artifact | `超维存储/Assemblies/SDS.Diag.dll` |
| Downstream | Core, Api, Vanilla, ThirdModCompat, and Bootstrap reference this module one-way |

## 4. public Entry Points

`SDS_Diag` / `SDS_StaticState` / `ISDSResettable` / `SDS_SelfCheck` / `SDS_CompatReportHub` / `SDS_CompatPatchReport` / `SDS_CompatAdapterReport`.

`ISDS_DiagnosticProvider` is defined in Contracts (`SDS_Host.cs`) and consumed by this module; it is not part of this assembly.

## 5. Iron Rules of This Module

Iron rules and rules are not restated here: the `Fail` caller restriction, no `Message` facade, DTO types, main-thread report writes, `ResetAll` counting, `Conditional` stripping, and one-way dependencies have each been materialized as D1–D13 in Rules.en.md (D4 = iron rule 1, D12 = iron rule 2).

## 6. Document Map

| Document | Content |
|---|---|
| [Interfaces.en.md](Interfaces.en.md) | Public signatures, DTO fields, and the actual call sites of each assembly; vanilla support and third-party support |
| [Rules.en.md](Rules.en.md) | This module's rules D1–D13, rule text, check methods, and known gaps |
| [DesignDecisions.en.md](DesignDecisions.en.md) | Historical decisions and pitfalls of the diagnostics conventions, old → new implementation differences, design trade-offs, TODOs |
| ModuleRules.en.md | The parent-level authoritative rules; reading the rest of this directory is enough to start work |
