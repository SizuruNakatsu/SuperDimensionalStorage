using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 「配方缺料判定」的只读查询服务（规则 S16；《读的准》§三.1 + 原版
    /// <see cref="RecipeDef.PotentiallyMissingIngredients"/> 的四条件）。
    /// S16 的唯一规则实现是 Core 的 SDS_BillSupplyPlanner；本文件只提供只读查询，不复制补料规则。
    /// 只读：不扣账、不预留；唯一副作用是复用展示物缓存（只读探针，绝不 Destroy）。
    /// </summary>
    internal static class SDS_BillIngredientQuery
    {
        /// <summary>
        /// 遍历一次超维空间库存（虚拟 + 保真），按槽位记录是否已有实物满足原版谓词，
        /// 再把「已满足的槽位」映射回原版返回的代表 def。
        /// </summary>
        internal static HashSet<ThingDef> CollectSatisfied(RecipeDef recipe, List<ThingDef> missing,
            Pawn billDoer, Map map)
        {
            HashSet<ThingDef> satisfied = new HashSet<ThingDef>();
            if (recipe == null || missing == null || missing.Count == 0 || !SDS_Rules.Available)
            {
                return satisfied;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return satisfied;
            }
            List<IngredientCount> slots = recipe.ingredients;
            if (slots == null || slots.Count == 0)
            {
                return satisfied;
            }
            bool[] satisfiedSlots = new bool[slots.Count];
            int remaining = slots.Count;

            List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
            for (int i = 0; i < snapshot.Count && remaining > 0; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                if (entry == null || entry.key == null || entry.count <= 0 || entry.key.preserved
                    || entry.key.thingDef == null || ledger.IsForbidden(entry.key)
                    || !DefMayMatchAnySlot(recipe, slots, entry.key.thingDef))
                {
                    continue;
                }
                Thing probe;
                if (!SDS_MirrorService.TryGetMirror(map, entry.key, out probe) || probe == null)
                {
                    probe = SDS_OutputUtility.CachedDisplayThing(entry.key);
                }
                if (probe == null || probe.Destroyed)
                {
                    continue;
                }
                remaining = MarkSatisfiedSlots(recipe, slots, satisfiedSlots, remaining, probe, billDoer, map);
            }

            if (remaining > 0)
            {
                List<Thing> preserved = new List<Thing>();
                ledger.AppendPreservedThings(preserved);
                for (int i = 0; i < preserved.Count && remaining > 0; i++)
                {
                    Thing thing = preserved[i];
                    if (thing == null || thing.Destroyed || thing.def == null)
                    {
                        continue;
                    }
                    if (!SDS_Rules.TryGetStockKey(thing, out SDS_StockKey preservedKey)
                        || preservedKey == null || ledger.IsForbidden(preservedKey))
                    {
                        continue;
                    }
                    // 缩装家具：原版 holder 树会递归展开内部建筑，这里用内部物做同一套匹配。
                    Thing probe = thing.GetInnerIfMinified();
                    if (probe == null || probe.def == null
                        || !DefMayMatchAnySlot(recipe, slots, probe.def))
                    {
                        continue;
                    }
                    remaining = MarkSatisfiedSlots(recipe, slots, satisfiedSlots, remaining, probe, billDoer, map);
                }
            }

            for (int i = 0; i < missing.Count; i++)
            {
                ThingDef def = missing[i];
                if (def == null)
                {
                    continue;
                }
                for (int j = 0; j < slots.Count; j++)
                {
                    IngredientCount slot = slots[j];
                    if (slot == null || !satisfiedSlots[j] || slot.filter == null
                        || !slot.filter.Allows(def))
                    {
                        continue;
                    }
                    if (!slot.IsFixedIngredient && !recipe.fixedIngredientFilter.Allows(def))
                    {
                        continue;
                    }
                    satisfied.Add(def);
                    break;
                }
            }
            return satisfied;
        }

        /// <summary>def 级快速过滤：该 def 有没有可能进入任一原料槽（不做材质 / 品质判定）。</summary>
        private static bool DefMayMatchAnySlot(RecipeDef recipe, List<IngredientCount> slots, ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            for (int i = 0; i < slots.Count; i++)
            {
                IngredientCount slot = slots[i];
                if (slot == null || slot.filter == null || !slot.filter.Allows(def))
                {
                    continue;
                }
                if (!slot.IsFixedIngredient && !recipe.fixedIngredientFilter.Allows(def))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        private static int MarkSatisfiedSlots(RecipeDef recipe, List<IngredientCount> slots,
            bool[] satisfiedSlots, int remaining, Thing thing, Pawn billDoer, Map map)
        {
            // 与 RecipeDef.PotentiallyMissingIngredients 的四个条件逐项对齐。
            if (billDoer != null && thing.IsForbidden(billDoer))
            {
                return remaining;
            }
            if (thing.Spawned && map != null && thing.Position.Fogged(map))
            {
                return remaining;
            }
            for (int j = 0; j < slots.Count; j++)
            {
                IngredientCount slot = slots[j];
                if (satisfiedSlots[j] || slot == null || slot.filter == null)
                {
                    continue;
                }
                if (!slot.IsFixedIngredient && !recipe.fixedIngredientFilter.Allows(thing))
                {
                    continue;
                }
                if (slot.filter.Allows(thing))
                {
                    satisfiedSlots[j] = true;
                    remaining--;
                }
            }
            return remaining;
        }
    }
}
