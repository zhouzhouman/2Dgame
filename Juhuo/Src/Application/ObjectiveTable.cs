using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// <c>beats.csv</c> 里 <c>target</c> / <c>hint</c> 两列的读取（票号 S4-03b）。
///
/// 为什么不挂在 <see cref="Juhuo.Core.Beat"/> 上：这两列是**表现层**的事（画哪、说什么），
/// 不参与"这一拍能不能做"的判定；而且表可能还没补这两列（D3 在写），
/// 这里读不到就退化成 <see cref="Objective.Empty"/>，旧表照开、照玩。
///
/// 只读这两列与 id：别的列自有 <see cref="BeatTable"/> 管，这里不抄第二份节拍模型。
/// </summary>
public sealed class ObjectiveTable
{
    /// <summary>目标点那一列。</summary>
    public const string TargetColumn = "target";

    /// <summary>"怎么去"那一列。</summary>
    public const string HintColumn = "hint";

    private const string IdColumn = "id";

    private readonly Dictionary<string, Objective> _byId;

    private ObjectiveTable(Dictionary<string, Objective> byId) => _byId = byId;

    /// <summary>一张空表：任何节拍都解析成 <see cref="Objective.Empty"/>。</summary>
    public static ObjectiveTable Empty { get; } = new(new Dictionary<string, Objective>(StringComparer.Ordinal));

    /// <summary>表里有几拍（只数写了 id 的行）。</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// 从 CSV 文本建表。
    ///
    /// 缺 <c>target</c> 与 <c>hint</c> 两列 → 返回 <see cref="Empty"/>（退化，不抛）；
    /// 只缺其中一列 → 另一列照读。行里写坏了不抛，按 <see cref="Objective.Parse"/> 退化成 None。
    /// </summary>
    /// <param name="csvText">beats.csv 全文。</param>
    /// <returns>目标表。</returns>
    public static ObjectiveTable FromCsv(string? csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0) return Empty;

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim();
            if (name.Length > 0) col[name] = i;
        }

        // 两列都没有：这张表还是旧表，整张退化成"没有目标点"。
        if (!col.ContainsKey(TargetColumn) && !col.ContainsKey(HintColumn)) return Empty;

        var byId = new Dictionary<string, Objective>(Math.Max(0, rows.Count - 1), StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get(IdColumn);
            if (id.Length == 0) continue;

            byId[id] = Objective.Parse(Get(TargetColumn), Get(HintColumn));
        }

        return new ObjectiveTable(byId);
    }

    /// <summary>读表文件（UTF-8，带不带 BOM 都行）。</summary>
    /// <param name="csvPath">beats.csv 的路径。</param>
    /// <returns>目标表。</returns>
    public static ObjectiveTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));
        return FromCsv(System.IO.File.ReadAllText(csvPath));
    }

    /// <summary>这一拍的目标点；表里没有这一拍时返回 <see cref="Objective.Empty"/>。</summary>
    /// <param name="beatId">节拍 id（如 b01）。</param>
    /// <returns>目标点，不会是 null。</returns>
    public Objective Of(string? beatId)
    {
        if (string.IsNullOrEmpty(beatId)) return Objective.Empty;
        return _byId.TryGetValue(beatId!, out var objective) ? objective : Objective.Empty;
    }
}
