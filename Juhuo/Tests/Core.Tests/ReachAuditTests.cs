using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 可达性自检（票号 S1-13）。这就是关卡门禁：
/// 120 件可搜点，每一件都得站得到旁边，够不着 0 件。
///
/// 判定规则与 <c>Src/Game/World.cs</c> 的 <c>CanReach</c> / <c>CanTouch</c> 一致
/// （八邻格、不穿墙、斜角不穿墙角），另外收紧一条：邻格必须从起点四向走得到。
/// </summary>
public class ReachAuditTests
{
    /// <summary>七个空间各自的起点（与 Src/Game/SceneConfig.cs 一致，1-based）。</summary>
    private static readonly Dictionary<string, (int X, int Y)> Spawns = new()
    {
        ["hub1"] = (8, 9),
        ["hub2"] = (2, 8),
        ["hub3"] = (4, 20),
        ["hub4"] = (12, 22),
        ["hub5"] = (12, 15),
        ["hub6"] = (15, 11),
        ["hub7"] = (3, 9),
    };

    private static PointTable Points()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "points.csv");
        Assert.True(File.Exists(path), $"找不到测试数据：{path}");
        return PointTable.FromCsv(File.ReadAllText(path));
    }

    /// <summary>从测试输出目录往上找工程里的 Content/maps。</summary>
    private static string MapsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var nested = Path.Combine(dir.FullName, "Juhuo", "Content", "maps");
            if (Directory.Exists(nested)) return nested;

            var local = Path.Combine(dir.FullName, "Content", "maps");
            if (Directory.Exists(local)) return local;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 Content/maps");
    }

    private static MapGrid Map(string hub) =>
        MapGrid.Load(Path.Combine(MapsDir(), hub + ".txt"));

    [Fact]
    public void 七个空间的120件全都够得着()
    {
        var table = Points();
        var total = 0;
        var bad = new List<string>();

        foreach (var (hub, spawn) in Spawns)
        {
            var points = table.ForHub(hub).ToList();
            Assert.NotEmpty(points);

            var report = ReachAudit.Run(Map(hub), points, spawn.X, spawn.Y);
            total += report.Total;

            foreach (var p in report.Unreachable)
                bad.Add($"{hub} {p.Id} {p.Name} ({p.X},{p.Y})");
        }

        Assert.Equal(120, total);
        Assert.True(bad.Count == 0, "够不着的点：" + string.Join("；", bad));
    }

    [Fact]
    public void 白幡村二十九件都够得着()
    {
        var points = Points().ForHub("hub3").ToList();
        Assert.Equal(29, points.Count);

        var report = ReachAudit.Run(Map("hub3"), points, Spawns["hub3"].X, Spawns["hub3"].Y);

        Assert.Equal(29, report.Total);
        Assert.Equal(29, report.Reachable);
        Assert.True(report.AllReachable);
    }

    [Fact]
    public void 被墙围死的点会被报出来()
    {
        //   #########
        //   #.......#
        //   #.#####.#
        //   #.#,,,#.#   ← 中间这间房四面全墙，走不进去
        //   #.#####.#
        //   #.......#
        //   #########
        var map = MapGrid.FromText(
            "#########\n" +
            "#.......#\n" +
            "#.#####.#\n" +
            "#.#,,,#.#\n" +
            "#.#####.#\n" +
            "#.......#\n" +
            "#########\n");

        var points = new List<SearchPoint>
        {
            new() { Hub = "t", Id = "t_open", X = 2, Y = 2, Name = "路上" },      // 走得过去
            new() { Hub = "t", Id = "t_cage", X = 5, Y = 4, Name = "封死的屋里" }, // 四邻全墙
        };

        var report = ReachAudit.Run(map, points, 2, 2);

        Assert.Equal(2, report.Total);
        Assert.Equal(1, report.Reachable);
        Assert.Equal("t_cage", Assert.Single(report.Unreachable).Id);
    }

    [Fact]
    public void 斜角不许穿墙角()
    {
        //   #####
        //   #,###     ← 点 (2,2)，八个邻格里只有斜角 (3,3) 能站
        //   ##..#
        //   #...#
        //   #####
        var map = MapGrid.FromText(
            "#####\n" +
            "#,###\n" +
            "##..#\n" +
            "#...#\n" +
            "#####\n");

        var points = new List<SearchPoint>
        {
            new() { Hub = "t", Id = "t_diag", X = 2, Y = 2, Name = "墙角" },
        };

        // 站在 (3,3) 是对角相邻，但 (3,2) 与 (2,3) 都是墙 → 不许穿墙角
        var report = ReachAudit.Run(map, points, 2, 4);

        Assert.Equal(0, report.Reachable);
        Assert.Single(report.Unreachable);
    }

    [Fact]
    public void 起点在墙里时不给假绿灯()
    {
        var map = MapGrid.FromText("#####\n#...#\n#####\n");
        var points = new List<SearchPoint>
        {
            new() { Hub = "t", Id = "t_a", X = 3, Y = 2, Name = "屋里" },
        };

        var report = ReachAudit.Run(map, points, 1, 1);   // (1,1) 是墙

        Assert.Equal(0, report.Reachable);
        Assert.Single(report.Unreachable);
    }
}
