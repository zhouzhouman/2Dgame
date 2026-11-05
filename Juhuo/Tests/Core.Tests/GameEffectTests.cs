using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 选项效果接进游戏之后（票号 S3-02c）。守五件事：
/// ① **确认一项之后，四份账真的动了**——钱、囊里的东西、三条轴、兵力、旗标；
/// ② **先落效果再记这一拍**——囊满 / 钱不够时整组不落，这一拍也不算完，原因说得出来；
/// ③ 成功时屏幕上那一行是"选项名 + 账目"，旗标不进玩家那一行；
/// ④ 敲门（b02）、搜查（b08）触发的拍**先弹选项**，确认了才算完；没选项的拍维持旧行为；
/// ⑤ <c>--effects</c> 三行真的跑过结算，不是把表里的字再抄一遍。
///
/// 这里测的是 Godot 之外那一层（<see cref="BeatChoiceRunner"/> / <see cref="BeatEffectWiring"/>）；
/// "面板有没有真的推到屏幕上"归手测与 --playthrough。
/// </summary>
public class GameEffectTests
{
    // ————————————————————————— 夹具 —————————————————————————

    private const string ItemsCsv =
        "id,name,kind,stack,note\n" +
        "coin,铜钱,token,1,钱\n" +
        "grain,谷子,food,1,粮\n" +
        "sword,刀,tool,0,不叠\n";

    private static ItemCatalog Catalog() => ItemCatalog.FromCsv(ItemsCsv);

    private static ItemCatalog RealCatalog()
    {
        var path = PathOf("items.csv");
        return ItemCatalog.FromCsv(File.ReadAllText(path));
    }

    private static BeatTable RealTable() => BeatTable.Load(PathOf("beats.csv"));

    /// <summary>从测试输出目录往上找 <c>Juhuo/Content/data/&lt;name&gt;</c>（与 BeatMachineTests 同一套找法）。</summary>
    private static string PathOf(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var nested = Path.Combine(dir.FullName, "Juhuo", "Content", "data", name);
            if (File.Exists(nested)) return nested;

            var local = Path.Combine(dir.FullName, "Content", "data", name);
            if (File.Exists(local)) return local;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 Content/data/{name}");
    }

    private static Beat B(BeatTable table, string id) =>
        table.Get(id) ?? throw new InvalidOperationException($"表里没有 {id}");

    /// <summary>全新一局；把 <paramref name="last"/> 之前的拍按顺序做掉（前置旗标与天数一起到位）。</summary>
    private static PrologueBeats UpTo(BeatTable table, FlagStore flags, string last)
    {
        var beats = PrologueBeats.FromState(table, flags, null);

        foreach (var beat in table.All)
        {
            if (string.Equals(beat.Id, last, StringComparison.Ordinal)) break;
            Assert.True(beats.Complete(beat), $"前置 {beat.Id} 没做成");
        }

        return beats;
    }

    /// <summary>三条轴都给 50：任何一轴的增减都看得见（从 0 起会被钳位盖住）。</summary>
    private static TrustState Trust() => new() { Guan = 50, Min = 50, Yi = 50 };

    /// <summary>跑一次确认，返回结果（兵按引用改一份本土变量）。</summary>
    private static ChoiceOutcome Confirm(
        Beat beat, int index, PrologueBeats beats, TrustState trust,
        Inventory bag, ItemCatalog catalog, FlagStore flags, ref int troops)
        => BeatChoiceRunner.Confirm(beat, index, beats, trust, bag, catalog, ref troops, flags);

    // ————————————————————————— ① 确认之后账真的动了 —————————————————————————

    [Fact]
    public void 确认卖书之后钱真的到账()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        var outcome = Confirm(beat, 2, beats, trust, bag, Catalog(), flags, ref troops);

        Assert.True(outcome.Ok);
        Assert.Equal(15, bag.Count(TradeBook.MoneyItem));
        Assert.True(beats.Machine.IsDone(beat));
        Assert.True(flags.Has("book_sold", null));
    }

    [Fact]
    public void 确认杀马之后谷子真的进了囊()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        Assert.True(Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);

        Assert.Equal(3, bag.Count("grain"));
        Assert.Equal(0, bag.Count(TradeBook.MoneyItem));   // 别的选项不该跟着落
    }

    [Fact]
    public void 确认杀马之后民望真的降了()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        Assert.True(Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);

        Assert.Equal(45, trust.Min);
        Assert.Equal(50, trust.Guan);
        Assert.Equal(50, trust.Yi);
    }

    [Fact]
    public void 确认写给旧部之后兵力与疑心真的动了()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b09");
        var beat = B(table, "b09");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 7;

        var outcome = Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops);

        Assert.True(outcome.Ok);
        Assert.Equal(17, troops);
        Assert.Equal(51, trust.Yi);
        Assert.True(flags.Has("letter_veterans", null));
    }

    [Fact]
    public void 确认挡住之后旗标与减益都落上了()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b02");
        var beat = B(table, "b02");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        Assert.True(Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);

        Assert.True(flags.Has("oldservant_died", null));
        Assert.Equal(48, trust.Yi);
        Assert.False(flags.Has("oldservant_betrayed", null));
    }

    [Fact]
    public void 只落所选的那一项()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        Assert.True(Confirm(beat, 2, beats, trust, bag, Catalog(), flags, ref troops).Ok);

        Assert.Equal(50, trust.Min);                       // 「杀马」的民望 −5 不该跟着落
        Assert.Equal(0, bag.Count("grain"));               // 「杀马」的谷子也不该落
        Assert.True(flags.Has("book_sold", null));
        Assert.False(flags.Has("horse_killed", null));
    }

    // ————————————————————————— ② 屏幕上那一行 —————————————————————————

    [Fact]
    public void 成功时屏幕上那一行是选项名加账目()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        var outcome = Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops);

        Assert.Equal("杀马：谷子 ×3，民望 −5", outcome.Line);
        Assert.DoesNotContain("旗标", outcome.Line);       // 旗标是内部名字，不进玩家那一行
    }

    [Fact]
    public void 卖书那一行说的是加钱()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        var outcome = Confirm(beat, 2, beats, trust, bag, Catalog(), flags, ref troops);

        Assert.Equal("卖书：钱 +15", outcome.Line);
    }

    // ————————————————————————— ③ 失败：不完成这一拍 —————————————————————————

    [Fact]
    public void 囊满时这一拍不算完且说出原因()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory(1);
        Assert.True(bag.TryAdd("sword", 1, Catalog(), out _));   // 唯一那格被刀占着
        var troops = 0;

        var outcome = Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops);

        Assert.False(outcome.Ok);
        Assert.Contains("囊", outcome.Reason);                   // 说得出是囊的事
        Assert.False(beats.Machine.IsDone(beat));                // 这一拍还等着
        Assert.False(flags.Has("horse_killed", null));
        Assert.Equal(50, trust.Min);                             // 民望一点没动
        Assert.Equal(0, bag.Count("grain"));                     // 谷子一件也没进来
    }

    [Fact]
    public void 钱不够时这一拍不算完且说出差多少()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b06");
        var beat = B(table, "b06");

        var trust = Trust();
        var bag = new Inventory();                               // 一文都没有
        var troops = 0;

        var outcome = Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops);

        Assert.False(outcome.Ok);
        Assert.Contains("钱不够", outcome.Reason);
        Assert.False(beats.Machine.IsDone(beat));
        Assert.Equal(50, trust.Guan);                            // 「送礼」的官望 +8 不该落
    }

    [Fact]
    public void 清出空位之后同一拍再确认一次就落得下()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory(1);
        Assert.True(bag.TryAdd("sword", 1, Catalog(), out _));
        var troops = 0;

        Assert.False(Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);
        Assert.False(beats.Machine.IsDone(beat));

        bag.Remove("sword", 1);                                  // 玩家自己清了囊

        Assert.True(Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);
        Assert.True(beats.Machine.IsDone(beat));
        Assert.Equal(3, bag.Count("grain"));
        Assert.Equal(45, trust.Min);
    }

    [Fact]
    public void 已经做过的拍再确认一次不会重复落账()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b03");
        var beat = B(table, "b03");

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        Assert.True(Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);
        Assert.False(Confirm(beat, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);

        Assert.Equal(3, bag.Count("grain"));       // 没有第二份
        Assert.Equal(45, trust.Min);               // 也没有再降一次
    }

    // ————————————————————————— ④ 敲门 / 搜查也会弹选项 —————————————————————————

    [Fact]
    public void 敲门拍带选项_敲到的那一下不直接算完()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b02");

        var beat = beats.PendingKnock("hub1");
        Assert.NotNull(beat);
        Assert.True(BeatEffectWiring.NeedsChoice(beat));

        // Main 的口径：有选项就先弹面板，这一拍此刻**不**记为完成。
        Assert.Same(beat, BeatEffectWiring.PendingShow(beat, null));
        Assert.False(beats.Machine.IsDone(beat));

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        var outcome = Confirm(beat!, 1, beats, trust, bag, Catalog(), flags, ref troops);

        Assert.True(outcome.Ok);
        Assert.True(beats.Machine.IsDone(beat));
        Assert.True(flags.Has("oldservant_betrayed", null));
        Assert.Equal(40, trust.Min);
    }

    [Fact]
    public void 搜查拍带选项_搜到的那一下不直接算完()
    {
        var table = RealTable();
        var flags = new FlagStore();
        var beats = UpTo(table, flags, "b08");

        var beat = beats.PendingSearch("hub6", "h6_04");
        Assert.NotNull(beat);
        Assert.True(BeatEffectWiring.NeedsChoice(beat));
        Assert.False(beats.Machine.IsDone(beat));

        var trust = Trust();
        var bag = new Inventory();
        var troops = 0;

        Assert.True(Confirm(beat!, 0, beats, trust, bag, Catalog(), flags, ref troops).Ok);

        Assert.True(beats.Machine.IsDone(beat));
        Assert.Equal(2, bag.Count("grain"));
        Assert.Equal(52, trust.Yi);
        Assert.True(flags.Has("granary_broken", null));
    }

    [Fact]
    public void 没选项的拍维持旧行为()
    {
        const string csv =
            "id,name,hub,day,requires,sets,trigger,options,note,effects\n" +
            "t1,旧拍,hub1,1,,t1_done,knock,,旧表没有选项,\n";

        var table = BeatTable.FromCsv(csv);
        var flags = new FlagStore();
        var beats = PrologueBeats.FromState(table, flags, null);
        var beat = B(table, "t1");

        Assert.False(BeatEffectWiring.NeedsChoice(beat));
        Assert.Null(BeatEffectWiring.PendingShow(beat, null));

        // 旧行为：没有选项就直接 Complete，不经过面板。
        Assert.True(beats.Complete(beat));
        Assert.True(beats.Machine.IsDone(beat));
    }

    [Fact]
    public void 已经在等同一拍时不再重复弹()
    {
        var table = RealTable();
        var b02 = B(table, "b02");
        var b03 = B(table, "b03");

        Assert.True(BeatEffectWiring.SameBeat(b02, b02));
        Assert.False(BeatEffectWiring.SameBeat(b02, b03));
        Assert.Null(BeatEffectWiring.PendingShow(b02, b02));    // 面板正等着它 → 别弹第二遍
        Assert.Same(b02, BeatEffectWiring.PendingShow(b02, b03));
    }

    // ————————————————————————— ⑤ --effects 三行 —————————————————————————

    [Fact]
    public void effects自检打印三行且每行都真的改变量()
    {
        var table = RealTable();
        var catalog = RealCatalog();

        var lines = EffectsDemo.Run(table, catalog);
        Assert.Equal(3, lines.Count);

        Assert.Contains("b03 选「杀马」", lines[0]);
        Assert.Contains("谷子 ×3", lines[0]);
        Assert.Contains("民望 −5", lines[0]);
        Assert.Contains("旗标 horse_killed", lines[0]);

        Assert.Contains("b03 选「卖书」", lines[1]);
        Assert.Contains("钱 +15", lines[1]);
        Assert.Contains("旗标 book_sold", lines[1]);

        Assert.Contains("b09 选「写给旧部」", lines[2]);
        Assert.Contains("兵 +10", lines[2]);
        Assert.Contains("疑心 +1", lines[2]);
        Assert.Contains("旗标 letter_veterans", lines[2]);

        // 三行说的数必须在状态里对得上——不是把表里的字抄一遍。
        foreach (var (id, pick) in EffectsDemo.Cases)
        {
            var beat = B(table, id);
            var trust = Trust();
            var bag = new Inventory();
            var flags = new FlagStore();
            var troops = 0;

            var result = BeatEffectBook.Apply(beat, pick, trust, bag, catalog, ref troops, flags);
            Assert.True(result.Ok);

            var changed = bag.SlotsUsed > 0 || trust.Guan != 50 || trust.Min != 50 || trust.Yi != 50
                          || troops != 0 || flags.Has("horse_killed", null) || flags.Has("book_sold", null)
                          || flags.Has("letter_veterans", null);
            Assert.True(changed, $"{id} 第 {pick + 1} 项什么都没改");
        }
    }
}
