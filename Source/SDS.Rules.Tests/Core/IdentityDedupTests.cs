using System;
using System.Collections.Generic;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 同一实物的身份门：同一引用或同一 thingID 在账本中只能有一个所有者。
    /// 覆盖保真存入拒收、虚拟通道拒绝接管账本真身、回充幂等与待恢复副本清理。
    /// </summary>
    public class IdentityDedupTests
    {
        private static int nextThingId = 700000000;

        /// <summary>确保实例有正 thingID（测试里的 ThingMaker 不一定分配，ID 只需在本测试内唯一）。</summary>
        private static int EnsureThingId(Thing thing)
        {
            if (thing.thingIDNumber <= 0)
            {
                CoreTestKit.SetField(thing, "thingIDNumber",
                    System.Threading.Interlocked.Increment(ref nextThingId));
            }
            return thing.thingIDNumber;
        }

        private static int InvariantCount(ISDS_LedgerHost ledger)
        {
            return (int)CoreTestKit.InvokeInternal(ledger, "AppendLedgerInvariantReport",
                new List<string>());
        }

        private static Thing MakeSameThingId(ThingDef def, int thingID)
        {
            Thing thing = CoreTestKit.Make(def, 1);
            CoreTestKit.SetField(thing, "thingIDNumber", thingID);
            return thing;
        }

        private static Thing DepositPreserved(RulesTestEnv env, ThingDef def)
        {
            Thing thing = CoreTestKit.Make(def, 1);
            EnsureThingId(thing);
            SDS_PreserveHooks.Register(t => ReferenceEquals(t, thing));
            Assert.True(SDS_Rules.TryDeposit(thing, env.Port, out _));
            SDS_PreserveHooks.Reset();
            return thing;
        }

        [Fact]
        public void PreservedDeposit_RejectsSameThingIdThroughCoreGate()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("IdentityPreserved_" + Guid.NewGuid().ToString("N"));
            Thing original = DepositPreserved(env, def);
            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));

            Thing duplicate = MakeSameThingId(def, EnsureThingId(original));
            Assert.False(SDS_Rules.CanDeposit(duplicate));
            Assert.False(SDS_Rules.TryDeposit(duplicate, env.Port, out int accepted));

            Assert.Equal(0, accepted);
            Assert.False(env.Ledger.IsPreserved(duplicate));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));
            Assert.Equal(0, InvariantCount(env.Ledger));
        }

        [Fact]
        public void PreservedDeposit_RejectsSameThingIdAtLedgerGuard()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("IdentityLedger_" + Guid.NewGuid().ToString("N"));
            Thing original = DepositPreserved(env, def);
            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);

            Thing duplicate = MakeSameThingId(def, EnsureThingId(original));
            Assert.False((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryDepositPreserved", duplicate));
            Assert.False(env.Ledger.IsPreserved(duplicate));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));
            Assert.Equal(0, InvariantCount(env.Ledger));
        }

        [Fact]
        public void VirtualConversion_RejectsLedgerOwnedPreservedThing()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("IdentityVirtual_" + Guid.NewGuid().ToString("N"));
            Thing original = DepositPreserved(env, def);

            object[] call = { original, 0 };
            Assert.False((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryConvertThingToVirtual", call));

            Assert.Equal(0, (int)call[1]);
            Assert.False(original.Destroyed);
            Assert.True(env.Ledger.IsPreserved(original));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(
                new SDS_StockKey(def, null, -1, true)));
            Assert.Equal(0L, env.Ledger.GetCount(new SDS_StockKey(def, null, -1)));
            Assert.Equal(0, InvariantCount(env.Ledger));
        }

        [Fact]
        public void ReturnVirtual_OnLedgerOwnedThing_IsIdempotentWithoutDestroying()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("IdentityReturnVirtual_" + Guid.NewGuid().ToString("N"));
            Thing original = DepositPreserved(env, def);

            Assert.True(SDS_Rules.ReturnThing(original, SDS_StockChannel.Virtual,
                null, null, SDS_ReturnFallback.Ground));

            Assert.False(original.Destroyed);
            Assert.True(env.Ledger.IsPreserved(original));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(
                new SDS_StockKey(def, null, -1, true)));
            Assert.Equal(0L, env.Ledger.GetCount(new SDS_StockKey(def, null, -1)));
            Assert.Equal(0, env.Delivery.PlaceCalls);
            Assert.Equal(0, InvariantCount(env.Ledger));
        }

        [Fact]
        public void ReturnVirtual_SameThingIdDifferentInstance_RejectedWithoutDelivery()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("IdentityReturnVirtualDup_" + Guid.NewGuid().ToString("N"));
            Thing original = DepositPreserved(env, def);
            Thing duplicate = MakeSameThingId(def, EnsureThingId(original));

            Assert.False(SDS_Rules.ReturnThing(duplicate, SDS_StockChannel.Virtual,
                null, null, SDS_ReturnFallback.Ground));

            Assert.False(duplicate.Destroyed);
            Assert.False(env.Ledger.IsPreserved(duplicate));
            Assert.Equal(0, env.Delivery.PlaceCalls);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(
                new SDS_StockKey(def, null, -1, true)));
            Assert.Equal(0, InvariantCount(env.Ledger));
        }

        [Fact]
        public void ReturnPreserved_SameThingIdDifferentInstance_RejectedWithoutDelivery()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("IdentityReturnPreservedDup_" + Guid.NewGuid().ToString("N"));
            Thing original = DepositPreserved(env, def);
            Thing duplicate = MakeSameThingId(def, EnsureThingId(original));

            Assert.False(SDS_Rules.ReturnThing(duplicate, SDS_StockChannel.Preserved,
                null, null, SDS_ReturnFallback.Ground));

            Assert.False(env.Ledger.IsPreserved(duplicate));
            Assert.Equal(0, env.Delivery.PlaceCalls);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(
                new SDS_StockKey(def, null, -1, true)));
            Assert.Equal(0, InvariantCount(env.Ledger));
        }

        [Fact]
        public void PendingRestore_DropsSameThingIdDuplicate()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("IdentityPending_" + Guid.NewGuid().ToString("N"));
            Thing original = DepositPreserved(env, def);
            Thing duplicate = MakeSameThingId(def, EnsureThingId(original));
            List<Thing> pending =
                (List<Thing>)CoreTestKit.GetField(env.Ledger, "pendingPreservedRestore");
            pending.Add(duplicate);

            Assert.False((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "RetryPendingPreservedRestore"));

            Assert.Empty(pending);
            Assert.False(env.Ledger.IsPreserved(duplicate));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(
                new SDS_StockKey(def, null, -1, true)));
            Assert.Equal(0, InvariantCount(env.Ledger));
        }
    }
}
