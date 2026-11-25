using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    public class EndpointTests
    {
        [Fact]
        public void Select_PrefersNearest_SelfIsNormalCandidate()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("EndpointA");
            SDS_EndpointQuery query = SDS_EndpointQuery.Deposit(CoreTestKit.Make(a), null);

            SDS_FakePort far = new SDS_FakePort
            {
                Thing = CoreTestKit.Make(CoreTestKit.Def("Far")),
                Distance = 100f,
                Operational = true
            };
            SDS_FakePort near = new SDS_FakePort
            {
                Thing = CoreTestKit.Make(CoreTestKit.Def("Near")),
                Distance = 4f,
                Operational = true
            };
            SDS_FakePort self = new SDS_FakePort
            {
                Thing = CoreTestKit.Make(CoreTestKit.Def("Self")),
                Distance = 8f,
                Operational = true,
                IsSelf = true
            };
            env.Ports.Candidates.Clear();
            env.Ports.Candidates.Add(far);
            env.Ports.Candidates.Add(near);
            env.Ports.Candidates.Add(self);

            // 自身端口按真实距离参与排序：更远时输给普通端点。
            Assert.Same(near, SDS_Rules.SelectEndpoint(query).port);

            // 自身端口更近时按距离胜出（证明仍参与排序）。
            self.Distance = 2f;
            Assert.Same(self, SDS_Rules.SelectEndpoint(query).port);

            query.allowSelf = false;
            Assert.Same(near, SDS_Rules.SelectEndpoint(query).port);
        }

        [Fact]
        public void NotifyEndpointChanged_AdvancesContractVersion()
        {
            int before = SDS_Rules.EndpointVersion;
            SDS_Rules.NotifyEndpointChanged();
            Assert.True(SDS_Rules.EndpointVersion > before);
        }

        [Fact]
        public void Select_RejectsWhenFilterOrStockFails()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("EndpointB");
            SDS_EndpointQuery deposit = SDS_EndpointQuery.Deposit(CoreTestKit.Make(a), null);

            env.Filter.AllowsFn = _ => false;
            Assert.False(SDS_Rules.SelectEndpoint(deposit).allowed);
            Assert.Null(SDS_Rules.SelectEndpoint(deposit).port);

            env.Filter.AllowsFn = _ => true;
            SDS_EndpointQuery withdraw = SDS_EndpointQuery.WithdrawPoint(null);
            withdraw.requireStock = true;
            withdraw.key = new SDS_StockKey(a, null, -1);
            Assert.False(SDS_Rules.SelectEndpoint(withdraw).allowed);
        }

        [Fact]
        public void Select_RejectsUnreachableAndInoperablePorts()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("EndpointC");
            SDS_EndpointQuery query = SDS_EndpointQuery.Deposit(CoreTestKit.Make(a), null);

            SDS_FakePort broken = new SDS_FakePort
            {
                Thing = CoreTestKit.Make(CoreTestKit.Def("Broken")),
                Operational = false,
                Distance = 1f
            };
            env.Ports.Candidates.Clear();
            env.Ports.Candidates.Add(broken);
            Assert.False(SDS_Rules.SelectEndpoint(query).allowed);

            broken.Operational = true;
            broken.Reachable = false;
            query.requireReach = true;
            query.actor = null;
            Assert.True(SDS_Rules.SelectEndpoint(query).allowed);
        }
    }
}
