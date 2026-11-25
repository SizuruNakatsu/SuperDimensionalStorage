# SDS.ThirdModCompat Rules

> Role: the executable, checkable rule list for this module. Global constraints: ModuleRules.en.md 6.6 / 7 / 8; responsibilities: ModuleResponsibilities.en.md 8; signatures: Interfaces.en.md. Each rule states only "requirement + how to check", without re-explaining the rationale.

## 1. Rules

### R1 No hard references to target assemblies

- Requirement: the `ProjectReference` entries of `SDS.ThirdModCompat.csproj` are only `SDS.Contracts` / `SDS.Diag` / `SDS.Api`; Harmony comes in via `SdsHarmonyRef`; no target mod's dll or `Reference` may appear (RimWorld / Unity base assemblies excepted).
- Check: `Tools/CheckAssemblyBoundaries.ps1` (reference graph + Harmony reference whitelist); `dotnet build SDS.ThirdModCompat.csproj` must succeed on a machine without the target mods installed; manual csproj review.
- Failure signature: the script reports `[引用越界]` / the build requires a third-party dll.

### R2 Read and write only via `SDS_API`

- Requirement: all of an adapter's stock / endpoint / withdrawal / deposit / rollback operations call only `SDS_API` (or forward through `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply`); no references to `SDS.Core` / `SDS.Vanilla` implementations or internal types; no direct access to the ledger, transactions, or the mirror / display thing registries. `SDS_CompatTools` does only reflection and Harmony, never reads or writes game state.
- Check: `Tools/CheckCompatSurface.ps1` (entry point ownership + call chains + compat-layer call graph); the compat-layer allowlist in `Tools/CheckPatchInventory.ps1` (only `SDS_API` / `SDS_Compat*` / `SDS_ModCompat*` allowed); `CompatAdapterTests.ApiBridge_ForwardsDisplayMirrorAndProducedFacts`.
- Failure signature: the script reports `[调用链门面未知]` / `[兼容直调]`; the test fails on references.

### R3 Do not call `SDS_Diag.Fail`

- Requirement: failure paths may use only `SDS_Diag.ErrorOnce` / `SDS_Diag.Warn` / `SDS_CompatReportHub`; normal rejections are silent.
- Check: `CompatAdapterTests.CompatSources_DoNotCallDiagFail` (scans `SDS_ModCompat_*.cs`); additionally `grep -n "SDS_Diag.Fail" SDS.ThirdModCompat` must return 0 lines (also applies to the Sdk utility classes).
- Note: `CheckCompatSurface.ps1` does not currently scan this string; the test + grep are authoritative.

### R4 Names and Harmony IDs are unique

- Requirement: the 9 adapters' `Name`, `TargetPackageId`, and `HarmonyId` values are mutually distinct, and `HarmonyId` matches ModuleRules.en.md 8; new adapters must satisfy both at once.
- Check: `CompatAdapterTests.AdapterIdentity_MatchesContract` + `HarmonyIds_AreUniqueAndMatchContract`; at runtime `SDS_CompatLoader` records same-name-different-type cases as `duplicates++` + `ErrorOnce` and skips the later registrant.
- Failure signature: test assertions fail; the startup log shows "duplicate adapter name (Harmony ID)".

### R5 `Apply` runs exactly once

- Requirement: each adapter uses a private static `applied` flag for idempotency; `Apply` creates `new Harmony(HarmonyId)` exactly once; Bootstrap re-scans must not apply twice.
- Check: manual review of the `applied` branches in the 9 `SDS_CompatAdapter_*.cs`; the `attempted` set in `SDS_CompatLoader.Process` (register first, then execute; exceptions are not retried); the startup log's `AppliedCount` must not exceed 9.
- TBD: there is currently no dedicated test for `Apply` idempotency; Tests could invoke it twice via reflection and assert the hook point count is unchanged.

### R6 Adapters never call each other

- Requirement: `SDS_ModCompat_*` files must not reference / call each other; shared logic may go only into `SDS_CompatTools` / `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply`.
- Check: `grep -n "SDS_ModCompat_[A-Za-z]*\." SDS.ThirdModCompat/SDS_ModCompat_*.cs` — hits may only be the file's own type name; the infrastructure-class whitelist in `Tools/CheckCompatSurface.ps1` (`SDS_CompatServices.cs` allows only 4 classes).
- Failure signature: cross-adapter calls appear; the script reports `[基础设施多类]`.

### R7 Static cleanup is registered only for CE and Milira

- Requirement: only `SDS_CompatAdapter_CE` and `SDS_CompatAdapter_MiliraExpanded` implement `ISDSResettable`, and only after `Apply` succeeds do they call `SDS_StaticState.Register(this)`; only cross-save caches are cleaned, process-level reflection constants are not registered.
- Check: `CompatAdapterTests.CeAndMilira_DeclareStaticCleanup`; manually confirm the other 6 adapters have no `ISDSResettable` / `SDS_StaticState.Register`; the startup log shows "static cleanup registered: N".
- Failure signature: test assertions fail; `SDS_StaticState` gains extra third-party entries.

### R8 Dynamic hook points are not counted in the 112

- Requirement: no `[HarmonyPatch]` attribute classes may appear in this module; the 9 adapters' hook points all use runtime `harmony.Patch` and are recorded only in the "dynamic patches" section of PatchInventory.en.md.
- Check: `Tools/CheckPatchInventory.ps1` (Vanilla attribute classes fixed at 112; every `SDS_ModCompat_*.cs` that enables `TryPatch` must have a dynamic registration line); `grep -n "\[HarmonyPatch" SDS.ThirdModCompat` must return 0 lines.
- Failure signature: the script reports `[动态缺行]` / the attribute class count drifts.

### R9 Exception isolation policy

- Requirement: a single adapter's instantiation / identity read / target detection / `Apply` / hook point application failures affect only itself: `SDS_CompatLoader` and `SDS_CompatTools.TryPatch` each have their own try/catch, failures are logged with `ErrorOnce` and processing continues; nothing may propagate up to Bootstrap, and the mod must not be blocked from starting.
- Check: manual review of the try/catch boundaries in `SDS_CompatLoader.Process`, `SDS_CompatTools.TryPatch`, and the 9 `TryApply` methods; the startup report should show "detected, hook points 0/N (reason)" instead of a startup exception.
- TBD: there is currently no automated "one adapter's exception does not affect other adapters" test; fake adapters could be added.

### R10 Steps to add a new adapter

1. Create `SDS_CompatAdapter_<Name>.cs` (`public sealed`, implements `ISDSCompatAdapter`) and `SDS_ModCompat_<Name>.cs` (`internal static`, holding only hook points and translations).
2. Give `Name` and `HarmonyId` unique values; `HarmonyId` follows the pattern `sizuru.sds.compat.<name>`; `TargetPackageId` uses the target mod's real packageId; `IsTargetLoaded` probes only via `AccessTools.TypeByName`.
3. Make `Apply` idempotent with `applied`, do `new Harmony(HarmonyId)`, and route all hook points through `SDS_CompatTools.TryPatch`; implement and register `ISDSResettable` only if there are cross-save caches.
4. Go through `SDS_API` only; when `SDS_CompatEndpoint` / `SDS_CompatStock` / `SDS_CompatBillSupply` can be reused, do not write new helpers; never compute shortfalls / pick endpoints / debit the ledger inside adapters.
5. Register `name` / `packageId` / `detected` / `report` / `note` / `reason` via `SDS_CompatReportHub.Register`; record each hook point as `Applied` / `Missing`.
6. Run the checks: `CompatAdapterTests` (adapter count, identity, uniqueness, cleanup declarations, no `SDS_Diag.Fail` calls), `Tools/CheckCompatSurface.ps1`, `Tools/CheckPatchInventory.ps1`, `Tools/CheckAssemblyBoundaries.ps1`; update Interfaces.en.md section B and the "Third-Party Support" section, plus the PatchInventory.en.md dynamic section, in sync.

### R11 Adapters only translate (W17)

- Canonical sentence: adapters perform only Intent → Query translation and read-only delegated queries; `SDS_WithdrawPlan` is generated by `Decide(query)`; they must not pick endpoints on their own, decide available counts, read `preservedOwner` directly, or create transactions or reservations.
- Check: manual review of each `SDS_ModCompat_*.cs` withdrawal call chain; the call chain / entry point tables in `CheckCompatSurface.ps1` guarantee that every adapter goes through the `SDS_API` facade only.
- Registration: the W17 row in RuleIndex.en.md.

## 2. Verification Commands

| Script / test | Rules covered |
|---|---|
| `Tools/CheckAssemblyBoundaries.ps1` | R1, R10 (the public whitelist constrains entry types separately) |
| `Tools/CheckCompatSurface.ps1` | R2, R6 (infrastructure-class whitelist), R10 |
| `Tools/CheckPatchInventory.ps1` | R2, R8, R10 |
| `SDS.Tests/Compat/CompatAdapterTests.cs` | R3, R4, R7, R10 |

## 3. Sources

- Rule body: `Framework/ModuleRules.en.md` 2.3 / 4 / 6.6 / 7 / 8 / 9; `ModuleResponsibilities.en.md` 8.
- Code facts: `SDS.ThirdModCompat.csproj`, `SDS_CompatServices.cs`, the 9 `SDS_CompatAdapter_*.cs`, `SDS_CompatLoader.cs`.
- Script facts: `超维存储/Source/Tools/Check*.ps1`; tests: `SDS.Tests/Compat/CompatAdapterTests.cs`.

## 4. Canonical Rule Text (Authoritative)

> The following are the canonical sentences from DesignDecisions.en.md's original "Related rule IDs" table that this file did not yet cover, migrated verbatim and carrying the same force as R1–R11. The canonical sentences for E6 / E1 / W17 are already covered by R1–R2 / R7 / R11 respectively and are not repeated.

| ID | Canonical sentence | Where it lands in this module |
|---|---|---|
| E7 | Every third-party entry point has exactly one write takeover path | Each adapter keeps exactly one write exit; read-only projections may coexist with translation |
| W50 | Stock visibility for third-party loading manifests can be provided only by display thing injection at the point where the manifest is built | CeleTech calls only `InjectTransportLoadCandidates` |
| S16 | Third-party supply refill fills only the shortfall; choosing def / required / shortfall / reservations happens only in `SDS_BillSupplyPlanner` | UFRecipes and Milira only provide facts and execute withdrawal / deposit |
| W30 / W58 | Third-party outfitting Jobs no longer register take counts; missing counts are filled uniformly by this mod based on the Job's reservation intent toward mirrors | Adapters must no longer provide counts; `SDS_JobFuelCount` / `RegisterJobFuelCountProvider` have been deleted |
| E5 | A Version change is a compatibility declaration | `SDS_API.Version = 4` (assembly renamed; third parties must re-soft-depend) |
| W33 | The force-virtualization toggle for weapons / apparel changes the `RequiresPreservedStorage` result | Milira's `canStoreCache` depends on that result |

> CE ammo refill policy (implementation fact, no new rule ID): the count supplied by `TryFindSupplyAmmo` = `MissingToFullMagazine ∩ stackLimit` (preferring `SelectedAmmo`; when not drafted, falling back to `CurAmmoSet` is allowed); the amount actually carried on the trip is clamped by the vanilla `TakeInventory` / CE's own reload chain — the adapter neither clamps the carried amount nor judges availability (R11).

## 5. Compatibility Boundary (Frozen, 2026-11-16)

> This section is the final boundary of "this mod's generic third-party compatibility"; new requirements must first be re-adjudicated under E3 — no ad-hoc holes may be opened inside adapters.

1. Third parties that explicitly require 1 thing (`count = 1` / `SplitOff(1)` / iterating `Thing` by `Thing`): **no count intervention**; this mod only guarantees that every single-thing withdrawal matches the real storage.
2. Only behaviors going through the vanilla seams are covered: `SplitOff` / `TryStartCarry` / `TakeToInventory` / `ThingOwner` / the vanilla driver's `Notify_Starting` / `Toils_Haul.ErrorCheckForCarry` / `ReservationManager` reservations.
3. Third parties that bypass those seams and `Destroy` mirrors directly: **fail-closed (refuse + warn)**, not supported; `ThingOwner.Remove` (no return value) is likewise refused, not supported.
4. No new adapters for individual mods; unless there is a separate L2 adjudication with the impact surface registered.
5. If a third party withdraws with `job.count ≤ 0` and has already reserved the mirror (`Reservation.StackCount`, -1 = whole stack), W58 fills the count in uniformly; third parties neither need to nor may register counts themselves.
