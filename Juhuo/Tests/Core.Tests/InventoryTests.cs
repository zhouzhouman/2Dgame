using System;
using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 囊与物品表（票号 S1-03b）。守三件事：
/// ① 12 格是真的：第 13 件放不进去，且要说人话，不许静默丢弃；
/// ② 可叠的并成一格（上限 99），不可叠的一件一格；
/// ③ 存档形状不变，表里没有的 id 读回来还留着。
/// </summary>
public class InventoryTests
{
    private const string MiniItems =
        "id,name,kind,stack,note\n" +
        "bread,半张饼,food,1,\n" +
        "shoe,童鞋,tool,0,阿禾弟弟的\n" +
        "coin,铜钱,token,1,\n" +
        "letter,未寄出的信,paper,0,\n";

    private static ItemCatalog Catalog() => ItemCatalog.FromCsv(MiniItems);

    private static Inventory Bag() => new();

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 BeatMachineTests 同一套找法）。</summary>
    private static string? FindData(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var nested = Path.Combine(dir.FullName, "Juhuo", "Content", "data", fileName);
            if (File.Exists(nested)) return nested;

            var local = Path.Combine(dir.FullName, "Content", "data", fileName);
            if (File.Exists(local)) return local;

            dir = dir.Parent;
        }

        return null;
    }

    // ————————————————————————— 放东西 —————————————————————————

    [Fact]
    public void 可叠的东西并成同一格()
    {
        var bag = Bag();
        Assert.True(bag.TryAdd("bread", 3, Catalog(), out var e1), e1);
        Assert.True(bag.TryAdd("bread", 2, Catalog(), out var e2), e2);

        Assert.Equal(1, bag.SlotsUsed);
        Assert.Equal(5, bag.Count("bread"));
        Assert.Single(bag.Items);
        Assert.Equal(5, bag.Items[0].N);
    }

    [Fact]
    public void 一叠最多九十九件多的拒收()
    {
        var bag = Bag();
        Assert.True(bag.TryAdd("bread", 99, Catalog(), out var e1), e1);

        Assert.False(bag.TryAdd("bread", 1, Catalog(), out var error));
        Assert.Contains("一叠装不下", error);
        Assert.Equal(99, bag.Count("bread"));
        Assert.Equal(1, bag.SlotsUsed);
    }

    [Fact]
    public void 不可叠的一件占一格()
    {
        var bag = Bag();
        Assert.True(bag.TryAdd("shoe", 2, Catalog(), out var error), error);

        Assert.Equal(2, bag.SlotsUsed);
        Assert.Equal(2, bag.Count("shoe"));
        Assert.All(bag.Items, i => Assert.Equal(1, i.N));
    }

    [Fact]
    public void 第十三件放不进去且给一句人话()
    {
        var bag = Bag();
        for (var i = 0; i < 12; i++)
            Assert.True(bag.TryAdd("letter", 1, Catalog(), out var e), e);

        Assert.True(bag.IsFull);

        Assert.False(bag.TryAdd("letter", 1, Catalog(), out var error));
        Assert.Contains("囊里满了", error);
        Assert.Equal(Inventory.FullMessage, error);
        Assert.Equal(12, bag.SlotsUsed);
        Assert.Equal(12, bag.Count("letter"));
    }

    [Fact]
    public void 格满之后再放还是失败且不会少东西()
    {
        var bag = Bag();
        for (var i = 0; i < 12; i++) bag.TryAdd("letter", 1, Catalog(), out _);

        Assert.False(bag.TryAdd("bread", 1, Catalog(), out var first));
        Assert.False(bag.TryAdd("bread", 1, Catalog(), out var second));

        Assert.Equal(Inventory.FullMessage, first);
        Assert.Equal(Inventory.FullMessage, second);
        Assert.Equal(12, bag.SlotsUsed);
        Assert.Equal(0, bag.Count("bread"));
    }

    [Fact]
    public void 表里没有的id按不可叠收下()
    {
        var bag = Bag();

        Assert.True(bag.TryAdd("unknown_thing", 2, Catalog(), out var error), error);
        Assert.Equal(2, bag.SlotsUsed);
        Assert.Equal(2, bag.Count("unknown_thing"));
    }

    [Fact]
    public void 件数不对时拒绝()
    {
        var bag = Bag();

        Assert.False(bag.TryAdd("bread", 0, Catalog(), out var zero));
        Assert.False(bag.TryAdd("", 1, Catalog(), out var empty));

        Assert.NotNull(zero);
        Assert.NotNull(empty);
        Assert.Empty(bag.Items);
    }

    // ————————————————————————— 拿东西 —————————————————————————

    [Fact]
    public void 拿空了那格就空出来()
    {
        var bag = Bag();
        bag.TryAdd("bread", 3, Catalog(), out _);

        Assert.Equal(2, bag.Remove("bread", 2));
        Assert.Equal(1, bag.SlotsUsed);
        Assert.Equal(1, bag.Count("bread"));

        Assert.Equal(1, bag.Remove("bread", 5));
        Assert.Equal(0, bag.SlotsUsed);
        Assert.Empty(bag.Items);
        Assert.False(bag.IsFull);
    }

    [Fact]
    public void 拿走没有的东西返回零()
    {
        var bag = Bag();

        Assert.Equal(0, bag.Remove("bread", 1));
        Assert.Equal(0, bag.Remove("", 1));
        Assert.Empty(bag.Items);
    }

    [Fact]
    public void 空出一格之后能再放()
    {
        var bag = Bag();
        for (var i = 0; i < 12; i++) bag.TryAdd("letter", 1, Catalog(), out _);

        Assert.False(bag.TryAdd("bread", 1, Catalog(), out _));
        Assert.Equal(1, bag.Remove("letter", 1));
        Assert.True(bag.TryAdd("bread", 1, Catalog(), out var error), error);
        Assert.Equal(12, bag.SlotsUsed);
    }

    // ————————————————————————— 存档 —————————————————————————

    [Fact]
    public void 存档往返形状不变()
    {
        var bag = Bag();
        bag.TryAdd("bread", 2, Catalog(), out _);
        bag.TryAdd("shoe", 1, Catalog(), out _);

        var state = new SaveState { Inventory = bag.ToSave() };
        var json = state.ToJson();

        Assert.Contains("\"inventory\"", json);
        Assert.Contains("\"item\": \"bread\"", json);
        Assert.Contains("\"n\": 2", json);

        var back = SaveState.FromJson(json);
        var again = Bag();
        again.LoadFrom(back.Inventory);

        Assert.Equal(2, again.SlotsUsed);
        Assert.Equal(2, again.Count("bread"));
        Assert.Equal(1, again.Count("shoe"));
        Assert.Equal(bag.Items.Select(i => i.Item), again.Items.Select(i => i.Item));
    }

    [Fact]
    public void 读档遇到表里没有的id照样留着()
    {
        var bag = Bag();

        var ex = Record.Exception(() => bag.LoadFrom(new[]
        {
            new InventoryEntry { Item = "bread", N = 2 },
            new InventoryEntry { Item = "从旧版本留下的东西", N = 1 },
        }));

        Assert.Null(ex);
        Assert.Equal(2, bag.SlotsUsed);
        Assert.Equal(1, bag.Count("从旧版本留下的东西"));

        // 表里没有它，也不影响之后的规则：照样算一格、照样能拿。
        Assert.Equal(1, bag.Remove("从旧版本留下的东西", 1));
        Assert.Equal(1, bag.SlotsUsed);
    }

    [Fact]
    public void 读档丢掉空id与零件的烂行()
    {
        var bag = Bag();

        bag.LoadFrom(new InventoryEntry?[]
        {
            null,
            new InventoryEntry { Item = "", N = 3 },
            new InventoryEntry { Item = "bread", N = 0 },
            new InventoryEntry { Item = "coin", N = 4 },
        });

        Assert.Single(bag.Items);
        Assert.Equal(4, bag.Count("coin"));
    }

    [Fact]
    public void 读档超过十二格也不丢东西()
    {
        var bag = Bag();

        bag.LoadFrom(Enumerable.Range(0, 15)
            .Select(i => new InventoryEntry { Item = $"old{i}", N = 1 })
            .ToArray());

        // 收下 15 格（玩家手上的东西不许凭空消失），但之后放不进新的。
        Assert.Equal(15, bag.SlotsUsed);
        Assert.True(bag.IsFull);
        Assert.False(bag.TryAdd("bread", 1, Catalog(), out var error));
        Assert.Equal(Inventory.FullMessage, error);
    }

    [Fact]
    public void 写档校验与存档服务用同一句话()
    {
        var entries = Enumerable.Range(0, 13)
            .Select(i => new InventoryEntry { Item = $"x{i}", N = 1 })
            .ToList();

        var error = Inventory.Validate(entries);

        Assert.NotNull(error);
        Assert.Contains("背包装不下", error);
        Assert.Equal(Inventory.DefaultCapacity, 12);
    }

    // ————————————————————————— 物品表 —————————————————————————

    [Fact]
    public void 物品表能按id取到定义()
    {
        var catalog = Catalog();

        Assert.Equal(4, catalog.Count);
        Assert.Equal("半张饼", catalog.TryGet("bread")!.Name);
        Assert.Equal("food", catalog.TryGet("bread")!.Kind);
        Assert.True(catalog.TryGet("bread")!.Stackable);
        Assert.False(catalog.TryGet("shoe")!.Stackable);
        Assert.Null(catalog.TryGet("nope"));
        Assert.Null(catalog.TryGet(null));
    }

    [Fact]
    public void 物品表缺列时带行号抛错()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ItemCatalog.FromCsv("id,name,kind,note\nbread,半张饼,food,\n"));

        Assert.Contains("第 1 行", ex.Message);
        Assert.Contains("stack", ex.Message);
    }

    [Fact]
    public void 物品表空id与重复id都带行号抛错()
    {
        var empty = Assert.Throws<InvalidDataException>(() =>
            ItemCatalog.FromCsv(MiniItems + ",无名,food,1,\n"));
        Assert.Contains("第 6 行", empty.Message);
        Assert.Contains("id 为空", empty.Message);

        var dup = Assert.Throws<InvalidDataException>(() =>
            ItemCatalog.FromCsv(MiniItems + "bread,再来一张,food,1,\n"));
        Assert.Contains("第 6 行", dup.Message);
        Assert.Contains("重复", dup.Message);
    }

    [Fact]
    public void 物品表stack不是零一也抛错()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ItemCatalog.FromCsv(MiniItems + "tea,茶,food,yes,\n"));

        Assert.Contains("第 6 行", ex.Message);
        Assert.Contains("stack", ex.Message);
    }

    [Fact]
    public void 真表能读出至少二十件()
    {
        var path = FindData("items.csv");
        if (path == null) return; // 数据还没生成：这条跳过，不算红。

        var catalog = ItemCatalog.Load(path);

        Assert.True(catalog.Count >= 20, $"items.csv 只有 {catalog.Count} 件");
        Assert.All(catalog.All, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));
        Assert.All(catalog.All, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }
}
