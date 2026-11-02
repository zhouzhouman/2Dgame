using System;
using System.Collections.Generic;
using System.Linq;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 通关跑批的**驱动与报表**（票号 S1-09）。
///
/// 这一层不知道什么叫"按 E"、什么叫"走出口"——它只按节拍表的顺序，一拍一拍地
/// 把一个 <see cref="Func{Beat,String}"/> 叫起来；每一拍怎么走，由 Game 那一层用真实入口实现。
/// 这样十二拍的顺序、失败即停、耗时汇总都能在 Godot 外面测（不读地图、不起引擎）。
///
/// 输出格式钉在这里，Main 只负责 Print：格式改一次，测试和命令行一起变，不会两处漂。
/// </summary>
public static class ProloguePlaythrough
{
    /// <summary>所有行共用的抬头。</summary>
    public const string Prefix = "[通关]";

    /// <summary>节拍名那一列按显示宽度对齐（汉字算两格）。</summary>
    public const int NameColumn = 12;

    /// <summary>
    /// 按表序跑完十二拍。某一拍返回了失败原因就立刻停下——后面的拍前置不满足，
    /// 硬跑只会把真实原因埋掉。
    /// </summary>
    /// <param name="beats">节拍运行器（日推进、完成判定都在它身上）。</param>
    /// <param name="act">怎么完成这一拍；返回 null 表示成功，返回一句话表示为什么没成。</param>
    /// <param name="clock">这一刻这一拍所在空间用了多少刻 / 预算多少；不给就记 0/0。</param>
    /// <returns>跑到哪里、停在哪里、每一拍那一行要用的数据。</returns>
    public static PlaythroughResult Run(
        PrologueBeats beats,
        Func<Beat, string?> act,
        Func<Beat, (int Used, int Budget)>? clock = null)
    {
        if (beats == null) throw new ArgumentNullException(nameof(beats));
        if (act == null) throw new ArgumentNullException(nameof(act));

        var table = beats.Table;
        var result = new PlaythroughResult { Total = table.Count };

        for (var i = 0; i < table.Count; i++)
        {
            var beat = table.All[i];

            // 日子要取"做这一拍的时候"：Complete 之后 PrologueBeats 会把日历推到下一拍的最早天，
            // 拿完成后的天数写日志，b02 就会写成第 3 天——它明明是在第 1 天敲的门。
            var day = beats.Day;
            var why = act(beat);

            if (why != null)
            {
                result.FailedNo = i + 1;
                result.FailedBeat = beat;
                result.Failure = why;
                break;
            }

            var (used, budget) = clock?.Invoke(beat) ?? (0, 0);
            result.Steps.Add(new PlaythroughStep(i + 1, beat.Id, beat.Name, beat.Hub, day, used, budget));
        }

        result.Day = beats.Day;
        return result;
    }

    /// <summary>
    /// 一拍的日志行：<c>[通关] 1/12 b01 焚账        hub1 第1天 刻 3/8</c>。
    /// </summary>
    /// <param name="step">跑批记下的一拍。</param>
    /// <param name="total">表里一共几拍。</param>
    /// <returns>一行字。</returns>
    public static string StepLine(PlaythroughStep step, int total)
    {
        if (step == null) throw new ArgumentNullException(nameof(step));

        return $"{Prefix} {step.No}/{total} {step.BeatId} {PadDisplay(step.Name, NameColumn)}"
             + $"{step.Hub} 第{step.Day}天 刻 {step.KeUsed}/{step.KeBudget}";
    }

    /// <summary>
    /// 停住那一拍的行：<c>[通关] 3/12 b03 第一口锅    hub2 停住：选项面板没有弹出来</c>。
    /// </summary>
    /// <param name="result">跑批结果（必须已失败）。</param>
    /// <returns>一行字。</returns>
    public static string FailureLine(PlaythroughResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (result.Ok) throw new InvalidOperationException("没有失败，不该打停住行");

        var beat = result.FailedBeat!;
        return $"{Prefix} {result.FailedNo}/{result.Total} {beat.Id} {PadDisplay(beat.Name, NameColumn)}"
             + $"{beat.Hub} 停住：{result.Failure}";
    }

    /// <summary>
    /// 把七个空间的实际用刻与预算摆成一行：<c>hub1 5/8  hub2 2/10  …</c>。
    /// 预算只查 <see cref="ClockTable"/>，这里不抄第二份数字。
    /// </summary>
    /// <param name="usedByHub">每个空间实际用了几刻；没进过的按 0 算。</param>
    /// <returns>合计与分空间两行所需的数。</returns>
    public static PlaythroughBudget Sum(IReadOnlyDictionary<string, int> usedByHub)
    {
        if (usedByHub == null) throw new ArgumentNullException(nameof(usedByHub));

        var parts = new List<string>(ClockTable.Hubs.Count);
        var total = 0;
        var over = 0;

        foreach (var hub in ClockTable.Hubs)
        {
            var budget = ClockTable.ForHub(hub) ?? 0;
            var used = usedByHub.TryGetValue(hub, out var v) ? v : 0;

            total += used;
            if (used > budget) over++;
            parts.Add($"{hub} {used}/{budget}");
        }

        return new PlaythroughBudget(total, ClockTable.TotalBudgetKe, over, parts);
    }

    /// <summary><c>[通关] 合计：12/12 拍，第 30 天，用刻 16（预算 88，超支 0 空间）</c>。</summary>
    /// <param name="result">跑批结果。</param>
    /// <param name="budget">汇总。</param>
    /// <returns>一行字。</returns>
    public static string TotalLine(PlaythroughResult result, PlaythroughBudget budget)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (budget == null) throw new ArgumentNullException(nameof(budget));

        return $"{Prefix} 合计：{result.Steps.Count}/{result.Total} 拍，第 {result.Day} 天，"
             + $"用刻 {budget.TotalUsed}（预算 {budget.TotalBudget}，超支 {budget.OverHubs} 空间）";
    }

    /// <summary><c>[通关] 分空间：hub1 5/8  hub2 2/10  …（实际/预算）</c>。</summary>
    /// <param name="budget">汇总。</param>
    /// <returns>一行字。</returns>
    public static string HubLine(PlaythroughBudget budget)
    {
        if (budget == null) throw new ArgumentNullException(nameof(budget));

        return $"{Prefix} 分空间：{string.Join("  ", budget.PerHub)}（实际/预算）";
    }

    /// <summary>
    /// <c>[通关] 结论：可以通关</c>；没走完就说停在哪一拍、为什么。
    /// </summary>
    /// <param name="result">跑批结果。</param>
    /// <returns>一行字。</returns>
    public static string ConclusionLine(PlaythroughResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        if (result.Ok) return $"{Prefix} 结论：可以通关";

        var beat = result.FailedBeat!;
        return $"{Prefix} 结论：不能通关（第 {result.FailedNo} 拍 {beat.Id} {beat.Name}：{result.Failure}）";
    }

    /// <summary>
    /// 按显示宽度补空格：一个汉字占两格。中英混排时不补齐，列就对不上。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="width">目标显示宽度。</param>
    /// <returns>补好空格的文本；本来就够宽就原样返回。</returns>
    public static string PadDisplay(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return new string(' ', width);

        var w = DisplayWidth(text);
        return w >= width ? text : text + new string(' ', width - w);
    }

    /// <summary>显示宽度：CJK / 全角算 2，其余算 1。</summary>
    /// <param name="text">原文。</param>
    /// <returns>占几格。</returns>
    public static int DisplayWidth(string text)
    {
        var w = 0;
        foreach (var c in text)
            w += IsWide(c) ? 2 : 1;
        return w;
    }

    private static bool IsWide(char c) =>
        (c >= 0x1100 && c <= 0x115F) ||
        (c >= 0x2E80 && c <= 0xA4CF) ||
        (c >= 0xAC00 && c <= 0xD7A3) ||
        (c >= 0xF900 && c <= 0xFAFF) ||
        (c >= 0xFE30 && c <= 0xFE6F) ||
        (c >= 0xFF00 && c <= 0xFF60) ||
        (c >= 0xFFE0 && c <= 0xFFE6);
}

/// <summary>跑批跑到的一拍。</summary>
/// <param name="No">第几拍，1 起。</param>
/// <param name="BeatId">节拍 id。</param>
/// <param name="Name">节拍名。</param>
/// <param name="Hub">这一拍所在空间。</param>
/// <param name="Day">完成时的第几天。</param>
/// <param name="KeUsed">完成时这个空间已用刻。</param>
/// <param name="KeBudget">这个空间的刻预算。</param>
public sealed record PlaythroughStep(
    int No,
    string BeatId,
    string Name,
    string Hub,
    int Day,
    int KeUsed,
    int KeBudget);

/// <summary>跑批结果：走完了几拍、停在哪一拍、为什么。</summary>
public sealed class PlaythroughResult
{
    /// <summary>成功走完的拍，按顺序。</summary>
    public List<PlaythroughStep> Steps { get; } = new();

    /// <summary>表里共几拍。</summary>
    public int Total { get; internal set; }

    /// <summary>结束时的第几天。</summary>
    public int Day { get; internal set; }

    /// <summary>停在第几拍（1 起）；0 = 没有失败。</summary>
    public int FailedNo { get; internal set; }

    /// <summary>停在哪一拍；全成时为 null。</summary>
    public Beat? FailedBeat { get; internal set; }

    /// <summary>为什么停；全成时为 null。这是"可以通关"的唯一判据。</summary>
    public string? Failure { get; internal set; }

    /// <summary>十二拍全部走完。</summary>
    public bool Ok => Failure == null;

    /// <summary>走到了几拍（失败时是失败前那一拍的序号）。</summary>
    public int Done => Steps.Count;
}

/// <summary>七个空间的用刻汇总。</summary>
/// <param name="TotalUsed">七个空间实际用刻之和。</param>
/// <param name="TotalBudget">七个空间预算之和（<see cref="ClockTable.TotalBudgetKe"/>）。</param>
/// <param name="OverHubs">用超了预算的空间个数。</param>
/// <param name="PerHub">每个空间一行 <c>hub1 5/8</c>。</param>
public sealed record PlaythroughBudget(
    int TotalUsed,
    int TotalBudget,
    int OverHubs,
    IReadOnlyList<string> PerHub);

/// <summary>
/// <c>--playthrough</c> 的参数：现在只有 <c>--pick=N</c>（选择型节拍挑第几项，0 起）。
/// 默认 0——和玩家进面板直接按 Enter 一样。
/// </summary>
public sealed class PlaythroughOptions
{
    /// <summary>选择型节拍确认哪一项（0 起）。负数按 0 处理。</summary>
    public int Pick { get; private init; }

    /// <summary>解析命令行参数。认不出来的参数忽略，不抛异常。</summary>
    /// <param name="args">Godot 的用户参数（<c>--</c> 之后那一串）。</param>
    /// <returns>解析结果。</returns>
    public static PlaythroughOptions Parse(string[]? args)
    {
        var pick = 0;

        if (args != null)
            foreach (var a in args)
            {
                if (!a.StartsWith("--pick=", StringComparison.Ordinal)) continue;
                if (int.TryParse(a[7..], out var n)) pick = Math.Max(0, n);
            }

        return new PlaythroughOptions { Pick = pick };
    }
}
