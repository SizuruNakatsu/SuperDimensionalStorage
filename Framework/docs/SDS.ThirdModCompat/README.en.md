# SDS.ThirdModCompat

> Role: a single assembly for third-party mod compatibility. One dll contains 5 Sdk shared utility classes and 9 adapters; target mods are only probed via reflection, never statically referenced. The binding constraints are in ModuleRules.en.md 6.6 / 7 / 8 and ModuleResponsibilities.en.md 2.

## 1. Responsibilities

### 1.1 Sdk utility classes (`SDS_CompatServices.cs`)

| Class | Responsibility |
|---|---|
| `SDS_CompatTools` | Reflection wrappers (`Method` / `Property` / `PropertyGetter` / `Field` / `GetBool`) and the sole entry point for `TryPatch` hook points |
| `SDS_CompatEndpoint` | Endpoint existence: `HasAnyEndpoint` / `HasUsableBuildingEndpointWithin` |
| `SDS_CompatStock` | Available count by def (virtual + preserved combined), withdrawal, and return |
| `SDS_CompatBillSupply` | Preserved ingredient supply refill service: candidates + the vanilla ingredient-selection algorithm + preserved withdrawal |
| `SDS_CompatMirrorSeam` | The sole implementation of endpoint materialization before third-party Job reservations: resolve the endpoint cell / withdraw goods / copy comp fields / place / rewrite target / return to ledger on failure; shared by same-shape adapters |

### 1.2 Adapters (`SDS_CompatAdapter_*.cs` + `SDS_ModCompat_*.cs`)

| Adapter | Target mod | Responsibility |
|---|---|---|
| `SDS_CompatAdapter_PUAH` | Pick Up And Haul | Haul Job detection and unload dispatch takeover |
| `SDS_CompatAdapter_CE` | Combat Extended | Reload / turret ammo refill / ammo mirror translation |
| `SDS_CompatAdapter_UFRecipes` | UF Light Industries | Preserved ingredients after printer ingredient-pull failure |
| `SDS_CompatAdapter_ManipulatorBeam` | Manipulator Beam Emitter | Special container type / scan table / transfer source |
| `SDS_CompatAdapter_MiliraExpanded` | Milira Expanded | Bill supply refill for the virtual production device / orders |
| `SDS_CompatAdapter_CeleTechShuttle` | CeleTech Shuttle Extension | Read-only loading manifest injection |
| `SDS_CompatAdapter_EquivalentExchange` | ProjectE | Transmutation dialog display thing ↔ true body translation |
| `SDS_CompatAdapter_Versatran` | VersatranZeroOne | Extracting mirror rows during vehicle cargo bay loading / loading true bodies into the bay |
| `SDS_CompatAdapter_MiliraGift` | Milira Raising: The One Entwined With You | Endpoint materialization and targetA rewriting before gift Job reservations |

Hook point counts, packageIds, and Harmony IDs: see Interfaces.en.md §F.1; individual hook points and takeover methods: see §F.2.

This module's iron rules are Rules.en.md R1–R11 (no hard references to target assemblies, access only via `SDS_API`, idempotent `Apply`, dynamic hook points not counted in the 112, etc.); they are not repeated here.

## 2. Dependencies and Artifacts

| Item | Value |
|---|---|
| Assembly | `SDS.ThirdModCompat.dll` |
| ProjectReference | `SDS.Contracts`, `SDS.Diag`, `SDS.Api` |
| Also required | Harmony (`SdsHarmonyRef`, `0Harmony`) |
| Not allowed | `SDS.Core`, `SDS.Vanilla`, any third-party dll |

## 3. Public Surface

The public surface is 0: the 5 Sdk utility classes and 9 adapters are all `internal` (ModuleResponsibilities.en.md 2). Bootstrap does not statically reference this assembly; it discovers and invokes adapters by reflectively enumerating `ISDSCompatAdapter`, and tests reference internal types directly via the registered `InternalsVisibleTo("SDS.Tests")`. The whitelist is checked at the type level by `Tools/CheckAssemblyBoundaries.ps1`.

## 4. Document Map

| File | Content |
|---|---|
| [README.en.md](README.en.md) | This file: role, responsibilities, dependencies and artifacts, public surface, map |
| [Interfaces.en.md](Interfaces.en.md) | Sdk signatures, the 9 adapters' identities and `SDS_API` call inventory, Contracts types, call relationships, and the adaptation facts in the "Vanilla Support" and "Third-Party Support" sections |
| [Rules.en.md](Rules.en.md) | This module's checkable rules R1–R11 and the corresponding verification methods |
| [DesignDecisions.en.md](DesignDecisions.en.md) | Historical decisions and pitfalls, old-implementation → new-implementation differences and pitfalls, design trade-offs and boundaries |

## 5. Sources

- Rules: `Framework/ModuleRules.en.md` 6.6 / 7 / 8; `ModuleResponsibilities.en.md` 2; `ModuleInterfaces.en.md` 6.
- Code: all sources under `超维存储/Source/SDS.ThirdModCompat/`; `SDS.Bootstrap/SDS_CompatLoader.cs`.
- Tests: `超维存储/Source/SDS.Tests/Compat/CompatAdapterTests.cs`; run tests via `Tools/TestModule.ps1` (build the target module first, then compile only the test project and compare against the production DLL; the test build must never rebuild the production DLL).

Reading this directory is enough to start work; the only additional reading required is ModuleRules.en.md.
