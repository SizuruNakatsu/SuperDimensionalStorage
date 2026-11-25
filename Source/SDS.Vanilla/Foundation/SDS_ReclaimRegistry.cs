using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 回收账本：Job 目标与受保护输出共用一种存档结构和一条回收入口。
    /// 世界事实（Job / Lord / tick）全部经 SDS_Host.ReclaimWorld；回充只调 SDS_Rules.ReturnThing，
    /// 本类不写账本内部、不寻路、不交付。
    /// </summary>
    internal static class SDS_ReclaimRegistry
    {
        private const int ProtectedOutputLifetimeTicks = 60000;
        private const int MaxEntries = 4096;

        private static readonly List<SDS_ReclaimSave> entries = new List<SDS_ReclaimSave>();

        /// <summary>
        /// 当前在册条目数（Job 目标 + 手上料 + 受保护输出，含按件未清空的条目）。
        /// 入：无。出：条目数；只读，不触发巡检。
        /// 区别：诊断 / 容量上限用；不代表「待回收实物件数」。
        /// </summary>
        public static int Count
        {
            get { return entries.Count; }
        }

        /// <summary>
        /// 清空回收账本（新游戏 / 重置静态时调用）。
        /// 入：无。出：无；丢弃全部条目，不回收、不回充，调用方须确保没有待回收实物。
        /// 区别：与 ImportFrom 的区别是「无来源、纯清空」。
        /// </summary>
        public static void Reset()
        {
            entries.Clear();
        }

        /// <summary>登记「为 Job 目标而取出的实物」：Job 结束仍未落地时由 ReclaimJobTarget 回充。</summary>
        /// <remarks>
        /// 入：job = 拥有该目标的 Job（loadID 作条目键）；thing = 已物化实物；count = 应回充件数（≤0 忽略）。
        /// 出：无；追加到该 Job 的非手上条目（同一 Job 多件共用一条），刷新 tick。
        /// 区别：与 TrackHand 的区别是「目标尚未到手」；与 RegisterProtectedOutput 的区别是 owner 是 Job 而非远征队。
        /// </remarks>
        public static void TrackJobTarget(Job job, Thing thing, int count)
        {
            if (job == null || thing == null || count <= 0)
            {
                return;
            }
            SDS_ReclaimSave entry = GetOrAdd(SDS_ReclaimPolicy.JobTarget, job.loadID, 0);
            if (entry == null)
            {
                return;
            }
            entry.things.Add(thing);
            entry.counts.Add(count);
            entry.tick = Now();
        }

        /// <summary>DoBill 材料物化进手后的「手上」账本；Job 失败时由 Patch 在原版 cleanup 前回充。</summary>
        /// <remarks>
        /// 入：job = 正在执行的 Job（loadID 作条目键）；thing = 已进入小人手上的实物。
        /// 出：无；写入 hand = true 的条目（件数记 1，实际按 stackCount 回收），刷新 tick。
        /// 区别：与 TrackJobTarget 的区别是本条目**只由 Patch 回收**（Sweep 跳过 hand），
        ///       因为手上料一旦被放到台边就应保持原版结果，不能靠超时巡检追回。
        /// </remarks>
        public static void TrackHand(Job job, Thing thing)
        {
            if (job == null || thing == null)
            {
                return;
            }
            SDS_ReclaimSave entry = GetOrAdd(SDS_ReclaimPolicy.JobTarget, job.loadID, 0, true);
            if (entry == null)
            {
                return;
            }
            entry.things.Add(thing);
            entry.counts.Add(1);
            entry.tick = Now();
        }

        /// <summary>
        /// 丢弃某 Job 的非手上条目（正常完成 / 已自行处理时调用），不回充。
        /// 入：loadID = Job.loadID；只删 policy=JobTarget 且 hand=false 的条目。
        /// 出：无；可能删除多条（历史遗留重复条目），全部静默移除。
        /// 区别：与 ReclaimJobTarget 的区别是「只忘不还」；手上料不受影响，需由 ReclaimHand 处理。
        /// </summary>
        public static void ForgetJobTarget(int loadID)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i] != null && entries[i].policy == SDS_ReclaimPolicy.JobTarget
                    && !entries[i].hand && entries[i].jobLoadID == loadID)
                {
                    entries.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 回收某 Job 的非手上条目：先回充，再移除；只处理第一条匹配条目。
        /// 入：loadID = Job.loadID。
        /// 出：None = 无匹配条目；其余为本次回充结果（Consumed / Abandoned / Returned 中最严重者）；
        ///     结果为 Consumed 时记一条警告（实物已被消耗，不回加账本）。
        /// 区别：与 ForgetJobTarget 的区别是「先还后忘」；与 Sweep 的区别是按 Job 精确触发，不看 tick / 世界存活。
        /// </summary>
        public static SDS_ReclaimOutcome ReclaimJobTarget(int loadID)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].policy == SDS_ReclaimPolicy.JobTarget
                    && !entries[i].hand && entries[i].jobLoadID == loadID)
                {
                    SDS_ReclaimOutcome outcome = ReclaimEntry(entries[i]);
                    entries.RemoveAt(i);
                    return outcome;
                }
            }
            return SDS_ReclaimOutcome.None;
        }

        /// <summary>只回收仍在小人手上的那件；已放到台边 / 被消耗的一律不碰。</summary>
        /// <remarks>
        /// 入：pawn = 当前执行者（null 或不在图上则放弃）；loadID = Job.loadID。
        /// 出：None = 无匹配的手上条目；Returned / Abandoned / Consumed = 本批最严重结果；
        ///     pawn 缺失或条目不在其 carryTracker 内时返回 Abandoned（条目已移除，不再重试）。
        /// 区别：与 ReclaimJobTarget 的区别是本方法逐件核对 holdingOwner，只拿回真正还在手上的；
        ///       条目一经移除即终结，不会因 Abandoned 而留到下次。
        /// </remarks>
        public static SDS_ReclaimOutcome ReclaimHand(Pawn pawn, int loadID)
        {
            SDS_ReclaimSave entry = null;
            for (int i = 0; i < entries.Count; i++)
            {
                SDS_ReclaimSave candidate = entries[i];
                if (candidate != null && candidate.policy == SDS_ReclaimPolicy.JobTarget
                    && candidate.hand && candidate.jobLoadID == loadID)
                {
                    entry = candidate;
                    entries.RemoveAt(i);
                    break;
                }
            }
            if (entry == null)
            {
                return SDS_ReclaimOutcome.None;
            }
            if (pawn == null || pawn.carryTracker == null || pawn.Map == null)
            {
                return SDS_ReclaimOutcome.Abandoned;
            }
            SDS_ReclaimOutcome result = SDS_ReclaimOutcome.None;
            for (int i = 0; i < entry.things.Count; i++)
            {
                Thing thing = entry.things[i];
                if (thing == null || thing.Destroyed)
                {
                    result = Worse(result, SDS_ReclaimOutcome.Consumed);
                    continue;
                }
                if (!ReferenceEquals(thing.holdingOwner, pawn.carryTracker.innerContainer))
                {
                    result = Worse(result, SDS_ReclaimOutcome.Abandoned);
                    continue;
                }
                if (!pawn.carryTracker.innerContainer.Remove(thing))
                {
                    result = Worse(result, SDS_ReclaimOutcome.Abandoned);
                    continue;
                }
                // 与旧实现一致：先回账本；失败按「背包 → 手持 → 脚下」退路交付，绝不吞物品。
                bool returned = SDS_Rules.ReturnThing(thing, SDS_StockChannel.Virtual,
                    pawn.Map, pawn, SDS_ReturnFallback.PawnInventory);
                result = Worse(result, returned
                    ? SDS_ReclaimOutcome.Returned
                    : SDS_ReclaimOutcome.Abandoned);
            }
            return result;
        }

        /// <summary>
        /// 登记「受保护输出」（远征队 / Lord 产出物）：owner 消失或超时后由 Sweep 回充。
        /// 入：thing = 产出实物（null 忽略）；lordLoadID = 持有它的 Lord.loadID。
        /// 出：无；追加到该 Lord 的条目（件数记登记时 stackCount），刷新 tick。
        /// 区别：与 TrackJobTarget 的区别是 owner 为 Lord 且带寿命上限（ProtectedOutputLifetimeTicks），
        ///       且 Sweep 会先摘远征队编成清单再回充。
        /// </summary>
        public static void RegisterProtectedOutput(Thing thing, int lordLoadID)
        {
            if (thing == null)
            {
                return;
            }
            SDS_ReclaimSave entry = GetOrAdd(SDS_ReclaimPolicy.ProtectedOutput, 0, lordLoadID);
            if (entry == null)
            {
                return;
            }
            entry.things.Add(thing);
            entry.counts.Add(thing.stackCount);
            entry.tick = Now();
        }

        /// <summary>巡检：owner 消失 / 超时 / Job 结束的条目回充；返回最严重的回收结果。</summary>
        /// <remarks>
        /// 入：now = 当前 tick（由宿主提供，保证可测）。
        /// 出：None = 无需回收或宿主未注入；否则为本轮所有回充结果中最严重者（Worse 排序）。
        /// 副作用：会就地清理「已结案」的条目（受保护输出已进容器 / 已销毁的按件移除），
        ///       远征队条目回充前先调 world.RemoveFromCaravan。
        /// 区别：与 ReclaimJobTarget / ReclaimHand 的区别是它按「世界存活 + 超时」批量判定，适合每 tick 低频调用；
        ///       hand 条目一律跳过，交 Patch 处理。
        /// </remarks>
        public static SDS_ReclaimOutcome Sweep(int now)
        {
            ISDS_ReclaimWorld world = SDS_Host.ReclaimWorld;
            if (world == null || entries.Count == 0)
            {
                return SDS_ReclaimOutcome.None;
            }
            SDS_ReclaimOutcome worst = SDS_ReclaimOutcome.None;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                SDS_ReclaimSave entry = entries[i];
                if (entry == null)
                {
                    entries.RemoveAt(i);
                    continue;
                }
                // 手上料只由 Patch 在原版 cleanup 前回收；Sweep 不处理仍在手上的实物。
                if (entry.hand)
                {
                    continue;
                }
                // 只有受保护输出按「世界存活 + 超时」巡检；Job 目标 / 手上料由 Patch 与 AuditRestored 处理，
                // 避免把「小人暂时不在图上、Job 仍在队列」的在途料提前收回（与旧实现一致）。
                if (entry.policy != SDS_ReclaimPolicy.ProtectedOutput)
                {
                    continue;
                }
                // 已被远征队搬走 / 进背包 / 进容器 / 已销毁的条目直接结案，
                // 不再等到 owner 消失或超时（避免悬空引用与存档膨胀）。
                for (int j = entry.things.Count - 1; j >= 0; j--)
                {
                    Thing thing = entry.things[j];
                    if (thing == null || thing.Destroyed)
                    {
                        RemoveAt(entry, j);
                        worst = Worse(worst, SDS_ReclaimOutcome.Consumed);
                        continue;
                    }
                    if (!thing.Spawned || thing.holdingOwner != null || thing.IsInAnyStorage())
                    {
                        RemoveAt(entry, j);
                        worst = Worse(worst, SDS_ReclaimOutcome.Abandoned);
                    }
                }
                if (entry.things.Count == 0)
                {
                    entries.RemoveAt(i);
                    continue;
                }
                bool alive = world.LordExists(entry.ownerLoadID)
                    && entry.tick > 0
                    && now - entry.tick < ProtectedOutputLifetimeTicks;
                if (alive)
                {
                    continue;
                }
                // 远征队若还在，先摘掉编成清单里的引用，再回充。
                for (int j = 0; j < entry.things.Count; j++)
                {
                    Thing thing = entry.things[j];
                    if (thing != null && !thing.Destroyed)
                    {
                        world.RemoveFromCaravan(entry.ownerLoadID, thing);
                    }
                }
                SDS_ReclaimOutcome outcome = Reclaim(entry);
                entries.RemoveAt(i);
                worst = Worse(worst, outcome);
            }
            return worst;
        }

        /// <summary>
        /// 读档后审计一次（在 FinalizeInit 调用，此时地图实物已 spawn）：
        /// 清理 Job 已不存在的手上料 → 回收 Job 已不存在的地面料 → 统一巡检。
        /// 入：无（tick / Job 存在性取自 SDS_Host.ReclaimWorld）。
        /// 出：无；宿主未注入时静默跳过。
        /// 区别：与 Sweep 的区别是它额外处理 Job 目标与手上料；Sweep 只巡检受保护输出。
        /// </summary>
        internal static void AuditRestored()
        {
            ISDS_ReclaimWorld world = SDS_Host.ReclaimWorld;
            if (world == null)
            {
                return;
            }
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                SDS_ReclaimSave entry = entries[i];
                if (entry == null)
                {
                    entries.RemoveAt(i);
                    continue;
                }
                if (entry.policy != SDS_ReclaimPolicy.JobTarget || world.JobExists(entry.jobLoadID))
                {
                    continue;
                }
                if (entry.hand)
                {
                    // 手上料只清账本：实物仍在背包 / 手上，交给原版 cleanup 按自身规则处理。
                    entries.RemoveAt(i);
                    continue;
                }
                ReclaimEntry(entry);
                entries.RemoveAt(i);
            }
            Sweep(world.CurrentTick);
        }

        /// <summary>
        /// 存档导出：把内部条目**深拷贝**后追加到目标列表（things / counts 都是新列表）。
        /// 入：into = 目标列表，null 忽略。
        /// 出：无；不清空目标列表，追加后的副本与原条目互不影响。
        /// 区别：与 ImportFrom 反向；深拷贝是为了让存档侧持有的列表在 Reset / Sweep 后仍然安全。
        /// </summary>
        internal static void ExportTo(List<SDS_ReclaimSave> into)
        {
            if (into == null)
            {
                return;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                SDS_ReclaimSave entry = entries[i];
                if (entry == null || entry.things == null)
                {
                    continue;
                }
                SDS_ReclaimSave copy = new SDS_ReclaimSave
                {
                    policy = entry.policy,
                    jobLoadID = entry.jobLoadID,
                    ownerLoadID = entry.ownerLoadID,
                    tick = entry.tick,
                    hand = entry.hand
                };
                // 与旧实现一致：只写仍存活的实物，避免把已销毁引用写进存档。
                for (int j = 0; j < entry.things.Count; j++)
                {
                    Thing thing = entry.things[j];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    copy.things.Add(thing);
                    copy.counts.Add(j < entry.counts.Count ? entry.counts[j] : thing.stackCount);
                }
                if (copy.things.Count == 0)
                {
                    continue;
                }
                into.Add(copy);
            }
        }

        /// <summary>
        /// 存档导入：清空现有条目后**直接接管**来源列表中的条目对象（不拷贝）。
        /// 入：from = 存档侧列表，可空；null 或 things / counts 为 null 的条目跳过。
        /// 出：无；导入后账本与存档对象共享同一 SDS_ReclaimSave 实例。
        /// 区别：与 ExportTo 的深拷贝不对称——因为读档后存档对象即由账本独占。
        /// </summary>
        internal static void ImportFrom(List<SDS_ReclaimSave> from)
        {
            entries.Clear();
            if (from == null)
            {
                return;
            }
            for (int i = 0; i < from.Count; i++)
            {
                SDS_ReclaimSave entry = from[i];
                if (entry == null || entry.things == null || entry.things.Count == 0 || entry.counts == null)
                {
                    continue;
                }
                entries.Add(entry);
            }
        }

        /// <summary>
        /// 条目级回收 + 诊断：Reclaim 的唯一告警包装。
        /// 入：entry = 待回收条目。出：Reclaim 的结果。
        /// 区别：与 Reclaim 的区别是 Consumed 时补一条一次性警告（Job 目标审计与精确回收共用，避免两处各写一份）。
        /// </summary>
        private static SDS_ReclaimOutcome ReclaimEntry(SDS_ReclaimSave entry)
        {
            SDS_ReclaimOutcome outcome = Reclaim(entry);
            if (outcome == SDS_ReclaimOutcome.Consumed)
            {
                SDS_Diag.Warn("回收时实物已被消耗（不回加账本）",
                    "jobLoadID=" + (entry != null ? entry.jobLoadID : 0));
            }
            return outcome;
        }

        /// <summary>
        /// 条目级回充：逐件把「仍在图上、无主、未入储存」的实物拆出并按通道回充账本。
        /// 入：entry = 待回收条目（things / counts 按索引对齐，counts 缺失时取 stackCount）。
        /// 出：None = 无实际回充（条目为空 / 件数 ≤0）；Consumed = 有件已销毁；Abandoned = 已被第三方
        ///     拿走 / 已进容器 / 账本缺失；Returned = 至少一件成功回充。多件时取最严重者。
        /// 区别：与 ReclaimHand 的区别是不核对持有者、直接从地图取回；与 ReturnUtility 的区别是本方法
        ///       先解除禁搬（回充是「在途」终点），再按 RequiresPreservedStorage 选保真 / 虚拟回充。
        /// </summary>
        private static SDS_ReclaimOutcome Reclaim(SDS_ReclaimSave entry)
        {
            if (SDS_Core.Ledger == null)
            {
                return SDS_ReclaimOutcome.Abandoned;
            }
            SDS_ReclaimOutcome worst = SDS_ReclaimOutcome.None;
            for (int i = 0; i < entry.things.Count; i++)
            {
                Thing thing = entry.things[i];
                if (thing == null)
                {
                    continue;
                }
                if (thing.Destroyed)
                {
                    worst = Worse(worst, SDS_ReclaimOutcome.Consumed);
                    continue;
                }
                // 已被正常投递 / 被第三方拿走 / 进入储存区：保持原版结果，不追回。
                if (!thing.Spawned || thing.holdingOwner != null || thing.IsInAnyStorage())
                {
                    worst = Worse(worst, SDS_ReclaimOutcome.Abandoned);
                    continue;
                }
                int want = i < entry.counts.Count ? entry.counts[i] : thing.stackCount;
                want = Math.Min(want, thing.stackCount);
                if (want <= 0)
                {
                    continue;
                }
                Map map = thing.MapHeld;
                Thing part = want >= thing.stackCount ? thing : thing.SplitOff(want);
                if (part == null)
                {
                    continue;
                }
                // 回充 / 退路是「在途」的终点：先撤禁搬，再走唯一回充实现。
                CompForbiddable forbiddable = part.TryGetComp<CompForbiddable>();
                if (forbiddable != null && forbiddable.Forbidden)
                {
                    forbiddable.Forbidden = false;
                }
                bool preservedChannel = SDS_Rules.TryGetStockKey(part, out SDS_StockKey partKey)
                    && partKey != null && partKey.preserved;
                // 与旧实现一致：已 spawn 的实物失败时保持原地（None），拆出的未 spawn 件才走 Ground 落地。
                SDS_ReturnFallback fallback = part.Spawned ? SDS_ReturnFallback.None : SDS_ReturnFallback.Ground;
                bool returned = SDS_Rules.ReturnThing(part,
                    preservedChannel ? SDS_StockChannel.Preserved : SDS_StockChannel.Virtual,
                    map, null, fallback, thing.PositionHeld);
                worst = Worse(worst, returned
                    ? SDS_ReclaimOutcome.Returned
                    : SDS_ReclaimOutcome.Abandoned);
            }
            return worst;
        }

        /// <summary>
        /// 按索引同时移除条目的实物与件数（counts 越界时只删实物），保持两列表对齐。
        /// 入：entry = 条目，可空；index = 下标（越界忽略）。
        /// 出：无。
        /// 区别：仅内部使用；与 List.RemoveAt 的区别是维护 things / counts 的并行索引不变式。
        /// </summary>
        private static void RemoveAt(SDS_ReclaimSave entry, int index)
        {
            if (entry == null || index < 0 || index >= entry.things.Count)
            {
                return;
            }
            entry.things.RemoveAt(index);
            if (index < entry.counts.Count)
            {
                entry.counts.RemoveAt(index);
            }
        }

        /// <summary>
        /// 取两个回收结果中较严重的一个（依赖枚举定义顺序：None &lt; Returned &lt; Abandoned &lt; Consumed）。
        /// 入：a / b = 待比较结果。出：较严重者；相等时返回 a。
        /// 区别：仅聚合语义，不做任何回收动作；枚举顺序即严重度，改动枚举须同步这里。
        /// </summary>
        private static SDS_ReclaimOutcome Worse(SDS_ReclaimOutcome a, SDS_ReclaimOutcome b)
        {
            return (int)a >= (int)b ? a : b;
        }

        /// <summary>
        /// 按（policy, jobLoadID, ownerLoadID, hand）四元组查条目，缺失则新建；达到 MaxEntries 时拒绝新建。
        /// 入：policy = 条目类型；jobLoadID / ownerLoadID = 两种 owner 键（不用的传 0）；hand = 是否手上料。
        /// 出：命中的现有条目或新建条目；账本已满返回 null（已记警告），调用方须放弃登记。
        /// 区别：与 RegisterProtectedOutput / TrackJobTarget 的关系是「唯一取条目口」；
        ///       同一四元组恒复用同一条目，因此多件实物共用一个 tick。
        /// </summary>
        private static SDS_ReclaimSave GetOrAdd(SDS_ReclaimPolicy policy, int jobLoadID, int ownerLoadID,
            bool hand = false)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                SDS_ReclaimSave entry = entries[i];
                if (entry != null && entry.policy == policy && entry.jobLoadID == jobLoadID
                    && entry.ownerLoadID == ownerLoadID && entry.hand == hand)
                {
                    return entry;
                }
            }
            if (entries.Count >= MaxEntries)
            {
                SDS_Diag.Warn("回收账本已满", "entries=" + entries.Count);
                return null;
            }
            SDS_ReclaimSave created = new SDS_ReclaimSave
            {
                policy = policy,
                jobLoadID = jobLoadID,
                ownerLoadID = ownerLoadID,
                hand = hand,
                tick = Now()
            };
            entries.Add(created);
            return created;
        }

        /// <summary>
        /// 当前 tick：取自宿主（SDS_Host.ReclaimWorld），宿主未注入时返回 0。
        /// 入：无。出：非负 tick，0 表示「未知」。
        /// 区别：与 Sweep(now) 的入参同源；注意 tick = 0 会让受保护输出在 Sweep 里被判为超期（0 不算有效起点）。
        /// </summary>
        private static int Now()
        {
            ISDS_ReclaimWorld world = SDS_Host.ReclaimWorld;
            return world != null ? world.CurrentTick : 0;
        }
    }
}
