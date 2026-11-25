using System.Collections.Generic;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 端点决议：候选 → 硬门 → 优先级 / 距离 / thingID 排序。只产出决定，不执行交付。
    /// 优先级由查询携带（调用方读设置后填入），本类不读宿主、不缓存排序结果。
    /// 入 / 出：入端点查询，出「一个」胜出端点或 none；不预留、不扣账、不派活。
    /// 区别：端点契约版本时钟在本程序集（`SDS_EndpointContractCache`），本类只做候选筛选与排序。
    /// </summary>
    internal static class SDS_EndpointResolver
    {
        /// <summary>
        /// 端点决议（唯一入口）。
        /// 入：query —— 操作类型 / 地图 / 小人 / 参考点 / 键 / 危险度 / 是否要求可达 / 是否允许自身 / 是否要求有货。
        /// 出：allowed + port + priority + distanceSquared；无候选时 allowed = false。
        /// 语义：先静态门筛候选并排序；要求可达时只对「当前胜出者」判一次可达，不可达才取下一位。
        /// 区别：TryEvaluateStaticGate 只判单个候选，本方法负责整轮决议。
        /// </summary>
        public static SDS_EndpointDecision Select(in SDS_EndpointQuery query)
        {
            SDS_EndpointDecision best = default;
            best.Reset();
            ISDS_PortProvider ports = SDS_Host.Ports;
            if (ports == null || !ports.IsLocalUsable(query.map))
            {
                return best;
            }

            List<ISDS_Port> candidates = new List<ISDS_Port>();
            ports.AppendCandidates(query, candidates);
            if (!query.requireReach || query.actor == null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    ISDS_Port port = candidates[i];
                    if (!TryEvaluateStaticGate(port, query))
                    {
                        continue;
                    }
                    SDS_EndpointDecision candidate = BuildDecision(port, query);
                    if (IsBetter(candidate, best))
                    {
                        best = candidate;
                    }
                }
                return best;
            }

            // 可达性是原版寻路，不能对全部候选预判：先按静态门与排序选出候选，
            // 再只对「当前胜出者」判可达；不可达才标记 used 取下一位。
            List<SDS_EndpointDecision> allowed = new List<SDS_EndpointDecision>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                ISDS_Port port = candidates[i];
                if (!TryEvaluateStaticGate(port, query))
                {
                    continue;
                }
                allowed.Add(BuildDecision(port, query));
            }
            if (allowed.Count == 0)
            {
                return best;
            }
            bool[] used = new bool[allowed.Count];
            while (true)
            {
                int bestIndex = -1;
                for (int i = 0; i < allowed.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }
                    if (bestIndex < 0 || IsBetter(allowed[i], allowed[bestIndex]))
                    {
                        bestIndex = i;
                    }
                }
                if (bestIndex < 0)
                {
                    return best;
                }
                SDS_EndpointDecision candidate = allowed[bestIndex];
                if (candidate.port.IsSelf || candidate.port.CanReach(query.actor))
                {
                    return candidate;
                }
                used[bestIndex] = true;
            }
        }

        /// <summary>
        /// 把端点包成可比较的决定（不做门禁）。
        /// 入：port —— 已过静态门的端点；query —— 用于取参考点。
        /// 出：allowed = true 的决定；优先级现读、距离为平方值。
        /// 区别：TryEvaluateStaticGate 负责「能不能用」，本方法只负责「怎么比」。
        /// </summary>
        private static SDS_EndpointDecision BuildDecision(ISDS_Port port, in SDS_EndpointQuery query)
        {
            SDS_EndpointDecision candidate = default;
            candidate.Reset();
            candidate.allowed = true;
            candidate.port = port;
            candidate.priority = query.priority;
            candidate.distanceSquared = port.RankDistanceSquared(query.reference);
            return candidate;
        }

        /// <summary>
        /// 静态门：不含寻路，供排序阶段先筛候选。
        /// 入：port —— 候选；query —— 查询（含操作类型、是否允许自身、是否要求有货）。
        /// 出：是否通过；不生成拒绝原因（决议侧只关心候选是否可用）。
        /// 区别：本方法刻意不寻路，避免 27 个端点放大成 27 次寻路；可达性由 Select 只对胜出候选判一次。
        /// </summary>
        private static bool TryEvaluateStaticGate(ISDS_Port port, in SDS_EndpointQuery query)
        {
            if (port == null)
            {
                return false;
            }
            if (!port.Operational)
            {
                return false;
            }
            if (port.IsSelf && !query.allowSelf)
            {
                return false;
            }

            if (query.op == SDS_EndpointOp.Deposit)
            {
                if (query.thing == null || query.thing.Destroyed || !SDS_Rules.CanDeposit(query.thing))
                {
                    return false;
                }
                if (!query.allowProducedMarked && SDS_Rules.IsProducedMarked(query.thing))
                {
                    return false;
                }
            }
            else if (query.op == SDS_EndpointOp.Withdraw && query.requireStock)
            {
                if (query.key == null || SDS_Availability.GetAvailableCount(query.key) <= 0L)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 比较两个决定：优先级 → 距离 → thingID。
        /// 入：candidate —— 新候选；current —— 当前最优。
        /// 出：candidate 是否更优；未 allowed 的候选永远不优。
        /// 区别：本方法只排序，不做门禁；自身端口按真实距离参与，平局用 thingIDNumber。
        /// </summary>
        private static bool IsBetter(in SDS_EndpointDecision candidate, in SDS_EndpointDecision current)
        {
            if (!candidate.allowed)
            {
                return false;
            }
            if (!current.allowed || current.port == null)
            {
                return true;
            }
            if (candidate.priority != current.priority)
            {
                return candidate.priority > current.priority;
            }
            if (candidate.distanceSquared != current.distanceSquared)
            {
                return candidate.distanceSquared < current.distanceSquared;
            }
            int candidateId = candidate.port.Thing != null ? candidate.port.Thing.thingIDNumber : int.MaxValue;
            int currentId = current.port.Thing != null ? current.port.Thing.thingIDNumber : int.MaxValue;
            return candidateId < currentId;
        }
    }
}
