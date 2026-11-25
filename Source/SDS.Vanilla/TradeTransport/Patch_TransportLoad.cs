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

    // ============================ 运输舱装载对话框 ============================

    /// <summary>
    /// TransporterUtility.AllSendableItems：任务穿梭机（CompShuttle.requiredItems）只从这份清单构建 leftToLoad。
    /// 这里只追加 requiredItems 点名的品类（不是全部库存），装载 Job 由既有补丁改派到端点取货。
    /// </summary>
    [HarmonyPatch(typeof(TransporterUtility), nameof(TransporterUtility.AllSendableItems))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_ShuttleRequiredItemsFromGrid
    {
        public static void Postfix(List<CompTransporter> transporters, Map map, ref IEnumerable<Thing> __result)
        {
            try
            {
                if (transporters == null || transporters.Count == 0 || map == null)
                {
                    return;
                }
                Thing parent = transporters[0] != null ? transporters[0].parent : null;
                CompShuttle shuttle = parent != null ? parent.TryGetComp<CompShuttle>() : null;
                if (shuttle == null || shuttle.requiredItems.NullOrEmpty())
                {
                    return;
                }
                if (SDS_Core.Ledger == null || !SDS_Rules.Available || !SDS_Exposure.MapExposesGrid(map))
                {
                    return;
                }
                __result = AppendRequiredItems(__result, shuttle, parent, map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("穿梭机必需物品注入超维空间失败（已忽略，原版装载不受影响）", e);
            }
        }

        private static IEnumerable<Thing> AppendRequiredItems(IEnumerable<Thing> source, CompShuttle shuttle,
            Thing transporter, Map map)
        {
            if (source != null)
            {
                foreach (Thing thing in source)
                {
                    yield return thing;
                }
            }
            IntVec3 displayCell = transporter.InteractionCell.InBounds(map)
                ? transporter.InteractionCell
                : transporter.Position;
            HashSet<ThingDef> requiredDefs = new HashSet<ThingDef>();
            bool needPreserved = false;
            for (int i = 0; i < shuttle.requiredItems.Count; i++)
            {
                ThingDefCount required = shuttle.requiredItems[i];
                ThingDef def = required != null ? required.ThingDef : null;
                if (def == null || def.category != ThingCategory.Item || !def.canLoadIntoCaravan)
                {
                    continue;
                }
                requiredDefs.Add(def);
                if (!SDS_Rules.TryFindWithdrawableKey(def, out SDS_StockKey key, out _))
                {
                    continue;
                }
                if (key.preserved)
                {
                    // 保真通道不在这里造展示物：统一走下面的原版分组桶。
                    needPreserved = true;
                    continue;
                }
                long available = SDS_TransportVirtual.LoadAvailable(key);
                if (available <= 0L)
                {
                    continue;
                }
                if (SDS_Rules.GetAvailableCountForDef(def) > available)
                {
                    needPreserved = true;
                }
                Thing fake = SDS_OutputUtility.CachedDisplayThing(key);
                if (fake == null)
                {
                    continue;
                }
                fake.stackCount = available > int.MaxValue ? int.MaxValue : (int)available;
                fake.Position = displayCell;
                yield return fake;
            }
            if (!needPreserved || requiredDefs.Count == 0)
            {
                yield break;
            }
            // 保真通道补齐：只喂 requiredItems 点名的 def，分组复用原版 TransferAsOne（不登记桶，纯读）。
            // 缩装物不参与：原版 AllReachableColonyItems 的 canMinify=false，展示物外层 def 是 MinifiedThing。
            List<SDS_PreservedBucket> buckets = SDS_PreservedBuckets.Build(SDS_Core.Ledger,
                delegate (Thing thing)
                {
                    return thing != null && thing.def != null
                        && thing.def.category == ThingCategory.Item
                        && !(thing is MinifiedThing)
                        && thing.def.canLoadIntoCaravan
                        && requiredDefs.Contains(thing.def);
                },
                tradeGrouping: false, trader: null, tradableBudget: false);
            for (int i = 0; i < buckets.Count; i++)
            {
                SDS_PreservedBucket bucket = buckets[i];
                if (bucket == null || bucket.display == null || bucket.units <= 0)
                {
                    continue;
                }
                bucket.display.Position = displayCell;
                yield return bucket.display;
            }
        }
    }

    /// <summary>运输舱装载清单建好后注入超维空间展示物（可见 / 可勾选 / 计质量）。</summary>
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "AddItemsToTransferables")]
    internal static class Patch_SDS_TransporterAddVirtual
    {
        public static void Postfix(Dialog_LoadTransporters __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                // ProjectE 等价交换的「装载清单瞬时转化」不是原版装载管线；挂点未生效时
                // 不得注入展示物，否则会被它直接销毁而账本不动（免费 EMC）。
                if (!SDS_CompatGates.AllowsTransportLoadInjection(__instance))
                {
                    return;
                }
                Traverse tr = Traverse.Create(__instance);
                List<TransferableOneWay> transferables = tr.Field("transferables").GetValue<List<TransferableOneWay>>();
                List<CompTransporter> transporters = tr.Field("transporters").GetValue<List<CompTransporter>>();
                Map map = tr.Field("map").GetValue<Map>();
                SDS_TransportVirtual.InjectIntoTransporters(transferables, transporters, map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("运输舱超维空间品类注入失败（已忽略，原版装载不受影响）", e);
            }
        }
    }

    /// <summary>
    /// 原版"能不能派装载活"的判定：地图上找不到实物、而超维空间能供给时放行
    /// （其余拒绝理由原样保留）。放行后由下面的 Job 补丁改派"去最近端点取货"。
    /// </summary>
    [HarmonyPatch(typeof(LoadTransportersJobUtility), "HasJobOnTransporter")]
    internal static class Patch_SDS_TransporterLoadHasJob
    {
        public static void Postfix(Pawn pawn, CompTransporter transporter, ref bool __result)
        {
            try
            {
                if (pawn == null || pawn.Map == null || transporter == null || transporter.parent == null)
                {
                    return;
                }
                // 原版已放行（地图上有实物）也要登记快照，JobOn 直接保留原版 Job。
                if (__result)
                {
                    SDS_TransportVirtual.RememberSelection(pawn, transporter.parent, null, true);
                    return;
                }
                // 地图上有实物 → 交原版。
                if (LoadTransportersJobUtility.FindThingToLoad(pawn, transporter).Thing != null)
                {
                    return;
                }
                // 原版在 FindThingToLoad 之前还有两条前置：会操作 + 走得到舱体。
                if (pawn.health == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    return;
                }
                if (!pawn.CanReach(transporter.parent, PathEndMode.Touch, pawn.NormalMaxDanger()))
                {
                    return;
                }
                // 处于取货失败冷却中 → 这一轮不接活（原版也拿不出实物，接了就成死循环）。
                if (SDS_TransportVirtual.IsLoadCoolingDown(pawn, transporter.parent))
                {
                    return;
                }
                // 可行性（含取物点）只在 TrySelectLoadRow 判定一次；行不可服务会自动跳到下一行。
                if (!SDS_TransportVirtual.TrySelectLoadRow(transporter.parent, transporter.leftToLoad, pawn,
                        out SDS_TransportVirtual.SDS_LoadSelection selection)
                    || selection == null || selection.count <= 0)
                {
                    return;
                }
                SDS_TransportVirtual.RememberSelection(pawn, transporter.parent, selection, false);
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("运输舱超维空间可装载判定失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>原版装载 Job：地图上找不到实物时改派「去最近端点取到手上 → 原版搬进舱」。</summary>
    [HarmonyPatch(typeof(LoadTransportersJobUtility), "JobOnTransporter")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_TransporterLoadJob
    {
        // 原版签名是 JobOnTransporter(Pawn p, CompTransporter transporter)：
        // Postfix 的普通参数必须与原版同名（Harmony 按名字注入），所以这里必须是 p。
        public static void Postfix(Pawn p, CompTransporter transporter, ref Job __result)
        {
            try
            {
                if (p == null || p.Map == null || transporter == null || transporter.parent == null)
                {
                    return;
                }
                // 先消费 HasJob 的判定快照。
                if (SDS_TransportVirtual.TryTakeSelection(p, transporter.parent, out bool vanilla,
                        out SDS_TransportVirtual.SDS_LoadSelection selection))
                {
                    if (vanilla)
                    {
                        return;
                    }
                    if (SDS_TransportVirtual.IsLoadCoolingDown(p, transporter.parent))
                    {
                        __result = null;
                        return;
                    }
                    __result = SDS_TransportVirtual.MakeLoadJob(p, transporter.parent, selection);
                    return;
                }
                // W42：JobOn 只消费 HasJob 在同一 tick 登记的快照。
                // 快照缺失时不重新求值可变运行期状态（含 FindThingToLoad / TrySelectLoadRow），
                // 原样保留原版结果；下一次 HasJob 会重新生成快照。
                SDS_Diag.Warn("装载/派活快照缺失（本 tick 不改派）",
                    SDS_Diag.Ctx(p.Map, p, transporter.parent, null));
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("运输舱超维空间取货派活失败（已忽略，交回原版）", e);
            }
        }
    }

    // ============================ 传送门进入对话框 ============================

    /// <summary>传送门进入清单建好后注入超维空间展示物。</summary>
    [HarmonyPatch(typeof(Dialog_EnterPortal), "AddItemsToTransferables")]
    internal static class Patch_SDS_PortalAddVirtual
    {
        public static void Postfix(Dialog_EnterPortal __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                Traverse tr = Traverse.Create(__instance);
                List<TransferableOneWay> transferables = tr.Field("transferables").GetValue<List<TransferableOneWay>>();
                MapPortal portal = tr.Field("portal").GetValue<MapPortal>();
                SDS_TransportVirtual.InjectIntoPortal(transferables, portal);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("传送门超维空间品类注入失败（已忽略，原版进入不受影响）", e);
            }
        }
    }

    /// <summary>传送门：地图上找不到实物、而超维空间能供给时放行。</summary>
    [HarmonyPatch(typeof(EnterPortalUtility), "HasJobOnPortal")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PortalLoadHasJob
    {
        public static void Postfix(Pawn pawn, MapPortal portal, ref bool __result)
        {
            try
            {
                if (pawn == null || pawn.Map == null || portal == null)
                {
                    return;
                }
                // 原版已放行也要登记快照，JobOn 直接保留原版 Job。
                if (__result)
                {
                    SDS_TransportVirtual.RememberSelection(pawn, portal, null, true);
                    return;
                }
                if (EnterPortalUtility.FindThingToLoad(pawn, portal).Thing != null)
                {
                    return;
                }
                // 与原版 HasJobOnPortal 一致：会操作 + 走得到传送门，才允许我们接管"缺实物"。
                if (pawn.health == null || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    return;
                }
                if (!pawn.CanReach(portal, PathEndMode.Touch, pawn.NormalMaxDanger()))
                {
                    return;
                }
                if (SDS_TransportVirtual.IsLoadCoolingDown(pawn, portal))
                {
                    return;
                }
                if (!SDS_TransportVirtual.TrySelectLoadRow(portal, portal.leftToLoad, pawn,
                        out SDS_TransportVirtual.SDS_LoadSelection selection)
                    || selection == null || selection.count <= 0)
                {
                    return;
                }
                SDS_TransportVirtual.RememberSelection(pawn, portal, selection, false);
                __result = true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("传送门超维空间可装载判定失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    /// <summary>传送门装载 Job：地图上找不到实物时改派「去最近端点取到手上 → 原版搬进传送门」。</summary>
    [HarmonyPatch(typeof(EnterPortalUtility), "JobOnPortal")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_PortalLoadJob
    {
        // 原版签名是 JobOnPortal(Pawn p, MapPortal portal) —— 参数名必须同名，故用 p。
        public static void Postfix(Pawn p, MapPortal portal, ref Job __result)
        {
            try
            {
                if (p == null || p.Map == null || portal == null)
                {
                    return;
                }
                // 先消费 HasJob 的判定快照（原版放行 → 保留原版 Job；超维取货 → 建我们的 Job）。
                if (SDS_TransportVirtual.TryTakeSelection(p, portal, out bool vanilla,
                        out SDS_TransportVirtual.SDS_LoadSelection selection))
                {
                    if (vanilla)
                    {
                        return;
                    }
                    if (SDS_TransportVirtual.IsLoadCoolingDown(p, portal))
                    {
                        __result = null;
                        return;
                    }
                    __result = SDS_TransportVirtual.MakeLoadJob(p, portal, selection);
                    return;
                }
                // W42：与运输舱同口径——只消费同 tick 快照，缺失时不重新求值，保留原版结果。
                SDS_Diag.Warn("装载/派活快照缺失（本 tick 不改派）",
                    SDS_Diag.Ctx(p.Map, p, portal, null));
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("传送门超维空间取货派活失败（已忽略，交回原版）", e);
            }
        }
    }

    /// <summary>
    /// W40：运输舱 / 空投舱 / 运输机 / 任务穿梭机共用 CompTransporter.SubtractFromToLoadList。
    /// 入舱前把真身归一化进它所属的行，让原版 things.Contains(真身) 精确命中；
    /// 本 Prefix 只补引用，扣数 / 移除行 / 装载完毕消息仍由原版执行。
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), "SubtractFromToLoadList")]
    internal static class Patch_SDS_TransporterDepositIdentity
    {
        public static void Prefix(CompTransporter __instance, Thing t)
        {
            try
            {
                if (__instance != null)
                {
                    SDS_TransportVirtual.NormalizeDepositedThing(__instance.leftToLoad, t);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("运输舱入舱身份归一失败（已忽略，原版装载不受影响）", e);
            }
        }
    }

    /// <summary>W40：传送门与运输舱共用同一条身份规则。</summary>
    [HarmonyPatch(typeof(MapPortal), "SubtractFromToLoadList")]
    internal static class Patch_SDS_PortalDepositIdentity
    {
        public static void Prefix(MapPortal __instance, Thing t)
        {
            try
            {
                if (__instance != null)
                {
                    SDS_TransportVirtual.NormalizeDepositedThing(__instance.leftToLoad, t);
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("传送门入舱身份归一失败（已忽略，原版装载不受影响）", e);
            }
        }
    }
}
