# 举火 · 2Dgame

> 2D 像素 · 王朝模拟经营 + 叙事策略 ｜ Godot 4.7.2 (.NET) + C# 12 / .NET 8 ｜ Windows / Linux · Steam

**一句话**：攻破旧朝只需要三十天，治理天下需要三十年。

序章 30 天（约 3 小时，占 12%）在 7 个可探索空间里搜、问、藏；转段 10 分钟定国号、立誓约；正篇 30 年 360 回合，朝会、度支、营造、军务、天下。玩家每回合只有 3 点行动力，永远不够——授权是唯一出路，而授权就是腐败与割据的起点。

---

## 现在到哪一步

| 模块 | 状态 |
|---|---|
| 序章探索（七空间、120 个可搜点、28 位 NPC、巡逻视野、敲门计时） | ✅ 可运行 |
| 程序化像素美术（地面/墙/屋顶/器物/四向行走图）、霞鹜文楷字体 | ✅ |
| 新手引导（不弹窗，按玩家真实行为推进五步） | ✅ |
| 刻预算、旗标、存档 | 🚧 施工中（S0） |
| 双信任、交易、攻城四法、誓约转段 | ⬜ 待做（S1–S2） |
| 正篇月循环、朝会奏报、财政、事件卡、编年史 | ⬜ 待做（S3） |
| 660 张事件卡、24 节点完整对白、40 条律法、8 区 | ⬜ 待做（M2–M3） |

单测 16 条全绿；120 件可搜物无一“够不着”。

---

## 仓库结构

```
仓库根/
├── Juhuo/                Godot 工程根（Src / Content / Assets / Tests / Juhuo.sln）
├── tools/                Python 数据管线（生成、校验、模拟、美术预览）
├── Docs/Rules/           系统策划的规则卡与数据契约（唯一数值源）
├── QA/                   QA 报告、缺陷票、平衡跑
├── agents/               多智能体协作：CANON / PROTOCOL / BOARD + 8 份角色提示词
└── 举火_*.md             设计文档（21 份）
```

**文档从哪看**：`README_文档索引.md` 里有全部文档的索引、依赖关系与已知缺口。

---

## 跑起来

```bash
# 编译与单测（在仓库根执行，路径带 Juhuo/ 前缀）
dotnet build Juhuo/Juhuo.sln
dotnet test Juhuo/Tests/Core.Tests/Core.Tests.csproj

# Godot 的绝对路径交给脚本解析，不依赖 PATH
GODOT="$(python tools/godot_path.py)"

# 关卡自检：120 件东西是不是都够得着
"$GODOT" --headless --path Juhuo --quit -- --audit

# 打开编辑器
"$GODOT" --path Juhuo
```

> `--quit` 必须写在 `--` 之前，否则会被当成游戏参数。
> 完整命令与调试参数见 `Juhuo/README.md`。

---

## 数据是脚本生成的，不是手改的

```bash
python tools/gen_prologue_data.py    # 生成 120 条可搜点 + 7 张地图（生成即校验）
python tools/validate.py             # 校验内容：越界/不可通行/重叠/空文本/引用缺失
python tools/validate.py --selftest  # 证明五类错都能被报出
python tools/sim_balance.py          # 30 年经济模拟与战斗推演
```

改内容要改 `tools/gen_prologue_data.py` 再重新生成。**直接改 `Content/data/*.csv` 会被下次生成覆盖**，历史上出过一次同格重叠。

---

## 多智能体开发

本仓库用 8 个智能体分工推进，规则写在 `agents/`：

- `CANON.md` —— 设定与约束的单一源（只读）
- `PROTOCOL.md` —— 消息格式、任务状态机、评审与反作弊
- `BOARD.md` —— 任务板，唯一状态源
- `prompts/` —— 制作人、系统、叙事、Core、玩法、美术、数据、QA 八份角色提示词

铁律三条：**一条路径只有一个写者**、**聊天不算数（要落到 BOARD/DECISIONS/交接单）**、**没有证据的完成等于没完成**。

---

## 许可与素材

- 引擎：Godot（MIT）｜ 字体：霞鹜文楷（SIL OFL 1.1）｜ 商业素材需逐个登记许可证
