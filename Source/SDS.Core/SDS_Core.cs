using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    internal sealed class SDS_ResetAction : ISDSResettable
    {
        private readonly Action action;

        /// <summary>
        /// 包装一个静态清理动作，供 SDS_StaticState 统一登记。
        /// 入：action = 待执行清理，可 null（则 ResetStatics 空转）。
        /// 出：无。区别：与直接登记委托不同，本类让「清理动作」可被同一集合多态持有。
        /// </summary>
        public SDS_ResetAction(Action action)
        {
            this.action = action;
        }

        /// <summary>
        /// 执行登记的清理动作（新游戏 / 重置静态时由 SDS_StaticState 调用）。
        /// 入：无。出：无；action 为 null 时静默跳过。
        /// 区别：与一次性 Reset 方法的区别是它可被重复调用（清理应幂等）。
        /// </summary>
        public void ResetStatics()
        {
            if (action != null)
            {
                action();
            }
        }
    }

    /// <summary>
    /// Core 自检：只校验账本自身口径（虚拟可用量 / 保真真身失效 / 完整账本不变量）。
    /// 规则侧自检（W35、保真可用量）随判定层在 SDS.Rules 的 SDS_RulesDiagnostics。
    /// </summary>
    internal sealed class SDS_CoreDiagnostics : ISDS_DiagnosticProvider
    {
        /// <summary>诊断提供者名称（报告分组标题）。入：无。出：固定 "Core"。纯读。
        /// 区别：其他诊断提供者各自独立，名称只用于报告分段。</summary>
        public string Name
        {
            get { return "Core"; }
        }

        /// <summary>
        /// 自检报告：虚拟可用量一致性 + 保真真身失效 + 账本完整不变量。
        /// 入：lines = 输出目标，null 直接返回（不做任何运算）。
        /// 出：无；失败项以 "[失败] [Core]" 前缀写入，末尾追加统计行。账本缺失时只写一行说明并返回。
        /// 区别：与 AuditReservations 的区别是本类不检查预留归属存活，只校验账面 / 实物口径是否一致。
        /// </summary>
        public void AppendReport(List<string> lines)
        {
            if (lines == null)
            {
                return;
            }
            int checkedCount = 0;
            int failures = 0;
            SDS_Ledger ledger = SDS_CoreImpl.Ledger;
            if (ledger == null)
            {
                lines.Add("[Core] 账本未附加。");
                return;
            }
            List<SDS_StockEntry> snapshot = ledger.Snapshot(0);
            int bad = 0;
            int unresolved = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_StockKey key = snapshot[i].key;
                // 未解析键按规则「保留、供给归零」：可用量恒 0，不参与 total − reserved 比对。
                if (key == null || !key.Resolved)
                {
                    unresolved++;
                    continue;
                }
                long total = ledger.GetCount(key);
                long reserved = ledger.GetReserved(key);
                long expected = total - reserved;
                if (expected < 0L)
                {
                    expected = 0L;
                }
                checkedCount++;
                if (ledger.GetAvailable(key) != expected)
                {
                    bad++;
                    failures++;
                    lines.Add("[失败] [Core] 虚拟可用量不符：def=" + key.EffectiveDefName
                        + " total=" + total + " reserved=" + reserved + " expected=" + expected
                        + " actual=" + ledger.GetAvailable(key));
                }
            }
            if (unresolved > 0)
            {
                lines.Add("[统计] [Core] 未解析 def 条目 " + unresolved + " 条（供给归零，不参与比对）。");
            }
            lines.Add("[统计] [Core] 虚拟可用量检查 " + (snapshot.Count - unresolved) + " 项，异常 " + bad + " 项。");

            List<Thing> preserved = new List<Thing>();
            // 诊断必须看到 owner 里的全部真身（含已销毁 / 内容失效），
            // 否则「保真真身失效」永远统计为 0。
            ledger.AppendAllPreservedOwnerThings(preserved);
            int alive = 0;
            int dead = 0;
            for (int i = 0; i < preserved.Count; i++)
            {
                if (preserved[i] == null || preserved[i].Destroyed)
                {
                    dead++;
                }
                else
                {
                    alive++;
                }
            }
            checkedCount++;
            if (dead > 0)
            {
                failures++;
                lines.Add("[失败] [Core] 保真真身失效 " + dead + " 件（存活 " + alive + " 件）。");
            }
            else
            {
                lines.Add("[统计] [Core] 保真真身 " + alive + " 件，失效 0 件。");
            }

            checkedCount++;
            // 完整账本不变量：stock / vitals / 三个派生索引 / 预留 / 待恢复 / 存档版本。
            // 不依赖宿主事实；保真真身检查也包含在内。
            if (ledger.AppendLedgerInvariantReport(lines) > 0)
            {
                failures++;
            }

            lines.Add("[统计] Core：检查 " + checkedCount + " 项，失败 " + failures + " 项。");
        }
    }

    /// <summary>
    /// Core 唯一对外门面：只持有账本与存档生命周期，不产出玩法决定、不寻路、不写 UI、不打补丁。
    /// 判定层（通道 / 容量 / 可用量 / 结构 / 芯片）自 P4 起在 SDS.Rules。
    /// </summary>
    internal static class SDS_CoreImpl
    {
        private static SDS_Ledger ledger;
        private static bool registered;

        /// <summary>当前账本实例（可写类型）；未附加为 null。入：无。出：账本引用。纯读。
        /// 区别：对外只暴露 ISDS_LedgerRead，本属性仅供 Core 内部与 SDS.Rules 的友元侧使用。</summary>
        public static SDS_Ledger Ledger
        {
            get { return ledger; }
        }

        /// <summary>附加账本（唯一写入口，只由 SDS_Core.Attach 调用）。入：value = 账本，可 null（等价于未进档）。出：无。
        /// 区别：与 SDS_Core.Attach 的区别是此处已收窄为 SDS_Ledger，不再做类型转换。</summary>
        public static void Attach(SDS_Ledger value)
        {
            ledger = value;
        }

        /// <summary>已用品类数。入：无。出：账本已占品类数；无账本为 0。纯读。
        /// 区别：按品类键计数，不是件数；与 SDS_Rules.TypeCapacity 共同构成容量门。</summary>
        public static long UsedCapacity
        {
            get
            {
                SDS_Ledger current = ledger;
                return current != null ? current.UsedCategoryCount : 0L;
            }
        }

        /// <summary>切换品类禁用（唯一写入口）；状态真正变化时推进 StockVersion。</summary>
        /// <remarks>入：key = 品类键；forbidden = 目标状态。出：true = 实际变化。无账本返回 false。
        /// 区别：与 NotifyDepositFilterChanged 的区别是它真的改禁用集合。</remarks>
        public static bool SetForbidden(SDS_StockKey key, bool forbidden)
        {
            return ledger != null && ledger.SetForbidden(key, forbidden);
        }

        // ---------- 账本版本通知 ----------

        /// <summary>存入筛选变化：推进账本筛选版本。入：无。出：无；无账本时静默忽略。
        /// 区别：只推版本，不改禁用集合（改禁用走 SetForbidden）。</summary>
        public static void NotifyDepositFilterChanged()
        {
            if (ledger != null)
            {
                ledger.BumpFilterVersion();
            }
        }

        /// <summary>品类口径（是否按品质 / 材料分组）变化时调用：账本派生索引全部失效。</summary>
        /// <remarks>入：无。出：无；不改存量，下一次查询重建索引。无账本时忽略。
        /// 区别：与 NotifyDepositFilterChanged 的区别是它作用于「分组口径」而非「筛选」。</remarks>
        public static void NotifyCapacitySettingsChanged()
        {
            SDS_Ledger current = ledger;
            if (current != null)
            {
                current.InvalidateDerivedIndexes();
            }
        }

        // ---------- 预留终老 ----------

        /// <summary>
        /// 预留终老：账本只提供快照与「按归属 id 释放」；归属是否存活由调用方传入的委托判定。
        /// 读档后（Vanilla 的 FinalizeInit）与运行期周期巡检各调一次。
        /// id ≤ 0 一律非法（负数 = Instant 事务残留，0 = 旧档 / 写坏的归属）。
        /// </summary>
        /// <remarks>入：ownerAlive = 「该归属 id 是否仍存活」，可空（空 = 不删正 id）。
        /// 出：无；按归属 id 整组释放（同一 id 只释放一次）。
        /// 区别：本方法不认识 Job / Lord；判活事实全部由 Vanilla 注入。
        /// </remarks>
        public static void AuditReservations(Func<int, bool> ownerAlive)
        {
            SDS_Ledger current = ledger;
            if (current == null || current.ReservationCount == 0)
            {
                return;
            }
            List<SDS_Reservation> snapshot = new List<SDS_Reservation>(current.ReservationCount);
            current.CopyReservations(snapshot);
            HashSet<int> deadOwners = null;
            for (int i = 0; i < snapshot.Count; i++)
            {
                SDS_Reservation reservation = snapshot[i];
                if (reservation == null || reservation.key == null || reservation.count <= 0)
                {
                    continue; // 结构性非法条目由账本读档时自行清理
                }
                bool alive;
                if (reservation.jobLoadID <= 0)
                {
                    alive = false;
                }
                else if (reservation.pawn != null && reservation.pawn.Destroyed)
                {
                    alive = false;
                }
                else if (ownerAlive == null)
                {
                    // 调用方无判活事实（测试 / 未注入）：不凭猜测删预留。
                    alive = true;
                }
                else
                {
                    alive = ownerAlive(reservation.jobLoadID);
                }
                if (!alive)
                {
                    if (deadOwners == null)
                    {
                        deadOwners = new HashSet<int>();
                    }
                    deadOwners.Add(reservation.jobLoadID);
                }
            }
            if (deadOwners == null)
            {
                return;
            }
            foreach (int ownerId in deadOwners)
            {
                current.ReleaseReservation(ownerId);
            }
        }

        /// <summary>各模块初始化时调用一次：登记 Core 自检提供者（E1）；重复调用忽略。</summary>
        /// <remarks>入：无。出：无；幂等。
        /// 区别：静态清理项随判定层迁入 SDS.Rules，由 `SDS_Rules.RegisterAll` 登记。</remarks>
        public static void RegisterAll()
        {
            if (registered)
            {
                return;
            }
            registered = true;
            SDS_SelfCheck.Register(new SDS_CoreDiagnostics());
        }
    }
}
