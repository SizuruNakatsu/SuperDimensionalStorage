using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{

    [HarmonyPatch(typeof(TradeDeal), "Reset")]
    internal static class Patch_SDS_TradeDealReset
    {
        public static void Prefix()
        {
            try
            {
                SDS_TradeVirtual.Reset();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("交易展示物清理失败（已忽略）", e);
            }
        }

        public static void Postfix(TradeDeal __instance)
        {
            try
            {
                SDS_TradeVirtual.Inject(__instance);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("交易超维空间品类注入失败（已忽略，原版交易不受影响）", e);
            }
        }
    }

    [HarmonyPatch(typeof(TradeDeal), "TryExecute")]
    internal static class Patch_SDS_TradeDealValidate
    {
        public static bool Prefix(TradeDeal __instance, ref bool actuallyTraded, ref bool __result)
        {
            try
            {
                return SDS_TradeVirtual.Validate(__instance, ref actuallyTraded, ref __result);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("交易超维空间校验失败（已忽略，放行原版交易）", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.ResolveTrade))]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_SDS_TradeableResolveTrade
    {
        public static bool Prefix(Tradeable __instance)
        {
            if (!SDS_TradeVirtual.HasConsumedDisplay(__instance))
            {
                return true;
            }
            try
            {
                SDS_TradeVirtual.SwapDisplaysForReal(__instance);
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("交易交收展示物替换失败（本行已跳过，绝不把展示物交给商人）", e);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), "Close")]
    internal static class Patch_SDS_DialogTradeClose
    {
        public static void Postfix()
        {
            try
            {
                SDS_TradeVirtual.Reset();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("交易展示物收尾清理失败（已忽略）", e);
            }
        }
    }

    [HarmonyPatch(typeof(TradeUtility), "AllLaunchableThingsForTrade")]
    internal static class Patch_SDS_BeaconLaunchRead
    {
        public static void Postfix(Map map, ITrader trader, ref IEnumerable<Thing> __result)
        {
            try
            {
                if (!SDS_BeaconDisplay.IsLaunchFromColony(map, trader))
                {
                    return;
                }
                __result = SDS_BeaconDisplay.Append(__result, map);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("通讯信标读出口注入超维空间失败（已忽略，原版行为不受影响）", e);
            }
        }
    }

    [HarmonyPatch(typeof(TradeUtility), "LaunchThingsOfType")]
    internal static class Patch_SDS_BeaconLaunchTake
    {
        public static bool Prefix(ThingDef resDef, ref int debt, Map map, TradeShip trader)
        {
            try
            {
                if (resDef == null || debt <= 0 || !SDS_BeaconDisplay.IsLaunchFromColony(map, trader))
                {
                    return true;
                }
                int onMap = SDS_BeaconDisplay.CountLaunchableOnMap(resDef, map);
                if (onMap >= debt)
                {
                    return true;
                }
                int taken = SDS_BeaconDisplay.ConsumeFromGrid(resDef, debt - onMap);
                if (taken > 0)
                {
                    debt -= taken;
                }
                return true;
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("通讯信标取出口扣减超维空间失败（已忽略，原版行为不受影响）", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Settlement_TraderTracker), "ColonyThingsWillingToBuy")]
    internal static class Patch_SDS_SettlementBuyList
    {
        public static void Postfix(Pawn playerNegotiator, ref IEnumerable<Thing> __result)
        {
            try
            {
                __result = SDS_RemoteTrade.AppendVirtualDisplays(__result, playerNegotiator);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("世界聚落交易清单注入超维空间失败（已忽略，原版交易不受影响）", e);
            }
        }
    }

    [HarmonyPatch(typeof(Caravan_TraderTracker), "ColonyThingsWillingToBuy")]
    internal static class Patch_SDS_CaravanBuyList
    {
        public static void Postfix(Pawn playerNegotiator, ref IEnumerable<Thing> __result)
        {
            try
            {
                __result = SDS_RemoteTrade.AppendVirtualDisplays(__result, playerNegotiator);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("商队交易清单注入超维空间失败（已忽略，原版交易不受影响）", e);
            }
        }
    }

    [HarmonyPatch(typeof(Settlement_TraderTracker), "GiveSoldThingToPlayer")]
    internal static class Patch_SDS_SettlementGiveBought
    {
        public static bool Prefix(Thing toGive, int countToGive)
        {
            try
            {
                return SDS_RemoteTrade.TryTakeBoughtIntoGrid(toGive, countToGive,
                    TradeSession.playerNegotiator);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("世界聚落买入并入超维空间失败（已忽略，交回原版交付）", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Caravan_TraderTracker), "GiveSoldThingToPlayer")]
    internal static class Patch_SDS_CaravanGiveBought
    {
        public static bool Prefix(Thing toGive, int countToGive)
        {
            try
            {
                return SDS_RemoteTrade.TryTakeBoughtIntoGrid(toGive, countToGive,
                    TradeSession.playerNegotiator);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("商队买入并入超维空间失败（已忽略，交回原版交付）", e);
                return true;
            }
        }
    }
}
