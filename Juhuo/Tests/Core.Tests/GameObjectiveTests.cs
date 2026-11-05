using System;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 黄色目标点 + 边缘箭头 + 旁边提醒（票号 S4-03b）。玩家的原话：
/// <b>「光有目标，没告诉玩家怎么去做，玩家一头雾水，可以像原神一样，有个黄色目标点，或者有旁边提醒」</b>。
///
/// 这一层守四件事：
/// ① <c>beats.csv</c> 的 <c>target</c> 五种写法解析得对，认不出、写空一律退化成"没有目标点"；
/// ② 表里没有 <c>target</c> / <c>hint</c> 两列时整张表退化，旧表照开（不崩）；
/// ③ 目标行下面那行小字与"在 XX · 按 M 过去（N 刻）"的文案，教学期间不重复显示；
/// ④ 边缘箭头的方向（四象限）、走近 1.5 格的判定、以及 <c>--objective</c> 的输出格式。
///
/// 全部不碰 Godot：判定在 <see cref="ObjectiveHud"/> / <see cref="ObjectiveArrow"/>，
/// 文案在 <see cref="ObjectiveText"/>，打印在 <see cref="ObjectiveReport"/>。
/// </summary>
public class GameObjectiveTests
{
    // ————————————————————————— 一、五种写法 —————————————————————————

    [Theory]
    [InlineData("exit")]
    [InlineData(" EXIT ")]
    [InlineData("Exit")]
    public void Parse_Exit_大小写与空格都认(string raw)
    {
        var o = Objective.Parse(raw, "顺着屋子北边的黄框走");

        Assert.Equal(TargetKind.Exit, o.Kind);
        Assert.Equal("", o.Id);
        Assert.Equal("顺着屋子北边的黄框走", o.Hint);
        Assert.True(o.HasTarget);
    }

    [Fact]
    public void Parse_Point_保留点位id()
    {
        var o = Objective.Parse("point:h1_05", "穿过院子推门");

        Assert.Equal(TargetKind.Point, o.Kind);
        Assert.Equal("h1_05", o.Id);
        Assert.Equal("穿过院子推门", o.Hint);
    }

    [Fact]
    public void Parse_Point_前缀大小写容错()
    {
        var o = Objective.Parse("POINT: h1_05 ", null);

        Assert.Equal(TargetKind.Point, o.Kind);
        Assert.Equal("h1_05", o.Id);
        Assert.Equal("", o.Hint);
    }

    [Fact]
    public void Parse_Npc_按名字()
    {
        var o = Objective.Parse("npc:陈三", "去村口找他");

        Assert.Equal(TargetKind.Npc, o.Kind);
        Assert.Equal("陈三", o.Id);
        Assert.Equal("去村口找他", o.Hint);
    }

    [Fact]
    public void Parse_Hub_id统一小写()
    {
        var o = Objective.Parse("hub:HUB4", "");

        Assert.Equal(TargetKind.Hub, o.Kind);
        Assert.Equal("hub4", o.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_空列_没有目标点但保留hint(string? raw)
    {
        var o = Objective.Parse(raw, " 先去看看灶台 ");

        Assert.Equal(TargetKind.None, o.Kind);
        Assert.Equal("", o.Id);
        Assert.Equal("先去看看灶台", o.Hint);
        Assert.False(o.HasTarget);
    }

    [Theory]
    [InlineData("point:")]
    [InlineData("point:    ")]
    [InlineData("npc:")]
    [InlineData("hub:")]
    [InlineData("exitx")]
    [InlineData("随便写点什么")]
    public void Parse_认不出_退化成None且hint照留(string raw)
    {
        var o = Objective.Parse(raw, "这句话还得留着");

        Assert.Equal(TargetKind.None, o.Kind);
        Assert.Equal("这句话还得留着", o.Hint);
    }

    [Theory]
    [InlineData("exit", "exit")]
    [InlineData("point:h1_05", "point:h1_05")]
    [InlineData("npc:陈三", "npc:陈三")]
    [InlineData("hub:hub4", "hub:hub4")]
    public void Token_回写成target列的样子(string raw, string expected)
    {
        Assert.Equal(expected, Objective.Parse(raw, "").Token);
    }

    // ————————————————————————— 二、表里没这两列 —————————————————————————

    private const string OldBeats =
        "\"id\",\"name\",\"hub\",\"day\",\"requires\",\"sets\",\"trigger\",\"note\"\n" +
        "\"b01\",\"焚账\",\"hub1\",\"1\",\"\",\"beat.b01.done\",\"choice\",\"n\"\n";

    [Fact]
    public void ObjectiveTable_旧表没有两列_整张退化不崩()
    {
        var table = ObjectiveTable.FromCsv(OldBeats);

        Assert.Equal(0, table.Count);
        Assert.Equal(Objective.Empty, table.Of("b01"));
        Assert.Equal(TargetKind.None, table.Of("b01").Kind);
        Assert.False(table.Of("b01").HasTarget);
    }

    [Fact]
    public void ObjectiveTable_只有hint列_也能读出提醒()
    {
        const string csv =
            "\"id\",\"hint\"\n" +
            "\"b07\",\"沿着灯走到巷子尽头\"\n";

        var table = ObjectiveTable.FromCsv(csv);

        Assert.Equal(TargetKind.None, table.Of("b07").Kind);
        Assert.Equal("沿着灯走到巷子尽头", table.Of("b07").Hint);
    }

    [Fact]
    public void ObjectiveTable_读出target与hint_并去掉空格()
    {
        const string csv =
            "\"id\",\"target\",\"hint\"\n" +
            "\"b02\",\" point:h1_05 \",\" 穿过院子推门 \"\n" +
            "\"b05\",\"hub:hub4\",\"\"\n";

        var table = ObjectiveTable.FromCsv(csv);

        Assert.Equal(TargetKind.Point, table.Of("b02").Kind);
        Assert.Equal("h1_05", table.Of("b02").Id);
        Assert.Equal("穿过院子推门", table.Of("b02").Hint);
        Assert.Equal(TargetKind.Hub, table.Of("b05").Kind);
        Assert.Equal(Objective.Empty, table.Of("b99"));
    }

    // ————————————————————————— 三、范围 / 到了 / 画什么 —————————————————————————

    [Theory]
    [InlineData(0f, 0f, 1.4f, 0f, true)]
    [InlineData(0f, 0f, 0f, 1.5f, true)]
    [InlineData(0f, 0f, 1.5f, 1.5f, false)]
    [InlineData(0f, 0f, 1.6f, 0f, false)]
    public void IsArrived_1点5格为界(float px, float py, float tx, float ty, bool expected)
    {
        Assert.Equal(expected, ObjectiveHud.IsArrived(px, py, tx, ty));
    }

    [Fact]
    public void Decide_同空间且在屏幕里_画菱形()
    {
        var o = Objective.Parse("point:h1_05", "");
        Assert.Equal(ObjectiveView.WorldDiamond, ObjectiveHud.Decide(o, here: true, onScreen: true));
    }

    [Fact]
    public void Decide_同空间但在屏幕外_画边缘箭头()
    {
        var o = Objective.Parse("exit", "");
        Assert.Equal(ObjectiveView.ScreenArrow, ObjectiveHud.Decide(o, here: true, onScreen: false));
    }

    [Fact]
    public void Decide_目标在别的空间_不画()
    {
        var o = Objective.Parse("point:h1_05", "");
        Assert.Equal(ObjectiveView.None, ObjectiveHud.Decide(o, here: false, onScreen: false));
    }

    [Fact]
    public void Decide_hub目标_只写按M过去()
    {
        var o = Objective.Parse("hub:hub4", "");
        Assert.Equal(ObjectiveView.AwayHint, ObjectiveHud.Decide(o, here: false, onScreen: false));
        Assert.Equal(ObjectiveView.AwayHint, ObjectiveHud.Decide(o, here: true, onScreen: true));
    }

    [Fact]
    public void Decide_没有目标点_什么都不画()
    {
        Assert.Equal(ObjectiveView.None, ObjectiveHud.Decide(Objective.Empty, here: true, onScreen: true));
    }

    // ————————————————————————— 四、边缘箭头四象限 —————————————————————————

    private const float W = 640f;
    private const float H = 440f;

    [Fact]
    public void Arrow_目标在右边_指右且贴右内缘()
    {
        Assert.True(ObjectiveArrow.TryPlace(2000f, H / 2f, W, H, ObjectiveArrow.Margin, out var a));

        Assert.True(a.DirX > 0.99f);
        Assert.True(MathF.Abs(a.DirY) < 0.01f);
        Assert.Equal(W - ObjectiveArrow.Margin, a.X, 3);
        Assert.Equal(H / 2f, a.Y, 3);
    }

    [Fact]
    public void Arrow_目标在左边_指左()
    {
        Assert.True(ObjectiveArrow.TryPlace(-800f, H / 2f, W, H, ObjectiveArrow.Margin, out var a));

        Assert.True(a.DirX < -0.99f);
        Assert.Equal(ObjectiveArrow.Margin, a.X, 3);
    }

    [Fact]
    public void Arrow_目标在上边_指上()
    {
        Assert.True(ObjectiveArrow.TryPlace(W / 2f, -600f, W, H, ObjectiveArrow.Margin, out var a));

        Assert.True(a.DirY < -0.99f);
        Assert.True(MathF.Abs(a.DirX) < 0.01f);
        Assert.Equal(ObjectiveArrow.Margin, a.Y, 3);
    }

    [Fact]
    public void Arrow_目标在下边_指下()
    {
        Assert.True(ObjectiveArrow.TryPlace(W / 2f, 1200f, W, H, ObjectiveArrow.Margin, out var a));

        Assert.True(a.DirY > 0.99f);
        Assert.Equal(H - ObjectiveArrow.Margin, a.Y, 3);
    }

    [Fact]
    public void Arrow_目标在视口内_不画()
    {
        Assert.False(ObjectiveArrow.TryPlace(W / 2f, H / 2f, W, H, ObjectiveArrow.Margin, out _));
        Assert.False(ObjectiveArrow.TryPlace(10f, 10f, W, H, ObjectiveArrow.Margin, out _));
    }

    [Fact]
    public void Arrow_越远越长_但有上限()
    {
        Assert.True(ObjectiveArrow.TryPlace(W + 200f, H / 2f, W, H, ObjectiveArrow.Margin, out var near));
        Assert.True(ObjectiveArrow.TryPlace(W + 4000f, H / 2f, W, H, ObjectiveArrow.Margin, out var far));

        Assert.True(near.Length >= ObjectiveArrow.MinLength);
        Assert.True(far.Length > near.Length);
        Assert.Equal(ObjectiveArrow.MaxLength, far.Length, 3);
    }

    // ————————————————————————— 五、文案 —————————————————————————

    [Fact]
    public void Away_说清地点与刻数()
    {
        Assert.Equal("在 县城 · 按 M 过去（2 刻）", ObjectiveText.Away("hub4", 2));
        Assert.Equal("在 城下营地 · 按 M 过去（3 刻）", ObjectiveText.Away("hub7", 3));
    }

    [Fact]
    public void Away_没有路时只说地点()
    {
        Assert.Equal("在 县城", ObjectiveText.Away("hub4", TravelBook.NoRoute));
    }

    [Fact]
    public void HintRow_有hint才显示()
    {
        var o = Objective.Parse("point:h1_05", "穿过院子推门");

        Assert.Equal("怎么去　穿过院子推门", ObjectiveText.HintRow(o, tutorialActive: false, travelCost: 0));
        Assert.Equal("", ObjectiveText.HintRow(Objective.Parse("exit", ""), tutorialActive: false, travelCost: 0));
    }

    [Fact]
    public void HintRow_教学期间不重复显示()
    {
        var o = Objective.Parse("point:h1_05", "穿过院子推门");

        // 底下那条教学条已经在说同一件事，这一行整行空着。
        Assert.Equal("", ObjectiveText.HintRow(o, tutorialActive: true, travelCost: 0));
    }

    [Fact]
    public void HintRow_hub目标写成按M过去()
    {
        var o = Objective.Parse("hub:hub4", "从南门过去");

        Assert.Equal("在 县城 · 按 M 过去（2 刻）", ObjectiveText.HintRow(o, tutorialActive: false, travelCost: 2));
    }

    // ————————————————————————— 六、--objective 输出 —————————————————————————

    private const string SampleBeats =
        "\"id\",\"name\",\"hub\",\"day\",\"requires\",\"sets\",\"trigger\",\"note\",\"target\",\"hint\"\n" +
        "\"b01\",\"焚账\",\"hub1\",\"1\",\"\",\"beat.b01.done\",\"choice\",\"n\",\"exit\",\"顺着屋子北边的黄框走\"\n" +
        "\"b02\",\"烧灶\",\"hub1\",\"1\",\"\",\"beat.b02.done\",\"search\",\"n\",\"point:h1_05\",\"穿过院子推门\"\n" +
        "\"b03\",\"赶路\",\"hub3\",\"3\",\"\",\"beat.b03.done\",\"search\",\"n\",\"\",\"\"\n" +
        "\"b04\",\"找陈三\",\"hub3\",\"3\",\"\",\"beat.b04.done\",\"search\",\"n\",\"npc:陈三\",\"去村口找他\"\n" +
        "\"b05\",\"进城\",\"hub4\",\"5\",\"\",\"beat.b05.done\",\"choice\",\"n\",\"hub:hub4\",\"\"\n";

    private static string? PointHub(string id) => id == "h1_05" ? "hub1" : null;

    private static int Cost(string hub) => hub == "hub4" ? 2 : TravelBook.NoRoute;

    [Fact]
    public void Report_按票面格式打十二拍()
    {
        var beats = BeatTable.FromCsv(SampleBeats);
        var objectives = ObjectiveTable.FromCsv(SampleBeats);

        var lines = ObjectiveReport.Lines(beats, objectives, PointHub, Cost);

        Assert.Equal(
            "[目标点] b01 exit　怎么去：顺着屋子北边的黄框走",
            lines[0]);
        Assert.Equal(
            "[目标点] b02 point:h1_05（城南小院）　怎么去：穿过院子推门",
            lines[1]);
        Assert.Equal("[目标点] b03 无目标", lines[2]);
        Assert.Equal(
            "[目标点] b04 npc:陈三　怎么去：去村口找他",
            lines[3]);
        Assert.Equal(
            "[目标点] b05 hub:hub4　在 县城 · 按 M 过去（2 刻）",
            lines[4]);
    }

    [Fact]
    public void Report_点位查不到时不留空括号()
    {
        var line = ObjectiveReport.Line(
            "b09", Objective.Parse("point:nowhere", ""), _ => null, _ => TravelBook.NoRoute);

        Assert.Equal("[目标点] b09 point:nowhere", line);
    }

    [Fact]
    public void Report_旧表没有两列_每一拍都写无目标()
    {
        var beats = BeatTable.FromCsv(OldBeats);
        var objectives = ObjectiveTable.FromCsv(OldBeats);

        var lines = ObjectiveReport.Lines(beats, objectives, _ => null, _ => TravelBook.NoRoute);

        Assert.Single(lines);
        Assert.Equal("[目标点] b01 无目标", lines[0]);
    }
}
