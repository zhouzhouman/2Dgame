using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>
/// 一次买卖的结果。
///
/// 失败也是正常结果（钱不够、囊满、卖完了），所以不抛异常——UI 拿到
/// <see cref="Reason"/> 直接显示那一句人话就行，不必自己把错误码翻译成中文。
/// </summary>
/// <param name="Ok">成没成。没成时其余字段只有 <see cref="Reason"/> 有意义。</param>
/// <param name="Reason">没成时给玩家看的一句话；成了为 null。</param>
/// <param name="Item">哪件货。</param>
/// <param name="Shop">哪个市：official / black。</param>
/// <param name="Amount">成了的话：买 = 付了几文，卖 = 得了几文。没成为 0。</param>
/// <param name="TrustAction">
/// 成了的话：**该由调用方去调**哪条信任动作（<c>market_official</c> / <c>market_black</c>）。
/// 本类不碰信任——一次交易只算一次账，信任怎么加是 <see cref="TrustBook"/> 的事。
/// </param>
public readonly record struct TradeResult(
    bool Ok,
    string? Reason,
    string? Item,
    string? Shop,
    int Amount,
    string? TrustAction)
{
    /// <summary>一次失败的买卖：只带原因，什么都没动。</summary>
    /// <param name="reason">给玩家看的一句话。</param>
    /// <param name="item">哪件货。</param>
    /// <param name="shop">哪个市。</param>
    /// <returns>失败结果。</returns>
    public static TradeResult Fail(string reason, string? item = null, string? shop = null) =>
        new(false, reason, item, shop, 0, null);
}

/// <summary>
/// 买卖的规则层（设计文档 §5.2 官市 / 黑市）。
///
/// 三件事：货架上有没有、你配不配买、钱和东西怎么换。**只改背包**，不改信任：
/// 成功时把该调的动作 id 放在 <see cref="TradeResult.TrustAction"/> 里交回去，
/// 由调用方 <c>TrustBook.Apply</c>——这样"交易"和"信任"各归各的账，
/// 不会出现交易失败却涨了官望、或者一次买两件只加一次官望这种糊涂账。
///
/// 钱就是背包里的 <c>coin</c>（序章没有钱庄，也没有第二种货币）。
/// 存货是**这一趟**的：本类自己记一份可变的余量，同一个实例内递减；
/// 读档后回满也说得通——那意味着你换了一天再来。
/// </summary>
public sealed class TradeBook
{
    /// <summary>钱在背包里的物品 id。</summary>
    public const string MoneyItem = "coin";

    /// <summary>官市的门槛值：官望到了这个数就卖给你（设计文档 §5.2「官望 ≥30」）。</summary>
    public const int OfficialGuanThreshold = 30;

    /// <summary>路引的旗标名：有它就等于有官府许可，官望不够也能买。</summary>
    public const string RoadPassFlag = "road_pass";

    /// <summary>官市交易成功该调的信任动作（trust.csv 里有这一行：官望 +1）。</summary>
    public const string OfficialTrustAction = "market_official";

    /// <summary>黑市交易成功该调的信任动作（trust.csv 里有这一行：疑心 +3）。</summary>
    public const string BlackTrustAction = "market_black";

    /// <summary>官市把你拦在门外时的那句话。要路引，或者官望够。</summary>
    public const string OfficialRefusedReason = "官市不卖给你（要路引，或官望 30）";

    /// <summary>钱不够时的那句话。</summary>
    public const string NotEnoughMoneyReason = "钱不够";

    /// <summary>货卖光时的那句话。</summary>
    public const string SoldOutReason = "卖完了";

    /// <summary>这个市不卖这件东西时的那句话。</summary>
    public const string NotStockedReason = "这儿不卖这件";

    /// <summary>这个市不收这件东西时的那句话。</summary>
    public const string NotBoughtReason = "这儿不收这件";

    /// <summary>想卖钱本身时的那句话。</summary>
    public const string CannotSellMoneyReason = "铜钱不能卖";

    /// <summary>囊里没有这件东西时的那句话。</summary>
    public const string NothingToSellReason = "你身上没有这件";

    /// <summary>回收价的分母：卖价 = 买价 × <see cref="SellRatePercent"/> / 100，向下取整。</summary>
    public const int SellRatePercent = 60;

    private readonly ShopTable _table;
    private readonly Inventory _bag;
    private readonly ItemCatalog _catalog;
    private readonly TrustState _trust;
    private readonly FlagProbe? _flags;

    /// <summary>这一趟各货还剩几件，键是 <c>shop/item</c>，跟着本实例走。</summary>
    private readonly Dictionary<string, int> _stock;

    /// <summary>
    /// 建一本账。表、背包、物品表、信任三轴都是外面给的：数据来自表，状态来自存档。
    /// </summary>
    /// <param name="table">货架表。</param>
    /// <param name="bag">背包（就地改）。</param>
    /// <param name="catalog">物品表，决定买进来的东西怎么占格。</param>
    /// <param name="trust">本地区的三条轴，官市门槛只看其中的官望。</param>
    /// <param name="flags">旗标探针（判路引）；不传 = 没有任何旗标。</param>
    public TradeBook(ShopTable table, Inventory bag, ItemCatalog catalog, TrustState trust, FlagProbe? flags = null)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _bag = bag ?? throw new ArgumentNullException(nameof(bag));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _flags = flags;

        _stock = new Dictionary<string, int>(_table.Count, StringComparer.Ordinal);
        foreach (var row in _table.All) _stock[Key(row.Shop, row.Item)] = row.Stock;
    }

    /// <summary>
    /// 官市现在卖不卖给你：官望 ≥30，或者身上有路引（二者其一）。
    /// 黑市没有门槛，别拿这个去问黑市。
    /// </summary>
    /// <returns>能买返回 true。</returns>
    public bool OfficialOpen =>
        _trust.Guan >= OfficialGuanThreshold || HasRoadPass;

    /// <summary>身上有没有路引。</summary>
    private bool HasRoadPass => _flags != null && _flags(RoadPassFlag);

    /// <summary>
    /// 买一件。成功 = 扣钱、货进囊、这一趟的存货 −1。
    ///
    /// 判定的先后是有讲究的：**门槛 → 存货 → 钱 → 囊**。
    /// 官市不卖给你的时候不该告诉你"卖完了"——那会让你以为再等等就有货。
    /// 囊满时**一分钱都不扣**（先放货、后扣钱），宁可让你白跑一趟也不吞钱。
    /// </summary>
    /// <param name="shop">official 或 black。</param>
    /// <param name="itemId">物品 id。</param>
    /// <returns>买卖结果；成功时 <see cref="TradeResult.TrustAction"/> 非空，由调用方去调。</returns>
    public TradeResult Buy(string shop, string itemId)
    {
        if (!_table.TryGet(shop, itemId, out var row))
            return TradeResult.Fail(NotStockedReason, itemId, shop);

        if (IsOfficial(shop) && !OfficialOpen)
            return TradeResult.Fail(OfficialRefusedReason, itemId, shop);

        if (StockOf(shop, itemId) <= 0)
            return TradeResult.Fail(SoldOutReason, itemId, shop);

        if (_bag.Count(MoneyItem) < row.Price)
            return TradeResult.Fail(NotEnoughMoneyReason, itemId, shop);

        // 先放货：放不进去就整笔作废，钱一动不动。
        if (!_bag.TryAdd(itemId, 1, _catalog, out var error))
            return TradeResult.Fail(error ?? Inventory.FullMessage, itemId, shop);

        _bag.Remove(MoneyItem, row.Price);
        _stock[Key(shop, itemId)] -= 1;

        return new TradeResult(true, null, itemId, shop, row.Price, TrustActionOf(shop));
    }

    /// <summary>
    /// 卖一件。回收价 = 表里买价 × 60% 向下取整，**至少 1 文**——
    /// 一文不值的东西不该让你白送，但也不能倒找钱。
    ///
    /// 只能卖这个市货架上的东西（表里得有价，没有价就没得算）；
    /// 卖 <c>coin</c> 本身一律拒绝，否则可以用"卖钱"把一文变两文。
    /// </summary>
    /// <param name="shop">official 或 black。</param>
    /// <param name="itemId">物品 id。</param>
    /// <returns>买卖结果；成功时 <see cref="TradeResult.TrustAction"/> 非空，由调用方去调。</returns>
    public TradeResult Sell(string shop, string itemId)
    {
        if (string.Equals(itemId, MoneyItem, StringComparison.Ordinal))
            return TradeResult.Fail(CannotSellMoneyReason, itemId, shop);

        if (!_table.TryGet(shop, itemId, out var row))
            return TradeResult.Fail(NotBoughtReason, itemId, shop);

        if (_bag.Count(itemId) < 1)
            return TradeResult.Fail(NothingToSellReason, itemId, shop);

        var gain = SellPrice(row.Price);

        // 先交货、再收钱：钱收不进来（囊满且一枚铜钱都没有）就把货放回去，等于没卖。
        if (_bag.Remove(itemId, 1) < 1)
            return TradeResult.Fail(NothingToSellReason, itemId, shop);

        if (!_bag.TryAdd(MoneyItem, gain, _catalog, out var error))
        {
            _bag.TryAdd(itemId, 1, _catalog, out _); // 刚腾出来的位置，放回去一定成功
            return TradeResult.Fail(error ?? Inventory.FullMessage, itemId, shop);
        }

        return new TradeResult(true, null, itemId, shop, gain, TrustActionOf(shop));
    }

    /// <summary>
    /// 这个市这一趟还剩几件。不在货架上的东西返回 0。
    /// </summary>
    /// <param name="shop">official 或 black。</param>
    /// <param name="itemId">物品 id。</param>
    /// <returns>余量。</returns>
    public int StockOf(string shop, string itemId) =>
        _stock.TryGetValue(Key(shop, itemId), out var n) ? n : 0;

    /// <summary>某个市的整排余量，按货架表的顺序，给 UI 画"剩 3 件"。</summary>
    /// <param name="shop">official 或 black。</param>
    /// <returns>(货, 余量) 列表。</returns>
    public IReadOnlyList<(ShopItem Row, int Stock)> ShelfOf(string shop)
    {
        var shelf = _table.ForShop(shop);
        var list = new List<(ShopItem, int)>(shelf.Count);
        foreach (var row in shelf) list.Add((row, StockOf(row.Shop, row.Item)));

        return list;
    }

    /// <summary>卖价：买价 × 60% 向下取整，至少 1 文。</summary>
    /// <param name="price">表里的买价，正整数。</param>
    /// <returns>回收价，≥1。</returns>
    public static int SellPrice(int price) => Math.Max(1, price * SellRatePercent / 100);

    /// <summary>这个市成交后该调的信任动作；未知的市返回 null（不猜）。</summary>
    /// <param name="shop">official 或 black。</param>
    /// <returns>trust.csv 里的动作 id。</returns>
    public static string? TrustActionOf(string? shop)
    {
        if (shop == ShopTable.Official) return OfficialTrustAction;
        if (shop == ShopTable.Black) return BlackTrustAction;

        return null;
    }

    /// <summary>官市判断统一走这里，免得各处比字符串时漏掉大小写或不一致。</summary>
    private static bool IsOfficial(string shop) => shop == ShopTable.Official;

    /// <summary>存货表的键：一个市里物品 id 唯一，拼起来就够。</summary>
    private static string Key(string shop, string item) => shop + "/" + item;
}
