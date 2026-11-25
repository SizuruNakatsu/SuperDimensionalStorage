using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>牵引光束搬运器适配器入口（§9.1）：只补目的地发现，投递走原版容器契约。</summary>
    internal sealed class SDS_CompatAdapter_ManipulatorBeam : ISDSCompatAdapter
    {
        private const string HarmonyId = "sizuru.sds.compat.manipulatorbeam";
        private const string NameText = "牵引光束搬运器Manipulator Beam Emitter";
        private const string PackageId = "natsuki.manipulatorbeam";
        private const string SettingsTypeName = "ManipulatorBeam.BeamFeatureSettings";
        private const string ScanCacheTypeName = "ManipulatorBeam.MB_BeamScanCacheMapComponent";
        private const string TransferUtilTypeName = "ManipulatorBeam.BeamSpecialTransferUtility";
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
                    return AccessTools.TypeByName(SettingsTypeName) != null
                        || AccessTools.TypeByName(ScanCacheTypeName) != null
                        || AccessTools.TypeByName(TransferUtilTypeName) != null;
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
            SDS_ModCompat_ManipulatorBeam.TryApply(new Harmony(HarmonyId));
        }
    }
}
