using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一个国号：<c>Content/data/realms.csv</c> 的一行（设计文档 §16.6 第 2 步「国号与年号」）。
///
/// 定国号影响天命路线，所以国号是**表里有的才作数**——不能由表现层随手拼一个字符串。
/// </summary>
/// <param name="Id">国号 id，全表唯一，也是存档里记的那个值。</param>
/// <param name="Label">国号正文，玩家看到的那两个字（如「大燮」）。</param>
/// <param name="Motto">年号 / 一句题眼，可以留空。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record Realm(string Id, string Label, string Motto, string Note);

/// <summary>
/// 国号表：<c>Content/data/realms.csv</c> 的内存形式，按文件顺序保存。
///
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：缺列、空 id、重复 id，全部带行号抛
/// <see cref="InvalidDataException"/>。
/// </summary>
public sealed class RealmTable
{
    /// <summary>表里必须有的列，缺一列即视为坏表。</summary>
    private static readonly string[] RequiredColumns = { "id", "label", "motto", "note" };

    private readonly List<Realm> _all;
    private readonly Dictionary<string, Realm> _byId;

    /// <summary>全部国号，按文件顺序。</summary>
    public IReadOnlyList<Realm> All => _all;

    /// <summary>国号条数。</summary>
    public int Count => _all.Count;

    private RealmTable(List<Realm> all)
    {
        _all = all;
        _byId = new Dictionary<string, Realm>(all.Count, StringComparer.Ordinal);
        foreach (var r in all) _byId[r.Id] = r;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">realms.csv 的路径。</param>
    /// <returns>国号表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id。</exception>
    public static RealmTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>国号表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id。</exception>
    public static RealmTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("国号表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"国号表第 1 行：缺少列「{name}」。");

        var list = new List<Realm>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException($"国号表第 {line} 行：id 为空。");

            if (!seen.Add(id))
                throw new InvalidDataException($"国号表第 {line} 行：id「{id}」重复。");

            list.Add(new Realm(id, Get("label"), Get("motto"), Get("note")));
        }

        return new RealmTable(list);
    }

    /// <summary>
    /// 按 id 取国号。
    /// </summary>
    /// <param name="id">国号 id；null / 空串一律视为找不到。</param>
    /// <param name="realm">找到的国号；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out Realm? realm)
    {
        if (string.IsNullOrEmpty(id))
        {
            realm = null;
            return false;
        }

        return _byId.TryGetValue(id!, out realm);
    }
}
