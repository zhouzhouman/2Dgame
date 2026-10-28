using System;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 对话分支、打断记忆、敲门计时（票号 S1-05 / S1-07）。
///
/// 这里只测能被单测覆盖的部分：选段规则、条件退链、旗标名、敲门只敲一次、盘问计费。
/// "真的按下 E 会发生什么"归 --tutrun / --knock 那几条无头自检。
/// </summary>
public class GameDialogueTests
{
    // ————————————————————————— 选段 —————————————————————————

    [Fact]
    public void 初见永远先走初见段_不被打断记忆顶掉()
    {
        // met=false 时哪怕有打断记忆，也得先说初见——首次对话 0 刻（D21），
        // 那段一次性的文本不能被一句"上次你走了"换掉。
        Assert.Equal(DialogueVariant.First, DialogueVariant.Choose(met: false, interrupted: true, interrogate: false));
        Assert.Equal(DialogueVariant.First, DialogueVariant.Choose(met: false, interrupted: false, interrogate: false));
    }

    [Fact]
    public void 见过且被打断过_走打断变体()
    {
        Assert.Equal(DialogueVariant.Interrupt, DialogueVariant.Choose(met: true, interrupted: true, interrogate: false));
    }

    [Fact]
    public void 见过没被打断_走再谈_盘问优先于一切()
    {
        Assert.Equal(DialogueVariant.Again, DialogueVariant.Choose(met: true, interrupted: false, interrogate: false));
        Assert.Equal(DialogueVariant.Interrogate, DialogueVariant.Choose(met: true, interrupted: true, interrogate: true));
        Assert.Equal(DialogueVariant.Interrogate, DialogueVariant.Choose(met: false, interrupted: false, interrogate: true));
    }

    // ————————————————————————— 分支 —————————————————————————

    [Fact]
    public void 条件不成立时_沿Next退到下一段_成立时说有条件那段()
    {
        var flags = new FlagStore();
        var graph = new DialogueGraph();

        graph.Add(new DialogueNode
        {
            Key = DialogueVariant.Again,
            Text = "他把门掩上一半。「今天不方便。」",
            Requires = "has_deed",
            Next = "again_plain",
        });
        graph.Add(new DialogueNode
        {
            Key = "again_plain",
            Text = "「你走吧。」",
        });

        // 条件没满足：退到 next 那段
        var node = graph.Resolve(DialogueVariant.Again, flags, "hub4");
        Assert.NotNull(node);
        Assert.Equal("again_plain", node!.Key);

        // 条件满足：说有条件的那段
        flags.Set("has_deed");
        var branch = graph.Resolve(DialogueVariant.Again, flags, "hub4");
        Assert.NotNull(branch);
        Assert.Equal(DialogueVariant.Again, branch!.Key);
    }

    [Fact]
    public void 段里的置位键能落到旗标仓库_空串不置位()
    {
        var flags = new FlagStore();
        var graph = new DialogueGraph();

        graph.Add(new DialogueNode { Key = DialogueVariant.Interrupt, Text = "「我还没说完。」", SetFlag = "hub4.door.opened" });
        graph.Add(new DialogueNode { Key = DialogueVariant.Again, Text = "「嗯。」" });

        var node = graph.Resolve(DialogueVariant.Again, flags, "hub4");
        Assert.Empty(node!.SetFlag);            // 这一段不置位

        if (!string.IsNullOrEmpty(node.SetFlag)) flags.Set(node.SetFlag);
        Assert.False(flags.Has("hub4.door.opened", "hub4"));

        var withFlag = graph.Resolve(DialogueVariant.Interrupt, flags, "hub4")!;
        Assert.True(flags.Set(withFlag.SetFlag));
        Assert.True(flags.Has("hub4.door.opened", "hub4"));
    }

    [Fact]
    public void 数据写错成环_不会转死_返回没有这一段()
    {
        var graph = new DialogueGraph();
        graph.Add(new DialogueNode { Key = "a", Text = "", Requires = "never", Next = "b" });
        graph.Add(new DialogueNode { Key = "b", Text = "", Requires = "never", Next = "a" });

        // 两段都没字、条件都不成立，链成环：必须在 MaxHops 内停下
        Assert.Null(graph.Resolve("a", new FlagStore(), "hub1"));
    }

    // ————————————————————————— 旗标名 —————————————————————————

    [Fact]
    public void 打断与盘问的旗标名合规_且不超四十字节()
    {
        var interrupted = DialogueFlags.Interrupted("hub3:guard:衙役甲");
        var allowed = DialogueFlags.Allowed("hub3:12,4");

        Assert.Equal("talk.hub3_12_4.allowed", allowed);
        Assert.StartsWith("talk.", interrupted, StringComparison.Ordinal);
        Assert.EndsWith(".interrupted", interrupted, StringComparison.Ordinal);
        Assert.DoesNotContain(":", interrupted);
        Assert.DoesNotContain(",", interrupted);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(interrupted) <= FlagStore.MaxNameBytes);
        Assert.True(FlagStore.IsValidName(interrupted));

        // 真的能写进去——名字合规不是看着像就行
        var flags = new FlagStore();
        Assert.True(flags.Set(interrupted));
        Assert.True(flags.Has(interrupted, "hub3"));
    }

    [Fact]
    public void 打断记忆用掉就清_下次回去只是再谈()
    {
        var flags = new FlagStore();
        var key = "hub3:12,4";
        var memory = DialogueFlags.Interrupted(key);

        flags.Set(memory);
        Assert.True(flags.Has(memory, "hub3"));

        // World.Talk() 说完打断那句就把记忆清掉，别让这人一辈子都是"上次你走了"
        Assert.True(flags.Clear(memory));
        Assert.False(flags.Has(memory, "hub3"));
    }

    // ————————————————————————— 真实台词表 —————————————————————————

    [Fact]
    public void 现有台词表建出来的分支_每人都有初见_且盘问默认关着()
    {
        var csv = File.ReadAllText("npcs.csv");
        var book = DialogueBook.FromCsv(csv);

        // 30 行台词、28 个唯一人名（CANON 6-2）；键按 hub+坐标/名字，去重后仍有二十多个人
        Assert.True(book.SpeakerCount >= 28, $"只建出 {book.SpeakerCount} 个人");

        foreach (var talk in NpcTalkTable.FromCsv(csv).All)
        {
            var key = talk.X > 0 || talk.Y > 0
                ? DialogueBook.KeyAt(talk.Hub, talk.X, talk.Y)
                : DialogueBook.KeyOfGuard(talk.Hub, talk.Name);

            var graph = book.Of(key);
            Assert.NotNull(graph);
            Assert.True(graph!.Has(DialogueVariant.First), $"{talk.Hub} {talk.Name} 没有初见段");
        }

        // 表里没有 interrogate 列 -> 这一段不存在 -> 盘问入口是关的（票面：缺则禁用）
        var any = NpcTalkTable.FromCsv(csv).All.First();
        var anyKey = any.X > 0 || any.Y > 0
            ? DialogueBook.KeyAt(any.Hub, any.X, any.Y)
            : DialogueBook.KeyOfGuard(any.Hub, any.Name);

        Assert.Null(book.Resolve(anyKey, DialogueVariant.Interrogate, new FlagStore(), any.Hub));
    }

    [Fact]
    public void 表里有interrogate列_就自动多出一段盘问()
    {
        const string csv =
            "hub,name,x,y,first,again,interrupt,interrogate\n" +
            "hub4,县丞,20,5,「你是哪来的。」,「又来？」,,\"「你袖子里是什么？」\"";

        var book = DialogueBook.FromCsv(csv);
        var node = book.Resolve(DialogueBook.KeyAt("hub4", 20, 5), DialogueVariant.Interrogate, new FlagStore(), "hub4");

        Assert.NotNull(node);
        Assert.Contains("袖子", node!.Text);
    }

    // ————————————————————————— 计费 —————————————————————————

    [Fact]
    public void 盘问四刻_替代打断不是叠加()
    {
        var clock = new Clock("hub3");
        Assert.Equal(16, clock.KeBudgetHalf / 2);

        clock.SpendInterrogate();
        Assert.Equal(8, clock.KeHalf);        // 8 半刻 = 4 刻
        Assert.Equal(4, clock.KeUsedKe);

        // 再来一次还是 +8，不是 +8+4
        clock.SpendInterrogate();
        Assert.Equal(16, clock.KeHalf);
        Assert.Equal(8, clock.KeUsedKe);
    }

    [Fact]
    public void 打断后重谈仍按再谈计费_不回到免费()
    {
        var clock = new Clock("hub2");

        clock.SpendTalk(first: true);          // 首次 0 刻（D21）
        Assert.Equal(0, clock.KeHalf);

        clock.SpendInterrupt();                // 被看见：2 刻
        Assert.Equal(4, clock.KeHalf);

        clock.SpendTalk(first: false);         // 重谈：1 刻，不因为"刚被打断"就免
        Assert.Equal(6, clock.KeHalf);
        Assert.Equal(3, clock.KeUsedKe);
    }

    // ————————————————————————— 敲门 —————————————————————————

    [Fact]
    public void 敲门按刻算_到第三刻才敲_且只敲一次()
    {
        var flags = new FlagStore();
        var knock = new KnockTimer();

        Assert.Equal(3, knock.TriggerKe);
        Assert.False(knock.TryKnock(flags, "hub1", 0));
        Assert.False(knock.TryKnock(flags, "hub1", 2));
        Assert.False(flags.Has(KnockTimer.HappenedFlag, "hub1"));   // 没到点不许留痕

        Assert.True(knock.TryKnock(flags, "hub1", 3));
        Assert.True(flags.Has(KnockTimer.HappenedFlag, "hub1"));

        // 存了档再进：旗标在 → 不重放
        Assert.False(knock.ShouldKnock("hub1", 9, flags.Has(KnockTimer.HappenedFlag, "hub1")));
        Assert.False(knock.TryKnock(flags, "hub1", 9));
    }

    [Fact]
    public void 敲门只在城南小院_别的空间刻再多也不敲()
    {
        var flags = new FlagStore();
        var knock = new KnockTimer();

        Assert.False(knock.TryKnock(flags, "hub2", 99));
        Assert.False(knock.TryKnock(flags, null, 99));
        Assert.False(flags.Has(KnockTimer.HappenedFlag, "hub1"));

        // 触发线可配（hubs.csv 落地后走这条），非法值退回缺省，不静默用 0
        Assert.Equal(KnockTimer.DefaultTriggerKe, new KnockTimer(0).TriggerKe);
        Assert.Equal(5, new KnockTimer(5).TriggerKe);
        Assert.False(new KnockTimer(5).ShouldKnock("hub1", 4, false));
        Assert.True(new KnockTimer(5).ShouldKnock("hub1", 5, false));
    }
}
