using System;

namespace Juhuo.Core;

/// <summary>
/// 某个空间的刻账快照。对应存档 <c>hubs.*.ke_half</c> 与 <c>hubs.*.nudges</c>。
/// 只存整数半刻，不存浮点。
/// </summary>
public sealed class ClockState
{
    /// <summary>已用半刻（0 – 2B+16）。</summary>
    public int KeHalf { get; set; }

    /// <summary>已触发催促次数（0 – 5）。</summary>
    public int Nudges { get; set; }
}

/// <summary>
/// 一个空间的刻账：花了多少刻、还剩多少刻、被催了几次。
/// 单位一律是"半刻"（ke_half）整数，禁止浮点累计（rule_time_clock.md §1）。
/// 规则源：Docs/Rules/rule_time_clock.md §2 预算表、§3 消耗表、§4 边界。
/// </summary>
public sealed class Clock
{
    /// <summary>越线之后还能记多少半刻（rule_time_clock.md §4 OVERFLOW_CAP）。</summary>
    public const int OverflowCapHalf = 16;

    /// <summary>
    /// 每多欠这么多半刻就记一次催促（rule_time_clock.md §4 NUDGE_STEP = 8 半刻 = 4 刻）。
    /// 取 8 而非更小的值，是为了让催促不压过"每 4 刻死一个病人"这条主线节拍（D-015）。
    /// </summary>
    public const int NudgeStepHalf = 8;

    /// <summary>
    /// 催促次数上限。规则卡 §边界给的是 1 + OVERFLOW_CAP/NUDGE_STEP = 3，
    /// 这里取 5，与 schema_v2.md §C 的 <c>hubs.*.nudges</c> 取值域 0–5 对齐（D-015）。
    /// </summary>
    public const int MaxNudges = 5;

    /// <summary>搜查一次的刻数基准（points.cost 的取值上限也是 3）。</summary>
    public const int MaxSearchCostKe = 3;

    /// <summary>重复对话的刻数。</summary>
    public const int TalkRepeatCostKe = 1;

    /// <summary>赶路（离开当前空间）的刻数。</summary>
    public const int TravelCostKe = 2;

    /// <summary>被看见打断的刻数。</summary>
    public const int InterruptCostKe = 2;

    /// <summary>睡觉 / 休息的刻数。</summary>
    public const int SleepCostKe = 2;

    /// <summary>给药救一个人的半刻数（0.5 刻）。</summary>
    public const int DoseCostHalf = 1;

    /// <summary>被连续发现第三次（盘问）的刻数。替代打断，不是叠加。</summary>
    public const int InterrogateCostKe = 4;

    /// <summary>B 缺失或非法时的兜底预算（rule_time_clock.md §边界）。</summary>
    private const int FallbackBudgetKe = 8;

    private string _hub;
    private int _budgetKe;
    private int _keHalf;
    private int _nudges;

    /// <summary>已经播报掉（消费掉）的催促次数。存档里不单列，读档时按"全部已播"处理。</summary>
    private int _nudged;

    /// <summary>
    /// 开一本新账。预算按 hub 查 <see cref="ClockTable"/>；查不到按 8 刻兜底（不静默用 0）。
    /// </summary>
    /// <param name="hub">空间 id，默认 "hub1"。</param>
    public Clock(string hub = "hub1")
    {
        _hub = hub ?? "";
        _budgetKe = ClockTable.ForHub(_hub) ?? FallbackBudgetKe;
    }

    /// <summary>当前空间的 id。</summary>
    public string Hub => _hub;

    /// <summary>已用半刻。</summary>
    public int KeHalf => _keHalf;

    /// <summary>预算半刻（2B）。</summary>
    public int KeBudgetHalf => _budgetKe * 2;

    /// <summary>已用整刻（半刻向下取整，故意对玩家有利）。</summary>
    public int KeUsedKe => _keHalf / 2;

    /// <summary>剩余整刻，不为负。</summary>
    public int KeLeftKe => Math.Max(0, _budgetKe - KeUsedKe);

    /// <summary>已触发催促次数。</summary>
    public int Nudges => _nudges;

    /// <summary>
    /// 第 <paramref name="n"/> 次催促触发时的半刻位置（n 从 1 起）：
    /// 第 1 次正好是 <see cref="KeBudgetHalf"/>（用光预算），此后每
    /// <see cref="NudgeStepHalf"/> 半刻再催一次（rule_time_clock.md §4）。
    /// </summary>
    /// <param name="n">第几次，从 1 起。</param>
    /// <returns>触发所需的半刻数；n &lt; 1 或超过 <see cref="MaxNudges"/> 返回 -1。</returns>
    public int NudgeAtHalf(int n)
    {
        if (n < 1 || n > MaxNudges) return -1;

        return KeBudgetHalf + NudgeStepHalf * (n - 1);
    }

    /// <summary>
    /// 账上已经触发了、但还没播报的催促次数（0–5）。
    /// 结算动作时只加计数，Game 播完事件后调 <see cref="MarkNudged"/> 消费掉，
    /// 这样"花完刻"与"播代价"能分成两步（rule_time_clock.md §边界 结算顺序 6）。
    /// </summary>
    public int PendingNudges
    {
        get
        {
            var pending = _nudges - _nudged;
            return pending < 0 ? 0 : pending;
        }
    }

    /// <summary>
    /// 消费一次催促：Game 播完那条事件后调用。
    /// </summary>
    /// <returns>确实有可消费的催促返回 true；没有返回 false（不重复播）。</returns>
    public bool MarkNudged()
    {
        if (PendingNudges <= 0) return false;

        _nudged++;
        return true;
    }

    /// <summary>
    /// 探索率：已用整刻 / 预算整刻，夹到 0–1。只在这里做一次除法，
    /// 刻本身仍按半刻整数累计（rule_time_clock.md §边界 取整方式）。
    /// </summary>
    public double Coverage
    {
        get
        {
            if (_budgetKe <= 0) return 0;

            var ratio = KeUsedKe / (double)_budgetKe;
            if (ratio < 0) return 0;
            return ratio > 1 ? 1 : ratio;
        }
    }

    /// <summary>
    /// 预算是不是已经用尽或超出（剩 0 刻）。与催促触发线是同一条线：半刻 ≥ 2B。
    /// </summary>
    public bool IsOverBudget => KeLeftKe <= 0;

    /// <summary>
    /// 搜查一个点。首次、重搜、空点一样收费（rule_time_clock.md §3）。
    /// 超预算不拦操作，只记催促。
    /// </summary>
    /// <param name="costKe">该点的刻数，0–3；超出按 3 算，负数视为非法并返回 false。</param>
    /// <returns>这次搜查是否计费成功。</returns>
    public bool SpendSearch(int costKe = 1)
    {
        if (costKe < 0) return false;
        if (costKe > MaxSearchCostKe) costKe = MaxSearchCostKe;

        return AddHalfKe(costKe * 2);
    }

    /// <summary>
    /// 跟人说话。首次对话免费（D21 / CANON 硬约束 3），重复对话收 1 刻。
    /// </summary>
    /// <param name="first">是不是第一次跟这个人说。</param>
    /// <returns>这次对话是否结算成功。</returns>
    public bool SpendTalk(bool first)
    {
        if (first) return true;

        return AddHalfKe(TalkRepeatCostKe * 2);
    }

    /// <summary>
    /// 赶路离开当前空间：收 2 刻，记在离开的那个空间。
    /// </summary>
    /// <returns>是否结算成功。</returns>
    public bool SpendTravel() => AddHalfKe(TravelCostKe * 2);

    /// <summary>
    /// 被守卫看见而打断：收 2 刻。不置 searched、不记搜查。
    /// </summary>
    /// <returns>是否结算成功。</returns>
    public bool SpendInterrupt() => AddHalfKe(InterruptCostKe * 2);

    /// <summary>
    /// 睡觉 / 休息：收 2 刻。
    /// </summary>
    /// <returns>是否结算成功。</returns>
    public bool SpendSleep() => AddHalfKe(SleepCostKe * 2);

    /// <summary>
    /// 给药救一个人：半刻（0.5 刻），累加到半刻不四舍五入（rule_time_clock.md §3）。
    /// </summary>
    /// <returns>是否结算成功。</returns>
    public bool SpendDose() => AddHalfKe(DoseCostHalf);

    /// <summary>
    /// 被连续发现第三次而遭盘问：收 4 刻。这是打断的替代，不是叠加（rule_time_clock.md §3）。
    /// </summary>
    /// <returns>是否结算成功。</returns>
    public bool SpendInterrogate() => AddHalfKe(InterrogateCostKe * 2);

    /// <summary>
    /// 直接记半刻（给药 1 半刻、盘问 8 半刻这类不走整刻的动作）。
    /// 负数视为非法，不记账并返回 false。
    /// </summary>
    /// <param name="half">要记的半刻数。</param>
    /// <returns>是否记账成功。</returns>
    public bool AddHalfKe(int half)
    {
        if (half < 0) return false;

        var cap = KeBudgetHalf + OverflowCapHalf;
        var next = _keHalf + half;
        _keHalf = next > cap ? cap : next;

        RefreshNudges();
        return true;
    }

    /// <summary>
    /// 换空间：新空间从 0 刻起算。未知 hub 忽略并返回 false。
    /// </summary>
    /// <param name="hub">要进入的空间 id。</param>
    /// <returns>是否换区成功。</returns>
    public bool ResetForHub(string? hub)
    {
        var budget = ClockTable.ForHub(hub);
        if (budget == null) return false;

        _hub = hub!;
        _budgetKe = budget.Value;
        _keHalf = 0;
        _nudges = 0;
        _nudged = 0;
        return true;
    }

    /// <summary>
    /// 导出账本快照，供存档写入 <c>hubs.*</c>。
    /// </summary>
    /// <returns>含已用半刻与催促次数的快照。</returns>
    public ClockState ToState() => new ClockState { KeHalf = _keHalf, Nudges = _nudges };

    /// <summary>
    /// 从存档快照恢复账本。半刻会被夹到 0 – 2B+16，催促次数夹到 0 – 5。
    /// </summary>
    /// <param name="hub">该账本属于哪个空间。</param>
    /// <param name="state">存档里的 <c>hubs.*</c> 账本部分。</param>
    /// <returns>恢复好的账本。</returns>
    public static Clock FromState(string hub, ClockState? state)
    {
        var clock = new Clock(hub);
        if (state == null) return clock;

        var cap = clock.KeBudgetHalf + OverflowCapHalf;
        clock._keHalf = Math.Clamp(state.KeHalf, 0, cap);
        clock._nudges = Math.Clamp(state.Nudges, 0, MaxNudges);
        clock.RefreshNudges();

        // 读档时账上的催促都已经是"玩家看过并承担过代价"的，
        // 不能因为重新进入就把旧催促再播一遍。
        clock._nudged = clock._nudges;
        return clock;
    }

    /// <summary>
    /// 记够半刻之后重算催促次数。越线后每多欠 <see cref="NudgeStepHalf"/> 半刻记一次，
    /// 最多 <see cref="MaxNudges"/> 次；欠再多也只记到上限。
    /// </summary>
    private void RefreshNudges()
    {
        var derived = DerivedNudges(_keHalf, KeBudgetHalf);
        if (derived > _nudges) _nudges = derived;
    }

    /// <summary>
    /// 由半刻数推出催促次数：没越线是 0，越线后每 <see cref="NudgeStepHalf"/> 半刻一次。
    /// </summary>
    /// <param name="keHalf">已用半刻。</param>
    /// <param name="budgetHalf">预算半刻（2B）。</param>
    /// <returns>催促次数，不超过 <see cref="MaxNudges"/>。</returns>
    private static int DerivedNudges(int keHalf, int budgetHalf)
    {
        if (keHalf < budgetHalf) return 0;

        var over = (keHalf - budgetHalf) / NudgeStepHalf + 1;
        return Math.Min(over, MaxNudges);
    }

    public override string ToString() => $"{_hub}: {KeUsedKe}/{_budgetKe} 刻（{_keHalf} 半刻，催 {_nudges} 次）";
}
