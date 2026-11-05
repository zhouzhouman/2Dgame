using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 教学的一步：<c>Content/data/tutorial.csv</c> 的一行。
///
/// <see cref="Title"/> 是这一行的主指令（"按 M 看看这座城"），<see cref="Hint"/> 是补一句
/// （"七处地方，走一趟要花刻"）。玩家看见的那一条就是这两截拼起来的——
/// <b>中文只在这张表里</b>，代码里不留第二份。
/// </summary>
/// <param name="Seq">第几步，1 起且必须连号。</param>
/// <param name="Key">这一步的键（move / look / …），与大写里的 <see cref="Onboarding.Stage"/> 一一对应。</param>
/// <param name="Title">主指令。</param>
/// <param name="Hint">补充一句；可以是空串。</param>
public sealed record TutorialStep(int Seq, string Key, string Title, string Hint)
{
    /// <summary>教学条上这一行显示的字：<c>按 M 看看这座城　·　七处地方，走一趟要花刻</c>。</summary>
    public string Text => Hint.Length == 0 ? Title : $"{Title}　·　{Hint}";
}

/// <summary>
/// 教学表：<c>Content/data/tutorial.csv</c> 的内存形式，按文件顺序（= 教的顺序）保存。
///
/// 为什么要把顺序也验死：<see cref="Onboarding.Stage"/> 是代码里的枚举，表里的第 N 行
/// 会直接落到第 N 个 Stage。表一改顺序，玩家就会看见"第 4 步说按 M"却怎么按都不推进。
/// 所以读表时**当场**对齐两份：seq 必须 1..8 连号，key 必须依次是下面这八个。
///
/// 坏表读表即抛（照 <see cref="BeatTable"/>）：缺列、行数不对、seq 不连号、key 不认识或重复、
/// title 为空。教学是玩家最初的十分钟，读歪了比一开始就读不出来更难查。
/// </summary>
public sealed class TutorialScript
{
    /// <summary>一共几步。八步：走 / 看 / 翻 / 图 / 囊 / 三轴 / 说 / 出门。</summary>
    public const int ExpectedCount = 8;

    /// <summary>第一步：走两步。</summary>
    public const string KeyMove = "move";

    /// <summary>第二步：按住 Ctrl 看清楚。</summary>
    public const string KeyLook = "look";

    /// <summary>第三步：伸手翻东西。</summary>
    public const string KeySearch = "search";

    /// <summary>第四步：按 M 打开地图。</summary>
    public const string KeyMap = "map";

    /// <summary>第五步：按 I 打开囊。</summary>
    public const string KeyBag = "bag";

    /// <summary>第六步：按 Tab 看三轴。</summary>
    public const string KeyTrust = "trust";

    /// <summary>第七步：跟人说话。</summary>
    public const string KeyTalk = "talk";

    /// <summary>第八步：走到出口。</summary>
    public const string KeyLeave = "leave";

    /// <summary>八个 key，顺序即教的顺序；必须与 <c>Onboarding.Stage</c> 对齐。</summary>
    public static readonly string[] ExpectedKeys =
    {
        KeyMove,
        KeyLook,
        KeySearch,
        KeyMap,
        KeyBag,
        KeyTrust,
        KeyTalk,
        KeyLeave,
    };

    /// <summary>表里必须有的列。缺列即坏表；<c>note</c> 只给人看，可缺。</summary>
    private static readonly string[] RequiredColumns = { "seq", "key", "title", "hint" };

    private readonly List<TutorialStep> _all;

    /// <summary>全部步骤，按教的顺序。</summary>
    public IReadOnlyList<TutorialStep> All => _all;

    /// <summary>几步。</summary>
    public int Count => _all.Count;

    /// <summary>第 <paramref name="index"/> 步（0 起）。</summary>
    /// <param name="index">0 起的下标。</param>
    public TutorialStep this[int index] => _all[index];

    private TutorialScript(List<TutorialStep> all) => _all = all;

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">tutorial.csv 的路径。</param>
    /// <returns>教学表。</returns>
    /// <exception cref="InvalidDataException">缺列、行数不对、seq 不连号、key 不齐、title 为空。</exception>
    public static TutorialScript Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>教学表。</returns>
    /// <exception cref="InvalidDataException">缺列、行数不对、seq 不连号、key 不齐、title 为空。</exception>
    public static TutorialScript FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("教学表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"教学表第 1 行：缺少列「{name}」。");

        var all = new List<TutorialStep>(ExpectedCount);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var seqText = Get("seq");
            if (!int.TryParse(seqText, out var seq))
                throw new InvalidDataException($"教学表第 {line} 行：seq「{seqText}」不是整数。");

            if (seq != all.Count + 1)
                throw new InvalidDataException(
                    $"教学表第 {line} 行：seq 应该是 {all.Count + 1}，写的是 {seq}（必须从 1 起连号）。");

            if (seq > ExpectedCount)
                throw new InvalidDataException($"教学表第 {line} 行：多出第 {seq} 步（一共只该有 {ExpectedCount} 步）。");

            var key = Get("key");
            if (key.Length == 0)
                throw new InvalidDataException($"教学表第 {line} 行：key 为空。");

            var expectedKey = ExpectedKeys[seq - 1];
            if (!string.Equals(key, expectedKey, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"教学表第 {line} 行：key「{key}」对不上第 {seq} 步（该是「{expectedKey}」）。"
                    + "顺序是代码里那八个 Stage，改表就得同步改代码。");

            var title = Get("title");
            if (title.Length == 0)
                throw new InvalidDataException($"教学表第 {line} 行：{key} 的 title 是空的。");

            all.Add(new TutorialStep(seq, key, title, Get("hint")));
        }

        if (all.Count != ExpectedCount)
            throw new InvalidDataException($"教学表只有 {all.Count} 步，该有 {ExpectedCount} 步。");

        return new TutorialScript(all);
    }

    /// <summary>按 key 找这一步；表里没有返回 null。</summary>
    /// <param name="key">move / look / search / map / bag / trust / talk / leave。</param>
    /// <returns>那一步或 null。</returns>
    public TutorialStep? Get(string key)
    {
        foreach (var step in _all)
            if (string.Equals(step.Key, key, StringComparison.Ordinal))
                return step;

        return null;
    }
}
