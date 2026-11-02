using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 招兵与攻城接进游戏（票号 S2-03c）时要钉住的那几个字：哪一拍、面板上那行怎么拼。
///
/// 规则在 Core（<see cref="SiegeBook"/> / <see cref="RecruitTable"/>），这里只管"接线与显示"——
/// 与 <see cref="ShopWiring"/> 一个位置，测试与画面读的是同一份常量。
/// </summary>
public static class SiegeWiring
{
    /// <summary>攻城那一拍（beats.csv）。</summary>
    public const string SiegeBeat = "b11";

    /// <summary>招兵那一拍（beats.csv）。</summary>
    public const string RecruitBeat = "b07";

    /// <summary>兵力那个词的写法。</summary>
    public const string TroopsLabel = "兵力";

    /// <summary>兵力那一行：<c>兵力 60</c>。信任面板底部与攻城面板顶部共用。</summary>
    /// <param name="troops">手里多少兵。</param>
    /// <returns>一行字。</returns>
    public static string TroopLine(int troops) => $"{TroopsLabel} {troops}";

    /// <summary>
    /// 攻城面板的抬头：<c>⑪　攻城　·　兵力 60</c>。
    /// </summary>
    /// <param name="ordinal">第几拍（①–⑫）。</param>
    /// <param name="name">这一拍的名字。</param>
    /// <param name="troops">手里多少兵。</param>
    /// <returns>一行字。</returns>
    public static string SiegeHead(string ordinal, string name, int troops) =>
        $"{ordinal}　{name}　·　{TroopLine(troops)}";
}

/// <summary>
/// 招兵那一下的接线（§18.5 场景 7）：按选中的档把兵加上去，该动的三轴交给调用方。
///
/// **为什么信任走委托**：游戏里涨三轴必须走 Main 的 <c>ApplyTrust</c>（它还要打提示、落盘），
/// 这里直接摸 <see cref="TrustBook"/> 会把那条路绕过去。传一个 <see cref="Action{T}"/> 进来，
/// 接线层只管"选到哪一档、加几个兵、该不该调信任"。
/// </summary>
public static class RecruitWiring
{
    /// <summary>
    /// 落定这一次招兵：加兵、必要时调信任动作，返回给玩家看的那一句。
    /// </summary>
    /// <param name="table">招兵表（recruit.csv）。</param>
    /// <param name="index">选中的选项序号（= beats.csv 里 options 的下标）。</param>
    /// <param name="troops">手里的兵，就地加。</param>
    /// <param name="applyTrust">信任动作的出口；不需要动三轴的档不会调它。</param>
    /// <returns><c>招到 60 人（现有 60）</c>；表里没有这一档返回空串（不改任何东西）。</returns>
    public static string Apply(RecruitTable table, int index, ref int troops, Action<string>? applyTrust)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));

        var option = table.ForOption(index);
        if (option == null) return "";

        troops = Math.Max(0, troops + option.Troops);

        if (!string.IsNullOrEmpty(option.TrustAction)) applyTrust?.Invoke(option.TrustAction);

        return option.Troops >= 0
            ? $"招到 {option.Troops} 人（现有 {troops}）"
            : $"走了 {-option.Troops} 人（现有 {troops}）";
    }
}

/// <summary>
/// 攻城面板上的一行：<c>围　耗 6 刻　伤亡 10%</c>，右边跟一句"为什么点不动 / 会输"。
/// </summary>
/// <param name="Id">手段 id（与 <c>siege.csv</c> 一致，按下之后按它取牌）。</param>
/// <param name="Label">行首那截：名字 + 耗时 + 伤亡。</param>
/// <param name="Note">右侧那半句；没有就是空串。</param>
/// <param name="Usable"><c>needs</c> 满没满足（灰不灰）。</param>
/// <param name="WillRetreat">能选，但兵不够打赢——选了就要撤。</param>
public readonly record struct SiegeRow(string Id, string Label, string Note, bool Usable, bool WillRetreat);

/// <summary>
/// 攻城面板（票号 S2-03c）：把 <see cref="SiegeBook"/> 的四法摊成四行，管选中、确认、结算之后的状态。
///
/// **只在这里落账**：赢了扣兵扣民望、输了扣 30% 并置 <see cref="SiegeBook.FailedFlag"/>——
/// 三件事都发生在 <see cref="Confirm"/> 里，Main 不再自己算一遍（算两遍迟早对不上）。
/// 屏上那两句（<c>城开了。折损 12 人，余 53 人。</c> / <c>没打开。退回营地，折损 30%，余 45 人。</c>）
/// 也钉在这里，测试与画面读同一份。
/// </summary>
public sealed class SiegeScreen
{
    /// <summary>底下那行操作提示。</summary>
    public const string FootLine = "1–4 选一法　·　Enter 确认　·　Esc 稍后再议";

    private readonly SiegeBook _book;
    private readonly TrustBook _trust;
    private readonly FlagProbe? _flags;
    private readonly FlagStore? _store;

    /// <summary>
    /// 摊开一张攻城面板。
    /// </summary>
    /// <param name="book">四法（判定与算术都在它手上）。</param>
    /// <param name="trust">三条轴：<c>needs</c> 要读它，赢了要扣民望。</param>
    /// <param name="troops">进场时手里多少兵。</param>
    /// <param name="flags">旗标探针（<c>flag:city_hungry</c> 那类门槛）。</param>
    /// <param name="store">撤退时置旗标用；不给就只算不落（测试与 <c>--siege</c> 用得上）。</param>
    public SiegeScreen(
        SiegeBook book, TrustBook trust, int troops, FlagProbe? flags = null, FlagStore? store = null)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _flags = flags;
        _store = store;
        Troops = Math.Max(0, troops);
    }

    /// <summary>现在手里多少兵（打完就变成剩下的）。</summary>
    public int Troops { get; private set; }

    /// <summary>打了几种法子。</summary>
    public int Count => _book.Count;

    /// <summary>最近一次确认的结果；还没打过为 null。</summary>
    public SiegeResult? LastResult { get; private set; }

    /// <summary>确认之后那句话；被拒时是"没让你打：……"。</summary>
    public string Message { get; private set; } = "";

    /// <summary>四行，按 <c>siege.csv</c> 的顺序（= 面板上 1–4 的顺序）。</summary>
    /// <returns>四行。</returns>
    public IReadOnlyList<SiegeRow> Rows()
    {
        var rows = new List<SiegeRow>(_book.Count);

        foreach (var method in _book.All)
        {
            var usable = _book.CanUse(method, Troops, _trust, _flags);
            var retreat = usable && Troops < method.TroopsNeeded;

            var note = !usable
                ? _book.NeedsText(method, Troops, _trust, _flags)
                : retreat
                    ? $"（{SiegeWiring.TroopsLabel} {Troops} / 需 {method.TroopsNeeded}，多半要撤）"
                    : "";

            rows.Add(new SiegeRow(method.Id, RowLabel(method), note, usable, retreat));
        }

        return rows;
    }

    /// <summary>
    /// 一行的行首：<c>围　耗 6 刻　伤亡 10%</c>。
    /// 耗时与伤亡都照表直说——玩家该在按下去之前就知道这一法的代价。
    /// </summary>
    /// <param name="method">手段。</param>
    /// <returns>一行字。</returns>
    public static string RowLabel(SiegeMethod method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));

        return $"{method.Label}　耗 {method.Ke} 刻　伤亡 {method.Casualty}%";
    }

    /// <summary>第几行能不能选；越界算不能。</summary>
    /// <param name="index">0 起。</param>
    /// <returns>能选返回 true。</returns>
    public bool CanUse(int index) => index >= 0 && index < _book.Count
        && _book.CanUse(_book.All[index], Troops, _trust, _flags);

    /// <summary>
    /// 第一条能选的法子（0 起）；一条都点不动时返回 0。
    /// 通关跑批用它兜底——默认选「围」而城里没断粮时，不能把跑批卡死在面板前。
    /// </summary>
    /// <returns>下标。</returns>
    public int FirstUsable()
    {
        for (var i = 0; i < _book.Count; i++)
            if (CanUse(i)) return i;

        return 0;
    }

    /// <summary>
    /// 按下去：结算、扣兵、赢了扣民望、输了置旗标，返回 Core 的结果。
    ///
    /// 门槛没过的法子**一个兵都不动**（<see cref="SiegeResult.Refused"/>），
    /// <see cref="Message"/> 里说清为什么，面板不关。
    /// </summary>
    /// <param name="index">选中的第几法，0 起。</param>
    /// <returns>结算结果。</returns>
    public SiegeResult Confirm(int index)
    {
        if (index < 0 || index >= _book.Count)
        {
            var bad = SiegeResult.Refused(Troops, $"没有第 {index + 1} 种打法", 0);
            LastResult = bad;
            Message = $"没让你打：{bad.Reason}";
            return bad;
        }

        var method = _book.All[index];
        var before = Troops;
        var result = _book.Resolve(method, Troops, _trust, _flags);

        LastResult = result;

        if (!result.Ok)
        {
            Message = $"没让你打：{result.Reason}";
            return result;
        }

        Troops = result.Survived;

        if (!string.IsNullOrEmpty(result.Flag)) _store?.Set(result.Flag);
        if (result.Win) DropMinyuan(result.Minyuan);

        Message = result.Win
            ? $"城开了。折损 {before - result.Survived} 人，余 {result.Survived} 人。"
            : $"没打开。退回营地，折损 {result.Casualty}%，余 {result.Survived} 人。";

        return result;
    }

    /// <summary>
    /// 民怨的临时落点（M1）。<see cref="TrustState"/> 只有官望 / 民望 / 疑心三条轴，
    /// 没有"民怨"这一条；表里的 <c>minyuan</c> 先记在**民望**上：正数 = 民望被扣，
    /// 钳在 0–100。等 D1 定了民怨轴（或者定了"民怨 = 民望的负数"）再换掉这一处。
    /// </summary>
    /// <param name="minyuan">表里的民怨增量（正数 = 百姓更恨你）。</param>
    private void DropMinyuan(int minyuan)
    {
        if (minyuan == 0) return;

        _trust.State.Min = Math.Clamp(_trust.State.Min - minyuan, TrustBook.MinValue, TrustBook.MaxValue);
    }
}

/// <summary>
/// <c>--siege</c> 的输出。格式钉在这里，命令行与测试读的是同一份。
/// </summary>
public static class SiegeReport
{
    /// <summary>所有行共用的抬头。</summary>
    public const string Prefix = "[攻]";

    /// <summary>
    /// 四法一眼看全：<c>[攻] 兵力 60｜围 不可用（城里还没断粮）｜攻 可打但多半撤（需 72）｜…</c>。
    /// 一句一行不换行——调试时 <c>head -1</c> 就是全部结论。
    /// </summary>
    /// <param name="book">四法。</param>
    /// <param name="troops">手里多少兵。</param>
    /// <param name="trust">三条轴。</param>
    /// <param name="flags">旗标探针。</param>
    /// <returns>一行字。</returns>
    public static string StatusLine(SiegeBook book, int troops, TrustBook trust, FlagProbe? flags = null)
    {
        if (book == null) throw new ArgumentNullException(nameof(book));

        var parts = new List<string>(book.Count + 1) { $"{Prefix} {SiegeWiring.TroopLine(troops)}" };

        foreach (var method in book.All)
        {
            var usable = book.CanUse(method, troops, trust, flags);
            parts.Add($"｜{CompactLabel(method)} {StateOf(method, troops, trust, usable)}");
        }

        return string.Concat(parts);
    }

    /// <summary>
    /// 紧凑行里的名字：去掉括注与斜杠——<c>间（内应）</c> → <c>间</c>、<c>水/火</c> → <c>水火</c>。
    /// 四种法子挤在一行里，一个名字带六个字的注解就读不完了；面板上仍旧用全名。
    /// </summary>
    /// <param name="method">手段。</param>
    /// <returns>短名。</returns>
    private static string CompactLabel(SiegeMethod method)
    {
        var label = method.Label;
        var note = label.IndexOf('（');
        if (note > 0) label = label[..note];

        return label.Replace("/", "");
    }

    /// <summary>
    /// 一种法子的短状态：<c>可用</c> / <c>可打但多半撤（需 72）</c> / <c>不可用（官望 0/60）</c>。
    /// 紧凑行里省掉 <see cref="SiegeBook.NeedsText"/> 的空格与"需"字，一句话读得完。
    /// </summary>
    /// <param name="method">手段。</param>
    /// <param name="troops">手里多少兵。</param>
    /// <param name="trust">三条轴。</param>
    /// <param name="usable">门槛过没过（调用方已经问过 <see cref="SiegeBook.CanUse"/>）。</param>
    /// <returns>短状态。</returns>
    private static string StateOf(SiegeMethod method, int troops, TrustBook trust, bool usable)
    {
        if (!usable) return $"不可用（{ShortNeed(method.Needs, troops, trust)}）";

        return troops < method.TroopsNeeded
            ? $"可打但多半撤（需 {method.TroopsNeeded}）"
            : "可用";
    }

    /// <summary>门槛缺什么，说得比 <see cref="SiegeBook.NeedsText"/> 短一截。</summary>
    private static string ShortNeed(SiegeNeed need, int troops, TrustBook trust) =>
        need.Kind switch
        {
            SiegeNeedKind.Trust => $"{AxisLabel(need.Key)} {AxisValue(trust, need.Key)}/{need.Value}",
            SiegeNeedKind.Troops => $"{SiegeWiring.TroopsLabel} {troops}/{need.Value}",
            SiegeNeedKind.Flag => need.Key == SiegeBook.CityHungryFlag
                ? "城里还没断粮"
                : need.Key == SiegeBook.TerrainRiverFlag
                    ? "地形"
                    : $"还差「{need.Key}」",
            _ => SiegeBook.NoNeedText,
        };

    /// <summary>
    /// 一仗的结果：<c>[攻] 兵力 60 硬攻 → 撤退，损 18，余 42，置 siege.battle.failed</c>。
    /// 没让打的那一支不装作打了，照实说原因。
    /// </summary>
    /// <param name="act">这一仗叫什么（"硬攻" / "攻"）。</param>
    /// <param name="before">打之前多少兵。</param>
    /// <param name="result">结算结果。</param>
    /// <returns>一行字。</returns>
    public static string OutcomeLine(string act, int before, SiegeResult result)
    {
        var head = $"{Prefix} 兵力 {before} {act} → ";
        var lost = Math.Max(0, before - result.Survived);

        if (result.Win) return $"{head}城开了，损 {lost}，余 {result.Survived}";
        if (result.Retreat) return $"{head}撤退，损 {lost}，余 {result.Survived}，置 {result.Flag}";

        return $"{head}没让打：{result.Reason}";
    }

    /// <summary>轴 id → 人话（三条轴的名词只有 TrustText 那一份）。</summary>
    private static string AxisLabel(string axis) =>
        axis == "min" ? TrustText.Min : axis == "yi" ? TrustText.Yi : TrustText.Guan;

    /// <summary>某条轴现在多少。</summary>
    private static int AxisValue(TrustBook trust, string axis) =>
        axis == "min" ? trust.State.Min : axis == "yi" ? trust.State.Yi : trust.State.Guan;
}

/// <summary>
/// <c>--siege</c> 的那一趟（票号 S2-03c）：同一起点、三种兵力，把"选不了 / 打了要撤 / 打得下"
/// 三件事摆在一屏里。
///
/// 只动自己造的那一份三轴，不碰玩家的档——撤退时也不置真旗标（<see cref="SiegeScreen"/> 不给 store）。
/// </summary>
public static class SiegeDemo
{
    /// <summary>拿来演"硬攻"的那一法（siege.csv 的攻）。</summary>
    public const string AssaultId = "assault";

    /// <summary>招完兵之后的常见兵力（b07 给粮/给承诺那一档 +60）。</summary>
    public const int StartTroops = 60;

    /// <summary>够打赢「攻」的兵力（表里 troops_needed = 72）。</summary>
    public const int EnoughTroops = 80;

    /// <summary>
    /// 跑一趟，把该打印的行按顺序交回来。
    /// </summary>
    /// <param name="table">攻城表。</param>
    /// <param name="trustTable">信任动作表（这一趟不用它，但 TrustBook 要一张）。</param>
    /// <param name="routes">路线表（同上）。</param>
    /// <returns>按打印顺序排好的行。</returns>
    public static IReadOnlyList<string> Run(SiegeTable table, TrustTable trustTable, RouteTable routes)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));

        var book = new SiegeBook(table);

        // 三轴全 0、一个旗标都没有：这正是刚招完兵、还没断粮也没地形的样子。
        var trust = new TrustBook(new TrustState(), trustTable, routes);

        var lines = new List<string>(3) { SiegeReport.StatusLine(book, StartTroops, trust) };

        if (!book.TryGet(AssaultId, out var assault))
        {
            lines.Add($"{SiegeReport.Prefix} 表里没有「{AssaultId}」，打不了");
            return lines;
        }

        lines.Add(SiegeReport.OutcomeLine("硬攻", StartTroops, book.Resolve(assault, StartTroops, trust)));
        lines.Add(SiegeReport.OutcomeLine("攻", EnoughTroops, book.Resolve(assault, EnoughTroops, trust)));

        return lines;
    }
}
