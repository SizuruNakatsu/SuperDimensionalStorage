using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// VC载具框架 VersatranZeroOne 适配器入口（仅第三方适配层）：
    /// 车辆货舱的“装载”入口在适配器里翻译成 SDS_API 的取货事务；
    /// 车辆本身的容器 / Job / 容量规则不动。
    /// </summary>
    internal sealed class SDS_CompatAdapter_Versatran : ISDSCompatAdapter
    {
        private const string HarmonyId = "sizuru.sds.compat.versatran";
        private const string NameText = "VC载具框架 VersatranZeroOne";
        private const string PackageId = "VersatranZeroOne.serval";
        private const string MarkerTypeName = "VersatranZeroOne.Building_VersatranChassis";
        private static bool applied;

        public string Name
        {
            get { return NameText; }
        }

        public string TargetPackageId
        {
            get { return PackageId; }
        }

        public bool IsTargetLoaded
        {
            get
            {
                try
                {
                    return AccessTools.TypeByName(MarkerTypeName) != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public void Apply()
        {
            if (applied)
            {
                return;
            }
            applied = true;
            SDS_ModCompat_Versatran.TryApply(new Harmony(HarmonyId));
        }
    }
}
