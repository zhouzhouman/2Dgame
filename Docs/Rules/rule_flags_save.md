# 规则：旗标与存档（flags / save）

**版本** 1 ｜ **票号** S0-02 ｜ **字段形状** 见 `schema_v2.md §C`
**上游** 数据表规范 v1.0 §5 / §7、CANON 10「延迟后果必须落地」、PROTOCOL「不塞第二套规则」

---

## 输入

| 名称 | 类型 | 范围 | 来源 |
|---|---|---|---|
| `name` | string(40) | 符合 §1 命名规范 | `points.set_flag`、事件卡 `effects`、代码 |
| `hub` | string(8) | `hub1`…`hub7` | `save_state.hub` |
| `flags`（全局） | map | ≤512 键，值仅 `true`/`false` | `save_state.flags` |
| `flags`（局部） | map | 每空间 ≤128 键 | `save_state.hubs[h].flags` |
| `known.clues` | string[] | 只增，≤512 条 | `save_state.known.clues` |
| `save_state.version` | int | `2`–`99` | `save_state.version` |

---

## 公式

```
eff(name, hub) = flags[name]  若 name ∈ flags
               = hubs[hub].flags[name]  若 name ∈ hubs[hub].flags
               = false  其余一切情况（缺失、空串、null、未知 hub）
```

写：

```
set(name)   → flags[name] = true   （幂等；重复 set 不改任何计数）
clear(name) → 删除 flags 中的 name  （等价于写 false；推荐删除，省体积）
```

**读一次公式就够**：全项目只有这一个取值入口，任何"某某情况下算它成立"的分支都是第二套规则，打回。

---

## 边界

- **缺失即 false**（硬规则）。没有"默认 true"的旗标，没有"未初始化"态。空串 `""`、`null`、`"0"`、`"false"` 一律读 `false`。
- **大小**：单档 `flags` ≤512 键，单空间局部 ≤128 键；超限拒绝写入并报错（防存档膨胀与拼写污染）。
- **键长**：`name` ≤40 字节；超长截断**不允许**，直接拒绝写入并打日志。
- **值域**：只允许布尔。想存数字（次数、进度、等级）→ 用独立字段（`known.npcs[].quest`、`caught_count`、`nudges`），**不许**把 `flag_x=3` 塞进 flags。
- **大小写**：全小写 ASCII。`Has_Shoe` 与 `has_shoe` 视为不同键，属**错误**，校验器直接报。
- **空值**：`set_flag` 列为空串 = 该点不置任何旗标（合法，不是错误）。
- **取整/除法**：本规则无算术。

---

## 例外

1. **存量 12 个旗标冻结**（§1.3）：`has_shoe`、`knows_shoe_inward`、`knows_lu_wenzhao`、`knows_11_banner`、`intel_seal_order`、`has_letter`、`has_deed`、`has_medicine`、`has_ash`、`took_temple_coin`、`knows_herb_path`、`path_backhill_open` **不改名**——它们已写进 120 行 CSV、正篇接口文档和数据表规范。改名成本 > 收益。
2. **`known.clues` 只增不删**：它是手记「记得」页的呈现缓存。`clear(has_shoe)` 之后，手记里"你翻过那口井"这句仍然在——**知识不能被撤回**，这也是设计要的味道（CANON 9）。clues 不参与任何判定，只参与显示。
3. **同一名字允许同时出现在全局与局部**：读取时全局胜；写入时必须显式指定作用域，**不许**"两边都写"。开发期（Debug 构建）遇到同名冲突打一条警告，正式构建静默按全局优先。
4. **转段不迁移 flags 名字**：序章 → 正篇 `tense` 改变，`flags` 原样继承，正篇只读这部分键，不再新增序章风格的名字。
5. **被打断不置旗标**：搜查被打断时 `set_flag` 不执行（`searched` 也不置），否则"被看见"会变成收益。

---

## 1 命名规范

### 1.1 形状

```
<域>_<对象>_<状态>          3 段
<域>_<对象>_<限定>_<状态>   4 段（对象需要二级限定，如 hub3_well_shoe_taken）
```

- 正则：`^[a-z][a-z0-9]*(_[a-z0-9]+){1,3}$`，≤40 字节。
- **只用下划线**。不用点号（`.` 在未来做路径查询/导出时会与 JSON 路径歧义）、不用短横（会被 CSV/参数解析吃掉）、不用中文。
- **域** ∈ `hub1`…`hub7` / `reg_<区拼音>` / `meta`（系统） / `ev`（事件派生）
- **状态** ∈ `taken has knows met opened closed done paid saved killed dead alive refused given sold seen failed`（封闭词表，新增需改本节）
- 反例：`HasShoe`（大写）、`has.shoe`（点号）、`shoe`（无域无状态）、`hub3_flag1`（无语义）

### 1.2 示例

| 旗标 | 域 | 读法 |
|---|---|---|
| `hub3_well_shoe_taken` | hub3 | 白幡村的井里那只鞋，拿了 |
| `reg_fengyuan_gate_opened` | 丰原 | 丰原城门开了（内应） |
| `meta_tutorial_done` | 系统 | 引导看过了 |
| `ev_s0_letter_paid` | 事件 | 那封信的账结了 |

### 1.3 豁免清单（冻结，不再新增此类写法）

`has_shoe` `knows_shoe_inward` `knows_lu_wenzhao` `knows_11_banner` `intel_seal_order` `has_letter` `has_deed` `has_medicine` `has_ash` `took_temple_coin` `knows_herb_path` `path_backhill_open`

> 这 12 个是"状态在前"的旧写法，与 §1.1 冲突。**以冻结换稳定**：已在数据与文档中落地，改名会同时打断 D2 的文本变体、E1 的知识门判定和正篇接口。

### 1.4 作用域怎么选

| 判据 | 作用域 | 存放位置 |
|---|---|---|
| 离开这个空间后还要读到 | 全局 | `flags` |
| 只在本空间内成立（门开没开、谁死没死） | 局部 | `hubs[h].flags` |
| 正篇要兑现（CANON 10） | 全局 | `flags` |
| 只影响一段文本变体 | 局部 | `hubs[h].flags` |

**默认写全局。**局部是优化，不是默认。

---

## 2 存档时机

必须存的 7 个点（其余时刻不写盘）：

1. 进入一个空间并完成装载（`Load(hub)` 末尾）。
2. 每次搜查结算之后（含被打断——打断也改了 `ke_half`）。
3. 每次对话结算之后（首次 / 再谈 / 赠礼）。
4. 离开空间结算之后（`ke_half += 4` 记账完再存）。
5. 催促触发、病人状态变化（`alive`→`dying`→`dead`）。
6. 12 个节拍中每个节拍结束；以及攻城战开始前。
7. 窗口关闭通知（`NOTIFICATION_WM_CLOSE_REQUEST`），同步写完再退出。

**禁止存盘的时刻**：蹲下 3 秒动画中途、对话选项未确认时、任何"半状态"（动作已开始未结算）。理由：读了这种档，玩家会回到一个既没扣刻也没拿到东西的中间态。

---

## 3 原子写入与坏档

### 3.1 写入序列（严格按序，任一步失败即中止并保留旧档）

```
1. 内存序列化 → 校验（必填键、旗标命名、inventory ≤12、flags ≤512）
2. 写 save.tmp（覆盖）→ flush → 关闭
3. 若 save.json 存在，复制为 save.bak（覆盖上一层 bak）
4. rename save.tmp → save.json   （同卷，原子）
```

**绝对禁止**：先删旧档再写；边玩边往文件追加；多线程/协程并发写；写一半就 `rename`。

### 3.2 坏档处理（四步，不许跳）

```
1. 解析失败 / 类型不符 / 必填键缺失 → 把文件改名 save.corrupt-YYYYMMDD-HHMMSS.json（不删、不改内容）
2. 尝试 save.bak：成功则载入，手记里写一句「你只记得上次歇脚的地方。」
3. bak 也坏 → 新开档；手记一句「前面的路，你记不清了。」；保留所有 .corrupt-* 供 Q1 分析
4. 任何情况下：不静默重置、不覆盖 .corrupt、不弹窗（弹窗违反"不打断"的美术规范）
```

- **不引入校验和**：第 4 步的 `rename` 已保证"整份或零份"；再加 CRC 是第二条防线，成本 > 收益。
- 玩家主动"重新开始"必须走单独菜单入口，且二次确认——**不是**坏档路径。

---

## 4 版本迁移

| 情形 | 处理 |
|---|---|
| `version == 当前` | 直接读 |
| `version < 当前` | 逐级 upcast（如 1→2→3），**禁止跳级**；每级是纯函数；成功后立即写回新版本，原档另存 `save.v<旧版本>` 保留 1 份 |
| `version > 当前` | **拒绝载入**，提示「这份记录来自更新的版本」；不许猜、不许降级读 |
| 迁移中途异常 | 走 §3.2 第 1 步（当坏档处理），保留半成品文件 |

v1 → v2 的逐键换算见 `schema_v2.md §D.2`（唯一一次现实迁移：`ke_used × 2 → ke_half`）。

---

## 验收

> 每条都可无头复现；判定为布尔，无误差。

| # | 用例 | 操作 | 期望 |
|---|---|---|---|
| 1 | **缺失即 false** | 存档 `flags` 里没有 `has_shoe`，读 `eff("has_shoe","hub3")` | `false`；阿禾第三层对话不出现；井里文本无变体 |
| 2 | **作用域隔离** | `hubs.hub3.flags.path_backhill_open=true`，`hubs.hub5.flags.path_backhill_open=false` | 在 hub3 读 `true`（可采药），在 hub5 读 `false` |
| 3 | **全局优先** | `flags.x=true` 且 `hubs.hub3.flags.x=false`，在 hub3 读 `x` | `true`；Debug 构建同时打一条同名警告 |
| 4 | **set 幂等** | 连续 `set(has_medicine)` 3 次 | 读 `true`；`flags` 键数不变；`inventory` 的 `medicine` 数量不变（旗标不计数） |
| 5 | **clear 语义** | `set(has_shoe)` → `clear(has_shoe)` → 读 | `false`；`known.clues` 里 `has_shoe` 对应条目**仍在**（只增不删） |
| 6 | **坏档回退** | 把 `save.json` 截断到 200 字节后启动 | 不崩；载入 `save.bak`；原档被改名为 `save.corrupt-*` 且内容未被修改 |
| 7 | **原子写** | 写完 `save.tmp` 后、`rename` 前强杀进程 | `save.json` 仍是上一份完整档，可正常载入；`save.tmp` 残留，下次启动时被覆盖 |
| 8 | **版本过新** | 把 `version` 改为 `99` | 拒绝载入并提示"版本过新"；**不**尝试读取；**不**新建档覆盖它 |
| 9 | **v1 迁移** | v1 存档 `hub3.ke_used=7` | 迁移后 `hubs.hub3.ke_half=14`、`ke_used=7` 刻、`searched` 顺序不变、`version=2` |

**敏感度**：本卡无系数。唯一会放大的规模项是 `flags` 键数——超过 300 键后存档体积与加载时间开始可见，是"旗标该合并了"的信号，不是调参。

---

## 可调参数

| 参数 | 默认 | 合理区间 | 说明 |
|---|---|---|---|
| `MAX_FLAGS` | 512 | 256–2048 | 单档旗标上限 |
| `MAX_FLAGS_PER_HUB` | 128 | 64–512 | 单空间局部旗标上限 |
| `BAK_KEEP` | 1 | 1–2 | 备份层数（多了没用，坏了照样坏） |
| 存档时机数量 | 7 | 5–9 | 加时机 = 更安全也更慢；7 个是"动作结算后"的最小完备集 |

---

## 待 P0 裁定

1. **12 个存量旗标是否在 v3 统一为三段式**：本卡默认冻结（§1.3）。若 P0 决定改名，需一张"旧名→新名"映射表 + 一次全量数据重导 + D2 文本变体回归。
2. **`known.clues` 与 `flags` 的双写**：本卡定为"clues 是只增的呈现缓存，不参与判定"。若希望 clues 参与判定（例如"你只是听说过"也算线索），会变成第二套状态源——不建议。
3. **`save.json` 与 `progress.cfg` 的关系**：本卡默认并存（`progress.cfg` 只管启动计数）。合并需 E2 改 `Progress.cs` 的读写路径。
4. **正篇是否沿用同一存档文件**：本卡按"同一文件、`tense` 字段区分"写。若正篇要分档（多君主/多周目），`save_state` 需要外层包一层 `slots`，属破坏性变更（v3）。
