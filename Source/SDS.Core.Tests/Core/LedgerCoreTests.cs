using System;
using System.Collections.Generic;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 纯账本用例：容量品类去重、保真预留 / 取出、预留终老与禁用持久化。
    /// 只经 Core 与 Contracts，不依赖 Rules / Vanilla（可独立单测）。
    /// </summary>
    public class LedgerCoreTests
    {
        [Fact]
        public void Capacity_DedupesPreservedThingId()
        {
            CoreTestEnv env = CoreTestEnv.Create(capacity: 30);
            ThingOwner<Thing> owner = (ThingOwner<Thing>)CoreTestKit.GetField(env.Ledger, "preservedOwner");
            Thing a = CoreTestKit.Make(CoreTestKit.Def("CatDedupeA_" + Guid.NewGuid().ToString("N")), 1);
            Thing b = CoreTestKit.Make(CoreTestKit.Def("CatDedupeB_" + Guid.NewGuid().ToString("N")), 1);
            CoreTestKit.SetField(a, "thingIDNumber", 910001);
            CoreTestKit.SetField(b, "thingIDNumber", 910001);
            Assert.True(owner.TryAdd(a, false));
            Assert.True(owner.TryAdd(b, false));

            // 同一 thingID 只算一个真身：容量不能再按第二个实例多算一个品类。
            Assert.Equal(1L, SDS_Core.UsedCapacity);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(new SDS_StockKey(a.def, null, -1, true)));
            Assert.Equal(0L, env.Ledger.GetPreservedCount(new SDS_StockKey(b.def, null, -1, true)));

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(env.Ledger, "AppendLedgerInvariantReport", lines);
            // 唯一违规是重复 thingID 本身；categoryKeys 必须与去重后的真身口径一致。
            Assert.Equal(1, bad);
            Assert.Contains(lines, line => line.Contains("重复 ID"));
            Assert.DoesNotContain(lines, line => line.Contains("categoryKeys"));
        }

        [Fact]
        public void PreservedPlan_ReserveWithdrawConsume_KeepsQuantityCaliber()
        {
            CoreTestEnv env = CoreTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("PresPlan_" + Guid.NewGuid().ToString("N"), stackLimit: 75);
            Thing stock = CoreTestKit.Make(def, 3);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryDepositPreserved", stock));

            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);
            Assert.Equal(3L, env.Ledger.GetPreservedCount(key));

            List<ThingCount> plan = new List<ThingCount> { new ThingCount(stock, 2) };
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryReservePreservedPlan", 101, null, plan));
            Assert.Equal(2L, env.Ledger.GetReserved(key));
            Assert.Equal(1L, (long)CoreTestKit.InvokeInternal(env.Ledger, "GetPreservedAvailable", key));

            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryWithdrawPreservedPlan", plan, null));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));
            Assert.Equal(2L, env.Ledger.GetReserved(key));

            Assert.Equal(2, (int)CoreTestKit.InvokeInternal(env.Ledger,
                "ConsumeReservation", 101, key, 2));
            Assert.Equal(0L, env.Ledger.GetReserved(key));

            // 不足计划必须整笔失败，且不得少给 / 不动账面。
            Assert.Equal(1, stock.stackCount);
            // ThingCount 默认会把 count 夹到 stackCount；用 ignoreStackLimit 构造真正的超量计划。
            List<ThingCount> tooMuch = new List<ThingCount> { new ThingCount(stock, 2, true) };
            Assert.False((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryWithdrawPreservedPlan", tooMuch, null));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));

            List<string> lines = new List<string>();
            Assert.Equal(0, (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines));
        }

        [Fact]
        public void AuditReservations_KeepsPositiveOwner_DropsNonPositive()
        {
            CoreTestEnv env = CoreTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("AuditOwner_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger,
                "TryDepositVirtual", key, 5L, null));

            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryReserve",
                101, new List<SDS_StockKey> { key }, new List<int> { 2 }, null));
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryReserve",
                -5, new List<SDS_StockKey> { key }, new List<int> { 1 }, null));
            Assert.Equal(3L, env.Ledger.GetReserved(key));

            // ownerAlive 为 null：正 id 保留，id ≤ 0 仍视为失效。
            SDS_Core.AuditReservations(null);
            Assert.Equal(2L, env.Ledger.GetReserved(key));

            // 判活委托返回 false：整组释放该归属。
            SDS_Core.AuditReservations(id => id != 101);
            Assert.Equal(0L, env.Ledger.GetReserved(key));

            List<string> lines = new List<string>();
            Assert.Equal(0, (int)CoreTestKit.InvokeInternal(env.Ledger,
                "AppendLedgerInvariantReport", lines));
        }

        [Fact]
        public void Forbidden_SurvivesRoundTripWithoutStock()
        {
            CoreTestEnv env = CoreTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.RegisteredDef("Forbidden_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Core.SetForbidden(key, true));
            Assert.Equal(0L, env.Ledger.GetCount(key));

            ISDS_LedgerHost target = SDS_Core.CreateLedger();
            CoreTestKit.RoundTrip(env.Ledger, target);

            // 禁用是玩家意图，不随库存为空而丢失（12.34）。
            Assert.True(target.IsForbidden(key));
            Assert.Equal(0L, target.GetCount(key));
            List<string> lines = new List<string>();
            Assert.Equal(0, (int)CoreTestKit.InvokeInternal(target,
                "AppendLedgerInvariantReport", lines));
        }
    }
}
