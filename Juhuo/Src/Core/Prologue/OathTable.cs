using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一张誓约的出处：它是在哪一拍的第几个选项上许下的。
///
/// 誓约本身不记"许没许过"——那是旗标的事（<c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>）。
/// 这里只把 <c>source</c> 列（形如 <c>b07:1</c>）拆成两个能用程序问的字段。
/// </summary>
/// <param name="BeatId">节拍 id，<c>source</c> 冒号左边那一半。</param>
/// <param name="OptionIndex">选项序号，0 起，冒号右边那一半。</param>
public readonly record struct OathSource(string BeatId, int OptionIndex)
{
    /// <summary><c>source</c> 列里节拍与序号之间的分隔符。</summary>
    public const char Separator = ':';

    /// <summary>
    /// 这一条誓约对应"选了什么"的那面旗标名：<c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>。
    /// 与 <see cref="Juhuo.Core.FlagStore"/> 认的四段点号写法一致。
    /// </summary>
    public string FlagName => $"beat.{BeatId}.pick.{OptionIndex}";

    /// <summary>
    /// 解析 <c>source</c> 列。
    /// </summary>
    /// <param name="raw">原始文本，形如 <c>b07:1</c>。</param>
    /// <param name="line">这一行在表里的行号，只用来写错误消息。</param>
    /// <returns>拆好的出处。</returns>
    /// <exception cref="InvalidDataException">
    /// 没有冒号、冒号左边为空、右边不是非负整数、或拼出来的旗标名过不了
    /// <see cref="FlagStore.IsValidName"/>——全部带行号抛。
    /// 最后这一条是防呆：旗标名不合法，<see cref="FlagStore.Set"/> 会静默忽略，
    /// 这张誓约就永远"没许过"，而没人知道是自己把节拍 id 写错了。
    /// </exception>
    public static OathSource Parse(string? raw, int line)
    {
        var text = (raw ?? "").Trim();

        var at = text.IndexOf(Separator);
        if (at < 0)
            throw new InvalidDataException(
                $"誓约表第 {line} 行：source「{text}」认不出来（要写成 节拍id:选项序号，比如 b07:1）。");

        var beat = text[..at].Trim();
        var indexText = text[(at + 1)..].Trim();

        if (beat.Length == 0)
            throw new InvalidDataException($"誓约表第 {line} 行：source「{text}」的节拍 id 是空的。");

        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            throw new InvalidDataException(
                $"誓约表第 {line} 行：source「{text}」的选项序号「{indexText}」不是整数。");

        if (index < 0)
            throw new InvalidDataException(
                $"誓约表第 {line} 行：source「{text}」的选项序号是负数（只能从 0 起）。");

        var source = new OathSource(beat, index);
        if (!FlagStore.IsValidName(source.FlagName))
            throw new InvalidDataException(
                $"誓约表第 {line} 行：source「{text}」拼出来的旗标名「{source.FlagName}」不合法（须符合 FlagStore.IsValidName）。");

        return source;
    }

    /// <summary>写回 <c>source</c> 列的写法。</summary>
    /// <returns>形如 <c>b07:1</c>。</returns>
    public override string ToString() =>
        string.Concat(BeatId, Separator.ToString(), OptionIndex.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// 一张誓约卡：<c>Content/data/oaths.csv</c> 的一行（设计文档 §14.2）。
///
/// 序章里每一次承诺生成一张这样的卡；转段时选 3 条成为开国国策，其余全部转成债。
/// </summary>
/// <param name="Id">誓约 id，全表唯一。</param>
/// <param name="Text">誓约正文，玩家看到的那句话。</param>
/// <param name="Beneficiary">受益者（势力 / 人群 / 某个人）。</param>
/// <param name="Deadline">期限，表里怎么写就怎么存——文案归内容，不在这里换算。</param>
/// <param name="Penalty">违约惩罚，同上。</param>
/// <param name="Source">在哪一拍的第几个选项上许下的。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record Oath(
    string Id,
    string Text,
    string Beneficiary,
    string Deadline,
    string Penalty,
    OathSource Source,
    string Note);

/// <summary>
/// 誓约表：<c>Content/data/oaths.csv</c> 的内存形式，按文件顺序保存。
///
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：缺列、空 id、重复 id、<c>source</c> 认不出来，
/// 全部带行号抛 <see cref="InvalidDataException"/>——静默吃掉一条誓约，转段时就少一条国策可选，
/// 玩家只会觉得"我明明许过"。
/// </summary>
public sealed class OathTable
{
    /// <summary>表里必须有的列，缺一列即视为坏表。</summary>
    private static readonly string[] RequiredColumns =
    {
        "id", "text", "beneficiary", "deadline", "penalty", "source", "note",
    };

    private readonly List<Oath> _all;
    private readonly Dictionary<string, Oath> _byId;

    /// <summary>全部誓约，按文件顺序（= 剧情顺序）。</summary>
    public IReadOnlyList<Oath> All => _all;

    /// <summary>誓约条数。</summary>
    public int Count => _all.Count;

    private OathTable(List<Oath> all)
    {
        _all = all;
        _byId = new Dictionary<string, Oath>(all.Count, StringComparer.Ordinal);
        foreach (var o in all) _byId[o.Id] = o;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">oaths.csv 的路径。</param>
    /// <returns>誓约表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、source 认不出来。</exception>
    public static OathTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>誓约表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、source 认不出来。</exception>
    public static OathTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("誓约表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"誓约表第 1 行：缺少列「{name}」。");

        var list = new List<Oath>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException($"誓约表第 {line} 行：id 为空。");

            if (!seen.Add(id))
                throw new InvalidDataException($"誓约表第 {line} 行：id「{id}」重复。");

            list.Add(new Oath(
                id,
                Get("text"),
                Get("beneficiary"),
                Get("deadline"),
                Get("penalty"),
                OathSource.Parse(Get("source"), line),
                Get("note")));
        }

        return new OathTable(list);
    }

    /// <summary>
    /// 按 id 取誓约。
    /// </summary>
    /// <param name="id">誓约 id；null / 空串一律视为找不到。</param>
    /// <param name="oath">找到的誓约；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out Oath? oath)
    {
        if (string.IsNullOrEmpty(id))
        {
            oath = null;
            return false;
        }

        return _byId.TryGetValue(id!, out oath);
    }
}
