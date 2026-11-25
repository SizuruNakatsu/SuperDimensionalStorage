using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>唯一取货执行入口：虚拟 / 精确保真 / Job 预留 / 镜像；只返回 Thing，不执行交付。</summary>
    internal static class SDS_WithdrawExecutor
    {
        /// <summary>
        /// 从虚拟通道一次性取出并交付：建同步事务 → 扣账物化 → Deliver；异常整笔回滚。
        /// 入：key = 虚拟品类键（preserved 必须为 false，否则事务不发预留）；quantity = 请求件数；
        ///     mode = 物化模式（影响堆叠 / 记录）；actor = 触发者，可空；requireFullQuantity = 是否要求足额，
        ///     为 true 时不足则整笔失败（不部分成功）。
        /// 出：true = 已物化并交付，made 为新建实物；false = 未扣账（或已回滚），made = null。
        /// 区别：与 TryWithdrawReservedForJob 不同，本方法自带预留（BeginSingle 直接预留并立即消费），
        ///       供 API / 兼容层「现取现用」；Job 路径已有预留，故走接管通道。
        /// </summary>
        public static bool TryWithdrawVirtual(SDS_StockKey key, int quantity, out List<Thing> made,
            SDS_WithdrawMode mode = SDS_WithdrawMode.Hand, Pawn actor = null, bool requireFullQuantity = false)
        {
            made = null;
            SDS_WithdrawTransaction transaction = null;
            try
            {
                transaction = SDS_WithdrawTransaction.BeginSingle(key, quantity, false, actor, mode);
                if (transaction == null)
                {
                    return false;
                }
                if (!transaction.ExecuteVirtual(out made, requireFullQuantity))
                {
                    transaction.Rollback();
                    made = null;
                    return false;
                }
                transaction.Deliver();
                return true;
            }
            catch (Exception e)
            {
                RollbackQuietly(transaction);
                SDS_Diag.Fail("取货/执行器异常", "虚拟通道", e);
                made = null;
                return false;
            }
        }

        /// <summary>
        /// 按「指定真身 + 件数」精确保真取出：先把计划聚合成保真键事务，再逐件移除真身。
        /// 入：plan = ThingCount 列表（每项必须是非销毁、count&gt;0、且落在保真口径的真身）；
        ///     actor = 触发者，可空；mode = 物化模式。
        /// 出：true = 全部真身已移出并交付，made 为取出的实物；false = 计划无效或扣账失败（已整笔回滚），made = null。
        /// 区别：与 TryWithdrawVirtual 的「按品类键凑数」不同，本方法锁定调用方点名的实例，
        ///       用于 DoBill 指定原料 / 运输指定货物；与 TryWithdrawPreservedExactAdopted 的区别是本方法自建事务并自行释放预留。
        /// </summary>
        public static bool TryWithdrawPreservedExact(List<ThingCount> plan, Pawn actor, out List<Thing> made,
            SDS_WithdrawMode mode = SDS_WithdrawMode.Container)
        {
            made = null;
            SDS_WithdrawTransaction transaction = null;
            try
            {
                List<SDS_WithdrawPlan> plans = BuildExactPreservedPlans(plan, mode);
                if (plans == null)
                {
                    return false;
                }
                transaction = SDS_WithdrawTransaction.Begin(plans, actor);
                if (transaction == null)
                {
                    return false;
                }
                if (!transaction.ExecutePreservedExact(plan, out made))
                {
                    transaction.Rollback();
                    made = null;
                    return false;
                }
                transaction.Deliver();
                return true;
            }
            catch (Exception e)
            {
                RollbackQuietly(transaction);
                SDS_Diag.Fail("取货/执行器异常", "精确保真通道", e);
                made = null;
                return false;
            }
        }

        /// <summary>
        /// 接管既有 Job 预留后的精确保真取出（DoBill 保真原料 / 运输装载）。
        /// 入：reservationId = Job 已建预留 id（0 直接失败）；plan = 目标真身 + 件数；
        ///     mode = 物化模式。
        /// 出：true = 预留已消费、真身已移出，made 为实物；false = made = null，且**不释放预留**
        ///     （由 Job 的 FinishAction 统一清理，避免半路重试丢预留）。
        /// 区别：与 TryWithdrawPreservedExact 的差别只在预留来源（接管 vs 自建），扣账与物化走同一 ExecutePreservedExact。
        /// </summary>
        public static bool TryWithdrawPreservedExactAdopted(int reservationId, List<ThingCount> plan,
            out List<Thing> made, SDS_WithdrawMode mode = SDS_WithdrawMode.Hand)
        {
            made = null;
            if (reservationId == 0)
            {
                return false;
            }
            SDS_WithdrawTransaction transaction = null;
            try
            {
                List<SDS_WithdrawPlan> plans = BuildExactPreservedPlans(plan, mode);
                if (plans == null)
                {
                    return false;
                }
                transaction = SDS_WithdrawTransaction.BeginJobAdopt(reservationId, plans, null);
                if (transaction == null)
                {
                    return false;
                }
                if (!transaction.ExecutePreservedExact(plan, out made))
                {
                    made = null;
                    return false;
                }
                transaction.Deliver();
                return true;
            }
            catch (Exception e)
            {
                // 接管路径不释放预留（留给 Job FinishAction），但已摘出的真身必须回账本。
                if (transaction != null)
                {
                    transaction.ReclaimMaterializedWithoutRelease();
                }
                SDS_Diag.Fail("取货/执行器异常", "接管预留守卫通道", e);
                made = null;
                return false;
            }
        }

        /// <summary>
        /// Job 预留的虚拟取出（部分成功契约）：只处理虚拟通道；物化成功即 true。
        /// 入：reservationId = Job 预留 id（0 或查不到直接失败）；无 plan，键与件数全部取自预留本身。
        /// 出：true = 已按预留件数物化，made 非空且**未交付**（调用方负责放置 / 交还）；
        ///     false = made = null，预留仍归 Job 的 FinishAction 清理。
        /// 区别：与 TryWithdrawVirtual 不同，本方法**不自行预留、不 Deliver**，只消费既有预留；
        ///       与 TryWithdrawPreservedExactAdopted 的区别是本方法只走虚拟通道且允许部分成功。
        /// </summary>
        public static bool TryWithdrawReservedForJob(int reservationId, out List<Thing> made)
        {
            made = null;
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || reservationId == 0)
            {
                return false;
            }
            if (!ledger.TryGetJobReservation(reservationId, out SDS_StockKey key, out int reserved)
                || key == null || key.preserved)
            {
                return false;
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = reserved,
                    preserved = false
                },
                endpoint = null,
                mode = SDS_WithdrawMode.Hand
            };
            SDS_WithdrawTransaction transaction = SDS_WithdrawTransaction.BeginJobAdopt(
                reservationId, new List<SDS_WithdrawPlan> { plan }, null);
            if (transaction == null)
            {
                return false;
            }
            try
            {
                if (!transaction.ExecuteVirtualReserved(out made) || made == null || made.Count == 0)
                {
                    made = null;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                // 接管路径不释放预留（留给 Job FinishAction），但已物化实物必须回账本，
                // 否则账已扣、件留在局部变量里被 GC。
                transaction.ReclaimMaterializedWithoutRelease();
                SDS_Diag.Fail("取货/执行器异常", SDS_Diag.Ctx(null, null, null, null,
                    "reservation=" + reservationId), e);
                made = null;
                return false;
            }
        }

        /// <summary>
        /// 从地图上的「镜像」实物扣减并取出同键的真实物：镜像只是账面代表，取出后仍生成新实物。
        /// 入：mirror = 地图上的镜像 Thing（取其 def / 材质 / 品质构造虚拟键，preserved 恒 false）；
        ///     count = 取出件数；physical = 调用方注入的物理转换器（Core 不读宿主槽位）。
        /// 出：true = 已物化并交付，taken 为取出实物；false = 镜像无效 / 转换器缺失 / 扣账失败（已回滚），taken = null。
        /// 区别：与 TryWithdrawVirtual 的区别是键来自传入的镜像实例而非调用方给定键；
        ///       镜像本身不参与扣账，扣的是同名虚拟键。
        /// </summary>
        public static bool TryTakeMirror(Thing mirror, int count, ISDS_MirrorPhysical physical,
            out Thing taken)
        {
            taken = null;
            if (physical == null)
            {
                SDS_Diag.Fail("取货/镜像转换器缺失", SDS_Diag.Ctx(
                    mirror != null ? mirror.Map : null, null, mirror, null, "count=" + count));
                return false;
            }
            SDS_WithdrawTransaction transaction = null;
            try
            {
                SDS_StockKey key = mirror != null ? SDS_StockData.FromThing(mirror) : null;
                if (key == null)
                {
                    return false;
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
                    mode = SDS_WithdrawMode.Hand,
                    physical = physical
                };
                transaction = SDS_WithdrawTransaction.Begin(new List<SDS_WithdrawPlan> { plan }, null);
                if (transaction == null)
                {
                    return false;
                }
                if (!transaction.ExecuteMirror(mirror, count, out taken) || taken == null)
                {
                    transaction.Rollback();
                    taken = null;
                    return false;
                }
                transaction.Deliver();
                return true;
            }
            catch (Exception e)
            {
                RollbackQuietly(transaction);
                SDS_Diag.Fail("取货/镜像执行异常", "镜像通道", e);
                taken = null;
                return false;
            }
        }

        /// <summary>
        /// 执行器异常路径的唯一回滚口：已交付 / 已回滚的不再动；回滚自身异常只记日志，不掩盖原始异常。
        /// 入：transaction = 可能为 null 或已结束的事务（同步事务用负 id，若不回滚会一直占用可用量到下次读档）。
        /// 出：无返回值；保证事务不再持有预留（回滚失败也已记日志）。
        /// 区别：与各方法内部的显式 Rollback 相同语义，但本方法吞异常，仅用于 catch 分支。
        /// </summary>
        private static void RollbackQuietly(SDS_WithdrawTransaction transaction)
        {
            if (transaction == null || transaction.state == SDS_WithdrawEndState.Delivered
                || transaction.state == SDS_WithdrawEndState.RolledBack)
            {
                return;
            }
            try
            {
                transaction.Rollback();
            }
            catch (Exception e)
            {
                SDS_Diag.Fail("取货/异常回滚失败", "执行器", e);
            }
        }

        /// <summary>
        /// 把 ThingCount 计划按保真键聚合成事务计划（同键件数求和，一项一 plan）。
        /// 入：plan = 调用方点名的真身 + 件数；mode = 物化模式，写入每个 plan。
        /// 出：非空列表 = 全部条目合法；null = 计划为空 / 有条目为 null、已销毁、count≤0、
        ///     或该实例不落在保真口径（已记诊断），调用方须整笔放弃。
        /// 区别：只做「校验 + 聚合」，不预留、不扣账；真正扣账在 ExecutePreservedExact。
        /// </summary>
        private static List<SDS_WithdrawPlan> BuildExactPreservedPlans(List<ThingCount> plan,
            SDS_WithdrawMode mode)
        {
            if (plan == null || plan.Count == 0)
            {
                SDS_Diag.Fail("取货/精确计划为空");
                return null;
            }
            Dictionary<SDS_StockKey, int> totals = new Dictionary<SDS_StockKey, int>();
            for (int i = 0; i < plan.Count; i++)
            {
                Thing thing = plan[i].Thing;
                int count = plan[i].Count;
                if (thing == null || thing.Destroyed || count <= 0)
                {
                    SDS_Diag.Fail("取货/精确计划条目无效", SDS_Diag.Ctx(
                        thing != null ? thing.MapHeld : null, null, thing, null, "index=" + i));
                    return null;
                }
                SDS_StockKey key = SDS_StockData.FromPreservedThing(thing);
                if (key == null || !key.preserved)
                {
                    SDS_Diag.Fail("取货/非保真条目进入保真通道", SDS_Diag.Ctx(
                        thing.MapHeld, null, thing, null, "index=" + i));
                    return null;
                }
                totals.TryGetValue(key, out int current);
                totals[key] = current + count;
            }
            List<SDS_WithdrawPlan> plans = new List<SDS_WithdrawPlan>();
            foreach (KeyValuePair<SDS_StockKey, int> pair in totals)
            {
                plans.Add(new SDS_WithdrawPlan
                {
                    query = new SDS_WithdrawQuery
                    {
                        key = pair.Key,
                        quantity = pair.Value,
                        preserved = true
                    },
                    endpoint = null,
                    mode = mode
                });
            }
            return plans;
        }
    }
}
