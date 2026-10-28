using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 一段台词。一段 = 一句话 + 可选条件 + 说完置位 + 接哪一段。
///
/// 这就是票面要的"一行文本 + 可选条件 + 置位"的结构：
/// 现在 <c>npcs.csv</c> 只有 first / again / interrupt 三列纯文本，
/// 建表时就只有这三个无条件的节点；等 D3 补了条件列，同一套结构直接吃得下，
/// 不用回头改 World。
/// </summary>
public sealed class DialogueNode
{
    /// <summary>段名：first / again / interrupt / interrogate，或数据表里自定的名字。</summary>
    public string Key = "";

    /// <summary>要说的话。空串视为"没有这一段"。</summary>
    public string Text = "";

    /// <summary>条件表达式（语法同 <see cref="FlagExpression"/>）。空 = 无条件。</summary>
    public string Requires = "";

    /// <summary>说完置这个旗标。空 = 不置。</summary>
    public string SetFlag = "";

    /// <summary>这一段条件不成立（或说完了）时接的下一段。空 = 到头。</summary>
    public string Next = "";
}

/// <summary>
/// 一个人的对话分支图。按键取段，条件不成立就沿 <see cref="DialogueNode.Next"/> 往下退，
/// 退到头还是没有 -> 返回 null（调用方回退到纯文本）。
///
/// 分支只在"段"这一层：一段一段往下走，不做树。序章每个 NPC 最多三四段，
/// 树会让人写不完，链够用了。
/// </summary>
public sealed class DialogueGraph
{
    /// <summary>沿 Next 最多走这么多段。数据写错（成环）时不会把主线程转死。</summary>
    public const int MaxHops = 8;

    private readonly Dictionary<string, DialogueNode> _nodes = new(StringComparer.Ordinal);

    /// <summary>有几段。</summary>
    public int Count => _nodes.Count;

    /// <summary>段名清单。给自检与调试用，顺序不保证。</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = new List<string>(_nodes.Count);
            foreach (var kv in _nodes) keys.Add(kv.Key);
            return keys;
        }
    }

    /// <summary>
    /// 加一段。同名重复加入时保留先来的那一段——
    /// 数据表里同一个 NPC 出现两行时，行为要可预测，不能"后写的赢"。
    /// </summary>
    /// <returns>真的加进去了返回 true。</returns>
    public bool Add(DialogueNode? node)
    {
        if (node is null) return false;
        if (string.IsNullOrEmpty(node.Key)) return false;

        return _nodes.TryAdd(node.Key, node);
    }

    /// <summary>取一段，不看条件。</summary>
    public DialogueNode? Get(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return _nodes.TryGetValue(key!, out var node) ? node : null;
    }

    /// <summary>有没有这一段（且这段有字）。</summary>
    public bool Has(string? key) => Get(key) is { Text.Length: > 0 };

    /// <summary>
    /// 从 <paramref name="startKey"/> 起，沿 Next 找第一段条件成立的。
    /// 条件不成立就退到下一段；退到头返回 null，调用方自己兜底。
    /// </summary>
    /// <param name="startKey">想说的那一段。</param>
    /// <param name="flags">旗标仓库。null 时带条件的段一律视为不成立。</param>
    /// <param name="hub">当前空间，查局部旗标用。</param>
    /// <returns>选中的那一段；选不出来返回 null。</returns>
    public DialogueNode? Resolve(string? startKey, FlagStore? flags, string? hub)
    {
        var key = startKey;
        var guard = 0;

        while (!string.IsNullOrEmpty(key) && guard++ < MaxHops)
        {
            var node = Get(key);
            if (node is null) return null;

            if (node.Text.Length > 0 && FlagExpression.Evaluate(node.Requires, flags, hub))
                return node;

            key = node.Next;
        }

        return null;
    }
}

/// <summary>
/// 选哪一段说。顺序是有讲究的，别随手改：
///
/// 1. <c>interrogate</c>：盘问是玩家主动追问，盖过其它一切；
/// 2. <c>first</c>：第一次见面永远先说完初见那段——D21 说首次对话不收刻，
///    要是被一次打断顶掉，玩家就白丢了一段一次性的文本；
/// 3. <c>interrupt</c>：见过之后又被打断过，先说上次那句"我还没说完"；
/// 4. <c>again</c>：其余。
/// </summary>
public static class DialogueVariant
{
    public const string First = "first";
    public const string Again = "again";
    public const string Interrupt = "interrupt";
    public const string Interrogate = "interrogate";

    /// <summary>挑一段。</summary>
    /// <param name="met">跟这个人说过话没有。</param>
    /// <param name="interrupted">有没有"上次被打断"的记忆。</param>
    /// <param name="interrogate">这次是不是盘问。</param>
    public static string Choose(bool met, bool interrupted, bool interrogate)
    {
        if (interrogate) return Interrogate;
        if (!met) return First;
        if (interrupted) return Interrupt;
        return Again;
    }
}

/// <summary>
/// 对话相关的旗标名。
///
/// 票面定的两条：<c>talk.&lt;npc&gt;.interrupted</c>（被打断过）、
/// <c>talk.&lt;npc&gt;.allowed</c>（可以盘问）。
/// 旗标名不许含冒号逗号（rule_flags_save.md §1），所以这里把台词表的定位键
/// <c>hub3:12,4</c> 洗成 <c>hub3_12_4</c>；名字太长按字节截，别撞 40 字节上限。
/// </summary>
public static class DialogueFlags
{
    /// <summary>旗标名总字节上限（与 FlagStore 一致）。</summary>
    private const int MaxFlagBytes = 40;

    public const string InterruptedSuffix = "interrupted";
    public const string AllowedSuffix = "allowed";

    /// <summary>把台词表的定位键洗成能进旗标名的样子。</summary>
    /// <param name="speakerKey">如 <c>hub3:12,4</c> / <c>hub3:guard:衙役甲</c>。</param>
    public static string KeyOf(string? speakerKey)
    {
        if (string.IsNullOrEmpty(speakerKey)) return "unknown";

        var chars = new char[speakerKey!.Length];
        for (var i = 0; i < speakerKey.Length; i++)
        {
            var c = speakerKey[i];
            chars[i] = char.IsLetterOrDigit(c) || c == '_' ? c : '_';
        }

        var key = new string(chars);

        // 留够 "talk." + "." + 后缀 的字节，剩下的才是名字能用的长度
        var budget = MaxFlagBytes - "talk.".Length - 1 - Math.Max(InterruptedSuffix.Length, AllowedSuffix.Length);
        while (System.Text.Encoding.UTF8.GetByteCount(key) > budget && key.Length > 1)
            key = key[..^1];

        return key;
    }

    /// <summary><c>talk.&lt;npc&gt;.interrupted</c>：上次被打断了，下次见面先说那句。</summary>
    public static string Interrupted(string? speakerKey) => $"talk.{KeyOf(speakerKey)}.{InterruptedSuffix}";

    /// <summary><c>talk.&lt;npc&gt;.allowed</c>：这个人可以盘问。</summary>
    public static string Allowed(string? speakerKey) => $"talk.{KeyOf(speakerKey)}.{AllowedSuffix}";
}

/// <summary>
/// 全部对话分支，按"说话的人"索引。键与 <see cref="Juhuo.Core.NpcTalk"/> 的定位一致：
/// 站着不动的是 <c>hub:x,y</c>，巡逻的是 <c>hub:guard:名字</c>（人会走，坐标靠不住）。
///
/// 数据来源只有一个：<c>npcs.csv</c>。表里没有条件列时，建出来就是
/// first / again / interrupt 三段纯文本——和改造前一模一样。
/// </summary>
public sealed class DialogueBook
{
    private readonly Dictionary<string, DialogueGraph> _bySpeaker = new(StringComparer.Ordinal);

    /// <summary>有几个人有对话。</summary>
    public int SpeakerCount => _bySpeaker.Count;

    /// <summary>站着不动的人的定位键。</summary>
    public static string KeyAt(string? hub, int x, int y) => $"{hub}:{x},{y}";

    /// <summary>巡逻的人的定位键。</summary>
    public static string KeyOfGuard(string? hub, string? name) => $"{hub}:guard:{name}";

    /// <summary>取一个人的分支图；没有就返回 null。</summary>
    public DialogueGraph? Of(string? speakerKey)
    {
        if (string.IsNullOrEmpty(speakerKey)) return null;
        return _bySpeaker.TryGetValue(speakerKey!, out var g) ? g : null;
    }

    /// <summary>取一个人的分支图；没有就建一个空的（接新数据时用）。</summary>
    public DialogueGraph GraphFor(string speakerKey)
    {
        if (!_bySpeaker.TryGetValue(speakerKey, out var g))
        {
            g = new DialogueGraph();
            _bySpeaker[speakerKey] = g;
        }

        return g;
    }

    /// <summary>
    /// 选段。人不存在或段不存在都返回 null，调用方回退到纯文本。
    /// </summary>
    public DialogueNode? Resolve(string? speakerKey, string? variant, FlagStore? flags, string? hub)
        => Of(speakerKey)?.Resolve(variant, flags, hub);

    /// <summary>
    /// 从台词表建。只认已有的三段文本，不加任何条件——这是"没列就走原逻辑"。
    /// </summary>
    public static DialogueBook FromTalks(NpcTalkTable? talks)
    {
        var book = new DialogueBook();
        if (talks is null) return book;

        foreach (var t in talks.All)
        {
            var key = t.X > 0 || t.Y > 0
                ? KeyAt(t.Hub, t.X, t.Y)
                : KeyOfGuard(t.Hub, t.Name);

            var g = book.GraphFor(key);
            g.Add(new DialogueNode { Key = DialogueVariant.First, Text = t.First });
            g.Add(new DialogueNode { Key = DialogueVariant.Again, Text = t.Again });
            g.Add(new DialogueNode { Key = DialogueVariant.Interrupt, Text = t.Interrupt });
        }

        return book;
    }

    /// <summary>
    /// 从 <c>npcs.csv</c> 建，并吃掉可选的附加列。
    ///
    /// 现在表里只有 7 列，所以走的就是 <see cref="FromTalks"/>。
    /// 表头里要是出现了 <c>interrogate</c> 列（D3 补列后），这一列会自动变成
    /// "盘问"那一段；没有这一列，盘问入口就是关着的（票面：缺则禁用该入口）。
    /// </summary>
    public static DialogueBook FromCsv(string? csv)
    {
        if (string.IsNullOrEmpty(csv)) return new DialogueBook();

        var book = FromTalks(NpcTalkTable.FromCsv(csv!));

        var rows = Csv.Parse(csv!);
        if (rows.Count < 2) return book;

        var header = new string[rows[0].Length];
        for (var c = 0; c < header.Length; c++) header[c] = rows[0][c].Trim().ToLowerInvariant();

        var iHub = Array.IndexOf(header, "hub");
        var iName = Array.IndexOf(header, "name");
        var iX = Array.IndexOf(header, "x");
        var iY = Array.IndexOf(header, "y");
        var iAsk = Array.IndexOf(header, "interrogate");

        if (iAsk < 0 || iHub < 0 || iName < 0) return book;

        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Length <= iAsk) continue;
            if (string.IsNullOrWhiteSpace(r[iAsk])) continue;

            var hub = r[iHub].Trim();
            var name = r[iName].Trim();
            var x = iX >= 0 && iX < r.Length && int.TryParse(r[iX].Trim(), out var px) ? px : 0;
            var y = iY >= 0 && iY < r.Length && int.TryParse(r[iY].Trim(), out var py) ? py : 0;

            var key = x > 0 || y > 0 ? KeyAt(hub, x, y) : KeyOfGuard(hub, name);
            book.GraphFor(key).Add(new DialogueNode { Key = DialogueVariant.Interrogate, Text = r[iAsk] });
        }

        return book;
    }
}
