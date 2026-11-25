# 数据流转（主流程）

> 定位：本文只回答「数据从哪来、经过谁、落在哪、被谁消费」，用于快速理清模块间的数据形态与流向。
> **不定义规则**：判据见《RuleIndex.zh-CN.md》与各模块 `Rules.zh-CN.md`；字段 / 签名见各模块 `*_API.cs`、`Interfaces.zh-CN.md` 与《ModuleInterfaces.zh-CN.md》；启动顺序见《ModuleRules.zh-CN.md》九；补丁挂点见《PatchInventory.zh-CN.md》。冲突时以上述文档为准。

## 一、总链路

```text
                          写入
地图实物 Thing ─────存入入口─────▶ SDS_Ledger（唯一真相）
                                    │ 虚拟通道：SDS_StockEntry（key / count / 堆平均状态）
                                    │ 保真通道：真身 Thing（Core 私有 owner 持有）
                                    │
              ┌─────────────────────┼──────────────────────┐
              │ 只读口径            │ 派生可见性           │ 事务（唯一改账本）
              ▼                     ▼                      ▼
      SDS_Rules / SDS_Availability      SDS_MirrorService      SDS_Rules（流程层）
      （可用 / 可交易量）    （每图镜像 / 展示物）   （预留 → 物化 → 交付）
              │                     │                      │
              ▼                     ▼                      ▼
       UI / 交易 / 捐献 /     原版枚举 / Job / 装载    实物 → 手 / 容器 / 地面
       账单 / 资源汇总
```

四条主链：**① 实物 → 账本，② 账本 → 只读派生，③ 账本 → 实物，④ 存档 ↔ 运行期**。
账本只由「存入」与「取货事务」修改；只读口径与派生可见性单向流出。

## 二、存入：地图实物 → 账本

1. 入口：搬运数字化补丁、右键 / gizmo、第三方存入 Job、交易买入收口，全部收敛到 `SDS_Rules.TryDeposit`（第三方走 `SDS_API` 桥）。
2. 端点：常规入口先经 `SDS_Rules.SelectEndpoint(SDS_EndpointQuery)`（候选与可用性来自 `SDS_Host.Ports`，Vanilla 实现；P2 从 Core 迁出）；交易买入等“货已在内部”的入口使用直接端口，不再选端点。
3. 通道分流（判据见 `SDS.Rules/Rules.zh-CN.md` §一与 `SDS.Core/Rules.zh-CN.md` §五）：
   - **保真**：真身原样进入 Core 私有 owner，地图上不再保留实体；
   - **虚拟**：实物销毁，按 `SDS_StockKey` + 件数 + 堆平均状态（HP / 腐烂）入账。
4. 结果：账本数量与版本变化 → `StockVersion` 推进 → 镜像、展示物、曝光在下一轮重建。

```text
Thing ──(key, units, vitals)──▶ SDS_StockEntry ──▶ SDS_Ledger
Thing ──(保真)───────────────▶ Core 私有 owner（真身）
```

## 三、可见：账本 → 只读派生

- **可用量口径**：`SDS_Availability` 统一给出可用量 / 可交易量 / 失败分类；Api、UI、交易、捐献、账单补料都只读它。
- **物理镜像**：`SDS_MirrorService.SyncIfDirty` 由 `SDS_VanillaState.Tick` 周期驱动（周期与失效判定见 `SDS.Vanilla/Rules.zh-CN.md`）；版本变化在下一轮同步触发重建，无变化时 O(1) 返回。重建只做「账本快照 → 每张图的镜像 `Thing`（增 / 删 / 改数量）」。镜像表是**派生缓存**，任何流程都不得把它当账本读回。
- **展示物**：交易、装载、账单列表使用 `SDS_OutputUtility` 按 key 造出的展示物；其身份是创建时封存的 `SDS_StockKey`，不是实物本身。
- **曝光**：`SDS_Exposure` 统一产出可见性增量（口径以该实现为唯一来源），资源条 / 财富 / 可达性补丁只消费这个增量，不各自推导。

```text
SDS_StockEntry ──▶ 镜像 Thing（每图一份）──▶ Lister / 枚举 / Job 目标
              ──▶ 展示物 Thing ──────────▶ 交易 / 装载 / 账单候选
              ──▶ 数字增量 ──────────────▶ ResourceCounter / WealthWatcher / 可达性
```

## 四、取出：账本 → 实物

1. 调用方给出 `SDS_WithdrawPlan`（key、数量、模式、端点）；Job 路径先 `SDS_Rules.TryReserveForJob` 再 `BeginJobAdopt`。
2. `SDS_WithdrawTransaction.Begin` 预留（虚拟 / 保真各走各的预留），状态 `Reserved`。
3. 物化（三条出口共用同一事务）：
   - 虚拟：`TryWithdrawVirtual` 扣账 + `SDS_StockRules.MakeThing` 按 key 与堆平均状态造实物；
   - 保真：`ExecutePreservedExact` 按精确真身计划取出；
   - 镜像：先扣账，再由调用方注入的 `ISDS_MirrorPhysical`（`SDS_WithdrawPlan.physical`）把镜像转成真身（失败回账）；Rules 不读 `SDS_Host.MirrorPhysical`。
4. 状态 `Materialized`；交付完成走 `Deliver()`（只释放预留），搬运 / 交付由调用方经 `SDS_Host.Delivery` 完成（手 / 容器 / 地面）；未交付而结束则 `Rollback()` 按通道回充（虚拟 / 保真专用回退）。

```text
SDS_StockKey + 数量 ──▶ SDS_WithdrawPlan ──▶ 事务（预留） ──▶ List<Thing> ──▶ Delivery
```

状态机与失败语义的权威：`SDS.Rules/Interfaces.zh-CN.md`、`SDS.Rules/Rules.zh-CN.md`（W21 / W27）。

## 五、原版入口接线

| 场景 | 读侧（账本 → 原版） | 写侧（原版 → 账本） |
|---|---|---|
| 本地 / 远程交易、赠送 | 往 `Tradeable` 清单注入展示物（`SDS_TradeVirtual.Inject`） | 卖侧收口 `SwapDisplaysForReal` 把展示物换成等量真身；买入经 `TryTakeBoughtIntoGrid` 直接入账 |
| 通讯信标 / 捐献 / 发射 | `TradeUtility.AllLaunchableThingsForTrade` 追加展示物 | `TradeUtility.LaunchThingsOfType` 用账本补缺口后交还原版 |
| 运输舱 / 传送门装载 | `TransferableOneWay` 清单注入展示物与欠量行 | 欠量落 `SDS_TransportLoadSave`；取货 Job 物化后交原版装载 |
| 账单 / 工作派活 | `SDS_BillIngredientQuery` 读可用量补缺料；选 def / 缺口由 `SDS_Rules` 的补料规划器决定 | 补料计划交 Job 取料；存入 Job 再入账 |
| 搬运 / 手动存入 | — | 投递瞬间数字化；右键 / gizmo 显式存入 |
| 资源汇总 / 财富 / UI | 只读曝光与可用量 | — |

入口与挂点的完整清单见《PublicAPI.zh-CN.md》与《PatchInventory.zh-CN.md》，本文不复述。

## 六、第三方适配器（单向）

```text
Bootstrap 扫描 ISDSCompatAdapter（反射，无静态引用）
        └─▶ 适配器 Apply ──▶ SDS_API 桥 ──▶ SDS_Rules（取存 / 回充） / SDS_Core（只读 / 结构）
第三方 Mod 的容器 / 配方 / 界面 ──▶ 适配器回调（只提供事实）
```

- 第三方自有库存只作为事实来源（S16）；适配器不自行扣账、不建第二条写路径（E7）。
- 目标 Mod 只做反射探测；反向读取（展示 / 派活）也经 `SDS_API` 桥，不触碰 Core 实现。

## 七、存档 ↔ 运行期

- **写**：`SDS_GameComponent_Grid.ExposeData` → `SDS_Ledger.ExposeData`（账本）+ `SDS_VanillaState.ExposeData`（运行壳）+ `sdsReclaim`（回收账本，组件级节点）；节点名与 LookMode 见《ModuleInterfaces.zh-CN.md》八。镜像 / 展示物 / 曝光都是派生缓存，除迁移用的镜像壳外不落盘。
- **读**：组件构造的固定顺序见《ModuleRules.zh-CN.md》九第 7 步；`ExposeData` 只填内容、不重建 → `FinalizeInit` 输出一次载入报数。
- **恢复**：结构事实由建筑 `SpawnSetup` 重新登记；镜像表由 `SyncAll` 按账本重建；未解析的 key 保留 `defName` / `stuffName` 等待下次解析；回收账本经 `sdsReclaim` 导入（旧 `sdsProtectedOutputs` 已于 2026-11-16 退役，见《SDS.Vanilla/CompatibilityRetirement.zh-CN.md》）。

```text
存档 XML ◀──▶ SDS_GameComponent_Grid（壳） ◀──▶ SDS_Ledger + SDS_VanillaState
                                    派生缓存（镜像 / 展示物 / 曝光）只从账本重建
```

## 八、设置

```text
设置页 ──▶ SDS_Settings（ModSettings，落盘）──▶ ClampAll
       ──▶ SDS_SettingsApplier：建筑 Def 电力 + 场上 CompPowerTrader 重算
                              + 端点优先级通知 + 财富重算
```

设置只影响建筑 Def / 实例消费口径与可见性，不写账本。

## 九、跨流程不变式

- 账本是唯一真相；镜像、展示物、曝光、资源条全部单向派生，不回写。
- 事务 / 预留是唯一的“在途”状态：交付前不释放预留，回充只走通道专用路径。
- 第三方与对外 API 只经 Contracts 契约与 `SDS_Core` 门面，不接触实现类型。
