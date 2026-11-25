using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>联合轻工家具拓展适配器入口（§9.1）：只补全自动打印机的保真供料。</summary>
    internal sealed class SDS_CompatAdapter_UFRecipes : ISDSCompatAdapter
    {
        private const string HarmonyId = "sizuru.sds.compat.ufrecipes";
        private const string NameText = "联合轻工家具拓展-UF Light Industries Furniture";
        private const string PackageId = "KindSeal.UFLI";
        private const string AutoUtilTypeName = "UFRecipes.AutoSequenceUtil";
        private const string WorkbenchTypeName = "UFRecipes.Building_SuperWorkbench";
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
                    return AccessTools.TypeByName(AutoUtilTypeName) != null
                        || AccessTools.TypeByName(WorkbenchTypeName) != null;
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
            SDS_ModCompat_UFRecipes.TryApply(new Harmony(HarmonyId));
        }
    }
}
