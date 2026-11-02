using System;
using System.IO;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 官市与黑市（票号 S2-02b）。哨的是设计文档 §5.2 那张表：
/// 官市贵、要门槛、但交易给你官望；黑市便宜、谁都进得去、但记你一笔疑心。
///
/// 所以这里盯的从来不是"能不能买"这一件事，而是：
/// 门槛挡住谁、失败时钱有没有被吞、卖价对不对，以及**成功后该调哪条信任动作**。
/// TradeBook 自己不碰信任——这条边界要是破了，交易失败也会涨官望。
/// </summary>
public class TradeTests
{
    /// <summary>够用的最小物品表：coin 可叠，blade 不可叠（用来把囊塞满）。</summary>
    private static readonly ItemCatalog Catalog = ItemCatalog.FromCsv(
        "id,name,kind,stack,note\n" +
        "coin,铜钱,token,1,\n" +
        "rice,粟米,food,1,\n" +
        "iron,生铁,tool,1,\n" +
        "salt,盐,token,1,\n" +
        "blade,横刀,tool,0,\n");

    /// <summary>
    /// 最小货架表：两个市都卖米（黑市便宜），刀只在官市、铁只在黑市——
    /// 这样"这个市不卖这件"能用真实数据测，不用另造一张表。
    /// </summary>
    private const string MiniShops =
        "shop,item,price,stock,note\n" +
        "official,rice,10,5,官市的粮贵\n" +
        "official,blade,60,1,官市的刀；不可叠，用来测塞满的囊\n" +
        "black,rice,6,20,黑市的粮便宜\n" +
        "black,iron,15,3,\n" +
        "black,salt,1,9,一文钱的盐：测卖价下限\n";

    /// <summary>钱先放进囊里（序章的钱就是背包里的 coin）。</summary>
    private static void Give(Inventory bag, string item, int n) =>
        Assert.True(bag.TryAdd(item, n, Catalog, out var error), error);

    private static (TradeBook Book, Inventory Bag, TrustState Trust) Make(
        int guan = 0,
        bool roadPass = false,
        int coins = 50,
        int capacity = Inventory.DefaultCapacity,
        string shops = MiniShops)
    {
        var bag = new Inventory(capacity);
        if (coins > 0) Give(bag, TradeBook.MoneyItem, coins);

        var trust = new TrustState { Guan = guan };
        var book = new TradeBook(
            ShopTable.FromCsv(shops), bag, Catalog, trust,
            name => roadPass && name == TradeBook.RoadPassFlag);

        return (book, bag, trust);
    }

    // ————————————————————————— 买 —————————————————————————

    [Fact]
    public void 买成功时扣钱入囊且存货减一()
    {
        var (book, bag, _) = Make(guan: 30);

        var result = book.Buy(ShopTable.Official, "rice");

        Assert.True(result.Ok);
        Assert.Equal(10, result.Amount);
        Assert.Equal(40, bag.Count(TradeBook.MoneyItem));
        Assert.Equal(1, bag.Count("rice"));
        Assert.Equal(4, book.StockOf(ShopTable.Official, "rice"));
    }

    [Fact]
    public void 钱不够时什么都不会动()
    {
        var (book, bag, _) = Make(guan: 30, coins: 9);

        var result = book.Buy(ShopTable.Official, "rice");

        Assert.False(result.Ok);
        Assert.Equal(TradeBook.NotEnoughMoneyReason, result.Reason);
        Assert.Equal(9, bag.Count(TradeBook.MoneyItem));
        Assert.Equal(0, bag.Count("rice"));
        Assert.Equal(5, book.StockOf(ShopTable.Official, "rice"));
    }

    [Fact]
    public void 囊满时失败且一分钱都不扣()
    {
        // 11 把不可叠的刀 + 1 格铜钱 = 12 格，正好满。
        var (book, bag, _) = Make(guan: 30, coins: 99, capacity: 12);
        Give(bag, "blade", 11);

        Assert.True(bag.IsFull);

        var result = book.Buy(ShopTable.Official, "blade");

        Assert.False(result.Ok);
        Assert.Equal(Inventory.FullMessage, result.Reason);
        Assert.Contains("囊里满了", result.Reason);
        Assert.Equal(99, bag.Count(TradeBook.MoneyItem)); // 钱没少
        Assert.Equal(1, book.StockOf(ShopTable.Official, "blade")); // 货还在架上
    }

    [Fact]
    public void 存货买完之后报卖完了()
    {
        var (book, _, _) = Make(guan: 30, coins: 99);

        Assert.True(book.Buy(ShopTable.Official, "blade").Ok);

        var second = book.Buy(ShopTable.Official, "blade");

        Assert.False(second.Ok);
        Assert.Equal(TradeBook.SoldOutReason, second.Reason);
        Assert.Equal(0, book.StockOf(ShopTable.Official, "blade"));
    }

    [Fact]
    public void 同一个实例里买几次存货就少几件()
    {
        var (book, _, _) = Make(guan: 30, coins: 99);

        Assert.Equal(5, book.StockOf(ShopTable.Official, "rice"));
        book.Buy(ShopTable.Official, "rice");
        Assert.Equal(4, book.StockOf(ShopTable.Official, "rice"));
        book.Buy(ShopTable.Official, "rice");
        Assert.Equal(3, book.StockOf(ShopTable.Official, "rice"));
    }

    [Fact]
    public void 官望二十九时官市不卖()
    {
        var (book, bag, _) = Make(guan: 29);

        var result = book.Buy(ShopTable.Official, "rice");

        Assert.False(result.Ok);
        Assert.Equal(TradeBook.OfficialRefusedReason, result.Reason);
        Assert.Equal("官市不卖给你（要路引，或官望 30）", result.Reason);
        Assert.Equal(50, bag.Count(TradeBook.MoneyItem));
        Assert.False(book.OfficialOpen);
    }

    [Fact]
    public void 官望三十时官市就卖了()
    {
        var (book, _, _) = Make(guan: 30);

        Assert.True(book.OfficialOpen);
        Assert.True(book.Buy(ShopTable.Official, "rice").Ok);
    }

    [Fact]
    public void 有路引时官望为零也能买()
    {
        var (book, bag, _) = Make(guan: 0, roadPass: true);

        Assert.True(book.OfficialOpen);

        var result = book.Buy(ShopTable.Official, "rice");

        Assert.True(result.Ok);
        Assert.Equal(40, bag.Count(TradeBook.MoneyItem));
    }

    [Fact]
    public void 门槛先于卖完报出来()
    {
        // 同一张货架、同一件刀：官望够时买光，再把官望降到 0，该报"不卖给你"而不是"卖完了"。
        var (book, _, trust) = Make(guan: 30, coins: 99);
        Assert.True(book.Buy(ShopTable.Official, "blade").Ok);
        Assert.Equal(0, book.StockOf(ShopTable.Official, "blade"));

        trust.Guan = 0;
        var result = book.Buy(ShopTable.Official, "blade");

        Assert.Equal(TradeBook.OfficialRefusedReason, result.Reason);
    }

    [Fact]
    public void 黑市没有门槛()
    {
        var (book, bag, _) = Make(guan: 0, roadPass: false);

        var result = book.Buy(ShopTable.Black, "rice");

        Assert.True(result.Ok);
        Assert.Equal(44, bag.Count(TradeBook.MoneyItem));
        Assert.Equal(19, book.StockOf(ShopTable.Black, "rice"));
    }

    [Fact]
    public void 官市不卖的东西黑市卖()
    {
        var (book, _, _) = Make(guan: 30);

        Assert.Equal(TradeBook.NotStockedReason, book.Buy(ShopTable.Official, "iron").Reason);
        Assert.True(book.Buy(ShopTable.Black, "iron").Ok);
    }

    // ————————————————————————— 卖 —————————————————————————

    [Fact]
    public void 卖价是买价的六成()
    {
        var (book, bag, _) = Make(guan: 30);
        Give(bag, "rice", 2);

        var result = book.Sell(ShopTable.Official, "rice");

        Assert.True(result.Ok);
        Assert.Equal(6, result.Amount); // 10 × 0.6
        Assert.Equal(56, bag.Count(TradeBook.MoneyItem));
        Assert.Equal(1, bag.Count("rice"));
    }

    [Fact]
    public void 卖价向下取整()
    {
        var (book, bag, _) = Make(guan: 30);
        Give(bag, "iron", 1);

        var result = book.Sell(ShopTable.Black, "iron");

        Assert.Equal(9, result.Amount); // 15 × 0.6 = 9
        Assert.Equal(59, bag.Count(TradeBook.MoneyItem));
    }

    [Fact]
    public void 卖价至少一文()
    {
        var (book, bag, _) = Make(guan: 30);
        Give(bag, "salt", 1);

        var result = book.Sell(ShopTable.Black, "salt");

        Assert.Equal(1, result.Amount); // 1 × 0.6 = 0.6 → 至少 1
        Assert.Equal(51, bag.Count(TradeBook.MoneyItem));
        Assert.Equal(1, TradeBook.SellPrice(1));
        Assert.Equal(1, TradeBook.SellPrice(3)); // 1.8 → 1
        Assert.Equal(2, TradeBook.SellPrice(4)); // 2.4 → 2
    }

    [Fact]
    public void 铜钱不能卖()
    {
        var (book, bag, _) = Make(guan: 30);

        var result = book.Sell(ShopTable.Black, TradeBook.MoneyItem);

        Assert.False(result.Ok);
        Assert.Equal(TradeBook.CannotSellMoneyReason, result.Reason);
        Assert.Equal(50, bag.Count(TradeBook.MoneyItem));
    }

    [Fact]
    public void 身上没有的东西卖不了()
    {
        var (book, bag, _) = Make(guan: 30);

        var result = book.Sell(ShopTable.Official, "rice");

        Assert.False(result.Ok);
        Assert.Equal(TradeBook.NothingToSellReason, result.Reason);
        Assert.Equal(50, bag.Count(TradeBook.MoneyItem));
    }

    [Fact]
    public void 这个市不收的东西卖不了()
    {
        var (book, bag, _) = Make(guan: 30);
        Give(bag, "iron", 1);

        var result = book.Sell(ShopTable.Official, "iron");

        Assert.False(result.Ok);
        Assert.Equal(TradeBook.NotBoughtReason, result.Reason);
        Assert.Equal(1, bag.Count("iron")); // 货没被吞
    }

    // ————————————————————————— 信任动作 —————————————————————————

    [Fact]
    public void 官市成交返回官市那条信任动作()
    {
        var (book, _, _) = Make(guan: 30);

        var buy = book.Buy(ShopTable.Official, "rice");

        Assert.Equal(TradeBook.OfficialTrustAction, buy.TrustAction);
        Assert.Equal("market_official", buy.TrustAction);
    }

    [Fact]
    public void 黑市成交返回黑市那条信任动作()
    {
        var (book, bag, _) = Make(guan: 0);
        Give(bag, "iron", 1);

        var buy = book.Buy(ShopTable.Black, "rice");
        var sell = book.Sell(ShopTable.Black, "iron");

        Assert.Equal(TradeBook.BlackTrustAction, buy.TrustAction);
        Assert.Equal("market_black", sell.TrustAction);
    }

    [Fact]
    public void 失败时不返回信任动作()
    {
        var (book, bag, _) = Make(guan: 0);

        Assert.Null(book.Buy(ShopTable.Official, "rice").TrustAction);
        Assert.Null(book.Sell(ShopTable.Black, "rice").TrustAction);
    }

    [Fact]
    public void TradeBook自己不碰信任()
    {
        var (book, _, trust) = Make(guan: 30);

        book.Buy(ShopTable.Official, "rice");

        // 官望还是买之前的 30：+1 得由调用方调 TrustBook.Apply 才会有。
        Assert.Equal(30, trust.Guan);
        Assert.Equal(0, trust.Min);
        Assert.Equal(0, trust.Yi);
    }

    // ————————————————————————— 货架表 —————————————————————————

    [Fact]
    public void 货架按市分开且保持文件顺序()
    {
        var table = ShopTable.FromCsv(MiniShops);

        Assert.Equal(5, table.Count);
        Assert.Equal(2, table.ForShop(ShopTable.Official).Count);
        Assert.Equal(3, table.ForShop(ShopTable.Black).Count);
        Assert.Equal("rice", table.ForShop(ShopTable.Official)[0].Item);
        Assert.Equal("blade", table.ForShop(ShopTable.Official)[1].Item);
        Assert.Empty(table.ForShop("nohows"));
        Assert.Empty(table.ForShop(""));
    }

    [Fact]
    public void 缺列时带行号抛错()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv("shop,item,price\n\"official\",\"rice\",\"10\"\n"));

        Assert.Contains("第 1 行", ex.Message);
        Assert.Contains("stock", ex.Message);
    }

    [Fact]
    public void 空item或空shop都抛错()
    {
        var emptyItem = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "official,,10,3,\n"));
        Assert.Contains("第 7 行", emptyItem.Message);
        Assert.Contains("item", emptyItem.Message);

        var emptyShop = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + ",rice,10,3,\n"));
        Assert.Contains("第 7 行", emptyShop.Message);
        Assert.Contains("shop", emptyShop.Message);
    }

    [Fact]
    public void 价钱不是正整数时抛错()
    {
        var zero = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "official,salt,0,3,\n"));
        Assert.Contains("第 7 行", zero.Message);
        Assert.Contains("price", zero.Message);

        var negative = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "official,salt,-5,3,\n"));
        Assert.Contains("price", negative.Message);

        var notInt = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "official,salt,十文,3,\n"));
        Assert.Contains("price", notInt.Message);
    }

    [Fact]
    public void 存货越界时抛错()
    {
        var zero = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "official,salt,10,0,\n"));
        Assert.Contains("第 7 行", zero.Message);
        Assert.Contains("stock", zero.Message);

        var tooMany = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "official,salt,10,21,\n"));
        Assert.Contains("stock", tooMany.Message);
    }

    [Fact]
    public void 不认识的市和不重复的同店同物都抛错()
    {
        var badShop = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "grey,rice,10,3,\n"));
        Assert.Contains("shop", badShop.Message);
        Assert.Contains("grey", badShop.Message);

        var dup = Assert.Throws<InvalidDataException>(() =>
            ShopTable.FromCsv(MiniShops + "official,rice,12,3,\n"));
        Assert.Contains("第 7 行", dup.Message);
        Assert.Contains("重复", dup.Message);
    }

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 GiftTests 同一套找法）。</summary>
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

    [Fact]
    public void 真表能读出两边的货架且黑市不比官市贵()
    {
        var path = FindData("shops.csv");
        if (path == null) return; // 数据还没生成：这条跳过，不算红。

        var table = ShopTable.Load(path);
        var official = table.ForShop(ShopTable.Official);
        var black = table.ForShop(ShopTable.Black);

        Assert.True(official.Count >= 1, "官市货架是空的");
        Assert.True(black.Count >= 1, "黑市货架是空的");
        Assert.All(table.All, r => Assert.True(r.Price >= 1));
        Assert.All(table.All, r => Assert.InRange(r.Stock, 1, ShopItem.MaxStock));

        var itemsPath = FindData("items.csv");
        if (itemsPath != null)
        {
            var catalog = ItemCatalog.Load(itemsPath);
            Assert.All(table.All, r => Assert.NotNull(catalog.TryGet(r.Item)));
        }

        // §5.2：官市贵两成、黑市便宜两成半——同一个东西黑市不该更贵。
        foreach (var cheap in black)
            foreach (var dear in official)
                if (cheap.Item == dear.Item)
                    Assert.True(cheap.Price <= dear.Price,
                        $"「{cheap.Item}」黑市 {cheap.Price} 文比官市 {dear.Price} 文还贵");
    }
}
