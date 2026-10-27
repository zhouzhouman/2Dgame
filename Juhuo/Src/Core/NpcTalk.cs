using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>一位 NPC 的台词。三段：初次见面、再谈、被打断。</summary>
public sealed class NpcTalk
{
    public string Hub = "";
    public string Name = "";
    public int X;
    public int Y;
    public string First = "";
    public string Again = "";
    public string Interrupt = "";
}

/// <summary>
/// 全部 NPC 台词，按 hub + 坐标索引。
/// 和 PointTable 一样的形状：纯 C#，不依赖引擎，能单测。
/// </summary>
public sealed class NpcTalkTable
{
    private readonly List<NpcTalk> _all = new();
    private readonly Dictionary<string, NpcTalk> _byPos = new();

    public IReadOnlyList<NpcTalk> All => _all;

    private static string Key(string hub, int x, int y) => $"{hub}:{x},{y}";

    public static NpcTalkTable FromCsv(string text)
    {
        var t = new NpcTalkTable();
        var rows = Csv.Parse(text);
        if (rows.Count < 2) return t;

        // 列序：hub,name,x,y,first,again,interrupt
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Length < 7) continue;

            var n = new NpcTalk
            {
                Hub = r[0].Trim(),
                Name = r[1].Trim(),
                X = int.TryParse(r[2].Trim(), out var x) ? x : 0,
                Y = int.TryParse(r[3].Trim(), out var y) ? y : 0,
                First = r[4],
                Again = r[5],
                Interrupt = r[6],
            };

            if (n.First.Length == 0 && n.Again.Length == 0) continue;

            t._all.Add(n);
            t._byPos[Key(n.Hub, n.X, n.Y)] = n;
        }

        return t;
    }

    public NpcTalk? At(string hub, int x, int y)
        => _byPos.TryGetValue(Key(hub, x, y), out var v) ? v : null;

    /// <summary>按名字找。巡逻的衙役会走开，坐标靠不住，只能靠名字认人。</summary>
    public NpcTalk? ByName(string hub, string name)
    {
        foreach (var n in _all)
            if (n.Hub == hub && n.Name == name) return n;

        return null;
    }

    /// <summary>
    /// 这一处站着不动的人。坐标 (0,0) 的条目是"会走的"（巡逻衙役），
    /// 不参与按坐标查人，只按名字找——不然空地也会冒出对话。
    /// </summary>
    public IEnumerable<NpcTalk> ForHub(string hub)
    {
        foreach (var n in _all)
            if (n.Hub == hub && (n.X > 0 || n.Y > 0)) yield return n;
    }
}
