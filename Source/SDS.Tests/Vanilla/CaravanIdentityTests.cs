using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 编成行身份回归：一行最多一个虚拟键 + 一个保真桶；多个身份必须 fail-closed，
    /// 保真身份按展示物唯一解析，不按 key 聚合（W45 / W47）。
    /// </summary>
    public class CaravanIdentityTests
    {
        private static bool TryScan(ISDS_LedgerHost ledger, TransferableOneWay row,
            out SDS_StockKey virtualKey, out SDS_StockKey preservedKey,
            out List<Thing> preservedBucket, out bool ambiguous)
        {
            MethodInfo scan = typeof(SDS_CaravanVirtual).GetMethod(
                "TryScanRowIdentities", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(scan);
            object[] args = { ledger, row, null, null, null, false, false };
            bool ok = (bool)scan.Invoke(null, args);
            virtualKey = (SDS_StockKey)args[2];
            preservedKey = (SDS_StockKey)args[3];
            preservedBucket = (List<Thing>)args[4];
            ambiguous = (bool)args[5];
            return ok;
        }

        [Fact]
        public void SameBucket_ReferenceOverlapAndDisjoint()
        {
            CoreTestKit.EnsureGameStub();
            ThingDef def = CoreTestKit.Def("SameBucket_" + Guid.NewGuid().ToString("N"));
            Thing a = CoreTestKit.Make(def);
            Thing b = CoreTestKit.Make(def);
            Thing c = CoreTestKit.Make(def);
            List<Thing> first = new List<Thing> { a, b };
            List<Thing> same = new List<Thing> { a, b };
            List<Thing> overlapping = new List<Thing> { b };
            List<Thing> disjoint = new List<Thing> { c };

            Assert.True(SDS_PreservedBuckets.SameBucket(first, first));
            Assert.True(SDS_PreservedBuckets.SameBucket(first, same));
            Assert.True(SDS_PreservedBuckets.SameBucket(first, overlapping));
            Assert.False(SDS_PreservedBuckets.SameBucket(first, disjoint));
            Assert.False(SDS_PreservedBuckets.SameBucket(first, null));
        }

        [Fact]
        public void RowWithTwoVirtualKeys_IsAmbiguousAndNotModified()
        {
            CoreTestKit.EnsureGameStub();
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            SDS_CaravanVirtual.Reset();

            ThingDef def = CoreTestKit.Def("RowTwoVirtual_" + Guid.NewGuid().ToString("N"));
            Thing first = SDS_OutputUtility.MakeDisplayThing(new SDS_StockKey(def, null, -1));
            Thing second = SDS_OutputUtility.MakeDisplayThing(new SDS_StockKey(def, null, 1));
            Assert.NotNull(first);
            Assert.NotNull(second);

            TransferableOneWay row = new TransferableOneWay();
            row.things.Add(first);
            row.things.Add(second);

            Assert.False(TryScan(env.Ledger, row, out SDS_StockKey virtualKey,
                out SDS_StockKey preservedKey, out List<Thing> bucket, out bool ambiguous));
            // 两个不同虚拟键：扫描必须报歧义，且不修改 things。
            Assert.True(ambiguous);
            Assert.Equal(2, row.things.Count);
            Assert.Null(bucket);
        }

        [Fact]
        public void RowWithVirtualAndPreservedDisplay_ScansBothChannels()
        {
            CoreTestKit.EnsureGameStub();
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            SDS_CaravanVirtual.Reset();

            ThingDef def = CoreTestKit.Def("RowMixed_" + Guid.NewGuid().ToString("N"));
            Thing preservedSource = CoreTestKit.Make(def, 1);
            SDS_PreserveHooks.Register(thing => thing.def == def);
            try
            {
                Assert.True(SDS_Rules.TryDeposit(preservedSource, env.Port, out _));
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }
            List<Thing> preserved = new List<Thing>();
            env.Ledger.AppendPreservedThings(preserved);
            Assert.Single(preserved);

            Thing preservedDisplay = SDS_OutputUtility.MakePreservedDisplayThingFrom(preserved[0]);
            Thing virtualDisplay = SDS_OutputUtility.MakeDisplayThing(new SDS_StockKey(def, null, -1));
            Assert.NotNull(preservedDisplay);
            Assert.NotNull(virtualDisplay);

            // 直接登记桶，避免在本测试宿主里触发 TransferAsOne 的原版 ModsConfig 初始化。
            FieldInfo bucketsField = typeof(SDS_CaravanVirtual).GetField(
                "preservedBuckets", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(bucketsField);
            SDS_DisplayBucketRegistry registry =
                (SDS_DisplayBucketRegistry)bucketsField.GetValue(null);
            registry.Register(preservedDisplay, new List<Thing> { preserved[0] });
            try
            {
                TransferableOneWay row = new TransferableOneWay();
                row.things.Add(virtualDisplay);
                row.things.Add(preservedDisplay);

                Assert.True(TryScan(env.Ledger, row, out SDS_StockKey virtualKey,
                    out SDS_StockKey preservedKey, out List<Thing> bucket, out bool ambiguous));
                Assert.False(ambiguous);
                Assert.NotNull(virtualKey);
                Assert.False(virtualKey.preserved);
                Assert.NotNull(preservedKey);
                Assert.True(preservedKey.preserved);
                Assert.NotNull(bucket);
                Assert.Contains(preserved[0], bucket);
            }
            finally
            {
                SDS_CaravanVirtual.Reset();
            }
        }
    }
}
