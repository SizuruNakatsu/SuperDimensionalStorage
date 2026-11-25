using System;
using HarmonyLib;

namespace SuperDimensionalStorage
{
    /// <summary>ProjectE 等价交换适配器入口（§9.1）：只做瞬时转化的边界翻译与回充。</summary>
    internal sealed class SDS_CompatAdapter_EquivalentExchange : ISDSCompatAdapter
    {
        private const string HarmonyId = "sizuru.sds.compat.equivalentexchange";
        private const string NameText = "ProjectE 等价交换";
        private const string PackageId = "Natsuki.ProjectE";
        private const string TableTypeName = "EquivalentExchange.Building_TransmutationTable";
        private const string InterfaceTypeName = "EquivalentExchange.Building_EmcInterface";
        private const string UtilityTypeName = "EquivalentExchange.TransmutationUtility";
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
                    return AccessTools.TypeByName(TableTypeName) != null
                        || AccessTools.TypeByName(InterfaceTypeName) != null
                        || AccessTools.TypeByName(UtilityTypeName) != null;
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
            SDS_ModCompat_EquivalentExchange.TryApply(new Harmony(HarmonyId));
            // 装载清单注入闸门：EE 已装但瞬时转化挂点未生效时，拒绝向它的对话框注入展示物。
            // 判定实现留在本程序集；Vanilla 只经 Contracts 的 SDS_CompatGates 消费。
            SDS_CompatGates.RegisterTransportLoadInjection(SDS_ModCompat_EquivalentExchange.AllowInjection);
        }
    }
}
