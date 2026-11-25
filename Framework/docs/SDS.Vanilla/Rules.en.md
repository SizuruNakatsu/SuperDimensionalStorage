# SDS.Vanilla Rules

> Purpose: the item-by-item verifiable implementation constraints of this module. The rule bodies of W / R / E are in §10 of this document (authoritative summary); the bodies of the S series are authoritative in §9 of this document; the mirror visibility conclusions are in §11; all other sections of this document only record implementation sites and verification methods.
> Signatures: see [Interfaces.en.md](Interfaces.en.md); global hard constraints: see ModuleRules.en.md; the reasoning behind the rules: see [DesignDecisions.en.md](DesignDecisions.en.md); patch counts and file distribution: see PatchInventory.en.md.

## 1. Rule Master Table

| ID | Rule | Basis | Verification |
|---|---|---|---|
| V1 | No references to `SDS.ThirdModCompat`; no target-mod packageId / type names / adapter branches may appear | ModuleRules.en.md 2.3, 6.4 | `CheckAssemblyBoundaries.ps1` reference graph; `Grep "ThirdModCompat\|Mehni.PickUpAndHaul\|CETeam.CombatExtended"` must return 0 |
| V2 | Do not invent CRUD rules: capacity, shortfall, sorting, reservations, debiting, and rollback call only `SDS_Core` / `SDS_Rules` / Contracts; Vanilla only provides facts and execution | ModuleRules.en.md 6.4 | Manual review; `Grep "SDS_Core\.\|SDS_Rules\."` checked against the whitelist in [Interfaces.en.md](Interfaces.en.md) 2.3; any new `total − reserved` style computation is a failure |
| V3 | No direct references to the internal types of Core / Rules: `SDS_WithdrawExecutor`, `SDS_WithdrawTransaction`, `SDS_ReturnUtility`, `SDS_StockRules`, and the private owners of `SDS_Ledger` (`SDS_ReclaimRegistry` has been internal to this module since C1 and is not on this list) | ModuleRules.en.md 6.4; ModuleInterfaces.en.md 3.1 | `CheckAssemblyBoundaries.ps1` type whitelist; a compile-time CS0122 is a failure |
| V4 | 112 attribute patch classes + 29 registry entries, conserved counts; patch class names unchanged; all in this assembly | ModuleRules.en.md 7 | `VanillaContractTests.PatchInventory_V2ToV5_HasOneHundredTwelvePatchClasses`; `MirrorContractTests.GuardRegistry_HasNineteenEntries` / `ProjectionOwnerBridge_HasTenEntries`; `CheckPatchInventory.ps1` |
| V5 | New vanilla seams default to Postfix + explicit `Priority.Low`; new Prefixes must explicitly declare a priority, be registered as a new row in §2 with a stated reason, and must not change ledger semantics. Existing Prefixes (including dynamic registry handlers) and classes not marked Low are kept as-is, are not retroactively ratified, and must not be cited as precedent for new classes; `Patch_SDS_ResourceCounterFastRefresh` is a throttling Prefix, and frozen-version Prefixes such as `Patch_SDS_PotentiallyMissingIngredients` remain unmarked | [DesignDecisions.en.md](DesignDecisions.en.md) 3.3 item 1; `Patch_VanillaSeams.cs` | `Grep "\[HarmonyPriority\(Priority\.Low\)\]"` + row-by-row comparison of new classes against §2; for legacy exceptions see the §2 note |
| V6 | `SDS_PatchInstaller` scans only `Assembly.GetExecutingAssembly()`, with per-class try/catch; Harmony ID is fixed at `sizuru.sds.vanilla` | ModuleRules.en.md 7, 9 | `VanillaContractTests.PatchInstaller_HarmonyId_IsStable`; `Grep "GetTypesFromAssembly"` must allow exactly 1 occurrence |
| V7 | Mirror physical operations forbid `mirror.SplitOff`, `mirror.TryAbsorbStack`, direct ledger writes, and `DeSpawn` without `Unmark`; partial takes use `SDS_OutputUtility.MakeDisplayThing` | ModuleInterfaces.en.md 4.3; W20 | Code review of the two `SDS_MirrorPhysicalOps` method bodies: no `SplitOff` / `TryAbsorbStack` / ledger writes |
| V8 | Save node names and LookModes locked: `inputBuffer` (Deep), `sdsReclaim` (Deep, owned by this module since C8), `sdsMirrors` (Reference), `depositFilter` (Deep), `coreStoragePriority` (Value), `introNoticeShown` (Value), `sidebarUI` (Deep), `transportLoadRequests` (Deep), `sdsMirrorJobTargets` (Deep, W60) | ModuleInterfaces.en.md 4.1, 4.4; ModuleRules.en.md 3 | `Grep "Scribe_.*Look"` checked against node names; save → load back → re-save verbatim comparison (automation pending) |
| V9 | `SDS_GameComponent_Grid` does not implement `IThingHolder`; `ExposeData` only serializes content — it does not rebuild the ledger or re-attach `SDS_Core` | ModuleInterfaces.en.md 4.1 | `VanillaContractTests.Grid_Shell_CreatesLedgerAndState_WithoutThingHolder` |
| V10 | Runtime static caches are registered only via `SDS_Vanilla.RegisterAll`, 16 entries in total; display-thing caches only clear references and never destroy physical things | E1; DiagnosticsCoverage.en.md M | `Grep "SDS_StaticState.Register" SDS_Vanilla.cs` count = 16; `VanillaContractTests.Vanilla_RegisterAll_IsIdempotent`; startup log "registered N / cleared M" |
| V11 | The 9 host implementation classes are all `internal sealed` with parameterless constructors; they read no `Instance` / `State` / `Find` / `Def` at construction time; every method call reads fresh | ModuleInterfaces.en.md 4.2 | `VanillaContractTests.HostImplementations_ConstructWithoutGameState`, `VanillaServices_Defaults_WithoutSave`, `EndpointProvider_EmptyWithoutSave` |
| V12 | Failure handling: a single patch failure logs `Warn` and continues; runtime exceptions log `ErrorOnce` and then return safe values; `SDS_Diag.Fail` is reserved for data-consistency failures | ModuleInterfaces.en.md 2.1; DiagnosticsCoverage.en.md 4 | Code review of all `catch` branches; `MirrorContractTests` empty-world cases must not throw |
| V13 | The public surface is limited to the §2 whitelist of ModuleResponsibilities.en.md; all other types / members stay internal | ModuleRules.en.md 5 | `CheckAssemblyBoundaries.ps1` type-level whitelist |

## 2. Patch Rules and Prefix Whitelist

> This table is a **category whitelist**, not a per-class inventory: existing Prefixes are spread across multiple business files; the authoritative full list is the source-level `Grep "public static bool Prefix"`; any new Prefix must add a row to this table with a stated reason.

| Category | Allowed Prefixes / Postfixes / Finalizers | Location | Constraint |
|---|---|---|---|
| Throttled refresh | `Patch_SDS_ResourceCounterFastRefresh.Prefix` | `Patch_VanillaSeams.cs` | Only fires `UpdateResourceCounts()` once per 30-tick throttle window, without changing the return value; no `HarmonyPriority` (frozen-version behavior preserved) |
| Intercepting guards | The 19 `SDS_MirrorGuardRegistry` Prefixes / Postfixes | `SDS_MirrorGuardRegistry.cs` | Each entry declares an explicit `Priority`; only returns false / filters, never writes the ledger |
| Projection owner translation | The 10 `SDS_ProjectionOwnerBridge` Prefixes / Postfixes / Finalizers | `SDS_ProjectionOwnerBridge.cs` | Only translates take primitives; failures go through `SDS_TakeService.ReturnToGridOrDrop` |
| Write takeover / validation | `TradeDealValidate`, `TradeableResolveTrade`, `BeaconLaunchTake`, `SettlementGiveBought`, `CaravanGiveBought`, `CaravanValidate`, `CaravanBlockInstant`, `OwnerTryAdd`, `OwnerTryAddOrTransfer`, `JobMirrorTargets`, `NoWearMirror`, `ReclaimJobTargets`, `TransporterDepositIdentity`, `PortalDepositIdentity` | Individual business files | Prefixes perform a single decision / single transaction; new entries must explicitly declare a priority (default Low) |
| Search / reachability | `ClosestGlobal`, `ClosestGlobalReachable`, `ReachabilityMirror` (Prefix); `ClosestReachable`, `ClosestRegionwisePrioritized`, `StealValueIgnoresHiddenGrid` (Postfix) | `SDS_Search.cs` | Only recompute / supplement candidates / translate reachability, never touch the ledger; region priority is enforced only by Prefix-type adapters (S9) |
| Storage-state decisions | `MirrorIsInValidStorage`, `MirrorStoragePriority` (Prefix); `MirrorIsInAnyStorage`, `MirrorHaulDestination` (Postfix) | `SDS_MirrorBehavior.cs` | Only answer storage facts, never write the ledger |
| Mirror take / absorb / carry | `MirrorAbsorb` / `MirrorAbsorbWithComps`, `MirrorTakeOnSplitOff`, `MirrorCarry` / `MirrorCarryCounted` (Prefix); `StartCarryAlreadyCarried` (Postfix) | `Patch_Mirrors.cs`, `SDS_StartCarryDepositFold.cs` | Take the true body first, then hand off to vanilla; `StartCarryAlreadyCarried` additionally carries the W62 self-deposit posting |
| Non-exclusive reservations | `MirrorReserveNonExclusiveCanReserve` / `...CanReserveStack` (Prefix) | `SDS_MirrorReservations.cs` | Only admits mirror reservations; refuses / returns 0 when G is off (W61) |
| Consume-point materialization | `WearConsumeSeam`, `EquipConsumeSeam`, `UseItemConsumeSeam`, `IngestConsumeSeam` (Prefix) | `SDS_MirrorConsumeSeams.cs` | Swaps in the true body at the consume point; on failure does not take over (W59) |
| Preserved-only materialization | `XenogermMaterialize`, `GrowthVatSelectEmbryo`, `CasketAcceptPreservedCorpse`, `PlaceBlueprintForInstall` (Prefix / Finalizer) | `SDS_PreservedUsage.cs` | Materializes the true body first, then hands off to vanilla; on failure blocks or returns |
| Medical gate | `MedicalIngredientGate` (Prefix) | `SDS_PreservedBill.cs` | Filters only by medicine level; the sole decision lives in `SDS_MedicalGate` (S17) |
| Player possession / hotkeys | `PlayerHasDef` (Prefix), `Hotkeys` (Prefix) | `SDS_Selection.cs`, `SDS_Hotkeys.cs` | Read-only recomputation / UI input; never touches the ledger |
| Wealth filter | `WealthItemsFilterProjection` (Prefix) | `Patch_Mirrors.cs` | Filters projections only by the vanilla pawn standard |
| Map-exit allowance | The `MapDeinitAllowMirrorDespawn` Prefix + Finalizer | `Patch_Mirrors.cs` | Temporarily permits `AllowDeSpawn` during map unload and restores it afterwards; no debiting |
| Destroy guards | The `MirrorDestroyConsume` / `MirrorDestroyConsumeWithComps` Prefixes | `Patch_Mirrors.cs` | W19 fail-closed: only refuses / permits destruction, no debiting |

**Legacy exceptions (2026-11-17)**: the module-wide 60 Prefix methods (including dynamic registry handlers; 48 of the 112 attribute classes lack `HarmonyPriority`) are kept as-is and must not be cited as precedent for new classes. The 8 currently unmarked-Low entries in the "Write takeover / validation" row: `TradeDealValidate`, `BeaconLaunchTake`, `SettlementGiveBought`, `CaravanGiveBought`, `CaravanValidate`, `CaravanBlockInstant`, `TransporterDepositIdentity`, `PortalDepositIdentity`. Other unmarked classes include 9 in `SDS_PreservedUsage`, 9 in `Patch_Trade`, 6 in `Patch_CaravanTransport`, 5 in `Patch_TransportLoad`, 4 in `SDS_MirrorConsumeSeams`, 4 in `SDS_Buildings`, etc.; reconcile the full list against the source.

Any new Prefix / Finalizer must simultaneously satisfy: a single purpose, explainability as a "vanilla contract gap", and no second debiting / job-assignment / rollback path. Check: `CheckPatchInventory.ps1` + manual diff.

## 3. Harmony Installation Scope

- Installs only `SDS.Vanilla`: `SDS_PatchInstaller.Install` uses `AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly())`; repeated calls are idempotent.
- Order: the 112 attribute classes are patched one by one via `CreateClassProcessor(type).Patch()` → `SDS_MirrorGuardRegistry.Apply` → `SDS_ProjectionOwnerBridge.Apply` → `SDS_CellOnlyWorkGiverPatches.Apply`. Attribute classes get per-class try/catch; the two registries use per-entry try/catch internally; the three `Apply` calls themselves have no outer try/catch (an exception thrown outside the per-entry loops aborts the subsequent segments).
- Runtime additions: `SDS_PatchInstaller.ApplyRuntimeHooks()` and `SDS_CellOnlyWorkGiverBootstrap` re-run the W63 site guard after defs load; `Applied` guarantees idempotence.
- Reporting: `Log.Message` prints "A/B = applied/declared"; the failure list goes through `Log.Warning`; each registry prints its own A/B.
- Check: `CheckPatchInventory.ps1` (reconciled per assembly), `VanillaContractTests.V4_PatchClasses_ArePresent` / `V5_PatchClasses_ArePresent`, startup logs.

## 4. Mirror and Projection Prohibitions

| Prohibited | Reason | Check |
|---|---|---|
| `mirror.SplitOff(count)` | `Patch_SDS_MirrorTakeOnSplitOff` would translate it back into a take transaction, recursing | `Grep "SplitOff" SDS_Mirrors.cs`; 0 within the physical class |
| `mirror.TryAbsorbStack(...)` | `Patch_SDS_MirrorAbsorb` translates mirror absorption into take-first-then-merge | Same as above |
| Direct `Ledger.TryDebitVirtual` / ledger writes | Debiting and rollback are allowed only via Core transactions | `Grep "Ledger\.(Try|Get)"`; read-only only |
| `DeSpawn` without `SDS_MirrorRegistry.Unmark` | `Patch_SDS_MirrorNoDeSpawn` intercepts, and the mirror table leaks stale entries | Review `TryTakeWholePhysical` / `EvacuateForStructureChange` |
| Creating display things directly with `ThingMaker.MakeThing` | Loses keys / vitals / quality materials | Always go through `SDS_OutputUtility.MakeDisplayThing` / `MakePreservedDisplayThingFrom` |
| Mirrors double-counted by `ResourceCounter` | The same mirrors are counted once via endpoint projections and once via the spawned list | `Patch_SDS_MapChildHoldersExposure` detaches endpoints / vaults; `MirrorContractTests.Exposure_NoWorld_ReturnsZero` |

## 5. Save Rules

| Object | Node / Field | Rule | Check |
|---|---|---|---|
| `Building_SDS_Core` / `Building_SDS_Port` | `inputBuffer` (`Scribe_Deep`, owner=this) | Rebuilds on load when empty in `PostLoadInit`; buffer contents are digitized by the Tick fallback | `Grep "inputBuffer" SDS_Buildings.cs` |
| `SDS_GameComponent_Grid` | `sdsReclaim` (`Deep`) | Read and written only by this component (sibling of this ledger node, same read path for old and new saves): `PostLoadInit` → `ImportFrom`; `FinalizeInit` re-imports as a fallback → `AuditRestored` (job-target audit + protected-output sweep). **The ledger must not read this node**, otherwise duplication | `Grep "sdsReclaim" SDS_GameComponent_Grid.cs`; `LedgerStructureTests` |
| `SDS_VanillaState` | `sdsMirrors` (`Reference`) | Loaded data may contain nulls; only destroys stale projections, never writes the ledger; then `NotifyStockChanged` | [Interfaces.en.md](Interfaces.en.md) 1.8; `MirrorContractTests` empty world |
| `SDS_VanillaState` | `depositFilter` (`Deep`) | `ThingFilter` callbacks are not persisted; after load, rebuild with `NotifyFilterChanged` and `CopyAllowancesFrom` | `Grep "CopyAllowancesFrom" SDS_VanillaState.cs` |
| `SDS_VanillaState` | `coreStoragePriority` (`Value`, default -1) | -1 falls back to mod settings; `SDS_Config.Clamp` before writing; `NotifyEndpointChanged` after changes | `Grep "coreStoragePriority"` |
| `SDS_VanillaState` | `sidebarUI` / `transportLoadScratch` | `Deep`; on load, default objects are supplied when empty | `Grep "sidebarUI\|transportLoadRequests"` |
| Legacy save migration | Unknown defs / missing fields | Keep unresolved entries, lose no data; one `Warn` then continue | Code review + manual load verification |

Forbidden: `new SDS_Ledger()` inside `ExposeData`, `SDS_Core.Attach`, clearing the ledger, or reordering nodes.

## 6. The 16 Static-Cleanup Entries (`SDS_Vanilla.RegisterAll`)

| # | Cleanup entry | What is cleared |
|---|---|---|
| 1 | `SDS_MirrorService.Reset` | Mirror table, dirty flags, version, vault-anchor cache, access points, exposure cache, display-thing cache |
| 2 | `SDS_ReclaimRegistry.Reset` | Reclaim ledger entries (migrated into this module at C1) |
| 3 | `SDS_ProjectionService.Reset` | Projection owner / holder caches |
| 4 | `SDS_OutputUtility.ResetDisplayCaches` | Display-thing key tables and cached references (physical things are not destroyed) |
| 5 | `SDS_MirrorHaulDestination.Reset` | Per-map warehouse proxies |
| 6 | `SDS_Exposure.Reset` | Resource-bar refresh cache and reusable buffers |
| 7 | `SDS_DepositDispatchLedger.Reset` | In-transit / failure backoff (keyed by pawnID / thingID) |
| 8 | `SDS_Selection.Reset` | Accessibility count caches |
| 9 | `SDS_CaravanVirtual.Reset` | Caravan-formation display things and bucket bindings |
| 10 | `SDS_TransportVirtual.Reset` | Loading plans, same-tick snapshots, bucket bindings |
| 11 | `SDS_OfferBridge.Reset` | The dual-channel supply offer table (shared by preserved / virtual) |
| 12 | `SDS_PreservedUsage.Reset` | Preserved-usage caches |
| 13 | `SDS_TradeVirtual.Reset` | Trade display things and buckets |
| 14 | `SDS_ReadOnlyUI.Reset` | Read-only list count caches |
| 15 | `SDS_CellOnlyDestinationFold.Reset` | W63 cell-only destination registrations (job instance → port / self) |
| 16 | `SDS_CellOnlyWorkGiverPatches.ResetDiagnostics` | W63 rewrite / probe / yield diagnostic counters (not accumulated across saves) |

Additionally, `SDS_SelfCheck.Register(new SDS_VanillaDiagnostics())` only registers a diagnostics provider and is not counted among the 16. Check: the `Grep "SDS_StaticState.Register"` count, `VanillaContractTests.Vanilla_RegisterAll_IsIdempotent`, startup logs.

## 7. Hosts and Failure Policy

- The 9 host implementation classes are all `internal sealed` with parameterless constructors; `SDS_Host.Install` may run before `SDS_GameComponent_Grid`, and must never produce an NRE.
- Host methods should avoid throwing upward wherever possible; implementations that are exception-dense or touch vanilla objects use `try/catch` + `SDS_Diag.ErrorOnce` + a safe return value. The current simple value-forwarding implementations (`SDS_MedicalCareAdapter.Allows`, `SDS_VanillaServices.Allows/Priority/TryPlace/TryGiveToPawn`) add no extra catch and rely on callees not throwing; new host methods must handle exceptions explicitly.
- Patch installation failure: `SDS_Diag.Warn` + continue; runtime patch exceptions: `ErrorOnce` + keep the vanilla result.
- `SDS_Diag.Fail` scope: data-consistency issues such as missing vault def / no free cell / creation failure, projection take or drop failure, unreservable pickup point; normal refusals (no stock, unreachable, filter rejection) stay silent.
- Check: `VanillaContractTests.HostImplementations_ConstructWithoutGameState`, `MirrorContractTests.*_NullInputs_AreSafe`, code review of all `catch` blocks.

## 8. The Sole Implementation of the Medicine-Level Criterion (S17)

- The criterion lives only in `SDS_MedicalCareAdapter.Allows`: `WorkGiver_DoBill.GetMedicalCareCategory(billGiver).AllowsMedicine(def)`; non-medicine / non-Pawn billGivers are always allowed (vanilla standard = Best).
- The sole entry point is Contracts' `SDS_MedicalGate.Allows(billGiver, def)`: bill job assignment (the `IsUsableIngredient` patch, preserved / virtual ingredient filtering) and preserved medicine supply (the `HealthAIUtility.FindBestMedicine` fallback) all go through it; do not write any further `GetMedicalCareCategory` / `AllowsMedicine` / `playerSettings.medCare.AllowsMedicine`.
- The former "patient self-selected level" branch was removed: when `patient.playerSettings == null`, treat as the vanilla `GetMedicalCareCategory` Best instead of refusing as `NoMeds`.
- On the ingredient-resupply side, the medicine gate is read by Vanilla / Api via `SDS_MedicalGate.Allows(billGiver, def)` and injected into `SDS_Rules` as `extraAllows` (from P2 onward; previously Core); `SDS_BillSupplyPlanner` neither reads `SDS_Host.MedicalCare` nor duplicates the decision.
- `Patch_SDS_MedicalIngredientGate` continues to intercept only `Bill_Medical` + pawn patients; all other shapes pass through, preserving vanilla behavior.
- When no host is injected, allow (treat the feature as unavailable); never throw or block job assignment.
- Check: `grep "GetMedicalCareCategory\|AllowsMedicine"` should only hit `SDS_MedicalCareAdapter.cs` (comments excluded); `BillMedicalGateTests.VanillaMedicineHelper_DelegatesToHostPolicy`, `VanillaContractTests.MedicalCare_NonMedicineAndNonPawnBillGiver_Allow`.

## 9. S Series: Search Adapter Rules

> This section is the authoritative body for S2 / S4 / S9 / S10 / S12a / S12b (originally published in VanillaSupport.md §6; that file has been merged into [Interfaces.en.md](Interfaces.en.md) 4.6); the S-series "Basis" column of the top-level RuleIndex.en.md is verified against this section. The adapters' vanilla contact surface is in [Interfaces.en.md](Interfaces.en.md) 4.6.

| ID | Normative statement | Site | Verification |
|---|---|---|---|
| S2 | Same candidate + same projection = every search adapter yields the same distance / reachability / storage state. | `Patch_SDS_ClosestGlobal`, `Patch_SDS_ClosestGlobalReachable` (`SDS_Search.cs`) | Code review |
| S4 | No search adapter may re-implement the distance formula; access-point translation goes through the sole implementation. | `Patch_SDS_ClosestGlobal`, `Patch_SDS_ClosestGlobalReachable`; the sole distance-translation implementation is in `SDS_AccessPoints.cs` | Code review |
| S9 | Region priority constrains only adapters that can control the phase and the candidate set (Prefix-type); `ClosestThingReachable` and `ClosestThing_Regionwise_ReachablePrioritized` are Postfix-type and cannot distinguish the Region / Global phases, so they uniformly pick between the Global-semantics candidate and the vanilla best (comparison key = `priorityGetter → effectiveDistance → tieBreaker`), and may replace a non-empty vanilla region result only when the mirror is strictly better; this is a recorded deviation. If strict region priority is ever needed, only new Prefix-type adapters are allowed; region BFS must not be replicated inside Postfixes. | `Patch_SDS_ClosestReachable`, `Patch_SDS_ClosestRegionwisePrioritized` (`SDS_Search.cs`) | Code review |
| S10 | EffectiveCell is kept separate for Source / Destination; the source's mirror / preserved side depends on root. | `Patch_SDS_ReachabilityMirror`, `Patch_SDS_MirrorTargetGoto` | Code review |
| S12a | Without `priorityGetter` (`ClosestThingReachable`): Postfix-type adapters perform only the either/or comparison of "supplement candidates vs vanilla best"; they do not reorder region candidates or cross Region / Global. | `Patch_SDS_ClosestReachable` (`SDS_Search.cs`) | Code review |
| S12b | With `priorityGetter` (`ClosestThing_Regionwise_ReachablePrioritized`): likewise only the either/or comparison; comparison key = `priorityGetter → effectiveDistance → tieBreaker`. | `Patch_SDS_ClosestRegionwisePrioritized` (`SDS_Search.cs`) | Code review |

## 10. Rule Bodies (Authoritative)

> This section summarizes the rule bodies of R / W / E and S15 / S17 / S18. The top-level RuleIndex.en.md is verified against this section.

| ID | Normative statement summary | Sites in this module |
|---|---|---|
| R15 | Trade / gifting / launch read "physical stock − transaction reservations" and are not zeroed by category disabling | `SDS_Trade.cs`, `SDS_Availability.GetTradableCount` |
| R16 | Read paths never write the trade registry / preserved bucket map | `SDS_Trade.cs` (read-only injection) |
| R17 | When G is off, all read-side availability is zeroed; the ledger is retained | `SDS_Exposure.cs`, `SDS_EndpointProvider.IsLocalUsable` |
| S15 | Mirrors / vaults never enter NPC stealing targets or steal values | `SDS_Search.cs` |
| S17 | This mod's medicine candidates must pass the vanilla medicine-level criterion | `SDS_BillIngredientQuery.cs`, `SDS_PreservedBill.cs`, `SDS_VirtualBillFallback.cs` |
| S18 | When the setting is on, non-player factions must not take from Hyperdimensional Space stock: mirror candidate supplements, mirror reachability, job-target materialization, carrying, equipment / apparel / inventory acceptance, and endpoint withdrawal are all uniformly refused via `SDS_AccessRules.BlocksNonPlayer`; behavior is unchanged for player factions and when the setting is off | `SDS_AccessRules.cs`, `SDS_Search.cs`, `SDS_AccessPoints.cs`, `SDS_JobTargets.cs`, `SDS_Mirrors.cs`, `SDS_Transmission.cs`, `SDS_EndpointHandle.cs` |
| W2 | Projection owner takes / materialization go through the unified take | `SDS_ProjectionOwnerBridge.cs`, `SDS_Mirrors.cs` |
| W5 | When mirrors are job / carry / split targets, take the true body first: taking happens at consume seams (W58 / W59) or take primitives, never during `StartJob` assignment materialization | `SDS_Mirrors.cs`, `SDS_JobTargets.cs` |
| W18 | Mirrors without `Unmark` refuse `DeSpawn`; map unload temporarily allows | `SDS_MirrorGuardRegistry.cs`, `SDS_Mirrors.cs` |
| W19 | External destruction of mirrors is **fail-closed**: refuse destruction + one-time warning; never debit the whole category by the mirror's current count; internal cleanup must `Unmark` first | `SDS_MirrorConsume`, `Patch_SDS_MirrorDestroyConsume*` |
| W20 | Mirrors do not participate in merge semantics; absorption takes first | `SDS_MirrorAbsorb`, `Patch_SDS_MirrorNotMergeable` |
| W23 / W24 | Trade deals are validated per row; preserved true bodies are picked only from the same group bucket | `SDS_Trade.cs`, `SDS_PreservedBuckets` |
| W25 | Caravan-formation shortfall and loading shortfall are two different measures: formation (departure validation) = `CountToTransfer − physical amount in the row not held by any holder` (display things excluded); loading (job assignment) = `CountToTransfer − the amount already claimed by vanilla in-flight jobs for that container` (the `initialCount` of `HaulToTransporter` / `HaulToPortal`, excluding this mod's pending assignments). The two must not be swapped: loading is not reduced by map physical amounts, because vanilla hands over to Hyperdimensional Space only when no loadable physical thing can be found; at the formation stage there are no vanilla in-flight jobs for that container, so the second term is 0 | `SDS_CaravanTransport.cs` (formation); `SDS_TransportVirtual.cs` (loading) |
| W28 | Formation / loading shortfall validation handles only rows of "items + rows carrying this mod's display things"; pawn rows and purely vanilla rows go to the vanilla `CheckForErrors` | `SDS_CaravanVirtual.ValidateQuantities` |
| W26 | Old saves stay readable; no data loss outranks format stability | `SDS_VanillaState.cs` |
| W27 | Rollback uses the channel-specific fallback | This module only calls `SDS_Rules.ReturnThing` / `SDS_TakeService.ReturnToGridOrDrop` |
| W30 | Mirror targets of direct-consume jobs are not materialized during job assignment; the default delivery form = Hand. The sole exception: escape-hatch jobs explicitly registered as `SpawnedTarget` (currently only `BottleFeedBaby`) materialize at the endpoint per the shortfall `WillIngestStackCountOf` without clamping the carry amount; inside the escape hatch, `job.count ≤ 0` counts as 1; count shortfalls for unregistered jobs are handled by W58 and must not fall back to 1; explicit `count = 1` is the caller's decision | `SDS_JobTargets.cs` |
| W31 / W32 | Installable minified output is fixed at 1; the install blueprint is unique | `SDS_UI.cs`, `SDS_PreservedUsage.cs` |
| W34 | Forced virtualization of weapons / apparel is a one-time migration, never silent conversion | `SDS_Rules.ConvertPreservedGearToVirtual` (called directly by Bootstrap); Vanilla's `SDS_GameComponent_Grid` shell no longer serves as the module-surface entry |
| W35 | `IsStudiable` is always preserved and precedes the default virtual decision | This module only consumes via `SDS_Rules.TryGetStockKey` / display-thing keys |
| W36 | Mirrors are never registered in the studiable cache | `Patch_SDS_MirrorNotStudiable` |
| W37 | Vaults anchor to available endpoints on their own map; 250-tick grace without an anchor | `Building_SDS_Vault` |
| W38 | Loading rows take true bodies only from the vanilla group bucket that the row's display thing belongs to; autoload rows without a registered bucket resolve their own single bucket via "display thing + `TransferAsOne(PodsOrCaravanPacking)`" and must not aggregate multiple buckets by key; `HasJob` and `JobOn` share the same "plannable units > 0 and key availability > 0 and pickup point resolvable" test; an unservable row is skipped, and that must not be used to green-light the whole transport pod | `SDS_TransportVirtual.cs` (`TrySelectLoadRow` / `ResolveRowBucket`), `Patch_TransportLoad.cs` (`HasJobOnTransporter` / `JobOnTransporter`) |
| W39 | On load, a loading row's owed amount is restored as `CountToTransfer − unloaded physical amount`; the display thing binds to the group bucket with the same key that is not occupied by another row; never take the first bucket by key, and never let multiple rows share one display thing | `SDS_CaravanTransport.cs` |
| W40 | Load-deposit posting locates the row by "true body → vanilla loading row" identity: first the registered bucket of the display thing containing that true body, then the sealed category key equal to the true body's key; on a hit the true body is placed back into that row's `things`, while count deduction / row removal / completion messages remain the vanilla's job; transport pods / map portals / shuttles share the same identity rule and must not rely on a `TransferAsOne` same-def fallback | `SDS_TransportVirtual.cs` (`NormalizeDepositedThing`), `Patch_TransportLoad.cs` (`SubtractFromToLoadList` prefix) |
| W42 | Loading job assignment must use the same-tick decision snapshot: `HasJob` produces and registers the selection result, `JobOn` only consumes that snapshot; mutable runtime state (including `FindThingToLoad`) must not be re-evaluated in `JobOn` | `SDS_TransportVirtual.cs` (`RememberSelection` / `TryTakeSelection`), `Patch_TransportLoad.cs` |
| W43 | A loading plan counts as occupied only when "created this tick" or "still some pawn's current job (including the pending queue)"; otherwise it is reclaimed at row selection; a `TryMakePreToilReservations` failure must explicitly release; during the `JobOnThing` probe of the destination-cell guard no plan may be registered (the `IsProbing` gate) | `SDS_TransportVirtual.cs` (`BuildPendingBusy` / `MakeLoadJob`), `JobDriver_SDS_CaravanJob.cs` (`TryMakePreToilReservations`), `Patch_VanillaSeams.cs` (`SDS_CellOnlyWorkGiverPatches.IsProbing`) |
| W44 | The loading dialog refresh clears only display-thing identity registrations; it must not wipe other containers' job-assignment plans / same-tick snapshots / preserved bucket bindings; global cleanup may be triggered only by `SDS_StaticState.ResetAll` | `SDS_TransportVirtual.cs` (`ResetForDialog` / `Reset`) |
| W45 | Preserved bucket identity is decided by "any overlapping true body inside the bucket means same bucket"; dialog-injection dedup, reopen-and-reuse during loading, and cross-container display-thing isolation all share this decision; the bucket's first element must never stand in for bucket identity | `SDS_Trade.cs` (`SDS_PreservedBuckets.SameBucket`), `SDS_TransportVirtual.cs` (`ResolveBucketDisplay`), `SDS_CaravanTransport.cs` (`TryScanRowIdentities`) |
| W46 | The loading pick-amount measure is uniformly "availability after reservations" (`SDS_Availability`: Total − Reserved, zeroed when disabled); the row-selection threshold / per-trip clamp / dialog injection / shuttle autoload share one entry point and must not compute physical stock separately; per-trip clamp = `min(owed amount, carry cap MaxStackSpaceEver) ∩ availability (sole implementation W56)`: the carry part's sole implementation is `ClampCarry` (shared by `ClampLoadTake` and the driver), availability goes only through `SDS_Availability`, and the same formula must not be inlined | `SDS_TransportVirtual.cs` (`ClampCarry` / `ClampLoadTake` / `LoadAvailable`), `JobDriver_SDS_CaravanJob.cs` |
| W47 | After a mid-loading save load, job-assignment identity takes the reservation ledger as the sole authority: before materializing, the driver must rebuild the preserved plan from the one vanilla group bucket in the container that owns the `job.loadID` reservation key; runtime plans / pending keys do not survive saves, must not degrade to grabbing any thing of the def, and must not aggregate multiple buckets by key. When one key maps to several distinct buckets, no bucket can be found, or the bucket's plannable units fall short, the result must fail-closed (stock and reservations untouched) | `JobDriver_SDS_CaravanJob.cs`, `SDS_TransportVirtual.cs` |
| W48 | Loading-row restore treats transport pods and map portals identically: display-thing references do not survive saves, so rebuild by identity + owed amount; containers whose loads drop purely-hyperdimensional rows on load must locate by category or rebuild rows by owed amount, never force-fit by row number | `SDS_TransportVirtual.cs` (`TryGetRestorableRowKey` / `TryGetRestoreDisplay`), `SDS_VanillaState.cs` (`CollectTransportLoadRows` / `TryRestoreTransportLoadRows`) |
| W49 | In-transit materialization marks are valid only during delivery; return / drop / failure fallbacks must revoke them | `SDS_Implants.cs` (revoked uniformly via Core), `SDS_Use.cs` |
| W50 | Third-party self-built lists go only through the sole injection implementation | `SDS_CompatHosts.cs`, `SDS_TransportVirtual` |
| W52 | When "allow priority outflow" is on and "highest priority" is off, mirrors enter the vanilla hauling candidates; outflow happens only when there is a strictly higher-priority storage destination that admits entry, the ledger spends only total − reserved, and destination cells / containers and mirror job reservations follow vanilla; no outflow when the setting is off or "highest priority" is on | `Patch_Mirrors.cs`, `SDS_Mirrors.cs` |
| W53 | Mirrors represent the ledger itself, not re-depositable physical things: `SDS_EndpointUtility.IsDepositable` is the sole criterion for "depositable physical thing"; endpoint acceptance (`IHaulDestination.Accepts`), deposit competition, and manual deposits uniformly refuse mirrors; a mirror's storage facts come solely from the per-map warehouse proxy (`CurrentHaulDestinationOf` returns the proxy, `CurrentStoragePriorityOf` reads the save-level priority, `IsInAnyStorage` / `IsInValidStorage` are true); this is what grounds W52's "outflow only to strictly higher priority" | `SDS_EndpointUtility.cs`, `SDS_Destinations.cs`, `SDS_MirrorBehavior.cs` |
| W54 | A mirror's true-body location (the hidden vault) is never a pickup point: mirror pathing, carrying, and deposit executors must never move by true-body coordinates under any circumstances; with no available endpoint, end the job as target unreachable (fail-closed); pickups outside port range are always refused; mirrors must not enter a pawn's own owner (inventory / equipment / apparel) via `ThingOwner`; third-party hauling (including PUAH) candidates and assignments always exclude mirrors; mirror references in unload lists map back to inventory true bodies by category key | `SDS_JobTargets.cs`, `SDS_Mirrors.cs`, `SDS_Destinations.cs`, `SDS_Transmission.cs`, `SDS_ModCompat_PUAH.cs` |
| W55 | When the "highest priority" setting is on, Hyperdimensional Space counts as top-priority storage in every hauling decision for depositable physical things: `ListerHaulables.ShouldBeHaulable` returns true for things that are "spawned, not mirrors, not forbidden, without an in-transit product mark, depositable, and with an available endpoint on this map"; `SDS_DepositService`'s deposit competition also ignores the "already in best storage" refusal; `SDS_DestinationResolver.TryDecideCore`'s deposit decision no longer compares priority against vanilla destinations and adopts the hyperdimensional endpoint directly. Functional destinations and owner-type container contents are outside coverage; mirrors are still handled per W52/W53/W54, and with the setting off there is zero behavior change | `Patch_Mirrors.cs`, `SDS_Destinations.cs` |
| W57 | All player-initiated manual deposit entry points (right-click menu, deposit-to-inventory, forced assignment from third-party player UIs) must share the single manual decision `SDS_DepositService.TryResolveManualDeposit`: manual = forced, permitted through the in-transit materialization mark gate (`SDS_EndpointUtility.AcceptsForManual`); auto hauling / return / delivery paths only allow `AcceptsForAuto`; new manual entry points must not write their own decision | `SDS_Destinations.cs`, `SDS_EndpointUtility.cs` |
| W58 | The sole shortfall-repair point = `Toils_Haul.ErrorCheckForCarry`: with mirror-only + `job.count ≤ 0`, restore from that job's `Reservation.StackCount` for the mirror (-1 = whole stack); without a reservation keep 1; read-only, no ledger writes, no reservations; after restoration, vanilla clamping and W56 still close out the amount | `SDS_HaulCountIntent.cs` |
| W59 | The sole consume-point materialization implementation = the pre-`Notify_Starting` hook of direct-consume drivers: Wear / Equip / UseItem take 1; Ingest for non-tool users takes `max(1, job.count)`; drop point = the nearest reachable endpoint access point; with no endpoint / on failure, do not take over and hand back to the vanilla failure conditions | `SDS_MirrorConsumeSeams.cs` |
| W60 | Mirror job-target remapping and invalidation closure (shared by save / load / runtime removal): persisted as "job loadID + slot + category key"; after a mirror rebuild, swap in the new mirror; replacing an invalid mirror under the same key first builds the new mirror, re-points by exact reference, then removes the old mirror (if building / placement / swap fails, fall back to closing the old target and removing it); process only slots still pointing at the old mirror (already `Destroyed` after load cleanup) or invalidated to null; when the job has advanced (the slot already holds a live thing such as a true body), skip, do not overwrite, do not end; on key invalidation, the current job's core slot ends the job first, queue slots only remove that entry, and queued jobs discard the target after dequeuing (countQueue removed in sync); never write `LocalTargetInfo.Invalid` under any circumstances; saves without the node are treated as empty | `SDS_VanillaState.cs` |
| W61 | Mirror reservations are non-exclusive: `CanReserve` always admits mirrors (ignoring `maxPawns` / other reservations) and vanilla `Reserve` registers accordingly; `CanReserveStack` returns the mirror stack amount; **when G is off, both refuse / return 0** (prevents job loops); exclusivity is guaranteed by ledger transactions; the non-mirror path has zero changes | `SDS_MirrorReservations.cs` |
| W62 | For any "deposit into Hyperdimensional Space" decision where the actor is an available port that can accept the thing, the destination must fold to the actor's own port; once the thing is in hand it is posted immediately through the sole deposit entry; explicitly designated ports and take / internal-return paths are exempt | `SDS_SelfDepositFold.cs`, `SDS_Destinations.cs` |
| W63 | When a cell-only job's best destination is a hyperdimensional building endpoint and the actor's own port is unavailable, the destination must be rewritten to that endpoint's `AccessCellOf` (basic standing standard; falls back to the building cell when not standable, walkability not guaranteed) and posted through the sole cell-placement entry; when the site is unreachable, unresolvable, or posting fails, yield or drop per vanilla "no free storage space"; the best-destination baseline = `StoragePriority.Unstored` (same standard as the cell-only WorkGiver) | `SDS_CellOnlyDestinationFold.cs`, `Patch_VanillaSeams.cs` |
| E1 / E2 | Static caches are registry-managed; dev-time assertions log Error only | `SDS_Vanilla.RegisterAll`, `SDS_Diag` |

## 11. Mirror Visibility Conclusions (Authoritative)

> This section is the authoritative definition of the mirror visibility rules. Distances, ordering, and filter boundaries for search and reachability are authoritative in §9 and §12.6 of this document and are not repeated here.

### 6.1 Counting / Accessibility Measures

**Measure distinction**: the APIs in this table ask "do we own it / how many in total" and use physical totals (no reservation deduction); `SDS_Availability` (Total − Reserved) asks "how many can be taken now". The two measures must not replace each other.

| Vanilla API | Virtual mirror | Preserved true body | This mod's handling |
|---|---|---|---|
| `ResourceCounter.UpdateResourceCounts` / `GetCount` | 1 per category (`stackCount` holds the ledger amount) | Only `CountAsResource` adds extra | Vault group + `MirrorExposure` correction ([Interfaces.en.md](Interfaces.en.md) 4.3) |
| `WealthWatcher.CalculateWealthItems` | By default kept only on canonical maps; with `excludeGridFromWealth`, every map subtracts mirrors | Adds 1 only on canonical maps | Canonical maps `+PreservedWealth`; non-canonical endpoint maps `−MirrorWealthOn`; result clamped to 0 ([Interfaces.en.md](Interfaces.en.md) 4.4) |
| `RecipeWorkerCounter.CountProducts` | The resource branch relies on `ResourceCounter`; non-resource 1 mirror = 1 item | Per true-body properties | `UsesResourceCounter` skipped; `CountGridProducts` adds N−1 / full amount |
| `CountValidThings(List<Thing>)` / `(ThingOwner)` | 1 per mirror; vault direct contents empty | True bodies item by item | Delta patch |
| `Alert_NeedWarmClothes.FreeWarmClothesSetsCount` | 1 per mirror (adds N−1) | True bodies item by item | With mirrors adds `entry.count − 1`, without mirrors adds `entry.count` |
| `PlayerItemAccessibilityUtility.Accessible` / `PossiblyAccessible` / `PlayerCanMake` | Mirrors visible | Derived from true bodies | Patches add ledger + preserved: `Patch_SDS_PossiblyAccessible` (owned amount + derived amount × `entry.count`), `Patch_SDS_PlayerCanMake` |
| `PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(def,count)` / `(filter)` | Mirrors (1 per category) | Supplemented by `LedgerTotalForDef` / `HasPreservedMatching` | Patch recomputes wholesale / supplements preserved matching |
| `RecipeDef.PotentiallyMissingIngredients` | — | — | Per-slot precise filtering (`Patch_VanillaSeams.cs`) |
| `Bill_Production.GetRepeatInfo` / `ShouldDoNow` / `CanUnpause` | Same as `CountProducts` | Same | Covered indirectly |

### 6.2 AI / Room / Building Perspectives (Side Effects)

| Vanilla API | Impact | This mod's handling |
|---|---|---|
| `Room.ContainedAndAdjacentThings` / `GetStat` / `ContainsThing` / `ContainedThings` / `ThingCount` | Mirrors affect room beauty / cleanliness / space / roles | `Patch_SDS_RoomIgnoresHiddenGrid` + the three filter patches `RoomContainsThing` / `RoomContainedThings` / `RoomThingCount` |
| `ListerBuildings` / `ListerArtificialBuildingsForMeditation` / `ListerBuldingOfDefInProximity` | Vaults are real buildings and may enter building stats / meditation / proximity queries | **No actual impact, no patch**: vaults have `MarketValue=0` / `Beauty=0`, no comps, no `buildingTags` |
| `TantrumMentalStateUtility` | Tantrum pawns may treat mirrors as smash targets | `CanSmash` returns false for mirrors / vaults |
| `JobGiver_FireStartingSpree` | May try to ignite mirrors | `TryFindRandomIgniteTarget` filters out mirrors / vaults |
| `PawnObserver` | Observation / horrifying thoughts may lock onto mirrors / vaults | `Patch_SDS_ObserverIgnoresHiddenGrid` filters `PossibleToObserve` |
| `StealAIUtility` / `Trigger_HighValueThingsAround` | A mirror's `stackCount` represents a whole category → falsely triggers "raid turns to stealing" | S15: the stealing group gets no mirror candidates; `GetValue` reads 0 for mirrors / vaults |
| `Find.StudyManager.UpdateStudiableCache` | Hidden vault proxies treated as study subjects | W36: `Patch_SDS_MirrorNotStudiable` never registers mirrors |
| `RoomRequirement_ForbiddenBuildings` | Vaults may affect room requirements | **No actual impact**: the vault def has no `buildingTags` |

### 6.3 Per-API "Visibility Count" Conclusions

| Measure | Virtual stock | Preserved stock |
|---|---|---|
| `ListerThings` (def / group) | 1 per category (`stackCount` holds the ledger amount) | 0 |
| `ListerThings.HaulableEver` region scan | 1 per category (vault region) | 0 |
| `Region.ListerThings` / `ThingGrid` | 1 per category (vault region / vault cell) | 0 |
| `Map.GetChildHolders` → map-level enumeration | 0 (endpoints / vaults detached; counted 1 via `spawnedThings`) | 0 |
| `Map.spawnedThings` / `GetAllThingsRecursively(map)` | 1 per category | 0 |
| `ThingOwnerUtility.GetAllThingsRecursively(vault/endpoint/pawn)` | 1 per category (projections) | 0 |
| `SlotGroup.HeldThings` | 1 per category (vault group) | 0 |
| `AllHaulSourcesListForReading` direct contents | 0 (vault direct contents empty) | 0 |
| `WealthWatcher` / `ResourceCounter` | 1 per category (patch-corrected) | Added only on canonical maps / extra via `CountAsResource` |
| `RecipeWorkerCounter` | Slow path 1 per category + delta patch | Supplemented per true body |
| Trade / caravan formation / loading lists | 0 (mirrors filtered out) | Display things, one row each |
| `Find.StudyManager` studiable cache | 0 (W36) | 0 (true bodies never spawn) |
| Display things | 0 (three base measures; no spawn / holder) | 0 (exist only in list UIs) |

**Preserved consume-bridge exception (Plan A)**: physical things materialized by `SDS_OfferBridge` are ordinary spawned things for the offer's lifetime and count as 1; after the timeout return to stock they count as 0 again. They were already debited from the corresponding channel at materialization, so they are not a second copy of stock.

## 12. Supplementary Measures (Still in Force, No Standalone Rule IDs)

> This section records criteria and facts only and introduces no new rule IDs; on conflict with §1–§11, the earlier text of this document prevails.

### 12.1 Category Disabling and Capacity

- Disabled ≠ deleted: disabled categories still count toward capacity / wealth / tradable amounts; R15 only zeroes the read side — the ledger and physical stock do not vanish with it.
- New categories are usable by default; the disabling granularity is the category key (not the def, not the individual item).
- `ThingDef.EverHaulable` only decides default checkmarks in the deposit filter: corpses / minified things default unchecked; gene carriers such as gene packs / xenogerms default checked. Never treat it as a "depositable" decision.
- Disabled categories build no mirrors and join no search enumeration; the search side needs a second filter to cover the 30-tick sync window.

### 12.2 Mirror Building and Sync

- Mirror-building conditions: non-preserved + resolved + count > 0; never filter legacy-save virtual entries by "rebuildable"; unresolved entries build no mirrors and join no enumeration.
- Generation order: set the mirror stack amount via `Math.Min(count, stackLimit)` first, then spawn; after a successful spawn, if the stack amount differs from the ledger amount, immediately write the full amount back into the mirror's `stackCount` and mark dirty (the ledger is not written by this path); on load, `CollectMirrors` clamps mirrors above `stackLimit` back to the cap. Count increments trigger sync; structural changes trigger full rebuilds; with no dirty flags it returns in O(1).
- Sync period 30 ticks; when G goes off, withdraw all mirrors; when L goes off for this map, withdraw that map's mirrors and enter the W37 grace period; when a map leaves `Find.Maps`, clear its table.
- Post-load mirror cleanup must run in `SDS_GameComponent_Grid.FinalizeInit` (after maps spawn); never `Destroy` early in `SDS_VanillaState.ExposeData/PostLoadInit`, or it triggers `Spawning destroyed thing` and zero-stack errors.
- G / L criteria: G = the world contains a player-faction, spawned, undestroyed core building; if power consumption > 0 it must also be powered, consumption = 0 counts as usable. L = G plus this map having an available core or an available chip pawn.
- Accessibility and fog follow vanilla exactly; this mod only supplies the "total-amount delta" and never derives reachable ranges itself.
- `ThingsInGroup(HaulableEver)` includes mirrors while `ListerHaulables` excludes them — two independent registries; do not mix them.
- Pocket maps (where the player is located, and the map the player is on) also participate in mirrors / exposure.
- Preserved invisible-group bridges: corpses (resurrecting mechanoids needs map corpses), minified things, Apparel / Medicine / Weapon / Food have bridges; Art / Book have none — no bridge means vanilla cannot see them; never assume visibility.
- W53 warehouse-proxy measure: a mirror's storage facts come solely from the per-map `SDS_MirrorHaulDestination` — `Patch_SDS_MirrorHaulDestination` makes `CurrentHaulDestinationOf(mirror)` return the proxy (`Accepts` always true), so `IsInAnyStorage` / `IsInValidStorage` are true and `CurrentStoragePriorityOf` reads the save-level priority; this is what grounds W52's "outflow only to strictly higher priority". The vault's own `StorageSettings` do not participate in that decision; never write another "vault filter / priority". `HaulDestinationEnabled=false` keeps vaults out of hauling destinations.
- W53 mirrors are not depositable: mirrors represent the ledger itself, not new goods to re-deposit. `SDS_EndpointUtility.IsDepositable` is the sole criterion; endpoint acceptance (`IHaulDestination.Accepts`), deposit competition (`SDS_DepositService`), and manual deposits uniformly refuse mirrors, and no entry point may write its own mirror check; otherwise outflow jobs would be rewritten into deposit jobs and endpoints would become outflow destinations.
- W55 highest priority: when `SDS_Config.IgnorePriorityMode` is on, `Patch_SDS_MirrorNotHaulable` puts things that are "spawned, not mirrors, not forbidden, without an in-transit product mark, depositable, and with an available endpoint on this map" back into hauling candidates; `SDS_DepositService.AutoOnlyGates` also ignores `IsInValidBestStorage` refusals; `SDS_DestinationResolver.TryDecideCore`'s deposit decision skips the priority comparison against vanilla destinations and adopts the hyperdimensional endpoint directly (then folds to self per W62). Owner-type container contents (caskets / hoppers / transporter interiors) and functional destinations are outside coverage; with the setting off there is zero behavior change.

### 12.3 Structure Departure and Single-Cell Self-Healing

- Structure-departure evacuation: zero ledger writes, no physical products, repeatable; local buffering prevents re-entry; counts inside the window fall back to the ledger; it and single-cell self-healing do not replace each other.
- W19's allow branches (not counted as "external destruction"): def not destroyable, not spawned, `stackCount <= 0` (the latter two `Unmark` first, then allow), `AllowDeSpawn` (map unload), not `Playing` (loading); all other in-play external `Destroy` is refused with `Log.WarningOnce`.
- Single-cell cap self-healing: `EdificeGrid.DeRegister` does not validate ownership → the cap can drop back to 1; rebuilding goes through `GenSpawn` relocation, and `DeSpawn` triggers the protection error; always check `GetMaxItemsAllowedInCell` before placing.

### 12.4 Deposit / Job-Assignment Atomicity

- Deposit commit atomically writes `targetB` / `targetQueueB` / the label — all items or none; destinations must not be re-picked during execution.
- Multi-item deposits go only to building endpoints; own ports are the exception; non-self pawn / mechanoid ports accept single items only.
- The ledger registers the job's exact `Thing` reference; delete on invalidation; recovering by (def, stuff, quality, count) is forbidden; inventory contents outside the ledger are never touched.
- Cleanup touches only "ledger items still in the inventory"; suspended jobs do not drop goods; only terminated-and-unsuccessful jobs drop.
- Default delivery form = Hand: mirror targets are not materialized in `StartJob`; `targetA/B/C` and `targetQueueA/B` are kept as-is, and true bodies are swapped in at the consume point by W58 / W59 / take primitives; only escape-hatch jobs explicitly registered as `SpawnedTarget` rewrite `targetA/B/C` and `job.count` and set the mark (currently only bottle-feeding); on failure, never re-route.
- Single-item owners take exactly 1 true body; stuffing the whole stack would turn "equip one" into "haul away the whole stack".
- On load, endpoint `inputBuffer` must be cleared of three classes of stale residue: duplicate copies sharing a `thingID` with a live preserved true body, `Destroyed` things, and zero stacks; the cleanup lives only in `SDS_EndpointUtility.PruneLoadedBuffer` — discarded copies are not Split, not Destroyed, and their counts are not changed.
- When `FlushInputBufferItem` encounters `Destroyed` / zero-stack / already-owned-by-another-owner entries, it only removes them from the buffer and must not re-post them.
- Take-amount measures: see RuleIndex.en.md W30 / W58 / W59; the sole implementations are `SDS_JobTargetAdapter.TakeCountForSlot` (escape hatch), `SDS_HaulCountIntent` (count shortfall), and `SDS_MirrorConsumeSeam` (consume-point materialization); this section does not restate them.
- Translation criterion: would the same action, applied to real container contents, make them leave? `DeSpawn` / loading / unloading are exempt.
- `Wear`: `DeSpawnOrDeselect` first, then validate body part / biocode / `PawnCanWear`; equipment goes `AddEquipment → TryAdd`, on the Container channel.
- Flush residue only while loading / not `Playing`; process item by item, never in batches.

### 12.5 Use Commands

- Use gating has four levels 0 / 0.5 / 1 / 2: own ports only, humanoid and player faction, the six usable categories, and synchronous spawn → decision → return within the same callback.
- JobDef white / blacklists; unknown defs are accepted by default; no job produced means no return; task-type jobs are canceled and returned.

### 12.6 Search Measures (Supplementing §9)

- Distance ordering does not decide reachability; movement / delivery use "nearest reachable point"; the two are not interchangeable.
- The sole pickup-point / reachability rule: when the own port is available and the origin is the pawn's own cell, the own port is the distance-0 access point (including reachability); otherwise take the nearest available building access point. `SDS_AccessPointResolver.TryGetNearestReachableAccess` is the sole implementation, shared by mirror movement, job materialization, the preserved bridge, and reachability decisions; no one may write their own.
- W54: a mirror's true body (the hidden vault) is never a pickup point. When `Patch_SDS_MirrorTargetGoto` cannot resolve an endpoint, end the job as target unreachable — never fall back to true-body coordinates; `SDS_MirrorCarry` requires non-chip pawns to stand within the resolved access point's hit range, otherwise it refuses the take; deposit executors must never move by true-body coordinates. Mirrors must not enter a pawn's own owner (inventory / equipment / apparel) via `ThingOwner` — picking up can only go through `SDS_MirrorCarry`; third-party hauling (including PUAH) candidates / assignments always exclude mirrors, mirror references in unload lists map back to inventory true bodies by category key, and third parties must never choose the destination themselves.
- Tie-breaking measure: endpoint vs vanilla destination takes the endpoint; between endpoints use the `thingIDNumber` stable key (own ports no longer get priority).
- On equal distance take the mirror; give up beyond `maxDistance²`; comparison-key order follows S12a / S12b.
- `searchRegionsMax` is intentionally bypassed, for the same reason as "supplementing candidates by Global semantics" (Postfixes cannot distinguish phases).
- Region / Global two-stage filter order: region candidates → global fallback → `CanReach` last; source reachable ≠ destination reachable.
- Candidates and takes are layered: search / bill candidates enter vanilla ingredient selection at the mirror total (`total`), and only the actual take clamps to `available`. This layering is intentional (it preserves vanilla ingredient selection / failure window-locking behavior); never change candidates directly to `available`.
- Caravan-formation row identity: a row holds **at most** **one virtual key + one preserved bucket**. Vanilla `TransferAsOne` groups rows by def / stuff / quality / HP etc. and **does not distinguish channels**, so a virtual display thing and a preserved-bucket display thing of the same def / stuff / quality may land in the same row. `TryScanRowIdentities` and `MaterializeForCaravan` handle only these two identities; with multiple virtual keys or multiple preserved buckets the result must fail-closed (record `Fail`, strip the virtual display thing, zero the row) — never silently take the first, and never aggregate multiple preserved buckets by key (W45 / W47).
- Loading row identity: when a row has multiple virtual display-thing keys, `TrySelectLoadRow` takes the first key with "availability > 0"; with multiple preserved buckets it tries each and picks any bucket from which "this trip can take the whole bucket's true bodies", **no fail-closed, no zeroing**. Bucket resolution itself stays fail-closed (0 or >1 bucket hits return null; never aggregate by key); the preserved per-trip take is also capped by the display thing's representable-unit limit.

### 12.7 Periodic Safety Nets and Debounce

- Map signature and stock nudges check once per **30 ticks**: `CheckMapSignature` + `TickStockNudges`; map changes converge within at most 30 ticks, and the wealth / apparel alerts themselves have 300 / 600-tick debounce.
- The structure safety net remains **250 ticks** but fetches buildings precisely by the three defs `SDS_BuildingCore` / `SDS_BuildingPort` / `SDS_BuildingCapacityArray`, no longer scanning all `BuildingArtificial`.
- The safety net calls `SDS_MirrorService.NotifyStockChanged()` and `SDS_AccessPointResolver.Invalidate()` only when `ReconcileStructures` returns `changed`; `Reconcile` already compares `Operational`, so core power loss / recovery goes through the `changed` branch and needs no unconditional re-evaluation.
- With `excludeGridFromWealth = true`, stock changes do not trigger the 300-tick `WealthWatcher.ForceRecount`; the toggle itself is backstopped by `SDS_SettingsApplier.RefreshWealth`.
- Toggling `separateByQuality` / `separateByStuff` must call `SDS_Core.NotifyCapacitySettingsChanged()` and discard ledger-derived indexes; otherwise capacity grouping carries over until the next stock change.
- Reservation retirement is driven by this module: `SDS_GameComponent_Grid.FinalizeInit` calls `SDS_Core.AuditReservations(ownerAlive)` once immediately, then once every **250 ticks**; the ledger only provides snapshots and release-by-owner-id, while liveness facts (`ISDS_ReclaimWorld.JobExists` + `Pawn.Destroyed`) are passed in by this module as delegates — the ledger must not read job / pawn liveness semantics itself.

## 13. Known Boundaries and Stable Facts

> This section records old design conclusions verified as still applicable, supplementing only boundaries not covered by §12; it introduces no new rule IDs, and on conflict with §1–§12 the earlier text prevails.

### 13.1 Visibility / Mirror Boundaries

0. Three access measures: ① `ListerThings` (`ThingsInGroup` / `ThingsOfDef` / `ThingsMatching`, spawned top-level things only); ② the holder tree (`Map.GetChildHolders` → `IThingHolder` → `ThingOwner` → `GetAllThingsRecursively`, including container interiors); ③ `SlotGroup` (`AllGroupsListForReading` / `HeldThings`, `ResourceCounter`'s main measure). There are also the `IHaulSource` / `IHaulDestination` registries and `ListerHaulables`, each with different mirror visibility.
1. Preserved true bodies never spawn and are not in the holder tree; invisible under all three measures.
2. Endpoints and vaults are detached from map-level holders; third parties that directly read endpoint / vault containers can still see them.
3. Mirrors are invisible to player rendering / selection / hauling / merging / damage / deconstruction but visible to queries and AI; with the setting on, non-players can still be seen by queries, but the regular take entries (search / reachability / jobs / carrying / containers / endpoints) are refused by S18.
4. Virtual deposit ⇒ `CanRebuildDef = true`; `CanRebuildDef` does not participate in mirror-building filters.
5. With G off, `SyncAll` takes the `RemoveAll` path; data is retained and rebuilt after recovery.
6. Trade / caravan formation / loading lists hold display things only, never mirrors.
7. `ListerBuildings` etc. still see vault buildings, but their market value / beauty are 0, with no comps and no `buildingTags`.
8. `GenRadial.RadialDistinctThingsAround` / `ThingGrid.ThingsAt` work by vault cell only, with no access-point adaptation.
9. Multi-colony wealth counts `PreservedWealth` only on "canonical maps"; non-canonical maps subtract their own mirrors.
10. Compatibility scope = vanilla implementation paths; third-party self-made products / direct `stackCount` edits are incompatible, with no polling reconciliation.
11. Projections are not real containers: `TryAdd` / `GetCountCanAccept` are always false / 0; `Remove` / `RemoveAt` beyond `stackLimit` return false; `Contains` is not guaranteed false (the internal list is empty before the first read; after any read that triggers `EnsureFresh` it can hit mirror references).
12. ProjectE instant transmutation bypasses the vanilla pipeline: swap in true bodies first, preserved takes no part, area transmutation excludes mirrors; fail-closed when the hook point is not in effect.
13. Mirror-build failure window: `MapExposesGrid` does not judge L off due to placement failure; within the window, counts / accessibility read from the ledger while enumeration / search see no mirrors; `ScheduleRetry` does a full retry after a 250-tick backoff.
14. Projections are not persisted: `SDS_ProjectionOwner.ExposeData` is empty, and loads rebuild from the ledger; `TryAdd` / `GetCountCanAccept` are always 0 / false; changes to `SDS_MirrorService.Version` force projection snapshot rebuilds.
15. Only when W52 priority outflow is on and "highest priority" is off do mirrors enter `ListerHaulables`; whether they stay is decided by vanilla `IsInValidBestStorage` (strictly higher priority and entry allowed), refreshed by `SDS_MirrorService` every 30 ticks; with the setting off or highest priority on, the refresh removes candidates. That decision's "current priority" comes from the warehouse proxy (W53); mirrors cannot be deposited into any endpoint (W53), so endpoints cannot become outflow destinations.
16. Display-thing identity: sealed by the factory at creation; after load, unsealed persisted display things inside container rows are sealed by `SDS_TransportVirtual.RebindLoadedDisplayKeys` (unique preserved bucket first, then virtual key; with no preserved candidate, fall back by virtual key — stale preserved display things may be classed as virtual, stock untouched); when identity cannot be uniquely resolved it stays unbound and fails closed as a non-display thing. The sole criterion is `SDS_OutputUtility.IsDisplay`; the sole key-resolution entry is `KeyOfDisplay`.

### 13.2 Representation × Measure Matrix

| Representation | Enumeration | Search | Counting | Wealth | Reachability |
|---|---|---|---|---|---|
| Mirror | 1 per category (vault cell / region / vault group); 0 at map level, counted 1 via `spawnedThings` | 1 candidate, distance translated to access point | 1, = ledger amount after patch correction | Kept on canonical maps, subtracted as own-map mirrors on non-canonical maps | Reachable via any access point |
| Projection | 1 in direct subtree, 0 at map level | 0 | 0 | 0 | — |
| Preserved true body | 0 | 0 | Only `CountAsResource` adds extra | Added 1 only on canonical maps | 0 |
| Display thing | 0 | 0 | 0 (read-only lists supplement separately) | 0 | 0 |
| Numeric delta | 0 | 0 | Target amount − exposure already counted by vanilla | Ledger virtual + preserved − this map's actual mirrors, clamped to 0 | Produces no candidates |

### 13.3 Known Search Boundaries (Supplementing §9 / §12.6)

- No candidate supplementation when `thingReq` is Undefined / Nothing / Everything.
- Unsupported `thingClass` falls back to the vanilla-class mirror; forced casts to subclasses may mismatch.
- Pickup / access points: chip-carrying pawns / mechanoids' own ports are distance 0 and outrank building endpoints; reachability decisions use the same standard (when the origin is the pawn's own cell, the own port is the distance-0 access point). Deposit ordering no longer carries that special case (see §12.6 tie-breaking).
- Mirror true-body coordinates (the hidden vault) serve only counting / exposure and never pathing or takes; with no available endpoint, end as target unreachable (W54).
- Preserved categories never enter `GenClosest`; a dedicated bridge takes over.
- `RegionwiseBFSWorker` is not patched separately; third-party direct calls see only the vault region.
- Atomic refueling goes through region BFS with no access-point fallback (not taken over).
- DoBill ingredients measure distance / radius from the vault cell; on failure the virtual / preserved fallback takes over, and `HasReachableEndpointWithin` respects the radius.
- Merge-on-the-way may pick a mirror, so it is switched to taking at the endpoint.
- `ThingGrid` / `GenRadial` do not translate access-point distances.
- Postfix-type adapters pick between Global-semantics candidates and the vanilla best; only Prefix-type adapters can enforce region priority (S9).
- The warehouse proxy has `Accepts` always true, `HaulDestinationEnabled = false`, priority following global.
- Endpoints are visible only in non-`SlotGroup` container chains.
- The same mirror may be produced simultaneously by `Region.ListerThings`, global collections, and search-time projections; dedupe by `Thing` reference and compare only once.

### 13.4 Work Type × Integration Matrix

> This table records conclusions and reasons for "vanilla work type → whether items can be taken via this mod's endpoints"; the "Criterion" column states vanilla conditions and does not necessarily correspond to this module's code. Actual integration points follow PatchInventory.en.md and [Interfaces.en.md](Interfaces.en.md) §4.5 / §4.6.

| Work type | Integrated | Criterion or reason |
|---|---|---|
| Construction materials | Yes | Does not go through `IsInValidStorage` |
| Repair | Yes | `IsForbidden` + `CanReserve` |
| Fermenter filling | Yes | — |
| Biosculpter pod | Yes | `CanAcceptNutrition` |
| Growth vat | Yes | Thresholds 2.5 / 0 in two segments |
| Subcore scanner | Yes | — |
| Gene bank loading | No | It looks for a target gene bank; genepacks go through the preserved bridge |
| Prisoner hemogen pack | Yes | — |
| Making xenogerms | Yes | — |
| Hair dyeing / styling | Yes | `Global_Reachable` |
| Medicine fetching | Yes | Has a `priorityGetter` |
| Food fetching | Yes | Humans -1, animals 100 / pens, internal budget 30; when S18 is on, non-player factions are not integrated |
| Delivering prisoners / guests | Yes | — |
| Generic reservables | Yes | — |
| Turret ammo loading | Yes | — |
| Single-item refueling | Yes | — |
| Atomic refueling | No | No `RegionTraverser` fallback |
| Hopper filling | Yes | Patched with `IsInValidStorage` |
| Dyeing | Yes | `CanReach` patch in effect |
| Automatic dressing | Yes | Patched with `IsInAnyStorage` |
| DoBill | Limited | Visible only within `ingredientSearchRadius` |
| Merge-on-the-way | Limited | May go take at the endpoint |
| Loading containers / loading | Yes | Does not call `GenClosest` itself; loading goes through `LoadTransportersJobUtility` |

## 14. Channel Split (Mutual No-Reference Between Virtual / Preserved)

> Per DesignPrinciples.en.md: channel rules are uniquely converged in Core / Rules (channel decisions / ledger / availability); ports / UI / saves / reclaim / vanilla hook points must be shared per Principle 1 / Corollary 3. The only thing that can be fixed at the file level is direct dependencies between the two **channel-exclusive directories**; the remaining interweavings are structural sharing and must not be split (the assembly-level two-split plan was rejected at the 2026-11-16 review).

| Directory | Channel ownership | Constraint |
|---|---|---|
| `Mirrors/` | Virtual-exclusive | Must not reference top-level types declared in `Preserve/` |
| `Preserve/` | Preserved-exclusive | Must not reference top-level types declared in `Mirrors/` |
| `Endpoints/` `Foundation/` `Jobs/` `Query/` `TradeTransport/` `UI/` `Install/` | Shared (the only legal bridge) | May reference both sides; channel-exclusive members may live only in the exclusive directories or this layer, and moving the other side's exclusive types into the shared layer to bypass checks is forbidden |

- Channel-exclusive member ownership is by "directory + type": `Preserve/SDS_PreservedBill.cs` carries only the preserved fallback and the preserved-take JobDriver; the sole implementation of the virtual-mirror ingredient fallback is `Jobs/SDS_VirtualBillFallback.cs` (including the reflection probe of the vanilla ingredient algorithm and `RootCell`, shared by both channels; the preserved side only calls it, never re-implements).
- `Patch_SDS_PreservedBillIngredients` is a **dual-channel hook point** (virtual mirrors first, then preserved true bodies, W51 multi-slot materialization): it may live in `Preserve/`, but the virtual leg must call the shared-layer `SDS_VirtualBillFallback`, and mirror facts must not be copied into preserved files.
- `SDS_PreservedOffer` carries only preserved selection and preserved hook points; the dual-channel supply offer table, placement, rot freezing, timeout-return-to-source, and the virtual leg are implemented solely in the shared layer `Foundation/SDS_OfferBridge.cs` (the preserved leg only calls `TryGetAccess` / `TrackPlaced` / `RollbackOrWarn` / `InCooldown` and must not build a second offer table; splitting into two means a second lifecycle, violating Principle 3). Source: 2026-11-16 Plan B.
- The channel decision lives uniquely in `SDS.Rules` (the `RequiresPreservedStorage` instance overload / the `key.preserved` key bit); this section constrains only contact-surface file ownership and adds no second channel criterion.

Verification: `CheckAssemblyBoundaries.ps1` §10 (extracts the top-level type names of the two directories, strips comments line by line, then scans symbols in both directions; any hit fails). Source: the 2026-11-16 plan review (assembly-level two-split rejected; Plan A adopted).
