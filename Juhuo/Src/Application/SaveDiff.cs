using System;
using System.Collections.Generic;
using System.Globalization;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 两份存档逐字段比对。写档 → 重读之后，拿它确认"落下去的和读回来的是同一份"。
///
/// 只比存档契约里有的字段（schema_v2.md §C）。比对结果是一串人话，
/// 有差异时直接能看出差在哪，不是一个 bool。
/// </summary>
public static class SaveDiff
{
    /// <summary>
    /// 比两份存档。
    /// </summary>
    /// <param name="a">写下去的那份。</param>
    /// <param name="b">读回来的那份。</param>
    /// <returns>差异列表；一模一样时是空列表。</returns>
    public static List<string> Compare(SaveState? a, SaveState? b)
    {
        var diffs = new List<string>();

        if (a == null || b == null)
        {
            if (!ReferenceEquals(a, b)) diffs.Add("一边是空的，一边不是。");
            return diffs;
        }

        Num(diffs, "version", a.Version, b.Version);
        Num(diffs, "seed", a.Seed, b.Seed);
        Num(diffs, "day", a.Day, b.Day);
        Str(diffs, "tense", a.Tense, b.Tense);
        Str(diffs, "hub", a.Hub, b.Hub);

        Num(diffs, "player.x", a.Player.X, b.Player.X);
        Num(diffs, "player.y", a.Player.Y, b.Player.Y);

        Flags(diffs, "flags", a.Flags, b.Flags);
        Items(diffs, a.Inventory, b.Inventory);
        Trust(diffs, a.Trust, b.Trust);
        Clues(diffs, a.Known.Clues, b.Known.Clues);
        Npcs(diffs, a.Known.Npcs, b.Known.Npcs);
        Hubs(diffs, a.Hubs, b.Hubs);

        return diffs;
    }

    private static void Num(List<string> diffs, string key, int a, int b)
    {
        if (a != b) diffs.Add($"{key}：{a} ≠ {b}");
    }

    private static void Num(List<string> diffs, string key, double a, double b)
    {
        if (Math.Abs(a - b) > 1e-9) diffs.Add($"{key}：{Fmt(a)} ≠ {Fmt(b)}");
    }

    private static void Str(List<string> diffs, string key, string? a, string? b)
    {
        if (!string.Equals(a, b, StringComparison.Ordinal)) diffs.Add($"{key}：{a ?? "(空)"} ≠ {b ?? "(空)"}");
    }

    private static void Flags(List<string> diffs, string key, Dictionary<string, bool> a, Dictionary<string, bool> b)
    {
        foreach (var name in Keys(a, b))
        {
            var va = a.TryGetValue(name, out var x) && x;
            var vb = b.TryGetValue(name, out var y) && y;
            if (va != vb) diffs.Add($"{key}.{name}：{va} ≠ {vb}");
        }
    }

    private static void Texts(List<string> diffs, string key, Dictionary<string, string> a, Dictionary<string, string> b)
    {
        foreach (var name in Keys(a, b))
        {
            var va = a.TryGetValue(name, out var x) ? x : "";
            var vb = b.TryGetValue(name, out var y) ? y : "";
            if (!string.Equals(va, vb, StringComparison.Ordinal)) diffs.Add($"{key}.{name}：{va} ≠ {vb}");
        }
    }

    private static void Items(List<string> diffs, List<InventoryEntry> a, List<InventoryEntry> b)    {
        if (a.Count != b.Count)
        {
            diffs.Add($"inventory：{a.Count} 件 ≠ {b.Count} 件");
            return;
        }

        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Item != y.Item) diffs.Add($"inventory[{i}].item：{x.Item} ≠ {y.Item}");
            if (x.N != y.N) diffs.Add($"inventory[{i}].n：{x.N} ≠ {y.N}");
        }
    }

    private static void Trust(List<string> diffs, Dictionary<string, TrustState> a, Dictionary<string, TrustState> b)
    {
        foreach (var region in Keys(a, b))
        {
            var x = a.TryGetValue(region, out var ax) ? ax : new TrustState();
            var y = b.TryGetValue(region, out var ay) ? ay : new TrustState();

            if (x.Min != y.Min) diffs.Add($"trust.{region}.min：{x.Min} ≠ {y.Min}");
            if (x.Guan != y.Guan) diffs.Add($"trust.{region}.guan：{x.Guan} ≠ {y.Guan}");
            if (x.Yi != y.Yi) diffs.Add($"trust.{region}.yi：{x.Yi} ≠ {y.Yi}");
        }
    }

    private static void Clues(List<string> diffs, List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
        {
            diffs.Add($"known.clues：{a.Count} 条 ≠ {b.Count} 条");
            return;
        }

        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i]) diffs.Add($"known.clues[{i}]：{a[i]} ≠ {b[i]}");
    }

    private static void Npcs(List<string> diffs, List<NpcState> a, List<NpcState> b)
    {
        if (a.Count != b.Count)
        {
            diffs.Add($"known.npcs：{a.Count} 人 ≠ {b.Count} 人");
            return;
        }

        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            var tag = $"known.npcs[{i}]({x.Hub}/{x.Id})";

            if (x.Hub != y.Hub) diffs.Add($"{tag}.hub：{x.Hub} ≠ {y.Hub}");
            if (x.Id != y.Id) diffs.Add($"{tag}.id：{x.Id} ≠ {y.Id}");
            if (x.Met != y.Met) diffs.Add($"{tag}.met：{x.Met} ≠ {y.Met}");
            if (x.Again != y.Again) diffs.Add($"{tag}.again：{x.Again} ≠ {y.Again}");
            if (x.Gift != y.Gift) diffs.Add($"{tag}.gift：{x.Gift} ≠ {y.Gift}");
            if (x.Quest != y.Quest) diffs.Add($"{tag}.quest：{x.Quest} ≠ {y.Quest}");
        }
    }

    private static void Hubs(List<string> diffs, Dictionary<string, HubState> a, Dictionary<string, HubState> b)
    {
        foreach (var hub in Keys(a, b))
        {
            var x = a.TryGetValue(hub, out var ax) ? ax : new HubState();
            var y = b.TryGetValue(hub, out var ay) ? ay : new HubState();

            if (x.Entered != y.Entered) diffs.Add($"hubs.{hub}.entered：{x.Entered} ≠ {y.Entered}");
            if (x.KeHalf != y.KeHalf) diffs.Add($"hubs.{hub}.ke_half：{x.KeHalf} ≠ {y.KeHalf}");
            if (x.CaughtCount != y.CaughtCount) diffs.Add($"hubs.{hub}.caught_count：{x.CaughtCount} ≠ {y.CaughtCount}");
            if (x.Nudges != y.Nudges) diffs.Add($"hubs.{hub}.nudges：{x.Nudges} ≠ {y.Nudges}");

            if (x.Searched.Count != y.Searched.Count)
            {
                diffs.Add($"hubs.{hub}.searched：{x.Searched.Count} 处 ≠ {y.Searched.Count} 处");
            }
            else
            {
                for (var i = 0; i < x.Searched.Count; i++)
                    if (x.Searched[i] != y.Searched[i]) diffs.Add($"hubs.{hub}.searched[{i}]：{x.Searched[i]} ≠ {y.Searched[i]}");
            }

            Flags(diffs, $"hubs.{hub}.flags", x.Flags, y.Flags);
            Texts(diffs, $"hubs.{hub}.patients", x.Patients, y.Patients);
        }
    }

    private static IEnumerable<string> Keys<T>(Dictionary<string, T> a, Dictionary<string, T> b)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in a.Keys) if (seen.Add(k)) yield return k;
        foreach (var k in b.Keys) if (seen.Add(k)) yield return k;
    }

    private static string Fmt(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
