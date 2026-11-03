using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 开场白的一行：<c>Content/data/intro.csv</c> 的一行。
///
/// 两个"空"是有意思的，不是漏填：
/// - <see cref="Speaker"/> 为空 = **旁白**（没有人说话，只是一段字）；
/// - <see cref="Portrait"/> 为空 = **不显示立绘**（声音在人后，或者在说天说地）。
/// 反过来不成立：旁白不能带立绘——没人站在那里，立绘就无处可挂。
/// </summary>
/// <param name="Seq">第几句，从 1 起且必须连号（见 <see cref="IntroTable"/>）。</param>
/// <param name="Speaker">说话人；空串表示旁白。</param>
/// <param name="Portrait">立绘的 npc 名（portraits.csv 的 npc 列）；空串表示不显示立绘。</param>
/// <param name="Text">这一句的正文。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record IntroLine(int Seq, string Speaker, string Portrait, string Text, string Note)
{
    /// <summary>这一句是不是旁白（没人说话）。</summary>
    public bool IsNarration => string.IsNullOrEmpty(Speaker);

    /// <summary>这一句要不要画立绘。</summary>
    public bool HasPortrait => !string.IsNullOrEmpty(Portrait);
}

/// <summary>
/// 开场白表：<c>Content/data/intro.csv</c> 的内存形式，按文件顺序（= 播出的顺序）保存。
///
/// 坏表读表即抛（照 <see cref="BeatTable"/>）：缺列、seq 不是整数或不连号、
/// 行数不在 <see cref="MinLines"/>..<see cref="MaxLines"/>、旁白却带了立绘、说话人重复占位。
/// 开场白是玩家看到的第一段字，播到一半断掉比一开始就读歪难查得多。
///
/// 纯 C#，不依赖引擎；<see cref="Load"/> 只负责把文件读成文本，解析在 <see cref="FromCsv"/>。
/// </summary>
public sealed class IntroTable
{
    /// <summary>开场白最少几句。少于这个数，玩家还没坐稳就进了游戏。</summary>
    public const int MinLines = 6;

    /// <summary>开场白最多几句。多到这个数以上，第一屏就变成了读小说。</summary>
    public const int MaxLines = 10;

    /// <summary>表里必须有的列，缺一列即视为坏表。</summary>
    private static readonly string[] RequiredColumns = { "seq", "speaker", "portrait", "text", "note" };

    private readonly List<IntroLine> _all;

    /// <summary>全部开场白，按文件顺序（= 第 1 句到第 N 句）。</summary>
    public IReadOnlyList<IntroLine> All => _all;

    /// <summary>几句。</summary>
    public int Count => _all.Count;

    private IntroTable(List<IntroLine> all) => _all = all;

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">intro.csv 的路径。</param>
    /// <returns>开场白表。</returns>
    /// <exception cref="InvalidDataException">缺列、seq 不连号、行数越界、旁白带立绘。</exception>
    public static IntroTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    ///
    /// seq 按**行序**必须依次是 1、2、3……：这样"漏了一行"和"两行写反了"都会当场报出来，
    /// 而不是播到一半才发现第 5 句说的是第 7 句的事。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>开场白表。</returns>
    /// <exception cref="InvalidDataException">缺列、seq 不连号、行数越界、旁白带立绘。</exception>
    public static IntroTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("开场白表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"开场白表第 1 行：缺少列「{name}」。");

        var list = new List<IntroLine>(Math.Max(0, rows.Count - 1));

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var seqText = Get("seq");
            if (!int.TryParse(seqText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
                throw new InvalidDataException($"开场白表第 {line} 行：seq「{seqText}」不是整数。");

            var expected = list.Count + 1;
            if (seq != expected)
                throw new InvalidDataException(
                    $"开场白表第 {line} 行：seq 是 {seq}，但按行序应该是 {expected}（seq 必须从 1 起连号）。");

            var speaker = Get("speaker");
            var portrait = Get("portrait");
            if (string.IsNullOrEmpty(speaker) && !string.IsNullOrEmpty(portrait))
                throw new InvalidDataException(
                    $"开场白表第 {line} 行：speaker 为空（旁白）却填了 portrait「{portrait}」——没人站在那里，立绘无处可挂。");

            list.Add(new IntroLine(seq, speaker, portrait, Get("text"), Get("note")));
        }

        if (list.Count < MinLines || list.Count > MaxLines)
            throw new InvalidDataException(
                $"开场白表共 {list.Count} 句，要求 {MinLines}..{MaxLines} 句（第 2 行起的正文行数）。");

        return new IntroTable(list);
    }
}
