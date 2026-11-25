using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 第三方 Job 预约前的端点物化（唯一实现）：
    /// 镜像目标 → 最近可达端点访问格 → 取指定件数 → 复制 comp 字段 → 落点 → 改写 target。
    /// 只经 SDS_API；不改 W54 / Core；失败一律回账或不接管。
    /// 第二个同形态适配器只传描述符，不复制本实现。
    /// </summary>
    internal static class SDS_CompatMirrorSeam
    {
        internal static bool TryMaterializeForReservation(
            JobDriver driver, TargetIndex ind, int count, Type compType, FieldInfo[] fields, string logLabel)
        {
            Thing real = null;
            Map map = null;
            IntVec3 cell = IntVec3.Invalid;
            bool swapped = false;
            try
            {
                if (driver == null || compType == null || fields == null || fields.Length == 0)
                {
                    return false;
                }
                Pawn pawn = driver.pawn;
                Job job = driver.job;
                if (pawn == null || job == null || !pawn.Spawned || pawn.Map == null || pawn.Dead)
                {
                    return false;
                }
                Thing mirror = job.GetTarget(ind).Thing;
                if (mirror == null || mirror.Destroyed || !SDS_API.IsMirror(mirror))
                {
                    return false;
                }
                map = pawn.Map;
                if (!SDS_API.IsGridUsable(map) || !SDS_API.HasAnyEndpoint(map))
                {
                    return false;
                }
                if (!SDS_API.TryGetWithdrawCell(pawn, out cell, out _))
                {
                    return false;
                }
                if (!SDS_API.TryWithdrawFromMirror(mirror, Math.Max(1, count), SDS_WithdrawMode.Hand,
                        SDS_ApiCaller.FromPawn(pawn), out real, out _) || real == null || real.Destroyed)
                {
                    real = null;
                    return false;
                }
                if (!CopyFields(mirror, real, compType, fields))
                {
                    // 任一侧没有目标 comp：结构性失配，记一次；不制造语义不完整的真身。
                    SDS_Diag.ErrorOnce(logLabel + "：目标 comp 缺失（已回账，不接管）", null);
                    SDS_API.TryReturnVirtual(real, map, cell);
                    real = null;
                    return false;
                }
                if (!GenPlace.TryPlaceThing(real, cell, map, ThingPlaceMode.Near, out Thing landed)
                    || landed == null || landed.Destroyed)
                {
                    SDS_API.TryReturnVirtual(real, map, cell);
                    real = null;
                    return false;
                }
                job.SetTarget(ind, new LocalTargetInfo(landed));
                swapped = true;
                return true;
            }
            catch (Exception e)
            {
                if (!swapped && real != null && !real.Destroyed)
                {
                    SDS_API.TryReturnVirtual(real, map, cell);
                }
                SDS_Diag.ErrorOnce(logLabel + "失败（已忽略，原行为不受影响）", e);
                return false;
            }
        }

        /// <summary>把镜像上指定 comp 的公开字段复制到交付对象；任一侧缺失 comp 则返回 false。</summary>
        private static bool CopyFields(Thing from, Thing to, Type compType, FieldInfo[] fields)
        {
            ThingComp source = FindComp(from, compType);
            ThingComp target = FindComp(to, compType);
            if (source == null || target == null)
            {
                return false;
            }
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field != null)
                {
                    field.SetValue(target, field.GetValue(source));
                }
            }
            return true;
        }

        private static ThingComp FindComp(Thing thing, Type compType)
        {
            ThingWithComps withComps = thing as ThingWithComps;
            if (withComps == null || withComps.AllComps == null)
            {
                return null;
            }
            for (int i = 0; i < withComps.AllComps.Count; i++)
            {
                ThingComp comp = withComps.AllComps[i];
                if (comp != null && compType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }
            return null;
        }
    }

    /// <summary>适配器共用的反射 / 挂点工具。只做类型解析和 Harmony 应用，不含业务规则。</summary>
    internal static class SDS_CompatTools
    {
        public static MethodInfo Method(Type type, string name, params Type[] args)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            try
            {
                return args == null || args.Length == 0
                    ? AccessTools.Method(type, name)
                    : AccessTools.Method(type, name, args);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static PropertyInfo Property(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            try
            {
                return AccessTools.Property(type, name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static MethodInfo PropertyGetter(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            try
            {
                return AccessTools.PropertyGetter(type, name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static FieldInfo Field(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            try
            {
                return AccessTools.Field(type, name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 第三方适配挂点的唯一应用入口：目标缺失 → Missing，应用异常 → Missing + ErrorOnce，成功 → Applied。
        /// priority 只影响执行顺序；回滚 / 清理类 finalizer 一律 Low。
        /// </summary>
        public static bool TryPatch(Harmony harmony, SDS_CompatPatchReport report, Type ownerType,
            string hook, MethodInfo target, string prefixName, string postfixName, string missingReason,
            string finalizerName = null, int priority = Priority.Low, int finalizerPriority = Priority.Low)
        {
            if (target == null)
            {
                report?.Missing(hook, missingReason);
                return false;
            }
            if (harmony == null || ownerType == null)
            {
                report?.Missing(hook, "Harmony / 适配器类型为空");
                return false;
            }
            try
            {
                HarmonyMethod prefix = string.IsNullOrEmpty(prefixName)
                    ? null
                    : new HarmonyMethod(AccessTools.Method(ownerType, prefixName)) { priority = priority };
                HarmonyMethod postfix = string.IsNullOrEmpty(postfixName)
                    ? null
                    : new HarmonyMethod(AccessTools.Method(ownerType, postfixName)) { priority = priority };
                HarmonyMethod finalizer = string.IsNullOrEmpty(finalizerName)
                    ? null
                    : new HarmonyMethod(AccessTools.Method(ownerType, finalizerName)) { priority = finalizerPriority };
                if (prefix == null && postfix == null && finalizer == null)
                {
                    report?.Missing(hook, "未指定 Prefix/Postfix/Finalizer");
                    return false;
                }
                harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
                report?.Applied(hook);
                return true;
            }
            catch (Exception e)
            {
                report?.Missing(hook, "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce("第三方兼容挂点应用失败（已跳过）：" + hook, e);
                return false;
            }
        }

        public static bool GetBool(PropertyInfo prop, object instance)
        {
            if (prop == null || instance == null)
            {
                return false;
            }
            try
            {
                object value = prop.GetValue(instance, null);
                return value is bool b && b;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>建筑端点判定：适配器只问「本图有没有可用端口」，不自己定义端口集合。</summary>
    internal static class SDS_CompatEndpoint
    {
        /// <summary>本图是否有任一可用端口（建筑口或带芯片小人 / 机械体）。</summary>
        public static bool HasAnyEndpoint(Map map)
        {
            return SDS_API.HasAnyEndpoint(map);
        }

        /// <summary>原版 DoBill 的半径语义：建筑端点在半径内且从工作台可达。</summary>
        public static bool HasUsableBuildingEndpointWithin(Map map, IntVec3 root, float radius)
        {
            return SDS_API.HasUsableBuildingEndpointWithin(map, root, radius);
        }
    }

    /// <summary>适配器共用的库存查询 / 取出原语：只调用 SDS_API，不直接碰账本。</summary>
    internal static class SDS_CompatStock
    {
        /// <summary>
        /// def 级可用量（虚拟 + 保真合计，统一由 SDS_API / Core 给出）。
        /// 调用方不得再叠加保真量，否则会重复计数。
        /// </summary>
        public static long CountAvailable(ThingDef def)
        {
            return SDS_API.GetAvailableCountForDef(def);
        }

        /// <summary>按 def 取出虚拟库存；不足则不取，返回 false（不留下半截）。</summary>
        public static bool TryWithdrawVirtualByDef(ThingDef def, int count, Map map, out List<Thing> made)
        {
            return SDS_API.TryWithdrawVirtualByDef(def, count, map,
                SDS_ApiCaller.FromFaction(Faction.OfPlayerSilentFail), out made, out _);
        }

        /// <summary>按 def 取出保真库存；只接受精确真身计划，不足则不取。</summary>
        public static bool TryWithdrawPreservedByDef(ThingDef def, int count, Map map, out List<Thing> made)
        {
            return SDS_API.TryWithdrawPreservedByDef(def, count, map,
                SDS_ApiCaller.FromFaction(Faction.OfPlayerSilentFail), out made, out _);
        }

        public static void ReturnPreserved(List<Thing> things, Map map)
        {
            if (things == null)
            {
                return;
            }
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                SDS_API.TryReturnPreserved(thing, map, IntVec3.Invalid);
            }
        }
    }

    /// <summary>
    /// 账单配料适配器（唯一 caller：联合轻工家具拓展全自动打印机）。
    /// 只做保真通道：选择仍复用原版 TryFindBestBillIngredientsInSet，不自己排序 / 不自己算数量。
    /// </summary>
    internal static class SDS_CompatBillSupply
    {
        private static MethodInfo findInSet;
        private static bool findInSetResolved;

        private static MethodInfo FindInSet
        {
            get
            {
                if (!findInSetResolved)
                {
                    findInSetResolved = true;
                    findInSet = SDS_CompatTools.Method(typeof(WorkGiver_DoBill),
                        "TryFindBestBillIngredientsInSet",
                        new[]
                        {
                            typeof(List<Thing>), typeof(Bill), typeof(List<ThingCount>),
                            typeof(IntVec3), typeof(bool), typeof(List<IngredientCount>)
                        });
                }
                return findInSet;
            }
        }

        public static bool TrySupplyPreservedBill(Bill bill, Map map, IntVec3 rootCell, ThingOwner destination,
            out List<Thing> made)
        {
            made = null;
            if (bill == null || bill.recipe == null || bill.recipe.ingredients == null
                || bill.recipe.ingredients.Count == 0 || map == null)
            {
                return false;
            }
            if (!SDS_API.IsGridAvailable())
            {
                return false;
            }
            MethodInfo selector = FindInSet;
            if (selector == null)
            {
                return false;
            }

            List<Thing> candidates = new List<Thing>();
            HashSet<Thing> seen = new HashSet<Thing>();
            if (destination != null)
            {
                foreach (Thing thing in destination)
                {
                    if (thing != null && !thing.Destroyed && seen.Add(thing))
                    {
                        candidates.Add(thing);
                    }
                }
            }

            List<Thing> preserved = new List<Thing>();
            SDS_API.AppendPreservedThings(preserved);
            for (int i = 0; i < preserved.Count; i++)
            {
                Thing thing = preserved[i];
                // 保真真身不做 SplitOff：只接受单件，避免把带内部状态的堆拆成空壳。
                if (thing == null || thing.Destroyed || thing.stackCount != 1
                    || !SDS_API.IsLivePreservedThing(thing) || !seen.Add(thing))
                {
                    continue;
                }
                if (!SDS_API.IsBillIngredientAllowed(null, thing, thing.def, bill, null, null, null))
                {
                    continue;
                }
                if (!SDS_API.TryGetStockKey(thing, out SDS_StockKey key)
                    || key == null || SDS_API.IsStockForbidden(key)
                    || SDS_API.GetAvailableCount(key) <= 0L)
                {
                    continue;
                }
                candidates.Add(thing);
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            List<ThingCount> chosen = new List<ThingCount>();
            bool ok;
            try
            {
                ok = (bool)selector.Invoke(null, new object[] { candidates, bill, chosen, rootCell, false, null });
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("第三方配料选择调用原版算法失败（已忽略）", e);
                return false;
            }
            if (!ok || chosen.Count == 0)
            {
                return false;
            }

            List<ThingCount> preservedPlan = new List<ThingCount>();
            for (int i = 0; i < chosen.Count; i++)
            {
                ThingCount count = chosen[i];
                Thing thing = count.Thing;
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (destination != null && destination.Contains(thing))
                {
                    continue;
                }
                if (SDS_API.IsLivePreservedThing(thing))
                {
                    preservedPlan.Add(count);
                }
            }
            if (preservedPlan.Count == 0)
            {
                return false;
            }

            int requested = SumCounts(preservedPlan);
            if (!SDS_API.TryWithdrawPreservedExact(preservedPlan, SDS_WithdrawMode.Container,
                    SDS_ApiCaller.FromFaction(Faction.OfPlayerSilentFail), out List<Thing> part, out _)
                || part == null || part.Count == 0)
            {
                return false;
            }
            List<Thing> madePreserved = new List<Thing>(part);
            if (SumUnits(part) < requested)
            {
                SDS_CompatStock.ReturnPreserved(madePreserved, map);
                return false;
            }
            if (destination != null)
            {
                for (int i = 0; i < madePreserved.Count; i++)
                {
                    Thing thing = madePreserved[i];
                    if (thing == null || thing.Destroyed || destination.Contains(thing))
                    {
                        continue;
                    }
                    if (!destination.TryAddOrTransfer(thing, false))
                    {
                        RemoveFromDestination(destination, madePreserved);
                        SDS_CompatStock.ReturnPreserved(madePreserved, map);
                        return false;
                    }
                }
            }
            made = madePreserved;
            return made.Count > 0;
        }

        private static int SumCounts(List<ThingCount> counts)
        {
            int total = 0;
            for (int i = 0; i < counts.Count; i++)
            {
                total += Math.Max(0, counts[i].Count);
            }
            return total;
        }

        private static int SumUnits(List<Thing> things)
        {
            int total = 0;
            if (things == null)
            {
                return total;
            }
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed)
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }

        private static void RemoveFromDestination(ThingOwner destination, List<Thing> things)
        {
            if (destination == null || things == null)
            {
                return;
            }
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed && destination.Contains(thing))
                {
                    try
                    {
                        destination.Remove(thing);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
    }
}
