using System;
using System.Collections.Generic;
using System.Linq;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 选项效果接进游戏的**玩法接线**（票号 S3-02c）。
///
/// Core 的 <see cref="BeatEffectBook"/> 只管"这组效果落不落得下"，这里补三件它故意不管的事：
/// ① 敲门 / 搜查触发的拍**带选项时先弹面板**，玩家确认了才算做完（不然等于替他做了主）；
/// ② 生效之后屏幕上那一行怎么说（选项名 + Core 给的人话）；
/// ③ 没落成时给玩家看的那一句——这一拍**不算完**，让他清囊再来。
///
/// 纯 C#，不认引擎：Main 只负责把面板推上去、把这一行显示出来。
/// </summary>
public static class BeatEffectWiring
{
    /// <summary>调试与日志共用的抬头。</summary>
    public const string Prefix = "[效果]";

    /// <summary>
    /// 这一拍被敲门 / 搜查触发时，该先弹选项还是直接算完。
    /// 有选项就必须问过玩家；没选项（旧表）维持原来的"一下做完"。
    /// </summary>
    /// <param name="beat">触发的节拍；null 也算"不用问"。</param>
    /// <returns>有可选项返回 true。</returns>
    public static bool NeedsChoice(Beat? beat) => beat is not null && beat.Options.Length > 0;

    /// <summary>
    /// 该弹出来的那一拍；同一拍的面板已经在等着了，就返回 null——别重复弹。
    /// </summary>
    /// <param name="beat">刚触发的节拍。</param>
    /// <param name="open">面板上正等着的节拍（没开面板传 null）。</param>
    /// <returns>要弹的那一拍；不必弹则 null。</returns>
    public static Beat? PendingShow(Beat? beat, Beat? open)
        => NeedsChoice(beat) && !SameBeat(beat, open) ? beat : null;

    /// <summary>两拍是不是同一拍（按 id 认；空的一律不算）。</summary>
    /// <param name="a">一拍。</param>
    /// <param name="b">另一拍。</param>
    /// <returns>同 id 返回 true。</returns>
    public static bool SameBeat(Beat? a, Beat? b)
        => a is not null && b is not null && string.Equals(a.Id, b.Id, StringComparison.Ordinal);

    /// <summary>
    /// 生效之后屏幕上那一行：<c>杀马：谷子 ×3，民望 −5</c>。
    ///
    /// 旗标那几句不进这一行——玩家关心的是账，不是内部名字。
    /// 这一组只有旗标（或干脆没效果）时返回空串，调用方退回原来那句"你记下了"。
    /// </summary>
    /// <param name="beat">这一拍。</param>
    /// <param name="index">选了第几项（0 起）。</param>
    /// <param name="result">Core 的结算结果。</param>
    /// <returns>一行字，或空串。</returns>
    public static string Line(Beat beat, int index, ApplyResult result)
    {
        if (beat == null) throw new ArgumentNullException(nameof(beat));

        var values = ValueLines(result);
        if (values.Count == 0) return "";

        return $"{OptionName(beat, index)}：{string.Join("，", values)}";
    }

    /// <summary>
    /// 没落成时给玩家看的那一句：<c>「杀马」没落成：囊里装不下这件东西。</c>
    /// 面板不关、这一拍不记——让他自己清囊再来。
    /// </summary>
    /// <param name="beat">这一拍。</param>
    /// <param name="index">选了第几项（0 起）。</param>
    /// <param name="result">Core 的失败结果。</param>
    /// <returns>一行字。</returns>
    public static string Failure(Beat beat, int index, ApplyResult result)
    {
        if (beat == null) throw new ArgumentNullException(nameof(beat));

        var reason = result.Ok ? "效果没结算" : result.Reason;
        return $"「{OptionName(beat, index)}」没落成：{reason}";
    }

    /// <summary>
    /// 第几项的选项文案；没有文案（或越界）时退回 <c>第 N 项</c>。
    /// </summary>
    /// <param name="beat">这一拍。</param>
    /// <param name="index">0 起。</param>
    /// <returns>给玩家看的名字。</returns>
    public static string OptionName(Beat beat, int index)
    {
        if (beat == null) throw new ArgumentNullException(nameof(beat));

        return index >= 0 && index < beat.Options.Length && beat.Options[index].Length > 0
            ? beat.Options[index]
            : $"第 {index + 1} 项";
    }

    /// <summary>成功结果里"数值那几句"（去掉 <c>旗标 xxx</c>）。</summary>
    /// <param name="result">结算结果。</param>
    /// <returns>按施加顺序排好的人话。</returns>
    public static IReadOnlyList<string> ValueLines(ApplyResult result)
        => result.Lines.Where(l => !l.StartsWith("旗标 ", StringComparison.Ordinal)).ToList();

    /// <summary>成功结果里置上的旗标名。</summary>
    /// <param name="result">结算结果。</param>
    /// <returns>旗标名，按施加顺序。</returns>
    public static IReadOnlyList<string> FlagLines(ApplyResult result)
        => result.Lines
            .Where(l => l.StartsWith("旗标 ", StringComparison.Ordinal))
            .Select(l => l["旗标 ".Length..])
            .ToList();
}

/// <summary>
/// 一次"确认选项"的结果：成了就带屏幕上那一行，没成带原因（这一拍没记）。
/// </summary>
/// <param name="Ok">这一拍记上了没有。</param>
/// <param name="Line">成了之后屏幕上那一行（<c>杀马：谷子 ×3，民望 −5</c>）；没成时为空串。</param>
/// <param name="Reason">没成时给玩家看的一句话。</param>
/// <param name="Effects">Core 的结算结果（含每一步的人话）。</param>
public readonly record struct ChoiceOutcome(bool Ok, string Line, string Reason, ApplyResult Effects)
{
    /// <summary>成了：这一拍记上了，屏幕上说 <paramref name="line"/>。</summary>
    public static ChoiceOutcome Made(string line, ApplyResult effects = default) =>
        new(true, line ?? "", "", effects);

    /// <summary>没成：这一拍不记，只把原因给玩家看。</summary>
    public static ChoiceOutcome Refused(string reason, ApplyResult effects = default) =>
        new(false, "", reason ?? "", effects);
}

/// <summary>
/// 玩家确认一项时的**正确顺序**：先落效果，成了才记这一拍（票号 S3-02c）。
///
/// 顺序反了会出两种账：先记拍再结算，装不下时这一拍已经做完了，玩家再也没机会补；
/// 先结算再判"拍还做不做得成"，则可能白改一通状态。这里把两件事钉在一起，Main 只负责显示。
/// </summary>
public static class BeatChoiceRunner
{
    /// <summary>
    /// 确认 <paramref name="beat"/> 的第 <paramref name="index"/> 项。
    /// 效果整组落不下（囊满、钱不够）时**这一拍不算完**，返回 <see cref="ChoiceOutcome.Reason"/>。
    /// </summary>
    /// <param name="beat">这一拍。</param>
    /// <param name="index">选了第几项（0 起）。</param>
    /// <param name="beats">节拍运行器（完成判定与日子推进在它身上）。</param>
    /// <param name="trust">本地区三条轴，就地改。</param>
    /// <param name="bag">囊（钱也在里面），就地改。</param>
    /// <param name="catalog">物品表。</param>
    /// <param name="troops">兵力，就地改。</param>
    /// <param name="flags">旗标仓库。</param>
    /// <returns>成没成、屏幕上那一行、没成的原因。</returns>
    public static ChoiceOutcome Confirm(
        Beat beat,
        int index,
        PrologueBeats beats,
        TrustState trust,
        Inventory bag,
        ItemCatalog catalog,
        ref int troops,
        FlagStore? flags)
    {
        if (beat == null) throw new ArgumentNullException(nameof(beat));
        if (beats == null) throw new ArgumentNullException(nameof(beats));
        if (trust == null) throw new ArgumentNullException(nameof(trust));
        if (bag == null) throw new ArgumentNullException(nameof(bag));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        // ① 这一拍还做不做得成：状态可能已经被另一条触发路改掉了，效果不能往做不成的拍上落。
        if (!beats.Machine.CanTrigger(beat))
            return ChoiceOutcome.Refused("这一拍已经做不成了。");

        // ② 先落效果：整组校验、整组施加，落不下就一步都不动。
        var applied = BeatEffectBook.Apply(beat, index, trust, bag, catalog, ref troops, flags);
        if (!applied.Ok)
            return ChoiceOutcome.Refused(BeatEffectWiring.Failure(beat, index, applied), applied);

        // ③ 再记这一拍：置 beat.<id>.pick.<i>、完成、日子往前走。
        if (!beats.Choose(beat, index))
            return ChoiceOutcome.Refused("这一拍已经做不成了。", applied);

        return ChoiceOutcome.Made(BeatEffectWiring.Line(beat, index, applied), applied);
    }
}

/// <summary>
/// <c>--effects</c> 的那一趟：把三条真实选项各结算一次，看数值是不是真的动了。
///
/// 用的是真表（<c>beats.csv</c>）与真物品表，状态是**这一趟自己造的**一份
/// （三轴 / 囊 / 兵 / 旗标），不碰玩家的档。
/// </summary>
public static class EffectsDemo
{
    /// <summary>要跑的三条：票面点名的 杀马 / 卖书 / 写给旧部。</summary>
    public static readonly (string Beat, int Pick)[] Cases =
    {
        ("b03", 0),   // 杀马：谷子 ×3，民望 −5
        ("b03", 2),   // 卖书：钱 +15
        ("b09", 0),   // 写给旧部：兵 +10，疑心 +1
    };

    /// <summary>
    /// 跑一趟，把该打印的行按顺序交回来。
    /// </summary>
    /// <param name="table">节拍表。</param>
    /// <param name="catalog">物品表（决定物品名与能不能叠）。</param>
    /// <returns>每条选项一行。</returns>
    public static IReadOnlyList<string> Run(BeatTable table, ItemCatalog catalog)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        var lines = new List<string>(Cases.Length);

        foreach (var (id, pick) in Cases)
        {
            var beat = table.Get(id)
                ?? throw new InvalidOperationException($"表里没有 {id}，--effects 跑不了");

            var trust = new TrustState { Guan = 50, Min = 50, Yi = 50 };
            var bag = new Inventory();
            var flags = new FlagStore();
            var troops = 0;

            var result = BeatEffectBook.Apply(beat, pick, trust, bag, catalog, ref troops, flags);
            lines.Add(DemoLine(beat, pick, result));
        }

        return lines;
    }

    /// <summary>
    /// 一条演示行：<c>[效果] b03 选「杀马」→ 谷子 ×3，民望 −5；旗标 horse_killed</c>。
    /// 数值那几句用「，」连，旗标另起一段——和屏幕上那一行同一个口径。
    /// </summary>
    /// <param name="beat">这一拍。</param>
    /// <param name="index">选了第几项。</param>
    /// <param name="result">结算结果。</param>
    /// <returns>一行字。</returns>
    public static string DemoLine(Beat beat, int index, ApplyResult result)
    {
        if (beat == null) throw new ArgumentNullException(nameof(beat));

        var name = BeatEffectWiring.OptionName(beat, index);
        if (!result.Ok)
            return $"{BeatEffectWiring.Prefix} {beat.Id} 选「{name}」→ 没落成：{result.Reason}";

        var text = $"{BeatEffectWiring.Prefix} {beat.Id} 选「{name}」→ "
                 + string.Join("，", BeatEffectWiring.ValueLines(result));

        var flags = BeatEffectWiring.FlagLines(result);
        if (flags.Count > 0) text += "；旗标 " + string.Join("、", flags);

        return text;
    }
}
