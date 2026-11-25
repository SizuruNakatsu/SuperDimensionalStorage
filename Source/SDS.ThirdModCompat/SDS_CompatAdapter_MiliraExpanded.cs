using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 米莉拉扩展适配器入口（§9.1）：Apply 后登记一次静态清理（§4.3 Milira 1 项）。
    /// </summary>
    internal sealed class SDS_CompatAdapter_MiliraExpanded : ISDSCompatAdapter, ISDSResettable
    {
        private const string HarmonyId = "sizuru.sds.compat.miliraexpanded";
        private const string NameText = "米莉拉扩展-未来科技Milira Expanded - Future Technology";
        private const string PackageId = "XiyueYM.MiliraExpandedXY";
        private const string NetworkTypeName = "MiliraExpandedXY.Buildings.GameComponent_VirtualStorageNetwork";
        private const string DeviceTypeName = "MiliraExpandedXY.Buildings.CompVirtualProductionDevice";
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
                    return AccessTools.TypeByName(NetworkTypeName) != null
                        || AccessTools.TypeByName(DeviceTypeName) != null;
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
            SDS_ModCompat_MiliraExpanded.TryApply(new Harmony(HarmonyId));
            SDS_StaticState.Register(this);
        }

        public void ResetStatics()
        {
            SDS_ModCompat_MiliraExpanded.Reset();
        }
    }
}
