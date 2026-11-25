using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 规则 / 流程测试环境（P5）：在账本环境之上接宿主槽位、外部保真委托、结构事实与测试端口。
    /// 只给 SDS.Rules.Tests 使用；SDS.Core.Tests 不再引用本程序集。
    /// </summary>
    internal sealed class RulesTestEnv
    {
        public ISDS_LedgerHost Ledger;
        public SDS_FakePortsProvider Ports;
        public SDS_FakeDepositFilter Filter;
        public SDS_FakePrioritySource Priority;
        public SDS_FakeDeliverySink Delivery;
        public SDS_FakeMirrorQuery MirrorQuery;
        public SDS_FakeMirrorPhysical MirrorPhysical;
        public SDS_FakeMedicalCarePolicy MedicalCare;
        public SDS_FakeReclaimWorld ReclaimWorld;
        public SDS_FakePort Port;

        public static RulesTestEnv Create(int capacity = 30, int perArray = 30,
            bool separateQuality = true, bool separateStuff = true)
        {
            CoreTestEnv core = CoreTestEnv.Create(capacity, perArray, separateQuality, separateStuff);
            SDS_Rules.RegisterAll();
            ResetRulesStatics();
            // 组合根同款接线：Core 不直接引用 SDS_PreserveHooks。
            SDS_Rules.SetExternalPreserveProvider(SDS_PreserveHooks.RequiresPreserved);

            RulesTestEnv env = new RulesTestEnv();
            env.Ledger = core.Ledger;
            env.Ports = new SDS_FakePortsProvider();
            env.Filter = new SDS_FakeDepositFilter();
            env.Priority = new SDS_FakePrioritySource();
            env.Delivery = new SDS_FakeDeliverySink();
            env.MirrorQuery = new SDS_FakeMirrorQuery();
            env.MirrorPhysical = new SDS_FakeMirrorPhysical();
            env.MedicalCare = new SDS_FakeMedicalCarePolicy();
            env.ReclaimWorld = new SDS_FakeReclaimWorld();
            SDS_Host.Install(new SDS_HostSlots
            {
                ports = env.Ports,
                depositFilter = env.Filter,
                priority = env.Priority,
                delivery = env.Delivery,
                mirrorQuery = env.MirrorQuery,
                mirrorPhysical = env.MirrorPhysical,
                medicalCare = env.MedicalCare,
                reclaimWorld = env.ReclaimWorld,
                display = new SDS_FakeDisplayQuery(),
                depositJobs = new SDS_FakeDepositJobs(),
                transportLoad = new SDS_FakeTransportLoad()
            });

            env.Port = new SDS_FakePort
            {
                Thing = CoreTestKit.Make(CoreTestKit.Def("TestPort")),
                Operational = true,
                Distance = 0f
            };
            env.Ports.Candidates.Add(env.Port);
            return env;
        }

        public void AddStructure(SDS_StructureKind kind, bool operational = true)
        {
            SDS_StructureFact fact = new SDS_StructureFact
            {
                Thing = CoreTestKit.Make(CoreTestKit.Def("Structure_" + kind + "_" + Guid.NewGuid().ToString("N"))),
                Kind = kind,
                Operational = operational
            };
            SDS_Rules.RegisterStructure(fact);
        }

        private static void ResetRulesStatics()
        {
            Assembly rulesAssembly = typeof(SDS_Rules).Assembly;
            Type rulesImpl = rulesAssembly.GetType("SuperDimensionalStorage.SDS_RulesImpl");
            FieldInfo field = rulesImpl != null
                ? rulesImpl.GetField("structures", BindingFlags.Static | BindingFlags.NonPublic)
                : null;
            object registry = field != null ? field.GetValue(null) : null;
            if (registry != null)
            {
                MethodInfo clear = registry.GetType().GetMethod("Clear",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (clear != null)
                {
                    clear.Invoke(registry, null);
                }
            }
            SDS_PreserveHooks.Reset();
            SDS_Rules.SetExternalPreserveProvider(null);
            string[] resetTypes =
            {
                "SDS_EndpointContractCache", "SDS_ProducedThingMarker"
            };
            for (int i = 0; i < resetTypes.Length; i++)
            {
                Type type = rulesAssembly.GetType("SuperDimensionalStorage." + resetTypes[i]);
                MethodInfo reset = type != null
                    ? type.GetMethod("Reset",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                if (reset != null)
                {
                    reset.Invoke(null, null);
                }
            }
            Type implant = rulesAssembly.GetType("SuperDimensionalStorage.SDS_ImplantUtilityImpl");
            MethodInfo resetChip = implant != null
                ? implant.GetMethod("ResetChipCache",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                : null;
            if (resetChip != null)
            {
                resetChip.Invoke(null, null);
            }
        }
    }

    /// <summary>
    /// 规则 / 流程测试辅助：访问已迁入 Rules 的内部实现类型。
    /// 与 RulesTestEnv 分离，供只操作内部状态机的用例复用。
    /// </summary>
    internal static class RulesTestKit
    {
        /// <summary>取事务实现类型。入：无。出：SDS_WithdrawTransaction 的 Type。
        /// 区别：与 CoreTestKit 的账本反射分开，事务实现自 P3 起在 Rules。</summary>
        public static Type TransactionType
        {
            get { return typeof(SDS_Rules).Assembly.GetType("SuperDimensionalStorage.SDS_WithdrawTransaction"); }
        }

        /// <summary>取事务状态。入：transaction = 事务实例。出：状态枚举；null 返回 None。
        /// 区别：只读公开字段 state，不触发状态机推进。</summary>
        public static SDS_WithdrawEndState TransactionState(object transaction)
        {
            if (transaction == null)
            {
                return SDS_WithdrawEndState.None;
            }
            FieldInfo field = transaction.GetType().GetField("state",
                BindingFlags.Instance | BindingFlags.Public);
            return (SDS_WithdrawEndState)field.GetValue(transaction);
        }
    }
}
