using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>9 个第三方适配器的契约级自测（不需要目标 Mod，也不进入游戏）。</summary>
    public class CompatAdapterTests
    {
        private static readonly Type[] AdapterTypes =
        {
            typeof(SDS_CompatAdapter_PUAH),
            typeof(SDS_CompatAdapter_CE),
            typeof(SDS_CompatAdapter_UFRecipes),
            typeof(SDS_CompatAdapter_ManipulatorBeam),
            typeof(SDS_CompatAdapter_MiliraExpanded),
            typeof(SDS_CompatAdapter_CeleTechShuttle),
            typeof(SDS_CompatAdapter_EquivalentExchange),
            typeof(SDS_CompatAdapter_Versatran),
            typeof(SDS_CompatAdapter_MiliraGift)
        };

        private static readonly string[] AssemblyNames =
        {
            "SDS.ThirdModCompat"
        };

        private static readonly Dictionary<Type, string> ExpectedPackages = new Dictionary<Type, string>
        {
            { typeof(SDS_CompatAdapter_PUAH), "Mehni.PickUpAndHaul" },
            { typeof(SDS_CompatAdapter_CE), "CETeam.CombatExtended" },
            { typeof(SDS_CompatAdapter_UFRecipes), "KindSeal.UFLI" },
            { typeof(SDS_CompatAdapter_ManipulatorBeam), "natsuki.manipulatorbeam" },
            { typeof(SDS_CompatAdapter_MiliraExpanded), "XiyueYM.MiliraExpandedXY" },
            { typeof(SDS_CompatAdapter_CeleTechShuttle), "LongRange.CeleTech.ShuttleExtension" },
            { typeof(SDS_CompatAdapter_EquivalentExchange), "Natsuki.ProjectE" },
            { typeof(SDS_CompatAdapter_Versatran), "VersatranZeroOne.serval" },
            { typeof(SDS_CompatAdapter_MiliraGift), "FunnyFlandre.milira.theoneentwinedwithyou" }
        };

        private static readonly Dictionary<Type, string> ExpectedHarmonyIds = new Dictionary<Type, string>
        {
            { typeof(SDS_CompatAdapter_PUAH), "sizuru.sds.compat.puah" },
            { typeof(SDS_CompatAdapter_CE), "sizuru.sds.compat.ce" },
            { typeof(SDS_CompatAdapter_UFRecipes), "sizuru.sds.compat.ufrecipes" },
            { typeof(SDS_CompatAdapter_ManipulatorBeam), "sizuru.sds.compat.manipulatorbeam" },
            { typeof(SDS_CompatAdapter_MiliraExpanded), "sizuru.sds.compat.miliraexpanded" },
            { typeof(SDS_CompatAdapter_CeleTechShuttle), "sizuru.sds.compat.celetechshuttle" },
            { typeof(SDS_CompatAdapter_EquivalentExchange), "sizuru.sds.compat.equivalentexchange" },
            { typeof(SDS_CompatAdapter_Versatran), "sizuru.sds.compat.versatran" },
            { typeof(SDS_CompatAdapter_MiliraGift), "sizuru.sds.compat.miliragift" }
        };

        /// <summary>2026-11-16 起：单程序集 SDS.ThirdModCompat 恰好包含全部 9 个适配器，一个不多一个不少。</summary>
        [Fact]
        public void ThirdModCompatAssembly_ContainsExactlyTheNineAdapters()
        {
            for (int i = 0; i < AssemblyNames.Length; i++)
            {
                Assembly assembly = Assembly.Load(AssemblyNames[i]);
                Type[] types = assembly.GetTypes();
                HashSet<Type> found = new HashSet<Type>();
                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type != null && !type.IsAbstract && !type.IsInterface
                        && typeof(ISDSCompatAdapter).IsAssignableFrom(type))
                    {
                        found.Add(type);
                    }
                }
                Assert.Equal(AdapterTypes.Length, found.Count);
                for (int a = 0; a < AdapterTypes.Length; a++)
                {
                    Assert.Contains(AdapterTypes[a], found);
                }
            }
        }

        [Fact]
        public void AdapterIdentity_MatchesContract()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> packages = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < AdapterTypes.Length; i++)
            {
                ISDSCompatAdapter adapter = (ISDSCompatAdapter)Activator.CreateInstance(AdapterTypes[i]);
                Assert.NotNull(adapter);
                Assert.False(string.IsNullOrEmpty(adapter.Name));
                Assert.Equal(ExpectedPackages[AdapterTypes[i]], adapter.TargetPackageId);
                Assert.True(names.Add(adapter.Name));
                Assert.True(packages.Add(adapter.TargetPackageId));
            }
        }

        [Fact]
        public void HarmonyIds_AreUniqueAndMatchContract()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < AdapterTypes.Length; i++)
            {
                FieldInfo field = AdapterTypes[i].GetField("HarmonyId",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(field);
                string id = (string)field.GetRawConstantValue();
                Assert.Equal(ExpectedHarmonyIds[AdapterTypes[i]], id);
                Assert.True(ids.Add(id));
            }
        }

        [Fact]
        public void TargetsAbsent_AreNotApplied()
        {
            for (int i = 0; i < AdapterTypes.Length; i++)
            {
                ISDSCompatAdapter adapter = (ISDSCompatAdapter)Activator.CreateInstance(AdapterTypes[i]);
                Assert.False(adapter.IsTargetLoaded);
            }
        }

        [Fact]
        public void CeAndMilira_DeclareStaticCleanup()
        {
            Assert.True(typeof(ISDSResettable).IsAssignableFrom(typeof(SDS_CompatAdapter_CE)));
            Assert.True(typeof(ISDSResettable).IsAssignableFrom(typeof(SDS_CompatAdapter_MiliraExpanded)));
        }

        [Fact]
        public void CompatSources_DoNotCallDiagFail()
        {
            string root = FindSourceRoot();
            string[] files = Directory.GetFiles(root, "SDS_ModCompat_*.cs", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            for (int i = 0; i < files.Length; i++)
            {
                string text = File.ReadAllText(files[i]);
                Assert.DoesNotContain("SDS_Diag.Fail", text);
            }
        }

        [Fact]
        public void ApiBridge_ForwardsDisplayMirrorAndProducedFacts()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            SDS_FakeDisplayQuery display = new SDS_FakeDisplayQuery();
            SDS_FakeDepositJobs jobs = new SDS_FakeDepositJobs();
            SDS_Host.Display = display;
            SDS_Host.DepositJobs = jobs;

            Thing thing = CoreTestKit.Make(CoreTestKit.Def("CompatProbe"));
            SDS_StockKey key = new SDS_StockKey { thingDef = thing.def, preserved = false };
            display.Displays.Add(thing);
            display.Keys[thing] = key;

            Assert.True(SDS_API.IsDisplayThing(thing));
            Assert.True(SDS_API.TryGetDisplayKey(thing, out SDS_StockKey resolved));
            Assert.Same(key, resolved);

            env.MirrorQuery.Visible.Add(thing);
            Assert.True(SDS_API.IsMirror(thing));

            jobs.CandidateAllowed = true;
            Assert.True(SDS_API.TryTakeDepositCandidate(null, thing, false, out SDS_ApiResultCode code));
            Assert.Equal(SDS_ApiResultCode.Ok, code);
            Assert.True(SDS_API.IsManualDepositAvailable(null));

            SDS_Rules.MarkProduced(thing);
            Assert.True(SDS_API.IsProducedPendingConsumption(thing));
            SDS_Rules.UnmarkProduced(thing);
            Assert.False(SDS_API.IsProducedPendingConsumption(thing));
        }

        private static string FindSourceRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SDS.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new InvalidOperationException("找不到 SDS.sln 源码根");
        }
    }
}
