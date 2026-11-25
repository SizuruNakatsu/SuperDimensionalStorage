using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LudeonTK;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    // ============================================================================
    // SDS.Diag · 公开面（唯一 public 文件）
    // 规则：本程序集的 public 类型与成员只允许出现在本文件；其余文件全部 internal。
    // 本文件只做契约声明与一行转发，不承载实现；实现见各 Impl 文件。
    // ============================================================================

    /// <summary>运行期静态缓存的清理契约；由各模块在 RegisterAll 时登记。</summary>
    public interface ISDSResettable
    {
        void ResetStatics();
    }

    /// <summary>
    /// 唯一日志门面：异常去重（ErrorOnce）、行为回退节流（Fail / Warn）、上下文串（Ctx）。
    /// 实现位于 SDS_DiagImpl；调用方只允许使用本类型。
    /// </summary>
    public static class SDS_Diag
    {
        /// <summary>同一「消息 + 调用点 + 异常类型」只完整记录一次；第 10 / 100 / 1000 次补一条重复警告。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ErrorOnce(string message, Exception exception)
            => SDS_DiagImpl.ErrorOnce(message, exception);

        /// <summary>行为回退（Error 级）：只允许 Core / Vanilla 调用，Compat 不得调用。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Fail(string reason, string context = null, Exception exception = null)
            => SDS_DiagImpl.Fail(reason, context, exception);

        /// <summary>可自愈的异常情况（Warning 级）：每个 reason 在进程内只记一次。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Warn(string reason, string context = null, Exception exception = null)
            => SDS_DiagImpl.Warn(reason, context, exception);

        /// <summary>E2 开发期断言入口（仅 DEBUG + 开发者模式；实现在内部 SDS_Invariants）。</summary>
        [Conditional("DEBUG")]
        public static void AssertAvailableWithinTotal(SDS_StockKey key, long total, long available)
            => SDS_Invariants.AvailableWithinTotal(key, total, available);

        [Conditional("DEBUG")]
        public static void AssertTransactionStage(int transactionId, SDS_WithdrawEndState actual,
            SDS_WithdrawEndState expected, string stage)
            => SDS_Invariants.TransactionStage(transactionId, actual, expected, stage);

        [Conditional("DEBUG")]
        public static void AssertTransactionTerminal(int transactionId, SDS_WithdrawEndState actual,
            string stage)
            => SDS_Invariants.TransactionTerminal(transactionId, actual, stage);

        /// <summary>统一上下文串；只允许在失败分支调用。</summary>
        public static string Ctx(Map map = null, Pawn pawn = null, Thing thing = null,
            Job job = null, string extra = null)
            => SDS_DiagImpl.Ctx(map, pawn, thing, job, extra);
    }

    /// <summary>
    /// 运行期静态状态的唯一清理入口：由 SDS_GameComponent_Grid 构造时调用一次。
    /// 只清进程级静态缓存，不清当前存档的账本 / 状态实例。实现位于 SDS_StaticStateImpl。
    /// </summary>
    public static class SDS_StaticState
    {
        /// <summary>已登记项数（启动日志的"登记 N"）。</summary>
        public static int RegisteredCount
            => SDS_StaticStateImpl.RegisteredCount;

        /// <summary>登记清理项；空值与重复实例忽略。</summary>
        public static void Register(ISDSResettable item)
            => SDS_StaticStateImpl.Register(item);

        /// <summary>
        /// 逐项清理：单项抛异常记 ErrorOnce 后继续，返回实际清理成功的项数。
        /// 登记表不随清理清空（重新登记只发生在下一次启动）。
        /// </summary>
        public static int ResetAll()
            => SDS_StaticStateImpl.ResetAll();
    }

    /// <summary>
    /// 自检入口与报告出口（§4.1：自检入口、日志门面统一归 Diag）。
    /// 各模块只把检查结果登记为 ISDS_DiagnosticProvider；Diag 负责收集、分级与落日志。
    /// 约定：provider 用 "[失败]" 前缀标记失败行，用 "[统计]" 前缀标记统计行；
    /// 其余行按普通信息输出。自检只读，不修复、不抛异常。实现位于 SDS_SelfCheckImpl。
    /// </summary>
    public static class SDS_SelfCheck
    {
        public static int Count
            => SDS_SelfCheckImpl.Count;

        public static void Register(ISDS_DiagnosticProvider provider)
            => SDS_SelfCheckImpl.Register(provider);

        public static void Reset()
            => SDS_SelfCheckImpl.Reset();

        /// <summary>开发者菜单手动入口。</summary>
        [DebugAction("超维存储", "自检：账本 / 预留 / 镜像")]
        public static void RunLedgerChecks()
            => SDS_SelfCheckImpl.Run(true);

        /// <summary>
        /// 收集全部 provider 并统一输出。verbose=false 时只输出失败行与统计行；
        /// E2 生效状态始终输出（启动 / 读档诊断的一部分）。
        /// </summary>
        public static void Run(bool verbose)
            => SDS_SelfCheckImpl.Run(verbose);

        /// <summary>进入存档时的 B 类报数：只输出 provider 标记为 "[载入]" 的行。</summary>
        public static void RunLoadReport()
            => SDS_SelfCheckImpl.RunLoadReport();

        /// <summary>逐个收集报告；单个 provider 抛异常记 ErrorOnce 后继续。</summary>
        internal static List<string> Collect()
            => SDS_SelfCheckImpl.Collect();
    }

    /// <summary>
    /// 单个第三方适配器的一次性补丁报告；只做汇总，不参与补丁逻辑。
    /// 纯数据：public 字段 / 属性只含 string / int / bool / List&lt;string&gt;。
    /// </summary>
    public sealed class SDS_CompatPatchReport
    {
        public string modName;
        public string category;
        public List<string> applied = new List<string>();
        public List<string> failed = new List<string>();

        public SDS_CompatPatchReport(string modName)
            : this(modName, "第三方 Mod 兼容补丁")
        {
        }

        public SDS_CompatPatchReport(string modName, string category)
        {
            this.modName = modName;
            this.category = string.IsNullOrEmpty(category) ? "第三方 Mod 兼容补丁" : category;
        }

        public int AppliedCount
        {
            get { return applied.Count; }
        }

        public int DeclaredCount
        {
            get { return applied.Count + failed.Count; }
        }

        public bool HasFailures
        {
            get { return failed.Count > 0; }
        }

        /// <summary>挂点已应用；hook 用「类型.成员（角色）」的可读形式。</summary>
        public void Applied(string hook)
            => SDS_CompatReportHubImpl.AddApplied(applied, hook);

        /// <summary>挂点缺失 / 失效：类型或方法找不到、签名不匹配、应用异常。</summary>
        public void Missing(string hook, string reason)
            => SDS_CompatReportHubImpl.AddMissing(failed, hook, reason);
    }

    /// <summary>
    /// 单个适配器的汇总快照：检测状态 + 挂点计数 + 失败 / 已生效名单。
    /// 纯数据：public 字段只含 string / int / bool / List&lt;string&gt;。
    /// </summary>
    public sealed class SDS_CompatAdapterReport
    {
        public string name;
        public string packageId;
        public string category;
        public bool detected;
        public int appliedCount;
        public int declaredCount;
        public bool hasFailures;
        public List<string> applied = new List<string>();
        public List<string> failed = new List<string>();
        public string remark;
        public string detectedButBroken;

        public SDS_CompatAdapterReport()
        {
        }

        public SDS_CompatAdapterReport(SDS_CompatAdapterReport other)
        {
            name = other.name;
            packageId = other.packageId;
            category = other.category;
            detected = other.detected;
            appliedCount = other.appliedCount;
            declaredCount = other.declaredCount;
            hasFailures = other.hasFailures;
            applied = new List<string>(other.applied);
            failed = new List<string>(other.failed);
            remark = other.remark;
            detectedButBroken = other.detectedButBroken;
        }
    }

    /// <summary>
    /// 适配器报告的唯一出口：Bootstrap 只从这里取快照，不引用 Compat.Sdk。
    /// 只在主线程写；读时用 Snapshot() 的副本，不持有内部列表引用。实现位于 SDS_CompatReportHubImpl。
    /// </summary>
    public static class SDS_CompatReportHub
    {
        public static void Reset()
            => SDS_CompatReportHubImpl.Reset();

        public static bool Has(string name)
            => SDS_CompatReportHubImpl.Has(name);

        public static void Register(string name, string packageId, bool detected,
            SDS_CompatPatchReport report, string remark = null, string detectedButBroken = null)
            => SDS_CompatReportHubImpl.Register(name, packageId, detected, report, remark, detectedButBroken);

        /// <summary>深拷贝快照；调用方改副本不影响 hub。</summary>
        public static SDS_CompatAdapterReport[] Snapshot()
            => SDS_CompatReportHubImpl.Snapshot();

        /// <summary>启动报告：只输出已检测到的适配器；仅对有失效挂点者输出详细 Warning。</summary>
        public static void LogAll()
            => SDS_CompatReportHubImpl.LogAll();
    }
}
