using System.Collections.Generic;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 医药等级门控：规则模块不读宿主，筛选由调用方以委托注入；
    /// `SDS_MedicalGate` 是 Vanilla / Api 共用的唯一取值口径，宿主缺失时放行。
    /// </summary>
    public class BillMedicalGateTests
    {
        [Fact]
        public void AllowsDef_InjectedGateDenies_RejectsCandidate()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef medicine = CoreTestKit.Def("SDS_TestMedicineDenied");

            Assert.False(SDS_Rules.AllowsDef(null, null, medicine, null, null, null, null,
                _ => false));
        }

        [Fact]
        public void AllowsDef_InjectedGateAllows_AcceptsCandidate()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef medicine = CoreTestKit.Def("SDS_TestMedicineAllowed");
            ThingDef seen = null;

            Assert.True(SDS_Rules.AllowsDef(null, null, medicine, null, null, null, null,
                def =>
                {
                    seen = def;
                    return true;
                }));
            Assert.Same(medicine, seen);
        }

        [Fact]
        public void AllowsDef_NoGate_FailsOpen()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef medicine = CoreTestKit.Def("SDS_TestMedicineNoGate");

            Assert.True(SDS_Rules.AllowsDef(null, null, medicine, null, null, null, null));
        }

        [Fact]
        public void MedicalGate_PassesBillGiverAndDefToPolicy()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            env.MedicalCare.Allow = false;
            ThingDef medicine = CoreTestKit.Def("SDS_TestMedicineGiver");
            Thing patient = CoreTestKit.Make(CoreTestKit.Def("SDS_TestPatient"));

            bool allowed = SDS_MedicalGate.Allows(patient, medicine);

            Assert.False(allowed);
            Assert.Same(patient, env.MedicalCare.LastBillGiver);
            Assert.Same(medicine, env.MedicalCare.LastMedicine);
        }

        [Fact]
        public void TryPlanAllowedDefSupply_GateDenies_ReturnsNoPlan()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef medicine = CoreTestKit.Def("SDS_TestMedicinePlanDenied");

            bool planned = SDS_Rules.TryPlanAllowedDefSupply(
                new List<ThingDef> { medicine }, null,
                _ => 5, _ => 0, _ => 10L, _ => false, null,
                new HashSet<ThingDef>(), new Dictionary<ThingDef, int>(),
                out ThingDef def, out int required, out int fromOwn, out int shortfall);

            Assert.False(planned);
            Assert.Null(def);
            Assert.Equal(0, required);
            Assert.Equal(0, fromOwn);
            Assert.Equal(0, shortfall);
        }

        [Fact]
        public void TryPlanAllowedDefSupply_GateAllows_PlansShortfall()
        {
            RulesTestEnv env = RulesTestEnv.Create();
            ThingDef medicine = CoreTestKit.Def("SDS_TestMedicinePlanAllowed");
            HashSet<ThingDef> applied = new HashSet<ThingDef>();
            Dictionary<ThingDef, int> pool = new Dictionary<ThingDef, int>();

            bool planned = SDS_Rules.TryPlanAllowedDefSupply(
                new List<ThingDef> { medicine }, null,
                _ => 5, _ => 0, _ => 10L, _ => true, null,
                applied, pool,
                out ThingDef def, out int required, out int fromOwn, out int shortfall);

            Assert.True(planned);
            Assert.Same(medicine, def);
            Assert.Equal(5, required);
            Assert.Equal(0, fromOwn);
            Assert.Equal(5, shortfall);
        }
    }
}
