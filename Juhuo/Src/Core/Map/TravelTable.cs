using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一条位移：<c>Content/data/travel.csv</c> 的一行。
///
/// **有向**——hub1→hub2 与 hub2→hub1 是两条，各算各的刻（设表的人可以用不对称的
/// 刻数表达"上坡费劲、下坡省力"）。七个空间两两有向即 7×6 = 42 条。
/// </summary>
/// <param name="From">从哪个空间走。</param>
/// <param name="To">到哪个空间去。</param>
/// <param name="Ke">走这趟要几刻，1..<see cref="TravelTable.MaxKe"/>。同地不算路（见 <see cref="TravelBook.Cost"/>）。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record TravelLeg(string From, string To, int Ke, string Note);

/// <summary>
/// 位移表：<c>Content/data/travel.csv</c> 的内存形式，按文件顺序保存。
///
/// 坏表读表即抛（照 <see cref="BeatTable"/>）：缺列、from/to 为空、from 与 to 相同、
/// from/to 不是已知空间、ke 不是整数或不在 1..<see cref="MaxKe"/>、同一对空间重复。
/// 一张读歪的位移表最坏的表现是"某条路怎么点都灰着"，玩家只会以为游戏坏了；
/// 所以宁可读表时就崩在带行号的那一行上。
///
/// 纯 C#，不依赖引擎；<see cref="Load"/> 只负责把文件读成文本，解析在 <see cref="FromCsv"/>。
/// </summary>
public sealed class TravelTable
{
    /// <summary>最贵的一条路要几刻。上限 6 刻，与 <see cref="TravelBook.MaxKe"/> 同源。</summary>
    public const int MaxKe = 6;

    /// <summary>最便宜的一条路要几刻。0 刻的路等于白送，是写表写漏了，不当合法数据。</summary>
    public const int MinKe = 1;

    /// <summary>表里必须有的列，缺一列即视为坏表。</summary>
    private static readonly string[] RequiredColumns = { "from", "to", "ke", "note" };

    private readonly List<TravelLeg> _all;
    private readonly Dictionary<string, TravelLeg> _byPair;

    /// <summary>全部路线，按文件顺序。</summary>
    public IReadOnlyList<TravelLeg> All => _all;

    /// <summary>路线条数（七个空间两两有向时是 42）。</summary>
    public int Count => _all.Count;

    private TravelTable(List<TravelLeg> all)
    {
        _all = all;
        _byPair = new Dictionary<string, TravelLeg>(all.Count, StringComparer.Ordinal);
        foreach (var leg in all) _byPair[Key(leg.From, leg.To)] = leg;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">travel.csv 的路径。</param>
    /// <returns>位移表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 from/to、同地、未知空间、ke 越界、重复路线。</exception>
    public static TravelTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>位移表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 from/to、同地、未知空间、ke 越界、重复路线。</exception>
    public static TravelTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("位移表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"位移表第 1 行：缺少列「{name}」。");

        var list = new List<TravelLeg>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var from = Get("from");
            if (string.IsNullOrEmpty(from))
                throw new InvalidDataException($"位移表第 {line} 行：from 为空。");

            var to = Get("to");
            if (string.IsNullOrEmpty(to))
                throw new InvalidDataException($"位移表第 {line} 行：to 为空。");

            if (string.Equals(from, to, StringComparison.Ordinal))
                throw new InvalidDataException($"位移表第 {line} 行：from 与 to 都是「{from}」，同地不是一条路。");

            if (!ClockTable.IsKnownHub(from))
                throw new InvalidDataException($"位移表第 {line} 行：from「{from}」不是已知空间（只能是 hub1–hub7）。");

            if (!ClockTable.IsKnownHub(to))
                throw new InvalidDataException($"位移表第 {line} 行：to「{to}」不是已知空间（只能是 hub1–hub7）。");

            var keText = Get("ke");
            if (!int.TryParse(keText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ke))
                throw new InvalidDataException($"位移表第 {line} 行：ke「{keText}」不是整数。");

            if (ke < MinKe || ke > MaxKe)
                throw new InvalidDataException(
                    $"位移表第 {line} 行：ke「{keText}」超出 {MinKe}..{MaxKe}。");

            var key = Key(from, to);
            if (!seen.Add(key))
                throw new InvalidDataException($"位移表第 {line} 行：{from} → {to} 这条路重复了。");

            list.Add(new TravelLeg(from, to, ke, Get("note")));
        }

        return new TravelTable(list);
    }

    /// <summary>
    /// 查一条路。
    /// </summary>
    /// <param name="from">起点空间 id；null / 空串一律视为找不到。</param>
    /// <param name="to">终点空间 id；null / 空串一律视为找不到。</param>
    /// <param name="leg">查到的路线；查不到为 null。</param>
    /// <returns>有这条路返回 true。</returns>
    public bool TryGet(string? from, string? to, [NotNullWhen(true)] out TravelLeg? leg)
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            leg = null;
            return false;
        }

        return _byPair.TryGetValue(Key(from!, to!), out leg);
    }

    /// <summary>
    /// 按起终点取一条路。查不到返回 null（没路不是异常，UI 拿它灰掉选项）。
    /// </summary>
    /// <param name="from">起点空间 id。</param>
    /// <param name="to">终点空间 id。</param>
    /// <returns>路线；查不到返回 null。</returns>
    public TravelLeg? Travel(string? from, string? to) => TryGet(from, to, out var leg) ? leg : null;

    /// <summary>路线表的键：一个空间 id 里不含斜杠，拼起来就够唯一。</summary>
    private static string Key(string from, string to) => from + "/" + to;
}
