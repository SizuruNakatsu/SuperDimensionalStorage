> Positioning: writes the constraints SDS.Bootstrap must obey as checkable items; the rule body is authoritative in ModuleRules.en.md §6.7 and §9 and ModuleResponsibilities.en.md §9; this file does not restate gameplay semantics.

# SDS.Bootstrap Rules

## 1. Checkable items

| ID | Rule | Specific requirement | How to check | Basis |
|---|---|---|---|---|
| BSP-1 | Must not be referenced | No SDS assembly may `ProjectReference` `SDS.Bootstrap` | The reference graph in `CheckAssemblyBoundaries.ps1` §2; a repo-wide search of other `.csproj` files for `ProjectReference` entries containing `SDS.Bootstrap` must yield 0 | ModuleRules.en.md 6.7 |
| BSP-2 | No static reference to ThirdModCompat | The `.csproj` must not reference `SDS.ThirdModCompat`; source code must not contain that assembly's type names outside comments (including `SDS_CompatAdapter_*`) | The same script's reference graph; test `BootstrapContractTests.Bootstrap_ReferencesOnlyAllowedSdsAssemblies`; grepping `SDS.ThirdModCompat` should hit only the comment at `SDS_CompatLoader.cs` 21, and `SDS_CompatAdapter_` must be 0 | ModuleRules.en.md 6.7; ModuleResponsibilities.en.md §9 |
| BSP-3 | No CRUD / patch rules | Does not implement storage/retrieval, capacity, filtering, ledger deduction, sorting, or transactions; declares no patch classes and creates no Harmony instance | Test `Bootstrap_DeclaresNoHarmonyPatchClasses`; grepping `[HarmonyPatch]` / `new Harmony` / `TryDeposit` / `TryWithdraw` / `Reserved` in this assembly must yield 0; cross-module calls are limited to members in the §3 table of [Interfaces.en.md](Interfaces.en.md) | ModuleRules.en.md 6.7 |
| BSP-4 | `SDS_Host.Install` has a single call site | Called only by `SDS_HostInstaller.Install`, which lives in this assembly | `CheckAssemblyBoundaries.ps1` §6 (any occurrence in a non-`SDS.Bootstrap` file fails) | ModuleRules.en.md §5 |
| BSP-5 | Harmony installs Vanilla only | Startup calls only `SDS_PatchInstaller.Install()` / `ApplyRuntimeHooks()`; no scanning of other assemblies | Grepping the call sites should hit only `SDS_Startup.cs` 43 and 86; `new Harmony` in this assembly must be 0; test `Bootstrap_DeclaresNoHarmonyPatchClasses` | ModuleRules.en.md §7 and §9.3 |
| BSP-6 | Adapter deduplication and target gate | Names (Harmony IDs) are unique; duplicates log an Error and skip the later registrant; when `IsTargetLoaded == false`, no `Apply` and no `attempted` write | Manual review of `SDS_CompatLoader.Process` (109–142); grep the `ownerByName` / `IsTargetLoaded` branches; behavioral unit tests (TBD) | ModuleRules.en.md §8; ModuleInterfaces.en.md §1.2 |
| BSP-7 | Re-scan idempotency | Each adapter is `Apply`-ed at most once per process; `attempted` is registered before execution, and exceptions are not retried | Manual review of 143–160; the two `Scan()` call sites (48, 87) must share the same `attempted`; behavioral unit tests (TBD) | ModuleRules.en.md §9.4 |
| BSP-8 | Failures do not block startup | The synchronous / registration / deferred segments each have their own try/catch, logging only `SDS_Diag.ErrorOnce` without rethrowing | Manual review of `SDS_Startup.cs` 53–56, 66–69, 96–98; grepping `throw` in this assembly must yield 0 | ModuleRules.en.md §9; ModuleInterfaces.en.md §1.2 |
| BSP-9 | Settings clamping | `ClampAll` is called at construction, at the end of every draw, in `WriteSettings`, and before applying Defs; out-of-range values must not flow into Core / Vanilla | Grepping `ClampAll()` should hit `SDS_Mod.cs` 29, 137, 320 and `SDS_SettingsApplier.cs` 32; clamping idempotency test (TBD) | ModuleInterfaces.en.md §1.3; DesignDecisions.en.md 3.3 item 4 |
| BSP-10 | Reports are read only from Diag | The startup report uses only Vanilla public counters and the Diag report hub; no references to adapter types and no self-computed A/B | Grep the symbols inside `LogReport`; no `SDS.ThirdModCompat` among assembly references; `SDS_CompatReportHub.LogAll()` is the only per-adapter output | ModuleResponsibilities.en.md §4, §9 |
| BSP-11 | Unique public surface | `SDS_Mod` is the only top-level public type | Test `Bootstrap_ExposesOnlySDSMod`; the type whitelist in `CheckAssemblyBoundaries.ps1` §4 | ModuleResponsibilities.en.md §2 |
| BSP-12 | Single first write of the settings instance | `SDS_Config.Current` is assigned exactly once, in this assembly | `CheckAssemblyBoundaries.ps1` §9 (any non-Bootstrap write fails) | ModuleInterfaces.en.md §1.3 |

## 2. Review criteria

- The normal path writes no logs; every exception goes through `SDS_Diag.ErrorOnce` and execution continues (the once-only semantics of DiagnosticsCoverage §A).
- `CheckAssemblyBoundaries.ps1` covers BSP-1 / BSP-2 / BSP-4 / BSP-11 / BSP-12; tests cover the static facts of BSP-3 / BSP-5 / BSP-11.
- BSP-6 / BSP-7 / BSP-9 currently have only source review and existing contract tests; behavior-level unit tests are not in place and must not be described as "verified" (per the RuleIndex.en.md convention).
- Adding a public type, adding an `SDS_Host` slot, or moving Harmony installation out of Vanilla are all boundary-violating changes and require updating ModuleRules.en.md and this file first.

## 3. Rule text (authoritative)

| ID | Normative-sentence gist | Relation to SDS.Bootstrap |
|---|---|---|
| E1 | Runtime static caches must, and may only, register their cleanup in `SDS_StaticState.ResetAll` | Step 5 `RegisterAll()`; the report outputs the registered item count |
| E2 | Development-time assertions only log Errors and never throw; DEBUG + developer mode only | Step 7 `SDS_SelfCheck.Run(false)` outputs status; this module does not check `Prefs` |
| E6 | The compat layer only allows `SDS_API` / `SDS_Compat*` / `SDS_Diag` and public API types | The scanner depends only on Contracts' `ISDSCompatAdapter` and Diag |
| E7 | Each third-party entry has exactly one takeover write path | The scanner calls `Apply()` exactly once and never retries on exception |
| W63 | Runtime WorkGiver enumeration + destination folding; yields only when a destination cannot be resolved | Step 6 `ApplyRuntimeHooks()` |
| W33 / W34 | Forced virtualization of weapons and apparel; converts existing stock only once, after player confirmation | Settings page confirmation dialog + `ConvertPreservedGearToVirtual()` |

(Source: the corresponding rows in `RuleIndex.en.md`; gameplay semantics are not copied back here.)
