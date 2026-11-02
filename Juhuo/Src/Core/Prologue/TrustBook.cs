using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>
/// 一次 <see cref="TrustBook.Apply"/> 真正发生了什么。
///
/// 记的是**实际落在状态上的增量**，不是表里写的数字：被 0..100 钳掉的部分不算数，
/// 只看 <see cref="Clamped"/> 就知道"这一下有一截打在墙上"。
/// </summary>
/// <param name="Guan">官望实际变动。</param>
/// <param name="Min">民望实际变动。</param>
/// <param name="Yi">疑心实际变动。</param>
/// <param name="Clamped">有没有哪条轴撞到 0 或 100 被钳过。</param>
/// <param name="Applied">表里有没有这条动作；false 时三个增量必为 0，状态没动。</param>
public readonly record struct TrustDelta(int Guan, int Min, int Yi, bool Clamped, bool Applied = true)
{
    /// <summary>什么都没发生：动作查无此行。</summary>
    public static TrustDelta None => new(0, 0, 0, false, false);

    /// <summary>三条轴都没动。</summary>
    public bool IsEmpty => Guan == 0 && Min == 0 && Yi == 0;
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

        var clamped = false;
        var guan = Step(State.Guan, action.Guan, ref clamped, out var dGuan);
        var min = Step(State.Min, action.Min, ref clamped, out var dMin);
        var yi = Step(State.Yi, action.Yi, ref clamped, out var dYi);

        State.Guan = guan;
        State.Min = min;
        State.Yi = yi;

        return new TrustDelta(dGuan, dMin, dYi, clamped, true);
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
    /// 没动的轴不出现；三条都没动则是空串。
    /// </summary>
    /// <param name="delta">一次 <see cref="Apply"/> 的结果。</param>
    /// <returns>一句话；无事发生返回空串。</returns>
    public string Explain(TrustDelta delta)
    {
        var parts = new List<string>(3);
        if (delta.Guan != 0) parts.Add(Segment("官望", delta.Guan));
        if (delta.Min != 0) parts.Add(Segment("民望", delta.Min));
        if (delta.Yi != 0) parts.Add(Segment("疑心", delta.Yi));

        return string.Join("  ", parts);
    }

    /// <summary>某条轴现在多少。表里的轴值已在读表时校验过，这里不会落空。</summary>
    private int AxisValue(string axis) => axis == TrustRoute.AxisGuan ? State.Guan : State.Min;

    /// <summary>
    /// 加一次步长并钳在 0..100。返回落定后的值，<paramref name="applied"/> 是实际变动。
    ///
    /// 先从"钳过的当前值"起算：存档里若因手改出现 120 这种数，这一次动作只该按 100 计，
    /// 不该把旧账算到这一次头上。
    /// </summary>
    private static int Step(int current, int delta, ref bool clamped, out int applied)
    {
        var from = Math.Clamp(current, MinValue, MaxValue);
        var target = from + delta;

        if (target < MinValue || target > MaxValue) clamped = true;

        var to = Math.Clamp(target, MinValue, MaxValue);
        applied = to - from;
        return to;
    }

    /// <summary>一条轴的半句话：<c>官望 +3</c> / <c>民望 −2</c>（用真正的减号，不用连字符）。</summary>
    private static string Segment(string name, int value) =>
        value > 0 ? $"{name} +{value}" : $"{name} \u2212{-value}";
}
