using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 取货事务状态机：Reserved → Materialized → Delivered / RolledBack。
    /// Deliver 只释放预留不搬运；Rollback 按通道回充（保真 / 虚拟），不销毁实物。
    /// </summary>
    internal sealed class SDS_WithdrawTransaction
    {
        private static int nextId = int.MinValue + 1;

        public int id;
        public Pawn actor;
        public List<SDS_WithdrawPlan> plans = new List<SDS_WithdrawPlan>();
        public List<Thing> materialized = new List<Thing>();
        public SDS_WithdrawEndState state;

        private bool materializedPreserved;
        private SDS_Ledger ledger;

        /// <summary>
        /// 由「单键 + 件数」构造单计划事务并立即预留。
        /// 入：key = 目标键；quantity = 件数（≤0 直接失败）；preserved = 是否强制走保真通道
        ///     （与 key.preserved 取或）；actor = 触发者，可空；mode = 物化模式。
        /// 出：Reserved 状态事务；失败返回 null（参数无效或预留不足），此时账本未变。
        /// 区别：与 Begin 的区别只是「一个键 vs 多计划」；与 BeginJobAdopt 的区别是本方法**新建预留**。
        /// </summary>
        public static SDS_WithdrawTransaction BeginSingle(SDS_StockKey key, int quantity, bool preserved,
            Pawn actor, SDS_WithdrawMode mode = SDS_WithdrawMode.Hand)
        {
            if (key == null || quantity <= 0)
            {
                SDS_Diag.Fail("取货/参数无效", SDS_Diag.Ctx(null, actor, null, null,
                    "quantity=" + quantity + " preserved=" + preserved));
                return null;
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = quantity,
                    preserved = preserved || key.preserved
                },
                endpoint = null,
                mode = mode
            };
            return Begin(new List<SDS_WithdrawPlan> { plan }, actor);
        }

        /// <summary>
        /// 多计划事务入口：校验计划 → 按通道统一预留（虚拟 TryReserve / 保真 TryReservePreserved）。
        /// 入：plans = 非空计划表，每项 key 非空、quantity&gt;0、mode ≠ VirtualHandoff，且不得虚拟 / 保真混排；
        ///     actor = 触发者，可空。
        /// 出：Reserved 状态事务（预留 id 由 NextId 生成）；失败返回 null，且不留下任何预留。
        /// 区别：与 BeginSingle 同源；与 BeginJobAdopt 的区别是本方法自己预留、失败即无副作用，
        ///       接管版只校验他人预留是否足额，不再预留。
        /// </summary>
        public static SDS_WithdrawTransaction Begin(List<SDS_WithdrawPlan> plans, Pawn actor)
        {
            // 全局可用性门：Begin 是唯一的新建事务入口，门必须定义在这里，
            // 不能依赖每个调用方各自判断（准则三）。
            if (!SDS_RulesImpl.Available)
            {
                return null;
            }
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || plans == null || plans.Count == 0)
            {
                SDS_Diag.Fail("取货/事务参数无效", SDS_Diag.Ctx(null, actor, null, null,
                    "plans=" + (plans != null ? plans.Count : -1)));
                return null;
            }
            List<SDS_StockKey> keys = new List<SDS_StockKey>();
            List<int> counts = new List<int>();
            bool anyPreserved = false;
            bool anyVirtual = false;
            for (int i = 0; i < plans.Count; i++)
            {
                SDS_WithdrawPlan plan = plans[i];
                if (plan == null || plan.query == null || plan.query.key == null || plan.query.quantity <= 0)
                {
                    SDS_Diag.Fail("取货/计划无效", SDS_Diag.Ctx(null, actor, null, null, "index=" + i));
                    return null;
                }
                if (plan.mode == SDS_WithdrawMode.VirtualHandoff)
                {
                    SDS_Diag.Fail("取货/交付模式不匹配", SDS_Diag.Ctx(null, actor, null, null,
                        "index=" + i + " mode=VirtualHandoff"));
                    return null;
                }
                if (plan.query.preserved || plan.query.key.preserved)
                {
                    anyPreserved = true;
                }
                else
                {
                    anyVirtual = true;
                }
                keys.Add(plan.query.key);
                counts.Add(plan.query.quantity);
            }
            if (anyPreserved && anyVirtual)
            {
                SDS_Diag.Fail("取货/通道混合计划不支持", SDS_Diag.Ctx(null, actor, null, null,
                    "plans=" + plans.Count));
                return null;
            }
            int id = NextId();
            // 保真计划必须按保真存量预留；虚拟预留只看 stock，会把保真取货永远挡在 Begin。
            bool reserved = anyPreserved
                ? ledger.TryReservePreserved(id, keys, counts, actor)
                : ledger.TryReserve(id, keys, counts, actor);
            if (!reserved)
            {
                return null;
            }
            return new SDS_WithdrawTransaction
            {
                id = id,
                actor = actor,
                plans = new List<SDS_WithdrawPlan>(plans),
                state = SDS_WithdrawEndState.Reserved,
                ledger = ledger
            };
        }

        /// <summary>
        /// 接管已有 Job 预留的事务入口：只校验该预留对每个键的余量是否足额，不再新增预留。
        /// 入：reservationId = Job 预留 id（0 直接失败）；plans = 本次要消费的计划；
        ///     actor = 触发者，可空。
        /// 出：以 reservationId 为 id 的 Reserved 状态事务；null = 参数无效、计划含 VirtualHandoff、
        ///     或某键预留余量不足（此时不动预留，交给 Job 的 FinishAction 清理）。
        /// 区别：与 Begin 的区别是「校验他人预留」而非「自己预留」；释放时机也不同——
        ///       本方法建出的事务由调用方（或 Job）决定何时 Deliver / Rollback。
        /// </summary>
        public static SDS_WithdrawTransaction BeginJobAdopt(int reservationId,
            List<SDS_WithdrawPlan> plans, Pawn actor)
        {
            // 接管既有预留同样属于事务入口：全局不可用时不得继续消费预留 / 物化。
            if (!SDS_RulesImpl.Available)
            {
                return null;
            }
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null || reservationId == 0 || plans == null || plans.Count == 0)
            {
                SDS_Diag.Fail("取货/接管参数无效", SDS_Diag.Ctx(null, actor, null, null,
                    "reservation=" + reservationId + " plans=" + (plans != null ? plans.Count : -1)));
                return null;
            }
            Dictionary<SDS_StockKey, int> totals = new Dictionary<SDS_StockKey, int>();
            for (int i = 0; i < plans.Count; i++)
            {
                SDS_WithdrawPlan plan = plans[i];
                if (plan == null || plan.query == null || plan.query.key == null || plan.query.quantity <= 0)
                {
                    return null;
                }
                if (plan.mode == SDS_WithdrawMode.VirtualHandoff)
                {
                    return null;
                }
                totals.TryGetValue(plan.query.key, out int current);
                totals[plan.query.key] = current + plan.query.quantity;
            }
            foreach (KeyValuePair<SDS_StockKey, int> pair in totals)
            {
                if (ledger.GetReserved(reservationId, pair.Key) < pair.Value)
                {
                    return null;
                }
            }
            return new SDS_WithdrawTransaction
            {
                id = reservationId,
                actor = actor,
                plans = new List<SDS_WithdrawPlan>(plans),
                state = SDS_WithdrawEndState.Reserved,
                ledger = ledger
            };
        }

        // W21：执行期足额口径由入口固定——requireFullQuantity 足额不足整笔回滚，尽量则按实得交付。
        // RULE:W21
        /// <summary>
        /// 执行虚拟通道：逐计划扣账物化，按实际件数消费预留；可选足额校验。
        /// 入：requireFullQuantity = true 时任一计划实得件数 &lt; 请求件数即整笔回滚；
        ///     false 时允许部分成功（按实得交付）。仅接受 Reserved 状态、且计划必须是虚拟通道。
        /// 出：true = 状态 Materialized，made 为全部实物（未交付，预留尚未释放）；
        ///     false = made = null，已物化的实物按虚拟通道回充，状态不变（仍 Reserved，由调用方 Rollback）。
        /// 区别：与 ExecuteVirtualReserved 的区别是本方法不释放预留、由 Deliver 统一释放，
        ///       且支持足额校验；与 ExecutePreservedExact 的区别是通道不同。
        /// </summary>
        public bool ExecuteVirtual(out List<Thing> made, bool requireFullQuantity = false)
        {
            return ExecuteVirtualCore(false, requireFullQuantity, "ExecuteVirtual", out made);
        }

        /// <summary>Job 预留的虚拟取出：扣账后立即消费并释放预留（不再由 JobDriver 调 Deliver）。</summary>
        /// <remarks>
        /// 入：无外部参数；键与件数取自事务计划（由 BeginJobAdopt 传入的 Job 预留），仅支持虚拟通道。
        /// 出：true = 状态直接置 Delivered，made 为实物（预留已释放，调用方无需再 Deliver）；
        ///     false = made = null，已物化部分回充，预留**不释放**（留给 Job 的 FinishAction）。
        /// 区别：与 ExecuteVirtual 的区别是「部分成功 + 立即终结」；与 Deliver 的区别是本方法自带释放。
        /// </remarks>
        public bool ExecuteVirtualReserved(out List<Thing> made)
        {
            return ExecuteVirtualCore(true, false, "ExecuteVirtualReserved", out made);
        }

        /// <summary>
        /// 虚拟通道执行循环的唯一实现：两条入口只在「是否接管既有预留 / 是否足额 / 末尾释放」上不同。
        /// 入：adoptReservation —— true = 消费既有 Job 预留并在成功时释放；
        ///     requireFullQuantity —— true = 任一计划不足即整笔失败；
        ///     stage —— 断言用阶段名；out made —— 物化结果。
        /// 出：true = 已物化；false = 已物化部分按通道回充，状态仍 Reserved。
        /// 区别：ExecuteVirtual / ExecuteVirtualReserved 只是本方法的两条语义外壳。
        /// </summary>
        private bool ExecuteVirtualCore(bool adoptReservation, bool requireFullQuantity,
            string stage, out List<Thing> made)
        {
            made = null;
            SDS_Diag.AssertTransactionStage(id, state, SDS_WithdrawEndState.Reserved, stage);
            if (ledger == null || !SDS_RulesImpl.Available || plans.Count == 0
                || state != SDS_WithdrawEndState.Reserved)
            {
                return false;
            }
            materializedPreserved = false;
            List<Thing> all = new List<Thing>();
            for (int i = 0; i < plans.Count; i++)
            {
                SDS_WithdrawPlan plan = plans[i];
                if (plan.query.preserved || (plan.query.key != null && plan.query.key.preserved))
                {
                    SDS_Diag.Fail("取货/通道不匹配", SDS_Diag.Ctx(null, actor, null, null,
                        "index=" + i + " 期望虚拟通道"));
                    RollbackMaterialized(all);
                    materialized.Clear();
                    return false;
                }
                if (adoptReservation
                    && ledger.GetReserved(id, plan.query.key) < plan.query.quantity)
                {
                    RollbackMaterialized(all);
                    materialized.Clear();
                    return false;
                }
                if (!ledger.TryWithdrawVirtual(plan.query.key, plan.query.quantity, out List<Thing> part)
                    || part == null || part.Count == 0)
                {
                    RollbackMaterialized(all);
                    materialized.Clear();
                    return false;
                }
                // 增量登记必须发生在 TryWithdrawVirtual 成功后的第一行：
                // 后续 CountUnits / ConsumeReservation / 足额校验任何一步抛异常，
                // 执行器都能从 materialized 把已扣账实物回充。
                all.AddRange(part);
                materialized = all;
                int units = CountUnits(part);
                int consumed = ledger.ConsumeReservation(id, plan.query.key, units);
                if (consumed != units)
                {
                    // 预留与扣账不一致：整笔回滚，绝不让「扣了账但没消费预留」或反之的状态流出。
                    SDS_Diag.Fail("取货/预留与扣账不一致（" + (adoptReservation ? "Job 预留" : "整笔回滚") + "）",
                        SDS_Diag.Ctx(actor != null ? actor.Map : null, actor, null, null,
                            "want=" + units + " consumed=" + consumed));
                    RollbackMaterialized(all);
                    materialized.Clear();
                    return false;
                }
                if (requireFullQuantity && units < plan.query.quantity)
                {
                    SDS_Diag.Fail("取货/足额校验失败（整笔回滚）", SDS_Diag.Ctx(
                        actor != null ? actor.Map : null, actor, null, null,
                        "want=" + plan.query.quantity + " got=" + units));
                    RollbackMaterialized(all);
                    materialized.Clear();
                    return false;
                }
            }
            if (adoptReservation)
            {
                ledger.ReleaseReservation(id);
                state = SDS_WithdrawEndState.Delivered;
            }
            else
            {
                state = SDS_WithdrawEndState.Materialized;
            }
            made = all;
            return true;
        }

        /// <summary>
        /// 精确保真取出：把调用方点名的真身整批移出账本，交由事务持有。
        /// 入：plan = ThingCount 列表（点名真身 + 件数，非空）；须为 Reserved 状态。
        /// 出：true = 状态 Materialized，made 为取出的真身（预留此后由 Deliver 释放）；
        ///     false = made = null；账本侧失败即整笔回滚（由 SDS_Ledger.TryWithdrawPreservedPlan 保证），
        ///     状态仍 Reserved，由调用方 Rollback 释放预留。
        /// 区别：与 ExecuteVirtual 的区别是只走保真通道且按实例取；账本侧按实例逐一移除，
        ///       本方法只校验「执行量不超过本事务预留」，不校验单个真身的足额。
        /// </summary>
        public bool ExecutePreservedExact(List<ThingCount> plan, out List<Thing> made)
        {
            made = null;
            SDS_Diag.AssertTransactionStage(id, state, SDS_WithdrawEndState.Reserved, "ExecutePreservedExact");
            // 与 ExecuteVirtual / ExecuteMirror 同一条运行时状态门：断言只是诊断，
            // 不能阻止二次执行把同一批真身再取一遍。
            if (ledger == null || !SDS_RulesImpl.Available
                || state != SDS_WithdrawEndState.Reserved
                || plan == null || plan.Count == 0)
            {
                return false;
            }
            // 执行量与预留同口径：本方法允许内部调用方传入任意 plan，
            // 不能只依赖「同一份 plan 派生」的隐式闭合。
            Dictionary<SDS_StockKey, long> planned = new Dictionary<SDS_StockKey, long>();
            for (int i = 0; i < plan.Count; i++)
            {
                Thing plannedThing = plan[i].Thing;
                int units = plan[i].Count;
                if (plannedThing == null || units <= 0)
                {
                    continue;
                }
                SDS_StockKey plannedKey = SDS_StockData.FromPreservedThing(plannedThing);
                if (plannedKey == null)
                {
                    continue;
                }
                planned.TryGetValue(plannedKey, out long already);
                planned[plannedKey] = already + units;
            }
            foreach (KeyValuePair<SDS_StockKey, long> pair in planned)
            {
                long reserved = ledger.GetReserved(id, pair.Key);
                if (reserved < pair.Value)
                {
                    SDS_Diag.Fail("取货/保真执行量超过预留（整笔失败）", SDS_Diag.Ctx(
                        actor != null ? actor.Map : null, actor, null, null,
                        "def=" + pair.Key.EffectiveDefName
                        + " want=" + pair.Value + " reserved=" + reserved));
                    return false;
                }
            }
            materializedPreserved = true;
            if (!ledger.TryWithdrawPreservedPlan(plan, out made) || made == null || made.Count == 0)
            {
                made = null;
                return false;
            }
            materialized = made;
            state = SDS_WithdrawEndState.Materialized;
            return true;
        }

        /// <summary>
        /// 镜像取出：先扣虚拟账，再让宿主把地图镜像转换为真实物（整堆或按件）。
        /// 入：mirror = 地图上的镜像实物；quantity = 请求件数（≥ mirror.stackCount 时整堆转换）；
        ///     事务必须恰好一个计划，其 query.key 为扣账键。
        /// 出：true = 状态 Materialized，taken 为新实物，预留已按实际扣账件数消费（待 Deliver 释放）；
        ///     false = taken = null；已扣账但未拿到实物时按原 vitals 退回虚拟账，
        ///     拿到实物则登记到 materialized 由 Rollback 回充。宿主未注入 / 参数不符 / 扣账不足均直接失败。
        /// 语义：实际扣账 / 消费件数 = min(quantity, mirror.stackCount)，绝不按请求量扣超出的部分。
        /// 区别：与 ExecuteVirtual 的区别是「实物换实物」——镜像不来自物化，而是宿主就地转换；
        ///       因此没有「足额」概念，且失败时用 TryDepositVirtual 反向补账而非 ReturnVirtual。
        /// </summary>
        public bool ExecuteMirror(Thing mirror, int quantity, out Thing taken)
        {
            taken = null;
            SDS_Diag.AssertTransactionStage(id, state, SDS_WithdrawEndState.Reserved, "ExecuteMirror");
            if (ledger == null || !SDS_RulesImpl.Available
                || state != SDS_WithdrawEndState.Reserved
                || mirror == null || quantity <= 0 || plans.Count != 1
                || plans[0].query == null || plans[0].query.key == null)
            {
                return false;
            }
            ISDS_MirrorPhysical physical = plans[0].physical;
            if (physical == null)
            {
                SDS_Diag.Fail("取货/镜像宿主未注入", SDS_Diag.Ctx(
                    mirror.Map, actor, mirror, null, "quantity=" + quantity));
                return false;
            }
            SDS_StockKey key = plans[0].query.key;
            // 整堆转换时实际能交付的只有 mirror.stackCount；请求量更大时必须按实际件数扣账，
            // 否则会出现「账扣 quantity、实物只给 stackCount」的静默丢量。
            int units = quantity >= mirror.stackCount ? mirror.stackCount : quantity;
            if (units <= 0)
            {
                SDS_Diag.Fail("取货/镜像件数无效", SDS_Diag.Ctx(
                    mirror.Map, actor, mirror, null, "quantity=" + quantity));
                return false;
            }
            // 先确认预留足额再扣账：镜像路径不做足额物化，但预留与扣账必须同口径。
            if (ledger.GetReserved(id, key) < units)
            {
                SDS_Diag.Fail("取货/镜像预留不足", SDS_Diag.Ctx(
                    mirror.Map, actor, mirror, null, "quantity=" + quantity + " units=" + units));
                return false;
            }
            SDS_StockVitals vitals = new SDS_StockVitals
            {
                hitPoints = SDS_StockData.CaptureHitPoints(mirror),
                rotProgress = SDS_StockData.CaptureRotProgress(mirror)
            };
            if (!ledger.TryDebitVirtual(key, units))
            {
                return false;
            }
            Thing piece = null;
            bool ok;
            try
            {
                ok = units >= mirror.stackCount
                    ? physical.TryTakeWholePhysical(key, mirror, out piece)
                    : physical.TryTakePhysical(key, mirror, units, out piece);
            }
            catch (Exception e)
            {
                // 物理转换本身抛异常：已经拿到实物就交给事务回滚，否则把刚扣的账退回。
                if (piece != null && piece.stackCount > 0)
                {
                    ReclaimMirrorPiece(key, units, piece);
                }
                else
                {
                    ledger.TryDepositVirtual(key, units, vitals);
                }
                SDS_Diag.Fail("取货/镜像物理转换异常", "镜像通道", e);
                return false;
            }
            if (!ok || piece == null)
            {
                if (piece != null && piece.stackCount > 0)
                {
                    // 物理层报失败但已经产出实物：不能只补账（那会一账一物），
                    // 按实际件数对齐账面后交给 Rollback 整件回充。
                    ReclaimMirrorPiece(key, units, piece);
                    SDS_Diag.Fail("取货/镜像物理转换失败但已产出实物（整笔回滚）", SDS_Diag.Ctx(
                        mirror.Map, actor, piece, null, "quantity=" + quantity + " units=" + units));
                    return false;
                }
                ledger.TryDepositVirtual(key, units, vitals);
                return false;
            }
            int actualUnits = piece.stackCount;
            if (actualUnits <= 0)
            {
                ledger.TryDepositVirtual(key, units, vitals);
                SDS_Diag.Fail("取货/镜像物理转换返回空件", SDS_Diag.Ctx(
                    mirror.Map, actor, piece, null, "quantity=" + quantity + " units=" + units));
                return false;
            }
            if (actualUnits != units)
            {
                // 物理层违背「按请求件数产出」契约：先把账面差额补齐，再把实物交给
                // Rollback 整件回充，确保账实回到调用前状态。
                ReclaimMirrorPiece(key, units, piece);
                SDS_Diag.Fail("取货/镜像件数与扣账不一致（整笔回滚）", SDS_Diag.Ctx(
                    mirror.Map, actor, piece, null,
                    "quantity=" + quantity + " debited=" + units + " actual=" + actualUnits));
                return false;
            }
            int consumed = ledger.ConsumeReservation(id, key, units);
            if (consumed != units)
            {
                materializedPreserved = false;
                materialized = new List<Thing> { piece };
                SDS_Diag.Fail("取货/镜像预留与扣账不一致（整笔回滚）", SDS_Diag.Ctx(
                    mirror.Map, actor, piece, null,
                    "want=" + units + " consumed=" + consumed));
                return false;
            }
            materializedPreserved = false;
            materialized = new List<Thing> { piece };
            state = SDS_WithdrawEndState.Materialized;
            taken = piece;
            return true;
        }

        /// <summary>
        /// 把「已扣的账面件数」对齐到「物理层实际产出的件数」。
        /// 入：key —— 扣账键；debited —— 已经扣掉的件数；piece —— 物理层返回的实物（可空）。
        /// 出：无；只补 / 扣差额，不改变实物所有权。
        /// 区别：正常路径不调用；只在物理层件数与请求不一致时，为随后的 Rollback 铺平账实相等。
        /// </summary>
        private void AlignMirrorDebit(SDS_StockKey key, int debited, Thing piece)
        {
            if (ledger == null || key == null || piece == null || piece.stackCount <= 0)
            {
                return;
            }
            int actual = piece.stackCount;
            if (actual < debited)
            {
                ledger.TryDepositVirtual(key, debited - actual, null);
            }
            else if (actual > debited)
            {
                ledger.TryDebitVirtual(key, actual - debited);
            }
        }

        /// <summary>
        /// 镜像失败收尾：把账面差额对齐到物理实体，并登记到 materialized 供 Rollback 整件回充。
        /// 入：key = 扣账键；debited = 已扣件数；piece = 物理层产出的实物（调用方已确认非空且 stackCount &gt; 0）。
        /// 出：无；只改账面与 materialized，不释放预留、不改状态。
        /// 区别：正常成功路径不调用；把 AlignMirrorDebit 与「登记待回滚实物」合成一步，避免各失败分支重复。
        /// </summary>
        private void ReclaimMirrorPiece(SDS_StockKey key, int debited, Thing piece)
        {
            AlignMirrorDebit(key, debited, piece);
            materializedPreserved = false;
            materialized = new List<Thing> { piece };
        }

        /// <summary>
        /// 交付收尾：只释放本事务预留，不搬运实物（实物已在 Materialized 时交给调用方）。
        /// 入：无；仅在 Materialized 状态生效。
        /// 出：无返回值；状态推进到 Delivered，预留被释放。非 Materialized 状态调用是空操作（已断言终态）。
        /// 区别：与 ExecuteVirtualReserved 的区别是「先物化、后交付」两段式的第二段；
        ///       与 Rollback 的区别是保留实物、只清预留。
        /// </summary>
        public void Deliver()
        {
            SDS_Diag.AssertTransactionTerminal(id, state, "Deliver");
            if (state != SDS_WithdrawEndState.Materialized)
            {
                return;
            }
            if (ledger != null)
            {
                ledger.ReleaseReservation(id);
            }
            state = SDS_WithdrawEndState.Delivered;
        }

        /// <summary>
        /// 异常路径清理：只把已物化实物按通道回充，不改变状态、不释放预留。
        /// 入：无。
        /// 出：无；materialized 清空。
        /// 区别：Rollback 会释放预留并置终态；本方法保留预留给 Job 的 FinishAction，
        ///       专供「接管既有预留」的执行器 catch 使用。
        /// </summary>
        internal void ReclaimMaterializedWithoutRelease()
        {
            RollbackMaterialized(materialized);
            if (materialized != null)
            {
                materialized.Clear();
            }
        }

        /// <summary>
        /// 整笔回滚：把已物化实物按通道回充，再释放预留；可在 Reserved / Materialized 调用。
        /// 入：无。
        /// 出：无返回值；状态置 RolledBack，materialized 清空；已 Delivered / 已 RolledBack 时为空操作（幂等）。
        /// 区别：与 Deliver 的区别是「还实物 + 清预留」；与 ExecuteVirtual 内部的局部回充不同，
        ///       本方法是事务级兜底，供执行器 catch 使用。
        /// </summary>
        public void Rollback()
        {
            SDS_Diag.AssertTransactionTerminal(id, state, "Rollback");
            if (state == SDS_WithdrawEndState.Delivered || state == SDS_WithdrawEndState.RolledBack)
            {
                return;
            }
            RollbackMaterialized(materialized);
            if (materialized != null)
            {
                materialized.Clear();
            }
            if (ledger != null)
            {
                ledger.ReleaseReservation(id);
            }
            state = SDS_WithdrawEndState.RolledBack;
        }

        // W27：回滚按事务通道走专用回退（保真 ReturnPreserved / 虚拟 ReturnVirtual），禁止通用 Return。
        /// <summary>
        /// 逐件回充已物化实物：按 materializedPreserved 选保真 / 虚拟回退，单件失败只记诊断并继续。
        /// 入：things = 待回充列表，可空。
        /// 出：无返回值；不改变事务状态与预留（由调用方负责）。
        /// 区别：与 Rollback 的区别是本方法只处理实物、不释放预留、不清状态，供增量回充复用。
        /// </summary>
        private void RollbackMaterialized(List<Thing> things)
        {
            if (things == null)
            {
                return;
            }
            Map map = actor != null ? actor.Map : null;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                try
                {
                    if (materializedPreserved)
                    {
                        SDS_ReturnUtility.ReturnPreserved(thing, map, actor, SDS_ReturnFallback.Ground);
                    }
                    else
                    {
                        SDS_ReturnUtility.ReturnVirtual(thing, map, actor, SDS_ReturnFallback.Ground);
                    }
                }
                catch (Exception e)
                {
                    SDS_Diag.Fail("取货/回滚失败（单件，已跳过）", SDS_Diag.Ctx(map, actor, thing, null,
                        "index=" + i), e);
                }
            }
        }

        /// <summary>
        /// 汇总实物件数（只数存活实例的 stackCount），用于把「物化结果」换算成需消费的预留单位。
        /// 入：things = 物化列表，可空。
        /// 出：件数合计；空列表或全销毁时为 0。
        /// 区别：与 plan.query.quantity（请求量）对照使用——两者不等即说明发生了部分成功。
        /// </summary>
        private static int CountUnits(List<Thing> things)
        {
            if (things == null)
            {
                return 0;
            }
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

        /// <summary>
        /// 生成同步事务 id：从 int.MinValue+1 递增（负数区），与 Job 预留的正 id 天然不冲突；到顶回卷。
        /// 入：无。
        /// 出：本次唯一 id（下一次调用递增）。
        /// 区别：Job 预留 id 由账本分配且存读档保留；本 id 仅用于进程内同步事务，不入档。
        /// </summary>
        private static int NextId()
        {
            if (nextId == int.MaxValue)
            {
                nextId = int.MinValue + 1;
            }
            return nextId++;
        }
    }
}
