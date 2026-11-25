using System.Collections.Generic;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// SDS_CompatReportHub 的唯一实现（internal）：适配器报告的登记与唯一输出出口。
    /// 公开面（含报告 DTO）在 SDS.Diag_API.cs 的 SDS_CompatReportHub 门面，本文件不得出现 public。
    /// 只在主线程写；读时用 Snapshot() 的副本，不持有内部列表引用。
    /// </summary>
    internal static class SDS_CompatReportHubImpl
    {
        private static readonly List<SDS_CompatAdapterReport> reports = new List<SDS_CompatAdapterReport>();

        /// <summary>报告 DTO 的写入规范化：空 hook 忽略（E9：判据不放在公开面文件里）。</summary>
        internal static void AddApplied(List<string> applied, string hook)
        {
            if (!string.IsNullOrEmpty(hook))
            {
                applied.Add(hook);
            }
        }

        /// <summary>缺失 / 失效挂点：空 hook 忽略；reason 为空时只记 hook。</summary>
        internal static void AddMissing(List<string> failed, string hook, string reason)
        {
            if (string.IsNullOrEmpty(hook))
            {
                return;
            }
            failed.Add(string.IsNullOrEmpty(reason) ? hook : hook + " → " + reason);
        }

        internal static void Reset()
        {
            reports.Clear();
        }

        internal static bool Has(string name)
        {
            for (int i = 0; i < reports.Count; i++)
            {
                if (reports[i] != null && reports[i].name == name)
                {
                    return true;
                }
            }
            return false;
        }

        internal static void Register(string name, string packageId, bool detected,
            SDS_CompatPatchReport report, string remark = null, string detectedButBroken = null)
        {
            SDS_CompatAdapterReport item = new SDS_CompatAdapterReport
            {
                name = name,
                packageId = packageId,
                detected = detected,
                remark = remark,
                detectedButBroken = detectedButBroken
            };
            // report == null 时 category 保持 null：LogAll 用它区分「未登记报告」与「空报告」。
            if (report != null)
            {
                item.category = report.category;
                item.appliedCount = report.AppliedCount;
                item.declaredCount = report.DeclaredCount;
                item.hasFailures = report.HasFailures;
                item.applied = new List<string>(report.applied);
                item.failed = new List<string>(report.failed);
            }
            reports.Add(item);
        }

        /// <summary>深拷贝快照；调用方改副本不影响 hub。</summary>
        internal static SDS_CompatAdapterReport[] Snapshot()
        {
            SDS_CompatAdapterReport[] copy = new SDS_CompatAdapterReport[reports.Count];
            for (int i = 0; i < reports.Count; i++)
            {
                copy[i] = new SDS_CompatAdapterReport(reports[i]);
            }
            return copy;
        }

        /// <summary>启动报告：只输出已检测到的适配器；仅对有失效挂点者输出详细 Warning。</summary>
        internal static void LogAll()
        {
            for (int i = 0; i < reports.Count; i++)
            {
                SDS_CompatAdapterReport item = reports[i];
                if (item == null || !item.detected)
                {
                    continue;
                }
                string label = string.IsNullOrEmpty(item.category)
                    ? item.name
                    : "【" + item.category + "】" + item.name;
                string tail = string.IsNullOrEmpty(item.remark) ? string.Empty : "（" + item.remark + "）";
                if (item.declaredCount == 0)
                {
                    if (string.IsNullOrEmpty(item.category))
                    {
                        // report == null：适配器被检测到但没来得及登记报告。
                        string reason = string.IsNullOrEmpty(item.detectedButBroken)
                            ? "关键成员缺失"
                            : item.detectedButBroken;
                        SDS_DiagImpl.EmitWarning("[超维存储] 适配器 · " + label
                            + "：已检测到，A/B = 0/0（" + reason + "）" + tail + "。");
                    }
                    else
                    {
                        // report != null 但 0 挂点：空报告不等于破损，保持中性输出。
                        SDS_DiagImpl.EmitInfo("[超维存储] 适配器 · " + label
                            + "：已检测到，A/B = 0/0 个挂点" + tail + "。");
                    }
                    continue;
                }
                SDS_DiagImpl.EmitInfo("[超维存储] 适配器 · " + label + "：已检测到，A/B = "
                    + item.appliedCount + "/" + item.declaredCount + " 个挂点" + tail + "。");
            }

            for (int i = 0; i < reports.Count; i++)
            {
                SDS_CompatAdapterReport item = reports[i];
                if (item == null || !item.detected || !item.hasFailures)
                {
                    continue;
                }
                string label = string.IsNullOrEmpty(item.category)
                    ? item.name
                    : "【" + item.category + "】" + item.name;
                string tail = string.IsNullOrEmpty(item.remark) ? string.Empty : "（" + item.remark + "）";
                SDS_DiagImpl.EmitWarning("[超维存储] " + label + "：A/B = "
                    + item.appliedCount + "/" + item.declaredCount + " 个挂点；失败 "
                    + item.failed.Count + " 个：" + string.Join("，", item.failed.ToArray())
                    + (item.applied.Count > 0 ? "；已生效：" + string.Join("，", item.applied.ToArray()) : string.Empty)
                    + tail + "。");
            }
        }
    }
}
