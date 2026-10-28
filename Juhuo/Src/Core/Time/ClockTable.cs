using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>
/// 刻预算表：七个序章空间各能花多少刻。
/// 数值唯一源 = Docs/Rules/rule_time_clock.md §2（合计 88 刻）。
/// 这里只放数字，不放逻辑；逻辑在 <see cref="Clock"/>。
/// </summary>
public static class ClockTable
{
    /// <summary>七个空间加起来一共 88 刻（rule_time_clock.md §2 合计行）。</summary>
    public const int TotalBudgetKe = 88;

    private static readonly Dictionary<string, int> Budgets = new(StringComparer.Ordinal)
    {
        ["hub1"] = 8,   // 城南小院
        ["hub2"] = 10,  // 逃亡路 · 废驿
        ["hub3"] = 16,  // 白幡村
        ["hub4"] = 20,  // 县城
        ["hub5"] = 12,  // 山神庙
        ["hub6"] = 14,  // 官仓 · 破庙
        ["hub7"] = 8,   // 城下营地
    };

    /// <summary>按序列出全部空间 id。</summary>
    public static IReadOnlyList<string> Hubs { get; } = new[] { "hub1", "hub2", "hub3", "hub4", "hub5", "hub6", "hub7" };

    /// <summary>
    /// 查一个空间的刻预算。查不到（未知 hub / null / 空串）返回 null，不抛异常。
    /// </summary>
    /// <param name="hub">空间 id，如 "hub3"。</param>
    /// <returns>预算刻数 B；查不到返回 null。</returns>
    public static int? ForHub(string? hub)
    {
        if (string.IsNullOrEmpty(hub)) return null;
        return Budgets.TryGetValue(hub, out var b) ? b : null;
    }

    /// <summary>
    /// 查一个空间的半刻预算（2B）。查不到返回 null。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>半刻预算；查不到返回 null。</returns>
    public static int? BudgetHalfForHub(string? hub)
    {
        var b = ForHub(hub);
        return b == null ? null : b.Value * 2;
    }

    /// <summary>
    /// 这个 id 是不是已知空间。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>已知返回 true。</returns>
    public static bool IsKnownHub(string? hub) => ForHub(hub) != null;
}
