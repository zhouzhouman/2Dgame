using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 双信任轴的存档接线（票号 S1-01c）。
///
/// 规则在 Core 的 <see cref="TrustBook"/>，这里只管一件事：三条轴存在哪个格子里。
/// 存档形状 <see cref="SaveState.Trust"/> 是"地区 → 三条轴"，序章只有一个地区，
/// 键固定 <see cref="Region"/>——与 <c>--savetest</c> 一直用的那个键同一份。
/// </summary>
public static class TrustWiring
{
    /// <summary>序章这个地区的键。</summary>
    public const string Region = "prologue";

    /// <summary>
    /// 从存档里取出三条轴。**没有条目时给的是 <see cref="TrustState"/> 的默认值（0/0/0），
    /// 不是玩法上的初值**——初值该由数据或设计定，接线这一层不许自己编一个。
    ///
    /// 返回的是存档里那一份对象本身：<see cref="TrustBook"/> 就地改它，
    /// 存档时再原样交回去，三轴只有一份真相。
    /// </summary>
    /// <param name="state">读进来的存档；null = 全新一局。</param>
    /// <returns>三条轴。</returns>
    public static TrustState Restore(SaveState? state)
    {
        if (state == null) return new TrustState();

        state.Trust ??= new Dictionary<string, TrustState>(StringComparer.Ordinal);

        if (state.Trust.TryGetValue(Region, out var saved) && saved != null) return saved;

        // 没有条目就补上一条空的：三轴从这一刻起就住在存档里，
        // 之后 SaveGame 拿到的永远是同一份对象。
        var fresh = new TrustState();
        state.Trust[Region] = fresh;
        return fresh;
    }

    /// <summary>
    /// 把三条轴写回存档。只写现有的 <see cref="SaveState.Trust"/> 字段，不加新字段。
    /// </summary>
    /// <param name="state">要写的存档。</param>
    /// <param name="book">账本（它手上就是 <see cref="Restore"/> 给的那一份）。</param>
    public static void Store(SaveState state, TrustBook book)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (book == null) throw new ArgumentNullException(nameof(book));

        state.Trust ??= new Dictionary<string, TrustState>(StringComparer.Ordinal);
        state.Trust[Region] = book.State;
    }
}

/// <summary>
/// 三轴的人话（票号 S1-01c）。名词一律照 CANON §3：官望 / 民望 / 疑心。
///
/// 增量那半句完全交给 <see cref="TrustBook.Explain"/>——同一份文案只有一处，
/// 这里只补"这一下打在墙上"的那半截。
/// </summary>
public static class TrustText
{
    /// <summary>撞上限时补的那一句。</summary>
    public const string TopSuffix = "（已到顶）";

    /// <summary>撞下限时补的那一句。</summary>
    public const string BottomSuffix = "（已见底）";

    /// <summary>官望。</summary>
    public const string Guan = "官望";

    /// <summary>民望。</summary>
    public const string Min = "民望";

    /// <summary>疑心。</summary>
    public const string Yi = "疑心";

    /// <summary>
    /// 屏幕上那一条提示：<c>官望 +3  民望 −2  疑心 +2</c>；
    /// 被钳过就在末尾补一句 <c>（已到顶）</c> / <c>（已见底）</c>。
    /// </summary>
    /// <param name="book">账本（要读钳完之后的当前值）。</param>
    /// <param name="action">表里那条动作；查不到传 null。</param>
    /// <param name="delta">这一次实际发生的增量。</param>
    /// <returns>一行字；什么都没动时是空串。</returns>
    public static string Toast(TrustBook book, TrustAction? action, TrustDelta delta)
    {
        if (book == null) throw new ArgumentNullException(nameof(book));

        // 钳没钳、是哪条轴钳的，Explain 已经按轴标好了（S1-01d）。
        // 这里再补一次全局后缀，会出现「疑心 +2（已见底）」这种张冠李戴——实测踩过。
        return book.Explain(delta);
    }

}

/// <summary>面板上一条横条：名字与当前值（0–100）。</summary>
/// <param name="Name">官望 / 民望 / 疑心。</param>
/// <param name="Value">当前值。</param>
public readonly record struct TrustBar(string Name, int Value)
{
    /// <summary>占满多少，0–1，给画横条用。</summary>
    public float Ratio => Math.Clamp(Value, TrustBook.MinValue, TrustBook.MaxValue) / (float)TrustBook.MaxValue;
}

/// <summary>
/// 「信任」面板要显示的东西（票号 S1-01c）：三条横条 + 两行攻城路线。
///
/// 只算文字与数值，画由 Main 画——测试不必起引擎就能盯住"够 / 差 N"这两句话。
/// </summary>
public sealed class TrustPanel
{
    private readonly TrustBook _book;

    /// <summary>建一张面板。</summary>
    /// <param name="book">这个地区的账本。</param>
    public TrustPanel(TrustBook book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
    }

    /// <summary>底下的账本。</summary>
    public TrustBook Book => _book;

    /// <summary>三条横条，固定顺序：官望、民望、疑心。</summary>
    /// <returns>三条。</returns>
    public IReadOnlyList<TrustBar> Bars() => new[]
    {
        new TrustBar(TrustText.Guan, _book.State.Guan),
        new TrustBar(TrustText.Min, _book.State.Min),
        new TrustBar(TrustText.Yi, _book.State.Yi),
    };

    /// <summary>
    /// 两条路线各一行，顺序照 <c>routes.csv</c>：
    /// <c>内应开门：可走（官望 62 ≥ 60）</c> / <c>内应开门：还差 24 官望</c>。
    /// </summary>
    /// <returns>与 <see cref="TrustBook.Routes"/> 一一对应的行。</returns>
    public IReadOnlyList<string> RouteLines()
    {
        var lines = new List<string>(_book.Routes.Count);
        foreach (var route in _book.Routes) lines.Add(RouteLine(route, ValueOf(route.Axis)));
        return lines;
    }

    /// <summary>对应的开门状态，顺序同上。</summary>
    /// <returns>开着为 true。</returns>
    public IReadOnlyList<bool> RouteOpen()
    {
        var open = new List<bool>(_book.Routes.Count);
        foreach (var route in _book.Routes) open.Add(ValueOf(route.Axis) >= route.Threshold);
        return open;
    }

    /// <summary>某条路线那一行字。</summary>
    /// <param name="route">路线。</param>
    /// <param name="value">它看的那条轴现在多少。</param>
    /// <returns>一行字。</returns>
    public static string RouteLine(TrustRoute route, int value)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));

        var axis = AxisName(route.Axis);

        return value >= route.Threshold
            ? $"{route.Label}：可走（{axis} {value} ≥ {route.Threshold}）"
            : $"{route.Label}：还差 {route.Threshold - value} {axis}";
    }

    /// <summary>轴 id → 人话。只认 routes.csv 允许的两个值。</summary>
    /// <param name="axis">guan / min。</param>
    /// <returns>官望 / 民望。</returns>
    public static string AxisName(string axis) =>
        axis == TrustRoute.AxisGuan ? TrustText.Guan : TrustText.Min;

    /// <summary>这条轴现在多少。</summary>
    private int ValueOf(string axis) =>
        axis == TrustRoute.AxisGuan ? _book.State.Guan : _book.State.Min;
}

/// <summary>
/// 攻城四法里的「间」（设计文档 §8.6：间 = 内应）要官望够才走得通（票号 S1-01c）。
///
/// 只认 b11 这一拍、只认「间」这一项——别的拍、别的项一律不管。
/// 文案钉在这里，测试与画面读的是同一句。
/// </summary>
public static class TrustChoice
{
    /// <summary>攻城那一拍。</summary>
    public const string SiegeBeat = "b11";

    /// <summary>内应那条路线的 id（routes.csv）。</summary>
    public const string InsideRoute = "inside";

    /// <summary>「间」这一项在表里的写法（beats.csv 的 options 列）。</summary>
    public const string InsideOption = "间";

    /// <summary>走不通时给玩家看的那一句：先说怎么了，不解释系统。</summary>
    public const string Refusal = "官望不够，城里没人给你开门";

    /// <summary>
    /// 「间」在这一拍里是第几项（0 起）；不是攻城那一拍、或者表里没有这一项，返回 −1。
    ///
    /// 按选项文本找，不写死序号：D3 若调整了四项的顺序，门禁跟着走，不会锁错人。
    /// </summary>
    /// <param name="beat">节拍。</param>
    /// <returns>序号或 −1。</returns>
    public static int InsideIndex(Beat? beat)
    {
        if (beat == null || !string.Equals(beat.Id, SiegeBeat, StringComparison.Ordinal)) return -1;

        for (var i = 0; i < beat.Options.Length; i++)
            if (beat.Options[i].Trim().StartsWith(InsideOption, StringComparison.Ordinal))
                return i;

        return -1;
    }

    /// <summary>
    /// 这一项能不能选：只有"攻城那一拍的间 + 内应门没开"才不许选。
    /// 账本没接上（表读不进来）时一律放行——数据坏了不该把玩家锁死在面板前。
    /// </summary>
    /// <param name="book">账本；null 表示这一局没接信任。</param>
    /// <param name="beat">节拍。</param>
    /// <param name="index">要选的那一项，0 起。</param>
    /// <returns>不许选返回 true。</returns>
    public static bool Locked(TrustBook? book, Beat? beat, int index)
    {
        if (book == null) return false;

        var inside = InsideIndex(beat);
        if (inside < 0 || index != inside) return false;

        return !book.IsOpen(InsideRoute);
    }

    /// <summary>不许选时给玩家的一句话；能选返回 null。</summary>
    /// <param name="book">账本。</param>
    /// <param name="beat">节拍。</param>
    /// <param name="index">要选的那一项。</param>
    /// <returns>拒绝的那句话或 null。</returns>
    public static string? RefusalFor(TrustBook? book, Beat? beat, int index) =>
        Locked(book, beat, index) ? Refusal : null;
}

/// <summary>
/// <c>--trust=&lt;id&gt;</c> 的输出。格式钉在这里，命令行与测试读的是同一份。
/// </summary>
public static class TrustReport
{
    /// <summary>所有行共用的抬头。</summary>
    public const string Prefix = "[信任]";

    /// <summary>
    /// 做了一次动作：<c>[信任] bribe 行贿 → 官望 +3  民望 −2  疑心 +2</c>。
    /// </summary>
    /// <param name="action">表里那条动作。</param>
    /// <param name="explain">已经拼好的那句（含钳制后缀）。</param>
    /// <returns>一行。</returns>
    public static string ActionLine(TrustAction action, string explain)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return $"{Prefix} {action.Id} {action.Label} → {explain}";
    }

    /// <summary>表里查无此条：<c>[信任] 没有这条动作：xxx</c>。</summary>
    /// <param name="id">动作 id。</param>
    /// <returns>一行。</returns>
    public static string UnknownLine(string? id) => $"{Prefix} 没有这条动作：{id}";
}
