> Positioning: SDS.Bootstrap is the sole Mod entry point and composition root of "SuperDimensionalStorage": it reads settings, injects hosts, installs Vanilla patches, scans third-party adapters, performs the 7-step startup, and outputs the startup report; it defines no gameplay rules.

# SDS.Bootstrap (Mod Entry Point and Composition Root)

## 1. Positioning

- Assembly `SDS.Bootstrap.dll`, with the unified namespace `SuperDimensionalStorage`; the only public type is `SDS_Mod` (ModuleResponsibilities.en.md §2, §9).
- RimWorld discovers and constructs this type via the `Mod` base class; this assembly is the sole implementation site of the startup sequence (ModuleRules.en.md §9).
- Everything else is `internal`: `SDS_Startup`, `SDS_CompatLoader`, `SDS_CompatScanResult`, `SDS_HostInstaller`, `SDS_SettingsApplier`.
- Not responsible for: CRUD / capacity / filtering / ledger deduction / sorting / transactions (Core), the 112 patches and buildings / UI (Vanilla), the ledger lifecycle (Vanilla `SDS_GameComponent_Grid`), settings field definitions (Contracts).

## 2. Responsibilities

| Responsibility | Location | Key points |
|---|---|---|
| Settings page | `SDS_Mod.cs` (`SDS_Mod`) | Only writes `SDS_Settings` fields; `ClampAll` at the end and before the window closes |
| Injection of the 11 hosts | `SDS_HostInstaller.cs` | All come from `SDS.Vanilla`; reads no save data at construction time |
| Harmony installation | `SDS_Startup.cs` calls Vanilla `SDS_PatchInstaller.Install()` | Only scans `SDS.Vanilla`; 0 `[HarmonyPatch]` in this assembly |
| Adapter scanning | `SDS_CompatLoader.cs` | Discovers `ISDSCompatAdapter` by reflection; name deduplication; no `Apply` when the target is not installed |
| 7-step startup | `SDS_Startup.cs` | The first 5 steps are synchronous, step 6 is deferred, step 7 is the report |
| Startup report | `SDS_Startup.LogReport` | Counts come from Vanilla public counters and the Diag report hub |

## 3. Dependencies and Artifacts

| Item | Content |
|---|---|
| Project references | `SDS.Contracts`, `SDS.Diag`, `SDS.Core`, `SDS.Vanilla`, `SDS.Api` (`SDS.Bootstrap.csproj`) |
| Forbidden references | `SDS.ThirdModCompat` (discovered only via interface reflection), `0Harmony` |
| Artifact | `SDS.Bootstrap.dll` |
| Static state registrations | 0 items (ModuleResponsibilities.en.md §4) |

## 4. Public entry

`SDS_Mod` (the only top-level public type). For members, settings page entries, and call sites, see [Interfaces.en.md](Interfaces.en.md) §1.

## 5. Constraints

For this module's hard constraints, see [Rules.en.md](Rules.en.md) (BSP-1 through BSP-12 are the sole authority).

## 6. Document map

| File | Content |
|---|---|
| [Interfaces.en.md](Interfaces.en.md) | Public surface, 7-step startup call sites, the 11 injected objects, consumed members, the Vanilla / third-party interaction surface |
| [Rules.en.md](Rules.en.md) | This module's checkable rules and how to check them |
| [DesignDecisions.en.md](DesignDecisions.en.md) | Historical decisions and pitfalls, old-implementation differences, design trade-offs, items pending verification |
| [../ModuleRules.en.md](../ModuleRules.en.md) | Global hard constraints, the dependency matrix, the startup sequence (§9 is authoritative) |
