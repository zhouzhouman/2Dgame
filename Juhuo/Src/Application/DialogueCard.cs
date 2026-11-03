using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 对话框上那一张卡片：谁在说、说什么、左边挂谁的立绘。
///
/// 立绘是"有就挂、没有就空着"：<c>portraits.csv</c> 只收了 30 个人，
/// 巡逻的衙役、临时说话的过路人都在表外——他们照样得能说话，
/// 只是左边不出人，退回改造前那副"一行字"的样子。
/// </summary>
/// <param name="Title">框顶那一行（现在就是 <c>名字　　初见</c>）。</param>
/// <param name="Body">正文。</param>
/// <param name="PortraitNpc">左边要画谁；null = 不画人。</param>
public readonly record struct DialogueCard(string Title, string Body, string? PortraitNpc)
{
    /// <summary>这一张卡片左边有没有人。</summary>
    public bool HasPortrait => !string.IsNullOrEmpty(PortraitNpc);

    /// <summary>
    /// 从"说了什么 + 说话的是谁"拼一张卡片。
    /// </summary>
    /// <param name="head">世界给的那一行标题（带名字与初见/再谈）。</param>
    /// <param name="body">正文。</param>
    /// <param name="speaker">身边这个人叫什么；空 = 没有人在说话（翻东西翻出来的那一页）。</param>
    /// <param name="portraits">立绘表；null = 这一局没有立绘（照样不崩）。</param>
    public static DialogueCard From(
        string? head,
        string? body,
        string? speaker,
        PortraitTable? portraits)
    {
        string? npc = null;

        if (!string.IsNullOrEmpty(speaker)
            && portraits is not null
            && portraits.TryGet(speaker, out _))
        {
            npc = speaker;
        }

        return new DialogueCard(head?.Trim() ?? "", body ?? "", npc);
    }
}
