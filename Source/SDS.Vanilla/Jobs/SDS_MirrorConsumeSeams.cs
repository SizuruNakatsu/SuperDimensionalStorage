using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ════════════════════════════════════════════════════════════════════════════════════════
    // W59：消费点物化（唯一实现）
    //
    // 默认交付形态改为 Hand 后，直接消费型 driver 会拿着“镜像目标”走到自己的消费入口
    // （穿 / 装备 / 使用 / 进食），这些入口不接受镜像（W54 / W18 / 衣物最终闸）。
    // 本文件在原版 driver 的 Notify_Starting 之前把镜像目标换成真身：
    //   1. 复用统一取货（TryTakeMirror）与端点访问点落点；
    //   2. 只处理镜像目标；非镜像零行为变化；
    //   3. 物化失败时不接管，由原版失败条件自然结束（绝不把镜像交给消费入口后继续跑）；
    //   4. 数量：Wear / Equip / UseItem = 1；非工具使用者 Ingest = max(1, job.count)。
    // 镜像预留的释放统一由 Patch_SDS_JobMirrorTargets 的 Postfix 按 Job 扫全部预留收口，
    // 这里不重复 Release。
    //
    // 不在这里改原版 Wear / AddEquipment / Destroy 的入口，也不改 W18 / W54 的守卫。
    // ════════════════════════════════════════════════════════════════════════════════════════

    internal static class SDS_MirrorConsumeSeam
    {
        /// <summary>
        /// 把 <paramref name="ind"/> 槽上的镜像目标换成真身；返回是否已接管。
        /// 非镜像 / 全局不可用 / 无端点 / 取不出 / 落不下 一律返回 false，目标保持原样。
        /// </summary>
        // RULE:W59
        internal static bool TryMaterialize(Pawn pawn, Job job, TargetIndex ind, int count)
        {
            if (pawn == null || job == null || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }
            // S18：非玩家派系不把隐藏库存物化成真身。
            if (SDS_AccessRules.BlocksNonPlayer(pawn))
            {
                return false;
            }
            Thing mirror = job.GetTarget(ind).Thing;
            if (mirror == null || mirror.Destroyed || !SDS_MirrorRegistry.IsMirror(mirror)
                || !SDS_Rules.Available)
            {
                return false;
            }
            if (!SDS_Rules.TryGetStockKey(mirror, out SDS_StockKey key)
                || key == null || key.preserved)
            {
                return false;
            }
            int want = SDS_Availability.ClampTakeToAvailable(
                Math.Min(Math.Max(1, count), mirror.stackCount),
                SDS_Availability.GetAvailableCount(key));
            if (want <= 0)
            {
                return false;
            }
            SDS_EndpointQuery query = default;
            query.ApplyHostPriority();
            query.op = SDS_EndpointOp.Withdraw;
            query.map = pawn.Map;
            query.actor = pawn;
            query.reference = pawn.Position;
            query.key = key;
            query.danger = pawn.NormalMaxDanger();
            query.requireReach = true;
            query.allowSelf = true;
            query.requireStock = true;
            SDS_EndpointDecision decision = SDS_Rules.SelectEndpoint(query);
            if (!decision.allowed || decision.port == null)
            {
                return false;
            }
            if (!SDS_TakeService.TryTake(mirror, want, out Thing real)
                || real == null || real.Destroyed)
            {
                return false;
            }
            IntVec3 cell = decision.port.AccessCell;
            if (!SDS_PlacementUtility.TryPlaceOrSpawn(real, cell, pawn.Map, out Thing landed)
                || landed == null)
            {
                // 落不下去也必须回账本（数量守恒），失败才交给落地兜底。
                SDS_Rules.ReturnThing(real, SDS_StockChannel.Virtual, pawn.Map, pawn,
                    SDS_ReturnFallback.Ground);
                return false;
            }
            job.SetTarget(ind, new LocalTargetInfo(landed));
            return true;
        }

        /// <summary>非工具使用者（动物 / 禁用桌餐的变异体）才会直接啃目标，其余走携带原语。</summary>
        internal static bool UsesDirectChew(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null)
            {
                return false;
            }
            bool toolUser = pawn.RaceProps.ToolUser
                && (!pawn.IsMutant || !pawn.mutant.Def.disableEatingAtTable);
            return !toolUser;
        }
    }

    /// <summary>W59：穿衣服前把镜像换成真身（1 件）。</summary>
    [HarmonyPatch(typeof(JobDriver_Wear), nameof(JobDriver_Wear.Notify_Starting))]
    internal static class Patch_SDS_WearConsumeSeam
    {
        public static void Prefix(JobDriver_Wear __instance)
        {
            try
            {
                SDS_MirrorConsumeSeam.TryMaterialize(__instance.pawn, __instance.job,
                    TargetIndex.A, 1);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像消费点物化失败（穿衣，已交回原版失败条件）", e);
            }
        }
    }

    /// <summary>W59：装备前把镜像换成真身（1 件）。</summary>
    [HarmonyPatch(typeof(JobDriver_Equip), nameof(JobDriver_Equip.Notify_Starting))]
    internal static class Patch_SDS_EquipConsumeSeam
    {
        public static void Prefix(JobDriver_Equip __instance)
        {
            try
            {
                SDS_MirrorConsumeSeam.TryMaterialize(__instance.pawn, __instance.job,
                    TargetIndex.A, 1);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像消费点物化失败（装备，已交回原版失败条件）", e);
            }
        }
    }

    /// <summary>W59：使用物品前把镜像换成真身（1 件；含研究台类子驱动）。</summary>
    [HarmonyPatch(typeof(JobDriver_UseItem), nameof(JobDriver_UseItem.Notify_Starting))]
    internal static class Patch_SDS_UseItemConsumeSeam
    {
        public static void Prefix(JobDriver_UseItem __instance)
        {
            try
            {
                SDS_MirrorConsumeSeam.TryMaterialize(__instance.pawn, __instance.job,
                    TargetIndex.A, 1);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像消费点物化失败（使用物品，已交回原版失败条件）", e);
            }
        }
    }

    /// <summary>W59：非工具使用者进食前把镜像换成真身（按本顿需求数量）。</summary>
    [HarmonyPatch(typeof(JobDriver_Ingest), nameof(JobDriver_Ingest.Notify_Starting))]
    internal static class Patch_SDS_IngestConsumeSeam
    {
        public static void Prefix(JobDriver_Ingest __instance)
        {
            try
            {
                if (!SDS_MirrorConsumeSeam.UsesDirectChew(__instance.pawn))
                {
                    return;
                }
                int count = __instance.job != null ? Math.Max(1, __instance.job.count) : 1;
                SDS_MirrorConsumeSeam.TryMaterialize(__instance.pawn, __instance.job,
                    TargetIndex.A, count);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像消费点物化失败（进食，已交回原版失败条件）", e);
            }
        }
    }
}
