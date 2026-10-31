using System;
using System.Collections.Generic;
using System.Linq;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 十二节拍的**玩法接线**（票号 S1-06c）：Core 的 <see cref="BeatMachine"/> 只管"能不能做"，
/// 这里补三件 Core 故意不管的事——
///
/// 1. **完成一拍之后日子怎么走**：<see cref="BeatMachine.Complete"/> 不推进日历（S1-06 语义表），
///    日切是玩法决定。这里的规矩是：推进到"下一拍的最早天"，不早于今天，到第 30 天封顶。
/// 2. **选择型节拍选了什么**：选第 i 项就置 <c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>，再完成这一拍。
///    旗标是这一笔"选了什么"的唯一副本，节拍本身只记"做完了"。
/// 3. **HUD 那一行怎么写**、**<c>--beats</c> 那张表怎么打**。
///
/// 纯 C#，不认引擎：Main 只负责把它画出来、把按键递进来。
/// </summary>
public sealed class PrologueBeats
{
    /// <summary>进入空间即弹选项的节拍。</summary>
    public const string ChoiceTrigger = "choice";

    /// <summary>搜到关键点时完成的节拍。</summary>
    public const string SearchTrigger = "search";

    /// <summary>敲门时完成的节拍。</summary>
    public const string KnockTrigger = "knock";

    /// <summary>选择型节拍的旗标：<c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>（四段点号，过 FlagStore.IsValidName）。</summary>
    public const string PickFlagFormat = "beat.{0}.pick.{1}";

    /// <summary>
    /// 每一拍对应的既有可搜点。
    ///
    /// **不新造点位 id**：数据表归 D1，表现层只认 <c>Content/data/points.csv</c> 里已经有的那一行。
    /// b08「空仓」——hub6 里名字带"仓"的可搜点只有 <c>h6_04「仓门」</c>（"门是新锁的。锁眼里塞着蜡。"）。
    /// 表里没有条目的 search 拍走退化路径：进这个空间、能触发时按 E 完成。
    /// </summary>
    private static readonly Dictionary<string, string> SearchKeys = new(StringComparer.Ordinal)
    {
        ["b08"] = "h6_04",
    };

    /// <summary>中文序号 ①–⑫，与表里的位置一一对应；超出就退回阿拉伯数字。</summary>
    private const string Circled = "①②③④⑤⑥⑦⑧⑨⑩⑪⑫";

    private readonly BeatTable _table;
    private readonly BeatMachine _machine;
    private readonly FlagStore _flags;

    private PrologueBeats(BeatTable table, BeatMachine machine, FlagStore flags, int day)
    {
        _table = table;
        _machine = machine;
        _flags = flags;
        Day = Math.Clamp(day, Calendar.FirstDay, Calendar.LastDay);
    }

    /// <summary>
    /// 从存档恢复。**调用前旗标必须已经灌进 <paramref name="flags"/>**——
    /// <see cref="BeatMachine.FromState"/> 不写旗标（S1-06 的第二个坑），
    /// 旗标没进去，<c>requires</c> 全不满足，十二拍会一起变成 Locked。
    /// </summary>
    /// <param name="table">节拍表。</param>
    /// <param name="flags">已经灌好旗标的仓库（通常是 World 那一份）。</param>
    /// <param name="state">存档；null = 全新一局（第 1 天、一拍没做）。</param>
    /// <returns>接线好的节拍运行器。</returns>
    public static PrologueBeats FromState(BeatTable table, FlagStore flags, SaveState? state)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (flags == null) throw new ArgumentNullException(nameof(flags));

        var machine = BeatMachine.FromState(table, flags, state);
        return new PrologueBeats(table, machine, flags, state?.Day ?? Calendar.FirstDay);
    }

    /// <summary>今天第几天（1–30）。只随 <see cref="BeatMachine.AdvanceDay"/> 动。</summary>
    public int Day { get; private set; }

    /// <summary>节拍表。</summary>
    public BeatTable Table => _table;

    /// <summary>底层状态机（存档与四态查询直接用它）。</summary>
    public BeatMachine Machine => _machine;

    /// <summary>进度快照，给 UI 用。</summary>
    public PrologueProgress Progress => _machine.Progress;

    /// <summary>十二拍都做完了。</summary>
    public bool AllDone => _table.Count > 0 && _machine.DoneCount >= _table.Count;

    /// <summary>当前该做的那一拍；没有（全做完或全锁着）为 null。</summary>
    public Beat? Current => _machine.Current;

    /// <summary>这一拍在表里的位置（1 起）；不在表里返回 0。</summary>
    /// <param name="beat">节拍。</param>
    /// <returns>1–12 或 0。</returns>
    public int IndexOf(Beat? beat)
    {
        if (beat == null) return 0;

        for (var i = 0; i < _table.Count; i++)
            if (string.Equals(_table.All[i].Id, beat.Id, StringComparison.Ordinal))
                return i + 1;

        return 0;
    }

    // ————————————————————————— 该弹什么 —————————————————————————

    /// <summary>
    /// 进入这个空间时该弹的那个选择拍：本空间、trigger=choice、现在能触发。
    /// 已经完成的拍不再 Available，所以同一拍不会重复弹。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>该弹的节拍；没有则 null。</returns>
    public Beat? PendingChoice(string? hub)
    {
        return _table.ForHub(hub)
            .FirstOrDefault(b => string.Equals(b.Trigger, ChoiceTrigger, StringComparison.Ordinal)
                                 && _machine.CanTrigger(b));
    }

    /// <summary>
    /// 搜到关键点时该完成的 search 拍。点位按 <see cref="SearchKeys"/> 认，
    /// 表里没有对应点位的拍不算（走 <see cref="PendingSearchFallback"/>）。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <param name="pointId">刚搜过的点位 id。</param>
    /// <returns>该完成的节拍；没有则 null。</returns>
    public Beat? PendingSearch(string? hub, string? pointId)
    {
        if (string.IsNullOrEmpty(pointId)) return null;

        foreach (var beat in _table.ForHub(hub))
        {
            if (!string.Equals(beat.Trigger, SearchTrigger, StringComparison.Ordinal)) continue;
            if (!SearchKeys.TryGetValue(beat.Id, out var key)) continue;
            if (!string.Equals(key, pointId, StringComparison.Ordinal)) continue;
            if (!_machine.CanTrigger(beat)) continue;

            return beat;
        }

        return null;
    }

    /// <summary>
    /// 退化路径：这个空间有一拍 search 但表里没给它指定点位——
    /// 那就"进到这里、能触发时按 E 完成"，而不是让它永远悬着。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>该完成的节拍；没有则 null。</returns>
    public Beat? PendingSearchFallback(string? hub)
    {
        return _table.ForHub(hub)
            .FirstOrDefault(b => string.Equals(b.Trigger, SearchTrigger, StringComparison.Ordinal)
                                 && !SearchKeys.ContainsKey(b.Id)
                                 && _machine.CanTrigger(b));
    }

    /// <summary>敲门这一下该完成的节拍。</summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>该完成的节拍；没有则 null。</returns>
    public Beat? PendingKnock(string? hub)
    {
        return _table.ForHub(hub)
            .FirstOrDefault(b => string.Equals(b.Trigger, KnockTrigger, StringComparison.Ordinal)
                                 && _machine.CanTrigger(b));
    }

    /// <summary>
    /// 门早就敲过了，但那一拍当时前置还没满足（比如账还没烧）——补记。
    /// 不补的话，玩家会永远缺一拍，后面十拍一起锁死。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>该补记的节拍；没有则 null。</returns>
    public Beat? PendingKnockLate(string? hub)
    {
        if (!string.Equals(hub, KnockTimer.KnockHub, StringComparison.Ordinal)) return null;
        if (!_flags.Has(KnockTimer.HappenedFlag, hub)) return null;

        return PendingKnock(hub);
    }

    // ————————————————————————— 做完一拍 —————————————————————————

    /// <summary>
    /// 完成一拍，并把日子推到下一拍的最早天（不早于今天，30 封顶）。
    /// </summary>
    /// <param name="beat">节拍。</param>
    /// <returns>真的完成了一次返回 true（重复完成、前置不足、日子没到都返回 false）。</returns>
    public bool Complete(Beat? beat)
    {
        if (!_machine.Complete(beat)) return false;

        AdvanceToNext(beat!);
        return true;
    }

    /// <summary>
    /// 选择型节拍确认：先置 <c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>，再完成这一拍。
    /// 选项为空时按第 0 项处理（"这一拍还没有可选项文案"是合法数据）。
    /// </summary>
    /// <param name="beat">节拍。</param>
    /// <param name="index">选中的项，0 起。</param>
    /// <returns>完成了返回 true。</returns>
    public bool Choose(Beat? beat, int index)
    {
        if (!_machine.CanTrigger(beat)) return false;

        // 先落"选了什么"，再落"做完了"：两步都写进旗标仓库，读档后是同一份事实。
        _flags.Set(PickFlagOf(beat!, index));

        return Complete(beat);
    }

    /// <summary>把选项序号夹回合法范围；没有选项时一律 0。</summary>
    /// <param name="beat">节拍。</param>
    /// <param name="index">想选的项。</param>
    /// <returns>实际落在哪一项。</returns>
    public static int NormalizePick(Beat beat, int index)
    {
        if (beat.Options.Length == 0) return 0;
        return Math.Clamp(index, 0, beat.Options.Length - 1);
    }

    /// <summary>这一次选择写进存档的旗标名（给日志与测试看）。</summary>
    /// <param name="beat">节拍。</param>
    /// <param name="index">选中的项。</param>
    /// <returns>旗标名。</returns>
    public static string PickFlagOf(Beat beat, int index)
        => string.Format(PickFlagFormat, beat.Id, NormalizePick(beat, index));

    /// <summary>
    /// 推到下一拍的最早天：完成 b02（第 1 天）之后，b03 是第 3 天，日子就走到第 3 天；
    /// 完成 b01 之后下一拍还是第 1 天，日子不动。最后一拍走完停在第 30 天。
    /// </summary>
    private void AdvanceToNext(Beat beat)
    {
        var index = IndexOf(beat);
        var next = index > 0 && index < _table.Count ? _table.All[index] : null;
        var target = next?.Day ?? Calendar.LastDay;

        while (Day < target && Day < Calendar.LastDay)
            Day = _machine.AdvanceDay();
    }

    // ————————————————————————— 给玩家看 —————————————————————————

    /// <summary>
    /// HUD 那一行：
    /// <c>第 3 天 · 废驿 · 该做：③ 第一口锅</c>；
    /// 全做完写 <c>第 30 天 · 序章已完成</c>；当前没有可做的写 <c>第 N 天 · 等一等</c>。
    /// </summary>
    /// <param name="hubLabel">当前空间的短名（"废驿"）；空串时这一段省掉。</param>
    /// <returns>一行字。</returns>
    public string HudLine(string? hubLabel = "")
    {
        if (AllDone) return $"第 {Day} 天 · 序章已完成";

        var current = _machine.Current;
        if (current is null) return $"第 {Day} 天 · 等一等";

        var where = string.IsNullOrEmpty(hubLabel) ? "" : $" · {hubLabel}";
        return $"第 {Day} 天{where} · 该做：{OrdinalOf(IndexOf(current))} {current.Name}";
    }

    /// <summary>
    /// <c>--beats</c> 那张表：第一行说总数与完成数，后面每一拍一行，当前那一拍标 ← 当前。
    /// </summary>
    /// <returns>按打印顺序排好的行。</returns>
    public IReadOnlyList<string> DescribeTable()
    {
        var currentId = _machine.Current?.Id;
        var lines = new List<string>(_table.Count + 1)
        {
            $"[节拍] 共 {_table.Count} 拍，已完成 {_machine.DoneCount} 拍",
        };

        for (var i = 0; i < _table.Count; i++)
        {
            var beat = _table.All[i];
            var state = _machine.StateOf(beat);
            var mark = string.Equals(beat.Id, currentId, StringComparison.Ordinal) ? " ← 当前" : "";
            lines.Add($"[节拍] {i + 1} {beat.Id} {beat.Name} 第{beat.Day}天 {beat.Hub} {StateText(state)}{mark}");
        }

        return lines;
    }

    /// <summary>四态的中文说法，HUD 与 <c>--beats</c> 共用一份。</summary>
    /// <param name="state">状态。</param>
    /// <returns>中文说法。</returns>
    public static string StateText(BeatState state) => state switch
    {
        BeatState.Done => "已完成",
        BeatState.Available => "可触发",
        BeatState.Locked => "锁定",
        _ => "未到天数",
    };

    /// <summary>第 n 拍的序号：1–12 用 ①–⑫，超出去用阿拉伯数字。</summary>
    /// <param name="index">表里的位置，1 起。</param>
    /// <returns>序号文本。</returns>
    public static string OrdinalOf(int index)
    {
        if (index >= 1 && index <= Circled.Length) return Circled[index - 1].ToString();
        return index > 0 ? index.ToString() : "·";
    }
}
