# SDS.Vanilla (Vanilla Adaptation)

> Positioning: this directory is the sole maintenance entry for `SDS.Vanilla.dll` and is **self-contained**: interfaces, rules, and historical comparisons are inlined here; the only additional read is ModuleRules.en.md (one global page).
> Sources of truth: `超维存储/Source/SDS.Vanilla/`, `SDS.Tests/Vanilla/`; for historical decisions and pitfalls see [DesignDecisions.en.md](DesignDecisions.en.md). This module's nine iron rules have all been materialized as the checkable entries V1–V13 of [Rules.en.md](Rules.en.md) (one-to-one correspondence); this document does not restate them.

## 1. Responsibilities

| Category | Content | Source location |
|---|---|---|
| Buildings / ports | Core, Anchor, Stabilizer, the hidden vault; the `ISDS_StorageEndpoint` vanilla container contract | `SDS_Buildings.cs` |
| Endpoint handles / buffers | Handles shared by buildings and Pawns; the `SDS_EndpointBuffer` input buffer and instant digitization | `SDS_EndpointHandle.cs`, `SDS_EndpointBuffer.cs`, `SDS_EndpointUtility.cs` |
| Mirrors / projections / exposure / access points | Per-map per-key mirrors, projection owners, the wealth and resource-readout basis, access point distance, the non-player withdrawal gate (S18), priority outflow (W52) | `SDS_Mirrors.cs`, `SDS_Projection.cs`, `SDS_Exposure.cs`, `SDS_AccessPoints.cs`, `SDS_AccessRules.cs` |
| 112 attribute patches | Vanilla seams, search, reachability, trading, caravan formation / loading, the preserved bridge, UI, hotkeys | See the statistics section of PatchInventory.en.md |
| 29 registry entries | 19 mirror guards + 10 projection owners, installed dynamically by `SDS_PatchInstaller` | `SDS_MirrorGuardRegistry.cs`, `SDS_ProjectionOwnerBridge.cs` |
| JobDriver / Thing / Comp | Deposit / pickup / preserved ingredient-fetch drivers, proxy things, the chip comp, extra glow effects | `SDS_Destinations.cs`, `SDS_CaravanTransport.cs`, `SDS_PreservedBill.cs`, `SDS_Proxies.cs`, `SDS_Implants.cs`, `SDS_Comp_DrawExtraOverlay.cs` |
| UI / hotkeys | Sidebar, the output dialog, the deposit-filter ITab, the placer, play settings, the work report | `SDS_UI.cs`, `Window_SuperSpace.cs`, `Window_SDS_OutputDialog.cs`, `SDS_ITab_DepositFilter.cs`, `SDS_Hotkeys.cs`, `SDS_Use.cs` |
| Trading / caravan formation / loading | Display thing injection, deal validation, departure / pod-loading settlement, load-time restoration | `SDS_Trade.cs`, `SDS_CaravanTransport.cs`, `SDS_TransportVirtual.cs`, `Patch_TransportLoad.cs` |
| Save shell | Ledger attach, Vanilla state, legacy node migration; 250 tick structure fact reconciliation, the first-save operation guide letter, debounced apparel / wealth nudges after stock changes | `SDS_GameComponent_Grid.cs`, `SDS_VanillaState.cs` |
| Reclaim ledger | Reclaim entries (job targets / carried items / protected outputs), `sdsReclaim` node reads and writes, load-time audit, 250 tick inspection | `SDS_ReclaimRegistry.cs` (`Foundation/`), `SDS_GameComponent_Grid.cs` |
| Channel separation | Two-way reference bans between `Mirrors/` (virtual-only) and `Preserve/` (preserved-only); the shared directories are the only legitimate bridge; the virtual ingredient fallback and the dual-channel supply bridge live in the shared layer | `Mirrors/`, `Preserve/`, `SDS_VirtualBillFallback.cs` (`Jobs/`), `SDS_OfferBridge.cs` (`Foundation/`); checks in [Rules.en.md](Rules.en.md) §14 |

Not responsible for: inventing CRUD rules; computing capacity / shortfall / sorting; writing ledger internals; referencing `SDS.ThirdModCompat`; defining gameplay semantics beyond the save format; providing the external API (the five-axes (See/Read/Find/Take/Store) facade lives in `SDS.Api`); carrying third-party target adaptation (in `SDS.ThirdModCompat`); deciding the startup order (`SDS_Host.Install` may be called only by Bootstrap); judging whether a target mod exists.

## 2. Dependencies and outputs

| Item | Content |
|---|---|
| Allowed references | `SDS.Contracts`, `SDS.Diag`, `SDS.Core`, Harmony (ModuleRules.en.md §4) |
| Forbidden references | `SDS.ThirdModCompat`, `SDS.Api`, `SDS.Bootstrap`, Core's internal types |
| Output | `SDS.Vanilla.dll`, output to `超维存储/Assemblies` |
| Namespace | All `SuperDimensionalStorage` (no sub-namespaces added) |
| Harmony ID | `sizuru.sds.vanilla` |

## 3. Public surface

| Surface | Types | Constraints |
|---|---|---|
| Inter-module surface | `SDS_Vanilla` (`SDS.Vanilla_API.cs`, the E8 registration file) | The only entry exposed to other modules: `CreateHostSlots` / `InstallPatches` / `ApplyRuntimeHooks` / `RegisterAll` etc., called by Bootstrap per the startup steps |
| Engine surface | `Building_SDS_*`, `JobDriver_SDS_*`, `SDS_Proxy_*`, `SDS_Comp*`; save types `SDS_GameComponent_Grid`, `SDS_EndpointBuffer` | Instantiated by Defs XML names or saved via Scribe; forbidden from being referenced by other modules |
| Internal implementations | The 9 host implementations (`SDS_EndpointProvider` … `SDS_TransportLoadAdapter`), `SDS_PatchInstallerImpl`, `SDS_VanillaState` | Do not constitute an external surface; serve as a maintenance index only |

For signatures, fields, and consumed surfaces see [Interfaces.en.md](Interfaces.en.md) §1; the complete public list is governed by the `DumpPublicSurface.ps1 -Check` baseline (adding any new public type fails).

## 4. Document map

| Document | Purpose |
|---|---|
| [Interfaces.en.md](Interfaces.en.md) | Public signatures, the consumed Contracts / Host / Core surfaces, call relationships, the vanilla flow integration overview (§4.0) and vanilla support (§4.1–4.15), third-party support |
| [Rules.en.md](Rules.en.md) | The checkable rules V1–V13, patch and save constraints, the S-series search rules, the 16 static cleanup items, rule ID text (§10), mirror visibility conclusions (§11) |
| [DesignDecisions.en.md](DesignDecisions.en.md) | Why things are done this way: historical decisions and pitfalls, differences from the old to the new implementation, design trade-offs |
| [CompatibilityRetirement.en.md](CompatibilityRetirement.en.md) | The legacy save compatibility register: the support window (from 2.4), which saves each item is meaningful for, verdicts and retirement prerequisites |
| [SplitPlan.en.md](SplitPlan.en.md) | The plan and basis for the directory reorganization (9 responsibility clusters) and the optional split of large files; read when evaluating Vanilla's size |
| ModuleRules.en.md | Global hard constraints, dependency direction, patch ownership, startup order |

Reading this directory is enough to start work; the only additional read is ModuleRules.en.md.
