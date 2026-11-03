using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 开场那段字的进度：第几句、翻没翻完、跳过了没有。
///
/// 规则只有三条，写在一处，免得 Main 里再散成三份：
///   · 一次一行，Enter 往前走；走到最后一行再按一次，这一段就收场；
///   · Esc 跳过 = 九行都记作 "看过"，这一趟不再放；
///   · 只有**新档**才放——读档进来的人，上一次已经看过了。
///
/// 纯 C#，不认 Godot：这样"第 7 行是不是老仆、带不带立绘"这类事能在单测里直接问。
/// </summary>
public sealed class IntroFlow
{
    private readonly IntroTable _table;

    /// <summary>
    /// 造一段开场。
    /// </summary>
    /// <param name="table">开场白表（intro.csv 的内存形式）。</param>
    public IntroFlow(IntroTable table)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    /// <summary>这一段的表。</summary>
    public IntroTable Table => _table;

    /// <summary>共几句。</summary>
    public int Count => _table.Count;

    /// <summary>现在停在第一句（0 起）。</summary>
    public int Index { get; private set; }

    /// <summary>现在这一句是第几句（1 起，给人看的）。</summary>
    public int Ordinal => Index + 1;

    /// <summary>现在这一句。</summary>
    public IntroLine Current => _table.All[Index];

    /// <summary>现在这一句就是最后一句。</summary>
    public bool IsLast => Index >= Count - 1;

    /// <summary>放完了（看完或跳过都算收场）。</summary>
    public bool IsFinished { get; private set; }

    /// <summary>九行都过了一遍：这一趟、或者这一局，不必再放。</summary>
    public bool Seen { get; private set; }

    /// <summary>
    /// 这一句该画谁的立绘：旁白、或者表里没有这个人的立绘，都返回 null。
    /// 立绘只在"有人站在那里"的时候才挂得上去。
    /// </summary>
    public string? PortraitName => Current.HasPortrait ? Current.Portrait : null;

    /// <summary>
    /// 翻到下一句。
    /// </summary>
    /// <returns>还有下一句返回 true；这一句之后收场返回 false（此时 <see cref="IsFinished"/> 为真）。</returns>
    public bool Advance()
    {
        if (IsFinished) return false;

        if (IsLast)
        {
            IsFinished = true;
            Seen = true;
            return false;
        }

        Index++;
        return true;
    }

    /// <summary>
    /// Esc：不看了。九行全部记作看过——下次进游戏不该再从第一句放起。
    /// </summary>
    public void Skip()
    {
        IsFinished = true;
        Seen = true;
    }

    /// <summary>
    /// 这一次启动要不要放开场。
    ///
    /// 三个条件缺一不可：从菜单选了「新的开始」、确实没读到旧档、这一趟还没放过。
    /// 读档进来的（<paramref name="loadedSave"/>）一律不放——他上次已经看过，
    /// 再放一遍就是拦着人不让走。
    /// </summary>
    /// <param name="newGame">是不是从「新的开始」进来的。</param>
    /// <param name="loadedSave">这一局有没有读到旧档。</param>
    /// <param name="seen">这一趟 / 这一局已经放过（跳过也算）。</param>
    public static bool ShouldPlay(bool newGame, bool loadedSave, bool seen) =>
        newGame && !loadedSave && !seen;
}

/// <summary>
/// <c>--intro</c> 的那九行：<c>[开场] 7/9 老仆（立绘）：「少爷。」</c>。
///
/// 打印与自检共用这一份，别在两边各拼一次字符串——
/// 拼法一漂，"打印出来的"和"验的"就不是同一件事了。
/// </summary>
public static class IntroReport
{
    /// <summary>每行的开头。</summary>
    public const string Prefix = "[开场]";

    /// <summary>
    /// 把整张表变成要打印的行：谁在第几句说了什么、这一句带不带立绘。
    /// </summary>
    /// <param name="table">开场白表。</param>
    public static IEnumerable<string> Lines(IntroTable table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));

        for (var i = 0; i < table.Count; i++)
        {
            var line = table.All[i];
            var who = line.IsNarration
                ? "旁白"
                : line.HasPortrait ? $"{line.Speaker}（立绘）" : line.Speaker;

            yield return $"{Prefix} {line.Seq}/{table.Count} {who}：{line.Text}";
        }
    }
}
