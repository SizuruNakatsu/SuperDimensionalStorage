using System;
using Xunit;

namespace SuperDimensionalStorage
{
    public class StaticStateTests
    {
        private sealed class Counter : ISDSResettable
        {
            public int count;

            public void ResetStatics()
            {
                count++;
            }
        }

        private sealed class Thrower : ISDSResettable
        {
            public int count;

            public void ResetStatics()
            {
                count++;
                throw new InvalidOperationException("boom");
            }
        }

        [Fact]
        public void ResetAll_CountsSuccesses_AndContinuesAfterException()
        {
            int before = SDS_StaticState.RegisteredCount;
            Counter first = new Counter();
            Thrower boom = new Thrower();
            Counter last = new Counter();
            SDS_StaticState.Register(first);
            SDS_StaticState.Register(boom);
            SDS_StaticState.Register(last);

            int cleaned = SDS_StaticState.ResetAll();

            Assert.Equal(before + 2, cleaned);
            Assert.Equal(1, first.count);
            Assert.Equal(1, boom.count);
            Assert.Equal(1, last.count);
            Assert.Equal(before + 3, SDS_StaticState.RegisteredCount);
        }

        [Fact]
        public void Register_IgnoresNullAndDuplicates()
        {
            int before = SDS_StaticState.RegisteredCount;
            SDS_StaticState.Register(null);
            Counter item = new Counter();
            SDS_StaticState.Register(item);
            SDS_StaticState.Register(item);
            Assert.Equal(before + 1, SDS_StaticState.RegisteredCount);
        }
    }
}
