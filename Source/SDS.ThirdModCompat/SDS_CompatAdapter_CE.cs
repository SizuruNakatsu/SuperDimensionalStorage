using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Combat Extended 适配器入口（§9.1）：Apply 后登记一次静态清理（§4.3 CE 1 项），
    /// 并把「弹匣内有弹」登记为保真事实探针（W33；Core 统一消费，不建第二条写路径）。
    /// </summary>
    internal sealed class SDS_CompatAdapter_CE : ISDSCompatAdapter, ISDSResettable
    {
        private const string HarmonyId = "sizuru.sds.compat.ce";
        private const string NameText = "Combat Extended";
        private const string PackageId = "CETeam.CombatExtended";
        private const string AmmoUserTypeName = "CombatExtended.CompAmmoUser";
        private const string ReloadUtilityTypeName = "CombatExtended.CombatExtended.Jobs.Utils.JobGiverUtils_Reload";
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
                    return AccessTools.TypeByName(AmmoUserTypeName) != null
                        || AccessTools.TypeByName(ReloadUtilityTypeName) != null;
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
            SDS_ModCompat_CE.TryApply(new Harmony(HarmonyId));
            // W33：CE 弹匣内有弹的武器必须走保真；探针只回答事实，由 Core 的
            // SDS_StockRules.RequiresPreservedStorage 统一消费。
            SDS_PreserveHooks.Register(SDS_ModCompat_CE.HasLoadedAmmoState);
            SDS_StaticState.Register(this);
        }

        public void ResetStatics()
        {
            SDS_ModCompat_CE.Reset();
        }
    }
}
