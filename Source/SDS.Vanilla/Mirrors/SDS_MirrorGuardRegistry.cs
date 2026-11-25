using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    internal enum SDS_GuardScope
    {
        Mirror,
        Vault,
        Both
    }

    internal enum SDS_GuardEscape
    {
        None,
        DeSpawnAllowed,
        AnyFastPath
    }

    internal enum SDS_GuardHandler
    {
        GenericPrefixFalseInstance,
        GenericPrefixFalseStatic,
        DamageResult,
        VaultAbsorb,
        SelectablePostfix,
        RoomContainsPostfix,
        RoomFilterList,
        RoomFilterEnumerable,
        RoomRecount,
        NullThingPostfix
    }

    /// <summary>
    /// 19 个拦截型守卫的唯一注册表：只做"入口 → 处理器"的声明与 Harmony 应用；
    /// 规则本身仍是 SDS_MirrorRegistry.IsMirror / Building_SDS_Vault.IsVault 两处。
    /// </summary>
    internal static class SDS_MirrorGuardRegistry
    {
        private sealed class Entry
        {
            internal string Id;
            internal Type Type;
            internal string TypeName;
            internal string Method;
            internal Type[] Args;
            internal bool PropertyGetter;
            internal SDS_GuardScope Scope;
            internal SDS_GuardEscape Escape;
            internal SDS_GuardHandler Handler;
            internal int Priority;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { Id = "Patch_SDS_MirrorNoDamage", Type = typeof(Thing), Method = nameof(Thing.TakeDamage), Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.DamageResult, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_VaultPreApplyDamage", Type = typeof(Thing), Method = nameof(Thing.PreApplyDamage), Scope = SDS_GuardScope.Vault, Handler = SDS_GuardHandler.VaultAbsorb, Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_MirrorNoFire", Type = typeof(FireUtility), Method = nameof(FireUtility.TryAttachFire), Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.GenericPrefixFalseStatic, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNoDeSpawn", Type = typeof(Thing), Method = nameof(Thing.DeSpawn), Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.DeSpawnAllowed, Handler = SDS_GuardHandler.GenericPrefixFalseInstance, Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_MirrorNoDeSpawnWithComps", Type = typeof(ThingWithComps), Method = nameof(ThingWithComps.DeSpawn), Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.DeSpawnAllowed, Handler = SDS_GuardHandler.GenericPrefixFalseInstance, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNoRealtimeDraw", Type = typeof(Thing), Method = nameof(Thing.DynamicDrawPhaseAt), Args = new[] { typeof(DrawPhase), typeof(Vector3), typeof(bool) }, Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.AnyFastPath, Handler = SDS_GuardHandler.GenericPrefixFalseInstance, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNoMeshPrint", Type = typeof(Thing), Method = nameof(Thing.Print), Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.AnyFastPath, Handler = SDS_GuardHandler.GenericPrefixFalseInstance, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNoMeshPrintWithComps", Type = typeof(ThingWithComps), Method = nameof(ThingWithComps.Print), Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.AnyFastPath, Handler = SDS_GuardHandler.GenericPrefixFalseInstance, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNoGuiOverlay", Type = typeof(Thing), Method = nameof(Thing.DrawGUIOverlay), Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.AnyFastPath, Handler = SDS_GuardHandler.GenericPrefixFalseInstance, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNoGuiOverlayWithComps", Type = typeof(ThingWithComps), Method = nameof(ThingWithComps.DrawGUIOverlay), Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.AnyFastPath, Handler = SDS_GuardHandler.GenericPrefixFalseInstance, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNotSelectable", Type = typeof(ThingSelectionUtility), Method = nameof(ThingSelectionUtility.SelectableByMapClick), Scope = SDS_GuardScope.Mirror, Handler = SDS_GuardHandler.SelectablePostfix, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_MirrorNotSelectableDirect", Type = typeof(Selector), Method = nameof(Selector.Select), Scope = SDS_GuardScope.Mirror, Escape = SDS_GuardEscape.AnyFastPath, Handler = SDS_GuardHandler.GenericPrefixFalseStatic, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_RoomIgnoresHiddenGrid", Type = typeof(Room), Method = nameof(Room.ContainedAndAdjacentThings), PropertyGetter = true, Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.RoomFilterList, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_RoomContainsThingIgnoresHiddenGrid", Type = typeof(Room), Method = nameof(Room.ContainsThing), Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.RoomContainsPostfix, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_RoomContainedThingsIgnoresHiddenGrid", Type = typeof(Room), Method = nameof(Room.ContainedThings), Args = new[] { typeof(ThingDef) }, Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.RoomFilterEnumerable, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_RoomThingCountIgnoresHiddenGrid", Type = typeof(Room), Method = nameof(Room.ThingCount), Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.RoomRecount, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_ObserverIgnoresHiddenGrid", Type = typeof(PawnObserver), Method = "PossibleToObserve", Args = new[] { typeof(Thing) }, Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.GenericPrefixFalseStatic, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_TantrumIgnoresHiddenGrid", Type = typeof(TantrumMentalStateUtility), Method = nameof(TantrumMentalStateUtility.CanSmash), Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.GenericPrefixFalseStatic, Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_FireSpreeIgnoresHiddenGrid", TypeName = "RimWorld.JobGiver_FireStartingSpree", Method = "TryFindRandomIgniteTarget", Scope = SDS_GuardScope.Both, Handler = SDS_GuardHandler.NullThingPostfix, Priority = Priority.Low }
        };

        private static readonly Dictionary<MethodBase, Entry> ByMethod = new Dictionary<MethodBase, Entry>();

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }
            SDS_CompatPatchReport report = new SDS_CompatPatchReport("镜像守卫注册表", "本体补丁");
            ByMethod.Clear();
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                MethodBase target = Resolve(entry);
                if (target == null)
                {
                    report.Missing(entry.Id, "目标方法不存在");
                    continue;
                }
                try
                {
                    HarmonyMethod prefix = PrefixFor(entry);
                    HarmonyMethod postfix = PostfixFor(entry);
                    if (prefix == null && postfix == null)
                    {
                        report.Missing(entry.Id, "未指定处理器");
                        continue;
                    }
                    harmony.Patch(target, prefix: prefix, postfix: postfix);
                    ByMethod[target] = entry;
                    report.Applied(entry.Id);
                }
                catch (Exception e)
                {
                    report.Missing(entry.Id, "应用异常 " + e.GetType().Name);
                    SDS_Diag.ErrorOnce("镜像守卫注册表挂点应用失败（已跳过）：" + entry.Id, e);
                }
            }
            LogReport(report);
        }

        private static void LogReport(SDS_CompatPatchReport report)
        {
            int total = report.AppliedCount + report.failed.Count;
            if (total == 0)
            {
                return;
            }
            if (!report.HasFailures)
            {
                Log.Message("[超维存储] 【" + report.category + "】" + report.modName
                    + "：A/B = " + report.AppliedCount + "/" + total + " 个挂点。");
                return;
            }
            Log.Warning("[超维存储] 【" + report.category + "】" + report.modName + "：A/B = "
                + report.AppliedCount + "/" + total + " 个挂点；失败 " + report.failed.Count
                + " 个：" + string.Join("，", report.failed.ToArray()) + "。");
        }

        private static MethodBase Resolve(Entry entry)
        {
            Type type = entry.Type;
            if (type == null && !string.IsNullOrEmpty(entry.TypeName))
            {
                type = AccessTools.TypeByName(entry.TypeName);
            }
            if (type == null)
            {
                return null;
            }
            if (entry.PropertyGetter)
            {
                return AccessTools.PropertyGetter(type, entry.Method);
            }
            return entry.Args == null || entry.Args.Length == 0
                ? AccessTools.Method(type, entry.Method)
                : AccessTools.Method(type, entry.Method, entry.Args);
        }

        private static HarmonyMethod PrefixFor(Entry entry)
        {
            string name;
            switch (entry.Handler)
            {
                case SDS_GuardHandler.GenericPrefixFalseInstance:
                    name = nameof(Handlers.PrefixFalseInstance);
                    break;
                case SDS_GuardHandler.GenericPrefixFalseStatic:
                    name = nameof(Handlers.PrefixFalseStatic);
                    break;
                case SDS_GuardHandler.DamageResult:
                    name = nameof(Handlers.PrefixDamageResult);
                    break;
                case SDS_GuardHandler.VaultAbsorb:
                    name = nameof(Handlers.PrefixVaultAbsorb);
                    break;
                case SDS_GuardHandler.RoomRecount:
                    name = nameof(Handlers.PrefixRoomRecount);
                    break;
                default:
                    return null;
            }
            return new HarmonyMethod(AccessTools.Method(typeof(Handlers), name)) { priority = entry.Priority };
        }

        private static HarmonyMethod PostfixFor(Entry entry)
        {
            string name;
            switch (entry.Handler)
            {
                case SDS_GuardHandler.SelectablePostfix:
                    name = nameof(Handlers.PostfixSelectable);
                    break;
                case SDS_GuardHandler.RoomContainsPostfix:
                    name = nameof(Handlers.PostfixRoomContains);
                    break;
                case SDS_GuardHandler.RoomFilterList:
                    name = nameof(Handlers.PostfixRoomFilterList);
                    break;
                case SDS_GuardHandler.RoomFilterEnumerable:
                    name = nameof(Handlers.PostfixRoomFilterEnumerable);
                    break;
                case SDS_GuardHandler.NullThingPostfix:
                    name = nameof(Handlers.PostfixNullThing);
                    break;
                default:
                    return null;
            }
            return new HarmonyMethod(AccessTools.Method(typeof(Handlers), name)) { priority = entry.Priority };
        }

        private static bool Matches(Thing thing, SDS_GuardScope scope)
        {
            if (thing == null)
            {
                return false;
            }
            bool mirror = SDS_MirrorRegistry.IsMirror(thing);
            bool vault = Building_SDS_Vault.IsVault(thing);
            if (scope == SDS_GuardScope.Mirror)
            {
                return mirror;
            }
            if (scope == SDS_GuardScope.Vault)
            {
                return vault;
            }
            return mirror || vault;
        }

        private static Thing ThingArg(object instance, object[] args)
        {
            Thing thing = instance as Thing;
            if (thing != null)
            {
                return thing;
            }
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    Thing arg = args[i] as Thing;
                    if (arg != null)
                    {
                        return arg;
                    }
                }
            }
            return null;
        }

        internal static class Handlers
        {
            private static readonly HashSet<Thing> recountSeen = new HashSet<Thing>();

            private static bool ShouldBlock(Entry entry, Thing thing)
            {
                if (entry.Escape == SDS_GuardEscape.AnyFastPath && !SDS_MirrorRegistry.Any)
                {
                    return false;
                }
                if (entry.Escape == SDS_GuardEscape.DeSpawnAllowed
                    && (SDS_MirrorRegistry.AllowDeSpawn || Current.ProgramState != ProgramState.Playing))
                {
                    return false;
                }
                return Matches(thing, entry.Scope);
            }

            public static bool PrefixFalseInstance(object __instance, MethodBase __originalMethod)
            {
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return true;
                }
                return !ShouldBlock(entry, __instance as Thing);
            }

            public static bool PrefixFalseStatic(object[] __args, MethodBase __originalMethod)
            {
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return true;
                }
                return !ShouldBlock(entry, ThingArg(null, __args));
            }

            public static bool PrefixDamageResult(Thing __instance, ref DamageWorker.DamageResult __result,
                MethodBase __originalMethod)
            {
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return true;
                }
                if (!Matches(__instance, entry.Scope))
                {
                    return true;
                }
                __result = new DamageWorker.DamageResult();
                return false;
            }

            public static bool PrefixVaultAbsorb(Thing __instance, ref bool absorbed,
                MethodBase __originalMethod)
            {
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return true;
                }
                if (!Matches(__instance, entry.Scope))
                {
                    return true;
                }
                absorbed = true;
                return false;
            }

            public static void PostfixSelectable(Thing t, ref bool __result, MethodBase __originalMethod)
            {
                if (!__result || !SDS_MirrorRegistry.Any)
                {
                    return;
                }
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return;
                }
                if (Matches(t, entry.Scope))
                {
                    __result = false;
                }
            }

            public static void PostfixRoomContains(Room __instance, ThingDef def, ref bool __result,
                MethodBase __originalMethod)
            {
                if (!__result || __instance == null || def == null || !SDS_MirrorRegistry.Any)
                {
                    return;
                }
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return;
                }
                try
                {
                    List<Region> regions = __instance.Regions;
                    for (int i = 0; i < regions.Count; i++)
                    {
                        List<Thing> things = regions[i].ListerThings.ThingsOfDef(def);
                        for (int j = 0; j < things.Count; j++)
                        {
                            Thing thing = things[j];
                            if (thing != null && !Matches(thing, entry.Scope))
                            {
                                return;
                            }
                        }
                    }
                    __result = false;
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("房间包含判定过滤隐藏库存失败（已忽略，原版行为不受影响）", e);
                }
            }

            public static void PostfixRoomFilterList(List<Thing> __result, MethodBase __originalMethod)
            {
                if (__result == null || __result.Count == 0)
                {
                    return;
                }
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return;
                }
                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    if (Matches(__result[i], entry.Scope))
                    {
                        __result.RemoveAt(i);
                    }
                }
            }

            public static void PostfixRoomFilterEnumerable(ref IEnumerable<Thing> __result,
                MethodBase __originalMethod)
            {
                if (__result == null || !SDS_MirrorRegistry.Any)
                {
                    return;
                }
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return;
                }
                try
                {
                    List<Thing> filtered = new List<Thing>();
                    foreach (Thing thing in __result)
                    {
                        if (thing == null || Matches(thing, entry.Scope))
                        {
                            continue;
                        }
                        filtered.Add(thing);
                    }
                    __result = filtered;
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("房间内容枚举过滤隐藏库存失败（已忽略，原版行为不受影响）", e);
                }
            }

            public static bool PrefixRoomRecount(Room __instance, ThingDef def, ref int __result,
                MethodBase __originalMethod)
            {
                if (__instance == null || def == null || !SDS_MirrorRegistry.Any)
                {
                    return true;
                }
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return true;
                }
                try
                {
                    int num = 0;
                    recountSeen.Clear();
                    List<Region> regions = __instance.Regions;
                    for (int i = 0; i < regions.Count; i++)
                    {
                        List<Thing> things = regions[i].ListerThings.ThingsOfDef(def);
                        for (int j = 0; j < things.Count; j++)
                        {
                            Thing thing = things[j];
                            if (thing == null || Matches(thing, entry.Scope))
                            {
                                continue;
                            }
                            if (recountSeen.Add(thing))
                            {
                                num += thing.stackCount;
                            }
                        }
                    }
                    recountSeen.Clear();
                    __result = num;
                    return false;
                }
                catch (Exception e)
                {
                    recountSeen.Clear();
                    SDS_Diag.ErrorOnce("房间数量统计过滤隐藏库存失败（已忽略，原版行为不受影响）", e);
                    return true;
                }
            }

            public static void PostfixNullThing(ref Thing __result, MethodBase __originalMethod)
            {
                if (__result == null)
                {
                    return;
                }
                if (__originalMethod == null || !ByMethod.TryGetValue(__originalMethod, out Entry entry))
                {
                    return;
                }
                if (Matches(__result, entry.Scope))
                {
                    __result = null;
                }
            }
        }
    }
}
