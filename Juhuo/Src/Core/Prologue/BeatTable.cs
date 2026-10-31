using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 十二节拍表：<c>Content/data/beats.csv</c> 的内存形式，按文件顺序保存。
/// 顺序即剧情顺序，<see cref="All"/> / <see cref="ForHub"/> 都按这个顺序返回。
/// 纯 C#，不依赖引擎；<see cref="Load"/> 只负责把文件读成文本，解析在 <see cref="FromCsv"/>。
/// </summary>
public sealed class BeatTable
{
    /// <summary>表里必须有的列，缺一列即视为坏表（S1-06）。</summary>
    private static readonly string[] RequiredColumns =
    {
        "id", "name", "hub", "day", "requires", "sets", "trigger", "note",
    };

    private readonly List<Beat> _all;
    private readonly Dictionary<string, Beat> _byId;

    /// <summary>全部节拍，按文件顺序（= 剧情顺序）。</summary>
    public IReadOnlyList<Beat> All => _all;

    /// <summary>节拍条数。</summary>
    public int Count => _all.Count;

    private BeatTable(List<Beat> all)
    {
        _all = all;
        _byId = new Dictionary<string, Beat>(all.Count, StringComparer.Ordinal);
        foreach (var b in all) _byId[b.Id] = b;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">beats.csv 的路径。</param>
    /// <returns>节拍表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、day 不是整数。</exception>
    public static BeatTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        // File.ReadAllText 带 BOM 时按 BOM 解码，无 BOM 时按 UTF-8；Csv.Parse 还会再兜一次 BOM。
        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行的行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>节拍表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、day 不是整数。</exception>
    public static BeatTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("节拍表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"节拍表第 1 行：缺少列「{name}」。");

        var list = new List<Beat>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException($"节拍表第 {line} 行：id 为空。");

            if (!seen.Add(id))
                throw new InvalidDataException($"节拍表第 {line} 行：id「{id}」重复。");

            var dayText = Get("day");
            if (!int.TryParse(dayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
                throw new InvalidDataException($"节拍表第 {line} 行：day「{dayText}」不是整数。");

            var sets = SplitSets(Get("sets"));

            // sets 是"完成时置的旗标"，名字必须过 FlagStore 的命名规则。
            // 不过不行：FlagStore.Set 对非法名静默忽略，这一拍会永远完不成而没人报错。
            foreach (var flag in sets)
                if (!FlagStore.IsValidName(flag))
                    throw new InvalidDataException(
                        $"节拍表第 {line} 行：sets 里的旗标名「{flag}」不合法（须符合 FlagStore.IsValidName）。");

            list.Add(new Beat(
                id,
                Get("name"),
                Get("hub"),
                day,
                Get("requires"),
                sets,
                Get("trigger"),
                SplitOptions(Get("options")),
                Get("note")));
        }

        return new BeatTable(list);
    }

    /// <summary>
    /// 按 id 取节拍。
    /// </summary>
    /// <param name="id">节拍 id。</param>
    /// <returns>找到返回节拍，找不到返回 null。</returns>
    public Beat? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _byId.TryGetValue(id!, out var beat) ? beat : null;
    }

    /// <summary>
    /// 某个空间的全部节拍，按剧情顺序。
    /// </summary>
    /// <param name="hub">空间 id（hub1–hub7）。</param>
    /// <returns>该空间的节拍；没有则是空序列。</returns>
    public IEnumerable<Beat> ForHub(string? hub)
    {
        if (string.IsNullOrEmpty(hub)) return Array.Empty<Beat>();
        return _all.Where(b => string.Equals(b.Hub, hub, StringComparison.Ordinal));
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

    /// <summary>
    /// 把 <c>options</c> 列的竖线分隔值拆成选项文本。缺列或空白 = 空数组。
    /// 空数组是合法的：表示这一拍还没有可选项文案，表现层退化成单一的"继续"。
    /// </summary>
    private static string[] SplitOptions(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        var parts = raw.Split('|');
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var text = p.Trim();
            if (text.Length > 0) list.Add(text);
        }

        return list.ToArray();
    }
}
