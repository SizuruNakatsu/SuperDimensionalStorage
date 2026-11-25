using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{

    // ============================ 编成窗口：注入 / 校验 / 清理 ============================

    /// <summary>重算清单前，先把上一轮残留的展示物清掉（避免重复堆叠）。</summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), "CalculateAndRecacheTransferables")]
    internal static class Patch_SDS_CaravanRecacheReset
    {
        public static void Prefix(Dialog_FormCaravan __instance)
        {
            try
            {
                SDS_CaravanVirtual.CleanupFakes(__instance != null ? __instance.transferables : null);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("远征队展示物清理失败（已忽略）", e);
            }
        }
    }

    /// <summary>编成清单建好后注入超维空间品类。前提：非改编（reform）且本图有可用端点。</summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), "AddItemsToTransferables")]
    internal static class Patch_SDS_CaravanAddVirtual
    {
        public static void Postfix(Dialog_FormCaravan __instance, Map ___map, bool ___reform)
        {
            try
            {
                if (__instance == null || ___reform)
                {
                    return;
                }
                // 没有端点时不注入，编成清单与原版完全一致（携带超维物资不做科技门控）。
                if (!SDS_Exposure.MapExposesGrid(___map))
                {
                    return;
                }
                SDS_CaravanVirtual.Inject(__instance.transferables);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("远征队超维空间品类注入失败（已忽略，原版编成不受影响）", e);
            }
        }
    }

    /// <summary>点出发时：清单里用到了超维空间品类却没端点 / 库存不够，就拦下并提示。</summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), "TryFormAndSendCaravan")]
    internal static class Patch_SDS_CaravanValidate
    {
        public static bool Prefix(Dialog_FormCaravan __instance, Map ___map, ref bool __result)
        {
            try
            {
                if (__instance == null
                    || !SDS_CaravanVirtual.AnyVirtualNeeded(__instance.transferables))
                {
                    return true;
                }
                if (!SDS_Exposure.MapExposesGrid(___map))
                {
                    Messages.Message("SDS.Caravan.NeedsOutputPoint".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    __result = false;
                    return false;
                }
                // 出发瞬间再对一次数量快照 —— 另一支远行队 / 交易可能已经先扣过账。
                if (!SDS_CaravanVirtual.ValidateQuantities(__instance.transferables, out string failLabel))
                {
                    Messages.Message("SDS.Caravan.NotEnoughStock".Translate(failLabel ?? "?"),
                        MessageTypeDefOf.RejectInput, false);
                    __result = false;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("远征队端点校验失败（已忽略，放行原版出发）", e);
                return true;
            }
        }
    }

    /// <summary>调试用的"瞬间编成"不支持超维空间品类（无法物化 + 就地装箱）。</summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), "DebugTryFormCaravanInstantly")]
    internal static class Patch_SDS_CaravanBlockInstant
    {
        public static bool Prefix(Dialog_FormCaravan __instance, ref bool __result)
        {
            try
            {
                if (__instance == null
                    || !SDS_CaravanVirtual.AnyVirtualNeeded(__instance.transferables))
                {
                    return true;
                }
                Messages.Message("SDS.Caravan.InstantUnsupported".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                __result = false;
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("远征队瞬间编成拦截失败（已忽略）", e);
                return true;
            }
        }
    }

    /// <summary>关闭编成窗口时清掉残留展示物（正常出发的那次展示物已在派出时剥掉）。</summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), "PostClose")]
    internal static class Patch_SDS_CaravanPostCloseCleanup
    {
        public static void Postfix(Dialog_FormCaravan __instance)
        {
            try
            {
                if (__instance != null && !__instance.choosingRoute)
                {
                    SDS_CaravanVirtual.CleanupFakes(__instance.transferables);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("远征队收尾清理失败（已忽略）", e);
            }
        }
    }

    // ============================ 派出：把超维空间额度物化成真物 ============================

    /// <summary>
    /// 远征队正式组建（LordMaker.MakeNewLord）时，把清单里的超维空间展示物换成真物，
    /// 放进编成清单供原版派人搬运。这是本 Mod 唯一改动"物品来源"的地方，其余全交原版。
    /// </summary>
    [HarmonyPatch(typeof(LordMaker), "MakeNewLord")]
    internal static class Patch_SDS_CaravanMaterialize
    {
        public static void Postfix(LordJob lordJob, Map map, IEnumerable<Pawn> startingPawns, Lord __result)
        {
            try
            {
                LordJob_FormAndSendCaravan caravanJob = lordJob as LordJob_FormAndSendCaravan;
                if (caravanJob != null)
                {
                    SDS_CaravanVirtual.MaterializeForCaravan(__result, caravanJob, map, startingPawns);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("远征队超维空间物资物化失败（已忽略）", e);
            }
        }
    }
}
