using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 植入体端口：芯片识别、机械体继承、可用性判定与等级缓存。
    /// 只认本 Mod 的两枚芯片 hediff；A 要求本图有可用核心，B 只要求全局可用。
    /// 入 / 出：入 Pawn（或 Pawn + 芯片），出芯片 def / 端口是否可用；不写账本。
    /// 区别：SDS_StructureRegistry 管建筑事实；本类把「芯片 + 核心 + 阵营 + 状态」折算成端口可用性。
    /// </summary>
    internal static class SDS_ImplantUtilityImpl
    {
        private sealed class ChipCacheEntry
        {
            public HediffDef chip;
            public long sig;
        }

        private static readonly Dictionary<Pawn, ChipCacheEntry> chipCache =
            new Dictionary<Pawn, ChipCacheEntry>();

        private const int ChipCachePruneThreshold = 512;

        /// <summary>
        /// 芯片状态变化后清缓存（安装 / 更换 / 进档）。
        /// 入：无。
        /// 出：无。
        /// 区别：与 ResetChipCache 等价；本方法是模块面入口，后者只给 SDS_StaticState 清理制用。
        /// </summary>
        public static void NotifyChipStateChanged()
        {
            ResetChipCache();
        }

        /// <summary>
        /// 清空芯片等级缓存（E1 注册的静态清理项）。
        /// 入：无。
        /// 出：无。
        /// 区别：调 NotifyChipStateChanged 也会到这里；本方法不对外承诺语义。
        /// </summary>
        internal static void ResetChipCache()
        {
            chipCache.Clear();
        }

        /// <summary>
        /// 缓存超过阈值时清掉已销毁 / 不在图上的 Pawn 条目。
        /// 入：无。
        /// 出：无。
        /// 区别：只做容量保护，不影响任何判定结果（缓存未命中会重算）。
        /// </summary>
        private static void PruneChipCacheIfNeeded()
        {
            if (chipCache.Count < ChipCachePruneThreshold)
            {
                return;
            }
            List<Pawn> stale = new List<Pawn>();
            foreach (KeyValuePair<Pawn, ChipCacheEntry> pair in chipCache)
            {
                Pawn pawn = pair.Key;
                if (pawn.Destroyed || !pawn.Spawned)
                {
                    stale.Add(pawn);
                }
            }
            for (int i = 0; i < stale.Count; i++)
            {
                chipCache.Remove(stale[i]);
            }
        }

        private static bool brainDefsResolved;
        private static BodyPartDef normalBrain;
        private static BodyPartDef mechBrain;

        /// <summary>
        /// 一次性解析脑部零件 def（进程级常量）。
        /// 入：无。
        /// 出：无；结果写入 normalBrain / mechBrain。
        /// 区别：ResolveBrainPart 每次按 Pawn 查实际零件；本方法只缓存 def 查询结果。
        /// </summary>
        private static void ResolveBrainDefs()
        {
            if (brainDefsResolved)
            {
                return;
            }
            brainDefsResolved = true;
            normalBrain = DefDatabase<BodyPartDef>.GetNamedSilentFail("Brain");
            mechBrain = DefDatabase<BodyPartDef>.GetNamedSilentFail("ArtificialBrain");
        }

        /// <summary>
        /// 取该 Pawn 的脑部零件（芯片必须绑到真实零件）。
        /// 入：pawn —— 目标小人 / 机械体。
        /// 出：脑部零件记录；无身体 / 无脑部返回 null（优先人类 Brain，其次机械 ArtificialBrain）。
        /// 区别：本方法只找零件，不判芯片是否存在；芯片判定在 CurrentChipHediffDef / EffectiveChipDef。
        /// </summary>
        public static BodyPartRecord ResolveBrainPart(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || pawn.RaceProps.body == null)
            {
                return null;
            }
            ResolveBrainDefs();
            if (normalBrain != null)
            {
                BodyPartRecord part = pawn.RaceProps.body.GetPartsWithDef(normalBrain).FirstOrFallback();
                if (part != null)
                {
                    return part;
                }
            }
            if (mechBrain != null)
            {
                BodyPartRecord part = pawn.RaceProps.body.GetPartsWithDef(mechBrain).FirstOrFallback();
                if (part != null)
                {
                    return part;
                }
            }
            return null;
        }

        /// <summary>
        /// 取该 Pawn 自身当前携带的芯片 def（不继承监工）。
        /// 入：pawn —— 目标小人 / 机械体。
        /// 出：ChipB 优先于 ChipA；都没有 / 健康为空返回 null。
        /// 区别：EffectiveChipDef 会再向机械体的监工继承，并带缓存；本方法是纯读原始状态。
        /// </summary>
        public static HediffDef CurrentChipHediffDef(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }
            HediffDef a = SDS_HediffDefOf.ChipA;
            HediffDef b = SDS_HediffDefOf.ChipB;
            if (a == null && b == null)
            {
                return null;
            }
            HediffDef foundA = null;
            HediffDef foundB = null;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff == null || hediff.def == null)
                {
                    continue;
                }
                if (a != null && hediff.def == a)
                {
                    foundA = a;
                }
                else if (b != null && hediff.def == b)
                {
                    foundB = b;
                }
            }
            return foundB ?? foundA;
        }

        /// <summary>
        /// 给定芯片在该 Pawn 上是否处于可用状态。
        /// 入：pawn —— 目标；chip —— 具体芯片 def。
        /// 出：是否可用；死亡 / 精神崩溃 / 未 spawn / 非玩家阵营 / 全局不可用一律 false。
        /// 口径：ChipB 只要求全局可用；ChipA 还要求本图有可用核心。
        /// 区别：EffectiveChipDef 只回答「戴的是哪枚」，本方法才回答「这枚现在能不能用」。
        /// </summary>
        public static bool IsChipOperational(Pawn pawn, HediffDef chip)
        {
            if (pawn == null || chip == null || pawn.Dead || pawn.InMentalState)
            {
                return false;
            }
            if (!pawn.Spawned || pawn.Map == null)
            {
                return false;
            }
            if (pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }
            if (!SDS_RulesImpl.Available)
            {
                return false;
            }
            if (chip == SDS_HediffDefOf.ChipB)
            {
                return true;
            }
            return SDS_RulesImpl.MapHasOperationalCore(pawn.Map);
        }

        /// <summary>
        /// 取机械体的监工（用于芯片继承）。
        /// 入：pawn —— 机械体。
        /// 出：监工 Pawn；无 Biotech / 非机械体 / 无监工返回 null。
        /// 区别：CurrentChipHediffDef 只看自己；本方法只找继承来源。
        /// </summary>
        private static Pawn OverseerOf(Pawn pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive || pawn.relations == null
                || pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid)
            {
                return null;
            }
            return pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Overseer);
        }

        /// <summary>
        /// 取该 Pawn 的有效芯片（自身优先，机械体可继承监工），带签名缓存。
        /// 入：pawn —— 目标小人 / 机械体。
        /// 出：有效芯片 def；都没有返回 null。缓存命中要求签名（自身 hediff 数 + 监工 id / hediff 数 / 死亡 / 销毁）一致。
        /// 区别：CurrentChipHediffDef 不继承、不缓存；IsPortOperational 会再用本结果判可用性。
        /// </summary>
        public static HediffDef EffectiveChipDef(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }
            PruneChipCacheIfNeeded();
            Pawn overseer = OverseerOf(pawn);
            long sig = ChipSignature(pawn, overseer);
            if (chipCache.TryGetValue(pawn, out ChipCacheEntry entry) && entry != null && entry.sig == sig)
            {
                return entry.chip;
            }

            HediffDef computed = CurrentChipHediffDef(pawn);
            if (computed == null && overseer != null && !overseer.Destroyed && !overseer.Dead
                && overseer.health != null)
            {
                computed = CurrentChipHediffDef(overseer);
            }
            if (entry == null)
            {
                entry = new ChipCacheEntry();
                chipCache[pawn] = entry;
            }
            entry.chip = computed;
            entry.sig = sig;
            return computed;
        }

        /// <summary>
        /// 计算缓存签名（自身 hediff 数 + 监工身份与状态）。
        /// 入：pawn —— 目标；overseer —— 监工（可空）。
        /// 出：签名值；只用于判断缓存是否失效。
        /// 区别：不参与游戏逻辑，也不是唯一 ID（理论上可能碰撞，但只影响缓存命中率）。
        /// </summary>
        private static long ChipSignature(Pawn pawn, Pawn overseer)
        {
            long h = pawn.health != null && pawn.health.hediffSet != null
                ? pawn.health.hediffSet.hediffs.Count
                : 0L;
            h = h * 1000003L + (overseer != null ? overseer.thingIDNumber : -1);
            long overseerHediffs = overseer != null && overseer.health != null
                && overseer.health.hediffSet != null ? overseer.health.hediffSet.hediffs.Count : 0L;
            h = h * 1000003L + overseerHediffs;
            h = h * 31L + (overseer != null && overseer.Dead ? 1 : 0);
            h = h * 31L + (overseer != null && overseer.Destroyed ? 2 : 0);
            return h;
        }

        /// <summary>
        /// 该 Pawn 的端口是否可用（聚合结论）。
        /// 入：pawn —— 目标小人 / 机械体。
        /// 出：有效芯片非空且该芯片当前可用。
        /// 区别：IsChipOperational 针对指定芯片；本方法先用 EffectiveChipDef 选芯片再判。
        /// </summary>
        public static bool IsPortOperational(Pawn pawn)
        {
            HediffDef chip = EffectiveChipDef(pawn);
            return chip != null && IsChipOperational(pawn, chip);
        }
    }
}
