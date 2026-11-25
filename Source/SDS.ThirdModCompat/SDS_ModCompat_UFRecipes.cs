using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 联合轻工家具拓展-UF Light Industries Furniture 适配器：**只补全自动打印机的保真通道供料**。
    ///
    /// 为什么还需要：
    ///  - 它自己的全自动拉料是 `CollectPullableIngredients`（`SlotGroup` / `IHaulSource` / holder 树）；
    ///  - 保真真身（尸体 / 缩装家具 / 基因载体）不 spawn、不在 holder 树，它永远看不到；
    ///  - 虚拟品类不在这里补：它自己的拉料能看到金库镜像，取料走 `SplitOff` + `ThingOwner`，
    ///    已由本体原版翻译覆盖（再补就是第二套规则）。
    ///
    /// 挂点放在它自己的拉料失败之后（`TryPullIngredientsFromCandidates` Postfix），
    /// 不在 `HasIngredients` 这种查询函数里做副作用。
    ///
    /// 明确不做：
    ///  - 不改打印机状态机 / 计时 / 功率 / 维护；
    ///  - 不改半自动的 WorkGiver / JobDriver；
    ///  - 不补虚拟品类。
    /// </summary>
    internal static class SDS_ModCompat_UFRecipes
    {
        private static Type autoUtilType;
        private static bool resolved;

        public static void TryApply(Harmony harmony)
        {
            bool detected = false;
            try
            {
                detected = AccessTools.TypeByName("UFRecipes.Building_SuperWorkbench") != null
                    || AccessTools.TypeByName("UFRecipes.AutoSequenceUtil") != null;
                if (!Resolve())
                {
                    SDS_CompatReportHub.Register("联合轻工家具拓展-UF Light Industries Furniture", "KindSeal.UFLI", detected, null,
                        null, detected ? "关键类型缺失（版本变化？）" : null);
                    return;
                }
                detected = true;
                SDS_CompatPatchReport report = new SDS_CompatPatchReport("联合轻工家具拓展-UF Light Industries Furniture");
                MethodInfo pull = SDS_CompatTools.Method(autoUtilType, "TryPullIngredientsFromCandidates",
                    typeof(Bill), typeof(List<Thing>), typeof(Map), typeof(ThingOwner),
                    typeof(bool).MakeByRefType());
                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_UFRecipes),
                    "AutoSequenceUtil.TryPullIngredientsFromCandidates（全自动·保真兜底）",
                    pull, null, nameof(PullCandidatesPostfix),
                    "找不到 TryPullIngredientsFromCandidates(Bill,List<Thing>,Map,ThingOwner,out bool)");
                SDS_CompatReportHub.Register("联合轻工家具拓展-UF Light Industries Furniture", "KindSeal.UFLI", true, report);
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register("联合轻工家具拓展-UF Light Industries Furniture", "KindSeal.UFLI", detected, null,
                    null, "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("联合轻工家具拓展-UF Light Industries Furniture 适配器应用失败（已跳过，双方原行为不受影响）", e);
            }
        }

        private static bool Resolve()
        {
            if (resolved)
            {
                return autoUtilType != null;
            }
            resolved = true;
            autoUtilType = AccessTools.TypeByName("UFRecipes.AutoSequenceUtil");
            return autoUtilType != null;
        }

        /// <summary>
        /// 它自己的「从地图 / 储存组拉料」返回 false 之后：
        /// 只补保真通道（尸体 / 缩装 / 基因），补上了就把结果改成 true；
        /// 虚拟品类仍交给它自己的原版拉料，绝不在适配器里再补一套。
        /// </summary>
        public static void PullCandidatesPostfix(Bill bill, Map map, ThingOwner destination, ref bool __result)
        {
            try
            {
                if (__result || bill == null || destination == null)
                {
                    return;
                }
                Thing bench = destination.Owner as Thing;
                if (bench == null || !bench.Spawned || bench.Map == null)
                {
                    return;
                }
                Map useMap = map ?? bench.Map;
                IntVec3 root = bench is Building building && bench.def != null && bench.def.hasInteractionCell
                    ? building.InteractionCell
                    : bench.Position;
                if (!SDS_CompatEndpoint.HasUsableBuildingEndpointWithin(useMap, root, bill.ingredientSearchRadius))
                {
                    return;
                }
                if (!SDS_CompatBillSupply.TrySupplyPreservedBill(bill, useMap, root, destination,
                        out List<Thing> made) || made == null || made.Count == 0)
                {
                    return;
                }
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("联合轻工家具拓展-UF Light Industries Furniture 兼容（全自动保真供料）失败（已忽略，打印机行为不受影响）", e);
            }
        }
    }

}
