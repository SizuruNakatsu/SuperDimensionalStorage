# SDS.Vanilla · Legacy Save Compatibility Retirement Register

> Positioning: a **compatibility register**, not rule text. Rules remain authoritative in [Rules.en.md](Rules.en.md) §10 W26 "old saves remain readable; no data loss takes priority over an unchanged format"; this table only registers "which branches are compatible with which era and when they can be deleted", so that compatibility code does not proliferate endlessly and saves still inside the window are not deleted by mistake.
> Same nature as [PatchInventory.en.md](../PatchInventory.en.md): registration and reconciliation only; it defines no gameplay.

## 1. Support window

| Item | Value | Basis |
|---|---|---|
| Lowest version promised readable | **2.4** | Set 2026-10-26 21:54 |
| Saves written before the window | **Readability not promised**; read if possible, and if unreadable log a single `Warn` without adding compatibility branches for them | W26 constrains only within the window |
| How the window may be changed | Only a human may edit this section; changing the window equals changing the compatibility promise, which is L3 | DesignPrinciples.en.md E4 |

## 2. Compatibility register

Verdict column: **Retired** = code deleted, no longer reading the old form; **Kept** = saves within the window still depend on it (including "tentative date" = lacking evidence of when it was written, conservatively kept per the given date); **Pending confirmation** = still lacking evidence and no date given.

| # | Compatibility item | Old form | Relevant to which saves | Current entry point | Evidence | Verdict |
|---|---|---|---|---|---|---|
| 1 | `sdsProtectedOutputs` | The old `MapComponent_SDS_ProtectedOutputs` node (`LookMode.Deep`) | Only meaningful for saves written **before 2026-10-22** (merged into the reclaim ledger that day; read-only afterwards) | Deleted (formerly `MapComponent_SDS_ProtectedOutputs.ExposeData` → `StashLegacyProtectedOutputs` / `AdoptPendingLegacyProtectedOutputs`) | [PatchInventory.en.md](../PatchInventory.en.md) §5 migration table; executed 2026-11-16 per this register | **Retired (2026-11-16)** |
| 2 | UI legacy aggregate key | `selectedKey` could be the "same def + same channel" aggregate key from before `CategoryKeyOf` zeroing | Only meaningful for saves written **before 2026-10-25** | Deleted (formerly `Window_SuperSpace.TryNormalizeLegacySelection` / `MatchesLegacyAggregate`) | Executed 2026-11-16 per this register | **Retired (2026-11-16)** |
| 3 | `SDS_StockKey` old field names | Save fields `thingDef` / `stuff` (now `defName` / `stuffName`) | Saves written **before 2026-11-13** (tentative date) | `SDS_StockKey.ExposeData` **retains only the read side** | [SDS_Stock.cs](../../超维存储/Source/SDS.Contracts/SDS_Stock.cs) | **Write stopped (2026-11-16)**: reading kept until the window expires |
| 4 | `dataVersion` default fallback `1` | Saves without a version node read as 1 | All saves from the first release to now | `SDS_Ledger.ExposeData` | [SDS_Ledger.cs](../../超维存储/Source/SDS.Core/SDS_Ledger.cs); [SDS.Core/Rules.en.md](../SDS.Core/Rules.en.md) | **Kept**: a pre-upgrade anchor, not a compatibility branch |
| 5 | `sdsReclaim` node ownership | Since C8 (2026-11-15), `SDS_GameComponent_Grid` exclusively reads and writes it, node name unchanged | All saves within the window (one read path for old and new) | `SDS_GameComponent_Grid.ExposeData` | [SDS.Core/Rules.en.md](../SDS.Core/Rules.en.md) §C8 migration | **Kept** (within the window) |
| 6 | Endpoint input buffer duplicates | 2.5.2's write side did not remove items from `inputBuffer`, so loads produce duplicate copies with the same `thingID` | Saves written by **2.5.2 and earlier** | `SDS_EndpointUtility.PruneLoadedBuffer` (removes by `thingID`) | [SDS.Vanilla/DesignDecisions.en.md](DesignDecisions.en.md) §2.8; [SDS_EndpointUtility.cs](../../超维存储/Source/SDS.Vanilla/Endpoints/SDS_EndpointUtility.cs) | **Kept** (2.5.2 is later than the window start) |
| 7 | `SDS_ReclaimSave.policy` default | When the field is missing, fall back to `JobTarget` | Saves written **before 2026-11-13** (tentative date) | `SDS_ReclaimSave.ExposeData` | [SDS_Withdraw.cs](../../超维存储/Source/SDS.Contracts/SDS_Withdraw.cs) | **Kept** (tentatively changed as of 2026-11-13) |
| 8 | `sdsMirrors` load shell | The old mirror node is only destroyed, not written to the ledger; after load, rebuilt from the ledger | Saves written **before 2026-11-13** (tentative date) | `SDS_VanillaState.ExposeData` | [SDS.Vanilla/Interfaces.en.md](Interfaces.en.md) 1.8 | **Kept** (tentatively changed as of 2026-11-13) |

## 3. Retirement order and prerequisites

1. **Item 3 has stopped writing (2026-11-16)**: the save-side `thingDef` / `stuff` have been deleted; the read side is kept until saves written before 2026-11-13 exit the window, then deleted, with at least one release cycle in between.
2. **Build the version anchor first, then delete structural compatibility**: item 4 is not retired; but only after `dataVersion` actually starts writing new values and migration consumers appear can the other structural branches be safely deleted.
3. **Every deletion must run a save/load round trip**: `SDS.Core.Tests` (ledger / key fields) and `SDS.Rules.Tests` (rules) are the baseline; item 1 was deleted on 2026-11-16, and the reclaim ledger regression now relies on normal `sdsReclaim` reads and writes.
4. **Deletion is L3**: anything touching `ExposeData` / `Scribe` is handled as L3 per DesignPrinciples.en.md E4; this round's items 1 / 2 (full deletion) and item 3 (write stop only) were executed after explicit human authorization on 2026-11-16, with the AI responsible only for execution and evidence collection.

## 4. Items that do not belong in this register

The following are often mistaken for "old save compatibility", but differ in nature; they **must not** be registered in this table, and the window verdicts of this table must not be used to delete them:

| Item | True nature | Belongs to |
|---|---|---|
| `SDS_Rules.ConvertPreservedGearToVirtual` (W34) | Rule / configuration migration, triggered by a player toggle, **may run for every new save** | [SDS.Rules/Rules.en.md](../SDS.Rules/Rules.en.md) W34 |
| CE's `checkCooldownUntil` / `priorityCacheUntil` | Runtime static caches crossing saves (absolute tick rollback), an E1 cleanup | [SDS_ModCompat_CE.cs](../../超维存储/Source/SDS.ThirdModCompat/SDS_ModCompat_CE.cs), [SDS.Vanilla/Rules.en.md](Rules.en.md) §6 |
| `pendingLegacyProtectedOutputs` | Runtime staging for item 1, retiring together with item 1 | Same as item 1 |
| Null fallback construction for `depositFilter` / `sidebarUI` / `transportLoadRequests` | Load-time null defense, not an old format | [SDS.Vanilla/Rules.en.md](Rules.en.md) §5 |

## 5. Maintenance conventions

- Adding any branch that is "meaningful only for old saves" requires adding a row to this table at the same time; otherwise it is treated as an unfinished L2 change per DesignPrinciples.en.md E3.
- After retiring an item, change that row's verdict to "Retired (date)" instead of deleting the row — history is preserved to make player reports traceable.
- Cross-referenced with the save rules of [Rules.en.md](Rules.en.md) §5 and [DesignDecisions.en.md](DesignDecisions.en.md) §2 "Differences and pitfalls: old implementation → new implementation"; the main text is not duplicated.
