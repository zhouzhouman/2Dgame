using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 序章就三十天。日历只做一件事：往前走，封顶 30，不许倒退。
/// </summary>
public class CalendarTests
{
    [Fact]
    public void 开头是第一天()
    {
        var c = new Calendar();

        Assert.Equal(1, c.Day);
        Assert.False(c.IsLastDay);
    }

    [Fact]
    public void 往前一天()
    {
        var c = new Calendar();

        Assert.True(c.Advance());
        Assert.Equal(2, c.Day);
    }

    [Fact]
    public void 可以一次走好几天()
    {
        var c = new Calendar();

        Assert.True(c.Advance(5));

        Assert.Equal(6, c.Day);
    }

    [Fact]
    public void 走到第三十天就停()
    {
        var c = new Calendar();

        Assert.True(c.Advance(100));

        Assert.Equal(30, c.Day);
        Assert.True(c.IsLastDay);
    }

    [Fact]
    public void 最后一天再走也走不动()
    {
        var c = new Calendar(30);

        Assert.False(c.Advance());
        Assert.Equal(30, c.Day);
    }

    [Fact]
    public void 负数不算数()
    {
        var c = new Calendar();

        Assert.False(c.Advance(-3));

        Assert.Equal(1, c.Day);
    }

    [Fact]
    public void 零天也不算前进()
    {
        var c = new Calendar();

        Assert.False(c.Advance(0));

        Assert.Equal(1, c.Day);
    }

    [Fact]
    public void 起始天越界会被夹住()
    {
        Assert.Equal(1, new Calendar(-5).Day);
        Assert.Equal(30, new Calendar(99).Day);
    }
}
