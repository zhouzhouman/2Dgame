using System;
using System.IO;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 买卖接进游戏（票号 S2-02c）。规则已经在 TradeBook 里（S2-02b），这一层只盯界面与接线：
/// 哪个人守哪个市、每行怎么写、门槛两态、成交之后钱 / 囊 / 三轴是不是一起动了，
/// 以及失败时是不是照 Core 的原话回话。
///
/// 全部不起 Godot：面板只算字，画在 Main 里；<c>--shop</c> 的输出格式钉在
/// <see cref="ShopReport"/>，命令行与这里读的是同一份。
/// </summary>
public class GameShopTests
{
    /// <summary>最小物品表：coin 是钱，grain 在柜台上称「粮」，blade 用来演"这个市不收"。</summary>
    private const string CatalogCsv =
        "id,name,kind,stack,note\n" +
        "coin,铜钱,token,1,\n" +
        "grain,谷子,food,1,\n" +
        "cloth,干净衣裳,tool,1,\n" +
        "blade,横刀,tool,0,\n";

    /// <summary>两个市都卖粮（官市贵、黑市便宜），布只在官市、刀只在黑市。</summary>
    private const string ShopsCsv =
        "shop,item,price,stock,note\n" +
        "official,grain,10,12,\n" +
        "official,cloth,36,4,\n" +
        "black,grain,6,15,\n" +
        "black,blade,23,2,\n";

    /// <summary>真实的两条交易动作：官市 +1 官望、黑市 +3 疑心。</summary>
    private const string TrustCsv =
        "id,label,guan,min,yi,note\n" +
        "market_official,官市交易,1,0,0,\n" +
        "market_black,黑市交易,0,0,3,\n";

    /// <summary>路线表（这一票不看路线，但 TrustBook 要一张）。</summary>
    private const string RouteCsv =
        "id,label,axis,threshold,note\n" +
        "inside,内应开门,guan,60,\n";

    private static readonly ItemCatalog Catalog = ItemCatalog.FromCsv(CatalogCsv);
    private static readonly ShopTable Shops = ShopTable.FromCsv(ShopsCsv);
    private static readonly TrustTable TrustActions = TrustTable.FromCsv(TrustCsv);
    private static readonly RouteTable Routes = RouteTable.FromCsv(RouteCsv);

    /// <summary>一张面板 + 它后面那本账，测试里要一起看。</summary>
    private sealed record Rig(ShopScreen Screen, TradeBook Trade, Inventory Bag, TrustBook Trust);

    private static Rig Make(
        int guan = 0,
        int coins = 50,
        bool roadPass = false,
        string shop = ShopTable.Official,
        string bagItems = "")
    {
        var bag = new Inventory();
        if (coins > 0) bag.TryAdd(TradeBook.MoneyItem, coins, Catalog, out _);

        foreach (var piece in bagItems.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = piece.IndexOf(':');
            var id = cut < 0 ? piece : piece[..cut];
            var n = cut < 0 ? 1 : int.Parse(piece[(cut + 1)..]);
            bag.TryAdd(id, n, Catalog, out _);
        }

        var state = new TrustState { Guan = guan };
        var trust = new TrustBook(state, TrustActions, Routes);
        var trade = new TradeBook(Shops, bag, Catalog, state, name => roadPass && name == TradeBook.RoadPassFlag);

        return new Rig(new ShopScreen(Shops, trade, bag, Catalog, trust, shop), trade, bag, trust);
    }

    // ————————————————————————— 入口 —————————————————————————

    [Fact]
    public void 商人按显示名认_布商守官市_掮客守黑市_别人没有市()
    {
        Assert.Equal(ShopTable.Official, ShopWiring.ShopOf("布商"));
        Assert.Equal(ShopTable.Black, ShopWiring.ShopOf("掮客"));
        Assert.Null(ShopWiring.ShopOf("县丞"));
        Assert.Null(ShopWiring.ShopOf(""));
        Assert.Null(ShopWiring.ShopOf(null));

        // 抬头上的名字也跟着市走，不会出现"掮客 · 官市"这种串台
        Assert.Equal("布商", ShopWiring.MerchantOf(ShopTable.Official));
        Assert.Equal("掮客", ShopWiring.MerchantOf(ShopTable.Black));
    }

    [Fact]
    public void 旁边没商人时_那句话是这儿没人做买卖()
    {
        Assert.Equal("这儿没人做买卖", ShopWiring.NoMerchantText);
    }

    // ————————————————————————— 面板文案 —————————————————————————

    [Fact]
    public void 买那一行_名字数量单价三样都在()
    {
        var rig = Make();
        var rows = rig.Screen.Rows();

        // 柜台上按行话叫「粮」（items.csv 里叫「谷子」），数量是这一趟的余量
        Assert.Equal("粮  ×12  单价 10 文", rows[0].Label);
        Assert.Equal(10, rows[0].Price);
        Assert.Equal(12, rows[0].Count);
        Assert.True(rows[0].Sellable);
    }

    [Fact]
    public void 官市门槛_官望够了就说够()
    {
        var rig = Make(guan: 34);
        Assert.Equal("官市：官望 34，够", rig.Screen.ThresholdLine);
    }

    [Fact]
    public void 官市门槛_不够就说要路引或三十_并报当前官望()
    {
        var rig = Make(guan: 12);
        Assert.Equal("官市：要路引，或官望 30（你 12）", rig.Screen.ThresholdLine);
    }

    [Fact]
    public void 黑市没有门槛_但把疑心写在脸上()
    {
        var rig = Make(guan: 0, shop: ShopTable.Black);
        Assert.Equal("黑市：不问出身，只记疑心", rig.Screen.ThresholdLine);
        Assert.Equal("掮客　·　买", rig.Screen.HeadLine);
    }

    [Fact]
    public void 抬头写着囊中铜钱()
    {
        var rig = Make(coins: 47);
        Assert.Equal("囊中铜钱 47", rig.Screen.MoneyLine);
    }

    // ————————————————————————— 成交 —————————————————————————

    [Fact]
    public void 官市买一件_钱减十_囊加一粮_官望加一()
    {
        var rig = Make(guan: TradeBook.OfficialGuanThreshold);

        var result = rig.Screen.Confirm();

        Assert.NotNull(result);
        Assert.True(result!.Value.Ok);
        Assert.Equal(40, rig.Bag.Count(TradeBook.MoneyItem));
        Assert.Equal(1, rig.Bag.Count("grain"));
        Assert.Equal(TradeBook.OfficialGuanThreshold + 1, rig.Trust.State.Guan);
        Assert.Equal("官望 +1", rig.Screen.LastToast);
        Assert.Equal("买下 粮 ×1，付 10 文", rig.Screen.Message);
    }

    [Fact]
    public void 黑市买同一升粮_便宜四文_但只涨疑心()
    {
        var rig = Make(guan: 0, shop: ShopTable.Black);

        var result = rig.Screen.Confirm();

        Assert.True(result!.Value.Ok);
        Assert.Equal(44, rig.Bag.Count(TradeBook.MoneyItem));
        Assert.Equal(1, rig.Bag.Count("grain"));

        // 官望一分没涨，疑心 +3：这就是两边最大的差别
        Assert.Equal(0, rig.Trust.State.Guan);
        Assert.Equal(3, rig.Trust.State.Yi);
        Assert.Equal("疑心 +3", rig.Screen.LastToast);
    }

    [Fact]
    public void 卖价按六成_买价十文的粮卖六文()
    {
        var rig = Make(coins: 50, bagItems: "grain:2");
        rig.Screen.ToggleMode();

        var rows = rig.Screen.Rows();
        Assert.Single(rows);
        Assert.Equal("粮  ×2  单价 6 文", rows[0].Label);
        Assert.Equal(TradeBook.SellPrice(10), rows[0].Price);

        var result = rig.Screen.Confirm();

        Assert.True(result!.Value.Ok);
        Assert.Equal(6, result.Value.Amount);
        Assert.Equal(56, rig.Bag.Count(TradeBook.MoneyItem));
        Assert.Equal(1, rig.Bag.Count("grain"));
    }

    [Fact]
    public void 这个市不收的东西_列出来但标这儿不收()
    {
        var rig = Make(bagItems: "blade:1");
        rig.Screen.ToggleMode();

        var rows = rig.Screen.Rows();
        Assert.Single(rows);
        Assert.Equal("横刀  ×1  这儿不收", rows[0].Label);
        Assert.False(rows[0].Sellable);
    }

    [Fact]
    public void 卖铜钱_面板不列_规则层也拒()
    {
        var rig = Make(coins: 30);

        rig.Screen.ToggleMode();
        Assert.Empty(rig.Screen.Rows());       // 铜钱永远不进"卖"这块

        var refused = rig.Trade.Sell(ShopTable.Official, TradeBook.MoneyItem);
        Assert.False(refused.Ok);
        Assert.Equal(TradeBook.CannotSellMoneyReason, refused.Reason);
    }

    // ————————————————————————— 失败 - 门槛 —————————————————————————

    [Fact]
    public void 官市未达标_按下去不成交_钱和官望都不动()
    {
        var rig = Make(guan: 12, coins: 50);

        var result = rig.Screen.Confirm();

        Assert.NotNull(result);
        Assert.False(result!.Value.Ok);
        Assert.Equal(TradeBook.OfficialRefusedReason, result.Value.Reason);

        Assert.Equal(50, rig.Bag.Count(TradeBook.MoneyItem));
        Assert.Equal(0, rig.Bag.Count("grain"));
        Assert.Equal(12, rig.Trust.State.Guan);   // 失败了也没有"顺便涨一点"
        Assert.Equal("", rig.Screen.LastToast);
    }

    [Fact]
    public void 钱不够_面板原样转述Core的原因()
    {
        var rig = Make(guan: TradeBook.OfficialGuanThreshold, coins: 5);

        var result = rig.Screen.Confirm();

        Assert.False(result!.Value.Ok);
        Assert.Equal(TradeBook.NotEnoughMoneyReason, result.Value.Reason);
        Assert.Equal(TradeBook.NotEnoughMoneyReason, rig.Screen.Message);
        Assert.Equal(5, rig.Bag.Count(TradeBook.MoneyItem));
    }

    // ————————————————————————— 面板行为 —————————————————————————

    [Fact]
    public void Tab切开买卖两块_行也跟着换()
    {
        var rig = Make(coins: 50, bagItems: "cloth:1");

        Assert.Equal(ShopMode.Buy, rig.Screen.Mode);
        Assert.Equal(2, rig.Screen.Rows().Count);        // 官市两件货

        rig.Screen.ToggleMode();

        Assert.Equal(ShopMode.Sell, rig.Screen.Mode);
        Assert.Single(rig.Screen.Rows());                // 囊里只有一件干净衣裳
        Assert.StartsWith("干净衣裳", rig.Screen.Rows()[0].Label);
        Assert.Equal(0, rig.Screen.Selected);
    }

    [Fact]
    public void 买走一件之后_货架那一行的余量少一()
    {
        var rig = Make(guan: TradeBook.OfficialGuanThreshold);

        rig.Screen.Confirm();

        Assert.Equal("粮  ×11  单价 10 文", rig.Screen.Rows()[0].Label);
    }

    // ————————————————————————— --shop —————————————————————————

    [Fact]
    public void shop自检_五行说清两边差价与门槛()
    {
        var lines = ShopDemo.Run(RealTable("shops.csv"), RealCatalog(), RealTrust(), RealRoutes());

        Assert.Equal(5, lines.Count);

        // 件数照 shops.csv 数，不照票面写死：现在官市 6、黑市 6（共 12 行）
        Assert.Equal("[市] 官市 6 件 / 黑市 6 件", lines[0]);
        Assert.Equal("[市] 官市 粮 10 文 ×12 ｜ 黑市 粮 6 文 ×15", lines[1]);
        Assert.Equal("[市] 官市买粮：成功，钱 −10，囊 +1，官望 +1", lines[2]);
        Assert.Equal("[市] 黑市买粮：成功，钱 −6，囊 +1，疑心 +3", lines[3]);
        Assert.Equal("[市] 官望 0 时官市买：官市不卖给你（要路引，或官望 30）", lines[4]);
    }

    /// <summary>从测试输出目录往上找工程里的 Content/data（与其他 Game* 测试同一套找法）。</summary>
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

    private static ShopTable RealTable(string name) => ShopTable.Load(FindData(name));

    private static ItemCatalog RealCatalog() => ItemCatalog.Load(FindData("items.csv"));

    private static TrustTable RealTrust() => TrustTable.Load(FindData("trust.csv"));

    private static RouteTable RealRoutes() => RouteTable.Load(FindData("routes.csv"));
}
