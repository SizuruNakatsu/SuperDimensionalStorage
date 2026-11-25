# SDS.Diag · Design Decisions (Why It Is Done This Way / Pitfalls Encountered)

> Positioning: records why this module "does it this way", the historical decisions, and the pitfalls hit during migration; new code follows `超维存储/Source/SDS.Diag`. For rules and check methods see Rules.en.md; for signatures and call sites see Interfaces.en.md; the sole authority for the diagnostics conventions is DiagnosticsCoverage.en.md.

## 1. Historical Decisions and Pitfalls of the Diagnostics Conventions

DiagnosticsCoverage.en.md is the sole authority for the diagnostics conventions, with the following method:

| Item | Content | Source |
|---|---|---|
| Judgment standard | A genuine-fault log contains at least: module + reason code, runtime context (map / pawn / thing / job / reservation), result semantics (abandon / rolled back / exception), and the exception stack; throttled per reason | §0 |
| Counting scope | `超维存储/Source/**/*.cs`, excluding `obj` / `bin`, non-comment lines only; `SDS_Diag.*` counts call sites only, not definitions; reconciled by `CheckPatchInventory.ps1` §1 | §1 note |
| Grading | Error = data consistency / execution exception; Warning = one-shot self-healing; no output = normal work and fallback | §1 "log level conventions" |
| Classification method | Grouped by entry point into groups A–M (startup / patches, mirrors, behavior, pickup, trade, loading, projection, API, compatibility, static state, etc.); each line is marked "has diagnostics / intentionally silent" with a concrete reason | §2 |
| Silence principle | Gate rejections, adjudication yields, no stock, unreachable, inventory full, no endpoint map, and display-only catches are never logged | §5 |
| Conclusion | The log surface converges to "exceptions + one-shot self-healing + one-shot stats"; the item-by-item reasons for "why no takeover" were left as a debug switch that is off by default, not implemented | §5, §7 |

This module carries only two segments, the "outlet" and "static state / assertions"; the concrete reason codes of each entry point still belong to the individual modules.

## 2. Old → New Implementation: Differences and Pitfalls

| # | Decision / pitfall | Conclusion and source |
|---|---|---|
| 1 | `ErrorOnce` builds its own key instead of calling `Verse.Log.ErrorOnce` directly | The old implementation's comments are explicit: the key must include "message + call site + exception type", so that the same kind of error from different places is not silenced together; the 10th / 100th / 1000th supplemental entries are also needed (`SDS_Mod.cs:932–935, 943–949`). The public facade and the Impl entries must be `NoInlining`, and frame skipping is counted across the two layers "facade + Impl"; any newly added proxy layer must be re-checked in sync (`DiagCallerTests.ErrorOnce_KeyUsesRealCallSite`). The line-by-line differences from `Verse.Log.ErrorOnce(string, int)` regarding "permanent silence / integer-only key" (TODO) |
| 2 | No `SDS_Diag.Message` | Ordinary information needs no dedup, throttling, or context; making it a facade would tempt people to log normal work (`SDS_Mod.cs:984–986`; DiagnosticsCoverage.en.md §1 "why ordinary work logs were removed") |
| 3 | `ResetAll` returns `int` instead of `bool` | The startup log must output "Registered N / Cleaned M / Failed K", which a `bool` cannot express as partial success (old `SDS_StaticState.cs:18–51`; new `SDS_StaticState.cs:41–62`) |
| 4 | Assertion signatures use only Contracts primitive types | Diag cannot see Core's `SDS_WithdrawTransaction`; compressing to `id + stage enum` keeps Core able to call without creating a reverse reference, at the cost of being able to report only the id (`SDS_Invariants.cs:39–67`) |
| 5 | `[Conditional("DEBUG")]` marked in two places | If only the internal implementation were marked, Release would still keep the public call and its argument evaluation; only when both the public facade and the implementation are marked is the call site stripped as a whole (`SDS.Diag_API.cs:44–58`, `SDS_Invariants.cs:32,48,65`) |
| 6 | `Emit*` swallows write exceptions, `Log.*` placed in a `NoInlining` core | Without a host (unit tests / early loading) logging is unavailable; a JIT failure resolving `Assembly-CSharp` must also land inside the `try`, so diagnostics must not in turn interrupt business (`SDS_Diag.cs:185–223`) |
| 7 | Reports are value-copied at `Register` | The old Hub held references to adapter reports; after the split the DTOs must be `public` and Bootstrap must not hold live objects; the read side additionally deep-copies via `Snapshot` (`SDS_CompatReports.cs:50–82`) |
| 8 | The registry is not cleared by `ResetAll` | Re-registration happens only at the next startup; `ResetAll` clears only process-level static caches, not the `SDS_Ledger` / `SDS_VanillaState` instances (`SDS_StaticState.cs:37–40`; ModuleInterfaces.en.md §2.2) |
| 9 | Old 18 items → new dynamic count | The old `ResetAll` directly called 18 static classes; currently it consists of Rules 4 + Vanilla 13 + enabled adapters 2 (CE / Milira), so the startup "Registered N" varies with what is actually enabled (`SDS_StaticState.cs`; ModuleResponsibilities.en.md §4) |
| 10 | The old `SDS_SelfCheck` hardcoded its checks | After the split Diag may not reference Core / Vanilla, so the checks could only be delegated to providers; Diag keeps only collection, grading, and logging (§5) |
| 11 | `SDS_CompatReportHub.Reset()` in the new implementation is called only by tests (the old `SDS_ModCompat.TryApply` called it before every apply) | The new flow's idempotency is guaranteed by `SDS_CompatLoader`'s `attempted` / `Has`, so no production Reset is currently needed; whether it must be cleared on save load is still undecided (`CompatReportTests.cs:38`) (TODO) |
| 12 | `SDS_Diag`'s own caches are not registered in `ResetAll` | `seen` / `fails` / `warned` have no `Reset`; when ticks roll back on save switch, `Fail`'s `nextTick` may stay silent for a long time (`SDS_Diag.cs:26–28,116–137`). Whether to accept this or add registration (TODO) |
| 13 | The old self-check's `MaxReported = 20` | The new implementation pushes truncation down to each provider: Vanilla keeps 20, Core / Rules set no limit; Diag no longer truncates uniformly (`SDS_SelfCheck.cs`; `SDS_VanillaDiagnostics.cs:15`; `SDS_Core.cs` / `SDS_RulesImpl.cs`) |

## 3. Design Trade-offs

> The former "implementation reasons" document has been merged into this section. Per-item "pitfalls" and migration costs are in section 2; this section keeps only the reasons for being a separate assembly and the division of documentation labor.

| Capability | Reason for being separate | Evidence |
|---|---|---|
| Logging facade | Reason codes, dedup keys, throttle windows, and context formats can have only one implementation; copying them into multiple assemblies would give the same fault two formats and two counts | `SDS.Diag/SDS_Diag.cs`; DiagnosticsCoverage.en.md §4 |
| Development-time assertions | Assertions must be callable by every module, yet Diag must not back-reference Core; only a separate assembly can achieve "caller-side stripping + a single implementation" at the same time | `SDS.Diag/SDS_Invariants.cs`; E2 (RuleIndex.en.md) |
| Self-check entry point | The check content is scattered across Core / Vanilla, so the outlet must be unique: the three prefixes failure / stats / load, the failure summary, and a single provider's exception not interrupting the run | `SDS.Diag/SDS_SelfCheck.cs`; DiagnosticsCoverage.en.md §M |
| Static state registry | The old implementation directly called the `Reset` of Core / Vanilla / Compat; after the split Diag may not reference them, so modules can only register `ISDSResettable` | old `SDS_StaticState.cs` lines 22–45; new `SDS.Diag/SDS_StaticState.cs` |
| Adapter report hub | Third-party adapters are allowed to write only to Diag; yet the report types must be readable across assemblies and cannot remain `internal` | ModuleResponsibilities.en.md §4; `SDS.Diag/SDS_CompatReports.cs` |
| Dependency graph | Diag references only Contracts, and every other module points one-way at Diag; if logging stayed in the individual modules, implementations would have to be copied or modules would reference each other — both would break the one-way dependency | ModuleRules.en.md §4 |

`SDS.Diag.csproj` has exactly one project reference, `SDS.Contracts`; `SdsHarmonyRef` is not enabled (`Directory.Build.props` 15, 35–38), hence no reference to `0Harmony`.

- Dependency directions, prohibitions, and the `Fail` caller restriction are defined only in ModuleRules.en.md §4 and §6.2; this section explains reasons only and adds no rules.
- Types and signatures are defined only in ModuleInterfaces.en.md §2.1 and §2.2; for the assertion and self-check conventions DiagnosticsCoverage.en.md §M is the sole authority, and the normative sentences of E1 / E2 are in RuleIndex.en.md.
- Line numbers and test names follow the current source and are synced when the code changes; no second inventory is maintained.

## 4. TODO

TODO: the item-by-item mapping between each provider's check items and DiagnosticsCoverage.en.md §M.

For the remaining scattered open items, see the rows marked "TODO" in the section 2 table.
