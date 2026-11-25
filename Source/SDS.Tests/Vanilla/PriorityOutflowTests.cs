using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>W52：优先级流出与「最高优先级」的互斥与默认值。</summary>
    public class PriorityOutflowTests
    {
        [Fact]
        public void Defaults_BothModesOff()
        {
            WithSettings(new SDS_Settings(), delegate
            {
                Assert.False(SDS_Config.IgnorePriorityMode);
                Assert.False(SDS_Config.AllowPriorityOutflow);
            });
        }

        [Fact]
        public void OutflowOnly_IsEffective()
        {
            SDS_Settings settings = new SDS_Settings
            {
                allowPriorityOutflow = true,
                highestStoragePriority = false
            };
            settings.ClampAll();
            WithSettings(settings, delegate
            {
                Assert.True(SDS_Config.AllowPriorityOutflow);
                Assert.False(SDS_Config.IgnorePriorityMode);
            });
        }

        [Fact]
        public void BothOn_ClampKeepsHighestPriorityOnly()
        {
            SDS_Settings settings = new SDS_Settings
            {
                allowPriorityOutflow = true,
                highestStoragePriority = true
            };
            settings.ClampAll();
            Assert.False(settings.allowPriorityOutflow);
            WithSettings(settings, delegate
            {
                Assert.True(SDS_Config.IgnorePriorityMode);
                Assert.False(SDS_Config.AllowPriorityOutflow);
            });
        }

        [Fact]
        public void MirrorIsRejectedByTheSingleDepositGate()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            env.AddStructure(SDS_StructureKind.Core);
            var mirror = CoreTestKit.Make(CoreTestKit.Def("W53Mirror"), 3);
            var real = CoreTestKit.Make(CoreTestKit.Def("W53Real"), 1);
            SDS_MirrorRegistry.Mark(mirror);
            try
            {
                // W53：镜像代表账本本身，不得再作为“可存入实物”进入端点接收 / 存入竞争。
                Assert.False(SDS_EndpointUtility.IsDepositable(mirror));
                Assert.False(SDS_ChipManualDeposit.CanDepositThing(mirror));
                // 对照：普通实物仍按原口径可存入，确认闸门没有扩大拦截面。
                Assert.True(SDS_EndpointUtility.IsDepositable(real));
                Assert.True(SDS_ChipManualDeposit.CanDepositThing(real));
            }
            finally
            {
                SDS_MirrorRegistry.Unmark(mirror);
            }
        }

        private static void WithSettings(SDS_Settings settings, System.Action action)
        {
            SDS_Settings previous = SDS_Config.Current;
            try
            {
                SDS_Config.Current = settings;
                action();
            }
            finally
            {
                SDS_Config.Current = previous;
            }
        }
    }
}
