> Positioning: writes the constraints SDS.Api must obey as checkable entries; the rule body itself is governed by ModuleRules.en.md and the frozen baseline — this file does not restate gameplay semantics.

# SDS.Api Rules

## 1. Checkable Entries

| ID | Rule | Specific requirements | How to check | Basis |
|---|---|---|---|---|
| API-1 | Dependency boundary | References only `SDS.Contracts`, `SDS.Diag`, `SDS.Core`; `SDS.Vanilla`, `0Harmony`, `SDS.ThirdModCompat` are forbidden | `CheckAssemblyBoundaries.ps1` reference graph; `SDS.Api.csproj` should have exactly 3 `ProjectReference`s; searching `SDS.Api/*.cs` for `SDS.Vanilla` / `Harmony` / `SDS.ThirdModCompat` should yield 0 hits | ModuleRules.en.md §4, 6.5 |
| API-2 | Writes no rules | Does not implement stock, capacity, filtering, endpoint ordering, transactions, deduction, or reservations; only assembles query DTOs and forwards | Manual review + searching `SDS.Api` for implementation symbols such as `Capacity` / `Filter` / `Reserved` / `TryWithdrawTransaction`; `SDS.Api` should call only public members of `SDS_Core` | ModuleRules.en.md 6.5; ModuleResponsibilities.en.md §7 |
| API-3 | Caller gate vs rule gate division | Player faction, ownership, forbidding, and same-map are judged only in the Api; stock / capacity / filtering / ordering are judged by Core | Tests `ApiTests.DepositAt_NonPlayerFaction_IsRejected`, `DepositNow_EndToEnd_WritesLedger`; check that `ValidateDepositCall` / `ValidateWithdrawCall` contain only these four kinds of judgment | ModuleResponsibilities.en.md §7; PublicAPI.en.md §3.4 |
| API-4 | `ReadStockFor` does not duplicate routing | Channel determination calls only `SDS_Rules.TryGetStockKey`; no `RequiresPreservedStorage` or `SDS_StockKey.From...` routing may appear | Test `ApiTests.ReadStockFor_RoutesByCoreChannel`; grepping `SDS_API.cs` for `RequiresPreservedStorage` should yield 0 hits | ModuleResponsibilities.en.md §7; ModuleInterfaces.en.md 3.2 |
| API-5 | `TryWithdraw` is virtual-only | `key == null`, `quantity <= 0`, `VirtualHandoff`, and `key.preserved` all yield `InvalidArgument`; preserved goes only through `TryWithdrawPreservedExact` | Test `ApiTests.Withdraw_PreservedKey_IsInvalidArgument`; grep that a `key.preserved` branch exists and returns `InvalidArgument` | ModuleResponsibilities.en.md §7; PublicAPI.en.md §2.5 |
| API-6 | The bridge adds no rules | Bridge methods only forward to Core / Host; withdraw by def in bulk can only be a per-key combination of the same transaction primitive plus a whole-batch return; no new ordering, capacity, deduction, or job assignment may be added | Manual review + the `CheckCompatSurface.ps1 -Report` call graph; no new candidate ordering or count criteria may appear in the bridge; `ApiTests` / `CompatSdkTests` cover the forwarding behavior | ModuleInterfaces.en.md §5; ModuleRules.en.md 6.6 |
| API-7 | Version semantics | `Version = 4` declares five-axis compatibility only; the version changes only when five-axis signatures or semantics change; adding or removing bridge methods does not change the version | Test `ApiTests.Version_IsFour`; when it changes, mark the impact surface per E5 in ModuleInterfaces.en.md §5 and in the version records | PublicAPI.en.md §3.8; RuleIndex.en.md E5 |
| API-8 | Logging gate | The Api allows only `SDS_Diag.ErrorOnce`; `SDS_Diag.Fail` must not be called (ModuleInterfaces.en.md §2 restricts `Fail` to Core / Rules / Vanilla — the Api is not listed among its callers, and ThirdModCompat is forbidden as well) | Grepping `SDS.Api` for `SDS_Diag.Fail` should yield 0 hits; cross-check against the caller whitelist in ModuleInterfaces.en.md §2 | ModuleInterfaces.en.md §2; ModuleRules.en.md 6.2 |
| API-9 | The sole `Find.*` exception | Only `Find.TickManager.TicksGame` is allowed; `Find.CurrentMap`, `Find.Maps`, `GenClosest`, `ListerThings`, `GenPlace`, `GenSpawn` must not be introduced | Grepping `Find\.` in `SDS.Api` should hit only `CurrentTick()`; all other symbols should yield 0 hits | ModuleResponsibilities.en.md §7; ModuleRules.en.md 6.5 |
| API-10 | Public-surface whitelist | Only `SDS_API` is exposed; new public bridge methods must be registered in ModuleInterfaces.en.md §5 at the same time, otherwise they count as leaks | `CheckAssemblyBoundaries.ps1` type-level whitelist; the public method list is reconciled item by item against ModuleInterfaces.en.md §5 | ModuleRules.en.md §5; ModuleResponsibilities.en.md §2 |
| API-11 | Zero patches | The Api installs no Harmony patches; patches live only in Vanilla / ThirdModCompat | `CheckPatchInventory.ps1`; grepping `SDS.Api` for `[HarmonyPatch]` / `Harmony` should yield 0 hits | ModuleRules.en.md §7, §8 |

## 2. Caller Gate Order (for checking)

Deposit (`ValidateDepositCall`, shared by `TryFindDepositEndpoint` / `TryDepositAt`, `SDS_API.cs`):

1. The thing is non-null, not destroyed, and `stackCount > 0`;
2. the caller's faction is the player faction (`IsPlayerFaction`);
3. `SDS_Rules.Available`;
4. the thing and its root holder belong to the caller (`IsOwnedByCaller`);
5. the thing is not forbidden (`ForbidUtility.IsForbidden`);
6. `SDS_Rules.CanDeposit`;
7. the thing's map matches the passed-in map.

Withdrawal (`ValidateWithdrawCall`) contains only steps 2 and 3; pawn validity is appended by the individual take methods (`TryWithdraw` / `TryWithdrawFromMirror`).

## 3. Review Criteria

- Normal rejections write no logs; exceptions uniformly yield `InternalError` (or an empty set / `default`) and record one `公共API/*异常` (PublicAPI.en.md §3.6).
- Any change of the form "first compute the available count / shortfall / priority" must go back to Core; the Api does only result-code mapping (DesignDecisions.en.md §4).
- New bridge methods must update ModuleInterfaces.en.md §5 and the whitelist at the same time, otherwise `CheckAssemblyBoundaries.ps1` records them as leaks (DesignDecisions.en.md §4).
- Entries that cannot be proven by scripts (API-2, API-6) are recorded as manual review and must not be written up as "verified" (RuleIndex.en.md conventions).

## 4. Rule Text (authoritative)

> The normative sentences are carried over verbatim from the original "related rule ID" table; E5 and API-7 share the same origin. The two "5.6" occurrences in §3 correspond to DesignDecisions.en.md §4, pending.

| ID | Normative sentence (authoritative) | Where it applies in this module |
|---|---|---|
| E5 | A Version change is a compatibility declaration; with no external adoption it may break directly; with adoption, a thin forwarder must be kept for at least one major version | the sole basis for changing `Version = 4` |
| E6 | The compatibility layer may use only `SDS_API` / `SDS_Compat*` / `SDS_Diag` and public API types | the direct reason the bridge is public; the Api is the compatibility layer's only write entry |
| E7 | Each third-party entry has exactly one write-takeover path; read-only projections may coexist with adapter translation | the bridge must not add a second deduction / job-assignment / rollback path |
| R17 | When G is off, the read-side available count is zeroed and the ledger data is retained | the `GridUnavailable` branch of `ReadStock` |
| S16 | The supply shortfall is implemented only in `SDS_BillSupplyPlanner`; adapters only provide facts and execute | `TryPlanBillSupplySlot` / `TryPlanAllowedDefSupply` only forward |
| W30 / W58 | Direct-consume jobs default to Hand and do not materialize; count shortfalls are filled per the job's reservation intent on the mirror; the Api no longer provides restock-count registration | `RegisterJobFuelCountProvider` was deleted on 2026-11-16; `SDS_API` no longer carries that bridge |
| W33 / W35 | Weapons and apparel are forced virtual; `IsStudiable` is always preserved; callers must follow the channel per the query results | `ReadStockFor` / `RequiresPreservedStorage` only forward to Core |
| W50 | Third-party load manifests may be injected only through the internal bridge in `SDS_API` | the `InjectTransportLoadCandidates` bridge |

(Source: the corresponding rows of RuleIndex.en.md.)

## 5. Supplementary Criteria

- Adapters / the API make no cross-dialog reservations: a reservation would lower the visible count in the manifest, producing "greyed out despite being in stock"; settlement happens only at delivery or caravan-loading time.
- The planning stage provides only "available count" facts; during the cross-dialog window it holds no quantity and writes back no reservations; settlement is still completed by the single executor.
