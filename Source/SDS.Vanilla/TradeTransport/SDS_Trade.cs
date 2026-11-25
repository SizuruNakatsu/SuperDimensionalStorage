using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 本地 / 远程 / 信标交易的虚拟交付适配器（规则锚点：R16 / W23 / W24）：
    /// 展示物只做 UI；打开清单只注入展示物，不设跨对话框预留；
    /// 成交 / 出发 / 装载时开 Instant 事务（Execute + Deliver），关闭 / 取消只解除展示物引用。
    /// </summary>
    internal static class SDS_TradeVirtual
    {
        /// <summary>保真展示物对应的「原版分组桶」：桶内就是会被原版合并成同一交易行的真身。</summary>
        private static readonly SDS_DisplayBucketRegistry preservedBuckets =
            new SDS_DisplayBucketRegistry();

        private static void RegisterPreservedBucket(Thing fake, List<Thing> bucket)
        {
            preservedBuckets.Register(fake, bucket);
        }

        /// <summary>
        /// 虚拟账本 → 展示物流（唯一实现）：本地交易 / 远程交易 / 信标共用同一份 Snapshot、
        /// 同一条「可交易量」闸门与同一个全局缓存展示物。
        /// </summary>
        internal static IEnumerable<Thing> VirtualDisplayStream(ISDS_LedgerRead ledger,
            ITrader trader, Action<Thing> onDisplay)
        {
            if (ledger == null)
            {
                yield break;
            }
            List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                if (entry == null || entry.key == null || entry.key.thingDef == null
                    || entry.key.preserved || entry.count <= 0)
                {
                    continue;
                }
                long available = SDS_Availability.GetTradableCount(entry.key);
                if (available <= 0)
                {
                    continue;
                }
                Thing fake = SDS_OutputUtility.CachedDisplayThing(entry.key);
                if (fake == null || !TradeUtility.PlayerSellableNow(fake, trader))
                {
                    continue;
                }
                fake.stackCount = SDS_Availability.ClampTakeToAvailable(int.MaxValue, available);
                onDisplay?.Invoke(fake);
                yield return fake;
            }
        }

        /// <summary>交易会话是否属于「本图的本地商人」。</summary>
        public static bool TryGetLocalTrader(out ITrader trader, out Map map)
        {
            trader = TradeSession.trader;
            map = TradeSession.playerNegotiator != null ? TradeSession.playerNegotiator.Map : null;
            if (map == null)
            {
                return false;
            }
            if (trader is Pawn pawnTrader)
            {
                return pawnTrader.Spawned && pawnTrader.Map == map;
            }
            if (trader is PassingShip passingShip)
            {
                return passingShip.Map == map;
            }
            return false;
        }

        private static bool MapReady(Map map)
        {
            return SDS_Core.Ledger != null && SDS_Exposure.MapExposesGrid(map);
        }

        /// <summary>清掉残留的展示物登记（只解除引用，绝不销毁）。</summary>
        public static void Reset()
        {
            preservedBuckets.Clear();
            SDS_OutputUtility.PruneBucketDisplays();
        }

        private static void InjectPreserved(ISDS_LedgerRead ledger, ITrader trader, List<Tradeable> tradeables)
        {
            List<Thing> displays = BuildTradePreservedDisplays(ledger, trader);
            for (int i = 0; i < displays.Count; i++)
            {
                Thing fake = displays[i];
                Tradeable tradeable = TransferableUtility.TradeableMatching(fake, tradeables);
                if (tradeable == null)
                {
                    tradeable = new Tradeable();
                    tradeables.Add(tradeable);
                }
                tradeable.AddThing(fake, Transactor.Colony);
            }
        }

        internal static List<Thing> BuildPreservedDisplays(ISDS_LedgerRead ledger, ITrader trader)
        {
            List<SDS_PreservedBucket> buckets = BuildPreservedBuckets(ledger, trader);
            List<Thing> result = new List<Thing>();
            for (int i = 0; i < buckets.Count; i++)
            {
                SDS_PreservedBucket bucket = buckets[i];
                if (bucket != null && bucket.display != null)
                {
                    result.Add(bucket.display);
                }
            }
            return result;
        }

        internal static List<Thing> BuildTradePreservedDisplays(ISDS_LedgerRead ledger, ITrader trader)
        {
            List<SDS_PreservedBucket> buckets = BuildPreservedBuckets(ledger, trader);
            List<Thing> result = new List<Thing>();
            for (int i = 0; i < buckets.Count; i++)
            {
                SDS_PreservedBucket bucket = buckets[i];
                if (bucket == null || bucket.display == null)
                {
                    continue;
                }
                RegisterPreservedBucket(bucket.display, bucket.things);
                result.Add(bucket.display);
            }
            return result;
        }

        private static List<SDS_PreservedBucket> BuildPreservedBuckets(ISDS_LedgerRead ledger, ITrader trader)
        {
            return SDS_PreservedBuckets.Build(ledger,
                delegate (Thing thing) { return TradeUtility.PlayerSellableNow(thing, trader); },
                tradeGrouping: true, trader, tradableBudget: true);
        }

        /// <summary>往 Colony 侧交易清单注入超维空间品类的展示物。</summary>
        public static void Inject(TradeDeal deal)
        {
            if (deal == null)
            {
                return;
            }
            ITrader trader;
            Map map;
            if (TradeSession.giftMode)
            {
                trader = TradeSession.trader;
                map = TradeSession.playerNegotiator != null ? TradeSession.playerNegotiator.Map : null;
                if (trader == null || map == null || !MapReady(map))
                {
                    return;
                }
            }
            else if (!TryGetLocalTrader(out trader, out map) || !MapReady(map))
            {
                return;
            }
            if (trader.TraderKind != null && trader.TraderKind.orbital)
            {
                bool powered = false;
                foreach (Building_OrbitalTradeBeacon _ in Building_OrbitalTradeBeacon.AllPowered(map))
                {
                    powered = true;
                    break;
                }
                if (!powered)
                {
                    return;
                }
            }

            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            List<Tradeable> tradeables = deal.AllTradeables;
            foreach (Thing fake in VirtualDisplayStream(ledger, trader, null))
            {
                Tradeable tradeable = TransferableUtility.TradeableMatching(fake, tradeables);
                int held = tradeable != null ? tradeable.CountHeldBy(Transactor.Colony) : 0;
                int room = Math.Max(0, int.MaxValue - held);
                if (fake.stackCount > room)
                {
                    fake.stackCount = room;
                }
                if (fake.stackCount <= 0)
                {
                    continue;
                }
                if (tradeable == null)
                {
                    tradeable = new Tradeable();
                    tradeables.Add(tradeable);
                }
                tradeable.AddThing(fake, Transactor.Colony);
            }
            InjectPreserved(ledger, trader, tradeables);
        }

        /// <summary>成交前校验：本次要卖出的超维空间可用量必须仍然够。</summary>
        // RULE:W23
        public static bool Validate(TradeDeal deal, ref bool actuallyTraded, ref bool __result)
        {
            if (deal == null || SDS_Core.Ledger == null)
            {
                return true;
            }
            bool changed = false;
            Dictionary<SDS_StockKey, int> budget = null;
            string failLabel = null;
            long failAvailable = 0L;
            List<Tradeable> tradeables = deal.AllTradeables;
            for (int i = 0; i < tradeables.Count && !changed; i++)
            {
                Tradeable tradeable = tradeables[i];
                if (tradeable == null || tradeable.ActionToDo != TradeAction.PlayerSells)
                {
                    continue;
                }
                int remaining = tradeable.CountToTransferToDestination;
                for (int j = 0; j < tradeable.thingsColony.Count; j++)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }
                    Thing thing = tradeable.thingsColony[j];
                    int take = Math.Min(remaining, thing.stackCount);
                    if (SDS_OutputUtility.TryGetDisplayKey(thing, out _))
                    {
                        if (preservedBuckets.TryGet(thing, out List<Thing> preservedBucket))
                        {
                            SDS_StockKey preservedKey = SDS_OutputUtility.KeyOfDisplay(thing, preservedBucket);
                            int budgetLeft = BudgetLeft(ref budget, preservedKey);
                            if (preservedKey == null || !preservedKey.preserved || budgetLeft < take)
                            {
                                changed = true;
                                failLabel = tradeable.Label;
                                failAvailable = Math.Max(0, budgetLeft);
                                break;
                            }
                            SDS_PreservedBuckets.TakePlan(preservedBucket, take, out int planned);
                            if (planned < take)
                            {
                                changed = true;
                                failLabel = tradeable.Label;
                                failAvailable = Math.Max(0, budgetLeft - planned);
                                break;
                            }
                            budget[preservedKey] = budgetLeft - take;
                        }
                        else
                        {
                            SDS_StockKey key = SDS_OutputUtility.KeyOfDisplay(thing, null);
                            int budgetLeft = BudgetLeft(ref budget, key);
                            if (key == null || budgetLeft < take)
                            {
                                changed = true;
                                failLabel = tradeable.Label;
                                failAvailable = Math.Max(0, budgetLeft);
                                break;
                            }
                            budget[key] = budgetLeft - take;
                        }
                    }
                    remaining -= take;
                }
            }
            if (!changed)
            {
                return true;
            }
            Messages.Message("SDS.Trade.VirtualStorageChanged".Translate(
                failLabel.NullOrEmpty() ? "?" : failLabel, failAvailable),
                MessageTypeDefOf.RejectInput, false);
            actuallyTraded = false;
            __result = false;
            return false;
        }

        private static int BudgetLeft(ref Dictionary<SDS_StockKey, int> budget, SDS_StockKey key)
        {
            if (key == null)
            {
                return 0;
            }
            if (budget == null)
            {
                budget = new Dictionary<SDS_StockKey, int>();
            }
            if (!budget.TryGetValue(key, out int left))
            {
                left = SDS_Availability.ClampTakeToAvailable(int.MaxValue,
                    SDS_Availability.GetTradableCount(key));
                budget[key] = left;
            }
            return left;
        }

        /// <summary>该交易行本次会被原版卖出的部分里是否含展示物（只读）。</summary>
        internal static bool HasConsumedDisplay(Tradeable tradeable)
        {
            if (tradeable == null || tradeable.ActionToDo != TradeAction.PlayerSells)
            {
                return false;
            }
            int remaining = tradeable.CountToTransferToDestination;
            if (remaining <= 0)
            {
                return false;
            }
            List<Thing> things = tradeable.thingsColony;
            for (int i = 0; i < things.Count && remaining > 0; i++)
            {
                Thing thing = things[i];
                if (thing == null)
                {
                    continue;
                }
                int take = Math.Min(remaining, thing.stackCount);
                if (take <= 0)
                {
                    continue;
                }
                if (SDS_OutputUtility.TryGetDisplayKey(thing, out _))
                {
                    return true;
                }
                remaining -= take;
            }
            return false;
        }

        /// <summary>卖侧唯一收口：按原版消费顺序把展示物替换成等量真身。</summary>
        internal static void SwapDisplaysForReal(Tradeable tradeable)
        {
            if (tradeable == null)
            {
                return;
            }
            List<Thing> old = tradeable.thingsColony;
            int remaining = tradeable.CountToTransferToDestination;
            if (remaining <= 0)
            {
                return;
            }
            List<Thing> rebuilt = new List<Thing>(old.Count);
            List<Thing> virtualReal = new List<Thing>();
            List<Thing> preservedReal = new List<Thing>();
            bool changed = false;
            try
            {
                for (int i = 0; i < old.Count; i++)
                {
                    Thing thing = old[i];
                    if (thing == null)
                    {
                        continue;
                    }
                    int take = remaining > 0 ? Math.Min(remaining, thing.stackCount) : 0;
                    if (take > 0 && SDS_OutputUtility.TryGetDisplayKey(thing, out _))
                    {
                        List<Thing> real = MaterializeDisplay(thing, take, out bool preserved);
                        rebuilt.AddRange(real);
                        if (preserved)
                        {
                            preservedReal.AddRange(real);
                        }
                        else
                        {
                            virtualReal.AddRange(real);
                        }
                        remaining -= take;
                        changed = true;
                        continue;
                    }
                    rebuilt.Add(thing);
                    if (take > 0)
                    {
                        remaining -= take;
                    }
                }
            }
            catch
            {
                SDS_Rules.ReturnThings(virtualReal, SDS_StockChannel.Virtual, RollbackMap(), null,
                    SDS_ReturnFallback.Ground);
                SDS_Rules.ReturnThings(preservedReal, SDS_StockChannel.Preserved, RollbackMap(), null,
                    SDS_ReturnFallback.Ground);
                throw;
            }
            if (changed)
            {
                old.Clear();
                old.AddRange(rebuilt);
            }
        }

        private static List<Thing> MaterializeDisplay(Thing display, int count, out bool preserved)
        {
            preserved = false;
            if (display == null || count <= 0 || display.def == null)
            {
                throw new InvalidOperationException("materialize precondition failed");
            }
            if (preservedBuckets.TryGet(display, out List<Thing> bucket))
            {
                preserved = true;
                SDS_StockKey preservedKey = SDS_OutputUtility.KeyOfDisplay(display, bucket);
                List<ThingCount> preservedPlan = SDS_PreservedBuckets.TakePlan(bucket, count, out int planned);
                if (preservedKey == null || !preservedKey.preserved || preservedPlan.Count == 0 || planned < count
                    || !SDS_Rules.TryWithdrawPreservedExact(preservedPlan, null, out List<Thing> kept)
                    || kept == null || kept.Count == 0)
                {
                    throw new InvalidOperationException(
                        "preserved materialize failed for " + display.def.defName);
                }
                if (CountUnits(kept) < count)
                {
                    SDS_Rules.ReturnThings(kept, SDS_StockChannel.Preserved, RollbackMap(), null,
                        SDS_ReturnFallback.Ground);
                    throw new InvalidOperationException(
                        "preserved materialize short for " + display.def.defName);
                }
                preservedBuckets.Remove(display);
                return kept;
            }
            SDS_StockKey key = SDS_OutputUtility.KeyOfDisplay(display, null);
            if (key == null || SDS_Availability.GetTradableCount(key) < count)
            {
                throw new InvalidOperationException("virtual materialize failed for " + display.def.defName);
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = count,
                    preserved = false
                },
                endpoint = null,
                mode = SDS_WithdrawMode.SpawnedTarget
            };
            if (!SDS_Rules.TryWithdraw(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                throw new InvalidOperationException("virtual materialize failed for " + display.def.defName);
            }
            if (CountUnits(made) < count)
            {
                SDS_Rules.ReturnThings(made, SDS_StockChannel.Virtual, RollbackMap(), null,
                    SDS_ReturnFallback.Ground);
                throw new InvalidOperationException("virtual materialize short for " + display.def.defName);
            }
            return made;
        }

        private static int CountUnits(List<Thing> things)
        {
            int units = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed)
                {
                    units += thing.stackCount;
                }
            }
            return units;
        }

        private static Map RollbackMap()
        {
            Map map = TradeSession.playerNegotiator != null ? TradeSession.playerNegotiator.Map : null;
            return map ?? Find.AnyPlayerHomeMap;
        }
    }

    /// <summary>保真展示物对应的「原版分组桶」：展示物 + 桶内真身 + 本次可见单位数。</summary>
    internal sealed class SDS_PreservedBucket
    {
        public Thing display;
        public List<Thing> things;
        public int units;
    }

    /// <summary>
    /// 保真物的「原版分组桶」统一构建器：交易、远征编成、运输舱、传送门共用同一套逻辑。
    /// 分组完全复用原版 TransferAsOne；数量按「该品类扣掉预留后的数量」封顶。
    /// </summary>
    internal static class SDS_PreservedBuckets
    {
        /// <summary>
        /// 把已过滤的保真真身按原版 TransferAsOne 分组（只分组，不建展示物、不读可用量）。
        /// 入：all = 已过滤真身；tradeGrouping / trader = 与展示层相同的分组模式。
        /// 出：互斥的原版分组桶列表。
        /// 区别：Build 用本方法分组后再按品类预算封顶；身份解析也复用本方法，避免第二套分桶。
        /// </summary>
        internal static List<List<Thing>> GroupThings(List<Thing> all, bool tradeGrouping, ITrader trader)
        {
            List<List<Thing>> buckets = new List<List<Thing>>();
            if (all == null)
            {
                return buckets;
            }
            for (int i = 0; i < all.Count; i++)
            {
                Thing thing = all[i];
                if (thing == null)
                {
                    continue;
                }
                if (tradeGrouping && thing is MinifiedThing)
                {
                    buckets.Add(new List<Thing> { thing });
                    continue;
                }
                TransferAsOneMode mode = TransferAsOneModeFor(thing, tradeGrouping, trader);
                bool added = false;
                for (int b = 0; b < buckets.Count; b++)
                {
                    if (tradeGrouping && buckets[b][0] is MinifiedThing)
                    {
                        continue;
                    }
                    if (TransferableUtility.TransferAsOne(thing, buckets[b][0], mode))
                    {
                        buckets[b].Add(thing);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    buckets.Add(new List<Thing> { thing });
                }
            }
            return buckets;
        }

        /// <summary>原版分组模式：编成 / 装载用 PodsOrCaravanPacking，交易按商人是否收购分流。</summary>
        internal static TransferAsOneMode TransferAsOneModeFor(Thing thing, bool tradeGrouping, ITrader trader)
        {
            if (!tradeGrouping)
            {
                return TransferAsOneMode.PodsOrCaravanPacking;
            }
            Thing inner = thing != null ? thing.GetInnerIfMinified() : null;
            return trader != null && trader.TraderKind != null && inner != null && inner.def != null
                && trader.TraderKind.WillTrade(inner.def)
                ? TransferAsOneMode.Normal
                : TransferAsOneMode.InactiveTradeable;
        }

        /// <summary>
        /// 按展示物身份解析它所属的**唯一**保真桶（读档 / 缓存丢失兜底）。
        /// 入：display = 展示物或真身；allPreserved = 当前存活真身；tradeGrouping / trader = 与展示层相同的分组模式。
        /// 出：唯一桶；candidateBuckets = 命中桶数（0 / &gt;1 时返回 null）。
        /// 区别：先用原版 TransferAsOne 重建 canonical 桶，再按桶整体匹配；绝不按 key 聚合多个桶（W45 / W47）。
        /// </summary>
        internal static List<Thing> ResolveForDisplay(ISDS_LedgerRead ledger, Thing display,
            List<Thing> allPreserved, bool tradeGrouping, ITrader trader, out int candidateBuckets)
        {
            candidateBuckets = 0;
            if (ledger == null || display == null || allPreserved == null || allPreserved.Count == 0)
            {
                return null;
            }
            List<Thing> filtered = new List<Thing>(allPreserved.Count);
            for (int i = 0; i < allPreserved.Count; i++)
            {
                Thing thing = allPreserved[i];
                if (thing == null || thing.Destroyed || !ledger.IsPreserved(thing)
                    || !SDS_OutputUtility.HasCompleteTransferIdentity(thing)
                    || (thing is MinifiedThing minified && minified.InnerThing == null))
                {
                    continue;
                }
                filtered.Add(thing);
            }
            List<List<Thing>> buckets = GroupThings(filtered, tradeGrouping, trader);
            List<Thing> found = null;
            for (int b = 0; b < buckets.Count; b++)
            {
                List<Thing> bucket = buckets[b];
                bool match = false;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Thing thing = bucket[i];
                    if (TransferableUtility.TransferAsOne(display, thing,
                        TransferAsOneModeFor(thing, tradeGrouping, trader)))
                    {
                        match = true;
                        break;
                    }
                }
                if (!match)
                {
                    continue;
                }
                candidateBuckets++;
                found = bucket;
                if (candidateBuckets > 1)
                {
                    return null;
                }
            }
            return candidateBuckets == 1 ? found : null;
        }

        /// <summary>两个桶是否同一个：引用相同，或桶内任一真身重叠（W45 的唯一判定）。</summary>
        internal static bool SameBucket(List<Thing> a, List<Thing> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null || a.Count == 0 || b.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                Thing thing = a[i];
                if (thing != null && b.Contains(thing))
                {
                    return true;
                }
            }
            return false;
        }

        public static List<SDS_PreservedBucket> Build(ISDS_LedgerRead ledger,
            Predicate<Thing> include, bool tradeGrouping, ITrader trader, bool tradableBudget)
        {
            List<SDS_PreservedBucket> result = new List<SDS_PreservedBucket>();
            if (ledger == null)
            {
                return result;
            }
            List<Thing> all = new List<Thing>();
            ledger.AppendPreservedThings(all);
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Thing thing = all[i];
                if (thing == null || thing.Destroyed
                    || !SDS_OutputUtility.HasCompleteTransferIdentity(thing)
                    || (thing is MinifiedThing minified && minified.InnerThing == null)
                    || (include != null && !include(thing)))
                {
                    all.RemoveAt(i);
                }
            }
            if (all.Count == 0)
            {
                return result;
            }
            // ---------- 1) 分组（原版规则；与身份解析共用 GroupThings） ----------
            List<List<Thing>> buckets = GroupThings(all, tradeGrouping, trader);
            // ---------- 2) 每桶一个展示物，数量按品类键预算封顶 ----------
            Dictionary<SDS_StockKey, long> budget = new Dictionary<SDS_StockKey, long>();
            for (int b = 0; b < buckets.Count; b++)
            {
                List<Thing> bucket = buckets[b];
                if (bucket.Count == 0)
                {
                    continue;
                }
                if (!SDS_Rules.TryGetStockKey(bucket[0], out SDS_StockKey key) || key == null)
                {
                    continue;
                }
                if (!budget.TryGetValue(key, out long available))
                {
                    available = tradableBudget
                        ? SDS_Availability.GetTradableCount(key)
                        : SDS_Availability.GetAvailableCount(key);
                    budget[key] = available;
                }
                if (available <= 0)
                {
                    continue;
                }
                long units = 0;
                for (int i = 0; i < bucket.Count; i++)
                {
                    units += bucket[i].stackCount;
                }
                int wantedUnits = (int)Math.Min(units, int.MaxValue);
                int show = SDS_Availability.ClampTakeToAvailable(wantedUnits, available);
                if (show <= 0)
                {
                    continue;
                }
                budget[key] = available - show;
                Thing display = SDS_OutputUtility.CachedPreservedBucketDisplay(bucket[0]);
                if (display == null)
                {
                    continue;
                }
                display.stackCount = show;
                result.Add(new SDS_PreservedBucket
                {
                    display = display,
                    things = bucket,
                    units = show
                });
            }
            return result;
        }

        /// <summary>从桶里按顺序取最多 wantedUnits 个单位的真身，生成精确取出计划。</summary>
        // RULE:W24
        public static List<ThingCount> TakePlan(List<Thing> bucket, int wantedUnits,
            out int plannedUnits, HashSet<Thing> excluded = null)
        {
            plannedUnits = 0;
            List<ThingCount> plan = new List<ThingCount>();
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || bucket == null || wantedUnits <= 0)
            {
                return plan;
            }
            int need = wantedUnits;
            for (int i = 0; i < bucket.Count && need > 0; i++)
            {
                Thing thing = bucket[i];
                if (thing == null || thing.Destroyed || !ledger.IsPreserved(thing)
                    || (excluded != null && excluded.Contains(thing)))
                {
                    continue;
                }
                int take = Math.Min(need, thing.stackCount);
                if (take <= 0)
                {
                    continue;
                }
                plan.Add(new ThingCount(thing, take));
                plannedUnits += take;
                need -= take;
            }
            return plan;
        }
    }

    /// <summary>通讯信标 / 米莉拉捐献的读出口与取出口。</summary>
    internal static class SDS_BeaconDisplay
    {
        public static bool HasPoweredBeacon(Map map)
        {
            if (map == null)
            {
                return false;
            }
            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                if (beacon != null)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsLaunchFromColony(Map map, ITrader trader)
        {
            return trader == null && map != null && HasPoweredBeacon(map)
                && SDS_Exposure.MapExposesGrid(map);
        }

        public static IEnumerable<Thing> Append(IEnumerable<Thing> original, Map map)
        {
            if (original != null)
            {
                foreach (Thing t in original)
                {
                    yield return t;
                }
            }
            foreach (Thing t in Displays(map))
            {
                yield return t;
            }
        }

        private static IEnumerable<Thing> Displays(Map map)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Exposure.MapExposesGrid(map))
            {
                yield break;
            }
            foreach (Thing fake in SDS_TradeVirtual.VirtualDisplayStream(ledger, null, null))
            {
                yield return fake;
            }
            List<Thing> preservedDisplays = SDS_TradeVirtual.BuildPreservedDisplays(ledger, null);
            for (int i = 0; i < preservedDisplays.Count; i++)
            {
                Thing display = preservedDisplays[i];
                if (display != null && !(display is MinifiedThing))
                {
                    yield return display;
                }
            }
        }

        public static int CountLaunchableOnMap(ThingDef resDef, Map map)
        {
            if (resDef == null || map == null)
            {
                return 0;
            }
            int total = 0;
            HashSet<Thing> seen = new HashSet<Thing>();
            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                if (beacon == null)
                {
                    continue;
                }
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    foreach (Thing t in map.thingGrid.ThingsAt(cell))
                    {
                        if (t == null || t.Destroyed || t.def != resDef || !seen.Add(t))
                        {
                            continue;
                        }
                        if (!TradeUtility.PlayerSellableNow(t, null))
                        {
                            continue;
                        }
                        total += t.stackCount;
                    }
                }
            }
            return total;
        }

        public static int ConsumeFromGrid(ThingDef resDef, int count)
        {
            if (resDef == null || count <= 0 || !SDS_Rules.Available)
            {
                return 0;
            }
            int remaining = count;
            int done = 0;
            while (remaining > 0)
            {
                // 交易 / 发射口径的唯一选键实现（虚拟精确档 → 可用量最大 → 保真回退，不因禁用归零）。
                SDS_StockKey key = SDS_Rules.TryFindTradableKey(resDef, out SDS_StockKey found, out _)
                    ? found
                    : null;
                if (key == null)
                {
                    break;
                }
                long available = SDS_Availability.GetTradableCount(key);
                if (available <= 0)
                {
                    break;
                }
                int chunk = SDS_Availability.ClampTakeToAvailable(remaining, available);
                if (chunk <= 0)
                {
                    break;
                }
                if (key.preserved)
                {
                    List<Thing> matches = new List<Thing>();
                    SDS_Core.Ledger.AppendPreservedThings(matches);
                    for (int i = matches.Count - 1; i >= 0; i--)
                    {
                        if (!SDS_Rules.TryGetStockKey(matches[i], out SDS_StockKey pk)
                            || pk == null || !pk.SameAs(key))
                        {
                            matches.RemoveAt(i);
                        }
                    }
                    List<ThingCount> plan = SDS_PreservedBuckets.TakePlan(matches, chunk, out int planned);
                    if (planned <= 0 || plan.Count == 0
                        || !SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> real)
                        || real == null || real.Count == 0)
                    {
                        break;
                    }
                    int got = 0;
                    for (int i = 0; i < real.Count; i++)
                    {
                        Thing t = real[i];
                        if (t != null && !t.Destroyed)
                        {
                            got += t.stackCount;
                            t.Destroy();
                        }
                    }
                    if (got <= 0)
                    {
                        break;
                    }
                    done += got;
                    remaining -= got;
                    continue;
                }
                SDS_WithdrawPlan planVirtual = new SDS_WithdrawPlan
                {
                    query = new SDS_WithdrawQuery
                    {
                        key = key,
                        quantity = chunk,
                        preserved = false
                    },
                    endpoint = null,
                    mode = SDS_WithdrawMode.SpawnedTarget
                };
                if (!SDS_Rules.TryWithdraw(planVirtual, null, out List<Thing> made)
                    || made == null || made.Count == 0)
                {
                    break;
                }
                int gotVirtual = 0;
                for (int i = 0; i < made.Count; i++)
                {
                    Thing t = made[i];
                    if (t != null && !t.Destroyed)
                    {
                        gotVirtual += t.stackCount;
                        t.Destroy();
                    }
                }
                if (gotVirtual <= 0)
                {
                    break;
                }
                done += gotVirtual;
                remaining -= gotVirtual;
            }
            return done;
        }
    }

    /// <summary>远程交易门控（高级芯片 B + 跨地图科技 + 全局可用）。</summary>
    internal static class SDS_RemoteTrade
    {
        public static bool CanUseRemote(Pawn negotiator)
        {
            if (negotiator == null || negotiator.Dead || negotiator.InMentalState)
            {
                return false;
            }
            if (negotiator.Faction != Faction.OfPlayer)
            {
                return false;
            }
            if (SDS_ImplantUtility.EffectiveChipDef(negotiator) != SDS_HediffDefOf.ChipB)
            {
                return false;
            }
            if (!SDS_ResearchUtility.CrossMapUnlocked)
            {
                return false;
            }
            return SDS_Rules.Available;
        }

        public static IEnumerable<Thing> AppendVirtualDisplays(IEnumerable<Thing> original, Pawn negotiator)
        {
            if (original != null)
            {
                foreach (Thing t in original)
                {
                    if (t != null)
                    {
                        yield return t;
                    }
                }
            }
            if (!CanUseRemote(negotiator) || SDS_Core.Ledger == null)
            {
                yield break;
            }
            ITrader trader = TradeSession.trader;
            foreach (Thing fake in SDS_TradeVirtual.VirtualDisplayStream(SDS_Core.Ledger, trader, null))
            {
                yield return fake;
            }
            List<Thing> preservedDisplays = SDS_TradeVirtual.BuildTradePreservedDisplays(
                SDS_Core.Ledger, trader);
            for (int i = 0; i < preservedDisplays.Count; i++)
            {
                yield return preservedDisplays[i];
            }
        }

        /// <summary>买入交付拦截：把买到的货直接并入超维空间。</summary>
        public static bool TryTakeBoughtIntoGrid(Thing toGive, int countToGive, Pawn negotiator)
        {
            if (toGive == null || toGive.Destroyed || toGive is Pawn)
            {
                return true;
            }
            if (!CanUseRemote(negotiator) || !SDS_Rules.CanDeposit(toGive))
            {
                return true;
            }
            int amount = Math.Min(countToGive, toGive.stackCount);
            if (amount <= 0)
            {
                return true;
            }
            Thing split = toGive.SplitOff(amount);
            if (split == null)
            {
                return true;
            }
            split.PreTraded(TradeAction.PlayerBuys, negotiator, TradeSession.trader);
            if (SDS_Rules.TryDeposit(split, DirectDepositPort, out int accepted) && accepted > 0)
            {
                return false;
            }
            RestoreBuySplit(toGive, split);
            return true;
        }

        private static readonly SDS_DirectDepositPort DirectDepositPort = new SDS_DirectDepositPort();

        private static void RestoreBuySplit(Thing toGive, Thing split)
        {
            if (split == null || split.Destroyed)
            {
                return;
            }
            ThingOwner owner = toGive != null ? toGive.holdingOwner : null;
            if (owner != null && owner.TryAdd(split, false))
            {
                return;
            }
            SDS_PlacementUtility.TryPlaceOrSpawn(split,
                toGive != null && toGive.Spawned ? toGive.Position : IntVec3.Invalid,
                toGive != null ? toGive.Map : Find.AnyPlayerHomeMap);
        }
    }

    /// <summary>跨地图能力判定（唯一消费者：远程交易）。</summary>
    internal static class SDS_ResearchUtility
    {
        public const string AdvancedPrecisionName = "SDS_AdvancedPrecisionSuperStorage";

        private static bool lookedUp;
        private static ResearchProjectDef advancedPrecision;

        private static void EnsureLookup()
        {
            if (lookedUp)
            {
                return;
            }
            lookedUp = true;
            advancedPrecision = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(AdvancedPrecisionName);
            if (advancedPrecision == null)
            {
                Log.Error("[超维存储] 找不到 ResearchProjectDef " + AdvancedPrecisionName
                    + "：跨地图交易不会启用（Game 可正常运行）。请检查 Defs/SDS_Research.xml。");
            }
        }

        public static ResearchProjectDef AdvancedPrecision
        {
            get
            {
                EnsureLookup();
                return advancedPrecision;
            }
        }

        public static bool CrossMapUnlocked
        {
            get
            {
                ResearchProjectDef def = AdvancedPrecision;
                if (def == null || Current.ProgramState != ProgramState.Playing
                    || Find.ResearchManager == null)
                {
                    return false;
                }
                return def.IsFinished;
            }
        }
    }
}
