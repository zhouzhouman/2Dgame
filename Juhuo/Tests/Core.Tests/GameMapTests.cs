using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 按 M 去任意空间、开局资金、转段报告"带走"（票号 S3-01c）。
///
/// 这一层盯的是**玩家看得见的那几句话与那几个数**：七行三态怎么写、走不动的行有没有灰着、
/// 走成了扣几刻换到哪、刻不够时那句原话、HUD 上的"钱 30"、报告页的"带走"，
/// 以及 <c>--map</c> 那四行。规则仍在 Core（<see cref="TravelBook"/> / <see cref="Clock"/>），
/// 这里只验证接线有没有把账接错。
///
/// 全部不走 Godot：<see cref="MapScreen"/> / <see cref="MapTravel"/> / <see cref="MapReport"/>
/// 只算字和账，画在 Main 里。
/// </summary>
public class GameMapTests
{
    // ————————————————————————— 夹具 —————————————————————————

    /// <summary>从测试输出目录往上找工程里的 Content/data（与其它 Game* 测试同一套找法）。</summary>
    private static string FindData(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var nested = Path.Combine(dir.FullName, "Juhuo", "Content", "data", fileName);
            if (File.Exists(nested)) return nested;

            var local = Path.Combine(dir.FullName, "Content", "data", fileName);
            if (File.Exists(local)) return local;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 Content/data/{fileName}");
    }

    /// <summary>真表：42 条路，1–6 刻。</summary>
    private static TravelBook RealTravel() => new(TravelTable.Load(FindData("travel.csv")));

    /// <summary>
    /// 内联小表：hub1 两条路（2 刻、3 刻），另外 hub3 → hub4 是 6 刻，
    /// hub1 → hub4 故意没有——用来验"没这条路"。
    /// </summary>
    private const string MiniTravel =
        "from,to,ke,note\n" +
        "hub1,hub2,2,出城\n" +
        "hub1,hub3,3,下堤\n" +
        "hub3,hub4,6,最贵的一条\n";

    private static TravelBook MiniBook() => new(TravelTable.FromCsv(MiniTravel));

    /// <summary>够用的最小物品表：钱叫"钱"、粮叫"粮"、药叫"药"，与报告页那张样张一致。</summary>
    private static readonly ItemCatalog Catalog = ItemCatalog.FromCsv(
        "id,name,kind,stack,note\n" +
        "coin,钱,token,1,\n" +
        "grain,粮,food,1,\n" +
        "medicine,药,tool,1,\n");

    // ————————————————————————— 一、七行三态 —————————————————————————

    [Fact]
    public void 地图七行_当前那一行写你在这儿()
    {
        var screen = new MapScreen(MiniBook(), "hub1", 1, 8, 8);

        var rows = screen.Rows();

        Assert.Equal(7, rows.Count);
        Assert.Equal("hub1", rows[0].Hub);
        Assert.Equal(MapRowState.Here, rows[0].State);
        Assert.Equal("· 城南小院　（你在这儿）", rows[0].Text);
        Assert.False(rows[0].Walkable);
    }

    [Fact]
    public void 地图七行_有路的那一行写要几刻()
    {
        var screen = new MapScreen(MiniBook(), "hub1", 1, 8, 8);

        var row = screen.Rows().First(r => r.Hub == "hub2");

        Assert.Equal(MapRowState.Ready, row.State);
        Assert.Equal("逃亡路 · 废驿　2 刻", row.Text);
        Assert.True(row.Walkable);
        Assert.False(row.Dim);
    }

    [Fact]
    public void 地图七行_刻不够的那一行写今天走不动了且灰着()
    {
        // 余 2 刻，去白幡村要 3 刻。
        var screen = new MapScreen(MiniBook(), "hub1", 1, 2, 8);

        var row = screen.Rows().First(r => r.Hub == "hub3");

        Assert.Equal(MapRowState.NoKe, row.State);
        Assert.Equal("白幡村　3 刻　（今天走不动了）", row.Text);
        Assert.False(row.Walkable);
        Assert.True(row.Dim);
    }

    [Fact]
    public void 地图七行_没这条路就说没路()
    {
        // 小表里没有 hub1 → hub4，那一行不该写成"0 刻"或"走不动"。
        var screen = new MapScreen(MiniBook(), "hub1", 1, 8, 8);

        var row = screen.Rows().First(r => r.Hub == "hub4");

        Assert.Equal(MapRowState.NoRoute, row.State);
        Assert.Equal("县城　没有路", row.Text);
        Assert.True(row.Dim);
    }

    [Fact]
    public void 顶上写第几天与余刻_底下写三个键()
    {
        var screen = new MapScreen(RealTravel(), "hub1", 3, 5, 8);

        Assert.Equal("去何处", screen.Title);
        Assert.Equal("第 3 天　余刻 5/8", screen.Head);
        Assert.Equal("↑↓ 选　·　Enter 走　·　M / Esc 收起", screen.Foot);
    }

    // ————————————————————————— 二、选中与走不动 —————————————————————————

    [Fact]
    public void 光标默认停在当前那一行_到头就停住()
    {
        var screen = new MapScreen(MiniBook(), "hub1", 1, 8, 8);
        Assert.Equal(0, screen.Selected);

        screen.Move(-1);
        Assert.Equal(0, screen.Selected);

        for (var i = 0; i < 12; i++) screen.Move(1);
        Assert.Equal(6, screen.Selected);
    }

    [Fact]
    public void 选中走不动的那一行_不扣刻也不换场景()
    {
        var book = MiniBook();
        var screen = new MapScreen(book, "hub1", 1, 2, 8);

        screen.Select(screen.IndexOf("hub3"));
        Assert.Equal(MapRowState.NoKe, screen.Current.State);

        var result = MapTravel.Go(book, screen.Here, screen.Current.Hub, 2);

        Assert.False(result.Ok);
        Assert.Null(result.Hub);
        Assert.Equal(2, result.KeLeft);          // 刻一分没动
        Assert.Equal(3, result.Cost);            // 但告诉你要走几刻
        Assert.Equal("今天走不动了——余刻 2，要走 3 刻。", result.Message);
    }

    [Fact]
    public void 选中走不动的那一行_选中时就把那句话显示出来()
    {
        var screen = new MapScreen(MiniBook(), "hub1", 1, 2, 8);

        screen.Select(screen.IndexOf("hub3"));

        Assert.Equal("白幡村　3 刻　（今天走不动了）", screen.Message);
    }

    [Fact]
    public void 刻不够的那句原话()
    {
        Assert.Equal("今天走不动了——余刻 2，要走 6 刻。", MapText.Stuck(2, 6));
    }

    // ————————————————————————— 三、走成了 —————————————————————————

    [Fact]
    public void 走成功_扣刻_换hub_留一句话()
    {
        var result = MapTravel.Go(MiniBook(), "hub1", "hub2", 8);

        Assert.True(result.Ok);
        Assert.Equal("hub2", result.Hub);
        Assert.Equal(2, result.Cost);
        Assert.Equal(6, result.KeLeft);
        Assert.Equal("从城南小院到逃亡路 · 废驿，走了 2 刻。", result.Message);
    }

    [Fact]
    public void 走原地_不走路也不扣刻()
    {
        var result = MapTravel.Go(MiniBook(), "hub1", "hub1", 8);

        Assert.False(result.Ok);
        Assert.Null(result.Hub);
        Assert.Equal(0, result.Cost);
        Assert.Equal(8, result.KeLeft);
        Assert.Equal(MapText.AlreadyHere, result.Message);
    }

    [Fact]
    public void 没这条路_说没路而不是刻不够()
    {
        var result = MapTravel.Go(MiniBook(), "hub1", "hub4", 8);

        Assert.False(result.Ok);
        Assert.Equal(8, result.KeLeft);
        Assert.Contains("没有路", result.Message);
        Assert.DoesNotContain("刻不够", result.Message);
    }

    [Fact]
    public void 真表里七行都走得通()
    {
        var screen = new MapScreen(RealTravel(), "hub1", 1, 8, 8);

        Assert.All(screen.Rows().Where(r => r.Hub != "hub1"), r =>
            Assert.True(r.State is MapRowState.Ready || r.State is MapRowState.NoKe,
                $"{r.Hub} 在真表里不该没路：{r.Text}"));
    }

    // ————————————————————————— 四、开局资金与 HUD —————————————————————————

    [Fact]
    public void 新档囊里三十文_占一格()
    {
        var bag = new Inventory();

        var filled = StartKit.Fill(bag, Catalog, out var error);

        Assert.Null(error);
        Assert.Equal(30, filled);
        Assert.Equal(1, bag.SlotsUsed);
        Assert.Equal(30, bag.Count(TradeBook.MoneyItem));
    }

    [Fact]
    public void HUD那一行写着钱数()
    {
        var bag = new Inventory();
        Assert.Equal("　钱 0", BagHud.MoneySuffix(bag));

        StartKit.Fill(bag, Catalog, out _);

        Assert.Equal("　钱 30", BagHud.MoneySuffix(bag));
        Assert.Equal("　囊 1/12　钱 30", BagHud.Suffix(bag) + BagHud.MoneySuffix(bag));
    }

    // ————————————————————————— 五、转段报告"带走" —————————————————————————

    [Fact]
    public void 报告带走一行_有东西就逐样列出来()
    {
        var bag = new Inventory();
        Assert.True(bag.TryAdd("grain", 3, Catalog, out _));
        Assert.True(bag.TryAdd("medicine", 1, Catalog, out _));
        Assert.True(bag.TryAdd(TradeBook.MoneyItem, 17, Catalog, out _));

        Assert.Equal("带走 粮 ×3、药 ×1、钱 17", TransitionText.CarryLine(bag, Catalog));
    }

    [Fact]
    public void 报告带走一行_空囊写空手()
    {
        Assert.Equal("带走 空手", TransitionText.CarryLine(new Inventory(), Catalog));
        Assert.Equal("带走 空手", TransitionText.CarryLine(null, Catalog));
    }

    // ————————————————————————— 六、--map 四行 —————————————————————————

    [Fact]
    public void 地图自检四行_当前路长走一趟与再走一趟()
    {
        var lines = MapReport.Lines(RealTravel(), "hub1", 8);

        Assert.Equal(4, lines.Count);

        Assert.Equal("[图] 当前 城南小院（hub1），余刻 8", lines[0]);
        Assert.StartsWith("[图] ", lines[1]);
        Assert.Contains("县城 2 刻", lines[1]);
        Assert.Contains("山神庙 6 刻", lines[1]);

        // 走一趟：走得起的里面最远的那条（hub1 → 白幡村 6 刻，余 2）。
        Assert.Equal("[图] 走一趟：城南小院 → 白幡村，耗 6 刻，余 2", lines[2]);

        // 再走一趟：余 2，从白幡村回城南小院要 6 刻——这一行就是面板灰显的那一句。
        Assert.Equal("[图] 再走一趟：余刻 2，去城南小院要 6 刻 —— 走不动", lines[3]);
    }
}
