using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ============================ 8 个镜像行为守卫 ============================

    /// <summary>镜像代表"已经在超维仓库里"：让原版"必须放在储存区里"的路径自然看到它。</summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsInAnyStorage))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorIsInStorage
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (!__result && SDS_MirrorRegistry.Any && SDS_MirrorRegistry.IsMirror(t))
            {
                __result = true;
            }
        }
    }

    /// <summary>IsInValidStorage：镜像不是"等待搬运的松散物"，而是已在超维仓库里。</summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsInValidStorage))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorIsInValidStorage
    {
        public static bool Prefix(Thing t, ref bool __result)
        {
            if (t == null || !SDS_MirrorRegistry.Any || !SDS_MirrorRegistry.IsMirror(t))
            {
                return true;
            }
            __result = true;
            return false;
        }
    }

    /// <summary>存储优先级取全局唯一来源，而不是走金库槽位得到的 Unstored。</summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.CurrentStoragePriorityOf))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorStoragePriority
    {
        public static bool Prefix(Thing t, bool forced, ref StoragePriority __result)
        {
            if (t == null || !SDS_MirrorRegistry.Any || !SDS_MirrorRegistry.IsMirror(t))
            {
                return true;
            }
            __result = SDS_VanillaState.CurrentStoragePriority;
            return false;
        }
    }

    /// <summary>
    /// W53：镜像的"当前存储目的地"代理（每图一个、不 spawn、不注册），是镜像存储事实的唯一来源：
    /// `CurrentHaulDestinationOf` 返回它 → `IsInAnyStorage` / `IsInValidStorage` 为真、
    /// `CurrentStoragePriorityOf` 取存档级优先级；W52 的「严格更高优先级才流出」据此成立。
    /// Accepts 恒 true（镜像代表仓库里的实物）；HaulDestinationEnabled=false。
    /// </summary>
    internal sealed class SDS_MirrorHaulDestination : IHaulDestination
    {
        private static readonly Dictionary<Map, SDS_MirrorHaulDestination> cache =
            new Dictionary<Map, SDS_MirrorHaulDestination>();

        internal static void Reset()
        {
            cache.Clear();
        }

        private readonly StorageSettings settings;

        private SDS_MirrorHaulDestination(Map map)
        {
            Map = map;
            // 优先级只在 GetStoreSettings 里按存档级来源同步；构造期写入是重复赋值。
            settings = new StorageSettings(this);
        }

        internal static SDS_MirrorHaulDestination For(Map map)
        {
            if (map == null)
            {
                return null;
            }
            if (cache.TryGetValue(map, out SDS_MirrorHaulDestination proxy) && proxy != null)
            {
                return proxy;
            }
            if (cache.Count > 32)
            {
                cache.Clear();
            }
            proxy = new SDS_MirrorHaulDestination(map);
            cache[map] = proxy;
            return proxy;
        }

        public IntVec3 Position
        {
            get { return Map.Center; }
        }

        public Map Map { get; }

        public bool HaulDestinationEnabled
        {
            get { return false; }
        }

        public bool Accepts(Thing t)
        {
            return t != null && !t.Destroyed;
        }

        public bool StorageTabVisible
        {
            get { return false; }
        }

        public StorageSettings GetStoreSettings()
        {
            StoragePriority target = SDS_VanillaState.CurrentStoragePriority;
            if (settings.Priority != target)
            {
                settings.Priority = target;
            }
            return settings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return null;
        }

        public void Notify_SettingsChanged()
        {
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.CurrentHaulDestinationOf))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorHaulDestination
    {
        public static void Postfix(Thing t, ref IHaulDestination __result)
        {
            try
            {
                if (t == null || !SDS_MirrorRegistry.Any || !SDS_MirrorRegistry.IsMirror(t))
                {
                    return;
                }
                SDS_MirrorHaulDestination proxy = SDS_MirrorHaulDestination.For(t.MapHeld);
                if (proxy != null)
                {
                    __result = proxy;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("镜像仓库代理解析失败（已忽略，保留原版目的地）", e);
            }
        }
    }

    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.PlayerSellableNow))]
    internal static class Patch_SDS_MirrorNotSellable
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (__result && SDS_MirrorRegistry.Any && SDS_MirrorRegistry.IsMirror(t))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.AllLaunchableThingsForTrade))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorNotLaunchable
    {
        public static void Postfix(ref IEnumerable<Thing> __result)
        {
            if (__result == null || !SDS_MirrorRegistry.Any)
            {
                return;
            }
            __result = WithoutMirrors(__result);
        }

        private static IEnumerable<Thing> WithoutMirrors(IEnumerable<Thing> source)
        {
            foreach (Thing thing in source)
            {
                if (!SDS_MirrorRegistry.IsMirror(thing))
                {
                    yield return thing;
                }
            }
        }
    }

    /// <summary>远行队编成 / 运输舱装载的原始清单里摘掉镜像（展示物另行注入）。</summary>
    [HarmonyPatch(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.AllReachableColonyItems))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_MirrorNotCaravanItem
    {
        public static void Postfix(List<Thing> __result)
        {
            if (__result == null || !SDS_MirrorRegistry.Any)
            {
                return;
            }
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                if (SDS_MirrorRegistry.IsMirror(__result[i]))
                {
                    __result.RemoveAt(i);
                }
            }
        }
    }
}
