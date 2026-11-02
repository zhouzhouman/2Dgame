using System;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 双信任轴接进游戏（票号 S1-01c）。
///
/// 测的是**接线与文案**这一层：三轴怎么进出存档、屏幕上那句话怎么写、
/// 攻城「间」什么时候能选、面板上两条路线怎么说。画横条与开面板归
/// <c>--shottrust</c> / <c>--shotbag</c> 那两条截图自检，这里不起 Godot。
/// 名词照 CANON §3：官望 / 民望 / 疑心。
/// </summary>
public class GameTrustTests
{
    /// <summary>三条动作：一条加官望，一条加民望，一条两头都掉。</summary>
    private const string TrustCsv =
        "id,label,guan,min,yi,note\n" +
        "bribe,行贿,3,-2,2,\n" +
        "gruel,施粥,-1,4,2,\n" +
        "betray,出卖同伴,4,-8,-8,\n";

    /// <summary>两条路线，门槛 60：一条看官望，一条看民望。</summary>
    private const string RouteCsv =
        "id,label,axis,threshold,note\n" +
        "inside,内应开门,guan,60,\n" +
        "revolt,民变开城,min,60,\n";

    /// <summary>攻城那一拍（b11）与一个不相干的拍，用来验门禁只认该认的项。</summary>
    private const string BeatCsv =
        "id,name,hub,day,requires,sets,trigger,options,note\n" +
        "b03,第一口锅,hub2,3,,beat.b03.done,choice,救人|救粮,\n" +
        "b11,攻城,hub7,29,,beat.b11.done,choice,围|攻|间|水火,\n";

    private static TrustTable Table() => TrustTable.FromCsv(TrustCsv);

    private static RouteTable Routes() => RouteTable.FromCsv(RouteCsv);

    private static BeatTable Beats() => BeatTable.FromCsv(BeatCsv);

    private static Beat BeatOf(string id) => Beats().All.First(b => b.Id == id);

    private static TrustState State(int guan, int min, int yi) => new() { Guan = guan, Min = min, Yi = yi };

    private static TrustBook Book(TrustState state) => new(state, Table(), Routes());

    // ————————————————————————— 存档接线 —————————————————————————

    [Fact]
    public void 应用之后_三轴写进存档_读档还原()
    {
        var save = new SaveState();

        // 三轴只有一份：Restore 交出来的就是存档里那个对象，账本就地改它
        var book = new TrustBook(TrustWiring.Restore(save), Table(), Routes());
        Assert.Same(save.Trust[TrustWiring.Region], book.State);

        book.Apply("bribe");       // 官望 +3、民望 −2（底在 0，落 0）、疑心 +2
        book.Apply("gruel");       // 官望 −1、民望 +4、疑心 +2

        TrustWiring.Store(save, book);
        Assert.Equal(2, save.Trust[TrustWiring.Region].Guan);
        Assert.Equal(4, save.Trust[TrustWiring.Region].Min);
        Assert.Equal(4, save.Trust[TrustWiring.Region].Yi);

        // 真走一遍 JSON：存档形状不变，读回来还是这三个数
        var back = SaveState.FromJson(save.ToJson());
        var restored = TrustWiring.Restore(back);

        Assert.Equal(2, restored.Guan);
        Assert.Equal(4, restored.Min);
        Assert.Equal(4, restored.Yi);
    }

    [Fact]
    public void 存档里没有这一条_按存档默认值起_不自己设初值()
    {
        var save = new SaveState();

        var state = TrustWiring.Restore(save);

        // SaveState.TrustState 的默认值就是 0/0/0；接线这一层不许编一个"初始好感"
        Assert.Equal(0, state.Guan);
        Assert.Equal(0, state.Min);
        Assert.Equal(0, state.Yi);
        Assert.Same(save.Trust[TrustWiring.Region], state);
    }

    [Fact]
    public void 读档时沿用存档里那一份_不被别的实例顶掉()
    {
        var save = new SaveState();
        save.Trust[TrustWiring.Region] = State(41, 7, 12);

        var restored = TrustWiring.Restore(save);
        var book = new TrustBook(restored, Table(), Routes());

        book.Apply("gruel");

        Assert.Equal(41 - 1, book.State.Guan);
        Assert.Equal(11, book.State.Min);
        Assert.Same(save.Trust[TrustWiring.Region], book.State);
    }

    // ————————————————————————— 变化提示 —————————————————————————

    [Fact]
    public void 提示文案就是Explain_没动的轴不出现()
    {
        var book = Book(State(10, 20, 5));
        var table = Table();

        var delta = book.Apply("bribe");
        Assert.True(table.TryGet("bribe", out var action));

        Assert.False(delta.Clamped);
        Assert.Equal("官望 +3  民望 −2  疑心 +2", TrustText.Toast(book, action, delta));
    }

    [Fact]
    public void 撞到顶_末尾补已到顶()
    {
        var book = Book(State(99, 50, 0));

        var delta = book.Apply("bribe");
        Assert.True(delta.Clamped);
        Assert.Equal(1, delta.Guan);                    // 表里写 3，只落得下 1

        var action = Table().TryGet("bribe", out var a) ? a : null;
        // 标记跟着那条轴走，不再统一贴在末尾：官望想加 3、只落得下 1
        Assert.Equal("官望 +3（已到顶）  民望 −2  疑心 +2", TrustText.Toast(book, action, delta));
    }

    [Fact]
    public void 撞到底_末尾补已见底()
    {
        var book = Book(State(10, 1, 0));

        var delta = book.Apply("bribe");
        Assert.True(delta.Clamped);
        Assert.Equal(-1, delta.Min);                    // 表里写 −2，只落得下 −1

        var action = Table().TryGet("bribe", out var a) ? a : null;
        // 民望想减 2、只减得下 1
        Assert.Equal("官望 +3  民望 −2（已见底）  疑心 +2", TrustText.Toast(book, action, delta));
    }

    [Fact]
    public void 查无此动作_状态不动_提示为空()
    {
        var book = Book(State(10, 20, 5));

        var delta = book.Apply("没有这一条");

        Assert.False(delta.Applied);
        Assert.True(delta.IsEmpty);
        Assert.Equal(10, book.State.Guan);
        Assert.Equal(20, book.State.Min);
        Assert.Equal(5, book.State.Yi);

        Assert.Equal("", TrustText.Toast(book, null, delta));
    }

    // ————————————————————————— 信任面板：横条与路线 —————————————————————————

    [Fact]
    public void 面板三条横条_顺带数值与占比()
    {
        var panel = new TrustPanel(Book(State(62, 60, 7)));

        var bars = panel.Bars();

        Assert.Equal(3, bars.Count);
        Assert.Equal("官望", bars[0].Name);
        Assert.Equal("民望", bars[1].Name);
        Assert.Equal("疑心", bars[2].Name);

        Assert.Equal(62, bars[0].Value);
        Assert.Equal(60, bars[1].Value);
        Assert.Equal(7, bars[2].Value);

        Assert.Equal(0.62f, bars[0].Ratio, 2);
        Assert.Equal(0.07f, bars[2].Ratio, 2);
    }

    [Fact]
    public void 路线没开_说还差多少()
    {
        var panel = new TrustPanel(Book(State(36, 0, 0)));

        var lines = panel.RouteLines();

        Assert.Equal(2, lines.Count);
        Assert.Equal("内应开门：还差 24 官望", lines[0]);
        Assert.Equal("民变开城：还差 60 民望", lines[1]);
        foreach (var open in panel.RouteOpen()) Assert.False(open);
    }

    [Fact]
    public void 路线开了_说可走并带上当时的数值()
    {
        var panel = new TrustPanel(Book(State(62, 60, 0)));

        var lines = panel.RouteLines();

        Assert.Equal("内应开门：可走（官望 62 ≥ 60）", lines[0]);
        Assert.Equal("民变开城：可走（民望 60 ≥ 60）", lines[1]);
        foreach (var open in panel.RouteOpen()) Assert.True(open);
    }

    // ————————————————————————— 攻城选「间」 —————————————————————————

    [Fact]
    public void 官望不够_攻城的间不可选_并说清为什么()
    {
        var beat = BeatOf("b11");
        var book = Book(State(36, 80, 0));

        Assert.Equal(2, TrustChoice.InsideIndex(beat));      // 围|攻|间|水火 → 第 3 项
        Assert.True(TrustChoice.Locked(book, beat, 2));
        Assert.Equal("官望不够，城里没人给你开门", TrustChoice.RefusalFor(book, beat, 2));
    }

    [Fact]
    public void 官望够了_攻城的间就能选()
    {
        var beat = BeatOf("b11");
        var book = Book(State(60, 0, 0));                    // 等于门槛算开

        Assert.False(TrustChoice.Locked(book, beat, 2));
        Assert.Null(TrustChoice.RefusalFor(book, beat, 2));
        Assert.True(book.IsOpen(TrustChoice.InsideRoute));
    }

    [Fact]
    public void 门禁只管攻城那一拍的间_别的项与别的拍一律放行()
    {
        var siege = BeatOf("b11");
        var other = BeatOf("b03");
        var book = Book(State(0, 0, 0));

        Assert.False(TrustChoice.Locked(book, siege, 0));    // 围
        Assert.False(TrustChoice.Locked(book, siege, 1));    // 攻
        Assert.False(TrustChoice.Locked(book, siege, 3));    // 水火
        Assert.False(TrustChoice.Locked(book, other, 0));    // 别的拍

        Assert.False(TrustChoice.Locked(null, siege, 2));    // 表没接上时不锁人
    }

    // ————————————————————————— --trust 输出 —————————————————————————

    [Fact]
    public void trust输出_带动作名与Explain()
    {
        var book = Book(State(10, 20, 5));
        var table = Table();

        var delta = book.Apply("bribe");
        Assert.True(table.TryGet("bribe", out var action));
        Assert.NotNull(action);

        var line = TrustReport.ActionLine(action!, TrustText.Toast(book, action, delta));

        Assert.Equal("[信任] bribe 行贿 → 官望 +3  民望 −2  疑心 +2", line);
        Assert.Equal("[信任] 没有这条动作：nope", TrustReport.UnknownLine("nope"));
    }

    [Fact]
    public void trust输出_从零起跑时会说出已见底()
    {
        // --trust=bribe 在新档（0/0/0）上就是这个结果：民望本来就在底，−2 落不下去
        var book = Book(State(0, 0, 0));
        var table = Table();

        var delta = book.Apply("bribe");
        Assert.True(table.TryGet("bribe", out var action));

        var line = TrustReport.ActionLine(action!, TrustText.Toast(book, action, delta));

        // 民望本来就在底：仍然要说出来，否则玩家看不见「行贿会掉民望」这条互斥
        Assert.Equal("[信任] bribe 行贿 → 官望 +3  民望 −2（已见底）  疑心 +2", line);
    }

    // ————————————————————————— 真表 —————————————————————————

    [Theory]
    [InlineData("bribe", 3, -2, 2)]
    [InlineData("gruel", -1, 4, 2)]
    [InlineData("betray_companion", 4, -8, -8)]
    public void 真表里那几条动作_数值对得上(string id, int guan, int min, int yi)
    {
        var path = FindData("trust.csv");
        Assert.NotNull(path);

        var table = TrustTable.Load(path!);
        // 不写死条数：表会随着系统接入变长（S2-02 就加了两条市场动作），
        // 写死会让学生每次加数据都红一次，而它想守的其实是"别把已有动作删了"。
        Assert.True(table.Count >= 9, $"信任动作只剩 {table.Count} 条，比 S1-01 的 9 条还少");
        Assert.True(table.TryGet("market_official", out _), "官市交易那条市场动作没了");
        Assert.True(table.TryGet("market_black", out _), "黑市交易那条市场动作没了");
        Assert.True(table.TryGet(id, out var action));

        Assert.Equal(guan, action!.Guan);
        Assert.Equal(min, action.Min);
        Assert.Equal(yi, action.Yi);
    }

    [Fact]
    public void 真表的攻城路线_零起步时两扇门都关着()
    {
        var path = FindData("routes.csv");
        Assert.NotNull(path);

        var routes = RouteTable.Load(path!);
        Assert.Equal(2, routes.Count);

        var book = new TrustBook(new TrustState(), TrustTable.Load(FindData("trust.csv")!), routes);

        Assert.False(book.IsOpen("inside"));
        Assert.False(book.IsOpen("revolt"));
        Assert.Empty(book.OpenRoutes());

        // 真表门槛是占位 60：先按表里的数说，不在这里另立一个
        var panel = new TrustPanel(book);
        foreach (var route in routes.All)
        {
            var line = TrustPanel.RouteLine(route, 0);
            Assert.Contains($"还差 {route.Threshold} ", line);
        }
    }

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 GameBagTests 同一套找法）。</summary>
    private static string? FindData(string fileName)
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

        return null;
    }
}
