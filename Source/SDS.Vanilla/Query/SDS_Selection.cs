using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// A 组 · 通用选择层：让原版「找东西」的三族查询都能看见超维空间。
    ///
    /// 1. ListerThings —— 无需补丁：物理镜像本身就以真实 def spawn 在隐藏金库；
    /// 2. GenClosest 四适配器 —— 见 SDS_Search.cs；
    /// 3. PlayerItemAccessibilityUtility —— 本文件补齐（保真通道 + 数量口径）。
    /// </summary>
    internal static class SDS_Selection
    {
        /// <summary>这张图此刻能不能通过端点取用超维空间。</summary>
        public static bool GridAccessibleFrom(Map map)
        {
            return map != null && !map.Disposed && SDS_Exposure.MapExposesGrid(map);
        }

        /// <summary>世界任意一张图是否有可用端点（用于没有 map 参数的全局查询）。</summary>
        public static bool GridAccessibleAnywhere()
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return false;
            }
            for (int i = 0; i < maps.Count; i++)
            {
                if (GridAccessibleFrom(maps[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 该类型在超维空间里的物理总量：虚拟按 def 汇总，保真按外壳 def 计件。
        /// 不扣预留——这些查询问的是「有没有」。
        /// </summary>
        public static long TotalAccessibleCount(ThingDef def)
        {
            if (def == null || !SDS_Rules.Available || SDS_Core.Ledger == null)
            {
                return 0L;
            }
            return SDS_Exposure.LedgerTotalForDef(def);
        }

        /// <summary>`Accessible` 口径的真实总量：统一由 SDS_Exposure.AccessibleTotal 投影。</summary>
        public static long AccessibleCount(ThingDef thing)
        {
            return SDS_Exposure.AccessibleTotal(thing);
        }

        // ---------- 派生量：拆解 / 收获 / 屠宰 / 熔炼 ----------

        private static readonly Dictionary<ThingDef, long> derivedCounts = new Dictionary<ThingDef, long>();
        private static readonly HashSet<ThingDef> derivedMakeable = new HashSet<ThingDef>();
        private static int derivedCacheFrame = -1;

        internal static void Reset()
        {
            derivedCounts.Clear();
            derivedMakeable.Clear();
            derivedCacheFrame = -1;
            accessibleThingsField = null;
            accessibleThingsFieldChecked = false;
        }

        private static void EnsureDerivedCounts()
        {
            int frame = RealTime.frameCount;
            if (derivedCacheFrame == frame)
            {
                return;
            }
            derivedCacheFrame = frame;
            derivedCounts.Clear();
            derivedMakeable.Clear();
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available)
            {
                return;
            }
            List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                if (entry == null || entry.key == null || entry.count <= 0 || entry.key.preserved)
                {
                    continue;
                }
                Thing probe = SDS_OutputUtility.CachedDisplayThing(entry.key);
                if (probe != null)
                {
                    AppendDerivedProducts(probe, entry.count);
                }
            }
            List<Thing> preserved = new List<Thing>();
            ledger.AppendPreservedThings(preserved);
            for (int i = 0; i < preserved.Count; i++)
            {
                Thing thing = preserved[i];
                if (thing != null && !thing.Destroyed)
                {
                    AppendDerivedProducts(thing.GetInnerIfMinified(), 1L);
                }
            }
        }

        /// <summary>复刻 CacheAccessibleThings 的派生分支；multiplier 是该品类在账本里的件数。</summary>
        private static void AppendDerivedProducts(Thing thing, long multiplier)
        {
            if (thing == null || thing.def == null || multiplier <= 0L)
            {
                return;
            }
            if (GenLeaving.CanBuildingLeaveResources(thing, DestroyMode.Deconstruct))
            {
                List<ThingDefCountClass> costs = thing.CostListAdjusted();
                for (int i = 0; i < costs.Count; i++)
                {
                    int count = Mathf.RoundToInt((float)costs[i].count
                        * thing.def.resourcesFractionWhenDeconstructed);
                    if (count > 0)
                    {
                        AddDerived(costs[i].thingDef, count * multiplier);
                    }
                }
            }
            if (thing is Plant plant && plant.def.plant != null && plant.def.plant.Harvestable)
            {
                int count = Mathf.RoundToInt(plant.def.plant.harvestYield
                    * Find.Storyteller.difficulty.cropYieldFactor);
                if (count > 0)
                {
                    AddDerived(plant.def.plant.harvestedThingDef, count * multiplier);
                }
            }
            if (!thing.def.butcherProducts.NullOrEmpty())
            {
                for (int i = 0; i < thing.def.butcherProducts.Count; i++)
                {
                    ThingDefCountClass product = thing.def.butcherProducts[i];
                    if (product != null)
                    {
                        AddDerived(product.thingDef, product.count * multiplier);
                    }
                }
            }
            if (thing is Pawn pawn)
            {
                if (pawn.RaceProps.meatDef != null)
                {
                    int count = Mathf.RoundToInt(pawn.GetStatValue(StatDefOf.MeatAmount));
                    if (count > 0)
                    {
                        AddDerived(pawn.RaceProps.meatDef, count * multiplier);
                    }
                }
                if (pawn.RaceProps.leatherDef != null)
                {
                    int count = GenMath.RoundRandom(pawn.GetStatValue(StatDefOf.LeatherAmount));
                    if (count > 0)
                    {
                        AddDerived(pawn.RaceProps.leatherDef, count * multiplier);
                    }
                }
                if (!pawn.RaceProps.Humanlike)
                {
                    PawnKindLifeStage stage = pawn.ageTracker.CurKindLifeStage;
                    if (stage != null && stage.butcherBodyPart != null)
                    {
                        AddDerived(stage.butcherBodyPart.thing, 1L * multiplier);
                    }
                }
            }
            if (thing.def.smeltable)
            {
                List<ThingDefCountClass> costs = thing.CostListAdjusted();
                for (int i = 0; i < costs.Count; i++)
                {
                    if (costs[i].thingDef.intricate)
                    {
                        continue;
                    }
                    int count = Mathf.RoundToInt((float)costs[i].count * 0.25f);
                    if (count > 0)
                    {
                        AddDerived(costs[i].thingDef, count * multiplier);
                    }
                }
                if (!thing.def.smeltProducts.NullOrEmpty())
                {
                    for (int i = 0; i < thing.def.smeltProducts.Count; i++)
                    {
                        ThingDefCountClass product = thing.def.smeltProducts[i];
                        if (product != null)
                        {
                            AddDerived(product.thingDef, product.count * multiplier);
                        }
                    }
                }
            }
        }

        private static void AddDerived(ThingDef def, long count)
        {
            if (def == null || count <= 0L)
            {
                return;
            }
            derivedCounts.TryGetValue(def, out long current);
            derivedCounts[def] = current + count;
            derivedMakeable.Add(def);
        }

        /// <summary>`PossiblyAccessible` 的派生量部分。</summary>
        public static long PossiblyAccessibleDerivedCount(ThingDef def)
        {
            if (def == null)
            {
                return 0L;
            }
            EnsureDerivedCounts();
            return derivedCounts.TryGetValue(def, out long count) ? count : 0L;
        }

        /// <summary>`PlayerCanMake`：能否从超维空间里的实物拆解 / 收获 / 屠宰 / 熔炼出该产物。</summary>
        public static bool CanMakeDerived(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            EnsureDerivedCounts();
            return derivedMakeable.Contains(def);
        }

        /// <summary>`PlayerItemAccessibilityUtility.cachedAccessibleThings` 的反射句柄。</summary>
        private static FieldInfo accessibleThingsField;
        private static bool accessibleThingsFieldChecked;

        private static List<Thing> GetCachedAccessibleThings()
        {
            if (!accessibleThingsFieldChecked)
            {
                accessibleThingsFieldChecked = true;
                accessibleThingsField = AccessTools.Field(typeof(PlayerItemAccessibilityUtility),
                    "cachedAccessibleThings");
            }
            return accessibleThingsField != null
                ? accessibleThingsField.GetValue(null) as List<Thing>
                : null;
        }

        /// <summary>原版已统计的 holder 树实物中，排除镜像后的真实数量。</summary>
        public static long NonMirrorAccessibleCount(ThingDef def)
        {
            if (def == null)
            {
                return 0L;
            }
            List<Thing> things = GetCachedAccessibleThings();
            if (things == null)
            {
                return 0L;
            }
            long total = 0L;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed && thing.def == def
                    && !SDS_MirrorRegistry.IsMirror(thing))
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }

        /// <summary>
        /// 重算「玩家 / 任务奖励是否已有 count 个」：地图实物（去掉镜像与注入的账本副本）
        /// + 账本 + 商队 + 世界站点。只在存在可用端点时由补丁接管。
        /// </summary>
        public static long PlayerOwnedCount(ThingDef def)
        {
            long total = TotalAccessibleCount(def);
            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    if (map == null || map.resourceCounter == null)
                    {
                        continue;
                    }
                    if (def.CountAsResource)
                    {
                        bool exposes = SDS_Exposure.MapExposesGrid(map);
                        // 必须无条件刷新：本图可能「曾经暴露、后来端点失效」，资源条仍缓存旧注入量。
                        SDS_Exposure.ForceCounterRefresh(map);
                        long mapCount = map.resourceCounter.GetCount(def);
                        mapCount -= SDS_Exposure.InjectedOnCounter(map, def, exposes);
                        total += mapCount > 0L ? mapCount : 0L;
                    }
                    else if (map.listerThings != null)
                    {
                        List<Thing> things = map.listerThings.ThingsOfDef(def);
                        for (int j = 0; j < things.Count; j++)
                        {
                            Thing thing = things[j];
                            if (thing != null && !thing.Destroyed && !SDS_MirrorRegistry.IsMirror(thing))
                            {
                                total += thing.stackCount;
                            }
                        }
                    }
                }
            }
            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan caravan = caravans[i];
                if (caravan == null || !caravan.IsPlayerControlled)
                {
                    continue;
                }
                List<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan);
                for (int j = 0; j < items.Count; j++)
                {
                    Thing item = items[j];
                    if (item != null && !item.Destroyed && item.def == def)
                    {
                        total += item.stackCount;
                    }
                }
            }
            List<Site> sites = Find.WorldObjects.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                Site site = sites[i];
                if (site == null)
                {
                    continue;
                }
                for (int j = 0; j < site.parts.Count; j++)
                {
                    SitePart part = site.parts[j];
                    if (part == null || part.things == null)
                    {
                        continue;
                    }
                    for (int k = 0; k < part.things.Count; k++)
                    {
                        Thing thing = part.things[k];
                        if (thing != null && !thing.Destroyed && thing.def == def)
                        {
                            total += thing.stackCount;
                        }
                    }
                }
                DefeatAllEnemiesQuestComp component = site.GetComponent<DefeatAllEnemiesQuestComp>();
                if (component == null || component.rewards == null)
                {
                    continue;
                }
                ThingOwner rewards = component.rewards;
                for (int k = 0; k < rewards.Count; k++)
                {
                    Thing thing = rewards[k];
                    if (thing != null && !thing.Destroyed && thing.def == def)
                    {
                        total += thing.stackCount;
                    }
                }
            }
            return total;
        }

        /// <summary>保真通道里是否有通过该筛选的真身。</summary>
        public static bool HasPreservedMatching(ThingFilter filter)
        {
            if (filter == null || !SDS_Rules.Available || SDS_Core.Ledger == null)
            {
                return false;
            }
            List<Thing> all = new List<Thing>();
            SDS_Core.Ledger.AppendPreservedThings(all);
            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                if (thing != null && !thing.Destroyed && filter.Allows(thing))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>`PlayerItemAccessibilityUtility.Accessible`：补保真通道 + 账本口径。</summary>
    [HarmonyPatch(typeof(PlayerItemAccessibilityUtility),
        nameof(PlayerItemAccessibilityUtility.Accessible))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_Accessible
    {
        public static void Postfix(ThingDef thing, int count, Map map, ref bool __result)
        {
            try
            {
                if (__result || thing == null || count <= 0 || map == null)
                {
                    return;
                }
                if (!SDS_Selection.GridAccessibleFrom(map))
                {
                    return;
                }
                __result = SDS_Selection.AccessibleCount(thing) >= count;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("玩家可及性补全失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>`PossiblyAccessible`：补派生量（× 账本件数）+ 保真。</summary>
    [HarmonyPatch(typeof(PlayerItemAccessibilityUtility),
        nameof(PlayerItemAccessibilityUtility.PossiblyAccessible))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PossiblyAccessible
    {
        public static void Postfix(ThingDef thing, int count, Map map, ref bool __result)
        {
            try
            {
                if (__result || thing == null || count <= 0 || map == null)
                {
                    return;
                }
                if (!SDS_Selection.GridAccessibleFrom(map))
                {
                    return;
                }
                __result = SDS_Selection.AccessibleCount(thing)
                    + SDS_Selection.PossiblyAccessibleDerivedCount(thing) >= count;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("玩家潜在可及性补全失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>`PlayerCanMake`：原版只看地图实物派生，这里补超维空间里的派生。</summary>
    [HarmonyPatch(typeof(PlayerItemAccessibilityUtility),
        nameof(PlayerItemAccessibilityUtility.PlayerCanMake))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PlayerCanMake
    {
        public static void Postfix(ThingDef thing, Map map, ref bool __result)
        {
            try
            {
                if (__result || thing == null || map == null)
                {
                    return;
                }
                if (!SDS_Selection.GridAccessibleFrom(map))
                {
                    return;
                }
                __result = SDS_Selection.CanMakeDerived(thing);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("玩家可制造判定补全失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>
    /// `PlayerOrQuestRewardHas(ThingDef,int)`：整体重算持有量，避免多图重复与镜像 1 件。
    /// </summary>
    [HarmonyPatch(typeof(PlayerItemAccessibilityUtility),
        nameof(PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas),
        new[] { typeof(ThingDef), typeof(int) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PlayerHasDef
    {
        public static bool Prefix(ThingDef thingDef, int count, ref bool __result)
        {
            try
            {
                if (thingDef == null || count <= 0)
                {
                    return true;
                }
                if (!SDS_Selection.GridAccessibleAnywhere())
                {
                    return true;
                }
                __result = SDS_Selection.PlayerOwnedCount(thingDef) >= count;
                return false;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("玩家持有量重算失败（已放行原版）", e);
                return true;
            }
        }
    }

    /// <summary>`PlayerOrQuestRewardHas(ThingFilter)`：只补保真通道里的真身。</summary>
    [HarmonyPatch(typeof(PlayerItemAccessibilityUtility),
        nameof(PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas),
        new[] { typeof(ThingFilter) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PlayerHasFilter
    {
        public static void Postfix(ThingFilter thingFilter, ref bool __result)
        {
            try
            {
                if (__result || thingFilter == null || !SDS_Selection.GridAccessibleAnywhere())
                {
                    return;
                }
                __result = SDS_Selection.HasPreservedMatching(thingFilter);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("玩家持有筛选补全失败（已忽略，原版行为不受影响）", e);
            }
        }
    }
}
