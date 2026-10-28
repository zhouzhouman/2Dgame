using System;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// Hub1（城南小院）"旧部来敲门"的计时。
///
/// 按**刻**算，不按真实秒算——刻是序章唯一的时间单位（CANON 3 / rule_time_clock.md），
/// 再开一套秒表就是第二份真相。判断只用 <see cref="Clock.KeUsedKe"/>，这里不自己累加。
///
/// 触发线 N 现在是常量 3（票面缺省）。等 <c>hubs.csv</c> 落地，把 <see cref="DefaultTriggerKe"/>
/// 换成读表即可，调用方不用动。
/// </summary>
public sealed class KnockTimer
{
    /// <summary>敲门只发生在这一处。</summary>
    public const string KnockHub = "hub1";

    /// <summary>敲过一次就置位，存档带着它，重进不重放。</summary>
    public const string HappenedFlag = "hub1.knock.happened";

    /// <summary>票面缺省的触发刻。</summary>
    public const int DefaultTriggerKe = 3;

    /// <summary>用几刻触发。</summary>
    public int TriggerKe { get; }

    /// <summary>
    /// 建一个计时。
    /// </summary>
    /// <param name="triggerKe">到第几刻敲门。≤0 时退回 <see cref="DefaultTriggerKe"/>，不静默用 0。</param>
    public KnockTimer(int triggerKe = DefaultTriggerKe)
    {
        TriggerKe = triggerKe > 0 ? triggerKe : DefaultTriggerKe;
    }

    /// <summary>
    /// 该不该敲。三条同时成立：在 hub1、没敲过、刻够了。
    /// </summary>
    /// <param name="hub">当前空间。</param>
    /// <param name="keUsed">已用整刻（读 <see cref="Clock.KeUsedKe"/>，不要自己算）。</param>
    /// <param name="happened">旗标 <see cref="HappenedFlag"/> 是否已置。</param>
    public bool ShouldKnock(string? hub, int keUsed, bool happened)
        => string.Equals(hub, KnockHub, StringComparison.Ordinal)
           && !happened
           && keUsed >= TriggerKe;

    /// <summary>
    /// 到点就置旗标并返回 true。置位失败（名字超长、仓库满）也返回 false——
    /// 宁可这一局不敲，也不要在存档里留一个"敲过"的假记录。
    /// </summary>
    /// <param name="flags">旗标仓库。</param>
    /// <param name="hub">当前空间。</param>
    /// <param name="keUsed">已用整刻。</param>
    /// <returns>这一次真的敲了返回 true；已经敲过或还没到点返回 false。</returns>
    public bool TryKnock(FlagStore? flags, string? hub, int keUsed)
    {
        if (flags is null) return false;
        if (!ShouldKnock(hub, keUsed, flags.Has(HappenedFlag, hub))) return false;

        return flags.Set(HappenedFlag);
    }
}
