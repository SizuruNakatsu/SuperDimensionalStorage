using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ════════════════════════════════════════════════════════════════════════════════════════
    // 虚拟通道账单兜底（通道分部 · 虚拟侧，宿主共享层）
    //
    // 原版 `WorkGiver_DoBill` 找不到原料后的**虚拟通道**兜底：本图镜像 = 可重建品类的地图代表，
    // 交给原版自己的配料算法挑选，不碰原版区域枚举。
    // 原版配料算法的反射探针（findInSet / findInSetNoMix）与参考格（RootCell）是两条通道兜底
    // 共用的唯一实现，放在本文件（共享层 Jobs/）；保真侧（Preserve/SDS_PreservedBill.cs）只调用、
    // 不复写。
    // 通道分部约束：Preserve/ 与 Mirrors/ 互不引用，共享目录是唯一合法桥
    // （CheckAssemblyBoundaries.ps1 §10；SDS.Vanilla/规则.md 通道分部节）。
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 账单配料兜底的共享探针 + 虚拟通道腿：
    /// 共享 = 原版配料算法反射探针（findInSet / findInSetNoMix）与参考格（RootCell），
    /// 保真侧（SDS_PreservedBillDispatch）只调用本探针，不得另写一份；
    /// 虚拟 = 原版失败后把本图镜像作为候选交给原版算法挑选。
    /// </summary>
    internal static class SDS_VirtualBillFallback
    {
        /// <summary>原版配料算法（private static）。延迟反射，签名不符时整块跳过；保真侧经 TryGetFinder 复用。</summary>
        internal static MethodInfo findInSet;

        /// <summary>原版固定配料算法（private static，TryFindBestFixedIngredients 用）。</summary>
        private static MethodInfo findInSetNoMix;

        /// <summary>原版配料算法延迟反射（保真侧与虚拟侧共用的唯一探针）。</summary>
        internal static bool TryGetFinder()
        {
            if (findInSet != null)
            {
                return true;
            }
            findInSet = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet",
                new[]
                {
                    typeof(List<Thing>), typeof(Bill), typeof(List<ThingCount>),
                    typeof(IntVec3), typeof(bool), typeof(List<IngredientCount>)
                });
            return findInSet != null;
        }

        private static bool TryGetNoMixFinder()
        {
            if (findInSetNoMix != null)
            {
                return true;
            }
            findInSetNoMix = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestIngredientsInSet_NoMixHelper",
                new[]
                {
                    typeof(List<Thing>), typeof(List<IngredientCount>), typeof(List<ThingCount>),
                    typeof(IntVec3), typeof(bool), typeof(List<IngredientCount>), typeof(Bill)
                });
            return findInSetNoMix != null;
        }

        /// <summary>虚拟兜底共用的镜像候选收集：来源只取本图镜像，过滤按本入口的原版契约。</summary>
        private static bool CollectIngredientMirrors(Map map, Pawn pawn, Thing billGiver,
            Predicate<Thing> predicate, List<Thing> candidates)
        {
            SDS_MirrorRegistry.CollectOn(map, candidates);
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                Thing thing = candidates[i];
                if (thing == null || thing.Destroyed || !thing.Spawned
                    || thing.IsForbidden(pawn) || !pawn.CanReserve(thing)
                    || !SDS_MedicalGate.Allows(billGiver, thing.def)
                    || (predicate != null && !predicate(thing)))
                {
                    candidates.RemoveAt(i);
                }
            }
            return candidates.Count > 0;
        }

        /// <summary>
        /// 原版找不到原料后的**虚拟通道**兜底：把本图镜像作为候选，交给原版
        /// `TryFindBestBillIngredientsInSet` 选择；不碰原版区域枚举。
        /// 只在「原版已失败 + 有可达端点」时执行，失败交给原版退避。
        /// </summary>
        public static bool TrySupplyVirtualIngredients(Bill bill, Pawn pawn, Thing billGiver,
            List<ThingCount> chosen, List<IngredientCount> missingIngredients)
        {
            if (!CanVirtualFallback(bill, pawn, billGiver, chosen))
            {
                return false;
            }
            Map map = billGiver.Map ?? pawn.Map;
            List<Thing> candidates = new List<Thing>();
            if (!CollectIngredientMirrors(map, pawn, billGiver, bill.IsFixedOrAllowedIngredient, candidates)
                || !TryGetFinder())
            {
                return false;
            }
            chosen.Clear();
            // S17：失败时不得改写真实 missing 列表——原版要靠它给出 NoMedicineMatchingCategory。
            List<IngredientCount> tempMissing = missingIngredients != null
                ? new List<IngredientCount>()
                : null;
            bool found;
            try
            {
                found = (bool)findInSet.Invoke(null,
                    new object[] { candidates, bill, chosen, RootCell(billGiver, pawn), false, tempMissing });
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("虚拟账本配料调用原版算法失败（已忽略，原版行为不受影响）", e);
                chosen.Clear();
                return false;
            }
            if (!found || chosen.Count == 0)
            {
                chosen.Clear();
                return false;
            }
            missingIngredients?.Clear();
            return true;
        }

        /// <summary>
        /// `TryFindBestFixedIngredients` 的虚拟通道兜底（生化舱等固定配料）。
        /// 同样只在原版失败后接管，使用镜像作为候选并复用原版 `_NoMixHelper`。
        /// </summary>
        public static bool TrySupplyVirtualFixedIngredients(List<IngredientCount> ingredients, Pawn pawn,
            Thing ingredientDestination, List<ThingCount> chosen, float searchRadius)
        {
            if (ingredients == null || ingredients.Count == 0 || pawn == null
                || ingredientDestination == null || chosen == null)
            {
                return false;
            }
            Map map = ingredientDestination.Map ?? pawn.Map;
            if (SDS_Core.Ledger == null || map == null || !SDS_Rules.Available
                || !SDS_Exposure.MapExposesGrid(map)
                || !HasReachableEndpointWithin(pawn, RootCell(ingredientDestination, pawn), searchRadius))
            {
                return false;
            }
            List<Thing> candidates = new List<Thing>();
            if (!CollectIngredientMirrors(map, pawn, ingredientDestination,
                    t => AnyIngredientAllows(ingredients, t), candidates)
                || !TryGetNoMixFinder())
            {
                return false;
            }
            chosen.Clear();
            bool found;
            try
            {
                found = (bool)findInSetNoMix.Invoke(null,
                    new object[]
                    {
                        candidates, ingredients, chosen, RootCell(ingredientDestination, pawn),
                        false, null, null
                    });
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("虚拟账本固定配料调用原版算法失败（已忽略，原版行为不受影响）", e);
                chosen.Clear();
                return false;
            }
            if (!found || chosen.Count == 0)
            {
                chosen.Clear();
                return false;
            }
            return true;
        }

        private static bool CanVirtualFallback(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
        {
            if (bill == null || pawn == null || billGiver == null || chosen == null
                || bill.recipe == null || bill.recipe.ingredients == null
                || bill.recipe.ingredients.Count == 0)
            {
                return false;
            }
            Map map = billGiver.Map ?? pawn.Map;
            return SDS_Core.Ledger != null && map != null && SDS_Rules.Available
                && SDS_Exposure.MapExposesGrid(map)
                && HasReachableEndpointWithin(pawn, RootCell(billGiver, pawn), bill.ingredientSearchRadius);
        }

        /// <summary>
        /// 是否存在一个可达、且距离参考格不超过原版搜索半径的端点。
        /// 这样账本兜底仍然遵守 `ingredientSearchRadius` / `searchRadius`，不把仓库变成全图瞬移。
        /// </summary>
        private static bool HasReachableEndpointWithin(Pawn pawn, IntVec3 reference, float radius)
        {
            if (pawn == null)
            {
                return false;
            }
            float radiusSquared = Math.Max(0f, radius) * Math.Max(0f, radius);
            if (!SDS_AccessPointResolver.TryGetNearestReachable(pawn, out SDS_AccessPoint point)
                || point == null)
            {
                return false;
            }
            float distanceSquared = ReferenceEquals(point.thing, pawn)
                ? (pawn.Position - reference).LengthHorizontalSquared
                : (reference - point.cell).LengthHorizontalSquared;
            return distanceSquared <= radiusSquared;
        }

        private static bool AnyIngredientAllows(List<IngredientCount> ingredients, Thing thing)
        {
            for (int i = 0; i < ingredients.Count; i++)
            {
                IngredientCount ingredient = ingredients[i];
                if (ingredient != null && ingredient.filter != null && ingredient.filter.Allows(thing))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>参考格：交互格优先，其次台位 / 小人。两条通道兜底共用（唯一实现）。</summary>
        internal static IntVec3 RootCell(Thing billGiver, Pawn pawn)
        {
            if (billGiver is Building building && building.def != null && building.def.hasInteractionCell)
            {
                return building.InteractionCell;
            }
            if (billGiver != null)
            {
                return billGiver.Position;
            }
            return pawn != null ? pawn.Position : IntVec3.Invalid;
        }
    }

    /// <summary>
    /// `WorkGiver_DoBill.TryFindBestFixedIngredients`（生化舱等固定配料）：
    /// 原版失败后，从虚拟账本镜像里兜底；仍走原版 `_NoMixHelper` 选择。
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.TryFindBestFixedIngredients))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_VirtualFixedIngredients
    {
        public static void Postfix(List<IngredientCount> ingredients, Pawn pawn, Thing ingredientDestination,
            List<ThingCount> chosen, float searchRadius, ref bool __result)
        {
            try
            {
                if (__result)
                {
                    return;
                }
                if (SDS_VirtualBillFallback.TrySupplyVirtualFixedIngredients(ingredients, pawn,
                    ingredientDestination, chosen, searchRadius))
                {
                    __result = true;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("虚拟账本固定配料补漏失败（已忽略，原版行为不受影响）", e);
            }
        }
    }
}
