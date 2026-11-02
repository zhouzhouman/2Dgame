using System;
using System.IO;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 双信任轴与攻城路线（票号 S1-01b）。
/// 规则源：举火_游戏设计文档_v0.2 §16.4、§7；CANON §3（官望 / 民望 / 疑心）。
/// 表的数据由另一人写：这里全用内联 CSV，真表只做"若存在"的体检。
/// </summary>
public class TrustBookTests
{
    /// <summary>三条路线：两条 40 门槛（各看一轴），一条 90 的高门槛。</summary>
    private const string RouteCsv =
        "id,label,axis,threshold,note\n" +
        "inside,内应开门,guan,40,守军里有人认得你\n" +
        "uprising,民变开城,min,40,百姓替你抬门闩\n" +
        "hold,死守,guan,90,硬碰硬\n";

    /// <summary>可用的动作表（crackdown 换成正常的负增量版）。</summary>
    private const string GoodTrustCsv =
        "id,label,guan,min,yi,note\n" +
        "granary,开仓放粮,0,3,0,施粥给流民\n" +
        "bribe,给县丞送绸,4,-3,1,绸是从农户家收的\n" +
        "guanup,替官府平事,5,0,0,只讨官面喜欢\n" +
        "crackdown,清乡,-8,-8,-8,官面干净了，人跑了\n";

    private static TrustTable Table() => TrustTable.FromCsv(GoodTrustCsv);

    private static RouteTable Routes() => RouteTable.FromCsv(RouteCsv);

    private static TrustBook Book(TrustState state) => new(state, Table(), Routes());

    private static TrustState State(int guan, int min, int yi) => new() { Guan = guan, Min = min, Yi = yi };

    // ── 一、单条正向 ───────────────────────────────────────────────

    [Fact]
    public void 单条正向只动它该动的轴()
    {
        var book = Book(State(10, 50, 5));

        var delta = book.Apply("granary");

        Assert.True(delta.Applied);
        Assert.False(delta.Clamped);
        Assert.Equal(0, delta.Guan);
        Assert.Equal(3, delta.Min);
        Assert.Equal(0, delta.Yi);
        Assert.Equal(10, book.State.Guan);
        Assert.Equal(53, book.State.Min);
        Assert.Equal(5, book.State.Yi);
    }

    [Fact]
    public void 三条轴可以同时一正一负()
    {
        var book = Book(State(10, 50, 5));

        var delta = book.Apply("bribe");

        Assert.Equal(4, delta.Guan);
        Assert.Equal(-3, delta.Min);
        Assert.Equal(1, delta.Yi);
        Assert.Equal(14, book.State.Guan);
        Assert.Equal(47, book.State.Min);
        Assert.Equal(6, book.State.Yi);
    }

    // ── 二、钳到 100 ──────────────────────────────────────────────

    [Fact]
    public void 刚好打满一百不算被钳()
    {
        var book = Book(State(96, 50, 5));

        var delta = book.Apply("bribe");

        Assert.Equal(100, book.State.Guan);
        Assert.Equal(4, delta.Guan);
        Assert.False(delta.Clamped);
    }

    [Fact]
    public void 离顶还有一格时只进去一格()
    {
        var book = Book(State(99, 50, 5));

        var delta = book.Apply("guanup");

        Assert.Equal(100, book.State.Guan);
        Assert.Equal(1, delta.Guan);
        Assert.True(delta.Clamped);
    }

    [Fact]
    public void 顶格之后再打增量为零但动作仍然成立()
    {
        var book = Book(State(100, 50, 5));

        var delta = book.Apply("guanup");

        Assert.True(delta.Applied);
        Assert.True(delta.Clamped);
        Assert.True(delta.IsEmpty);
        Assert.Equal(100, book.State.Guan);
        Assert.Equal(50, book.State.Min);
    }

    [Fact]
    public void 只有一条轴撞墙也算钳过且别的轴照常落账()
    {
        var book = Book(State(98, 50, 5));

        var delta = book.Apply("bribe");

        Assert.Equal(2, delta.Guan);
        Assert.Equal(-3, delta.Min);
        Assert.Equal(1, delta.Yi);
        Assert.True(delta.Clamped);
        Assert.Equal(47, book.State.Min);
        Assert.Equal(6, book.State.Yi);
    }

    // ── 三、钳到 0 ────────────────────────────────────────────────

    [Fact]
    public void 撞到零只记落进去的那部分()
    {
        var book = Book(State(50, 2, 5));

        var delta = book.Apply("crackdown");

        Assert.Equal(0, book.State.Min);
        Assert.Equal(-2, delta.Min);
        Assert.Equal(42, book.State.Guan);
        Assert.Equal(0, book.State.Yi);
        Assert.True(delta.Clamped);
    }

    [Fact]
    public void 见底之后再减增量为零而状态不动()
    {
        var book = Book(State(0, 50, 0));

        var delta = book.Apply("crackdown");

        Assert.Equal(0, book.State.Guan);
        Assert.Equal(42, book.State.Min);
        Assert.Equal(0, delta.Guan);
        Assert.Equal(0, delta.Yi);
        Assert.True(delta.Clamped);
    }

    // ── 四、未知 id ───────────────────────────────────────────────

    [Fact]
    public void 未知动作不抛也不改状态()
    {
        var book = Book(State(10, 50, 5));

        var delta = book.Apply("没有这一行");

        Assert.False(delta.Applied);
        Assert.True(delta.IsEmpty);
        Assert.False(delta.Clamped);
        Assert.Equal(10, book.State.Guan);
        Assert.Equal(50, book.State.Min);
        Assert.Equal(5, book.State.Yi);
    }

    [Fact]
    public void 空id与null同样只是白点一下()
    {
        var book = Book(State(10, 50, 5));

        Assert.False(book.Apply(null).Applied);
        Assert.False(book.Apply("").Applied);
        Assert.Equal(10, book.State.Guan);
        Assert.Equal(50, book.State.Min);
        Assert.Equal(5, book.State.Yi);
    }

    // ── 五、两条路线各自的开关 ────────────────────────────────────

    [Fact]
    public void 官望恰好等于门槛算开门()
    {
        Assert.True(Book(State(40, 0, 0)).IsOpen("inside"));
        Assert.False(Book(State(39, 0, 0)).IsOpen("inside"));
    }

    [Fact]
    public void 民望恰好等于门槛算开门()
    {
        Assert.True(Book(State(0, 40, 0)).IsOpen("uprising"));
        Assert.False(Book(State(0, 39, 0)).IsOpen("uprising"));
    }

    [Fact]
    public void 两条路线互不顶替()
    {
        // 官望够、民望不够：只有内应那条开着
        var guanOnly = Book(State(45, 10, 0));
        Assert.True(guanOnly.IsOpen("inside"));
        Assert.False(guanOnly.IsOpen("uprising"));

        // 反过来也一样
        var minOnly = Book(State(10, 45, 0));
        Assert.False(minOnly.IsOpen("inside"));
        Assert.True(minOnly.IsOpen("uprising"));
    }

    [Fact]
    public void 门槛更高的那条要更高的数()
    {
        Assert.False(Book(State(89, 0, 0)).IsOpen("hold"));
        Assert.True(Book(State(90, 0, 0)).IsOpen("hold"));
    }

    [Fact]
    public void 未知路线算没开且不抛()
    {
        var book = Book(State(100, 100, 0));

        Assert.False(book.IsOpen("不存在的门"));
        Assert.False(book.IsOpen(null));
        Assert.False(book.IsOpen(""));
    }

    // ── 六、OpenRoutes 顺序 ──────────────────────────────────────

    [Fact]
    public void 开着的路线按文件顺序排()
    {
        var book = Book(State(40, 45, 0));

        Assert.Equal(new[] { "inside", "uprising" }, book.OpenRoutes());
    }

    [Fact]
    public void 一条没开时列表是空的()
    {
        Assert.Empty(Book(State(0, 0, 0)).OpenRoutes());
    }

    [Fact]
    public void 全开时仍是文件顺序()
    {
        var book = Book(State(95, 95, 0));

        Assert.Equal(new[] { "inside", "uprising", "hold" }, book.OpenRoutes());
    }

    [Fact]
    public void 打一次动作能把路线推开()
    {
        var book = Book(State(38, 10, 0));
        Assert.False(book.IsOpen("inside"));

        book.Apply("guanup");

        Assert.Equal(43, book.State.Guan);
        Assert.True(book.IsOpen("inside"));
    }

    // ── 七、Explain 文案 ─────────────────────────────────────────

    [Fact]
    public void 三条轴都动时按官民疑的顺序说()
    {
        var book = Book(State(10, 50, 5));

        Assert.Equal("官望 +4  民望 \u22123  疑心 +1", book.Explain(book.Apply("bribe")));
    }

    [Fact]
    public void 增量为零的轴不出现()
    {
        var book = Book(State(10, 50, 5));

        Assert.Equal("民望 +3", book.Explain(book.Apply("granary")));
    }

    [Fact]
    public void 全零增量是空串()
    {
        var book = Book(State(10, 50, 5));

        Assert.Equal("", book.Explain(TrustDelta.None));
        Assert.Equal("", book.Explain(book.Apply("查无此action")));
    }

    [Fact]
    public void 负号是真减号不是连字符()
    {
        var book = Book(State(50, 10, 10));

        var text = book.Explain(book.Apply("crackdown"));

        Assert.Equal("官望 \u22128  民望 \u22128  疑心 \u22128", text);
        Assert.DoesNotContain('-', text);
    }

    // ── 七之二、扣不动的部分也要说出来（票号 S1-01d）────────────────

    [Fact]
    public void 三轴见底时民望照样出现在提示里并标已见底()
    {
        // --trust=bribe 在新档（0/0/0）上：民望想 −3，实际一格都扣不动
        var book = Book(State(0, 0, 0));

        var delta = book.Apply("bribe");

        Assert.Equal(0, delta.Min);                          // 实际没落下去
        Assert.Equal(-3, delta.IntendedMin);                 // 表里想扣 3
        Assert.Equal(4, delta.IntendedGuan);
        Assert.Equal(1, delta.IntendedYi);
        Assert.True(delta.WasClamped(TrustAxes.Min));
        Assert.False(delta.WasClamped(TrustAxes.Guan));

        var text = book.Explain(delta);
        Assert.Contains("民望 \u22123（已见底）", text);
        Assert.Equal("官望 +4  民望 \u22123（已见底）  疑心 +1", text);
    }

    [Fact]
    public void 到顶时标的是那一条轴不是整条末尾()
    {
        var book = Book(State(100, 50, 5));

        var delta = book.Apply("guanup");

        Assert.True(delta.WasClamped(TrustAxes.Guan));
        Assert.Equal(0, delta.Guan);                         // 一格都没进去
        Assert.Equal("官望 +5（已到顶）", book.Explain(delta));
    }

    [Fact]
    public void 两条轴各撞一个方向时各标各的()
    {
        // 官望顶格还要加、民望见底还要减：同一句话里两堵墙都得说清楚
        var book = Book(State(100, 0, 5));

        var delta = book.Apply("bribe");

        Assert.Equal(TrustAxes.Guan | TrustAxes.Min, delta.ClampedAxes);
        Assert.Equal("官望 +4（已到顶）  民望 \u22123（已见底）  疑心 +1", book.Explain(delta));
    }

    [Fact]
    public void 没被钳时一个括号都不带()
    {
        var book = Book(State(10, 50, 5));

        var delta = book.Apply("bribe");

        Assert.Equal(TrustAxes.None, delta.ClampedAxes);
        Assert.False(delta.Clamped);

        var text = book.Explain(delta);
        Assert.Equal("官望 +4  民望 \u22123  疑心 +1", text);
        Assert.DoesNotContain("（", text);
        Assert.DoesNotContain("）", text);
    }

    [Fact]
    public void 未知动作仍是空串也不带括号()
    {
        var book = Book(State(0, 0, 0));

        var delta = book.Apply("查无此action");

        Assert.Equal(TrustAxes.None, delta.ClampedAxes);
        Assert.Equal("", book.Explain(delta));
    }

    // ── 八、坏表 ─────────────────────────────────────────────────

    [Fact]
    public void 信任表缺列带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => TrustTable.FromCsv("id,label,guan,min,yi\nx,某,1,1,1\n"));

        Assert.Contains("第 1 行", ex.Message);
        Assert.Contains("note", ex.Message);
    }

    [Fact]
    public void 信任表空id带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => TrustTable.FromCsv("id,label,guan,min,yi,note\nx,甲,1,1,1,\n,乙,1,1,1,\n"));

        Assert.Contains("第 3 行", ex.Message);
        Assert.Contains("id 为空", ex.Message);
    }

    [Fact]
    public void 信任表重复id带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => TrustTable.FromCsv("id,label,guan,min,yi,note\nx,甲,1,1,1,\nx,乙,1,1,1,\n"));

        Assert.Contains("第 3 行", ex.Message);
        Assert.Contains("重复", ex.Message);
    }

    [Fact]
    public void 信任表步长不是整数带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => TrustTable.FromCsv("id,label,guan,min,yi,note\nx,甲,三,1,1,\n"));

        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("guan", ex.Message);
    }

    [Fact]
    public void 信任表步长超出正负十带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => TrustTable.FromCsv("id,label,guan,min,yi,note\ny,乙,3,30,1,\n"));

        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("min", ex.Message);
    }

    [Fact]
    public void 路线表轴写错带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => RouteTable.FromCsv("id,label,axis,threshold,note\na,门,yi,40,\n"));

        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("axis", ex.Message);
    }

    [Fact]
    public void 路线表门槛越界带行号抛()
    {
        Assert.Contains("第 2 行", Assert.Throws<InvalidDataException>(
            () => RouteTable.FromCsv("id,label,axis,threshold,note\na,门,guan,0,\n")).Message);

        Assert.Contains("第 2 行", Assert.Throws<InvalidDataException>(
            () => RouteTable.FromCsv("id,label,axis,threshold,note\na,门,guan,101,\n")).Message);
    }

    [Fact]
    public void 空表连表头都没有就抛()
    {
        Assert.Throws<InvalidDataException>(() => TrustTable.FromCsv(""));
        Assert.Throws<InvalidDataException>(() => RouteTable.FromCsv(""));
    }

    // ── 九、真表体检（表还没到位就跳过）────────────────────────────

    [Fact]
    public void 真表若已存在则至少八条动作两条路线()
    {
        var trustPath = FindData("trust.csv");
        var routePath = FindData("routes.csv");
        if (trustPath == null || routePath == null) return; // 数据还没写，不红

        var table = TrustTable.Load(trustPath);
        var routes = RouteTable.Load(routePath);

        Assert.True(table.Count >= 8, $"trust.csv 只有 {table.Count} 条动作，至少要 8 条。");
        Assert.True(routes.Count >= 2, $"routes.csv 只有 {routes.Count} 条路线，至少要 2 条。");

        // 每条动作都得真能在账本上打出来，且文案不是空的
        var book = new TrustBook(new TrustState { Guan = 50, Min = 50, Yi = 50 }, table, routes);
        foreach (var action in table.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Label), $"动作「{action.Id}」没有 label。");
            var delta = book.Apply(action.Id);
            Assert.True(delta.Applied, $"动作「{action.Id}」在真表里打不出来。");
        }

        Assert.NotEmpty(routes.All);
    }

    /// <summary>从测试输出目录往上找 <c>Content/data/&lt;name&gt;</c>；找不到返回 null。</summary>
    private static string? FindData(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "Content", "data", name);
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }

        return null;
    }
}
