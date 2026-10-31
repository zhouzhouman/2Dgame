namespace Juhuo.Core;

/// <summary>
/// 一个序章节拍：三十天里的一个必经剧情节点，对应 <c>Content/data/beats.csv</c> 的一行。
/// 只描述"什么时候该做什么"，不含结算；判定与推进在 <see cref="BeatMachine"/>。
/// </summary>
/// <param name="Id">节拍 id（如 b01），表内唯一，也是存档里记录完成情况的键。</param>
/// <param name="Name">节拍名（如"焚账"），给 UI 显示。</param>
/// <param name="Hub">期望发生的空间 hub1–hub7。**不锁空间**，玩家仍可自由走动。</param>
/// <param name="Day">最早可触发的天（1–30）。到得早不算完成；到得晚可以。</param>
/// <param name="Requires">前置旗标表达式，语法见 <see cref="FlagExpression"/>；空串 = 无条件。</param>
/// <param name="Sets">完成时置的旗标，CSV 里逗号分隔，可能为空。</param>
/// <param name="Trigger">触发方式（choice / knock / search），表现层按这个挑交互。</param>
/// <param name="Options">
/// 选择型节拍的选项文本，CSV 里用 <c>|</c> 分隔；非选择型为空数组。
/// 空数组不是错误——表示"这一拍还没有可选项文案"，表现层退化成单一的"继续"。
/// </param>
/// <param name="Note">设计备注，只给人看，不参与判定。</param>
public sealed record Beat(
    string Id,
    string Name,
    string Hub,
    int Day,
    string Requires,
    string[] Sets,
    string Trigger,
    string[] Options,
    string Note);
