using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 宿主注入（§10.2 第 2 步 / §6.3）：槽位由 Vanilla 装配，Bootstrap 只做注入与计数。
    /// 每个实现都是无参构造、构造时不读存档，因此可在 SDS_GameComponent_Grid 之前完成。
    /// SDS_Host.Install 的调用方只允许本文件（CheckAssemblyBoundaries.ps1 第 6 节）。
    /// </summary>
    internal static class SDS_HostInstaller
    {
        private static bool installed;
        private static int count;

        internal static int Install()
        {
            if (installed)
            {
                return count;
            }
            installed = true;

            SDS_Host.Install(SDS_Vanilla.CreateHostSlots());
            // 组合根把第三方的「必须保真」事实注入 Core；Core 自身不引用任何模块。
            SDS_Rules.SetExternalPreserveProvider(SDS_PreserveHooks.RequiresPreserved);

            count = 0;
            if (SDS_Host.Ports != null) { count++; }
            if (SDS_Host.DepositFilter != null) { count++; }
            if (SDS_Host.Priority != null) { count++; }
            if (SDS_Host.Delivery != null) { count++; }
            if (SDS_Host.MirrorQuery != null) { count++; }
            if (SDS_Host.MirrorPhysical != null) { count++; }
            if (SDS_Host.MedicalCare != null) { count++; }
            if (SDS_Host.ReclaimWorld != null) { count++; }
            if (SDS_Host.Display != null) { count++; }
            if (SDS_Host.DepositJobs != null) { count++; }
            if (SDS_Host.TransportLoad != null) { count++; }
            return count;
        }
    }
}
