using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    public class ReturnTests
    {
        [Fact]
        public void ReturnThing_MapNull_DoesNotFallback()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            SDS_Core.Attach(null);

            SDS_Rules.ReturnThing(CoreTestKit.Make(CoreTestKit.Def("ReturnA")),
                SDS_StockChannel.Virtual, null, null, SDS_ReturnFallback.Ground);
            Assert.Equal(0, env.Delivery.PlaceCalls);

            SDS_Rules.ReturnThing(CoreTestKit.Make(CoreTestKit.Def("ReturnB")),
                SDS_StockChannel.Virtual, null, null, SDS_ReturnFallback.PawnInventory);
            Assert.Equal(0, env.Delivery.PawnCalls);

            SDS_Rules.ReturnThing(CoreTestKit.Make(CoreTestKit.Def("ReturnC")),
                SDS_StockChannel.Preserved, null, null, SDS_ReturnFallback.PawnCarry);
            Assert.Equal(0, env.Delivery.PawnCalls);
        }

        [Fact]
        public void ReturnThing_LedgerFirst_NoDelivery()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef a = CoreTestKit.Def("ReturnD");
            Assert.True(SDS_Rules.ReturnThing(CoreTestKit.Make(a, 2), SDS_StockChannel.Virtual,
                null, null, SDS_ReturnFallback.Ground));

            Assert.Equal(2L, env.Ledger.GetCount(new SDS_StockKey(a, null, -1)));
            Assert.Equal(0, env.Delivery.PlaceCalls);
        }

        [Fact]
        public void ReturnThing_DestroyThrows_DoesNotCountTwice()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("ThrowingDestroyReturn");
            def.thingClass = typeof(ThrowingDestroyThing);
            Thing thing = CoreTestKit.Make(def, 2);

            // 回充与存入共用同一条「实转虚」原子实现：销毁抛异常必须回滚，不能账实各留一份。
            Assert.False(SDS_Rules.ReturnThing(thing, SDS_StockChannel.Virtual, null, null,
                SDS_ReturnFallback.Ground));
            Assert.Equal(0L, env.Ledger.GetCount(new SDS_StockKey(def, null, -1)));
            Assert.False(thing.Destroyed);
        }
    }
}
