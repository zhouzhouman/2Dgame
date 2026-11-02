using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>一条攻城门槛看的是哪一类东西（<c>needs</c> 列的四类：空 / trust / troops / flag）。</summary>
public enum SiegeNeedKind
{
    /// <summary>无门槛，随时可用。</summary>
    None,

    /// <summary>看一条信任轴到没到门槛。</summary>
    Trust,

    /// <summary>看手里兵够不够。</summary>
    Troops,

    /// <summary>看某个旗标置没置位。</summary>
    Flag,
}

/// <summary>
/// 一条攻城门槛：读表时解析好，规则层不再碰字符串。
/// </summary>
/// <param name="Kind">哪一类门槛。</param>
/// <param name="Key">信任轴（guan / min / yi）或旗标名；其余为空串。</param>
/// <param name="Value">门槛值；<see cref="SiegeNeedKind.Flag"/> 与 <see cref="SiegeNeedKind.None"/> 时无意义。</param>
public readonly record struct SiegeNeed(SiegeNeedKind Kind, string Key, int Value)
{
    /// <summary>无条件门槛。</summary>
    public static SiegeNeed None => new(SiegeNeedKind.None, "", 0);

    /// <summary>信任轴门槛。</summary>
    /// <param name="axis">guan / min / yi。</param>
    /// <param name="threshold">0–100。</param>
    /// <returns>门槛。</returns>
    public static SiegeNeed OfTrust(string axis, int threshold) => new(SiegeNeedKind.Trust, axis, threshold);

    /// <summary>兵力门槛。</summary>
    /// <param name="threshold">至少多少兵。</param>
    /// <returns>门槛。</returns>
    public static SiegeNeed OfTroops(int threshold) => new(SiegeNeedKind.Troops, "", threshold);

    /// <summary>旗标门槛。</summary>
    /// <param name="name">旗标名，已过 <see cref="FlagStore.IsValidName"/>。</param>
    /// <returns>门槛。</returns>
    public static SiegeNeed OfFlag(string name) => new(SiegeNeedKind.Flag, name, 0);

    /// <summary>是不是"无条件"。</summary>
    public bool IsNone => Kind == SiegeNeedKind.None;
}

/// <summary>
/// 一种攻城手段（设计文档 §8.6 四法）：<c>Content/data/siege.csv</c> 的一行。
///
/// 数值都是**代价与收益的增量**：<see cref="Casualty"/> 是伤亡百分比，<see cref="Minyuan"/> /
/// <see cref="Tianming"/> 是有符号增量（民怨 +20 = 百姓更恨你）。判定与扣人由 <see cref="SiegeBook"/> 干。
/// </summary>
/// <param name="Id">手段 id，全表唯一（surround / assault / inside / burn）。</param>
/// <param name="Label">给人看的名字（围 / 攻 / 间 / 水·火）。</param>
/// <param name="Ke">耗时刻数，<see cref="MinKe"/>–<see cref="MaxKe"/>。</param>
/// <param name="Casualty">伤亡百分比，0–<see cref="MaxCasualty"/>。</param>
/// <param name="Minyuan">民怨增量，−<see cref="MaxShift"/>..<see cref="MaxShift"/>。</param>
/// <param name="Tianming">天命增量，−<see cref="MaxShift"/>..<see cref="MaxShift"/>。</param>
/// <param name="Needs">用这一法要满足的门槛（能不能选）；<see cref="SiegeNeed.None"/> = 无条件。</param>
/// <param name="TroopsNeeded">打这一法要多少兵才**打得赢**（数据直给，各法一个数）。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record SiegeMethod(
    string Id,
    string Label,
    int Ke,
    int Casualty,
    int Minyuan,
    int Tianming,
    SiegeNeed Needs,
    int TroopsNeeded,
    string Note)
{
    /// <summary>耗时下限。攻城没有"不耗时刻"的手段。</summary>
    public const int MinKe = 1;

    /// <summary>耗时上限（与 <c>siege.csv</c> / E14 校验器一致）。</summary>
    public const int MaxKe = 8;

    /// <summary>伤亡百分比上限。</summary>
    public const int MaxCasualty = 100;

    /// <summary>民怨 / 天命的绝对值上限。</summary>
    public const int MaxShift = 100;

    /// <summary>表里能写的兵力门槛上限。超过它多半是多打了一个零。</summary>
    public const int MaxTroopsNeed = 9999;
}

/// <summary>
/// 攻城手段表：<c>Content/data/siege.csv</c> 的内存形式，按文件顺序保存。
///
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：缺列、空 id、重复 id、ke / casualty /
/// minyuan / tianming 越界、troops_needed 不是正整数、needs 语法不认识（含旗标名不合规），
/// 全部带行号抛 <see cref="InvalidDataException"/>——一张读歪的攻城表不会崩，只会让某一法永远点不动。
/// </summary>
public sealed class SiegeTable
{
    /// <summary>表里必须有的列，缺一列即视为坏表。</summary>
    private static readonly string[] RequiredColumns =
    {
        "id", "label", "ke", "casualty", "minyuan", "tianming", "needs", "troops_needed", "note",
    };

    private readonly List<SiegeMethod> _all;
    private readonly Dictionary<string, SiegeMethod> _byId;

    /// <summary>全部手段，按文件顺序（= UI 里选项的顺序）。</summary>
    public IReadOnlyList<SiegeMethod> All => _all;

    /// <summary>手段条数。</summary>
    public int Count => _all.Count;

    private SiegeTable(List<SiegeMethod> all)
    {
        _all = all;
        _byId = new Dictionary<string, SiegeMethod>(all.Count, StringComparer.Ordinal);
        foreach (var m in all) _byId[m.Id] = m;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">siege.csv 的路径。</param>
    /// <returns>攻城手段表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、数值越界、needs 语法不认识。</exception>
    public static SiegeTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>攻城手段表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、数值越界、needs 语法不认识。</exception>
    public static SiegeTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("攻城表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"攻城表第 1 行：缺少列「{name}」。");

        var list = new List<SiegeMethod>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException($"攻城表第 {line} 行：id 为空。");

            if (!seen.Add(id))
                throw new InvalidDataException($"攻城表第 {line} 行：id「{id}」重复。");

            list.Add(new SiegeMethod(
                id,
                Get("label"),
                ParseKe(Get("ke"), line),
                ParseCasualty(Get("casualty"), line),
                ParseShift(Get("minyuan"), "minyuan", line),
                ParseShift(Get("tianming"), "tianming", line),
                ParseNeeds(Get("needs"), line),
                ParseTroopsNeeded(Get("troops_needed"), line),
                Get("note")));
        }

        return new SiegeTable(list);
    }

    /// <summary>
    /// 按 id 取一种手段。null / 空串一律视为找不到，不抛。
    /// </summary>
    /// <param name="id">手段 id。</param>
    /// <param name="method">取到的手段；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out SiegeMethod? method)
    {
        if (string.IsNullOrEmpty(id))
        {
            method = null;
            return false;
        }

        return _byId.TryGetValue(id!, out method);
    }

    /// <summary>解析耗时：整数 <see cref="SiegeMethod.MinKe"/>–<see cref="SiegeMethod.MaxKe"/>。</summary>
    private static int ParseKe(string text, int line)
    {
        var value = ParseInt(text, "ke", line);

        if (value < SiegeMethod.MinKe || value > SiegeMethod.MaxKe)
            throw new InvalidDataException(
                $"攻城表第 {line} 行：ke「{text}」超出 {SiegeMethod.MinKe}..{SiegeMethod.MaxKe}。");

        return value;
    }

    /// <summary>解析伤亡：整数 0–100 的百分比。</summary>
    private static int ParseCasualty(string text, int line)
    {
        var value = ParseInt(text, "casualty", line);

        if (value < 0 || value > SiegeMethod.MaxCasualty)
            throw new InvalidDataException(
                $"攻城表第 {line} 行：casualty「{text}」超出 0..{SiegeMethod.MaxCasualty}。");

        return value;
    }

    /// <summary>解析有符号增量：−100..100。</summary>
    private static int ParseShift(string text, string column, int line)
    {
        var value = ParseInt(text, column, line);

        if (value < -SiegeMethod.MaxShift || value > SiegeMethod.MaxShift)
            throw new InvalidDataException(
                $"攻城表第 {line} 行：{column}「{text}」超出 ±{SiegeMethod.MaxShift}。");

        return value;
    }

    /// <summary>
    /// 解析 <c>troops_needed</c> 列：正整数 1–<see cref="SiegeMethod.MaxTroopsNeed"/>。
    /// 空、零、负数、非数字一律算坏表——"这一法不用兵"不是设计里有的东西，
    /// 让它悄悄通过只会造出一个兵不够也必胜的门槛。
    /// </summary>
    private static int ParseTroopsNeeded(string text, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1)
            throw new InvalidDataException($"攻城表第 {line} 行：troops_needed「{text}」须为正整数。");

        if (value > SiegeMethod.MaxTroopsNeed)
            throw new InvalidDataException(
                $"攻城表第 {line} 行：troops_needed「{text}」超出 1..{SiegeMethod.MaxTroopsNeed}。");

        return value;
    }

    /// <summary>
    /// 解析 <c>needs</c> 列。只认单条条件：
    /// <c>trust:guan&gt;=60</c> / <c>trust:min&gt;=60</c> / <c>trust:yi&gt;=60</c> /
    /// <c>troops&gt;=60</c> / <c>flag:xxx</c>；空 = 无条件。
    /// 组合条件（含 <c>&amp;</c> / <c>|</c>）、缺 <c>&gt;=</c>、轴名不认识、旗标名不合规，全部算坏表。
    /// </summary>
    private static SiegeNeed ParseNeeds(string raw, int line)
    {
        var text = raw.Trim();
        if (text.Length == 0) return SiegeNeed.None;

        if (text.StartsWith("trust:", StringComparison.OrdinalIgnoreCase))
        {
            var body = text["trust:".Length..];
            var parts = body.Split(">=", StringSplitOptions.None);
            if (parts.Length != 2)
                throw BadNeeds(line, raw, "信任门槛要写成 trust:guan>=60");

            var axis = parts[0].Trim().ToLowerInvariant();
            if (axis != "guan" && axis != "min" && axis != "yi")
                throw BadNeeds(line, raw, $"信任轴「{axis}」不认识（只能是 guan / min / yi）");

            var threshold = ParseInt(parts[1].Trim(), "trust 门槛", line);
            if (threshold < 0 || threshold > TrustBook.MaxValue)
                throw BadNeeds(line, raw, $"信任门槛「{threshold}」超出 0..{TrustBook.MaxValue}");

            return SiegeNeed.OfTrust(axis, threshold);
        }

        if (text.StartsWith("troops", StringComparison.OrdinalIgnoreCase))
        {
            var body = text["troops".Length..].Trim();
            if (!body.StartsWith(">=", StringComparison.Ordinal))
                throw BadNeeds(line, raw, "兵力门槛要写成 troops>=60");

            var threshold = ParseInt(body[2..].Trim(), "troops 门槛", line);
            if (threshold < 0 || threshold > SiegeMethod.MaxTroopsNeed)
                throw BadNeeds(line, raw, $"兵力门槛「{threshold}」超出 0..{SiegeMethod.MaxTroopsNeed}");

            return SiegeNeed.OfTroops(threshold);
        }

        if (text.StartsWith("flag:", StringComparison.OrdinalIgnoreCase))
        {
            var name = text["flag:".Length..].Trim();
            if (!FlagStore.IsValidName(name))
                throw BadNeeds(line, raw, $"旗标名「{name}」不合法（须符合 FlagStore.IsValidName）");

            return SiegeNeed.OfFlag(name);
        }

        throw BadNeeds(line, raw, "只认 trust:轴>=值 / troops>=值 / flag:旗标名 / 空");
    }

    /// <summary>整数解析失败就带列名与行号抛。</summary>
    private static int ParseInt(string text, string column, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"攻城表第 {line} 行：{column}「{text}」不是整数。");

        return value;
    }

    /// <summary>坏 needs 的统一说法：带行号、带原文、带正确写法。</summary>
    private static InvalidDataException BadNeeds(int line, string raw, string why) =>
        new($"攻城表第 {line} 行：needs「{raw}」不认识——{why}。");
}
