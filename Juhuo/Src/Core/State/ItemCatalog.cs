using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 一件东西：<c>Content/data/items.csv</c> 的一行。
/// 只描述"这是什么、能不能叠"，不参与判定；格数与叠法在 <see cref="Inventory"/>。
/// </summary>
/// <param name="Id">物品 id，表内唯一，存档里写的就是它。</param>
/// <param name="Name">显示名（如"半张饼"）。</param>
/// <param name="Kind">分类：food / tool / token / paper。只给 UI 分组用，不参与规则。</param>
/// <param name="Stackable">能不能叠。<c>stack=1</c> 可叠，<c>stack=0</c> 一件一格。</param>
/// <param name="Note">设计备注，只给人看。</param>
public sealed record ItemDef(
    string Id,
    string Name,
    string Kind,
    bool Stackable,
    string Note);

/// <summary>
/// 物品表：<c>Content/data/items.csv</c> 的内存形式，按文件顺序保存。
/// 纯 C#，不依赖引擎；破坏性的问题（缺列、空 id、重复 id、stack 不是 0/1）读表即抛，
/// 因为一张读歪的表会让背包规则悄悄失效，比崩一次难查得多（照 <see cref="BeatTable"/> 的规矩）。
/// </summary>
public sealed class ItemCatalog
{
    /// <summary>表里必须有的列。</summary>
    private static readonly string[] RequiredColumns = { "id", "name", "kind", "stack", "note" };

    private readonly List<ItemDef> _all;
    private readonly Dictionary<string, ItemDef> _byId;

    /// <summary>全部物品，按文件顺序。</summary>
    public IReadOnlyList<ItemDef> All => _all;

    /// <summary>物品条数。</summary>
    public int Count => _all.Count;

    private ItemCatalog(List<ItemDef> all)
    {
        _all = all;
        _byId = new Dictionary<string, ItemDef>(all.Count, StringComparer.Ordinal);
        foreach (var def in all) _byId[def.Id] = def;
    }

    /// <summary>
    /// 读表文件（UTF-8，带不带 BOM 都行）。
    /// </summary>
    /// <param name="csvPath">items.csv 的路径。</param>
    /// <returns>物品表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、stack 不是 0 / 1。</exception>
    public static ItemCatalog Load(string csvPath)
    {
        if (csvPath == null) throw new ArgumentNullException(nameof(csvPath));

        return FromCsv(File.ReadAllText(csvPath, Encoding.UTF8));
    }

    /// <summary>
    /// 从 CSV 文本建表。空行会被忽略；行号按它在文本里的位置算（表头是第 1 行）。
    /// </summary>
    /// <param name="csvText">CSV 全文。</param>
    /// <returns>物品表。</returns>
    /// <exception cref="InvalidDataException">缺列、空 id、重复 id、stack 不是 0 / 1。</exception>
    public static ItemCatalog FromCsv(string csvText)
    {
        var rows = Csv.Parse(csvText ?? "");
        if (rows.Count == 0)
            throw new InvalidDataException("物品表是空的：连表头都没有。");

        var header = rows[0];
        var col = new Dictionary<string, int>(header.Length, StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++) col[header[i].Trim()] = i;

        foreach (var name in RequiredColumns)
            if (!col.ContainsKey(name))
                throw new InvalidDataException($"物品表第 1 行：缺少列「{name}」。");

        var list = new List<ItemDef>(Math.Max(0, rows.Count - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1; // 表头占第 1 行
            var row = rows[i];

            string Get(string name) =>
                col.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : "";

            var id = Get("id");
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException($"物品表第 {line} 行：id 为空。");

            if (!seen.Add(id))
                throw new InvalidDataException($"物品表第 {line} 行：id「{id}」重复。");

            var stackText = Get("stack");
            bool stackable;
            if (stackText == "1") stackable = true;
            else if (stackText == "0") stackable = false;
            else
                throw new InvalidDataException($"物品表第 {line} 行：stack「{stackText}」只能是 0 或 1。");

            list.Add(new ItemDef(id, Get("name"), Get("kind"), stackable, Get("note")));
        }

        return new ItemCatalog(list);
    }

    /// <summary>
    /// 按 id 取物品。
    /// </summary>
    /// <param name="id">物品 id。</param>
    /// <returns>找到返回定义，找不到返回 null（调用方按"不可叠"处理，别替玩家丢东西）。</returns>
    public ItemDef? TryGet(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _byId.TryGetValue(id!, out var def) ? def : null;
    }
}
