using System;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 第三方兼容闸门注册表：适配器只登记只读判定，原版接缝处统一消费。
    /// 与 <see cref="SDS_PreserveHooks"/> 同型：只回答事实，不扣账、不派活、不建立第二条写路径。
    /// 注册项是进程级常量，不登记 <c>SDS_StaticState</c>。
    /// </summary>
    public static class SDS_CompatGates
    {
        private static Func<Dialog_LoadTransporters, bool> transportLoadInjection;

        /// <summary>
        /// 登记「运输舱装载清单注入」闸门：返回 false 时不得注入展示物。
        /// 未登记默认放行；已有登记时重复登记返回 false（不覆盖先登记者）。
        /// </summary>
        public static bool RegisterTransportLoadInjection(Func<Dialog_LoadTransporters, bool> gate)
        {
            if (gate == null || transportLoadInjection != null)
            {
                return false;
            }
            transportLoadInjection = gate;
            return true;
        }

        /// <summary>
        /// 是否允许向该装载对话框注入展示物。未登记放行；判定异常按 fail-closed 拒绝，
        /// 与旧《补丁清单》EE 第 3 行的语义一致。
        /// </summary>
        public static bool AllowsTransportLoadInjection(Dialog_LoadTransporters dialog)
        {
            Func<Dialog_LoadTransporters, bool> gate = transportLoadInjection;
            if (gate == null)
            {
                return true;
            }
            try
            {
                return gate(dialog);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[超维存储] 兼容注入闸门执行失败（按拒绝处理）：" + e, 0x5D5F32);
                return false;
            }
        }

        /// <summary>清空登记（仅供测试与重载场景）。</summary>
        public static void Reset()
        {
            transportLoadInjection = null;
        }
    }
}
