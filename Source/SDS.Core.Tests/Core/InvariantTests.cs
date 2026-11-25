using System;
using System.Reflection;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 回归（2026-11-12，E2 归位 Diag）：开发期不变量断言与自检入口。
    /// 断言只记 Error、不抛异常；真正的日志内容需开发者模式人工核对。
    /// </summary>
    public class InvariantTests
    {
        private static Type InvariantType
        {
            get { return typeof(SDS_Diag).Assembly.GetType("SuperDimensionalStorage.SDS_Invariants"); }
        }

        [Fact]
        public void Invariants_IllegalStates_DoNotThrow()
        {
            Assert.NotNull(InvariantType);
            // 测试宿主没有 Prefs.data，Prefs.DevMode 的 getter 直接返回 true（setter 会 NRE），
            // 因此这里天然走"开发者模式生效"分支，直接验证只记 Error、不抛异常。
            ThingDef def = CoreTestKit.Def("InvariantItem");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            CoreTestKit.StaticInternal(InvariantType, "AvailableWithinTotal", key, 10L, -1L);
            CoreTestKit.StaticInternal(InvariantType, "AvailableWithinTotal", key, 10L, 11L);
            CoreTestKit.StaticInternal(InvariantType, "TransactionStage", 1,
                SDS_WithdrawEndState.Delivered, SDS_WithdrawEndState.Reserved, "test");
            CoreTestKit.StaticInternal(InvariantType, "TransactionTerminal", 1,
                SDS_WithdrawEndState.Delivered, "test");
        }

        [Fact]
        public void Invariants_StatusText_ContainsRuleId()
        {
            Assert.NotNull(InvariantType);
            PropertyInfo status = InvariantType.GetProperty("StatusText",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(status);
            string text = (string)status.GetValue(null);
            Assert.False(string.IsNullOrEmpty(text));
            Assert.Contains("E2", text);
        }

        [Fact]
        public void SelfCheckRunLedgerChecks_DebugAction_IsDiscoverable()
        {
            MethodInfo method = typeof(SDS_SelfCheck).GetMethod("RunLedgerChecks",
                BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            bool hasDebugAction = false;
            foreach (object attribute in method.GetCustomAttributes(false))
            {
                if (attribute != null && attribute.GetType().Name == "DebugActionAttribute")
                {
                    hasDebugAction = true;
                    break;
                }
            }
            Assert.True(hasDebugAction);
            Assert.NotNull(typeof(SDS_SelfCheck).GetMethod("RunLoadReport",
                BindingFlags.Static | BindingFlags.Public));
        }
    }
}
