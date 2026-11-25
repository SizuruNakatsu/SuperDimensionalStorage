# SDS.Bootstrap · Design Decisions (Why It Is Done This Way / Pitfalls Encountered)

> Positioning: this file preserves SDS.Bootstrap's historical decisions, old-implementation differences, design trade-offs, and items pending verification, answering "why it is done this way". For the checkable rules see [Rules.en.md](Rules.en.md); for the public surface see [Interfaces.en.md](Interfaces.en.md). "Old" in this file refers to the pre-refactor single-file implementation (`SDS_Mod.cs` / `SDS_ModCompat.cs`), kept only for historical traceability and no longer accessible.

## 1. Historical decisions and pitfalls

| Topic | Old approach / reason | New approach and maintenance notes |
|---|---|---|
| Deferring to `LongEventHandler.ExecuteWhenFinished` | When `Mod` is constructed, Defs are not loaded, so writing `ThingDef` power consumption has to be postponed (old `SDS_Mod.cs` 36–37) | Split into a synchronous segment + `RunDeferred`; the `started` / `deferredRan` dual flags |
| Per-class patch installation | No `PatchAll`: one bad patch would fail the whole Mod construction (old 49–52) | Owned by Vanilla's `SDS_PatchInstaller`; Bootstrap only calls it |
| Single Harmony instance | The old `cagier.superdimensionalstorage` passed one instance to all adapters | Vanilla fixes `sizuru.sds.vanilla`; each adapter builds its own instance with its ID matching its name |
| Re-scan idempotency | The old compat layer ran once at startup only, with no re-scan | `attempted` is registered before `Apply`; when the target is not installed, `attempted` is not written, so step 6 can still take over |
| Reports now read from Diag | The old entry point called the 9 adapter classes directly and ran `Hub.Reset()` / `LogAll()` | Bootstrap only calls `LogAll()`; `Reset()` is not called on the startup path (the `attempted` + `Has(name)` double gate already prevents duplicates — an intentional trade-off) |
| Settings page side effects swallow their own exceptions | An exception thrown from `WriteSettings` would leave the settings window unable to close, forcing a process kill (old 400–402) | Kept the try/catch + `Log.Error` (`SDS_Mod.cs` 324–332) |
| Opening settings from the main menu | `Instance` is not automatically nulled after exiting a save and may point at a stale component (old 450–452) | Uniformly check `Current.ProgramState` / `Current.Game` / `Find.Maps` |
| Writing power consumption | Def-level sharing; must skip when the `CompProperties_Power` instance is shared with other Defs | Kept the `Log.ErrorOnce` skip + `SetUpPowerVars()` to recompute spawned instances |
| Settings application differences | The old `ApplySettings` also called `SDS_GameComponent_Grid.NotifyStructureChanged()` and Millira's `ClearStoreCache()` | The new entry point uses `SDS_Rules.NotifyEndpointChanged()` + `SDS_Vanilla.ApplyStoragePriorityToEndpoints()` instead; the Millira cache is self-invalidated by its adapter according to the W33 toggle; structure changes are covered by the 250-tick reconciliation + building Tick fallback (intentionally not re-evaluated immediately) |
| "Use" command reporting | The old entry point's `SDS_UseCommand.ReportProvidersOnce()` | Restored: `SDS_Vanilla.ReportUseProvidersOnce()` is called by step 7, outputting the included / excluded counts and the excluded-table miss list |
| In-game verification | The old design stopped at the "patch A/B log" level | The new startup report's final line always outputs "in-game A/B verification pending"; the in-game verification of G3 is incomplete |

## 2. Differences and pitfalls: old implementation → new implementation

### 2.1 Conclusions from the old flow relevant to this module

| Old node | Conclusion (paraphrased) | New location |
|---|---|---|
| §0·0.1 | `SDS_Mod.ApplyHarmonyPatches` loaded 103 attribute patches class by class, then applied 29 registry entries (19 guards + 10 projection owners), 132 patch points in total; a single-point failure did not affect the rest | Step 3 `SDS_PatchInstaller.Install()`; the new count is 112 + 29 (ModuleRules.en.md §7) |
| §0·0.2–0.5 | The G / L switches, endpoint registration, and `GameComponentTick` frequency division belong to the runtime | Not in Bootstrap; owned by Vanilla's `SDS_GameComponent_Grid` |
| §5·5.11 | W63 enumerates all WorkGivers at runtime and attaches destination guards, executed once after Def loading; failures are only logged | Step 6 `SDS_PatchInstaller.ApplyRuntimeHooks()` |
| §5·5.3 / 5.4 | Job dispatch outlets and the Job factory | Vanilla; not in Bootstrap |
| §5·5·5·5.5 | The 9 third-party adapters' phase 4 has moved to translation layers that only call `SDS_API` | ThirdModCompat; Bootstrap only handles discovery and `Apply` |
| §5·5·5·5.7 | Tiangong shuttle load-list injection (W50) | Adapter + Api; Bootstrap does not touch it |

## 3. Design trade-offs

The former ImplementationRationale.md has been merged into this section (its §4·4 corresponds to item 4 of this section's 3.3); the rule body is in ModuleRules.en.md §6.7, §8, and §9 and the signatures in ModuleInterfaces.en.md §7, and is not restated here.

### 3.1 Why there must be exactly one composition root

The order of the seven startup steps is defined by ModuleRules.en.md §9; here we only explain why each step can only be done by the entry point.

| Composition action | Why only Bootstrap can do it | Source evidence |
|---|---|---|
| Settings reading and clamping | The `SDS_Mod` constructor obtains `GetSettings<SDS_Settings>()` and runs `ClampAll`; the Mod instance may be constructed repeatedly, and composition is skipped via `SDS_Startup`'s `started` flag | `SDS.Bootstrap/SDS_Mod.cs` 24–34; `SDS_Startup.cs` 18–25; old `SDS_Mod.cs` 55–56 |
| Host injection | The 11 host objects must be in place before any Core / Vanilla call; `SDS_Host.Install` may be called only by Bootstrap | `SDS.Bootstrap/SDS_HostInstaller.cs` 15–50; ModuleRules.en.md §5 |
| Harmony installation | Patches are process-level global; installing twice would run the same Postfix twice; the installer's static `harmony` field guarantees a single install per process | `SDS.Vanilla/SDS_PatchInstaller.cs` 54–60; old `SDS_Mod.cs` 42–61 |
| Adapter scanning | The scan enumerates `AppDomain.CurrentDomain.GetAssemblies()` and can only run after the Mod assemblies have entered the AppDomain; the target Mod may become detectable only after the first scan, hence the re-scan | `SDS.Bootstrap/SDS_CompatLoader.cs` 45–71; ModuleRules.en.md §9.4 |
| Module registration | Each module's `RegisterAll()` is called once during startup only; without an entry point there is no unified call timing | `SDS.Bootstrap/SDS_Startup.cs` 58–69 |
| Startup report | The report aggregates the patch, host, adapter, static-cleanup, and self-check counts; only the entry point holds all these numbers at once | `SDS.Bootstrap/SDS_Startup.cs` 101–114 |

Three hard constraints on ordering:

1. Hosts come before every consumer. The host implementation classes all have public parameterless constructors that read no `Instance` / `State` / `Find` / `Def` at construction time, so they can be injected before `SDS_GameComponent_Grid` without null references (ModuleInterfaces.en.md §4.2).
2. Settings come before hosts and scanning. `Settings.ClampAll()` and the `SDS_Config.Current` write happen at the very beginning of construction; later steps only read (`SDS.Bootstrap/SDS_Mod.cs` 26–33).
3. Patches come before adapter scanning. Step 3 installs Vanilla attribute patches; step 4's third-party dynamic patches are created by the adapters' own `Apply`, and the two never scan each other's assemblies (ModuleRules.en.md §7 and §9).

### 3.2 Why Bootstrap is not referenced by other SDS assemblies

| Reason | Explanation |
|---|---|
| The dependency direction would form a cycle | Bootstrap already references Vanilla / Api / Core; if Vanilla also referenced Bootstrap, that would form `Vanilla → Bootstrap → Vanilla`, violating the unidirectional graph of ModuleRules.en.md §4 |
| Plug-in style reverse dependency | ThirdModCompat may only be discovered via reflection through Contracts' `ISDSCompatAdapter`; adapters should not know whether the launcher exists, otherwise the whole compat layer would be dragged into the dependency chain when the target is not installed |
| Prevents bypassing the Api | Having Bootstrap means being able to touch the startup sequence, the settings instance, and the host slots directly, which easily creates a second write path, violating E6 / E7 (ModuleRules.en.md §3) |
| Not a compatibility contract | The adapters' only discovery surface is Contracts' `ISDSCompatAdapter`; Bootstrap has no version constants or adapter bridge, so referencing it yields no external commitments — version checks should read `SDS_API.Version` (ModuleResponsibilities.en.md §7) |
| Unique public surface | Bootstrap exposes `SDS_Mod` only for RimWorld Mod discovery, with all other types `internal` (ModuleResponsibilities.en.md §2; ModuleInterfaces.en.md §7) |
| Machine verification | `CheckAssemblyBoundaries.ps1` checks the assembly reference graph and the callers of `SDS_Host.Install`; violations fail outright (ModuleRules.en.md §10) |

`SDS.Bootstrap.csproj` references only Contracts / Diag / Core / Api / Vanilla, not ThirdModCompat; the startup path contains no static `SDS_API` calls either. The Api reference is an item allowed by the matrix and does not mean the entry point forwards the five axes.

### 3.3 Design trade-offs

1. Deferring to `LongEventHandler.ExecuteWhenFinished`: postponing step 6 as a whole avoids repeated null checks early in startup. The cost is that startup is split into a synchronous segment and a deferred segment, requiring the two idempotency flags `started` / `deferredRan` (`SDS_Startup.cs` 14–16, 72–81).
2. Adapter scanning is idempotent and allows re-scanning: `ownerByName` enforces name uniqueness, and `attempted` guarantees each adapter is `Apply`-ed only once; when the target is not installed, `attempted` is not written, so the adapter can still be taken over when it becomes visible in later scans. The cost is that both scans traverse all assemblies and types (`SDS_CompatLoader.cs` 28–31, 125–160).
3. Reports are read from Diag, with no reference to ThirdModCompat: there is no compile-time `Bootstrap → ThirdModCompat` edge, so the startup path is unchanged even when no adapter is installed (`SDS_Startup.cs` 101–114; ModuleResponsibilities.en.md §9). The cost is that adapters must register themselves diligently; a missed registration can only be patched by the scanner adding a 0/0 record (`SDS_CompatLoader.cs` 163–171).
4. Settings clamping is centralized in `ClampAll`: it is called at construction, at the end of every frame's draw, in `WriteSettings`, and before applying Defs, so out-of-range values never flow into Core / Vanilla; runtime reads uniformly go through `SDS_Config`'s pure read accessors (ModuleInterfaces.en.md §1.3). The cost is that the same method is called from many places, so it must be kept idempotent (Contracts `SDS_Mod.cs` 53–63).
5. Failures only log `ErrorOnce` and do not block startup: the synchronous and deferred segments each have their own try/catch; step 5 registration has its own fallback, so registration proceeds even if earlier steps fail (`SDS_Startup.cs` 53–56, 58–69, 96–98). The cost is that when startup is incomplete, the only way to tell is from the logs and the startup report — the entry point never raises a blocking prompt.

## 4. Pending verification

- Verifying the in-game startup report's "Vanilla patches A/B = 112/112, hosts 11/11" (TBD).

From this point on, the old implementation serves only as a historical archive; maintaining this module requires no further reading of it.
