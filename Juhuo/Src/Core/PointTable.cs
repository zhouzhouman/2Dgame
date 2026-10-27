using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Juhuo.Core;

/// <summary>
/// 全部可搜点。按 hub + 坐标建索引，供"点格子查点"使用。
/// 纯 C#，不依赖引擎，可在单元测试与 CLI 里直接用。
/// </summary>
public sealed class PointTable
{
    private readonly List<SearchPoint> _all;
    private readonly Dictionary<string, SearchPoint> _byPos;

    public IReadOnlyList<SearchPoint> All => _all;

    private PointTable(List<SearchPoint> all)
    {
        _all = all;
        _byPos = new Dictionary<string, SearchPoint>(all.Count);
        foreach (var p in all)
            _byPos[Key(p.Hub, p.X, p.Y)] = p;
    }

    private static string Key(string hub, int x, int y) => $"{hub}:{x},{y}";

    public static PointTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText);
        if (rows.Count < 2) throw new FormatException("CSV 至少需要表头与一行数据");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        string Get(string[] row, string name) =>
            col.TryGetValue(name, out var i) && i < row.Length ? row[i] : "";

        int GetInt(string[] row, string name) =>
            int.TryParse(Get(row, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        double GetDouble(string[] row, string name) =>
            double.TryParse(Get(row, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1;

        var list = new List<SearchPoint>(rows.Count - 1);
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (string.IsNullOrWhiteSpace(Get(r, "id"))) continue;

            list.Add(new SearchPoint
            {
                Hub = Get(r, "hub"),
                Id = Get(r, "id"),
                Mark = Get(r, "mark"),
                X = GetInt(r, "x"),
                Y = GetInt(r, "y"),
                Layer = string.IsNullOrEmpty(Get(r, "layer")) ? "surface" : Get(r, "layer"),
                Name = Get(r, "name"),
                Requires = Get(r, "requires"),
                TextOnce = Get(r, "text_once"),
                TextRepeat = Get(r, "text_repeat"),
                Loot = Get(r, "loot"),
                SetFlag = Get(r, "set_flag"),
                WatchNpc = Get(r, "watch_npc"),
                Cost = GetDouble(r, "cost"),
            });
        }

        return new PointTable(list);
    }

    public IEnumerable<SearchPoint> ForHub(string hub) => _all.Where(p => p.Hub == hub);

    public SearchPoint? At(string hub, int x, int y) =>
        _byPos.TryGetValue(Key(hub, x, y), out var p) ? p : null;

    public int CountForHub(string hub) => _all.Count(p => p.Hub == hub);
}
