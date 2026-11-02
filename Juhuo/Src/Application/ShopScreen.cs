using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 买卖的入口（票号 S2-02c）：哪个 NPC 守哪个市。
///
/// <c>npcs.csv</c> 还没有 id 列，所以按显示名认人——官市的布商（hub4 14,5）、
/// 黑市的掮客（hub4 25,18）。名字对不上就是"这儿没人做买卖"，不猜。
/// </summary>
public static class ShopWiring
{
    /// <summary>官市那位商人（<c>npcs.csv</c> 的显示名）。</summary>
    public const string OfficialMerchant = "布商";

    /// <summary>黑市那位商人（<c>npcs.csv</c> 的显示名）。</summary>
    public const string BlackMerchant = "掮客";

    /// <summary>旁边没商人时给玩家的一句话。</summary>
    public const string NoMerchantText = "这儿没人做买卖";

    /// <summary>货架上按行话称「粮」的那件货（<c>items.csv</c> 里 id=grain 叫「谷子」）。</summary>
    public const string GrainItem = "grain";

    /// <summary>「粮」在柜台上的写法。</summary>
    public const string GrainTradeName = "粮";

    /// <summary>
    /// 这个 NPC 守哪个市；不是商人返回 null。
    /// </summary>
    /// <param name="npcName">显示名。</param>
    /// <returns>official / black / null。</returns>
    public static string? ShopOf(string? npcName)
    {
        if (string.Equals(npcName, OfficialMerchant, StringComparison.Ordinal)) return ShopTable.Official;
        if (string.Equals(npcName, BlackMerchant, StringComparison.Ordinal)) return ShopTable.Black;

        return null;
    }

    /// <summary>某个市守摊的那位（给面板抬头用）。</summary>
    /// <param name="shop">official 或 black。</param>
    /// <returns>显示名。</returns>
    public static string MerchantOf(string shop) =>
        shop == ShopTable.Black ? BlackMerchant : OfficialMerchant;

    /// <summary>某个市的人话：官市 / 黑市。</summary>
    /// <param name="shop">official 或 black。</param>
    /// <returns>官市或黑市。</returns>
    public static string NameOf(string shop) =>
        shop == ShopTable.Black ? "黑市" : "官市";

    /// <summary>
    /// 柜台上的货名。粮按行话叫「粮」——<c>items.csv</c> 里叫「谷子」，
    /// 那是囊里的正式名；柜台上说"一升粮"才像买卖。其余一律用物品表的名字。
    /// </summary>
    /// <param name="catalog">物品表。</param>
    /// <param name="itemId">物品 id。</param>
    /// <returns>显示名。</returns>
    public static string TradeName(ItemCatalog? catalog, string itemId) =>
        string.Equals(itemId, GrainItem, StringComparison.Ordinal)
            ? GrainTradeName
            : BagPickup.NameOf(catalog, itemId);
}

/// <summary>面板上下两块：买（货架）与卖（囊）。</summary>
public enum ShopMode
{
    /// <summary>看货架，按买价付钱。</summary>
    Buy,

    /// <summary>看囊，按六成回收价换钱。</summary>
    Sell,
}

/// <summary>
/// 面板上的一行：<c>粮  ×12  单价 10 文</c>。
/// </summary>
/// <param name="Item">物品 id（成交时交给 TradeBook 的就是它）。</param>
/// <param name="Label">整行文字。</param>
/// <param name="Price">单价：买价，或卖价（买价 ×60%）。</param>
/// <param name="Count">这一趟还剩几件 / 囊里有几件。</param>
/// <param name="Sellable">这个市收不收这件（只有卖那一块用得上）。</param>
public readonly record struct ShopRow(string Item, string Label, int Price, int Count, bool Sellable);

/// <summary>
/// 交易面板（票号 S2-02c）：只算"选到哪一行、成交后说什么"，不知道颜色和字——
/// 画由 Main 画，规则由 <see cref="TradeBook"/> 判，信任由 <see cref="TrustBook"/> 记。
///
/// 这里**不自己写失败文案**：失败就说 <see cref="TradeResult.Reason"/>，
/// 与 Core 的常量是同一句。成功才需要多一句"官望 +1"，那句也从
/// <see cref="TrustBook.Explain"/> 来，不另抄一份。
/// </summary>
public sealed class ShopScreen
{
    private readonly ShopTable _table;
    private readonly TradeBook _book;
    private readonly Inventory _bag;
    private readonly ItemCatalog? _catalog;
    private readonly TrustBook _trust;

    /// <summary>
    /// 建一张面板。
    /// </summary>
    /// <param name="table">货架表（卖那一块要查这个市收不收）。</param>
    /// <param name="book">这一趟的买卖账（存货跟着它走）。</param>
    /// <param name="bag">玩家的囊（钱也在里头）。</param>
    /// <param name="catalog">物品表，用来取显示名。</param>
    /// <param name="trust">三条轴；官市门槛与成交后的提示都看它。</param>
    /// <param name="shop">official 或 black。</param>
    public ShopScreen(
        ShopTable table,
        TradeBook book,
        Inventory bag,
        ItemCatalog? catalog,
        TrustBook trust,
        string shop)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _bag = bag ?? throw new ArgumentNullException(nameof(bag));
        _catalog = catalog;
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));

        Shop = shop == ShopTable.Black ? ShopTable.Black : ShopTable.Official;
    }

    /// <summary>这个市：official / black。</summary>
    public string Shop { get; }

    /// <summary>守摊的人（布商 / 掮客）。</summary>
    public string Merchant => ShopWiring.MerchantOf(Shop);

    /// <summary>现在在看买还是卖。</summary>
    public ShopMode Mode { get; private set; } = ShopMode.Buy;

    /// <summary>现在选在第几行（0 起）。没有行时恒为 0。</summary>
    public int Selected { get; private set; }

    /// <summary>面板底部那一句：上一次成交的结果，或 Core 给的失败原因。</summary>
    public string Message { get; private set; } = "选一样，按 Enter 成交。";

    /// <summary>成交后该亮的信任提示（<c>官望 +1</c> / <c>疑心 +3</c>）；没成交是空串。</summary>
    public string LastToast { get; private set; } = "";

    /// <summary>最近一次 <see cref="Confirm"/> 的结果；还没成交过为 null。</summary>
    public TradeResult? LastResult { get; private set; }

    /// <summary>最近一次成交落在三轴上的增量。</summary>
    public TrustDelta LastDelta { get; private set; } = TrustDelta.None;

    /// <summary>囊里现在有多少铜钱。</summary>
    public int Money => _bag.Count(TradeBook.MoneyItem);

    /// <summary>抬头右边那半截：<c>囊中铜钱 14</c>。</summary>
    public string MoneyLine => $"囊中铜钱 {Money}";

    /// <summary>抬头：<c>布商　·　买</c>。</summary>
    public string HeadLine => $"{Merchant}　·　{(Mode == ShopMode.Buy ? "买" : "卖")}";

    /// <summary>
    /// 门槛那一行。官市两态分得开：
    /// <c>官市：官望 34，够</c> / <c>官市：要路引，或官望 30（你 12）</c>。
    /// 黑市没有门槛，但要说清它记你一笔疑心。
    /// </summary>
    public string ThresholdLine
    {
        get
        {
            if (Shop != ShopTable.Official) return "黑市：不问出身，只记疑心";

            return _book.OfficialOpen
                ? $"官市：官望 {_trust.State.Guan}，够"
                : $"官市：要路引，或官望 {TradeBook.OfficialGuanThreshold}（你 {_trust.State.Guan}）";
        }
    }

    /// <summary>底下那行操作提示。</summary>
    public string FootLine => "↑↓ 选行　·　Enter 买卖　·　Tab 切买 / 卖　·　Esc / T 关上";

    /// <summary>
    /// 现在这一块有哪些行：
    /// 买 = 货架上每一件（余量跟着 <see cref="TradeBook"/> 走）；
    /// 卖 = 囊里除了铜钱以外的每一件，价是这个市给的六成回收价。
    /// 这个市不收的东西也列出来，标"这儿不收"——藏起来会让玩家以为东西丢了。
    /// </summary>
    /// <returns>按显示顺序排好的行。</returns>
    public IReadOnlyList<ShopRow> Rows()
    {
        var rows = new List<ShopRow>(_table.ForShop(Shop).Count);

        if (Mode == ShopMode.Buy)
        {
            foreach (var (row, stock) in _book.ShelfOf(Shop))
            {
                var name = ShopWiring.TradeName(_catalog, row.Item);
                rows.Add(new ShopRow(
                    row.Item,
                    $"{name}  ×{stock}  单价 {row.Price} 文",
                    row.Price,
                    stock,
                    stock > 0));
            }

            return rows;
        }

        foreach (var slot in _bag.Items)
        {
            // 铜钱不能卖：列出来只会让人手贱按一下，然后被一句"铜钱不能卖"噎住。
            if (string.Equals(slot.Item, TradeBook.MoneyItem, StringComparison.Ordinal)) continue;

            var name = ShopWiring.TradeName(_catalog, slot.Item);

            if (_table.TryGet(Shop, slot.Item, out var row))
            {
                var price = TradeBook.SellPrice(row.Price);
                rows.Add(new ShopRow(slot.Item, $"{name}  ×{slot.N}  单价 {price} 文", price, slot.N, true));
            }
            else
            {
                rows.Add(new ShopRow(slot.Item, $"{name}  ×{slot.N}  这儿不收", 0, slot.N, false));
            }
        }

        return rows;
    }

    /// <summary>上下挪一行，到头就停住（不绕圈）。</summary>
    /// <param name="delta">-1 或 +1。</param>
    public void Move(int delta)
    {
        var n = Rows().Count;
        if (n == 0)
        {
            Selected = 0;
            return;
        }

        Selected = Math.Clamp(Selected + delta, 0, n - 1);
    }

    /// <summary>直接选某一行。</summary>
    /// <param name="index">0 起。</param>
    public void Select(int index)
    {
        var n = Rows().Count;
        Selected = n == 0 ? 0 : Math.Clamp(index, 0, n - 1);
    }

    /// <summary>Tab：买 ↔ 卖。换一块就从第一行起。</summary>
    public void ToggleMode()
    {
        Mode = Mode == ShopMode.Buy ? ShopMode.Sell : ShopMode.Buy;
        Selected = 0;
        Message = Mode == ShopMode.Buy ? "选一样，按 Enter 买下。" : "选一样，按 Enter 卖掉。";
        LastToast = "";
    }

    /// <summary>
    /// Enter：买 / 卖一件。
    ///
    /// 失败照 Core 的话说（钱不够、卖完了、官市不卖给你……）；
    /// 成功才动信任——<see cref="TradeResult.TrustAction"/> 是 Core 交回来的，
    /// 这里只负责把它加到 <see cref="TrustBook"/> 上，并把变化说成一句人话。
    /// </summary>
    /// <returns>这一次的结果；这一块没有行时返回 null（什么也没发生）。</returns>
    public TradeResult? Confirm()
    {
        var rows = Rows();
        if (rows.Count == 0)
        {
            Message = Mode == ShopMode.Buy ? "货架空着" : "囊里没有能卖的东西";
            return null;
        }

        Selected = Math.Clamp(Selected, 0, rows.Count - 1);
        var row = rows[Selected];
        var name = ShopWiring.TradeName(_catalog, row.Item);

        var result = Mode == ShopMode.Buy
            ? _book.Buy(Shop, row.Item)
            : _book.Sell(Shop, row.Item);

        LastResult = result;

        if (!result.Ok)
        {
            Message = result.Reason ?? "";
            LastToast = "";
            return result;
        }

        LastDelta = _trust.Apply(result.TrustAction);
        LastToast = _trust.Explain(LastDelta);
        Message = Mode == ShopMode.Buy
            ? $"买下 {name} ×1，付 {result.Amount} 文"
            : $"卖掉 {name} ×1，得 {result.Amount} 文";

        // 卖掉最后一件之后这一行就没了：选中别停在一个不存在的位置上。
        var left = Rows().Count;
        Selected = Math.Clamp(Selected, 0, Math.Max(0, left - 1));

        return result;
    }
}

/// <summary>
/// <c>--shop</c> 的输出。格式钉在这里，命令行与测试读的是同一份。
/// </summary>
public static class ShopReport
{
    /// <summary>所有行共用的抬头。</summary>
    public const string Prefix = "[市]";

    /// <summary>两个市各有几件货：<c>[市] 官市 7 件 / 黑市 5 件</c>。</summary>
    /// <param name="table">货架表。</param>
    /// <returns>一行。</returns>
    public static string CountLine(ShopTable table) =>
        $"{Prefix} 官市 {table.ForShop(ShopTable.Official).Count} 件 / 黑市 {table.ForShop(ShopTable.Black).Count} 件";

    /// <summary>
    /// 同一件货在两边的价与存货：<c>[市] 官市 粮 10 文 ×12 ｜ 黑市 粮 6 文 ×15</c>。
    /// 有一边没上架就照实说"没有"，不编一个价。
    /// </summary>
    /// <param name="table">货架表。</param>
    /// <param name="catalog">物品表。</param>
    /// <param name="itemId">要比的那件货。</param>
    /// <returns>一行。</returns>
    public static string PriceLine(ShopTable table, ItemCatalog? catalog, string itemId)
    {
        var official = First(table.ForShop(ShopTable.Official), itemId);
        var black = First(table.ForShop(ShopTable.Black), itemId);

        if (official is null || black is null)
            return $"{Prefix} 官市 / 黑市没有同一件货：{itemId}";

        var name = ShopWiring.TradeName(catalog, itemId);

        return $"{Prefix} 官市 {name} {official.Price} 文 ×{official.Stock} ｜ 黑市 {name} {black.Price} 文 ×{black.Stock}";
    }

    /// <summary>
    /// 成交了一笔：<c>[市] 官市买粮：成功，钱 −10，囊 +1，官望 +1</c>。
    /// </summary>
    /// <param name="shop">official / black。</param>
    /// <param name="itemName">柜台上的货名。</param>
    /// <param name="result">成交结果（只该在成功时调）。</param>
    /// <param name="gained">囊里多了几件。</param>
    /// <param name="trustText">三轴变化那一句。</param>
    /// <returns>一行。</returns>
    public static string BoughtLine(string shop, string itemName, TradeResult result, int gained, string trustText) =>
        $"{Prefix} {ShopWiring.NameOf(shop)}买{itemName}：成功，钱 −{result.Amount}，囊 +{gained}，{trustText}";

    /// <summary>没成交：<c>[市] 官望 0 时官市买：官市不卖给你（要路引，或官望 30）</c>。</summary>
    /// <param name="guan">当时官望。</param>
    /// <param name="shop">official / black。</param>
    /// <param name="result">失败结果。</param>
    /// <returns>一行。</returns>
    public static string RefusedLine(int guan, string shop, TradeResult result) =>
        $"{Prefix} 官望 {guan} 时{ShopWiring.NameOf(shop)}买：{result.Reason}";

    /// <summary>按 id 在一条货架上找。</summary>
    private static ShopItem? First(IReadOnlyList<ShopItem> shelf, string itemId)
    {
        foreach (var row in shelf)
            if (string.Equals(row.Item, itemId, StringComparison.Ordinal))
                return row;

        return null;
    }
}

/// <summary>
/// <c>--shop</c> 的那一趟：两边货架、两边各买一升粮、再用全新状态撞一次门槛。
///
/// 结论要一眼看得见：**同一个东西，官市贵 4 文但涨官望，黑市便宜 4 文但涨疑心**。
/// 只动内存里自己造的那一份囊与三轴，不碰玩家的档。
/// </summary>
public static class ShopDemo
{
    /// <summary>拿来做对照的那件货：两边都有，价不一样。</summary>
    public const string Item = ShopWiring.GrainItem;

    /// <summary>起手给多少文——够买两升粮，也够看出钱的进出。</summary>
    public const int Coins = 100;

    /// <summary>
    /// 跑一趟，把该打印的行按顺序交回来。
    /// </summary>
    /// <param name="table">货架表。</param>
    /// <param name="catalog">物品表。</param>
    /// <param name="trustTable">信任动作表（涨官望 / 涨疑心都在这上面）。</param>
    /// <param name="routes">路线表（TrustBook 要用，这一趟不看）。</param>
    /// <returns>按打印顺序排好的行。</returns>
    public static IReadOnlyList<string> Run(
        ShopTable table, ItemCatalog catalog, TrustTable trustTable, RouteTable routes)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        var lines = new List<string>(5)
        {
            ShopReport.CountLine(table),
            ShopReport.PriceLine(table, catalog, Item),
        };

        // 一囊一百文，两个市都从这一份里进出：黑市那一笔要能看见官望没再涨。
        // 官望先摆到门槛上（30）——不然第一笔就被门槛挡住，演不出"贵 4 文换 +1 官望"。
        var bag = new Inventory();
        bag.TryAdd(TradeBook.MoneyItem, Coins, catalog, out _);

        var state = new TrustState { Guan = TradeBook.OfficialGuanThreshold };
        var trust = new TrustBook(state, trustTable, routes);
        var trade = new TradeBook(table, bag, catalog, state);

        lines.Add(BuyLine(catalog, bag, trade, trust, ShopTable.Official));
        lines.Add(BuyLine(catalog, bag, trade, trust, ShopTable.Black));

        // 全新一局、官望 0：门槛必须真的挡人，不能只是文案上写着"要 30"。
        var freshBag = new Inventory();
        freshBag.TryAdd(TradeBook.MoneyItem, Coins, catalog, out _);

        var freshState = new TrustState();
        var freshTrust = new TrustBook(freshState, trustTable, routes);
        var freshTrade = new TradeBook(table, freshBag, catalog, freshState);

        var refused = freshTrade.Buy(ShopTable.Official, Item);
        lines.Add(ShopReport.RefusedLine(freshTrust.State.Guan, ShopTable.Official, refused));

        return lines;
    }

    /// <summary>买一升，拼一行；没成也照实写，不假装成功。</summary>
    private static string BuyLine(
        ItemCatalog catalog, Inventory bag, TradeBook trade, TrustBook trust, string shop)
    {
        var name = ShopWiring.TradeName(catalog, Item);
        var before = bag.Count(Item);

        var result = trade.Buy(shop, Item);
        if (!result.Ok) return $"{ShopReport.Prefix} {ShopWiring.NameOf(shop)}买{name}：没成，{result.Reason}";

        var delta = trust.Apply(result.TrustAction);
        return ShopReport.BoughtLine(shop, name, result, bag.Count(Item) - before, trust.Explain(delta));
    }
}
