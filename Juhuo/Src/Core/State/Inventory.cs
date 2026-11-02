using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>
/// 囊：12 格，没有仓库（CANON §4）。一格放一样东西，可叠的最多 99 件。
///
/// 规矩（S1-03b）：
/// - 可叠物品（<c>items.csv</c> 的 stack=1）优先并进已有的那一格，一叠最多 99；
/// - 不可叠物品（stack=0）一件占一格——稀有品具名，不堆叠；
/// - 格满了**不静默丢弃**：<see cref="TryAdd"/> 返回 false 并给一句人话；
/// - 存档形状不变：<c>inventory: [{item, n}]</c>，一格一项；
/// - 读档遇到表里没有的 item id **照样留着**，不替玩家丢东西。
///
/// 纯 C#：不认识 Godot，也不读文件（表由 <see cref="ItemCatalog"/> 提供）。
/// </summary>
public sealed class Inventory
{
    /// <summary>格数上限（CANON §4：背包 12 格，无仓库）。</summary>
    public const int DefaultCapacity = 12;

    /// <summary>一叠的上限。到了就不再并，多的拒收——宁可让玩家看见"装不下"，也不要偷偷吞掉。</summary>
    public const int MaxStack = 99;

    /// <summary>格满时给玩家看的一句（手记体，不是报错）。</summary>
    public const string FullMessage = "囊里满了，得先丢点东西。";

    private readonly List<InventoryEntry> _slots;
    private readonly int _capacity;

    /// <summary>
    /// 建一个空囊。
    /// </summary>
    /// <param name="capacity">格数，默认 12。小于 1 按 1 算。</param>
    public Inventory(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
        _slots = new List<InventoryEntry>(_capacity);
    }

    /// <summary>格数上限。</summary>
    public int Capacity => _capacity;

    /// <summary>用掉几格。可叠物品占一格，不管里面有几件。</summary>
    public int SlotsUsed => _slots.Count;

    /// <summary>还有没有空格。</summary>
    public bool IsFull => _slots.Count >= _capacity;

    /// <summary>
    /// 囊里的东西，按放进来的顺序，给 UI 读。每一项就是存档里的一项。
    /// </summary>
    public IReadOnlyList<InventoryEntry> Items => _slots;

    /// <summary>
    /// 往囊里放东西。
    /// </summary>
    /// <param name="itemId">物品 id。表里查不到时按"不可叠"处理，一件一格。</param>
    /// <param name="n">件数，必须 ≥1。</param>
    /// <param name="catalog">物品表，决定能不能叠。</param>
    /// <param name="error">放不下时的一句人话；放得下时为 null。</param>
    /// <returns>全放进去了返回 true；一件都没放返回 false（不会放一半）。</returns>
    public bool TryAdd(string? itemId, int n, ItemCatalog catalog, out string? error)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        error = null;

        if (string.IsNullOrEmpty(itemId))
        {
            error = "没有这件东西。";
            return false;
        }

        if (n < 1)
        {
            error = "件数不对。";
            return false;
        }

        var def = catalog.TryGet(itemId);
        var stackable = def != null && def.Stackable;

        if (stackable)
        {
            var at = SlotWithRoomFor(itemId!, n);

            if (at >= 0)
            {
                _slots[at].N += n;
                return true;
            }

            // 已经有这一叠、但一叠满了：多的拒收，不另开一格把它藏起来。
            if (IndexOf(itemId!) >= 0)
            {
                error = $"这一叠装不下（最多 {MaxStack} 件）。";
                return false;
            }

            if (IsFull)
            {
                error = FullMessage;
                return false;
            }

            _slots.Add(new InventoryEntry { Item = itemId!, N = n });
            return true;
        }

        // 不可叠：一件一格，格数不够就整批拒绝——放一半会让玩家以为放完了。
        if (_slots.Count + n > _capacity)
        {
            error = FullMessage;
            return false;
        }

        for (var i = 0; i < n; i++)
            _slots.Add(new InventoryEntry { Item = itemId!, N = 1 });

        return true;
    }

    /// <summary>
    /// 从囊里拿走东西。拿空了就把那一格也空出来。
    /// </summary>
    /// <param name="itemId">物品 id。</param>
    /// <param name="n">想拿几件，必须 ≥1。</param>
    /// <returns>实际拿走的件数；囊里不够时就是全拿走。</returns>
    public int Remove(string? itemId, int n)
    {
        if (string.IsNullOrEmpty(itemId) || n < 1) return 0;

        var removed = 0;

        for (var i = 0; i < _slots.Count && removed < n;)
        {
            var slot = _slots[i];
            if (!string.Equals(slot.Item, itemId, StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            var take = Math.Min(slot.N, n - removed);
            slot.N -= take;
            removed += take;

            if (slot.N <= 0) _slots.RemoveAt(i);
            else i++;
        }

        return removed;
    }

    /// <summary>
    /// 囊里有多少件这种东西。
    /// </summary>
    /// <param name="itemId">物品 id。</param>
    /// <returns>件数；没有返回 0。</returns>
    public int Count(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return 0;

        var total = 0;
        foreach (var slot in _slots)
            if (string.Equals(slot.Item, itemId, StringComparison.Ordinal))
                total += slot.N;

        return total;
    }

    /// <summary>
    /// 用存档里的背包覆盖囊里的内容。
    ///
    /// 原样收下：表里查不到的 id 也留着（玩家手里的东西不能因为版本更新消失）。
    /// 只丢两种烂行——空 id 和 n&lt;1，它们是坏档的产物，不是东西。
    /// 超过格数也照收，之后自然放不进新的（<see cref="IsFull"/> 为真）。
    /// </summary>
    /// <param name="entries">存档里的 inventory 列表；null 视为空，列表里的 null 项直接跳过（坏档会有）。</param>
    public void LoadFrom(IEnumerable<InventoryEntry?>? entries)
    {
        _slots.Clear();
        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (entry == null) continue;
            if (string.IsNullOrEmpty(entry.Item)) continue;
            if (entry.N < 1) continue;

            _slots.Add(new InventoryEntry { Item = entry.Item, N = entry.N });
        }
    }

    /// <summary>
    /// 导出存档里的形状：<c>[{item, n}]</c>，一格一项。返回的是副本，改它不影响囊。
    /// </summary>
    /// <returns>背包条目列表。</returns>
    public List<InventoryEntry> ToSave()
    {
        var list = new List<InventoryEntry>(_slots.Count);
        foreach (var slot in _slots)
            list.Add(new InventoryEntry { Item = slot.Item, N = slot.N });

        return list;
    }

    /// <summary>
    /// 写档前的格数检查，给 <c>SaveService.Validate</c> 转调用。
    /// 只查两条硬规矩：格数不超过上限、每项件数 ≥1（规则卡里写死的，不自己发明标准）。
    /// </summary>
    /// <param name="entries">存档里的 inventory 列表。</param>
    /// <param name="capacity">格数上限，默认 12。</param>
    /// <returns>不合格时给一句人话；合格时返回 null。</returns>
    public static string? Validate(IReadOnlyList<InventoryEntry>? entries, int capacity = DefaultCapacity)
    {
        if (entries == null) return null;

        var max = Math.Max(1, capacity);

        if (entries.Count > max)
            return $"背包装不下（{entries.Count} > {max}）。";

        foreach (var item in entries)
            if (item != null && item.N < 1)
                return $"背包里有件东西数量不对（{item.Item}）。";

        return null;
    }

    /// <summary>找第一个 item 匹配的格子的下标；没有返回 -1。</summary>
    private int IndexOf(string itemId)
    {
        for (var i = 0; i < _slots.Count; i++)
            if (string.Equals(_slots[i].Item, itemId, StringComparison.Ordinal))
                return i;

        return -1;
    }

    /// <summary>找一格还能再放 n 件的同种物品；没有返回 -1。</summary>
    private int SlotWithRoomFor(string itemId, int n)
    {
        for (var i = 0; i < _slots.Count; i++)
            if (string.Equals(_slots[i].Item, itemId, StringComparison.Ordinal) && _slots[i].N + n <= MaxStack)
                return i;

        return -1;
    }
}
