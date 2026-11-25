namespace SuperDimensionalStorage
{
    /// <summary>回收策略：生命周期判定不同，执行入口只有一个。</summary>
    public enum SDS_ReclaimPolicy
    {
        JobTarget,
        ProtectedOutput
    }

    /// <summary>回充失败后的退路策略（只描述"实物放哪"，不改变"先回充"这个唯一顺序）。</summary>
    public enum SDS_ReturnFallback
    {
        None,
        Ground,
        PawnInventory,
        PawnCarry
    }
}
