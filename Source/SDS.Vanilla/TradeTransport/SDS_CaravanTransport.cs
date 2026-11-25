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
    // 规则锚点（编成 / 车运共用）：W25 / W28 / W39；装载 / 运输链（W38 / W40 / W42–W48）见
    // SDS_TransportVirtual.cs、Patch_TransportLoad.cs、JobDriver_SDS_CaravanJob.cs、SDS_VanillaState.cs。
    // 唯一取货 / 回充实现仍在 Core；本文件只提供原版派活适配与执行期事实。

    // ============================ 远征队：注入 / 校验 / 物化 ============================

    /// <summary>
    /// 远征队（照搬原版取货流程）：编成清单只注入展示物；出发时按差额物化成实物；
    /// 之后全部交回原版 JobGiver_PrepareCaravan_GatherItems 派活、小人自行搬齐离图。
    /// 数量守恒：物化多少就扣多少，放不进任何地方的实物回充超维空间（不销毁）。
    /// </summary>
    internal static class SDS_CaravanVirtual
    {
        /// <summary>编成清单里的保真展示物 → 原版分组桶（桶内是真实真身）。</summary>
        private static readonly SDS_DisplayBucketRegistry preservedBuckets =
            new SDS_DisplayBucketRegistry();

        private static bool TryGetPreservedBucket(Thing display, out List<Thing> bucket)
        {
            return preservedBuckets.TryGet(display, out bucket);
        }

        /// <summary>新档 / 读档：清空运行期登记（旧游戏对象不属于本局）。只由 SDS_StaticState 调用。</summary>
        internal static void Reset()
        {
            preservedBuckets.Clear();
        }

        /// <summary>该 loadID 的 Lord 是否仍在世界中（ISDS_ReclaimWorld 的唯一实现）。</summary>
        internal static bool LordExists(int lordLoadID)
        {
            return FindLord(lordLoadID) != null;
        }

        /// <summary>
        /// 从编成清单里摘掉一件物资（回收受保护输出时用），并把数量夹回剩余实物量，
        /// 避免留下指向已销毁实物的悬空引用。
        /// </summary>
        internal static void RemoveFromCaravan(int lordLoadID, Thing thing)
        {
            if (thing == null)
            {
                return;
            }
            Lord lord = FindLord(lordLoadID);
            LordJob_FormAndSendCaravan lordJob = lord != null ? lord.LordJob as LordJob_FormAndSendCaravan : null;
            if (lordJob == null || lordJob.transferables == null)
            {
                return;
            }
            List<TransferableOneWay> transferables = lordJob.transferables;
            for (int i = 0; i < transferables.Count; i++)
            {
                TransferableOneWay transferable = transferables[i];
                if (transferable == null || transferable.things == null)
                {
                    continue;
                }
                if (transferable.things.Remove(thing))
                {
                    int max = transferable.MaxCount;
                    if (transferable.CountToTransfer > max)
                    {
                        transferable.AdjustTo(max);
                    }
                }
            }
        }

        private static Lord FindLord(int loadID)
        {
            if (loadID == 0 || Find.Maps == null)
            {
                return null;
            }
            for (int m = 0; m < Find.Maps.Count; m++)
            {
                Map map = Find.Maps[m];
                LordManager manager = map != null ? map.lordManager : null;
                List<Lord> lords = manager != null ? manager.lords : null;
                if (lords == null)
                {
                    continue;
                }
                for (int i = 0; i < lords.Count; i++)
                {
                    Lord lord = lords[i];
                    if (lord != null && lord.loadID == loadID)
                    {
                        return lord;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 把编成清单里的残留展示物摘掉（只摘属于超维空间的壳子，绝不误伤真物）。
        /// 只解除引用、绝不 Destroy：原版窗口可能仍持有并按品类键缓存的展示物继续绘制。
        /// </summary>
        public static void CleanupFakes(List<TransferableOneWay> transferables)
        {
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
                for (int j = t.things.Count - 1; j >= 0; j--)
                {
                    Thing thing = t.things[j];
                    if (thing != null && SDS_OutputUtility.IsDisplay(thing))
                    {
                        t.things.RemoveAt(j);
                    }
                }
            }
        }

        /// <summary>清单里是否有「地图实物量不够、必须靠超维空间补」的项（点出发前据此要端点）。</summary>
        public static bool AnyVirtualNeeded(List<TransferableOneWay> transferables)
        {
            if (transferables == null)
            {
                return false;
            }
            for (int i = 0; i < transferables.Count; i++)
            {
                TransferableOneWay t = transferables[i];
                // 只有「物品 + 带本 Mod 展示物」的行才有超维差额。
                if (!TryGetVirtualRowKeys(t, out SDS_StockKey _, out SDS_StockKey _))
                {
                    continue;
                }
                if (t.CountToTransfer > RealCount(t))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 点「出发」时的数量快照校验：超维空间可用量不够就整趟拒绝，绝不带着少一截的清单出发。
        /// 只处理「物品 + 带本 Mod 展示物」的行；pawn 行与纯原版行交原版 CheckForErrors。
        /// </summary>
        public static bool ValidateQuantities(List<TransferableOneWay> transferables, out string failLabel)
        {
            failLabel = null;
            if (SDS_Core.Ledger == null || transferables == null)
            {
                return true;
            }
            for (int i = 0; i < transferables.Count; i++)
            {
                TransferableOneWay t = transferables[i];
                if (t == null || t.CountToTransfer <= 0)
                {
                    continue;
                }
                if (!TryGetVirtualRowKeys(t, out SDS_StockKey virtualKey, out SDS_StockKey preservedKey))
                {
                    continue;
                }
                int deficit = t.CountToTransfer - RealCount(t);
                if (deficit <= 0)
                {
                    continue;
                }
                // 一行里可能同时有虚拟展示物与保真桶展示物（原版按 def/材质/品质/血量档合并）：
                // 两条通道的可用量相加，只要合计够就不拦。
                long have = 0L;
                if (virtualKey != null)
                {
                    have += SDS_Availability.GetAvailableCount(virtualKey);
                }
                if (preservedKey != null)
                {
                    have += SDS_Availability.GetAvailableCount(preservedKey);
                }
                if (have < deficit)
                {
                    failLabel = t.Label;
                    return false;
                }
            }
            return true;
        }

        /// <summary>按展示物身份重建唯一保真桶；0 / 多个候选返回 false（fail-closed）。</summary>
        private static bool TryResolveCanonicalBucket(ISDS_LedgerRead ledger, Thing display,
            ref List<Thing> allPreserved, out List<Thing> bucket)
        {
            bucket = null;
            if (ledger == null || display == null)
            {
                return false;
            }
            if (allPreserved == null)
            {
                allPreserved = new List<Thing>();
                ledger.AppendPreservedThings(allPreserved);
            }
            bucket = SDS_PreservedBuckets.ResolveForDisplay(ledger, display, allPreserved,
                false, null, out int candidates);
            return candidates == 1 && bucket != null;
        }

        /// <summary>
        /// 扫描一行编成清单的身份，不修改 things。
        /// 结果最多一个虚拟键 + 一个保真桶；任一通道出现多个身份即 ambiguous = true。
        /// 保真桶必须由展示物身份唯一解析，绝不按 key 聚合（W45 / W47）。
        /// </summary>
        private static bool TryScanRowIdentities(ISDS_LedgerRead ledger, TransferableOneWay t,
            out SDS_StockKey virtualKey, out SDS_StockKey preservedKey,
            out List<Thing> preservedBucket, out bool ambiguous, bool allowFallback = false)
        {
            virtualKey = null;
            preservedKey = null;
            preservedBucket = null;
            ambiguous = false;
            if (ledger == null || t == null || t.things == null || t.ThingDef == null
                || t.ThingDef.category != ThingCategory.Item)
            {
                return false;
            }
            List<Thing> allPreserved = null;
            for (int j = 0; j < t.things.Count; j++)
            {
                Thing display = t.things[j];
                if (display == null || !SDS_OutputUtility.IsDisplay(display))
                {
                    continue;
                }
                SDS_StockKey displayKey = SDS_OutputUtility.KeyOfDisplay(display, null);
                if (TryGetPreservedBucket(display, out List<Thing> bucket) && bucket != null)
                {
                    if (preservedBucket == null)
                    {
                        preservedBucket = bucket;
                    }
                    else if (!SDS_PreservedBuckets.SameBucket(preservedBucket, bucket))
                    {
                        ambiguous = true;
                        return false;
                    }
                    if (!TryMergePreservedKey(display, bucket, ref preservedKey))
                    {
                        ambiguous = true;
                        return false;
                    }
                    continue;
                }
                if (displayKey != null && displayKey.preserved)
                {
                    if (!TryResolveCanonicalBucket(ledger, display, ref allPreserved, out List<Thing> resolved))
                    {
                        ambiguous = true;
                        return false;
                    }
                    if (preservedBucket == null)
                    {
                        preservedBucket = resolved;
                    }
                    else if (!SDS_PreservedBuckets.SameBucket(preservedBucket, resolved))
                    {
                        ambiguous = true;
                        return false;
                    }
                    if (!TryMergePreservedKey(display, resolved, ref preservedKey))
                    {
                        ambiguous = true;
                        return false;
                    }
                    continue;
                }
                if (displayKey != null)
                {
                    if (virtualKey == null)
                    {
                        virtualKey = displayKey;
                    }
                    else if (!virtualKey.SameAs(displayKey))
                    {
                        ambiguous = true;
                        return false;
                    }
                }
            }
            if (allowFallback && virtualKey == null && preservedBucket == null && t.AnyThing != null)
            {
                Thing any = t.AnyThing;
                if (TryGetPreservedBucket(any, out List<Thing> registered) && registered != null)
                {
                    preservedBucket = registered;
                    if (!TryMergePreservedKey(any, registered, ref preservedKey))
                    {
                        ambiguous = true;
                        return false;
                    }
                }
                else if (SDS_OutputUtility.TryGetDisplayKey(any, out SDS_StockKey anyDisplayKey)
                    && anyDisplayKey != null)
                {
                    if (anyDisplayKey.preserved)
                    {
                        if (!TryResolveCanonicalBucket(ledger, any, ref allPreserved, out List<Thing> resolved))
                        {
                            ambiguous = true;
                            return false;
                        }
                        preservedBucket = resolved;
                        preservedKey = anyDisplayKey;
                    }
                    else
                    {
                        virtualKey = anyDisplayKey;
                    }
                }
                else
                {
                    SDS_StockKey anyKey = SDS_OutputUtility.KeyOfThing(any);
                    if (anyKey != null)
                    {
                        if (anyKey.preserved)
                        {
                            if (!TryResolveCanonicalBucket(ledger, any, ref allPreserved, out List<Thing> resolved))
                            {
                                ambiguous = true;
                                return false;
                            }
                            preservedBucket = resolved;
                            preservedKey = anyKey;
                        }
                        else
                        {
                            virtualKey = anyKey;
                        }
                    }
                }
            }
            if (allowFallback && virtualKey == null && preservedBucket == null && t.ThingDef != null)
            {
                if (SDS_Rules.TryFindWithdrawableKey(t.ThingDef, out SDS_StockKey found, out _) && found != null)
                {
                    if (found.preserved)
                    {
                        // 没有可核对的展示物 / 桶身份：绝不按键聚合，fail-closed。
                        ambiguous = true;
                        return false;
                    }
                    virtualKey = found;
                }
            }
            return virtualKey != null || preservedBucket != null;
        }

        private static bool TryMergePreservedKey(Thing display, List<Thing> bucket,
            ref SDS_StockKey preservedKey)
        {
            SDS_StockKey key = SDS_OutputUtility.KeyOfDisplay(display, bucket);
            if (key == null)
            {
                return false;
            }
            if (preservedKey == null)
            {
                preservedKey = key;
                return true;
            }
            return preservedKey.SameAs(key);
        }

        /// <summary>
        /// 该行是否「物品 + 带本 Mod 展示物」，并给出这一行的两条通道身份。
        /// 一行最多一个虚拟键 + 一个保真桶；出现多个身份时返回 false。
        /// 保真身份只能由展示物 / 登记桶唯一解析，不得按键聚合多个桶（W45 / W47）。
        /// </summary>
        private static bool TryGetVirtualRowKeys(TransferableOneWay t, out SDS_StockKey virtualKey,
            out SDS_StockKey preservedKey)
        {
            return TryScanRowIdentities(SDS_Core.Ledger, t, out virtualKey, out preservedKey,
                out _, out _);
        }

        /// <summary>
        /// 某项里「地图上、尚未被任何 holder 持有」的真实实物总量（展示物不算）。
        /// 已装进容器 / 已拿在手上的部分（holdingOwner != null）不算——原版 CountToTransfer 已扣过。
        /// </summary>
        public static int RealCount(TransferableOneWay t)
        {
            if (t == null || t.things == null)
            {
                return 0;
            }
            long total = 0L;
            for (int j = 0; j < t.things.Count; j++)
            {
                Thing thing = t.things[j];
                if (thing != null && thing.holdingOwner == null && !SDS_OutputUtility.IsDisplay(thing))
                {
                    total += thing.stackCount;
                    if (total >= int.MaxValue)
                    {
                        return int.MaxValue;
                    }
                }
            }
            return (int)total;
        }

        /// <summary>把超维空间品类以展示物注入编成清单（只收可进远征队的物品）。</summary>
        public static void Inject(List<TransferableOneWay> transferables)
        {
            if (transferables == null)
            {
                return;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null)
            {
                return;
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
                // 这里只枚举虚拟账本（保真走另一条桶）；是否可展示由 CachedDisplayThing 回答。
                long available = SDS_Availability.GetAvailableCount(entry.key);
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
                if (t == null)
                {
                    t = new TransferableOneWay();
                    transferables.Add(t);
                }
                if (!t.things.Contains(fake))
                {
                    t.things.Add(fake);
                }
            }
            // 保真通道品类（缩装家具 / 基因载体等）追加注入（纯追加分支）。
            InjectPreserved(transferables, ledger);
        }

        /// <summary>保真通道品类以展示物注入编成清单：保留真实实体的品类也能在出发时原样物化。</summary>
        private static void InjectPreserved(List<TransferableOneWay> transferables, ISDS_LedgerRead ledger)
        {
            // 本图这次编成清单的桶映射先清空，再按原版分组重新登记。
            preservedBuckets.Clear();
            List<SDS_PreservedBucket> buckets = SDS_PreservedBuckets.Build(ledger,
                delegate (Thing thing)
                {
                    Thing inner = thing.GetInnerIfMinified();
                    // 与原版 AllReachableColonyItems 逐条对齐：外壳 def 决定"是不是可搬运物品"，
                    // 真身 def 决定 canLoadIntoCaravan。
                    return thing.def != null && thing.def.category == ThingCategory.Item
                        && inner != null && inner.def != null && inner.def.canLoadIntoCaravan;
                },
                tradeGrouping: false, trader: null, tradableBudget: false);
            for (int i = 0; i < buckets.Count; i++)
            {
                SDS_PreservedBucket bucket = buckets[i];
                if (bucket == null || bucket.display == null || bucket.units <= 0)
                {
                    continue;
                }
                TransferableOneWay t = TransferableUtility.TransferableMatching<TransferableOneWay>(
                    bucket.display, transferables, TransferAsOneMode.PodsOrCaravanPacking);
                long room = Math.Max(0L, (long)int.MaxValue - (t != null ? t.MaxCount : 0));
                int amount = (int)Math.Min((long)bucket.units, room);
                if (amount <= 0)
                {
                    continue;
                }
                bucket.display.stackCount = amount;
                if (t == null)
                {
                    t = new TransferableOneWay();
                    transferables.Add(t);
                }
                if (!t.things.Contains(bucket.display))
                {
                    t.things.Add(bucket.display);
                }
                preservedBuckets.Register(bucket.display, bucket.things);
            }
        }

        /// <summary>
        /// 正式派出：把清单里的展示物剥掉，并按差额从超维空间物化出真实实物，
        /// 优先直接放进编成成员背包；收不下的落到端点附近并标记为受保护输出。
        /// </summary>
        // RULE:W25
        public static void MaterializeForCaravan(Lord lord, LordJob_FormAndSendCaravan lordJob, Map map,
            IEnumerable<Pawn> startingPawns)
        {
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (lord == null || lordJob == null || lordJob.transferables == null || map == null || ledger == null)
            {
                return;
            }
            IntVec3 reference = CaravanReferenceCell(lord, startingPawns);
            bool hasCenter = TryFindEndpointCell(map, reference, out IntVec3 center);
            bool endpointAvailable = SDS_Exposure.MapExposesGrid(map);
            // 「自身端口优先」：带可用芯片的成员（货直接进背包、不落地）。
            List<Pawn> members = CollectMembers(lord, startingPawns);

            List<TransferableOneWay> transferables = lordJob.transferables;
            for (int i = 0; i < transferables.Count; i++)
            {
                TransferableOneWay t = transferables[i];
                if (t == null)
                {
                    continue;
                }
                // 编成只接管物品行；pawn 行（及任何非 Item 行）一律交原版，不物化、不调账。
                if (t.ThingDef == null || t.ThingDef.category != ThingCategory.Item)
                {
                    continue;
                }
                // 1. 先扫描行身份（不修改 things）：最多一个虚拟键 + 一个保真桶；
                //    保真桶按展示物身份唯一解析，绝不按 key 聚合（W45 / W47）。
                if (!TryScanRowIdentities(ledger, t, out SDS_StockKey key,
                        out SDS_StockKey _, out List<Thing> preservedBucket,
                        out bool ambiguousIdentity, true))
                {
                    if (ambiguousIdentity)
                    {
                        SDS_Diag.Fail("编成/展示物身份不唯一或保真桶无法唯一解析（已跳过该行）",
                            SDS_Diag.Ctx(map, null, null, null,
                                "def=" + (t.ThingDef != null ? t.ThingDef.defName : "?")
                                + " row=" + t.Label));
                        // 歧义行不能把虚拟展示物交给原版搬运：剥掉展示物并把本行归零，
                        // 保证「宁可不发这一行，也不串桶 / 不搬展示物」。
                        for (int j = t.things.Count - 1; j >= 0; j--)
                        {
                            if (t.things[j] != null && SDS_OutputUtility.IsDisplay(t.things[j]))
                            {
                                t.things.RemoveAt(j);
                            }
                        }
                        t.AdjustTo(0);
                    }
                    continue;
                }
                // 2. 身份唯一后才剥展示物；无身份的原版行保持清单原状。
                for (int j = t.things.Count - 1; j >= 0; j--)
                {
                    if (t.things[j] != null && SDS_OutputUtility.IsDisplay(t.things[j]))
                    {
                        t.things.RemoveAt(j);
                    }
                }

                // 3. 超维空间补足「清单数量」与「地图实物量」之间的差额（虚拟通道）。
                int deficit = t.CountToTransfer - RealCount(t);
                if (deficit > 0 && key != null && !key.preserved)
                {
                    // 2a. 自身端口优先：有可用端点时先把差额直接放进编成成员背包（不落地）。
                    int injected = endpointAvailable && members.Count > 0
                        ? MaterializeIntoMembers(key, deficit, members)
                        : 0;
                    // 2b. 编成里没人收得下的部分才退回「落地物化」这条原版腿（绝不少送）。
                    if (injected < deficit && hasCenter)
                    {
                        MaterializeInto(map, center, key, deficit - injected, t, lord.loadID);
                    }
                    // 2c. 记账：已进背包的数量从"待收集"里扣掉（结果钳到 [0, MaxCount]）。
                    if (injected > 0)
                    {
                        t.AdjustTo(Math.Max(0, t.CountToTransfer - injected));
                    }
                }

                // 2'. 保真通道补差额：只从玩家勾选的那一桶里摘真身，绝不跨桶拿。
                deficit = t.CountToTransfer - RealCount(t);
                if (deficit > 0 && preservedBucket != null)
                {
                    int injectedPreserved = endpointAvailable && members.Count > 0
                        ? MaterializePreservedBucketIntoMembers(preservedBucket, deficit, members)
                        : 0;
                    if (injectedPreserved < deficit && hasCenter)
                    {
                        MaterializePreservedBucketInto(map, center, preservedBucket, deficit - injectedPreserved,
                            t, lord.loadID);
                    }
                    if (injectedPreserved > 0)
                    {
                        t.AdjustTo(Math.Max(0, t.CountToTransfer - injectedPreserved));
                    }
                }

                // 3. 清单数量夹回「地图上的真实实物量」（此时已包含刚物化/落地出来的）。
                ClampToReal(t);
            }
        }

        private static int MaterializeInto(Map map, IntVec3 center, SDS_StockKey key,
            int deficit, TransferableOneWay t, int lordLoadID)
        {
            // 直接扣账必须尊重预留：已被 Job 占住的量不能拿来补远征差额（W56）。
            deficit = SDS_Availability.ClampTakeToAvailable(deficit,
                SDS_Availability.GetAvailableCount(key));
            if (deficit <= 0)
            {
                return 0;
            }
            if (!TryWithdrawVirtual(key, deficit, out List<Thing> made))
            {
                SDS_Diag.Fail("编成/取货物化失败", SDS_Diag.Ctx(map, null, null, null,
                    "def=" + (key.thingDef != null ? key.thingDef.defName : "?") + " deficit=" + deficit));
                return 0;
            }
            int placedTotal = 0;
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (!TryPlaceAndTrack(thing, center, map, out Thing placed))
                {
                    continue;
                }
                // 受保护输出：普通搬运工不得搬走（远征队自己的取货链不看 forbidden，照常搬）；
                // 同时交 Core 的统一回收账本，未被搬走时由它按策略回充。
                SDS_ReclaimRegistry.RegisterProtectedOutput(placed, lordLoadID);
                CompForbiddable forbiddable = placed.TryGetComp<CompForbiddable>();
                if (forbiddable != null)
                {
                    forbiddable.Forbidden = true;
                }
                if (!t.things.Contains(placed))
                {
                    t.things.Add(placed);
                }
                placedTotal += placed.stackCount;
            }
            return placedTotal;
        }

        /// <summary>
        /// 「可直接投背包」的成员 = 自身端口可用的成员（带芯片且芯片可用）。
        /// 无芯片的成员 / 驮畜不享受直投，差额落到端点由原版搬运链自己取。
        /// </summary>
        private static List<Pawn> CollectMembers(Lord lord, IEnumerable<Pawn> startingPawns)
        {
            List<Pawn> chipMembers = new List<Pawn>();
            IEnumerable<Pawn> source = startingPawns ?? (lord != null ? (IEnumerable<Pawn>)lord.ownedPawns : null);
            if (source == null)
            {
                return chipMembers;
            }
            foreach (Pawn p in source)
            {
                if (p == null || p.Destroyed || p.inventory == null)
                {
                    continue;
                }
                if (p.Spawned && p.Map != null && SDS_ImplantUtility.IsPortOperational(p))
                {
                    chipMembers.Add(p);
                }
            }
            return chipMembers;
        }

        /// <summary>编成集合的参考格（各"在图上"成员位置的平均；一个都没有时返回 Invalid）。</summary>
        private static IntVec3 CaravanReferenceCell(Lord lord, IEnumerable<Pawn> startingPawns)
        {
            IEnumerable<Pawn> source = startingPawns ?? (lord != null ? (IEnumerable<Pawn>)lord.ownedPawns : null);
            if (source == null)
            {
                return IntVec3.Invalid;
            }
            int count = 0;
            float sx = 0f;
            float sz = 0f;
            bool haveFirst = false;
            IntVec3 first = IntVec3.Invalid;
            foreach (Pawn p in source)
            {
                if (p == null || !p.Spawned || p.Map == null)
                {
                    continue;
                }
                if (!haveFirst)
                {
                    first = p.Position;
                    haveFirst = true;
                }
                sx += p.Position.x;
                sz += p.Position.z;
                count++;
            }
            if (count == 0)
            {
                return first;
            }
            return new IntVec3((int)Math.Round(sx / count), 0, (int)Math.Round(sz / count));
        }

        /// <summary>就近原则：离编成集合最近的端点落货格（Core 统一端点决议）。</summary>
        private static bool TryFindEndpointCell(Map map, IntVec3 reference, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null)
            {
                return false;
            }
            SDS_EndpointQuery query = default;
            query.ApplyHostPriority();
            query.op = SDS_EndpointOp.Withdraw;
            query.map = map;
            query.reference = reference;
            query.requireReach = false;
            query.allowSelf = false;
            query.requireStock = false;
            SDS_EndpointDecision decision = SDS_Rules.SelectEndpoint(query);
            if (!decision.allowed || decision.port == null)
            {
                return false;
            }
            cell = decision.port.AccessCell;
            return cell.IsValid;
        }

        /// <summary>
        /// 「自身端口优先」的物化腿：把超维空间额度直接放进远征队成员背包（不落地）。
        /// 收不下的部分**退回超维空间**（不销毁掉数），由调用方改走"落地物化"腿。
        /// </summary>
        private static int MaterializeIntoMembers(SDS_StockKey key, int deficit, List<Pawn> members)
        {
            if (key == null || deficit <= 0 || members == null || members.Count == 0)
            {
                return 0;
            }
            deficit = SDS_Availability.ClampTakeToAvailable(deficit,
                SDS_Availability.GetAvailableCount(key));
            if (deficit <= 0)
            {
                return 0;
            }
            if (!TryWithdrawVirtual(key, deficit, out List<Thing> made))
            {
                SDS_Diag.Fail("编成/取货物化失败", SDS_Diag.Ctx(null,
                    members.Count > 0 ? members[0] : null, null, null,
                    "def=" + (key.thingDef != null ? key.thingDef.defName : "?") + " deficit=" + deficit));
                return 0;
            }
            int injected = 0;
            int nextMember = 0;
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                int amount = thing.stackCount;
                bool accepted = false;
                // 轮转投放：每件都从"下一个成员"开始试，投满一个换下一个。
                for (int attempt = 0; attempt < members.Count; attempt++)
                {
                    Pawn carrier = members[nextMember];
                    nextMember = (nextMember + 1) % members.Count;
                    if (carrier == null || carrier.Destroyed || carrier.inventory == null)
                    {
                        continue;
                    }
                    if (carrier.inventory.innerContainer.TryAdd(thing, canMergeWithExistingStacks: true))
                    {
                        injected += amount;
                        accepted = true;
                        break;
                    }
                }
                if (accepted)
                {
                    continue;
                }
                // 没有任何成员收得下这件：回充超维空间（不过筛选），失败再落地。
                Pawn fallback = FirstSpawned(members);
                Map dropMap = fallback != null ? fallback.Map : Find.AnyPlayerHomeMap;
                if (!SDS_Rules.ReturnThing(thing, SDS_StockChannel.Virtual, dropMap, fallback,
                        SDS_ReturnFallback.Ground))
                {
                    SDS_Diag.Fail("编成/虚拟回滚失败", SDS_Diag.Ctx(dropMap, fallback, thing, null,
                        "def=" + (key.thingDef != null ? key.thingDef.defName : "?") + " amount=" + amount));
                }
            }
            return injected;
        }

        /// <summary>
        /// 保真通道的「落地物化」腿：把桶里的真身【原样】摘出（不做重建），
        /// 落到端点附近、标记为受保护输出后纳入编成清单。
        /// </summary>
        private static int MaterializePreservedBucketInto(Map map, IntVec3 center,
            List<Thing> bucket, int deficit, TransferableOneWay t, int lordLoadID)
        {
            long available = PreservedAvailable(bucket);
            if (available <= 0L)
            {
                return 0;
            }
            deficit = SDS_Availability.ClampTakeToAvailable(deficit, available);
            List<ThingCount> plan = SDS_PreservedBuckets.TakePlan(bucket, deficit, out int planned);
            if (planned <= 0 || plan.Count == 0)
            {
                SDS_Diag.Warn("编成/保真无可用计划", SDS_Diag.Ctx(map, null, null, null,
                    "deficit=" + deficit + " available=" + available));
                return 0;
            }
            if (!SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                SDS_Diag.Fail("编成/保真物化失败", SDS_Diag.Ctx(map, null, null, null,
                    "planned=" + planned + " plan=" + plan.Count));
                return 0;
            }
            int placedTotal = 0;
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                if (!TryPlaceAndTrack(thing, center, map, out Thing placed))
                {
                    continue;
                }
                // 受保护输出：普通搬运工不得搬走；同时交 Core 的统一回收账本。
                SDS_ReclaimRegistry.RegisterProtectedOutput(placed, lordLoadID);
                CompForbiddable forbiddable = placed.TryGetComp<CompForbiddable>();
                if (forbiddable != null)
                {
                    forbiddable.Forbidden = true;
                }
                if (!t.things.Contains(placed))
                {
                    t.things.Add(placed);
                }
                placedTotal += placed.stackCount;
            }
            return placedTotal;
        }

        /// <summary>
        /// 保真通道的「自身端口优先」腿：把桶里的真身直接放进远征队成员背包。
        /// 收不下的**退回保真通道**（不销毁掉数），由调用方改走"落地物化"腿。
        /// </summary>
        private static int MaterializePreservedBucketIntoMembers(List<Thing> bucket,
            int deficit, List<Pawn> members)
        {
            if (bucket == null || deficit <= 0 || members == null || members.Count == 0)
            {
                return 0;
            }
            long available = PreservedAvailable(bucket);
            if (available <= 0L)
            {
                return 0;
            }
            deficit = SDS_Availability.ClampTakeToAvailable(deficit, available);
            List<ThingCount> plan = SDS_PreservedBuckets.TakePlan(bucket, deficit, out int planned);
            if (planned <= 0 || plan.Count == 0)
            {
                SDS_Diag.Warn("编成/保真无可用计划", SDS_Diag.Ctx(null,
                    members.Count > 0 ? members[0] : null, null, null,
                    "deficit=" + deficit + " available=" + available));
                return 0;
            }
            if (!SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                SDS_Diag.Fail("编成/保真物化失败", SDS_Diag.Ctx(null,
                    members.Count > 0 ? members[0] : null, null, null,
                    "planned=" + planned + " plan=" + plan.Count));
                return 0;
            }
            int injected = 0;
            int nextMember = 0;
            for (int i = 0; i < made.Count; i++)
            {
                Thing thing = made[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                int amount = thing.stackCount;
                bool accepted = false;
                for (int attempt = 0; attempt < members.Count; attempt++)
                {
                    Pawn carrier = members[nextMember];
                    nextMember = (nextMember + 1) % members.Count;
                    if (carrier == null || carrier.Destroyed || carrier.inventory == null)
                    {
                        continue;
                    }
                    if (carrier.inventory.innerContainer.TryAdd(thing, canMergeWithExistingStacks: true))
                    {
                        injected += amount;
                        accepted = true;
                        break;
                    }
                }
                if (accepted)
                {
                    continue;
                }
                // 没有任何成员收得下这件：退回保真通道（回滚专用，不过筛选），失败再落地。
                Pawn fallback = FirstSpawned(members);
                if (fallback != null)
                {
                    SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, fallback.Map, fallback,
                        SDS_ReturnFallback.Ground);
                }
                else
                {
                    SDS_Rules.ReturnThing(thing, SDS_StockChannel.Preserved, Find.AnyPlayerHomeMap, null,
                        SDS_ReturnFallback.Ground);
                }
            }
            return injected;
        }

        /// <summary>第一个还在图上的成员（兜底落地用）。</summary>
        private static Pawn FirstSpawned(List<Pawn> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                Pawn p = members[i];
                if (p != null && !p.Destroyed && p.Spawned && p.Map != null)
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>把清单数量夹回"地图上的真实实物量"（AdjustTo 要求不得超过 MaxCount）。</summary>
        private static void ClampToReal(TransferableOneWay t)
        {
            int max = t.MaxCount;
            if (t.CountToTransfer > max)
            {
                t.AdjustTo(max);
            }
        }

        /// <summary>
        /// 落货并取回"清单该引用的那个实例"：TryPlaceOrSpawn 用 Near 模式，可能并入邻近同类堆
        /// （此时原实例被销毁）；这里按原版分组规则把并入的那一堆找回来，避免清单指向已销毁实例。
        /// </summary>
        private static bool TryPlaceAndTrack(Thing thing, IntVec3 cell, Map map, out Thing placed)
        {
            placed = null;
            if (thing == null || thing.Destroyed || map == null)
            {
                return false;
            }
            if (!SDS_PlacementUtility.TryPlaceOrSpawn(thing, cell, map))
            {
                return false;
            }
            if (!thing.Destroyed)
            {
                placed = thing;
                return true;
            }
            IntVec3 at = cell.IsValid && cell.InBounds(map) ? cell : map.Center;
            foreach (IntVec3 near in GenRadial.RadialCellsAround(at, 1.5f, true))
            {
                if (!near.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = map.thingGrid.ThingsListAt(near);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing candidate = things[i];
                    if (candidate != null && !candidate.Destroyed && !ReferenceEquals(candidate, thing)
                        && candidate.def == thing.def && candidate.Stuff == thing.Stuff
                        && TransferableUtility.TransferAsOne(candidate, thing, TransferAsOneMode.PodsOrCaravanPacking))
                    {
                        placed = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>保真桶的可用量（同一口径：物理件数 − 预留）。</summary>
        private static long PreservedAvailable(List<Thing> bucket)
        {
            SDS_StockKey key = BucketKey(bucket);
            return key != null ? SDS_Availability.GetAvailableCount(key) : 0L;
        }

        /// <summary>保真桶的代表键（桶内第一件仍在库的真身）。</summary>
        private static SDS_StockKey BucketKey(List<Thing> bucket)
        {
            if (bucket == null)
            {
                return null;
            }
            for (int i = 0; i < bucket.Count; i++)
            {
                Thing thing = bucket[i];
                if (thing != null && !thing.Destroyed && SDS_OutputUtility.KeyOfThing(thing) != null)
                {
                    return SDS_OutputUtility.KeyOfThing(thing);
                }
            }
            return null;
        }

        /// <summary>虚拟通道取货（整笔足额，Core 门面唯一入口）。</summary>
        private static bool TryWithdrawVirtual(SDS_StockKey key, int quantity, out List<Thing> made)
        {
            made = null;
            if (key == null || key.preserved || quantity <= 0)
            {
                return false;
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = quantity,
                    preserved = false
                },
                endpoint = null,
                mode = SDS_WithdrawMode.SpawnedTarget
            };
            return SDS_Rules.TryWithdraw(plan, null, out made) && made != null && made.Count > 0;
        }
    }
}
