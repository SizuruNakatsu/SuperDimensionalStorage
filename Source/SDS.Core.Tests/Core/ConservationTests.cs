using System;
using System.Collections.Generic;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 随机操作下的数量守恒：stock / reserved / available 始终与预期一致。
    /// 只走账本内部写入口（虚拟入账 / 扣账 / 预留 / 消费 / 释放），不依赖宿主行为。
    /// </summary>
    public class ConservationTests
    {
        [Fact]
        public void RandomVirtualOperations_KeepStockReservedAndAvailableConsistent()
        {
            CoreTestEnv env = CoreTestEnv.Create(capacity: 30);
            ISDS_LedgerHost ledger = env.Ledger;
            ThingDef def = CoreTestKit.Def("Conservation_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);

            Random random = new Random(20261116);
            long expectedStock = 0L;
            long expectedReserved = 0L;
            Dictionary<int, int> active = new Dictionary<int, int>();
            int nextTransaction = 1000;

            for (int step = 0; step < 300; step++)
            {
                int operation = random.Next(5);
                if (operation == 0)
                {
                    int count = random.Next(1, 6);
                    bool deposited = (bool)CoreTestKit.InvokeInternal(ledger,
                        "TryDepositVirtual", key, (long)count, null);
                    Assert.True(deposited);
                    expectedStock += count;
                }
                else if (operation == 1 && expectedStock > 0L)
                {
                    int count = random.Next(1, 6);
                    if (count > expectedStock)
                    {
                        count = (int)expectedStock;
                    }
                    bool debited = (bool)CoreTestKit.InvokeInternal(ledger,
                        "TryDebitVirtual", key, count);
                    Assert.True(debited);
                    expectedStock -= count;
                }
                else if (operation == 2)
                {
                    long available = Math.Max(0L, expectedStock - expectedReserved);
                    if (available > 0L)
                    {
                        int count = random.Next(1, 4);
                        if (count > available)
                        {
                            count = (int)available;
                        }
                        int id = nextTransaction++;
                        bool reserved = (bool)CoreTestKit.InvokeInternal(ledger, "TryReserve",
                            id, new List<SDS_StockKey> { key }, new List<int> { count }, null);
                        Assert.True(reserved);
                        expectedReserved += count;
                        active[id] = count;
                    }
                }
                else if (operation == 3 && active.Count > 0 && expectedStock > 0L)
                {
                    int chosen = -1;
                    foreach (int candidate in active.Keys)
                    {
                        chosen = candidate;
                        break;
                    }
                    int count = Math.Min(active[chosen], (int)expectedStock);
                    if (count > 0)
                    {
                        bool debited = (bool)CoreTestKit.InvokeInternal(ledger,
                            "TryDebitVirtual", key, count);
                        Assert.True(debited);
                        int consumed = (int)CoreTestKit.InvokeInternal(ledger,
                            "ConsumeReservation", chosen, key, count);
                        Assert.Equal(count, consumed);
                        expectedStock -= count;
                        expectedReserved -= consumed;
                        active[chosen] -= consumed;
                        if (active[chosen] == 0)
                        {
                            active.Remove(chosen);
                        }
                    }
                }
                else if (operation == 4 && active.Count > 0)
                {
                    int chosen = -1;
                    foreach (int candidate in active.Keys)
                    {
                        chosen = candidate;
                        break;
                    }
                    int count = active[chosen];
                    CoreTestKit.InvokeInternal(ledger, "ReleaseReservation", chosen);
                    expectedReserved -= count;
                    active.Remove(chosen);
                }

                Assert.Equal(expectedStock, ledger.GetCount(key));
                Assert.Equal(expectedReserved, ledger.GetReserved(key));
                long actualAvailable = (long)CoreTestKit.InvokeInternal(ledger, "GetAvailable", key);
                Assert.Equal(Math.Max(0L, expectedStock - expectedReserved), actualAvailable);
            }

            List<string> report = new List<string>();
            int bad = (int)CoreTestKit.InvokeInternal(ledger, "AppendLedgerInvariantReport", report);
            Assert.Equal(0, bad);
        }
    }
}
