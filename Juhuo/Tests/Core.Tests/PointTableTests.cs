using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Tests;

public class CsvTests
{
    [Fact]
    public void 解析带引号与逗号的字段()
    {
        var text = "a,b,c\n\"x,1\",\"他说\"\"好\"\"\",3\n";
        var rows = Csv.Parse(text);

        Assert.Equal(2, rows.Count);
        Assert.Equal("x,1", rows[1][0]);
        Assert.Equal("他说\"好\"", rows[1][1]);
        Assert.Equal("3", rows[1][2]);
    }

    [Fact]
    public void 忽略BOM与CRLF()
    {
        var rows = Csv.Parse("\uFEFFa,b\r\n1,2\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[0][0]);
        Assert.Equal("2", rows[1][1]);
    }
}

public class PointTableTests
{
    private static PointTable Load()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "points.csv");
        Assert.True(File.Exists(path), $"找不到测试数据：{path}");
        return PointTable.FromCsv(File.ReadAllText(path));
    }

    [Fact]
    public void 全量数据为120条()
    {
        Assert.Equal(120, Load().All.Count);
    }

    [Fact]
    public void 白幡村有29条()
    {
        Assert.Equal(29, Load().CountForHub("hub3"));
    }

    [Fact]
    public void 按坐标能取到村口白幡()
    {
        var p = Load().At("hub3", 13, 19);
        Assert.NotNull(p);
        Assert.Equal("村口白幡", p!.Name);
        Assert.Equal("surface", p.Layer);
        Assert.StartsWith("村口的竹竿上挂着十一根白幡", p.TextOnce);
    }

    [Fact]
    public void 知识门只有两个()
    {
        var all = Load().All;
        Assert.Equal(2, all.Count(p => p.IsKnowledge));
    }

    [Fact]
    public void 每个点的重复文本都不为空()
    {
        var bad = Load().All.Where(p => string.IsNullOrWhiteSpace(p.TextRepeat)).Select(p => p.Id).ToArray();
        Assert.Empty(bad);
    }
}
