# Module Interfaces

> Positioning: the **registry of cross-module shared contracts**. Each module's outward signatures take `SDS.*/*_API.cs` as the sole authority; each module's `Interfaces.en.md` records only consumption semantics. This document keeps only three kinds of cross-module content: the shared-contract index, the adapter bridge registry (read by machine checks), and the save lock list.
> Binding constraints are in ModuleRules.en.md; the module division of labor is in ModuleResponsibilities.en.md.

## 1. SDS.Contracts

Cross-module shared types (enums, DTOs, host interfaces, DefOf, registries, settings) are all defined in Contracts — the only language every module is allowed to reference in common.

Full signatures → [SDS.Contracts/Interfaces.en.md](SDS.Contracts/Interfaces.en.md) (formerly 1.1 enums and DTOs, 1.2 ports and host interfaces, 1.3 DefOf / registries / settings). Consumers inline the subset they actually reference, per module: see each module's `Interfaces.en.md` consumed-contract sections (Section B / consumed surface / callers).

The 11 slots of `SDS_Host` and "who provides / who consumes":

| Slot | Provider | Consumer |
|---|---|---|
| `Ports` | Vanilla | Rules (endpoint resolution; moved out of Core in P2) |
| `DepositFilter` | Vanilla | Rules (`SDS_StockRules.CanDeposit`; moved out of Core in P4) |
| `Priority` | Vanilla | Vanilla / Api (writes `SDS_EndpointQuery.priority` after reading settings; Core does not read the slot) |
| `Delivery` | Vanilla | Rules (return / landing / into-hands; moved in with `SDS_ReturnUtility` in P3) |
| `MirrorQuery` | Vanilla | Api, Vanilla (its own exposure accounting) |
| `MirrorPhysical` | Vanilla | Vanilla / Api (passed in via `TryTakeMirror(..., physical, ...)`; Core does not read the slot) |
| `MedicalCare` | Vanilla | Vanilla / Api (the S17 medical gate via `SDS_MedicalGate`; Core does not read the slot) |
| `ReclaimWorld` | Vanilla | Vanilla (reclaim ledger / Job·Lord facts; Core does not consume it) |
| `Display` | Vanilla | ThirdModCompat (via the Api bridge) |
| `DepositJobs` | Vanilla | ThirdModCompat (via the Api bridge) |
| `TransportLoad` | Vanilla | ThirdModCompat (via the Api bridge) |

> P2–P4 rules moved into `SDS.Rules`: `Ports` (P2), `Delivery` (P3), and `DepositFilter` (P4) are now consumed by Rules; Core does not read host slots.

## 2. SDS.Diag

The `SDS_Diag` method table, `ISDSResettable` / `SDS_StaticState`, report DTOs, and caller restrictions on `Fail` → [SDS.Diag/Interfaces.en.md](SDS.Diag/Interfaces.en.md) and [SDS.Diag/Rules.en.md](SDS.Diag/Rules.en.md) (formerly 2.1 / 2.2).

## 3. SDS.Core

The ledger, data primitives, and Core saves → [SDS.Core/Interfaces.en.md](SDS.Core/Interfaces.en.md) (formerly 3.1 the ledger, 3.6 Core saves); endpoint resolution, bill supply (P2), the flow layer (P3), the adjudication layer (P4) → [SDS.Rules/Interfaces.en.md](SDS.Rules/Interfaces.en.md).

Rules module (P4, migration complete): `SDS.Rules` now carries all rules / flows plus `SDS_Availability` / `SDS_ImplantUtility`; Core keeps only the ledger and `SDS_StockData`. The `SDS_API` bridge signatures are unchanged; forwarding targets switched to Rules.

## 4. SDS.Vanilla

The save shell, host implementation constraints, the mirror physical contract, and Vanilla saves → [SDS.Vanilla/Interfaces.en.md](SDS.Vanilla/Interfaces.en.md) (formerly 4.1 the save shell, 4.2 host implementation constraints, 4.3 mirror physical operations, 4.4 Vanilla saves).

## 5. SDS.Api (Adapter Bridge Registry)

`SDS_API.Version = 4`. The five-axes signatures and forwarding targets are in [SDS.Api/Interfaces.en.md](SDS.Api/Interfaces.en.md).

Adapter bridges (public, outside the five-axes contract): there is no `InternalsVisibleTo` across assemblies, so utilities `SDS.ThirdModCompat` needs must hang off `SDS_API` as public methods; they add no ordering / capacity / debit / job-assignment rules. Bulk withdraw by def merely composes the same transaction / exact-true-body plan primitives key by key and returns in bulk. This table is the sole registry of the bridge surface; `CheckCompatSurface.ps1` reads the bridge method names from here.

| Category | Methods |
|---|---|
| Endpoint / availability | `HasAnyEndpoint(Map)` / `HasUsableBuildingEndpointWithin(Map, IntVec3, float)` / `IsDepositEndpoint(Thing)` / `IsGridUsable(Map)` / `IsManualDepositAvailable(Pawn)` / `IsPawnPortOperational(Pawn)` / `TryGetWithdrawCell(Pawn, out IntVec3, out SDS_ApiResultCode)` |
| Ledger read-only | `IsGridAvailable()` / `AppendPreservedThings(List<Thing>)` / `IsLivePreservedThing(Thing)` / `IsStockForbidden(SDS_StockKey)` / `TryGetStockKey(Thing, out SDS_StockKey)` / `GetAvailableCount(SDS_StockKey)` / `GetAvailableCountForDef(ThingDef)` / `GetPreservedAvailableCount(ThingDef)` / `TryFindWithdrawableKey(ThingDef, out SDS_StockKey, out int)` / `ClampTakeToAvailable(int, long)` / `IsForceWeaponApparelVirtual()` / `IsBillIngredientAllowed(...)` |
| Display / mirror | `IsMirror(Thing)` / `IsDisplayThing(Thing)` / `TryGetDisplayKey(Thing, out SDS_StockKey)` |
| Source / channel | `IsProducedPendingConsumption(Thing)` / `MarkProducedPendingConsumption(IList<Thing>)` / `IsDepositJob(Job)` / `RequiresPreservedStorage(ThingDef)` |
| Job assignment | `TryTakeDepositCandidate(Pawn, Thing, bool, out SDS_ApiResultCode)` / `TryMakeDepositJob(Pawn, Thing, bool, Job, bool, out Job, out SDS_ApiResultCode)` / `TryMakeDepositJobFromInventory(Pawn, IList<Thing>, out Job, out SDS_ApiResultCode)` |
| Bulk withdraw by def | `TryWithdrawVirtualByDef(...)` / `TryWithdrawPreservedByDef(...)` |
| Supply planning | `TryPlanBillSupplySlot(...)` / `TryPlanAllowedDefSupply(...)` / `NotifyBillSupplyDeposited(...)` |
| Return | `TryReturnPreserved(Thing, Map, IntVec3)` / `TryReturnVirtual(Thing, Map, IntVec3, bool landOnFailure = true)` |
| Loading | `InjectTransportLoadCandidates(ThingWithComps, List<TransferableOneWay>)` (W50) |

## 6. SDS.ThirdModCompat

Sdk utility class signatures and the 9 adapters' `Name` / `TargetPackageId` / `ISDSResettable` / `SDS_API` call lists → [SDS.ThirdModCompat/Interfaces.en.md](SDS.ThirdModCompat/Interfaces.en.md) (formerly 6.1 / 6.2).

## 7. SDS.Bootstrap

`SDS_Mod`'s public surface, the actual call sites of the 7 startup steps, and the 11 injected objects → [SDS.Bootstrap/Interfaces.en.md](SDS.Bootstrap/Interfaces.en.md) (formerly 7.1). Startup-order rules are in ModuleRules.en.md §9.

## 8. Save Lock List

### 8.1 Type full names that must be locked

```text
SDS_GameComponent_Grid
SDS_StockKey
SDS_StockEntry
SDS_Reservation
SDS_ReclaimSave
SDS_TransportLoadSave
SDS_SidebarUIState
SDS_EndpointBuffer
SDS_Settings
SDS_WithdrawDeliveryPolicyDef
SDS_MirrorJobTargetSave
```

Pending re-review: `SDS_ProjectionOwner` (no ExposeData; decide whether to remove after review).

### 8.2 Types referenced by Defs XML

```text
Building_SDS_Core
Building_SDS_Port
Building_SDS_CapacityArray
Building_SDS_Vault
SDS_CompUsable
SDS_CompProperties_InstallChip
SDS_CompInstallChip
SDS_Comp_SDS_DrawExtraOverlay
CompProperties_SDS_DrawExtraOverlay
SDS_Proxy_Thing
SDS_Proxy_ThingWithComps
SDS_Proxy_Apparel
JobDriver_SDS_WithdrawFromGrid
JobDriver_SDS_DepositToGrid
JobDriver_SDS_WithdrawPreservedForBill
```

### 8.3 Deep-serialized fields and write nodes

The sole authority for fields, `LookMode`, and node names is each module's `Interfaces.en.md`: Core in [SDS.Core/Interfaces.en.md](SDS.Core/Interfaces.en.md), Vanilla in [SDS.Vanilla/Interfaces.en.md](SDS.Vanilla/Interfaces.en.md). This document only locks down "which types must survive save/load" and does not repeat the field tables.
