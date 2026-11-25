using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 端点通用逻辑（§6.4）：接收判定、余量、缓冲数字化。
    /// 核心 / 锚点 / 人端口都收口在这里；Accepts 是全 Mod 唯一的存入硬门。
    /// </summary>
    internal static class SDS_EndpointUtility
    {
        /// <summary>
        /// IHaulEnroute 的余量：缓冲是瞬态的（成功入缓冲即数字化），没有真实容量上限，
        /// 可用时返回富余常数，不可用时返回 0 让原版不要把料往这里送。
        /// </summary>
        internal const int EnrouteSpace = 99999;

        /// <summary>端点能否再收新料：全局可用 + 端点可用 + 未超品类上限。</summary>
        internal static bool CanReceive(ISDS_Port port)
        {
            if (port == null || !port.Operational || !SDS_Rules.Available)
            {
                return false;
            }
            return SDS_Core.UsedCapacity <= SDS_Rules.TypeCapacity;
        }

        /// <summary>
        /// W53 唯一「可存入实物」判据：镜像代表账本本身，不是可再入库的新货。
        /// 端点接收（IHaulDestination.Accepts）、存入竞争与手动存入全部只经这里，
        /// 不得在调用方各自补镜像判断。
        /// </summary>
        // RULE:W53
        internal static bool IsDepositable(Thing thing)
        {
            if (thing == null || thing.Destroyed || SDS_MirrorRegistry.IsMirror(thing))
            {
                return false;
            }
            return SDS_Rules.Available && SDS_Rules.CanDeposit(thing);
        }

        /// <summary>
        /// 仓位判定 = 端点可用 + 可存入实物（W53 唯一判据）+ 非在途来源标记。
        /// 原版搬运 / 存储搜索经 IHaulDestination.Accepts 问这里；不允许在调用方另写一套。
        /// </summary>
        internal static bool Accepts(ISDS_Port port, Thing thing, bool allowProducedMarked)
        {
            if (port == null || !port.Operational || !IsDepositable(thing))
            {
                return false;
            }
            return allowProducedMarked || !SDS_Rules.IsProducedMarked(thing);
        }

        /// <summary>
        /// W57 唯一“手动 = forced”事实：玩家发起的存入允许通过在途物化标记闸。
        /// 所有手动入口（右键 / 存入背包 / 第三方玩家 UI）只允许调用这里，不得再传字面量。
        /// </summary>
        internal static bool AcceptsForManual(ISDS_Port port, Thing thing)
        {
            return Accepts(port, thing, allowProducedMarked: true);
        }

        /// <summary>自动搬运 / 回充的唯一事实：不允许通过在途物化标记闸。</summary>
        internal static bool AcceptsForAuto(ISDS_Port port, Thing thing)
        {
            return Accepts(port, thing, allowProducedMarked: false);
        }

        internal static int SpaceRemainingFor(ISDS_Port port)
        {
            return CanReceive(port) ? EnrouteSpace : 0;
        }

        /// <summary>
        /// 把缓冲里的实物并入账本：逐件处理，失败立刻退回地图，绝不跨 tick 留在缓冲。
        /// </summary>
        internal static void FlushInputBuffer(ISDS_StorageEndpoint endpoint)
        {
            ThingOwner buffer = endpoint != null ? endpoint.InputBuffer : null;
            if (buffer == null || buffer.Count == 0)
            {
                return;
            }
            List<Thing> snapshot = new List<Thing>(buffer.Count);
            for (int i = 0; i < buffer.Count; i++)
            {
                snapshot.Add(buffer[i]);
            }
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                FlushInputBufferItem(endpoint, snapshot[i]);
            }
        }

        /// <summary>
        /// 读档专用：剔除端点缓冲里的失效残留——与存活保真真身同 thingID 的重复副本、已销毁、零栈。
        /// 「同一真身同一时刻只能有一个所有者」，重复副本必须丢弃；真实库存不 Split、不 Destroy。
        /// </summary>
        internal static int PruneLoadedBuffer(ThingOwner buffer)
        {
            return PruneLoadedOwnership(buffer, "存档/输入缓冲残留已清理");
        }

        /// <summary>
        /// 读档归一化的唯一实现：任何容器（端点缓冲 / 小人背包 / 小人手持）共用同一判据。
        /// 只移除引用，不 Split、不 Destroy、不改数量。
        /// </summary>
        internal static int PruneLoadedOwnership(ThingOwner container, string logLabel)
        {
            if (container == null || container.Count == 0)
            {
                return 0;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            List<Thing> remove = null;
            for (int i = 0; i < container.Count; i++)
            {
                Thing thing = container[i];
                if (thing == null)
                {
                    continue;
                }
                if (thing.Destroyed || thing.stackCount <= 0
                    || (ledger != null && ledger.IsPreservedThingId(thing.thingIDNumber)))
                {
                    if (remove == null)
                    {
                        remove = new List<Thing>();
                    }
                    remove.Add(thing);
                }
            }
            if (remove == null)
            {
                return 0;
            }
            for (int i = 0; i < remove.Count; i++)
            {
                container.Remove(remove[i]);
            }
            SDS_Diag.Warn(logLabel, "件数=" + remove.Count);
            return remove.Count;
        }

        /// <summary>
        /// 单件事务：只处理这一件；成功即离开缓冲，失败立刻退回地图。
        /// 入账只调用 SDS_EndpointHandle.TryDeposit，不在这里复制 SplitOff / Destroy 逻辑。
        /// </summary>
        internal static void FlushInputBufferItem(ISDS_StorageEndpoint endpoint, Thing thing)
        {
            ThingOwner buffer = endpoint != null ? endpoint.InputBuffer : null;
            if (buffer == null || thing == null || !buffer.Contains(thing))
            {
                return;
            }
            // 已销毁 / 零栈 / 已被上一轮转移走：都只清残留条目，绝不再入账。
            if (thing.Destroyed || thing.stackCount <= 0
                || (thing.holdingOwner != null && !ReferenceEquals(thing.holdingOwner, buffer)))
            {
                buffer.Remove(thing);
                return;
            }
            try
            {
                SDS_EndpointHandle handle = SDS_EndpointHandle.ForBuilding(endpoint);
                if (handle != null && handle.TryDeposit(thing, out int accepted) && accepted > 0)
                {
                    RemoveIfContained(buffer, thing);
                    return;
                }
                EjectToMap(endpoint, thing);
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("端点输入缓冲入账失败（已退回地图）", e);
                EjectToMap(endpoint, thing);
            }
        }

        /// <summary>
        /// 端点拆除 / 销毁前的缓冲清空：先 flush，再逐级回退（地图 → 账本），绝不吞物品。
        /// </summary>
        internal static void DrainInputBufferSafely(ISDS_StorageEndpoint endpoint, ThingOwner buffer)
        {
            if (endpoint == null || buffer == null || buffer.Count == 0)
            {
                return;
            }
            FlushInputBuffer(endpoint);
            if (buffer.Count == 0)
            {
                return;
            }
            Map map = EndpointMap(endpoint);
            Thing building = endpoint as Thing;
            if (map != null && building != null)
            {
                buffer.TryDropAll(building.Position, map, ThingPlaceMode.Near);
            }
            if (buffer.Count == 0)
            {
                return;
            }
            // 没有地图 / 掉落失败：账本不依赖地图，作为最后一道回收入口。
            List<Thing> remaining = new List<Thing>(buffer);
            for (int i = 0; i < remaining.Count; i++)
            {
                Thing thing = remaining[i];
                if (TryReturnToLedger(thing, map))
                {
                    RemoveIfContained(buffer, thing);
                    continue;
                }
                SDS_Diag.Fail("端点拆除/缓冲残留无法回收", SDS_Diag.Ctx(
                    map, null, thing, null, "endpoint=" + endpoint.GetType().Name));
            }
        }

        private static bool TryReturnToLedger(Thing thing, Map map)
        {
            if (thing == null || thing.Destroyed)
            {
                return true;
            }
            SDS_StockKey key;
            if (!SDS_Rules.TryGetStockKey(thing, out key) || key == null)
            {
                return false;
            }
            return SDS_Rules.ReturnThing(thing,
                key.preserved ? SDS_StockChannel.Preserved : SDS_StockChannel.Virtual,
                map, null, SDS_ReturnFallback.None);
        }

        /// <summary>把滞留在缓冲里的实物退回地图（拆除 / 不可用时不吞物品）。</summary>
        private static void EjectToMap(ISDS_StorageEndpoint endpoint, Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }
            Map map = EndpointMap(endpoint);
            Thing building = endpoint as Thing;
            if (map == null || building == null)
            {
                // 端点已无地图（不该发生）：实物留在缓冲里等下个心跳重试，绝不吞物品。
                return;
            }
            Thing ejected = thing.SplitOff(thing.stackCount);
            if (ejected == null)
            {
                return;
            }
            SDS_PlacementUtility.TryPlaceOrSpawn(ejected, building.Position, map);
        }

        private static void RemoveIfContained(ThingOwner buffer, Thing thing)
        {
            if (buffer != null && thing != null && buffer.Contains(thing))
            {
                buffer.Remove(thing);
            }
        }

        /// <summary>
        /// 端点所在地图。ISDS_StorageEndpoint 同时继承 IHaulDestination 与 IHaulEnroute，
        /// 两者都声明 Map，直接写 endpoint.Map 会二义；端点都是 Thing，统一经 Thing.Map 取值。
        /// </summary>
        private static Map EndpointMap(ISDS_StorageEndpoint endpoint)
        {
            Thing thing = endpoint as Thing;
            return thing != null ? thing.Map : null;
        }
    }
}
