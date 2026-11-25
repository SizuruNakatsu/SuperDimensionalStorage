using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{

    // ============================ 14 个特性补丁 ============================

    /// <summary>小人 / 机械体作为端点时，把投影挂到自己的 GetChildHolders。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetChildHolders))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PawnProjectionExposure
    {
        public static void Postfix(Pawn __instance, List<IThingHolder> outChildren)
        {
            try
            {
                if (__instance == null || outChildren == null || !__instance.Spawned)
                {
                    return;
                }
                if (SDS_GameComponent_Grid.Instance == null
                    || !SDS_ImplantUtility.IsPortOperational(__instance))
                {
                    return;
                }
                SDS_ProjectionHolder projection = SDS_ProjectionService.ProjectionFor(__instance);
                if (projection != null)
                {
                    outChildren.Add(projection);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("小人投影挂载失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>Map 级 holder 换成 SDS_MapLevelHolder 后，按原 Pawn 口径恢复财富过滤。</summary>
    [HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.WealthItemsFilter))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_WealthItemsFilterProjection
    {
        public static bool Prefix(IThingHolder x, ref bool __result)
        {
            if (x is SDS_MapLevelHolder level)
            {
                __result = level.PassesWealthFilter;
                return false;
            }
            if (x is SDS_ProjectionHolder)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 搬运候选唯一门（W52 / W55）：镜像按流出设置放行 / 排除；
    /// 最高优先级模式下，原版判定「已在最优储存」的可存实物也重新进入候选
    /// （超维空间视为最优储存）。
    /// </summary>
    [HarmonyPatch(typeof(ListerHaulables), "ShouldBeHaulable")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorNotHaulable
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            // W52：开启优先级流出时不再强制排除；由原版 IsInValidBestStorage
            // 只在存在严格更高优先级且允许进入的目的地时保留 true。
            if (__result && SDS_MirrorRegistry.IsMirror(t)
                && !SDS_Config.AllowPriorityOutflow)
            {
                __result = false;
                return;
            }
            // W55：最高优先级模式下，储存区 / 储存建筑里的可存实物即使原版判定
            // 「已在最优储存」也进入候选；owner 型容器内容物不 spawn，不受影响。
            if (!__result && SDS_DepositService.IsGridAlwaysBetter(t))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ListerMergeables), "ShouldBeMergeable")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorNotMergeable
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (__result && SDS_MirrorRegistry.IsMirror(t))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorAbsorb
    {
        public static bool Prefix(Thing __instance, Thing other, bool respectStackLimit, ref bool __result)
        {
            if (SDS_MirrorAbsorb.TryHandle(__instance, other, respectStackLimit, out bool result))
            {
                __result = result;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.TryAbsorbStack))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorAbsorbWithComps
    {
        public static bool Prefix(ThingWithComps __instance, Thing other, bool respectStackLimit,
            ref bool __result)
        {
            if (SDS_MirrorAbsorb.TryHandle(__instance, other, respectStackLimit, out bool result))
            {
                __result = result;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorTakeOnSplitOff
    {
        public static bool Prefix(Thing __instance, int count, ref Thing __result)
        {
            if (!SDS_MirrorRegistry.IsMirror(__instance))
            {
                return true;
            }
            try
            {
                if (SDS_TakeService.TryTake(__instance, count, out Thing taken))
                {
                    __result = taken;
                    return false;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.Fail("取货/SplitOff异常", SDS_Diag.Ctx(
                    __instance.Map, null, __instance, null, "count=" + count), e);
            }
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry),
        new[] { typeof(Thing) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorCarry
    {
        public static bool Prefix(Pawn_CarryTracker __instance, Thing item, ref bool __result)
        {
            if (!SDS_MirrorRegistry.IsMirror(item))
            {
                return true;
            }
            try
            {
                __result = SDS_MirrorCarry.TakeIntoCarry(__instance, item, item.stackCount, true) > 0;
            }
            catch (Exception e)
            {
                SDS_Diag.Fail("取货/入手异常", SDS_Diag.Ctx(
                    __instance != null && __instance.pawn != null ? __instance.pawn.Map : null,
                    __instance != null ? __instance.pawn : null, item, null), e);
                __result = false;
            }
            return false;
        }
    }

    /// <summary>TryStartCarry 的带数量重载返回 int，必须单独拦。</summary>
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing), typeof(int), typeof(bool) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorCarryCounted
    {
        public static bool Prefix(Pawn_CarryTracker __instance, Thing item, int count, bool reserve,
            ref int __result)
        {
            if (!SDS_MirrorRegistry.IsMirror(item))
            {
                return true;
            }
            try
            {
                __result = SDS_MirrorCarry.TakeIntoCarry(__instance, item, count, reserve);
            }
            catch (Exception e)
            {
                SDS_Diag.Fail("取货/入手异常", SDS_Diag.Ctx(
                    __instance != null && __instance.pawn != null ? __instance.pawn.Map : null,
                    __instance != null ? __instance.pawn : null, item, null,
                    "count=" + count), e);
                __result = 0;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(MapDeiniter), nameof(MapDeiniter.Deinit))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MapDeinitAllowMirrorDespawn
    {
        public static void Prefix(Map map)
        {
            SDS_MirrorRegistry.AllowDeSpawn = true;
            try
            {
                SDS_MirrorService.EvacuateForStructureChange(map, "map-deinit");
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像/结构撤离异常（已忽略，继续卸载地图）", e);
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            SDS_MirrorRegistry.AllowDeSpawn = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    internal static class Patch_SDS_MirrorSpawnSetup
    {
        public static void Postfix(Thing __instance)
        {
            if (!SDS_MirrorRegistry.IsMirror(__instance))
            {
                return;
            }
            try
            {
                SDS_MirrorService.OnMirrorSpawned(__instance);
            }
            catch (Exception e)
            {
                SDS_Diag.Fail("镜像/落点初始化异常", SDS_Diag.Ctx(
                    __instance.Map, null, __instance, null), e);
            }
        }
    }

    /// <summary>代理镜像不是实体：不得登记进原版可研究缓存（规则 W36；真实物品路径不变）。</summary>
    [HarmonyPatch(typeof(StudyManager), nameof(StudyManager.UpdateStudiableCache))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorNotStudiable
    {
        // RULE:W36
        public static bool Prefix(Thing thing)
        {
            return !SDS_MirrorRegistry.IsMirror(thing);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorDestroyConsume
    {
        public static bool Prefix(Thing __instance)
        {
            return SDS_MirrorConsume.Allow(__instance);
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.Destroy))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorDestroyConsumeWithComps
    {
        public static bool Prefix(ThingWithComps __instance)
        {
            return SDS_MirrorConsume.Allow(__instance);
        }
    }
}
