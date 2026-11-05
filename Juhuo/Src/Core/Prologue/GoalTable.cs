using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 目标文本表：<c>Content/data/goals.csv</c>（三行：起手 / 已转段 / 十二拍走完却没转段）。
///
/// 只干一件事——决定"现在该给玩家看哪一句目标"。哪一句算"现在"，规矩在
/// <see cref="Current"/>：转段优先，其次是走完十二拍，再次是正等着的那一拍自己的话，
/// 都没有才是起手那句。
///
/// 坏表照 <see cref="BeatTable"/> 的规矩读表即抛：缺列、<c>when</c> 认不出来 / 重复、
/// 正文为空，全部带行号抛 <see cref="InvalidDataException"/>——少一句目标不会崩，
/// 但会让玩家在某一整段里看见空白的"当前目标"，很难从反馈里认出是表写错了。
/// </summary>
public sealed class GoalTable
{
    /// <summary><c>when</c> 列的三种取值。</summary>
    public const string WhenStart = "start";

    /// <summary>已转段（国号已定，进正篇）。</summary>
    public const string WhenRealm = "realm";

    /// <summary>十二拍走完了，却还没转段——这时该催一句。</summary>
    public const string WhenDeficit = "deficit";

    /// <summary>表里必须有的列。缺列即坏表；<c>note</c> 只给人看，可缺。</summary>
    private static readonly string[] RequiredColumns = { "when", "text" };

    /// <summary>三档 <c>when</c>，按报错时的固定顺序列出来。</summary>
    private static readonly string[] AllWhens = { WhenStart, WhenRealm, WhenDeficit };

    /// <summary>起手那句：还在序章里、手上没有正等着的一拍。</summary>
    public string Start { get; }

    /// <summary>已转段那句。</summary>
    public string Realm { get; }

    /// <summary>十二拍走完、还没转段那句。</summary>
    public string Deficit { get; }

    private GoalTable(string start, string realm, string deficit)
    {
        Start = start;
        Realm = realm;
        Deficit = deficit;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">goals.csv 的路径。</param>
    /// <returns>目标表。</returns>
    /// <exception cref="InvalidDataException">缺列、空表、when 不认识 / 重复、正文为空、三档不齐。</exception>
    public static GoalTable Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>目标表。</returns>
    /// <exception cref="InvalidDataException">缺列、空表、when 不认识 / 重复、正文为空、三档不齐。</exception>
    public static GoalTable FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("目标表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"目标表第 1 行：缺少列「{name}」。");

        var texts = new Dictionary<string, string>(AllWhens.Length, StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var when = Get("when");
            if (when.Length == 0)
                throw new InvalidDataException($"目标表第 {line} 行：when 为空（该写 start / realm / deficit）。");

            if (Array.IndexOf(AllWhens, when) < 0)
                throw new InvalidDataException(
                    $"目标表第 {line} 行：when「{when}」认不出来（只认 start / realm / deficit）。");

            if (texts.ContainsKey(when))
                throw new InvalidDataException($"目标表第 {line} 行：when「{when}」重复。");

            var text = Get("text");
            if (text.Length == 0)
                throw new InvalidDataException($"目标表第 {line} 行：{when} 的 text 是空的。");

            texts[when] = text;
        }

        // 三档缺一档就抛：Current 一旦落到缺的那一档，玩家会看到空白目标，而不是一句错话。
        foreach (var when in AllWhens)
            if (!texts.ContainsKey(when))
                throw new InvalidDataException($"目标表缺少「{when}」这一档。");

        return new GoalTable(texts[WhenStart], texts[WhenRealm], texts[WhenDeficit]);
    }

    /// <summary>
    /// 现在该显示的那句目标。
    ///
    /// 先后是有讲究的，按这个顺序问：
    /// 1. 已经转段了 → <see cref="Realm"/>（这时候"在等哪一拍"已经不重要了）；
    /// 2. 十二拍走完却没转段 → <see cref="Deficit"/>（该催他去定都定国号，而不是继续催节拍）；
    /// 3. 有正等着的一拍，且那一拍写了目标 → 那一拍自己的话；
    /// 4. 其余 → <see cref="Start"/>。
    /// </summary>
    /// <param name="pending">正在等的那一拍；没有传 null。</param>
    /// <param name="done">十二拍是不是都走完了（<c>beats_done</c> ≥ 12）。</param>
    /// <param name="turnedRealm">是不是已经转段（国号已定 / <c>prologue.done</c>）。</param>
    /// <returns>给玩家看的一句话，不会是空串。</returns>
    public string Current(Beat? pending, bool done, bool turnedRealm)
    {
        if (turnedRealm) return Realm;
        if (done) return Deficit;

        // 拍上没写 goal（旧表 / 这一拍留白）时退回起手那句，不要给 UI 一个空串。
        if (pending != null && !string.IsNullOrWhiteSpace(pending.Goal)) return pending.Goal;

        return Start;
    }
}
