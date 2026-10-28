using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 催促与探索率（票号 S1-01）。规则源：rule_time_clock.md §4 与 D-015。
///
/// 催促节拍：用光预算（2B 半刻）时第 1 次，之后每 NUDGE_STEP=8 半刻再一次。
/// 账上"触发"与"播报"分开：结算只加计数，Game 播完事件再 MarkNudged 消费。
/// </summary>
public class ClockNudgeTests
{
    /// <summary>把账打到指定半刻（每搜一次 2 半刻 = 1 刻）。</summary>
    private static Clock SpentToHalf(string hub, int half)
    {
        var c = new Clock(hub);
        var searches = half / 2;
        for (var i = 0; i < searches; i++) c.SpendSearch(1);
        return c;
    }

    [Fact]
    public void 催促位置按预算加步长算()
    {
        var hub1 = new Clock("hub1");   // B=8，2B=16
        Assert.Equal(16, hub1.NudgeAtHalf(1));
        Assert.Equal(24, hub1.NudgeAtHalf(2));
        Assert.Equal(32, hub1.NudgeAtHalf(3));

        var hub3 = new Clock("hub3");   // B=16，2B=32
        Assert.Equal(32, hub3.NudgeAtHalf(1));
        Assert.Equal(40, hub3.NudgeAtHalf(2));
        Assert.Equal(48, hub3.NudgeAtHalf(3));
    }

    [Fact]
    public void 催促位置越界返回负一()
    {
        var c = new Clock("hub1");

        Assert.Equal(-1, c.NudgeAtHalf(0));
        Assert.Equal(-1, c.NudgeAtHalf(-3));
        Assert.Equal(-1, c.NudgeAtHalf(Clock.MaxNudges + 1));
        Assert.Equal(16 + 8 * 4, c.NudgeAtHalf(Clock.MaxNudges));
    }

    [Fact]
    public void 用光预算的那一刻才第一次催()
    {
        var c = SpentToHalf("hub1", 14);
        Assert.Equal(0, c.Nudges);
        Assert.Equal(0, c.PendingNudges);

        c.SpendSearch(1);               // 16 半刻 = 刚好用光
        Assert.Equal(1, c.Nudges);
        Assert.Equal(1, c.PendingNudges);
    }

    [Fact]
    public void 消费一次之后不再重复催()
    {
        var c = SpentToHalf("hub1", 16);
        Assert.Equal(1, c.PendingNudges);

        Assert.True(c.MarkNudged());
        Assert.Equal(0, c.PendingNudges);
        Assert.Equal(1, c.Nudges);

        // 没有新越线，再消费是空操作
        Assert.False(c.MarkNudged());
        Assert.Equal(0, c.PendingNudges);
    }

    [Fact]
    public void 再欠四刻催第二次()
    {
        var c = SpentToHalf("hub1", 16);
        c.MarkNudged();

        for (var i = 0; i < 3; i++) c.SpendSearch(1);   // 22 半刻，还没到 24
        Assert.Equal(0, c.PendingNudges);

        c.SpendSearch(1);                                // 24 半刻 = 第 2 次
        Assert.Equal(2, c.Nudges);
        Assert.Equal(1, c.PendingNudges);
        Assert.True(c.MarkNudged());
        Assert.Equal(0, c.PendingNudges);
    }

    [Fact]
    public void 超支再多也不超过上限()
    {
        var c = new Clock("hub1");
        c.AddHalfKe(1000);              // 封顶 2B+16 = 32

        Assert.Equal(32, c.KeHalf);
        Assert.Equal(3, c.Nudges);      // 1 + 16/8
        Assert.True(c.Nudges <= Clock.MaxNudges);

        for (var i = 0; i < Clock.MaxNudges; i++) c.MarkNudged();
        Assert.Equal(0, c.PendingNudges);
    }

    [Fact]
    public void 读档不会把旧催促当成待播()
    {
        var back = Clock.FromState("hub3", new ClockState { KeHalf = 32, Nudges = 1 });

        Assert.Equal(1, back.Nudges);
        Assert.Equal(0, back.PendingNudges);
        Assert.False(back.MarkNudged());
    }

    [Fact]
    public void 探索率从零到一()
    {
        var c = new Clock("hub1");      // B=8
        Assert.Equal(0, c.Coverage, 6);

        c.AddHalfKe(8);                 // 4 刻
        Assert.Equal(0.5, c.Coverage, 6);

        c.AddHalfKe(8);                 // 8 刻，正好用光
        Assert.Equal(1, c.Coverage, 6);

        c.AddHalfKe(100);               // 超支不涨过 1
        Assert.Equal(1, c.Coverage, 6);
    }

    [Fact]
    public void 换空间后催促与探索率归零()
    {
        var c = SpentToHalf("hub1", 16);
        Assert.True(c.PendingNudges > 0);

        Assert.True(c.ResetForHub("hub3"));

        Assert.Equal(0, c.Nudges);
        Assert.Equal(0, c.PendingNudges);
        Assert.Equal(0, c.Coverage, 6);
        Assert.Equal(32, c.NudgeAtHalf(1));
    }
}
