using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 结构事实注册表：核心 / 锚点 / 稳固装置的位置、类型与可用性。
    /// 只读事实，不含建筑行为；容量由 SDS_Config 与可用稳固装置数量现算，事实本身不存加成。
    /// 登记 / 注销由 Vanilla 在 SpawnSetup / DeSpawn 时调用。
    /// 入 / 出：入或删结构事实，出全局可用性、单图可用性与品类容量；不写账本。
    /// 区别：账本管数量，本表管「建筑还在不在、通不通电」。
    /// </summary>
    internal sealed class SDS_StructureRegistry
    {
        private readonly List<SDS_StructureFact> facts = new List<SDS_StructureFact>();

        /// <summary>
        /// 登记 / 更新一条结构事实。
        /// 入：fact —— 建筑引用 + 类型 + 可用性。
        /// 出：无；同一建筑先删旧条目再新增（幂等）。
        /// 区别：Unregister 只删；Reconcile 是整表对账（补齐 + 剔除 + 修正）。
        /// </summary>
        public void Register(in SDS_StructureFact fact)
        {
            if (fact.Thing == null)
            {
                return;
            }
            Unregister(fact.Thing);
            facts.Add(fact);
        }

        /// <summary>
        /// 注销某建筑的全部事实。
        /// 入：thing —— 建筑引用；null 忽略。
        /// 出：无；引用匹配即删（同一建筑理论上只有一条）。
        /// 区别：Register 会先调本方法再登记；本方法不推进端点版本（由调用方决定）。
        /// </summary>
        public void Unregister(Thing thing)
        {
            if (thing == null)
            {
                return;
            }
            for (int i = facts.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(facts[i].Thing, thing))
                {
                    facts.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 以活体事实集为准整表对账。
        /// 入：live —— Vanilla 按地图重扫出的活体事实；null 视为无变化。
        /// 出：是否发生变化（有变化才应推进端点版本）。
        /// 语义：剔除非活体 / 已销毁条目、补齐漏登记、修正位置 / 类型 / 可用性字段。
        /// 区别：Register / Unregister 是单点增量；本方法是 250 tick 安全网，兜住漏回调。
        /// </summary>
        public bool Reconcile(List<SDS_StructureFact> live)
        {
            if (live == null)
            {
                return false;
            }
            HashSet<Thing> liveThings = new HashSet<Thing>();
            for (int i = 0; i < live.Count; i++)
            {
                Thing thing = live[i].Thing;
                if (thing != null && !thing.Destroyed)
                {
                    liveThings.Add(thing);
                }
            }
            bool changed = false;
            for (int i = facts.Count - 1; i >= 0; i--)
            {
                Thing thing = facts[i].Thing;
                if (thing == null || thing.Destroyed || !liveThings.Contains(thing))
                {
                    facts.RemoveAt(i);
                    changed = true;
                }
            }
            for (int i = 0; i < live.Count; i++)
            {
                SDS_StructureFact fact = live[i];
                if (fact.Thing == null || fact.Thing.Destroyed)
                {
                    continue;
                }
                int index = IndexOf(fact.Thing);
                if (index < 0)
                {
                    facts.Add(fact);
                    changed = true;
                    continue;
                }
                SDS_StructureFact current = facts[index];
                if (current.Map != fact.Map || current.Kind != fact.Kind
                    || current.Operational != fact.Operational)
                {
                    facts[index] = fact;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// 按引用查条目下标。
        /// 入：thing —— 建筑引用。
        /// 出：下标；找不到返回 -1。
        /// 区别：只读查询，不修改表。
        /// </summary>
        private int IndexOf(Thing thing)
        {
            for (int i = 0; i < facts.Count; i++)
            {
                if (ReferenceEquals(facts[i].Thing, thing))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 清空登记（新档静态清理 + 测试隔离）。
        /// 入：无。
        /// 出：无。
        /// 区别：Unregister 按引用删；本方法整表清空。生产侧由 SDS_Rules.RegisterAll 登记到 SDS_StaticState，
        ///       测试侧通过反射调用做用例隔离。
        /// </summary>
        public void Clear()
        {
            facts.Clear();
        }

        /// <summary>
        /// 世界上是否存在可用核心（超维空间总开关 G）。
        /// 入：无。
        /// 出：是否有「玩家可用核心」；O(N) 扫描。
        /// 区别：MapHasOperationalCore 只看单图；本属性跨图取或。
        /// </summary>
        public bool Available
        {
            get
            {
                for (int i = 0; i < facts.Count; i++)
                {
                    SDS_StructureFact fact = facts[i];
                    if (fact.Kind == SDS_StructureKind.Core && fact.Operational)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// 该地图是否有可用核心（芯片端口的本地可用性判据）。
        /// 入：map —— 目标地图；null 返回 false。
        /// 出：是否有同图可用核心。
        /// 区别：Available 是跨图总开关；本方法是单图事实。
        /// </summary>
        public bool MapHasOperationalCore(Map map)
        {
            if (map == null)
            {
                return false;
            }
            for (int i = 0; i < facts.Count; i++)
            {
                SDS_StructureFact fact = facts[i];
                if (fact.Kind == SDS_StructureKind.Core && fact.Operational && fact.Map == map)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 品类容量上限：设置基础值 + 所有可用稳固装置的容量加成。
        /// 入：无。
        /// 出：容量上限；每次都现读设置与可用事实。
        /// 区别：账本的 UsedCategoryCount 是已用数；本属性是上限，二者不互相缓存。
        /// </summary>
        public long TypeCapacity
        {
            get
            {
                long capacity = SDS_Config.GridCapacity;
                long perArray = SDS_Config.CapacityPerArray;
                for (int i = 0; i < facts.Count; i++)
                {
                    SDS_StructureFact fact = facts[i];
                    if (fact.Kind == SDS_StructureKind.CapacityArray && fact.Operational)
                    {
                        capacity += perArray;
                    }
                }
                return capacity;
            }
        }
    }
}
