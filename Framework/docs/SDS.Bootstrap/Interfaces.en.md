> Positioning: lists SDS.Bootstrap's public surface, the actual call sites of the 7-step startup, the 11 objects injected by `SDS_HostInstaller`, the cross-module members consumed by this module, and the interaction surface with RimWorld / third parties; the source code is authoritative for signatures.

# SDS.Bootstrap Interfaces

Source paths are all relative to `超维存储/Source/SDS.Bootstrap/`. The authoritative rules for the startup sequence are in ModuleRules.en.md §9; this document only registers where things land.

## 1. Section A: public surface of `SDS_Mod`

```csharp
public class SDS_Mod : Mod
{
    public static SDS_Settings Settings;              // SDS_Mod.cs 16
    public SDS_Mod(ModContentPack content);            // 24–34
    public override string SettingsCategory();         // 36–39
    public override void DoSettingsWindowContents(Rect inRect); // 41–138
    public override void WriteSettings();              // 315–334
}
```

| Member | Behavior |
|---|---|
| Constructor | `GetSettings<SDS_Settings>()` → non-null `ClampAll()` → `SDS_Config.Current = Settings` → `SDS_Startup.Run(Settings)` (26–33) |
| `SettingsCategory` | Returns `"SDS.Settings.Category".Translate()` |
| `DoSettingsWindowContents` | First notifies Vanilla that the settings page has been drawn; while a hotkey capture is in progress, draws only the mask; otherwise draws the scroll area, with `ClampAll()` before the end |
| `WriteSettings` | `SDS_Vanilla.CancelHotkeyCapture()` → `ClampAll()` → `SDS_SettingsApplier.ApplyPowerAndPriority` + `RefreshWealth` (try/catch logging `Log.Error`) → `base.WriteSettings()` |

Settings page entries (field definitions and defaults / ranges live in Contracts' `SDS_Settings` / `SDS_Config`):

| Control | Field | Side effects |
|---|---|---|
| Slider | `gridCapacity` | None (Core reads `SDS_Config` read-only) |
| Slider | `capacityPerArray` | None (Vanilla reads `SDS_Config` read-only) |
| Slider | `corePower` / `arrayPower` / `portPower` | On window close, writes building Def power consumption |
| Checkbox | `separateByQuality` / `separateByStuff` | None (Core read-only) |
| Checkbox | `excludeGridFromWealth` | `RefreshWealth()` on window close |
| Checkbox | `playerFactionOnlyAccess` | First check pops a confirmation dialog first; set to true after confirmation; no other side effects (Vanilla reads `SDS_Config` live on every access; the gate is checked before the access-point cache) |
| Checkbox | `highestStoragePriority` | On change, `SDS_Rules.NotifyEndpointChanged()` + `SDS_Vanilla.ApplyStoragePriorityToEndpoints()`; when enabled, also turns off `allowPriorityOutflow` |
| Checkbox | `allowPriorityOutflow` | First check pops a confirmation dialog first; after confirmation it is set to true and `highestStoragePriority` is turned off (if it was originally on, priorities are refreshed in sync); cancelling keeps it off |
| Checkbox | `forceWeaponApparelVirtual` | First check reverts to `false` and pops a confirmation dialog; after confirmation it is set to true, and in game calls `SDS_Rules.ConvertPreservedGearToVirtual()` |
| Hotkey section | Vanilla `KeyPrefs` (Open / Output / Use) | Set / clear / restore-default all delegate to `SDS_Vanilla` |
| Button | `ResetToDefaults()` | Immediately `SDS_SettingsApplier.ApplyPowerAndPriority` + `RefreshWealth` |

ModSettings reads and writes:

- Read: `GetSettings<SDS_Settings>()`; its implementation lives in Contracts together with the save-node read/write implementation (ModuleInterfaces.en.md §8.3); this module adds no new nodes.
- Write: only the clamping and apply side effects of `WriteSettings`; persisting fields to disk is triggered by `base.WriteSettings()`.
- Whether other public types are exposed: no. The whole assembly has only `SDS_Mod` as a top-level public type, doubly constrained by `SDS.Tests/Bootstrap/BootstrapContractTests.cs` 21–34 and the `CheckAssemblyBoundaries.ps1` whitelist.

## 2. Section B: actual call sites of the 7-step startup

| Step | Action | Call | Location |
|---|---|---|---|
| 1 | Settings reading and clamping | `SDS_Mod` constructor: `GetSettings` / `ClampAll` / `SDS_Config.Current =` / `SDS_Startup.Run` | `SDS_Mod.cs` 26–33 |
| 2 | Host injection | `SDS_Startup.Run` → `SDS_HostInstaller.Install()` → `SDS_Host.Install(SDS_HostSlots)` | `SDS_Startup.cs` 37; `SDS_HostInstaller.cs` 15–50 |
| 3 | Harmony installation | `SDS_PatchInstaller.Install()` | `SDS_Startup.cs` 43 |
| 4 | Initial adapter scan | `SDS_CompatLoader.Scan()` | `SDS_Startup.cs` 48 |
| 5 | Module registration | `SDS_Core.RegisterAll()`, `SDS_Rules.RegisterAll()`, `SDS_Vanilla.RegisterAll()` | `SDS_Startup.cs` 61–63 |
| 6 | Deferred apply | `LongEventHandler.ExecuteWhenFinished(RunDeferred)` → `SDS_SettingsApplier.ApplyPowerAndPriority`, `SDS_PatchInstaller.ApplyRuntimeHooks`, `SDS_CompatLoader.Scan` | `SDS_Startup.cs` 72, 85–87 |
| 7 | Startup report | `SDS_Vanilla.ReportUseProvidersOnce()`, `SDS_CompatReportHub.LogAll()`, `SDS_SelfCheck.Run(false)`, a consolidated `Log.Message` | `SDS_Startup.cs` 92–114 |

Additional conventions:

- Idempotency flags: `started` prevents repeated initialization (18–25), `deferredRan` prevents re-entry of the deferred segment (76–81); `SDS_HostInstaller.installed` prevents duplicate host injection.
- Step 5 has an independent try/catch: even if steps 1–4 fail, registration still proceeds (58–69).
- The rules' "Step 7: entering the game / loading a save" is taken over by Vanilla's `SDS_GameComponent_Grid` constructor; Bootstrap does not re-implement it (the comment at `SDS_Startup.cs` 10 and the log at 113).

The 11 objects injected by `SDS_HostInstaller` (filled into the `SDS_HostSlots` fields, `SDS_HostInstaller.cs` 23–35):

| Slot | Slot field type | Interface |
|---|---|---|
| `Ports` | `SDS_EndpointProvider` | `ISDS_PortProvider` |
| `DepositFilter` | `SDS_VanillaServices` | `ISDS_DepositFilter` |
| `Priority` | Same instance as above | `ISDS_PrioritySource` |
| `Delivery` | Same instance as above | `ISDS_DeliverySink` |
| `MirrorQuery` | `SDS_MirrorService` | `ISDS_MirrorQuery` |
| `MirrorPhysical` | `SDS_MirrorPhysicalOps` | `ISDS_MirrorPhysical` |
| `MedicalCare` | `SDS_MedicalCareAdapter` | `ISDS_MedicalCarePolicy` |
| `ReclaimWorld` | `SDS_ReclaimWorldAdapter` | `ISDS_ReclaimWorld` |
| `Display` | `SDS_DisplayAdapter` | `ISDS_DisplayQuery` |
| `DepositJobs` | `SDS_DepositJobsAdapter` | `ISDS_DepositJobs` |
| `TransportLoad` | `SDS_TransportLoadAdapter` | `ISDS_TransportLoadSink` |

- A single `SDS_VanillaServices` instance occupies 3 slots; every other slot gets a new instance.
- After injection, each slot is checked for non-null, counted, and returned; the startup log reports `N/11`; no exception is thrown because a slot is null (ModuleInterfaces.en.md §1.2).

## 3. Section C: consumed cross-module members

| Source | Member | Purpose |
|---|---|---|
| Contracts | `SDS_Settings` (fields, `ClampAll`, `ResetToDefaults`) | Settings page read/write and clamping |
| Contracts | `SDS_Config` (`Current`, `*Min` / `*Max`, `*DefName`) | First-write instance, slider ranges, building Def names |
| Contracts | `SDS_Host` (11 slots + `Install`) | Host injection |
| Contracts | `SDS_PreserveHooks` (`RequiresPreserved`) | Wired into Core as the external preserved-classification delegate |
| Contracts | `ISDSCompatAdapter` (`Name` / `TargetPackageId` / `IsTargetLoaded` / `Apply`) | Reflective scanning and execution |
| Diag | `SDS_Diag.ErrorOnce` | All failure branches |
| Diag | `SDS_StaticState.RegisteredCount` | Step 5 and report counts |
| Diag | `SDS_SelfCheck.Count` / `SDS_SelfCheck.Run(false)` | Provider counts and self-check output |
| Diag | `SDS_CompatReportHub.LogAll()` / `Has` / `Register` | Report output and supplementary registration |
| Core | `SDS_Core.RegisterAll()` | Registers Core's self-check provider |
| Rules | `SDS_Rules.RegisterAll()`, `SDS_Rules.NotifyEndpointChanged()`, `SDS_Rules.SetExternalPreserveProvider(...)`; after W34 confirmation, `SDS_Rules.ConvertPreservedGearToVirtual()` | Static cleanup / self-check registration, endpoint contract notification, external preserved wiring, existing-stock migration |
| Vanilla | `SDS_PatchInstaller.Install` / `ApplyRuntimeHooks` / `AppliedCount` / `DeclaredCount` | Patch installation and counting |
| Vanilla | `SDS_Vanilla.RegisterAll` / `NotifySettingsPageDrawn` / `HotkeyCapturing` / `HotkeyCaptureAction` / `HotkeyCaptureNote` / `GetHotkeyLabel` / `BeginHotkeyCapture` / `CancelHotkeyCapture` / `ClearHotkey` / `ResetHotkeys` / `ApplyStoragePriorityToEndpoints` | Settings page and hotkeys |
| Vanilla | `SDS_GameComponent_Grid.Instance` | Save-shell existence check (`SDS_Vanilla.HasGrid`); the W33 conversion now calls Rules instead |
| Vanilla | 9 host implementation classes (see the Section B table) | Host injection |
| Api | None | The project reference exists (allowed by the matrix); the source code contains no `SDS_API` calls |

## 4. Call relationships

```text
RimWorld → SDS_Mod(ModContentPack)
  └→ SDS_Startup.Run(settings)
       ├→ SDS_HostInstaller.Install() ─→ SDS_Host.Install(SDS_HostSlots)
       ├→ SDS_PatchInstaller.Install()        [SDS.Vanilla]
       ├→ SDS_CompatLoader.Scan() ─→ ISDSCompatAdapter.Apply()
       ├→ SDS_Core.RegisterAll() / SDS_Rules.RegisterAll() / SDS_Vanilla.RegisterAll()
       └→ LongEventHandler.ExecuteWhenFinished
            └→ SDS_SettingsApplier.ApplyPowerAndPriority() → SDS_Rules.NotifyEndpointChanged / SDS_Vanilla.ApplyStoragePriorityToEndpoints
               SDS_PatchInstaller.ApplyRuntimeHooks() / SDS_CompatLoader.Scan()
               LogReport() → SDS_CompatReportHub.LogAll() / SDS_SelfCheck.Run(false)

设置页：SDS_Mod.DoSettingsWindowContents / WriteSettings → SDS_Settings 字段 + SDS_SettingsApplier
```

## 5. Vanilla support

The sequencing rules for the Vanilla lifecycle are in ModuleRules.en.md §9; this section only registers the Vanilla types, methods, and timings that Bootstrap touches.

### 5.1 Interaction surface overview

| Vanilla API / mechanism | Use in Bootstrap | Location | Origin |
|---|---|---|---|
| `Mod` base class and `ModContentPack` construction | RimWorld discovers and constructs the sole entry point | `SDS.Bootstrap/SDS_Mod.cs` 24 | Old `SDS_Mod.cs` 31 |
| `Mod.GetSettings<T>()` | Obtains the `SDS_Settings` instance | Same file 26 | Old `SDS_Mod.cs` 33 |
| `ModSettings` / `Scribe_Values.Look` | Persisting settings to disk and reading them back (implemented in Contracts) | `SDS.Contracts/SDS_Mod.cs` 21–34 | Old `SDS_Mod.cs` 606–619; ModuleInterfaces.en.md §8.3 |
| `Mod.SettingsCategory` | Settings page category name | `SDS.Bootstrap/SDS_Mod.cs` 36–39 | Old `SDS_Mod.cs` 124–127 |
| `Mod.DoSettingsWindowContents` | Settings page drawing | Same file 41–138 | Old `SDS_Mod.cs` 129–225 |
| `Mod.WriteSettings` | Clamping and side effects before the window closes | Same file 315–334 | Old `SDS_Mod.cs` 396–413 |
| `Listing_Standard` / `Widgets` / `Mathf` | Sliders, checkboxes, scroll area | Same file 54–135 | Old `SDS_Mod.cs` 138–222 |
| `Dialog_MessageBox.CreateConfirmation` / `Messages.Message` / `Find.WindowStack` | W33 forced-virtualization confirmation and blocking-window determination | Same file 144–210 | Old `SDS_Mod.cs` 231–298; DiagnosticsCoverage.en.md §H |
| `Current.ProgramState` | Determines whether the game is running | Same file 146, 163, 168 | Old `SDS_Mod.cs` 233, 239 |
| `HarmonyLib.Harmony` | Creates the Vanilla patch instance | Vanilla `SDS_PatchInstaller.cs` 60 | Old `SDS_Mod.cs` 62 |
| `AccessTools.GetTypesFromAssembly` | Enumerates this assembly's patch classes | Same file 61 | Old `SDS_Mod.cs` 64 |
| `LongEventHandler.ExecuteWhenFinished` | Schedules step 6 to run after loading completes | `SDS.Bootstrap/SDS_Startup.cs` 72 | Old `SDS_Mod.cs` 37–39 |
| `DefDatabase<ThingDef>.GetNamedSilentFail` | Obtains building Defs | `SDS.Bootstrap/SDS_SettingsApplier.cs` 68 | Old `SDS_Mod.cs` 467 |
| `Def.GetCompProperties<CompProperties_Power>()` | Obtains the power-comp properties | Same file 73 | Old `SDS_Mod.cs` 472 |
| Two `CompProperties_Power` fields (reflection) | Writes power consumption and the "needs power" alert | Same file 16–21, 86–92 | Old `SDS_Mod.cs` 24–29, 484–490 |
| `CompPowerTrader.SetUpPowerVars` | Makes spawned buildings recompute their power consumption immediately | Same file 101–124 | Old `SDS_Mod.cs` 498–530 |
| `Find.Maps` / `map.listerThings` | Enumerates spawned buildings | Same file 103–123 | Old `SDS_Mod.cs` 503–529 |
| `map.wealthWatcher.ForceRecount` | Recomputes wealth immediately after a setting is toggled | Same file 46–60 | Old `SDS_Mod.cs` 420–435 |

### 5.2 Startup timing

- The Harmony instance is created inside Vanilla's `SDS_PatchInstaller.Install()` (`SDS_PatchInstaller.cs` 54–60); the call site is step 3 (`SDS_Startup.cs` 43); it returns immediately if the static `harmony` is non-null, so it is installed only once per process.
- Installation is per class via `harmony.CreateClassProcessor(type).Patch()`, not `Harmony.PatchAll`; a single failing patch class is only logged and skipped, while the remaining patches continue. The two dynamic registries (`SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge`) and the W63 destination hook points are also installed inside `Install()` (`SDS_PatchInstaller.cs` 72–99), and step 6's `ApplyRuntimeHooks()` runs an idempotent fallback pass again (`SDS_Startup.cs` 86). The ID is `sizuru.sds.vanilla` (ModuleRules.en.md §8); third-party dynamic patches are not installed here — adapters create their own instances inside `Apply`.
- At the end, `SDS_Startup.Run` calls `LongEventHandler.ExecuteWhenFinished(RunDeferred)` (`SDS_Startup.cs` 72): the synchronous segment only does settings, hosts, Vanilla patches, the initial adapter scan, and module registration, with step 6 placed into the callback; the reason is that when `Mod` is constructed the Defs are not fully loaded yet, so writing `ThingDef`'s `CompProperties_Power` has to wait until loading finishes.
- When the callback runs there may be no game (main menu / loading phase): `RefreshWealth` and `RefreshSpawnedPowerComps` both check `Current.ProgramState` / `Current.Game` / `Find.Maps` at the start to avoid null references (`SDS_SettingsApplier.cs` 48, 103); Def-level writes execute before the null checks, so they happen whenever the Defs exist.
- `DebugAction` is not in Bootstrap: the only entry point is Diag's `SDS_SelfCheck.RunLedgerChecks` (`SDS.Diag/SDS_SelfCheck.cs` 44), under the developer menu "SuperDimensionalStorage → Self-check: ledger / reservation / mirror". `Prefs.DevMode` is read by Diag assertions and Vanilla's `SDS_GameComponent_Grid`; Bootstrap does not read `Prefs`, and step 7 only calls `SDS_SelfCheck.Run(false)` to output the self-check (`SDS_Startup.cs` 106). The settings page's W33 confirmation dialog is player-visible UI, determined via `Current.ProgramState` and the window type name, and is not a developer entry point (`SDS_Mod.cs` 144–210).

### 5.3 TBD

- The actual number of times `ExecuteWhenFinished` fires on the "opening Mod settings directly from the main menu" and "loading a save" paths (TBD; needs in-game logs).
- Whether the `CompProperties_Power` `basePowerConsumption` field name changes across RimWorld versions (TBD; must be verified against the target version).
- `DebugAction` menu visibility outside developer mode (TBD; needs in-game confirmation; same origin as the TBD item under Vanilla support in SDS.Diag/Interfaces.en.md).

## 6. Third-party support

The adapters' own translation responsibilities are defined in ModuleRules.en.md §6.6, ModuleResponsibilities.en.md §8, and ModuleInterfaces.en.md §6; this section covers only the Bootstrap side.

### 6.1 The contract surface Bootstrap exposes to third parties

| Object | Role | How Bootstrap touches it |
|---|---|---|
| `ISDSCompatAdapter` (Contracts) | Adapter identity and target-probing interface | Enumerates implementing types via reflection, instantiates them, and calls them through the interface (signatures in ModuleInterfaces.en.md §1.2) |
| `SDS_CompatReportHub` (Diag) | Adapter report registration and output | Only calls `LogAll()`; the read side additionally has the `Snapshot()` deep copy |
| `SDS_Diag.ErrorOnce` / `Warn` (Diag) | Scan-time exception logging | Called when the scanner itself fails |
| The `SDS.ThirdModCompat` assembly | 9 adapters and the Sdk utility classes | Not statically referenced; discovered only via the interface |

Bootstrap exposes no `public` entry point to third parties: `SDS_Mod` serves only RimWorld Mod discovery (ModuleResponsibilities.en.md §2). The third-party integration point is `SDS_API` (`Version = 4`, probed via soft-depend reflection; see ModuleResponsibilities.en.md §7 and PublicAPI.en.md §3.8), not Bootstrap; adapters only implement `ISDSCompatAdapter` and are discovered by Bootstrap's scan without referencing Bootstrap (rationale in [DesignDecisions.en.md](DesignDecisions.en.md) §3.2).

### 6.2 Adapter scanning mechanism

Scanning happens at startup step 4 and is re-run once in step 6's `ExecuteWhenFinished` (sequence in ModuleRules.en.md §9.4). The implementation lives in `SDS.Bootstrap/SDS_CompatLoader.cs`.

| Phase | Approach | Source |
|---|---|---|
| Enumerate assemblies | `AppDomain.CurrentDomain.GetAssemblies()`; skips null and dynamic assemblies | 47–57 |
| Get types | `assembly.GetTypes()`; on `ReflectionTypeLoadException` takes `e.Types`; other exceptions return an empty array | 173–187 |
| Filter | Skips abstract classes, interfaces, and non-`ISDSCompatAdapter` implementations | 59–68 |
| Instantiate | `Activator.CreateInstance(type)`; failures are logged with `ErrorOnce` and skipped | 75–85 |
| Read identity | Failures reading `Name` / `TargetPackageId` are logged with `ErrorOnce`; an empty name falls back to `FullName` | 91–107 |
| Name deduplication | `ownerByName` keyed by `Name`; same name but a different type → logs `ErrorOnce` and skips the later registrant | 109–123 |
| Target detection | `IsTargetLoaded` false → counted only in `notLoaded`; no `Apply` call and no `attempted` write | 125–142 |
| Idempotency | Returns directly if `attempted` already contains the name; **register first, then execute**, so an `Apply` exception is never retried on re-scan | 143–160 |
| Execute | `appliedCount` is incremented only when `adapter.Apply()` succeeds | 150–154 |
| Exception isolation | An `Apply` exception is logged with `ErrorOnce`, and `RegisterBrokenReport` adds a 0/0 record | 155–171 |

- When the target is not installed, everything stays silent: no `Apply`, no report registration; when the target is installed but key-member probing fails, the adapter itself registers `detectedButBroken` (DiagnosticsCoverage.en.md §3).
- The scanner holds no adapter instances: every `Scan` recreates them via `Activator.CreateInstance`, so adapters must have a parameterless constructor (`SDS_CompatLoader.cs` 75–85).
- The scanner does not call `SDS_Diag.Fail`: the Compat layer only allows `ErrorOnce` / `Warn` / the report hub (ModuleRules.en.md §6.6).

### 6.3 Startup report and the third-party visible surface

- The output semantics of `SDS_CompatReportHub.LogAll()` (`detected` filtering, the two 0/0 branches, the failure-detail format) are governed solely by SDS.Diag/Interfaces.en.md §5.2; Bootstrap only calls it at startup step 7 and does not restate the rules. The read-side `Snapshot()` returns a per-item deep copy; the startup report uses `LogAll()` for in-place output and holds none of the Hub's internal lists.
- Reports are written only on the main thread: `Apply` is invoked by Bootstrap on the main thread, and the Hub's internal `List` is lock-free (ModuleResponsibilities.en.md §4).
- The settings page does not draw `endpointStoragePriority` (adjusted from the Hyperdimensional Core's interface); what third parties read via `ISDS_PrioritySource` is `SDS_Config.CoreStoragePriority` (`SDS.Vanilla/SDS_VanillaState.cs` 125–137).

### 6.4 TBD

- For the 9 adapters under real Mod combinations, the hit differences between the step-4 initial scan and the step-6 re-scan (TBD; needs in-game logs).
- Whether `SDS_CompatReportHub.Reset()` needs to be cleared on save load or re-scan (Bootstrap currently does not call it; TBD).
- Whether third-party caches need an extra invalidation notification after settings page toggles change; currently the only direct notifications are `SDS_Rules.NotifyEndpointChanged()` and `SDS_Vanilla.ApplyStoragePriorityToEndpoints()` (TBD).
