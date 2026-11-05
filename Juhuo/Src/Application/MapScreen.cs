using System;
using System.Collections.Generic;
using System.Text;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>地图上一行是什么状态。</summary>
public enum MapRowState
{
    /// <summary>你就站在这儿。</summary>
    Here,

    /// <summary>有路，刻也够。</summary>
    Ready,

    /// <summary>有路，但今天剩下的刻不够走。</summary>
    NoKe,

    /// <summary>表里没有这条路。</summary>
    NoRoute,
}

/// <summary>
/// 地图面板上的一行。
/// </summary>
/// <param name="Hub">目的地 hub id。</param>
/// <param name="Text">整行字（<see cref="MapScreen"/> 已经把「你在这儿」/「今天走不动了」拼进去）。</param>
/// <param name="State">这一行现在能不能走。</param>
public readonly record struct MapRow(string Hub, string Text, MapRowState State)
{
    /// <summary>这一行按下去会真的换场景。</summary>
    public bool Walkable => State == MapRowState.Ready;

    /// <summary>这一行该灰着画。</summary>
    public bool Dim => State is MapRowState.NoKe or MapRowState.NoRoute;
}

/// <summary>
/// 地图面板上的固定字（票号 S3-01c）。格式钉在这里，画与测试读的是同一份。
/// </summary>
public static class MapText
{
    /// <summary>面板标题。</summary>
    public const string Title = "去何处";

    /// <summary>当前所在那一行的尾巴。</summary>
    public const string HereNote = "（你在这儿）";

    /// <summary>走不动那一行的尾巴。</summary>
    public const string StuckNote = "（今天走不动了）";

    /// <summary>面板底部那一行。</summary>
    public const string Foot = "↑↓ 选　·　Enter 走　·　M / Esc 收起";

    /// <summary>走成了的那一句：<c>从城南小院到县城，走了 3 刻。</c></summary>
    /// <param name="from">起点标题。</param>
    /// <param name="to">终点标题。</param>
    /// <param name="cost">花了几刻。</param>
    /// <returns>一句话。</returns>
    public static string Arrived(string from, string to, int cost) => $"从{from}到{to}，走了 {cost} 刻。";

    /// <summary>刻不够的那一句：<c>今天走不动了——余刻 2，要走 3 刻。</c></summary>
    /// <param name="keLeft">还剩几刻。</param>
    /// <param name="cost">这趟要几刻。</param>
    /// <returns>一句话。</returns>
    public static string Stuck(int keLeft, int cost) => $"今天走不动了——余刻 {keLeft}，要走 {cost} 刻。";

    /// <summary>站在这一格还想去同一格。</summary>
    public const string AlreadyHere = "你已经在这儿了。";

    /// <summary>
    /// 教学还没走完就想去别处（S4-01c）。出口与地图问的是同一句话——
    /// 玩家从哪条路走都得先上完这一课。
    /// </summary>
    public const string FinishLessonFirst = "先把这一课学完，再出城。";

    /// <summary>顶上那一行：<c>第 3 天　余刻 5/8</c>。</summary>
    /// <param name="day">第几天。</param>
    /// <param name="keLeft">还剩几刻。</param>
    /// <param name="keBudget">这一处的预算。</param>
    /// <returns>一行字。</returns>
    public static string HeadLine(int day, int keLeft, int keBudget) =>
        $"第 {day} 天　余刻 {keLeft}/{keBudget}";

    /// <summary>一行的字：三态三种写法。</summary>
    /// <param name="hub">目的地。</param>
    /// <param name="state">状态。</param>
    /// <param name="cost">这条路要几刻。</param>
    /// <returns>整行字（不带光标）。</returns>
    public static string RowText(string hub, MapRowState state, int cost) => state switch
    {
        MapRowState.Here => $"· {HubNames.Of(hub)}　{HereNote}",
        MapRowState.NoKe => $"{HubNames.Of(hub)}　{cost} 刻　{StuckNote}",
        MapRowState.NoRoute => $"{HubNames.Of(hub)}　没有路",
        _ => $"{HubNames.Of(hub)}　{cost} 刻",
    };
}

/// <summary>
/// 一次"按地图走"的结果。**没有副作用**：账算完交回去，落库由调用方决定
/// （和 <see cref="TravelBook.Go"/> 一个规矩）。
/// </summary>
/// <param name="Ok">走没走成。</param>
/// <param name="Hub">走成了到哪儿；没成为 null。</param>
/// <param name="Cost">这趟花了 / 该花几刻；同地或没路为 0。</param>
/// <param name="KeLeft">走完之后还剩几刻（没成时原样）。</param>
/// <param name="Message">给玩家看的那一句。</param>
public readonly record struct MapTravelResult(bool Ok, string? Hub, int Cost, int KeLeft, string Message);

/// <summary>
/// 按地图走一趟：查路、比刻、扣刻、报出目的地。
///
/// 规则的唯一来源仍是 <see cref="TravelBook.Go"/>；这一层只补三件事：
/// 同地不走路（<c>Cost</c> 是 0，会变成"走了 0 刻"）、把刻不够翻译成玩家听得懂的那一句、
/// 把 hub id 翻成地名。纯 C#，不认引擎——所以面板、<c>--map</c> 与测试走的是同一条路。
/// </summary>
public static class MapTravel
{
    /// <summary>
    /// 走一趟。
    /// </summary>
    /// <param name="book">位移账。</param>
    /// <param name="from">现在在哪。</param>
    /// <param name="to">要去哪。</param>
    /// <param name="keLeft">还剩几刻。</param>
    /// <returns>结果；失败时 <see cref="MapTravelResult.KeLeft"/> 与传入的一样。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="book"/> 为 null。</exception>
    public static MapTravelResult Go(TravelBook book, string from, string to, int keLeft)
    {
        if (book == null) throw new ArgumentNullException(nameof(book));

        if (string.Equals(from, to, StringComparison.Ordinal))
            return new MapTravelResult(false, null, 0, keLeft, MapText.AlreadyHere);

        var state = new SaveState { Hub = from };
        var left = keLeft;
        var result = book.Go(state, from, to, ref left);

        if (!result.Ok)
        {
            var cost = result.Cost;
            var message = cost > 0
                ? MapText.Stuck(keLeft, cost)
                : TravelBook.NoRouteReason(HubNames.Of(from), HubNames.Of(to));

            return new MapTravelResult(false, null, cost, keLeft, message);
        }

        return new MapTravelResult(
            true,
            result.Hub,
            result.Cost,
            left,
            MapText.Arrived(HubNames.Of(from), HubNames.Of(result.Hub!), result.Cost));
    }
}

/// <summary>
/// 地图面板（M）：七个空间一行一个，↑↓ 选、Enter 走。
///
/// 只算"画哪七行、选在第几行、这一行能不能走"，不知道颜色和控件——
/// 画由 Main 画。刻数、路长一律问 <see cref="TravelBook"/>，这里不抄第二份数字。
/// </summary>
public sealed class MapScreen
{
    private readonly TravelBook _book;
    private readonly string _here;
    private readonly int _day;
    private readonly int _keLeft;
    private readonly int _keBudget;
    private readonly List<MapRow> _rows;

    /// <summary>
    /// 摆开面板。选中默认停在"你在这儿"那一行——玩家一按 M 先看见的是自己站的地方。
    /// </summary>
    /// <param name="book">位移账。</param>
    /// <param name="here">现在所在的 hub id。</param>
    /// <param name="day">第几天（HUD 那一行要念）。</param>
    /// <param name="keLeft">还剩几刻。</param>
    /// <param name="keBudget">这一处的刻预算。</param>
    /// <exception cref="ArgumentNullException"><paramref name="book"/> 为 null。</exception>
    public MapScreen(TravelBook book, string here, int day, int keLeft, int keBudget)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _here = here ?? "hub1";
        _day = day;
        _keLeft = keLeft;
        _keBudget = keBudget;

        _rows = new List<MapRow>(ClockTable.Hubs.Count);

        foreach (var hub in ClockTable.Hubs)
        {
            var cost = _book.Cost(_here, hub);

            var state = string.Equals(hub, _here, StringComparison.Ordinal) ? MapRowState.Here
                : cost < 0 ? MapRowState.NoRoute
                : keLeft < cost ? MapRowState.NoKe
                : MapRowState.Ready;

            _rows.Add(new MapRow(hub, MapText.RowText(hub, state, cost < 0 ? 0 : cost), state));
        }

        Selected = Math.Max(0, IndexOf(_here));
    }

    /// <summary>面板标题。</summary>
    public string Title => MapText.Title;

    /// <summary>顶上那一行。</summary>
    public string Head => MapText.HeadLine(_day, _keLeft, _keBudget);

    /// <summary>底下那行按键提示。</summary>
    public string Foot => MapText.Foot;

    /// <summary>上下移的光标（0 起）。</summary>
    public int Selected { get; private set; }

    /// <summary>最近一次没走成的原因；换一行就清掉。</summary>
    public string Note { get; private set; } = "";

    /// <summary>现在这一处。</summary>
    public string Here => _here;

    /// <summary>七行，顺序即 <see cref="ClockTable.Hubs"/>。</summary>
    /// <returns>七行。</returns>
    public IReadOnlyList<MapRow> Rows() => _rows;

    /// <summary>光标那一行。</summary>
    public MapRow Current => _rows[Math.Clamp(Selected, 0, _rows.Count - 1)];

    /// <summary>
    /// 光标底下要说的那一句：没走成的原因优先，其次是"走不动"那一行自己的话。
    /// </summary>
    public string Message
    {
        get
        {
            if (Note.Length > 0) return Note;
            return Current.State == MapRowState.NoKe ? Current.Text : "";
        }
    }

    /// <summary>光标挪一格。到头就停，不绕圈——七行里绕圈比按不动更难受。</summary>
    /// <param name="delta">−1 / +1。</param>
    public void Move(int delta)
    {
        Selected = Math.Clamp(Selected + delta, 0, _rows.Count - 1);
        Note = "";
    }

    /// <summary>直接把光标挪到某一行。</summary>
    /// <param name="index">0 起。</param>
    public void Select(int index)
    {
        Selected = Math.Clamp(index, 0, _rows.Count - 1);
        Note = "";
    }

    /// <summary>记账没走成的原因（由 Main 在 <see cref="MapTravel.Go"/> 之后塞进来）。</summary>
    /// <param name="message">那一句人话。</param>
    public void Refuse(string message) => Note = message;

    /// <summary>第几行是某个 hub；不在地图上返回 −1。</summary>
    /// <param name="hub">hub id。</param>
    /// <returns>0 起的行号。</returns>
    public int IndexOf(string? hub)
    {
        for (var i = 0; i < _rows.Count; i++)
            if (string.Equals(_rows[i].Hub, hub, StringComparison.Ordinal))
                return i;

        return -1;
    }
}

/// <summary>
/// <c>--map</c> 的四行：当前在哪、六个目的地各要几刻、走一趟成什么样、再走一趟为什么走不动。
///
/// 走的是真的 <see cref="MapTravel.Go"/>，不是另写一条捷径——打出来的就是面板上按出来的。
/// 只动自己算的四个整数，不碰存档。
/// </summary>
public static class MapReport
{
    /// <summary>所有行共用的抬头。</summary>
    public const string Prefix = "[图]";

    /// <summary>
    /// 跑一趟，把四行交回来。默认从 hub1、余 8 刻起（新档第一天早上的样子）。
    /// </summary>
    /// <param name="book">位移账。</param>
    /// <param name="here">起点 hub id。</param>
    /// <param name="keLeft">还剩几刻。</param>
    /// <returns>四行。</returns>
    public static IReadOnlyList<string> Lines(TravelBook book, string here = "hub1", int keLeft = 8)
    {
        if (book == null) throw new ArgumentNullException(nameof(book));

        var lines = new List<string>(4)
        {
            $"{Prefix} 当前 {HubNames.Of(here)}（{here}），余刻 {keLeft}",
        };

        // 六个目的地，按空间顺序；路长为 0 的不列（表里不该有，但坏了也别印成"0 刻"）。
        var legs = new StringBuilder();
        foreach (var hub in ClockTable.Hubs)
        {
            if (string.Equals(hub, here, StringComparison.Ordinal)) continue;

            var cost = book.Cost(here, hub);
            if (cost < 0) continue;

            if (legs.Length > 0) legs.Append('｜');
            legs.Append(HubNames.Of(hub)).Append(' ').Append(cost).Append(" 刻");
        }

        lines.Add($"{Prefix} {legs}");

        // 第三行：走一趟"走得起的里面最远的那条"——最远，才看得出刻是怎么少下去的。
        var first = FarthestAffordable(book, here, keLeft);
        var one = MapTravel.Go(book, here, first, keLeft);

        lines.Add(one.Ok
            ? $"{Prefix} 走一趟：{HubNames.Of(here)} → {HubNames.Of(one.Hub!)}，耗 {one.Cost} 刻，余 {one.KeLeft}"
            : $"{Prefix} 走一趟：{HubNames.Of(here)} → {HubNames.Of(first)}，走不动（{one.Message}）");

        // 第四行：从新位置再想走最远的那条，这时多半已经走不动了——这一行就是面板灰显的那一句。
        var now = one.Hub ?? here;
        var left = one.KeLeft;
        var far = Farthest(book, now);

        var two = MapTravel.Go(book, now, far, left);

        lines.Add(two.Ok
            ? $"{Prefix} 再走一趟：{HubNames.Of(now)} → {HubNames.Of(two.Hub!)}，耗 {two.Cost} 刻，余 {two.KeLeft}"
            : $"{Prefix} 再走一趟：余刻 {left}，去{HubNames.Of(far)}要 {Math.Max(0, book.Cost(now, far))} 刻 —— 走不动");

        return lines;
    }

    /// <summary>从这儿走、刻还够的那些里面最贵的一条；一条都走不起就走最便宜的。</summary>
    private static string FarthestAffordable(TravelBook book, string here, int keLeft)
    {
        var best = "";
        var bestCost = -1;
        var cheapest = "";
        var cheapestCost = int.MaxValue;

        foreach (var hub in ClockTable.Hubs)
        {
            if (string.Equals(hub, here, StringComparison.Ordinal)) continue;

            var cost = book.Cost(here, hub);
            if (cost < 0) continue;

            if (cost < cheapestCost) { cheapest = hub; cheapestCost = cost; }
            if (cost > keLeft) continue;
            if (cost > bestCost) { best = hub; bestCost = cost; }
        }

        return best.Length > 0 ? best : cheapest;
    }

    /// <summary>从这儿走路最长的一条（不管刻够不够）。</summary>
    private static string Farthest(TravelBook book, string here)
    {
        var best = "";
        var bestCost = -1;

        foreach (var hub in ClockTable.Hubs)
        {
            if (string.Equals(hub, here, StringComparison.Ordinal)) continue;

            var cost = book.Cost(here, hub);
            if (cost <= bestCost) continue;

            best = hub;
            bestCost = cost;
        }

        return best.Length > 0 ? best : here;
    }
}
