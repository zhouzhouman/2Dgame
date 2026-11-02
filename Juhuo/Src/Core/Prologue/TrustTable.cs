using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一件"做了什么"对三条轴的增减：<c>Content/data/trust.csv</c> 的一行。
/// 三轴是**增量**，不是绝对值；钳到 0..100 的活由 <see cref="TrustBook"/> 干。
/// </summary>
/// <param name="Id">动作 id，全表唯一。</param>
/// <param name="Label">给人看的一行字（UI 不自己编）。</param>
/// <param name="Guan">官望增量，−10..10。</param>
/// <param name="Min">民望增量，−10..10。</param>
/// <param name="Yi">疑心增量，−10..10（正数 = 更被盯着）。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record TrustAction(string Id, string Label, int Guan, int Min, int Yi, string Note)
{
    /// <summary>单条动作在某一条轴上的最大步长。超过它一定是把 3 写成 30 了。</summary>
    public const int MaxStep = 10;
}

/// <summary>
/// 信任动作表：<c>Content/data/trust.csv</c> 的内存形式，按文件顺序保存。
///
/// 只有"加多少"这件事在这里；加在谁身上、加完怎么钳，是 <see cref="TrustBook"/>。
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：缺列、空 id、重复 id、数值不合规，
/// 全部带行号抛 <see cref="InvalidDataException"/>——静默吃掉一条动作，玩家只会觉得点了没反应。
/// </summary>
public sealed class TrustTable
{
    /// <summary>表里必须有的列，缺一列即视为坏表。</summary>
    private static readonly string[] RequiredColumns = { "id", "label", "guan", "min", "yi", "note" };

    private readonly List<TrustAction> _all;
    private readonly Dictionary<string, TrustAction> _byId;

    /// <summary>全部动作，按文件顺序。</summary>
    public IReadOnlyList<TrustAction> All => _all;

    /// <summary>动作条数。</summary>
    public int Count => _all.Count;

    private TrustTable(List<TrustAction> all)
    {
        _all = all;
        _byId = new Dictionary<string, TrustAction>(all.Count, StringComparer.Ordinal);
        foreach (var a in all) _byId[a.Id] = a;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">trust.csv 的路径。</param>
    /// <returns>信任动作表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、三轴不是 −10..10 的整数。</exception>
    public static TrustTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>信任动作表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、三轴不是 −10..10 的整数。</exception>
    public static TrustTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("信任表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"信任表第 1 行：缺少列「{name}」。");

        var list = new List<TrustAction>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException($"信任表第 {line} 行：id 为空。");

            if (!seen.Add(id))
                throw new InvalidDataException($"信任表第 {line} 行：id「{id}」重复。");

            list.Add(new TrustAction(
                id,
                Get("label"),
                ParseStep(Get("guan"), "guan", line),
                ParseStep(Get("min"), "min", line),
                ParseStep(Get("yi"), "yi", line),
                Get("note")));
        }

        return new TrustTable(list);
    }

    /// <summary>
    /// 按 id 取动作。
    /// </summary>
    /// <param name="id">动作 id；null / 空串一律视为找不到。</param>
    /// <param name="action">找到的动作；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out TrustAction? action)
    {
        if (string.IsNullOrEmpty(id))
        {
            action = null;
            return false;
        }

        return _byId.TryGetValue(id!, out action);
    }

    /// <summary>解析一列步长：必须是 −10..10 的整数，否则带行号抛。</summary>
    private static int ParseStep(string text, string column, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"信任表第 {line} 行：{column}「{text}」不是整数。");

        if (value < -TrustAction.MaxStep || value > TrustAction.MaxStep)
            throw new InvalidDataException(
                $"信任表第 {line} 行：{column}「{text}」超出 ±{TrustAction.MaxStep}。");

        return value;
    }
}
