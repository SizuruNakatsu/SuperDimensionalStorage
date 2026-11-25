using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 端点输入缓冲（存档类型锁定：SDS_EndpointBuffer）。
    /// 任何一次成功入缓冲都立刻尝试数字化；读档 / 非 Playing 阶段由端点的 Tick 兜底。
    /// </summary>
    public class SDS_EndpointBuffer : ThingOwner<Thing>
    {
        private readonly ISDS_StorageEndpoint endpoint;

        public SDS_EndpointBuffer(IThingHolder owner) : base(owner, false)
        {
            endpoint = owner as ISDS_StorageEndpoint;
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks)
        {
            // 先过硬门再入缓冲：端点离线 / 总开关关闭 / 筛选或容量拒绝时让投递失败，
            // 不要把货吞进缓冲再跨 tick 等恢复。
            if (!CanAcceptNow(item))
            {
                return false;
            }
            // 单件事务：禁止与缓冲里的旧内容合并；成功入缓冲后只处理这一件。
            bool added = base.TryAdd(item, false);
            if (added)
            {
                TryFlushItem(item);
            }
            return added;
        }

        public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks)
        {
            if (!CanAcceptNow(item))
            {
                return 0;
            }
            List<Thing> before = Count > 0 ? new List<Thing>(this) : null;
            int added = base.TryAdd(item, count, false);
            if (added > 0)
            {
                FlushNewItems(before);
            }
            return added;
        }

        /// <summary>
        /// 原版容器预检（ThingOwner.CanAcceptAnyOf → GetCountCanAccept）必须与 TryAdd 同口径，
        /// 否则 JobDriver 会在端点离线 / 筛选拒绝时预检通过、投递又静默失败。
        /// </summary>
        public override int GetCountCanAccept(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (!CanAcceptNow(item))
            {
                return 0;
            }
            return base.GetCountCanAccept(item, canMergeWithExistingStacks);
        }

        /// <summary>
        /// 缓冲层唯一接收门：正常游玩走 Accepts；读档 / 非 Playing 阶段放行存档残留。
        /// </summary>
        private bool CanAcceptNow(Thing item)
        {
            if (item == null)
            {
                return false;
            }
            if (Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
            {
                return true;
            }
            SDS_EndpointHandle handle = SDS_EndpointHandle.ForBuilding(endpoint);
            return handle != null && handle.Accepts(item);
        }

        private void FlushNewItems(List<Thing> before)
        {
            if (endpoint == null || Current.ProgramState != ProgramState.Playing
                || Scribe.mode != LoadSaveMode.Inactive)
            {
                return;
            }
            List<Thing> snapshot = new List<Thing>(this);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Thing thing = snapshot[i];
                if (thing == null || (before != null && before.Contains(thing)))
                {
                    continue;
                }
                SDS_EndpointUtility.FlushInputBufferItem(endpoint, thing);
            }
        }

        private void TryFlushItem(Thing item)
        {
            // 读档期间入缓冲的是存档内容，不能抢跑；这段时间由端点 Tick 兜底。
            if (endpoint == null || item == null
                || Current.ProgramState != ProgramState.Playing
                || Scribe.mode != LoadSaveMode.Inactive)
            {
                return;
            }
            SDS_EndpointUtility.FlushInputBufferItem(endpoint, item);
        }
    }
}
