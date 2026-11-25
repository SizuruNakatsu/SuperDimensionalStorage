using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// W30：默认交付形态 = Hand，派活期不再猜 1 件；逃生门（显式 SpawnedTarget）保留瓶喂缺口取。
    /// W56：可用量夹取唯一实现（0 即不取）。
    /// </summary>
    public class TripCountTests
    {
        [Fact]
        public void WithdrawDeliveryPolicy_DefaultMode_IsHand()
        {
            SDS_WithdrawDeliveryPolicyDef def = new SDS_WithdrawDeliveryPolicyDef();
            Assert.Equal(SDS_WithdrawMode.Hand, def.mode);
        }

        [Fact]
        public void MirrorReservation_NonMirrorTarget_IsNotMirror()
        {
            Assert.False(SDS_MirrorReservation.IsMirrorTarget(default(LocalTargetInfo)));
        }

        [Theory]
        [InlineData(30, 50L, 30)]
        [InlineData(50, 30L, 30)]
        [InlineData(30, 0L, 0)]
        [InlineData(30, -1L, 0)]
        [InlineData(0, 10L, 0)]
        public void ClampToAvailable_ClampsWantByAvailable(int want, long free, int expected)
        {
            Assert.Equal(expected, SDS_Availability.ClampTakeToAvailable(want, free));
        }

        /// <summary>W46：装载空手规划口径（携带上限），上限不可用（≤ 0）时不派活。</summary>
        [Theory]
        [InlineData(0, 75, 0)]
        [InlineData(-1, 75, 0)]
        [InlineData(100, 75, 75)]
        [InlineData(30, 75, 30)]
        [InlineData(30, 0, 0)]
        [InlineData(30, -5, 0)]
        public void ClampCarry_ClampsNeedByFullCapacity(int need, int carrySpace, int expected)
        {
            Assert.Equal(expected, SDS_TransportVirtual.ClampCarry(need, carrySpace));
        }
    }
}
