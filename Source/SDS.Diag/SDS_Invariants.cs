using System.Diagnostics;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 开发期不变量断言（规则 E2）：按 §4.1 归 SDS.Diag。
    /// 生效条件：DEBUG 构建 + 开发者模式；Release 构建由 <see cref="ConditionalAttribute"/>
    /// 把全部调用点剥离。只记 Error，不抛异常。
    /// 签名只依赖 Contracts 的基础类型（int / SDS_WithdrawEndState / SDS_StockKey），
    /// 因此 Core 可以调用，而 Diag 不反向引用 Core。
    /// 断言口径以《诊断覆盖清单》§M 为唯一权威，本文件只做实现，不复述规则。
    /// </summary>
    internal static class SDS_Invariants
    {
        /// <summary>启动/自检摘要（E2 的验收口径）：报告当前构建与开发者模式下的生效状态。</summary>
        internal static string StatusText
        {
            get
            {
#if DEBUG
                return Prefs.DevMode
                    ? "[E2] 开发期断言：生效（DEBUG 构建 + 开发者模式）。"
                    : "[E2] 开发期断言：未生效（DEBUG 构建，开发者模式关闭）。";
#else
                return "[E2] 开发期断言：未编译（Release 构建）。";
#endif
            }
        }

        /// <summary>可用量必须落在 [0, Total]；不断言 Total − Reserved（外部销毁后 Reserved &gt; Total 合法）。</summary>
        [Conditional("DEBUG")]
        internal static void AvailableWithinTotal(SDS_StockKey key, long total, long available)
        {
            if (!Prefs.DevMode || key == null)
            {
                return;
            }
            if (available < 0L || available > total)
            {
                EmitError("[超维存储][E2][不变量] 可用量越界：def=" + key.EffectiveDefName
                    + " preserved=" + key.preserved
                    + " total=" + total + " available=" + available);
            }
        }

        /// <summary>事务进入某个阶段前，必须处于预期状态（重复 Execute / 未预留就扣账）。</summary>
        [Conditional("DEBUG")]
        internal static void TransactionStage(int transactionId, SDS_WithdrawEndState actual,
            SDS_WithdrawEndState expected, string stage)
        {
            if (!Prefs.DevMode)
            {
                return;
            }
            if (actual != expected)
            {
                EmitError("[超维存储][E2][不变量] 事务阶段错误：" + stage
                    + " id=" + transactionId
                    + " 期望=" + expected + " 实际=" + actual);
            }
        }

        /// <summary>事务已终结（Delivered / RolledBack）后不得再次推进。</summary>
        [Conditional("DEBUG")]
        internal static void TransactionTerminal(int transactionId, SDS_WithdrawEndState actual, string stage)
        {
            if (!Prefs.DevMode)
            {
                return;
            }
            if (actual == SDS_WithdrawEndState.Delivered
                || actual == SDS_WithdrawEndState.RolledBack)
            {
                EmitError("[超维存储][E2][不变量] 事务已终结仍被推进：" + stage
                    + " id=" + transactionId + " state=" + actual);
            }
        }

        /// <summary>E2 的 Error 走 Diag 统一落点；Diag 内部无宿主时自行吞掉写入失败。</summary>
        private static void EmitError(string line)
        {
            SDS_DiagImpl.EmitError(line);
        }
    }
}
