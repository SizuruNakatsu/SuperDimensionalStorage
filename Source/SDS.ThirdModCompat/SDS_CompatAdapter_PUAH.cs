using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Pick Up And Haul 适配器入口（§9.1）：目标未装不 Apply；Harmony ID 唯一；只登记挂点。
    /// </summary>
    internal sealed class SDS_CompatAdapter_PUAH : ISDSCompatAdapter
    {
        private const string HarmonyId = "sizuru.sds.compat.puah";
        private const string NameText = "Pick Up And Haul";
        private const string PackageId = "Mehni.PickUpAndHaul";
        private const string GiverTypeName = "PickUpAndHaul.WorkGiver_HaulToInventory";
        private const string UnloadCheckerTypeName = "PickUpAndHaul.PawnUnloadChecker";
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
                    return AccessTools.TypeByName(GiverTypeName) != null
                        || AccessTools.TypeByName(UnloadCheckerTypeName) != null;
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
            SDS_ModCompat_PUAH.TryApply(new Harmony(HarmonyId));
        }
    }
}
