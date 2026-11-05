using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 转段第四页「分封」（票号 S4-01d）。规则在 <see cref="EnfeoffBook"/>（S4-01a），
/// 存档在 <see cref="EnfeoffRecord"/>（S4-01b），这一层只盯界面：
/// 六行怎么念、左右改档、上下选人、Enter 落档、报告页追加两行、
/// 已经封过的档跳过这一页，以及 <c>--enfeoff</c> 那四行。
///
/// 全部不走 Godot：<see cref="TransitionScreen"/> 只算字，画在 Main 里。
/// 数据用真表（companions.csv / oaths.csv / realms.csv），文案读的与画面是同一份。
/// </summary>
public class GameEnfeoffTests
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

    private static CompanionTable RealCompanions() => CompanionTable.Load(FindData("companions.csv"));

    private static OathTable RealOaths() => OathTable.Load(FindData("oaths.csv"));

    private static RealmTable RealRealms() => RealmTable.Load(FindData("realms.csv"));

    /// <summary>转段这一步假定许过的两条承诺（oaths.csv 的 source 列）。</summary>
    private const string Pick07 = "beat.b07.pick.1";

    /// <summary>第二条承诺。</summary>
    private const string Pick12 = "beat.b12.pick.1";

    /// <summary>十二拍都走完的一份存档。</summary>
    private static SaveState TwelveBeats()
    {
        var state = new SaveState();
        for (var i = 1; i <= TransitionBook.MinBeats; i++)
            state.Prologue.BeatsDone.Add($"b{i:00}");
        return state;
    }

    private sealed record Fixture(
        TransitionBook Book,
        TransitionScreen Screen,
        SaveState State,
        TrustState Trust,
        FlagStore Flags,
        CompanionTable Companions);

    /// <summary>摊开转段界面：三轴从 民 10 / 官 5 / 疑 3 起，许过两条承诺。</summary>
    private static Fixture Open(SaveState? state = null, CompanionTable? companions = null)
    {
        var flags = new FlagStore();
        Assert.True(flags.Set(Pick07));
        Assert.True(flags.Set(Pick12));

        var realms = RealRealms();
        var save = state ?? TwelveBeats();
        var trust = new TrustState { Min = 10, Guan = 5, Yi = 3 };
        var table = companions ?? RealCompanions();
        var book = new TransitionBook(RealOaths(), realms, flags, save);
        var screen = new TransitionScreen(
            book, realms, save, table, new EnfeoffBook(), trust, flags);

        return new Fixture(book, screen, save, trust, flags, table);
    }

    /// <summary>走到第四页：定都 hub4、取第一个国号、国策默认全立。</summary>
    private static void ToEnfeoff(TransitionScreen screen)
    {
        Assert.True(screen.SelectCapital(3));
        screen.Advance();
        Assert.True(screen.SelectRealm(0));
        screen.Advance();
        Assert.Equal(TransitionPage.Oaths, screen.Page);
        Assert.True(screen.Advance());
        Assert.Equal(TransitionPage.Enfeoff, screen.Page);
    }

    /// <summary>报告页用的那一份实数。</summary>
    private static ReportFacts Facts(int troops = 54, int guan = 8, int min = 22, int yi = 3,
        int bagUsed = 7, int bagCapacity = 12, int day = 30) =>
        new(troops, guan, min, yi, bagUsed, bagCapacity, day);

    /// <summary>六个低野心的人：薄封不会有人离心，用来看"无提醒"那一态。</summary>
    private const string CalmCsv =
        "\"id\",\"name\",\"office\",\"role\",\"loyalty\",\"ambition\",\"note\"\n"
        + "\"a\",\"甲\",\"左吏\",\"文\",\"50\",\"10\",\"\"\n"
        + "\"b\",\"乙\",\"右吏\",\"文\",\"50\",\"10\",\"\"\n"
        + "\"c\",\"丙\",\"少吏\",\"文\",\"50\",\"10\",\"\"\n"
        + "\"d\",\"丁\",\"典吏\",\"文\",\"50\",\"10\",\"\"\n"
        + "\"e\",\"戊\",\"书吏\",\"文\",\"50\",\"10\",\"\"\n"
        + "\"f\",\"己\",\"库吏\",\"文\",\"50\",\"10\",\"\"";

    // ————————————————————————— 一、六行与默认档 —————————————————————————

    [Fact]
    public void 分封页六行_照表念官忠野心_默认薄封()
    {
        var (_, screen, _, _, _, _) = Open();
        ToEnfeoff(screen);

        var rows = screen.EnfeoffRows();

        Assert.Equal(6, rows.Count);
        Assert.Equal("阿禾　大司农　忠 78 野心 20　［薄封］", rows[0].Text);
        Assert.Equal("沈砚　御史大夫　忠 62 野心 55　［薄封］", rows[1].Text);
        Assert.Equal("裴无咎　车骑将军　忠 34 野心 78　［薄封］", rows[4].Text);
        Assert.Equal("小满　中尉　忠 94 野心 8　［薄封］", rows[5].Text);
        Assert.All(screen.EnfeoffGrades, g => Assert.Equal(Enfeoff.Light, g));

        Assert.Equal(0, screen.Selected);
        Assert.True(screen.IsCursorAt(0));
    }

    [Fact]
    public void 分封页抬头与底注_排在国策之后报告之前()
    {
        var (_, screen, _, _, _, _) = Open();
        ToEnfeoff(screen);

        Assert.Equal("四　分封", screen.Head);
        Assert.Equal("↑↓ 选人　·　←→ 改封赏　·　Enter 定下", screen.Foot);
        Assert.Equal(6, screen.RowCount());
        Assert.False(screen.CanClose);

        screen.Advance();
        Assert.Equal("五　国情报告", screen.Head);
    }

    // ————————————————————————— 二、左右改档、上下选人 —————————————————————————

    [Fact]
    public void 左右改档_薄封换成厚封再换回来_存档一个字没动()
    {
        var (_, screen, state, _, _, _) = Open();
        ToEnfeoff(screen);

        Assert.True(screen.CycleGrade(-1));                 // 薄封 → 厚封
        Assert.Equal(Enfeoff.Heavy, screen.GradeAtCursor);
        Assert.Contains("［厚封］", screen.EnfeoffRows()[0].Text);

        Assert.True(screen.CycleGrade(1));                  // 厚封 → 薄封
        Assert.Equal(Enfeoff.Light, screen.GradeAtCursor);

        Assert.True(screen.CycleGrade(1));                  // 薄封 → 不封
        Assert.Equal(Enfeoff.None, screen.GradeAtCursor);

        Assert.True(screen.CycleGrade(1));                  // 不封 → 厚封
        Assert.Equal(Enfeoff.Heavy, screen.GradeAtCursor);

        // 只改面板，不结算：没按 Enter 之前存档里一条封赏都没有。
        Assert.Equal(0, EnfeoffRecord.Count(state));
    }

    [Fact]
    public void 上下换人_到头停住不绕圈()
    {
        var (_, screen, _, _, _, _) = Open();
        ToEnfeoff(screen);

        screen.MoveEnfeoffCursor(-1);
        Assert.Equal(0, screen.Selected);                   // 已经在第一行，停住

        screen.MoveEnfeoffCursor(2);
        Assert.Equal(2, screen.Selected);

        screen.MoveEnfeoffCursor(99);
        Assert.Equal(5, screen.Selected);                   // 末行，停住

        screen.MoveEnfeoffCursor(1);
        Assert.Equal(5, screen.Selected);

        Assert.True(screen.IsCursorAt(5));
        Assert.False(screen.IsCursorAt(4));
    }

    // ————————————————————————— 三、Enter 落档 —————————————————————————

    [Fact]
    public void 回车定下_六人全落EnfeoffRecord并进报告页()
    {
        var (_, screen, state, _, _, _) = Open();
        ToEnfeoff(screen);

        Assert.True(screen.Advance());

        Assert.Equal(TransitionPage.Report, screen.Page);
        Assert.Equal(6, EnfeoffRecord.Count(state));
        foreach (var companion in RealCompanions().All)
        {
            Assert.True(EnfeoffRecord.TryGet(state, companion.Id, out var grade));
            Assert.Equal(Enfeoff.Light, grade);
        }
    }

    [Fact]
    public void 回车定下_忠诚与三轴按Core的账走()
    {
        var (_, screen, _, trust, flags, _) = Open();
        ToEnfeoff(screen);
        screen.Advance();

        var sealedParts = screen.SealedParts;

        Assert.Equal(6, sealedParts.Count);
        Assert.Equal(new[] { "阿禾 薄封（忠 78→84）", "沈砚 薄封（忠 62→68）",
            "老鸦 薄封（忠 66→72）", "玄真 薄封（忠 58→64）",
            "裴无咎 薄封（忠 34→40）", "小满 薄封（忠 94→100）" },
            sealedParts.Select(p => p.Text));

        Assert.Equal(22, trust.Min);                        // 六次薄封：民望 +12
        Assert.Equal(5, trust.Guan);                        // 官望没动
        Assert.Equal(3, trust.Yi);                          // 疑心没动

        Assert.True(flags.Has(EnfeoffBook.ResentedFlag("laoya"), null));
        Assert.True(flags.Has(EnfeoffBook.ResentedFlag("peiwujiu"), null));
        Assert.False(flags.Has(EnfeoffBook.ResentedFlag("ahe"), null));
    }

    [Fact]
    public void 厚封裴无咎_忠掉八点_官望加三_置权臣旗标()
    {
        var (_, screen, _, trust, flags, _) = Open();
        ToEnfeoff(screen);

        screen.MoveEnfeoffCursor(4);
        Assert.True(screen.CycleGrade(-1));                 // 薄封 → 厚封
        Assert.True(screen.Advance());

        Assert.True(flags.Has(EnfeoffBook.WillBePowerfulFlag("peiwujiu"), null));
        Assert.Equal(8, trust.Guan);                        // 5 + 3

        var pei = screen.SealedParts.Single(p => p.Name == "裴无咎");
        Assert.Equal("厚封", pei.Grade);
        Assert.Equal(34, pei.Before);
        Assert.Equal(26, pei.After);

        Assert.Contains("裴无咎 厚封——他迟早要更多。", screen.EnfeoffNotice());
    }

    // ————————————————————————— 四、报告页两行 —————————————————————————

    [Fact]
    public void 报告页追加分封行与提醒行_有离心的人就提醒()
    {
        var (_, screen, _, _, _, _) = Open();
        ToEnfeoff(screen);
        screen.Advance();

        var lines = screen.ReportLines(Facts());

        Assert.Equal(12, lines.Count);
        Assert.Equal("都城 县城", lines[0]);
        Assert.Equal("国策 2 条", lines[2]);

        Assert.StartsWith("分封　阿禾 薄封（忠 78→84）", lines[4]);
        Assert.EndsWith("…", lines[4]);                     // 六个人一行放不下，截断
        Assert.Contains("老鸦 薄封——他心里记着。", lines[5]);
        Assert.Contains("裴无咎 薄封——他心里记着。", lines[5]);

        // 下面五行照设计文档 §16.6 第 5 步，一个字不改。
        Assert.Equal(new[] { "国库空", "粮价高", "旧朝三路集结", "宗室要供养", "官员在等你发俸禄" },
            lines.Skip(6).Take(5));
        Assert.StartsWith("兵力 54", lines[11]);
    }

    [Fact]
    public void 没有离心与权臣_报告页只加分封那一行()
    {
        var calm = CompanionTable.FromCsv(CalmCsv);
        var (_, screen, _, _, _, _) = Open(companions: calm);
        ToEnfeoff(screen);
        screen.Advance();

        Assert.Equal("", screen.EnfeoffNotice());

        var lines = screen.ReportLines(Facts());

        Assert.Equal(11, lines.Count);
        Assert.StartsWith("分封　甲 薄封（忠 50→56）", lines[4]);
        Assert.Equal("国库空", lines[5]);
    }

    // ————————————————————————— 五、已分封的档跳过这一页 —————————————————————————

    [Fact]
    public void 已经分封过的档_再进转段直接跳过这一页()
    {
        var state = TwelveBeats();
        var companions = RealCompanions();
        foreach (var companion in companions.All)
            EnfeoffRecord.Put(state, companion.Id, Enfeoff.Heavy);

        var (_, screen, _, trust, _, _) = Open(state, companions);

        Assert.False(screen.NeedsEnfeoff);

        Assert.True(screen.SelectCapital(3));
        screen.Advance();
        Assert.True(screen.SelectRealm(0));
        screen.Advance();
        Assert.Equal(TransitionPage.Oaths, screen.Page);
        screen.Advance();                                   // 国策定下 → 跳过第四页

        Assert.Equal(TransitionPage.Report, screen.Page);
        Assert.Equal(6, EnfeoffRecord.Count(state));
        Assert.Equal(5, trust.Guan);                        // 没有第二次厚封，官望没再涨
        Assert.Empty(screen.SealedParts);
    }

    [Fact]
    public void 只封过一部分_页面照进_已经记过的人不再结算()
    {
        var state = TwelveBeats();
        var companions = RealCompanions();
        EnfeoffRecord.Put(state, "peiwujiu", Enfeoff.Light);

        var (_, screen, _, trust, _, _) = Open(state, companions);
        ToEnfeoff(screen);

        Assert.Equal(Enfeoff.Light, screen.EnfeoffGrades[4]);   // 照记过的摆

        Assert.True(screen.Advance());

        Assert.Equal(6, EnfeoffRecord.Count(state));
        Assert.Equal(5, screen.SealedParts.Count);              // 裴无咎没再算一遍
        Assert.Equal(20, trust.Min);                            // 只加了五个人：10 + 10
        Assert.DoesNotContain(screen.SealedParts, p => p.Name == "裴无咎");
    }

    // ————————————————————————— 六、Esc 不退 —————————————————————————

    [Fact]
    public void 分封页上Esc不退_只有完成页才放人走()
    {
        var (_, screen, _, _, _, _) = Open();
        ToEnfeoff(screen);

        Assert.False(screen.CanClose);
        screen.RefuseBack();
        Assert.Equal("这一步退不回去。", screen.Note);
        Assert.Equal(TransitionPage.Enfeoff, screen.Page);

        screen.Advance();                                   // 报告页
        Assert.False(screen.CanClose);

        screen.Advance();                                   // 完成页
        Assert.True(screen.CanClose);
        Assert.Equal(TransitionPage.Done, screen.Page);
    }

    // ————————————————————————— 七、--enfeoff —————————————————————————

    [Fact]
    public void enfeoff自检_四行说清六人的官_默认薄封_裴无咎厚封与报告行()
    {
        var lines = EnfeoffDemo.Lines(RealCompanions());

        Assert.Equal(4, lines.Count);

        Assert.Equal("[分封] 六人：阿禾 大司农｜沈砚 御史大夫｜老鸦 太尉"
                     + "｜玄真 太常｜裴无咎 车骑将军｜小满 中尉", lines[0]);
        Assert.Equal("[分封] 默认薄封：六人忠诚 +6", lines[1]);
        Assert.Equal("[分封] 把裴无咎改成厚封 → 忠 34 → 26"
                     + "，置 enfeoff.will_be_powerful.peiwujiu，官望 +3", lines[2]);
        Assert.StartsWith("[分封] 报告行：分封　阿禾 薄封（忠 78→84）", lines[3]);
        Assert.EndsWith("…", lines[3]);
    }

    [Fact]
    public void enfeoff自检的报告行里_裴无咎那一段是厚封()
    {
        var line = EnfeoffDemo.Lines(RealCompanions())[3];

        // 六个人一行放不下，裴无咎排在第五，截断后不一定在——这一段只在没截断时校验。
        if (!line.Contains('…'))
            Assert.Contains("裴无咎 厚封（忠 34→26）", line);

        // 无论截不截断，报告行都以"分封　"开头。
        Assert.StartsWith("[分封] 报告行：分封　", line);
    }

    [Fact]
    public void 报告行宽度不超过面板_超过就截断并缀省略号()
    {
        var parts = Enumerable.Range(0, 6)
            .Select(i => new EnfeoffPart($"功臣{i}", "薄封", 50, 56))
            .ToList();

        var line = TransitionText.EnfeoffReportLine(parts);

        Assert.StartsWith("分封　功臣0 薄封（忠 50→56）", line);
        Assert.Contains("· …", line);
        Assert.DoesNotContain("功臣5", line);
    }
}
