using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// VC载具框架 VersatranZeroOne（packageId = VersatranZeroOne.serval）适配。
    ///
    /// 目标只做一件事：车辆货舱“装载”时能正常取用超维库存（虚拟 + 保真）。
    ///  - 读：RefreshItems 后把镜像行上限改成 available；追加保真真身行（只读引用）。
    ///  - 写：ExecuteInstantLoad 前把超维行从原版循环摘掉，虚拟走 TryWithdraw、
    ///        保真走 TryWithdrawPreservedExact；真身交给车辆自己的入舱方法。
    ///  - 守门：CanThingBeStoredInCargo 拒绝镜像 / 存活保真真身，防止原版路径直接吞掉表示。
    ///
    /// 适配器不新增公开面、不新增存档字段、不碰 Core / Vanilla 实现；
    /// 虚拟行沿用镜像、保真行沿用真身，都是框架已有表示，不另造第二套库存。
    /// </summary>
    internal static class SDS_ModCompat_Versatran
    {
        private const string AdapterName = "VC载具框架 VersatranZeroOne";
        private const string PackageId = "VersatranZeroOne.serval";
        private const string MarkerTypeName = "VersatranZeroOne.Building_VersatranChassis";
        private const string WindowTypeName = "VersatranZeroOne.Window_VersatranCargo";
        private const string CompTypeName = "VersatranZeroOne.CompVersatranTransporter";
        private const string GroupTypeName = "VersatranZeroOne.CargoItemGroup";

        public static void TryApply(Harmony harmony)
        {
            bool found = false;
            try
            {
                if (harmony == null)
                {
                    return;
                }

                found = AccessTools.TypeByName(MarkerTypeName) != null;
                if (!found)
                {
                    SDS_CompatReportHub.Register(AdapterName, PackageId, false, null);
                    return;
                }

                Type window = AccessTools.TypeByName(WindowTypeName);
                Type comp = AccessTools.TypeByName(CompTypeName);
                SDS_CompatPatchReport report = new SDS_CompatPatchReport(AdapterName);

                bool loadPatched = SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_Versatran),
                    "Window_VersatranCargo.ExecuteInstantLoad（超维库存装载）",
                    SDS_CompatTools.Method(window, "ExecuteInstantLoad"),
                    nameof(LoadPrefix), nameof(LoadPostfix),
                    "找不到 Window_VersatranCargo.ExecuteInstantLoad");

                SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_Versatran),
                    "CompVersatranTransporter.CanThingBeStoredInCargo（镜像 / 保真守门）",
                    SDS_CompatTools.Method(comp, "CanThingBeStoredInCargo", typeof(Thing)),
                    nameof(CanStorePrefix), null,
                    "找不到 CompVersatranTransporter.CanThingBeStoredInCargo(Thing)");

                if (loadPatched)
                {
                    SDS_CompatTools.TryPatch(harmony, report, typeof(SDS_ModCompat_Versatran),
                        "Window_VersatranCargo.RefreshItems（装载列表投影）",
                        SDS_CompatTools.Method(window, "RefreshItems"),
                        null, nameof(RefreshPostfix),
                        "找不到 Window_VersatranCargo.RefreshItems");
                }

                SDS_CompatReportHub.Register(AdapterName, PackageId, true, report);
            }
            catch (Exception e)
            {
                SDS_CompatReportHub.Register(AdapterName, PackageId, found, null, null,
                    "应用异常 " + e.GetType().Name);
                SDS_Diag.ErrorOnce(AdapterName + " 兼容应用失败（已跳过，本体行为不受影响）", e);
            }
        }

        /// <summary>
        /// 装载前：把超维行从原版循环摘掉；虚拟 / 保真分别走五轴取货，再交车辆入舱。
        /// 原版循环继续处理真实地图物品（镜像 / 保真真身已被移除，且另有守门）。
        /// </summary>
        public static bool LoadPrefix(object __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (__instance == null)
                {
                    return true;
                }

                IList groups = GetGroups(__instance);
                if (groups == null)
                {
                    return true;
                }

                Thing vehicle = GetMember(__instance, "vehicle") as Thing;
                object comp = GetMember(__instance, "CargoComp");
                bool usable = vehicle != null && comp != null && vehicle.Spawned && vehicle.Map != null
                    && vehicle.Faction == Faction.OfPlayerSilentFail
                    && SDS_API.IsGridUsable(vehicle.Map);

                if (usable)
                {
                    SDS_ApiCaller caller = SDS_ApiCaller.FromFaction(Faction.OfPlayerSilentFail, vehicle);
                    List<object> copy = Snapshot(groups);
                    for (int i = 0; i < copy.Count; i++)
                    {
                        object group = copy[i];
                        Thing any = GetMember(group, "AnyThing") as Thing;
                        bool isMirror = SDS_API.IsMirror(any);
                        bool isPreserved = !isMirror && SDS_API.IsLivePreservedThing(any);
                        if (!isMirror && !isPreserved)
                        {
                            continue;
                        }

                        // 超维行绝不能留给原版：镜像会被 SplitOff、保真真身会被容器直接拿走。
                        groups.Remove(group);

                        int count = IntMember(group, "CountToTransfer");
                        if (count <= 0)
                        {
                            continue;
                        }

                        if (!SDS_API.TryGetStockKey(any, out SDS_StockKey key)
                            || key == null || SDS_API.IsStockForbidden(key))
                        {
                            continue;
                        }

                        List<Thing> made = null;
                        if (isPreserved)
                        {
                            if (!key.preserved)
                            {
                                continue;
                            }
                            long preservedAvailable = SDS_API.GetAvailableCount(key);
                            if (preservedAvailable <= 0L)
                            {
                                continue;
                            }
                            int preservedQty = SDS_API.ClampTakeToAvailable(count,
                                preservedAvailable);
                            if (preservedQty <= 0)
                            {
                                continue;
                            }
                            List<ThingCount> plan = BuildPreservedPlan(key, preservedQty);
                            if (plan.Count == 0)
                            {
                                continue;
                            }
                            if (!SDS_API.TryWithdrawPreservedExact(plan, SDS_WithdrawMode.Container,
                                    caller, out made, out _))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            SDS_ApiStockInfo info = SDS_API.ReadStock(key);
                            if (info.result != SDS_ApiResultCode.Ok || info.forbidden || info.available <= 0)
                            {
                                continue;
                            }
                            int qty = SDS_API.ClampTakeToAvailable(count, info.available);
                            if (qty <= 0)
                            {
                                continue;
                            }
                            if (!SDS_API.TryWithdraw(key, qty, SDS_WithdrawMode.Container,
                                    caller, out made, out _))
                            {
                                continue;
                            }
                        }

                        if (made == null || made.Count == 0)
                        {
                            continue;
                        }

                        for (int j = 0; j < made.Count; j++)
                        {
                            Thing thing = made[j];
                            if (thing == null || thing.Destroyed)
                            {
                                continue;
                            }
                            if (!AddToCargo(comp, thing))
                            {
                                ReturnToGridOrPreserved(thing, key, vehicle);
                            }
                        }

                        SetMember(group, "CountToTransfer", 0);
                        SetMember(group, "EditBuffer", "0");
                        __state = true;
                    }

                    if (__state)
                    {
                        InvokeVoid(comp, "Notify_MassChanged");
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容（超维库存装载）失败（已忽略，原版行为不受影响）", e);
            }
            finally
            {
                // 任何情况下都不让镜像 / 保真行进入原版 ExecuteInstantLoad。
                RemoveStockRows(GetGroups(__instance));
            }
            return true;
        }

        /// <summary>原版循环结束后再刷新：让刚取出的数量 / 剩余可用量立刻反映到窗口。</summary>
        public static void LoadPostfix(object __instance, bool __state)
        {
            if (!__state || __instance == null)
            {
                return;
            }
            try
            {
                InvokeVoid(__instance, "NotifyCargoContentsChanged");
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容（装载后刷新）失败（已忽略）", e);
            }
        }

        /// <summary>
        /// 读侧投影：镜像行上限 = available；保真真身行追加到装载列表。
        /// 只改车辆的 UI 读模型，不写账、不建第二套数量。
        /// </summary>
        public static void RefreshPostfix(object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                IList groups = GetGroups(__instance);
                if (groups == null)
                {
                    return;
                }

                Thing vehicle = GetMember(__instance, "vehicle") as Thing;
                bool usable = vehicle != null && vehicle.Spawned && vehicle.Map != null
                    && vehicle.Faction == Faction.OfPlayerSilentFail
                    && SDS_API.IsGridUsable(vehicle.Map);

                object tabValue = GetMember(__instance, "currentTab");
                string tab = tabValue != null ? tabValue.ToString() : string.Empty;
                bool goods = tab != "Pawns" && tab != "Seats" && tab != "Ammo"
                    && tab != "Backpacks" && tab != "Modifications";

                List<object> copy = Snapshot(groups);
                for (int i = 0; i < copy.Count; i++)
                {
                    object group = copy[i];
                    Thing any = GetMember(group, "AnyThing") as Thing;
                    if (!SDS_API.IsMirror(any))
                    {
                        continue;
                    }

                    if (!usable || !goods)
                    {
                        groups.Remove(group);
                        continue;
                    }

                    if (!SDS_API.TryGetStockKey(any, out SDS_StockKey key)
                        || key == null || SDS_API.IsStockForbidden(key))
                    {
                        groups.Remove(group);
                        continue;
                    }

                    SDS_ApiStockInfo info = SDS_API.ReadStock(key);
                    if (info.result != SDS_ApiResultCode.Ok || info.forbidden || info.available <= 0)
                    {
                        groups.Remove(group);
                        continue;
                    }

                    int available = SDS_API.ClampTakeToAvailable(int.MaxValue, info.available);
                    SetMember(group, "MaxCount", available);
                    int count = IntMember(group, "CountToTransfer");
                    if (count > available)
                    {
                        SetMember(group, "CountToTransfer", available);
                        SetMember(group, "EditBuffer", available.ToString());
                    }
                }

                if (usable && goods)
                {
                    AddPreservedRows(groups);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容（装载列表投影）失败（已忽略）", e);
            }
        }

        /// <summary>
        /// 守门：镜像与存活保真真身不得被原版容器路径接收。
        /// 适配器自己的取货产物已离开保真 owner / 不是镜像，正常放行。
        /// </summary>
        public static bool CanStorePrefix(Thing thing, ref bool __result)
        {
            try
            {
                if (SDS_API.IsMirror(thing) || SDS_API.IsLivePreservedThing(thing))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容（容器守门）失败（已忽略）", e);
            }
            return true;
        }

        private static void AddPreservedRows(IList groups)
        {
            Type groupType = AccessTools.TypeByName(GroupTypeName);
            if (groupType == null)
            {
                return;
            }

            List<Thing> all = new List<Thing>();
            SDS_API.AppendPreservedThings(all);
            Dictionary<SDS_StockKey, List<Thing>> buckets = new Dictionary<SDS_StockKey, List<Thing>>();
            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                // 保真真身不做 SplitOff：只接受单件；堆叠交给其它通道。
                if (thing == null || thing.Destroyed || thing.def == null
                    || thing.stackCount != 1 || !SDS_API.IsLivePreservedThing(thing))
                {
                    continue;
                }
                if (!SDS_API.TryGetStockKey(thing, out SDS_StockKey key)
                    || key == null || !key.preserved || SDS_API.IsStockForbidden(key))
                {
                    continue;
                }

                List<Thing> list;
                if (!buckets.TryGetValue(key, out list))
                {
                    list = new List<Thing>();
                    buckets[key] = list;
                }
                list.Add(thing);
            }

            foreach (KeyValuePair<SDS_StockKey, List<Thing>> pair in buckets)
            {
                List<Thing> bodies = pair.Value;
                if (bodies.Count == 0)
                {
                    continue;
                }

                long preservedAvailable = SDS_API.GetAvailableCount(pair.Key);
                if (preservedAvailable <= 0L)
                {
                    continue;
                }
                int available = SDS_API.ClampTakeToAvailable(bodies.Count, preservedAvailable);
                if (available <= 0)
                {
                    continue;
                }

                Thing body = bodies[0];
                object group = Activator.CreateInstance(groupType);
                if (group == null)
                {
                    continue;
                }

                SetMember(group, "AnyThing", body);
                SetMember(group, "LabelCap", body.LabelCap.ToString());
                SetMember(group, "HeaderGroup", HeaderFor(body));
                SetMember(group, "MaxCount", available);
                SetMember(group, "CountToTransfer", 0);
                SetMember(group, "EditBuffer", "0");
                SetMember(group, "things", new List<Thing>(bodies));
                groups.Add(group);
            }
        }

        private static List<ThingCount> BuildPreservedPlan(SDS_StockKey key, int want)
        {
            List<ThingCount> plan = new List<ThingCount>();
            if (key == null || want <= 0)
            {
                return plan;
            }

            List<Thing> all = new List<Thing>();
            SDS_API.AppendPreservedThings(all);
            for (int i = 0; i < all.Count && want > 0; i++)
            {
                Thing thing = all[i];
                if (thing == null || thing.Destroyed || thing.stackCount != 1
                    || !SDS_API.IsLivePreservedThing(thing))
                {
                    continue;
                }
                if (!SDS_API.TryGetStockKey(thing, out SDS_StockKey candidate)
                    || candidate == null || !candidate.Equals(key)
                    || SDS_API.IsStockForbidden(candidate))
                {
                    continue;
                }
                plan.Add(new ThingCount(thing, 1));
                want--;
            }
            return plan;
        }

        private static void RemoveStockRows(IList groups)
        {
            if (groups == null)
            {
                return;
            }

            List<object> copy = Snapshot(groups);
            for (int i = 0; i < copy.Count; i++)
            {
                object group = copy[i];
                Thing any = GetMember(group, "AnyThing") as Thing;
                if (any == null)
                {
                    continue;
                }
                if (SDS_API.IsMirror(any))
                {
                    groups.Remove(group);
                    continue;
                }
                if (SDS_API.TryGetStockKey(any, out SDS_StockKey key)
                    && key != null && key.preserved)
                {
                    groups.Remove(group);
                }
            }
        }

        private static void ReturnToGridOrPreserved(Thing thing, SDS_StockKey key, Thing vehicle)
        {
            if (thing == null || thing.Destroyed || vehicle == null || vehicle.Map == null)
            {
                return;
            }

            bool returned;
            try
            {
                returned = key != null && key.preserved
                    ? SDS_API.TryReturnPreserved(thing, vehicle.Map, vehicle.Position)
                    : SDS_API.TryReturnVirtual(thing, vehicle.Map, vehicle.Position);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容：回充异常（改为落地）", e);
                returned = false;
            }
            if (returned || thing.Destroyed)
            {
                return;
            }

            try
            {
                GenPlace.TryPlaceThing(thing, vehicle.Position, vehicle.Map, ThingPlaceMode.Near,
                    null, null, null, 1);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容：回充失败后落地失败", e);
            }
        }

        private static bool AddToCargo(object comp, Thing thing)
        {
            if (comp == null || thing == null)
            {
                return false;
            }
            try
            {
                MethodInfo method = AccessTools.Method(comp.GetType(),
                    "TryAddOrTransferThingToCargoStrict",
                    new[] { typeof(Thing), typeof(bool), typeof(bool) });
                if (method == null)
                {
                    return false;
                }
                object result = method.Invoke(comp, new object[] { thing, true, false });
                return result is bool ok && ok;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容：入舱调用失败（将回充）", e);
                return false;
            }
        }

        private static void InvokeVoid(object target, string name)
        {
            if (target == null)
            {
                return;
            }
            try
            {
                MethodInfo method = AccessTools.Method(target.GetType(), name, Type.EmptyTypes);
                if (method != null)
                {
                    method.Invoke(target, null);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容：调用 " + name + " 失败", e);
            }
        }

        private static string HeaderFor(Thing thing)
        {
            try
            {
                if (thing != null && thing.def != null && thing.def.thingCategories != null
                    && thing.def.thingCategories.Count > 0)
                {
                    return thing.def.thingCategories[0].LabelCap.ToString();
                }
            }
            catch (Exception)
            {
                // 分类标签只影响展示分组，退回 MISC 即可。
            }
            return Translator.Translate("Versatran_MiscUncategorized").ToString();
        }

        private static IList GetGroups(object window)
        {
            if (window == null)
            {
                return null;
            }
            try
            {
                FieldInfo field = AccessTools.Field(window.GetType(), "globalAllAvailableGroups");
                return field != null ? field.GetValue(window) as IList : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static object GetMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            try
            {
                Type type = target.GetType();
                PropertyInfo property = AccessTools.Property(type, name);
                if (property != null && property.CanRead)
                {
                    return property.GetValue(target, null);
                }
                FieldInfo field = AccessTools.Field(type, name);
                return field != null ? field.GetValue(target) : null;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容：读取成员 " + name + " 失败（返回默认值）", e);
                return null;
            }
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name))
            {
                return;
            }
            try
            {
                FieldInfo field = AccessTools.Field(target.GetType(), name);
                if (field != null)
                {
                    field.SetValue(target, value);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce(AdapterName + " 兼容：写入成员 " + name + " 失败（已忽略）", e);
            }
        }

        private static int IntMember(object target, string name)
        {
            object value = GetMember(target, name);
            return value is int number ? number : 0;
        }

        private static List<object> Snapshot(IList groups)
        {
            List<object> copy = new List<object>();
            if (groups == null)
            {
                return copy;
            }
            for (int i = 0; i < groups.Count; i++)
            {
                copy.Add(groups[i]);
            }
            return copy;
        }
    }
}
