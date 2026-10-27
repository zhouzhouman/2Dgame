using Juhuo.Application;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 进度文件是玩家唯一会"丢"的东西，读写必须稳。
/// 这里的每一条都对应一个真实会发生的场景：首次启动、看过引导、文件被删、文件被手改坏。
/// </summary>
public class ProgressTests
{
    [Fact]
    public void 空文件等于全新开始()
    {
        var p = Progress.Parse("");

        Assert.False(p.TutorialDone);
        Assert.Equal("hub1", p.LastHub);
        Assert.Equal(0, p.Launches);
    }

    [Fact]
    public void 读出看过引导()
    {
        var p = Progress.Parse("tutorial_done=1\nlast_hub=hub3\nlaunches=7\n");

        Assert.True(p.TutorialDone);
        Assert.Equal("hub3", p.LastHub);
        Assert.Equal(7, p.Launches);
    }

    [Fact]
    public void 写出去再读回来不变()
    {
        var a = new Progress { TutorialDone = true, LastHub = "hub5", Launches = 12 };
        var b = Progress.Parse(a.Serialize());

        Assert.Equal(a.TutorialDone, b.TutorialDone);
        Assert.Equal(a.LastHub, b.LastHub);
        Assert.Equal(a.Launches, b.Launches);
    }

    [Fact]
    public void 坏行直接跳过不炸()
    {
        var p = Progress.Parse("这不是键值对\n=错的\nlaunches=abc\ntutorial_done=1\n");

        Assert.True(p.TutorialDone);
        Assert.Equal(0, p.Launches);      // 解析失败保持默认
        Assert.Equal("hub1", p.LastHub);
    }
}
