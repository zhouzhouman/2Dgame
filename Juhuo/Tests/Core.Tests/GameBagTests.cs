using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 囊与"给他看"的接线（票号 S1-03c）。
///
/// 测的是**规则与文案**这一层：loot 怎么拆、装不装得下、丢一件之后格数怎么变、
/// 三种反应分不分得开、旗标落没落、读档还认不认。按 E 之后画面长什么样归
/// <c>--bag</c> / <c>--playthrough</c> 那几条无头自检，这里不起 Godot。
/// </summary>
public class GameBagTests
{
    private const string MiniItems =
        "id,name,kind,stack,note\n" +
        "coin,铜钱,token,1,\n" +
        "cloth,干净衣裳,tool,1,\n" +
        "bread,半张饼,food,1,\n" +
        "shoe,童鞋,tool,0,\n" +
        "abacus,算盘,tool,0,\n";

    private static ItemCatalog Catalog() => ItemCatalog.FromCsv(MiniItems);

    /// <summary>装到一半失败会留下半批东西，所以每一步都断言放进去了。</summary>
    private static Inventory Bag(params (string Id, int N)[] items)
    {
        var catalog = Catalog();
        var bag = new Inventory();

        foreach (var (id, n) in items)
            Assert.True(bag.TryAdd(id, n, catalog, out _), $"测试自己没装进去：{id}×{n}");

        return bag;
    }

    /// <summary>装 12 只不可叠的鞋，把囊塞满。</summary>
    private static Inventory FullBag()
    {
        var catalog = Catalog();
        var bag = new Inventory();

        for (var i = 0; i < bag.Capacity; i++)
            Assert.True(bag.TryAdd("shoe", 1, catalog, out _));

        return bag;
    }

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 GiftTests 同一套找法）。</summary>
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

    // ————————————————————————— loot 解析 —————————————————————————

    [Fact]
    public void loot_单件能拆出id与件数()
    {
        var loot = BagLoot.Parse("coin:3");

        Assert.Single(loot);
        Assert.Equal("coin", loot[0].ItemId);
        Assert.Equal(3, loot[0].N);
    }

    [Fact]
    public void loot_多件_竖线与分号都认()
    {
        var bar = BagLoot.Parse("coin:14|cloth:1");
        Assert.Equal(2, bar.Count);
        Assert.Equal("coin", bar[0].ItemId);
        Assert.Equal(14, bar[0].N);
        Assert.Equal("cloth", bar[1].ItemId);
        Assert.Equal(1, bar[1].N);

        var semi = BagLoot.Parse("coin:1;bread:2");
        Assert.Equal(2, semi.Count);
        Assert.Equal("bread", semi[1].ItemId);
        Assert.Equal(2, semi[1].N);
    }

    [Fact]
    public void loot_空串与写坏的段_只丢那一段()
    {
        Assert.Empty(BagLoot.Parse(null));
        Assert.Empty(BagLoot.Parse(""));
        Assert.Empty(BagLoot.Parse("   "));

        // 没有冒号的按一件算；件数不是正数的整段丢掉
        var loot = BagLoot.Parse("coin|coin:0|:3|cloth:x|bread:2");
        Assert.Equal(2, loot.Count);
        Assert.Equal("coin", loot[0].ItemId);
        Assert.Equal(1, loot[0].N);
        Assert.Equal("bread", loot[1].ItemId);
        Assert.Equal(2, loot[1].N);
    }

    // ————————————————————————— 捡东西 —————————————————————————

    [Fact]
    public void 捡起后_格数与件数都对_可叠的并进同一格()
    {
        var catalog = Catalog();
        var bag = new Inventory();

        var plan = BagSearch.Plan(first: true, "coin:14|cloth:1", bag, catalog);
        Assert.True(plan.CanSearch);

        var picked = BagPickup.Apply(plan.Loot, bag, catalog);
        Assert.Equal(2, picked.Count);
        Assert.Equal(2, bag.SlotsUsed);
        Assert.Equal(14, bag.Count("coin"));
        Assert.Equal(1, bag.Count("cloth"));

        // 再捡一枚铜钱：并进原来那一格，不新开一格
        var again = BagPickup.Apply(BagSearch.Plan(true, "coin:1", bag, catalog).Loot, bag, catalog);
        Assert.Single(again);
        Assert.Equal(15, bag.Count("coin"));
        Assert.Equal(2, bag.SlotsUsed);
    }

    [Fact]
    public void 囊满时_拒收_一件都不动()
    {
        var bag = FullBag();
        var before = bag.ToSave();

        var plan = BagSearch.Plan(first: true, "coin:3", bag, Catalog());

        Assert.False(plan.CanSearch);                  // 这一处不算搜过
        Assert.Equal("coin", plan.Loot.RejectedId);    // 拒的是哪一件
        Assert.False(plan.Loot.Any);                   // 半批都不收
        Assert.False(string.IsNullOrEmpty(plan.Refusal));
        Assert.Contains("满", plan.Refusal!);

        Assert.Equal(12, bag.SlotsUsed);
        Assert.Equal(0, bag.Count("coin"));
        Assert.Equal(before.Count, bag.ToSave().Count);
    }

    [Fact]
    public void 一批里有一件装不下_整批退回()
    {
        var catalog = Catalog();
        var bag = new Inventory();

        // 11 只鞋：还有 1 格。钱进得去，第二只鞋进不去。
        for (var i = 0; i < 11; i++) Assert.True(bag.TryAdd("shoe", 1, catalog, out _));

        var plan = BagSearch.Plan(first: true, "coin:1|shoe:1", bag, catalog);

        Assert.False(plan.CanSearch);
        Assert.Equal("shoe", plan.Loot.RejectedId);
        Assert.Equal(0, bag.Count("coin"));         // 钱也没进来
        Assert.Equal(11, bag.SlotsUsed);
    }

    [Fact]
    public void 重搜不收东西_只给文本()
    {
        var bag = new Inventory();

        var plan = BagSearch.Plan(first: false, "coin:3", bag, Catalog());

        Assert.True(plan.CanSearch);
        Assert.False(plan.Loot.Any);
        Assert.Equal(0, bag.Count("coin"));
    }

    // ————————————————————————— 面板：选与丢 —————————————————————————

    [Fact]
    public void 丢掉可叠物品_一次减一件_减到零才腾格()
    {
        var bag = Bag(("coin", 3));
        var panel = new BagPanel(bag, Catalog());

        Assert.Equal(1, panel.SlotsUsed);
        Assert.True(panel.Drop());
        Assert.Equal(2, bag.Count("coin"));
        Assert.Equal(1, panel.SlotsUsed);

        Assert.True(panel.Drop());
        Assert.True(panel.Drop());
        Assert.Equal(0, bag.Count("coin"));
        Assert.Equal(0, panel.SlotsUsed);
        Assert.False(panel.Drop());                 // 空囊再按没有反应
    }

    [Fact]
    public void 丢掉不可叠物品_腾出一格()
    {
        var bag = Bag(("shoe", 1), ("abacus", 1));
        var panel = new BagPanel(bag, Catalog());

        Assert.Equal(2, panel.SlotsUsed);
        Assert.True(panel.Drop());
        Assert.Equal(1, panel.SlotsUsed);
        Assert.Equal(0, bag.Count("shoe"));
        Assert.Equal(1, bag.Count("abacus"));
    }

    [Fact]
    public void 方向键在格子里走_不绕圈_不越界()
    {
        var bag = Bag(("coin", 3), ("shoe", 1), ("abacus", 1), ("cloth", 2), ("bread", 1));
        var panel = new BagPanel(bag, Catalog());

        Assert.Equal(0, panel.Selected);
        panel.Move(1, 0);
        Assert.Equal(1, panel.Selected);

        panel.Move(-1, 0);
        Assert.Equal(0, panel.Selected);

        panel.Move(-1, 0);                          // 已经在最左，停住
        Assert.Equal(0, panel.Selected);

        panel.Move(0, 1);                           // 下到第二行第一格
        Assert.Equal(4, panel.Selected);

        panel.Move(0, 1);                           // 只剩五格，再往下没有第二行
        Assert.Equal(4, panel.Selected);
    }

    [Fact]
    public void 面板标签用显示名_空格留白_角上是格数()
    {
        var bag = Bag(("coin", 3), ("cloth", 1));
        var panel = new BagPanel(bag, Catalog());

        Assert.Equal("铜钱 ×3", panel.LabelAt(0));
        Assert.Equal("干净衣裳 ×1", panel.LabelAt(1));
        Assert.Equal("", panel.LabelAt(2));
        Assert.Equal("2/12", panel.CountLabel);
        Assert.Equal("coin", panel.SelectedItem);
    }

    // ————————————————————————— 给他看 —————————————————————————

    [Fact]
    public void 给他看_三种反应互不相同()
    {
        var silent = GiftPresenter.Reaction(new GiftResult(true, true, new[] { "gift.ahe.shoe" }));
        var notMine = GiftPresenter.Reaction(new GiftResult(true, false, Array.Empty<string>()));
        var nothing = GiftPresenter.Reaction(GiftResult.None);

        Assert.NotEqual(silent, notMine);
        Assert.NotEqual(silent, nothing);
        Assert.NotEqual(notMine, nothing);

        // 事先知道 → 沉默很久；认得出但没细看 → "这不是我的"
        Assert.Contains("很久", silent);
        Assert.Contains("这不是我的", notMine);
    }

    [Fact]
    public void 给他看_命中且事先知道_把Applied落进旗标仓库()
    {
        var gifts = GiftTable.FromCsv(
            "npc,item,requires,sets,note\n" +
            "阿禾,shoe,knows_shoe_inward,\"gift.ah_e.shoe_given\",\n");

        var flags = new FlagStore();
        Assert.True(flags.Set("knows_shoe_inward"));

        var result = gifts.Show("阿禾", "shoe", name => flags.Has(name, "hub1"));

        Assert.True(result.Matched);
        Assert.True(result.KnownBefore);
        Assert.Contains("gift.ah_e.shoe_given", result.Applied);

        // Show 自己不写旗标，落旗标是调用方的事（World.ShowItem 就是这么接的）
        foreach (var flag in result.Applied) Assert.True(flags.Set(flag));
        Assert.True(flags.Has("gift.ah_e.shoe_given", "hub1"));
    }

    [Fact]
    public void 给他看_认得出但事先不知道_不落旗标()
    {
        var gifts = GiftTable.FromCsv(
            "npc,item,requires,sets,note\n" +
            "阿禾,shoe,knows_shoe_inward,\"gift.ah_e.shoe_given\",\n");

        var flags = new FlagStore();
        var result = gifts.Show("阿禾", "shoe", name => flags.Has(name, "hub1"));

        Assert.True(result.Matched);
        Assert.False(result.KnownBefore);
        Assert.Empty(result.Applied);
    }

    // ————————————————————————— 报表与 HUD —————————————————————————

    [Fact]
    public void bag输出_行数与内容()
    {
        var bag = Bag(("coin", 3), ("cloth", 1), ("shoe", 1));
        var lines = BagReport.Lines(bag, Catalog(), picked: 3);

        Assert.Equal(bag.SlotsUsed + 1, lines.Count);
        Assert.Equal(4, lines.Count);
        Assert.Equal("[囊] 拾得 3 件，占 3/12 格", lines[0]);
        Assert.Equal("[囊] 1 铜钱 ×3", lines[1]);
        Assert.Equal("[囊] 2 干净衣裳 ×1", lines[2]);
        Assert.Equal("[囊] 3 童鞋 ×1", lines[3]);

        Assert.Equal("[囊] 满了，拒收：铜钱", BagReport.RejectedLine(Catalog(), "coin"));
    }

    [Fact]
    public void HUD_拼上囊的格数_快满才变色()
    {
        var bag = new Inventory();

        Assert.Equal("　囊 0/12", BagHud.Suffix(bag));
        Assert.False(BagHud.Warn(bag));

        for (var i = 0; i < 9; i++) Assert.True(bag.TryAdd("shoe", 1, Catalog(), out _));
        Assert.Equal("　囊 9/12", BagHud.Suffix(bag));
        Assert.False(BagHud.Warn(bag));

        Assert.True(bag.TryAdd("shoe", 1, Catalog(), out _));
        Assert.True(BagHud.Warn(bag));               // 10 格起变色
    }

    // ————————————————————————— 存档 —————————————————————————

    [Fact]
    public void 读档后_背包原样还原_顺序和件数都不变()
    {
        var bag = Bag(("coin", 14), ("cloth", 1), ("shoe", 2));
        var saved = bag.ToSave();

        var back = new Inventory();
        back.LoadFrom(saved);

        Assert.Equal(bag.SlotsUsed, back.SlotsUsed);
        Assert.Equal(bag.Count("coin"), back.Count("coin"));
        Assert.Equal(bag.Count("shoe"), back.Count("shoe"));

        for (var i = 0; i < bag.SlotsUsed; i++)
        {
            Assert.Equal(bag.Items[i].Item, back.Items[i].Item);
            Assert.Equal(bag.Items[i].N, back.Items[i].N);
        }
    }

    // ————————————————————————— 真表 —————————————————————————

    [Fact]
    public void 真表里的coin叫铜钱_hub1柜子掉钱与衣裳()
    {
        var itemsPath = FindData("items.csv");
        Assert.NotNull(itemsPath);

        var catalog = ItemCatalog.Load(itemsPath!);
        Assert.Equal("铜钱", catalog.TryGet("coin")?.Name);
        Assert.Equal("干净衣裳", catalog.TryGet("cloth")?.Name);

        // points.csv 的 h1_03 是 coin:14|cloth:1（竖线，不是分号）
        var pointsPath = FindData("points.csv");
        Assert.NotNull(pointsPath);

        var table = PointTable.FromCsv(File.ReadAllText(pointsPath!));
        var cabinet = table.All.First(p => p.Id == "h1_03");

        var loot = BagLoot.Parse(cabinet.Loot);
        Assert.Equal(2, loot.Count);
        Assert.Equal("coin", loot[0].ItemId);
        Assert.Equal(14, loot[0].N);
        Assert.Equal("cloth", loot[1].ItemId);

        var bag = new Inventory();
        var plan = BagSearch.Plan(true, cabinet.Loot, bag, catalog);
        Assert.True(plan.CanSearch);

        BagPickup.Apply(plan.Loot, bag, catalog);
        Assert.Equal(2, bag.SlotsUsed);
        Assert.Equal("（+14 铜钱 · +1 干净衣裳）", BagPickup.Describe(bag.Items.Select(i => new LootEntry(i.Item, i.N)).ToList(), catalog));
    }
}
