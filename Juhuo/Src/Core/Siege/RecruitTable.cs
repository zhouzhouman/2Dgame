using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 招兵的一个选项：<c>Content/data/recruit.csv</c> 的一行。
///
/// 对应设计文档 §18.5 场景 7「山神庙招兵」：给粮 / 给承诺 / 抢兵器。
/// <see cref="Troops"/> 是**增量**，<see cref="TrustAction"/> 是 trust.csv 里的动作 id，可能为空
/// （承诺的代价是誓约卡、抢兵器没有对应信任动作，这两档都不动三轴）。
///
/// **符号待 D1**：文档写「抢兵器」是"兵 −"，但 recruit.csv 现在填 15（被胁从的少数人），
/// 按增量直接加会把人数算多。接线时要么等 D1 给负数，要么由调用方对这一档单独处理。
/// </summary>
/// <param name="Index">option 列解析出来的编号（= <see cref="RecruitTable.ForOption"/> 的下标）。</param>
/// <param name="Option">option 列的原文，给日志与排查用。</param>
/// <param name="Troops">兵力增量，可负。</param>
/// <param name="TrustAction">该调的信任动作 id；空串 = 不动信任。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record RecruitOption(int Index, string Option, int Troops, string TrustAction, string Note)
{
    /// <summary>单次招兵的兵力增量绝对值上限。超过它多半是多打了一个零。</summary>
    public const int MaxTroops = 9999;
}

/// <summary>
/// 招兵选项表：<c>Content/data/recruit.csv</c> 的内存形式，按文件顺序保存。
///
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：缺列、空 option、option 重复、
/// troops 不是整数或越界，全部带行号抛 <see cref="InvalidDataException"/>。
/// </summary>
public sealed class RecruitTable
{
    /// <summary>表里必须有的列。</summary>
    private static readonly string[] RequiredColumns = { "option", "troops", "trust", "note" };

    private readonly List<RecruitOption> _all;
    private readonly Dictionary<int, RecruitOption> _byIndex;

    /// <summary>全部选项，按文件顺序。</summary>
    public IReadOnlyList<RecruitOption> All => _all;

    /// <summary>选项条数。</summary>
    public int Count => _all.Count;

    private RecruitTable(List<RecruitOption> all)
    {
        _all = all;
        _byIndex = new Dictionary<int, RecruitOption>(all.Count);
        foreach (var option in all) _byIndex[option.Index] = option;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">recruit.csv 的路径。</param>
    /// <returns>招兵选项表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 option、option 重复、troops 不是整数或越界。</exception>
    public static RecruitTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>招兵选项表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 option、option 重复、troops 不是整数或越界。</exception>
    public static RecruitTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("招兵表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"招兵表第 1 行：缺少列「{name}」。");

        var list = new List<RecruitOption>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<int>();

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var option = Get("option");
            if (string.IsNullOrEmpty(option))
                throw new InvalidDataException($"招兵表第 {line} 行：option 为空。");

            if (!int.TryParse(option, NumberStyles.Integer, CultureInfo.InvariantCulture, out var optionIndex))
                throw new InvalidDataException($"招兵表第 {line} 行：option「{option}」不是整数编号。");

            if (optionIndex < 0)
                throw new InvalidDataException($"招兵表第 {line} 行：option「{option}」必须是非负整数。");

            if (!seen.Add(optionIndex))
                throw new InvalidDataException($"招兵表第 {line} 行：option「{option}」重复。");

            var troopsText = Get("troops");
            if (!int.TryParse(troopsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var troops))
                throw new InvalidDataException($"招兵表第 {line} 行：troops「{troopsText}」不是整数。");

            if (troops < -RecruitOption.MaxTroops || troops > RecruitOption.MaxTroops)
                throw new InvalidDataException(
                    $"招兵表第 {line} 行：troops「{troopsText}」超出 ±{RecruitOption.MaxTroops}。");

            list.Add(new RecruitOption(optionIndex, option, troops, Get("trust"), Get("note")));
        }

        return new RecruitTable(list);
    }

    /// <summary>
    /// 按 option 列的数字编号取一个选项——**不依赖行序**，行写乱了也取对。
    /// 越界返回 null，不抛：玩家多点一下不该让游戏崩。
    /// </summary>
    /// <param name="index">option 列的编号（0 基）。</param>
    /// <returns>选项；没有这个编号为 null。</returns>
    public RecruitOption? ForOption(int index) =>
        _byIndex.TryGetValue(index, out var option) ? option : null;
}
