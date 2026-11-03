using System;
using System.IO;
using System.Text;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 位移、开场白、立绘、开局资金（票号 S3-01b）。
///
/// 这四张表都是"读歪了也不会当场崩、只会悄悄奇怪"的那一类：路走不通、第一屏少一句、
/// 某人少了半张脸、新档一文钱都没有。所以这里盯得最紧的不是功能跑通，
/// 而是**坏表有没有当场带着行号喊出来**，以及**失败时账有没有被偷偷动过**。
/// </summary>
public class TravelTests
{
    // ── 内联小表：四条路，故意不对称（回程省一刻），另有一条 6 刻的最贵路 ──────────

    private const string MiniTravel =
        "from,to,ke,note\n" +
        "hub1,hub2,2,出城\n" +
        "hub2,hub1,1,回程下坡省一刻\n" +
        "hub1,hub3,3,\n" +
        "hub3,hub4,6,最贵的一条\n";

    private static TravelTable MiniTable() => TravelTable.FromCsv(MiniTravel);

    private static TravelBook MiniBook() => new(MiniTable());

    /// <summary>够用的最小物品表：coin 可叠，其余不可叠（用来把囊塞满）。</summary>
    private static readonly ItemCatalog Catalog = ItemCatalog.FromCsv(
        "id,name,kind,stack,note\n" +
        "coin,铜钱,token,1,\n" +
        "book,书,paper,0,\n");

    // ── 1. 位移表 ────────────────────────────────────────────

    [Fact]
    public void 位移表按文件顺序读入且备注去空白()
    {
        var table = MiniTable();

        Assert.Equal(4, table.Count);
        Assert.Equal("hub1", table.All[0].From);
        Assert.Equal("hub2", table.All[0].To);
        Assert.Equal(2, table.All[0].Ke);
        Assert.Equal("出城", table.All[0].Note);
        Assert.Equal(6, table.All[3].Ke);
    }

    [Fact]
    public void Travel_命中时带回起终点刻数与备注()
    {
        var leg = MiniTable().Travel("hub2", "hub1");

        Assert.NotNull(leg);
        Assert.Equal("hub2", leg!.From);
        Assert.Equal("hub1", leg.To);
        Assert.Equal(1, leg.Ke);
        Assert.Equal("回程下坡省一刻", leg.Note);
    }

    [Fact]
    public void Travel_没这条路返回null()
    {
        Assert.Null(MiniTable().Travel("hub2", "hub3"));
    }

    [Fact]
    public void TryGet_空参数一律当没路()
    {
        var table = MiniTable();

        Assert.False(table.TryGet(null, "hub2", out _));
        Assert.False(table.TryGet("hub1", "", out _));
        Assert.False(table.TryGet("", "", out _));
    }

    [Fact]
    public void 七个空间两两有向共四十二条路都建得进表()
    {
        var csv = new StringBuilder("from,to,ke,note\n");
        foreach (var from in ClockTable.Hubs)
            foreach (var to in ClockTable.Hubs)
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    csv.Append(from).Append(',').Append(to).Append(",2,\n");

        var table = TravelTable.FromCsv(csv.ToString());

        Assert.Equal(42, table.Count);

        foreach (var from in ClockTable.Hubs)
            foreach (var to in ClockTable.Hubs)
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    Assert.True(table.TryGet(from, to, out _), $"{from} → {to} 没建进表");
    }

    // ── 2. 位移账 ────────────────────────────────────────────

    [Fact]
    public void 同地零刻()
    {
        Assert.Equal(0, MiniBook().Cost("hub1", "hub1"));
    }

    [Fact]
    public void 没路返回NoRoute()
    {
        Assert.Equal(-1, TravelBook.NoRoute);
        Assert.Equal(TravelBook.NoRoute, MiniBook().Cost("hub2", "hub3"));
    }

    [Fact]
    public void 有路时返回表里的刻数()
    {
        var book = MiniBook();

        Assert.Equal(2, book.Cost("hub1", "hub2"));
        Assert.Equal(1, book.Cost("hub2", "hub1"));
        Assert.Equal(6, book.Cost("hub3", "hub4"));
    }

    [Fact]
    public void 空参数当没路()
    {
        var book = MiniBook();

        Assert.Equal(TravelBook.NoRoute, book.Cost(null, "hub2"));
        Assert.Equal(TravelBook.NoRoute, book.Cost("hub1", null));
    }

    [Fact]
    public void CanGo_刻刚好够就走得成()
    {
        Assert.True(MiniBook().CanGo("hub1", "hub2", 2));
    }

    [Fact]
    public void CanGo_差一刻就走不成()
    {
        Assert.False(MiniBook().CanGo("hub1", "hub2", 1));
    }

    [Fact]
    public void CanGo_没路时刻再多也走不成()
    {
        Assert.False(MiniBook().CanGo("hub2", "hub3", 99));
    }

    [Fact]
    public void CanGo_同地不算走也算走得成()
    {
        Assert.True(MiniBook().CanGo("hub1", "hub1", 0));
    }

    [Fact]
    public void Go_扣刻并报出到达的空间()
    {
        var state = new SaveState { Hub = "hub1" };
        var keLeft = 5;

        var result = MiniBook().Go(state, "hub1", "hub3", ref keLeft);

        Assert.True(result.Ok);
        Assert.Null(result.Reason);
        Assert.Equal("hub3", result.Hub);
        Assert.Equal(3, result.Cost);
        Assert.Equal(2, keLeft);
    }

    [Fact]
    public void Go_刻刚好够时走完剩零刻()
    {
        var state = new SaveState { Hub = "hub3" };
        var keLeft = 6;

        var result = MiniBook().Go(state, "hub3", "hub4", ref keLeft);

        Assert.True(result.Ok);
        Assert.Equal(0, keLeft);
    }

    [Fact]
    public void Go_不写存档()
    {
        var state = new SaveState { Hub = "hub1" };
        var before = state.ToJson();
        var keLeft = 9;

        var result = MiniBook().Go(state, "hub1", "hub2", ref keLeft);

        Assert.True(result.Ok);
        Assert.Equal("hub1", state.Hub); // 落库是调用方的事
        Assert.Equal(before, state.ToJson());
    }

    [Fact]
    public void Go_刻不够时一刻都不扣且给原因()
    {
        var state = new SaveState { Hub = "hub1" };
        var keLeft = 1;

        var result = MiniBook().Go(state, "hub1", "hub2", ref keLeft);

        Assert.False(result.Ok);
        Assert.Null(result.Hub);
        Assert.Equal(2, result.Cost);
        Assert.Contains("刻不够", result.Reason);
        Assert.Equal(1, keLeft);
        Assert.Equal("hub1", state.Hub);
    }

    [Fact]
    public void Go_没路时先说没路不说刻不够()
    {
        var state = new SaveState { Hub = "hub2" };
        var keLeft = 0;

        var result = MiniBook().Go(state, "hub2", "hub3", ref keLeft);

        Assert.False(result.Ok);
        Assert.Equal(0, result.Cost);
        Assert.Contains("没有路", result.Reason);
        Assert.DoesNotContain("刻不够", result.Reason);
        Assert.Equal(0, keLeft);
    }

    [Fact]
    public void Go_起点与存档对不上时拒绝()
    {
        var state = new SaveState { Hub = "hub1" };
        var keLeft = 9;

        var result = MiniBook().Go(state, "hub2", "hub1", ref keLeft);

        Assert.False(result.Ok);
        Assert.Contains("不在", result.Reason);
        Assert.Equal(9, keLeft);
    }

    // ── 3. 坏表带行号 ────────────────────────────────────────

    [Fact]
    public void 位移表缺列时抛在第1行()
    {
        var csv = "from,ke,note\nhub1,2,\n";

        var ex = Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(csv));
        Assert.Contains("第 1 行", ex.Message);
        Assert.Contains("to", ex.Message);
    }

    [Fact]
    public void 位移表to为空时抛在第3行()
    {
        var csv =
            "from,to,ke,note\n" +
            "hub1,hub2,2,\n" +
            "hub1,,3,\n";

        var ex = Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(csv));
        Assert.Contains("第 3 行", ex.Message);
        Assert.Contains("to 为空", ex.Message);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("-1")]
    public void 位移表ke越界时抛在第2行(string ke)
    {
        var csv = "from,to,ke,note\nhub1,hub2," + ke + ",\n";

        var ex = Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(csv));
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("超出", ex.Message);
    }

    [Fact]
    public void 位移表ke不是整数时抛()
    {
        var csv = "from,to,ke,note\nhub1,hub2,两刻,\n";

        var ex = Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(csv));
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("不是整数", ex.Message);
    }

    [Fact]
    public void 位移表from与to相同时抛()
    {
        var csv = "from,to,ke,note\nhub1,hub1,2,\n";

        var ex = Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(csv));
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("同地", ex.Message);
    }

    [Fact]
    public void 位移表写了不认识的空间时抛()
    {
        var csv = "from,to,ke,note\nhub1,hub9,2,\n";

        var ex = Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(csv));
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("hub9", ex.Message);
    }

    [Fact]
    public void 位移表同一对空间写两次时抛()
    {
        var csv =
            "from,to,ke,note\n" +
            "hub1,hub2,2,\n" +
            "hub1,hub2,3,又写了一遍\n";

        var ex = Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(csv));
        Assert.Contains("第 3 行", ex.Message);
        Assert.Contains("重复", ex.Message);
    }

    [Fact]
    public void 位移表连表头都没有时抛()
    {
        Assert.Throws<InvalidDataException>(() => TravelTable.FromCsv(""));
    }

    [Fact]
    public void 位移表只有表头时是空表不是坏表()
    {
        var table = TravelTable.FromCsv("from,to,ke,note\n");

        Assert.Equal(0, table.Count);
    }

    // ── 4. 开场白 ────────────────────────────────────────────

    private const string MiniIntro =
        "seq,speaker,portrait,text,note\n" +
        "1,,,天还没亮。,\n" +
        "2,阿禾,阿禾,你醒了？,\n" +
        "3,阿禾,,别出声。,同一张脸不再画一次\n" +
        "4,,,门外有脚步声。,\n" +
        "5,老周,老周,把火点上。,\n" +
        "6,,,火亮了。,\n";

    [Fact]
    public void 开场白六句按顺序读入且旁白与立绘两个空都认()
    {
        var table = IntroTable.FromCsv(MiniIntro);

        Assert.Equal(6, table.Count);

        var first = table.All[0];
        Assert.Equal(1, first.Seq);
        Assert.True(first.IsNarration);
        Assert.False(first.HasPortrait);
        Assert.Equal("天还没亮。", first.Text);

        var second = table.All[1];
        Assert.False(second.IsNarration);
        Assert.True(second.HasPortrait);
        Assert.Equal("阿禾", second.Speaker);
        Assert.Equal("阿禾", second.Portrait);

        // 说话人不必每句都画立绘：第 3 句就是同一张脸不再画一次。
        Assert.False(table.All[2].HasPortrait);
        Assert.Equal("别出声。", table.All[2].Text);
    }

    [Fact]
    public void 开场白seq不连号时抛在第4行()
    {
        var csv =
            "seq,speaker,portrait,text,note\n" +
            "1,,,一,\n" +
            "2,,,二,\n" +
            "4,,,四,\n" +
            "5,,,五,\n" +
            "6,,,六,\n";

        var ex = Assert.Throws<InvalidDataException>(() => IntroTable.FromCsv(csv));
        Assert.Contains("第 4 行", ex.Message);
        Assert.Contains("seq", ex.Message);
    }

    [Fact]
    public void 开场白不足六句时抛()
    {
        var csv =
            "seq,speaker,portrait,text,note\n" +
            "1,,,一,\n" +
            "2,,,二,\n" +
            "3,,,三,\n";

        var ex = Assert.Throws<InvalidDataException>(() => IntroTable.FromCsv(csv));
        Assert.Contains("6..10", ex.Message);
    }

    [Fact]
    public void 开场白超过十句时抛()
    {
        var csv = new StringBuilder("seq,speaker,portrait,text,note\n");
        for (var i = 1; i <= 11; i++) csv.Append(i).Append(",,,").Append("第").Append(i).Append("句,\n");

        var ex = Assert.Throws<InvalidDataException>(() => IntroTable.FromCsv(csv.ToString()));
        Assert.Contains("6..10", ex.Message);
    }

    [Fact]
    public void 开场白旁白带立绘时抛在第3行()
    {
        var csv =
            "seq,speaker,portrait,text,note\n" +
            "1,,,一,\n" +
            "2,,阿禾,没人却有脸,\n" +
            "3,,,三,\n" +
            "4,,,四,\n" +
            "5,,,五,\n" +
            "6,,,六,\n";

        var ex = Assert.Throws<InvalidDataException>(() => IntroTable.FromCsv(csv));
        Assert.Contains("第 3 行", ex.Message);
        Assert.Contains("旁白", ex.Message);
    }

    [Fact]
    public void 开场白缺列时抛在第1行()
    {
        var ex = Assert.Throws<InvalidDataException>(() => IntroTable.FromCsv("seq,speaker,text\n1,,一\n"));
        Assert.Contains("第 1 行", ex.Message);
        Assert.Contains("portrait", ex.Message);
    }

    [Fact]
    public void 开场白seq不是整数时抛()
    {
        var csv =
            "seq,speaker,portrait,text,note\n" +
            "一,,,一,\n";

        var ex = Assert.Throws<InvalidDataException>(() => IntroTable.FromCsv(csv));
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("不是整数", ex.Message);
    }

    // ── 5. 立绘 ──────────────────────────────────────────────

    /// <summary>造一行立绘：七个零件全给默认值，只改要测的那一个。</summary>
    private static string PortraitCsv(
        object? skin = null,
        object? hair = null,
        object? style = null,
        object? beard = null,
        object? hat = null,
        object? robe = null,
        object? mark = null) =>
        "npc,skin,hair,style,beard,hat,robe,mark,note\n" +
        $"阿禾,{skin ?? 2},{hair ?? 3},{style ?? 1},{beard ?? 1},{hat ?? 1},{robe ?? 2},{mark ?? 0},\n";

    [Fact]
    public void 立绘按npc名取到七层零件()
    {
        var table = PortraitTable.FromCsv(
            "npc,skin,hair,style,beard,hat,robe,mark,note\n" +
            "阿禾,2,3,1,0,1,2,0,\n" +
            "老周,4,6,3,3,4,5,4,满脸风霜\n");

        Assert.Equal(2, table.Count);
        Assert.True(table.TryGet("老周", out var zhou));
        Assert.Equal(4, zhou!.Skin);
        Assert.Equal(6, zhou.Hair);
        Assert.Equal(3, zhou.Style);
        Assert.Equal(3, zhou.Beard);
        Assert.Equal(4, zhou.Hat);
        Assert.Equal(5, zhou.Robe);
        Assert.Equal(4, zhou.Mark);
        Assert.Equal("满脸风霜", zhou.Note);
    }

    [Fact]
    public void 立绘查不到这个人时返回false()
    {
        var table = PortraitTable.FromCsv(PortraitCsv());

        Assert.False(table.TryGet("查无此人", out var p));
        Assert.Null(p);
        Assert.False(table.TryGet(null, out _));
    }

    [Theory]
    [InlineData("skin", "0")]
    [InlineData("skin", "5")]
    [InlineData("hair", "0")]
    [InlineData("hair", "7")]
    [InlineData("style", "0")]
    [InlineData("style", "5")]
    [InlineData("beard", "4")]
    [InlineData("hat", "5")]
    [InlineData("robe", "0")]
    [InlineData("robe", "7")]
    [InlineData("mark", "5")]
    public void 立绘任一层零件越界都抛在第2行(string field, string badValue)
    {
        string Csv(string name, string value) => name switch
        {
            "skin" => PortraitCsv(skin: value),
            "hair" => PortraitCsv(hair: value),
            "style" => PortraitCsv(style: value),
            "beard" => PortraitCsv(beard: value),
            "hat" => PortraitCsv(hat: value),
            "robe" => PortraitCsv(robe: value),
            _ => PortraitCsv(mark: value),
        };

        var ex = Assert.Throws<InvalidDataException>(() => PortraitTable.FromCsv(Csv(field, badValue)));
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains(field, ex.Message);
        Assert.Contains("超出", ex.Message);
    }

    [Fact]
    public void 立绘参数不是整数时抛()
    {
        var ex = Assert.Throws<InvalidDataException>(() => PortraitTable.FromCsv(PortraitCsv(hair: "三")));
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("不是整数", ex.Message);
    }

    [Fact]
    public void 立绘同名同相时两行都留着()
    {
        // 同一个人在两处登场：hub2 与 hub7 各写一行，备注各写各的空间。
        var csv =
            "npc,skin,hair,style,beard,hat,robe,mark,note\n" +
            "沈砚,2,1,1,1,2,3,0,hub2 落第书生\n" +
            "沈砚,2,1,1,1,2,3,0,hub7 同一人\n";

        var table = PortraitTable.FromCsv(csv);

        Assert.Equal(2, table.Count);
        Assert.True(table.TryGet("沈砚", out var who));
        Assert.Equal(2, who!.Skin);
    }

    [Fact]
    public void 立绘同名却长得不一样时抛在第三行()
    {
        var csv =
            "npc,skin,hair,style,beard,hat,robe,mark,note\n" +
            "沈砚,2,1,1,1,2,3,0,\n" +
            "沈砚,2,1,1,1,2,4,0,袍子写错了\n";

        var ex = Assert.Throws<InvalidDataException>(() => PortraitTable.FromCsv(csv));
        Assert.Contains("第 3 行", ex.Message);
        Assert.Contains("第 2 行", ex.Message);
        Assert.Contains("沈砚", ex.Message);
    }

    [Fact]
    public void 立绘缺列时抛在第1行()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => PortraitTable.FromCsv("npc,skin,hair,style,beard,hat,robe,note\n阿禾,1,1,1,0,0,1,\n"));

        Assert.Contains("第 1 行", ex.Message);
        Assert.Contains("mark", ex.Message);
    }

    // ── 6. 开局资金 ──────────────────────────────────────────

    [Fact]
    public void 开局三十文且只占一格()
    {
        var bag = new Inventory();

        var placed = StartKit.Fill(bag, Catalog, out var error);

        Assert.Null(error);
        Assert.Equal(30, placed);
        Assert.Equal(30, bag.Count("coin"));
        Assert.Equal(1, bag.SlotsUsed);
    }

    [Fact]
    public void 囊只剩五格也放得下三十文()
    {
        var bag = new Inventory(12);
        for (var i = 0; i < 7; i++) Assert.True(bag.TryAdd("book", 1, Catalog, out var e), e);

        Assert.Equal(5, bag.Capacity - bag.SlotsUsed);

        var placed = StartKit.Fill(bag, Catalog, out var error);

        Assert.Null(error);
        Assert.Equal(30, placed);
        Assert.Equal(8, bag.SlotsUsed);
    }

    [Fact]
    public void 囊满时三十文一文也放不进并给一句人话()
    {
        var bag = new Inventory(12);
        for (var i = 0; i < 12; i++) Assert.True(bag.TryAdd("book", 1, Catalog, out var e), e);
        Assert.True(bag.IsFull);

        var placed = StartKit.Fill(bag, Catalog, out var error);

        Assert.Equal(0, placed);
        Assert.Equal(Inventory.FullMessage, error);
        Assert.Equal(12, bag.SlotsUsed);
        Assert.Equal(0, bag.Count("coin"));
    }

    [Fact]
    public void 开局资金常量与铜钱物品对得上()
    {
        Assert.Equal(30, StartKit.Coins);
        Assert.Equal(TradeBook.MoneyItem, StartKit.CoinItem);
    }

    // ── 7. 读真表（D3 的表还没到位就跳过）────────────────────

    [Fact]
    public void 真位移表四十二条有向路全在且刻数在一到六之间()
    {
        var path = FindData("travel.csv");
        if (path == null) return; // 表还没到位

        var table = TravelTable.Load(path);

        Assert.Equal(42, table.Count);

        foreach (var leg in table.All)
        {
            Assert.InRange(leg.Ke, TravelTable.MinKe, TravelTable.MaxKe);
            Assert.NotEqual(leg.From, leg.To);
        }

        foreach (var from in ClockTable.Hubs)
            foreach (var to in ClockTable.Hubs)
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    Assert.True(table.TryGet(from, to, out _), $"{from} → {to} 不在真表里");
    }

    [Fact]
    public void 真开场白六到十句且旁白不带立绘()
    {
        var path = FindData("intro.csv");
        if (path == null) return; // 表还没到位

        var table = IntroTable.Load(path);

        Assert.InRange(table.Count, IntroTable.MinLines, IntroTable.MaxLines);

        for (var i = 0; i < table.All.Count; i++)
        {
            var line = table.All[i];
            Assert.Equal(i + 1, line.Seq);
            Assert.False(line.IsNarration && line.HasPortrait);
            Assert.False(string.IsNullOrWhiteSpace(line.Text));
        }
    }

    [Fact]
    public void 真立绘表三十行且零件都在范围内()
    {
        var path = FindData("portraits.csv");
        if (path == null) return; // 表还没到位

        var table = PortraitTable.Load(path);

        Assert.Equal(30, table.Count);

        foreach (var p in table.All)
        {
            Assert.InRange(p.Skin, 1, 4);
            Assert.InRange(p.Hair, 1, 6);
            Assert.InRange(p.Style, 1, 4);
            Assert.InRange(p.Beard, 0, 3);
            Assert.InRange(p.Hat, 0, 4);
            Assert.InRange(p.Robe, 1, 6);
            Assert.InRange(p.Mark, 0, 4);

            // 同名的人（沈砚 / 老鸦 各在两处登场）按名字查得到，且零件与这一行一致。
            Assert.True(table.TryGet(p.Npc, out var back));
            Assert.Equal(p.Skin, back!.Skin);
            Assert.Equal(p.Hair, back.Hair);
            Assert.Equal(p.Style, back.Style);
            Assert.Equal(p.Beard, back.Beard);
            Assert.Equal(p.Hat, back.Hat);
            Assert.Equal(p.Robe, back.Robe);
            Assert.Equal(p.Mark, back.Mark);
        }
    }

    /// <summary>从测试输出目录往上找工程里的 Content/data（与其他 Game* 测试同一套找法）。</summary>
    /// <returns>找到返回路径；还没有这张表返回 null。</returns>
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
