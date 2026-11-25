using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 唯一取用门（S18）：设置开启时，非玩家派系不能把镜像 / 账本当成可取用来源。
    /// 规则只在这里定义；搜索补候选、镜像可达性、Job 目标物化、携带、容器接收与端点取出
    /// 全部只调用这里，不在各入口重复写阵营判断。
    /// 未知 actor（null）不新增拦截：实际取用入口都带 Pawn，保持原版口径。
    /// </summary>
    internal static class SDS_AccessRules
    {
        // RULE:S18
        internal static bool BlocksNonPlayer(Pawn pawn)
        {
            if (pawn == null || !SDS_Config.PlayerFactionOnlyAccess)
            {
                return false;
            }
            if (pawn.Faction != Faction.OfPlayer)
            {
                return true;
            }
            // 任务寄居者 / 任务帮手属于玩家阵营但没有“玩家控制”身份，按访客处理。
            return Find.QuestManager != null && pawn.IsQuestLodger();
        }
    }
}
