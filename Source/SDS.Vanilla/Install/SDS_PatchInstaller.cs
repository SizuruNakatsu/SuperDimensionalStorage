using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Vanilla 补丁安装器（§10.2 第 3 步）：只扫描本程序集，逐类 try/catch。
    /// 同一进程只安装一次；Harmony ID 固定 sizuru.sds.vanilla。
    /// </summary>
    internal static class SDS_PatchInstallerImpl
    {
        internal const string HarmonyId = "sizuru.sds.vanilla";

        private static Harmony harmony;
        private static int applied;
        private static int declared;

        /// <summary>本程序集的 Harmony 实例；动态注册表 / 运行期枚举挂点共用。</summary>
        internal static Harmony HarmonyInstance
        {
            get { return harmony; }
        }

        /// <summary>启动报告用：实际安装成功的特性补丁类数。</summary>
        internal static int AppliedCount
        {
            get { return applied; }
        }

        /// <summary>启动报告用：本程序集声明的特性补丁类数。</summary>
        internal static int DeclaredCount
        {
            get { return declared; }
        }

        /// <summary>
        /// §10.2 第 6 步：Def 加载完成后重跑 W63 落点守卫（幂等）。
        /// 返回已生效的 JobOnThing / HasJobOnThing 挂点数。
        /// </summary>
        internal static int ApplyRuntimeHooks()
        {
            if (harmony != null)
            {
                SDS_CellOnlyWorkGiverPatches.Apply(harmony);
            }
            return SDS_CellOnlyWorkGiverPatches.JobOnThingCount
                + SDS_CellOnlyWorkGiverPatches.HasJobOnThingCount
                + (SDS_CellOnlyWorkGiverPatches.PlacementHookApplied ? 1 : 0);
        }

        /// <summary>返回实际安装成功的补丁类数；重复调用幂等。</summary>
        internal static int Install()
        {
            if (harmony != null)
            {
                return applied;
            }
            harmony = new Harmony(HarmonyId);
            Type[] types = AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly());
            int declaredLocal = 0;
            List<string> failed = null;
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                object[] attributes = type.GetCustomAttributes(typeof(HarmonyPatch), false);
                if (attributes == null || attributes.Length == 0)
                {
                    continue;
                }
                declaredLocal++;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    applied++;
                }
                catch (Exception e)
                {
                    if (failed == null)
                    {
                        failed = new List<string>();
                    }
                    failed.Add(type.Name);
                    SDS_Diag.Warn("Vanilla 补丁类安装失败：" + type.Name, null, e);
                }
            }
            declared = declaredLocal;
            Log.Message("[超维存储] Vanilla 补丁类安装完成：A/B = " + applied + "/" + declared + " 个。");
            if (failed != null)
            {
                Log.Warning("[超维存储][Vanilla 补丁失败] " + failed.Count + " 个："
                    + string.Join("，", failed.ToArray()));
            }
            // 两张动态注册表（拦截型守卫 / 投影 owner 桥）各自 try/catch，互不影响。
            SDS_MirrorGuardRegistry.Apply(harmony);
            SDS_ProjectionOwnerBridge.Apply(harmony);
            SDS_CellOnlyWorkGiverPatches.Apply(harmony);
            return applied;
        }
    }
}

