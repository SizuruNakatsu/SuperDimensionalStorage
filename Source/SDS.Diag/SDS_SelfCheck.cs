using System;
using System.Collections.Generic;
using LudeonTK;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// SDS_SelfCheck 的唯一实现（internal）：收集各模块 ISDS_DiagnosticProvider 并分级落日志。
    /// 公开面在 SDS.Diag_API.cs 的 SDS_SelfCheck 门面（含 [DebugAction] 开发者菜单入口），
    /// 本文件不得出现 public。
    /// </summary>
    internal static class SDS_SelfCheckImpl
    {
        private static readonly List<ISDS_DiagnosticProvider> providers = new List<ISDS_DiagnosticProvider>();

        internal static int Count
        {
            get { return providers.Count; }
        }

        internal static void Register(ISDS_DiagnosticProvider provider)
        {
            if (provider == null)
            {
                return;
            }
            for (int i = 0; i < providers.Count; i++)
            {
                if (ReferenceEquals(providers[i], provider))
                {
                    return;
                }
            }
            providers.Add(provider);
        }

        internal static void Reset()
        {
            providers.Clear();
        }

        /// <summary>
        /// 收集全部 provider 并统一输出。verbose=false 时只输出失败行与统计行；
        /// E2 生效状态始终输出（启动 / 读档诊断的一部分）。
        /// </summary>
        internal static void Run(bool verbose)
        {
            List<string> lines = Collect();
            int failures = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsFailure(lines[i]))
                {
                    failures++;
                }
            }

            SDS_DiagImpl.EmitInfo(SDS_Invariants.StatusText);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                if (IsFailure(line))
                {
                    SDS_DiagImpl.EmitError("[超维存储][自检] " + line);
                }
                else if (IsStatistic(line))
                {
                    if (verbose || failures > 0)
                    {
                        SDS_DiagImpl.EmitInfo("[超维存储][自检] " + line);
                    }
                }
                else if (verbose)
                {
                    SDS_DiagImpl.EmitInfo("[超维存储][自检] " + line);
                }
            }

            if (failures > 0)
            {
                SDS_DiagImpl.EmitWarning("[超维存储][自检] 完成：失败 " + failures + " 项（逐条见上）。");
            }
            else if (verbose)
            {
                SDS_DiagImpl.EmitInfo("[超维存储][自检] 完成：失败 0 项。");
            }
        }

        /// <summary>进入存档时的 B 类报数：只输出 provider 标记为 "[载入]" 的行。</summary>
        internal static void RunLoadReport()
        {
            List<string> lines = Collect();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (!string.IsNullOrEmpty(line) && line.StartsWith("[载入]", StringComparison.Ordinal))
                {
                    SDS_DiagImpl.EmitInfo("[超维存储]" + line.Substring("[载入]".Length));
                }
            }
        }

        /// <summary>逐个收集报告；单个 provider 抛异常记 ErrorOnce 后继续。</summary>
        internal static List<string> Collect()
        {
            List<string> lines = new List<string>();
            ISDS_DiagnosticProvider[] snapshot = providers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ISDS_DiagnosticProvider provider = snapshot[i];
                string name = provider.Name;
                try
                {
                    provider.AppendReport(lines);
                }
                catch (Exception e)
                {
                    SDS_Diag.ErrorOnce("自检报告提供者执行失败（已跳过）：" + name, e);
                    lines.Add("[失败] [自检] " + name + "：报告失败");
                }
            }
            return lines;
        }

        private static bool IsFailure(string line)
        {
            return line != null && line.StartsWith("[失败]", StringComparison.Ordinal);
        }

        private static bool IsStatistic(string line)
        {
            return line != null && line.StartsWith("[统计]", StringComparison.Ordinal);
        }
    }
}
