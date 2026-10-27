using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 台词表的两种查法必须分开：
///   站着的人按坐标查（ForHub），会走的人按名字查（ByName）。
/// 混了就会出现"空地上冒出对话"或者"人在眼前却不出声"。
/// </summary>
public class NpcTalkTests
{
    private static NpcTalkTable Load()
        => NpcTalkTable.FromCsv(File.ReadAllText(Path.Combine("npcs.csv")));

    [Fact]
    public void 一共三十位()
    {
        Assert.Equal(30, Load().All.Count);
    }

    [Fact]
    public void 白幡村站着的六位()
    {
        var t = Load();
        var here = t.ForHub("hub3").ToList();

        Assert.Equal(6, here.Count);
        Assert.DoesNotContain(here, n => n.Name == "衙役甲");
        Assert.DoesNotContain(here, n => n.Name == "衙役乙");
    }

    [Fact]
    public void 巡逻的衙役按名字找得到()
    {
        var t = Load();

        Assert.NotNull(t.ByName("hub3", "衙役甲"));
        Assert.NotNull(t.ByName("hub3", "衙役乙"));
        Assert.Contains("封村", t.ByName("hub3", "衙役甲")!.First);
    }

    [Fact]
    public void 每一行都有初见台词()
    {
        var t = Load();
        Assert.All(t.All, n => Assert.False(string.IsNullOrWhiteSpace(n.First)));
    }

    [Fact]
    public void 每一关都有能说话的人()
    {
        var t = Load();

        foreach (var hub in new[] { "hub2", "hub3", "hub4", "hub5", "hub6", "hub7" })
            Assert.True(t.ForHub(hub).Any(), $"{hub} 一个人都不能说话");
    }
}
