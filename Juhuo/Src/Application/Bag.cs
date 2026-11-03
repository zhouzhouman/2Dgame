using System;
using System.Collections.Generic;
using System.Text;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>一处可搜点里的"一样东西"：<c>coin:14</c> 拆出来的那一份。</summary>
/// <param name="ItemId">物品 id（items.csv 的 id）。</param>
/// <param name="N">件数，≥1。</param>
public readonly record struct LootEntry(string ItemId, int N);

/// <summary>
/// <c>points.csv</c> 的 <c>loot</c> 列：<c>coin:14|cloth:1</c> 这种写法。
///
/// 分隔符两种都认：竖线（现在这张表用的）和分号（票面写的）。
/// 认不出来的一段直接跳过——坏一行不该让整处东西都捡不到。
/// </summary>
public static class BagLoot
{
    /// <summary>分隔符：竖线与分号。</summary>
    private static readonly char[] Separators = { '|', ';' };

    /// <summary>
    /// 把 loot 列拆成一份一份。
    /// </summary>
    /// <param name="raw">loot 原文；空串或 null 返回空表。</param>
    /// <returns>按表内顺序的条目；没有合法的就返回空表。</returns>
    public static IReadOnlyList<LootEntry> Parse(string? raw)
    {
        var list = new List<LootEntry>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        foreach (var part in raw!.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = part.Trim();
            if (piece.Length == 0) continue;

            var cut = piece.IndexOf(':');
            var id = (cut < 0 ? piece : piece[..cut]).Trim();

            var n = 1;
            if (cut >= 0 && !int.TryParse(piece[(cut + 1)..].Trim(), out n)) continue;

            if (id.Length == 0 || n < 1) continue;
            list.Add(new LootEntry(id, n));
        }

        return list;
    }
}

/// <summary>
/// 一次"往囊里装"的结果。
/// </summary>
/// <param name="Entries">装得下的那些；<see cref="RejectedId"/> 非空时这里必为空。</param>
/// <param name="RejectedId">装不下的那一件的 id；全装得下时为 null。</param>
public readonly record struct BagPickupOutcome(IReadOnlyList<LootEntry> Entries, string? RejectedId)
{
    /// <summary>什么都没捡（这一处没有东西，或者已经搜过了）。</summary>
    public static BagPickupOutcome Empty => new(Array.Empty<LootEntry>(), null);

    /// <summary>装不下。</summary>
    public bool Full => RejectedId != null;

    /// <summary>真捡到了东西。</summary>
    public bool Any => Entries.Count > 0;
}

/// <summary>
/// 捡东西：先算装不装得下，再决定动不动囊。
///
/// 一处点位常常一次掉好几样（钱 + 衣裳），装一半会让玩家以为已经装完了，
/// 所以**要么全装，要么一件都不装**——这也是"该点位不算搜过"的前提。
/// 纯 C#，不认引擎。
/// </summary>
public static class BagPickup
{
    /// <summary>
    /// 试装。**不改囊**：在副本上走一遍，装不下就整批退回。
    /// </summary>
    /// <param name="loot">loot 原文。</param>
    /// <param name="bag">玩家的囊。</param>
    /// <param name="catalog">物品表。</param>
    /// <returns>结果；<see cref="BagPickupOutcome.Full"/> 为真表示一件都没装。</returns>
    public static BagPickupOutcome Simulate(string? loot, Inventory bag, ItemCatalog catalog)
    {
        if (bag == null) throw new ArgumentNullException(nameof(bag));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        var entries = BagLoot.Parse(loot);
        if (entries.Count == 0) return BagPickupOutcome.Empty;

        var trial = new Inventory(bag.Capacity);
        trial.LoadFrom(bag.ToSave());

        foreach (var entry in entries)
            if (!trial.TryAdd(entry.ItemId, entry.N, catalog, out _))
                return new BagPickupOutcome(Array.Empty<LootEntry>(), entry.ItemId);

        return new BagPickupOutcome(entries, null);
    }

    /// <summary>
    /// 把 <see cref="Simulate"/> 算好的那一批真正装进囊里。
    /// 只该在结果不是满的时候调。
    /// </summary>
    /// <param name="outcome">试装结果。</param>
    /// <param name="bag">玩家的囊。</param>
    /// <param name="catalog">物品表。</param>
    /// <returns>实际装进去的条目。</returns>
    public static IReadOnlyList<LootEntry> Apply(BagPickupOutcome outcome, Inventory bag, ItemCatalog catalog)
    {
        if (bag == null) throw new ArgumentNullException(nameof(bag));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (outcome.Full) return Array.Empty<LootEntry>();

        var done = new List<LootEntry>(outcome.Entries.Count);
        foreach (var entry in outcome.Entries)
            if (bag.TryAdd(entry.ItemId, entry.N, catalog, out _))
                done.Add(entry);

        return done;
    }

    /// <summary>物品的显示名。表里查不到就退回 id——宁可难看，也不能凭空编一个名字。</summary>
    /// <param name="catalog">物品表。</param>
    /// <param name="itemId">物品 id。</param>
    /// <returns>显示名。</returns>
    public static string NameOf(ItemCatalog? catalog, string itemId)
    {
        var name = catalog?.TryGet(itemId)?.Name;
        return string.IsNullOrEmpty(name) ? itemId : name!;
    }

    /// <summary>
    /// 搜完之后补在提示后面的那一句：<c>（+14 铜钱 · +1 干净衣裳）</c>。
    /// 什么都没捡到就返回空串，调用方直接拼上去也不难看。
    /// </summary>
    /// <param name="picked">捡到的条目。</param>
    /// <param name="catalog">物品表。</param>
    /// <returns>带全角括号的一句，或空串。</returns>
    public static string Describe(IReadOnlyList<LootEntry>? picked, ItemCatalog? catalog)
    {
        if (picked == null || picked.Count == 0) return "";

        var text = new StringBuilder("（");
        for (var i = 0; i < picked.Count; i++)
        {
            if (i > 0) text.Append(" · ");
            text.Append('+').Append(picked[i].N).Append(' ').Append(NameOf(catalog, picked[i].ItemId));
        }

        return text.Append('）').ToString();
    }
}

/// <summary>
/// 一次搜查要不要收东西。
/// </summary>
/// <param name="CanSearch">false = 囊满，这一处不算搜过，也不该收刻。</param>
/// <param name="Loot">试装结果（<see cref="CanSearch"/> 为假时带 <c>RejectedId</c>）。</param>
/// <param name="Refusal">给玩家看的那句人话；能搜时为 null。</param>
public readonly record struct BagSearchPlan(bool CanSearch, BagPickupOutcome Loot, string? Refusal);

/// <summary>
/// 搜查与囊的接线：把"这一处还有没有东西、装不装得下"这两件事从 World 里拎出来，
/// 好在不起引擎的情况下测（票号 S1-03c）。
/// </summary>
public static class BagSearch
{
    /// <summary>囊满时给玩家看的那一句：先说怎么了，再说怎么办。</summary>
    public static readonly string RefusalText = Inventory.FullMessage + "按 I 打开囊，丢掉一件再来。";

    /// <summary>
    /// 判这一处能不能搜。
    ///
    /// 只有"第一次搜"才收东西：重搜只给文本、只收刻，不然一处墙缝能翻出无穷枚铜钱。
    /// 装不下就整处不算搜过——玩家丢掉点东西还能回来再搜。
    /// </summary>
    /// <param name="first">这一处之前没搜过。</param>
    /// <param name="loot">这一处的 loot 原文。</param>
    /// <param name="bag">玩家的囊。</param>
    /// <param name="catalog">物品表；null 表示这一局没接物品表，只当没东西可捡。</param>
    /// <returns>能不能搜、要装哪几样。</returns>
    public static BagSearchPlan Plan(bool first, string? loot, Inventory bag, ItemCatalog? catalog)
    {
        if (!first || catalog == null) return new BagSearchPlan(true, BagPickupOutcome.Empty, null);

        var outcome = BagPickup.Simulate(loot, bag, catalog);
        return outcome.Full
            ? new BagSearchPlan(false, outcome, RefusalText)
            : new BagSearchPlan(true, outcome, null);
    }
}

/// <summary>
/// 囊的面板：12 格、4 列、方向键选格、X / Del 丢一件。
///
/// 只算"选到第几格、丢了哪一件"，不知道颜色和字——画由 Main 画。
/// "给他看"复用同一张网：选中的那一格就是他要看的东西。
/// </summary>
public sealed class BagPanel
{
    /// <summary>一格一行摆几件（12 格 = 4 × 3）。</summary>
    public const int Columns = 4;

    private readonly Inventory _bag;
    private readonly ItemCatalog? _catalog;

    /// <summary>建一张面板。</summary>
    /// <param name="bag">玩家的囊。</param>
    /// <param name="catalog">物品表，用来取显示名。</param>
    public BagPanel(Inventory bag, ItemCatalog? catalog)
    {
        _bag = bag ?? throw new ArgumentNullException(nameof(bag));
        _catalog = catalog;
    }

    /// <summary>囊。</summary>
    public Inventory Bag => _bag;

    /// <summary>格数上限。</summary>
    public int Capacity => _bag.Capacity;

    /// <summary>用掉几格。</summary>
    public int SlotsUsed => _bag.SlotsUsed;

    /// <summary>现在选在第几格（0 起）。空格时恒为 0。</summary>
    public int Selected { get; private set; }

    /// <summary>右上角那个 <c>7/12</c>。</summary>
    public string CountLabel => $"{_bag.SlotsUsed}/{_bag.Capacity}";

    /// <summary>选中的东西；空格或囊空时为 null。</summary>
    public string? SelectedItem
    {
        get
        {
            if (SlotsUsed == 0) return null;
            var at = Math.Clamp(Selected, 0, SlotsUsed - 1);
            return _bag.Items[at].Item;
        }
    }

    /// <summary>
    /// 选中格子里该写的字：<c>铜钱 ×3</c>。空格返回空串。
    /// </summary>
    /// <param name="index">0 起的格子号。</param>
    /// <returns>显示文本。</returns>
    public string LabelAt(int index)
    {
        if (index < 0 || index >= SlotsUsed) return "";

        var slot = _bag.Items[index];
        return $"{BagPickup.NameOf(_catalog, slot.Item)} ×{slot.N}";
    }

    /// <summary>
    /// 挪一格。左右不跨行，上下跨一行；到头就停住，不绕圈——
    /// 绕圈会让"再按一下就回到第一格"这种手感出现，12 格里很难受。
    /// </summary>
    /// <param name="dx">左右，-1 / 0 / 1。</param>
    /// <param name="dy">上下，-1 / 0 / 1。</param>
    public void Move(int dx, int dy)
    {
        if (SlotsUsed == 0) return;

        var at = Math.Clamp(Selected, 0, SlotsUsed - 1);

        if (dy != 0)
        {
            at += dy * Columns;
        }
        else if (dx != 0)
        {
            var next = at + dx;
            // 只在本行里走：跨过行尾会跳到下一行开头，看着像按键失灵
            if (next >= 0 && next < SlotsUsed && next / Columns == at / Columns) at = next;
        }

        Selected = Math.Clamp(at, 0, SlotsUsed - 1);
    }

    /// <summary>直接把选中挪到某一格（空囊忽略）。</summary>
    /// <param name="index">0 起的格子号。</param>
    public void Select(int index)
    {
        if (SlotsUsed == 0) return;
        Selected = Math.Clamp(index, 0, SlotsUsed - 1);
    }

    /// <summary>
    /// 丢掉选中的一件。可叠的减一件，减到 0 才腾出这一格；
    /// 不可叠的一件一格，丢完这一格就没了。
    /// </summary>
    /// <returns>真丢掉了返回 true；囊是空的返回 false。</returns>
    public bool Drop()
    {
        var item = SelectedItem;
        if (item == null) return false;

        if (_bag.Remove(item, 1) <= 0) return false;

        Selected = Math.Clamp(Selected, 0, Math.Max(0, SlotsUsed - 1));
        return true;
    }
}

/// <summary>
/// "给他看"的三句话（占位，D2 之后换）。
///
/// 三种分支必须分得开：他认不认这件东西是一层，你事先知不知道又是一层。
/// §2.5 的全部意义就在这里——同一件童鞋，两种反应，区别只在"你看没看见"。
/// </summary>
public static class GiftPresenter
{
    /// <summary>他认得，而且你事先就知道这是什么。</summary>
    public const string Silent = "他接过去，看了很久，久到你以为他不会开口了。";

    /// <summary>他认得，但你没细看过，说不出它是什么。</summary>
    public const string NotMine = "「这不是我的。」他把东西推回你手里。";

    /// <summary>他不认这件东西。</summary>
    public const string Nothing = "他看了看，没说什么。";

    /// <summary>
    /// 按一次"给他看"的结果挑一句话。
    /// </summary>
    /// <param name="result">赠礼表给的结果。</param>
    /// <returns>占位文案。</returns>
    public static string Reaction(GiftResult result)
    {
        if (!result.Matched) return Nothing;
        return result.KnownBefore ? Silent : NotMine;
    }
}

/// <summary>
/// HUD 那半句：<c>囊 7/12</c>。
///
/// 只在快满（≥ 格数 − 2）时变色，平时跟节拍那一行同一个色——
/// 一直刺眼的数字等于没有提示。
/// </summary>
public static class BagHud
{
    /// <summary>离满还差几格开始提示。</summary>
    public const int WarnWithin = 2;

    /// <summary>
    /// 拼在节拍行后面的一句。
    /// </summary>
    /// <param name="bag">玩家的囊。</param>
    /// <returns><c>　囊 7/12</c>。</returns>
    public static string Suffix(Inventory? bag) =>
        bag == null ? "" : $"　囊 {bag.SlotsUsed}/{bag.Capacity}";

    /// <summary>该不该变色。</summary>
    /// <param name="bag">玩家的囊。</param>
    /// <returns>用掉 ≥ 上限 − 2 格时为真。</returns>
    public static bool Warn(Inventory? bag) =>
        bag != null && bag.SlotsUsed >= bag.Capacity - WarnWithin;

    /// <summary>
    /// 拼在囊计数后面的一句：<c>　钱 30</c>。
    ///
    /// 钱就在囊里（<c>coin</c> 那一格），这里只把数念出来——不另存一份"钱"的账。
    /// </summary>
    /// <param name="bag">玩家的囊。</param>
    /// <returns><c>　钱 30</c>；没接囊就是空串。</returns>
    public static string MoneySuffix(Inventory? bag) =>
        bag == null ? "" : $"　钱 {bag.Count(TradeBook.MoneyItem)}";
}

/// <summary>
/// <c>--bag</c> 的输出。格式钉在这里，命令行和测试读的是同一份。
/// </summary>
public static class BagReport
{
    /// <summary>所有行共用的抬头。</summary>
    public const string Prefix = "[囊]";

    /// <summary>
    /// 背包清单：第一行说捡了几件、占几格，后面一格一行。
    /// </summary>
    /// <param name="bag">玩家的囊。</param>
    /// <param name="catalog">物品表。</param>
    /// <param name="picked">这一趟捡到的条目数。</param>
    /// <returns>按打印顺序排好的行。</returns>
    public static IReadOnlyList<string> Lines(Inventory bag, ItemCatalog? catalog, int picked)
    {
        if (bag == null) throw new ArgumentNullException(nameof(bag));

        var lines = new List<string>(bag.SlotsUsed + 1)
        {
            $"{Prefix} 拾得 {picked} 件，占 {bag.SlotsUsed}/{bag.Capacity} 格",
        };

        for (var i = 0; i < bag.SlotsUsed; i++)
        {
            var slot = bag.Items[i];
            lines.Add($"{Prefix} {i + 1} {BagPickup.NameOf(catalog, slot.Item)} ×{slot.N}");
        }

        return lines;
    }

    /// <summary>装不下时那一行：<c>[囊] 满了，拒收：铜钱</c>。</summary>
    /// <param name="catalog">物品表。</param>
    /// <param name="itemId">被拒的那一件。</param>
    /// <returns>一行。</returns>
    public static string RejectedLine(ItemCatalog? catalog, string itemId) =>
        $"{Prefix} 满了，拒收：{BagPickup.NameOf(catalog, itemId)}";
}
