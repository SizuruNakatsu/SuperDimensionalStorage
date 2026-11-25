using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 双通道供给桥（共享层）：保真 / 虚拟两条腿共用同一份 offer 生命周期。
    ///
    /// 背景：带键外状态的物品在保真通道（真身不 spawn），旧档虚拟条目与 Core+DLC 物品在虚拟通道，
    /// 而原版决策函数只枚举地图上的实物。两条腿都必须「决策时按需物化 1 件 → 落到访问点 →
    /// 冻结腐烂 → 原版自己的 Job 消费 → 超时按来源回库」。
    ///
    /// 归属：本文件在共享层，通道只体现为 <see cref="OfferEntry.preservedOrigin"/> 与回库通道参数。
    /// 保真候选筛选留在 <c>Preserve/SDS_PreservedOffer</c>，虚拟候选筛选在本文件；
    /// offer 表、落点、超时回收只有一份——拆成两份就是第二个生命周期（准则三）。
    /// </summary>
    internal static class SDS_OfferBridge
    {
        private sealed class OfferEntry
        {
            public int expireTick;
            public bool rotFrozen;
            /// <summary>true = 从保真通道取出（超时回保真）；false = 从虚拟通道取出（超时走统一 Return）。</summary>
            public bool preservedOrigin;
        }

        /// <summary>默认存活时长：约 1.7 游戏小时（足够走到访问点并完成一次取用）。</summary>
        private const int DefaultLifetimeTicks = 6000;

        /// <summary>同一 Pawn + 同一 def 的提供冷却，避免高频 think 反复物化 / 回收。</summary>
        private const int CooldownTicks = 600;

        private const int MaxEntries = 512;

        private static readonly Dictionary<Thing, OfferEntry> offers = new Dictionary<Thing, OfferEntry>();
        private static readonly Dictionary<(int pawnId, ThingDef def), int> cooldowns =
            new Dictionary<(int pawnId, ThingDef def), int>();

        /// <summary>超时回收的节流时钟（全模块唯一的 offer 清理登记点）。</summary>
        private static int lastSweepTick;

        /// <summary>新档 / 读档：清空运行期提供表（旧游戏对象不属于本局）。</summary>
        internal static void Reset()
        {
            offers.Clear();
            cooldowns.Clear();
            lastSweepTick = 0;
        }

        /// <summary>该实物是否是「本子系统物化出来的待消费 offer」（供 IsInAnyStorage 桥使用）。</summary>
        public static bool IsOffer(Thing thing)
        {
            if (thing == null || offers.Count == 0)
            {
                return false;
            }
            TickIfDue();
            return offers.ContainsKey(thing);
        }

        /// <summary>
        /// 从虚拟通道提供 1 件：覆盖「旧档虚拟条目 / Core+DLC 物品」。
        /// 只用于原版自动判定（武器补位等），与保真腿共用同一套落点 / 超时回滚。
        /// </summary>
        public static bool TryOfferVirtual(Pawn pawn, Map map, Predicate<ThingDef> defFilter,
            Func<ThingDef, float> score, int lifetimeTicks, out Thing offered)
        {
            offered = null;
            if (pawn == null || map == null || defFilter == null || !pawn.Spawned)
            {
                return false;
            }
            ISDS_LedgerRead ledger = SDS_Core.Ledger;
            if (ledger == null || !SDS_Rules.Available || !SDS_Exposure.MapExposesGrid(map))
            {
                return false;
            }
            if (!TryGetAccess(pawn, map, out SDS_AccessPoint access))
            {
                return false;
            }
            TickIfDue();

            SDS_StockKey best = null;
            float bestScore = float.MinValue;
            List<SDS_StockKey> keys = new List<SDS_StockKey>();
            ledger.CopyKeys(keys);
            for (int i = 0; i < keys.Count; i++)
            {
                SDS_StockKey key = keys[i];
                if (key == null || key.preserved || !key.Resolved || key.thingDef == null
                    || !defFilter(key.thingDef))
                {
                    continue;
                }
                if (ledger.IsForbidden(key) || SDS_Availability.GetAvailableCount(key) <= 0L
                    || ledger.GetReserved(key) > 0L)
                {
                    continue;
                }
                float value = score != null ? score(key.thingDef) : 0f;
                if (best == null || value > bestScore)
                {
                    best = key;
                    bestScore = value;
                }
            }
            if (best == null || InCooldown(pawn, best.thingDef))
            {
                return false;
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = best,
                    quantity = 1,
                    preserved = false
                },
                endpoint = null,
                mode = SDS_WithdrawMode.SpawnedTarget
            };
            if (!SDS_Rules.TryWithdraw(plan, pawn, out List<Thing> made)
                || made == null || made.Count == 0)
            {
                return false;
            }
            if (TrackPlaced(pawn, map, made[0], access, lifetimeTicks, false, out offered))
            {
                return true;
            }
            // 已并入地图上的同类堆（实物已销毁）时数量已守恒，无需回库。
            if (!made[0].Destroyed)
            {
                RollbackOrWarn(made[0], false, map, pawn, IntVec3.Invalid,
                    SDS_ReturnFallback.Ground, "保真桥/虚拟落点失败（实物已落地，未回库）");
            }
            return false;
        }

        /// <summary>唯一取货点：带芯片小人自身端口距离 0，实物直接落在脚下，不绕到建筑端点。</summary>
        internal static bool TryGetAccess(Pawn pawn, Map map, out SDS_AccessPoint access)
        {
            access = null;
            if (pawn == null || map == null || pawn.Map != map)
            {
                return false;
            }
            return SDS_AccessPointResolver.TryGetNearestReachable(pawn, out access)
                && access != null && access.cell.IsValid;
        }

        /// <summary>共用的落点 / 解禁 / offer 登记。失败时不回滚（由调用方按来源回库）。</summary>
        internal static bool TrackPlaced(Pawn pawn, Map map, Thing item, SDS_AccessPoint access,
            int lifetimeTicks, bool preservedOrigin, out Thing offered)
        {
            offered = null;
            if (item == null || item.Destroyed)
            {
                return false;
            }
            // Near 模式可能并入已有同类堆并销毁临时实例；必须拿到实际落点实例再登记。
            if (!SDS_PlacementUtility.TryPlaceOrSpawn(item, access.cell, map, out Thing placed)
                || placed == null || placed.Destroyed)
            {
                return false;
            }
            offered = placed;
            // 被原版落点合并进已有堆：这一件已并入玩家实物，不再单独跟踪 / 打标 / 解禁。
            if (!ReferenceEquals(placed, item))
            {
                SetCooldown(pawn, placed.def);
                return true;
            }
            SDS_Rules.MarkProduced(placed);
            // 原版自动穿衣 / 拾取都会跳过 forbidden 目标；物化出来的是"给小人用的实物"，
            // 必须在这里解禁（保真真身入库时可能保留着旧的 Forbidden 状态）。
            placed.SetForbidden(false);
            SetCooldown(pawn, placed.def);
            if (offers.Count >= MaxEntries)
            {
                // 安全阀：宁可少跟踪几件，也不能让表无限增长。
                return true;
            }
            OfferEntry entry = new OfferEntry
            {
                expireTick = Now + Math.Max(60, lifetimeTicks <= 0 ? DefaultLifetimeTicks : lifetimeTicks),
                rotFrozen = false,
                preservedOrigin = preservedOrigin
            };
            CompRottable rot = placed.TryGetComp<CompRottable>();
            if (rot != null && !rot.disabled)
            {
                // 等待原版消费期间冻结腐烂；领取 / 回库时恢复。
                rot.disabled = true;
                entry.rotFrozen = true;
            }
            offers[placed] = entry;
            return true;
        }

        /// <summary>
        /// 周期清理入口：由 <see cref="SDS_VanillaState.Tick"/> 每 tick 调用，内部按 60 tick 节流；
        /// 各提供入口也调用一次，保证没有 tick 驱动的测试 / 极端时序下仍能结案。
        /// </summary>
        internal static void TickIfDue()
        {
            if (offers.Count == 0)
            {
                return;
            }
            int now = Now;
            if (now - lastSweepTick < 60)
            {
                return;
            }
            lastSweepTick = now;
            Tick();
        }

        /// <summary>每 60 tick：清理 / 超时回库。</summary>
        public static void Tick()
        {
            if (offers.Count == 0 && cooldowns.Count == 0)
            {
                return;
            }
            int now = Now;
            List<Thing> remove = null;
            foreach (KeyValuePair<Thing, OfferEntry> pair in offers)
            {
                Thing thing = pair.Key;
                OfferEntry entry = pair.Value;
                if (thing == null || thing.Destroyed || thing.stackCount <= 0)
                {
                    (remove ?? (remove = new List<Thing>())).Add(thing);
                    continue;
                }
                // 已被取走 / 穿 / 装 / 进容器：视为已领取，不再回收。
                if (thing.holdingOwner != null)
                {
                    RestoreRot(thing, entry);
                    (remove ?? (remove = new List<Thing>())).Add(thing);
                    continue;
                }
                // 未 spawn 且无人持有（地图卸载 / 被 DeSpawn）：真身按来源回库，绝不凭空丢。
                if (!thing.Spawned)
                {
                    RestoreRot(thing, entry);
                    RollbackOrWarn(thing, entry.preservedOrigin, thing.Map, null, IntVec3.Invalid,
                        SDS_ReturnFallback.None, "保真桥/失图回库失败（实物已落地）");
                    (remove ?? (remove = new List<Thing>())).Add(thing);
                    continue;
                }
                if (now < entry.expireTick)
                {
                    continue;
                }
                RestoreRot(thing, entry);
                RollbackOrWarn(thing, entry.preservedOrigin, thing.Map, null, thing.Position,
                    SDS_ReturnFallback.Ground, "保真桥/超时回库失败（实物已落地）");
                (remove ?? (remove = new List<Thing>())).Add(thing);
            }
            if (remove != null)
            {
                for (int i = 0; i < remove.Count; i++)
                {
                    offers.Remove(remove[i]);
                }
            }
            PruneCooldowns(now);
        }

        /// <summary>
        /// 按来源回滚（唯一实现）：保真回保真通道，虚拟走统一 Return；
        /// 失败落地时记同一类诊断。`fallback` 只对虚拟来源有意义（保真没有 fallback 分支）。
        /// </summary>
        internal static void RollbackOrWarn(Thing thing, bool preservedOrigin, Map map, Pawn pawn,
            IntVec3 cell, SDS_ReturnFallback fallback, string warnReason)
        {
            if (thing == null)
            {
                return;
            }
            bool returned = SDS_Rules.ReturnThing(thing,
                preservedOrigin ? SDS_StockChannel.Preserved : SDS_StockChannel.Virtual,
                map, pawn, fallback);
            if (!returned)
            {
                SDS_Diag.Warn(warnReason, SDS_Diag.Ctx(map, pawn, thing, null,
                    "def=" + thing.def.defName));
            }
        }

        internal static bool InCooldown(Pawn pawn, ThingDef def)
        {
            if (cooldowns.TryGetValue(CooldownKey(pawn, def), out int until) && until > Now)
            {
                return true;
            }
            return false;
        }

        private static void RestoreRot(Thing thing, OfferEntry entry)
        {
            if (entry == null || !entry.rotFrozen || thing == null || thing.Destroyed)
            {
                return;
            }
            CompRottable rot = thing.TryGetComp<CompRottable>();
            if (rot != null)
            {
                rot.disabled = false;
            }
        }

        private static (int pawnId, ThingDef def) CooldownKey(Pawn pawn, ThingDef def)
        {
            // 值键：不再每次拼接字符串（运行期不落盘，类型引用在本局内稳定）。
            return (pawn != null ? pawn.thingIDNumber : 0, def);
        }

        private static int Now
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
        }

        private static void SetCooldown(Pawn pawn, ThingDef def)
        {
            cooldowns[CooldownKey(pawn, def)] = Now + CooldownTicks;
        }

        private static void PruneCooldowns(int now)
        {
            if (cooldowns.Count == 0)
            {
                return;
            }
            List<(int pawnId, ThingDef def)> stale = null;
            foreach (KeyValuePair<(int pawnId, ThingDef def), int> pair in cooldowns)
            {
                if (pair.Value <= now)
                {
                    (stale ?? (stale = new List<(int pawnId, ThingDef def)>())).Add(pair.Key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    cooldowns.Remove(stale[i]);
                }
            }
        }
    }
}
