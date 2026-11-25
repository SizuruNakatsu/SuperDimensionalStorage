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

    // ============================ 运输舱 / 传送门装载 ============================

    /// <summary>
    /// 装载 / 运输入口：把超维空间品类接进运输舱装载对话框与传送门进入对话框。
    ///
    /// 接法（不落地、不直接进舱，最终表现形式 = 原版搬运）：
    ///  1. 装载清单里注入展示物（无地图、无 holder），让玩家看得到、算得进质量、选得了；
    ///  2. 原版派活时地图上找不到实物、而超维空间能供给，改派 SDS_WithdrawForTransportLoad ——
    ///     小人先走到最近端点把货取到手上，再交回原版 HaulToTransporter / HaulToPortal；
    ///  3. 数量守恒：取不出来就退回超维空间，绝不吞物品。
    /// </summary>
    internal static class SDS_TransportVirtual
    {
        /// <summary>装载清单里的保真展示物 → 原版分组桶（桶内是真实真身）。</summary>
        private static readonly SDS_DisplayBucketRegistry preservedBuckets =
            new SDS_DisplayBucketRegistry();

        /// <summary>按 loadID 索引的「这次装载该取哪几件真身」待用计划（由派活登记、驱动器取用）。</summary>
        private static readonly Dictionary<int, List<ThingCount>> pendingLoadPlans =
            new Dictionary<int, List<ThingCount>>();

        /// <summary>计划登记的 tick（用于「本 tick 新建」有效性判定）。</summary>
        private static readonly Dictionary<int, int> pendingLoadPlanTick = new Dictionary<int, int>();

        /// <summary>按 loadID 索引的「这次装载该取哪一条品类键」待用表（Job 里只能放 ThingDef）。</summary>
        private static readonly Dictionary<int, SDS_StockKey> pendingLoadKeys =
            new Dictionary<int, SDS_StockKey>();

        /// <summary>同 tick 判定快照（HasJob 生成、JobOn 消费；按 tick 过期）。</summary>
        private sealed class SDS_TickSelection
        {
            public int tick;
            public bool vanilla;            // true = 原版 HasJob 已放行（JobOn 必须保留原版 Job）
            public SDS_LoadSelection selection;
        }

        private static readonly Dictionary<long, SDS_TickSelection> tickLoadSelections =
            new Dictionary<long, SDS_TickSelection>();

        internal static void RegisterPreservedBucket(Thing display, List<Thing> bucket)
        {
            preservedBuckets.Register(display, bucket);
        }

        internal static bool TryGetPreservedBucket(Thing display, out List<Thing> bucket)
        {
            return preservedBuckets.TryGet(display, out bucket);
        }

        /// <summary>清掉残留展示物登记（只解除引用，绝不销毁）。</summary>
        internal static void Reset()
        {
            // 残留的"待用品类键"一并清掉（那些 Job 已被取消 / 从未开起来）。
            pendingLoadKeys.Clear();
            pendingLoadPlans.Clear();
            pendingLoadPlanTick.Clear();
            tickLoadSelections.Clear();
            loadFailureUntil.Clear();
            preservedBuckets.Clear();
            SDS_OutputUtility.PruneBucketDisplays();
        }

        /// <summary>
        /// W44：对话框刷新只清理展示物身份缓存，**绝不**清跨容器的派活计划 / 快照 / 桶绑定
        /// （打开 A 舱的对话框不能把 B 舱正在执行的计划一起清掉）。
        /// </summary>
        public static void ResetForDialog()
        {
            SDS_OutputUtility.PruneBucketDisplays();
        }

        /// <summary>
        /// W38：把「行里的保真展示物」解析成它代表的**那一个**原版分组桶（纯读：不登记、不改展示物）。
        /// 未登记（AllSendableItems 这类只读枚举产出的穿梭机 autoload 行）时，
        /// 用原版 TransferAsOne(PodsOrCaravanPacking) 从账本重建 canonical 桶；
        /// 命中 0 个或多个桶一律返回 null（fail-closed），绝不按 key 聚合（W45 / W47）。
        /// </summary>
        private static List<Thing> ResolveRowBucket(ISDS_LedgerRead ledger, Thing display,
            List<Thing> allPreserved)
        {
            if (TryGetPreservedBucket(display, out List<Thing> registered) && registered != null)
            {
                return registered;
            }
            // 未登记时按原版 TransferAsOne 重建唯一桶；0 / 多个候选一律返回 null（fail-closed），
            // 绝不按键聚合多个桶（W45 / W47）。
            return SDS_PreservedBuckets.ResolveForDisplay(ledger, display, allPreserved, false, null, out _);
        }

        private static bool TransferablesContain(List<TransferableOneWay> transferables, Thing display)
        {
            if (transferables == null || display == null)
            {
                return false;
            }
            for (int i = 0; i < transferables.Count; i++)
            {
                TransferableOneWay t = transferables[i];
                if (t != null && t.things != null && t.things.Contains(display))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// W45：为桶取本对话框应使用的展示物。
        ///  - 同桶任一个展示物已在该对话框里 → 复用它（装载中重开去重）；
        ///  - 同桶只登记在别的容器 → 给本对话框一个新实例，避免跨容器共享 stackCount；
        ///  - 从未登记 → 用工厂给的缓存展示物。
        /// </summary>
        private static Thing ResolveBucketDisplay(List<TransferableOneWay> transferables,
            SDS_PreservedBucket bucket)
        {
            if (bucket == null || bucket.display == null || bucket.things == null || bucket.things.Count == 0)
            {
                return null;
            }
            Thing registered = null;
            foreach (KeyValuePair<Thing, List<Thing>> pair in preservedBuckets.Entries)
            {
                Thing candidate = pair.Key;
                if (candidate == null || candidate.Destroyed || !SDS_OutputUtility.IsDisplay(candidate)
                    || !SDS_PreservedBuckets.SameBucket(bucket.things, pair.Value))
                {
                    continue;
                }
                if (TransferablesContain(transferables, candidate))
                {
                    return candidate;
                }
                if (registered == null)
                {
                    registered = candidate;
                }
            }
            if (registered != null)
            {
                Thing fresh = SDS_OutputUtility.MakePreservedDisplayThingFrom(bucket.things[0]);
                return fresh != null ? fresh : registered;
            }
            return bucket.display;
        }

        /// <summary>
        /// 本容器此刻「在途」的实物单位数（按行），与 FindThingToLoad 的 tmpAlreadyLoading 同口径：
        /// 正在执行 HaulToTransporter / HaulToPortal、目的地是本容器、且 ThingToCarry 命中该行。
        /// </summary>
        private static Dictionary<TransferableOneWay, int> BuildInFlight(Thing container,
            List<TransferableOneWay> leftToLoad)
        {
            if (container == null || leftToLoad == null || container.Map == null)
            {
                return null;
            }
            // 口径说明：这里只统计「已被原版 HaulToTransporter/Portal 拿在手上」的量；
            // 虚拟通道的待派活不在此计入——选行是只读计划，真正的独占由 Job 首个 toil 的
            // TryReserveForJob 完成（W42/W43）。两段口径不得合并，否则会把同一批货算两次。
            Dictionary<TransferableOneWay, int> result = null;
            IReadOnlyList<Pawn> pawns = container.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.jobs == null)
                {
                    continue;
                }
                JobDef def = pawn.CurJobDef;
                if (def != JobDefOf.HaulToTransporter && def != JobDefOf.HaulToPortal)
                {
                    continue;
                }
                JobDriver_HaulToContainer driver = pawn.jobs.curDriver as JobDriver_HaulToContainer;
                if (driver == null || !ReferenceEquals(driver.Container, container))
                {
                    continue;
                }
                Thing carry = driver.ThingToCarry;
                if (carry == null)
                {
                    continue;
                }
                TransferableOneWay row = FindDepositRow(leftToLoad, carry);
                if (row == null)
                {
                    continue;
                }
                int units = 0;
                JobDriver_HaulToTransporter byTransporter = driver as JobDriver_HaulToTransporter;
                if (byTransporter != null)
                {
                    units = byTransporter.initialCount;
                }
                else
                {
                    JobDriver_HaulToPortal byPortal = driver as JobDriver_HaulToPortal;
                    if (byPortal != null)
                    {
                        units = byPortal.initialCount;
                    }
                }
                if (units <= 0)
                {
                    continue;
                }
                if (result == null)
                {
                    result = new Dictionary<TransferableOneWay, int>();
                }
                result.TryGetValue(row, out int already);
                result[row] = already + units;
            }
            return result;
        }

        /// <summary>
        /// W40 的唯一身份入口：为「刚装进容器的真身」找回它所属的原版装载行。
        ///  1. 真身已经在某行 things 里 → 就是那一行；
        ///  2. 某行展示物登记的保真桶包含该真身 → 就是那一行；
        ///  3. 某行展示物的封存品类键 == 真身的键 → 就是那一行（同键多行时返回 null 交回原版）。
        /// </summary>
        private static TransferableOneWay FindDepositRow(List<TransferableOneWay> rows, Thing thing)
        {
            if (rows == null || rows.Count == 0 || thing == null || thing.Destroyed)
            {
                return null;
            }
            // 1) 已在某行
            for (int i = 0; i < rows.Count; i++)
            {
                TransferableOneWay row = rows[i];
                if (row != null && row.things != null && row.things.Contains(thing))
                {
                    return row;
                }
            }
            // 2) 登记桶包含真身
            for (int i = 0; i < rows.Count; i++)
            {
                TransferableOneWay row = rows[i];
                if (row == null || row.things == null)
                {
                    continue;
                }
                for (int j = 0; j < row.things.Count; j++)
                {
                    Thing display = row.things[j];
                    if (!SDS_OutputUtility.IsDisplay(display))
                    {
                        continue;
                    }
                    if (TryGetPreservedBucket(display, out List<Thing> bucket)
                        && bucket != null && bucket.Contains(thing))
                    {
                        return row;
                    }
                }
            }
            // 3) 封存键（仅无登记桶的行）
            SDS_StockKey thingKey = SDS_OutputUtility.KeyOfThing(thing);
            TransferableOneWay found = null;
            for (int i = 0; i < rows.Count; i++)
            {
                TransferableOneWay row = rows[i];
                if (row == null || row.things == null)
                {
                    continue;
                }
                for (int j = 0; j < row.things.Count; j++)
                {
                    Thing display = row.things[j];
                    if (!SDS_OutputUtility.IsDisplay(display)
                        || TryGetPreservedBucket(display, out List<Thing> _))
                    {
                        continue;
                    }
                    SDS_StockKey key = SDS_OutputUtility.KeyOfDisplay(display, null);
                    if (key == null || thingKey == null)
                    {
                        continue;
                    }
                    if (key.preserved != thingKey.preserved || !key.SameAs(thingKey))
                    {
                        continue;
                    }
                    if (found != null && !ReferenceEquals(found, row))
                    {
                        return null;   // 同键多行，无法唯一确定 → 交回原版
                    }
                    found = row;
                }
            }
            return found;
        }

        /// <summary>
        /// W40：装舱 / 传送门入舱时，把真身归一化进它所属的那一行 things，随后原版的
        /// SubtractFromToLoadList 就能用 things.Contains(真身) 精确命中（本方法只补引用，不改数量）。
        /// </summary>
        internal static void NormalizeDepositedThing(List<TransferableOneWay> rows, Thing thing)
        {
            try
            {
                if (rows == null || rows.Count == 0 || thing == null || thing.Destroyed)
                {
                    return;
                }
                if (SDS_OutputUtility.TryGetDisplayKey(thing, out SDS_StockKey _))
                {
                    return;   // 展示物本身永远不参与装舱记账
                }
                // 只处理"已在容器里 / 已在地图上"的真身；账本里未物化的真身不走这条路。
                if (thing.holdingOwner == null && !thing.Spawned)
                {
                    return;
                }
                TransferableOneWay target = FindDepositRow(rows, thing);
                if (target == null)
                {
                    return;   // 找不到唯一行：交回原版（保持现状）
                }
                for (int i = 0; i < rows.Count; i++)
                {
                    TransferableOneWay row = rows[i];
                    if (row == null || row.things == null)
                    {
                        continue;
                    }
                    if (ReferenceEquals(row, target))
                    {
                        if (!row.things.Contains(thing))
                        {
                            row.things.Add(thing);
                        }
                        continue;
                    }
                    // 同一真身只能属于一行：清掉其它行里的漂移引用，避免原版先命中错误行。
                    while (row.things.Remove(thing))
                    {
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("装载/装舱身份绑定失败（已忽略，交回原版）", e);
            }
        }

        /// <summary>该装载清单里是否已含我们的展示物（= 装载进行中：不重复注入、也不重置）。</summary>
        public static bool LeftToLoadHasFake(List<TransferableOneWay> leftToLoad)
        {
            if (leftToLoad == null)
            {
                return false;
            }
            for (int i = 0; i < leftToLoad.Count; i++)
            {
                TransferableOneWay t = leftToLoad[i];
                if (t == null || t.things == null)
                {
                    continue;
                }
                for (int j = 0; j < t.things.Count; j++)
                {
                    if (SDS_OutputUtility.IsDisplay(t.things[j]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // ============================ 注入（仅对话框展示） ============================

        /// <summary>运输舱装载清单：注入超维空间**所有**可装载品类（不做质量预封顶）。</summary>
        public static void InjectIntoTransporters(List<TransferableOneWay> transferables,
            List<CompTransporter> transporters, Map map)
        {
            if (transferables == null || transporters == null || transporters.Count == 0 || map == null)
            {
                return;
            }
            // 装载进行中（leftToLoad 里已有展示物）：原版每次打开对话框都会新建空 transferables，
            // 直接 return 会让列表变空、已选数量也恢复不了。这里把已有展示物实例补回新清单。
            bool loading = false;
            for (int i = 0; i < transporters.Count; i++)
            {
                CompTransporter t = transporters[i];
                if (t != null && LeftToLoadHasFake(t.leftToLoad))
                {
                    loading = true;
                    break;
                }
            }
            if (loading)
            {
                // 展示物位置只用于原版 CheckForErrors 的可达性检查：沿用"第一个运输舱交互格"。
                Thing anchor = transporters[0] != null ? transporters[0].parent : null;
                IntVec3 cell = IntVec3.Invalid;
                if (anchor != null)
                {
                    cell = anchor.InteractionCell.InBounds(map) ? anchor.InteractionCell : anchor.Position;
                }
                for (int i = 0; i < transporters.Count; i++)
                {
                    CompTransporter t = transporters[i];
                    if (t != null)
                    {
                        InjectExistingRows(transferables, t.leftToLoad, cell);
                    }
                }
                // 装载中重开对话框：除补回已有展示物外，必须继续注入当前仍可用的新品类。
                Inject(transferables, map, cell, skipExisting: true);
                return;
            }
            ResetForDialog();
            // 展示物的"落点"设成第一个运输舱的交互格：让原版 CheckForErrors 的可达性检查通过。
            Thing first = transporters[0] != null ? transporters[0].parent : null;
            IntVec3 displayCell = IntVec3.Invalid;
            if (first != null)
            {
                displayCell = first.InteractionCell.InBounds(map) ? first.InteractionCell : first.Position;
            }
            Inject(transferables, map, displayCell, skipExisting: false);
        }

        /// <summary>传送门进入清单：不校验质量，直接注入展示物。</summary>
        public static void InjectIntoPortal(List<TransferableOneWay> transferables, MapPortal portal)
        {
            if (transferables == null || portal == null || portal.Map == null)
            {
                return;
            }
            if (LeftToLoadHasFake(portal.leftToLoad))
            {
                // 装载中重开对话框：补回已有展示物实例，再注入当前仍可用的新品类。
                InjectExistingRows(transferables, portal.leftToLoad, IntVec3.Invalid);
                Inject(transferables, portal.Map, IntVec3.Invalid, skipExisting: true);
                return;
            }
            ResetForDialog();
            Inject(transferables, portal.Map, IntVec3.Invalid, skipExisting: false);
        }

        /// <summary>
        /// 把某个容器 leftToLoad 里已有的展示物实例补回新的 transferables。
        /// 只补引用、不改数量：数量由原版 SetLoadedItemsToLoad 按 AnyThing 恢复。
        /// </summary>
        private static void InjectExistingRows(List<TransferableOneWay> transferables,
            List<TransferableOneWay> rows, IntVec3 displayCell)
        {
            if (transferables == null || rows == null)
            {
                return;
            }
            for (int r = 0; r < rows.Count; r++)
            {
                TransferableOneWay row = rows[r];
                if (row == null || row.CountToTransfer <= 0 || row.things == null)
                {
                    continue;
                }
                // 必须补回行内**全部**展示物：autoload / 虚拟+保真混合行可以有多个展示物。
                for (int j = 0; j < row.things.Count; j++)
                {
                    Thing display = row.things[j];
                    if (!SDS_OutputUtility.IsDisplay(display))
                    {
                        continue;
                    }
                    if (displayCell.IsValid)
                    {
                        display.Position = displayCell;
                    }
                    TransferableOneWay target = TransferableUtility.TransferableMatching<TransferableOneWay>(
                        display, transferables, TransferAsOneMode.PodsOrCaravanPacking);
                    if (target == null)
                    {
                        target = new TransferableOneWay();
                        transferables.Add(target);
                    }
                    if (!target.things.Contains(display))
                    {
                        target.things.Add(display);
                    }
                }
            }
        }

        /// <summary>收集清单里已存在的虚拟身份（装载中重开对话框时用于去重）。</summary>
        private static void CollectPresentIdentities(List<TransferableOneWay> transferables,
            out HashSet<SDS_StockKey> virtualKeys)
        {
            virtualKeys = null;
            if (transferables == null)
            {
                return;
            }
            for (int i = 0; i < transferables.Count; i++)
            {
                TransferableOneWay t = transferables[i];
                if (t == null || t.things == null)
                {
                    continue;
                }
                for (int j = 0; j < t.things.Count; j++)
                {
                    Thing thing = t.things[j];
                    if (!SDS_OutputUtility.IsDisplay(thing))
                    {
                        continue;
                    }
                    if (TryGetPreservedBucket(thing, out List<Thing> _))
                    {
                        continue;   // 保真展示物按桶身份去重
                    }
                    SDS_StockKey key = SDS_OutputUtility.KeyOfDisplay(thing, null);
                    if (key != null && !key.preserved)
                    {
                        if (virtualKeys == null)
                        {
                            virtualKeys = new HashSet<SDS_StockKey>();
                        }
                        virtualKeys.Add(key);
                    }
                }
            }
        }

        /// <summary>
        /// 统一注入：把超维空间品类以展示物追加进 transferables。
        /// displayCell 有效时把展示物位置设到该格（仅供原版可达性检查用），Invalid 则跳过。
        /// 刻意**不做质量预封顶**：超容量由原版 CheckForErrors 自己拦下。
        /// </summary>
        private static void Inject(List<TransferableOneWay> transferables, Map map, IntVec3 displayCell,
            bool skipExisting)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available || !SDS_Exposure.MapExposesGrid(map))
            {
                return;
            }
            // 装载中重开对话框：只补**尚未出现**的品类，避免同一桶/同一键被注入第二个展示物。
            HashSet<SDS_StockKey> presentVirtual = null;
            if (skipExisting)
            {
                CollectPresentIdentities(transferables, out presentVirtual);
            }
            List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockEntry entry = snapshot[i];
                if (entry == null || entry.key == null || entry.count <= 0)
                {
                    continue;
                }
                ThingDef def = entry.key.thingDef;
                if (def == null || def.category != ThingCategory.Item || !def.canLoadIntoCaravan)
                {
                    continue;
                }
                if (presentVirtual != null && presentVirtual.Contains(entry.key))
                {
                    continue;
                }
                long available = LoadAvailable(entry.key);
                if (available <= 0)
                {
                    continue;
                }
                Thing fake = SDS_OutputUtility.CachedDisplayThing(entry.key);
                if (fake == null)
                {
                    continue;
                }
                TransferableOneWay t = TransferableUtility.TransferableMatching<TransferableOneWay>(
                    fake, transferables, TransferAsOneMode.PodsOrCaravanPacking);
                long room = Math.Max(0L, (long)int.MaxValue - (t != null ? t.MaxCount : 0));
                int amount = SDS_Availability.ClampTakeToAvailable((int)room, available);
                if (amount <= 0)
                {
                    continue;
                }
                fake.stackCount = amount;
                if (displayCell.IsValid)
                {
                    fake.Position = displayCell;
                }
                if (t == null)
                {
                    t = new TransferableOneWay();
                    transferables.Add(t);
                }
                // AllSendableItems 也可能已把同一展示物放进同一个 TransferableOneWay。
                if (!t.things.Contains(fake))
                {
                    t.things.Add(fake);
                }
            }
            // ---------- 保真通道（纯追加分支） ----------
            List<SDS_PreservedBucket> buckets = SDS_PreservedBuckets.Build(ledger,
                IncludeForTransportLoad, tradeGrouping: false, trader: null, tradableBudget: false);
            for (int i = 0; i < buckets.Count; i++)
            {
                SDS_PreservedBucket bucket = buckets[i];
                if (bucket == null || bucket.display == null || bucket.units <= 0
                    || bucket.things == null || bucket.things.Count == 0)
                {
                    continue;
                }
                // W45：按"桶身份"取展示物（同桶在该对话框已存在则复用；否则给本对话框独立实例）。
                Thing display = ResolveBucketDisplay(transferables, bucket);
                if (display == null)
                {
                    continue;
                }
                TransferableOneWay pt = TransferableUtility.TransferableMatching<TransferableOneWay>(
                    display, transferables, TransferAsOneMode.PodsOrCaravanPacking);
                if (pt != null && pt.things.Contains(display))
                {
                    // 本对话框已存在该展示物（装载中重开）：保持原数量，绝不重设 stackCount。
                    continue;
                }
                long proom = Math.Max(0L, (long)int.MaxValue - (pt != null ? pt.MaxCount : 0));
                long pamount = Math.Min((long)bucket.units, proom);
                if (pamount <= 0)
                {
                    continue;
                }
                display.stackCount = pamount > int.MaxValue ? int.MaxValue : (int)pamount;
                if (displayCell.IsValid)
                {
                    display.Position = displayCell;
                }
                if (pt == null)
                {
                    pt = new TransferableOneWay();
                    transferables.Add(pt);
                }
                if (!pt.things.Contains(display))
                {
                    pt.things.Add(display);
                }
                RegisterPreservedBucket(display, bucket.things);
            }
        }

        // ============================ 供给（取到手上 → 交原版搬进舱） ============================

        /// <summary>
        /// 装载行选行 + 出计划的唯一入口（纯读：不登记桶、不预留、不建 Job）。
        /// W25 装载腿：deficit = CountToTransfer − 在途量（不使用编成腿的实物量口径）；一行一趟只认一个桶。
        /// </summary>
        public static bool TrySelectLoadRow(Thing container, List<TransferableOneWay> leftToLoad,
            Pawn pawn, out SDS_LoadSelection selection)
        {
            selection = null;
            if (container == null || leftToLoad == null || pawn == null || pawn.Map == null)
            {
                return false;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available)
            {
                return false;
            }
            Dictionary<TransferableOneWay, int> inFlight = BuildInFlight(container, leftToLoad);
            HashSet<Thing> busy = BuildPendingBusy(container);
            List<Thing> allPreserved = null;
            for (int i = 0; i < leftToLoad.Count; i++)
            {
                TransferableOneWay t = leftToLoad[i];
                if (t == null || t.CountToTransfer <= 0 || t.things == null)
                {
                    continue;
                }
                int inFlightUnits = 0;
                if (inFlight != null)
                {
                    inFlight.TryGetValue(t, out inFlightUnits);
                }
                int deficit = t.CountToTransfer - inFlightUnits;
                if (deficit <= 0)
                {
                    continue;
                }
                // 该行必须带"我们的展示物"才算超维品类（纯地图实物行交原版）。
                SDS_StockKey virtualKey = null;
                SDS_StockKey fallbackKey = null;
                Thing fallbackDisplay = null;
                List<Thing> fallbackBucket = null;
                for (int j = 0; j < t.things.Count; j++)
                {
                    Thing thing = t.things[j];
                    if (!SDS_OutputUtility.IsDisplay(thing))
                    {
                        continue;
                    }
                    if (TryGetPreservedBucket(thing, out List<Thing> registered) && registered != null)
                    {
                        continue;   // 有登记桶 → 第二段按桶处理
                    }
                    SDS_StockKey displayKey = SDS_OutputUtility.KeyOfDisplay(thing, null);
                    if (displayKey == null || displayKey.thingDef == null)
                    {
                        continue;
                    }
                    if (displayKey.preserved)
                    {
                        if (fallbackKey == null && LoadAvailable(displayKey) > 0L)
                        {
                            fallbackKey = displayKey;
                            fallbackDisplay = thing;
                            if (allPreserved == null)
                            {
                                allPreserved = new List<Thing>();
                                ledger.AppendPreservedThings(allPreserved);
                            }
                            // 按「展示物 + 原版分组规则」找回它自己的桶，绝不按键聚合多桶。
                            fallbackBucket = ResolveRowBucket(ledger, thing, allPreserved);
                        }
                        continue;
                    }
                    if (virtualKey == null && LoadAvailable(displayKey) > 0L)
                    {
                        virtualKey = displayKey;
                    }
                }
                // 虚拟优先（与原实现同口径）。
                if (virtualKey != null)
                {
                    int take = ClampLoadTake(pawn, virtualKey, deficit);
                    SDS_WithdrawPlan virtualPlan = take > 0 ? DecideLoadPlan(pawn, virtualKey, take) : null;
                    if (virtualPlan != null)
                    {
                        selection = new SDS_LoadSelection
                        {
                            key = virtualKey,
                            count = take,
                            plan = null,
                            endpoint = virtualPlan.endpoint
                        };
                        return true;
                    }
                }
                // 保真：逐个展示物自己的桶，任选一个「本趟能整桶取到真身」的桶。
                for (int j = 0; j < t.things.Count; j++)
                {
                    Thing thing = t.things[j];
                    if (!SDS_OutputUtility.IsDisplay(thing)
                        || !TryGetPreservedBucket(thing, out List<Thing> bucket)
                        || bucket == null || bucket.Count == 0)
                    {
                        continue;
                    }
                    SDS_StockKey pk = SDS_OutputUtility.KeyOfDisplay(thing, bucket);
                    if (pk == null || pk.thingDef == null || LoadAvailable(pk) <= 0L)
                    {
                        continue;
                    }
                    int take = ClampLoadTake(pawn, pk, deficit);
                    if (take <= 0)
                    {
                        continue;
                    }
                    // 该展示物只代表本行绑定的那几件：一趟不得超过它的可代表单位。
                    int displayCap = Math.Max(1, thing.stackCount);
                    if (take > displayCap)
                    {
                        take = displayCap;
                    }
                    List<ThingCount> plan = SDS_PreservedBuckets.TakePlan(bucket, take, out int planned, busy);
                    if (planned <= 0 || plan == null || plan.Count == 0)
                    {
                        continue;   // 桶内已无活真身 / 全被其它 Job 计划：换桶或换行
                    }
                    SDS_WithdrawPlan withdrawPlan = DecideLoadPlan(pawn, pk, planned);
                    if (withdrawPlan == null)
                    {
                        continue;
                    }
                    selection = new SDS_LoadSelection
                    {
                        key = pk,
                        count = planned,
                        plan = plan,
                        endpoint = withdrawPlan.endpoint
                    };
                    return true;
                }
                // 无登记桶的保真兜底（穿梭机 autoload 行）：桶由 ResolveRowBucket 按展示物身份解析。
                if (fallbackKey != null && fallbackBucket != null && fallbackBucket.Count > 0)
                {
                    int take = ClampLoadTake(pawn, fallbackKey, deficit);
                    if (take > 0 && fallbackDisplay != null)
                    {
                        int displayCap = Math.Max(1, fallbackDisplay.stackCount);
                        if (take > displayCap)
                        {
                            take = displayCap;
                        }
                    }
                    if (take > 0)
                    {
                        List<ThingCount> plan = SDS_PreservedBuckets.TakePlan(fallbackBucket, take,
                            out int planned, busy);
                        if (planned > 0 && plan != null && plan.Count > 0)
                        {
                            SDS_WithdrawPlan withdrawPlan = DecideLoadPlan(pawn, fallbackKey, planned);
                            if (withdrawPlan != null)
                            {
                                selection = new SDS_LoadSelection
                                {
                                    key = fallbackKey,
                                    count = planned,
                                    plan = plan,
                                    endpoint = withdrawPlan.endpoint
                                };
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 一次装载派活选中的「通道 + 具体取货计划」。
        /// HasJob 与 JobOn 共用 TrySelectLoadRow 的同一判定，绝不把 null Job 交回原版。
        /// </summary>
        internal sealed class SDS_LoadSelection
        {
            public SDS_StockKey key;
            public int count;                    // 已按携带上限 / 欠量 / 可用量夹取
            public List<ThingCount> plan;        // 保真：本趟精确真身；虚拟：null
            public ISDS_Port endpoint;
        }

        /// <summary>装载选件的唯一数量口径 = 预留后可用量（Total − Reserved，禁用归零）。</summary>
        internal static long LoadAvailable(SDS_StockKey key)
        {
            return SDS_Availability.GetAvailableCount(key);
        }

        /// <summary>「这一趟最多取多少」的唯一夹取口径：携带上限（ClampCarry）∩ 欠量 ∩ 当前可用量。</summary>
        // RULE:W46
        private static int ClampLoadTake(Pawn pawn, SDS_StockKey key, int deficit)
        {
            if (pawn == null || key == null || key.thingDef == null || deficit <= 0)
            {
                return 0;
            }
            int carrySpace = pawn.carryTracker != null
                ? pawn.carryTracker.MaxStackSpaceEver(key.thingDef)
                : 0;
            int take = ClampCarry(deficit, carrySpace);
            if (take <= 0)
            {
                return 0;
            }
            long available = LoadAvailable(key);
            if (available <= 0L)
            {
                return 0;
            }
            if (take > available)
            {
                // W56：可取下限的唯一实现。
                take = SDS_Availability.ClampTakeToAvailable(take, available);
            }
            return take;
        }

        /// <summary>
        /// W46：本趟可携带量的唯一夹取实现（装载是空手规划口径，取 `MaxStackSpaceEver`）。
        /// 携带上限不可用（≤ 0）时返回 0：本趟不派活，不先在图上物化再失败。
        /// </summary>
        internal static int ClampCarry(int need, int carrySpace)
        {
            if (need <= 0)
            {
                return 0;
            }
            return Math.Min(need, Math.Max(0, carrySpace));
        }

        /// <summary>唯一取物决议：Core 统一端点决议（只读；没有端点返回 null）。</summary>
        private static SDS_WithdrawPlan DecideLoadPlan(Pawn pawn, SDS_StockKey key, int take)
        {
            if (pawn == null || key == null || take <= 0)
            {
                return null;
            }
            SDS_EndpointQuery query = SDS_EndpointQuery.WithdrawPoint(pawn);
            query.key = key;
            query.map = pawn.Map;
            query.requireStock = true;
            SDS_EndpointDecision decision = SDS_Rules.SelectEndpoint(query);
            if (!decision.allowed || decision.port == null || decision.port.Thing == null)
            {
                return null;
            }
            return new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = take,
                    preserved = key.preserved
                },
                endpoint = decision.port,
                mode = SDS_WithdrawMode.Hand
            };
        }

        /// <summary>
        /// 其它在途 Job 已登记的真身（跨行防重复计划）。
        /// 计划只在「本 tick 新建」或「仍是某个小人的当前 Job / 待执行队列」时算占用；其余当场回收。
        /// </summary>
        private static HashSet<Thing> BuildPendingBusy(Thing container)
        {
            if (pendingLoadPlans.Count == 0)
            {
                return null;
            }
            int now = GenTicks.TicksGame;
            HashSet<Thing> busy = null;
            List<int> stale = null;
            foreach (KeyValuePair<int, List<ThingCount>> pair in pendingLoadPlans)
            {
                int loadID = pair.Key;
                bool active = !pendingLoadPlanTick.TryGetValue(loadID, out int registered)
                    || registered == now
                    || IsLoadJobCurrent(container, loadID);
                if (!active)
                {
                    if (stale == null)
                    {
                        stale = new List<int>();
                    }
                    stale.Add(loadID);
                    continue;
                }
                List<ThingCount> other = pair.Value;
                if (other == null)
                {
                    continue;
                }
                for (int i = 0; i < other.Count; i++)
                {
                    if (other[i].Thing == null)
                    {
                        continue;
                    }
                    if (busy == null)
                    {
                        busy = new HashSet<Thing>();
                    }
                    busy.Add(other[i].Thing);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    int id = stale[i];
                    pendingLoadPlans.Remove(id);
                    pendingLoadPlanTick.Remove(id);
                    pendingLoadKeys.Remove(id);
                }
            }
            return busy;
        }

        /// <summary>该 loadID 是否仍是某个已 spawn 小人的 Job（当前 Job 或待执行队列）。</summary>
        private static bool IsLoadJobCurrent(Thing container, int loadID)
        {
            Map map = container != null ? container.Map : null;
            if (map == null || map.mapPawns == null)
            {
                return false;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null)
                {
                    continue;
                }
                Job job = pawn.CurJob;
                if (job != null && job.loadID == loadID)
                {
                    return true;
                }
                // JobGiver 可能先经 TryTakeOrderedJob 入队，排队期间同样是「这个 Job 的预留」。
                JobQueue queue = pawn.jobs != null ? pawn.jobs.jobQueue : null;
                if (queue == null)
                {
                    continue;
                }
                foreach (QueuedJob queued in queue)
                {
                    if (queued != null && queued.job != null && queued.job.loadID == loadID)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// HasJob 成功后登记本 tick 的判定快照，供同一 pass 的 JobOn 直接消费。
        /// vanilla=true 表示原版 HasJob 已放行（selection 可为 null）；JobOn 必须保留原版 Job。
        /// </summary>
        internal static void RememberSelection(Pawn pawn, Thing container, SDS_LoadSelection selection, bool vanilla)
        {
            if (pawn == null || container == null)
            {
                return;
            }
            int now = GenTicks.TicksGame;
            // 早于本 tick 的条目一律作废（只在同一 tick 内有效）。
            if (tickLoadSelections.Count > 8)
            {
                List<long> stale = null;
                foreach (KeyValuePair<long, SDS_TickSelection> pair in tickLoadSelections)
                {
                    if (pair.Value == null || pair.Value.tick != now)
                    {
                        if (stale == null)
                        {
                            stale = new List<long>();
                        }
                        stale.Add(pair.Key);
                    }
                }
                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        tickLoadSelections.Remove(stale[i]);
                    }
                }
            }
            tickLoadSelections[LoadFailureKey(pawn, container)] = new SDS_TickSelection
            {
                tick = now,
                vanilla = vanilla,
                selection = selection
            };
        }

        /// <summary>
        /// JobOn 读取 HasJob 的快照（同 tick 同 pawn 同容器）。
        /// 同 tick 内**只读、可重复消费**：落点格守卫的 JobOnThing 探针会先读一次。
        /// </summary>
        internal static bool TryTakeSelection(Pawn pawn, Thing container, out bool vanilla,
            out SDS_LoadSelection selection)
        {
            vanilla = false;
            selection = null;
            if (pawn == null || container == null)
            {
                return false;
            }
            long key = LoadFailureKey(pawn, container);
            if (!tickLoadSelections.TryGetValue(key, out SDS_TickSelection entry) || entry == null)
            {
                return false;
            }
            if (entry.tick != GenTicks.TicksGame)
            {
                tickLoadSelections.Remove(key);
                return false;
            }
            vanilla = entry.vanilla;
            selection = entry.selection;
            return vanilla || selection != null;
        }

        /// <summary>
        /// 把选好的行变成 Job（只建 Job + 登记待用品类键 / 计划）。
        /// 可行性、取量夹取、端点决议都已在选行阶段完成，这里不再自行判库存。
        /// </summary>
        internal static Job MakeLoadJob(Pawn pawn, Thing container, SDS_LoadSelection selection)
        {
            JobDef jobDef = SDS_JobDefOf.WithdrawForTransportLoad;
            if (jobDef == null || pawn == null || pawn.Map == null || container == null
                || selection == null || selection.key == null || selection.key.thingDef == null
                || selection.count <= 0 || selection.endpoint == null || selection.endpoint.Thing == null)
            {
                return null;
            }
            Job job = JobMaker.MakeJob(jobDef, new LocalTargetInfo(selection.endpoint.Thing),
                new LocalTargetInfo(container));
            job.thingDefToCarry = selection.key.thingDef;
            job.count = selection.count;
            // 落点格守卫会先做一次 JobOnThing 探针并丢弃结果，
            // 探针期间只造 Job、不登记待用键 / 计划，避免幽灵计划永久占住真身。
            if (!SDS_CellOnlyWorkGiverPatches.IsProbing)
            {
                pendingLoadKeys[job.loadID] = selection.key;
                if (selection.plan != null && selection.plan.Count > 0)
                {
                    pendingLoadPlans[job.loadID] = selection.plan;
                    pendingLoadPlanTick[job.loadID] = GenTicks.TicksGame;
                }
            }
            return job;
        }

        /// <summary>取走该 Job 的品类键（取一次即删；没有登记过返回 null，调用方退回按 def 找）。</summary>
        public static SDS_StockKey TakePendingLoadKey(int jobLoadID)
        {
            if (!pendingLoadKeys.TryGetValue(jobLoadID, out SDS_StockKey key))
            {
                return null;
            }
            pendingLoadKeys.Remove(jobLoadID);
            return key;
        }

        /// <summary>查看该 Job 的保真取出计划（不删除；预留腿与物化腿都要用）。</summary>
        public static List<ThingCount> PeekPendingLoadPlan(int jobLoadID)
        {
            return pendingLoadPlans.TryGetValue(jobLoadID, out List<ThingCount> plan) ? plan : null;
        }

        /// <summary>
        /// W47：读档后按账本里该 Job 的唯一预留键，从容器内该键所属的那一个原版分组桶重建保真计划。
        /// 只读预留，不新建预留；找不到桶、同键多桶、桶内可计划单位不足额一律 fail-closed。
        /// </summary>
        internal static bool TryRebuildPreservedLoadPlan(Thing container, int jobLoadID,
            out List<ThingCount> plan)
        {
            plan = null;
            if (container == null || jobLoadID == 0)
            {
                return false;
            }
            if (!SDS_Rules.TryGetJobReservation(jobLoadID, out SDS_StockKey key, out int total)
                || key == null || !key.preserved || total <= 0)
            {
                return false;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            List<TransferableOneWay> rows = LeftToLoadOf(container);
            if (ledger == null || rows == null || rows.Count == 0)
            {
                return false;
            }
            List<Thing> allPreserved = new List<Thing>();
            ledger.AppendPreservedThings(allPreserved);
            List<Thing> matched = null;
            for (int r = 0; r < rows.Count; r++)
            {
                TransferableOneWay row = rows[r];
                if (row == null || row.things == null)
                {
                    continue;
                }
                for (int i = 0; i < row.things.Count; i++)
                {
                    Thing display = row.things[i];
                    if (!SDS_OutputUtility.IsDisplay(display))
                    {
                        continue;
                    }
                    List<Thing> bucket = ResolveRowBucket(ledger, display, allPreserved);
                    if (bucket == null || bucket.Count == 0)
                    {
                        continue;
                    }
                    SDS_StockKey bucketKey = SDS_OutputUtility.KeyOfThing(bucket[0]);
                    if (bucketKey == null || !bucketKey.preserved || !bucketKey.SameAs(key))
                    {
                        continue;
                    }
                    if (matched == null)
                    {
                        matched = bucket;
                    }
                    else if (!SDS_PreservedBuckets.SameBucket(matched, bucket))
                    {
                        // 同键对应多个不同桶：无法唯一确定；绝不按键聚合，直接 fail-closed。
                        return false;
                    }
                }
            }
            if (matched == null)
            {
                return false;
            }
            HashSet<Thing> busy = BuildPendingBusy(container);
            List<ThingCount> rebuilt = SDS_PreservedBuckets.TakePlan(matched, total, out int planned, busy);
            if (rebuilt == null || rebuilt.Count == 0 || planned != total)
            {
                return false;
            }
            plan = rebuilt;
            return true;
        }

        /// <summary>W47：把重建出的保真计划登记为运行时计划，供预留腿与物化腿复用。</summary>
        internal static void RegisterRebuiltLoadPlan(int jobLoadID, List<ThingCount> plan)
        {
            if (jobLoadID == 0 || plan == null || plan.Count == 0)
            {
                return;
            }
            pendingLoadPlans[jobLoadID] = plan;
            pendingLoadPlanTick[jobLoadID] = GenTicks.TicksGame;
        }

        /// <summary>
        /// 读档归一化：displayKeys 不落盘；对容器行内未 spawn、无 holder 的持久化展示物补封存。
        /// 只认唯一保真桶或虚拟键；同键多桶无法唯一解析时保持未绑定，按非展示物 fail-closed。
        /// </summary>
        internal static void RebindLoadedDisplayKeys()
        {
            try
            {
                ISDS_LedgerRead ledger = SDS_Core.Ledger;
                List<Map> maps = Find.Maps;
                if (ledger == null || maps == null)
                {
                    return;
                }
                List<Thing> allPreserved = new List<Thing>();
                ledger.AppendPreservedThings(allPreserved);
                for (int m = 0; m < maps.Count; m++)
                {
                    Map map = maps[m];
                    if (map == null || map.Disposed || map.listerThings == null)
                    {
                        continue;
                    }
                    List<Thing> all = map.listerThings.AllThings;
                    for (int i = 0; i < all.Count; i++)
                    {
                        Thing host = all[i];
                        if (host == null || host.Destroyed)
                        {
                            continue;
                        }
                        if (host.TryGetComp<CompTransporter>() == null && !(host is MapPortal))
                        {
                            continue;
                        }
                        List<TransferableOneWay> rows = LeftToLoadOf(host);
                        if (rows == null)
                        {
                            continue;
                        }
                        for (int r = 0; r < rows.Count; r++)
                        {
                            TransferableOneWay row = rows[r];
                            if (row == null || row.things == null)
                            {
                                continue;
                            }
                            for (int t = 0; t < row.things.Count; t++)
                            {
                                Thing display = row.things[t];
                                if (display == null || display.Destroyed || display.Spawned
                                    || display.ParentHolder != null || display is Pawn)
                                {
                                    continue;
                                }
                                if (SDS_OutputUtility.TryGetDisplayKey(display, out SDS_StockKey _))
                                {
                                    continue;
                                }
                                List<Thing> bucket = SDS_PreservedBuckets.ResolveForDisplay(
                                    ledger, display, allPreserved, false, null, out int candidates);
                                SDS_StockKey key = bucket != null
                                    ? SDS_OutputUtility.KeyOfDisplay(display, bucket)
                                    : (candidates == 0 ? SDS_OutputUtility.KeyOfThing(display) : null);
                                if (key != null)
                                {
                                    SDS_OutputUtility.BindDisplayKey(display, key);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("读档展示物补封存失败（已跳过）", e);
            }
        }

        private static List<TransferableOneWay> LeftToLoadOf(Thing container)
        {
            if (container is MapPortal portal)
            {
                return portal.leftToLoad;
            }
            CompTransporter transporter = container.TryGetComp<CompTransporter>();
            return transporter != null ? transporter.leftToLoad : null;
        }

        /// <summary>取走该 Job 的保真取出计划（物化完成后调用）。</summary>
        public static List<ThingCount> TakePendingLoadPlan(int jobLoadID)
        {
            if (!pendingLoadPlans.TryGetValue(jobLoadID, out List<ThingCount> plan))
            {
                return null;
            }
            pendingLoadPlans.Remove(jobLoadID);
            pendingLoadPlanTick.Remove(jobLoadID);
            return plan;
        }

        /// <summary>Job 结束（含失败 / 取消）时清掉计划与待用键，避免残留。</summary>
        public static void ForgetPendingLoadPlan(int jobLoadID)
        {
            pendingLoadPlans.Remove(jobLoadID);
            pendingLoadPlanTick.Remove(jobLoadID);
            pendingLoadKeys.Remove(jobLoadID);
        }

        // ---------- 装载派活失败熔断 ----------
        //
        // 极少数情况会"清单里有超维品类、却取不出来"（端点不可达 / 预留失败 / 携带上限为 0）。
        // 不熔断的话 JobGiver 会在同一 tick 内反复派活（"started 10 jobs in one tick"）。

        private const int LoadFailureCooldownTicks = 250;
        private static readonly Dictionary<long, int> loadFailureUntil = new Dictionary<long, int>();

        private static long LoadFailureKey(Pawn pawn, Thing container)
        {
            if (pawn == null || container == null)
            {
                return 0L;
            }
            return ((long)pawn.thingIDNumber << 32) | (uint)container.thingIDNumber;
        }

        /// <summary>该 (小人, 容器) 是否处于"取货失败"冷却中。</summary>
        public static bool IsLoadCoolingDown(Pawn pawn, Thing container)
        {
            long key = LoadFailureKey(pawn, container);
            if (key == 0L || !loadFailureUntil.TryGetValue(key, out int until))
            {
                return false;
            }
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now >= until)
            {
                loadFailureUntil.Remove(key);
                return false;
            }
            return true;
        }

        /// <summary>记录一次取货失败，冷却期内不再为同一 (小人, 容器) 派活。</summary>
        public static void NotifyLoadFailed(Pawn pawn, Thing container)
        {
            long key = LoadFailureKey(pawn, container);
            if (key == 0L)
            {
                return;
            }
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            loadFailureUntil[key] = now + LoadFailureCooldownTicks;
        }

        /// <summary>运输舱装载的保真桶构建判据（Inject 与派活共用同一份）。</summary>
        /// <summary>
        /// W48：该装载行是否为「纯展示物行」；是则给出可跨存档重建的品类键。
        /// 只接受：行内全部是不带保真桶的虚拟展示物（或全部属于同一个保真桶）。
        /// 真实物 / 虚拟与保真混行 / 多桶混行一律返回 false（交回原版，不做第二套身份）。
        /// </summary>
        internal static bool TryGetRestorableRowKey(TransferableOneWay row, out SDS_StockKey key,
            out bool preserved)
        {
            key = null;
            preserved = false;
            if (row == null || row.things == null || row.things.Count == 0 || row.CountToTransfer <= 0)
            {
                return false;
            }
            List<Thing> bucket = null;
            for (int i = 0; i < row.things.Count; i++)
            {
                Thing thing = row.things[i];
                if (!SDS_OutputUtility.IsDisplay(thing))
                {
                    return false;
                }
                bool hasBucket = TryGetPreservedBucket(thing, out List<Thing> found)
                    && found != null && found.Count > 0;
                SDS_StockKey candidate;
                if (hasBucket)
                {
                    if (bucket == null)
                    {
                        bucket = found;
                    }
                    else if (!SDS_PreservedBuckets.SameBucket(bucket, found))
                    {
                        return false;
                    }
                    candidate = SDS_OutputUtility.KeyOfThing(found[0]);
                }
                else
                {
                    if (bucket != null)
                    {
                        return false;
                    }
                    candidate = SDS_OutputUtility.TryGetDisplayKey(thing, out SDS_StockKey displayKey)
                        ? displayKey
                        : null;
                }
                if (candidate == null || candidate.preserved != hasBucket)
                {
                    return false;
                }
                if (key == null)
                {
                    key = candidate;
                }
                else if (!key.SameAs(candidate))
                {
                    return false;
                }
            }
            preserved = bucket != null;
            return key != null;
        }

        /// <summary>W48：读档后按品类键重建装载行展示物；取不到（库存已空 / 桶已不存在）返回 null。</summary>
        internal static Thing TryGetRestoreDisplay(SDS_StockKey key)
        {
            if (key == null)
            {
                return null;
            }
            if (!key.preserved)
            {
                return SDS_OutputUtility.CachedDisplayThing(key);
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return null;
            }
            List<SDS_PreservedBucket> buckets = SDS_PreservedBuckets.Build(ledger,
                IncludeForTransportLoad, tradeGrouping: false, trader: null, tradableBudget: false);
            for (int i = 0; i < buckets.Count; i++)
            {
                SDS_PreservedBucket bucket = buckets[i];
                if (bucket == null || bucket.things == null || bucket.things.Count == 0)
                {
                    continue;
                }
                SDS_StockKey bucketKey = SDS_OutputUtility.KeyOfThing(bucket.things[0]);
                if (bucketKey == null || bucketKey.preserved != key.preserved
                    || !bucketKey.SameAs(key))
                {
                    continue;
                }
                return ResolveBucketDisplay(null, bucket);
            }
            return null;
        }

        /// <summary>运输舱装载的保真桶构建判据（Inject 与派活共用同一份）。</summary>
        private static bool IncludeForTransportLoad(Thing thing)
        {
            Thing inner = thing.GetInnerIfMinified();
            return thing.def != null && thing.def.category == ThingCategory.Item
                && inner != null && inner.def != null && inner.def.canLoadIntoCaravan;
        }
    }
}
