# S1-DIALOG · 对话分支、打断记忆、Hub1 敲门计时

- 状态：DONE（含 S1-DIALOG-b 可达性收口）
- 负责人：E2
- 日期：2026-10-28
- 覆盖票号：S1-05、S1-07

## 交付

| 文件 | 内容 |
|---|---|
| `Application/Dialogue.cs` | 对话分支：有 `cond`/`next`/`flag` 列就用，没列走原逻辑（现有 30 位 NPC 的行为不变）；打断记忆 `talk.<npc>.interrupted`；盘问入口要求 `talk.<npc>.allowed` |
| `Application/KnockTimer.cs` | Hub1 敲门按**刻**计时（读 `Clock.KeUsedKe`，不自己算），默认第 3 刻触发，只触发一次，存档后不重放 |
| `Game/Main.cs`（改） | 删除旧的 `AuditReach()`，`--audit` 改调 `Core/Map/ReachAudit`，传真实物件阻挡谓词 |
| `Game/World.cs`（改） | 删除重复的 `CanReach`，只保留 Core 一套可达性口径 |
| `Tests/Core.Tests/ReachWiringTests.cs` | 3 条：口径反证、hub4 东屋回归守卫、3×3 房 + 唯一门 + 阻谇点搬开即恢复 |

## 证据

```text
$ bash tools/dotnet.sh test Juhuo/Tests/Core.Tests/Core.Tests.csproj --nologo -v q
已通过! 失败: 0，通过: 158
$ bash tools/smoke.sh
全部通过：9 项
$ godot --headless --path Juhuo -- --audit
[自检] hub1 14/0 … hub4 28/0 … 合计 120 件，够不着 0 件   （exit 0）
```

## 这次真正修掉的东西

`--audit` 接上真实物件阻挡后，暴露出 **hub4 官署东屋被堂前鼓封死**：
整间屋只有 (24,9) 一个门格，进门唯一落脚格 (24,8) 上站着 `h4_07 堂前鼓`（原型 Rack，判挡路），
屋里的 `h4_03 官署廊下`、`h4_08 县丞的椅子` 永远搜不到。

- 旧的 Game 侧自检**不看物件**，一直报"120 件、够不着 0"，是假绿（基线报告已更正）。
- 修法：堂前鼓挪到 (25,8)（贴着屋内墙，不挡路）。修完 120/0。
- 回归守卫：`hub4官署东屋整间都够得着` 这条测试会盯住"鼓又站回门口"这类回退。

## 偏差

| # | 偏差 | 裁定 |
|---|---|---|
| 1 | 对话表的 `cond`/`next`/`flag` 列目前还不存在，分支走的是"有列就用"的空实现 | 接受；列由 D3 在 S1-08 补，补上即生效 |
| 2 | 测试引用不到 `PropKinds`，hub4 回归用了"全部点当障碍"的极端谓词 | 接受；正解见 S1-14 |

## 遗留

- `tools/smoke.sh` 的可达性判定曾被单 hub 明细行"骗过"（假绿），已收紧为匹配合计行 + 禁止任何 `[够不着]` 行，并用"把鼓挪回门里"做了一次负向验证。
