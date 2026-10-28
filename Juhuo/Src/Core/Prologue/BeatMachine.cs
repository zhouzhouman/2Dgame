using System;
using System.Collections.Generic;
using System.Linq;

namespace Juhuo.Core;

/// <summary>
/// 一个节拍在当前存档下的状态。四态不合并：UI 要靠 Locked / TooEarly 分别说
/// "还差前一件事"和"日子还没到"。
/// </summary>
public enum BeatState
{
    /// <summary>已经做完。</summary>
    Done = 0,

    /// <summary>前置满足、日子也到了，现在就能做。</summary>
    Available = 1,

    /// <summary>前置没满足，做不了（不许跳）。</summary>
    Locked = 2,

    /// <summary>前置满足，但还没到那一日；早到不算完成。</summary>
    TooEarly = 3,
}

/// <summary>
/// 给 UI 的进度快照：做了几件、一共几件、现在该做哪件。
/// 全部做完了 <see cref="CurrentId"/> / <see cref="CurrentName"/> 为 null。
/// </summary>
/// <param name="DoneCount">已完成的节拍数。</param>
/// <param name="TotalCount">表里的节拍总数。</param>
/// <param name="CurrentId">当前该做的节拍 id；没有则为 null。</param>
/// <param name="CurrentName">当前该做的节拍名；没有则为 null。</param>
public readonly record struct PrologueProgress(int DoneCount, int TotalCount, string? CurrentId, string? CurrentName);

/// <summary>
/// 十二节拍状态机（S1-06）。把"七个能自由走的房间"收成"一条 30 天的线"：
///
/// - 节拍**不锁空间**：玩家任何时候都能在 hub1–hub7 之间走，节拍只说明"故事现在期望你做什么"；
/// - 早到不算完成：<see cref="BeatState.TooEarly"/> 时 <see cref="Complete"/> 返回 false；
/// - 不能跳：前面的没做完，后面前置不满足 → <see cref="BeatState.Locked"/>；
/// - 日历到第 30 天封顶，不抛异常。
///
/// 依赖 <see cref="BeatTable"/> + <see cref="Calendar"/> + <see cref="FlagStore"/>，三者都从构造函数传入；
/// 本类不认引擎、不读文件、不推进日历以外的时间。
/// </summary>
public sealed class BeatMachine
{
    private readonly BeatTable _beats;
    private readonly Calendar _calendar;
    private readonly FlagStore _flags;
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>
    /// 建一台状态机。已完成的节拍从空开始（读档走 <see cref="FromState"/>）。
    /// </summary>
    /// <param name="beats">节拍表。</param>
    /// <param name="calendar">日历（第几天）。</param>
    /// <param name="flags">旗标仓库，前置判定与完成置旗都读它、写它。</param>
    public BeatMachine(BeatTable beats, Calendar calendar, FlagStore flags)
    {
        _beats = beats ?? throw new ArgumentNullException(nameof(beats));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
    }

    /// <summary>已完成的节拍数（只数表里有的 id）。</summary>
    public int DoneCount => _done.Count;

    /// <summary>一共几个节拍。</summary>
    public int TotalCount => _beats.Count;

    /// <summary>当前该做的节拍：第一个未完成、前置满足、且日子已到的那个；没有则为 null。</summary>
    public Beat? Current
    {
        get
        {
            foreach (var beat in _beats.All)
                if (StateOf(beat) == BeatState.Available)
                    return beat;

            return null;
        }
    }

    /// <summary>给 UI 的进度快照。</summary>
    public PrologueProgress Progress
    {
        get
        {
            var current = Current;
            return new PrologueProgress(_done.Count, _beats.Count, current?.Id, current?.Name);
        }
    }

    /// <summary>
    /// 这个节拍现在什么状态。判定次序：已完成 → 前置不满足（Locked） → 日子没到（TooEarly） → Available。
    /// </summary>
    /// <param name="beat">节拍。</param>
    /// <returns>四态之一。</returns>
    public BeatState StateOf(Beat beat)
    {
        if (beat == null) throw new ArgumentNullException(nameof(beat));

        if (_done.Contains(beat.Id)) return BeatState.Done;

        // 前置按节拍自己的空间查：局部旗标写在那个 hub 上，节拍不锁空间但旗标仍带归属。
        // 表达式原样交给 FlagExpression；写坏了它返回 false，这一拍就是 Locked，不抛异常。
        var ok = FlagExpression.Evaluate(beat.Requires, name => _flags.Has(name, beat.Hub));
        if (!ok) return BeatState.Locked;

        if (_calendar.Day < beat.Day) return BeatState.TooEarly;

        return BeatState.Available;
    }

    /// <summary>做完了没有。</summary>
    /// <param name="beat">节拍；null 视为没做完。</param>
    /// <returns>已完成返回 true。</returns>
    public bool IsDone(Beat? beat) => beat != null && _done.Contains(beat.Id);

    /// <summary>现在能不能触发：只有 <see cref="BeatState.Available"/> 才算。</summary>
    /// <param name="beat">节拍；null 一律 false。</param>
    /// <returns>可以触发返回 true。</returns>
    public bool CanTrigger(Beat? beat)
    {
        if (beat == null) return false;
        return StateOf(beat) == BeatState.Available;
    }

    /// <summary>
    /// 完成一个节拍：置 <c>sets</c> 里的旗标并记进已完成集合。**不推进日历**，日切由调用方决定。
    /// 重复完成、前置未满足、日子没到都返回 false 且不产生任何改动（幂等）。
    /// </summary>
    /// <param name="beat">节拍；null 返回 false。</param>
    /// <returns>真的完成了一次返回 true。</returns>
    public bool Complete(Beat? beat)
    {
        if (!CanTrigger(beat)) return false;

        foreach (var flag in beat!.Sets) _flags.Set(flag);
        _done.Add(beat.Id);

        return true;
    }

    /// <summary>
    /// 让日历往前走一天，返回新的第几天。第 30 天封顶，到顶后再走仍返回 30，不抛异常。
    /// </summary>
    /// <returns>走完之后的第几天（1–30）。</returns>
    public int AdvanceDay()
    {
        _calendar.Advance(1);
        return _calendar.Day;
    }

    /// <summary>
    /// 把"第几天 + 已完成 id 列表"写进存档。第几天复用顶层 <c>day</c>，不另存一份。
    /// </summary>
    /// <param name="state">目标存档。</param>
    public void ToState(SaveState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        state.Day = _calendar.Day;
        state.Prologue ??= new PrologueState();

        // 按剧情顺序存，读回来与写出去是同一串；不认识的 id 不写。
        state.Prologue.BeatsDone = _beats.All
            .Where(b => _done.Contains(b.Id))
            .Select(b => b.Id)
            .ToList();
    }

    /// <summary>
    /// 从存档恢复一台状态机：日历取 <c>day</c>，已完成取 <c>prologue.beats_done</c>。
    /// 表里已经没有的 id 会被忽略；旗标由调用方另行灌进 <paramref name="flags"/>。
    /// </summary>
    /// <param name="beats">节拍表。</param>
    /// <param name="flags">已经灌好旗标的仓库。</param>
    /// <param name="state">存档；null 等于全新开始（第 1 天、什么都没做）。</param>
    /// <returns>状态机。</returns>
    public static BeatMachine FromState(BeatTable beats, FlagStore flags, SaveState? state)
    {
        var machine = new BeatMachine(beats, new Calendar(state?.Day ?? Calendar.FirstDay), flags);

        var done = state?.Prologue?.BeatsDone;
        if (done == null) return machine;

        foreach (var id in done)
            if (beats.Get(id) is { } beat)
                machine._done.Add(beat.Id);

        return machine;
    }
}
