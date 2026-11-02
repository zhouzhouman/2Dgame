using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 货架上的一件货：<c>Content/data/shops.csv</c> 的一行。
///
/// 价钱是**文**（1 贯 = 1000 文），已经含了官市 / 黑市的差价——
/// 设计文档 §5.2 说的是「官市 ×1.2、黑市 ×0.75」，那是 D3 定价时的事，
/// 规则层只认表里这个数，不再乘一遍（乘两遍就是双倍溢价，最难查的那类 bug）。
/// </summary>
/// <param name="Shop">哪个市：<see cref="ShopTable.Official"/> 或 <see cref="ShopTable.Black"/>。</param>
/// <param name="Item">物品 id，对上 <c>items.csv</c>。</param>
/// <param name="Price">单价，单位文，正整数。</param>
/// <param name="Stock">这一趟的存货，1–<see cref="MaxStock"/>。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record ShopItem(string Shop, string Item, int Price, int Stock, string Note)
{
    /// <summary>一件货的存货上限。超过这个数一定是把 2 写成 20 了。</summary>
    public const int MaxStock = 20;
}

/// <summary>
/// 货架表：<c>Content/data/shops.csv</c> 的内存形式，按文件顺序保存。
///
/// 这里只有「卖什么、多少钱、有几件」；能不能买、钱从哪儿扣，是 <see cref="TradeBook"/>。
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：缺列、空 shop / item、价钱不是正整数、
/// 存货越界、同店同物重复，全部带行号抛 <see cref="InvalidDataException"/>——
/// 一张读歪的货架表不会崩，只会让某件货永远买不到，比崩一次难查得多。
/// </summary>
public sealed class ShopTable
{
    /// <summary>官市（设计文档 §5.2 左列）。</summary>
    public const string Official = "official";

    /// <summary>黑市（设计文档 §5.2 右列）。</summary>
    public const string Black = "black";

    /// <summary>表里必须有的列。</summary>
    private static readonly string[] RequiredColumns = { "shop", "item", "price", "stock", "note" };

    private readonly List<ShopItem> _all;
    private readonly Dictionary<string, Dictionary<string, ShopItem>> _byShop;

    /// <summary>全部货，按文件顺序。</summary>
    public IReadOnlyList<ShopItem> All => _all;

    /// <summary>货的条数（两个市加起来）。</summary>
    public int Count => _all.Count;

    private ShopTable(List<ShopItem> all)
    {
        _all = all;
        _byShop = new Dictionary<string, Dictionary<string, ShopItem>>(2, StringComparer.Ordinal);
        foreach (var row in all)
        {
            if (!_byShop.TryGetValue(row.Shop, out var shelf))
                _byShop[row.Shop] = shelf = new Dictionary<string, ShopItem>(StringComparer.Ordinal);

            shelf[row.Item] = row;
        }
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">shops.csv 的路径。</param>
    /// <returns>货架表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 shop / item、价钱非正整数、存货越界、同店同物重复。</exception>
    public static ShopTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>货架表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 shop / item、价钱非正整数、存货越界、同店同物重复。</exception>
    public static ShopTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("货架表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"货架表第 1 行：缺少列「{name}」。");

        var list = new List<ShopItem>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var shop = Get("shop");
            if (string.IsNullOrEmpty(shop))
                throw new InvalidDataException($"货架表第 {line} 行：shop 为空。");

            if (shop != Official && shop != Black)
                throw new InvalidDataException(
                    $"货架表第 {line} 行：shop「{shop}」只能是 {Official} 或 {Black}。");

            var item = Get("item");
            if (string.IsNullOrEmpty(item))
                throw new InvalidDataException($"货架表第 {line} 行：item 为空。");

            if (!seen.Add(shop + "/" + item))
                throw new InvalidDataException($"货架表第 {line} 行：{shop} 的「{item}」重复。");

            list.Add(new ShopItem(
                shop,
                item,
                ParsePrice(Get("price"), line),
                ParseStock(Get("stock"), line),
                Get("note")));
        }

        return new ShopTable(list);
    }

    /// <summary>
    /// 某个市上的全部货，按文件顺序。未知 shop 返回空表，不抛——
    /// 数据缺一个市不该让界面崩，只该让货架空着。
    /// </summary>
    /// <param name="shop">official 或 black。</param>
    /// <returns>货架；未知 shop 为空。</returns>
    public IReadOnlyList<ShopItem> ForShop(string? shop)
    {
        var shelf = new List<ShopItem>();
        if (string.IsNullOrEmpty(shop)) return shelf;

        foreach (var row in _all)
            if (string.Equals(row.Shop, shop, StringComparison.Ordinal))
                shelf.Add(row);

        return shelf;
    }

    /// <summary>
    /// 按「哪个市 + 哪件货」取一行。找不到返回 false，不抛——
    /// 玩家点了一件这个市不卖的东西是正常的，不是数据错。
    /// </summary>
    /// <param name="shop">official 或 black。</param>
    /// <param name="item">物品 id。</param>
    /// <param name="row">取到的货；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? shop, string? item, [NotNullWhen(true)] out ShopItem? row)
    {
        row = null;
        if (string.IsNullOrEmpty(shop) || string.IsNullOrEmpty(item)) return false;

        return _byShop.TryGetValue(shop!, out var shelf) && shelf.TryGetValue(item!, out row);
    }

    /// <summary>解析价钱：必须是正整数（0 文的东西不能拿来卖）。</summary>
    private static int ParsePrice(string text, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"货架表第 {line} 行：price「{text}」不是整数。");

        if (value < 1)
            throw new InvalidDataException($"货架表第 {line} 行：price「{text}」必须是正整数。");

        return value;
    }

    /// <summary>解析存货：必须是 1–<see cref="ShopItem.MaxStock"/>。</summary>
    private static int ParseStock(string text, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"货架表第 {line} 行：stock「{text}」不是整数。");

        if (value < 1 || value > ShopItem.MaxStock)
            throw new InvalidDataException(
                $"货架表第 {line} 行：stock「{text}」超出 1–{ShopItem.MaxStock}。");

        return value;
    }
}
