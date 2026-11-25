using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 账本身份占用状态：未持有 / 同引用已持有 / 同 thingID 的其他实例。
    /// 由 SDS_Ledger.ClassifyPreservedIdentity 唯一产出，供存入 / 回充 / 待恢复重试共用。
    /// </summary>
    internal enum SDS_PreservedIdentityState
    {
        None,
        SameReference,
        SameThingId
    }

    /// <summary>
    /// 唯一账本：虚拟数量 + 堆平均状态、保真真身、品类禁用、取货预留、三个版本号、存档。
    /// 保真真自由账本私有的 ThingOwner 持有；holder 的 GetChildHolders 恒为空，绝不递归暴露。
    /// </summary>
    internal sealed class SDS_Ledger : IExposable, ISDS_LedgerRead, ISDS_LedgerHost
    {
        internal const int CurrentDataVersion = 1;

        /// <summary>
        /// 保真真身的私有持有者；本身不存数据，只把 owner 句柄暴露给原版容器接口。
        /// 入：owner —— 唯一账本实例。
        /// 出：无。
        /// 区别：与端点/建筑 holder 不同，它不进入地图 holder 树，也不递归暴露内容。
        /// </summary>
        private sealed class SDS_PreservedHolder : IThingHolder
        {
            private readonly SDS_Ledger owner;

            /// <summary>
            /// 入：owner —— 唯一账本实例。
            /// 出：无。
            /// 区别：只保存引用，不复制任何库存数据。
            /// </summary>
            public SDS_PreservedHolder(SDS_Ledger owner)
            {
                this.owner = owner;
            }

            /// <summary>
            /// 入：无。
            /// 出：恒为 null。
            /// 区别：与普通容器不同，账本 holder 没有父链，避免被地图级枚举顺着父链找到。
            /// </summary>
            public IThingHolder ParentHolder
            {
                get { return null; }
            }

            /// <summary>
            /// 入：outChildren —— 外部收集列表。
            /// 出：无（恒为空）。
            /// 区别：GetDirectlyHeldThings 给出本层内容，本方法必须为空，禁止保真真身被递归枚举。
            /// </summary>
            public void GetChildHolders(List<IThingHolder> outChildren)
            {
            }

            /// <summary>
            /// 入：无。
            /// 出：账本私有的 preservedOwner。
            /// 区别：只给原版容器语义与自检使用；不把它挂进地图 holder 树，也不公开给 Vanilla。
            /// </summary>
            public ThingOwner GetDirectlyHeldThings()
            {
                return owner.preservedOwner;
            }
        }

        private readonly Dictionary<SDS_StockKey, long> stock = new Dictionary<SDS_StockKey, long>();
        private readonly Dictionary<SDS_StockKey, SDS_StockVitals> stockVitals =
            new Dictionary<SDS_StockKey, SDS_StockVitals>();
        private readonly ThingOwner<Thing> preservedOwner;
        private readonly HashSet<SDS_StockKey> forbiddenKeys = new HashSet<SDS_StockKey>();

        // 派生索引：只在结构变化后的首次查询重建；读侧查询不再全量扫描。
        private readonly Dictionary<SDS_StockKey, List<Thing>> preservedByKey =
            new Dictionary<SDS_StockKey, List<Thing>>();
        private readonly Dictionary<ThingDef, List<SDS_StockKey>> preservedKeysByInnerDef =
            new Dictionary<ThingDef, List<SDS_StockKey>>();
        // 防御性引用去重：ThingOwner 正常不会重复持有同一实例，
        // 保留它是为了挡住外部/旧档写入的同一引用重复条目；ID 去重见 preservedMemberIds。
        private readonly HashSet<Thing> preservedMembers = new HashSet<Thing>();
        // 保真真身以 thingID 为物理身份：同一 ID 只允许一个实例进入派生索引（数量口径唯一）。
        private readonly HashSet<int> preservedMemberIds = new HashSet<int>();
        private bool preservedIndexDirty = true;
        private readonly Dictionary<ThingDef, List<SDS_StockKey>> keysByDef =
            new Dictionary<ThingDef, List<SDS_StockKey>>();
        private int keysByDefVersion = int.MinValue;
        private readonly HashSet<SDS_StockKey> categoryKeys = new HashSet<SDS_StockKey>();
        private int categoryVersion = int.MinValue;
        private bool categoryDirty = true;

        private List<SDS_StockEntry> stockEntries = new List<SDS_StockEntry>();
        private List<SDS_StockKey> forbiddenScratch = new List<SDS_StockKey>();
        private List<SDS_Reservation> reservations = new List<SDS_Reservation>();
        private List<Thing> preservedScratch = new List<Thing>();

        // 存档版本锚点：当前没有迁移消费方，保留给后续升版判断「旧档 → 新版」；
        // 缺节点时按 1 读取（见 ExposeData），不得跟随 CurrentDataVersion 漂移。
        private int dataVersion = CurrentDataVersion;
        private int stockVersion;
        private int reservationVersion;
        private int filterVersion;
        private int tickCounter;

        /// <summary>读档时未能放回账本的真身（transient，不入档）；巡检重试直至成功或失效。</summary>
        private readonly List<Thing> pendingPreservedRestore = new List<Thing>();

        /// <summary>
        /// 构造一个空账本（零库存 / 零预留 / 全部索引待建）。
        /// 入：无。
        /// 出：无；内部创建私有 preservedOwner。
        /// 区别：唯一允许的构造点是 SDS_Core.CreateLedger；存档壳只 Attach，不 new。
        /// </summary>
        public SDS_Ledger()
        {
            preservedOwner = new ThingOwner<Thing>(new SDS_PreservedHolder(this), false);
        }

        // ---------- 只读 ----------

        /// <summary>
        /// 虚拟通道的账面数量。
        /// 入：key —— 品类键；null 视为未知键。
        /// 出：账面件数；未知键 / 未入账返回 0。
        /// 区别：保真键恒返回 0（本方法只看虚拟字典），保真件数必须用 GetPreservedCount；
        ///       判断「能取多少」必须用 SDS_Availability，而不是本方法。
        /// </summary>
        public long GetCount(SDS_StockKey key)
        {
            if (key == null)
            {
                return 0L;
            }
            return stock.TryGetValue(key, out long value) ? value : 0L;
        }

        /// <summary>
        /// 该键被全部归属占用的预留合计（不区分通道之外的条件，按 key 精确匹配）。
        /// 入：key —— 品类键；null 返回 0。
        /// 出：预留件数；无预留返回 0。
        /// 区别：GetReserved(jobLoadID, key) 只算某个归属；GetAvailable 会自动扣掉本值。
        /// </summary>
        public long GetReserved(SDS_StockKey key)
        {
            if (key == null)
            {
                return 0L;
            }
            long sum = 0L;
            for (int i = 0; i < reservations.Count; i++)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation != null && reservation.key != null && reservation.key.SameAs(key))
                {
                    sum += reservation.count;
                }
            }
            return sum;
        }

        /// <summary>
        /// 虚拟通道可用量：max(0, 账面 − 预留)。
        /// 入：key —— 品类键；未解析（thingDef 为空）返回 0。
        /// 出：可用件数，恒落在 [0, 账面]；会触发 E2 断言。
        /// 区别：GetPreservedAvailable 是保真通道的同口径版本；模块面读可用量必须走
        ///       SDS_Availability（它还会叠加禁用与全局可用性门），不得直接调本方法。
        /// </summary>
        public long GetAvailable(SDS_StockKey key)
        {
            if (key == null || !key.Resolved)
            {
                return 0L;
            }
            long total = GetCount(key);
            long reserved = GetReserved(key);
            long available = total - reserved;
            if (available < 0L)
            {
                available = 0L;
            }
            SDS_Diag.AssertAvailableWithinTotal(key, total, available);
            return available;
        }

        /// <summary>
        /// 该品类是否被玩家禁用。
        /// 入：key —— 品类键；null 返回 false。
        /// 出：是否在禁用集合中。
        /// 区别：只看禁用集合，不看库存与可用量；禁用不删账本数据，只影响读侧口径。
        /// </summary>
        public bool IsForbidden(SDS_StockKey key)
        {
            return key != null && forbiddenKeys.Contains(key);
        }

        /// <summary>
        /// 切换品类禁用。
        /// 入：key —— 品类键（thingDef 必填）；forbidden —— true 加禁用，false 解除。
        /// 出：状态是否真正变化；已是目标状态返回 false。
        /// 副作用：真正变化时 StockVersion++（UI / 镜像据版本重建）。
        /// 区别：BumpFilterVersion 只推筛选版本，不碰禁用集合。
        /// </summary>
        internal bool SetForbidden(SDS_StockKey key, bool forbidden)
        {
            if (key == null || key.thingDef == null)
            {
                return false;
            }
            bool changed = forbidden ? forbiddenKeys.Add(key) : forbiddenKeys.Remove(key);
            if (!changed)
            {
                return false;
            }
            stockVersion++;
            return true;
        }

        /// <summary>
        /// 存活保真真身的唯一判据：未销毁且内容完整（尸体带内层 / 缩装带内层物）。
        /// 入：thing —— 待判定真身，可空。
        /// 出：true = 可计入数量、可进入候选、可参与读档去重。
        /// 区别：IsPreserved 只回答「是否由本账本持有」，不判内容是否可用；两者不得互换。
        /// </summary>
        internal static bool IsLivePreserved(Thing thing)
        {
            return thing != null && !thing.Destroyed && SDS_StockData.HasLiveContent(thing);
        }

        /// <summary>
        /// 该实物引用是否由本账本持有（保真通道）。
        /// 入：thing —— 实物引用；null 返回 false。
        /// 出：是否在 preservedOwner 中。
        /// 区别：只判所有权，不判内容是否完整（内容判据见 IsLivePreserved）；按 thingID 的
        ///       读档去重口径见 IsPreservedThingId。
        /// </summary>
        public bool IsPreserved(Thing thing)
        {
            return thing != null && preservedOwner.Contains(thing);
        }

        /// <summary>
        /// 该 thingID 是否存在存活保真真身；唯一的 ID 级判定口。
        /// 入：thingID —— 必须 > 0，否则返回 false。
        /// 出：是否存在同 ID 且「存活 + 内容完整」的真身；O(P) 扫描。
        /// 区别：与 IsPreserved 的引用判定不同，本方法能识别「同 ID 不同对象」的读档副本；
        ///       判据必须与数量口径同源（IsLivePreserved），否则会丢弃同 ID 的正常副本。
        /// </summary>
        public bool IsPreservedThingId(int thingID)
        {
            if (thingID <= 0)
            {
                return false;
            }
            for (int i = 0; i < preservedOwner.Count; i++)
            {
                Thing thing = preservedOwner[i];
                if (IsLivePreserved(thing) && thing.thingIDNumber == thingID)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 账本是否已占用该实物的身份（同一实物同一时刻只能有一个所有者）。
        /// 入：thing —— 待判定实物，可 null。
        /// 出：true = 引用已在账本，或存在同 thingID 的存活真身。
        /// 区别：IsPreserved 只判引用；IsPreservedThingId 只判 ID；本方法是 ClassifyPreservedIdentity
        ///       的布尔包装，供存入 / 待恢复重试使用，只读，不改变任何状态。
        /// </summary>
        internal bool IsPreservedIdentityOccupied(Thing thing)
        {
            return ClassifyPreservedIdentity(thing) != SDS_PreservedIdentityState.None;
        }

        /// <summary>
        /// 账本身份占用的唯一判定：把「同引用」与「同 thingID」分开。
        /// 入：thing —— 待判定实物，可 null。
        /// 出：None = 未持有；SameReference = 引用已在账本；SameThingId = 同 ID 的其他实例。
        /// 区别：IsPreserved / IsPreservedThingId 各自只答一半；本方法是所有写入口的唯一规则源，
        ///       回充入口据此区分「幂等成功」与「冲突拒绝」。
        /// </summary>
        internal SDS_PreservedIdentityState ClassifyPreservedIdentity(Thing thing)
        {
            if (thing == null)
            {
                return SDS_PreservedIdentityState.None;
            }
            if (preservedOwner.Contains(thing))
            {
                return SDS_PreservedIdentityState.SameReference;
            }
            if (thing.thingIDNumber > 0 && IsPreservedThingId(thing.thingIDNumber))
            {
                return SDS_PreservedIdentityState.SameThingId;
            }
            return SDS_PreservedIdentityState.None;
        }

        /// <summary>
        /// 保真通道的件数：桶内存活真身 stackCount 之和。
        /// 入：key —— 保真品类键（含 preserved 标记）；null 返回 0。
        /// 出：件数；同一 thingID 只计一次（与 AppendPreservedThings 同源）。
        /// 区别：GetCount 只覆盖虚拟通道；本方法不扣预留，可用量用 GetPreservedAvailable。
        /// </summary>
        public long GetPreservedCount(SDS_StockKey key)
        {
            if (key == null)
            {
                return 0L;
            }
            EnsurePreservedIndex();
            if (!preservedByKey.TryGetValue(key, out List<Thing> list) || list == null)
            {
                return 0L;
            }
            return SumLivePreserved(list);
        }

        /// <summary>
        /// 保真件数求和的唯一实现：只统计存活且内容完整的真身 stackCount。
        /// 入：things —— 同一 key 下的真身列表，可空。
        /// 出：件数合计。
        /// 区别：GetPreservedCount / PreservedSnapshot 共用；AppendPreservedThings 只追加不求和。
        /// </summary>
        private static long SumLivePreserved(List<Thing> things)
        {
            long sum = 0L;
            if (things == null)
            {
                return 0L;
            }
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (IsLivePreserved(thing))
                {
                    sum += thing.stackCount;
                }
            }
            return sum;
        }

        /// <summary>
        /// 保真通道可用量：max(0, 真身件数 − 预留)。
        /// 入：key —— 保真品类键；null 返回 0。
        /// 出：可用件数，恒 ≥ 0（外部销毁后预留可暂时大于件数，W22）。
        /// 区别：与 GetAvailable 同口径但走保真桶；同样不叠加禁用 / 全局门（那在 SDS_Availability）。
        /// </summary>
        internal long GetPreservedAvailable(SDS_StockKey key)
        {
            if (key == null)
            {
                return 0L;
            }
            long available = GetPreservedCount(key) - GetReserved(key);
            return available < 0L ? 0L : available;
        }

        /// <summary>
        /// 按 def 取虚拟品类键。
        /// 入：def —— 外壳 def；into —— 目标列表（会先清空）。
        /// 出：数量 > 0 的虚拟键；def / 列表为空时列表为空。
        /// 区别：CopyPreservedKeysForDef 走保真索引（缩装按内层 def）；CopyKeys 是全部键且不清空。
        /// </summary>
        public void CopyKeysForDef(ThingDef def, List<SDS_StockKey> into)
        {
            if (into == null)
            {
                return;
            }
            into.Clear();
            if (def == null)
            {
                return;
            }
            EnsureKeysByDef();
            if (keysByDef.TryGetValue(def, out List<SDS_StockKey> list) && list != null)
            {
                into.AddRange(list);
            }
        }

        /// <summary>
        /// 按真身 def 取保真品类键。
        /// 入：def —— 真身 def（缩装家具是内部建筑 def）；into —— 目标列表（会先清空）。
        /// 出：该 def 下有存活真身的键。
        /// 区别：CopyKeysForDef 是虚拟通道；本方法不含实物，只给键。
        /// </summary>
        public void CopyPreservedKeysForDef(ThingDef def, List<SDS_StockKey> into)
        {
            if (into == null)
            {
                return;
            }
            into.Clear();
            if (def == null)
            {
                return;
            }
            EnsurePreservedIndex();
            if (preservedKeysByInnerDef.TryGetValue(def, out List<SDS_StockKey> list) && list != null)
            {
                into.AddRange(list);
            }
        }

        /// <summary>
        /// 重建「def → 虚拟键」派生索引。
        /// 入：无。
        /// 出：无（就地重建 keysByDef）。
        /// 区别：EnsurePreservedIndex 重建保真索引；EnsureCategoryKeys 重建容量品类集合；
        ///       本索引只由 StockVersion 变化后的首次查询触发，不在每次读时全扫。
        /// </summary>
        private void EnsureKeysByDef()
        {
            if (keysByDefVersion == stockVersion)
            {
                return;
            }
            keysByDefVersion = stockVersion;
            keysByDef.Clear();
            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                SDS_StockKey key = pair.Key;
                if (key == null || key.thingDef == null || pair.Value <= 0L)
                {
                    continue;
                }
                if (!keysByDef.TryGetValue(key.thingDef, out List<SDS_StockKey> list) || list == null)
                {
                    list = new List<SDS_StockKey>();
                    keysByDef[key.thingDef] = list;
                }
                list.Add(key);
            }
        }

        /// <summary>
        /// 标记保真派生索引失效。
        /// 入：无。
        /// 出：无。
        /// 区别：InvalidateDerivedIndexes 连虚拟索引一起失效（设置变化用）；本方法只覆盖
        ///       保真索引与容量品类集合（真身集合变化用），成本更低。
        /// </summary>
        private void MarkPreservedIndexDirty()
        {
            preservedIndexDirty = true;
            categoryDirty = true;
        }

        /// <summary>
        /// 重建保真派生索引（key → 真身列表、内层 def → 键、成员集合、ID 集合）。
        /// 入：无。
        /// 出：无（就地重建）。
        /// 规则：跳过空 / 已销毁 / 无存活内容的真身；同一 thingID 只收第一个实例（数量口径唯一）。
        /// 区别：EnsureKeysByDef 只服务虚拟通道；本索引是保真读侧的唯一来源。
        /// </summary>
        private void EnsurePreservedIndex()
        {
            if (!preservedIndexDirty)
            {
                return;
            }
            preservedIndexDirty = false;
            preservedByKey.Clear();
            preservedKeysByInnerDef.Clear();
            preservedMembers.Clear();
            preservedMemberIds.Clear();
            for (int i = 0; i < preservedOwner.Count; i++)
            {
                Thing thing = preservedOwner[i];
                if (!IsLivePreserved(thing))
                {
                    continue;
                }
                if (!preservedMembers.Add(thing))
                {
                    continue;
                }
                // 同一真身只允许一个所有者：同 thingID 的重复实例不进入数量口径。
                if (thing.thingIDNumber > 0 && !preservedMemberIds.Add(thing.thingIDNumber))
                {
                    continue;
                }
                SDS_StockKey key = SDS_StockData.FromPreservedThing(thing);
                if (key == null || key.thingDef == null)
                {
                    continue;
                }
                if (!preservedByKey.TryGetValue(key, out List<Thing> list) || list == null)
                {
                    list = new List<Thing>();
                    preservedByKey[key] = list;
                    if (!preservedKeysByInnerDef.TryGetValue(key.thingDef, out List<SDS_StockKey> keys)
                        || keys == null)
                    {
                        keys = new List<SDS_StockKey>();
                        preservedKeysByInnerDef[key.thingDef] = keys;
                    }
                    keys.Add(key);
                }
                list.Add(thing);
            }
        }

        /// <summary>
        /// 已用品类数（容量口径）。
        /// 入：无。
        /// 出：虚拟 + 保真按 CategoryKeyOf 归并后的品类个数；外部不可修改。
        /// 区别：HasCategory 判单个品类是否已存在；本属性给总数，二者共用同一份 categoryKeys。
        /// </summary>
        public long UsedCategoryCount
        {
            get
            {
                EnsureCategoryKeys();
                return categoryKeys.Count;
            }
        }

        /// <summary>
        /// 让全部派生索引失效（品类口径 / 分组开关变化时调用）。
        /// 入：无。
        /// 出：无；下一次查询按键时重建。
        /// 区别：MarkPreservedIndexDirty 只失效保真与品类；本方法连虚拟 def 索引一起丢。
        /// </summary>
        public void InvalidateDerivedIndexes()
        {
            keysByDefVersion = int.MinValue;
            preservedIndexDirty = true;
            categoryDirty = true;
        }

        /// <summary>
        /// 该品类是否已被占用（容量「新品类」判定的唯一依据）。
        /// 入：category —— CategoryKeyOf 归并后的品类键；null 返回 false。
        /// 出：是否已在 categoryKeys 中。
        /// 区别：UsedCategoryCount 给总数；本方法只答单个品类。
        /// </summary>
        public bool HasCategory(SDS_StockKey category)
        {
            if (category == null)
            {
                return false;
            }
            EnsureCategoryKeys();
            return categoryKeys.Contains(category);
        }

        /// <summary>
        /// 重建容量口径的品类集合（虚拟 + 保真共用）。
        /// 入：无。
        /// 出：无（就地重建 categoryKeys）。
        /// 区别：EnsurePreservedIndex 建的是「键 → 真身」；本方法只保留 CategoryKeyOf 归并后的品类，
        ///       供 UsedCategoryCount / HasCategory 使用，二者不得各建一份。
        /// </summary>
        private void EnsureCategoryKeys()
        {
            if (!categoryDirty && categoryVersion == stockVersion)
            {
                return;
            }
            categoryDirty = false;
            categoryVersion = stockVersion;
            categoryKeys.Clear();
            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                if (pair.Key != null && pair.Value > 0L)
                {
                    SDS_StockKey category = SDS_StockData.CategoryKeyOf(pair.Key);
                    if (category != null)
                    {
                        categoryKeys.Add(category);
                    }
                }
            }
            AppendLivePreservedCategories(categoryKeys);
        }

        /// <summary>
        /// 把存活保真真身按「引用 + thingID」去重后并入容量品类集合。
        /// 入：into = 目标集合（null 忽略）。
        /// 出：无。
        /// 区别：与 EnsurePreservedIndex 共用同一去重口径（同引用 / 同 thingID 只取第一个）；
        ///       EnsureCategoryKeys 与自检的期望集合都只调用本方法，避免两处各写一份去重。
        /// </summary>
        private void AppendLivePreservedCategories(HashSet<SDS_StockKey> into)
        {
            if (into == null)
            {
                return;
            }
            HashSet<Thing> seenRefs = null;
            HashSet<int> seenIds = null;
            for (int i = 0; i < preservedOwner.Count; i++)
            {
                Thing thing = preservedOwner[i];
                if (!IsLivePreserved(thing))
                {
                    continue;
                }
                // 同一真身只允许一个所有者：与 EnsurePreservedIndex 同序、同判据。
                if (seenRefs == null)
                {
                    seenRefs = new HashSet<Thing>();
                }
                if (!seenRefs.Add(thing))
                {
                    continue;
                }
                if (thing.thingIDNumber > 0)
                {
                    if (seenIds == null)
                    {
                        seenIds = new HashSet<int>();
                    }
                    if (!seenIds.Add(thing.thingIDNumber))
                    {
                        continue;
                    }
                }
                SDS_StockKey key = SDS_StockData.FromPreservedThing(thing);
                SDS_StockKey category = key != null ? SDS_StockData.CategoryKeyOf(key) : null;
                if (category != null)
                {
                    into.Add(category);
                }
            }
        }

        /// <summary>
        /// 虚拟品类快照（副本，含堆平均状态）。
        /// 入：max —— 最多返回条数，0 表示不限。
        /// 出：按身份排序的条目；不含保真条目。
        /// 区别：PreservedSnapshot 只给保真件数；UnresolvedSnapshot 只给未解析条目。
        ///       条目里的 key 是账本内部实例，调用方只读，修改会破坏字典哈希。
        /// </summary>
        public List<SDS_StockEntry> Snapshot(int max)
        {
            List<SDS_StockEntry> result = new List<SDS_StockEntry>();
            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                if (pair.Key == null || pair.Value <= 0L)
                {
                    continue;
                }
                SDS_StockEntry entry = new SDS_StockEntry(pair.Key, pair.Value);
                if (stockVitals.TryGetValue(pair.Key, out SDS_StockVitals vitals) && vitals != null)
                {
                    entry.avgHitPoints = vitals.hitPoints;
                    entry.avgRotProgress = vitals.rotProgress;
                }
                result.Add(entry);
            }
            result.Sort((a, b) => SDS_StockKey.CompareIdentity(a.key, b.key));
            if (max > 0 && result.Count > max)
            {
                result.RemoveRange(max, result.Count - max);
            }
            return result;
        }

        /// <summary>
        /// 保真品类快照（副本）。
        /// 入：无。
        /// 出：按身份排序的键 + 件数；件数与 GetPreservedCount 同源（同 thingID 只算一次）。
        /// 区别：Snapshot 是虚拟通道；本方法不带堆平均状态（保真真身各自保存自身状态）。
        /// </summary>
        public List<SDS_StockEntry> PreservedSnapshot()
        {
            EnsurePreservedIndex();
            List<SDS_StockEntry> result = new List<SDS_StockEntry>();
            foreach (KeyValuePair<SDS_StockKey, List<Thing>> pair in preservedByKey)
            {
                long count = SumLivePreserved(pair.Value);
                if (pair.Key != null && count > 0L)
                {
                    result.Add(new SDS_StockEntry(pair.Key, count));
                }
            }
            result.Sort((a, b) => SDS_StockKey.CompareIdentity(a.key, b.key));
            return result;
        }

        /// <summary>
        /// 未解析 def 的虚拟条目快照（供诊断 / 旧档核对）。
        /// 入：无。
        /// 出：key 未解析但有 defName 且数量 > 0 的条目。
        /// 区别：Snapshot 含全部虚拟条目；本方法只筛未解析那一部分，不参与可用量 / 财富。
        /// </summary>
        public List<SDS_StockEntry> UnresolvedSnapshot()
        {
            List<SDS_StockEntry> result = new List<SDS_StockEntry>();
            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                if (pair.Key == null || pair.Value <= 0L || pair.Key.Resolved || !pair.Key.HasIdentity)
                {
                    continue;
                }
                result.Add(new SDS_StockEntry(pair.Key, pair.Value));
            }
            result.Sort((a, b) => SDS_StockKey.CompareIdentity(a.key, b.key));
            return result;
        }

        /// <summary>
        /// 把全部虚拟键追加到列表。
        /// 入：into —— 目标列表；null 忽略。
        /// 出：无；不清空目标列表。
        /// 区别：CopyKeysForDef 会先清空且只给某个 def；本方法是全部虚拟键的追加语义。
        /// </summary>
        public void CopyKeys(List<SDS_StockKey> into)
        {
            if (into == null)
            {
                return;
            }
            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                if (pair.Key != null && pair.Value > 0L)
                {
                    into.Add(pair.Key);
                }
            }
        }

        /// <summary>
        /// 把当前存活真身（引用）追加到列表，供原版候选注入。
        /// 入：into —— 目标列表；null 忽略。
        /// 出：无；不清空目标列表；同 thingID 只出现一次。
        /// 区别：PreservedSnapshot 给数量；CopyPreservedKeysForDef 给键；本方法给实物引用。
        /// </summary>
        public void AppendPreservedThings(List<Thing> into)
        {
            if (into == null)
            {
                return;
            }
            // 与数量口径同源：同一 thingID 只出现一次，第三方候选列表不会重复报价。
            EnsurePreservedIndex();
            foreach (KeyValuePair<SDS_StockKey, List<Thing>> pair in preservedByKey)
            {
                List<Thing> things = pair.Value;
                if (things == null)
                {
                    continue;
                }
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (IsLivePreserved(thing))
                    {
                        into.Add(thing);
                    }
                }
            }
        }

        /// <summary>
        /// 把账本持有的全部真身引用追加到列表（含已销毁 / 内容失效项），仅供诊断使用。
        /// 入：into —— 目标列表；null 忽略。
        /// 出：无；不清空目标列表。
        /// 区别：AppendPreservedThings 只给「可参与数量口径」的存活真身；本方法不做任何过滤。
        /// </summary>
        internal void AppendAllPreservedOwnerThings(List<Thing> into)
        {
            if (into == null)
            {
                return;
            }
            for (int i = 0; i < preservedOwner.Count; i++)
            {
                Thing thing = preservedOwner[i];
                if (thing != null)
                {
                    into.Add(thing);
                }
            }
        }

        /// <summary>
        /// 收集本次存档要写入的真身：owner 内「存活且内容完整」的 + 无主待恢复的。
        /// 入：into —— 目标列表（调用方负责先清空）；null 忽略。
        /// 出：无。
        /// 区别：AppendPreservedThings 是读侧候选（只存活）；本方法含待恢复项，供存档使用。
        /// </summary>
        internal void CollectPreservedForSave(List<Thing> into)
        {
            if (into == null)
            {
                return;
            }
            for (int i = 0; i < preservedOwner.Count; i++)
            {
                Thing thing = preservedOwner[i];
                if (thing != null && SDS_StockData.HasLiveContent(thing))
                {
                    into.Add(thing);
                }
            }
            // 读档待恢复真身不在 owner 里；若它既不在图上也没有其他持有者，
            // 必须随本节点一并落档，否则「读档失败 + 立刻存档」会永久丢件。
            for (int i = 0; i < pendingPreservedRestore.Count; i++)
            {
                Thing pending = pendingPreservedRestore[i];
                if (pending == null || pending.Destroyed || pending.Spawned
                    || !SDS_StockData.HasLiveContent(pending)
                    || preservedOwner.Contains(pending)
                    || (pending.holdingOwner != null && pending.holdingOwner != preservedOwner))
                {
                    continue;
                }
                into.Add(pending);
            }
        }

        /// <summary>
        /// 库存版本：入账 / 扣账 / 保真集合变化 / 禁用切换时推进。
        /// 入：无。
        /// 出：当前版本号（只读）。
        /// 区别：ReservationVersion 只跟预留走；FilterVersion 只跟存入筛选走。
        /// </summary>
        public int StockVersion
        {
            get { return stockVersion; }
        }

        /// <summary>
        /// 预留版本：建 / 消费 / 释放 / 审计清理时推进。
        /// 入：无。
        /// 出：当前版本号（只读）。
        /// 区别：StockVersion 只跟数量走；本版本用于可用量缓存失效。
        /// </summary>
        public int ReservationVersion
        {
            get { return reservationVersion; }
        }

        /// <summary>
        /// 存入筛选版本：全局筛选变化时推进。
        /// 入：无。
        /// 出：当前版本号（只读）。
        /// 区别：不参与库存与可用量口径；只让读侧缓存知道筛选变了。
        /// </summary>
        public int FilterVersion
        {
            get { return filterVersion; }
        }

        /// <summary>
        /// 推进筛选版本（唯一写入口）。
        /// 入：无。
        /// 出：无。
        /// 区别：SetForbidden 推的是 StockVersion；本方法只动 FilterVersion。
        /// </summary>
        internal void BumpFilterVersion()
        {
            filterVersion++;
        }

        // ---------- 虚拟通道写入 ----------

        /// <summary>
        /// 虚拟通道入账（只记数量，不碰实物）。
        /// 入：key —— 已解析的品类键；count —— 正整数；vitals —— 可空，非空则按件数加权并入堆平均。
        /// 出：是否入账；键 / 数量非法返回 false（此时零副作用）。
        /// 副作用：账面 += count、StockVersion++；账面归零后不会保留 vitals（由扣账侧清理）。
        /// 区别：TryConvertThingToVirtual 负责「实物 → 账面」的完整原子链；本方法只是数量入账，
        ///       不做容量 / 筛选 / 可用性门，这些必须由调用方先校验。
        /// </summary>
        internal bool TryDepositVirtual(SDS_StockKey key, long count, SDS_StockVitals vitals)
        {
            if (key != null && key.preserved)
            {
                // 通道守卫：保真键只能走 TryDepositPreserved，混入虚拟表会让该数量在其他口径里「消失」。
                SDS_Diag.Fail("虚拟入账/通道不匹配",
                    SDS_Diag.Ctx(null, null, null, null, "def=" + key.EffectiveDefName));
                return false;
            }
            if (key == null || key.thingDef == null || count <= 0L)
            {
                return false;
            }
            long oldCount = GetCount(key);
            stock[key] = oldCount + count;
            if (vitals != null)
            {
                if (!stockVitals.TryGetValue(key, out SDS_StockVitals current) || current == null)
                {
                    current = new SDS_StockVitals();
                    stockVitals[key] = current;
                }
                SDS_StockData.MergeVitals(current, key, oldCount, (int)Math.Min(count, int.MaxValue),
                    vitals.hitPoints, vitals.rotProgress);
            }
            stockVersion++;
            return true;
        }

        /// <summary>
        /// 虚拟通道取出：按 stackLimit 拆堆物化并扣账。
        /// 入：key —— 品类键；count —— 正整数。
        /// 出：made —— 未 spawn 的新实物列表（多堆）；失败时 made = null。
        /// 语义：先建齐再一次性扣账；任一 MakeThing 失败则整笔不扣账、已建实例丢弃（未入世界）。
        /// 区别：TryDebitVirtual 只扣不物化；TryConvertThingToVirtual 是反方向（实物转账面）。
        ///       本方法不校验预留，预留语义由事务层负责。
        /// </summary>
        internal bool TryWithdrawVirtual(SDS_StockKey key, int count, out List<Thing> made)
        {
            made = null;
            // 通道 / 解析守卫：保真键只能走保真取出；未解析键按 R13 带数量保留在 stock，
            // 供给归零（口径唯一在 SDS_Availability），这里再加一道门防 thingDef.stackLimit 空引用，
            // 与 TryDepositVirtual 的 thingDef 守卫对称。
            if (key == null || key.preserved || count <= 0 || GetCount(key) < count
                || key.thingDef == null)
            {
                return false;
            }
            stockVitals.TryGetValue(key, out SDS_StockVitals vitals);
            List<Thing> result = new List<Thing>();
            int remaining = count;
            int stackLimit = key.thingDef.stackLimit > 0 ? key.thingDef.stackLimit : 1;
            while (remaining > 0)
            {
                int take = Math.Min(remaining, stackLimit);
                Thing thing = SDS_StockData.MakeThing(key, take, vitals);
                if (thing == null)
                {
                    // 尚未扣账：直接丢弃已建实例即可（未 spawn，不进入世界）。
                    result.Clear();
                    made = null;
                    return false;
                }
                result.Add(thing);
                remaining -= take;
            }
            DebitVirtual(key, count);
            made = result;
            return true;
        }

        /// <summary>
        /// 虚拟通道扣账（不物化、不裁剪他人预留）。
        /// 入：key —— 品类键；count —— 正整数，必须 ≤ 当前账面。
        /// 出：是否扣账；不足返回 false 且不改变账面。
        /// 区别：TryConsumeVirtual 是外部销毁确认（允许按现有量扣、0 也放行）；
        ///       TryWithdrawVirtual 会顺带物化实物；RollbackVirtualDeposit 是唯一的回滚扣账口。
        /// </summary>
        // W22：外部销毁只扣账、不回改预留；可用量 = max(0, 物理 − 预留)，预留可暂时大于物理。
        internal bool TryDebitVirtual(SDS_StockKey key, int count)
        {
            if (key == null || count <= 0 || GetCount(key) < count)
            {
                return false;
            }
            DebitVirtual(key, count);
            return true;
        }

        /// <summary>
        /// 实物 → 虚拟账面的唯一原子实现：摘出原容器 → 入账 → 销毁。
        /// 入：thing —— 待消耗实物（未销毁、stackCount > 0）。
        /// 出：accepted —— 实际入账件数；失败为 0，且实物回到调用前状态。
        /// 回退：销毁未生效（含 Destroy 抛异常且未销毁）时，按当前存量回滚账面并尝试回挂原容器。
        /// 区别：TryDepositVirtual 只记数量、不碰实物；TryConsumeVirtual 只扣账、不碰实物；
        ///       本方法同时负责入账与销毁，任何入口都必须复用它，禁止各写一遍。
        /// </summary>
        internal bool TryConvertThingToVirtual(Thing thing, out int accepted)
        {
            accepted = 0;
            if (thing == null || thing.Destroyed || thing.stackCount <= 0)
            {
                return false;
            }
            // 通道守卫：账本已持有的真身不得走通用虚拟入账（保真 → 虚拟只允许 W34 专用迁移），
            // 否则会绕过 W34 的开关 / 预留 / 品类门禁，并在摘出后留下未标脏的保真索引。
            if (IsPreservedIdentityOccupied(thing))
            {
                SDS_Diag.Fail("虚拟入账/账本已持有该实物",
                    SDS_Diag.Ctx(thing.MapHeld, null, thing, null, "units=" + thing.stackCount));
                return false;
            }
            SDS_StockKey key = SDS_StockData.FromThing(thing);
            if (key == null || key.thingDef == null)
            {
                return false;
            }
            int units = thing.stackCount;
            SDS_StockVitals vitals = new SDS_StockVitals
            {
                hitPoints = SDS_StockData.CaptureHitPoints(thing),
                rotProgress = SDS_StockData.CaptureRotProgress(thing)
            };
            // 所有权转移只有一份实现：先按原版容器语义摘出，再入账；失败原状返回。
            if (!SDS_Ownership.TryDetachForDestroy(thing, out ThingOwner detachedFrom))
            {
                return false;
            }
            if (!TryDepositVirtual(key, units, vitals))
            {
                SDS_Ownership.TryRestoreTo(detachedFrom, thing);
                return false;
            }
            bool destroyed;
            try
            {
                thing.Destroy();
                destroyed = thing.Destroyed;
            }
            catch (Exception e)
            {
                // 销毁可能在中途抛异常：只认实际状态，不能把「已销毁」回滚成丢货。
                destroyed = thing.Destroyed;
                SDS_Diag.Fail("入账/销毁异常（按实际销毁状态处理）",
                    SDS_Diag.Ctx(thing.MapHeld, null, thing, null, "units=" + units), e);
            }
            if (!destroyed)
            {
                // 销毁未生效：撤回账本（夹到当前存量）并尽量回挂原容器，保持数量守恒。
                RollbackVirtualDeposit(key, units);
                SDS_Ownership.TryRestoreTo(detachedFrom, thing);
                return false;
            }
            accepted = units;
            return true;
        }

        /// <summary>
        /// 虚拟通道写入后的回滚（只供「入账 + 销毁」原子块使用）。
        /// 入：key —— 品类键；count —— 需要撤回的件数。
        /// 出：无。
        /// 语义：回滚量夹到当前存量；账面已空则跳过并记 Warn（外部消费抢先），绝不扣成负值。
        /// 区别：TryDebitVirtual 带足额校验、供正常消费；DebitVirtual 是私有无校验扣账；
        ///       本方法是唯一允许「少扣 / 不扣」的回滚口径（W22）。
        /// </summary>
        internal void RollbackVirtualDeposit(SDS_StockKey key, int count)
        {
            if (key == null || count <= 0)
            {
                return;
            }
            long have = GetCount(key);
            if (have <= 0L)
            {
                SDS_Diag.Warn("入账回滚/账面已空（外部消费抢先），跳过回滚",
                    "def=" + key.EffectiveDefName);
                return;
            }
            int take = (int)Math.Min((long)count, have);
            if (take < count)
            {
                SDS_Diag.Warn("入账回滚/账面不足（外部消费抢先），按现有量回滚",
                    "def=" + key.EffectiveDefName + " want=" + count + " have=" + have);
            }
            DebitVirtual(key, take);
        }

        /// <summary>
        /// 外部销毁确认（镜像被地图上的实物销毁时调用）。
        /// 入：key —— 品类键；count —— 期望扣减件数。
        /// 出：consumed —— 实际扣减件数；账面已空返回 true + 0（销毁是既成事实）。
        /// 语义：不足按现有量扣，不回改预留。
        /// 区别：TryDebitVirtual 要求足额且失败即 false；TryWithdrawVirtual 会物化；本方法只对账。
        /// </summary>
        internal bool TryConsumeVirtual(SDS_StockKey key, int count, out int consumed)
        {
            consumed = 0;
            if (key == null || count <= 0)
            {
                return false;
            }
            long have = GetCount(key);
            if (have <= 0L)
            {
                return true;
            }
            int take = (int)Math.Min((long)count, have);
            if (!TryDebitVirtual(key, take))
            {
                return false;
            }
            consumed = take;
            return true;
        }

        /// <summary>
        /// 私有扣账实现：不做任何校验，归零即删键与堆平均。
        /// 入：key —— 品类键；count —— 已由调用方校验过的件数。
        /// 出：无；StockVersion++。
        /// 区别：所有公开扣账口（TryDebitVirtual / TryConsumeVirtual / TryWithdrawVirtual /
        ///       RollbackVirtualDeposit）最终都落到这里，禁止在别处再写一份扣账。
        /// </summary>
        private void DebitVirtual(SDS_StockKey key, int count)
        {
            long have = GetCount(key);
            if (count > have)
            {
                // 数量下限保护：任何路径都不允许把账面扣成负值，超出部分按现有量截断并留证。
                SDS_Diag.Warn("扣账/超出账面（按现有量扣）",
                    "def=" + key.EffectiveDefName + " want=" + count + " have=" + have);
                count = (int)Math.Min((long)count, have);
            }
            if (count <= 0)
            {
                stockVersion++;
                return;
            }
            long left = have - count;
            if (left > 0L)
            {
                stock[key] = left;
            }
            else
            {
                stock.Remove(key);
                stockVitals.Remove(key);
            }
            stockVersion++;
        }

        // ---------- 保真通道写入 ----------

        /// <summary>
        /// 保真通道入账：把真身收进账本私有容器。
        /// 入：thing —— 可存且内容完整的实物（尸体带内层、缩装带内层物）。
        /// 出：是否接管；已在账本 / 转移失败返回 false（失败零副作用）。
        /// 副作用：所有权转移、保真索引失效、StockVersion++。
        /// 区别：TryDepositVirtual 会销毁实物只留数量；本方法保留真身、不重建；
        ///       所有权转移只经 SDS_Ownership.TryTransferTo。
        /// </summary>
        internal bool TryDepositPreserved(Thing thing)
        {
            if (!SDS_StockData.CanStore(thing) || !SDS_StockData.HasLiveContent(thing))
            {
                return false;
            }
            // 同一实物（引用或同 thingID）只允许一个所有者：重复入库一律拒收，
            // 避免同 ID 副本成为不计数的“幽灵真身”，在取出后又被索引重建算回库存。
            if (IsPreservedIdentityOccupied(thing))
            {
                SDS_Diag.Warn("保真入账/同一实物重复（已拒绝）",
                    "def=" + (thing.def != null ? thing.def.defName : "?")
                    + "#" + thing.thingIDNumber);
                return false;
            }
            // 原版原子转移：spawned 先 DeSpawn（失败原位恢复）；held 走 TryTransferToContainer。
            // 禁止直接置空 holdingOwner —— 那会让旧容器仍列出实物，形成双所有者。
            if (!SDS_Ownership.TryTransferTo(thing, preservedOwner, false))
            {
                return false;
            }
            MarkPreservedIndexDirty();
            stockVersion++;
            return true;
        }

        /// <summary>
        /// W34 存量迁移：把按 W33 已不再需要保真的武器 / 衣物一次性迁到虚拟通道。
        /// 入：canMigrate = 「该真身是否可迁移」判据（由 SDS.Rules 注入，Core 不反向依赖规则）。
        /// 出：实际转换件数；无候选 / canMigrate 为空返回 0。
        /// 语义：有事务预留的归属组跳过；迁移入账绕过容量 / 筛选 / 可用性门；
        ///       先入账再摘真身，摘出失败或异常按件回退。
        /// 区别：TryConvertThingToVirtual 是运行期「实物 → 账面」通用路径（走容量 / 筛选门）；
        ///       本方法是玩家确认后的一次性存量迁移，语义与门禁都不同，不得混用。
        /// </summary>
        // RULE:W34
        internal int ConvertPreservedGearToVirtual(Func<Thing, bool> canMigrate)
        {
            if (canMigrate == null || preservedOwner.Count == 0)
            {
                return 0;
            }
            List<Thing> candidates = null;
            for (int i = preservedOwner.Count - 1; i >= 0; i--)
            {
                Thing thing = preservedOwner[i];
                if (!canMigrate(thing))
                {
                    continue;
                }
                SDS_StockKey preservedKey = SDS_StockData.FromPreservedThing(thing);
                if (preservedKey != null && GetReserved(preservedKey) > 0L)
                {
                    continue; // 在途事务：留给玩家结清后再转
                }
                (candidates ?? (candidates = new List<Thing>())).Add(thing);
            }
            if (candidates == null)
            {
                return 0;
            }

            int converted = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing thing = candidates[i];
                if (thing == null || thing.Destroyed || thing.stackCount <= 0
                    || !preservedOwner.Contains(thing))
                {
                    continue;
                }
                SDS_StockKey key = SDS_StockData.FromThing(thing);
                if (key == null || key.thingDef == null)
                {
                    continue;
                }
                int amount = thing.stackCount;
                SDS_StockVitals vitals = new SDS_StockVitals
                {
                    hitPoints = SDS_StockData.CaptureHitPoints(thing),
                    rotProgress = SDS_StockData.CaptureRotProgress(thing)
                };
                // 先入账（迁移专用，不过容量 / 筛选 / 可用性门），再摘真身。
                if (!TryDepositVirtual(key, amount, vitals))
                {
                    continue;
                }
                bool removed = false;
                try
                {
                    removed = preservedOwner.Remove(thing);
                    if (!removed)
                    {
                        // 理论上不可达：回退账本，保持数量守恒。
                        DebitVirtual(key, amount);
                        continue;
                    }
                    MarkPreservedIndexDirty();
                    thing.Destroy();
                    converted += amount;
                }
                catch (Exception e)
                {
                    // 以实际销毁状态为准：已销毁 = 迁移成立；未销毁 = 撤回账本并尽量回挂真身。
                    if (thing.Destroyed)
                    {
                        converted += amount;
                    }
                    else
                    {
                        if (removed && !preservedOwner.Contains(thing)
                            && !SDS_Ownership.TryTransferTo(thing, preservedOwner, false))
                        {
                            SDS_Diag.Fail("强制虚拟化/真身回挂失败（实物可能悬空）",
                                SDS_Diag.Ctx(null, null, thing, null, "units=" + amount));
                        }
                        DebitVirtual(key, amount);
                        MarkPreservedIndexDirty();
                    }
                    SDS_Diag.Fail("强制虚拟化/转换失败", SDS_Diag.Ctx(thing: thing), e);
                }
            }
            if (converted > 0)
            {
                stockVersion++;
            }
            return converted;
        }

        /// <summary>
        /// 保真计划的实例级预校验：按 Thing 累计件数，校验存活 / 归属 / 累计不超 stackCount。
        /// 入：plan —— 逐件计划（ThingCount 列表）。
        /// 出：perThing —— Thing → 累计件数；失败返回 false。
        /// 区别：TryWithdrawPreservedPlan / TryReservePreservedPlan 共用本方法，禁止各写一遍。
        /// </summary>
        private bool TryCollectPreservedPlan(List<ThingCount> plan, out Dictionary<Thing, long> perThing)
        {
            perThing = null;
            if (plan == null || plan.Count == 0)
            {
                return false;
            }
            Dictionary<Thing, long> collected = new Dictionary<Thing, long>();
            for (int i = 0; i < plan.Count; i++)
            {
                Thing thing = plan[i].Thing;
                int units = plan[i].Count;
                if (thing == null || units <= 0 || !IsLivePreserved(thing)
                    || !preservedOwner.Contains(thing))
                {
                    return false;
                }
                collected.TryGetValue(thing, out long already);
                collected[thing] = already + units;
            }
            foreach (KeyValuePair<Thing, long> pair in collected)
            {
                if (pair.Value > pair.Key.stackCount)
                {
                    return false;
                }
            }
            perThing = collected;
            return true;
        }

        /// <summary>
        /// 保真通道按精确计划取出真身（不重建）。
        /// 入：plan —— 逐件计划；每条 count 必须 ≤ 该真身当前 stackCount。
        /// 出：made —— 摘出的真身（必要时拆堆）；任一条件不满足即整笔回滚、made = null。
        /// 区别：TryWithdrawVirtual 按键重建新实例；本方法取原始真身（尸体 / 缩装 / 基因载体）；
        ///       预留与事务状态由 SDS_WithdrawTransaction 负责，本方法只做数量与所有权。
        /// </summary>
        internal bool TryWithdrawPreservedPlan(List<ThingCount> plan, out List<Thing> made)
        {
            made = null;
            if (plan == null || plan.Count == 0)
            {
                return false;
            }
            // 先做整计划校验：同一真身在计划中出现多次时按「累计件数」校验。
            // 只逐条比较 units <= stackCount 会放过 [(A,3),(A,3)] 这类计划——
            // 建预留时能过，取出时第二条必然不足，形成永远取不出的假预留。
            if (!TryCollectPreservedPlan(plan, out _))
            {
                return false;
            }
            List<Thing> result = new List<Thing>();
            Thing current = null;
            int index = -1;
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    index = i;
                    // ThingCount 是值类型：plan[i] 不可能为 null，直接读字段。
                    Thing expected = plan[i].Thing;
                    current = expected;
                    int units = plan[i].Count;
                    // 足额口径（W21）：预校验已保证累计不超；这里保留逐条防御，
                    // 因为拆分后 stackCount 会在循环中变化。
                    if (expected == null || units <= 0 || units > expected.stackCount
                        || !preservedOwner.Contains(expected))
                    {
                        RollbackPreserved(result);
                        return false;
                    }
                    int remaining = units;
                    Thing piece = TakePreservedUnits(expected, units, ref remaining);
                    if (piece == null || remaining != 0)
                    {
                        if (piece != null)
                        {
                            result.Add(piece);
                        }
                        RollbackPreserved(result);
                        return false;
                    }
                    result.Add(piece);
                }
            }
            catch (Exception e)
            {
                // 异常路径同样必须整笔回滚：已摘出的真身回到账本，绝不留在「半摘出」状态。
                RollbackPreserved(result);
                SDS_Diag.Fail("保真取出异常（已回滚）",
                    SDS_Diag.Ctx(null, null, current, null, "index=" + index), e);
                made = null;
                return false;
            }
            stockVersion++;
            made = result;
            return true;
        }

        /// <summary>
        /// 保真取出的整笔回滚：把已摘出的真身放回账本容器。
        /// 入：things —— 本次已摘出的件（会被清空）。
        /// 出：无。
        /// 区别：RollbackVirtualDeposit 回滚的是数量；本方法回滚的是实物所有权；
        ///       回挂同样只经 SDS_Ownership.TryTransferTo。
        /// </summary>
        private void RollbackPreserved(List<Thing> things)
        {
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed)
                {
                    // 回滚同样走唯一所有权转移；已在账本里的实例不重复入列。
                    if (!preservedOwner.Contains(thing)
                        && !SDS_Ownership.TryTransferTo(thing, preservedOwner, false))
                    {
                        // 回滚失败意味着实物既不在账本也不在世界，必须留下可定位的证据。
                        SDS_Diag.Fail("保真回滚/转移失败（实物可能悬空）",
                            SDS_Diag.Ctx(null, null, thing, null,
                                "def=" + (thing.def != null ? thing.def.defName : "?")
                                + "#" + thing.thingIDNumber));
                    }
                    MarkPreservedIndexDirty();
                }
            }
            things.Clear();
        }

        /// <summary>
        /// 从保真容器摘出至多 need 单位（需要时拆堆）。
        /// 入：thing —— 目标真身；need —— 期望件数；remaining —— 剩余量（按实取递减）。
        /// 出：摘出的实物（可能等于 thing，也可能是拆分出的新件）。
        /// 区别：RollbackPreserved 是反操作；本方法不校验足额，调用方必须检查 remaining == 0。
        /// </summary>
        private Thing TakePreservedUnits(Thing thing, int need, ref int remaining)
        {
            int take = Math.Min(need, thing.stackCount);
            Thing piece = thing;
            if (take < thing.stackCount)
            {
                piece = thing.SplitOff(take);
            }
            if (piece == null)
            {
                return null;
            }
            // 拆出件通常已脱离容器；仍归容器时按引用移除，失败则整条计划失败（由调用方回滚）。
            if (piece.holdingOwner == preservedOwner && !preservedOwner.Remove(piece))
            {
                SDS_Diag.Fail("保真取出/移除失败（仍归账本）",
                    SDS_Diag.Ctx(null, null, piece, null,
                        "def=" + (piece.def != null ? piece.def.defName : "?")
                        + "#" + piece.thingIDNumber));
                return null;
            }
            MarkPreservedIndexDirty();
            remaining -= take;
            return piece;
        }

        // ---------- 预留 ----------

        /// <summary>
        /// 虚拟通道按 key 建预留（多 key 原子，W11）。
        /// 入：transactionId —— 归属 id；keys / counts —— 等长且同序；pawn —— 可空，仅作审计线索。
        /// 出：是否整笔预留成功；任一 key 不足即 false 且不留部分预留。
        /// 语义：先对全部 key 校验可用量，再统一写入；同归属同 key 合并累加。
        /// 区别：TryReservePreserved 走保真存量；TryReservePreservedPlan 按真身逐件计划
        ///       并额外校验单件上限；本方法只按 key 汇总。
        /// </summary>
        internal bool TryReserve(int transactionId, List<SDS_StockKey> keys, List<int> counts,
            Pawn pawn = null)
        {
            return TryReserveCore(transactionId, keys, counts, pawn, false);
        }

        /// <summary>
        /// 保真通道按 key 建预留（多 key 原子）。
        /// 入：transactionId / keys / counts / pawn，含义同 TryReserve。
        /// 出：是否整笔预留成功。
        /// 区别：校验的是 GetPreservedAvailable（真身件数 − 预留），不是虚拟账面；
        ///       不知道「具体哪几件」，逐件校验在 TryReservePreservedPlan。
        /// </summary>
        internal bool TryReservePreserved(int transactionId, List<SDS_StockKey> keys,
            List<int> counts, Pawn pawn)
        {
            return TryReserveCore(transactionId, keys, counts, pawn, true);
        }

        /// <summary>
        /// 虚拟 / 保真按 key 预留的唯一实现：先按 key 汇总需求，全部校验通过后再统一写入（W11）。
        /// 入：transactionId —— 归属 id；keys / counts —— 等长且同序；pawn —— 可空；preserved —— 通道。
        /// 出：是否整笔预留成功；任一 key 不足 / 超 int 上限即 false 且不留部分预留。
        /// 区别：TryReserve / TryReservePreserved 只是本方法的两条通道入口。
        /// </summary>
        private bool TryReserveCore(int transactionId, List<SDS_StockKey> keys, List<int> counts,
            Pawn pawn, bool preserved)
        {
            if (keys == null || counts == null || keys.Count != counts.Count || keys.Count == 0)
            {
                return false;
            }
            Dictionary<SDS_StockKey, long> needed = new Dictionary<SDS_StockKey, long>();
            for (int i = 0; i < keys.Count; i++)
            {
                SDS_StockKey key = keys[i];
                int count = counts[i];
                if (key == null || count <= 0)
                {
                    return false;
                }
                needed.TryGetValue(key, out long current);
                needed[key] = current + count;
            }
            foreach (KeyValuePair<SDS_StockKey, long> pair in needed)
            {
                long available = preserved ? GetPreservedAvailable(pair.Key) : GetAvailable(pair.Key);
                if (available < pair.Value)
                {
                    return false;
                }
            }
            foreach (KeyValuePair<SDS_StockKey, long> pair in needed)
            {
                if (pair.Value > int.MaxValue)
                {
                    // 预留条目是 int：宁可整笔失败，也不做静默截断（否则调用方以为已足额预留）。
                    SDS_Diag.Fail("预留/数量超出 int 上限",
                        "def=" + pair.Key.EffectiveDefName + " want=" + pair.Value);
                    return false;
                }
                AddReservation(transactionId, pair.Key, (int)pair.Value, pawn);
            }
            reservationVersion++;
            return true;
        }

        /// <summary>
        /// 保真通道按「真身 + 件数」精确计划建预留。
        /// 入：jobLoadID —— 归属 id；pawn —— 归属小人（可空）；plan —— 逐件计划。
        /// 出：是否整笔预留成功；任一件不属于账本 / 该真身累计件数超上限 / 总量不足即 false。
        /// 区别：TryReservePreserved 只按 key 汇总；本方法是 Job 装载 / 保真补料的精确口径，
        ///       建预留时就保证「后面一定摘得出来」（同一真身多次出现按累计件数校验）。
        /// </summary>
        internal bool TryReservePreservedPlan(int jobLoadID, Pawn pawn, List<ThingCount> plan)
        {
            if (plan == null || plan.Count == 0)
            {
                return false;
            }
            // 第一遍：按真身累计（共用 TryCollectPreservedPlan），拦掉「逐条都不超、合计超了」的计划。
            if (!TryCollectPreservedPlan(plan, out Dictionary<Thing, long> perThing))
            {
                return false;
            }
            // 第二遍：按 key 汇总。用 long 累加，最后统一做 int 上限检查，
            // 对齐 TryReserve / TryReservePreserved 的「宁可整笔失败也不静默截断」。
            Dictionary<SDS_StockKey, long> totals = new Dictionary<SDS_StockKey, long>();
            foreach (KeyValuePair<Thing, long> pair in perThing)
            {
                SDS_StockKey key = SDS_StockData.FromPreservedThing(pair.Key);
                if (key == null)
                {
                    return false;
                }
                totals.TryGetValue(key, out long current);
                totals[key] = current + pair.Value;
            }
            foreach (KeyValuePair<SDS_StockKey, long> pair in totals)
            {
                if (pair.Value > int.MaxValue)
                {
                    SDS_Diag.Fail("预留/数量超出 int 上限",
                        "def=" + pair.Key.EffectiveDefName + " want=" + pair.Value);
                    return false;
                }
                long available = GetPreservedAvailable(pair.Key);
                if (available < pair.Value)
                {
                    return false;
                }
            }
            foreach (KeyValuePair<SDS_StockKey, long> pair in totals)
            {
                AddReservation(jobLoadID, pair.Key, (int)pair.Value, pawn);
            }
            reservationVersion++;
            return true;
        }

        /// <summary>
        /// 消费预留：按归属 + key 扣减指定数量（已实际取走时调用）。
        /// 入：transactionId —— 归属 id；key —— 品类键；count —— 消费件数。
        /// 出：实际消费件数（可能小于 count，调用方必须与物化件数比对）；扣到 0 的条目直接删除。
        /// 区别：ReleaseReservation 是整笔释放（交付完成 / 失败清理），不按数量；
        ///       本方法只扣减，不删除仍有剩余的条目。
        /// </summary>
        internal int ConsumeReservation(int transactionId, SDS_StockKey key, int count)
        {
            if (key == null || count <= 0)
            {
                return 0;
            }
            int left = count;
            for (int i = reservations.Count - 1; i >= 0 && left > 0; i--)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation == null || reservation.jobLoadID != transactionId
                    || reservation.key == null || !reservation.key.SameAs(key))
                {
                    continue;
                }
                int take = Math.Min(left, reservation.count);
                reservation.count -= take;
                left -= take;
                if (reservation.count <= 0)
                {
                    reservations.RemoveAt(i);
                }
            }
            reservationVersion++;
            if (left > 0)
            {
                SDS_Diag.Warn("预留/消费不足（账面与预留不一致）",
                    "owner=" + transactionId + " def=" + key.EffectiveDefName
                    + " want=" + count + " short=" + left);
            }
            return count - left;
        }

        /// <summary>
        /// 释放该归属的全部预留（幂等）。
        /// 入：jobLoadID —— 归属 id；不存在的 id 忽略。
        /// 出：无；ReservationVersion++。
        /// 区别：ConsumeReservation 按数量扣减；本方法整笔删除，供 Deliver / Rollback / 审计使用。
        /// </summary>
        internal void ReleaseReservation(int jobLoadID)
        {
            for (int i = reservations.Count - 1; i >= 0; i--)
            {
                if (reservations[i] != null && reservations[i].jobLoadID == jobLoadID)
                {
                    reservations.RemoveAt(i);
                }
            }
            reservationVersion++;
        }

        /// <summary>
        /// 该归属在该 key 上的预留数量。
        /// 入：jobLoadID —— 归属 id；key —— 品类键。
        /// 出：预留件数；无预留返回 0。
        /// 区别：GetReserved(key) 汇总全部归属；TryGetJobReservation 探测「该归属唯一的键」。
        /// </summary>
        internal long GetReserved(int jobLoadID, SDS_StockKey key)
        {
            if (key == null)
            {
                return 0L;
            }
            long sum = 0L;
            for (int i = 0; i < reservations.Count; i++)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation != null && reservation.jobLoadID == jobLoadID
                    && reservation.key != null && reservation.key.SameAs(key))
                {
                    sum += reservation.count;
                }
            }
            return sum;
        }

        /// <summary>
        /// 探测该归属的预留，并回填「唯一的键 + 合计件数」。
        /// 入：jobLoadID —— 归属 id。
        /// 出：key / total；该归属有多个键时返回 false（key = null、total = 0）。
        /// 区别：GetReserved(id, key) 需要指定键；HasJobReservation 只要「有没有」。
        /// </summary>
        internal bool TryGetJobReservation(int jobLoadID, out SDS_StockKey key, out int total)
        {
            key = null;
            total = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation == null || reservation.jobLoadID != jobLoadID
                    || reservation.key == null || reservation.count <= 0)
                {
                    continue;
                }
                if (key == null)
                {
                    key = reservation.key;
                }
                else if (!key.SameAs(reservation.key))
                {
                    key = null;
                    total = 0;
                    return false;
                }
                total += reservation.count;
            }
            return key != null && total > 0;
        }

        /// <summary>
        /// 该归属是否持有任何正数量预留。
        /// 入：jobLoadID —— 归属 id。
        /// 出：是否有预留。
        /// 区别：TryGetJobReservation 要明细；本方法只做存在性判断（防重复建预留）。
        /// </summary>
        internal bool HasJobReservation(int jobLoadID)
        {
            for (int i = 0; i < reservations.Count; i++)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation != null && reservation.jobLoadID == jobLoadID && reservation.count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 当前预留条目数（不是件数）。
        /// 入：无。
        /// 出：条目个数；纯读。
        /// 区别：CopyReservations 给内容；本属性只供门面在「无预留」时跳过审计。
        /// </summary>
        internal int ReservationCount
        {
            get { return reservations.Count; }
        }

        /// <summary>
        /// 预留快照（逐条复制，调用方修改副本不影响账本）。
        /// 入：into —— 目标列表（会先清空）。
        /// 出：无。
        /// 语义：只复制事实（归属 id / 键 / 件数 / pawn），不判活、不释放。
        /// 区别：CopyKeys 给库存键；SDS_Core.AuditReservations 消费本快照后按归属 id 释放。
        /// </summary>
        internal void CopyReservations(List<SDS_Reservation> into)
        {
            if (into == null)
            {
                return;
            }
            into.Clear();
            for (int i = 0; i < reservations.Count; i++)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation == null)
                {
                    continue;
                }
                into.Add(new SDS_Reservation
                {
                    jobLoadID = reservation.jobLoadID,
                    key = reservation.key,
                    count = reservation.count,
                    pawn = reservation.pawn
                });
            }
        }

        /// <summary>
        /// 只清结构性非法的预留条目（空键 / 非正数量）。
        /// 入：无。
        /// 出：清除条数；有清除时 ReservationVersion++。
        /// 区别：判定「归属是否还活着」不在这里，归 SDS_Core.AuditReservations（宿主事实）。
        /// </summary>
        internal int PruneMalformedReservations()
        {
            int removed = 0;
            for (int i = reservations.Count - 1; i >= 0; i--)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation == null || reservation.key == null || reservation.count <= 0)
                {
                    reservations.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
            {
                reservationVersion++;
            }
            return removed;
        }

        /// <summary>
        /// 预留的唯一写入口：同归属 + 同 key 合并累加，否则新建。
        /// 入：jobLoadID —— 归属 id；key —— 品类键；count —— 增量；pawn —— 可空，已有条目缺 pawn 时补上。
        /// 出：无。
        /// 区别：ConsumeReservation / ReleaseReservation 只做扣减与删除；本方法是唯一新增路径，
        ///       任何「按 key 建预留」都必须经过这里，不得直接往 reservations 里 Add。
        /// </summary>
        private void AddReservation(int jobLoadID, SDS_StockKey key, int count, Pawn pawn = null)
        {
            for (int i = 0; i < reservations.Count; i++)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation != null && reservation.jobLoadID == jobLoadID
                    && reservation.key != null && reservation.key.SameAs(key))
                {
                    reservation.count += count;
                    if (reservation.pawn == null && pawn != null)
                    {
                        reservation.pawn = pawn;
                    }
                    return;
                }
            }
            reservations.Add(new SDS_Reservation
            {
                jobLoadID = jobLoadID,
                key = key,
                count = count,
                pawn = pawn
            });
        }

        // ---------- 生命周期 ----------

        // W26：旧档 → 新版必须能读；数据不丢优先于格式不变。
        /// <summary>
        /// 存读档（节点名 / LookMode 属存档锁定清单，改即 L3）。
        /// 入：无（读 Scribe.mode）。
        /// 出：无。
        /// 保存：只写「有身份且数量 > 0」的虚拟条目，以及有存活内容的真身（含无主待恢复真身）。
        /// 读档：键先 TryResolve（解析失败按未解析保留）、真身按 thingID 去重后交回容器，
        ///       入容器失败进入待恢复表；保真键不得落进虚拟表；只做结构性预留清理；归属判活由宿主在 FinalizeInit 调 SDS_Core.AuditReservations。
        /// 区别：Tick 是运行期巡检；本方法只处理存档边界，不重建账本实例。
        /// </summary>
        public void ExposeData()
        {
            string repair = null;
            if (stockEntries == null) { stockEntries = new List<SDS_StockEntry>(); repair = "stockEntries"; }
            if (forbiddenScratch == null) { forbiddenScratch = new List<SDS_StockKey>(); repair = Append(repair, "forbiddenKeys"); }
            if (reservations == null) { reservations = new List<SDS_Reservation>(); repair = Append(repair, "reservations"); }
            if (preservedScratch == null) { preservedScratch = new List<Thing>(); repair = Append(repair, "preservedThings"); }
            if (repair != null)
            {
                SDS_Diag.Warn("存档/旧档字段补齐", "字段=" + repair);
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 同名节点缺失时 Scribe 会保留字段旧内容；先清空，确保「读档 = 用存档内容替换」。
                // 这是同一账本实例被重复 Attach / 重载时的保护，正常新档实例本身就是空的。
                stockEntries.Clear();
                forbiddenScratch.Clear();
                reservations.Clear();
                preservedScratch.Clear();
            }

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                stockEntries.Clear();
                foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
                {
                    if (pair.Key == null || !pair.Key.HasIdentity || pair.Value <= 0L)
                    {
                        continue;
                    }
                    pair.Key.EnsureNames();
                    SDS_StockEntry entry = new SDS_StockEntry(pair.Key, pair.Value);
                    if (stockVitals.TryGetValue(pair.Key, out SDS_StockVitals vitals) && vitals != null)
                    {
                        entry.avgHitPoints = vitals.hitPoints;
                        entry.avgRotProgress = vitals.rotProgress;
                    }
                    stockEntries.Add(entry);
                }
                preservedScratch.Clear();
                CollectPreservedForSave(preservedScratch);
                forbiddenScratch.Clear();
                foreach (SDS_StockKey key in forbiddenKeys)
                {
                    forbiddenScratch.Add(key);
                }
            }

            // 缺节点时固定回落到 1：不能用 CurrentDataVersion 作默认值，否则未来升版
            // 会把「没有版本字段的旧档」误判成新版而跳过迁移。
            Scribe_Values.Look(ref dataVersion, "dataVersion", 1);
            Scribe_Collections.Look(ref stockEntries, "stockEntries", LookMode.Deep);
            Scribe_Collections.Look(ref forbiddenScratch, "forbiddenKeys", LookMode.Deep);
            Scribe_Collections.Look(ref reservations, "reservations", LookMode.Deep);
            Scribe_Collections.Look(ref preservedScratch, "preservedThings", LookMode.Deep);
            // sdsReclaim 与本账本的节点同处 SDS_GameComponent_Grid 节点下；读写统一由该组件负责，
            // 账本不读也不写，避免同一节点被两个 ExposeData 各读一次造成重复条目。

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            stock.Clear();
            stockVitals.Clear();
            pendingPreservedRestore.Clear();
            // 当前账本若被重复读档（同一实例再次 PostLoadInit），旧真身必须先摘出，
            // 否则新旧两批真身会同时计数、同时落档。
            ClearPreservedOwner();
            if (stockEntries != null)
            {
                int invalidVirtual = 0;
                int orphanRot = 0;
                for (int i = 0; i < stockEntries.Count; i++)
                {
                    SDS_StockEntry entry = stockEntries[i];
                    if (entry == null || entry.key == null || entry.count <= 0L || !entry.key.HasIdentity)
                    {
                        continue;
                    }
                    // 写入侧有通道守卫；读档同样不能让保真键落进虚拟表，否则
                    // GetCount / Snapshot 看得到、SDS_Availability 按保真口径看不到。
                    if (entry.key.preserved)
                    {
                        invalidVirtual++;
                        continue;
                    }
                    entry.key.TryResolve();
                    long oldCount = GetCount(entry.key);
                    stock[entry.key] = oldCount + entry.count;
                    if (!stockVitals.TryGetValue(entry.key, out SDS_StockVitals vitals) || vitals == null)
                    {
                        vitals = new SDS_StockVitals();
                        stockVitals[entry.key] = vitals;
                    }
                    // 历史孤儿值归一化（判据唯一在 SDS_StockData.RotProgressIllegal）：
                    // 旧版口径遗留的「无腐坏件却带进度 / 越哨兵」值复位 -1，下次存档自然落盘。
                    // 只归一化派生状态，数量口径不变；与既有「读档清理失效保真」同类。
                    float loadedRot = entry.avgRotProgress;
                    if (SDS_StockData.RotProgressIllegal(entry.key, loadedRot))
                    {
                        loadedRot = -1f;
                        orphanRot++;
                    }
                    SDS_StockData.MergeVitals(vitals, entry.key, oldCount, (int)Math.Min(entry.count, int.MaxValue),
                        entry.avgHitPoints, loadedRot);
                }
                if (invalidVirtual > 0)
                {
                    SDS_Diag.Fail("读档/虚拟表含保真键（已跳过）", "条数=" + invalidVirtual);
                }
                if (orphanRot > 0)
                {
                    SDS_Diag.Warn("读档/归一化腐坏进度孤儿值（已复位 -1）", "条数=" + orphanRot);
                }
                stockEntries.Clear();
            }

            if (preservedScratch != null)
            {
                int dropped = 0;
                int duplicates = 0;
                HashSet<int> seenIds = null;
                for (int i = 0; i < preservedScratch.Count; i++)
                {
                    Thing thing = preservedScratch[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }
                    // 存档里可能残留"没有内层小人的尸体"（旧档 / 第三方清除内层）：
                    // 原版尸体系筛选与 TransferAsOne 都会直接空引用，必须在这里摘掉。
                    if (!SDS_StockData.HasLiveContent(thing))
                    {
                        dropped++;
                        continue;
                    }
                    // 「同一真身同一时刻只能有一个所有者」：deep 序列化会把同一引用写成多份，
                    // 读档后按 thingID 去重，只保留一份；不 Split、不 Destroy、不改数量。
                    if (thing.thingIDNumber > 0)
                    {
                        if (seenIds == null)
                        {
                            seenIds = new HashSet<int>();
                        }
                        if (!seenIds.Add(thing.thingIDNumber))
                        {
                            duplicates++;
                            continue;
                        }
                    }
                    if (!SDS_Ownership.TryTransferTo(thing, preservedOwner, false))
                    {
                        // 入容器失败不能直接丢件：留在待恢复表里，由每次巡检重试所有权转移。
                        pendingPreservedRestore.Add(thing);
                        SDS_Diag.Fail("读档时保真实体入容器失败（已进入待恢复）",
                            "def=" + (thing.def != null ? thing.def.defName : "?")
                            + "#" + thing.thingIDNumber);
                    }
                    else
                    {
                        MarkPreservedIndexDirty();
                    }
                }
                if (dropped > 0)
                {
                    SDS_Diag.Warn("读档清理失效保真实物（无内层，无法交回原版判定）", "件数=" + dropped);
                }
                if (duplicates > 0)
                {
                    SDS_Diag.Warn("读档清理保真重复副本", "件数=" + duplicates);
                }
                preservedScratch.Clear();
            }

            forbiddenKeys.Clear();
            if (forbiddenScratch != null)
            {
                for (int i = 0; i < forbiddenScratch.Count; i++)
                {
                    SDS_StockKey key = forbiddenScratch[i];
                    if (key != null && key.HasIdentity)
                    {
                        key.TryResolve();
                        forbiddenKeys.Add(key);
                    }
                }
                forbiddenScratch.Clear();
            }

            // 账本只清结构性非法预留；归属存活判定与释放由 Vanilla 在 FinalizeInit / 周期巡检里
            // 调 SDS_Core.AuditReservations(ownerAlive) 完成（账本不读 Job / Pawn 的存活语义）。
            PruneMalformedReservations();

            stockVersion++;
            reservationVersion++;
            tickCounter = 0;
        }

        /// <summary>
        /// 清空保真容器（只摘出引用，不销毁实物；仅用于「同一实例再次读档」的替换语义）。
        /// 入：无。
        /// 出：无；索引标记为脏。
        /// 区别：Tick 只移除失效项；本方法是整表清空，正常新档实例上为空操作。
        /// </summary>
        private void ClearPreservedOwner()
        {
            for (int i = preservedOwner.Count - 1; i >= 0; i--)
            {
                Thing thing = preservedOwner[i];
                if (thing != null)
                {
                    preservedOwner.Remove(thing);
                }
            }
            MarkPreservedIndexDirty();
        }

        /// <summary>
        /// 运行期巡检（250 tick 一次）。
        /// 入：无。
        /// 出：无。
        /// 语义：移除已销毁 / 内容失效的保真真身；重试读档未回账的真身。
        /// 区别：不处理预留终老（由 Vanilla 调 SDS_Core.AuditReservations）；不驱动回收账本（归 Vanilla）。
        /// </summary>
        public void Tick()
        {
            tickCounter++;
            if (tickCounter < 250)
            {
                return;
            }
            tickCounter = 0;
            bool changed = false;
            int invalid = 0;
            for (int i = preservedOwner.Count - 1; i >= 0; i--)
            {
                Thing thing = preservedOwner[i];
                if (thing == null)
                {
                    // ThingOwner 只能按引用移除；空条目本身不构成数量，记一次即可，避免每次巡检重复计数。
                    SDS_Diag.ErrorOnce("保真容器含空条目（无法按引用移除）", null);
                    continue;
                }
                if (IsLivePreserved(thing))
                {
                    continue;
                }
                if (!thing.Destroyed)
                {
                    invalid++;
                }
                preservedOwner.Remove(thing);
                MarkPreservedIndexDirty();
                changed = true;
            }
            if (invalid > 0)
            {
                SDS_Diag.Warn("巡检移除内容失效的保真真身", "件数=" + invalid);
            }
            if (RetryPendingPreservedRestore())
            {
                changed = true;
            }
            if (changed)
            {
                stockVersion++;
            }
        }

        /// <summary>
        /// 读档失败的真身重试（每次巡检一次）。
        /// 入：无。
        /// 出：true = 至少一件成功回到容器（调用方需推进 StockVersion）。
        /// 区别：只把所有权交回账本，不落地、不销毁；失败保持待恢复状态，下次巡检继续。
        /// </summary>
        private bool RetryPendingPreservedRestore()
        {
            if (pendingPreservedRestore.Count == 0)
            {
                return false;
            }
            bool any = false;
            int duplicates = 0;
            for (int i = pendingPreservedRestore.Count - 1; i >= 0; i--)
            {
                Thing thing = pendingPreservedRestore[i];
                if (thing == null || thing.Destroyed)
                {
                    pendingPreservedRestore.RemoveAt(i);
                    continue;
                }
                if (preservedOwner.Contains(thing))
                {
                    pendingPreservedRestore.RemoveAt(i);
                    continue;
                }
                // 同 thingID 的真身已在账本：与读档去重同语义，丢弃待恢复副本，绝不重复入列。
                if (IsPreservedIdentityOccupied(thing))
                {
                    pendingPreservedRestore.RemoveAt(i);
                    duplicates++;
                    continue;
                }
                if (SDS_Ownership.TryTransferTo(thing, preservedOwner, false))
                {
                    pendingPreservedRestore.RemoveAt(i);
                    MarkPreservedIndexDirty();
                    any = true;
                }
            }
            if (duplicates > 0)
            {
                SDS_Diag.Warn("读档待恢复/同 thingID 重复副本（已丢弃）", "件数=" + duplicates);
            }
            return any;
        }

        /// <summary>
        /// 自检：保真真身「一个所有者 + 一个 thingID + 内容完整」不变量。
        /// 入：lines —— 可选报告输出；null 只统计不写文本。
        /// 出：违规件数；只读，不修改任何状态。
        /// 区别：AppendPreservedThings 输出候选实物；本方法只做一致性检查。
        /// </summary>
        internal int AppendPreservedInvariantReport(List<string> lines)
        {
            int bad = 0;
            HashSet<int> ids = null;
            for (int i = 0; i < preservedOwner.Count; i++)
            {
                Thing thing = preservedOwner[i];
                if (thing == null)
                {
                    continue;
                }
                if (thing.Destroyed)
                {
                    bad++;
                    if (lines != null && bad <= 10)
                    {
                        lines.Add("[失败] [Core] 保真真身已销毁（未移除）：def="
                            + (thing.def != null ? thing.def.defName : "?")
                            + "#" + thing.thingIDNumber);
                    }
                    continue;
                }
                if (!SDS_StockData.HasLiveContent(thing))
                {
                    bad++;
                    if (lines != null && bad <= 5)
                    {
                        lines.Add("[失败] [Core] 保真真身内容失效（未计入数量）：def="
                            + (thing.def != null ? thing.def.defName : "?")
                            + "#" + thing.thingIDNumber);
                    }
                    continue;
                }
                if (thing.holdingOwner != preservedOwner)
                {
                    bad++;
                    if (lines != null && bad <= 5)
                    {
                        lines.Add("[失败] [Core] 保真真身所有者不符：def="
                            + (thing.def != null ? thing.def.defName : "?")
                            + "#" + thing.thingIDNumber);
                    }
                }
                if (thing.thingIDNumber > 0)
                {
                    if (ids == null)
                    {
                        ids = new HashSet<int>();
                    }
                    if (!ids.Add(thing.thingIDNumber))
                    {
                        bad++;
                        if (lines != null && bad <= 10)
                        {
                            lines.Add("[失败] [Core] 保真真身重复 ID：def="
                                + (thing.def != null ? thing.def.defName : "?")
                                + "#" + thing.thingIDNumber);
                        }
                    }
                }
            }
            for (int i = 0; i < pendingPreservedRestore.Count; i++)
            {
                Thing pending = pendingPreservedRestore[i];
                if (pending != null && !pending.Destroyed)
                {
                    bad++;
                    if (lines != null && bad <= 10)
                    {
                        lines.Add("[失败] [Core] 读档保真未回账（待巡检重试）：def="
                            + (pending.def != null ? pending.def.defName : "?")
                            + "#" + pending.thingIDNumber);
                    }
                }
            }
            if (bad == 0 && lines != null)
            {
                lines.Add("[统计] [Core] 保真所有者 / thingID 唯一性检查通过。");
            }
            return bad;
        }

        /// <summary>
        /// 自检：账本完整不变量（不依赖宿主事实）。
        /// 入：lines —— 可选报告输出；null 只统计。
        /// 出：违规项数；会先重建派生索引，再与独立扫描结果比对。
        /// 区别：AppendPreservedInvariantReport 只查保真真身；本方法覆盖 stock / vitals /
        ///       三个派生索引 / 预留 / 待恢复 / 存档版本。
        /// </summary>
        internal int AppendLedgerInvariantReport(List<string> lines)
        {
            int bad = 0;
            Action<string> fail = delegate (string message)
            {
                bad++;
                if (lines != null && bad <= 40)
                {
                    lines.Add("[失败] [Core] " + message);
                }
            };

            if (dataVersion <= 0 || dataVersion > CurrentDataVersion)
            {
                fail("存档版本非法 / 过新：dataVersion=" + dataVersion
                    + " current=" + CurrentDataVersion);
            }

            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                SDS_StockKey key = pair.Key;
                if (key == null)
                {
                    fail("stock 含空键");
                    continue;
                }
                if (!key.HasIdentity)
                {
                    fail("stock 键无身份：preserved=" + key.preserved);
                }
                if (key.preserved)
                {
                    fail("stock 混入保真键：def=" + key.EffectiveDefName);
                }
                if (pair.Value <= 0L)
                {
                    fail("stock 非正数量：def=" + key.EffectiveDefName
                        + " count=" + pair.Value);
                }
            }
            foreach (KeyValuePair<SDS_StockKey, SDS_StockVitals> pair in stockVitals)
            {
                if (pair.Key == null || !stock.ContainsKey(pair.Key))
                {
                    fail("vitals 对应不存在的 stock 键：def="
                        + (pair.Key != null ? pair.Key.EffectiveDefName : "?"));
                    continue;
                }
                SDS_StockVitals vitals = pair.Value;
                if (vitals == null)
                {
                    fail("vitals 为空：def=" + pair.Key.EffectiveDefName);
                    continue;
                }
                if (float.IsNaN(vitals.hitPoints) || float.IsInfinity(vitals.hitPoints)
                    || vitals.hitPoints < -1f)
                {
                    fail("vitals.hitPoints 非法：def=" + pair.Key.EffectiveDefName
                        + " value=" + vitals.hitPoints);
                }
                // 口径唯一在 SDS_StockData.HitPointsAboveDefBound：HP 上限 = stat(含材质)×1.8
                // （对齐 ApplyVitals 实例钳制），rotProgress 为原始 tick、无上限（推论二），仅做结构 / 哨兵校验。
                if (SDS_StockData.HitPointsAboveDefBound(pair.Key, vitals.hitPoints))
                {
                    fail("vitals.hitPoints 超过 def 上限：def=" + pair.Key.EffectiveDefName
                        + " value=" + vitals.hitPoints);
                }
                if (SDS_StockData.RotProgressIllegal(pair.Key, vitals.rotProgress))
                {
                    fail("vitals.rotProgress 非法：def=" + pair.Key.EffectiveDefName
                        + " value=" + vitals.rotProgress);
                }
            }

            EnsureKeysByDef();
            if (keysByDefVersion != stockVersion)
            {
                fail("keysByDefVersion 未跟上 stockVersion");
            }
            Dictionary<ThingDef, HashSet<SDS_StockKey>> expectedByDef =
                new Dictionary<ThingDef, HashSet<SDS_StockKey>>();
            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                if (pair.Key == null || pair.Key.thingDef == null || pair.Value <= 0L)
                {
                    continue;
                }
                if (!expectedByDef.TryGetValue(pair.Key.thingDef, out HashSet<SDS_StockKey> keys))
                {
                    keys = new HashSet<SDS_StockKey>();
                    expectedByDef[pair.Key.thingDef] = keys;
                }
                keys.Add(pair.Key);
            }
            if (keysByDef.Count != expectedByDef.Count)
            {
                fail("keysByDef 条目数不符：expected=" + expectedByDef.Count
                    + " actual=" + keysByDef.Count);
            }
            foreach (KeyValuePair<ThingDef, HashSet<SDS_StockKey>> pair in expectedByDef)
            {
                if (!keysByDef.TryGetValue(pair.Key, out List<SDS_StockKey> actual) || actual == null)
                {
                    fail("keysByDef 缺少 def：" + pair.Key.defName);
                }
                else if (!new HashSet<SDS_StockKey>(actual).SetEquals(pair.Value))
                {
                    fail("keysByDef 键集合不符：def=" + pair.Key.defName);
                }
            }
            foreach (KeyValuePair<ThingDef, List<SDS_StockKey>> pair in keysByDef)
            {
                if (!expectedByDef.ContainsKey(pair.Key))
                {
                    fail("keysByDef 含多余 def：" + (pair.Key != null ? pair.Key.defName : "?"));
                }
            }

            EnsurePreservedIndex();
            if (preservedIndexDirty)
            {
                fail("保真索引仍为 dirty");
            }
            Dictionary<SDS_StockKey, HashSet<Thing>> expectedPreserved =
                new Dictionary<SDS_StockKey, HashSet<Thing>>();
            Dictionary<ThingDef, HashSet<SDS_StockKey>> expectedInner =
                new Dictionary<ThingDef, HashSet<SDS_StockKey>>();
            HashSet<Thing> expectedMembers = new HashSet<Thing>();
            HashSet<int> expectedIds = new HashSet<int>();
            for (int i = 0; i < preservedOwner.Count; i++)
            {
                Thing thing = preservedOwner[i];
                if (!IsLivePreserved(thing))
                {
                    continue;
                }
                if (!expectedMembers.Add(thing))
                {
                    continue;
                }
                if (thing.thingIDNumber > 0 && !expectedIds.Add(thing.thingIDNumber))
                {
                    continue;
                }
                SDS_StockKey key = SDS_StockData.FromPreservedThing(thing);
                if (key == null || key.thingDef == null)
                {
                    fail("保真真身无法构键：def="
                        + (thing.def != null ? thing.def.defName : "?")
                        + "#" + thing.thingIDNumber);
                    continue;
                }
                if (!expectedPreserved.TryGetValue(key, out HashSet<Thing> things))
                {
                    things = new HashSet<Thing>();
                    expectedPreserved[key] = things;
                    if (!expectedInner.TryGetValue(key.thingDef, out HashSet<SDS_StockKey> keys))
                    {
                        keys = new HashSet<SDS_StockKey>();
                        expectedInner[key.thingDef] = keys;
                    }
                    keys.Add(key);
                }
                things.Add(thing);
            }
            if (preservedByKey.Count != expectedPreserved.Count)
            {
                fail("preservedByKey 条目数不符：expected=" + expectedPreserved.Count
                    + " actual=" + preservedByKey.Count);
            }
            foreach (KeyValuePair<SDS_StockKey, HashSet<Thing>> pair in expectedPreserved)
            {
                if (!preservedByKey.TryGetValue(pair.Key, out List<Thing> actual) || actual == null)
                {
                    fail("preservedByKey 缺键：def=" + pair.Key.EffectiveDefName);
                }
                else if (!new HashSet<Thing>(actual).SetEquals(pair.Value))
                {
                    fail("preservedByKey 真身集合不符：def=" + pair.Key.EffectiveDefName);
                }
            }
            foreach (KeyValuePair<SDS_StockKey, List<Thing>> pair in preservedByKey)
            {
                if (!expectedPreserved.ContainsKey(pair.Key))
                {
                    fail("preservedByKey 含多余键：def="
                        + (pair.Key != null ? pair.Key.EffectiveDefName : "?"));
                }
            }
            if (!preservedMembers.SetEquals(expectedMembers))
            {
                fail("preservedMembers 与 owner 不一致");
            }
            if (!preservedMemberIds.SetEquals(expectedIds))
            {
                fail("preservedMemberIds 与 owner 不一致");
            }
            if (preservedKeysByInnerDef.Count != expectedInner.Count)
            {
                fail("preservedKeysByInnerDef 条目数不符：expected=" + expectedInner.Count
                    + " actual=" + preservedKeysByInnerDef.Count);
            }
            foreach (KeyValuePair<ThingDef, HashSet<SDS_StockKey>> pair in expectedInner)
            {
                if (!preservedKeysByInnerDef.TryGetValue(pair.Key,
                        out List<SDS_StockKey> actual) || actual == null)
                {
                    fail("preservedKeysByInnerDef 缺少 def：" + pair.Key.defName);
                }
                else if (!new HashSet<SDS_StockKey>(actual).SetEquals(pair.Value))
                {
                    fail("preservedKeysByInnerDef 键集合不符：def=" + pair.Key.defName);
                }
            }

            EnsureCategoryKeys();
            if (categoryVersion != stockVersion)
            {
                fail("categoryVersion 未跟上 stockVersion");
            }
            HashSet<SDS_StockKey> expectedCategories = new HashSet<SDS_StockKey>();
            foreach (KeyValuePair<SDS_StockKey, long> pair in stock)
            {
                if (pair.Key != null && pair.Value > 0L)
                {
                    SDS_StockKey category = SDS_StockData.CategoryKeyOf(pair.Key);
                    if (category != null)
                    {
                        expectedCategories.Add(category);
                    }
                }
            }
            AppendLivePreservedCategories(expectedCategories);
            if (!categoryKeys.SetEquals(expectedCategories))
            {
                fail("categoryKeys 与 stock / 保真不一致：expected="
                    + expectedCategories.Count + " actual=" + categoryKeys.Count);
            }

            HashSet<(int, SDS_StockKey)> seenReservations = new HashSet<(int, SDS_StockKey)>();
            for (int i = 0; i < reservations.Count; i++)
            {
                SDS_Reservation reservation = reservations[i];
                if (reservation == null)
                {
                    fail("reservations 含空条目");
                    continue;
                }
                if (reservation.key == null)
                {
                    fail("预留缺 key：job=" + reservation.jobLoadID);
                    continue;
                }
                if (reservation.count <= 0)
                {
                    fail("预留非正数量：def=" + reservation.key.EffectiveDefName
                        + " count=" + reservation.count);
                }
                if (reservation.jobLoadID == 0)
                {
                    fail("预留 jobLoadID 为 0：def=" + reservation.key.EffectiveDefName);
                }
                if (!reservation.key.HasIdentity)
                {
                    fail("预留键无身份：job=" + reservation.jobLoadID);
                }
                if (!seenReservations.Add((reservation.jobLoadID, reservation.key)))
                {
                    fail("预留同归属同 key 重复：job=" + reservation.jobLoadID
                        + " def=" + reservation.key.EffectiveDefName);
                }
            }

            HashSet<int> pendingIds = new HashSet<int>();
            for (int i = 0; i < pendingPreservedRestore.Count; i++)
            {
                Thing pending = pendingPreservedRestore[i];
                if (pending == null)
                {
                    fail("pendingPreservedRestore 含空条目");
                    continue;
                }
                if (preservedOwner.Contains(pending))
                {
                    fail("待恢复真身同时在 owner 内：def="
                        + (pending.def != null ? pending.def.defName : "?")
                        + "#" + pending.thingIDNumber);
                }
                if (pending.thingIDNumber > 0 && !pendingIds.Add(pending.thingIDNumber))
                {
                    fail("待恢复真身重复 ID：" + pending.thingIDNumber);
                }
            }

            bad += AppendPreservedInvariantReport(lines);
            return bad;
        }

        /// <summary>
        /// 拼接「字段补齐」错误串。
        /// 入：current —— 已有串（可空）；name —— 本次补齐的字段名。
        /// 出：逗号分隔的串；仅用于存档修复日志。
        /// 区别：纯文本工具，不参与任何状态判定。
        /// </summary>
        private static string Append(string current, string name)
        {
            return current == null ? name : current + "," + name;
        }
    }

    /// <summary>
    /// 唯一所有权转移实现：实物在「地图 / 容器 / 账本」之间移动只经由这里。
    /// 禁止调用方直接改 holdingOwner —— 旧容器仍会列出实物，形成双所有者。
    /// </summary>
    internal static class SDS_Ownership
    {
        /// <summary>
        /// 原子转移：把实物交给目标容器（spawned 先入容器再离图；held 走原版转移）。
        /// 入：thing —— 待转移实物；target —— 目标容器；canMergeWithExistingStacks —— 是否允许合堆。
        /// 出：是否成功；失败时任何容器内容都不变（零副作用）。
        /// 区别：TryDetachForDestroy 只摘出、不指定目标；TryRestoreTo 是失败回挂；
        ///       正常收进账本用本方法，销毁前摘出用 TryDetachForDestroy。
        /// </summary>
        internal static bool TryTransferTo(Thing thing, ThingOwner target, bool canMergeWithExistingStacks)
        {
            if (thing == null || thing.Destroyed || target == null)
            {
                return false;
            }
            if (!target.CanAcceptAnyOf(thing, canMergeWithExistingStacks))
            {
                return false;
            }
            if (thing.Spawned)
            {
                // 先入容器（失败零副作用），再离图；顺序反过来会在失败时把实物留在图外无法恢复。
                if (!target.TryAdd(thing, canMergeWithExistingStacks))
                {
                    return false;
                }
                thing.DeSpawn();
                return true;
            }
            if (thing.holdingOwner != null)
            {
                return thing.holdingOwner.TryTransferToContainer(thing, target, canMergeWithExistingStacks);
            }
            return target.TryAdd(thing, canMergeWithExistingStacks);
        }

        /// <summary>
        /// 销毁前把实物从当前容器摘出，并回传原容器用于失败回挂。
        /// 入：thing —— 待销毁实物。
        /// 出：owner —— 原容器；spawned 实物为 null（销毁自带离图）；Remove 失败返回 false。
        /// 区别：TryTransferTo 会把实物交给指定目标；本方法只摘出、不落新容器；
        ///       与 TryRestoreTo 成对使用，构成「摘出 → 入账 → 销毁」的失败回退链。
        /// </summary>
        internal static bool TryDetachForDestroy(Thing thing, out ThingOwner owner)
        {
            owner = null;
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            if (thing.Spawned)
            {
                return true;
            }
            owner = thing.holdingOwner;
            return owner == null || owner.Remove(thing);
        }

        /// <summary>
        /// 入账失败 / 销毁未生效时的唯一回挂实现。
        /// 入：owner —— TryDetachForDestroy 回传的原容器（可空）；thing —— 待回挂实物。
        /// 出：是否已就位（已在容器 / 已销毁 / 无原容器都算成功）；TryAdd 失败记 Fail 并返回 false。
        /// 区别：TryTransferTo 是正常转移；本方法只负责失败回退，不改变实物数量。
        /// </summary>
        internal static bool TryRestoreTo(ThingOwner owner, Thing thing)
        {
            if (owner == null || thing == null || thing.Destroyed)
            {
                return true;
            }
            if (owner.Contains(thing))
            {
                return true;
            }
            if (owner.TryAdd(thing, false))
            {
                return true;
            }
            SDS_Diag.Fail("所有权回挂失败（实物已从容器摘出，未能放回）",
                SDS_Diag.Ctx(thing.MapHeld, null, thing));
            return false;
        }
    }
}
