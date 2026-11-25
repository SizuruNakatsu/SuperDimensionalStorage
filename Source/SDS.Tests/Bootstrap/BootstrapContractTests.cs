using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 契约级自测：Bootstrap 只暴露 SDS_Mod、不装 Harmony 补丁、不静态引用 Compat；
    /// Vanilla 侧的 Bootstrap 门面在无游戏环境下可安全调用。
    /// 启动日志、设置页读写与游戏内 A/B 不在这里覆盖。
    /// </summary>
    public class BootstrapContractTests
    {
        private static Assembly BootstrapAssembly
        {
            get { return typeof(SDS_Mod).Assembly; }
        }

        [Fact]
        public void Bootstrap_ExposesOnlySDSMod()
        {
            int publicTypes = 0;
            foreach (Type type in BootstrapAssembly.GetTypes())
            {
                if (type.IsPublic && !type.IsNested)
                {
                    publicTypes++;
                    Assert.Equal("SDS_Mod", type.Name);
                }
            }
            Assert.Equal(1, publicTypes);
        }

        [Fact]
        public void SDSMod_IsRimWorldModEntry()
        {
            Assert.True(typeof(Mod).IsAssignableFrom(typeof(SDS_Mod)));
            Assert.NotNull(typeof(SDS_Mod).GetConstructor(new[] { typeof(ModContentPack) }));
            Assert.NotNull(typeof(SDS_Mod).GetMethod("DoSettingsWindowContents",
                BindingFlags.Instance | BindingFlags.Public));
        }

        [Fact]
        public void Bootstrap_DeclaresNoHarmonyPatchClasses()
        {
            int declared = 0;
            foreach (Type type in BootstrapAssembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                {
                    declared++;
                }
            }
            Assert.Equal(0, declared);
        }

        [Fact]
        public void Bootstrap_ReferencesOnlyAllowedSdsAssemblies()
        {
            string[] allowed =
            {
                "SDS.Contracts", "SDS.Diag", "SDS.Core", "SDS.Rules", "SDS.Api", "SDS.Vanilla"
            };
            foreach (AssemblyName reference in BootstrapAssembly.GetReferencedAssemblies())
            {
                if (reference.Name == null || !reference.Name.StartsWith("SDS.", StringComparison.Ordinal))
                {
                    continue;
                }
                Assert.Contains(reference.Name, allowed);
            }
        }

        [Fact]
        public void Vanilla_PatchInstaller_ReportsCounters()
        {
            Assert.True(SDS_Vanilla.PatchAppliedCount >= 0);
            Assert.True(SDS_Vanilla.PatchDeclaredCount >= 0);
            Assert.Equal("sizuru.sds.vanilla", SDS_PatchInstallerImpl.HarmonyId);
        }

        [Fact]
        public void Vanilla_HotkeyFacade_SafeWithoutGame()
        {
            try
            {
                SDS_Vanilla.CancelHotkeyCapture();
                Assert.False(SDS_Vanilla.HotkeyCapturing);
                Assert.False(string.IsNullOrEmpty(SDS_Vanilla.GetHotkeyLabel(SDS_Vanilla.HotkeyActionOpen)));
                Assert.False(string.IsNullOrEmpty(SDS_Vanilla.GetHotkeyLabel(SDS_Vanilla.HotkeyActionOutput)));
                Assert.False(string.IsNullOrEmpty(SDS_Vanilla.GetHotkeyLabel(SDS_Vanilla.HotkeyActionUse)));
                SDS_Vanilla.BeginHotkeyCapture(SDS_Vanilla.HotkeyActionOpen);
                SDS_Vanilla.CancelHotkeyCapture();
            }
            finally
            {
                SDS_Vanilla.CancelHotkeyCapture();
            }
        }
    }
}
