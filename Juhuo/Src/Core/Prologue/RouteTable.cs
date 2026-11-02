using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一条攻城路线：<c>Content/data/routes.csv</c> 的一行。
///
/// 设计文档 §16.4：官望高 → 内应开门；民望高 → 民变开城。两条门各有各的门槛，
/// 玩家从头到尾在选"我要哪扇门"。
/// </summary>
/// <param name="Id">路线 id。</param>
/// <param name="Label">给人看的名字。</param>
/// <param name="Axis">看哪条轴：<see cref="AxisGuan"/> 或 <see cref="AxisMin"/>。</param>
/// <param name="Threshold">门槛，1..100；该轴 ≥ 门槛即开门（等于算开）。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record TrustRoute(string Id, string Label, string Axis, int Threshold, string Note)
{
    /// <summary>官望轴。</summary>
    public const string AxisGuan = "guan";

    /// <summary>民望轴。</summary>
    public const string AxisMin = "min";

    /// <summary>门槛下限。</summary>
    public const int MinThreshold = 1;

    /// <summary>门槛上限。</summary>
    public const int MaxThreshold = 100;
}

/// <summary>
/// 攻城路线表：<c>Content/data/routes.csv</c> 的内存形式，按文件顺序保存。
///
/// 坏表读表即抛（照 <see cref="BeatTable"/>）：缺列、空 id、重复 id、axis 不是 guan/min、
/// threshold 不在 1..100。轴写错的路线会永远不开门，而没人会知道是自己写错了字。
/// </summary>
public sealed class RouteTable
{
    /// <summary>表里必须有的列，缺一列即视为坏表。</summary>
    private static readonly string[] RequiredColumns = { "id", "label", "axis", "threshold", "note" };

    private readonly List<TrustRoute> _all;
    private readonly Dictionary<string, TrustRoute> _byId;

    /// <summary>全部路线，按文件顺序。</summary>
    public IReadOnlyList<TrustRoute> All => _all;

    /// <summary>路线条数。</summary>
    public int Count => _all.Count;

    private RouteTable(List<TrustRoute> all)
    {
        _all = all;
        _byId = new Dictionary<string, TrustRoute>(all.Count, StringComparer.Ordinal);
        foreach (var r in all) _byId[r.Id] = r;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">routes.csv 的路径。</param>
    /// <returns>路线表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、axis 非法、threshold 越界。</exception>
    public static RouteTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>路线表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、axis 非法、threshold 越界。</exception>
    public static RouteTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("路线表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"路线表第 1 行：缺少列「{name}」。");

        var list = new List<TrustRoute>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException($"路线表第 {line} 行：id 为空。");

            if (!seen.Add(id))
                throw new InvalidDataException($"路线表第 {line} 行：id「{id}」重复。");

            var axis = Get("axis");
            if (axis != TrustRoute.AxisGuan && axis != TrustRoute.AxisMin)
                throw new InvalidDataException(
                    $"路线表第 {line} 行：axis「{axis}」不认识（只能是 {TrustRoute.AxisGuan} 或 {TrustRoute.AxisMin}）。");

            var thresholdText = Get("threshold");
            if (!int.TryParse(thresholdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold))
                throw new InvalidDataException($"路线表第 {line} 行：threshold「{thresholdText}」不是整数。");

            if (threshold < TrustRoute.MinThreshold || threshold > TrustRoute.MaxThreshold)
                throw new InvalidDataException(
                    $"路线表第 {line} 行：threshold「{thresholdText}」超出 {TrustRoute.MinThreshold}..{TrustRoute.MaxThreshold}。");

            list.Add(new TrustRoute(id, Get("label"), axis, threshold, Get("note")));
        }

        return new RouteTable(list);
    }

    /// <summary>
    /// 按 id 取路线。
    /// </summary>
    /// <param name="id">路线 id；null / 空串一律视为找不到。</param>
    /// <param name="route">找到的路线；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out TrustRoute? route)
    {
        if (string.IsNullOrEmpty(id))
        {
            route = null;
            return false;
        }

        return _byId.TryGetValue(id!, out route);
    }
}
