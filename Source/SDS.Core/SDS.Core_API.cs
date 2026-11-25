using System;
using System.Collections.Generic;
using Verse;

namespace SuperDimensionalStorage
{
    // ============================================================================
    // SDS.Core · 公开面（唯一 public 文件）
    // 规则：本程序集的 public 顶层类型与成员只允许出现在本文件；其余文件全部 internal
    //       （internal 类型内部的 public 成员不构成对外面）。
    // 本文件只做契约声明与一行转发，不承载领域决策；实现见 SDS_CoreImpl。
    //
    // P4 起 Core 只保留账本与数据原语；通道 / 容量 / 可用量 / 结构 / 芯片的公开门面在 SDS.Rules。
    //
    // 语义约定（消费方只需读本文件）：
    //   · 「纯读」= 不改变账本、不建事务、不推进版本号，可在任意 tick 调用；
    //   · 「失败」= 返回 false 且 out 参数为默认值；账本与实物保持调用前状态（整笔回滚）；
    //   · 「推进版本」= 对应 Version 自增，调用方缓存应失效；
    //   · 所有成员只在主线程调用，不保证并发安全。
    // ============================================================================

    /// <summary>
    /// 账本只读面：Vanilla / Api 只经此接口读账本，看不到 SDS_Ledger 的实现与写入口。
    /// 全部成员为纯读；可用量口径不在此接口上，统一走 SDS_Availability（准则三：规则只定义一次）。
    /// </summary>
    public interface ISDS_LedgerRead
    {
        /// <summary>该键的虚拟账面数量（件数）；纯读，未知键返回 0。</summary>
        /// <remarks>
        /// 入：key = 品类键，null 返回 0。出：虚拟账面件数。
        /// 区别：本方法只查虚拟表，**保真键恒返回 0**，保真数量请用 <see cref="GetPreservedCount"/>；
        ///       可用量再走 SDS_Availability（本接口不扣预留）。
        /// </remarks>
        long GetCount(SDS_StockKey key);

        /// <summary>该键当前被事务 / Job 预留的数量；纯读，不扣除、不清理过期预留。</summary>
        /// <remarks>
        /// 入：key = 品类键。出：全部归属预留之和。
        /// 区别：与 SDS_Ledger.GetReserved(jobLoadID, key) 不同，本方法不分归属；
        ///       过期预留由 SDS_Core.AuditReservations 释放，本模块的 Tick 不处理预留。
        /// </remarks>
        long GetReserved(SDS_StockKey key);

        /// <summary>该品类是否被玩家禁用；纯读。禁用只影响读侧口径，不删除账本数据。</summary>
        /// <remarks>
        /// 入：key = 品类键（null 视为未禁用）。出：true = 该键被禁用。
        /// 区别：与 SDS_Availability.GetFailure 不同，本方法只答「是否禁用」，不判断全局可用 / 数量不足。
        /// </remarks>
        bool IsForbidden(SDS_StockKey key);

        /// <summary>该实例是否仍由账本持有（保真通道）；纯读。</summary>
        /// <remarks>
        /// 入：thing = 待查实例，null 返回 false。出：true = 账本保真容器中仍有该引用。
        /// 区别：与 IsPreservedThingId 相比按对象核对（同 ID 重建的新实例不算命中）；
        ///       本方法只答「是否仍在容器里」；实物被 Destroy 时原版 holder 默认摘出，因此销毁后通常立即返回 false，
        ///       巡检移除只兜底异常残留。
        /// </remarks>
        bool IsPreserved(Thing thing);

        /// <summary>该 thingID 是否已由账本作为存活保真真身持有；纯读，用于读档去重（同一真身只能有一个所有者）。</summary>
        /// <remarks>
        /// 入：thingID = 实例 ID（≤0 恒 false）。出：true = 已持有。
        /// 区别：与 IsPreserved(Thing) 的区别是只看 ID，用于读档时的重复所有权判定。
        /// </remarks>
        bool IsPreservedThingId(int thingID);

        /// <summary>该键的保真真身件数（按 stackCount 计）；纯读。</summary>
        /// <remarks>
        /// 入：key = 保真品类键，null 返回 0。出：存活且有有效内容（<see cref="SDS_StockData.HasLiveContent"/>）的件数。
        /// 区别：与 GetCount 互补——GetCount 只查虚拟表，本方法只查保真索引。
        /// </remarks>
        long GetPreservedCount(SDS_StockKey key);

        /// <summary>取虚拟品类快照（最多 max 条，0 = 不限）；返回副本，调用方修改不影响账本。</summary>
        /// <remarks>
        /// 入：max = 条数上限，≤0 表示不限。出：新列表（可修改）。纯读。
        /// 区别：与 PreservedSnapshot 的区别是只含虚拟品类；本方法纯读，不重建派生索引，也不修改账本。
        /// </remarks>
        List<SDS_StockEntry> Snapshot(int max);

        /// <summary>取保真真身快照；返回副本。</summary>
        /// <remarks>
        /// 入：无。出：新列表；只含「存活且内容完整」的真身（与 GetPreservedCount 同源），
        /// 已销毁 / 内容失效项不出现在结果里；全部真身（含失效）走诊断通道 AppendAllPreservedOwnerThings。纯读。
        /// 区别：与 Snapshot 互补；条目 key 的 preserved 恒为 true。
        /// </remarks>
        List<SDS_StockEntry> PreservedSnapshot();

        /// <summary>取「未解析 def」条目快照；返回副本，供诊断与旧档核对。</summary>
        /// <remarks>
        /// 入：无。出：新列表；条目 thingDef 可能为 null（旧档 def 缺失）。
        /// 区别：与 Snapshot 的区别是只含无法解析的存量，正常玩法下应为空。
        /// </remarks>
        List<SDS_StockEntry> UnresolvedSnapshot();

        /// <summary>把全部虚拟键追加到列表；纯读，不清空目标列表。</summary>
        /// <remarks>
        /// 入：into = 目标列表（null 忽略）。出：无；只追加虚拟键。
        /// 区别：保真键用 CopyPreservedKeysForDef 或 PreservedSnapshot；
        ///       本方法与 CopyKeysForDef 的区别是「全部、追加」；后者按 def 过滤且**先清空**。
        /// </remarks>
        void CopyKeys(List<SDS_StockKey> into);

        /// <summary>把该 def 的虚拟品类键写入列表（清空后写入）；纯读。</summary>
        /// <remarks>
        /// 入：def = 目标 def；into = 目标列表。出：无；into 被清空后只含该 def 的虚拟键。
        /// 区别：与 CopyKeys 的「追加全部」相反；与 CopyPreservedKeysForDef 的区别是通道。
        /// </remarks>
        void CopyKeysForDef(ThingDef def, List<SDS_StockKey> into);

        /// <summary>把该 def 的保真品类键写入列表（清空后写入）；纯读。</summary>
        /// <remarks>
        /// 入：def = 目标 def；into = 目标列表。出：无；into 被清空后只含该 def 的保真键。
        /// 区别：与 CopyKeysForDef 的区别仅在通道；同 def 两通道互不覆盖。
        /// </remarks>
        void CopyPreservedKeysForDef(ThingDef def, List<SDS_StockKey> into);

        /// <summary>把当前存活保真真身追加到列表；纯读，不改变账本，不清空目标列表。</summary>
        /// <remarks>
        /// 入：into = 目标列表（null 忽略）。出：无；追加「存活且内容完整」的实物引用。
        /// 区别：与 PreservedSnapshot 的区别是「只要实物、不要计数配对」，且为追加语义；
        ///       已销毁 / 内容失效的真身不会出现在结果里（诊断走账本内部的全部快照）。
        /// </remarks>
        void AppendPreservedThings(List<Thing> into);

        /// <summary>账面数量版本；扣账 / 入账 / 保真集合变化 / 禁用切换时推进，用于读侧缓存失效。</summary>
        /// <remarks>
        /// 入：无。出：当前版本号（单调递增，可能回绕）。纯读。
        /// 区别：与 ReservationVersion / FilterVersion 分别对应「数量（含禁用与保真集合） / 预留 / 存入筛选」
        ///       三类变化，缓存须各看各的；品类禁用（SetForbidden）推进的是本版本，不是 FilterVersion。
        /// </remarks>
        int StockVersion { get; }

        /// <summary>预留版本；建 / 消费 / 释放预留及审计清理时推进。</summary>
        /// <remarks>
        /// 入：无。出：当前版本号。纯读。
        /// 区别：只反映预留集合变化，扣账本身推 StockVersion。
        /// </remarks>
        int ReservationVersion { get; }

        /// <summary>存入筛选版本；宿主存入筛选变化（NotifyDepositFilterChanged）时推进。</summary>
        /// <remarks>
        /// 入：无。出：当前版本号。纯读。
        /// 区别：只反映宿主筛选变化；品类禁用（SetForbidden）推进的是 StockVersion；
        ///       全局可用性变化由 SDS_Rules.Available + EndpointVersion 反映。
        /// </remarks>
        int FilterVersion { get; }
    }

    /// <summary>
    /// 账本生命周期面：只由存档壳 SDS_GameComponent_Grid 使用（构造 / 存读档 / tick）。
    /// 取得唯一入口是 SDS_Core.CreateLedger()。
    /// </summary>
    public interface ISDS_LedgerHost : ISDS_LedgerRead
    {
        /// <summary>存读档节点；节点名与字段名属存档锁定清单，改动即 L3。</summary>
        /// <remarks>入：无（字段名由实现固定）。出：无；读写 Scribe 节点，读档后账本内容被替换。
        /// 区别：与 Tick 的区别是本方法只在存档 / 读档时调用，且允许替换整个账本内容。</remarks>
        void ExposeData();

        /// <summary>每 tick 驱动一次：校验并移除失效保真真身、重试读档待恢复真身，不改变对外语义。</summary>
        /// <remarks>入：无。出：无；只清失效状态与推进版本，不主动物化 / 交付。
        /// 区别：与 ExposeData 的区别是运行期增量维护，不替换账本；
        ///       预留终老与回收巡检不在本方法内（由 SDS_Core.AuditReservations 与 Vanilla 负责）。
        /// </remarks>
        void Tick();
    }

    /// <summary>
    /// Core 唯一对外门面：只持有账本与数据写回，不产出玩法决定、不执行交付、不寻路、不写 UI、不打补丁。
    /// 判定层公开面（通道 / 容量 / 可用量 / 结构 / 芯片）在 SDS_Rules。
    /// </summary>
    public static class SDS_Core
    {
        /// <summary>当前账本只读面；未进档 / 未 Attach 时为 null。纯读。</summary>
        /// <remarks>入：无。出：只读接口或 null。区别：与 CreateLedger 的「新建」不同，本属性只暴露已附加实例。</remarks>
        public static ISDS_LedgerRead Ledger => SDS_CoreImpl.Ledger;

        /// <summary>
        /// 创建空账本；只允许 SDS_GameComponent_Grid 构造时调用一次。
        /// 不注册到 Core，需随后调用 <see cref="Attach"/>。
        /// </summary>
        /// <remarks>入：无。出：新账本（未附加、版本从 0 起）。区别：不触碰 Core 静态状态，可安全丢弃。</remarks>
        public static ISDS_LedgerHost CreateLedger() => new SDS_Ledger();

        /// <summary>
        /// 把账本交给 Core；只允许 SDS_GameComponent_Grid 构造时调用一次。
        /// 传 null 等价于未进档（门面调用按各自失败语义返回）。
        /// </summary>
        /// <remarks>入：value = CreateLedger 的产物，可 null。出：无；Core 静态持有该实例。
        /// 区别：本方法不登记静态清理、不触发巡检；读档后由宿主自行调用 AuditReservations 等。</remarks>
        public static void Attach(ISDS_LedgerHost value) => SDS_CoreImpl.Attach(value as SDS_Ledger);

        /// <summary>已用品类数（含保真品类）；纯读。</summary>
        /// <remarks>入：无。出：账本已占品类数；无账本时为 0。区别：与 TypeCapacity 的「上限」配对，这里按品类键计。</remarks>
        public static long UsedCapacity => SDS_CoreImpl.UsedCapacity;

        /// <summary>切换品类禁用（唯一写入口）；返回是否真正发生变化。</summary>
        /// <remarks>入：key = 品类键（thingDef 必填）；forbidden = 目标状态。出：true = 实际变化并推进版本。
        /// 区别：只改读侧口径，不动账面数据；与 NotifyDepositFilterChanged 的区别是后者只推版本、不改禁用集合。</remarks>
        public static bool SetForbidden(SDS_StockKey key, bool forbidden)
            => SDS_CoreImpl.SetForbidden(key, forbidden);

        /// <summary>把品质写回实物（负值 = 无品质概念）；展示物 / 代理 / 镜像共用这一条规则。不改账本。</summary>
        /// <remarks>入：thing = 目标实物，可空；qualityInt = 品质等级，&lt;0 表示不写品质（保留实物原值）。出：无。
        /// 区别：与 ApplyVitals 互补（品质 vs 血量 / 腐烂）；只作用于实物组件，不参与扣账。</remarks>
        public static void ApplyQuality(Thing thing, int qualityInt)
            => SDS_StockData.ApplyQuality(thing, qualityInt);

        /// <summary>把堆平均血量 / 腐烂进度写回实物（负值 = 不写）；不改账本。</summary>
        /// <remarks>入：thing = 目标实物，可空；hitPoints / rotProgress = 目标值，&lt;0 表示跳过该项。出：无。
        /// 区别：与 ApplyQuality 互补；只写能承载该状态的实物，缺组件时静默跳过。</remarks>
        public static void ApplyVitals(Thing thing, float hitPoints, float rotProgress)
            => SDS_StockData.ApplyVitals(thing, hitPoints, rotProgress);

        /// <summary>对预留账本做一次预留终老审计：按归属存活委托整组释放失效预留，由启动 / 周期巡检调用。</summary>
        /// <remarks>入：ownerAlive = 「该归属 id 是否仍存活」，可空（空 = 不删正 id；id ≤ 0 仍视为失效）。
        /// 出：无；按归属 id 整组释放预留并推进 ReservationVersion。不改账面数量、不写日志。
        /// 区别：Core 不认识 Job / Lord；判活事实由 Vanilla 注入。</remarks>
        public static void AuditReservations(Func<int, bool> ownerAlive)
            => SDS_CoreImpl.AuditReservations(ownerAlive);

        /// <summary>通知存入筛选变化：推进账本筛选版本。</summary>
        /// <remarks>入：无。出：无；不清除已有数据。
        /// 区别：与 SetForbidden 的「改禁用集合」不同，本方法只应对宿主筛选变化。</remarks>
        public static void NotifyDepositFilterChanged() => SDS_CoreImpl.NotifyDepositFilterChanged();

        /// <summary>通知品类分组口径变化：账本派生索引失效，下一次查询重建。</summary>
        /// <remarks>入：无。出：无；不重算存量。
        /// 区别：与 SetForbidden 等「数据变化」通知不同，本方法针对设置（是否按品质 / 材质分组）。</remarks>
        public static void NotifyCapacitySettingsChanged() => SDS_CoreImpl.NotifyCapacitySettingsChanged();

        /// <summary>各模块初始化时调用一次：登记 Core 自检提供者（E1）；重复调用忽略。</summary>
        /// <remarks>入：无。出：无；幂等。
        /// 区别：静态清理项随判定层迁入 SDS.Rules，由 `SDS_Rules.RegisterAll` 登记。</remarks>
        public static void RegisterAll() => SDS_CoreImpl.RegisterAll();
    }
}
