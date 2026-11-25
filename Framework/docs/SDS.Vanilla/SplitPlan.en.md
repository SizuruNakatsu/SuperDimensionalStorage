# SDS.Vanilla Split Plan

> Positioning: this directory holds the split evaluation and implementation plan for the `SDS.Vanilla.dll` size problem. It records the plan and its basis only and changes no rules; rules remain governed by [Rules.en.md](Rules.en.md) and ModuleRules.en.md.
> Current state (re-verified 2026-11-12): 53 `.cs` files, about 23,866 lines; all flat in the root directory except `Properties/AssemblyInfo.cs`, with a single namespace `SuperDimensionalStorage`.

## 1. Conclusions

1. **Assembly-level splitting (adding a csproj) is not feasible** — verified by actual testing to violate this mod's principles and the module independence principle (basis in §2: three hard facts).
2. **The compliant split: keep the assembly untouched and divide the source directory into 9 subdirectories by responsibility cluster** — all 5 verification scripts are anchored on "filename-based recursive resolution"; the only exception is the public-surface registration file existence check of `CheckAssemblyBoundaries.ps1`, which originally looked only at the module root and was changed to `-Recurse` after `SDS.Vanilla_API.cs` moved into `Install/` (a one-line tool fix, no verification rule changed); all other scripts and registration documents change nothing. Per E4 this is L1 (public surface diff is empty).
3. **Optional second step: split the 7 thousand-line files by class** — this requires updating the PatchInventory.en.md statistics section and about 17 anchors in RuleIndex.en.md in sync; the recommendation is to split incrementally by hot spots only when a file becomes a maintenance bottleneck, not as a one-shot batch split.

> Directories carry navigation duty only and are **not dependency boundaries**. All subdirectories still compile into the same assembly and share the same namespace; cross-cluster internal direct references are a design fact, and the directories must not be interpreted as "9 modules" on that basis.

## 2. Why the assembly cannot be split (three hard facts)

1. **The 112 patch classes are spread across 17 files**, half embedded inside functional files (SDS_PreservedUsage 9, SDS_MirrorBehavior 8, SDS_Search 6, SDS_Selection / SDS_PreservedOffer / SDS_UI 5 each...; explicit `Patch_*.cs` files account for only 50). Splitting the assembly by functional cluster would inevitably fragment the patch counts, directly touching five places in the rules: the hard constraint "all 112 attribute patch classes must be kept; patches only in Vanilla" + `SDS_PatchInstallerImpl` scanning only this assembly + the Harmony ID table + CheckPatchInventory reconciling by source file + the startup order.
2. **Inter-cluster coupling is mesh-like, not layered**: `SDS_Exposure` is consumed across clusters by 10 files (trading / mirrors / preserved / transport / facade); `SDS_OutputUtility` is consumed in 16 places and `SDS_PlacementUtility` in 13 by nearly every cluster; `SDS_UI ↔ SDS_PreservedUsage` reference each other both ways. Split-out submodules could only communicate through internal state — either making internal types public (violating the E8 whitelist) or mass-applying `InternalsVisibleTo` (violating the spirit of the registration system), producing a mesh of pseudo-modules in which "modules communicate only through Contracts DTOs and interfaces" cannot hold.
3. **E8 engine-surface types are forbidden from being referenced by other modules**: types instantiated by name / saved, such as `Building_SDS_*`, `JobDriver_SDS_*`, `SDS_Proxy_*`, `SDS_Comp*`, coexist in the same files as business logic and naturally belong to the single Vanilla assembly.

> ModuleRules.en.md §2: "Modules may communicate only through SDS.Contracts DTOs and interfaces; dependencies are strictly one-way" — Vanilla's 9 responsibility clusters share the same foundation (Grid.State, endpoint handles, the exposure basis); they are **subsystems of one module**, not 9 independent modules. Splitting directories by responsibility cluster is precisely a faithful expression of that fact.

## 3. Plan body: 53 files → 9 responsibility subdirectories

The namespace stays `SuperDimensionalStorage` unchanged (ModuleRules.en.md §4: no sub-namespaces are added; layering relies on assembly boundaries — subdirectories are only navigation groupings and create no namespaces). Directory names use English ASCII, consistent with the code file style.

| # | Subdirectory | Responsibility | Files (line counts) | Subtotal |
|---|---|---|---|---|
| 1 | `Foundation/` foundation | Save shell, state, host services, cross-cluster shared utilities (output / placement) — the "facts and execution" layer | SDS_GameComponent_Grid(271), SDS_VanillaState(316), SDS_ProtectedOutputs(30, retired 2026-11-16), SDS_VanillaDiagnostics(288), SDS_VanillaServices(133), SDS_CompatHosts(136), SDS_ReclaimWorldAdapter(75), SDS_MedicalCareAdapter(22), SDS_OutputUtility(334), SDS_PlacementUtility(43) | 10 files / 1,648 lines |
| 2 | `Endpoints/` endpoints | The four building types, endpoint handles / buffers / providers, access points, building comps | SDS_Buildings(1259), SDS_EndpointHandle(230), SDS_EndpointUtility(201), SDS_EndpointProvider(174), SDS_EndpointBuffer(112), SDS_AccessPoints(294), SDS_Comp_DrawExtraOverlay(73) | 7 files / 2,343 lines |
| 3 | `Mirrors/` mirrors & projections | Mirrors, the guard registry (19), the projection owner bridge (10), proxy Things, mirror seam patches | SDS_Mirrors(1239), SDS_MirrorBehavior(257), SDS_MirrorGuardRegistry(480), SDS_Projection(443), SDS_ProjectionOwnerBridge(298), SDS_Proxies(493), Patch_Mirrors(291) | 7 files / 3,501 lines |
| 4 | `Jobs/` take / place & delivery | Destinations per work type, delivery strategy resolution, the transmission layer, bill missing-ingredient queries, the "Use" command, the chip comp | SDS_Destinations(1556), SDS_JobTargets(474), SDS_Transmission(209), SDS_BillIngredientQuery(171), SDS_Use(534), SDS_Implants(331) | 6 files / 3,275 lines |
| 5 | `TradeTransport/` trading & transport | Virtual trade delivery, caravan formation / loading, transport pod virtualization, the corresponding patches and JobDriver | SDS_Trade(1058), Patch_Trade(214), SDS_CaravanTransport(999), Patch_CaravanTransport(167), SDS_TransportVirtual(1264), Patch_TransportLoad(455), JobDriver_SDS_CaravanJob(289) | 7 files / 4,446 lines |
| 6 | `Preserve/` preserved | Preserved bills, preserved usage, preserved supply | SDS_PreservedBill(842), SDS_PreservedUsage(799), SDS_PreservedOffer(671) | 3 files / 2,312 lines |
| 7 | `Query/` read-side visibility | The trio for "making vanilla query surfaces see the Hyperdimensional Space": the exposure counting basis, the generic selection layer, the search sorter | SDS_Exposure(464), SDS_Selection(555), SDS_Search(546) | 3 files / 1,565 lines |
| 8 | `UI/` interface | Sidebar, the Hyperdimensional Space window, the output dialog, the deposit-filter ITab, hotkeys | SDS_UI(1248), Window_SuperSpace(1279), Window_SDS_OutputDialog(514), SDS_ITab_DepositFilter(112), SDS_Hotkeys(511) | 5 files / 3,664 lines |
| 9 | `Install/` assembly seams | The public facade, internal assembly, the patch installer, vanilla seam stitching patches | SDS.Vanilla_API(71), SDS_Vanilla(187), SDS_PatchInstaller(103), Patch_VanillaSeams(747), Properties/AssemblyInfo.cs(4) | 4 files + Properties / 1,112 lines |

> Note: `SDS_Exposure` goes into `Query/` rather than Foundation because "exposure / selection / search" all belong to the read-side visibility basis, which better matches the same-kind responsibility principle; its 10 cross-cluster consumers are unaffected by the directory (same-assembly internal direct references).
> `SDS_OutputUtility` / `SDS_PlacementUtility`, consumed in 16 / 13 places across clusters, go into `Foundation/` to avoid being misread as "serving endpoints only".

**Basis for cluster assignment**: the cluster boundaries follow the existing classification of the [README.en.md](README.en.md) §1 responsibility table (buildings / ports, mirrors / projections, 112 patches, JobDriver, UI, trading / caravan formation / loading, save shell); only "the cross-cluster shared query basis" is separated into the Query cluster and "the facade and installation" into the Install cluster — the README responsibility table need not be rewritten; at implementation time just append a directory mapping after the table.

### Out of scope (explicitly not done)

- No types or logic are added / deleted / changed; pure file moves;
- The namespace is untouched (stays `SuperDimensionalStorage`);
- csproj, sln, Bootstrap, PatchInventory.en.md, and RuleIndex.en.md are untouched (all file names unchanged); among the verification scripts only the public-surface file existence check of `CheckAssemblyBoundaries.ps1` becomes recursive, the rest are untouched.

## 4. Optional second step: splitting the 7 thousand-line files by class

Files currently over 1000 lines (measured 2026-11-12):

| File | Lines | Split approach (one top-level class per file; patch classes follow their host logic) |
|---|---|---|
| SDS_Destinations.cs | 1556 | Deposit-to-port by callers without a map / destination addressing per work type; split by class |
| Window_SuperSpace.cs | 1279 | Window body / child controls / deposit filter area |
| SDS_TransportVirtual.cs | 1264 | Transport pod virtualization state machine / loading injection; split by class |
| SDS_Buildings.cs | 1259 | Building_SDS_Core / _Port / _CapacityArray / _Vault + the endpoint contract, one file each |
| SDS_UI.cs | 1248 | Sidebar / work report / the various patch classes; split by class |
| SDS_Mirrors.cs | 1239 | Mirror service / mirror physical operations; split by class |
| SDS_Trade.cs | 1058 | Display thing injection / deal validation / the registry; split by class |

`SDS_CaravanTransport.cs` has dropped to 999 lines and is no longer on the thousand-line split list.

**Registration cost (per file split)**:

1. PatchInventory.en.md "Statistics" section: the patch class count lines for that file are re-arranged by the new file names (CheckPatchInventory.ps1 verifies line by line mechanically; one line more or less fails outright — run the script after editing to verify);
2. RuleIndex.en.md "code anchors" column: rules anchored to that file name (currently about 17 places, concentrated in SDS_Trade.cs 3, SDS_CaravanTransport.cs 10+, SDS_UI.cs 1) change to the new file names (CheckRuleIds.ps1 checks anchor existence);
3. Documents in this directory (the [README.en.md](README.en.md) responsibility table, [DesignDecisions.en.md](DesignDecisions.en.md)) mention the split files, add a split note line;
4. Immediately after splitting, run the full acceptance of §6; `partial class`es (currently 9) and nested types must stay in the same file, and visibility must not be changed in passing.

**Recommended cadence**: no one-shot batch splits; split by hot spots only when a file actually becomes a maintenance bottleneck, and run the full acceptance once per file split.

## 5. Why registration cost is zero (verified via script anchoring)

| Script | Anchoring method | Impact of subdirectory moves |
|---|---|---|
| CheckPatchInventory.ps1 | `Get-ChildItem -Recurse` extracts [HarmonyPatch] and reconciles against the PatchInventory.en.md statistics section by **file name** | None (file names unchanged) |
| CheckRuleIds.ps1 | Code anchors **record only file names**, resolved recursively by file name (comment verbatim: "the new layout separates modules into directories, so anchors resolve recursively by file name") | None |
| CheckAssemblyBoundaries.ps1 | Type-level whitelist checked by content; public-surface registration file existence originally checked only the module root | The existence check was changed to `-Recurse` (`SDS.Vanilla_API.cs` moved into `Install/`); type rules unchanged |
| CheckCompatSurface.ps1 | Scans the Api / Contracts / Diag surfaces, unrelated to Vanilla's internal layout | None |
| DumpPublicSurface.ps1 | Reflects over the compiled assembly and compares against the baseline, unrelated to the source directory | None (after the move, `-Check` must still match) |

The csproj uses SDK-style wildcard compilation (no explicit Compile items), so subdirectories are included automatically; the sln references the csproj path, unchanged; tests reference by namespace / type, unchanged.

## 6. Execution and acceptance (when implementation is approved)

1. Create the 9 subdirectories and move the 53 files per the §3 mapping (the current workspace is **not a git repository**: `git mv` is unavailable; decide first between "initialize git before moving" or "plain move + full verification before and after"; do not copy `git mv` blindly);
2. Build: `dotnet build -p:GameDir=D:\RimWorld`, 0 warnings 0 errors;
3. All 5 verification scripts pass: `CheckRuleIds / CheckPatchInventory / CheckCompatSurface / CheckAssemblyBoundaries / DumpPublicSurface -Check` (run under `Source/Tools`);
4. All 135 unit tests pass;
5. Spot checks: the `SDS_PatchInstallerImpl` installation log still reports 112/112; type namespaces remain `SuperDimensionalStorage`; the public surface baseline diff is empty;
6. (If step four is approved) split the thousand-line files one by one, repeating steps 2–4 for each.

## 7. Status of related items (2026-11-12)

| Item | Status |
|---|---|
| Consolidating 4 duplicate implementations into Core (find-withdrawable-key by def, def-level availability aggregation, the CategoryKeyOf duplicate, the unrebuildable blacklist duplicate) | Done: `SDS_Rules.TryFindWithdrawableKey` / `GetAvailableCountForDef` / `CategoryKeyOf` / `IsLegacyUnrebuildableDef` are the sole implementations, Vanilla only forwards |
| Periodic structure reconciliation, the first-save entry notice letter | Done: `SDS_GameComponent_Grid.SweepRegistries` + `SDS_Rules.ReconcileStructures` (a silent 250 tick safety net), `SDS_VanillaState.TrySendIntroNotice` |
| Apparel optimization / wealth recount nudges after stock changes | Done: `SDS_GameComponent_Grid.TickStockNudges` (apparel 600 tick / wealth 300 tick debounce) |

This plan adds no rules; neither the directory reorganization nor file splitting changes the `SDS.Vanilla` public surface.
