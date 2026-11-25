using System;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 启动顺序的唯一实现（《模块规则》九 七步）：
    /// 1 设置读取 / 钳制 → 2 宿主注入 → 3 Harmony 安装（只扫 Vanilla）→ 4 适配器扫描
    /// → 5 RegisterAll → 6 延迟应用（建筑 Def / W63 落点守卫 / 适配器复扫）→ 7 启动报告。
    /// 第 7 步之后由 SDS_GameComponent_Grid 构造接管存档壳（Vanilla），Bootstrap 不重复实现。
    /// </summary>
    internal static class SDS_Startup
    {
        private static bool started;
        private static bool deferredRan;
        private static int hostCount;

        internal static void Run(SDS_Settings settings)
        {
            if (started)
            {
                Log.Message("[超维存储][Bootstrap] 本进程已完成启动，跳过重复初始化。");
                return;
            }

            try
            {
                // 第 1 步：设置（Mod 构造时已 GetSettings + ClampAll + 写入 SDS_Config.Current）。
                Log.Message("[超维存储][Bootstrap][1/7] 设置读取与钳制：gridCapacity="
                    + (settings != null ? settings.gridCapacity : 0)
                    + " capacityPerArray=" + (settings != null ? settings.capacityPerArray : 0)
                    + " corePower=" + (settings != null ? settings.corePower : 0f)
                    + " arrayPower=" + (settings != null ? settings.arrayPower : 0f)
                    + " portPower=" + (settings != null ? settings.portPower : 0f) + "。");

                // 第 2 步：宿主注入（11 个接口全部来自 Vanilla，构造时不读存档）。
                hostCount = SDS_HostInstaller.Install();
                Log.Message("[超维存储][Bootstrap][2/7] 宿主注入：" + hostCount + "/11（Ports / DepositFilter / "
                    + "Priority / Delivery / MirrorQuery / MirrorPhysical / MedicalCare / ReclaimWorld / "
                    + "Display / DepositJobs / TransportLoad）。");

                // 第 3 步：Harmony 安装，只扫描 SDS.Vanilla 程序集。
                int applied = SDS_Vanilla.InstallPatches();
                Log.Message("[超维存储][Bootstrap][3/7] Harmony 安装：仅扫描 SDS.Vanilla 程序集；特性补丁类 A/B = "
                    + applied + "/" + SDS_Vanilla.PatchDeclaredCount + "。");

                // 第 4 步：适配器扫描（目标未装不调用 Apply；本阶段 0 个适配器属正常）。
                SDS_CompatScanResult firstScan = SDS_CompatLoader.Scan();
                Log.Message("[超维存储][Bootstrap][4/7] 适配器扫描：程序集 " + firstScan.assemblies
                    + " 个，发现适配器 " + firstScan.found + " 个，已应用 " + firstScan.applied
                    + " 个，目标未装 " + firstScan.notLoaded + " 个。");
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("Bootstrap 同步启动失败（已降级继续，延迟步骤仍会执行）", e);
            }

            // 第 5 步：模块注册（静态清理 + 自检 provider）。独立兜底：前面任一步失败也必须登记。
            try
            {
                SDS_Core.RegisterAll();
                SDS_Rules.RegisterAll();
                SDS_Vanilla.RegisterAll();
                Log.Message("[超维存储][Bootstrap][5/7] 模块注册：静态清理登记 "
                    + SDS_StaticState.RegisteredCount + " 项，自检 provider " + SDS_SelfCheck.Count + " 个。");
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("Bootstrap 模块注册失败（静态清理 / 自检可能不完整）", e);
            }

            started = true;
            LongEventHandler.ExecuteWhenFinished(RunDeferred);
        }

        private static void RunDeferred()
        {
            if (deferredRan)
            {
                return;
            }
            deferredRan = true;
            try
            {
                // 第 6 步：Def 已加载，写建筑 Def 耗电 / 重算场上电力 / W63 落点守卫。
                int powerDefs = SDS_SettingsApplier.ApplyPowerAndPriority(SDS_Mod.Settings);
                int runtimeHooks = SDS_Vanilla.ApplyRuntimeHooks();
                SDS_CompatScanResult rescan = SDS_CompatLoader.Scan();
                Log.Message("[超维存储][Bootstrap][6/7] 延迟应用：建筑 Def 耗电 " + powerDefs
                    + " 项，场上电力已重算；W63 落点守卫挂点 " + runtimeHooks
                    + " 个；适配器复扫新增 " + rescan.applied + " 个。");

                // 第 7 步：启动报告（适配器报告 / 自检 / A/B / 游戏内验证状态）。
                SDS_Vanilla.ReportUseProvidersOnce();
                LogReport();
            }
            catch (Exception e)
            {
                SDS_Diag.ErrorOnce("Bootstrap 延迟启动失败（已跳过后续应用，Mod 其余功能照常）", e);
            }
        }

        private static void LogReport()
        {
            SDS_CompatReportHub.LogAll();

            // 自检输出统一走 SDS.Diag 的门面（含 E2 状态与各 provider 的统计/失败行）。
            SDS_SelfCheck.Run(false);

            Log.Message("[超维存储][Bootstrap][7/7] 启动报告：Vanilla 补丁 A/B = "
                + SDS_Vanilla.PatchAppliedCount + "/" + SDS_Vanilla.PatchDeclaredCount
                + "；宿主 " + hostCount + "/11；适配器 " + SDS_CompatLoader.AppliedCount
                + "/" + SDS_CompatLoader.KnownCount + "；静态清理登记 " + SDS_StaticState.RegisteredCount
                + "；自检 provider " + SDS_SelfCheck.Count
                + "；读档 / 新档由 Vanilla 存档壳接管。游戏内 A/B 验证待跑。");
        }
    }
}
