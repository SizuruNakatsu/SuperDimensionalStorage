using System;
using System.Collections.Generic;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 未解析 def 的 vitals 保留：暂时缺 mod 时读档不得把 HP / 腐烂永久写成 -1。
    /// 只走账本与 Scribe，不依赖 Rules / Vanilla。
    /// </summary>
    public class UnresolvedVitalsTests
    {
        [Fact]
        public void RoundTrip_PreservesVitalsForUnresolvedDef()
        {
            CoreTestEnv env = CoreTestEnv.Create();
            ThingDef def = CoreTestKit.Def("UnresolvedVitals_" + Guid.NewGuid().ToString("N"));
            def.useHitPoints = true;
            SDS_StockKey key = new SDS_StockKey(def, null, -1);

            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual",
                key, 3L, new SDS_StockVitals { hitPoints = 60f, rotProgress = 50f }));

            // def 不在 DefDatabase：读档后该条目必须是未解析状态，但数量与 vitals 都要保留。
            ISDS_LedgerHost target = SDS_Core.CreateLedger();
            CoreTestKit.RoundTrip(env.Ledger, target);

            Assert.Equal(3L, target.GetCount(key));
            Dictionary<SDS_StockKey, SDS_StockVitals> vitals =
                (Dictionary<SDS_StockKey, SDS_StockVitals>)CoreTestKit.GetField(target, "stockVitals");
            Assert.True(vitals.TryGetValue(key, out SDS_StockVitals loaded));
            Assert.Equal(60f, loaded.hitPoints);
            Assert.Equal(50f, loaded.rotProgress);

            List<string> lines = new List<string>();
            Assert.Equal(0, (int)CoreTestKit.InvokeInternal(target, "AppendLedgerInvariantReport", lines));
        }

        [Fact]
        public void RoundTrip_StillNormalizesRotForResolvedNonRottableDef()
        {
            CoreTestEnv env = CoreTestEnv.Create();
            ThingDef def = CoreTestKit.RegisteredDef("ResolvedNoRot_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);

            Assert.True((bool)CoreTestKit.InvokeInternal(env.Ledger, "TryDepositVirtual",
                key, 2L, new SDS_StockVitals { hitPoints = -1f, rotProgress = 50f }));

            ISDS_LedgerHost target = SDS_Core.CreateLedger();
            CoreTestKit.RoundTrip(env.Ledger, target);

            // 已解析且无 CompRottable：旧档孤儿值仍按原口径复位 -1。
            Dictionary<SDS_StockKey, SDS_StockVitals> vitals =
                (Dictionary<SDS_StockKey, SDS_StockVitals>)CoreTestKit.GetField(target, "stockVitals");
            Assert.True(vitals.TryGetValue(key, out SDS_StockVitals loaded));
            Assert.Equal(-1f, loaded.rotProgress);

            List<string> lines = new List<string>();
            Assert.Equal(0, (int)CoreTestKit.InvokeInternal(target, "AppendLedgerInvariantReport", lines));
        }
    }
}
