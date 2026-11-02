using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>三条轴，可组合成位标记：一次动作里哪几条被 0..100 钳过。</summary>
[Flags]
public enum TrustAxes
{
    /// <summary>一条都没有。</summary>
    None = 0,

    /// <summary>官望。</summary>
    Guan = 1,

    /// <summary>民望。</summary>
    Min = 2,

    /// <summary>疑心。</summary>
    Yi = 4,
}

/// <summary>
/// 一次 <see cref="TrustBook.Apply"/> 真正发生了什么。
///
/// 两套数一起记：<see cref="Guan"/> / <see cref="Min"/> / <see cref="Yi"/> 是**实际落在状态上的增量**，
/// <see cref="IntendedGuan"/> / <see cref="IntendedMin"/> / <see cref="IntendedYi"/> 是**表里写的原始增量**。
/// 被 0..100 钳掉的那一截只存在于后者——比如民望已经是 0 还要 −2，实际增量是 0，
/// 但玩家该看见的是"民望 −2（已见底）"，不是什么都不说。
/// 哪几条被钳过看 <see cref="ClampedAxes"/>，也能按轴问 <see cref="WasClamped"/>。
/// </summary>
/// <param name="Guan">官望实际变动。</param>
/// <param name="Min">民望实际变动。</param>
/// <param name="Yi">疑心实际变动。</param>
/// <param name="Clamped">有没有哪条轴撞到 0 或 100 被钳过（等价于 <see cref="ClampedAxes"/> 非空）。</param>
/// <param name="Applied">表里有没有这条动作；false 时三个增量必为 0，状态没动。</param>
/// <param name="IntendedGuan">官望表里写的原始增量。</param>
/// <param name="IntendedMin">民望表里写的原始增量。</param>
/// <param name="IntendedYi">疑心表里写的原始增量。</param>
/// <param name="ClampedAxes">被钳过的轴；没钳是 <see cref="TrustAxes.None"/>。</param>
public readonly record struct TrustDelta(
    int Guan,
    int Min,
    int Yi,
    bool Clamped,
    bool Applied = true,
    int IntendedGuan = 0,
    int IntendedMin = 0,
    int IntendedYi = 0,
    TrustAxes ClampedAxes = TrustAxes.None)
{
    /// <summary>什么都没发生：动作查无此行。</summary>
    public static TrustDelta None => new(0, 0, 0, false, false);

    /// <summary>三条轴都没动。</summary>
    public bool IsEmpty => Guan == 0 && Min == 0 && Yi == 0;

    /// <summary>这一条轴有没有被钳过。</summary>
    /// <param name="axis">要问的轴。</param>
    /// <returns>被钳过返回 true。</returns>
    public bool WasClamped(TrustAxes axis) => (ClampedAxes & axis) != 0;
}

/// <summary>
/// 某个地区的双信任轴账本（设计文档 §16.4）。
///
/// 三件事：把 <c>trust.csv</c> 的一行动作加到 <see cref="TrustState"/> 上（钳 0..100，回报实际增量）；
/// 按 <c>routes.csv</c> 判两条攻城路线开没开；把增量说成一句人话给 UI。
/// 纯 C#，不碰引擎、不写存档——存的是 <see cref="TrustState"/> 本身，调用方拿它序列化。
/// </summary>
public sealed class TrustBook
{
    /// <summary>三轴的下限。</summary>
    public const int MinValue = 0;

    /// <summary>三轴的上限。</summary>
    public const int MaxValue = 100;

    private readonly TrustTable _table;
    private readonly RouteTable _routes;

    /// <summary>
    /// 建一本账。表与状态都是外面给的：表来自数据，状态来自存档。
    /// </summary>
    /// <param name="state">这个地区的三条轴，就地改。</param>
    /// <param name="table">信任动作表。</param>
    /// <param name="routes">攻城路线表。</param>
    public TrustBook(TrustState state, TrustTable table, RouteTable routes)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    /// <summary>这个地区的三条轴（引用，就地改）。</summary>
    public TrustState State { get; }

    /// <summary>全部攻城路线，按文件顺序——含着没开的，UI 要用。</summary>
    public IReadOnlyList<TrustRoute> Routes => _routes.All;

    /// <summary>
    /// 做一件事的三轴代价。未知 id 返回 <see cref="TrustDelta.None"/>、状态不变、**不抛**：
    /// 数据缺一行不该让游戏崩，只该让这一次点击白点。
    /// </summary>
    /// <param name="actionId">trust.csv 里的动作 id。</param>
    /// <returns>实际发生的增量。</returns>
    public TrustDelta Apply(string? actionId)
    {
        if (!_table.TryGet(actionId, out var action)) return TrustDelta.None;

        var clamped = TrustAxes.None;
        var guan = Step(State.Guan, action.Guan, ref clamped, TrustAxes.Guan, out var dGuan);
        var min = Step(State.Min, action.Min, ref clamped, TrustAxes.Min, out var dMin);
        var yi = Step(State.Yi, action.Yi, ref clamped, TrustAxes.Yi, out var dYi);

        State.Guan = guan;
        State.Min = min;
        State.Yi = yi;

        return new TrustDelta(
            dGuan, dMin, dYi, clamped != TrustAxes.None, true,
            action.Guan, action.Min, action.Yi, clamped);
    }

    /// <summary>
    /// 这条路线开没开：它看的那条轴 ≥ 门槛就算开（等于算开）。未知 id 一律算没开，不抛。
    /// </summary>
    /// <param name="routeId">routes.csv 里的路线 id。</param>
    /// <returns>开着返回 true。</returns>
    public bool IsOpen(string? routeId)
    {
        if (!_routes.TryGet(routeId, out var route)) return false;

        return AxisValue(route.Axis) >= route.Threshold;
    }

    /// <summary>
    /// 现在开着的路线 id，按 routes.csv 的文件顺序（顺序稳定，UI 不必自己排）。
    /// </summary>
    /// <returns>路线 id 列表；一条都没开则空。</returns>
    public IReadOnlyList<string> OpenRoutes()
    {
        var open = new List<string>(_routes.Count);
        foreach (var route in _routes.All)
            if (AxisValue(route.Axis) >= route.Threshold)
                open.Add(route.Id);

        return open;
    }

    /// <summary>
    /// 把一次增量说成一句话，给 UI 用：<c>官望 +3  民望 −2  疑心 +1</c>。
    ///
    /// 说的是**表里想加多少**，不是实际落了多少：民望已经是 0 还要 −2，照样说"民望 −2"，
    /// 只是在该轴后面补一句 <c>（已见底）</c>；到顶同理补 <c>（已到顶）</c>。
    /// 这样玩家才知道"这一下还想扣，只是扣不动了"，而不是以为这次动作只配官方。
    /// 意图为 0 的轴不出现；三条都没动则是空串。
    /// </summary>
    /// <param name="delta">一次 <see cref="Apply"/> 的结果。</param>
    /// <returns>一句话；无事发生返回空串。</returns>
    public string Explain(TrustDelta delta)
    {
        var parts = new List<string>(3);
        AddAxis(parts, "官望", delta.IntendedGuan, delta, TrustAxes.Guan);
        AddAxis(parts, "民望", delta.IntendedMin, delta, TrustAxes.Min);
        AddAxis(parts, "疑心", delta.IntendedYi, delta, TrustAxes.Yi);

        return string.Join("  ", parts);
    }

    /// <summary>某条轴现在多少。表里的轴值已在读表时校验过，这里不会落空。</summary>
    private int AxisValue(string axis) => axis == TrustRoute.AxisGuan ? State.Guan : State.Min;

    /// <summary>
    /// 加一次步长并钳在 0..100。返回落定后的值，<paramref name="applied"/> 是实际变动；
    /// 真被钳了就把 <paramref name="axis"/> 记进 <paramref name="clamped"/>。
    ///
    /// 先从"钳过的当前值"起算：存档里若因手改出现 120 这种数，这一次动作只该按 100 计，
    /// 不该把旧账算到这一次头上。
    /// </summary>
    private static int Step(int current, int delta, ref TrustAxes clamped, TrustAxes axis, out int applied)
    {
        var from = Math.Clamp(current, MinValue, MaxValue);
        var target = from + delta;

        if (target < MinValue || target > MaxValue) clamped |= axis;

        var to = Math.Clamp(target, MinValue, MaxValue);
        applied = to - from;
        return to;
    }

    /// <summary>一条轴的半句话：<c>官望 +3</c> / <c>民望 −2</c>（用真正的减号，不用连字符）。</summary>
    private static string Segment(string name, int value) =>
        value > 0 ? $"{name} +{value}" : $"{name} \u2212{-value}";

    /// <summary>
    /// 按意图追加一条轴：意图为 0 的不出现；被钳过的在**这条轴**后面标方向。
    /// 方向看意图的正负——只有往正走才可能撞 100，只有往负走才可能撞 0。
    /// </summary>
    private static void AddAxis(List<string> parts, string name, int intended, TrustDelta delta, TrustAxes axis)
    {
        if (intended == 0) return;

        parts.Add(Segment(name, intended) + (delta.WasClamped(axis) ? Mark(intended) : ""));
    }

    /// <summary>被墙吃掉那一截的去向：要加没加上 = 到顶，要减没减成 = 见底。</summary>
    private static string Mark(int intended) => intended > 0 ? "（已到顶）" : "（已见底）";
}
