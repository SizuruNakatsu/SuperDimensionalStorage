using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 刚物化、等待原版消费的来源标记；用于端点接收时排除自身产物。
    /// 标记带短寿命与上限，失败路径不会让实物长期以「在途」身份滞留（W49）。
    /// 入 / 出：入实物引用，出「是否在途」；只维护进程级引用表，不碰账本数量。
    /// 区别：展示物工厂与身份表在 Vanilla（`SDS_OutputUtility`）；本类只记「刚物化、别吸回」的实物。
    /// </summary>
    internal static class SDS_ProducedThingMarker
    {
        private const int LifetimeTicks = 60000;
        private const int MaxEntries = 8192;
        private const int PruneIntervalTicks = 500;

        private static readonly Dictionary<Thing, int> marked = new Dictionary<Thing, int>();
        private static int nextPruneTick;

        /// <summary>
        /// 打上「刚物化」标记。
        /// 入：thing —— 待标记实物；空 / 已销毁忽略。
        /// 出：无；表满时直接放弃新增（宁少排除，不无限膨胀）。
        /// 区别：Unmark 是反操作；本标记必须由交付 / 回充 / 落地三条终点路径撤销（W49）。
        /// </summary>
        public static void Mark(Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }
            int now = Now();
            PruneIfNeeded(now);
            if (marked.Count >= MaxEntries)
            {
                // 满了就不再新增：宁可少排除一批，也不能让标记表无限膨胀。
                return;
            }
            marked[thing] = now + LifetimeTicks;
        }

        /// <summary>
        /// 解除标记（交付完成 / 回充 / 落地失败退路）。
        /// 入：thing —— 实物引用；null 忽略。
        /// 出：无；不存在时无操作。
        /// 区别：Mark 是登记；本方法只删登记，不改实物状态。
        /// </summary>
        public static void Unmark(Thing thing)
        {
            if (thing != null)
            {
                marked.Remove(thing);
            }
        }

        /// <summary>
        /// 该实物是否仍在「刚物化」期间。
        /// 入：thing —— 实物引用。
        /// 出：是否在途；过期条目会被顺手删除并返回 false。
        /// 区别：本方法是端点拒收 / PUAH 拦截的唯一判定口，不得在别处另读标记表。
        /// </summary>
        public static bool IsMarked(Thing thing)
        {
            if (thing == null || marked.Count == 0)
            {
                return false;
            }
            int now = Now();
            PruneIfNeeded(now);
            if (!marked.TryGetValue(thing, out int expiry))
            {
                return false;
            }
            if (now >= expiry)
            {
                marked.Remove(thing);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 清空进程级标记表（新档 / 退出时由 SDS_StaticState 调用）。
        /// 入：无。
        /// 出：无。
        /// 区别：只清表，不触碰任何实物。
        /// </summary>
        public static void Reset()
        {
            marked.Clear();
            nextPruneTick = 0;
        }

        /// <summary>
        /// 当前游戏 tick（宿主时间）。
        /// 入：无。
        /// 出：TicksGame；非游戏进程返回 0。
        /// 区别：纯取时，不参与任何业务判定。
        /// </summary>
        private static int Now()
        {
            return GenTicks.TicksGame;
        }

        /// <summary>
        /// 按间隔清理过期 / 已销毁条目。
        /// 入：now —— 当前 tick。
        /// 出：无；表满时无视间隔立即清理，避免无法再登记。
        /// 区别：IsMarked 只处理被查询的那一件；本方法批量清理整表。
        /// </summary>
        private static void PruneIfNeeded(int now)
        {
            if (now < nextPruneTick && marked.Count < MaxEntries)
            {
                return;
            }
            nextPruneTick = now + PruneIntervalTicks;
            if (marked.Count == 0)
            {
                return;
            }
            List<Thing> expired = null;
            foreach (KeyValuePair<Thing, int> pair in marked)
            {
                Thing thing = pair.Key;
                if (thing == null || thing.Destroyed || now >= pair.Value)
                {
                    if (expired == null)
                    {
                        expired = new List<Thing>();
                    }
                    expired.Add(thing);
                }
            }
            if (expired == null)
            {
                return;
            }
            for (int i = 0; i < expired.Count; i++)
            {
                Thing thing = expired[i];
                if (thing != null)
                {
                    marked.Remove(thing);
                }
            }
        }
    }
}
