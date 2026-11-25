using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 10 个投影 owner 取用原语的唯一桥：只把"投影 owner 上的原版调用"翻译成 SDS 语义，
    /// 不选端点、不算数量、不直接扣账。
    /// </summary>
    internal static class SDS_ProjectionOwnerBridge
    {
        private sealed class Entry
        {
            internal string Id;
            internal Type Type;
            internal string Method;
            internal Type[] Args;
            internal string Prefix;
            internal string Postfix;
            internal string Finalizer;
            internal int Priority;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { Id = "Patch_SDS_ProjectionTake", Type = typeof(ThingOwner), Method = nameof(ThingOwner.Take), Args = new[] { typeof(Thing), typeof(int) }, Prefix = nameof(Handlers.PrefixTake), Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_ProjectionTryDropCounted", Type = typeof(ThingOwner), Method = nameof(ThingOwner.TryDrop), Args = new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(int), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>), typeof(Predicate<IntVec3>) }, Prefix = nameof(Handlers.PrefixTryDropCounted), Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_ProjectionTryDropAll", Type = typeof(ThingOwner), Method = nameof(ThingOwner.TryDrop), Args = new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(bool) }, Prefix = nameof(Handlers.PrefixTryDropAll), Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_ProjectionTryTransfer", Type = typeof(ThingOwner), Method = nameof(ThingOwner.TryTransferToContainer), Args = new[] { typeof(Thing), typeof(ThingOwner), typeof(int), typeof(Thing).MakeByRefType(), typeof(bool) }, Prefix = nameof(Handlers.PrefixTryTransfer), Priority = Priority.Low },
            new Entry { Id = "Patch_SDS_EndpointTransferReturn", Type = typeof(ThingOwner), Method = nameof(ThingOwner.TryTransferToContainer), Args = new[] { typeof(Thing), typeof(ThingOwner), typeof(int), typeof(Thing).MakeByRefType(), typeof(bool) }, Prefix = nameof(Handlers.PrefixEndpointTransferReturn), Postfix = nameof(Handlers.PostfixEndpointTransferReturn), Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_ProjectionClear", Type = typeof(ThingOwner), Method = nameof(ThingOwner.Clear), Prefix = nameof(Handlers.PrefixViewOp), Finalizer = nameof(Handlers.FinalizerViewOp), Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_ProjectionClearAndDestroy", Type = typeof(ThingOwner), Method = nameof(ThingOwner.ClearAndDestroyContents), Prefix = nameof(Handlers.PrefixViewOp), Finalizer = nameof(Handlers.FinalizerViewOp), Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_ProjectionClearAndDestroyOrPassToWorld", Type = typeof(ThingOwner), Method = nameof(ThingOwner.ClearAndDestroyContentsOrPassToWorld), Prefix = nameof(Handlers.PrefixViewOp), Finalizer = nameof(Handlers.FinalizerViewOp), Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_ProjectionRemoveAll", Type = typeof(ThingOwner), Method = "RemoveAll", Args = new[] { typeof(Predicate<Thing>) }, Prefix = nameof(Handlers.PrefixViewOp), Finalizer = nameof(Handlers.FinalizerViewOp), Priority = Priority.Normal },
            new Entry { Id = "Patch_SDS_ProjectionRemoveAllGeneric", Type = typeof(ThingOwner<Thing>), Method = "RemoveAll", Args = new[] { typeof(Predicate<Thing>) }, Prefix = nameof(Handlers.PrefixViewOpGeneric), Finalizer = nameof(Handlers.FinalizerViewOpGeneric), Priority = Priority.Normal }
        };

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }
            SDS_CompatPatchReport report = new SDS_CompatPatchReport("投影 owner 桥", "本体补丁");
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
                    HarmonyMethod prefix = Method(entry.Prefix, entry.Priority);
                    HarmonyMethod postfix = Method(entry.Postfix, entry.Priority);
                    HarmonyMethod finalizer = Method(entry.Finalizer, Priority.Normal);
                    if (prefix == null && postfix == null && finalizer == null)
                    {
                        report.Missing(entry.Id, "未指定处理器");
                        continue;
                    }
                    harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
                    report.Applied(entry.Id);
                }
                catch (Exception e)
                {
                    report.Missing(entry.Id, "应用异常 " + e.GetType().Name);
                    SDS_Diag.ErrorOnce("投影 owner 桥挂点应用失败（已跳过）：" + entry.Id, e);
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
            if (entry.Type == null)
            {
                return null;
            }
            return entry.Args == null || entry.Args.Length == 0
                ? AccessTools.Method(entry.Type, entry.Method)
                : AccessTools.Method(entry.Type, entry.Method, entry.Args);
        }

        private static HarmonyMethod Method(string name, int priority)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            MethodInfo method = AccessTools.Method(typeof(Handlers), name);
            return method == null ? null : new HarmonyMethod(method) { priority = priority };
        }

        internal static class Handlers
        {
            public static bool PrefixTake(ThingOwner __instance, Thing thing, int count, ref Thing __result)
            {
                SDS_ProjectionOwner owner = __instance as SDS_ProjectionOwner;
                if (owner == null)
                {
                    return true;
                }
                __result = owner.TakeFromProjection(thing, count);
                if (__result == null)
                {
                    SDS_Diag.Fail("投影/取出失败", SDS_Diag.Ctx(
                        thing != null ? thing.MapHeld : null, null, thing, null, "count=" + count));
                }
                return false;
            }

            public static bool PrefixTryDropCounted(ThingOwner __instance, Thing thing, IntVec3 dropLoc,
                Map map, ThingPlaceMode mode, int count, ref Thing resultingThing,
                Action<Thing, int> placedAction, Predicate<IntVec3> nearPlaceValidator, ref bool __result)
            {
                SDS_ProjectionOwner owner = __instance as SDS_ProjectionOwner;
                if (owner == null)
                {
                    return true;
                }
                __result = false;
                resultingThing = null;
                if (thing == null || !owner.ContainsReference(thing) || count <= 0)
                {
                    SDS_Diag.Fail("投影/落地参数无效", SDS_Diag.Ctx(
                        map, null, thing, null, "count=" + count));
                    return false;
                }
                if (count > thing.stackCount)
                {
                    count = thing.stackCount;
                }
                if (!owner.TryTakeForTransfer(thing, count, out Thing taken))
                {
                    SDS_Diag.Fail("投影/落地扣账失败", SDS_Diag.Ctx(
                        map, null, thing, null, "count=" + count));
                    return false;
                }
                if (GenDrop.TryDropSpawn(taken, dropLoc, map, mode, out Thing placed, placedAction,
                        nearPlaceValidator))
                {
                    resultingThing = placed;
                    __result = true;
                    return false;
                }
                SDS_Diag.Fail("投影/落地失败", SDS_Diag.Ctx(map, null, taken, null, "cell=" + dropLoc));
                SDS_TakeService.ReturnToGridOrDrop(taken, map, dropLoc);
                return false;
            }

            public static bool PrefixTryDropAll(ThingOwner __instance, Thing thing, IntVec3 dropLoc,
                Map map, ThingPlaceMode mode, ref Thing lastResultingThing,
                Action<Thing, int> placedAction, Predicate<IntVec3> nearPlaceValidator,
                bool playDropSound, ref bool __result)
            {
                SDS_ProjectionOwner owner = __instance as SDS_ProjectionOwner;
                if (owner == null)
                {
                    return true;
                }
                __result = false;
                lastResultingThing = null;
                if (thing == null || !owner.ContainsReference(thing))
                {
                    SDS_Diag.Fail("投影/整堆落地参数无效", SDS_Diag.Ctx(map, null, thing));
                    return false;
                }
                if (!owner.TryTakeForTransfer(thing, thing.stackCount, out Thing taken))
                {
                    SDS_Diag.Fail("投影/整堆落地扣账失败", SDS_Diag.Ctx(map, null, thing));
                    return false;
                }
                if (GenDrop.TryDropSpawn(taken, dropLoc, map, mode, out Thing placed, placedAction,
                        nearPlaceValidator, playDropSound))
                {
                    lastResultingThing = placed;
                    __result = true;
                    return false;
                }
                SDS_Diag.Fail("投影/整堆落地失败", SDS_Diag.Ctx(map, null, taken, null,
                    "cell=" + dropLoc));
                SDS_TakeService.ReturnToGridOrDrop(taken, map, dropLoc);
                return false;
            }

            public static bool PrefixTryTransfer(ThingOwner __instance, Thing item,
                ThingOwner otherContainer, int count, ref Thing resultingTransferredItem,
                bool canMergeWithExistingStacks, ref int __result)
            {
                SDS_ProjectionOwner owner = __instance as SDS_ProjectionOwner;
                if (owner == null)
                {
                    return true;
                }
                __result = 0;
                resultingTransferredItem = null;
                if (item == null || otherContainer == null || count <= 0
                    || !owner.ContainsReference(item))
                {
                    SDS_Diag.Fail("投影/转移参数无效", SDS_Diag.Ctx(
                        item != null ? item.MapHeld : null, null, item, null, "count=" + count));
                    return false;
                }
                if (otherContainer == __instance)
                {
                    resultingTransferredItem = item;
                    __result = item.stackCount;
                    return false;
                }
                int take = Math.Min(count, item.stackCount);
                if (take <= 0 || otherContainer.GetCountCanAccept(item, canMergeWithExistingStacks) < take)
                {
                    SDS_Diag.Fail("投影/目标容量不足", SDS_Diag.Ctx(
                        item.MapHeld, null, item, null, "take=" + take));
                    return false;
                }
                if (!owner.TryTakeForTransfer(item, take, out Thing piece))
                {
                    SDS_Diag.Fail("投影/转移扣账失败", SDS_Diag.Ctx(
                        item.MapHeld, null, item, null, "take=" + take));
                    return false;
                }
                if (otherContainer.TryAdd(piece, canMergeWithExistingStacks))
                {
                    resultingTransferredItem = piece;
                    __result = piece != null && !piece.Destroyed ? piece.stackCount : 0;
                    return false;
                }
                IThingHolder ownerHolder = __instance.Owner;
                Map map = ThingOwnerUtility.GetRootMap(ownerHolder);
                IntVec3 cell = ThingOwnerUtility.GetRootPosition(ownerHolder);
                SDS_Diag.Fail("投影/目标拒收", SDS_Diag.Ctx(map, null, piece, null, "take=" + take));
                SDS_TakeService.ReturnToGridOrDrop(piece, map, cell);
                return false;
            }

            public static void PrefixEndpointTransferReturn(Thing item, int count, out int __state)
            {
                __state = item != null ? Math.Min(item.stackCount, count) : 0;
            }

            public static void PostfixEndpointTransferReturn(ThingOwner otherContainer, int __state,
                ref Thing resultingTransferredItem, ref int __result)
            {
                if (__result == 0 && __state > 0 && otherContainer is SDS_EndpointBuffer
                    && resultingTransferredItem != null && resultingTransferredItem.Destroyed)
                {
                    __result = __state;
                }
            }

            public static void PrefixViewOp(ThingOwner __instance)
            {
                SDS_ProjectionViewOps.Begin(__instance);
            }

            public static Exception FinalizerViewOp(ThingOwner __instance, Exception __exception)
            {
                return SDS_ProjectionViewOps.End(__instance, __exception);
            }

            public static void PrefixViewOpGeneric(ThingOwner<Thing> __instance)
            {
                SDS_ProjectionViewOps.Begin(__instance);
            }

            public static Exception FinalizerViewOpGeneric(ThingOwner<Thing> __instance,
                Exception __exception)
            {
                return SDS_ProjectionViewOps.End(__instance, __exception);
            }
        }
    }
}
