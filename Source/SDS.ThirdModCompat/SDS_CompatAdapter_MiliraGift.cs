using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 边缘米莉拉养成：与你相连之人（FunnyFlandre.milira.theoneentwinedwithyou）适配器入口。
    /// 只登记身份与目标探测；行为在 SDS_ModCompat_MiliraGift + SDS_CompatMirrorSeam。
    /// </summary>
    internal sealed class SDS_CompatAdapter_MiliraGift : ISDSCompatAdapter
    {
        internal const string NameText = "边缘米莉拉养成：与你相连之人";
        internal const string PackageId = "FunnyFlandre.milira.theoneentwinedwithyou";
        internal const string DriverTypeName = "MiliraTheOneEntwinedWithYou.Gifts.JobDriver_IdealReceiveGift";
        internal const string CompTypeName = "MiliraTheOneEntwinedWithYou.Gifts.CompIdealGift";
        private const string HarmonyId = "sizuru.sds.compat.miliragift";
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
                    return AccessTools.TypeByName(DriverTypeName) != null
                        || AccessTools.TypeByName(CompTypeName) != null;
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
            SDS_ModCompat_MiliraGift.TryApply(new Harmony(HarmonyId));
        }
    }
}
