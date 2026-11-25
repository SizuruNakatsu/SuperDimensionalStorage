using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 「额外保真事实」的纯注册表：第三方适配器只登记判断（该实物是否必须保留真身），
    /// 由 Core 的分流规则统一消费。探针不扣账、不派活、不建立第二条写路径。
    /// 注册项是进程级常量，不登记 SDS_StaticState。
    /// </summary>
    public static class SDS_PreserveHooks
    {
        private static readonly List<Func<Thing, bool>> probes = new List<Func<Thing, bool>>();

        public static int Count
        {
            get { return probes.Count; }
        }

        /// <summary>登记一个事实探针；空或重复返回 false。</summary>
        public static bool Register(Func<Thing, bool> probe)
        {
            if (probe == null || probes.Contains(probe))
            {
                return false;
            }
            probes.Add(probe);
            return true;
        }

        /// <summary>任一探针为真即要求保真；单个探针异常记一次后跳过。</summary>
        public static bool RequiresPreserved(Thing thing)
        {
            if (thing == null || probes.Count == 0)
            {
                return false;
            }
            Func<Thing, bool>[] snapshot = probes.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    if (snapshot[i] != null && snapshot[i](thing))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[超维存储] 保真事实探针执行失败（已跳过）：" + e, 0x5D5F31);
                }
            }
            return false;
        }

        /// <summary>清空登记（仅供测试与重载场景）。</summary>
        public static void Reset()
        {
            probes.Clear();
        }
    }
}
