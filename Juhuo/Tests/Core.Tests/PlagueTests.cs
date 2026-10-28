using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 白幡村死亡节拍（票号 S1-11）。公式：deaths = min(3, ke_used/4)。
/// 规则源：rule_time_clock.md §公式、§验收 10。
/// </summary>
public class PlagueTests
{
    [Fact]
    public void 没花刻时没人死()
    {
        Assert.Equal(0, Plague.DeathsByKe(0));
        Assert.Equal(0, Plague.DeathsByKe(3));
    }

    [Fact]
    public void 第四刻死第一个()
    {
        Assert.Equal(1, Plague.DeathsByKe(4));
        Assert.Equal(1, Plague.DeathsByKe(7));
    }

    [Fact]
    public void 第八刻死第二个()
    {
        Assert.Equal(2, Plague.DeathsByKe(8));
    }

    [Fact]
    public void 第十二刻三个全死光()
    {
        Assert.Equal(3, Plague.DeathsByKe(12));
    }

    [Fact]
    public void 死满三人后不再增加()
    {
        Assert.Equal(3, Plague.DeathsByKe(16));
        Assert.Equal(3, Plague.DeathsByKe(88));
    }

    [Fact]
    public void 负刻数当零处理()
    {
        Assert.Equal(0, Plague.DeathsByKe(-5));
        Assert.Equal(4, Plague.NextDeathAtKe(-5));
    }

    [Fact]
    public void 下一个死亡节拍是四的倍数()
    {
        Assert.Equal(4, Plague.NextDeathAtKe(0));
        Assert.Equal(4, Plague.NextDeathAtKe(3));
        Assert.Equal(8, Plague.NextDeathAtKe(4));
        Assert.Equal(12, Plague.NextDeathAtKe(8));
        Assert.Equal(12, Plague.NextDeathAtKe(11));
    }

    [Fact]
    public void 死满之后没有下一个()
    {
        Assert.Equal(-1, Plague.NextDeathAtKe(12));
        Assert.Equal(-1, Plague.NextDeathAtKe(16));
        Assert.Equal(-1, Plague.NextDeathAtKe(88));
    }

    [Fact]
    public void 只在四的倍数上算节拍()
    {
        Assert.False(Plague.IsDeathBeat(0));
        Assert.False(Plague.IsDeathBeat(5));
        Assert.True(Plague.IsDeathBeat(4));
        Assert.True(Plague.IsDeathBeat(8));
        Assert.True(Plague.IsDeathBeat(12));

        // 16 刻虽是 4 的倍数，但三个人已经死完了，不该再播"又倒下一个"
        Assert.False(Plague.IsDeathBeat(16));
    }
}
