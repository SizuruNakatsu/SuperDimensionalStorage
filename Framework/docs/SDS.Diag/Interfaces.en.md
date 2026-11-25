# SDS.Diag Interfaces

> Positioning: this document only registers the consumption surface and call facts of this module; the sole authority for signatures and member semantics is `SDS.Diag_API.cs`. For rules see Rules.en.md; for historical trade-offs see DesignDecisions.en.md.
> Source paths are relative to `超维存储/Source/`; line numbers follow the current source.

## 1. Public Contract (Section A)

### 1.1 Public methods of `SDS_Diag`

The sole authority for signatures and member semantics: `Source/SDS.Diag/SDS_Diag_API.cs` (XML comments). The table below merges "allowed callers" and "implementation conventions".

| Method | Allowed callers | Key / throttle and semantics | Lands in | Source |
|---|---|---|---|---|
| `ErrorOnce` | All layers (including ThirdModCompat) | key = message + **real call site** + full exception type name; the same combination is fully logged only once, with one supplemental Warning at the 10th / 100th / 1000th occurrence | `EmitError` | lines 30–55 |
| `Warn` | All layers (including ThirdModCompat) | Self-healing exceptional situations; once per reason per process (`HashSet`), no time-based throttling | `EmitWarning` | lines 68–77 |
| `Fail` | Core / Rules / Vanilla; ThirdModCompat must not call | Behavioral fallback (Error level); per reason, one entry per 600 ticks; supplemental entries at the 10th / 100th / 1000th occurrence | `EmitError` | lines 57–66, 139–164 |
| `Ctx` | All layers, called only in failure branches | Outputs `map= pawn= thing= job= extra` | — | lines 80–114 |
| `AssertAvailableWithinTotal` | Core / Vanilla; `[Conditional("DEBUG")]` | Logs Error only, never throws; implementation in internal `SDS_Invariants` | `SDS_Invariants` | line 32 |
| `AssertTransactionStage` | Same as above | Transaction stage assertion | `SDS_Invariants` | line 48 |
| `AssertTransactionTerminal` | Same as above | Transaction terminal-state assertion | `SDS_Invariants` | line 65 |
| `EmitError` / `EmitWarning` / `EmitInfo` | `internal` | `try` swallows write exceptions; `Log.*` placed inside `NoInlining` core methods | `Verse.Log` | lines 189–223 |

There is no `SDS_Diag.Message`; ordinary information goes through `Log.Message` directly. The actual implementation of E2 lives in internal `SDS_Invariants`.

### 1.2 `ISDSResettable` / `SDS_StaticState`

The sole authority for signatures and member semantics: `SDS.Diag_API.cs`. `Register` ignores null values and duplicate instances; `ResetAll` wraps each item in try/catch and returns the number of items cleaned successfully.

Semantics of `ResetAll` (`SDS_StaticState.cs` 47–68):

| Point | Behavior |
|---|---|
| Snapshot | `items.ToArray()` first; additions / removals during cleanup do not affect the current round |
| Per-item exception | Logs `SDS_Diag.ErrorOnce("Runtime static state cleanup failed (skipped)", e)` and continues with the next item |
| Return value | The number of items whose `ResetStatics` did not throw |
| Registry | Not cleared; re-registration happens only at the next startup |
| Log | Outputs `[SuperDimensional Storage][State] Registered N / Cleaned M / Failed K` every time |

### 1.3 Public methods of `SDS_SelfCheck`

The sole authority for signatures: `SDS.Diag_API.cs`.

| Method | Semantics / order |
|---|---|
| `Count` | Number of registered providers |
| `Register` | Null values and duplicate instances are ignored |
| `Reset` | Clears the provider table (currently used only by tests) |
| `RunLedgerChecks` | Developer menu entry, equivalent to `Run(true)` |
| `Run` | First outputs the E2 status line; failure lines are always Error; stats lines are output only when `verbose` or when there are no failures; all other lines are output only when `verbose` |
| `RunLoadReport` | Outputs only the lines providers mark as `[LOAD]`; the implementation first runs `Collect()` over all providers, and the `[LOAD]` filter affects output only — it does not reduce the checks performed |
| `Collect` (internal) | Calls providers one by one; a single exception is logged via `ErrorOnce` and collection continues; no longer part of the public surface since 2026-11-14 |

Provider line conventions (`SDS_SelfCheck.cs` 7–11):

| Prefix | Semantics | Output level |
|---|---|---|
| `[FAIL]` | Check failed | `EmitError` |
| `[STATS]` | Stats line | `EmitInfo`; omitted when `verbose=false` and there are no failures |
| `[LOAD]` | Count report on save entry | Output only by `RunLoadReport`, prefix stripped before `EmitInfo` |
| Others | Ordinary information | `EmitInfo`; output only when `verbose` |

### 1.4 Report hub and DTOs

The sole authority for signatures, fields, and semantics: `Source/SDS.Diag/SDS_Diag_API.cs`.

Constraints: DTOs contain only `string` / `int` / `bool` / `List<string>`; `Register` copies values; `Snapshot` deep-copies, so callers mutating the copy do not affect the hub. `SDS_CompatPatchReport.Missing(hook, reason)` records "`hook → reason`".

## 2. Callers (Section B, verified by grep)

Consumed Contracts: `ISDS_DiagnosticProvider` (definition), `SDS_StockKey` / `SDS_WithdrawEndState` (assertion parameters).

Counting scope: `超维存储/Source/**/*.cs`, excluding `obj` / `bin`, `SDS.Diag` itself, and tests; non-comment lines only. The counts in the table below are a grep snapshot of 2026-11-15 (to be re-measured once the C1 reclaim ledger moves into Vanilla; the P3–P5 flow-layer / adjudication-layer files have moved to `SDS.Rules`, and the Core / Rules distribution is explained below); the script output is authoritative.

| Assembly | Called content | Call lines | Representative locations |
|---|---|---|---|
| Contracts | Defines `ISDS_DiagnosticProvider`; does not reference Diag | 1 definition | `SDS_Host.cs:119` |
| Core | Ledger-side diagnostics (`Fail` / `Warn` / `ErrorOnce` / `Ctx`); `StaticState.Register` 0; `SelfCheck.Register` 1 | ledger self-check | `SDS_Core.cs`, `SDS_Ledger.cs` |
| Rules | Adjudication-layer / flow-layer diagnostics (`Fail` / `Warn` / `ErrorOnce` / `Ctx`); `StaticState.Register` 4; `SelfCheck.Register` 1 | to be re-measured | `SDS_WithdrawTransaction.cs`, `SDS_WithdrawExecutor.cs`, `SDS_ReturnUtility.cs`, `SDS_ProducedThingMarker.cs`, `SDS_StockRules.cs`, `SDS_Availability.cs` |
| Vanilla | `Fail` 49, `Warn` 18, `ErrorOnce` 130, `Ctx` 65; `StaticState.Register` 14; `ResetAll` 1; `SelfCheck.Register` 1; `SelfCheck.Run` 2; `RunLoadReport` 1 | 281 | `SDS_Vanilla.cs:44–60`, `SDS_GameComponent_Grid.cs`; `SDS_ReclaimRegistry.cs`, `SDS_Mirrors.cs`, `SDS_Buildings.cs` |
| Api | `ErrorOnce` 25; does not call `Fail` / `Warn` / assertions | 25 | `SDS_API.cs`, `SDS_API_Adapters.cs` |
| ThirdModCompat | `ErrorOnce` 60; `CompatReportHub.Register` 26; `StaticState.Register` 2; implements `ISDSResettable` 2 | 90 | `SDS_ModCompat_CE.cs`, `SDS_CompatAdapter_CE.cs:52`, `SDS_CompatAdapter_MiliraExpanded.cs:52` |
| Bootstrap | `ErrorOnce` 8; `SelfCheck.Run(false)` 1; `SelfCheck.Count` 2; `StaticState.RegisteredCount` 2; `CompatReportHub.LogAll` 1, `Has` 1, `Register` 1 | 16 | `SDS_Startup.cs:64,103,106,111–112`, `SDS_CompatLoader.cs:166,170` |

Supplementary facts:

- `SDS_SelfCheck.Collect` (internal) is called only by `Run` / `RunLoadReport` in the same file; `RunLedgerChecks` serves only as the developer menu entry; `Reset` is called only by tests.
- `SDS_CompatReportHub.Snapshot` currently has only test callers; the production read side is Bootstrap's `LogAll`.
- Implementations of `ISDSResettable`: Core's `SDS_ResetAction`, Vanilla's private `ResetAction`, and the CE / Milira adapters.
- Implementations of `ISDS_DiagnosticProvider`: Core `SDS_CoreDiagnostics`, Vanilla `SDS_VanillaDiagnostics`.
- `SDS_SelfCheck.Register` is called exactly once each in `SDS_Core.RegisterAll` and `SDS_Vanilla.RegisterAll`.

## 3. Call Relationships

```text
Bootstrap ──(read counts / LogAll / Run(false))──→ SDS.Diag
    │
Core ──(Fail / Warn / ErrorOnce / Ctx / Assert* / Register)──→ SDS.Diag
    │
Vanilla ──(Fail / Warn / ErrorOnce / Ctx / Register / ResetAll / Run)──→ SDS.Diag
    │
Api ──(ErrorOnce)──→ SDS.Diag
    │
ThirdModCompat ──(ErrorOnce / ReportHub.Register / StaticState.Register)──→ SDS.Diag
Contracts ──(defines ISDS_DiagnosticProvider)──→ consumed by SDS_SelfCheck
```

Startup-phase registration order: at step 5 Bootstrap calls `SDS_Core.RegisterAll()` → `SDS_Vanilla.RegisterAll()`, then reads `SDS_StaticState.RegisteredCount` and `SDS_SelfCheck.Count`; at step 7 it calls `SDS_CompatReportHub.LogAll()` → `SDS_SelfCheck.Run(false)`. Save-load phase order: `SDS_GameComponent_Grid` construction → `ResetAll`; `FinalizeInit` → `RunLoadReport` (plus `Run(false)` in developer mode).

During startup there is no `SDS_GameComponent_Grid`: `SDS_VanillaDiagnostics` still runs the map-independent W63 first, then skips the mirror / vault / load-report parts because the grid is missing; Core's W35 and the E2 status are output as usual.

No reverse calls: Diag's assembly references include only Contracts; it references no `SDS.*` implementation.

## 4. Vanilla Support

> Source paths are relative to `超维存储/Source/`. For rules and signatures see Rules.en.md and ModuleInterfaces.en.md; this section records only interaction facts.

### 4.1 Interaction surface with the vanilla API

| Vanilla API | Purpose | Location | Effective condition |
|---|---|---|---|
| `Verse.Log.Error` | Where `ErrorOnce` / `Fail` land | `SDS_Diag.EmitError` → `EmitErrorCore` | Always (write failures are swallowed) |
| `Verse.Log.Warning` | `Warn`, duplicate-error milestones, the self-check failure summary | `SDS_Diag.EmitWarning` | Always |
| `Verse.Log.Message` | Self-check, load report, static-cleanup status lines | `SDS_Diag.EmitInfo` | Always |
| `Prefs.DevMode` | Runtime switch for assertions | the three implementations in `SDS.Diag/SDS_Invariants.cs` | Inside DEBUG builds |
| `Prefs.DevMode` | Runs one full read-only self-check after a save load | `SDS.Vanilla/SDS_GameComponent_Grid.cs` 66–79 | After entering a save and the map has been generated |
| `[Conditional("DEBUG")]` | Strips assertion call sites | `SDS_Diag.Assert*`, `SDS_Invariants.*` | Release builds |
| `GenTicks.TicksGame` | The 600-tick throttle clock of `Fail` | `SDS_Diag.ShouldLogFail` | Always |
| `LudeonTK.DebugAction` | Manual self-check entry in the developer menu | `SDS_SelfCheck.RunLedgerChecks` | Developer mode |
| `GameComponent` constructor | Triggers `SDS_StaticState.ResetAll` | `SDS.Vanilla/SDS_GameComponent_Grid.cs` lines 24–30 | New save / save load |
| `GameComponent.FinalizeInit` | One-time count report after entering a save | same file, lines 41–47 | After entering a save |

Contracts does not reference Diag, so the Contracts side uses the vanilla `Log.ErrorOnce(string, int fixed key)` directly: the 11 host null checks (`SDS.Contracts/SDS_Host.cs` 179–189), missing JobDef (`SDS.Contracts/SDS_CaravanTransport.cs` 20–48), missing HediffDef (`SDS.Contracts/SDS_Implants.cs` 19–33), and preserved-channel probe exceptions (`SDS.Contracts/SDS_PreserveHooks.cs` 51).

### 4.2 `[Conditional("DEBUG")]` and developer mode

- The conditional attribute is placed both on the three public assertion entries of `SDS_Diag` and on the three implementations in internal `SDS_Invariants`; in Release the entire call-site statement (including argument evaluation) is removed — it is not "the implementation body is empty" (`SDS.Diag/SDS_Diag.cs` 74–93; `SDS_Invariants.cs` 32 / 48 / 65). The public entries guarantee caller-side stripping, and the implementation side guarantees that a standalone internal call is likewise stripped.
- The three assertion implementations check `Prefs.DevMode` at the start and return directly when it is off, without building the message string (`SDS_Invariants.cs` 32–78).
- `SDS_SelfCheck.Run` unconditionally outputs the E2 status line first; `SDS_Invariants.StatusText` uses `#if DEBUG` + `Prefs.DevMode` to give three states: active / DEBUG but developer mode off / Release not compiled (the `#else` branch).
- Behavioral difference from the old implementation: the old `ResetAll` printed the cleanup line only in developer mode, while the new implementation outputs "Registered N / Cleaned M / Failed K" on every `ResetAll`, making it easy to verify the cleaned item count from player reports (`SDS.Diag/SDS_StaticState.cs` 66).

### 4.3 GameComponent timing

- The vanilla `Game.FillComponents` constructs components via `Activator.CreateInstance(type, this)`, so the constructor must keep the `(Game)` parameter (`SDS.Vanilla/SDS_GameComponent_Grid.cs` 20–22).
- The construction order is fixed: `ResetAll` → `SDS_Core.CreateLedger()` → `new SDS_VanillaState()` → `Instance = this` → `SDS_Core.Attach` (constructor of the same file).
- Timing constraint: at construction time `Find.Maps` may be empty, so `ResetAll` may only clear process-level static caches and must not depend on the map or the save; `ExposeData` only serializes content and does not rebuild the ledger as a fallback (same file, 32–39).
- The count report is deferred: `FinalizeInit` only sets a pending flag, and the output really happens on the first non-empty tick (same file, 41–79).
- The `GenTicks.TicksGame` clock of `Fail` rolls back when switching to an older save; static caches must be cleared before switching saves, see Rules.en.md §2; `ResetAll` clears only static caches, not the current save's `SDS_Ledger` / `SDS_VanillaState` (ModuleInterfaces.en.md §2.2).

### 4.4 Ownership of Harmony and patch counting

Diag installs no patches and does not reference Harmony (D2); patch counts are provided by Vanilla's `SDS_PatchInstaller`, the mirror guard / projection owner bridge registries, and each of the 9 adapters on their own; Diag only provides the `SDS_CompatPatchReport` carrier and the `SDS_CompatReportHub.LogAll` outlet, counts no patches, and creates no Harmony instances.

### 4.5 TODO

- Whether the runtime `DeclaredCount` equals the 112 in ModuleResponsibilities.en.md §6 (needs verification against a real startup report).
- Visibility of the developer menu entries when developer mode is off (needs in-game confirmation).

## 5. Third-Party Support

> For rules and signatures see Rules.en.md §6.6 and §8, and ModuleInterfaces.en.md §2, §4, §8; this section does not restate them.

### 5.1 The contract surface Diag exposes to third parties

| Type | Role | What an adapter may / may not do |
|---|---|---|
| `SDS_CompatPatchReport` | The hook point A/B record of a single adapter | May `Applied` / `Missing`; not responsible for output |
| `SDS_CompatAdapterReport` | Read-only snapshot of a single adapter (value copy) | Read-only; holds no internal list |
| `SDS_CompatReportHub` | The sole registration and output outlet | May `Register` / `Has`; `LogAll` is called by Bootstrap |
| `SDS_StaticState` / `ISDSResettable` | Registry for runtime static-cache cleanup | May `Register` |
| `SDS_Diag.ErrorOnce` / `Warn` | Exceptions and one-shot self-healing | May call |
| `SDS_Diag.Fail` | Data-consistency Error | Must not call |

### 5.2 How adapters write results into the report hub

Taking Combat Extended as an example (`SDS.ThirdModCompat/SDS_ModCompat_CE.cs` 130–208):

1. Called but key types resolve incompletely: `Register(name, packageId, detected, null, null, reason)`, i.e. `report = null` + `detectedButBroken`; the startup report outputs `A/B = 0/0` with the reason. When the target mod is not installed, `Apply` is never called at all, so nothing is registered and everything stays silent (`SDS.Bootstrap/SDS_CompatLoader.cs` 138–142).
2. Target installed: create `SDS_CompatPatchReport(adapter name)`, and for each hook point record `Applied(hook point name)` or `Missing(hook point name, reason)` via `SDS_CompatTools.TryPatch` (`SDS.ThirdModCompat/SDS_CompatServices.cs` 84–124).
3. Wrap-up: `SDS_CompatReportHub.Register(name, packageId, true, report, remark)`.
4. Overall exception: inside the catch, call `Register(..., null, ..., "Apply exception XxxException")` again plus `SDS_Diag.ErrorOnce` (same file, 203–208).

Output is decided solely by `LogAll`: undetected adapters are completely silent; detected ones output one A/B line (`【category】name`). `declaredCount == 0` then splits into two cases: `report == null` → Warning + `detectedButBroken` / "key members missing"; `report != null` but 0 hook points → a neutral Message (an empty report does not mean broken). When there are failed hook points, one detail Warning is appended, carrying `【category】name`, the failed list, and the applied list (`SDS.Diag/SDS_CompatReports.cs`).

If an adapter throws directly inside `Apply` without having registered yet, Bootstrap's `RegisterBrokenReport` adds a 0/0 record so the startup report does not miss entries (`SDS.Bootstrap/SDS_CompatLoader.cs` 155–171).

### 5.3 Why Bootstrap reads A/B only from Diag

- Basis: ModuleRules.en.md §6.7 forbids Bootstrap from statically referencing ThirdModCompat, relying on reflection scanning instead; ModuleResponsibilities.en.md §9 stipulates that adapter A/B results are read from Diag.
- Implementation: `SDS_CompatLoader` enumerates `ISDSCompatAdapter` implementations via reflection and validates name uniqueness (duplicates get an `ErrorOnce` and the later registrant is skipped); `Apply` runs only when the target is loaded; the report is written into the Hub by the adapter itself.
- Bootstrap does only two things: read its own scan counts (`AppliedCount` / `KnownCount`) and call `SDS_CompatReportHub.LogAll()` (`SDS.Bootstrap/SDS_Startup.cs` 101–114). `SDS.ThirdModCompat` is not among its compile references; when no adapter at all is installed the startup path is unchanged, and Diag is the only data channel.

### 5.4 How adapters register when resetting static caches

| Adapter | Registered items | Registration timing | Cleaned content |
|---|---:|---|---|
| `SDS_CompatAdapter_CE` | 1 | `SDS_StaticState.Register(this)` after `Apply` succeeds | Forwards to `SDS_ModCompat_CE.Reset()` |
| `SDS_CompatAdapter_MiliraExpanded` | 1 | Same as above | Forwards to `SDS_ModCompat_MiliraExpanded.Reset()` |
| The other 6 adapters | 0 | — | No cross-save static caches (TODO: verify one by one) |

- The two adapter classes implement `ISDSResettable` directly, and `ResetStatics` only forwards (`SDS.ThirdModCompat/SDS_CompatAdapter_CE.cs` 9–58, `SDS_CompatAdapter_MiliraExpanded.cs` 9–58); the test `SDS.Tests/Compat/CompatAdapterTests` asserts that both are assignable to `ISDSResettable`.
- Registration happens after `Apply` succeeds, not at class construction; if the target is not installed there is no `Apply` and no registration, so the total registered count varies with the adapters actually enabled (`SDS.Bootstrap/SDS_CompatLoader.cs` 120–160).
- What is registered is the process-level short-lived cache (reload cooldown and feed throttle keyed by ID / absolute tick), not the current save's ledger instance; item ownership is in ModuleResponsibilities.en.md §4.

### 5.5 The `Fail` prohibition surface

- Semantic difference: `Fail` is the data-consistency / execution-exception level, carries a 600-tick throttle, and goes through the Error channel. A missing third-party hook point, a renamed target type, or a signature mismatch only means "no takeover"; it is not a data error of this mod, and writing an Error would pollute real fault localization.
- Alternative ways to report:
  - Missing hook point / signature mismatch / apply exception → `SDS_CompatPatchReport.Missing`, finally summarized into one Warning;
  - Adapter runtime exception → `SDS_Diag.ErrorOnce` (the same call site + exception type is fully logged only once);
  - One-shot self-healing situations → `SDS_Diag.Warn` (once per reason per process).
- Verification: see Rules.en.md D4 and the §3 check commands (`DiagCallerTests.Fail_OnlyCalledFromImplementationModules`, `CompatAdapterTests.CompatSources_DoNotCallDiagFail`).
- Visibility consequence: third parties can express "not in effect" only through the A/B line and the per-hook-point Warning list, and cannot escalate "cannot take over" into an Error. Stay silent when the target mod is not installed; when it is installed but cannot be detected, a Warning is mandatory.

### 5.6 Threads and lifecycle

- Reports are written only on the main thread: `Apply` is called by Bootstrap on the main thread; the Hub's internal `List` is lock-free, and the write side relies on this constraint for safety.
- The read side does not hold internal lists: the production path is Bootstrap calling `LogAll()` to output in place; when data is needed, use the `Snapshot()` deep copy.
- `SDS_CompatReportHub.Reset()` is currently called only in tests; `Apply` is kept in-process idempotent by the adapter classes' static `applied`, so reports do not accumulate repeatedly (`SDS.ThirdModCompat/SDS_CompatAdapter_CE.cs` 16, 44–53; `SDS.Tests/Diag/CompatReportTests.cs` 38).
- The adapters' `applied` is a static bool that is not reset across saves, so adapters do not participate in "re-Apply on save switch", only in static-cache cleanup (TODO: whether a reset on save load is needed).

### 5.7 TODO

- Whether the other 6 adapters truly have no cross-save static caches (verify one by one).
- A unified convention for the `remark` wording of each adapter.
