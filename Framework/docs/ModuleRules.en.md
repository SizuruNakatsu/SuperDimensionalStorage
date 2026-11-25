# Module Rules

> Positioning: this document is the **single global page** — it records only the cross-module constraints and flows that cannot be split apart; each module's own prohibitions, save rules, patch details, and interface signatures all live inside the `SDS.*/` directories, so maintaining a single module only requires reading that directory + this document.
> `旧框架/` has been a **pure historical archive** since 2026-11-12: the validation data (rule index / patch inventory / public API / diagnostics coverage) has moved into this framework, and the validation scripts, implementation, and review never read the legacy framework. The sole exception is **save-compat forensics**: when verifying historical `ExposeData` node names / field aliases / LookMode you may consult it manually, but it must never be used as a design basis and must never be modified.

## 1. Design Principles

The design principles are in [DesignPrinciples.en.md](DesignPrinciples.en.md); this document only references them, never restates them. If this document conflicts with them, they win.

Corollary of Principle 3 for documentation: a given rule may be defined in exactly one document, and all other documents only reference it. Therefore this document keeps only cross-module rules, and module-specific rules are always pushed down into the corresponding module's `Rules.en.md`.

## 2. Module Independence Rules

1. **Every module is an independent project, written independently, compiled independently.** A module is complete solely by the interfaces / data in `SDS.Contracts`.
2. Modules may communicate only through the DTOs and interfaces of `SDS.Contracts`; referencing other modules' `internal` classes, implementation classes, or static state is forbidden.
3. Dependencies are strictly one-way; `SDS.Vanilla` and `SDS.ThirdModCompat` never reference each other; adapters share only the Sdk utility classes and never call each other.
4. Each implementation module makes only the types required by cross-module contracts `public`; everything else stays `internal` (whitelist in ModuleResponsibilities.en.md §2).
5. **Docs are self-contained**: every `SDS.*/` directory must let a maintainer modify that module without reading any other module's **implementation**; for cross-module contracts, the other side's `*_API.cs` is the authority on signatures and semantics, and this module's `Interfaces.en.md` records only the **consumed surface** (members actually consumed and the guarantees relied upon) + the **vanilla / third-party contact surface** — signatures are never copied.
6. Modules never depend on implementation order: the acceptance criterion is "this module's docs + independent compilation + independent testing".

## 3. Hard Constraints

- Old saves must remain readable in new versions; avoiding data loss outranks keeping the format unchanged (W26).
- All 112 feature patch classes + 29 registry entries must be preserved; patches exist only in Vanilla / ThirdModCompat.
- Quantity conservation: virtual + preserved + on-map physical things + container physical things are never lost or duplicated.
- Runtime static caches must be cleaned through the `SDS.Diag` registration system (E1).
- The compat layer goes only through `SDS.Api` / `SDS.Contracts` / `SDS.Diag` (E6); each third-party entry point has exactly one write-takeover path (E7).

## 4. Dependency Direction

The matrix below is exactly the compile-time-allowed `ProjectReference` set; any violation fails `CheckAssemblyBoundaries.ps1` immediately. Artifact names are in ModuleResponsibilities.en.md §1.

| Module | Allowed references only |
|---|---|
| `SDS.Contracts` | RimWorld / Verse / UnityEngine data types |
| `SDS.Diag` | Contracts |
| `SDS.Core` | Contracts, Diag, data types |
| `SDS.Rules` | Contracts, Diag, Core, data types |
| `SDS.Api` | Contracts, Diag, Core, Rules |
| `SDS.Vanilla` | Contracts, Diag, Core, Rules, Harmony |
| `SDS.ThirdModCompat` | Contracts, Diag, Api, Harmony; target mods are only probed via reflection |
| `SDS.Bootstrap` | Contracts, Diag, Core, Rules, Vanilla, Api |
| `SDS.Tests` | all modules + Harmony (tests only, never in production output) |
| `SDS.Core.Tests` | Contracts, Diag, Core (tests only, never in production output) |
| `SDS.Rules.Tests` | Contracts, Diag, Core, Rules + Harmony (tests only, never in production output) |

```text
SDS.Bootstrap
  ├─→ SDS.Vanilla ──┬─→ SDS.Rules ─→ SDS.Core ─→ SDS.Contracts
  ├─→ SDS.Api ──────┤
  ├─→ SDS.Core ─────┤
  └─→ SDS.Diag ─────┴─→ SDS.Contracts

SDS.ThirdModCompat → SDS.Api + SDS.Contracts + SDS.Diag (+ Harmony)
                ↑
        target mods are only probed via reflection, never statically referenced
```

Naming rules:

- Assembly file name = project directory name = `SDS.*`; all type namespaces stay `SuperDimensionalStorage`.
- No new sub-namespaces; layering is enforced by assembly boundaries, not namespaces.
- `超维存储/Assemblies` must not keep the old `SuperDimensionalStorage.dll`, to avoid type duplication and `GenTypes` ambiguity.

## 5. Visibility and Friends

- Top-level `public` types not on the ModuleResponsibilities.en.md §2 whitelist are treated as leaks; `CheckAssemblyBoundaries.ps1` enforces this at type level.
- **E8 public-surface registration**: for registered modules, top-level `public` types may be declared only in that module's registered public-surface file. The registry is maintained solely in `CheckAssemblyBoundaries.ps1`'s `$apiSurfaceFiles`; this document does not duplicate the module list. All other implementation files are `internal`.
- **E8 dual whitelist**: `SDS.Vanilla`'s public types split into two classes — the **inter-module surface** (`SDS_Vanilla`, only in `SDS.Vanilla_API.cs`) and the **engine surface** (instantiated by XML by name / saved by Scribe by type; list in ModuleResponsibilities.en.md §2). The engine surface may be scattered, but **other modules must never reference it** (script §4.7).
- **E9 public-surface files carry no domain decisions**: `*_API.cs` only makes contract declarations and one-line forwards, never rules; control flow (`if` / `else` / `for` / `foreach` / `while` / `switch` / `try` / `catch`) is allowed only in the outward gate `SDS_API` — the sole registered exception.
- Vanilla never exposes implementation to Core / Api; Core does not reference Vanilla.
- `SDS_Ledger` never exposes `PreservedOwner`; preserved true bodies are held by the Core-private owner, and Vanilla must never access them.
- `SDS.Tests` / `SDS.Core.Tests` / `SDS.Rules.Tests` are the test exceptions: `SDS.Tests` may reference all modules and `0Harmony`; `SDS.Core.Tests` references only Contracts / Diag / Core; `SDS.Rules.Tests` references only Contracts / Diag / Core / Rules and `0Harmony` (from P5 on, all rules / flow tests live here); all three go into test output only.
- Caller restrictions on `SDS_Diag.Fail` are in ModuleInterfaces.en.md §2 and each module's `Rules.en.md`; the whitelist does not relax this restriction.

The `InternalsVisibleTo` whitelist (registration-based; new entries must be registered here, and the script fails immediately on unregistered items):

| Grantor | Grantee | Purpose |
|---|---|---|
| `SDS.Core` | `SDS.Rules` | the rules / flow module reads and writes ledger write interfaces and derived indexes (from P3 on) |
| `SDS.ThirdModCompat` | `SDS.Tests` | the assembly's public surface is 0 (adapters and Sdk utility classes are all internal), so tests reference internal types directly |
| `SDS.Vanilla` | `SDS.Tests` | host implementations and patch counters are all internal, so tests reference internal types directly |

> P3 landed the `SDS.Core → SDS.Rules` grant (`SDS.Core/Properties/AssemblyInfo.cs`); P4–P5 continue to use that friend grant and add no others.

The only place allowed to touch Core state assembly across modules is the `SDS_GameComponent_Grid` constructor (`SDS_Core.CreateLedger()` + `SDS_Core.Attach`). Reason: the ledger's lifetime hangs off the `Game` component, and Bootstrap runs before any `Game` exists. `ExposeData` only serializes content and must never rebuild the ledger as a fallback.

`SDS_Host.Install` may be called only by `SDS.Bootstrap`; calls from any other module are script errors.

## 6. Per-Module Prohibitions (Index)

Rule bodies have been pushed down; this section keeps only the numbered index for cross-document reference.

| No. | Module | Rule body |
|---|---|---|
| 6.1 | Contracts | [SDS.Contracts/Rules.en.md](SDS.Contracts/Rules.en.md) |
| 6.2 | Diag | [SDS.Diag/Rules.en.md](SDS.Diag/Rules.en.md) |
| 6.3 | Core | [SDS.Core/Rules.en.md](SDS.Core/Rules.en.md) |
| 6.4 | Vanilla | [SDS.Vanilla/Rules.en.md](SDS.Vanilla/Rules.en.md) |
| 6.5 | Api | [SDS.Api/Rules.en.md](SDS.Api/Rules.en.md) |
| 6.6 | ThirdModCompat | [SDS.ThirdModCompat/Rules.en.md](SDS.ThirdModCompat/Rules.en.md) |
| 6.7 | Bootstrap | [SDS.Bootstrap/Rules.en.md](SDS.Bootstrap/Rules.en.md) |
| 6.8 | Rules | [SDS.Rules/Rules.en.md](SDS.Rules/Rules.en.md) |

## 7. Patch Ownership

- All 112 feature patch classes live in Vanilla; the 29 registry entries (19 mirror guards + 10 projection owners) live in Vanilla too.
- The 9 third-party adapters' patches are dynamic `harmony.Patch` calls, all owned by `SDS.ThirdModCompat`; they do not count toward the 112.
- `SDS_PatchInstallerImpl` scans only the `SDS.Vanilla` assembly.
- Patch class names stay unchanged; `PatchInventory.en.md` only adds an "owning assembly" column.

## 8. Harmony IDs

All dynamic patches live inside the single `SDS.ThirdModCompat` assembly; IDs match adapter names; a duplicate is logged as Error and the later registrant is skipped.

| Owner | Adapter | Harmony ID |
|---|---|---|
| Vanilla | — | `sizuru.sds.vanilla` |
| ThirdModCompat | `SDS_CompatAdapter_PUAH` | `sizuru.sds.compat.puah` |
| ThirdModCompat | `SDS_CompatAdapter_CE` | `sizuru.sds.compat.ce` |
| ThirdModCompat | `SDS_CompatAdapter_UFRecipes` | `sizuru.sds.compat.ufrecipes` |
| ThirdModCompat | `SDS_CompatAdapter_ManipulatorBeam` | `sizuru.sds.compat.manipulatorbeam` |
| ThirdModCompat | `SDS_CompatAdapter_MiliraExpanded` | `sizuru.sds.compat.miliraexpanded` |
| ThirdModCompat | `SDS_CompatAdapter_CeleTechShuttle` | `sizuru.sds.compat.celetechshuttle` |
| ThirdModCompat | `SDS_CompatAdapter_EquivalentExchange` | `sizuru.sds.compat.equivalentexchange` |
| ThirdModCompat | `SDS_CompatAdapter_Versatran` | `sizuru.sds.compat.versatran` |

## 9. Startup Order

1. Read `SDS_Settings`, run `ClampAll`.
2. `SDS_HostInstaller.Install`: `SDS_Vanilla.CreateHostSlots()` first assembles the 11 slots, then Bootstrap injects Ports / DepositFilter / Priority / Delivery / MirrorQuery / MirrorPhysical / MedicalCare / ReclaimWorld / Display / DepositJobs / TransportLoad.
3. `SDS_Vanilla.InstallPatches()` (implemented in `SDS.Vanilla`): enumerates every `[HarmonyPatch]` class in this assembly and installs them class by class under try/catch; then installs the `SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge` registries; no other assemblies are scanned.
4. `SDS_CompatLoader.Scan`: scans for `ISDSCompatAdapter`, verifies that names (Harmony IDs) are unique, and `Apply`s only when the target is loaded; if needed it scans once more inside `LongEventHandler.ExecuteWhenFinished` (idempotent).
5. Each module's `RegisterAll()`: registers `ISDSResettable` with `SDS_StaticState` and `ISDS_DiagnosticProvider` with `SDS_SelfCheck`.
6. `LongEventHandler.ExecuteWhenFinished`: applies settings to building defs; runs the W63 drop-cell guard; emits the startup report.
7. Entering a game / loading a save: `SDS_GameComponent_Grid` constructed → `SDS_StaticState.ResetAll` → `SDS_Ledger` created → `Instance` set → `SDS_Core.Attach(Ledger)`.

## 10. Validation Scripts

| Script | Content | When it runs |
|---|---|---|
| `CheckRuleIds.ps1` | rule IDs and code anchors | before every L2 change commit |
| `CheckRuleSingleSource.ps1` | sole rule implementation: every `// RULE:<ID>` marker must appear exactly once and land in its registered file; Vanilla / Compat must not duplicate the `min(…, available)` / `total − reserved` formulas; the key primitives of the distributed rules (W1 / W5+W54 / W38–W48) may be called only from their registered files; Compat must not reach directly into Core / Ledger / Vanilla | before changing any rule implementation / any L2 change |
| `CheckPatchInventory.ps1` | per-assembly reconciliation; Vanilla 112; whitelist of third-party adapter dynamic hook points | after any patch / hook point change |
| `CheckCompatSurface.ps1` | only Api / Contracts / Diag allowed; Core / Vanilla forbidden; adapters must not call `SDS_Diag.Fail` | after any compat layer / bridge surface change |
| `CheckAssemblyBoundaries.ps1` | assembly reference graph + type-level whitelist + independence rules + `InternalsVisibleTo` + `SDS_Host.Install` / `SDS_Config.Current` write points | after any assembly reference / public-surface change |
| `DumpPublicSurface.ps1` | public-surface inventory: extracts public types / members from `*_API*.cs`; `-Write` updates the `Tools/PublicSurface/` baseline, `-Check` reconciles against it | after any `*_API*.cs` or public-surface change |
| `BuildModule.ps1` | build scope (E11): `Tools/.build-state.json` records each module's source / public surface / DLL fingerprints plus GameDir / props / SDK / referenced-assembly fingerprints; skip when source is unchanged and DLL fingerprints match, reject when source is unchanged but DLL fingerprints differ; build only the modules whose source changed, following the ProjectReference topology (one `--no-dependencies` build each), and never rewrite DLLs whose source did not change; reference-state changes require `-AcknowledgeReferenceChange`, public-surface changes or dependent rebuilds caused by `InternalsVisibleTo` friend coupling require `-AllowChanged`, explicit full rebuilds use `-ForceRebuild`; `-InitializeState` only establishes the baseline without building, `-DryRun` only prints the plan; after building it still re-verifies via SHA256 and rolls back unapproved changes | every build |
| `TestModule.ps1` | test build scope: optionally calls `BuildModule.ps1` first (forwarding the `-AcknowledgeReferenceChange` confirmation for reference-state changes), then builds only the test projects (`--no-dependencies`) and compares `超维存储/Assemblies/*.dll` SHA256 before and after; exit code 1 if any production DLL changed; on success runs `dotnet test --no-build --no-restore` | every test run |

## 11. Hard Acceptance Criteria

- Behavior identical: the five-axes manual matrix; the 112 Vanilla patches A/B = 112/112; each third-party adapter A/B'd separately; quantity conservation holds.
- Old saves load: everything on the save lock list (ModuleInterfaces.en.md §8) is restored; unresolved defs are kept; preserved true bodies are not lost; reservation / reclaim / protected-output audits are restored.
- Structural validation: the module reference graph is one-way; forbidden symbols appear zero times; a new save → read back → save again must keep node names and field names verbatim identical.

## 12. Document Map

| Location | Content | When to read |
|---|---|---|
| [DesignPrinciples.en.md](DesignPrinciples.en.md) | the three principles (sole authority) | when decisions conflict |
| This document | the global one-pager | at the start of every task |
| [ModuleResponsibilities.en.md](ModuleResponsibilities.en.md) | module list, artifacts, the public whitelist, project structure, legacy file ownership | when confirming boundaries |
| [ModuleInterfaces.en.md](ModuleInterfaces.en.md) | cross-module shared-contract index, adapter bridge registry, save lock list | when making cross-module changes |
| [DataFlow.en.md](DataFlow.en.md) | data shapes and flows of the main paths (defines no rules, repeats no fields) | when untangling a chain / changing cross-module data |
| [RuleIndex.en.md](RuleIndex.en.md) | rule ID ↔ canonical sentence ↔ authoritative doc ↔ code anchor ↔ machine check (registry only; defines no rules) | before changing rules / committing L2 |
| [PatchInventory.en.md](PatchInventory.en.md) | 112 feature patches + 29 registry entries + dynamic patch registry (read by `CheckPatchInventory.ps1`) | when changing patches |
| [PublicAPI.en.md](PublicAPI.en.md) | the five-axes contracts, caller contexts, the bridge derivation table (read by `CheckCompatSurface.ps1`) | when changing the Api / adapters |
| [DiagnosticsCoverage.en.md](DiagnosticsCoverage.en.md) | diagnostics capability reconciliation and metrics (reconciled by `CheckPatchInventory.ps1`) | when changing logging / patches |
| `SDS.*/README.en.md` | the module one-pager (positioning / dependencies / artifacts / reading order) | when maintaining that module |
| `SDS.*/Interfaces.en.md` | consumed surface (external members relied on and their guarantees) + vanilla / third-party contact surface + signature index (pointing at `*_API.cs`) | when maintaining that module |
| `SDS.*/Rules.en.md` | the module's checkable rules + rule bodies (authoritative) | when maintaining that module |
| `SDS.*/DesignDecisions.en.md` | historical decisions, differences from the old implementation and its pitfalls, design trade-offs (rule bodies are in `Rules.en.md`) | when tracing where a design came from |
| [SDS.Vanilla/CompatibilityRetirement.en.md](SDS.Vanilla/CompatibilityRetirement.en.md) | the legacy-save compatibility register: support windows, which saves each item applies to, detection and retirement prerequisites | when deciding to delete a compat branch |
