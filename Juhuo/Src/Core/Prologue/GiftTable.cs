using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一条"给他看"的应答：<c>Content/data/gifts.csv</c> 的一行。
/// 只记"谁认这件东西、认得之后置什么旗标"，反应文案在数据侧。
/// </summary>
/// <param name="Npc">认这件东西的人（NPC id）。</param>
/// <param name="Item">物品 id。</param>
/// <param name="Requires">
/// "你给他看之前就已经知道这是什么"的条件（<see cref="FlagExpression"/> 语法）。
/// 成立 = 有备而来的那种反应；不成立 = "这不是我的"。空串 = 谁都认。
/// </param>
/// <param name="Sets">认得之后置的旗标，CSV 里逗号分隔，可能为空。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record Gift(
    string Npc,
    string Item,
    string Requires,
    string[] Sets,
    string Note);

/// <summary>
/// 一次"给他看"的结果。
/// </summary>
/// <param name="Matched">
/// 表里有没有这一行：false = 这个人对这件东西没什么可说的。
/// **不是错误**——玩家可以随便把东西给人看。
/// </param>
/// <param name="KnownBefore">
/// 你给他看之前是不是就已经知道这是什么（<c>requires</c> 成立）。
/// 两种反应的分界就在这里：知道 → 沉默很久；不知道 → "这不是我的"。
/// </param>
/// <param name="Applied">该置的旗标（<c>KnownBefore</c> 为假时为空）。由调用方写进旗标仓库。</param>
public readonly record struct GiftResult(bool Matched, bool KnownBefore, IReadOnlyList<string> Applied)
{
    /// <summary>没有匹配行：什么都不发生。</summary>
    public static GiftResult None => new(false, false, Array.Empty<string>());
}

/// <summary>
/// 赠礼表：<c>Content/data/gifts.csv</c> 的内存形式，按文件顺序保存。
///
/// 这不是"送礼加好感"，是"给他看"（序章关卡设计 §2.5）：NPC 的反应由物品本身、
/// 时机、以及你**已经知道的信息**决定。这里只负责后两件里能写成旗标的那部分。
///
/// 坏数据只关门：<c>requires</c> 语法错 = 这一行永远"不认得"，不抛；
/// 但 <c>sets</c> 里的非法旗标名读表即抛——否则会静默停摆（照 <see cref="BeatTable"/>）。
/// </summary>
public sealed class GiftTable
{
    /// <summary>表里必须有的列。</summary>
    private static readonly string[] RequiredColumns = { "npc", "item", "requires", "sets", "note" };

    private readonly List<Gift> _all;

    /// <summary>全部应答行，按文件顺序。</summary>
    public IReadOnlyList<Gift> All => _all;

    /// <summary>行数。</summary>
    public int Count => _all.Count;

    private GiftTable(List<Gift> all) => _all = all;

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">gifts.csv 的路径。</param>
    /// <returns>赠礼表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 npc / item、sets 里的旗标名不合法。</exception>
    public static GiftTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// 同一个人对同一件东西有多行是合法的（不同 requires 对应不同反应），按文件顺序都收。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>赠礼表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 npc / item、sets 里的旗标名不合法。</exception>
    public static GiftTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("赠礼表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"赠礼表第 1 行：缺少列「{name}」。");

        var list = new List<Gift>(Math.Max(0, rows.Count - 1));

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var npc = Get("npc");
            if (string.IsNullOrEmpty(npc))
                throw new InvalidDataException($"赠礼表第 {line} 行：npc 为空。");

            var item = Get("item");
            if (string.IsNullOrEmpty(item))
                throw new InvalidDataException($"赠礼表第 {line} 行：item 为空。");

            var sets = SplitSets(Get("sets"));

            // sets 是"认得之后置的旗标"，名字必须过 FlagStore 的命名规则：
            // 不过不行，FlagStore.Set 对非法名静默忽略，这一行就永远看不出效果。
            foreach (var flag in sets)
                if (!FlagStore.IsValidName(flag))
                    throw new InvalidDataException(
                        $"赠礼表第 {line} 行：sets 里的旗标名「{flag}」不合法（须符合 FlagStore.IsValidName）。");

            list.Add(new Gift(npc, item, Get("requires"), sets, Get("note")));
        }

        return new GiftTable(list);
    }

    /// <summary>
    /// 这个人认不认这件东西，且你是否已经有备而来。
    /// </summary>
    /// <param name="npc">NPC id。</param>
    /// <param name="item">物品 id。</param>
    /// <param name="flags">旗标取值委托；null 视为任何旗标都不成立。</param>
    /// <returns>会给出"认得"的反应返回 true。</returns>
    public bool CanShow(string? npc, string? item, FlagProbe? flags) => Show(npc, item, flags).KnownBefore;

    /// <summary>
    /// 把一件东西给一个人看。
    ///
    /// 命中就置 <c>sets</c> 里的旗标；没有匹配行则什么都不做（<see cref="GiftResult.Matched"/> 为 false），
    /// 不抛异常——玩家可以随便把东西给人看。
    /// </summary>
    /// <param name="npc">NPC id。</param>
    /// <param name="item">物品 id。</param>
    /// <param name="flags">旗标取值委托，用来判 <c>requires</c>。</param>
    /// <param name="setFlag">顺手把旗标写进去的回调；不给就只用返回值里的 <see cref="GiftResult.Applied"/>。</param>
    /// <returns>结果：命中没有、你事先知道不知道、该置哪些旗标。</returns>
    public GiftResult Show(string? npc, string? item, FlagProbe? flags, Action<string>? setFlag = null)
    {
        if (string.IsNullOrEmpty(npc) || string.IsNullOrEmpty(item)) return GiftResult.None;

        var matched = false;
        var known = false;
        List<string>? applied = null;

        foreach (var row in _all)
        {
            if (!string.Equals(row.Npc, npc, StringComparison.Ordinal)) continue;
            if (!string.Equals(row.Item, item, StringComparison.Ordinal)) continue;

            matched = true;

            // requires 语法错只会让这一行"不认得"，不抛——坏数据只该让门关上。
            if (!FlagExpression.Evaluate(row.Requires, flags)) continue;

            known = true;

            foreach (var flag in row.Sets)
            {
                applied ??= new List<string>(row.Sets.Length);
                if (applied.Contains(flag)) continue;

                applied.Add(flag);
                setFlag?.Invoke(flag);
            }
        }

        if (!matched) return GiftResult.None;

        return new GiftResult(true, known, applied == null ? Array.Empty<string>() : applied.ToArray());
    }

    /// <summary>把 <c>sets</c> 列的逗号分隔值拆成旗标名数组，空白项丢掉。</summary>
    private static string[] SplitSets(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        var parts = raw.Split(',');
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var name = p.Trim();
            if (name.Length > 0) list.Add(name);
        }

        return list.ToArray();
    }
}
