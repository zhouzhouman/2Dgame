using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 可达性接线回归（票号 S1-DIALOG-b）。
///
/// 这张票把 <c>--audit</c> 从 Main 里那份只看局部的老判断，换成了 Core 的
/// <see cref="ReachAudit"/>。接线只有两种错法，两种都要在这儿被挡住：
///   1. 坐标口径错位——<c>SearchPoint</c> 是 1-based，<c>MapGrid.Ground</c> 收 0-based，
///      中间只该减一次 1；多减或少减一次，阻挡物就整体压到隔壁格上；
///   2. 阻挡谓词写错——门被物件堵死的房间会永远亮绿灯。
///
/// 测试里拿不到 <c>PropKinds</c>（它在 Src/Game/Art，Core.Tests 引用不到 Game），
/// 所以第 2 条不搬真实规则，改用极端谓词当探针：口径对时它只切掉应有的房间，
/// 口径错时结果会明显不一样。
/// </summary>
public class ReachWiringTests
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

    /// <summary>
    /// 口径证明之一：点位的 1-based 坐标减一次 1 之后，120 个点没有一个落在墙里。
    ///
    /// 这条断言的读法：<c>Ground(x-1, y-1)</c> 是"把 1-based 点位换算成 0-based 地形"的
    /// 唯一正确式子。若有人图省事写成 <c>Ground(x, y)</c>（把点位当 0-based 用），
    /// 整张表会往右下错一格——实测有 31 个点落到 <c>#</c> 上。所以"正确式子 0 个、
    /// 误读 31 个"同时成立，才说明换算方向是对的，不是碰巧。
    /// </summary>
    [Fact]
    public void 点位的基坐标换算只减一次一()
    {
        var table = Points();
        var points = 0;
        var onWall = new List<string>();

        foreach (var (hub, _) in Spawns)
        {
            var map = Map(hub);

            foreach (var p in table.ForHub(hub))
            {
                points++;

                // 正确：1-based → 0-based
                if (map.Ground(p.X - 1, p.Y - 1) != '#') continue;
                onWall.Add($"{hub} {p.Id} ({p.X},{p.Y})");
            }
        }

        Assert.Equal(120, points);
        Assert.True(onWall.Count == 0, "有点落在墙里，说明坐标换算反了：" + string.Join("；", onWall));

        // 反证：同一批点按 0-based 误读，会有一大片直接落进墙里。
        var misread = 0;
        foreach (var (hub, _) in Spawns)
        {
            var map = Map(hub);
            misread += table.ForHub(hub).Count(p => map.Ground(p.X, p.Y) == '#');
        }

        Assert.True(misread > 20, $"0-based 误读只落墙 {misread} 个，这条反证失去了意义，请复核数据");
    }

    /// <summary>
    /// 回归守卫：hub4 官署东屋必须整间够得着（真实地图 + 真实点位 + 极端阻挡谓词）。
    ///
    /// 这条测试守的是一个**真实修掉的缺陷**：
    ///   2026-10-28 之前，堂前鼓 h4_07 站在 (24,8)——那是门格 (24,9) 进去后的**唯一落脚格**。
    ///   鼓的原型是 Rack，`PropKinds.Blocks` 判它挡路，于是整间东屋被一个道具封死，
    ///   屋里的 h4_03（官署廊下）与 h4_08（县丞的椅子）永远搜不到。
    ///   旧的 Game 侧自检不看物件，一直报"120 件、够不着 0"，是**假绿**。
    ///   当天把鼓挪到 (25,8)（照片墙上、不挡路），本测试随之从"断言 26 件"改成"断言 28 件"。
    ///
    /// 为什么用极端谓词：真实规则在 `PropKinds`（Game 层），Core 测试引用不到。
    /// "全部 28 个点都当障碍"比真实规则更狠——它都能全通，说明点位与格子是一格对一格，
    /// 没有整体偏移；若谁把阻挡点错读成 0-based（整体往左上偏一格），(24,8) 会空出来、
    /// 门后那格反而被占，通的结果会立刻变样，这条就会红。
    /// </summary>
    [Fact]
    public void hub4官署东屋整间都够得着()
    {
        var points = Points().ForHub("hub4").ToList();
        Assert.Equal(28, points.Count);

        var spawn = Spawns["hub4"];
        var report = ReachAudit.Run(Map("hub4"), points, spawn.X, spawn.Y, _ => true);

        Assert.Equal(28, report.Total);
        Assert.Empty(report.Unreachable);
        // 这两件曾经够不着，写死在这里，防止"鼓又站回门口"这类回退
        Assert.DoesNotContain("h4_03", report.Unreachable.Select(p => p.Id));
        Assert.DoesNotContain("h4_08", report.Unreachable.Select(p => p.Id));
    }

    /// <summary>
    /// 阻挡谓词的回归测试：三乘三的房间，唯一一道门，门里第一格放一个"阻挡点"。
    ///
    /// 这是"未来有人把谓词写错"的保险丝。判定的关键在"阻挡点压的必须是门后那一格"：
    /// 只在 (5,6) 被占时屋子才封死；偏一格（比如误读成 (4,6)）门后就是空的，屋里照样够得着，
    /// 断言立刻红。把那个点挪到屋外，房间必须恢复可达——否则说明别的什么东西在误伤。
    /// </summary>
    [Fact]
    public void 门后一格被占时房间封死搬开即恢复()
    {
        // 列 1..9，行 1..9。屋内是行 4-6、列 4-6；门在 (5,7) 的 '+'；
        // 门后第一格 (5,6) 是唯一入口，其余三面全是墙。
        var map = MapGrid.FromText(
            "#########\n" +
            "#.......#\n" +
            "#.#####.#\n" +
            "#.#...#.#\n" +
            "#.#.,.#.#\n" +
            "#.#...#.#\n" +
            "#.##+##.#\n" +
            "#.......#\n" +
            "#########\n");

        var inside = new SearchPoint { Hub = "t", Id = "t_inside", X = 5, Y = 5, Name = "屋里" };
        var drum = new SearchPoint { Hub = "t", Id = "t_drum", X = 5, Y = 6, Name = "门后的鼓" };
        var points = new List<SearchPoint> { inside, drum };

        // 起点在屋外南边的走廊上 (2,8)
        Func<SearchPoint, bool> drumBlocks = p => p.Id == "t_drum";

        var sealedUp = ReachAudit.Run(map, points, 2, 8, drumBlocks);
        Assert.Equal(2, sealedUp.Total);
        Assert.Equal("t_inside", Assert.Single(sealedUp.Unreachable).Id);

        // 把鼓挪到屋外的东侧走廊 (8,4)：门后那一格空出来，屋里必须恢复可达。
        var moved = new List<SearchPoint>
        {
            inside,
            new() { Hub = "t", Id = "t_drum", X = 8, Y = 4, Name = "门后的鼓" },
        };

        var reopened = ReachAudit.Run(map, moved, 2, 8, drumBlocks);
        Assert.Equal(2, reopened.Total);
        Assert.True(reopened.AllReachable, "鼓挪开后屋里仍够不着，说明阻挡点没落在它该落的格子上");
    }
}
