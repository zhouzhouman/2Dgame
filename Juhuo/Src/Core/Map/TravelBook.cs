using System;

namespace Juhuo.Core;

/// <summary>
/// 一次位移的结果。
///
/// 失败也是正常结果（没这条路、刻不够），所以不抛异常——UI 拿到
/// <see cref="Reason"/> 直接显示那一句人话就行（照 <see cref="TradeResult"/> 的规矩）。
/// </summary>
/// <param name="Ok">走没走成。没成时 <see cref="Hub"/> 为 null，<see cref="Cost"/> 仍可能是这条路该花的刻数（给 UI 提示用）。</param>
/// <param name="Reason">没成时给玩家看的一句话；成了为 null。</param>
/// <param name="Hub">成了的话：到了哪个空间（= 目标 hub）。没成为 null。</param>
/// <param name="Cost">这趟花了 / 该花几刻。没这条路为 0。</param>
public readonly record struct TravelResult(bool Ok, string? Reason, string? Hub, int Cost)
{
    /// <summary>一次失败的位移：只带原因，什么都没动。</summary>
    /// <param name="reason">给玩家看的一句话。</param>
    /// <param name="cost">这条路该花几刻；没这条路传 0。</param>
    /// <returns>失败结果。</returns>
    public static TravelResult Fail(string reason, int cost = 0) => new(false, reason, null, cost);

    /// <summary>一次成功的位移。</summary>
    /// <param name="hub">到了哪个空间。</param>
    /// <param name="cost">花了几个刻。</param>
    /// <returns>成功结果。</returns>
    public static TravelResult Arrive(string hub, int cost) => new(true, null, hub, cost);
}

/// <summary>
/// 位移的规则层（设计文档 §7 七个空间之间的走动）。
///
/// **只算账**：查路、比刻、扣刻、报出目的地。
/// 不碰 <see cref="SaveState.Hub"/>、不碰 World、不写存档——落库由调用方决定，
/// 这样"这一趟走没走成"与"存档写成什么样"各归各的账（和 <see cref="TradeBook"/> 一个规矩）。
/// 刻账本身由调用方的 <see cref="Clock"/> 管，这里只借一个"还剩几刻"的整数。
///
/// 与 <see cref="Clock.SpendTravel"/> 的分工：那一笔是"离开当前空间"的固定开销（2 刻，
/// 记在离开的那个空间）；这里是空间与空间之间的路程长短，走哪条路由 travel.csv 说了算。
/// </summary>
public sealed class TravelBook
{
    /// <summary>没有这条路时 <see cref="Cost"/> 的返回值。调用方据此灰掉选项。</summary>
    public const int NoRoute = -1;

    /// <summary>最贵的一条路要几刻，与 <see cref="TravelTable.MaxKe"/> 同源。</summary>
    public const int MaxKe = TravelTable.MaxKe;

    private readonly TravelTable _table;

    /// <summary>
    /// 建一本位移账。表由外面给：数据来自 travel.csv。
    /// </summary>
    /// <param name="table">位移表。</param>
    public TravelBook(TravelTable table)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    /// <summary>
    /// 从 <paramref name="from"/> 到 <paramref name="to"/> 要几刻。
    ///
    /// 三种情况：同地 0 刻（站着不动不花钱）；有路 = 表里的 ke；没这条路 = <see cref="NoRoute"/>。
    /// **不判刻够不够**——那是 <see cref="CanGo"/> / <see cref="Go"/> 的事，
    /// 因为 UI 要能同时画出"要 3 刻"和"你只剩 1 刻"。
    /// </summary>
    /// <param name="from">起点空间 id。</param>
    /// <param name="to">终点空间 id。</param>
    /// <returns>刻数（≥0），或 <see cref="NoRoute"/>。</returns>
    public int Cost(string? from, string? to)
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return NoRoute;
        if (string.Equals(from, to, StringComparison.Ordinal)) return 0;

        return _table.TryGet(from, to, out var leg) ? leg.Ke : NoRoute;
    }

    /// <summary>
    /// 现在走不走得成：有这条路，且剩下的刻够。同地视为走得成（0 刻，等于原地不动）。
    /// </summary>
    /// <param name="from">起点空间 id。</param>
    /// <param name="to">终点空间 id。</param>
    /// <param name="keLeft">还剩几刻。</param>
    /// <returns>走得成返回 true。</returns>
    public bool CanGo(string? from, string? to, int keLeft)
    {
        var cost = Cost(from, to);
        return cost >= 0 && keLeft >= cost;
    }

    /// <summary>
    /// 走一趟：扣刻，报出到了哪个空间。
    ///
    /// 判定的先后是**路 → 刻**：没这条路的时候不该说"刻不够"（那会让你以为多攒点刻就能走）。
    /// 失败时 <paramref name="keLeft"/> 一个字都不动，<see cref="SaveState"/> 也一个字都不改——
    /// 白跑一趟，但账没乱。
    ///
    /// <paramref name="state"/> 只用来看玩家现在在哪：起点对不上就拒绝，
    /// 这样 UI 传错参数时不会把人从千里之外凭空挪走。
    /// </summary>
    /// <param name="state">存档（只读，不写回）。</param>
    /// <param name="from">玩家**现在**所在的空间 id。</param>
    /// <param name="to">要去哪。</param>
    /// <param name="keLeft">还剩几刻；走成了就地扣掉。</param>
    /// <returns>位移结果；成功时 <see cref="TravelResult.Hub"/> 是新的空间 id，由调用方落库。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 为 null。</exception>
    public TravelResult Go(SaveState state, string from, string to, ref int keLeft)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (!string.Equals(state.Hub, from, StringComparison.Ordinal))
            return TravelResult.Fail($"你现在在「{state.Hub}」，不在「{from}」。");

        var cost = Cost(from, to);
        if (cost < 0)
            return TravelResult.Fail(NoRouteReason(from, to));

        if (keLeft < cost)
            return TravelResult.Fail(NotEnoughKeReason(cost, keLeft), cost);

        keLeft -= cost;
        return TravelResult.Arrive(to, cost);
    }

    /// <summary>没这条路时给玩家看的一句话。</summary>
    /// <param name="from">起点空间 id。</param>
    /// <param name="to">终点空间 id。</param>
    /// <returns>一句话。</returns>
    public static string NoRouteReason(string? from, string? to) =>
        $"从「{from}」到「{to}」没有路。";

    /// <summary>刻不够时给玩家看的一句话。</summary>
    /// <param name="cost">这条路要几刻。</param>
    /// <param name="keLeft">还剩几刻。</param>
    /// <returns>一句话。</returns>
    public static string NotEnoughKeReason(int cost, int keLeft) =>
        $"刻不够：这趟要 {cost} 刻，只剩 {keLeft} 刻。";
}
