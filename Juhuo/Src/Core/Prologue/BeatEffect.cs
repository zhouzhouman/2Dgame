using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Juhuo.Core;

/// <summary>
/// 一个原子效果的种类。<c>beats.csv</c> 的 <c>effects</c> 列里只认这七种，
/// 多一种都得先改这里——免得表里写了个没人认识的东西还被静默吞掉。
/// </summary>
public enum EffectKind
{
    /// <summary>钱（<see cref="TradeBook.MoneyItem"/>）增减，单位文。</summary>
    Coin,

    /// <summary>加物品，<see cref="Effect.Target"/> 是 items.csv 的 id。</summary>
    Item,

    /// <summary>官望增减。</summary>
    Guan,

    /// <summary>民望增减。</summary>
    Min,

    /// <summary>疑心增减。</summary>
    Yi,

    /// <summary>兵力增减。</summary>
    Troops,

    /// <summary>置旗标，<see cref="Effect.Target"/> 是旗标名。</summary>
    Flag,
}

/// <summary>
/// 一个原子效果：<c>coin:+15</c> / <c>item:grain=3</c> / <c>min:-15</c> / <c>flag:raided</c>。
/// 只描述"什么、多少、对谁"，怎么落账在 <see cref="BeatEffectBook"/>。
/// </summary>
/// <param name="Kind">种类。</param>
/// <param name="Amount">
/// 增量。钱、三轴、兵力是带符号的增量；物品是件数（正数）；
/// 旗标没有数值，为 0。
/// </param>
/// <param name="Target">物品 id 或旗标名；钱与三轴、兵力为 null。</param>
public readonly record struct Effect(EffectKind Kind, int Amount, string? Target);

/// <summary>
/// <c>beats.csv</c> 的 <c>effects</c> 列解析器。
///
/// 列的写法：**每个选项一组，组之间 <c>|</c>，组内原子之间 <c>;</c>，顺序与 options 一一对应**：
/// <code>coin:+15;item:grain=3|min:-15;yi:+1;flag:raided</code>
///
/// 坏数据当场炸（带行号）：未知原子、缺值、非法数字、item id 不在物品表、
/// 组数与选项数对不上。静默吞掉一条效果，玩家只会觉得"选了没反应"，那种 bug 最难查。
/// 整格留空是例外：表示这一拍的每个选项都没有效果（数据还没填完时也读得动）。
/// 纯 C#，不碰引擎。
/// </summary>
public static class BeatEffect
{
    /// <summary>组与组之间的分隔符。</summary>
    public const char GroupSeparator = '|';

    /// <summary>组内原子之间的分隔符。</summary>
    public const char AtomSeparator = ';';

    /// <summary>
    /// 解析**一个选项**的效果组。
    /// </summary>
    /// <param name="raw">这一组的原文，如 <c>coin:+15;flag:raided</c>；空白 = 没有效果。</param>
    /// <param name="line">这一行在 beats.csv 里的行号（表头算第 1 行），报错用。</param>
    /// <param name="optionIndex">选项序号（0 起），报错用。</param>
    /// <param name="catalog">
    /// 物品表；传了就在解析时校验 <c>item</c> 的 id 存在，不传只校验语法。
    /// </param>
    /// <returns>按书写顺序排好的原子效果；空组返回空表。</returns>
    /// <exception cref="InvalidDataException">语法不对、数值非法、item id 不在表里。</exception>
    public static IReadOnlyList<Effect> Parse(string? raw, int line, int optionIndex, ItemCatalog? catalog = null)
    {
        if (raw == null || raw.Trim().Length == 0) return Array.Empty<Effect>();

        var atoms = raw.Split(AtomSeparator);
        var list = new List<Effect>(atoms.Length);

        foreach (var atom in atoms)
        {
            var text = atom.Trim();
            if (text.Length == 0) continue; // 允许结尾多写一个 ;，不算错
            list.Add(ParseAtom(text, line, optionIndex, catalog));
        }

        return list;
    }

    /// <summary>
    /// 解析**一整格** effects：按 <c>|</c> 拆成组，组数必须等于选项数。
    ///
    /// 整格空白是唯一被放行的"组数不匹配"：它表示每一个选项都没有效果
    /// （缺 effects 列的旧表、以及数据还没填到的行都会走到这里）。
    /// </summary>
    /// <param name="raw">整格原文。</param>
    /// <param name="line">行号（表头算第 1 行）。</param>
    /// <param name="optionCount">这一拍 options 的条数。</param>
    /// <param name="catalog">物品表；传了就校验 item id。</param>
    /// <returns>与 options 一一对应的组；每拍都是非 null 的列表。</returns>
    /// <exception cref="InvalidDataException">组数与选项数不一致，或任一组解析失败。</exception>
    public static IReadOnlyList<IReadOnlyList<Effect>> ParseAll(
        string? raw, int line, int optionCount, ItemCatalog? catalog = null)
    {
        if (optionCount < 0) throw new ArgumentOutOfRangeException(nameof(optionCount));

        if (raw == null || raw.Trim().Length == 0)
        {
            if (optionCount == 0) return Array.Empty<IReadOnlyList<Effect>>();

            // 整格空白 = 这一拍的选项全都还没配效果，不是"少写了几组"。
            var blanks = new IReadOnlyList<Effect>[optionCount];
            for (var i = 0; i < blanks.Length; i++) blanks[i] = Array.Empty<Effect>();
            return blanks;
        }

        var parts = raw.Split(GroupSeparator);
        if (parts.Length != optionCount)
            throw new InvalidDataException(
                $"节拍表第 {line} 行：effects 有 {parts.Length} 组，options 却有 {optionCount} 条，对不上。");

        var groups = new IReadOnlyList<Effect>[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            groups[i] = Parse(parts[i], line, i, catalog);

        return groups;
    }

    /// <summary>解析一个原子：<c>头:值</c>，值里可能再带 <c>=</c>（item）。</summary>
    private static Effect ParseAtom(string atom, int line, int optionIndex, ItemCatalog? catalog)
    {
        var colon = atom.IndexOf(':');
        if (colon < 0) throw Bad(line, optionIndex, atom, "缺少「:」");

        var head = atom.Substring(0, colon).Trim();
        var value = atom.Substring(colon + 1).Trim();

        switch (head)
        {
            case "coin":
                return new Effect(EffectKind.Coin, ParseInt(value, line, optionIndex, atom), null);

            case "item":
                return ParseItem(value, line, optionIndex, atom, catalog);

            case "guan":
                return new Effect(EffectKind.Guan, ParseInt(value, line, optionIndex, atom), null);

            case "min":
                return new Effect(EffectKind.Min, ParseInt(value, line, optionIndex, atom), null);

            case "yi":
                return new Effect(EffectKind.Yi, ParseInt(value, line, optionIndex, atom), null);

            case "troops":
                return new Effect(EffectKind.Troops, ParseInt(value, line, optionIndex, atom), null);

            case "flag":
                // 这里只要求有名字：旗标能不能写由 FlagStore 的规矩定，结算时会再核一遍，
                // 写不进去就整组报失败——但不在这里挡，免得表里一个旧名单段名就让十二拍都读不进来。
                if (value.Length == 0) throw Bad(line, optionIndex, atom, "缺少旗标名");
                return new Effect(EffectKind.Flag, 0, value);

            default:
                throw Bad(line, optionIndex, atom, $"认不出「{head}」这种效果");
        }
    }

    /// <summary>解析 <c>item:&lt;id&gt;=&lt;n&gt;</c>；n 必须是正整数，给了物品表还要查 id 在不在。</summary>
    private static Effect ParseItem(string value, int line, int optionIndex, string atom, ItemCatalog? catalog)
    {
        var eq = value.IndexOf('=');
        if (eq < 0) throw Bad(line, optionIndex, atom, "缺少「=件数」");

        var id = value.Substring(0, eq).Trim();
        var countText = value.Substring(eq + 1).Trim();

        if (id.Length == 0) throw Bad(line, optionIndex, atom, "缺少物品 id");

        var count = ParseInt(countText, line, optionIndex, atom);
        if (count < 1) throw Bad(line, optionIndex, atom, $"件数「{countText}」必须是正整数");

        if (catalog != null && catalog.TryGet(id) == null)
            throw Bad(line, optionIndex, atom, $"物品表里没有「{id}」");

        return new Effect(EffectKind.Item, count, id);
    }

    /// <summary>解析一个整数增量；非整数带行号抛。</summary>
    private static int ParseInt(string text, int line, int optionIndex, string atom)
    {
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            throw Bad(line, optionIndex, atom, $"「{text}」不是整数");

        return n;
    }

    /// <summary>统一的报错：行号 + 第几个选项 + 原文 + 毛病，一句说清。</summary>
    private static InvalidDataException Bad(int line, int optionIndex, string atom, string why) =>
        new($"节拍表第 {line} 行：第 {optionIndex + 1} 个选项的效果「{atom}」{why}。");
}
