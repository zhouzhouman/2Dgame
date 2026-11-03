using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一个人的立绘长相：<c>Content/data/portraits.csv</c> 的一行。
///
/// 七个数字就是七层零件（肤色 / 头发 / 脸型 / 胡须 / 帽子 / 袍子 / 记号），
/// 表现层拿它们去拼图；这里只保证**每个数字都在美术给过的范围内**，
/// 免得拼到第 30 个人时才发现有一层没有对应图。
/// </summary>
/// <param name="Npc">npc 名，与 <c>npcs.csv</c> / <c>intro.csv</c> 的 speaker 对得上。</param>
/// <param name="Skin">肤色，1–4。</param>
/// <param name="Hair">头发，1–6。</param>
/// <param name="Style">脸型，1–4。</param>
/// <param name="Beard">胡须，0–3（0 = 无须）。</param>
/// <param name="Hat">帽子，0–4（0 = 不戴）。</param>
/// <param name="Robe">袍子，1–6。</param>
/// <param name="Mark">记号（疤、痣等），0–4（0 = 无）。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record Portrait(
    string Npc,
    int Skin,
    int Hair,
    int Style,
    int Beard,
    int Hat,
    int Robe,
    int Mark,
    string Note);

/// <summary>
/// 立绘参数表：<c>Content/data/portraits.csv</c> 的内存形式，按文件顺序保存。
///
/// 坏表读表即抛（照 <see cref="BeatTable"/>）：缺列、空 npc、任一参数不是整数或越界、
/// **同名却长得不一样**。越界的一个数字在表现层只会变成"这个人少了半张脸"，
/// 没人会想到是表写错了。
///
/// 同名可以写多行：同一个人在两个空间登场时（比如沈砚在 hub2 与 hub7），
/// 表里就按各自的 note 写两行，七层零件必须一致——不一致就是有个人认错了脸。
/// 按名字查永远拿到第一行，同一个名字在 <see cref="All"/> 里可能不止一次。
///
/// 纯 C#，不依赖引擎；<see cref="Load"/> 只负责把文件读成文本，解析在 <see cref="FromCsv"/>。
/// </summary>
public sealed class PortraitTable
{
    /// <summary>七层零件各自的范围（票面值，来自美术规范）。</summary>
    private static readonly (string Name, int Min, int Max)[] IntFields =
    {
        ("skin", 1, 4),
        ("hair", 1, 6),
        ("style", 1, 4),
        ("beard", 0, 3),
        ("hat", 0, 4),
        ("robe", 1, 6),
        ("mark", 0, 4),
    };

    /// <summary>表里必须有的列：npc + 七层零件 + note。</summary>
    private static readonly string[] RequiredColumns =
    {
        "npc", "skin", "hair", "style", "beard", "hat", "robe", "mark", "note",
    };

    private readonly List<Portrait> _all;
    private readonly Dictionary<string, Portrait> _byNpc;

    /// <summary>全部立绘，按文件顺序；同一个人在两处登场就会有两条。</summary>
    public IReadOnlyList<Portrait> All => _all;

    /// <summary>几行（不是几个人：同名的人可能占两行）。</summary>
    public int Count => _all.Count;

    private PortraitTable(List<Portrait> all)
    {
        _all = all;
        _byNpc = new Dictionary<string, Portrait>(all.Count, StringComparer.Ordinal);
        foreach (var p in all) _byNpc[p.Npc] = p;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">portraits.csv 的路径。</param>
    /// <returns>立绘表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 npc、参数越界、同名异相。</exception>
    public static PortraitTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>立绘表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 npc、参数越界、同名异相。</exception>
    public static PortraitTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("立绘表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"立绘表第 1 行：缺少列「{name}」。");

        var list = new List<Portrait>(Math.Max(0, rows.Count - 1));
        var firstSeen = new Dictionary<string, (Portrait Look, int Line)>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var npc = Get("npc");
            if (string.IsNullOrEmpty(npc))
                throw new InvalidDataException($"立绘表第 {line} 行：npc 为空。");

            var values = new int[IntFields.Length];
            for (var f = 0; f < IntFields.Length; f++)
            {
                var (name, min, max) = IntFields[f];
                var text = Get(name);

                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    throw new InvalidDataException($"立绘表第 {line} 行：{name}「{text}」不是整数。");

                if (value < min || value > max)
                    throw new InvalidDataException(
                        $"立绘表第 {line} 行：{name}「{text}」超出 {min}..{max}。");

                values[f] = value;
            }

            var portrait = new Portrait(
                npc,
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5],
                values[6],
                Get("note"));

            if (firstSeen.TryGetValue(npc, out var seen))
            {
                if (!SameLook(seen.Look, portrait))
                    throw new InvalidDataException(
                        $"立绘表第 {line} 行：npc「{npc}」与第 {seen.Line} 行同名，但七层零件不一样——" +
                        "同一个人在两处登场必须长得一样。");

                // 同名同相 = 同一个人在两处登场：两行都留着（note 各写各的空间），按名字查取第一行。
            }
            else
            {
                firstSeen[npc] = (portrait, line);
            }

            list.Add(portrait);
        }

        return new PortraitTable(list);
    }

    /// <summary>七层零件一模一样就是同一个人。note 不参与比较——两处登场的备注本来就不同。</summary>
    private static bool SameLook(Portrait a, Portrait b) =>
        a.Skin == b.Skin &&
        a.Hair == b.Hair &&
        a.Style == b.Style &&
        a.Beard == b.Beard &&
        a.Hat == b.Hat &&
        a.Robe == b.Robe &&
        a.Mark == b.Mark;

    /// <summary>
    /// 按 npc 名取立绘。
    /// </summary>
    /// <param name="npcName">npc 名；null / 空串一律视为没有。</param>
    /// <param name="portrait">找到的立绘；没找到为 null（表现层就不画人）。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? npcName, [NotNullWhen(true)] out Portrait? portrait)
    {
        if (string.IsNullOrEmpty(npcName))
        {
            portrait = null;
            return false;
        }

        return _byNpc.TryGetValue(npcName!, out portrait);
    }
}
