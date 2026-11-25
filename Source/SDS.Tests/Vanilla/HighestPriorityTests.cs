using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>W55：最高优先级模式下「超维空间视为最优储存」的唯一判据。</summary>
    public class HighestPriorityTests
    {
        [Fact]
        public void Off_IsNeverGridBetter()
        {
            WithSpawned("W55Off", delegate (RulesTestEnv env, Map map, Thing thing)
            {
                Assert.False(SDS_Config.IgnorePriorityMode);
                Assert.False(SDS_DepositService.IsGridAlwaysBetter(thing));
            });
        }

        [Fact]
        public void On_DepositableSpawned_IsGridBetter()
        {
            WithSpawned("W55On", delegate (RulesTestEnv env, Map map, Thing thing)
            {
                SDS_Config.Current.highestStoragePriority = true;
                Assert.True(SDS_DepositService.IsGridAlwaysBetter(thing));
            });
        }

        [Fact]
        public void On_Mirror_IsNotGridBetter()
        {
            WithSpawned("W55Mirror", delegate (RulesTestEnv env, Map map, Thing thing)
            {
                SDS_Config.Current.highestStoragePriority = true;
                SDS_MirrorRegistry.Mark(thing);
                try
                {
                    Assert.False(SDS_DepositService.IsGridAlwaysBetter(thing));
                }
                finally
                {
                    SDS_MirrorRegistry.Unmark(thing);
                }
            });
        }

        [Fact]
        public void On_Unspawned_IsNotGridBetter()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            env.AddStructure(SDS_StructureKind.Core);
            env.Ports.LocalUsable = true;
            SDS_Config.Current.highestStoragePriority = true;
            Thing loose = CoreTestKit.Make(CoreTestKit.Def("W55Loose"), 1);
            Assert.False(loose.Spawned);
            Assert.False(SDS_DepositService.IsGridAlwaysBetter(loose));
        }

        [Fact]
        public void On_FilterRejects_IsNotGridBetter()
        {
            WithSpawned("W55Filtered", delegate (RulesTestEnv env, Map map, Thing thing)
            {
                SDS_Config.Current.highestStoragePriority = true;
                env.Filter.AllowsFn = _ => false;
                Assert.False(SDS_DepositService.IsGridAlwaysBetter(thing));
            });
        }

        [Fact]
        public void On_NoLocalEndpoint_IsNotGridBetter()
        {
            WithSpawned("W55NoEndpoint", delegate (RulesTestEnv env, Map map, Thing thing)
            {
                SDS_Config.Current.highestStoragePriority = true;
                env.Ports.LocalUsable = false;
                Assert.False(SDS_DepositService.IsGridAlwaysBetter(thing));
            });
        }

        [Fact]
        public void On_ProducedMarked_IsNotGridBetter()
        {
            WithSpawned("W55Produced", delegate (RulesTestEnv env, Map map, Thing thing)
            {
                SDS_Config.Current.highestStoragePriority = true;
                SDS_Rules.MarkProduced(thing);
                try
                {
                    Assert.False(SDS_DepositService.IsGridAlwaysBetter(thing));
                }
                finally
                {
                    SDS_Rules.UnmarkProduced(thing);
                }
            });
        }

        private static void WithSpawned(string defName,
            System.Action<RulesTestEnv, Map, Thing> action)
        {
            RulesTestEnv env = RulesTestEnv.Create();
            env.AddStructure(SDS_StructureKind.Core);
            env.Ports.LocalUsable = true;
            Map map = ApiTestKit.NewMap();
            ApiTestKit.RegisterMap(map);
            try
            {
                Thing thing = CoreTestKit.Make(CoreTestKit.Def(defName), 3);
                ApiTestKit.Spawn(thing, map);
                Assert.True(thing.Spawned);
                action(env, map, thing);
            }
            finally
            {
                ApiTestKit.ReleaseMap(map);
            }
        }
    }
}
