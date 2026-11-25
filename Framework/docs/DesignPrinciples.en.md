# SuperDimensionalStorage · Design Principles

## 1. General Principles (Original Text Preserved)

Principle 1: Buildings, pawns, and mechanoids share the same kind of port.

Principle 2: This port is equivalently treated as "a warehouse / container with enormous capacity" — that is, "this much stuff is genuinely placed on the map".

Corollary 1: The port participates in vanilla checks and is not special in itself.

Corollary 2: Do not change the working logic vanilla exhibits.

Corollary 3: The ports of the three share a single handle in implementation.

## 2. Document Division of Labor (finalized 2026-10-21, converged 2026-10-24)

Principle 3: This mod pursues only process uniqueness and logic uniqueness, not uniqueness of a concrete function. Logic uniqueness = a rule is defined once + every entry point calls it.

**Principle 3 applies to documents as well: the same rule may be defined in only one document; all other documents may only reference it and must not restate a second copy of the rule.**

**This document keeps only the general principles, the document division of labor, and the change tiers; specific rules, fields, and processes always take the documents pointed to by ModuleRules.en.md §12 "Document Map" as the sole authority.**

**When cross-document definitions conflict, the "Authoritative doc" column of RuleIndex.en.md is the sole arbitration source; rule ownership follows ModuleRules.en.md §12 "Document Map".** The historical five-axes capability documents have been split by module and merged into `SDS.*/Rules.en.md` (criteria) and `SDS.*/Interfaces.en.md` (contact surfaces), and are no longer maintained separately; the still-valid startup / lifecycle content was merged into ModuleRules.en.md §9, DataFlow.en.md, and `SDS.Bootstrap/Interfaces.en.md`.

## 3. Change Tiers and Impact Scope (set 2026-10-27)

> This section only defines "which changes must go back to a human"; it defines no gameplay rules. Any change to a rule ID (W/R/S/E) or a canonical sentence counts as an L2 change.

**E4 Change tiers (by objective trigger conditions, not subjective judgment)**

| Tier | Trigger condition (any single hit qualifies) | Execution |
|---|---|---|
| **L1** | Pure implementation: renames, file splits, equivalent refactoring, comment wording, adding machine checks, visibility narrowing, or physical moves; criterion: **the public surface inventory diff is empty** (`Source/Tools/DumpPublicSurface.ps1 -Check` exit code 0) and logs and assertions are unchanged; touches no rule IDs / canonical sentences / save fields / patch hook point sets | AI executes autonomously, running `Source/Tools/CheckRuleIds.ps1` + `CheckPatchInventory.ps1` + `DumpPublicSurface.ps1 -Check` once |
| **L2** | Adding or modifying rule IDs / canonical sentences; **inter-module public API changes** (`*_API.cs` members added/removed or signatures changed, public whitelist changed — i.e., the inventory diff is non-empty); new runtime static caches; patch hook points added or removed; changing the transaction state machine or the count measure | AI executes, **produces the impact list first**, a human reviews |
| **L3** | Modifying this document's three principles / corollaries / document division of labor / the tier table of this section; deleting rule IDs; modifying the save structure (`ExposeData` / `Scribe`); modifying the external `SDS_API` five-axes contract or `Version` | A human decides; the AI only provides impact scope and evidence |

**E10 Criterion artifacts (added 2026-11-12)**

Additions, removals, and signature changes of inter-module public APIs are always handled as L2; "external" refers only to the `SDS_API` five axes and `Version`, and that layer is L3. Tiering accepts only artifacts, never subjective descriptions: an empty diff from `DumpPublicSurface.ps1 -Check` is L1, a non-empty diff is L2. The inventory's coverage follows the implementation-side registry (currently `SDS.Core` / `SDS.Diag` / `SDS.Api`); for unregistered modules, changes to the public whitelist (`CheckAssemblyBoundaries.ps1` §4) are judged equivalently.

**E3 Impact list (mandatory for L2 / L3; declare before touching code)**

1. Changed rule IDs: added / modified / deprecated, plus a **verbatim diff of the canonical sentences**;
2. Document sections that need syncing (the AI lists them itself; a human spot-checks);
3. Assertions / self-checks that need updating;
4. Static caches that must be registered with `SDS_StaticState.ResetAll`;
5. Patch hook points added or removed (sync the A/B report and the registry, see ModuleRules.en.md §7);
6. **Verification evidence**: build result + self-check result.

> A list missing item 6 is merely a declaration, not completion; a change missing item 1 is always handled as L2 (default to the stricter side).

**E11 Build scope and evidence (set 2026-11-13)**

By default, build only the changed module's csproj (`dotnet build Source/SDS.<module>/SDS.<module>.csproj -c Debug`); when the reference state is confirmed unchanged, `--no-dependencies` may be added. Build the entire solution only when `SDS.Contracts`, the public surface, the `ProjectReference` graph, `Directory.Build.props`, the SDK, or the NuGet reference state changes.

Every build must go through `Source/Tools/BuildModule.ps1`: compare the SHA256 of `超维存储/Assemblies/*.dll` before and after the build; any DLL change beyond the target module and explicitly declared direct dependencies (`-AllowChanged`) fails the build. When a first restore invalidates every project's `CoreCompileInputs.cache`, `-AcknowledgeFullRebuild` must be added explicitly, and the report must state that "the reference state changed, causing a one-time full recompilation"; this phenomenon must not be attributed to the scope of the current change. Without a pre-build baseline (first build), only this change is recorded; run once more to obtain scope evidence.
