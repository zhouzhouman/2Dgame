using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 开场介绍与 NPC 立绘（票号 S3-01d）。
///
/// 玩家的两句话：<b>「游戏开始时要有 npc 介绍剧情，否则玩家一开始将会玩的一头雾水」</b>、
/// <b>「每个 npc 要有自己的立绘」</b>。所以这一层盯三件事：
///
/// 1. 那九行**按顺序**播、旁白行不挂立绘、Enter 一行一行走、Esc 跳过记作看过、老档不重放；
/// 2. 立绘的七个参数**每一个都真的画在画面上**——不是"存进了结构体"。所以逐档断言
///    某个格子上最后落的是哪种颜料：皮肤改了脸真的换色、胡须改了腮帮真的多一块；
/// 3. 对话里的人名与立绘挂得上；表里没有的人退回原来那一行字，不崩。
///
/// 全部不碰 Godot：构图在 <see cref="PortraitArt"/>，播法在 <see cref="IntroFlow"/>，
/// 卡片在 <see cref="DialogueCard"/>；<c>PortraitView</c> 只是照着指令 DrawRect。
/// </summary>
public class GameIntroTests
{
    // ————————————————————————— 夹具 —————————————————————————

    /// <summary>从测试输出目录往上找工程里的 Content/data（与其它 Game* 测试同一套找法）。</summary>
    private static string FindData(string fileName)
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

        throw new FileNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 Content/data/{fileName}");
    }

    private static IntroTable Intro() => IntroTable.Load(FindData("intro.csv"));

    private static PortraitTable Portraits() => PortraitTable.Load(FindData("portraits.csv"));

    /// <summary>一个只用来验"哪个参数落在哪一格"的人：默认是青衫方巾的书生。</summary>
    private static Portrait Look(
        int skin = 2,
        int hair = 1,
        int style = 1,
        int beard = 0,
        int hat = 2,
        int robe = 3,
        int mark = 0) => new("测试", skin, hair, style, beard, hat, robe, mark, "");

    /// <summary>
    /// (x,y) 这一格最后落的是哪种颜料——按绘制顺序取最后一条覆盖它的指令，
    /// 和 <c>PortraitView._Draw</c> 的"后画的压住先画的"完全一致。
    /// </summary>
    private static (bool Found, PortraitTone Tone) At(Portrait p, int x, int y)
    {
        var parts = PortraitArt.Build(p);

        for (var i = parts.Count - 1; i >= 0; i--)
        {
            var part = parts[i];
            if (x >= part.X && x < part.X + part.W && y >= part.Y && y < part.Y + part.H)
                return (true, part.Tone);
        }

        return (false, default);
    }

    private static PortraitTone Tone(Portrait p, int x, int y)
    {
        var (found, tone) = At(p, x, y);
        Assert.True(found, $"({x},{y}) 这一格没有被画到——立绘缺了一块。");
        return tone;
    }

    /// <summary>把一整张图压成一行字，用来判"两个人是不是长得一样"。</summary>
    private static string Sig(Portrait p) =>
        string.Join("|", PortraitArt.Build(p).Select(x => $"{x.X},{x.Y},{x.W},{x.H},{x.Tone}"));

    // ————————————————————————— 一、开场若干行 —————————————————————————

    [Fact]
    public void 开场各行_顺序连号_老仆的那几句带立绘()
    {
        var table = Intro();

        // 行数以表为准：P0 后来补了一句"三百三十万石"（主角动机），
        // 写死 9 会让补文案变成改测试 —— 这正是内容迭代时最容易卡住的地方。
        Assert.True(table.Count >= 9, $"开场至少九句，实际 {table.Count}");
        Assert.True(table.Count <= 10, $"开场不超过十句，实际 {table.Count}");

        for (var i = 0; i < table.Count; i++)
            Assert.Equal(i + 1, table.All[i].Seq);

        Assert.True(table.All[0].IsNarration);
        Assert.False(table.All[0].HasPortrait);

        // 老仆那两句必须带立绘（位置随文案增删而变，按说话人找，不按序号找）
        var oldServant = table.All.Where(l => l.Speaker == "老仆").ToList();
        Assert.Equal(2, oldServant.Count);
        Assert.All(oldServant, l => Assert.True(l.HasPortrait));
        Assert.All(oldServant, l => Assert.Equal("老仆", l.Portrait));
        Assert.All(oldServant, l => Assert.False(l.IsNarration));

        // 旁白若干 + 老仆两句；两者相加必须等于总行数（别再有第三类没写 speaker 的行被漏掉）
        Assert.Equal(table.Count - 2, table.All.Count(l => l.IsNarration));
        Assert.Equal(2, table.All.Count(l => !l.IsNarration));
    }

    [Fact]
    public void 旁白行_一律不挂立绘()
    {
        var flow = new IntroFlow(Intro());

        for (var i = 0; i < flow.Count; i++)
        {
            if (flow.Current.IsNarration) Assert.Null(flow.PortraitName);
            else Assert.Equal("老仆", flow.PortraitName);

            if (i < flow.Count - 1) flow.Advance();
        }
    }

    [Fact]
    public void 按一次回车_往前走一行()
    {
        var flow = new IntroFlow(Intro());

        Assert.Equal(1, flow.Ordinal);
        Assert.Equal(1, flow.Current.Seq);

        Assert.True(flow.Advance());
        Assert.Equal(2, flow.Ordinal);
        Assert.Equal(2, flow.Current.Seq);
        Assert.False(flow.IsFinished);
        Assert.False(flow.Seen);
    }

    [Fact]
    public void 最后一行再按回车_收场并记作看过()
    {
        var flow = new IntroFlow(Intro());

        for (var i = 0; i < flow.Count - 1; i++) Assert.True(flow.Advance());

        Assert.True(flow.IsLast);
        Assert.Equal(flow.Count, flow.Ordinal);   // Ordinal 是 1 起的序号

        // 最后一行再按一次：这一段收场，进教程的那一步由 Main 接。
        Assert.False(flow.Advance());
        Assert.True(flow.IsFinished);
        Assert.True(flow.Seen);
    }

    [Fact]
    public void Esc跳过_九行都记作看过()
    {
        var flow = new IntroFlow(Intro());

        Assert.False(flow.Seen);
        flow.Skip();

        Assert.True(flow.Seen);
        Assert.True(flow.IsFinished);

        // 跳过之后不许再翻：否则下一帧 Enter 会把第一句放出来。
        Assert.False(flow.Advance());
    }

    [Fact]
    public void 读档进来的_不放开场()
    {
        // 新档、没读到旧档、这一趟还没放过 → 放。
        Assert.True(IntroFlow.ShouldPlay(newGame: true, loadedSave: false, seen: false));

        // 读档进来：他上次看过了，再放一遍就是拦人。
        Assert.False(IntroFlow.ShouldPlay(newGame: true, loadedSave: true, seen: false));

        // 不是从"新的开始"进来的，本来就没有开场。
        Assert.False(IntroFlow.ShouldPlay(newGame: false, loadedSave: false, seen: false));

        // 这一趟已经放过（跳过也算）→ 不再放第二遍。
        Assert.False(IntroFlow.ShouldPlay(newGame: true, loadedSave: false, seen: true));
    }

    [Fact]
    public void 开场打印_每行格式对得上()
    {
        var table = Intro();
        var lines = IntroReport.Lines(table).ToList();

        Assert.Equal(table.Count, lines.Count);
        Assert.StartsWith($"[开场] 1/{table.Count} 旁白：夜。", lines[0]);

        // 老仆那一行：序号按数据算，文案与"（立绘）"标记要都在
        var idx = table.All.ToList().FindIndex(l => l.Speaker == "老仆");
        Assert.StartsWith($"[开场] {idx + 1}/{table.Count} 老仆（立绘）：「少爷。」", lines[idx]);

        Assert.StartsWith($"[开场] {table.Count}/{table.Count} 旁白：后窗开着。", lines[^1]);
    }

    // ————————————————————————— 二、立绘的七层 —————————————————————————

    [Fact]
    public void 立绘_肤色四档各画一种颜色()
    {
        var tones = new List<PortraitTone>();
        for (var skin = 1; skin <= 4; skin++) tones.Add(Tone(Look(skin: skin), 7, 6));

        Assert.Equal(4, tones.Distinct().Count());
        Assert.Equal(PortraitTone.Skin1, tones[0]);
        Assert.Equal(PortraitTone.Skin4, tones[3]);
    }

    [Fact]
    public void 立绘_发色六档各画一种颜色()
    {
        var tones = new List<PortraitTone>();
        for (var hair = 1; hair <= 6; hair++) tones.Add(Tone(Look(hair: hair, hat: 0), 7, 3));

        Assert.Equal(6, tones.Distinct().Count());
        Assert.Equal(PortraitTone.Hair1, tones[0]);
        Assert.Equal(PortraitTone.Hair6, tones[5]);
    }

    [Fact]
    public void 立绘_四种发型落在不同位置()
    {
        // 束发：头顶多一个髻（y1）
        Assert.Equal(PortraitTone.Hair1, Tone(Look(style: 1, hat: 0), 7, 1));

        // 披散：两侧垂到腮下（x4,y8）
        Assert.Equal(PortraitTone.Hair1, Tone(Look(style: 2, hat: 0), 4, 8));

        // 短：只到耳上，腮边露出皮肤（x5,y8）
        Assert.Equal(PortraitTone.Skin2, Tone(Look(style: 3, hat: 0), 5, 8));

        // 包头：两颊被头发包住（x4,y6）
        Assert.Equal(PortraitTone.Hair1, Tone(Look(style: 4, hat: 0), 4, 6));

        // 四种都画出来，两两不同。
        var looks = Enumerable.Range(1, 4).Select(s => Sig(Look(style: s, hat: 0))).ToList();
        Assert.Equal(4, looks.Distinct().Count());
    }

    [Fact]
    public void 立绘_胡须四档下巴上不一样()
    {
        // 无须：下巴是皮肤
        Assert.Equal(PortraitTone.Skin2, Tone(Look(beard: 0), 7, 9));

        // 短须：只盖住下巴那一行
        Assert.Equal(PortraitTone.Hair1, Tone(Look(beard: 1), 7, 9));
        Assert.Equal(PortraitTone.Skin2, Tone(Look(beard: 1), 7, 10));

        // 长须：垂到脖子以下
        Assert.Equal(PortraitTone.Hair1, Tone(Look(beard: 2), 7, 10));
        Assert.Equal(PortraitTone.HairShade, Tone(Look(beard: 2), 7, 12));

        // 络腮：连两腮一起包住（x4,y8 本来是描边）
        Assert.Equal(PortraitTone.Hair1, Tone(Look(beard: 3), 4, 8));

        var looks = Enumerable.Range(0, 4).Select(b => Sig(Look(beard: b))).ToList();
        Assert.Equal(4, looks.Distinct().Count());
    }

    [Fact]
    public void 立绘_五种帽子额上不一样()
    {
        // 不戴：右上角那一格什么都没画（x2,y2 在头的轮廓之外）
        Assert.False(At(Look(hat: 0), 2, 2).Found);

        // 斗笠：宽檐一直伸到 x2
        Assert.Equal(PortraitTone.Hat, Tone(Look(hat: 1), 2, 2));

        // 方巾：一道箍压在额上
        Assert.Equal(PortraitTone.HatShade, Tone(Look(hat: 2), 4, 3));

        // 官帽：两侧展脚，檐外那一格是暗的
        Assert.Equal(PortraitTone.HatShade, Tone(Look(hat: 3), 2, 2));

        // 头巾：右边打个结，垂到腮边
        Assert.Equal(PortraitTone.HatShade, Tone(Look(hat: 4), 12, 5));

        var looks = Enumerable.Range(0, 5).Select(h => Sig(Look(hat: h))).ToList();
        Assert.Equal(5, looks.Distinct().Count());
    }

    [Fact]
    public void 立绘_五种记号脸上不一样()
    {
        // 无记号：整张脸上没有朱砂
        Assert.DoesNotContain(PortraitArt.Build(Look(mark: 0)), part => part.Tone == PortraitTone.Accent);

        // 疤：右颊一道竖的
        Assert.Equal(PortraitTone.Accent, Tone(Look(mark: 1), 9, 7));
        Assert.Equal(PortraitTone.Accent, Tone(Look(mark: 1), 9, 8));

        // 痣：一颗
        Assert.Equal(PortraitTone.Accent, Tone(Look(mark: 2), 9, 8));

        // 眼罩：连眼睛一起压黑
        Assert.Equal(PortraitTone.Ink, Tone(Look(mark: 3), 8, 6));
        Assert.Equal(PortraitTone.Ink, Tone(Look(mark: 3), 9, 7));

        // 皱纹：额上一道、眼下两道
        Assert.Equal(PortraitTone.SkinShade, Tone(Look(mark: 4), 6, 5));

        var looks = Enumerable.Range(0, 5).Select(m => Sig(Look(mark: m))).ToList();
        Assert.Equal(5, looks.Distinct().Count());
    }

    [Fact]
    public void 立绘_六档衣色画在肩上()
    {
        var tones = new List<PortraitTone>();
        for (var robe = 1; robe <= 6; robe++) tones.Add(Tone(Look(robe: robe), 6, 15));

        Assert.Equal(6, tones.Distinct().Count());
        Assert.Equal(PortraitTone.Robe1, tones[0]);
        Assert.Equal(PortraitTone.Robe6, tones[5]);
    }

    [Fact]
    public void 立绘_十六格对齐_192与128都是整倍数()
    {
        Assert.Equal(192, PortraitArt.Grid * PortraitArt.BigScale);
        Assert.Equal(128, PortraitArt.Grid * PortraitArt.SmallScale);
        Assert.Equal(PortraitArt.BigPixels, PortraitArt.Grid * PortraitArt.BigScale);
        Assert.Equal(PortraitArt.SmallPixels, PortraitArt.Grid * PortraitArt.SmallScale);

        foreach (var part in PortraitArt.Build(Look()))
        {
            Assert.InRange(part.X, 0, PortraitArt.Grid - 1);
            Assert.InRange(part.Y, 0, PortraitArt.Grid - 1);
            Assert.InRange(part.X + part.W, 1, PortraitArt.Grid);
            Assert.InRange(part.Y + part.H, 1, PortraitArt.Grid);
        }
    }

    [Fact]
    public void 立绘_真表里同一个人长得一样_不同人不一样()
    {
        var table = Portraits();

        // 三十行都画得出来，而且不是空板（最省的一档也有二十来笔）。
        foreach (var p in table.All)
            Assert.True(PortraitArt.Build(p).Count >= 20, $"{p.Npc} 的立绘只有几笔。");

        // 同一个人在两处登场（沈砚、老鸦各两行）：七层必须一致。
        foreach (var group in table.All.GroupBy(p => p.Npc))
            Assert.Single(group.Select(Sig).Distinct());

        // 不同的人不许撞脸：名字不同，画出来就必须是两张图。
        var byName = table.All.GroupBy(p => p.Npc).Select(g => g.First()).ToList();
        Assert.Equal(byName.Count, byName.Select(Sig).Distinct().Count());
    }

    // ————————————————————————— 三、对话里的名字与立绘 —————————————————————————

    [Fact]
    public void 对话卡片_有立绘的人带名字与立绘()
    {
        var card = DialogueCard.From("老仆　　初见", "「你先走。我挡一下。」", "老仆", Portraits());

        Assert.True(card.HasPortrait);
        Assert.Equal("老仆", card.PortraitNpc);
        Assert.Contains("老仆", card.Title);
        Assert.Equal("「你先走。我挡一下。」", card.Body);
    }

    [Fact]
    public void 对话卡片_表里没有的人退回一行字()
    {
        var card = DialogueCard.From("衙役甲　　初见", "「站住。你不是本村的」", "查无此人", Portraits());

        Assert.False(card.HasPortrait);
        Assert.Null(card.PortraitNpc);
        Assert.Equal("衙役甲　　初见", card.Title);
        Assert.Equal("「站住。你不是本村的」", card.Body);

        // 立绘表整个读不进来也不能崩：照样一行字。
        var noTable = DialogueCard.From("老仆　　初见", "「少爷。」", "老仆", null);
        Assert.False(noTable.HasPortrait);
        Assert.Equal("「少爷。」", noTable.Body);
    }
}
