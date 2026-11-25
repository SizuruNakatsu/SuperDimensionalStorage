using System.Collections.Generic;
using Verse;
using Verse.AI;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// W60 的纯数据部分：镜像槽收集、队列槽增删、引用判定。
    /// 结束 / 出队需要真实 Pawn，留给实机 A/B；这里只锁定不写 Invalid、队列与 countQueue 不错位。
    /// </summary>
    public class MirrorJobTargetTests
    {
        [Fact]
        public void CollectReferencedMirrors_ScansCoreSlotsAndQueues()
        {
            SDS_MirrorService.Reset();
            ThingDef def = CoreTestKit.Def("W60Mirror");
            Thing core = CoreTestKit.Make(def);
            Thing queued = CoreTestKit.Make(def);
            Thing real = CoreTestKit.Make(def);
            SDS_MirrorRegistry.Mark(core);
            SDS_MirrorRegistry.Mark(queued);
            try
            {
                Job job = new Job
                {
                    targetA = new LocalTargetInfo(core),
                    targetB = new LocalTargetInfo(real),
                    targetQueueA = new List<LocalTargetInfo> { new LocalTargetInfo(queued) },
                    targetQueueB = new List<LocalTargetInfo>()
                };

                List<Thing> refs = new List<Thing>();
                SDS_MirrorJobTargets.CollectReferencedMirrors(job, refs);

                Assert.Contains(core, refs);
                Assert.Contains(queued, refs);
                Assert.DoesNotContain(real, refs);
            }
            finally
            {
                SDS_MirrorRegistry.Unmark(core);
                SDS_MirrorRegistry.Unmark(queued);
            }
        }

        [Fact]
        public void ApplyQueueSlot_Null_RemovesTargetAndCountQueue()
        {
            SDS_MirrorService.Reset();
            ThingDef def = CoreTestKit.Def("W60Queue");
            Thing mirror = CoreTestKit.Make(def);
            Job job = new Job
            {
                targetQueueA = new List<LocalTargetInfo> { new LocalTargetInfo(mirror) },
                countQueue = new List<int> { 3 }
            };

            Assert.True(SDS_MirrorJobTargets.ApplyQueueSlot(job, job.targetQueueA, 0, null));
            Assert.Empty(job.targetQueueA);
            Assert.Empty(job.countQueue);
        }

        [Fact]
        public void ForEachSlot_VisitsCoreAndQueueSlotsOnce()
        {
            SDS_MirrorService.Reset();
            ThingDef def = CoreTestKit.Def("W60Slots");
            Thing a = CoreTestKit.Make(def);
            Thing b = CoreTestKit.Make(def);
            Thing c = CoreTestKit.Make(def);
            Job job = new Job
            {
                targetA = new LocalTargetInfo(a),
                targetB = new LocalTargetInfo(b),
                targetC = new LocalTargetInfo(c),
                targetQueueA = new List<LocalTargetInfo> { new LocalTargetInfo(a) },
                targetQueueB = new List<LocalTargetInfo> { new LocalTargetInfo(b) }
            };

            List<string> seen = new List<string>();
            SDS_MirrorJobTargets.ForEachSlot(job,
                delegate (LocalTargetInfo target, int slot, int subIndex)
                {
                    seen.Add(slot + ":" + subIndex);
                });

            Assert.Equal(new[] { "0:0", "1:0", "2:0", "3:0", "4:0" }, seen);
        }

        [Fact]
        public void SlotAwaitingRemap_OnlyDestroyedOrNullSlotsAreEligible()
        {
            SDS_MirrorService.Reset();
            ThingDef def = CoreTestKit.Def("W60Guard");
            Thing oldMirror = CoreTestKit.Make(def);
            oldMirror.Destroy(DestroyMode.Vanish);
            Thing real = CoreTestKit.Make(def);
            Job job = new Job
            {
                targetA = new LocalTargetInfo(oldMirror),
                targetB = new LocalTargetInfo(real),
                targetQueueA = new List<LocalTargetInfo>
                {
                    new LocalTargetInfo(oldMirror),
                    new LocalTargetInfo(real)
                }
            };

            Assert.True(SDS_MirrorJobTargets.SlotAwaitingRemap(job, SDS_MirrorJobSlots.A, 0));
            Assert.False(SDS_MirrorJobTargets.SlotAwaitingRemap(job, SDS_MirrorJobSlots.B, 0));
            Assert.True(SDS_MirrorJobTargets.SlotAwaitingRemap(job, SDS_MirrorJobSlots.QueueA, 0));
            Assert.False(SDS_MirrorJobTargets.SlotAwaitingRemap(job, SDS_MirrorJobSlots.QueueA, 1));
            Assert.True(SDS_MirrorJobTargets.SlotAwaitingRemap(job, SDS_MirrorJobSlots.C, 0));
            Assert.False(SDS_MirrorJobTargets.SlotAwaitingRemap(job, 99, 0));
        }

        [Fact]
        public void SetSlot_RewritesCoreAndQueueSlots()
        {
            SDS_MirrorService.Reset();
            ThingDef def = CoreTestKit.Def("W60SetSlot");
            Thing oldThing = CoreTestKit.Make(def);
            Thing newThing = CoreTestKit.Make(def);
            Job job = new Job
            {
                targetA = new LocalTargetInfo(oldThing),
                targetB = new LocalTargetInfo(oldThing),
                targetQueueA = new List<LocalTargetInfo> { new LocalTargetInfo(oldThing) }
            };

            Assert.True(SDS_MirrorJobTargets.SetSlot(job, SDS_MirrorJobSlots.A, 0, newThing));
            Assert.True(SDS_MirrorJobTargets.SetSlot(job, SDS_MirrorJobSlots.B, 0, newThing));
            Assert.True(SDS_MirrorJobTargets.SetSlot(job, SDS_MirrorJobSlots.QueueA, 0, newThing));
            Assert.Same(newThing, job.targetA.Thing);
            Assert.Same(newThing, job.targetB.Thing);
            Assert.Same(newThing, job.targetQueueA[0].Thing);
            Assert.False(SDS_MirrorJobTargets.SetSlot(job, 99, 0, newThing));
        }
    }
}
