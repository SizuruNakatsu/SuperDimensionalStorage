using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// SDS_Diag 的唯一实现（internal）：异常去重（ErrorOnce）、行为回退节流（Fail / Warn）、上下文串（Ctx）。
    /// 公开面在 SDS.Diag_API.cs 的 SDS_Diag 门面，本文件不得出现 public。
    /// 高频路径只做字典查询 + 整数比较；上下文由调用方只在真正记录的分支构造。
    /// </summary>
    internal static class SDS_DiagImpl
    {
        private const int RepeatSuppressTicks = 600;

        private sealed class FailState
        {
            internal int count;
            internal int nextTick;
        }

        private static readonly Dictionary<string, int> seen = new Dictionary<string, int>();
        private static readonly Dictionary<string, FailState> fails = new Dictionary<string, FailState>();
        private static readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>同一「消息 + 调用点 + 异常类型」只完整记录一次；第 10 / 100 / 1000 次补一条重复警告。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ErrorOnce(string message, Exception exception)
        {
            if (message == null)
            {
                return;
            }
            string caller = CurrentCaller(3);
            string key = message + "|" + caller + "|"
                + (exception != null ? exception.GetType().FullName : "none");
            if (seen.TryGetValue(key, out int count))
            {
                count++;
                seen[key] = count;
                if (count == 10 || count == 100 || count == 1000)
                {
                    EmitWarning("[超维存储] 重复错误第 " + count + " 次（" + caller + "）："
                        + message + (exception != null ? "\n" + exception : string.Empty));
                }
                return;
            }
            seen[key] = 1;
            EmitError("[超维存储] " + message + "（调用点：" + caller + "）"
                + (exception != null ? "\n" + exception : string.Empty));
        }

        /// <summary>行为回退（Error 级）：只允许 Core / Vanilla 调用，Compat 不得调用。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Fail(string reason, string context = null, Exception exception = null)
        {
            if (!ShouldLogFail(reason))
            {
                return;
            }
            Write(reason, context, exception, true);
        }

        /// <summary>可自愈的异常情况（Warning 级）：每个 reason 在进程内只记一次。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Warn(string reason, string context = null, Exception exception = null)
        {
            if (string.IsNullOrEmpty(reason) || !warned.Add(reason))
            {
                return;
            }
            Write(reason, context, exception, false);
        }

        /// <summary>统一上下文串；只允许在失败分支调用。</summary>
        internal static string Ctx(Map map = null, Pawn pawn = null, Thing thing = null,
            Job job = null, string extra = null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
            if (map != null)
            {
                sb.Append("map=").Append(map.uniqueID);
            }
            if (pawn != null)
            {
                sb.Append(sb.Length > 0 ? " " : string.Empty)
                    .Append("pawn=").Append(pawn.LabelShort).Append('#').Append(pawn.thingIDNumber);
            }
            if (thing != null)
            {
                sb.Append(sb.Length > 0 ? " " : string.Empty)
                    .Append("thing=").Append(thing.def != null ? thing.def.defName : "?")
                    .Append('#').Append(thing.thingIDNumber);
                if (thing.stackCount > 1)
                {
                    sb.Append('x').Append(thing.stackCount);
                }
            }
            if (job != null)
            {
                sb.Append(sb.Length > 0 ? " " : string.Empty)
                    .Append("job=").Append(job.def != null ? job.def.defName : "?")
                    .Append('#').Append(job.loadID);
            }
            if (!string.IsNullOrEmpty(extra))
            {
                sb.Append(sb.Length > 0 ? " " : string.Empty).Append(extra);
            }
            return sb.ToString();
        }

        /// <summary>Error 级节流：同一 reason 600 tick 内只写一条，第 10 / 100 / 1000 次补一条。</summary>
        private static bool ShouldLogFail(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }
            int now = GenTicks.TicksGame;
            if (!fails.TryGetValue(reason, out FailState state))
            {
                state = new FailState { nextTick = now };
                fails[reason] = state;
            }
            state.count++;
            bool milestone = state.count == 10 || state.count == 100 || state.count == 1000;
            if (now < state.nextTick && !milestone)
            {
                return false;
            }
            state.nextTick = now + RepeatSuppressTicks;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Write(string reason, string context, Exception exception, bool error)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return;
            }
            fails.TryGetValue(reason, out FailState state);
            int count = state != null ? state.count : 1;
            bool milestone = count == 10 || count == 100 || count == 1000;

            string caller = CurrentCaller(4);
            string line = "[超维存储][" + reason + "]"
                + (string.IsNullOrEmpty(context) ? string.Empty : " " + context)
                + (milestone ? "（第 " + count + " 次）" : string.Empty)
                + "（调用点：" + caller + "）"
                + (exception != null ? "\n" + exception : string.Empty);
            if (error)
            {
                EmitError(line);
            }
            else
            {
                EmitWarning(line);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string CurrentCaller(int skip)
        {
            try
            {
                MethodBase method = new StackFrame(skip, false).GetMethod();
                if (method == null)
                {
                    return "?";
                }
                Type type = method.DeclaringType;
                return (type != null ? type.FullName + "." : string.Empty) + method.Name;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        // 日志落点受 Unity 宿主约束：无宿主（单测 / 早期加载）时日志不可用，
        // 诊断自身不得反过来中断业务，因此这里吞掉写入失败。
        // 真正的 Verse.Log 调用放到 NoInlining 的 Core 方法里：JIT 解析 Assembly-CSharp
        // 发生在 try 之内，加载失败可被捕获；否则 Emit 方法自身 JIT 就会抛出。
        internal static void EmitError(string line)
        {
            try { EmitErrorCore(line); }
            catch (Exception) { }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void EmitErrorCore(string line)
        {
            Log.Error(line);
        }

        internal static void EmitWarning(string line)
        {
            try { EmitWarningCore(line); }
            catch (Exception) { }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void EmitWarningCore(string line)
        {
            Log.Warning(line);
        }

        internal static void EmitInfo(string line)
        {
            try { EmitInfoCore(line); }
            catch (Exception) { }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void EmitInfoCore(string line)
        {
            Log.Message(line);
        }
    }
}
