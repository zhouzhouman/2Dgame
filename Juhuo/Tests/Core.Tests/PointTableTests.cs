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

    [Fact]
    public void 搜查代价是整数一()
    {
        // D-018：cost 由 double 改 int，数据表里现在全是 "1"。
        Assert.All(Load().All, p => Assert.Equal(1, p.Cost));
    }

    [Fact]
    public void 代价非法时回落为一而不是零()
    {
        // 列序与 points.csv 一致，共 14 列：hub,id,mark,x,y,layer,name,requires,
        // text_once,text_repeat,loot,set_flag,watch_npc,cost
        var csv =
            "hub,id,mark,x,y,layer,name,requires,text_once,text_repeat,loot,set_flag,watch_npc,cost\n" +
            "\"hub1\",\"a1\",\"1\",\"1\",\"1\",\"surface\",\"甲\",\"\",\"t\",\"t\",\"\",\"\",\"\",\"\"\n" +
            "\"hub1\",\"a2\",\"2\",\"2\",\"2\",\"surface\",\"乙\",\"\",\"t\",\"t\",\"\",\"\",\"\",\"2\"\n" +
            "\"hub1\",\"a3\",\"3\",\"3\",\"3\",\"surface\",\"丙\",\"\",\"t\",\"t\",\"\",\"\",\"\",\"1.5\"\n" +
            "\"hub1\",\"a4\",\"4\",\"4\",\"4\",\"surface\",\"丁\",\"\",\"t\",\"t\",\"\",\"\",\"\",\"abc\"\n" +
            "\"hub1\",\"a5\",\"5\",\"5\",\"5\",\"surface\",\"戊\",\"\",\"t\",\"t\",\"\",\"\",\"\",\"9\"\n";

        var table = PointTable.FromCsv(csv);

        // 空 → 1（rule_time_clock.md §边界）
        Assert.Equal(1, table.All.Single(p => p.Id == "a1").Cost);
        // 整数照读
        Assert.Equal(2, table.All.Single(p => p.Id == "a2").Cost);
        // 非整数向下取整
        Assert.Equal(1, table.All.Single(p => p.Id == "a3").Cost);
        // 根本不是数 → 回落 1，不能是 0（否则搜查白送刻数）
        Assert.Equal(1, table.All.Single(p => p.Id == "a4").Cost);
        // 越界 → clamp 到 0–3
        Assert.Equal(3, table.All.Single(p => p.Id == "a5").Cost);
    }
}
