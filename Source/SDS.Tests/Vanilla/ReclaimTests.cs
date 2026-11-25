using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 回收账本巡检的契约级回归：验证驱动、Spawned / 持有 / 已销毁守卫与条目清理。
    /// 真正的“回充成功 / 落地”路径需要真实 Map，由游戏内验证覆盖。
    /// </summary>
    public class ReclaimTests
    {
        public ReclaimTests()
        {
            MethodInfo reset = ReclaimType.GetMethod("Reset",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (reset != null)
            {
                reset.Invoke(null, null);
            }
        }

        private static Type ReclaimType
        {
            get { return typeof(SDS_Vanilla).Assembly.GetType("SuperDimensionalStorage.SDS_ReclaimRegistry"); }
        }

        private static int Count
        {
            get
            {
                PropertyInfo property = ReclaimType.GetProperty("Count",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return (int)property.GetValue(null);
            }
        }

        private static SDS_ReclaimOutcome Sweep(int now)
        {
            return (SDS_ReclaimOutcome)CoreTestKit.StaticInternal(ReclaimType, "Sweep", now);
        }

        private static void AuditRestored()
        {
            CoreTestKit.StaticInternal(ReclaimType, "AuditRestored");
        }

        private static void Register(Thing thing, int lordLoadID)
        {
            CoreTestKit.StaticInternal(ReclaimType, "RegisterProtectedOutput", thing, lordLoadID);
            Assert.Equal(1, Count);
        }

        private static Job MakeJob(int loadID)
        {
            Job job = (Job)RuntimeHelpers.GetUninitializedObject(typeof(Job));
            typeof(Job).GetField("loadID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(job, loadID);
            return job;
        }

        [Fact]
        public void TrackJobTarget_ReclaimJobTarget_RemovesEntry()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 5;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimJobTarget"), 1);

            CoreTestKit.StaticInternal(ReclaimType, "TrackJobTarget", MakeJob(4242), thing, 1);
            Assert.Equal(1, Count);

            object outcome = CoreTestKit.StaticInternal(ReclaimType, "ReclaimJobTarget", 4242);
            Assert.Equal(SDS_ReclaimOutcome.Abandoned, (SDS_ReclaimOutcome)outcome);
            Assert.Equal(0, Count);
        }

        [Fact]
        public void ForgetJobTarget_LeavesHandEntry()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 6;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimHandForget"), 1);

            CoreTestKit.StaticInternal(ReclaimType, "TrackHand", MakeJob(4243), thing);
            Assert.Equal(1, Count);

            CoreTestKit.StaticInternal(ReclaimType, "ForgetJobTarget", 4243);
            Assert.Equal(1, Count);
        }

        [Fact]
        public void Sweep_SkipsHandEntry()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 7;
            env.ReclaimWorld.JobExistsFn = _ => false;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimHandSweep"), 1);

            CoreTestKit.StaticInternal(ReclaimType, "TrackHand", MakeJob(4244), thing);
            Sweep(7);

            Assert.Equal(1, Count);
        }

        [Fact]
        public void Sweep_OwnerGone_RemovesUnspawnedEntryWithoutDelivery()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 1000;
            env.ReclaimWorld.LordExistsFn = _ => false;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimOwnerGone"), 2);
            Register(thing, 7);

            Assert.Equal(SDS_ReclaimOutcome.Abandoned, Sweep(1000));
            Assert.Equal(0, Count);
            Assert.Equal(0, env.Delivery.PlaceCalls);
            Assert.Empty(env.ReclaimWorld.RemovedFromCaravan);
        }

        [Fact]
        public void Sweep_Timeout_RemovesUnspawnedEntry()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 0;
            env.ReclaimWorld.LordExistsFn = _ => true;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimTimeout"), 1);
            Register(thing, 8);

            env.ReclaimWorld.Tick = 60000;
            Assert.Equal(SDS_ReclaimOutcome.Abandoned, Sweep(60000));
            Assert.Equal(0, Count);
        }

        [Fact]
        public void Sweep_AlreadyMoved_DoesNotReclaim()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 100;
            env.ReclaimWorld.LordExistsFn = _ => false;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimMoved"), 1);
            Register(thing, 9);

            // 未 spawn = 已被远征队搬走 / 进入库存；Sweep 只结案，不调用回充交付。
            Assert.Equal(SDS_ReclaimOutcome.Abandoned, Sweep(100));
            Assert.Equal(0, Count);
            Assert.Equal(0, env.Delivery.PlaceCalls);
        }

        [Fact]
        public void Sweep_Destroyed_ReturnsConsumed()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 100;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimDestroyed"), 1);
            Register(thing, 10);
            thing.Destroy();

            Assert.Equal(SDS_ReclaimOutcome.Consumed, Sweep(100));
            Assert.Equal(0, Count);
        }

        [Fact]
        public void AuditRestored_DropsHandEntry_WhenJobGone()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 12;
            env.ReclaimWorld.JobExistsFn = _ => false;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimAuditHand"), 1);

            CoreTestKit.StaticInternal(ReclaimType, "TrackHand", MakeJob(4247), thing);
            AuditRestored();

            Assert.Equal(0, Count);
        }

        [Fact]
        public void AuditRestored_ReclaimsGroundEntry_WhenJobGone()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 13;
            env.ReclaimWorld.JobExistsFn = _ => false;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimAuditGround"), 1);

            CoreTestKit.StaticInternal(ReclaimType, "TrackJobTarget", MakeJob(4248), thing, 1);
            AuditRestored();

            Assert.Equal(0, Count);
        }

        [Fact]
        public void AuditRestored_KeepsGroundEntry_WhenJobAlive()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 14;
            env.ReclaimWorld.JobExistsFn = _ => true;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimAuditKeep"), 1);

            CoreTestKit.StaticInternal(ReclaimType, "TrackJobTarget", MakeJob(4249), thing, 1);
            AuditRestored();

            Assert.Equal(1, Count);
        }

        [Fact]
        public void Reset_ClearsEntries()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            env.ReclaimWorld.Tick = 11;
            Thing thing = CoreTestKit.Make(CoreTestKit.Def("ReclaimReset"), 1);
            Register(thing, 11);

            CoreTestKit.StaticInternal(ReclaimType, "Reset");
            Assert.Equal(0, Count);
        }
    }
}
