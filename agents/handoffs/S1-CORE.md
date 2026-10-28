# S1-CORE · 序章规则层进 Core（知识门 / 催促 / 瘟疫 / 可达性）

- 状态：DONE
- 负责人：E1
- 日期：2026-10-28
- 依赖票：S0-04
- 覆盖票号：S1-01、S1-02、S1-11、S1-12、S1-13

## 交付

| 文件 | 内容 |
|---|---|
| `Core/Knowledge/FlagExpression.cs` | `requires` 求值：`has(x)`、`!`、`&`、`|`、括号；名字允许点号与下划线（D-017）；非法表达式返回 false 不抛 |
| `Core/Knowledge/KnowledgeGate.cs` | `GateResult(Open, Missing[])`，缺项列全给 UI 提示 |
| `Core/Time/Clock.cs`（改） | 新增 `NudgeAtHalf` / `PendingNudges` / `MarkNudged` / `Coverage`；催促步长 8 半刻（D-015），上限 5 |
| `Core/Prologue/Plague.cs` | `DeathsByKe = min(3, ke/4)`、`NextDeathAtKe`（S1-11：死亡节拍按刻算，不再按搜点数） |
| `Core/Map/MapGrid.cs` | 地形解析；`Ground/IsWalkable` 收 **0-based**，`WalkableCell` 收 **1-based** |
| `Core/Map/ReachAudit.cs` | `ReachReport(Total, Reachable, Unreachable)`；可传物件阻挡谓词 |
| `Application/FlagExpression.cs`（改） | 薄壳转调 Core，保留原签名，Game 层不用改 |

## 证据

```text
$ bash tools/dotnet.sh build Juhuo/Juhuo.sln --nologo
0 个警告 0 个错误
$ bash tools/dotnet.sh test Juhuo/Tests/Core.Tests/Core.Tests.csproj --nologo -v q
已通过! 失败: 0，通过: 158（含本票新增）
$ bash tools/smoke.sh
全部通过：9 项
```

## 偏差与裁定

| # | 偏差 | 裁定 |
|---|---|---|
| 1 | 可达性测试用"全部点当障碍"的极端谓词，因为 `PropKinds` 在 Game 层、Core 测试引用不到 | 接受；由此留下的盲区（真实阻挡规则没进测试）已在 Q-014 / S1-14 里排上——正解是让物件阻挡进数据 |
| 2 | 未知 hub 的 `ClockTable` 预算回落 8 刻 | 接受（规则卡 §边界） |

## 遗留

- **CI 只能守地形级可达性**；含物件的完整判定要等 `points.csv` 加 `blocks` 列（S1-14）。
- `Plague` 目前只有纯函数，Game 侧的死亡节拍还没接（S1-11 的接线部分由后续票做）。
