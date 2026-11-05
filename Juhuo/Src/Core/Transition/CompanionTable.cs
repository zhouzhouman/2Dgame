using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一位功臣：<c>Content/data/companions.csv</c> 的一行（设计文档 §16.6 第 4 步）。
/// </summary>
/// <param name="Id">同伴 id，全表唯一，也是存档里记封赏的键。</param>
/// <param name="Name">名字，给玩家看（封赏那句话以它开头）。</param>
/// <param name="Office">本来担的差事（如"掌书记"），封官时参照它给高低。</param>
/// <param name="Role">在军中的角色（文 / 武 / 谍……），只给人看。</param>
/// <param name="Loyalty">忠诚 0–100，起点。</param>
/// <param name="Ambition">野心 0–100，≥ <see cref="CompanionTable.AmbitionThreshold"/> 的人会被封赏左右。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record Companion(
    string Id,
    string Name,
    string Office,
    string Role,
    int Loyalty,
    int Ambition,
    string Note);

/// <summary>
/// 六位功臣表：<c>Content/data/companions.csv</c> 的内存形式，按文件顺序保存。
///
/// 为什么卡死六行：分封这一屏就是"六个人一人一档"，多一个少一个都是表写坏了，
/// 不会"少封一个人也没关系"——那会让玩家以为自己漏点了什么。
///
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：人数不是 6、id / office 重复、
/// 忠诚或野心越界、id 拼出来的封赏旗标名不合法，全部带行号抛 <see cref="InvalidDataException"/>。
/// 最后一条是防呆：旗标名不合法时 <see cref="FlagStore.Set"/> 会静默忽略，
/// "未来权臣"就永远不会发生，而没人知道是自己把 id 写错了。
/// </summary>
public sealed class CompanionTable
{
    /// <summary>功臣必须正好几位（§16.6：给 6 位同伴封官）。</summary>
    public const int RequiredCount = 6;

    /// <summary>忠诚与野心的下限。</summary>
    public const int MinStat = 0;

    /// <summary>忠诚与野心的上限。</summary>
    public const int MaxStat = 100;

    /// <summary>
    /// 野心到这个数就算"想要更多"：厚封会骄（未来权臣），薄封会怨（离心）。
    /// 59 与 60 是两种人，差一点都不行。
    /// </summary>
    public const int AmbitionThreshold = 60;

    /// <summary>表里必须有的列。缺列即坏表；<c>note</c> 只给人看，可缺。</summary>
    private static readonly string[] RequiredColumns =
    {
        "id", "name", "office", "role", "loyalty", "ambition",
    };

    private readonly List<Companion> _all;
    private readonly Dictionary<string, Companion> _byId;
    private readonly Dictionary<string, Companion> _byName;

    /// <summary>全部功臣，按文件顺序。</summary>
    public IReadOnlyList<Companion> All => _all;

    /// <summary>功臣人数，恒为 <see cref="RequiredCount"/>。</summary>
    public int Count => _all.Count;

    private CompanionTable(List<Companion> all)
    {
        _all = all;
        _byId = new Dictionary<string, Companion>(all.Count, StringComparer.Ordinal);
        foreach (var c in all) _byId[c.Id] = c;

        _byName = new Dictionary<string, Companion>(all.Count, StringComparer.Ordinal);
        foreach (var c in all) _byName.TryAdd(c.Name, c);
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">companions.csv 的路径。</param>
    /// <returns>功臣表。</returns>
    /// <exception cref="InvalidDataException">人数不是 6、缺列、空 id / 空名、id 或 office 重复、忠诚或野心越界。</exception>
    public static CompanionTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>功臣表。</returns>
    /// <exception cref="InvalidDataException">人数不是 6、缺列、空 id / 空名、id 或 office 重复、忠诚或野心越界。</exception>
    public static CompanionTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("功臣表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"功臣表第 1 行：缺少列「{name}」。");

        var body = rows.Count - 1;
        if (body != RequiredCount)
            throw new InvalidDataException(
                $"功臣表第 2 行起：功臣必须正好 {RequiredCount} 行，现在 {body} 行。");

        var list = new List<Companion>(RequiredCount);
        var ids = new HashSet<string>(RequiredCount, StringComparer.Ordinal);
        var offices = new HashSet<string>(RequiredCount, StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (id.Length == 0)
                throw new InvalidDataException($"功臣表第 {line} 行：id 为空。");

            if (!ids.Add(id))
                throw new InvalidDataException($"功臣表第 {line} 行：id「{id}」重复。");

            var name = Get("name");
            if (name.Length == 0)
                throw new InvalidDataException($"功臣表第 {line} 行：name 为空（id「{id}」）。");

            // 一官不封二臣：office 撞了，分封屏上两行一模一样，玩家分不清谁是谁。
            var office = Get("office");
            if (office.Length > 0 && !offices.Add(office))
                throw new InvalidDataException($"功臣表第 {line} 行：office「{office}」重复。");

            var loyalty = ParseStat(Get("loyalty"), "loyalty", line);
            var ambition = ParseStat(Get("ambition"), "ambition", line);

            foreach (var flag in EnfeoffBook.FlagsOf(id))
                if (!FlagStore.IsValidName(flag))
                    throw new InvalidDataException(
                        $"功臣表第 {line} 行：id「{id}」拼出来的旗标名「{flag}」不合法（须符合 FlagStore.IsValidName）。");

            list.Add(new Companion(id, name, office, Get("role"), loyalty, ambition, Get("note")));
        }

        return new CompanionTable(list);
    }

    /// <summary>
    /// 按 id 取功臣。
    /// </summary>
    /// <param name="id">功臣 id；null / 空串一律视为找不到。</param>
    /// <param name="companion">找到的功臣；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out Companion? companion)
    {
        if (string.IsNullOrEmpty(id))
        {
            companion = null;
            return false;
        }

        return _byId.TryGetValue(id!, out companion);
    }

    /// <summary>
    /// 按名字取功臣（UI 手里常常只有名字）。同名取表里靠前的那位；找不到返回 null。
    /// </summary>
    /// <param name="name">功臣名。</param>
    /// <returns>功臣；找不到为 null。</returns>
    public Companion? Get(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _byName.TryGetValue(name!, out var companion) ? companion : null;
    }

    /// <summary>解析一列 0–100 的整数，越界或不是整数都带行号抛。</summary>
    private static int ParseStat(string text, string column, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"功臣表第 {line} 行：{column}「{text}」不是整数。");

        if (value < MinStat || value > MaxStat)
            throw new InvalidDataException(
                $"功臣表第 {line} 行：{column}「{text}」超出 {MinStat}–{MaxStat}。");

        return value;
    }
}
