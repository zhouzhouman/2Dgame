using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 选项效果（票号 S3-02b）：把"选了某一项之后发生什么"从代码搬进 <c>beats.csv</c> 的 <c>effects</c> 列。
///
/// 守四件事：
/// ① 七种原子都能解析，坏数据（未知原子 / 缺值 / 非法数字 / 非法旗标 / 物品不存在 / 组数不符）带行号炸；
/// ② 结算**先全量校验再施加**——囊满或钱不够时整组不落，不会留下半截账；
/// ③ 返回的每一行都是中文人话，正负号用真正的减号；
/// ④ 缺 effects 列的旧表照常读，真表的组数必须与选项数一致。
/// </summary>
public class BeatEffectTests
{
    // ————————————————————————— 夹具 —————————————————————————

    private const string ItemsCsv =
        "id,name,kind,stack,note\n" +
        "coin,铜钱,token,1,钱\n" +
        "grain,谷子,food,1,粮\n" +
        "sword,刀,tool,0,不叠\n";

    private static ItemCatalog Catalog() => ItemCatalog.FromCsv(ItemsCsv);

    /// <summary>建一份只有这些物品的囊（用真目录，叠放规则才准）。</summary>
    private static Inventory Bag(params (string Id, int N)[] items)
    {
        var bag = new Inventory();
        foreach (var (id, n) in items)
            Assert.True(bag.TryAdd(id, n, Catalog(), out var error), error);
        return bag;
    }

    /// <summary>直接拼一个带效果的节拍；选项文案按效果组数补齐。</summary>
    private static Beat MakeBeat(IReadOnlyList<IReadOnlyList<Effect>> effects, int optionCount = -1)
    {
        var count = optionCount >= 0 ? optionCount : effects.Count;
        var options = new string[count];
        for (var i = 0; i < count; i++) options[i] = $"选项{i + 1}";

        return new Beat("t1", "试", "hub1", 1, "", Array.Empty<string>(), "choice", options, "")
        {
            Effects = effects,
        };
    }

    /// <summary>从 effects 原文拼一个节拍（解析走真路径）。</summary>
    private static Beat BeatOf(string effectsColumn, int optionCount) =>
        MakeBeat(BeatEffect.ParseAll(effectsColumn, 9, optionCount), optionCount);

    /// <summary>背包快照：只看东西和件数，用来断言"一点没动"。</summary>
    private static string Snap(Inventory bag) =>
        string.Join("|", bag.Items.Select(e => $"{e.Item}:{e.N}"));

    private static ApplyResult Apply(
        Beat beat, int index, TrustState trust, Inventory bag, ref int troops, FlagStore? flags = null)
    {
        var catalog = Catalog();
        return BeatEffectBook.Apply(beat, index, trust, bag, catalog, ref troops, flags);
    }

    // ————————————————————————— 解析 —————————————————————————

    [Fact]
    public void 解析钱的正负增量()
    {
        var gain = BeatEffect.Parse("coin:+15", 2, 0);
        Assert.Single(gain);
        Assert.Equal(EffectKind.Coin, gain[0].Kind);
        Assert.Equal(15, gain[0].Amount);
        Assert.Null(gain[0].Target);

        var loss = BeatEffect.Parse("coin:-10", 2, 0);
        Assert.Equal(-10, loss[0].Amount);
    }

    [Fact]
    public void 解析物品与件数()
    {
        var effects = BeatEffect.Parse("item:grain=3", 2, 0);
        Assert.Single(effects);
        Assert.Equal(EffectKind.Item, effects[0].Kind);
        Assert.Equal(3, effects[0].Amount);
        Assert.Equal("grain", effects[0].Target);
    }

    [Fact]
    public void 解析三条信任轴()
    {
        var effects = BeatEffect.Parse("guan:+3;min:-15;yi:+1", 2, 0);

        Assert.Equal(3, effects.Count);
        Assert.Equal(EffectKind.Guan, effects[0].Kind);
        Assert.Equal(3, effects[0].Amount);
        Assert.Equal(EffectKind.Min, effects[1].Kind);
        Assert.Equal(-15, effects[1].Amount);
        Assert.Equal(EffectKind.Yi, effects[2].Kind);
        Assert.Equal(1, effects[2].Amount);
    }

    [Fact]
    public void 解析兵力()
    {
        var effects = BeatEffect.Parse("troops:+10", 2, 0);
        Assert.Equal(EffectKind.Troops, effects[0].Kind);
        Assert.Equal(10, effects[0].Amount);
    }

    [Fact]
    public void 解析旗标()
    {
        var effects = BeatEffect.Parse("flag:raided", 2, 0);
        Assert.Equal(EffectKind.Flag, effects[0].Kind);
        Assert.Equal("raided", effects[0].Target);
        Assert.Equal(0, effects[0].Amount);
    }

    [Fact]
    public void 空组就是没有效果()
    {
        Assert.Empty(BeatEffect.Parse("", 2, 0));
        Assert.Empty(BeatEffect.Parse("   ", 2, 0));
        Assert.Empty(BeatEffect.Parse(null, 2, 0));
    }

    [Fact]
    public void 一组里多个原子保持书写顺序()
    {
        var effects = BeatEffect.Parse("coin:+15;item:grain=3;flag:raided", 2, 0);

        Assert.Equal(3, effects.Count);
        Assert.Equal(EffectKind.Coin, effects[0].Kind);
        Assert.Equal(EffectKind.Item, effects[1].Kind);
        Assert.Equal(EffectKind.Flag, effects[2].Kind);
    }

    [Fact]
    public void 多余的空白段被跳过而不是报错()
    {
        var effects = BeatEffect.Parse(" coin:+1 ;; flag:raided ; ", 2, 0);
        Assert.Equal(2, effects.Count);
    }

    [Fact]
    public void 未知原子带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BeatEffect.Parse("gold:+1", 42, 0));
        Assert.Contains("第 42 行", ex.Message);
        Assert.Contains("gold", ex.Message);
    }

    [Fact]
    public void 缺冒号带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BeatEffect.Parse("coin15", 7, 1));
        Assert.Contains("第 7 行", ex.Message);
        Assert.Contains("第 2 个选项", ex.Message);
    }

    [Fact]
    public void 缺值带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BeatEffect.Parse("coin:", 3, 0));
        Assert.Contains("第 3 行", ex.Message);
    }

    [Fact]
    public void 非法数字带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BeatEffect.Parse("min:abc", 4, 0));
        Assert.Contains("第 4 行", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void item缺等号带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BeatEffect.Parse("item:grain", 5, 0));
        Assert.Contains("第 5 行", ex.Message);
    }

    [Fact]
    public void item件数不是正整数抛()
    {
        Assert.Throws<InvalidDataException>(() => BeatEffect.Parse("item:grain=0", 5, 0));
        Assert.Throws<InvalidDataException>(() => BeatEffect.Parse("item:grain=-2", 5, 0));
    }

    [Fact]
    public void item的id不在物品表时带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => BeatEffect.Parse("item:no_such_thing=1", 11, 0, Catalog()));

        Assert.Contains("第 11 行", ex.Message);
        Assert.Contains("no_such_thing", ex.Message);
    }

    [Fact]
    public void 旗标名不合法时结算整组报失败而不是悄悄丢掉()
    {
        var bag = Bag(("coin", 10));
        var trust = new TrustState { Min = 50 };
        var flags = new FlagStore();
        var troops = 0;

        // 解析放行（名字非空即可），但 FlagStore 写不进去的名字必须在结算时露出来。
        var result = Apply(BeatOf("min:-5;flag:坏名字", 1), 0, trust, bag, ref troops, flags);

        Assert.False(result.Ok);
        Assert.Contains("坏名字", result.Reason);
        Assert.Equal(50, trust.Min);
        Assert.Equal(10, bag.Count("coin"));
    }

    [Fact]
    public void 组数与选项数不符带行号抛()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => BeatEffect.ParseAll("coin:+15|min:+1", 8, 3));

        Assert.Contains("第 8 行", ex.Message);
        Assert.Contains("2 组", ex.Message);
        Assert.Contains("3 条", ex.Message);
    }

    [Fact]
    public void 组数相符时逐组解析()
    {
        var groups = BeatEffect.ParseAll("coin:+15;item:grain=3|min:-15;yi:+1;flag:raided", 2, 2);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Count);
        Assert.Equal(3, groups[1].Count);
        Assert.Equal(EffectKind.Coin, groups[0][0].Kind);
        Assert.Equal(EffectKind.Flag, groups[1][2].Kind);
    }

    [Fact]
    public void 整格空白等于每个选项都没有效果()
    {
        var groups = BeatEffect.ParseAll("", 2, 3);

        Assert.Equal(3, groups.Count);
        Assert.All(groups, g => Assert.Empty(g));
    }

    [Fact]
    public void 没有选项也没有效果时是空表()
    {
        Assert.Empty(BeatEffect.ParseAll("", 2, 0));
    }

    // ————————————————————————— 结算：钱 —————————————————————————

    [Fact]
    public void 钱正向增加()
    {
        var bag = Bag(("coin", 3));
        var trust = new TrustState();
        var troops = 0;

        var result = Apply(BeatOf("coin:+15", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Equal(18, bag.Count("coin"));
        Assert.Equal(new[] { "钱 +15" }, result.Lines);
    }

    [Fact]
    public void 钱负向扣减()
    {
        var bag = Bag(("coin", 20));
        var trust = new TrustState();
        var troops = 0;

        var result = Apply(BeatOf("coin:-15", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Equal(5, bag.Count("coin"));
        Assert.Equal(new[] { "钱 \u221215" }, result.Lines);
    }

    [Fact]
    public void 钱不够时整组不落()
    {
        var bag = Bag(("coin", 3));
        var trust = new TrustState { Min = 40 };
        var flags = new FlagStore();
        var troops = 7;

        var result = Apply(BeatOf("coin:-15;min:+5;flag:fee_paid", 1), 0, trust, bag, ref troops, flags);

        Assert.False(result.Ok);
        Assert.Contains("钱不够", result.Reason);
        Assert.Empty(result.Lines);

        Assert.Equal(3, bag.Count("coin"));
        Assert.Equal(40, trust.Min);
        Assert.Equal(7, troops);
        Assert.False(flags.Has("paid", null));
    }

    [Fact]
    public void 囊满装不下钱时整组不落且状态不变()
    {
        var bag = new Inventory(1);
        Assert.True(bag.TryAdd("sword", 1, Catalog(), out _)); // 唯一的格子被刀占满
        var trust = new TrustState { Guan = 50, Min = 50, Yi = 50 };
        var flags = new FlagStore();
        var troops = 12;

        var before = Snap(bag);
        var result = Apply(BeatOf("flag:rich_paid;coin:+15;guan:+3", 1), 0, trust, bag, ref troops, flags);

        Assert.False(result.Ok);
        Assert.Equal(Inventory.FullMessage, result.Reason);

        Assert.Equal(before, Snap(bag));
        Assert.Equal(50, trust.Guan);
        Assert.Equal(12, troops);
        Assert.False(flags.Has("rich", null));
    }

    [Fact]
    public void 物品id不存在时整组不落且前面那步也不生效()
    {
        var bag = new Inventory(); // 空囊
        var trust = new TrustState();
        var troops = 0;

        // coin 写在前面，但 item 校验失败 → 整组回滚，钱不能先加。
        var result = Apply(BeatOf("coin:+15;item:no_such_thing=1", 1), 0, trust, bag, ref troops);

        Assert.False(result.Ok);
        Assert.Contains("no_such_thing", result.Reason);
        Assert.Equal(0, bag.Count("coin"));
    }

    // ————————————————————————— 结算：物品 —————————————————————————

    [Fact]
    public void 物品增加并用人话报出物品名()
    {
        var bag = new Inventory();
        var trust = new TrustState();
        var troops = 0;

        var result = Apply(BeatOf("item:grain=3", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Equal(3, bag.Count("grain"));
        Assert.Equal(new[] { "谷子 ×3" }, result.Lines);
    }

    [Fact]
    public void 不可叠物品一件占一格_格数不够整组不落()
    {
        var bag = new Inventory(1);
        Assert.True(bag.TryAdd("coin", 1, Catalog(), out _)); // 占掉唯一一格
        var trust = new TrustState();
        var troops = 0;
        var before = Snap(bag);

        var result = Apply(BeatOf("item:sword=2", 1), 0, trust, bag, ref troops);

        Assert.False(result.Ok);
        Assert.Equal(before, Snap(bag));
    }

    // ————————————————————————— 结算：三轴 / 兵 / 旗标 —————————————————————————

    [Fact]
    public void 三条信任轴按增量落账()
    {
        var bag = new Inventory();
        var trust = new TrustState { Guan = 10, Min = 30, Yi = 5 };
        var troops = 0;

        var result = Apply(BeatOf("guan:+3;min:-15;yi:+1", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Equal(13, trust.Guan);
        Assert.Equal(15, trust.Min);
        Assert.Equal(6, trust.Yi);
        Assert.Equal(new[] { "官望 +3", "民望 \u221215", "疑心 +1" }, result.Lines);
    }

    [Fact]
    public void 三条信任轴钳在0到100()
    {
        var bag = new Inventory();
        var trust = new TrustState { Guan = 98, Min = 2, Yi = 100 };
        var troops = 0;

        var result = Apply(BeatOf("guan:+10;min:-10;yi:+5", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Equal(100, trust.Guan);
        Assert.Equal(0, trust.Min);
        Assert.Equal(100, trust.Yi);
    }

    [Fact]
    public void 兵力增减且不落到负数()
    {
        var bag = new Inventory();
        var trust = new TrustState();

        var up = 20;
        var result = Apply(BeatOf("troops:+10", 1), 0, trust, bag, ref up);
        Assert.True(result.Ok);
        Assert.Equal(30, up);
        Assert.Equal(new[] { "兵 +10" }, result.Lines);

        var down = 5;
        Apply(BeatOf("troops:-20", 1), 0, trust, bag, ref down);
        Assert.Equal(0, down);
    }

    [Fact]
    public void 旗标置位并报出名字()
    {
        var bag = new Inventory();
        var trust = new TrustState();
        var flags = new FlagStore();
        var troops = 0;

        var result = Apply(BeatOf("flag:village_raided", 1), 0, trust, bag, ref troops, flags);

        Assert.True(result.Ok);
        Assert.True(flags.Has("village_raided", null));
        Assert.Equal(new[] { "旗标 village_raided" }, result.Lines);
    }

    [Fact]
    public void 没有旗标仓库时跳过旗标这一步()
    {
        var bag = new Inventory();
        var trust = new TrustState();
        var troops = 0;

        var result = Apply(BeatOf("flag:village_raided", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Empty(result.Lines);
    }

    // ————————————————————————— 结算：边界 —————————————————————————

    [Fact]
    public void 空效果组什么都不做()
    {
        var bag = Bag(("coin", 5));
        var trust = new TrustState { Min = 33 };
        var troops = 4;

        var result = Apply(BeatOf("", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Empty(result.Lines);
        Assert.Equal(5, bag.Count("coin"));
        Assert.Equal(33, trust.Min);
        Assert.Equal(4, troops);
    }

    [Fact]
    public void 选项序号越界返回失败而不是抛()
    {
        var bag = new Inventory();
        var trust = new TrustState();
        var troops = 0;

        var result = Apply(BeatOf("coin:+1", 1), 3, trust, bag, ref troops);

        Assert.False(result.Ok);
        Assert.Empty(result.Lines);
        Assert.Equal(0, bag.Count("coin"));
    }

    [Fact]
    public void 数值为零的原子是空操作不产生人话()
    {
        var bag = new Inventory();
        var trust = new TrustState { Min = 10 };
        var troops = 0;

        var result = Apply(BeatOf("min:+0;coin:+0", 1), 0, trust, bag, ref troops);

        Assert.True(result.Ok);
        Assert.Empty(result.Lines);
        Assert.Equal(10, trust.Min);
    }

    [Fact]
    public void 人话行的顺序与书写顺序一致()
    {
        var bag = Bag(("coin", 10));
        var trust = new TrustState();
        var flags = new FlagStore();
        var troops = 0;

        var result = Apply(
            BeatOf("coin:-5;item:grain=1;min:-15;yi:+1;troops:+10;flag:village_raided", 1),
            0, trust, bag, ref troops, flags);

        Assert.True(result.Ok);
        Assert.Equal(
            new[] { "钱 \u22125", "谷子 ×1", "民望 \u221215", "疑心 +1", "兵 +10", "旗标 village_raided" },
            result.Lines);
        Assert.DoesNotContain(result.Lines, l => l.Contains('-'));
    }

    // ————————————————————————— 读表 —————————————————————————

    [Fact]
    public void 缺effects列的旧表照常读且每个选项都是空组()
    {
        const string oldCsv =
            "id,name,hub,day,requires,sets,trigger,options,note\n" +
            "t1,甲,hub1,1,,t1_done,choice,烧掉|带走,旧表\n";

        var table = BeatTable.FromCsv(oldCsv);
        var beat = table.Get("t1")!;

        Assert.Equal(2, beat.Options.Length);
        Assert.Equal(2, beat.Effects.Count);
        Assert.All(beat.Effects, g => Assert.Empty(g));
    }

    [Fact]
    public void 有effects列时逐组读进来()
    {
        const string csv =
            "id,name,hub,day,requires,sets,trigger,options,note,effects\n" +
            "t1,甲,hub1,1,,t1_done,choice,烧掉|带走,备注,coin:+15;item:grain=3|min:-15;yi:+1\n";

        var beat = BeatTable.FromCsv(csv).Get("t1")!;

        Assert.Equal(2, beat.Effects.Count);
        Assert.Equal(2, beat.Effects[0].Count);
        Assert.Equal(EffectKind.Coin, beat.Effects[0][0].Kind);
        Assert.Equal(15, beat.Effects[0][0].Amount);
        Assert.Equal(EffectKind.Item, beat.Effects[0][1].Kind);
        Assert.Equal(2, beat.Effects[1].Count);
        Assert.Equal(-15, beat.Effects[1][0].Amount);
    }

    [Fact]
    public void 有effects列但组数对不上时读表即抛带行号()
    {
        const string csv =
            "id,name,hub,day,requires,sets,trigger,options,note,effects\n" +
            "t1,甲,hub1,1,,t1_done,choice,烧掉|带走,备注,coin:+15\n";

        var ex = Assert.Throws<InvalidDataException>(() => BeatTable.FromCsv(csv));
        Assert.Contains("第 2 行", ex.Message);
    }

    [Fact]
    public void 有effects列但某行留空时该拍全部选项无效果()
    {
        const string csv =
            "id,name,hub,day,requires,sets,trigger,options,note,effects\n" +
            "t1,甲,hub1,1,,t1_done,choice,烧掉|带走,备注,\n";

        var beat = BeatTable.FromCsv(csv).Get("t1")!;

        Assert.Equal(2, beat.Effects.Count);
        Assert.All(beat.Effects, g => Assert.Empty(g));
    }

    [Fact]
    public void 真表每一拍的效果组数都与选项数一致()
    {
        var table = BeatTable.Load(BeatsPath());

        foreach (var beat in table.All)
        {
            // 软断言：不写死具体数值（数据由 P0 在填），只要求"组数 == 选项数"。
            Assert.Equal(beat.Options.Length, beat.Effects.Count);

            foreach (var group in beat.Effects)
                Assert.NotNull(group);
        }
    }

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 BeatMachineTests 同一套找法）。</summary>
    private static string BeatsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var nested = Path.Combine(dir.FullName, "Juhuo", "Content", "data", "beats.csv");
            if (File.Exists(nested)) return nested;

            var local = Path.Combine(dir.FullName, "Content", "data", "beats.csv");
            if (File.Exists(local)) return local;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 Content/data/beats.csv");
    }
}
