# SDS.Vanilla · 旧档兼容退役清单

> 定位：**兼容项台账**，不是规则正文。规则仍以 [Rules.zh-CN.md](Rules.zh-CN.md) §十 W26「旧档可读；数据不丢优先于格式不变」为权威；本表只登记「哪些分支在兼容哪个年代、什么时候可以删」，避免兼容代码无限增殖，也避免误删仍在窗口内的档。
> 与 [PatchInventory.zh-CN.md](../PatchInventory.zh-CN.md) 同性质：只做登记与对账，不定义玩法。

## 一、支持窗口

| 项 | 值 | 依据 |
|---|---|---|
| 承诺可读的最低版本 | **2.4** | 2026-10-26 21:54 定 |
| 早于窗口写出的档 | **不承诺可读**；能读则读，读不动只记一次 `Warn`，不为此新增兼容分支 | W26 只约束窗口内 |
| 窗口调整方式 | 只能由真人改本节；改窗口等于改兼容承诺，属 L3 | 《DesignPrinciples.zh-CN.md》E4 |

## 二、兼容项台账

判定列：**已退役** = 代码已删除，不再读取旧形态；**保留** = 窗口内档仍依赖（含「暂定日期」= 缺写出时间证据、按给定日期保守保留）；**待确认** = 仍缺证据且未给日期。

| # | 兼容项 | 旧形态 | 只对哪些档有意义 | 当前入口 | 证据 | 判定 |
|---|---|---|---|---|---|---|
| 1 | `sdsProtectedOutputs` | 旧 `MapComponent_SDS_ProtectedOutputs` 节点（`LookMode.Deep`） | 只对 **2026-10-22 之前**写出的档有意义（该日并入回收账本，之后只读不写） | 已删除（原 `MapComponent_SDS_ProtectedOutputs.ExposeData` → `StashLegacyProtectedOutputs` / `AdoptPendingLegacyProtectedOutputs`） | [PatchInventory.zh-CN.md](../PatchInventory.zh-CN.md) §五迁移表；本清单 2026-11-16 执行 | **已退役（2026-11-16）** |
| 2 | UI 旧聚合键 | `selectedKey` 可能是 `CategoryKeyOf` 归零前的「同 def + 同通道」聚合键 | 只对 **2026-10-25 之前**写出的档有意义 | 已删除（原 `Window_SuperSpace.TryNormalizeLegacySelection` / `MatchesLegacyAggregate`） | 本清单 2026-11-16 执行 | **已退役（2026-11-16）** |
| 3 | `SDS_StockKey` 旧字段名 | 存档字段 `thingDef` / `stuff`（现为 `defName` / `stuffName`） | **2026-11-13 之前**写出的档（暂定日期） | `SDS_StockKey.ExposeData` **只剩读取侧** | [SDS_Stock.cs](../../超维存储/Source/SDS.Contracts/SDS_Stock.cs) | **已停写（2026-11-16）**：读取保留至窗口过期 |
| 4 | `dataVersion` 缺省回落 `1` | 无版本节点的档按 1 读取 | 首版至今全部档 | `SDS_Ledger.ExposeData` | [SDS_Ledger.cs](../../超维存储/Source/SDS.Core/SDS_Ledger.cs)；[SDS.Core/Rules.zh-CN.md](../SDS.Core/Rules.zh-CN.md) | **保留**：升版前置锚点，不是兼容分支 |
| 5 | `sdsReclaim` 节点归属 | C8（2026-11-15）起改由 `SDS_GameComponent_Grid` 独占读写，节点名不变 | 窗口内全部档（新旧同一读路径） | `SDS_GameComponent_Grid.ExposeData` | [SDS.Core/Rules.zh-CN.md](../SDS.Core/Rules.zh-CN.md) §C8 迁移 | **保留**（窗口内） |
| 6 | 端点输入缓冲重复 | 2.5.2 写侧未把物品移出 `inputBuffer`，读档出现同 `thingID` 双副本 | **2.5.2 及更早**写出的档 | `SDS_EndpointUtility.PruneLoadedBuffer`（按 `thingID` 剔除） | [SDS.Vanilla/DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) §二.8；[SDS_EndpointUtility.cs](../../超维存储/Source/SDS.Vanilla/Endpoints/SDS_EndpointUtility.cs) | **保留**（2.5.2 晚于窗口起点） |
| 7 | `SDS_ReclaimSave.policy` 缺省 | 缺该字段时按 `JobTarget` 兜底 | **2026-11-13 之前**写出的档（暂定日期） | `SDS_ReclaimSave.ExposeData` | [SDS_Withdraw.cs](../../超维存储/Source/SDS.Contracts/SDS_Withdraw.cs) | **保留**（暂定 2026-11-13 起改变） |
| 8 | `sdsMirrors` 读档壳 | 旧镜像节点只销毁、不写账本，读后按账本重建 | **2026-11-13 之前**写出的档（暂定日期） | `SDS_VanillaState.ExposeData` | [SDS.Vanilla/Interfaces.zh-CN.md](Interfaces.zh-CN.md) 1.8 | **保留**（暂定 2026-11-13 起改变） |

## 三、退役顺序与前置

1. **第 3 项已停写（2026-11-16）**：保存侧的 `thingDef` / `stuff` 已删除；读取侧保留到 2026-11-13 之前写出的档退出窗口后再删，至少隔一个发布周期。
2. **先建版本锚点、再删结构兼容**：第 4 项不退役；但只有 `dataVersion` 真正开始写新值并产生迁移消费方后，才能安全删其它结构分支。
3. **每删一项必须跑存读档往返**：`SDS.Core.Tests`（账本 / 键字段）与 `SDS.Rules.Tests`（规则）是基线；第 1 项已于 2026-11-16 删除，回收账本回归改以 `sdsReclaim` 正常读写为准。
4. **删除属 L3**：`ExposeData` / `Scribe` 相关一律按《DesignPrinciples.zh-CN.md》E4 的 L3 处理；本次第 1 / 2 项（整体删除）与第 3 项（仅停写）均由真人于 2026-11-16 明确授权后执行，AI 只负责执行与取证。

## 四、不属本清单的项

以下常被误当成「旧档兼容」，但性质不同，**不得**登记进本表，也不得用本表的窗口判定去删：

| 项 | 真实性质 | 归属 |
|---|---|---|
| `SDS_Rules.ConvertPreservedGearToVirtual`（W34） | 规则 / 配置迁移，玩家开关触发，**每份新档都可能跑** | [SDS.Rules/Rules.zh-CN.md](../SDS.Rules/Rules.zh-CN.md) W34 |
| CE 的 `checkCooldownUntil` / `priorityCacheUntil` | 运行期静态缓存跨档（绝对 tick 回退），属 E1 清理 | [SDS_ModCompat_CE.cs](../../超维存储/Source/SDS.ThirdModCompat/SDS_ModCompat_CE.cs)、[SDS.Vanilla/Rules.zh-CN.md](Rules.zh-CN.md) §六 |
| `pendingLegacyProtectedOutputs` | 第 1 项的运行时暂存，随第 1 项一起退役 | 同第 1 项 |
| `depositFilter` / `sidebarUI` / `transportLoadRequests` 的空值补建 | 读档空值防御，不是旧格式 | [SDS.Vanilla/Rules.zh-CN.md](Rules.zh-CN.md) §五 |

## 五、维护约定

- 新增任何「只对旧档有意义」的分支，必须同时在本表加一行，否则按《DesignPrinciples.zh-CN.md》E3 视为未完成的 L2 变更。
- 退役一项后，把该行判定改为「已退役（日期）」，不要直接删行——保留历史，便于回溯玩家报告。
- 与 [Rules.zh-CN.md](Rules.zh-CN.md) §五存档规则、[DesignDecisions.zh-CN.md](DesignDecisions.zh-CN.md) §二「旧实现 → 新实现的差异与坑」交叉引用，不重复正文。
