using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 第三方补料规划的唯一规则出口：第三方自有库存优先，SDS 只补缺口。
    /// 适配器只提供事实（自有量 / 外部可用量 / 周期用量），选 def、目标量、预留与缺口都只在这里决定。
    /// 入 / 出：入候选与事实委托，出「选中的 def + 需求 + 自有量 + 缺口」；不改账本、不取货。
    /// 区别：SDS_Availability 只管可用量读数，本类负责选料与缺口口径。
    /// </summary>
    internal static class SDS_BillSupplyPlanner
    {
        // S17：医药等级等外部筛选由调用方注入；Core 不读 SDS_Host.MedicalCare。
        // RULE:S17
        /// <summary>
        /// 配方槽 / def 是否接受某候选（原版判据复刻；医药等外部筛选由调用方以委托注入）。
        /// 入：slot —— 配方槽（可空）；thing —— 具体实物（可空，非空时走实例级判据）；def —— 候选 def；bill / recipe —— 当前账单；allowedIngredients / corpseFilter —— 适配器给的白 / 黑名单；medicineAllowed —— 调用方的附加筛选（可空，医药等级由 Vanilla 注入）。
        /// 出：是否接受；缺内层内容的保真真身一律 false（原版判定会空引用）。
        /// 区别：本方法不读任何宿主；外部判据只经 medicineAllowed 进入。
        /// </summary>
        internal static bool AllowsDef(IngredientCount slot, Thing thing, ThingDef def, Bill bill,
            RecipeDef recipe, List<ThingDef> allowedIngredients, ThingFilter corpseFilter,
            Func<ThingDef, bool> medicineAllowed = null)
        {
            if (def == null)
            {
                return false;
            }
            // 保真真身若已失去内层（尸体无内层小人 / 缩装无内层物），原版判定会空引用，一律不参与。
            if (thing != null && !SDS_StockData.HasLiveContent(thing))
            {
                return false;
            }
            // 外部筛选（医药等级等）由调用方注入；Core 不读宿主。
            if (medicineAllowed != null && !medicineAllowed(def))
            {
                return false;
            }
            if (bill != null && thing != null)
            {
                return bill.IsFixedOrAllowedIngredient(thing);
            }
            if (slot != null && slot.filter != null && !slot.filter.Allows(def))
            {
                return false;
            }
            if (bill != null && slot != null && !slot.IsFixedIngredient
                && (bill.ingredientFilter == null || !bill.ingredientFilter.Allows(def)))
            {
                return false;
            }
            if (allowedIngredients != null && allowedIngredients.Count > 0
                && !allowedIngredients.Contains(def))
            {
                return false;
            }
            if (slot != null && !slot.IsFixedIngredient && recipe?.fixedIngredientFilter != null
                && !recipe.fixedIngredientFilter.Allows(def))
            {
                return false;
            }
            if (corpseFilter != null && def.IsCorpse && !corpseFilter.Allows(def))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 按单个配方槽规划补料（单槽口径）。
        /// 入：slot / bill / recipe / 白名单 / 尸体筛 —— 判据上下文；need —— 本次缺口；ownCount / externalCount —— 自有与外部事实；reserveProvider —— 预留事实；pool —— 自有池（会被扣减）。
        /// 出：选中的 def、required（含预留）、fromOwn、shortfall；无候选返回 false。
        /// 区别：TryPlanAllowedDefSupply 按 def 列表规划（不绑定配方槽）；两者最终都走 TryPlanFromCandidates。
        /// </summary>
        internal static bool TryPlanSlot(IngredientCount slot, Bill bill, RecipeDef recipe,
            List<ThingDef> allowedIngredients, ThingFilter corpseFilter, int need,
            Func<ThingDef, int> ownCount, Func<ThingDef, long> externalCount,
            Func<ThingDef, bool> extraAllows,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
        {
            def = null;
            required = 0;
            fromOwn = 0;
            shortfall = 0;
            if (slot?.filter == null || need <= 0)
            {
                return false;
            }
            IEnumerable<ThingDef> candidates = slot.filter.AllowedThingDefs;
            if (candidates == null)
            {
                return false;
            }
            return TryPlanFromCandidates(candidates,
                candidate => AllowsDef(slot, null, candidate, bill, recipe, allowedIngredients, corpseFilter)
                    && (extraAllows == null || extraAllows(candidate)),
                _ => need, ownCount, externalCount, reserveProvider, reserveApplied, pool,
                false, false, out def, out required, out fromOwn, out shortfall);
        }

        /// <summary>
        /// 按 def 列表规划补料（不绑定配方槽的通用口径）。
        /// 入：allowedDefs —— 候选 def；needForDef —— 每个 def 的需求；其余同 TryPlanSlot。
        /// 出：同 TryPlanSlot；列表空 / 需求委托为空返回 false。
        /// 区别：TryPlanSlot 用槽筛选并逐槽消耗 pool；本方法直接在 def 列表里选，允许部分供给。
        /// </summary>
        internal static bool TryPlanAllowedDefSupply(
            List<ThingDef> allowedDefs, ThingFilter corpseFilter,
            Func<ThingDef, int> needForDef,
            Func<ThingDef, int> ownCount, Func<ThingDef, long> externalCount,
            Func<ThingDef, bool> extraAllows,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
        {
            def = null;
            required = 0;
            fromOwn = 0;
            shortfall = 0;
            if (allowedDefs == null || allowedDefs.Count == 0 || needForDef == null)
            {
                return false;
            }
            return TryPlanFromCandidates(allowedDefs,
                candidate => AllowsDef(null, null, candidate, null, null, allowedDefs, corpseFilter)
                    && (extraAllows == null || extraAllows(candidate)),
                needForDef, ownCount, externalCount, reserveProvider, reserveApplied, pool,
                true, true, out def, out required, out fromOwn, out shortfall);
        }

        /// <summary>
        /// 登记一次已存入的自有量。
        /// 入：pool —— 调用方的自有池；def —— 品类；amount —— 本次入库量。
        /// 出：无；只更新 pool 计数（夹到 int 上限），不写账本。
        /// 区别：账本入账走 TryDepositVirtual / TryConvertThingToVirtual；本方法只维护规划期自有池。
        /// </summary>
        internal static void NotifyDeposited(Dictionary<ThingDef, int> pool, ThingDef def, int amount)
        {
            if (pool == null || def == null || amount <= 0)
            {
                return;
            }
            pool.TryGetValue(def, out int current);
            long next = (long)Math.Max(0, current) + amount;
            pool[def] = next > int.MaxValue ? int.MaxValue : (int)next;
        }

        /// <summary>
        /// 候选遍历与选择（两个规划入口的唯一实现，S16）。
        /// 入：candidates —— 候选序列；allows —— 判据；needForDef —— 每个候选的需求；ownCount / externalCount / reserveProvider / pool —— 事实；reserveBeforeSelection —— 选料前是否先扣预留；allowPartialSupply —— 是否允许部分供给。
        /// 出：选中的 def + 需求 + 自有量 + 缺口；无满足条件的候选返回 false。
        /// 语义：自有量够则直接选；否则记住第一个「自有 + 外部」够的候选作兜底；允许部分供给时只要外部 > 0 就选。
        /// 区别：Choose 只做最终账目结算；本方法负责「选谁」。
        /// </summary>
        // RULE:S16
        private static bool TryPlanFromCandidates(
            IEnumerable<ThingDef> candidates, Func<ThingDef, bool> allows,
            Func<ThingDef, int> needForDef,
            Func<ThingDef, int> ownCount, Func<ThingDef, long> externalCount,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            bool reserveBeforeSelection, bool allowPartialSupply,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
        {
            def = null;
            required = 0;
            fromOwn = 0;
            shortfall = 0;
            if (candidates == null || allows == null || needForDef == null)
            {
                return false;
            }
            ThingDef fallback = null;
            int fallbackOwn = 0;
            foreach (ThingDef candidate in candidates)
            {
                if (candidate == null || !allows(candidate))
                {
                    continue;
                }
                int need = Math.Max(0, needForDef(candidate));
                if (need <= 0)
                {
                    continue;
                }
                int own = ownCount != null ? Math.Max(0, ownCount(candidate)) : 0;
                long selectionNeed = need;
                if (reserveBeforeSelection)
                {
                    selectionNeed += RawReserve(candidate, reserveProvider);
                }
                int selectionRequired = selectionNeed > int.MaxValue
                    ? int.MaxValue
                    : (int)selectionNeed;
                if (own >= selectionRequired)
                {
                    Choose(candidate, need, own, reserveProvider, reserveApplied, pool,
                        out def, out required, out fromOwn, out shortfall);
                    return true;
                }
                long external = externalCount != null ? Math.Max(0L, externalCount(candidate)) : 0L;
                if (allowPartialSupply)
                {
                    if (external > 0L)
                    {
                        Choose(candidate, need, own, reserveProvider, reserveApplied, pool,
                            out def, out required, out fromOwn, out shortfall);
                        return true;
                    }
                    continue;
                }
                if (fallback == null && own + external >= selectionRequired)
                {
                    fallback = candidate;
                    fallbackOwn = own;
                }
            }
            if (fallback == null)
            {
                return false;
            }
            Choose(fallback, Math.Max(0, needForDef(fallback)), fallbackOwn,
                reserveProvider, reserveApplied, pool, out def, out required, out fromOwn, out shortfall);
            return true;
        }

        /// <summary>
        /// 结算选中候选的账目（需求 + 预留 − 自有 = 缺口）。
        /// 入：chosen —— 选中 def；need —— 需求；own —— 自有量；reserveProvider / reserveApplied —— 预留事实（同 def 只应用一次）；pool —— 自有池（会被扣到剩余）。
        /// 出：def / required / fromOwn / shortfall。
        /// 区别：TryPlanFromCandidates 负责选择；本方法只算数，不判断候选是否可用。
        /// </summary>
        private static void Choose(ThingDef chosen, int need, int own,
            Func<ThingDef, int> reserveProvider, HashSet<ThingDef> reserveApplied,
            Dictionary<ThingDef, int> pool,
            out ThingDef def, out int required, out int fromOwn, out int shortfall)
        {
            int reserve = ApplyReserve(chosen, reserveProvider, reserveApplied);
            long requiredLong = (long)Math.Max(0, need) + reserve;
            required = requiredLong > int.MaxValue ? int.MaxValue : (int)requiredLong;
            fromOwn = Math.Min(required, Math.Max(0, own));
            if (pool != null)
            {
                pool[chosen] = Math.Max(0, own - fromOwn);
            }
            shortfall = required - fromOwn;
            def = chosen;
        }

        /// <summary>
        /// 读预留量（不登记已应用）。
        /// 入：def —— 候选；reserveProvider —— 预留事实。
        /// 出：≥ 0 的预留量；无委托返回 0。
        /// 区别：ApplyReserve 会按 def 去重登记；本方法只读，供选料阶段估算。
        /// </summary>
        private static int RawReserve(ThingDef def, Func<ThingDef, int> reserveProvider)
        {
            if (reserveProvider == null || def == null)
            {
                return 0;
            }
            return Math.Max(0, reserveProvider(def));
        }

        /// <summary>
        /// 读预留量并登记「该 def 已应用过」。
        /// 入：def —— 候选；reserveProvider —— 预留事实；reserveApplied —— 已应用集合。
        /// 出：首次应用返回预留量，重复 / 缺委托返回 0。
        /// 区别：RawReserve 不登记、可重复读；本方法保证同一次规划里预留只加一次。
        /// </summary>
        private static int ApplyReserve(ThingDef def, Func<ThingDef, int> reserveProvider,
            HashSet<ThingDef> reserveApplied)
        {
            if (reserveProvider == null || reserveApplied == null || !reserveApplied.Add(def))
            {
                return 0;
            }
            return Math.Max(0, reserveProvider(def));
        }
    }
}
