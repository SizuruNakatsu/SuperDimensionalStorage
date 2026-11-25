using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 规则侧自检：W35 逐件跟踪保真 + 保真可用量一致性。
    /// 账本自身口径（虚拟可用量 / 真身失效 / 完整不变量）仍在 SDS.Core 的 SDS_CoreDiagnostics。
    /// </summary>
    internal sealed class SDS_RulesDiagnostics : ISDS_DiagnosticProvider
    {
        /// <summary>诊断提供者名称（报告分组标题）。入：无。出：固定 "Rules"。纯读。
        /// 区别：与其他诊断提供者各自独立，名称只用于报告分段。</summary>
        public string Name
        {
            get { return "Rules"; }
        }

        /// <summary>
        /// 自检报告：W35 规则自检 + 保真可用量一致性。
        /// 入：lines = 输出目标，null 直接返回（不做任何运算）。
        /// 出：无；失败项以 "[失败] [Rules]" 前缀写入，末尾追加统计行。账本缺失时只写一行说明并返回。
        /// 区别：与 SDS_CoreDiagnostics 的区别是只校验判定层口径，不重复账本不变量检查。
        /// </summary>
        public void AppendReport(List<string> lines)
        {
            if (lines == null)
            {
                return;
            }
            SDS_StockRules.SelfCheck(lines);
            int checkedCount = 0;
            int failures = 0;
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null)
            {
                lines.Add("[Rules] 账本未附加。");
                return;
            }

            // 保真可用量：真身件数 − 预留，夹到 ≥0；G 关闭 / 品类禁用时不参与比对。
            if (SDS_RulesImpl.Available)
            {
                List<SDS_StockEntry> preservedSnapshot = ledger.PreservedSnapshot();
                for (int i = 0; i < preservedSnapshot.Count; i++)
                {
                    SDS_StockKey key = preservedSnapshot[i].key;
                    if (key == null || !key.preserved || ledger.IsForbidden(key))
                    {
                        continue;
                    }
                    long total = ledger.GetPreservedCount(key);
                    long reserved = ledger.GetReserved(key);
                    long expected = total - reserved;
                    if (expected < 0L)
                    {
                        expected = 0L;
                    }
                    long actual = SDS_AvailabilityImpl.GetAvailableCount(key);
                    checkedCount++;
                    if (actual != expected)
                    {
                        failures++;
                        lines.Add("[失败] [Rules] 保真可用量不符：def=" + key.EffectiveDefName
                            + " total=" + total + " reserved=" + reserved
                            + " expected=" + expected + " actual=" + actual);
                    }
                }
            }

            lines.Add("[统计] Rules：检查 " + checkedCount + " 项，失败 " + failures + " 项。");
        }
    }

    /// <summary>
    /// 规则 / 流程实现（P3–P4）：判定层（通道 / 容量 / 可用量 / 结构 / 芯片）与流程层（存 / 取 / 预留 / 回充 / 在途标记）。
    /// 账本读写经 Core 的已登记友元授权；Core 不反向依赖本程序集。
    /// </summary>
    internal static class SDS_RulesImpl
    {
        private static readonly SDS_StructureRegistry structures = new SDS_StructureRegistry();
        private static bool registered;

        // ---------- 判定层：结构 / 容量 / 端点版本 ----------

        /// <summary>全局可用性（转发结构注册表）。入：无。出：true = 存在可用核心。纯读。
        /// 区别：与 MapHasOperationalCore 的区别是跨图取或，不看单图。</summary>
        public static bool Available
        {
            get { return structures.Available; }
        }

        /// <summary>品类容量（转发结构注册表）。入：无。出：容量上限。纯读。
        /// 区别：与 SDS_Core.UsedCapacity 配对：本属性是上限，后者是已用数，二者不互相缓存。</summary>
        public static long TypeCapacity
        {
            get { return structures.TypeCapacity; }
        }

        /// <summary>端点契约版本。入：无。出：版本号。纯读。
        /// 区别：与账本的 Stock / Reservation / Filter 三个版本相互独立，只随端点契约推进。</summary>
        public static int EndpointVersion
        {
            get { return SDS_EndpointContractCache.Version; }
        }

        /// <summary>
        /// 实例 → 通道 / 键的唯一规则；Api 的 ReadStockFor 只转发这里。
        /// 已在保真账本中的实例按保真口径，其余按实例分流规则（设置 / 实例状态变化后仍读对通道）。
        /// </summary>
        /// <remarks>入：thing = 实例；out key = 结果键。出：true = key 非空。
        /// 区别：与 SDS_StockRules.FromAnyThing 的区别是多一步「已在保真账本 ⇒ 强制保真口径」。</remarks>
        public static bool TryGetStockKey(Thing thing, out SDS_StockKey key)
        {
            if (thing == null)
            {
                key = null;
                return false;
            }
            SDS_Ledger current = SDS_CoreImpl.Ledger;
            key = current != null && current.IsPreserved(thing)
                ? SDS_StockData.FromPreservedThing(thing)
                : SDS_StockRules.FromAnyThing(thing);
            return key != null;
        }

        /// <summary>def 级通道判定（只转发 SDS_StockRules 的唯一规则）：第三方按 def 选通道时使用。</summary>
        /// <remarks>入：def = 目标 def。出：true = 必须保真。区别：纯转发，不缓存、不看实例状态。</remarks>
        public static bool RequiresPreservedStorage(ThingDef def)
        {
            return SDS_StockRules.RequiresPreservedStorage(def);
        }

        /// <summary>旧口径不可重建黑名单（尸体 / 缩装 / 未完成 / 基因载体）；供 Vanilla 判定复用。</summary>
        /// <remarks>入：def = 目标 def。出：true = 命中黑名单。区别：纯转发；只答黑名单，不含逐件跟踪等判据。</remarks>
        public static bool IsLegacyUnrebuildableDef(ThingDef def)
        {
            return SDS_StockRules.IsLegacyUnrebuildableDef(def);
        }

        /// <summary>品类归属键：按设置归零品质 / 材质维度（唯一实现，展示与分组共用）。</summary>
        /// <remarks>入：key = 精确键。出：归属键。区别：纯转发；只用于分组 / 容量，不用于扣账。</remarks>
        public static SDS_StockKey CategoryKeyOf(SDS_StockKey key)
        {
            return SDS_StockData.CategoryKeyOf(key);
        }

        /// <summary>
        /// 按 def 找一条可取出键（生产 / 取用口径）：精确键优先 → 可用量最大 → 保真同口径。
        /// 禁用与全局不可用归零；供 Job / 装载 / 兼容层共用；纯读，不预留。
        /// </summary>
        /// <remarks>入：def = 目标 def；out key / available = 选中键与可用件数。出：true = 有可用量。
        /// 区别：与 TryFindTradableKey 共用 TryFindKeyForDef，唯一差别是受品类禁用约束。</remarks>
        public static bool TryFindWithdrawableKey(ThingDef def, out SDS_StockKey key, out int available)
        {
            return TryFindKeyForDef(def, false, out key, out available);
        }

        /// <summary>
        /// 按 def 找一条可交易键（交易 / 捐献 / 发射口径，R15）：选择顺序与
        /// <see cref="TryFindWithdrawableKey"/> 共用同一实现，但**不因品类禁用归零**；
        /// 全局不可用时仍返回 false。
        /// </summary>
        /// <remarks>入：def = 目标 def；out key / available = 选中键与可交易件数。出：true = 有可交易量。
        /// 区别：与 TryFindWithdrawableKey 的唯一差别是忽略品类禁用。</remarks>
        public static bool TryFindTradableKey(ThingDef def, out SDS_StockKey key, out int available)
        {
            return TryFindKeyForDef(def, true, out key, out available);
        }

        /// <summary>
        /// 按 def 选键的共用入口：先 FindWithdrawableKey，再按口径取可用量并夹到 int 上限。
        /// 入：def = 目标 def；tradable = 是否交易口径（影响禁用过滤与可用量口径）；out key / available。
        /// 出：true = 选中且可用量 &gt; 0；false = key = null、available = 0（def 空 / 全局不可用 / 无键 / 量为 0）。
        /// 区别：与 FindWithdrawableKey 的区别是它还做可用量换算与门槛判定；纯读。
        /// </summary>
        private static bool TryFindKeyForDef(ThingDef def, bool tradable,
            out SDS_StockKey key, out int available)
        {
            key = null;
            available = 0;
            if (def == null || !Available)
            {
                return false;
            }
            key = FindWithdrawableKey(def, tradable);
            if (key == null)
            {
                return false;
            }
            long count = tradable
                ? SDS_AvailabilityImpl.GetTradableCount(key)
                : SDS_AvailabilityImpl.GetAvailableCount(key);
            available = count > int.MaxValue ? int.MaxValue : (int)count;
            return available > 0;
        }

        /// <summary>def 级总可用量：全部虚拟键 + 全部保真键（同 def、非禁用）的可用量之和。</summary>
        /// <remarks>入：def = 目标 def。出：合计可用件数；无账本 / def 空 / 全局不可用返回 0。
        /// 区别：与 TryFindKeyForDef 的「只取一条键」不同，本方法求和，且不做 int 夹取。</remarks>
        public static long GetAvailableCountForDef(ThingDef def)
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || def == null || !Available)
            {
                return 0L;
            }
            long total = 0L;
            List<SDS_StockKey> keys = new List<SDS_StockKey>();
            ledger.CopyKeysForDef(def, keys);
            for (int i = 0; i < keys.Count; i++)
            {
                SDS_StockKey key = keys[i];
                if (key == null || key.preserved || ledger.IsForbidden(key))
                {
                    continue;
                }
                total += SDS_AvailabilityImpl.GetAvailableCount(key);
            }
            ledger.CopyPreservedKeysForDef(def, keys);
            for (int i = 0; i < keys.Count; i++)
            {
                SDS_StockKey key = keys[i];
                if (key == null || !key.preserved || ledger.IsForbidden(key))
                {
                    continue;
                }
                total += SDS_AvailabilityImpl.GetAvailableCount(key);
            }
            return total;
        }

        /// <summary>
        /// 注入外部保真分类输入（组合根调用一次；传 null 清除）。
        /// 入：provider = 「该实例是否额外要求保真」；出：无。
        /// 区别：Rules 只持有委托，不引用任何模块；只允许把物品加严为保真。
        /// </summary>
        public static void SetExternalPreserveProvider(Func<Thing, bool> provider)
        {
            SDS_StockRules.ExternalPreserveProvider = provider;
        }

        // ---------- 判定层：结构事实 ----------

        /// <summary>登记结构事实并推进端点版本。</summary>
        /// <remarks>入：fact = 结构事实（位置 / 类型 / 可用性等，in 传引用）。出：无；同实例重复登记以最新为准。
        /// 区别：与 ReconcileStructures 的「批量对账」不同，本方法是增量单条登记。</remarks>
        public static void RegisterStructure(in SDS_StructureFact fact)
        {
            structures.Register(fact);
            NotifyEndpointChanged();
        }

        /// <summary>移除结构事实并推进端点版本。</summary>
        /// <remarks>入：thing = 被移除的结构，可空。出：无；不存在时静默忽略。
        /// 区别：与 ReconcileStructures 的批量剔除互补。</remarks>
        public static void UnregisterStructure(Thing thing)
        {
            structures.Unregister(thing);
            NotifyEndpointChanged();
        }

        /// <summary>
        /// 以活体事实集为准对账结构注册表：补齐漏登记、剔除陈旧项、修正未走回调的变化；
        /// 有变化才推进端点版本。返回是否发生变化。
        /// </summary>
        /// <remarks>入：live = 当前实际存活的结构事实列表。出：true = 注册表被改动。
        /// 区别：与 RegisterStructure / UnregisterStructure 的单条增量不同，本方法做整体一致化，供巡检调用。</remarks>
        public static bool ReconcileStructures(List<SDS_StructureFact> live)
        {
            if (!structures.Reconcile(live))
            {
                return false;
            }
            NotifyEndpointChanged();
            return true;
        }

        /// <summary>通知端点集合 / 优先级来源变化：推进端点契约版本，端点缓存失效。</summary>
        /// <remarks>入：无。出：无；只推版本，不重算端点。
        /// 区别：与 NotifyDepositFilterChanged 等账本版本通知相互独立。</remarks>
        public static void NotifyEndpointChanged()
        {
            SDS_EndpointContractCache.NotifyEndpointsChanged();
        }

        /// <summary>该地图是否有可用核心（转发结构注册表）。入：map = 目标地图，可空。出：true = 有可用核心。纯读。
        /// 区别：与 Available 的区别是只看单图。</summary>
        internal static bool MapHasOperationalCore(Map map)
        {
            return structures.MapHasOperationalCore(map);
        }

        /// <summary>
        /// 按 def 选一条键的唯一实现：虚拟精确档 → 可用量最大档 → 保真同口径。
        /// 生产 / 取用（tradable=false）跳过禁用品类；交易 / 发射（tradable=true）不跳过（R15）。
        /// </summary>
        /// <remarks>入：def = 目标 def；tradable = 是否交易口径。出：选中键或 null。
        /// 选择顺序：虚拟「无材质无品质」精确档 → 虚拟可用量最大 → 保真精确档 → 保真可用量最大。
        /// 区别：它只选键、不换算可用量；也不预留（预留由 TryReserveForJob 负责）。
        /// </remarks>
        private static SDS_StockKey FindWithdrawableKey(ThingDef def, bool tradable)
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || def == null)
            {
                return null;
            }
            List<SDS_StockKey> keys = new List<SDS_StockKey>();
            ledger.CopyKeysForDef(def, keys);
            SDS_StockKey exact = null;
            SDS_StockKey best = null;
            long bestAvailable = 0L;
            for (int i = 0; i < keys.Count; i++)
            {
                SDS_StockKey key = keys[i];
                if (key == null || key.preserved || (!tradable && ledger.IsForbidden(key)))
                {
                    continue;
                }
                long available = tradable
                    ? SDS_AvailabilityImpl.GetTradableCount(key)
                    : SDS_AvailabilityImpl.GetAvailableCount(key);
                if (available <= 0L)
                {
                    continue;
                }
                if (exact == null && key.stuff == null && key.qualityInt < 0)
                {
                    exact = key;
                }
                if (available > bestAvailable)
                {
                    bestAvailable = available;
                    best = key;
                }
            }
            if (exact != null)
            {
                return exact;
            }
            if (best != null)
            {
                return best;
            }

            ledger.CopyPreservedKeysForDef(def, keys);
            SDS_StockKey preservedExact = null;
            SDS_StockKey preservedBest = null;
            long preservedBestCount = 0L;
            for (int i = 0; i < keys.Count; i++)
            {
                SDS_StockKey key = keys[i];
                if (key == null || !key.preserved || (!tradable && ledger.IsForbidden(key)))
                {
                    continue;
                }
                long available = tradable
                    ? SDS_AvailabilityImpl.GetTradableCount(key)
                    : SDS_AvailabilityImpl.GetAvailableCount(key);
                if (available <= 0L)
                {
                    continue;
                }
                if (preservedExact == null && key.stuff == null && key.qualityInt < 0)
                {
                    preservedExact = key;
                }
                if (available > preservedBestCount)
                {
                    preservedBestCount = available;
                    preservedBest = key;
                }
            }
            return preservedExact ?? preservedBest;
        }

        // ---------- 存入 ----------

        /// <summary>
        /// 存入可行性：先过品类 / 通道 / 宿主筛选，再排除「账本已持有同一实物」，最后过容量门（只拦新品类）。
        /// 入：thing = 待判定实物。出：true = 当前可收。纯读。
        /// 区别：与 SDS_StockRules.CanDeposit 的区别是多身份门与容量门；与 TryDeposit 的区别是不落账。
        /// </summary>
        public static bool CanDeposit(Thing thing)
        {
            return CanDeposit(thing, out _);
        }

        /// <summary>
        /// 存入可行性（回填通道）：供 TryDeposit 复用一次实例级通道判定。
        /// 入：thing = 待判定实物。出：true = 当前可收；preserved = 本次通道判定结果。
        /// 区别：与单参 CanDeposit 的唯一差别是多回填 preserved，身份门与容量门完全一致。
        /// </summary>
        private static bool CanDeposit(Thing thing, out bool preserved)
        {
            preserved = false;
            // 存入不受全局可用性约束：G 关闭只影响读侧与取货事务，已存数据必须保留
            // （否则临时断电 / 拆核心会把玩家刚存入的货判成不可收）。
            if (!SDS_StockRules.CanDeposit(thing, out preserved))
            {
                return false;
            }
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            // 同一实物同一时刻只能有一个所有者：账本已持有（引用或同 thingID）时，
            // 再次存入不是新入库，直接判不可收（最终守卫仍在账本写入路径）。
            if (ledger != null && ledger.IsPreservedIdentityOccupied(thing))
            {
                return false;
            }
            SDS_StockKey key = SDS_StockRules.FromAnyThing(thing, preserved);
            return key != null && CanAcceptKey(key);
        }

        /// <summary>
        /// 向端口入账：保真通道整件转入账本；虚拟通道走唯一「实转虚」入口（摘出 → 入账 → 销毁，失败整笔回退）。
        /// 入：thing = 源实物；port = 目标端口（须 Operational）。出：true = 已入账，accepted = stackCount；false = accepted = 0。
        /// 区别：与 CanDeposit 的区别是真正落账；与 SDS_Ledger.TryConvertThingToVirtual 的区别是含前置门槛。
        /// </summary>
        public static bool TryDeposit(Thing thing, ISDS_Port port, out int accepted)
        {
            accepted = 0;
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (port == null || !port.Operational || ledger == null)
            {
                return false;
            }
            bool preserved;
            // thing 的空 / 销毁 / 零堆量由 CanDeposit 统一判定，此处不重复。
            if (!CanDeposit(thing, out preserved))
            {
                return false;
            }
            if (preserved)
            {
                int units = thing.stackCount;
                if (!ledger.TryDepositPreserved(thing))
                {
                    return false;
                }
                accepted = units;
                return true;
            }
            // 实物 → 虚拟账面只有这一条路径：摘出 → 入账 → 销毁，销毁未生效则整笔回退。
            return ledger.TryConvertThingToVirtual(thing, out accepted);
        }

        // ---------- 取出 ----------

        /// <summary>
        /// 事务级虚拟取出：校验单计划 → 拒绝保真通道 → 走执行器并**强制足额**（W21）。
        /// 入：plan = 单计划（键 / 件数 / 模式）；actor = 触发者，可空。出：true = made 非空；false = made = null，已整笔回滚。
        /// 区别：与 Job 预留路径的区别是本方法自建预留且不允许部分成功。
        /// </summary>
        public static bool TryWithdraw(SDS_WithdrawPlan plan, Pawn actor, out List<Thing> made)
        {
            made = null;
            if (plan == null || plan.query == null || plan.query.key == null || plan.query.quantity <= 0)
            {
                return false;
            }
            if (plan.query.preserved || plan.query.key.preserved)
            {
                SDS_Diag.Fail("取货/通道不匹配", SDS_Diag.Ctx(null, actor, null, null, "保真请用 TryWithdrawPreservedExact"));
                return false;
            }
            return SDS_WithdrawExecutor.TryWithdrawVirtual(plan.query.key, plan.query.quantity, out made,
                plan.mode, actor, true);
        }

        /// <summary>精确保真取出的转发口（纯转发执行器）。入：plan = 点名真身 + 件数；actor = 触发者。出：true = made 非空。
        /// 区别：与 TryWithdrawPreservedExactAdopted 的区别是自建并自行释放预留。</summary>
        public static bool TryWithdrawPreservedExact(List<ThingCount> plan, Pawn actor,
            out List<Thing> made)
        {
            return SDS_WithdrawExecutor.TryWithdrawPreservedExact(plan, actor, out made);
        }

        /// <summary>
        /// 接管 Job 预留后的精确保真取出：失败不释放预留，由 Job 的 FinishAction 统一清理。
        /// </summary>
        /// <remarks>入：reservationId = Job 预留 id；plan = 点名真身；mode = 物化模式。出：true = made 非空。
        /// 区别：与 TryWithdrawPreservedExact 的区别只在预留来源；失败同样不释放预留。</remarks>
        public static bool TryWithdrawPreservedExactAdopted(int reservationId, List<ThingCount> plan,
            out List<Thing> made, SDS_WithdrawMode mode = SDS_WithdrawMode.Hand)
        {
            return SDS_WithdrawExecutor.TryWithdrawPreservedExactAdopted(reservationId, plan, out made, mode);
        }

        /// <summary>消费既有 Job 预留的虚拟取出（纯转发执行器）。
        /// 入：reservationId = Job 预留 id。出：true = made 非空且未交付；false = made = null，预留留给 Job 清理。
        /// 区别：与 TryWithdraw 的区别是不自建预留、不 Deliver，且允许部分成功。</summary>
        public static bool TryWithdrawReservedForJob(int reservationId, out List<Thing> made)
        {
            return SDS_WithdrawExecutor.TryWithdrawReservedForJob(reservationId, out made);
        }

        // ---------- Job 预留生命周期（Vanilla 只交事实） ----------

        /// <summary>
        /// 为 Job 建虚拟预留：优先键可用则用之，否则按 def 重新选键，再按 maxStackSpace 夹取。
        /// 入：jobLoadID = Job 归属 id（0 失败）；def = 目标 def；maxStackSpace = 单次上限（≤0 失败）；
        /// pawn = 位置参考，可空；preferredKey = 优先键，可 null。
        /// 出：true = 已预留（已有同 job 预留时只回填 key / count）；false = key = null、count = 0
        ///      （含「已有预留但归属不匹配」——不覆盖既有预留）。
        /// 区别：与 TryReservePreservedForJob 的区别是按 def 选键而非点名真身；与 TryWithdraw 的区别是只预留不取出。
        /// </summary>
        public static bool TryReserveForJob(int jobLoadID, ThingDef def, int maxStackSpace,
            Pawn pawn, SDS_StockKey preferredKey, out SDS_StockKey key, out int count)
        {
            key = null;
            count = 0;
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            // 预留只是「先占住」的标记，不移动实物；真正的写门在 Begin / Execute。
            // 候选可用量仍走 SDS_Availability（G 关闭时自然为 0），不额外叠加一次 Available 判断。
            if (ledger == null || jobLoadID == 0 || def == null || maxStackSpace <= 0)
            {
                return false;
            }
            if (ledger.TryGetJobReservation(jobLoadID, out key, out count))
            {
                // 幂等分支也要复核既有预留的通道 / def，避免把别的键当成本次已预留。
                if (key != null && !key.preserved && key.thingDef == def && count > 0)
                {
                    return true;
                }
                // 公开契约：返回 false 时 out 参数必须是默认值。
                key = null;
                count = 0;
                return false;
            }
            if (ledger.HasJobReservation(jobLoadID))
            {
                return false;
            }
            SDS_StockKey candidate = preferredKey;
            long available = candidate != null && candidate.thingDef == def && !candidate.preserved
                ? SDS_AvailabilityImpl.GetAvailableCount(candidate)
                : 0L;
            if (available <= 0L)
            {
                candidate = FindWithdrawableKey(def, false);
                if (candidate == null)
                {
                    return false;
                }
                available = SDS_AvailabilityImpl.GetAvailableCount(candidate);
            }
            int want = SDS_AvailabilityImpl.ClampTakeToAvailable(maxStackSpace, available);
            if (want <= 0)
            {
                return false;
            }
            if (!ledger.TryReserve(jobLoadID,
                new List<SDS_StockKey> { candidate }, new List<int> { want }, pawn))
            {
                return false;
            }
            key = candidate;
            count = want;
            return true;
        }

        /// <summary>
        /// 为 Job 建保真预留：参数有效且已有该 job 的任意预留时直接视为成功（幂等），否则按点名真身预留。
        /// 入：jobLoadID = Job 归属 id（0 失败）；plan = 点名真身 + 件数；pawn = 触发者，可空。
        /// 出：true = 已预留 / 参数有效且已有预留；false = 参数无效或预留不足。
        /// 区别：与 TryReserveForJob 的区别是按实例而非 def，且幂等判定不看键是否匹配。
        /// </summary>
        public static bool TryReservePreservedForJob(int jobLoadID, List<ThingCount> plan, Pawn pawn)
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            // 保真预留按真身事实构造；全局门由 Begin / Execute 执行，
            // 低层预留（迁移 / 单测）不要求当前存在可用核心。
            if (ledger == null || jobLoadID == 0 || plan == null || plan.Count == 0)
            {
                return false;
            }
            if (ledger.HasJobReservation(jobLoadID))
            {
                return true;
            }
            return ledger.TryReservePreservedPlan(jobLoadID, pawn, plan);
        }

        /// <summary>读取某个 Job 的唯一预留键与合计件数；只读，不改账、不释放。</summary>
        /// <remarks>入：jobLoadID = Job 归属 id。出：key / total；多键或无预留返回 false。
        /// 用途：W47 读档后按预留键重建保真装载计划。</remarks>
        public static bool TryGetJobReservation(int jobLoadID, out SDS_StockKey key, out int total)
        {
            key = null;
            total = 0;
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            return ledger != null && jobLoadID != 0
                && ledger.TryGetJobReservation(jobLoadID, out key, out total);
        }

        /// <summary>释放该 Job 的全部预留（幂等）。入：jobLoadID = Job 归属 id（0 忽略）。出：无；不动实物与账面。
        /// 区别：与 TryWithdrawReservedForJob 的「消费」相反，本方法不取出任何东西。</summary>
        public static void ReleaseJobReservation(int jobLoadID)
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger != null && jobLoadID != 0)
            {
                ledger.ReleaseReservation(jobLoadID);
            }
        }

        /// <summary>镜像取出（纯转发执行器）。入：mirror = 地图镜像；count = 件数；physical = 调用方注入的转换器。出：true = taken 非空。
        /// 区别：与 TryConsumeMirror 的区别是生成实物；与 TryWithdraw 的区别是实物换实物。</summary>
        public static bool TryTakeMirror(Thing mirror, int count, ISDS_MirrorPhysical physical,
            out Thing taken)
        {
            return SDS_WithdrawExecutor.TryTakeMirror(mirror, count, physical, out taken);
        }

        /// <summary>
        /// 外部消费确认能力（W22）：只扣账、不预留；账本不足时按现有量扣。
        /// 当前无生产调用：镜像被外部 Destroy 已改为 W19 fail-closed（拒绝销毁），不再由此扣账。
        /// </summary>
        /// <remarks>入：mirror = 待销毁镜像（须在图上、未销毁、虚拟通道）。出：true = 允许销毁，consumed = 实扣件数。
        /// 区别：与 TryTakeMirror 的区别是不生成实物；与 TryWithdraw 的区别是不足额也放行。</remarks>
        public static bool TryConsumeMirror(Thing mirror, out int consumed)
        {
            consumed = 0;
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || mirror == null || mirror.Destroyed || !mirror.Spawned || mirror.Map == null)
            {
                return false;
            }
            SDS_StockKey key = SDS_StockData.FromThing(mirror);
            if (key == null || key.thingDef == null || key.preserved)
            {
                return false;
            }
            return ledger.TryConsumeVirtual(key, mirror.stackCount, out consumed);
        }

        // ---------- W34 存量迁移 ----------

        /// <summary>
        /// 玩家确认「武器/装备强制虚拟化」后的一次性存量迁移：只迁移按 W33 已不再需要保真的
        /// 武器 / 衣物；有事务预留的归属组、仍需保真的身份 / 状态物一律不动。返回转换件数。
        /// </summary>
        /// <remarks>入：无。出：转换件数；无账本返回 0。
        /// 区别：绕过容量 / 筛选 / 可用性门（迁移专用），只由玩家确认后调用。</remarks>
        public static int ConvertPreservedGearToVirtual()
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null)
            {
                return 0;
            }
            return ledger.ConvertPreservedGearToVirtual(SDS_StockRules.CanMigratePreservedGearToVirtual);
        }

        // ---------- 回充 ----------

        /// <summary>回充单件（无指定落点）：落点推导交给 SDS_ReturnUtility。入：thing / channel / map / pawn / fallback。出：true = 已回充。
        /// 区别：与带 cell 重载的区别是落点由实现推导。</summary>
        public static bool ReturnThing(Thing thing, SDS_StockChannel channel,
            Map map, Pawn pawn, SDS_ReturnFallback fallback)
        {
            return ReturnThing(thing, channel, map, pawn, fallback, IntVec3.Invalid);
        }

        /// <summary>
        /// 带调用方落点的回充：cell 只用于 Ground 退路，有效且在界内时优先于 pawn 脚下 / 地图中心。
        /// 回账本成功时 cell 无意义（实物已被账本消化）。
        /// </summary>
        /// <remarks>入：cell = 期望落点（仅 Ground 退路有效）。出：true = 已回充。
        /// 区别：与无 cell 重载的唯一差别是显式落点；通道选择按 channel 分派 ReturnPreserved / ReturnVirtual。</remarks>
        public static bool ReturnThing(Thing thing, SDS_StockChannel channel,
            Map map, Pawn pawn, SDS_ReturnFallback fallback, IntVec3 cell)
        {
            if (channel == SDS_StockChannel.Preserved)
            {
                return SDS_ReturnUtility.ReturnPreserved(thing, map, pawn, fallback, cell);
            }
            return SDS_ReturnUtility.ReturnVirtual(thing, map, pawn, fallback, cell);
        }

        /// <summary>销毁失败时的数量回充：只补虚拟账本，不涉及实物。</summary>
        /// <remarks>入：key = 虚拟键（保真键 / null / count ≤0 一律 false）；count = 件数。出：true = 已补账。
        /// 区别：与 ReturnThing 的区别是没有实物、不做物化。</remarks>
        public static bool ReturnVirtualCount(SDS_StockKey key, int count)
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || key == null || key.preserved || count <= 0)
            {
                return false;
            }
            return ledger.TryDepositVirtual(key, count, null);
        }

        /// <summary>批量回充（逐件走 ReturnThing，忽略单件结果）。入：things = 列表，可空。出：无。
        /// 区别：与 ReturnThing 的区别是批量语义，不汇总失败数。</summary>
        public static void ReturnThings(List<Thing> things, SDS_StockChannel channel,
            Map map, Pawn pawn, SDS_ReturnFallback fallback)
        {
            if (things == null)
            {
                return;
            }
            for (int i = 0; i < things.Count; i++)
            {
                ReturnThing(things[i], channel, map, pawn, fallback);
            }
        }

        // ---------- 展示物 / 来源标记 / 旧档 ----------

        /// <summary>是否带「刚物化」标记（纯转发 SDS_ProducedThingMarker）。入：thing。出：true = 已标记。纯读。
        /// 区别：与 IsPreserved 无关，标记只表示来源，不代表所有权。</summary>
        public static bool IsProducedMarked(Thing thing)
        {
            return SDS_ProducedThingMarker.IsMarked(thing);
        }

        /// <summary>刚物化、待原版消费的来源标记：Vanilla 物化后登记。</summary>
        /// <remarks>入：thing = 刚生成实物。出：无。区别：短命标记，不参与回收 / 回充。</remarks>
        public static void MarkProduced(Thing thing)
        {
            SDS_ProducedThingMarker.Mark(thing);
        }

        /// <summary>解除来源标记（回充 / 交付完成后）。</summary>
        /// <remarks>入：thing = 目标实物，可空。出：无；幂等。区别：与 MarkProduced 成对。</remarks>
        public static void UnmarkProduced(Thing thing)
        {
            SDS_ProducedThingMarker.Unmark(thing);
        }

        // ---------- 静态清理登记 ----------

        /// <summary>
        /// 本模块初始化时调用一次：登记判定层 / 流程层的 4 个静态清理项与 Rules 自检提供者（E1）。
        /// 入：无。出：无；幂等。区别：只登记清理动作与诊断提供者，不执行清理、不触碰账本。
        /// </summary>
        public static void RegisterAll()
        {
            if (registered)
            {
                return;
            }
            registered = true;
            SDS_StaticState.Register(new SDS_ResetAction(SDS_EndpointContractCache.Reset));
            SDS_StaticState.Register(new SDS_ResetAction(SDS_ProducedThingMarker.Reset));
            SDS_StaticState.Register(new SDS_ResetAction(SDS_ImplantUtilityImpl.ResetChipCache));
            // 结构事实属于「当前存档」的静态状态；不清空会把上一档的 Available / TypeCapacity
            // 带进新档，直到 Reconcile 收到第一份活体事实。
            SDS_StaticState.Register(new SDS_ResetAction(structures.Clear));
            SDS_SelfCheck.Register(new SDS_RulesDiagnostics());
        }

        // ---------- 内部：容量口径 ----------

        /// <summary>
        /// 容量门的唯一实现：已存在的品类恒可继续存入；新品类要求 UsedCapacity &lt; TypeCapacity。
        /// 入：key = 待存入键，可 null。出：true = 允许。
        /// 区别：与 CategoryExists 的区别是它还带容量判定；容量下调时不驱逐存量。
        /// </summary>
        private static bool CanAcceptKey(SDS_StockKey key)
        {
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || key == null)
            {
                return false;
            }
            // 容量上限只约束「新品类」；容量被玩家下调到低于已用数时，
            // 已有品类仍可继续存入（存量不驱逐），不得整体拒收。
            return CategoryExists(key) || SDS_CoreImpl.UsedCapacity < TypeCapacity;
        }

        /// <summary>
        /// 该键的品类归属是否已在账本中（按 CategoryKeyOf 归零维度后比对）。
        /// 入：key = 精确键。出：true = 该品类已存在。
        /// 区别：与 CanAcceptKey 的区别是只答「是否已有」，不含容量比较。
        /// </summary>
        private static bool CategoryExists(SDS_StockKey key)
        {
            SDS_StockKey target = SDS_StockData.CategoryKeyOf(key);
            if (target == null)
            {
                return false;
            }
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            return ledger != null && ledger.HasCategory(target);
        }
    }
}
