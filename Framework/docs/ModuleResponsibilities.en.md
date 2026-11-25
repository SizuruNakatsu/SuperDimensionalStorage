# Module Responsibilities

> Positioning: the global index — module list and artifacts, outward-visible surfaces, project structure, and legacy file ownership. How each module divides its internal work is in the corresponding `SDS.*/README.en.md` and `SDS.*/Interfaces.en.md`.
> Binding constraints are in ModuleRules.en.md; types and signatures are in each module's `Interfaces.en.md` and ModuleInterfaces.en.md.

## 1. Module Overview

| Module | Responsibilities | Artifact | Docs directory |
|---|---|---|---|
| `SDS.Contracts` | pure data, enums, DefOf, settings, cross-layer interfaces, pure registries, host injection points | `SDS.Contracts.dll` | [SDS.Contracts/](SDS.Contracts/README.en.md) |
| `SDS.Diag` | logging, assertions, self-check, the static-state registration system, the adapter report hub | `SDS.Diag.dll` | [SDS.Diag/](SDS.Diag/README.en.md) |
| `SDS.Core` | the ledger and quantity accounting: the ledger, ledger data primitives (keys / materialization / vitals), ownership transfer (currently; the adjudication and flow layers have moved to `SDS.Rules`) | `SDS.Core.dll` | [SDS.Core/](SDS.Core/README.en.md) |
| `SDS.Rules` | rules and flows: endpoint resolution, bill supply (P2); the flow layer (P3); channels / capacity / available counts, structural facts, chip ports, endpoint versions (P4) | `SDS.Rules.dll` | [SDS.Rules/](SDS.Rules/README.en.md) |
| `SDS.Vanilla` | vanilla adaptation: buildings / ports, mirrors / projections / access points / exposure, the 112 patches, JobDrivers, Things / Comps, UI, trading / caravan formation / loading, the save shell, the reclaim ledger (`sdsReclaim`), the display-thing factory / identity table | `SDS.Vanilla.dll` | [SDS.Vanilla/](SDS.Vanilla/README.en.md) |
| `SDS.Api` | the outward five-axes facade + adapter bridges | `SDS.Api.dll` | [SDS.Api/](SDS.Api/README.en.md) |
| `SDS.ThirdModCompat` | the single third-party mod compat assembly: 5 Sdk utility classes + 9 adapters | `SDS.ThirdModCompat.dll` | [SDS.ThirdModCompat/](SDS.ThirdModCompat/README.en.md) |
| `SDS.Bootstrap` | mod entry point, settings page, Harmony installation, host injection, adapter scanning, startup order | `SDS.Bootstrap.dll` | [SDS.Bootstrap/](SDS.Bootstrap/README.en.md) |

Dependency direction is in ModuleRules.en.md §4.

Where rules live: Contracts holds only facts and contracts (DTOs / interfaces / DefOf / pure registries) and never implements any "how to take / how much to take"; cross-module rules live in `SDS.Rules` (migrating in step by step from P2), vanilla contact-surface rules live in Vanilla, and Compat has zero rules and only calls `SDS_API`. The sole-implementation locations of accounting-type rules are registered in RuleIndex.en.md ("Sole Implementation Registry") and machine-checked by `CheckRuleSingleSource.ps1`.

## 2. Module Public API Whitelist

| Module | Exposed to | Allowed public API |
|---|---|---|
| `SDS.Contracts` | all modules | all data types, interfaces, DefOf, settings, `SDS_Host` |
| `SDS.Diag` | all modules | `SDS_Diag`, `SDS_StaticState`, `ISDSResettable`, `SDS_SelfCheck`, `SDS_CompatReportHub`, and the report DTOs |
| `SDS.Core` | Vanilla / Api / Bootstrap / SDS.Rules (internal friends only) | `SDS_Core` (ledger lifetime / `UsedCapacity` / `SetForbidden` / `ApplyQuality` / `ApplyVitals` / version notifications / `AuditReservations` / `RegisterAll`); `ISDS_LedgerRead` / `ISDS_LedgerHost`; internal ledger write access only through `InternalsVisibleTo("SDS.Rules")` |
| `SDS.Rules` | Vanilla / Api / Bootstrap | `SDS_Rules` (P2 endpoints / supply; P3 flow layer; P4 adjudication layer and structure / endpoint versions); `SDS_Availability`; `SDS_ImplantUtility`; all other rule classes `internal` |
| `SDS.Vanilla` | Bootstrap (host assembly only) / Defs XML / saves | inter-module surface: `SDS_Vanilla` (`CreateHostSlots` / `InstallPatches` / `RegisterAll` / settings page and hotkeys / `HasGrid`); engine surface (instantiated by XML by name / saved by Scribe by type; other modules must not reference it): `SDS_GameComponent_Grid`, `SDS_EndpointBuffer`, `Building_SDS_*`, `SDS_Proxy_*`, `JobDriver_SDS_*`, `SDS_Comp*` |
| `SDS.Api` | ThirdModCompat / third parties | `SDS_API`; Contracts' public types |
| `SDS.ThirdModCompat` | Bootstrap (reflection scan) | no public top-level types: the 5 Sdk utility classes + 9 `ISDSCompatAdapter` implementations are all `internal` (zero dependents; discovered by Bootstrap via reflection; tests reference them directly through the registered `InternalsVisibleTo("SDS.Tests")`) |
| `SDS.Bootstrap` | RimWorld mod discovery | `SDS_Mod` |

The whitelist enforcement rules (not listed = leak, the Tests exception, Diag's overall visibility) are in ModuleRules.en.md §5.

## 3. SDS.Contracts Responsibility Boundaries

→ [README.en.md](SDS.Contracts/README.en.md) / [Interfaces.en.md](SDS.Contracts/Interfaces.en.md) / [Rules.en.md](SDS.Contracts/Rules.en.md)

## 4. SDS.Diag Responsibility Boundaries

→ [README.en.md](SDS.Diag/README.en.md) / [Interfaces.en.md](SDS.Diag/Interfaces.en.md) / [Rules.en.md](SDS.Diag/Rules.en.md)

## 5. SDS.Core Responsibility Boundaries

→ [README.en.md](SDS.Core/README.en.md) / [Interfaces.en.md](SDS.Core/Interfaces.en.md) / [Rules.en.md](SDS.Core/Rules.en.md)

## 6. SDS.Vanilla Responsibility Boundaries

→ [README.en.md](SDS.Vanilla/README.en.md) / [Interfaces.en.md](SDS.Vanilla/Interfaces.en.md) / [Rules.en.md](SDS.Vanilla/Rules.en.md)

## 7. SDS.Api Responsibility Boundaries

→ [README.en.md](SDS.Api/README.en.md) / [Interfaces.en.md](SDS.Api/Interfaces.en.md) / [Rules.en.md](SDS.Api/Rules.en.md)

## 8. SDS.ThirdModCompat Responsibility Boundaries

→ [README.en.md](SDS.ThirdModCompat/README.en.md) / [Interfaces.en.md](SDS.ThirdModCompat/Interfaces.en.md) / [Rules.en.md](SDS.ThirdModCompat/Rules.en.md)

## 9. SDS.Bootstrap Responsibility Boundaries

→ [README.en.md](SDS.Bootstrap/README.en.md) / [Interfaces.en.md](SDS.Bootstrap/Interfaces.en.md) / [Rules.en.md](SDS.Bootstrap/Rules.en.md)

## 10. Project Structure

```text
超维存储/Source/                     ← all new code (the single source tree)
  SDS.sln
  Directory.Build.props
  SDS.Contracts/SDS.Contracts.csproj
  SDS.Diag/SDS.Diag.csproj
  SDS.Core/SDS.Core.csproj
  SDS.Rules/SDS.Rules.csproj
  SDS.Vanilla/SDS.Vanilla.csproj
  SDS.Api/SDS.Api.csproj
  SDS.ThirdModCompat/SDS.ThirdModCompat.csproj
  SDS.Bootstrap/SDS.Bootstrap.csproj
  SDS.Tests/SDS.Tests.csproj
  SDS.Core.Tests/SDS.Core.Tests.csproj
  SDS.Rules.Tests/SDS.Rules.Tests.csproj
  Tools/Check*.ps1
Framework/                           ← design docs and registry data (no code)
  DesignPrinciples.en.md  ModuleRules.en.md  ModuleResponsibilities.en.md  ModuleInterfaces.en.md  DataFlow.en.md
  RuleIndex.en.md  PatchInventory.en.md  PublicAPI.en.md  DiagnosticsCoverage.en.md
  SDS.Contracts/   SDS.Diag/   SDS.Core/   SDS.Rules/   SDS.Api/
  SDS.Vanilla/     SDS.ThirdModCompat/    SDS.Bootstrap/
旧框架/                              ← pure historical archive (since 2026-11-12): never read by validation scripts, implementation, or review
```

Every `SDS.*/` directory holds exactly 4 docs: `README.en.md` / `Interfaces.en.md` / `Rules.en.md` / `DesignDecisions.en.md` (`SDS.Vanilla` additionally has `SplitPlan.en.md`).

- Source and `.csproj` sit side by side in `超维存储/Source/<module>/`; project directory name = assembly name = `SDS.*`.
- No file under `旧框架/` may be modified, moved, or deleted, and new code must never be written there.
- Each module has its own `.csproj` and can be built alone with `dotnet build`.
- `SDS.Tests` is the integration test project allowed to reference all modules; `SDS.Core.Tests` references only Contracts / Diag / Core (ledger tests only), and `SDS.Rules.Tests` references Contracts / Diag / Core / Rules (rules / flows); all three go into test output only.
- All modules output to `超维存储/Assemblies`; the old `SuperDimensionalStorage.dll` is not kept.

## 11. Legacy File Ownership (Integrity Audit)

This table only audits "old file → new module" ownership; it is not a migration order. A file may be split across several modules; section-level placement was archived along with the legacy framework and is no longer maintained, so this document keeps only file-level ownership.

| Module | Old files |
|---|---|
| Contracts | `SDS_Stock.cs` (key / entry / vitals / reservation), `SDS_Withdraw.cs` (enums / DTOs), `SDS_EndpointContract.cs` (query / decision DTOs), `SDS_API.cs` (caller / result / stock info), `SDS_Mod.cs` (settings / config), `SDS_CaravanTransport.cs` (`SDS_TransportLoadSave`), `SDS_Implants.cs` (DefOf), `SDS_UI.cs` (`SDS_SidebarUIState`), `SDS_Search.cs` (reclaim / return enums) |
| Diag | `SDS_SelfCheck.cs` (provider registry + self-check entry / exit), `SDS_Invariants.cs` (E2 development-time assertions), `SDS_StaticState.cs`, `SDS_CompatReports.cs`, `SDS_Mod.cs` (the `SDS_Diag` fragment) |
| Core | `SDS_GameComponent_Grid.cs` (ledger), `SDS_Stock.cs` (rules), `SDS_Withdraw.cs`, `SDS_ReturnAndReclaim.cs` (the return section), `SDS_EndpointContract.cs` (the structural facts section), `SDS_Buildings.cs` (the structure registry section), `SDS_Implants.cs` (port state), `SDS_SelfCheck.cs` (the ledger / deposit-rule self-check fragments) |
| Rules | `SDS_EndpointContract.cs` (the hard gates / ordering section), `SDS_BillIngredientQuery.cs` (the supply planning section), `SDS_Buildings.cs` (the `SDS_EndpointResolver` section) |
| Vanilla | `Patch_SDS_Core.cs` (now `Patch_VanillaSeams.cs`), `SDS_AccessPoints.cs`, `SDS_BillIngredientQuery.cs` (the missing-ingredient query section), `SDS_Buildings.cs` (buildings / buffer / handles), `SDS_CaravanTransport.cs`, `SDS_Comp_DrawExtraOverlay.cs`, `SDS_Destinations.cs`, `SDS_GameComponent_Grid.cs` (shell / state / mirrors), `SDS_ReturnAndReclaim.cs` (the reclaim section), `SDS_VirtualDisplay.cs` (display-thing factory / identity table), `SDS_Hotkeys.cs`, `SDS_Implants.cs` (patches / JobDrivers), `SDS_InstallDesignator.cs`, `SDS_JobTargets.cs`, `SDS_MirrorBehavior.cs`, `SDS_MirrorGuardRegistry.cs`, `SDS_Mirrors.cs`, `SDS_PreservedBill.cs`, `SDS_PreservedOffer.cs`, `SDS_PreservedUsage.cs`, `SDS_ProjectionOwnerBridge.cs`, `SDS_Proxies.cs`, `SDS_Search.cs`, `SDS_Selection.cs`, `SDS_Transmission.cs`, `SDS_Trade.cs`, `SDS_UI.cs`, `SDS_Use.cs`, `SDS_VirtualBillFallback.cs`, `SDS_Mod.cs` (the `SDS_ResearchUtility` fragment), `SDS_SelfCheck.cs` (the DebugAction / mirror / vault / worktype enum check fragments), `SDS_VanillaDiagnostics.cs` |
| Api | `SDS_API.cs`, `SDS_API_Adapters.cs` |
| ThirdModCompat | `SDS_CompatServices.cs` (the Sdk utility classes), `SDS_ModCompat_PUAH.cs`, `SDS_ModCompat_CE.cs`, `SDS_ModCompat_UFRecipes.cs`, `SDS_ModCompat_ManipulatorBeam.cs`, `SDS_ModCompat_MiliraExpanded.cs`, `SDS_ModCompat_CeleTechShuttle.cs`, `SDS_ModCompat_EquivalentExchange.cs`, and their respective `SDS_CompatAdapter_*.cs` |
| Bootstrap | `SDS_Mod.cs` (the mod / settings page), `SDS_ModCompat.cs` (converted into the scanner) |
