using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 真实 Scribe 往返：不是源码文本断言，而是 Save → Load → PostLoadInit 后比对账本。
    /// 覆盖虚拟库存 / 禁用键 / 预留；保真用例使用注册到 DefDatabase 的唯一 def。
    /// </summary>
    public class ScribeRoundTripTests
    {
        [Fact]
        public void RoundTrip_PreservesVirtualStockForbiddenAndReservations()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("ScribeVirtual_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);

            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 7), env.Port, out _));
            SDS_Core.SetForbidden(key, true);
            object reserved = CoreTestKit.InvokeInternal(env.Ledger, "TryReserve", 9001,
                new List<SDS_StockKey> { key }, new List<int> { 2 }, null);
            Assert.True((bool)reserved);

            ISDS_LedgerHost target = SDS_Core.CreateLedger();
            CoreTestKit.RoundTrip(env.Ledger, target);

            Assert.Equal(7L, target.GetCount(key));
            Assert.Equal(2L, target.GetReserved(key));
            Assert.True(target.IsForbidden(key));
            Assert.Equal(0L, target.GetPreservedCount(key));
        }

        [Fact]
        public void RoundTrip_NormalizesOrphanRotProgressWithoutRotComp()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            // 必须注册到 DefDatabase：本用例验证的是「已解析且无 CompRottable」的归一化；
            // 未注册 def 属于未解析保留（vitals 不再归一化，见 SDS.Core.Tests 的 UnresolvedVitalsTests）。
            ThingDef def = CoreTestKit.RegisteredDef("ScribeOrphanRot_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);

            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual", key, 3L,
                new SDS_StockVitals { hitPoints = -1f, rotProgress = -1f }));
            // 注入历史孤儿值（旧档遗留：无腐坏件却带进度）；随存档落盘，读档时必须被归一化。
            Dictionary<SDS_StockKey, SDS_StockVitals> sourceVitals =
                (Dictionary<SDS_StockKey, SDS_StockVitals>)CoreTestKit.GetField(env.Ledger, "stockVitals");
            sourceVitals[key].rotProgress = 6189.946f;

            ISDS_LedgerHost target = SDS_Core.CreateLedger();
            CoreTestKit.RoundTrip(env.Ledger, target);

            // 数量口径不受归一化影响；孤儿值复位 -1 哨兵，自检不再报。
            Assert.Equal(3L, target.GetCount(key));
            Dictionary<SDS_StockKey, SDS_StockVitals> loadedVitals =
                (Dictionary<SDS_StockKey, SDS_StockVitals>)CoreTestKit.GetField(target, "stockVitals");
            Assert.True(loadedVitals.TryGetValue(key, out SDS_StockVitals vitals));
            Assert.Equal(-1f, vitals.rotProgress);

            List<string> lines = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(target, "AppendLedgerInvariantReport", lines);
            Assert.Equal(0, bad);
        }

        [Fact]
        public void RoundTrip_PreservesLivePreservedThing()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.RegisteredDef("ScribePreserved_" + Guid.NewGuid().ToString("N"));
            Func<Thing, bool> probe = thing => thing != null && thing.def == def;
            SDS_PreserveHooks.Register(probe);
            try
            {
                Thing thing = CoreTestKit.Make(def, 3);
                Assert.True(SDS_Rules.TryDeposit(thing, env.Port, out _));

                SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);
                Assert.Equal(3L, env.Ledger.GetPreservedCount(preservedKey));

                ISDS_LedgerHost target = SDS_Core.CreateLedger();
                CoreTestKit.RoundTrip(env.Ledger, target);

                Assert.Equal(3L, target.GetPreservedCount(preservedKey));
                List<Thing> restored = new List<Thing>();
                target.AppendPreservedThings(restored);
                Assert.Single(restored);
                Assert.Equal(3, restored[0].stackCount);
                Assert.Equal(def.defName, restored[0].def.defName);
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }
        }

        [Fact]
        public void RoundTrip_ResolvesReservationPawnReference()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.Def("ScribePawn_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 5), env.Port, out _));

            Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
            ThingDef pawnDef = CoreTestKit.RegisteredDef(
                "ScribePawnDef_" + Guid.NewGuid().ToString("N"));
            CoreTestKit.SetField(pawn, "thingIDNumber", 424242);
            CoreTestKit.SetField(pawn, "def", pawnDef);
            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryReserve", 9100,
                new List<SDS_StockKey> { key }, new List<int> { 2 }, pawn));
            List<SDS_Reservation> sourceReservations =
                (List<SDS_Reservation>)CoreTestKit.GetField(env.Ledger, "reservations");
            Assert.Same(pawn, sourceReservations[0].pawn);

            ISDS_LedgerHost target = SDS_Core.CreateLedger();
            CoreTestKit.RoundTrip(env.Ledger, target, pawn);
            Assert.True(CoreTestKit.LastSavedXml.Contains(pawnDef.defName + "424242"),
                CoreTestKit.LastSavedXml);

            Assert.Equal(2L, target.GetReserved(key));
            List<SDS_Reservation> reservations =
                (List<SDS_Reservation>)CoreTestKit.GetField(target, "reservations");
            Assert.Single(reservations);
            Assert.Same(pawn, reservations[0].pawn);
        }

        [Fact]
        public void RoundTrip_SavesOrphanPending_AndRestoresToOwner()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef def = CoreTestKit.RegisteredDef("ScribePending_" + Guid.NewGuid().ToString("N"));
            Thing pendingThing = CoreTestKit.Make(def, 2);
            List<Thing> pending =
                (List<Thing>)CoreTestKit.GetField(env.Ledger, "pendingPreservedRestore");
            pending.Add(pendingThing);

            ISDS_LedgerHost target = SDS_Core.CreateLedger();
            CoreTestKit.RoundTrip(env.Ledger, target);

            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);
            Assert.Equal(2L, target.GetPreservedCount(preservedKey));
            List<Thing> stillPending =
                (List<Thing>)CoreTestKit.GetField(target, "pendingPreservedRestore");
            Assert.Empty(stillPending);
        }

        [Fact]
        public void RoundTrip_PreservesMinifiedThingInnerContent()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            ThingDef innerDef = CoreTestKit.RegisteredDef(
                "ScribeMinifiedInner_" + Guid.NewGuid().ToString("N"));
            ThingDef shellDef = CoreTestKit.Def(
                "ScribeMinifiedShell_" + Guid.NewGuid().ToString("N"));
            shellDef.thingClass = typeof(MinifiedThing);
            shellDef.category = ThingCategory.Item;
            shellDef.stackLimit = 1;
            DefDatabase<ThingDef>.Add(shellDef);

            MinifiedThing minified = (MinifiedThing)ThingMaker.MakeThing(shellDef);
            minified.InnerThing = CoreTestKit.Make(innerDef, 1);
            Func<Thing, bool> probe = thing => thing == minified;
            SDS_PreserveHooks.Register(probe);
            try
            {
                Assert.True(SDS_Rules.TryDeposit(minified, env.Port, out _));
                SDS_StockKey key = new SDS_StockKey(innerDef, null, -1, true);
                Assert.Equal(1L, env.Ledger.GetPreservedCount(key));

                ISDS_LedgerHost target = SDS_Core.CreateLedger();
                CoreTestKit.RoundTrip(env.Ledger, target);

                Assert.Equal(1L, target.GetPreservedCount(key));
                List<Thing> restored = new List<Thing>();
                target.AppendPreservedThings(restored);
                Assert.Single(restored);
                Assert.IsType<MinifiedThing>(restored[0]);
                MinifiedThing restoredMinified = (MinifiedThing)restored[0];
                Assert.NotNull(restoredMinified.InnerThing);
                Assert.Equal(innerDef.defName, restoredMinified.InnerThing.def.defName);
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }
        }
    }
}
