using System;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 转段四页接进游戏（票号 S2-05c）。规则在 <see cref="TransitionBook"/>（S2-05b），
/// 这一层只盯界面：七块地怎么列、四个国号怎么排、国策页只列许过的、
/// 一条没许就跳过、立几条剩几条转成债、报告页那十行、Finish 之后不再弹，以及 <c>--transition</c> 六行。
///
/// 全部不走 Godot：<see cref="TransitionScreen"/> 只算字，画在 Main 里。
/// 数据用真表（oaths.csv / realms.csv），文案读的与画面是同一份。
/// </summary>
public class GameTransitionTests
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

    private static OathTable RealOaths() => OathTable.Load(FindData("oaths.csv"));

    private static RealmTable RealRealms() => RealmTable.Load(FindData("realms.csv"));

    /// <summary>十二拍都走完的一份存档。</summary>
    private static SaveState TwelveBeats()
    {
        var state = new SaveState();
        for (var i = 1; i <= TransitionBook.MinBeats; i++)
            state.Prologue.BeatsDone.Add($"b{i:00}");
        return state;
    }

    /// <summary>摊开转段界面；<paramref name="picks"/> 是要预先许过的旗标。</summary>
    private static (TransitionBook Book, TransitionScreen Screen, SaveState State, FlagStore Flags)
        Open(params string[] picks)
    {
        var flags = new FlagStore();
        foreach (var pick in picks) Assert.True(flags.Set(pick), $"旗标「{pick}」没写进去。");

        var state = TwelveBeats();
        var book = new TransitionBook(RealOaths(), RealRealms(), flags, state);
        var screen = new TransitionScreen(book, RealRealms(), state);

        return (book, screen, state, flags);
    }

    /// <summary>走到第二页：定都 hub4（第 4 行）再回车。</summary>
    private static void ToRealm(TransitionScreen screen)
    {
        Assert.True(screen.SelectCapital(3));
        screen.Advance();
    }

    /// <summary>走到第三页：国号取第一个再回车。</summary>
    private static void ToOaths(TransitionScreen screen)
    {
        ToRealm(screen);
        Assert.True(screen.SelectRealm(0));
        screen.Advance();
    }

    /// <summary>报告页用的那一份实数。</summary>
    private static ReportFacts Facts(int troops = 54, int guan = 0, int min = 0, int yi = 3,
        int bagUsed = 7, int bagCapacity = 12, int day = 30) =>
        new(troops, guan, min, yi, bagUsed, bagCapacity, day);

    // ————————————————————————— 一、四页的数据组装 —————————————————————————

    [Fact]
    public void 定都页七行_名字用HubNames_第四行是县城hub4()
    {
        var (_, screen, _, _) = Open();

        var rows = screen.CapitalRows();

        Assert.Equal(7, rows.Count);
        Assert.Equal("hub1", rows[0].Id);
        Assert.Equal("1　城南小院（hub1）", rows[0].Text);
        Assert.Equal("4　县城（hub4）", rows[3].Text);
        Assert.Equal("7　城下营地（hub7）", rows[6].Text);
    }

    [Fact]
    public void 国号页四行_国号与年号写在同一行()
    {
        var (_, screen, _, _) = Open();

        var rows = screen.RealmRows();

        Assert.Equal(4, rows.Count);
        Assert.Equal("yan", rows[0].Id);
        Assert.Equal("1　炎 · 火德既明", rows[0].Text);
        Assert.Equal("2　燧 · 燧火不熄", rows[1].Text);
        Assert.Equal("4　昭 · 昭明四方", rows[3].Text);
    }

    [Fact]
    public void 国策页只列真许过的_文本照oaths表()
    {
        // 只许过 b07:1 与 b12:1；b04:2 那张（还田）不该出现在面板上。
        var (_, screen, _, _) = Open("beat.b07.pick.1", "beat.b12.pick.1");
        ToOaths(screen);

        var rows = screen.OathRows();

        Assert.Equal(new[] { "oath_pioneer", "oath_no_kill" }, rows.Select(r => r.Id));
        Assert.Equal("封你为先锋将军", rows[0].Text);
        Assert.Equal("开城之后不杀降", rows[1].Text);
        Assert.All(rows, r => Assert.True(r.Picked));      // ≤3 条：默认全选
    }

    [Fact]
    public void 国策页默认全选_空格撤一条后摘要跟着变()
    {
        var (_, screen, _, _) = Open(
            "beat.b07.pick.1", "beat.b12.pick.1", "beat.b04.pick.2");
        ToOaths(screen);

        Assert.Equal("已立 3 条，其余 0 条将转成债", screen.OathSummary());

        screen.MoveCursor(2);                 // 光标落到第三条：还田
        Assert.True(screen.ToggleOath());

        Assert.False(screen.OathRows()[2].Picked);
        Assert.Equal("已立 2 条，其余 1 条将转成债", screen.OathSummary());
    }

    // ————————————————————————— 二、一条都没许过 —————————————————————————

    [Fact]
    public void 一条承诺都没许_国策页被跳过并闪一句()
    {
        var (_, screen, state, _) = Open();
        ToRealm(screen);
        Assert.True(screen.SelectRealm(0));

        screen.Advance();                      // 国号页回车：本该进国策页

        Assert.Equal(TransitionPage.Report, screen.Page);
        Assert.Equal(TransitionText.NoOathsFlash, screen.Flash);
        Assert.Empty(screen.Made);

        var lines = screen.ReportLines(Facts());
        Assert.Equal("国策 0 条", lines[2]);
        Assert.Equal("未决 0 条", lines[3]);
        Assert.Empty(state.Prologue.Oaths);
    }

    // ————————————————————————— 三、立几条、剩几条 —————————————————————————

    [Fact]
    public void 三条里撤一条_摘要说两条_回车被挡住()
    {
        var (_, screen, state, _) = Open(
            "beat.b07.pick.1", "beat.b12.pick.1", "beat.b04.pick.2");
        ToOaths(screen);

        screen.MoveCursor(2);
        Assert.True(screen.ToggleOath());
        Assert.Equal("已立 2 条，其余 1 条将转成债", screen.OathSummary());

        // Core 的口径：许过 3 条就要立满 3 条，少一条过不去（S2-05b 的 Missing）。
        Assert.False(screen.Advance());
        Assert.Equal(TransitionPage.Oaths, screen.Page);
        Assert.Contains("还差 1 条", screen.Note);
        Assert.Empty(state.Prologue.Oaths);
    }

    [Fact]
    public void 选两条时报告的未决计数是一条()
    {
        // 直接让规则层落账（立 2 条，1 条转债），报告页照存档里的数念。
        var (book, screen, state, _) = Open(
            "beat.b07.pick.1", "beat.b12.pick.1", "beat.b04.pick.2");

        Assert.True(book.ChooseCapital("hub4").Ok);
        Assert.True(book.ChooseRealm("yan").Ok);
        Assert.True(book.ChooseOaths(new[] { "oath_pioneer", "oath_no_kill" }).Ok);

        Assert.Equal(new[] { "oath_land_back" }, state.Prologue.Debts);

        var lines = screen.ReportLines(Facts());
        Assert.Equal("国策 2 条", lines[2]);
        Assert.Equal("未决 1 条", lines[3]);
    }

    [Fact]
    public void 三条全立就能过_未决是零()
    {
        var (book, screen, state, _) = Open(
            "beat.b07.pick.1", "beat.b12.pick.1", "beat.b04.pick.2");
        ToOaths(screen);

        Assert.True(screen.Advance());          // 默认全选：回车即过

        Assert.Equal(3, state.Prologue.Oaths.Count);
        Assert.Empty(state.Prologue.Debts);
        Assert.True(book.IsComplete);

        var lines = screen.ReportLines(Facts());
        Assert.Equal("国策 3 条", lines[2]);
        Assert.Equal("未决 0 条", lines[3]);
    }

    [Fact]
    public void 数字键选国号_当场写进存档()
    {
        var (_, screen, state, _) = Open();
        ToRealm(screen);

        Assert.True(screen.SelectRealm(1));
        Assert.Equal("sui", state.Prologue.Realm);
        Assert.Equal("2　燧 · 燧火不熄", screen.RealmRows()[1].Text);
    }

    [Fact]
    public void 没选都就回车_挡住并说明()
    {
        var (_, screen, _, _) = Open();

        screen.Advance();

        Assert.Equal(TransitionPage.Capital, screen.Page);
        Assert.Equal("先定都。", screen.Note);
    }

    // ————————————————————————— 四、报告页 —————————————————————————

    [Fact]
    public void 报告页十行_上四行照刚选的_中间五行是文档原句()
    {
        var (book, screen, _, _) = Open("beat.b07.pick.1", "beat.b12.pick.1");
        ToOaths(screen);
        Assert.True(screen.Advance());

        var lines = screen.ReportLines(Facts());

        Assert.Equal(10, lines.Count);
        Assert.Equal("都城 县城", lines[0]);
        Assert.Equal("国号 炎 · 火德既明", lines[1]);
        Assert.Equal("国策 2 条", lines[2]);
        Assert.Equal("未决 0 条", lines[3]);

        // 下面五行照设计文档 §16.6 第 5 步，一个字不改。
        Assert.Equal(new[] { "国库空", "粮价高", "旧朝三路集结", "宗室要供养", "官员在等你发俸禄" },
            lines.Skip(4).Take(5));

        Assert.True(book.IsComplete);
    }

    [Fact]
    public void 报告最后一行_随兵力三轴囊与天数变()
    {
        var a = TransitionText.NumberLine(Facts());
        var b = TransitionText.NumberLine(Facts(troops: 0, guan: 12, min: 40, yi: 0, bagUsed: 2, day: 17));

        Assert.Equal("兵力 54　官望 0　民望 0　疑心 3　囊 7/12　第 30 天", a);
        Assert.Equal("兵力 0　官望 12　民望 40　疑心 0　囊 2/12　第 17 天", b);
        Assert.NotEqual(a, b);
    }

    // ————————————————————————— 五、四页之内退不出去 —————————————————————————

    [Fact]
    public void 四页之内Esc退不出去_只有完成页才行()
    {
        var (_, screen, _, _) = Open("beat.b07.pick.1");
        Assert.False(screen.CanClose);

        screen.RefuseBack();
        Assert.Equal("这一步退不回去。", screen.Note);

        ToRealm(screen);
        Assert.False(screen.CanClose);

        screen.SelectRealm(0);
        screen.Advance();
        Assert.False(screen.CanClose);          // 国策页

        screen.Advance();                       // 落定 → 报告页
        Assert.False(screen.CanClose);

        screen.Advance();                       // 收尾
        Assert.True(screen.CanClose);
        Assert.Equal(TransitionPage.Done, screen.Page);
    }

    // ————————————————————————— 六、Finish —————————————————————————

    [Fact]
    public void 报告页回车_Finish置旗标并落进存档()
    {
        var (book, screen, state, flags) = Open("beat.b07.pick.1", "beat.b12.pick.1");
        ToOaths(screen);
        screen.Advance();
        Assert.True(screen.Advance());

        Assert.True(book.IsDone);
        Assert.True(flags.Has(TransitionBook.DoneFlag, null));
        Assert.True(state.Flags[TransitionBook.DoneFlag]);
        Assert.Equal(TransitionPage.Done, screen.Page);
        Assert.Equal("序章完成", screen.Head);
        Assert.Equal("按 Esc 退出", screen.Foot);
    }

    [Fact]
    public void 已转段的档_b12做完也不再弹()
    {
        var (book, _, _, _) = Open("beat.b07.pick.1", "beat.b12.pick.1");

        Assert.True(TransitionTrigger.IsDue(book, "b12"));

        ToOathsOnly(book);
        Assert.True(book.Finish().Ok);

        Assert.True(book.IsDone);
        Assert.False(TransitionTrigger.IsDue(book, "b12"));      // 读档已转段：不弹
        Assert.False(TransitionTrigger.IsDue(null, "b12"));
        Assert.False(TransitionTrigger.IsDue(book, "b11"));
    }

    /// <summary>把一份已有承诺的转段直接走完（只为造出"已转段"的档）。</summary>
    private static void ToOathsOnly(TransitionBook book)
    {
        Assert.True(book.ChooseCapital("hub4").Ok);
        Assert.True(book.ChooseRealm("yan").Ok);
        Assert.True(book.ChooseOaths(book.MadeOaths().Select(o => o.Id)).Ok);
    }

    // ————————————————————————— 七、--transition —————————————————————————

    [Fact]
    public void transition自检_六行说清可立几条_定了哪_未决几条()
    {
        var lines = TransitionDemo.Run(RealOaths(), RealRealms());

        Assert.Equal(6, lines.Count);

        Assert.Equal("[转段] 可立的国策：2 条（许过 2 条）", lines[0]);
        Assert.Equal("[转段] 定都 县城（hub4）｜国号 炎 · 火德既明", lines[1]);
        Assert.Equal("[转段] 国策 2 条：封你为先锋将军 / 开城之后不杀降", lines[2]);
        Assert.Equal("[转段] 未决 0 条", lines[3]);
        Assert.Equal("[转段] 序章完成：是", lines[4]);
        Assert.Equal("[转段] 存档：未写（调试跑批不碰玩家的档）", lines[5]);
    }

    [Fact]
    public void transition自检真的把四样写进存档()
    {
        var state = new SaveState();

        TransitionDemo.Run(RealOaths(), RealRealms(), state);

        Assert.Equal("hub4", state.Prologue.Capital);
        Assert.Equal("yan", state.Prologue.Realm);
        Assert.Equal(new[] { "oath_pioneer", "oath_no_kill" }, state.Prologue.Oaths);
        Assert.Empty(state.Prologue.Debts);
        Assert.True(state.Flags[TransitionBook.DoneFlag]);
    }
}
