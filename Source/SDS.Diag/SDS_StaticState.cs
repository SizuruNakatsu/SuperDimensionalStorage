using System;
using System.Collections.Generic;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// SDS_StaticState 的唯一实现（internal）：运行期静态状态的清理登记表。
    /// 公开面在 SDS.Diag_API.cs 的 SDS_StaticState 门面，本文件不得出现 public。
    /// </summary>
    internal static class SDS_StaticStateImpl
    {
        private static readonly List<ISDSResettable> items = new List<ISDSResettable>();

        /// <summary>已登记项数（启动日志的"登记 N"）。</summary>
        internal static int RegisteredCount
        {
            get { return items.Count; }
        }

        /// <summary>登记清理项；空值与重复实例忽略。</summary>
        internal static void Register(ISDSResettable item)
        {
            if (item == null)
            {
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], item))
                {
                    return;
                }
            }
            items.Add(item);
        }

        /// <summary>
        /// 逐项清理：单项抛异常记 ErrorOnce 后继续，返回实际清理成功的项数。
        /// 登记表不随清理清空（重新登记只发生在下一次启动）。
        /// </summary>
        internal static int ResetAll()
        {
            int registered = items.Count;
            int cleaned = 0;
            int failed = 0;
            ISDSResettable[] snapshot = items.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].ResetStatics();
                    cleaned++;
                }
                catch (Exception e)
                {
                    failed++;
                    SDS_Diag.ErrorOnce("运行期静态状态清理失败（已跳过）", e);
                }
            }
            SDS_DiagImpl.EmitInfo("[超维存储][状态] 登记 " + registered + " / 清理 " + cleaned + " / 失败 " + failed);
            return cleaned;
        }
    }
}
