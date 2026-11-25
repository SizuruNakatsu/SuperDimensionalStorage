# SuperDimensional Storage · Patch Inventory

> Historical baseline: the old framework's five-axis design document (a pure historical archive since 2026-11-12, no longer referenced); every entry in this table's "Authority" column now points to documents within this framework.
>
> Patch loading: `SDS_Mod.ApplyHarmonyPatches` runs `CreateClassProcessor(type).Patch()` per class with try/catch; a single bad patch cannot bring down the whole system.
> "Node" = vanilla type.method; `TargetMethod()` classes are annotated according to the actual resolution result.
>
> **Column conventions (2026-10-28)**
>
> - **Layer**: `Scalar read` / `Seeing/Search` / `Consumption` / `Job assignment` / `Manifests/Settlement` / `Boundary`. Describes only the contract layer the patch serves; it does not indicate code ownership.
> - **Reason**: `Contract` (hard cost of the vanilla contract) / `Projection` (cost of the projection representation) / `Rule` (cost of this mod's rules) / `Compatibility` (third-party compatibility cost).
> - **Authority**: where the patch's design basis lands within this framework (`module/document §section`); `—` = no domain document (pure presentation / engineering boundary).
> - **Rules**: invariant ID (R / S / W / E) or `—`. This inventory only references rules; it does not define them.
> - **Patch class**: 112 `[HarmonyPatch]` attribute classes + 29 registry entry Ids (§11, `SDS_MirrorGuardRegistry` / `SDS_ProjectionOwnerBridge`); the latter are not C# classes.
>
> **Verification**: `powershell -NoProfile -ExecutionPolicy Bypass -File .\CheckPatchInventory.ps1`
> performs a bidirectional comparison between source `[HarmonyPatch(...)]` / `[HarmonyPatch]`+`TargetMethod()` / both registries' `Id = "Patch_..."` and this inventory, plus tally reconciliation, dynamic patch registration, metadata validity, the §11 baseline (19 + 10) and §12 special cases (3); `-Report` prints the attribute classes + registry entries and the deduplicated count of TargetMethod text keys; `-Targets` prints TargetMethod descriptions and method bodies.

---

## 1. Mirror lifecycle and protection (SDS_Mirrors.cs + Patch_Mirrors.cs / SDS_MirrorGuardRegistry.cs)

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_PawnProjectionExposure` | `Pawn.GetChildHolders` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.2 | — | Attaches Hyperdimensional projections to the subtree enumeration of chip-carrying pawns |
| `Patch_SDS_WealthItemsFilterProjection` | `WealthWatcher.WealthItemsFilter` | Scalar read | Projection | SDS.Vanilla/Interfaces §4.4 | — | Lets `SDS_MapLevelHolder` / projections be filtered under the same criteria as the original pawn |
| `Patch_SDS_MirrorNotHaulable` | `ListerHaulables.ShouldBeHaulable` | Seeing/Search | Projection | SDS.Vanilla/Rules §10 | W52 / W55 | W52: with the setting off / highest priority on, mirrors do not enter "haulable"; when outflow is on, they are handed back to vanilla to be judged by strictly higher priority; W55: with highest priority on, storable physical items that vanilla judged "already in optimal storage" become haulable again via `IsGridAlwaysBetter` |
| `Patch_SDS_MirrorNotMergeable` | `ListerMergeables.ShouldBeMergeable` | Seeing/Search | Projection | SDS.Vanilla/Rules §11; SDS.Vanilla/Rules §10 | W20 | Mirrors do not enter the "mergeable" list |
| `Patch_SDS_MirrorAbsorb` | `Thing.TryAbsorbStack` | Consumption | Projection | SDS.Vanilla/Rules §11; SDS.Vanilla/Rules §10 | W20 | A mirror being absorbed → take the goods first, then merge; a mirror absorbing others → refused (depositing does not follow merge semantics) |
| `Patch_SDS_MirrorAbsorbWithComps` | `ThingWithComps.TryAbsorbStack` | Consumption | Projection | SDS.Vanilla/Rules §11; SDS.Vanilla/Rules §10 | W20 | Same as above (WithComps subclass) |
| `Patch_SDS_MirrorSpawnSetup` | `Thing.SpawnSetup` | Seeing/Search | Projection | SDS.Vanilla/Rules §10 | — | After a mirror spawns, it is registered into the mirror index |
| `Patch_SDS_MirrorNotStudiable` | `StudyManager.UpdateStudiableCache` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | W36 | Mirrors are not registered into the vanilla studiable cache, so work assignment does not treat the hidden vault as a study target |
| `Patch_SDS_MirrorTakeOnSplitOff` | `Thing.SplitOff` | Consumption | Projection | SDS.Vanilla/Rules §10 | W5 | When a mirror is SplitOff, the true body is taken from the ledger (covers mirrors that fell back to vanilla classes) |
| `Patch_SDS_MirrorCarry` | `Pawn_CarryTracker.TryStartCarry(Thing)` | Consumption | Projection | SDS.Vanilla/Rules §10 | W5 | Single-parameter overload: taking a mirror → the true body enters the hand |
| `Patch_SDS_MirrorCarryCounted` | `Pawn_CarryTracker.TryStartCarry(Thing,int,bool)` | Consumption | Projection | SDS.Vanilla/Rules §10 | W5 | Counted overload: taking a mirror → the true body enters the hand and the actual count is returned |
| `Patch_SDS_MirrorNoDamage` | `Thing.TakeDamage` | Consumption | Projection | SDS.Vanilla/Rules §11 | — | Mirrors are immune to damage |
| `Patch_SDS_VaultPreApplyDamage` | `Thing.PreApplyDamage` | Consumption | Projection | SDS.Vanilla/Rules §11 | — | Vaults are immune to damage (does not block legitimate Destroy) |
| `Patch_SDS_MirrorNoFire` | `FireUtility.TryAttachFire` | Consumption | Projection | SDS.Vanilla/Rules §11 | — | Mirrors / vaults do not catch fire |
| `Patch_SDS_MirrorNoDeSpawn` | `Thing.DeSpawn` | Consumption | Projection | SDS.Vanilla/Rules §10 | W18 | Mirrors refuse DeSpawn unless Unmarked |
| `Patch_SDS_MirrorNoDeSpawnWithComps` | `ThingWithComps.DeSpawn` | Consumption | Projection | SDS.Vanilla/Rules §10 | W18 | Same as above (WithComps subclass) |
| `Patch_SDS_MapDeinitAllowMirrorDespawn` | `MapDeiniter.Deinit` | Consumption | Projection | SDS.Vanilla/Rules §10 | W18 | Temporarily allows mirror DeSpawn during map unloading; also calls `EvacuateForStructureChange` before `Deinit` to evacuate this map's projections |
| `Patch_SDS_MirrorDestroyConsume` | `Thing.Destroy` | Consumption | Projection | SDS.Vanilla/Rules §10 | W19 | External destruction of mirrors is **fail-closed**: refuse destruction + one-time warning; internal cleanup Unmarks first and bypasses this check |
| `Patch_SDS_MirrorDestroyConsumeWithComps` | `ThingWithComps.Destroy` | Consumption | Projection | SDS.Vanilla/Rules §10 | W19 | Same as above (WithComps subclass) |
| `Patch_SDS_MirrorReserveNonExclusiveCanReserve` | `ReservationManager.CanReserve` | Consumption | Projection | SDS.Vanilla/Rules §10 | W61 | Reservation conflicts on mirror targets are always ignored; **returns false when G is off** (candidate-stage filtering, preventing Job infinite loops); non-mirrors unchanged |
| `Patch_SDS_MirrorReserveNonExclusiveCanReserveStack` | `ReservationManager.CanReserveStack` | Consumption | Projection | SDS.Vanilla/Rules §10 | W61 | Reservable count for a mirror target = the mirror's current stack count; **returns 0 when G is off** |
| `Patch_SDS_MirrorNoRealtimeDraw` | `Thing.DynamicDrawPhaseAt` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Mirrors are excluded from realtime drawing |
| `Patch_SDS_MirrorNoMeshPrint` | `Thing.Print` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Mirrors are excluded from map mesh printing |
| `Patch_SDS_MirrorNoMeshPrintWithComps` | `ThingWithComps.Print` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Same as above (WithComps subclass) |
| `Patch_SDS_MirrorNoGuiOverlay` | `Thing.DrawGUIOverlay` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Mirrors draw no stack-count / quality overlay |
| `Patch_SDS_MirrorNoGuiOverlayWithComps` | `ThingWithComps.DrawGUIOverlay` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Same as above (WithComps subclass) |
| `Patch_SDS_MirrorNotSelectable` | `ThingSelectionUtility.SelectableByMapClick` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Mirrors cannot be selected by map click |
| `Patch_SDS_MirrorNotSelectableDirect` | `Selector.Select` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Third-party direct selection of mirrors is also refused |
| `Patch_SDS_RoomIgnoresHiddenGrid` | `Room.ContainedAndAdjacentThings` (property getter) | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Room statistics ignore mirrors / vaults |
| `Patch_SDS_RoomContainsThingIgnoresHiddenGrid` | `Room.ContainsThing` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Room "contains thing" does not count mirrors / vaults as a hit |
| `Patch_SDS_RoomContainedThingsIgnoresHiddenGrid` | `Room.ContainedThings` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Room content enumeration strips mirrors / vaults |
| `Patch_SDS_RoomThingCountIgnoresHiddenGrid` | `Room.ThingCount` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Room count statistics strip mirrors / vaults |
| `Patch_SDS_ObserverIgnoresHiddenGrid` | `PawnObserver.PossibleToObserve` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Observation / dread thoughts do not target mirrors / vaults |
| `Patch_SDS_TantrumIgnoresHiddenGrid` | `TantrumMentalStateUtility.CanSmash` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Tantrums do not target mirrors / vaults |
| `Patch_SDS_FireSpreeIgnoresHiddenGrid` | `JobGiver_FireStartingSpree.TryFindRandomIgniteTarget` | Seeing/Search | Projection | SDS.Vanilla/Rules §11 | — | Fire sprees do not target mirrors / vaults |

## 2. Take primitives and the projection owner (SDS_ProjectionOwnerBridge.cs)

> The "take-primitive adapter" of the target architecture: it only identifies mirrors, calls `SDS_WithdrawExecutor` to fetch true bodies, and delivers into hand / container per the vanilla contract.
> It does not select endpoints, does not compute quantities, and does not debit directly.
> The mirror-side patches of the take primitives (`Patch_SDS_MirrorTakeOnSplitOff` / `MirrorCarry` / `MirrorCarryCounted`) are in section 1;
> `SDS_Transmission`'s `Patch_SDS_OwnerTryAdd` / `TryAddOrTransfer` are in section 7.

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_ProjectionTake` | `ThingOwner.Take(Thing,int)` | Consumption | Projection | SDS.Rules/Rules §1 | W2, W5 | Takes on the projection owner are redirected to `SDS_TakeService` |
| `Patch_SDS_ProjectionTryDropCounted` | `ThingOwner.TryDrop(...,int,...)` | Consumption | Projection | SDS.Rules/Rules §1 | — | Counted drops on the projection owner are redirected to the ledger |
| `Patch_SDS_ProjectionTryDropAll` | `ThingOwner.TryDrop(...)` | Consumption | Projection | SDS.Rules/Rules §1 | — | Whole-stack drops on the projection owner are redirected to the ledger |
| `Patch_SDS_ProjectionTryTransfer` | `ThingOwner.TryTransferToContainer` | Consumption | Projection | SDS.Rules/Rules §1 | — | Transfers on the projection owner are redirected to the ledger |
| `Patch_SDS_EndpointTransferReturn` | `ThingOwner.TryTransferToContainer` (5-param) | Consumption | Contract | SDS.Rules/Rules §1 | — | After an endpoint write-through succeeds, backfills the actually received amount and restores the enroute / `INotifyHauledTo` / `placedThings` callbacks |
| `Patch_SDS_ProjectionClear` | `ThingOwner.Clear` | Consumption | Projection | SDS.Rules/Rules §1 | — | Projection bulk clears only detach references, no debiting |
| `Patch_SDS_ProjectionClearAndDestroy` | `ThingOwner.ClearAndDestroyContents` | Consumption | Projection | SDS.Rules/Rules §1 | — | Same as above |
| `Patch_SDS_ProjectionClearAndDestroyOrPassToWorld` | `ThingOwner.ClearAndDestroyContentsOrPassToWorld` | Consumption | Projection | SDS.Rules/Rules §1 | — | Same as above |
| `Patch_SDS_ProjectionRemoveAll` | `ThingOwner.RemoveAll(Predicate<Thing>)` | Consumption | Projection | SDS.Rules/Rules §1 | — | Same as above |
| `Patch_SDS_ProjectionRemoveAllGeneric` | `ThingOwner<Thing>.RemoveAll(Predicate<Thing>)` | Consumption | Projection | SDS.Rules/Rules §1 | — | Same as above (generic overload) |

## 3. Search / reachability / Job targets / storage checks / bill supplements

> The "search adapter" of the target architecture: only handles mirror visibility, distance folding to access points, and reachability rewriting.
> **Does not materialize, does not debit, does not rewrite Jobs.**

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_ClosestGlobal` | `GenClosest.ClosestThing_Global` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.6 | S2, S4 | Mirror distance is folded to the nearest access point; ties prefer the mirror |
| `Patch_SDS_ClosestGlobalReachable` | `GenClosest.ClosestThing_Global_Reachable` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.6 | S2, S4 | Same as above + reachability delegated to the `CanReach` patch |
| `Patch_SDS_ClosestReachable` | `GenClosest.ClosestThingReachable` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.6 | S9, S12a | Appends mirror candidates at the exit; the Postfix cannot distinguish Region / Global, so it picks between Global semantics and the vanilla best |
| `Patch_SDS_ClosestRegionwisePrioritized` | `GenClosest.ClosestThing_Regionwise_ReachablePrioritized` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.6 | S12b | Appends mirror candidates when a priority is given |
| `Patch_SDS_ReachabilityMirror` | `Reachability.CanReach(IntVec3,LocalTargetInfo,PathEndMode,TraverseParms)` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.6 | S10 | A mirror is reachable iff any access point is reachable |
| `Patch_SDS_StealValueIgnoresHiddenGrid` | `StealAIUtility.GetValue` | Seeing/Search | Projection | SDS.Vanilla/Rules §10 | S15 | The steal value of mirrors / vaults reads as 0 (companion: `HaulableEverOrMinifiable` adds no mirror candidates), so raid AI does not treat projections as stealable goods |
| `Patch_SDS_MirrorTargetGoto` | `Toils_Goto.GotoThing` | Consumption | Projection | SDS.Vanilla/Interfaces §4.6 | S10 | When pathing to a mirror, the destination is switched to the access point |
| `Patch_SDS_JobMirrorTargets` | `Pawn_JobTracker.StartJob` | Consumption | Projection | SDS.Vanilla/Rules §10 | W5, W30 | By default Hand does not materialize; only escape-hatch Jobs explicitly registered as `SpawnedTarget` (bottle feeding) materialize during job assignment; the Postfix releases this Job's reservations on mirrors |
| `Patch_SDS_HaulCountIntent` | `Toils_Haul.ErrorCheckForCarry` | Consumption | Rule | SDS.Vanilla/Rules §10 | W58 | When a mirror target has `job.count ≤ 0`, it is topped up according to the Job's reservation intent (`Reservation.StackCount`); without a reservation it stays 1 |
| `Patch_SDS_WearConsumeSeam` | `JobDriver_Wear.Notify_Starting` | Consumption | Rule | SDS.Vanilla/Rules §10 | W59 | Before wearing, swaps the mirror target for 1 true body |
| `Patch_SDS_EquipConsumeSeam` | `JobDriver_Equip.Notify_Starting` | Consumption | Rule | SDS.Vanilla/Rules §10 | W59 | Before equipping, swaps the mirror target for 1 true body |
| `Patch_SDS_UseItemConsumeSeam` | `JobDriver_UseItem.Notify_Starting` | Consumption | Rule | SDS.Vanilla/Rules §10 | W59 | Before using an item, swaps the mirror target for 1 true body (including research-bench drivers) |
| `Patch_SDS_IngestConsumeSeam` | `JobDriver_Ingest.Notify_Starting` | Consumption | Rule | SDS.Vanilla/Rules §10 | W59 | Non-tool users only: before ingesting, swaps the mirror target for `max(1, job.count)` true bodies |
| `Patch_SDS_NoWearMirror` | `Pawn_ApparelTracker.Wear` | Consumption | Projection | SDS.Vanilla/Rules §11 | W5 | Mirrors can never enter `wornApparel` (final gate, kept) |
| `Patch_SDS_ReclaimJobTargets` | `Pawn_JobTracker.CleanupCurrentJob` | Consumption | Rule | SDS.Rules/Rules §1 | W27 | Transitional: when a Job fails / is interrupted, returns the Job targets and carried items; on success, discards the registration (Core/DesignDecisions §2 item 13); in the target architecture this merges into transaction rollback |
| `Patch_SDS_MirrorIsInStorage` | `StoreUtility.IsInAnyStorage` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.1 | — | Mirrors count as "in storage" |
| `Patch_SDS_MirrorIsInValidStorage` | `StoreUtility.IsInValidStorage` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.1 | — | Mirrors count as "in valid storage" (hopper loading) |
| `Patch_SDS_MirrorStoragePriority` | `StoreUtility.CurrentStoragePriorityOf` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.1 | — | Mirror priority comes from the global single source of truth |
| `Patch_SDS_MirrorHaulDestination` | `StoreUtility.CurrentHaulDestinationOf` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.1 | — | Mirror → the per-map stockpile proxy; on exception, the vanilla destination is kept |
| `Patch_SDS_MirrorNotSellable` | `TradeUtility.PlayerSellableNow` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.7 | — | Mirrors cannot be sold |
| `Patch_SDS_MirrorNotLaunchable` | `TradeUtility.AllLaunchableThingsForTrade` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.7 | — | Mirrors are excluded from the launch manifest |
| `Patch_SDS_StartCarryAlreadyCarried` | `Toils_Haul.StartCarryThing` | Consumption | Rule | SDS.Vanilla/Rules §10 | W62 | No duplicate hauling when the target is already carried; the cell-only self-deposit fold credits the ledger immediately after pickup, falling back to dropping in place on failure |
| `Patch_SDS_MirrorNotCaravanItem` | `CaravanFormingUtility.AllReachableColonyItems` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.8 | — | Mirrors are stripped from the original caravan formation / loading manifest |
| `Patch_SDS_PreservedBillIngredients` | `WorkGiver_DoBill.TryFindBestBillIngredients` | Job assignment | Rule | SDS.Vanilla/Interfaces §4.9 | S17 / W51 | After vanilla fails, first tries virtual mirrors, then preserved true bodies as fallback (single slot); for multi-slot recipes, finally materializes the preserved shortfall onto the workbench interaction cell and re-runs the vanilla search in the same tick (W51, no new hook points); medical candidates first reuse the vanilla medical-care-category criterion |
| `Patch_SDS_MedicalIngredientGate` | `WorkGiver_DoBill.IsUsableIngredient` | Job assignment | Rule | SDS.Vanilla/Rules §10 | S17 | Medicine candidates for medical bills (`Bill_Medical`) uniformly pass the vanilla medical-care-category criterion; covers the `IHaulSource` recursion branch |
| `Patch_SDS_VirtualFixedIngredients` | `WorkGiver_DoBill.TryFindBestFixedIngredients` | Job assignment | Rule | SDS.Vanilla/Rules §10 | — | Virtual fallback after fixed ingredients fail |
| `Patch_SDS_PreservedBillJob` | `WorkGiver_DoBill.TryStartNewDoBillJob` | Job assignment | Rule | SDS.Vanilla/Rules §10 | — | Pure preserved-ingredient Jobs are swapped for endpoint take Jobs |

## 3.6. Vanilla and third-party "consume-destination-cell-only" entry folding / deferral (W63)

> W63 full text: see SDS.Vanilla/Rules.en.md §10.
> W63 first tries to fold the destination (its own port or the building endpoint's `AccessCellOf`; when not standable, falls back to the building cell, walkability not guaranteed; defers if the destination is unreachable); only when the destination cannot be resolved does it defer to vanilla's "no empty storage space".
> Coverage = enumerating all `Worker`s of `DefDatabase<WorkGiverDef>` at runtime and attaching deduplicated Postfixes to the most derived `JobOnThing` / `HasJobOnThing` (implementation class `SDS_CellOnlyWorkGiverPatches`, not a `[HarmonyPatch]` attribute class, not counted in this table's tally). The type-specialized hook points for vanilla `EmptyWasteContainer` / `EmptyEggBox` have been removed; coverage is now unified under the runtime enumeration.
> `TryFindBestBetterStorageFor` itself is not patched; W63 only rewrites Job destinations and adds no second debiting path.

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_CellOnlyOrderedJob` | `Pawn_JobTracker.TryTakeOrderedJob` | Job assignment | Contract | SDS.Vanilla/Rules §10 | W63 | Player-ordered cell-only Jobs (including relic extraction) fold the destination first; only on failure are they handed to vanilla |

- Runtime hook points (not attribute classes, not counted in the table above): after Def loading completes, `SDS_CellOnlyWorkGiverPatches` enumerates all `WorkGiverDef.Worker` and attaches deduplicated Postfixes to the most derived `JobOnThing` / `HasJobOnThing`, uniformly calling `SDS_CellOnlyDestinationFold.TryRewrite` / `SDS_CellOnlyJobShape`; it also attaches to `Toils_Haul.PlaceHauledThingInCell` as the sole cell-placement crediting hook (W63). Startup logs print `JobOnThing N / HasJobOnThing M`; failures are logged one by one via `ErrorOnce` and summarized.
- The relic entry is no longer grayed out separately; `Patch_SDS_CellOnlyOrderedJob` folds the destination at Job ordering time without duplicating the check.

## 3.5. Unified take trunk (target architecture)

> This section is a historical migration-planning archive, not a Harmony patch, and does not change the 141-patch-point tally; the core `SDS_WithdrawResolver` / `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor` / `SDS_WithdrawQuery` have landed; Job virtual withdrawal has been folded into the transaction, while session transactions are still settled as Instant.
> W1–W16 full text: see SDS.Rules/Rules.en.md §5 12.18; W21 / W22 / W27: see §1 / §2 of the same file.

| New node | Role | Rules |
|---|---|---|
| `SDS_WithdrawResolver` | Sole resolver | endpoint / access point, key, qty, preserved, mode; withdrawal considers distance only; one transaction per endpoint |
| `SDS_WithdrawTransaction` | Sole transaction | `Begin` atomically reserves multiple keys; `Execute` debits and materializes; `Deliver` delivers; `Rollback` rolls back; suspend keeps reservations, terminate releases them |
| `SDS_WithdrawExecutor` | Sole debit / materialization | three paths: virtual / preserved / mirror; only here may `TryWithdraw` be called or true bodies be taken |
| `SDS_WithdrawPlan` / `SDS_WithdrawMode` | Plan and mode | Hand / Container / SpawnedTarget / VirtualHandoff |
| `SDS_WithdrawQuery` | Read-only query | key / qty / preserved; available counts uniformly go through `SDS_Availability`; creates no transactions |
| `SDS_WithdrawDeliveryPolicyDef` | Delivery policy config | Def table; default `Hand` (no materialization); third parties may append; mode validated at load; jobDef / driver resolved lazily on first use |
| `JobDriver_SDS_WithdrawForJob` | Job target adapter (**target architecture, not yet implemented**) | consumes Jobs directly with the default Hand; explicit `SpawnedTarget` serves only as the escape hatch (currently bottle feeding); rewrites the necessary target fields per mode. The current implementation is still `JobDriver_SDS_WithdrawFromGrid` / `JobDriver_SDS_WithdrawPreservedForBill` |
| `SDS_GameComponent_Grid` reservation pool | Logical reservations | `GetAvailableCount = total − reserved`; multi-key atomic (validate all first, then write together) |
| `SDS_ReclaimRegistry` | Sole reclaim owner (Vanilla, C1) | returns go through `SDS_Rules.ReturnThing` (on failure, fall back to Ground / PawnInventory); `SDS_ReclaimPolicy` (JobTarget / ProtectedOutput); the `sdsReclaim` node, inspection, and load audit all live in Vanilla |
| Load audit | Reservation end-of-life + reclaim restore | `SDS_Core.AuditReservations(ownerAlive)` is called by Vanilla at `FinalizeInit` and at tick 250; reclaim restore goes through `SDS_ReclaimRegistry.AuditRestored` (the former `SDS_WithdrawSave` type no longer exists) |
| `SDS_ReturnUtility` | Sole return / fallback implementation | `Return` (return → ground placement policy); `ReturnPreserved` (preserved rollback); `ReturnVirtual` / `ReturnVirtualCount` (virtual-ledger direct-write rollback) |
| `SDS_PlacementUtility` / `SDS_PlacementPolicy` | Sole placement-floor implementation | `Exact` / `NearPawn` / `RandomNear`; hard precondition `!Spawned` |

### Migration status (current → target)

| Current node | Target role | Status |
|---|---|---|
| `SDS_TakeService` | Thin wrapper over the executor | Rework |
| `SDS_Transmission` | Take-primitive adapter (Container) | Keep / wire into the executor |
| `SDS_JobTargetAdapter` | Job target adapter | Rework / superseded by `WithdrawForJob` |
| `SDS_JobTargetReclaim` | Merged into `SDS_ReclaimRegistry` (`ReclaimPolicy.JobTarget`) | Done (2026-10-22) |
| `MapComponent_SDS_ProtectedOutputs` | Merged into `SDS_ReclaimRegistry` (`ReclaimPolicy.ProtectedOutput`) | Retired (2026-11-16, outside the 2.4 window) |
| `SDS_TradeVirtual` / `SDS_CaravanVirtual` / `SDS_TransportVirtual` | Virtual delivery adapter | Keep injection timing; the display thing factory / identity table is unified in Vanilla `SDS_OutputUtility` (`ISDS_DisplayQuery`) |
| `SDS_PreservedUsage` | Preserved adapter | Rework into a unified transaction |
| `SDS_ModCompat_CE` take portion | CE adapter | Wire into the executor |
| `JobDriver_SDS_WithdrawFromGrid` / `SDS_WithdrawPreservedForBill` | Unify into `WithdrawForJob` or a shared base class | Merge |

> The take trunk must obey W1–W14; no entry may call `grid.TryWithdraw` directly or read `preservedOwner` directly.

## 4. Counting / accessibility / missing ingredients / wealth

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_ResourceCounterWithGrid` | `ResourceCounter.UpdateResourceCounts` | Scalar read | Projection | SDS.Vanilla/Interfaces §4.3 | — | Injects the ledger into the resource bar + mirror exposure delta correction |
| `Patch_SDS_ResourceCounterFastRefresh` | `ResourceCounter.ResourceCounterTick` | Scalar read | Projection | SDS.Vanilla/Interfaces §4.3 | — | Throttled forced refresh within 30 ticks after `StockVersion` changes |
| `Patch_SDS_PotentiallyMissingIngredients` | `RecipeDef.PotentiallyMissingIngredients` | Scalar read | Projection | SDS.Vanilla/Interfaces §4.3 | — | Replicates the stuff / quality / disabled / fog-of-war checks slot by slot |
| `Patch_SDS_WealthNormalize` | `WealthWatcher.CalculateWealthItems` | Scalar read | Projection | SDS.Vanilla/Interfaces §4.4 | — | Canonical maps add preserved wealth, non-canonical maps subtract mirrors |
| `Patch_SDS_MapChildHoldersExposure` | `Map.GetChildHolders` | Seeing/Search | Projection | SDS.Vanilla/Interfaces §4.2 | — | Strips endpoints / vaults from map-level enumeration and wraps projection-carrying pawns |
| `Patch_SDS_HaulGeneralToGrid` | `WorkGiver_Haul.JobOnThing` | Job assignment | Contract | SDS.Vanilla/Interfaces §4.5 | — | Job-assignment exit: when the upstream function-level exit has already re-routed, only backfills `workGiverDef` (for report semantics); otherwise replaces with this mod's multi-item deposit Job |
| `Patch_SDS_HaulToStorageJob` | `HaulAIUtility.HaulToStorageJob` | Job assignment | Contract | SDS.Vanilla/Interfaces §4.5 | — | The **sole function-level exit** of the vanilla "haul-to-storage Job": covers `WorkGiver_Haul` / `JobGiver_Haul` / KAU urgent hauling / the `HaulFromSource` float menu / `Toils_Haul.StoreThingJob` / `JobDriver_Reading`, and third-party direct calls |
| `Patch_SDS_JobGiverHaul` | `JobGiver_Haul.TryGiveJob` | Job assignment | Contract | SDS.Vanilla/Interfaces §4.5 | — | The **supplementary dispatch point** for when vanilla returns null (no SlotGroup / no storage zone): routes the same batch of candidates through `SDS_DepositService.TryMake`; a different division of labor from `HaulToStorageJob`, **not** a deleted node |
| `Patch_SDS_CarriedProductToGrid` | `HaulAIUtility.HaulToCellStorageJob` | Job assignment | Contract | SDS.Vanilla/Interfaces §4.5 | — | Cell-hauling exit: with the product already in hand (`Toils_Recipe`) goes through `TryMakeCarried`; **opportunistic hauling** (`Pawn_JobTracker.TryOpportunisticJob` calls this function directly) goes through ordinary `TryMake`. When the endpoint wins → replaced with `SDS_DepositToGrid` |
| `Patch_SDS_HaulToContainerJob` | `HaulAIUtility.HaulToContainerJob` | Job assignment | Contract | SDS.Vanilla/Interfaces §4.5 | — | Container-hauling exit: **opportunistic hauling** calls this function directly (`Pawn_JobTracker.cs:739`), completely bypassing `HaulToStorageJob`; when the endpoint wins → replaced with `SDS_DepositToGrid`, otherwise kept as-is |
| `Patch_SDS_Accessible` | `PlayerItemAccessibilityUtility.Accessible` | Scalar read | Projection | SDS.Vanilla/Rules §11 | — | Non-mirror physical items + ledger + preserved, added together |
| `Patch_SDS_PossiblyAccessible` | `PlayerItemAccessibilityUtility.PossiblyAccessible` | Scalar read | Projection | SDS.Vanilla/Rules §11 | — | Adds derived quantities (× `entry.count`) + preserved |
| `Patch_SDS_PlayerCanMake` | `PlayerItemAccessibilityUtility.PlayerCanMake` | Scalar read | Projection | SDS.Vanilla/Rules §11 | — | Adds deconstruction / harvest / slaughter / smelting derivatives |
| `Patch_SDS_PlayerHasDef` | `PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(ThingDef,int)` | Scalar read | Projection | SDS.Vanilla/Rules §11 | — | Recounts holdings wholesale (prevents multi-map double counting / mirrors counting as 1) |
| `Patch_SDS_PlayerHasFilter` | `PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas(ThingFilter)` | Scalar read | Projection | SDS.Vanilla/Rules §11 | — | Adds preserved true-body matching |
| `Patch_SDS_RecipeCounterCountProducts` | `RecipeWorkerCounter.CountProducts` | Scalar read | Projection | SDS.Vanilla/Rules §11 | — | Product counting adds N-1 or the full amount |
| `Patch_SDS_AlertNeedWarmClothes` | `Alert_NeedWarmClothes.FreeWarmClothesSetsCount` | Scalar read | Projection | SDS.Vanilla/Rules §11 | — | Warm clothing set count adds N-1 |
| `Patch_SDS_JobReport` | `JobDriver.GetReport` | Boundary | Rule | — | — | Current work text for deposit / take / endpoint Jobs |
| `Patch_SDS_JobReportHaulToContainer` | `JobDriver_HaulToContainer.GetReport` | Boundary | Rule | — | — | `HaulToContainer` overrides `GetReport` (without calling the base), so it is patched separately once, covering Hyperdimensional Core / Anchor / transport pod / map portal hauling |

## 5. Trading / caravan formation / loading / shuttles

> The "virtual delivery adapter" of the target architecture: display things are UI-only; opening a manifest only injects display things and **sets no cross-dialog reservations**;
> on deal execution / departure / loading, an Instant transaction is opened (`Execute + Deliver`); on close / cancel, only the display thing references are released.
> Must never call `grid.TryWithdraw` directly.

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_TradeDealReset` | `TradeDeal.Reset` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.7 | R16 | Clears leftover display things and injects ledger categories |
| `Patch_SDS_TradeDealValidate` | `TradeDeal.TryExecute` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W23 | Snapshot validation before the deal executes; a shortfall blocks the whole deal |
| `Patch_SDS_TradeableResolveTrade` | `Tradeable.ResolveTrade` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W24 | The sole choke point for sale settlement: swaps display things for true bodies and hands them back to vanilla (one path for local / orbital / remote / gifting, no per-trader-type patches) |
| `Patch_SDS_DialogTradeClose` | `Dialog_Trade.Close` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.7 | — | Releases display thing references (does not destroy) |
| `Patch_SDS_BeaconLaunchRead` | `TradeUtility.AllLaunchableThingsForTrade` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.7 | R15 | The beacon read exit appends display things |
| `Patch_SDS_BeaconLaunchTake` | `TradeUtility.LaunchThingsOfType` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W23, W24 | The beacon take exit debits the shortfall from the ledger first |
| `Patch_SDS_SettlementBuyList` | `Settlement_TraderTracker.ColonyThingsWillingToBuy` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.7 | — | Remote trade list injection |
| `Patch_SDS_CaravanBuyList` | `Caravan_TraderTracker.ColonyThingsWillingToBuy` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.7 | — | Caravan trade list injection |
| `Patch_SDS_SettlementGiveBought` | `Settlement_TraderTracker.GiveSoldThingToPlayer` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | — | Purchases go straight into the ledger |
| `Patch_SDS_CaravanGiveBought` | `Caravan_TraderTracker.GiveSoldThingToPlayer` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | — | Same as above |
| `Patch_SDS_CaravanRecacheReset` | `Dialog_FormCaravan.CalculateAndRecacheTransferables` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.8 | — | Clears leftover display things before recalculating the manifest |
| `Patch_SDS_CaravanAddVirtual` | `Dialog_FormCaravan.AddItemsToTransferables` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.8 | — | Caravan formation manifest injection |
| `Patch_SDS_CaravanValidate` | `Dialog_FormCaravan.TryFormAndSendCaravan` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W25 | Quantity validation before departure |
| `Patch_SDS_CaravanBlockInstant` | `Dialog_FormCaravan.DebugTryFormCaravanInstantly` | Manifests/Settlement | Rule | SDS.Vanilla/Interfaces §4.8 | — | Debug instant caravan formation does not support display things |
| `Patch_SDS_CaravanPostCloseCleanup` | `Dialog_FormCaravan.PostClose` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.8 | — | Cleans up display things on close |
| `Patch_SDS_CaravanMaterialize` | `LordMaker.MakeNewLord` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W25 | At caravan formation, swaps display things for real things |
| `Patch_SDS_ShuttleRequiredItemsFromGrid` | `TransporterUtility.AllSendableItems` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.8 | — | Mission shuttle required items append display things (virtual + preserved grouped buckets) |
| `Patch_SDS_TransporterAddVirtual` | `Dialog_LoadTransporters.AddItemsToTransferables` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.8 | — | Transport pod manifest injection |
| `Patch_SDS_TransporterLoadHasJob` | `LoadTransportersJobUtility.HasJobOnTransporter` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W25 | Passes when the map has no physical items but the ledger can supply |
| `Patch_SDS_TransporterLoadJob` | `LoadTransportersJobUtility.JobOnTransporter` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W25 | Re-routes to an endpoint take Job |
| `Patch_SDS_PortalAddVirtual` | `Dialog_EnterPortal.AddItemsToTransferables` | Manifests/Settlement | Projection | SDS.Vanilla/Interfaces §4.8 | — | Map portal manifest injection |
| `Patch_SDS_PortalLoadHasJob` | `EnterPortalUtility.HasJobOnPortal` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W25 | Same as above (pass check) |
| `Patch_SDS_PortalLoadJob` | `EnterPortalUtility.JobOnPortal` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W25 | Same as above (re-routes the take Job) |
| `Patch_SDS_TransporterDepositIdentity` | `CompTransporter.SubtractFromToLoadList` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W40 | Before entering the pod, normalizes true bodies to the loading row they belong to (transport pod / drop pod / cargo transport / shuttle) |
| `Patch_SDS_PortalDepositIdentity` | `MapPortal.SubtractFromToLoadList` | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | W40 | Same as above (map portal) |

## 6. Preserved-only bridge (SDS_PreservedUsage.cs)

> The "preserved adapter" of the target architecture: reserves by true-body reference; only calls the preserved mode of `SDS_WithdrawExecutor`; never reads `preservedOwner` directly.

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_XenogermImplantAvailable` | `Recipe_ImplantXenogerm.AvailableOnNow` | Job assignment | Rule | SDS.Vanilla/Rules §10 | — | Preserved xenogerms make the recipe available |
| `Patch_SDS_SelectXenogermList` | `Dialog_SelectXenogerm` constructor | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | — | The selection window appends preserved xenogerms |
| `Patch_SDS_XenogermMaterialize` | `Xenogerm.SetTargetPawn` | Consumption | Rule | SDS.Vanilla/Rules §10 | W2 | Materializes the exact true body when a target is chosen |
| `Patch_SDS_GeneDialogBegin` | `Dialog_CreateXenogerm` constructor | Manifests/Settlement | Rule | SDS.Vanilla/Rules §10 | — | The xenogerm creation window injects preserved genepacks into the selectable list |
| `Patch_SDS_GeneAssemblerGenepacks` | `Building_GeneAssembler.GetGenepacks` | Job assignment | Rule | SDS.Vanilla/Rules §10 | — | The gene assembler appends preserved genepacks |
| `Patch_SDS_GrowthVatEmbryoGizmo` | `Building_GrowthVat.GetGizmos` | Boundary | Rule | SDS.Vanilla/Rules §10 | — | The growth vat gains an "implant embryo from stock" button |
| `Patch_SDS_GrowthVatSelectEmbryo` | `Building_GrowthVat.SelectEmbryo` | Consumption | Rule | SDS.Vanilla/Rules §10 | W2 | Materializes the true body on selection |
| `Patch_SDS_CasketAcceptPreservedCorpse` | `Building_Casket.TryAcceptThing` | Consumption | Rule | SDS.Vanilla/Rules §10 | W2 | Preserved corpses are handed directly to the vanilla container |
| `Patch_SDS_PlaceBlueprintForInstall` | `GenConstruct.PlaceBlueprintForInstall` | Consumption | Rule | SDS.Vanilla/Rules §10 | W2 | Preserved minified things are materialized before building the install blueprint; after materialization, `SDS_InstallBlueprintGuard` cancels the registered old blueprint for the same innerThing (same semantics as `Designator_Install`, preventing duplicate registration) |

## 6.5. Preserved consumption bridge (Plan A, SDS_PreservedOffer.cs)

> Not a search adapter: when a vanilla decision function returns "none", 1 preserved true body is materialized at the access point and the result handed back to the vanilla Job.
> Rules: see the S series in RuleIndex.en.md; boundaries: see [SDS.Vanilla/Interfaces.en.md](SDS.Vanilla/Interfaces.en.md) 4.6; debiting / rollback reuse `SDS_WithdrawExecutor` and `SDS_ReturnUtility`.
> 2026-11-16: the five hook points remain in `Preserve/SDS_PreservedOffer.cs`; the offer table, destinations, timeout return-to-stock, and the virtual leg moved to the shared layer `Foundation/SDS_OfferBridge.cs` (patch-point count unchanged).

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_PreservedMedicineOffer` | `HealthAIUtility.FindBestMedicine` | Consumption | Rule | SDS.Vanilla/Rules §10 | — | When no medicine is found, materializes 1 preserved medicine and returns it (respects `medCare` / `onlyUseInventory`) |
| `Patch_SDS_PreservedFoodOffer` | `FoodUtility.TryFindBestFoodSourceFor` | Consumption | Rule | SDS.Vanilla/Rules §10 | — | When no food is found, materializes 1 preserved food and writes back `foodSource` / `foodDef` |
| `Patch_SDS_PreservedWeaponOffer` | `JobGiver_PickUpOpportunisticWeapon.TryGiveJob` | Consumption | Rule | SDS.Vanilla/Rules §10 | — | When the pawn has no weapon, materializes 1 and returns an `Equip` Job; **effective only when vanilla calls this giver under a DutyDef (raids / prison breaks / hunts, etc.)** — vanilla does not call it for idle colonists |
| `Patch_SDS_PreservedApparelOffer` | `JobGiver_OptimizeApparel.TryGiveJob` | Consumption | Rule | SDS.Vanilla/Rules §10 | — | Materializes 1 piece and zeroes `nextApparelOptimizeTick`, letting vanilla re-score on its next think |
| `Patch_SDS_PreservedOfferInStorage` | `StoreUtility.IsInAnyStorage` | Seeing/Search | Rule | SDS.Vanilla/Rules §10 | — | Makes the offer appear "in storage" to apparel scans (limited to physical items provided by this subsystem) |

## 7. Deposit direction and container entries (SDS_Implants.cs / SDS_Transmission.cs)

> `Patch_SDS_OwnerTryAdd` / `TryAddOrTransfer` are also the Container-mode entries of the "take-primitive adapter";
> in the target architecture they only call `SDS_WithdrawExecutor` and no longer debit on their own.

> The three Toil digitize patches (`StartCarryDigitize` / `HaulToCellDigitize` / `HaulToContainerDigitize`) have been removed.
> Digitization can only happen inside this mod's deposit Jobs whose destination is an endpoint; the chip's "store what is held" is implemented by resolving to its own port at job-assignment time.

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_FloatMenuDeposit` | `FloatMenuMakerMap.GetOptions` | Job assignment | Contract | SDS.Vanilla/Interfaces §4.7 | — | Right-click "Store into Hyperdimensional Space" entry |
| `Patch_SDS_PawnDepositGizmo` | `Pawn.GetGizmos` | Job assignment | Contract | SDS.Vanilla/Interfaces §4.7 | — | Pawn gizmo "Store into inventory" entry |
| `Patch_SDS_OwnerTryAdd` | `ThingOwner<Thing>.TryAdd(Thing,bool)` | Consumption | Contract | SDS.Vanilla/Interfaces §4.9 | — | Swaps in the true body before a container accepts a mirror; refusal rolls back |
| `Patch_SDS_OwnerTryAddOrTransfer` | `ThingOwner.TryAddOrTransfer(Thing,bool)` | Consumption | Contract | SDS.Vanilla/Interfaces §4.9 | — | Same as above (transfer entry) |

## 8. Endpoints / toggles / events (SDS_Buildings.cs)

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_EndpointChipAdded` | `Pawn_HealthTracker.AddHediff` | Boundary | Rule | SDS.Vanilla/Interfaces §4.1 | — | Marks the endpoint registry dirty after chips are added or removed |
| `Patch_SDS_EndpointPawnSpawned` | `Pawn.SpawnSetup` | Boundary | Rule | SDS.Vanilla/Interfaces §4.1 | — | Refreshes endpoints and the map set when player / chip pawns enter a map |
| `Patch_SDS_PlayerPresencePawnDespawned` | `Pawn.DeSpawn` | Boundary | Rule | SDS.Vanilla/Interfaces §4.1 | — | Refreshes the map set when player pawns leave a map |
| `Patch_SDS_PlayerPresenceFactionChanged` | `Pawn.SetFaction` | Boundary | Rule | SDS.Vanilla/Interfaces §4.1 | — | Refreshes the map set on faction change |

## 9. UI (SDS_UI.cs / SDS_Hotkeys.cs)

| Patch class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `Patch_SDS_PlaySettingsButton` | `PlaySettings.DoPlaySettingsGlobalControls` | Boundary | Rule | SDS.Vanilla/Rules §10 | — | The "Hyperdimensional" entry button at the bottom right (toggles the right-side sidebar) |
| `Patch_SDS_Hotkeys` | `WindowStack.HandleEventsHighPriority` | Boundary | Rule | SDS.Vanilla/Rules §10 | — | Input routing: settings-page key capture, C toggles the sidebar, Q withdraws when rows are selected, middle-click confirms in the withdraw dialog. Yielding is uniformly decided by `SDS_Hotkeys.MustYieldHotkey`: build mode / a same-key gizmo on screen (vanilla reuses `Designator_Cancel` for canceling growing zones / canceling construction / clearing prioritized work) / `Targeter` target selection / focused text input / input-absorbing windows / search boxes — if any condition hits, the event is not consumed |

## 10. Dynamic patches (no `[HarmonyPatch]` attributes)

> All third-party mod adapters live in `Source/SDS_ModCompat*.cs` (split into files by responsibility on 2026-10-27, still within one directory): `SDS_ModCompat.cs` handles only orchestration and dispatch; `SDS_CompatServices.cs` provides shared infrastructure and read-only services; `SDS_CompatReports.cs` does hook-point A/B reporting; each third party gets one `SDS_ModCompat_<name>.cs`; the bill missing-ingredient slot matching query moved to `SDS_BillIngredientQuery.cs`. The Glitterworld Destroyer adapter was removed on 2026-11-16 (the missing-count case is now handled uniformly by W58).
> The table below is indexed by "containing class" and no longer corresponds to standalone files; it is not counted in the 112 attribute-patch tally.

| Containing class | Node | Layer | Reason | Authority | Rules | Function |
|---|---|---|---|---|---|---|
| `SDS_ModCompat_PUAH` | `WorkGiver_HaulToInventory.HasJobOnThing / JobOnThing` | Job assignment | Compatibility | PublicAPI §4 | W17 | Candidate gate; when the endpoint wins, the entire order is replaced with this mod's multi-item Job |
| `SDS_ModCompat_PUAH` | `PawnUnloadChecker.CheckIfPawnShouldUnloadInventory` (Prefix) | Job assignment | Compatibility | PublicAPI §4 | W17 | Unload job assignment: if it can deposit in place → enqueues this mod's deposit Job and skips PUAH's enqueueing; otherwise handed back to PUAH unchanged (timing / self-healing logic untouched). The take driver's internal enqueue pass has no dispatch API and is not taken over per Plan B (Jobs constructed by others are not modified in `StartJob`) |
| `SDS_ModCompat_CE` | `WorkGiver_ReloadTurret` / `ReloadAutoLoader` / `JobGiver_*Reload*` / `CompAmmoUser.*` etc. | Consumption | Compatibility | PublicAPI §4 | W17 | CE ammo / reload pipeline filters out mirrors; the take portion is wired into the unified executor and reservation pool. Hook points go through `SDS_CompatTools.TryPatch` like the other adapters, with priority uniformly `Low` (since 2026-10-27; previously Harmony's default `Normal`, see "Priority conventions" at the end of this section) |
| `SDS_ModCompat_UFRecipes` | `AutoSequenceUtil.TryPullIngredientsFromCandidates` (Postfix) | Job assignment | Compatibility | PublicAPI §4 | W17 | UF Light Industries Furniture's fully automatic printer: after its own ingredient-pull fails, **only the preserved channel** (corpses / minified things / gene carriers) is topped up into the printer container; virtual categories are left to its own vanilla ingredient pulling (mirrors in `SlotGroup` + `SplitOff`), and the adapter adds no second path |
| `SDS_ModCompat_ManipulatorBeam` | `BeamFeatureSettings.IsSpecialContainerTypeEnabled` (Postfix) | Job assignment | Compatibility | PublicAPI §4 | W17 | Manipulator Beam Emitter's special containers are a closed type whitelist: this mod's endpoints are registered as a container type it recognizes (only relaxes the type check; does not touch the toggle / exclusive occupancy / enqueueing) |
| `SDS_ModCompat_ManipulatorBeam` | `MB_BeamScanCacheMapComponent.get_SpecialContainers` (Postfix) | Job assignment | Compatibility | PublicAPI §4 | W17 | Idempotently appends this map's endpoints to its container scan table (it rebuilds every 30 ticks; appended entries are automatically re-added after being invalidated by a rebuild); otherwise the endpoints are completely invisible to it |
| `SDS_ModCompat_ManipulatorBeam` | `BeamSpecialTransferUtility.TryFindSpecialContainerTransfer` (Prefix) | Job assignment | Compatibility | PublicAPI §4 | W17 | Describes "ground items → endpoint" using its `BeamTransfer` shape; the candidate set uses vanilla `ListerHaulables`, and acceptance is decided solely by the endpoint's `IHaulDestination.Accepts` (this mod's deposit hard gate) — it no longer compares global "find" results against destinations: the tractor beam already has a fixed destination, and comparing by global nearest-match would make every building endpoint judge "no job" simultaneously when a chip pawn is nearby. **Delivery is not taken over**: its own generic branch (`TryGetInnerInteractableThingOwner` + `ThingOwner.TryAdd`) is the vanilla container contract, and the endpoint's `InputBuffer` catches it directly. Since 1.2.0 the method takes 5 parameters including `out BeamTransfer` (this assembly does not reference their DLL; written back via `__args[4]`); when the Prefix skips the original method it must write both `__result` and transfer; a signature mismatch is reported as a missing hook point |
| `SDS_ModCompat_MiliraExpanded` | `CompVirtualProductionDevice.CompTick` / `TryProcessAutomatedRecipeCycle` / `TryProcessManualRecomposeBill` / `TryProcessRecomposeOrder` / `TryProcessDecomposeOrder` / `TryProcessLifeEssenceOrder` (auto-refill) | Job assignment | Compatibility | PublicAPI §4 | W17 | Missing ingredients for automated production / linked workbench / recompose / decompose and life-essence orders: Milira Expanded - Future Technology's network's own stock takes priority, and shortfalls are drawn from SDS's **virtual + preserved channels** and deposited into the network (standard matter topped up via `MEXY_StandardMatterUnit`; undelivered remainders returned to SDS by source); when the order table cannot be read, the miss is recorded as a "hook point that should be active" — no silent degradation |
| `SDS_ModCompat_CeleTechShuttle` | `CargoTransferFilter.BuildCargoTransferables` (Postfix) | Manifests/Settlement | Compatibility | PublicAPI §4 | W50 | CeleTech Shuttle Extension's self-built loading manifest gets Hyperdimensional display things injected; admission / refrigeration / loading Jobs / entering the pod remain with the other mod and vanilla — the adapter does read-only projection only |
| `SDS_ModCompat_EquivalentExchange` | `Dialog_LoadTransporters.TryAccept` (`TryPatch`: Prefix `Priority.High` + Finalizer `Low`) | Consumption | Compatibility | PublicAPI §4 | W17 | ProjectE's "instant transmutation of the loading manifest" is not the vanilla loading pipeline: before transmutation, the quantity the display things represent is exchanged for true bodies through the unified take transaction, then handed to it for destruction; if withdrawal fails / no real transmutation happened → return to the ledger; preserved display things do not participate |
| `SDS_ModCompat_EquivalentExchange` | `TransmutationUtility.GetTransmutableThingsInArea` (Postfix) | Seeing/Search | Compatibility | PublicAPI §4 | W17 | Area transmutation scans cell-by-cell `GetThingList`, treating mirrors in vault cells as "ground items" → removed from candidates (container contents are not on cells to begin with) |
| `SDS_ModCompat_EquivalentExchange` | `Dialog_LoadTransporters.AddItemsToTransferables` injection gate (consumed by the core Postfix via `SDS_CompatGates`) | Manifests/Settlement | Compatibility | PublicAPI §4 | W17 | When ProjectE is installed but the instant-transmutation hook point is not active, its dialog injects no display things (fail-closed, preventing display things from being destroyed while the ledger stays untouched) |
| `SDS_ModCompat_Versatran` | `Window_VersatranCargo.ExecuteInstantLoad` (Prefix / Postfix) | Consumption | Compatibility | PublicAPI §4 | W46, W54 | VC vehicle framework vehicle cargo-hold loading: first strips mirror / preserved rows out of the vanilla loop — virtual goes through `TryWithdraw`, preserved through `TryWithdrawPreservedExact`; true bodies are handed to the vehicle's own boarding method; real map items still follow the vehicle's original logic |
| `SDS_ModCompat_Versatran` | `Window_VersatranCargo.RefreshItems` (Postfix) | Manifests/Settlement | Compatibility | PublicAPI §4 | W46 | Read-only projection: mirror rows get `MaxCount = ReadStock.available`; preserved true-body rows are appended (`AppendPreservedThings`); no second set of quantities is introduced |
| `SDS_ModCompat_Versatran` | `CompVersatranTransporter.CanThingBeStoredInCargo` (Prefix) | Boundary | Compatibility | PublicAPI §4 | W54 | Gatekeeper: mirrors / still-alive preserved true bodies must not enter vehicle containers; the adapter's own take products pass normally |
| `SDS_ModCompat_MiliraGift` | `MiliraTheOneEntwinedWithYou.Gifts.JobDriver_IdealReceiveGift.TryMakePreToilReservations(bool)` (Prefix) | Consumption | Compatibility | PublicAPI §4 | W54, W59 | Milira gift-giving: when targetA is a mirror, first materializes 1 item at the nearest reachable endpoint access cell and copies `CompIdealGift` state, then rewrites targetA; releases the vanilla reservation regardless of takeover; does not change W54 / Core |

> All dynamic patches probe via `AccessTools.TypeByName` first; silently skipped when the third-party mod is absent.
>
> **Every hook point must be registered A/B** (`SDS_CompatPatchReport`, same criteria as the core `ApplyHarmonyPatches`):
> on success → one Message `hooks active: applied/expected hook points`; if any hook point cannot find its type / method or fails to apply →
> a Warning listing each "hook point name (reason)" plus the list of active hook points. If a third party is detected but its structure changed, silently skipped hook points are never acceptable.
>
> **Third parties do not need individual hook points**: as long as their WorkGiver ends up calling vanilla `HaulAIUtility.HaulToStorageJob`
> (e.g. KAU urgent hauling's `JobOnThingDelegate`), they are already covered at the function level by the attribute patch `Patch_SDS_HaulToStorageJob`.
> Only third parties with their own Job factory / their own JobDef (Pick Up And Haul's `HaulToInventory`, Combat Extended's reload pipeline) need dynamic hook points.
>
> Combat Extended's take portion belongs to the "Combat Extended adapter" and must be wired into the unified executor and reservation pool; Pick Up And Haul only affects deposit job assignment and takes no part in take transactions.
>
> UF Light Industries Furniture: the fully automatic printer's **virtual categories** are covered by its own vanilla ingredient pulling (mirrors in `SlotGroup` + `SplitOff` + `ThingOwner`);
> the adapter only supplements the **preserved channel** it cannot see (corpses / minified things / genes); the hook point fires after its ingredient pull fails, and the query function performs no side effects.

**Priority conventions (2026-10-27)**

- Applying third-party hook points is allowed only via `SDS_CompatTools.TryPatch`: resolution failure → `Missing`; application exception → `Missing` + `ErrorOnce` (`third-party compatibility hook point application failed (skipped): hook point name`); success → `Applied`. Adapters must no longer hand-write `harmony.Patch` scaffolding.
- Default priority `Priority.Low`: the Postfix reads `__result` last (falling back only when vanilla / other mods have not taken over), and the Prefix lets the other mod and the core decide first. Finalizers use the separate `finalizerPriority` parameter; rollback / cleanup Finalizers always stay `Low` and are not raised along with the Prefix.
- **The sole exception**: ProjectE's `Dialog_LoadTransporters.TryAccept` Prefix must be `Priority.High` — its own Prefix takes over and skips the vanilla method, so Prefixes with lower priority would not run after it returns `false`; it also guarantees other mods' Prefixes never see fake display things.
- Combat Extended's 15 hook points were unified from Harmony's default `Normal` to `Low` (**L2 ordering change**). In-game verification checklist: both turret-reload stages (`HasJobOnThing` pass-through + `JobOnThing` restocking), auto-loader reload, empty-magazine warning, running dry mid-attack, defensive-duty ammo resupply, ammo pickup, `FindBestAmmo` mirror filtering, and coexistence of `Pawn_JobTracker.JobTrackerTick` with other mods hooking the same method.

---

## 11. Registry-based baseline (TargetMethod / GuardKind, 19 guards + 10 projection owners)

> This table is the **sole diff baseline** for step 3 (registry conversion): it defines no rules and only declares "what is guarded / how it is guarded / which entry is guarded / whether an escape hatch exists".
> **Classification**: this section is descriptive and adds no rule IDs; E6 (mandatory guard registration) awaits a separate L2 decision.
> **Two registries**: `guard` → `MirrorGuardRegistry`; `projection owner` → `ProjectionOwnerBridge`. They are not merged into a single N.
> The primary key is `TargetKey`; the `Patch class` column holds registry entry Ids (`MirrorGuardRegistry` / `ProjectionOwnerBridge`), not C# classes. Format: `Type.Method` / `Type.Method[arg,...]` / `Type.Property#getter`.
> Target = `mirror` / `vault` / `both`.
> Actions are isolated per group: guards = `false` / `filter` / `postfix` / `recount`; projection owners = `translate`; special cases = `allow` / `suppress` (see §12).
> For Authority, the main tables remain authoritative; this table only repeats the "Rules" column for easier diffing. `—` means no escape hatch or no dedicated rule.
> Acceptance: `CheckPatchInventory.ps1` must report `attribute classes 112 + registry 29 = patch points 141 / baseline 29 rows / 28 distinct TargetKeys / special cases 3 rows`; the deduplicated count of full TargetMethod text keys measured in practice is **138** (emitted by `-Report`; not reflection-level).

| Patch class | TargetKey | Group | Target | Action | Escape hatch / fast path | Rules |
|---|---|---|---|---|---|---|
| `Patch_SDS_MirrorNoDamage` | `Thing.TakeDamage` | Guard | both | false | — | — |
| `Patch_SDS_VaultPreApplyDamage` | `Thing.PreApplyDamage` | Guard | vault | false | — | — |
| `Patch_SDS_MirrorNoFire` | `FireUtility.TryAttachFire` | Guard | both | false | — | — |
| `Patch_SDS_MirrorNoDeSpawn` | `Thing.DeSpawn` | Guard | mirror | false | `AllowDeSpawn` / `ProgramState != Playing` | W18 |
| `Patch_SDS_MirrorNoDeSpawnWithComps` | `ThingWithComps.DeSpawn` | Guard | mirror | false | `AllowDeSpawn` / `ProgramState != Playing` | W18 |
| `Patch_SDS_MirrorNoRealtimeDraw` | `Thing.DynamicDrawPhaseAt` | Guard | mirror | false | `SDS_MirrorRegistry.Any` fast path | — |
| `Patch_SDS_MirrorNoMeshPrint` | `Thing.Print` | Guard | mirror | false | `Any` fast path | — |
| `Patch_SDS_MirrorNoMeshPrintWithComps` | `ThingWithComps.Print` | Guard | mirror | false | `Any` fast path | — |
| `Patch_SDS_MirrorNoGuiOverlay` | `Thing.DrawGUIOverlay` | Guard | mirror | false | `Any` fast path | — |
| `Patch_SDS_MirrorNoGuiOverlayWithComps` | `ThingWithComps.DrawGUIOverlay` | Guard | mirror | false | `Any` fast path | — |
| `Patch_SDS_MirrorNotSelectable` | `ThingSelectionUtility.SelectableByMapClick` | Guard | mirror | postfix | `Any` fast path / `__result` already false | — |
| `Patch_SDS_MirrorNotSelectableDirect` | `Selector.Select` | Guard | mirror | false | `Any` fast path | — |
| `Patch_SDS_RoomIgnoresHiddenGrid` | `Room.ContainedAndAdjacentThings#getter` | Guard | both | filter | — | — |
| `Patch_SDS_RoomContainsThingIgnoresHiddenGrid` | `Room.ContainsThing` | Guard | both | postfix | `Any` fast path / flips to false only when all hit items are hidden-grid | — |
| `Patch_SDS_RoomContainedThingsIgnoresHiddenGrid` | `Room.ContainedThings[ThingDef]` | Guard | both | filter | `Any` fast path | — |
| `Patch_SDS_RoomThingCountIgnoresHiddenGrid` | `Room.ThingCount` | Guard | both | recount | `Any` fast path | — |
| `Patch_SDS_ObserverIgnoresHiddenGrid` | `PawnObserver.PossibleToObserve[Thing]` | Guard | both | false | — | — |
| `Patch_SDS_TantrumIgnoresHiddenGrid` | `TantrumMentalStateUtility.CanSmash` | Guard | both | false | — | — |
| `Patch_SDS_FireSpreeIgnoresHiddenGrid` | `JobGiver_FireStartingSpree.TryFindRandomIgniteTarget` | Guard | both | postfix | — | — |
| `Patch_SDS_ProjectionTake` | `ThingOwner.Take[Thing,int]` | Projection owner | mirror | translate | — | W2, W5 |
| `Patch_SDS_ProjectionTryDropCounted` | `ThingOwner.TryDrop[Thing,IntVec3,Map,ThingPlaceMode,int,ref Thing,Action,Predicate]` | Projection owner | mirror | translate | — | W5 |
| `Patch_SDS_ProjectionTryDropAll` | `ThingOwner.TryDrop[Thing,IntVec3,Map,ThingPlaceMode,ref Thing,Action,Predicate,bool]` | Projection owner | mirror | translate | — | W5 |
| `Patch_SDS_ProjectionTryTransfer` | `ThingOwner.TryTransferToContainer[Thing,ThingOwner,int,ref Thing,bool]` | Projection owner | mirror | translate | — | W5 |
| `Patch_SDS_EndpointTransferReturn` | `ThingOwner.TryTransferToContainer[Thing,ThingOwner,int,ref Thing,bool]` | Projection owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionClear` | `ThingOwner.Clear` | Projection owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionClearAndDestroy` | `ThingOwner.ClearAndDestroyContents` | Projection owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionClearAndDestroyOrPassToWorld` | `ThingOwner.ClearAndDestroyContentsOrPassToWorld` | Projection owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionRemoveAll` | `ThingOwner.RemoveAll[Predicate]` | Projection owner | mirror | translate | — | — |
| `Patch_SDS_ProjectionRemoveAllGeneric` | `ThingOwner<Thing>.RemoveAll[Predicate]` | Projection owner | mirror | translate | — | — |

## 12. Special-case patches (not yet registry-based, 3 rows)

> These patches have independent semantics and **do not enter** `MirrorGuardRegistry` / `ProjectionOwnerBridge`; `allow` / `suppress` are special-case action enums and are not migrated when registry conversion happens.
> Verification: `CheckPatchInventory.ps1` must report `special cases 3 rows`.

| Patch class | TargetKey | Action | Reason | Rules |
|---|---|---|---|---|
| `Patch_SDS_MapDeinitAllowMirrorDespawn` | `MapDeiniter.Deinit` | allow | The map-unload window needs a temporary DeSpawn allowance; a stateful allowance, not a simple predicate | W18 |
| `Patch_SDS_MirrorNotHaulable` | `ListerHaulables.ShouldBeHaulable` | suppress | Enumeration suppression, not returning an empty value; same kind as `NotMergeable` | — |
| `Patch_SDS_MirrorNotMergeable` | `ListerMergeables.ShouldBeMergeable` | suppress | Enumeration suppression, not returning an empty value; merge semantics in W20 | W20 |

### Step 3 grouping (planning, not the baseline)

| Group | Handling | Scope |
|---|---|---|
| Guard | `MirrorGuardRegistry` | §11, 19 rows |
| Projection owner | `ProjectionOwnerBridge` | §11, 10 rows |
| **Consumption-primitive group** (not named "paired-overload group") | Separate list in step 3 | Absorb / Destroy / SplitOff / TryStartCarry / TryAdd; not in this baseline |
| Special cases | Not registry-based | §12, 3 rows |

> Paired overloads (Thing / ThingWithComps) are a **structural property**, not a parallel group: 3 pairs are already inside the guards, and 2 pairs (Absorb / Destroy) belong to the consumption-primitive group.

---

## Statistics

> This table reflects measurements taken from source code; after any patch change, `CheckPatchInventory.ps1` must be run to reconcile — any number mismatch is a failure.

| File | Attribute patch class count |
|---|---|
| `Patch_Mirrors.cs` | 14 |
| `SDS_AccessPoints.cs` | 0 |
| `SDS_Search.cs` | 6 |
| `SDS_ReturnAndReclaim.cs` | 0 |
| `SDS_JobTargets.cs` | 4 |
| `SDS_MirrorBehavior.cs` | 7 |
| `SDS_StartCarryDepositFold.cs` | 1 |
| `SDS_PreservedBill.cs` | 3 |
| `SDS_VirtualBillFallback.cs` | 1 |
| `Patch_CaravanTransport.cs` | 6 |
| `Patch_TransportLoad.cs` | 9 |
| `Patch_Trade.cs` | 10 |
| `SDS_PreservedUsage.cs` | 9 |
| `SDS_Selection.cs` | 5 |
| `Patch_VanillaSeams.cs` | 11 |
| `SDS_Buildings.cs` | 4 |
| `SDS_Implants.cs` | 2 |
| `SDS_Transmission.cs` | 2 |
| `SDS_UI.cs` | 5 |
| `SDS_Hotkeys.cs` | 1 |
| `SDS_PreservedOffer.cs` | 5 |
| `SDS_HaulCountIntent.cs` | 1 |
| `SDS_MirrorConsumeSeams.cs` | 4 |
| `SDS_MirrorReservations.cs` | 2 |
| **Total (attribute patches)** | **112** |
| Registry entries (19 guards + 10 projection owners, see §11) | 29 |
| **Total (patch points)** | **141** |
| Dynamic patches (Pick Up And Haul / Combat Extended / UF Light Industries Furniture / Manipulator Beam Emitter / Milira Expanded - Future Technology / CeleTech Shuttle Extension / ProjectE — all in `SDS_ModCompat*.cs`: files split by responsibility within one directory, one per third party) | 7 groups (multi-target) |

> The public API (`SDS_API`) is a code facade, not a Harmony patch, and is not counted in the table above; its contract is in [PublicAPI.en.md](PublicAPI.en.md). Phases 1–4 (the See/Read/Find/Store/Take facades + adapter migration) and the phase-5 thin adapters for Manipulator Beam Emitter / CeleTech Shuttle Extension are implemented; phase 6 has not started.

> Source measurements are authoritative: attribute classes **112** + registry entries **29** = patch points **141** (reconciled by the checker on 2026-11-16; the attribute count covers both `[HarmonyPatch(...)]` and `[HarmonyPatch]`+`TargetMethod()` forms).
> Source measurement: TargetMethod text keys deduplicated to **138** (see the §11 acceptance criteria; the difference comes from multiple classes hooking the same method).
> `Patch_SDS_JobGiverHaul` **is still present**: it handles supplementary dispatch when vanilla `JobGiver_Haul` returns null — a different division of labor from `HaulToStorageJob`.
> `Patch_SDS_JobReportHaulToContainer` is the 5th UI-side patch (`HaulToContainer.GetReport` override that does not call the base class).
> `JobDriver_SDS_WithdrawForJob` is the **target-architecture name**, not yet implemented; the current drivers are `JobDriver_SDS_WithdrawFromGrid` / `JobDriver_SDS_WithdrawPreservedForBill`.
> The take trunk's new classes (`SDS_WithdrawResolver` / `SDS_WithdrawTransaction` / `SDS_WithdrawExecutor`) are not Harmony patches and are not counted in the 141.

All attribute patches compile; `SDS_Mod.ApplyHarmonyPatches` wraps each class in try/catch, so a single failure does not affect the other patches.
