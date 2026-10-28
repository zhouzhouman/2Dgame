using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 刻是序章唯一的软时钟。这里钉死三件事：预算表、消耗表、超支不拦人。
/// 数值源：Docs/Rules/rule_time_clock.md §2 / §3 / §4。
/// </summary>
public class ClockTests
{
    [Fact]
    public void 七个空间的预算都对()
    {
        Assert.Equal(8, ClockTable.ForHub("hub1"));
        Assert.Equal(10, ClockTable.ForHub("hub2"));
        Assert.Equal(16, ClockTable.ForHub("hub3"));
        Assert.Equal(20, ClockTable.ForHub("hub4"));
        Assert.Equal(12, ClockTable.ForHub("hub5"));
        Assert.Equal(14, ClockTable.ForHub("hub6"));
        Assert.Equal(8, ClockTable.ForHub("hub7"));
    }

    [Fact]
    public void 七个空间加起来八十八刻()
    {
        Assert.Equal(88, ClockTable.TotalBudgetKe);

        var sum = 0;
        foreach (var hub in ClockTable.Hubs)
            sum += ClockTable.ForHub(hub)!.Value;

        Assert.Equal(88, sum);
    }

    [Fact]
    public void 查不到的空间给不出预算()
    {
        Assert.Null(ClockTable.ForHub("hub8"));
        Assert.Null(ClockTable.ForHub("reg_fengyuan"));
        Assert.Null(ClockTable.ForHub(""));
        Assert.Null(ClockTable.ForHub(null));
    }

    [Fact]
    public void 搜查一次扣一刻()
    {
        var c = new Clock("hub3");

        Assert.True(c.SpendSearch());

        Assert.Equal(2, c.KeHalf);
        Assert.Equal(1, c.KeUsedKe);
        Assert.Equal(15, c.KeLeftKe);
    }

    [Fact]
    public void 搜查两刻的点扣两刻()
    {
        var c = new Clock("hub3");

        Assert.True(c.SpendSearch(2));

        Assert.Equal(4, c.KeHalf);
        Assert.Equal(2, c.KeUsedKe);
    }

    [Fact]
    public void 首次对话不扣刻()
    {
        var c = new Clock("hub2");

        Assert.True(c.SpendTalk(true));

        Assert.Equal(0, c.KeHalf);
        Assert.Equal(10, c.KeLeftKe);
    }

    [Fact]
    public void 再谈一次扣一刻()
    {
        var c = new Clock("hub2");

        c.SpendTalk(true);
        Assert.True(c.SpendTalk(false));

        Assert.Equal(2, c.KeHalf);
        Assert.Equal(1, c.KeUsedKe);
        Assert.Equal(9, c.KeLeftKe);
    }

    [Fact]
    public void 赶路扣两刻()
    {
        var c = new Clock("hub2");

        Assert.True(c.SpendTravel());

        Assert.Equal(4, c.KeHalf);
        Assert.Equal(2, c.KeUsedKe);
    }

    [Fact]
    public void 被打断扣两刻()
    {
        var c = new Clock("hub3");

        Assert.True(c.SpendInterrupt());

        Assert.Equal(4, c.KeHalf);
        Assert.Equal(2, c.KeUsedKe);
    }

    [Fact]
    public void 睡觉扣两刻()
    {
        var c = new Clock("hub4");

        Assert.True(c.SpendSleep());

        Assert.Equal(4, c.KeHalf);
        Assert.Equal(2, c.KeUsedKe);
    }

    [Fact]
    public void 半刻只显示零刻()
    {
        var c = new Clock("hub3");

        c.AddHalfKe(1);

        Assert.Equal(1, c.KeHalf);
        Assert.Equal(0, c.KeUsedKe);
    }

    [Fact]
    public void 给药只扣半刻()
    {
        var c = new Clock("hub3");

        Assert.True(c.SpendDose());
        Assert.True(c.SpendDose());

        Assert.Equal(2, c.KeHalf);
        Assert.Equal(1, c.KeUsedKe);
    }

    [Fact]
    public void 盘问扣四刻()
    {
        var c = new Clock("hub3");

        Assert.True(c.SpendInterrogate());

        Assert.Equal(8, c.KeHalf);
        Assert.Equal(4, c.KeUsedKe);
    }

    [Fact]
    public void 超预算照样能搜()
    {
        var c = new Clock("hub1");

        // hub1 预算 8 刻，搜 12 次早就超了。
        for (var i = 0; i < 12; i++)
            Assert.True(c.SpendSearch());

        Assert.True(c.IsOverBudget);
        Assert.Equal(0, c.KeLeftKe);
        Assert.Equal(24, c.KeHalf);
    }

    [Fact]
    public void 越线之后每欠四半刻催一次()
    {
        var c = new Clock("hub3");

        // 2B = 32 半刻。搜 16 次刚好到线，催 1 次。
        for (var i = 0; i < 16; i++) c.SpendSearch();
        Assert.Equal(32, c.KeHalf);
        Assert.Equal(1, c.Nudges);

        // 再欠 4 半刻，催 2 次。
        c.AddHalfKe(4);
        Assert.Equal(2, c.Nudges);
    }

    [Fact]
    public void 催促最多五次()
    {
        var c = new Clock("hub3");

        for (var i = 0; i < 40; i++) c.AddHalfKe(4);

        Assert.Equal(5, c.Nudges);
    }

    [Fact]
    public void 半刻封顶在两倍预算加十六()
    {
        var c = new Clock("hub3");

        for (var i = 0; i < 60; i++) c.AddHalfKe(4);

        Assert.Equal(32 + 16, c.KeHalf);
    }

    [Fact]
    public void 换区重置()
    {
        var c = new Clock("hub1");
        c.SpendSearch();
        c.SpendSearch();

        Assert.True(c.ResetForHub("hub3"));

        Assert.Equal(0, c.KeHalf);
        Assert.Equal(0, c.Nudges);
        Assert.Equal(32, c.KeBudgetHalf);
        Assert.Equal(16, c.KeLeftKe);
    }

    [Fact]
    public void 未知空间不换区()
    {
        var c = new Clock("hub1");
        c.SpendSearch();

        Assert.False(c.ResetForHub("hub99"));
        Assert.False(c.ResetForHub(null));

        Assert.Equal(2, c.KeHalf);
    }

    [Fact]
    public void 非法参数不当真()
    {
        var c = new Clock("hub3");

        Assert.False(c.SpendSearch(-1));
        Assert.False(c.AddHalfKe(-5));

        Assert.Equal(0, c.KeHalf);
    }

    [Fact]
    public void 存档往返刻账不变()
    {
        var c = new Clock("hub3");
        c.SpendSearch();
        c.SpendSearch();
        c.SpendTalk(false);

        var back = Clock.FromState("hub3", c.ToState());

        Assert.Equal(c.KeHalf, back.KeHalf);
        Assert.Equal(c.Nudges, back.Nudges);
    }
}
