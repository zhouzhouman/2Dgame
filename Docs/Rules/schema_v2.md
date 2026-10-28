# 数据契约 v2（G0 schema 冻结）

**版本** 2 ｜ **票号** S0-02 ｜ **效力** 与设计文档 v0.2 同级，字段冲突以本文件为准
**写者** D1 ｜ **读依赖** 生成器 `tools/gen_prologue_data.py`（D3）、`Src/Core/*`（E1）、存档（E2）
**本文件只定义形状，不含数值平衡**（数值在规则卡与 `<日期>.md` 平衡报告里）

---

## 0 三条总则

1. **单一形状源**：任何表/存档字段只在这里定义一次。代码常量、CSV 表头、文档举例都必须能指回本节。
2. **整数优先**：不用浮点。百分比用千分比（`+200` = +20%），时间用刻的半数（半刻），速度用 0.1 格/秒。
3. **只加不改**：新增字段保持 `schema_version=2`；改语义/类型/删字段升版本并写迁移（见 §D）。

**通用类型约定**

| 记法 | 含义 |
|---|---|
| `int` | 32 位有符号整数，禁止小数、禁止空串 |
| `string(n)` | UTF-8，字节上限 n；不含换行、不含半角逗号（逗号必须由 CSV 引号包裹） |
| `enum` | 只允许列出的字面量，小写 ASCII |
| `bool` | CSV 里写 `1`/`0`；JSON 里写 `true`/`false` |
| `effects` | 效果串，语法见 §B.0 |
| `id` | 小写 ASCII + 数字 + 下划线，全表唯一 |

**CSV 编码**：UTF-8 **带 BOM**（沿用现状，Excel 与现有生成器依赖），`QUOTE_ALL`，`\n` 换行，禁止字段内换行。解析层负责剥 BOM（`header[0]` 直接 `Trim()` **剥不掉** `\uFEFF`）。

---

## A 现有两张表的 v2 字段

### A.1 `points.csv`（可搜点，序章 120 条）

| 字段 | 类型 | 可空 | 默认 | 取值 / 枚举 | 说明 |
|---|---|---|---|---|---|
| `schema_version` | int | 否 | 2 | `2` | 行级契约版本，逐行写 |
| `hub` | string(8) | 否 | — | `hub1`…`hub7`；正篇 `reg_*` | 所属空间 |
| `id` | string(8) | 否 | — | `^[a-z0-9]{2}_[a-z0-9]{2,3}$`，全局唯一 | 主键。`bf_01` / `bf_e01` / `h1_01` |
| `mark` | string(3) | 否 | — | `1`–`9` / `a`–`k` / `e01`–`e09` | 图符，**同一 hub 内唯一** |
| `x` | int | 否 | — | `1`…宽（1-based，左上为 1） | 格坐标 |
| `y` | int | 否 | — | `1`…高 | 格坐标 |
| `layer` | enum | 否 | `surface` | `surface` / `close` / `knowledge` | 明面 / 细看 / 知识门 |
| `name` | string(24) | 否 | — | 非空 | 编辑器显示名，不进游戏 |
| `requires` | string(64) | 是 | `""` | 旗标表达式：`f`、`!f`，`&` 连接，≤3 项 | 空 = 无条件；`layer=knowledge` 时必填 |
| `text_id` | string(32) | 是 | `""` | 文本表主键 | 非空则优先于内联文本；空 = 用内联 |
| `text_once` | string(360) | 条件必填 | — | ≤120 汉字 | `text_id` 为空时必填；首次搜查文本 |
| `text_repeat` | string(360) | 否 | — | ≤120 汉字，**不许空** | 第二次及以后。空值视为校验失败 |
| `loot` | string(64) | 是 | `""` | `item:n`，多项 `\|` 分隔，`n≥1` | 空 = 玩家得到的是信息（由 `set_flag` 承载） |
| `set_flag` | string(40) | 是 | `""` | 符合旗标命名（见 `rule_flags_save.md` §1） | 搜查成功后置位 |
| `watch_npc` | string(8) | 是 | `""` | 同 hub 内 `npcs.id` | 非空时，该 NPC 在视野内会打断搜查 |
| `cost` | int | 否 | 1 | `0`–`3` | 消耗刻数；空点与重搜同样计费 |

### A.2 `npcs.csv`（序章 30 行 / 28 个唯一人名，见 CANON 6-2）

| 字段 | 类型 | 可空 | 默认 | 取值 / 枚举 | 说明 |
|---|---|---|---|---|---|
| `schema_version` | int | 否 | 2 | `2` | 行级契约版本 |
| `hub` | string(8) | 否 | — | `hub1`…`hub7` | 所属空间 |
| `id` | string(8) | 否 | — | 大写字母 `A`–`H` 或 `n01`…，**(hub,id) 唯一** | 主键。`watch_npc`、存档 `known.npcs` 都引用它 |
| `name` | string(24) | 否 | — | 非空，同一 hub 内唯一 | 显示名 |
| `x` | int | 否 | — | `1`…宽 | 格坐标（巡逻者填出生点） |
| `y` | int | 否 | — | `1`…高 | 格坐标 |
| `kind` | enum | 否 | `static` | `static` / `pace` / `patrol` | 静态 / 踱步 / 巡逻 |
| `first` | string(1200) | 否 | — | 非空（哑巴 NPC 不许存在） | 首次对话全文 |
| `again` | string(1200) | 是 | `""` | 空 = 只说一次 | 重复对话 |
| `interrupt` | string(600) | 是 | `""` | 空 = 无变体 | **对话被 Esc 打断后，下次见面的第一句** |
| `search_interrupt` | string(600) | 是 | `""` | 空 = 不打断（视野内也不拦） | **搜查被他看见时的打断文本**（数据表规范 §2 五句） |
| `vision_range` | int | 否 | 0 | `0`–`6`，格 | `0` = 不视（病人、尸体） |
| `vision_deg` | int | 否 | 90 | `0`–`360` | 视锥角度 |
| `speed_ds` | int | 否 | 0 | `0`–`40`，0.1 格/秒 | 2.2 格/秒 → `22`；`0` = 不动 |
| `waypoints` | string(120) | 是 | `""` | `x,y;x,y;…`，≥2 点，首点=出生点 | 空 = 无巡逻 |
| `pause_ds` | int | 否 | 0 | `0`–`600`，0.1 秒 | 每个路点停留；`20` = 2.0 秒 |

### A.3 与当前表头的差异（这是 D3/E1 的施工清单）

| 表 | 当前表头 | v2 变化 | 性质 |
|---|---|---|---|
| points | `hub,id,mark,x,y,layer,name,requires,text_once,text_repeat,loot,set_flag,watch_npc,cost`（14 列，无 `schema_version`） | 新增 `schema_version`、`text_id` | 新增（向后兼容） |
| points | `cost` 在代码里是 `double`（`PointTable.GetDouble`） | 收紧为 `int` 刻 | 类型收紧（需改 `SearchPoint.Cost`） |
| points | `requires` 语义未定，可写任意串 | 收紧为单/双旗标表达式，≤3 项 | 语义收紧 |
| points | `mark` 未要求 hub 内唯一 | 强制 hub 内唯一 | 校验加强 |
| npcs | `hub,name,x,y,first,again,interrupt`（7 列，**无主键**） | 新增 `schema_version,id,kind,search_interrupt,vision_range,vision_deg,speed_ds,waypoints,pause_ds` | 新增 9 列 |
| npcs | 身份靠"字母"隐式对应（`watch_npc` 写 `A`/`G`/`H`，表里没有 `A`） | 显式 `id` 列，`watch_npc` 引用它 | **修 bug 级变更**：现状是巧合成立 |
| npcs | 视野/移速/路点只存在于 `World.BuildPatrol` 硬编码 | 落入表内，代码只读 | 消除第二份真相 |

> **不改的东西**：`loot` 的物品 id 字面量（`bread` / `coin` / `shoe`…）不改名；`text_once`/`text_repeat` 继续内联；表名不变。

---

## B 未来三张表的契约

### B.0 通用效果串（`effects`，四表共用）

一行文本，`;` 分隔若干条，每条 `目标+值` / `目标-值` / `目标=值`：

```
min+5;guan-10;flag.has_deed=1;inv.coin+14;ke+2
```

| 目标命名空间 | 合法键 | 单位 |
|---|---|---|
| `min` `guan` `yi` | — | 0–100 整数 |
| `shizhen` `morale` `tiandming` `action` `support` `oath` `baozhen` | — | 0–100 整数 |
| `res.` | `food coin wood stone iron cloth salt med horse intel` | 见 CANON §4 单位 |
| `inv.` | 物品 id | 件 |
| `flag.` | 旗标名 | `=1` / `=0` |
| `ke` | — | **半刻**（`ke+2` = 1 刻） |
| `pct.` | `tax_base hidden_rate output` | 千分比（`+200` = +20%） |

解析失败即整条 `effects` 作废并报错（不许静默跳过）。空串 = 无效果。

### B.1 `events`（事件卡，序章 60 + 正篇 600）

**形态**：JSONL（一行一卡），因为选项是嵌套结构，CSV 放不下。

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | string(12) | 否 | — | `ev_00001`，全局唯一 |
| `schema_version` | int | 否 | 2 | |
| `stage` | enum | 否 | — | `prologue` / `reign` |
| `deck` | enum | 否 | — | `court`（朝会）/ `disaster` / `war` / `diplo` / `hr` / `private` |
| `tier` | int | 否 | 1 | `1`–`3`，轻重；决定是否进朝会牌堆 |
| `weight` | int | 否 | 100 | `1`–`10000`，抽取权重（整数，不概率） |
| `once` | bool | 否 | true | 只触发一次 |
| `cooldown_days` | int | 否 | 0 | `0`–`3600`，同卡再触发最短间隔 |
| `trigger.day_min` / `day_max` | int | 是 | — | 序章 1–30；正篇用 `year_min`/`year_max`（1–30） |
| `trigger.region` | string(16) | 是 | `""` | `""` = 任意地区 |
| `trigger.flags_all` / `flags_any` / `flags_none` | string[] | 是 | `[]` | 三组旗标条件（与 / 或 / 非） |
| `trigger.res_min` | object | 是 | `{}` | `{"food":300}` 资源下限 |
| `trigger.trust_min` | object | 是 | `{}` | `{"min":40,"guan":30}` |
| `trigger.prev_events` | string[] | 是 | `[]` | 前序事件 id（须已进编年史） |
| `trigger.chance_permille` | int | 否 | 1000 | `0`–`10000`，万分比；必须显式给种子（`save_state.seed`） |
| `title` | string(24) | 否 | — | 卡面标题 |
| `body` | string(1200) | 是 | — | 80–200 字，第二人称手记体（CANON 8） |
| `options` | array | 否 | — | 2–4 条，见下 |
| `options[].id` | string(4) | 否 | — | `a`/`b`/`c`/`d` |
| `options[].text` | string(120) | 否 | — | 选项文案 |
| `options[].requires` | object | 是 | `{}` | 同 `trigger` 的子集（旗标/资源） |
| `options[].hint` | enum | 否 | `partial` | `none` / `partial` / `full`：代价预览显示程度（17.4） |
| `options[].effects` | effects | 是 | `""` | **即时**后果 |
| `delayed` | array | 是 | `[]` | **延迟后果**，CANON 10 的落点 |
| `delayed[].delay_days` | int | 否 | — | `30`–`180`（正篇换算 1–6 月） |
| `delayed[].effects` | effects | 否 | — | 到期结算 |
| `delayed[].event_id` | string(12) | 是 | `""` | 到期插一张后续卡；空 = 只结算 |
| `delayed[].chronicle` | string(120) | 是 | `""` | 到期写进编年史的一句 |
| `oath` | object | 是 | `null` | `{enabled,beneficiary,due_days,penalty}`，见 14.2 |
| `chronicle_key` | string(40) | 是 | `""` | 编年史条目键 |

**校验**：至少一条 `options[]` 的 `requires` 为空（保证有解）；`delayed` 非空时 `delay_days` 必填；`stage=prologue` 的 `delayed` 必须能被正篇消费（否则违反 CANON 10）。

### B.2 `laws`（律法条款，共 40 条）

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | string(12) | 否 | — | `law_0001` |
| `schema_version` | int | 否 | 2 | |
| `name` | string(24) | 否 | — | 《均田令》 |
| `code` | enum | 否 | — | `tian`(田制) / `shui`(税制) / `jun`(军制) / `guan`(官制) / `xing`(刑名) / `shang`(商税) / `zong`(宗室) / `xuan`(选举) |
| `tier` | int | 否 | 1 | `1`–`3`，解锁时代（对应 17.2 的四时代） |
| `effects` | effects | 否 | — | 收益 |
| `costs` | effects | 是 | `""` | 代价（士绅/民心/商路…） |
| `combo_with` | string[] | 是 | `[]` | 组合伙伴 id |
| `combo_effects` | effects | 是 | `""` | 组合成立时的追加效果（12.4「田籍相符」） |
| `conflicts_with` | string[] | 是 | `[]` | 互斥条款 |
| `unlock_year` | int | 是 | 0 | `0`–`30`；`0` = 开局可选 |
| `unlock_tech` | string(24) | 是 | `""` | 科技 id（技术四支） |
| `upkeep` | effects | 是 | `""` | 每回合维护（宗室封爵的供养费） |

**不设** `enforce_bias` 字段：所有条款统一走 `律法实效 = 条款效果 × (0.5 + 吏治/200)`（设计文档 20.3），单列开关等于第二套公式。

### B.3 `regions`（八区 + 旧京）

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | string(16) | 否 | — | `reg_fengyuan` … `reg_jiujing` |
| `schema_version` | int | 否 | 2 | |
| `name` | string(8) | 否 | — | 丰原 |
| `terrain` | string(24) | 否 | — | 地形描述 |
| `produce` | string(40) | 是 | `""` | `;` 分隔主产（粮/布/盐…） |
| `rare_item` | string(16) | 是 | `""` | 稀有品 id（解池盐印/百年楠木…） |
| `trust_entry` | enum | 否 | — | `shunmin`(顺民畏官) / `shangbang` / `zongzu` / `bianjun` / `chuanbang` / `buluo` / `tusi` / `liumin` |
| `garrison` | int | 否 | 0 | 守军人数（900–8000） |
| `capital_bonus` | string(24) | 是 | `""` | 作为首都的优势（UI 一句） |
| `capital_risk` | string(24) | 是 | `""` | 死穴 |
| `first_crisis` | string(12) | 是 | `""` | 正篇首场危机的事件 id（19.3） |
| `hidden_rate` | int | 否 | 0 | 隐户率，千分比（云梦 `400` = 40%） |
| `grain_output` | int | 是 | 0 | 每月产粮，石；`0` = 不产（铁门/朔原） |
| `north` | bool | 否 | 0 | 北方（冬季封路、粮耗 +50%） |
| `river` | bool | 否 | 0 | 通水运（运粮成本 ×1/4） |
| `pass` | bool | 否 | 0 | 关隘（攻方战力 ×0.6） |
| `adjacent` | string(120) | 是 | `""` | `;` 分隔相邻区 id，双向必须一致 |
| `capital_ok` | bool | 否 | 1 | 旧京为 `0`（不可攻略） |

**待填数值**：`grain_output`、`hidden_rate` 目前无出处，先留 `0`，由平衡票填。**不许在 schema 里编数**。

---

## C `save_state` 的 JSON 契约

**文件**：`user://save.json`（单一当前档），辅档 `save.bak`、`save.tmp`。全文 JSON，无注释。

```json
{
  "version": 2,
  "seed": 20260815,
  "day": 7,
  "tense": "prologue",
  "hub": "hub3",
  "player": { "x": 4.0, "y": 20.0 },
  "flags": { "has_medicine": true, "took_temple_coin": false },
  "inventory": [ { "item": "bread", "n": 1 }, { "item": "shoe", "n": 1 } ],
  "trust": { "prologue": { "min": 12, "guan": 0, "yi": 8 } },
  "known": {
    "clues": [ "knows_herb_path", "knows_shoe_inward" ],
    "npcs": [ { "hub": "hub3", "id": "A", "met": true, "again": 2, "gift": false, "quest": 1 } ]
  },
  "hubs": {
    "hub3": {
      "entered": true,
      "ke_half": 14,
      "searched": [ "bf_01", "bf_03", "bf_08" ],
      "flags": { "path_backhill_open": false },
      "patients": { "D": "alive", "E": "dying", "F": "dead" },
      "caught_count": 1,
      "nudges": 0,
      "patrol": { "G": { "wp": 2, "t": 13.4 }, "H": { "wp": 0, "t": 2.1 } }
    }
  }
}
```

| 字段 | 类型 | 默认 | 约束 | 迁移 / 说明 |
|---|---|---|---|---|
| `version` | int | 2 | `2`–`99` | 唯一版本号，见 §D |
| `seed` | int | 0 | `0`–`2^31-1` | 随机种子。同一输入 + 同一种子 = 同一输出（硬约束 3） |
| `day` | int | 1 | 序章 `1`–`30`；正篇 = 回合 `1`–`360` | 旧档无则 `1` |
| `tense` | enum | `prologue` | `prologue` / `reign` | 转段时改此值，其余字段原样保留 |
| `hub` | string(8) | `hub1` | `hub1`…`hub7`；正篇 `reg_*` | 存档时所在空间 |
| `player.x` / `player.y` | number | 出生点 | `1.0`–`宽`，2 位小数，1-based | 与运行时 `Vector2` 同单位；无 `player` 则用该空间出生点 |
| `flags` | object | `{}` | 键符合旗标命名，值仅 `true`/`false` | **全局作用域**，见 `rule_flags_save.md` |
| `inventory` | array | `[]` | ≤12 项（CANON 背包 12 格）；`n≥1`；同名合并 | 稀有品 `n` 恒 1；旧档无则空 |
| `trust` | object | `{}` | 键 = `prologue` 或 `reg_*`；值 `{min,guan,yi}` 各 `0`–`100` | 缺键读 0；序章全部记在 `prologue`，转段折算进首都区 |
| `known.clues` | string[] | `[]` | 手记「记得」条目 id，**只增不删** | 缺则空 |
| `known.npcs` | array | `[]` | 主键 `(hub,id)`；`met` bool、`again` int `≥0`、`gift` bool、`quest` int `0`–`3` | 兼容旧 `npc_state` |
| `hubs` | object | `{}` | 键 = 当前及曾进入的空间 | 未进入的空间不应出现 |
| `hubs.*.entered` | bool | false | — | |
| `hubs.*.ke_half` | int | 0 | `0`–`2B+16` | **旧 `ke_used` ×2** |
| `hubs.*.searched` | string[] | `[]` | `points.id`，**有序**（后搜的文本引用先搜的） | 不是位图 |
| `hubs.*.flags` | object | `{}` | **hub 局部作用域** | 与全局同名时全局优先 |
| `hubs.*.patients` | object | `{}` | 值 `alive`/`dying`/`dead` | 仅 hub3 |
| `hubs.*.caught_count` | int | 0 | `0`–`99` | 被发现次数，每 3 次一次盘问 |
| `hubs.*.nudges` | int | 0 | `0`–`5` | 已触发催促次数（防重放） |
| `hubs.*.patrol` | object | `{}` | `{wp:int, t:number}`，`t` 秒 | 仅在空间内的巡逻者 |

**读取规则**：缺字段补默认（不报错）；**额外**字段原样保留并写回（前向兼容）；类型不符按"坏档"处理（`rule_flags_save.md` §4）。

**转段（序章→正篇）**：`tense` 改 `reign`、`day` 归 1、`hub` = 首都 `reg_*`；`hubs` 保留只读；`flags`/`inventory`/`trust`/`known` 原样继承（CANON 2：只有一套数值）。

---

## D `schema_version` 与迁移

### D.1 规则

| 变更 | 版本 | 要求 |
|---|---|---|
| 末尾追加可空字段 | 保持 2 | 更新本文件字段表 + 生成器表头 |
| 追加枚举值 | 保持 2 | 必须在表内注明"追加值" |
| 放宽取值范围 | 保持 2 | — |
| **删字段 / 改字段语义 / 改类型 / 改单位 / 删枚举值 / 改主键 / 改默认值导致旧数据行为变化** | **升 3** | 写 `Migrate_v2_v3` 纯函数 + 至少 1 条回归用例 + 本文件迁移表 |
| 表改名 / 拆表 | 升 3 | 同上，另加 D3 重新导出 |

- 升版本**必须**同时改：本文件、`tools/gen_prologue_data.py` 表头、`PointTable`/`NpcTalk` 读取、`ProgressTests` 断言。
- 读到 `schema_version > 当前`：**拒绝加载并报"版本过新"**，不许猜、不许降级读。
- 读到 `< 当前`：逐级 upcast（2→3→4），**禁止跳级**。每级是纯函数：旧结构进、新结构出，不读运行时状态。

### D.2 v1 → v2 存档迁移表（唯一一次现实迁移）

| v1（YAML，数据表规范 §7） | v2 | 换算 |
|---|---|---|
| `hub3.ke_used` | `hubs.hub3.ke_half` | `×2` |
| `hub3.searched` | `hubs.hub3.searched` | 原序保留 |
| `hub3.flags` | `hubs.hub3.flags` | 原样 |
| `hub3.patients` | `hubs.hub3.patients` | 原样 |
| `hub3.patrol` | `hubs.hub3.patrol` | 原样 |
| `hub3.caught_count` | `hubs.hub3.caught_count` | 原样 |
| `hub3.npc_state.A.{met,gift,quest}` | `known.npcs[hub3,A].*` | `again` 补 0 |
| — | `version` | `2` |
| — | `seed` | `0` |
| — | `trust` | 全 0 |

**现状**：仓库里没有 v1 存档，只有 `user://progress.cfg`（`tutorial_done`/`last_hub`/`launches`，见 `Progress.cs`）。合并方案见文末待裁定 4。

---

## E 变更影响清单（只列，不改）

| # | 位置 | 影响 | 严重度 |
|---|---|---|---|
| 1 | `tools/gen_prologue_data.py:266` | 表头写死 14 列，需加 `schema_version`、`text_id` 与 npcs 的 9 列；`watch_npc` 从字母改为引用 `npcs.id` | 高 |
| 2 | `Juhuo/Src/Core/PointTable.cs:38-68` | 新增列需读取；`cost` 由 `GetDouble` 改 `GetInt`；需剥 BOM；需校验 `schema_version` | 高 |
| 3 | `Juhuo/Src/Core/SearchPoint.cs:31` | `Cost` 从 `double` 改 `int`；新增 `TextId`；`Requires` 变表达式 | 高 |
| 4 | `Juhuo/Src/Core/NpcTalk.cs` | 需读 `id`/`kind`/`vision_*`/`search_interrupt`；键从 `hub:x,y` 改为 `hub:id` | 高 |
| 5 | `Juhuo/Src/Game/World.cs:139-148` | 7 个预算常量加注释指回 `rule_time_clock.md §2`（唯一数值源） | 中 |
| 6 | `Juhuo/Src/Game/World.cs:119-121,321,542` | `KeUsed` → `ke_half`；`Math.Min` 封顶改为"可超预算到 `2B+16`"；重搜也计费；`Talk()` 需对重复对话收 1 刻 | 高 |
| 7 | `Juhuo/Src/Game/World.cs:584` `BuildPatrol` | 路点/视野/移速硬编码，应改为读 `npcs.waypoints` 等列 | 中 |
| 8 | `Juhuo/Src/Game/SceneConfig.cs:22-80` | NPC 坐标与 `npcs.csv` 重复（注释已自认"差一格就变摆设"）；v2 后应只读 CSV | 中 |
| 9 | `Juhuo/Src/Application/Progress.cs` | 3 键 cfg 需接入 `save_state`（或并存，见待裁定 4） | 中 |
| 10 | `Juhuo/Src/Game/Main.cs:525` | 副标题 `刻 x/y` 需按 `ke_half/2` 换算；引导文案（`Main.cs:737`）与"重搜收费"对齐 | 低 |
| 11 | `Juhuo/Tests/Core.Tests/{PointTableTests,ProgressTests,NpcTalkTests}.cs` | 表头/字段/键的断言会挂，需同步 | 高 |
| 12 | `tools/extract_texts.py`、`tools/extract_npc_dialogue.py`、`举火_文本表_v1.0.csv` | `text_id` 落地后需与文本表对齐（当前是反向抽表，方向相反） | 中 |
| 13 | `举火_序章数据表规范_白幡村_v1.0.md §8` | QA 第 7/8 条按"重搜收费、只催不逐"重写 | 低（文档票） |
| 14 | `举火_序章数据总表说明_v1.1.md` | 列说明过时 | 低（文档票） |

**不受影响**：`Assets/**`、`Scenes/**`、`project.godot`、`.csproj`（本契约不新增资源类型、不新增系统）。

---

## 待 P0 裁定

1. **`coin` 的单位**：`loot` 里 `coin:3` / `coin:14`。若按 CANON「钱 = 贯」则为 3 贯（≈37 石粮），与"香炉里三个钱"的叙事不符，疑为「文」。默认按 **文** 记，显示时换算。
2. **是否建 `hubs` 表**：预算/出生点/出口目前只有两处：本仓 `rule_time_clock.md §2`（常量）与 `SceneConfig.cs`（硬编码）。建议 v3 建表，本轮不建。
3. **12 个存量旗标是否按三段式改名**：默认 **不改**（已写入 120 行 CSV 与正篇接口文档），冻结为别名，见 `rule_flags_save.md §1.3`。
4. **`save_state` 与 `progress.cfg` 合并或并存**：默认并存 —— `progress.cfg` 退化为"启动计数"，游戏状态全进 `save.json`。
5. **重复搜查是否收费**：默认 **收费 1 刻**（否则"29 刻搜完 29 点"的算术不成立，白幡村的取舍设计失效）。当前实现不收费。
6. **基准分辨率冲突**：设计文档 §21.1 与关卡设计 §1.2 写 320×180，CANON 写 640×440。不影响本契约（坐标一律以格计），但需 P0 落一条 DECISIONS。
7. **赠礼矩阵（关卡设计 §4.3）无表**：v2 不建，暂由 `npcs` + 事件卡承载。

---

*数据契约 v2 结束 · 冻结于 G0 · 字段变更走 §D*
