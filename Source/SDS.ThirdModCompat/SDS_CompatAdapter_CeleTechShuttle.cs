using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>天工模块化穿梭机适配器入口（§9.1 / W50）：只补装载清单注入。</summary>
    internal sealed class SDS_CompatAdapter_CeleTechShuttle : ISDSCompatAdapter
    {
        private const string HarmonyId = "sizuru.sds.compat.celetechshuttle";
        private const string NameText = "天工模块化穿梭机 CeleTech Shuttle Extension";
        private const string PackageId = "LongRange.CeleTech.ShuttleExtension";
        private const string MarkerTypeName =
            "CeleTech.ShuttleExtension.ModularShuttle.Core.Building_ModularShuttle";
        private const string FilterTypeName =
            "CeleTech.ShuttleExtension.ModularShuttle.Cargo.CargoTransferFilter";
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
                    return AccessTools.TypeByName(MarkerTypeName) != null
                        || AccessTools.TypeByName(FilterTypeName) != null;
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
            SDS_ModCompat_CeleTechShuttle.TryApply(new Harmony(HarmonyId));
        }
    }
}
