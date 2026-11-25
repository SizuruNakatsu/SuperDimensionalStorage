# SDS.Diag Rules

> Positioning: materializes ModuleRules.en.md §6.2 and the related hard constraints into this module's checkable items. Each item gives a source anchor and a check method; items without machine verification are explicitly marked "manual".
> The authority for rule text remains ModuleRules.en.md; this document only expands and cross-checks for this module.

## 1. Rule List

| # | Rule | Source anchor | Check method | Result |
|---|---|---|---|---|
| D1 | Referencing the implementations of Core / Vanilla / ThirdModCompat is forbidden | `SDS.Diag.csproj` has only `ProjectReference SDS.Contracts` | the allowed-reference table at lines 39–45 of `Tools/CheckAssemblyBoundaries.ps1`; out-of-bound references report `[out-of-bound reference]` | Pass |
| D2 | Harmony is forbidden | No `0Harmony` in `SDS.Diag.csproj`; `SdsHarmonyRef` not enabled in `Directory.Build.props` | the Harmony exception table of the same script; `SDS.Tests/Diag/CompatReportTests.DiagAssembly_DoesNotReferenceHarmony` | Pass |
| D3 | Defining gameplay rules is forbidden | No category / capacity / debit / dispatch branches; assertions only read parameters | Manual review (no script; rule text in ModuleRules.en.md §6.2) | Manual |
| D4 | `SDS_Diag.Fail` may be called only by Core / Rules / Vanilla | `SDS_Diag.cs:55`; Api / ThirdModCompat / Bootstrap all have 0 calls | `SDS.Tests/Diag/DiagCallerTests.Fail_OnlyCalledFromImplementationModules`; `SDS.Tests/Compat/CompatAdapterTests.CompatSources_DoNotCallDiagFail` | Pass |
| D5 | Assertions are stripped as whole statements in Release | `SDS_Diag.cs:75,81,88` and `SDS_Invariants.cs:32,48,65` all carry `[Conditional("DEBUG")]` | Compile-time attribute (no script); `InvariantTests` verifies no throw under DEBUG and that `StatusText` contains E2 | Partially machine-checked |
| D6 | Assertions only log Error, never throw; and take effect only in developer mode | `SDS_Invariants.cs:33–45,49–62,66–78` | `SDS.Core.Tests/Core/InvariantTests.Invariants_IllegalStates_DoNotThrow` | Pass |
| D7 | `ResetAll` continues after a per-item failure and returns the count of successfully cleaned items | `SDS_StaticState.cs:47–68` | `SDS.Tests/Diag/StaticStateTests.ResetAll_CountsSuccesses_AndContinuesAfterException` | Pass |
| D8 | `Register` ignores null and duplicate instances | `SDS_StaticState.cs:27–41` | `StaticStateTests.Register_IgnoresNullAndDuplicates` | Pass |
| D9 | Registration points and item counts: Rules 4 / Vanilla 13 / CE and Milira 1 each | the `RegisterAll` of `SDS_RulesImpl.cs`; `SDS_Vanilla.cs:40–52`; both adapters call `Register(this)` after `Apply` succeeds | source grep counting; `VanillaContractTests.Vanilla_RegisterAll_IsIdempotent`; `CompatAdapterTests.CeAndMilira_DeclareStaticCleanup` | Pass |
| D10 | Reports may be written only on the main thread, and Bootstrap reads snapshots | The Hub's internal `List` is lock-free; the write side is only adapter `Apply`; Bootstrap calls `LogAll` directly on the main thread (use the `Snapshot` deep copy when data is needed) | Manual (thread constraint); `CompatReportTests.Hub_SnapshotIsDeepCopy` verifies the deep copy | Manual + test |
| D11 | Report DTOs contain no Harmony types | DTOs in `SDS_CompatReports.cs` contain only primitive types | `CompatReportTests.ReportDtos_OnlyPrimitiveAndStringListMembers` | Pass |
| D12 | There is no `SDS_Diag.Message` | No `public static void Message` in `SDS_Diag.cs` | `DiagCallerTests.Diag_HasNoMessageFacade` | Pass |
| D13 | Top-level public is limited to the 7-type whitelist | `SDS_Diag`, `SDS_StaticState`, `ISDSResettable`, `SDS_SelfCheck`, `SDS_CompatReportHub`, and the two DTOs | the public whitelist at lines 94–97 of `CheckAssemblyBoundaries.ps1`; out-of-bound leaks report `[public leak]` | Pass |

## 2. Why Diag Does Not Register Its Own State

The `seen` / `fails` / `warned` of `SDS_Diag` are process-level diagnostic caches; they are not in the `SDS_StaticState` registry and have no external `Reset`. This is in tension with E1, "static caches that must not survive across saves must be registered":

| Point | Fact | Source |
|---|---|---|
| Dedup tables | `seen` / `fails` / `warned` only grow and never shrink | `SDS_Diag.cs:24–26` |
| Save-switch impact | `fails[reason].nextTick` is based on `GenTicks.TicksGame`; when switching to an older save the tick rolls back, which may cause long silence | `SDS_Diag.cs:139–151`; the comment in the old `SDS_StaticState.cs:34–39` |
| Current registration | No `ISDSResettable` implementation, no `Reset` | `SDS_StaticState.cs`, `SDS_Diag.cs` |

Whether these three tables should be brought into `ResetAll` is an open item; this document records only the current state and does not codify an undecided item as a rule (TODO).

## 3. Check Commands

| Purpose | Command / test |
|---|---|
| Reference graph, Harmony, public whitelist | `pwsh 超维存储/Source/Tools/CheckAssemblyBoundaries.ps1` |
| `Fail` callers | `SDS.Tests/Diag/DiagCallerTests`, `SDS.Tests/Compat/CompatAdapterTests` |
| Static cleanup | `SDS.Tests/Diag/StaticStateTests` |
| Report DTOs / snapshot / Harmony | `SDS.Tests/Diag/CompatReportTests` |
| E2 assertions | `SDS.Core.Tests/Core/InvariantTests` |

## 4. Known Gaps

| Gap | Explanation |
|---|---|
| `CheckCompatSurface.ps1` does not actually scan `SDS_Diag.Fail` | ModuleRules.en.md §10 lists it as a compatibility-surface check item, but the script has no symbol rule for it; two tests currently serve as the backstop (TODO: add a script) |
| Release stripping has no build-level verification | Currently relies on the `[Conditional]` attribute and manual confirmation; no script or IL check (TODO) |
| Registered item counts have no script reconciliation | Rules 4 / Vanilla 13 / adapters 2 are guaranteed indirectly by source counting and tests; no independent reconciliation script (TODO) |
| Main-thread writes have no runtime assertion | Relies on the calling convention of ModuleResponsibilities.en.md §4; the Hub neither locks nor detects threads (TODO) |

## 5. Rule Text (Authoritative)

> The following keeps only the normative sentences not yet covered by D1–D13 above and directly relevant to this module; the full text and the machine-verification columns are in RuleIndex.en.md, and D1–D13 are the check landing points of these normative sentences.

| ID | Normative sentence | Relation to this module | Current anchor |
|---|---|---|---|
| E6 | The compatibility layer may reference only `SDS_API`, `SDS_Compat*`, `SDS_Diag`, and public API types | Third parties can write reports / log only via Diag | `SDS_CompatReports.cs` |
| E7 | Each third-party entry point has exactly one write-takeover path | The report hub does not participate in patch logic, it only aggregates | `SDS_CompatReportHub` |
| R15 / W22 | Trade / donation reads "physical stock − transaction reservations"; availability = `max(0, physical − reservations)` | The availability convention verified by the self-check and the assertions | `SDS_Diag.AssertAvailableWithinTotal` |
| W21 | The full-amount convention during transient transaction execution is fixed at the entry point | Transaction stage assertions | `AssertTransactionStage` / `AssertTransactionTerminal` |
