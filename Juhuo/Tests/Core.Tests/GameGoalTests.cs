using System;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 常驻目标 + 八步强制教学（票号 S4-01c）。玩家的两句话：
/// <b>「主线剧情中主角的目的要明确一点」</b>、<b>「给多一点强制的教学」</b>。
///
/// 这一层守四件事：
/// ① HUD 那一行 **目标** 的四种取值——起手 / 正等着的那一拍 / 十二拍走完 / 已转段，
///    以及"拍做完、读档、转段之后它真的会变"；
/// ② 开局那张目标卡：说 start 那一句、2.5 秒自己走、任意键能收、**永远不挡键**；
/// ③ 教学的**八步顺序**与 tutorial.csv 一字不差，中文只从表里来；
/// ④ 面板三步（map / bag / trust）"打开就算做到"，以及**八步没走完不许出城**。
///
/// 全部不碰 Godot：目标拼法在 <see cref="GoalHud"/>，卡在 <see cref="GoalCard"/>，
/// 步骤机在 <see cref="TutorialRun"/>（Application 层；<c>Onboarding</c> 只是 Game 层的一层转手）。
/// </summary>
public class GameGoalTests
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

    private static GoalTable Goals() => GoalTable.Load(FindData("goals.csv"));

    private static BeatTable Beats() => BeatTable.Load(FindData("beats.csv"));

    private static TutorialScript Tutorial() => TutorialScript.Load(FindData("tutorial.csv"));

    private static Beat B(BeatTable table, string id) =>
        table.Get(id) ?? throw new InvalidOperationException($"表里没有 {id}");

    /// <summary>全新一局：第 1 天、一拍没做、旗标仓库空的。</summary>
    private static PrologueBeats Fresh(out BeatTable table)
    {
        table = Beats();
        return PrologueBeats.FromState(table, new FlagStore(), null);
    }

    /// <summary>造一份"做到第几拍、停在第几天"的存档并读回来（读档口径照 GameBeatsTests）。</summary>
    private static PrologueBeats Restored(BeatTable table, int day, params string[] done)
    {
        var state = new SaveState { Day = day };

        foreach (var id in done)
        {
            state.Prologue.BeatsDone.Add(id);

            var beat = table.Get(id);
            if (beat is null) continue;
            foreach (var flag in beat.Sets) state.Flags[flag] = true;
        }

        var flags = new FlagStore();
        flags.Load(state);
        return PrologueBeats.FromState(table, flags, state);
    }

    // ————————————————————————— ① HUD 那一行 —————————————————————————

    [Fact(DisplayName = "目标：起手那一句是 goals.csv 的 start，前面挂「目标　」")]
    public void GoalHud_Start_WhenNothingPending()
    {
        var goals = Goals();

        Assert.Equal("目标　活过今夜", GoalHud.Line(goals, null, beatsDone: false, turnedRealm: false));
    }

    [Fact(DisplayName = "目标：有正等着的一拍时，说的是那一拍自己的 goal")]
    public void GoalHud_BeatGoal_WhenBeatPending()
    {
        var beats = Fresh(out var table);
        var goals = Goals();

        Assert.Equal("目标　烧了账册，或者带走",
            GoalHud.Line(goals, beats.Current, beats.AllDone, turnedRealm: false));
    }

    [Fact(DisplayName = "目标：十二拍走完还没转段，改催「定都、定国号、定国策」")]
    public void GoalHud_Deficit_WhenAllBeatsDone()
    {
        var goals = Goals();

        Assert.Equal("目标　定都、定国号、定国策",
            GoalHud.Line(goals, null, beatsDone: true, turnedRealm: false));
    }

    [Fact(DisplayName = "目标：转段之后是 realm 那一句")]
    public void GoalHud_Realm_WhenTurnedRealm()
    {
        var goals = Goals();

        Assert.Equal("目标　坐稳这把椅子",
            GoalHud.Line(goals, B(Beats(), "b03"), beatsDone: false, turnedRealm: true));
    }

    [Fact(DisplayName = "目标：转段压过「十二拍走完」——两样同时成立时说 realm")]
    public void GoalHud_Realm_OutranksDeficit()
    {
        var goals = Goals();

        Assert.Equal("目标　坐稳这把椅子",
            GoalHud.Line(goals, null, beatsDone: true, turnedRealm: true));
    }

    [Fact(DisplayName = "目标：做完一拍之后，HUD 那一行跟着换成下一拍的话")]
    public void Goal_AfterCompletingBeat_MovesToNextBeat()
    {
        var beats = Fresh(out var table);
        var goals = Goals();

        Assert.Equal("目标　烧了账册，或者带走",
            GoalHud.Line(goals, beats.Current, beats.AllDone, false));

        Assert.True(beats.Complete(B(table, "b01")));

        Assert.Equal("目标　从后窗逃出去",
            GoalHud.Line(goals, beats.Current, beats.AllDone, false));
    }

    [Fact(DisplayName = "目标：读档进来（前四拍已做、第 12 天）显示第五拍那句")]
    public void Goal_LoadedSave_FollowsPendingBeat()
    {
        var table = Beats();
        var goals = Goals();
        var beats = Restored(table, 12, "b01", "b02", "b03", "b04");

        Assert.Equal("b05", beats.Current?.Id);
        Assert.Equal("目标　进城，先摸清市价",
            GoalHud.Line(goals, beats.Current, beats.AllDone, false));
    }

    [Fact(DisplayName = "目标：读档进来十二拍全做完却没转段，显示 deficit 那一句")]
    public void Goal_LoadedSave_AllDone_ShowsDeficit()
    {
        var table = Beats();
        var goals = Goals();
        var beats = Restored(table, 30, table.All.Select(b => b.Id).ToArray());

        Assert.True(beats.AllDone);
        Assert.Equal("目标　定都、定国号、定国策",
            GoalHud.Line(goals, beats.Current, beats.AllDone, false));
    }

    [Fact(DisplayName = "目标：读档进来已经转过段（prologue.done），显示 realm 那一句")]
    public void Goal_LoadedSave_TurnedRealm_ShowsRealm()
    {
        var table = Beats();
        var goals = Goals();
        var beats = Restored(table, 30, table.All.Select(b => b.Id).ToArray());

        var state = new SaveState { Day = 30 };
        state.Flags[TransitionBook.DoneFlag] = true;

        var flags = new FlagStore();
        flags.Load(state);

        var transition = new TransitionBook(
            OathTable.Load(FindData("oaths.csv")),
            RealmTable.Load(FindData("realms.csv")),
            flags,
            state);

        Assert.True(transition.IsDone);
        Assert.Equal("目标　坐稳这把椅子",
            GoalHud.Line(goals, beats.Current, beats.AllDone, transition.IsDone));
    }

    // ————————————————————————— ② 开局那张目标卡 —————————————————————————

    [Fact(DisplayName = "目标卡：正文取 start 那一句，2.5 秒到点自己收")]
    public void GoalCard_ShowsStartText_AndAutoHides()
    {
        var card = new GoalCard();

        card.Show(Goals().Start);

        Assert.True(card.Visible);
        Assert.Equal("活过今夜", card.Body);
        Assert.Equal("你要做的", GoalCard.Head);

        // 2.4 秒还在（淡入早就满了）。
        Assert.True(card.Tick(2.4f));
        Assert.True(card.Visible);
        Assert.Equal(1f, card.Alpha, 3);

        // 再过 0.2 秒（合计 2.6 > 2.5）自己走。
        Assert.False(card.Tick(0.2f));
        Assert.False(card.Visible);
    }

    [Fact(DisplayName = "目标卡：任意键能收，而且它永远不挡键（不是「按 Enter 继续」的门）")]
    public void GoalCard_AnyKeyDismisses_AndNeverBlocksKeys()
    {
        var card = new GoalCard();
        card.Show("活过今夜");

        Assert.False(card.BlocksInput);          // 卡在屏幕上也不吃键

        Assert.True(card.Dismiss());             // 按了任意一个键
        Assert.False(card.Visible);
        Assert.False(card.Dismiss());            // 已经收了，再按不算
    }

    [Fact(DisplayName = "目标卡：淡入是渐显，不是一下子糊上脸")]
    public void GoalCard_FadesIn()
    {
        var card = new GoalCard();
        card.Show("活过今夜");

        Assert.Equal(0f, card.Alpha, 3);

        card.Tick(GoalCard.FadeSeconds / 2f);
        Assert.InRange(card.Alpha, 0.3f, 0.7f);

        card.Tick(GoalCard.FadeSeconds);
        Assert.Equal(1f, card.Alpha, 3);
    }

    // ————————————————————————— ③ 八步与表 —————————————————————————

    [Fact(DisplayName = "教学：八步顺序与 tutorial.csv 一字不差，title/hint 都从表里来")]
    public void Tutorial_OrderMatchesCsv()
    {
        var script = Tutorial();
        var tut = new TutorialRun(script);

        Assert.Equal(8, script.Count);
        Assert.Equal(
            new[] { "move", "look", "search", "map", "bag", "trust", "talk", "leave" },
            script.All.Select(s => s.Key).ToArray());

        Assert.Equal(Enumerable.Range(1, 8), script.All.Select(s => s.Seq));

        // 第一行就是表里的字：不是代码里另写一份。
        Assert.Equal("按 W A S D 走两步", script[0].Title);
        Assert.Equal("先走出这间屋子", script[0].Hint);
        Assert.Equal("按 W A S D 走两步　·　先走出这间屋子", tut.Line.Text);

        // 八行依次落在这八步上（第 N 行就是第 N 步）。
        var order = new[] { "move", "look", "search", "map", "bag", "trust", "talk", "leave" };

        for (var i = 0; i < order.Length; i++)
        {
            Assert.True(tut.IsAt(order[i]), $"第 {i + 1} 步该是 {order[i]}");
            Assert.Equal($"{i + 1}/8", tut.Bar);
            if (i < order.Length - 1) Assert.False(tut.Advance());
        }

        Assert.Equal("leave", tut.Step!.Key);
        Assert.True(tut.Advance());
        Assert.True(tut.Done);
    }

    [Fact(DisplayName = "教学：进度条写「3/8」——现在第几步 / 共几步")]
    public void Tutorial_BarShowsOrdinalOfTotal()
    {
        var tut = new TutorialRun(Tutorial());

        Assert.Equal("1/8", tut.Bar);

        tut.Advance();
        tut.Advance();
        Assert.Equal("3/8", tut.Bar);
        Assert.Equal("站到东西旁边按 E　·　斜着站也够得着，翻一次花一刻", tut.Line.Text);

        for (var i = 0; i < 6; i++) tut.Advance();
        Assert.True(tut.Done);
        Assert.Equal("8/8", tut.Bar);
    }

    [Fact(DisplayName = "教学：map / bag / trust 三步，打开那个面板就算做到")]
    public void Tutorial_PanelSteps_CompleteByOpening()
    {
        var tut = new TutorialRun(Tutorial());

        // 走到 map 这一步之前，按 M 不算数。
        Assert.False(tut.PanelOpened(TutorialScript.KeyMap));
        tut.Advance();
        tut.Advance();                                   // → search
        Assert.False(tut.PanelOpened(TutorialScript.KeyMap)); // 还在翻东西

        tut.Advance();                                   // → map
        Assert.True(tut.IsAt(TutorialScript.KeyMap));
        Assert.False(tut.PanelOpened(TutorialScript.KeyBag)); // 开错面板不算
        Assert.Equal("4/8", tut.Bar);

        Assert.True(tut.PanelOpened(TutorialScript.KeyMap));  // 面板开出来 → 过
        Assert.True(tut.IsAt(TutorialScript.KeyBag));

        Assert.True(tut.PanelOpened(TutorialScript.KeyBag));
        Assert.True(tut.IsAt(TutorialScript.KeyTrust));

        Assert.True(tut.PanelOpened(TutorialScript.KeyTrust));
        Assert.True(tut.IsAt(TutorialScript.KeyTalk));
    }

    // ————————————————————————— ④ 八步走完才放行 —————————————————————————

    [Fact(DisplayName = "教学：八步没走完不许出城，走完才放行")]
    public void Tutorial_CannotLeaveCity_UntilEightStepsDone()
    {
        var tut = new TutorialRun(Tutorial());

        for (var i = 0; i < 8; i++)
        {
            Assert.False(tut.CanLeaveCity);   // 前八步里任何一步都不放行
            tut.Advance();
        }

        Assert.True(tut.Done);
        Assert.True(tut.CanLeaveCity);
    }

    [Fact(DisplayName = "教学：走一遍八步之后停在 Done，教学条不再显示")]
    public void Tutorial_AllEightSteps_EndsInDone()
    {
        var tut = new TutorialRun(Tutorial());

        for (var i = 0; i < 8; i++) tut.Advance();

        Assert.True(tut.Done);
        Assert.Null(tut.Step);
        Assert.Equal(("", ""), tut.Line);
    }

    [Fact(DisplayName = "教学：Reset 回到第一步，Finish 直接算教完（读档 / 跑批用）")]
    public void Tutorial_ResetAndFinish()
    {
        var tut = new TutorialRun(Tutorial());
        tut.Advance();
        tut.Advance();
        Assert.Equal("3/8", tut.Bar);

        tut.Reset();
        Assert.Equal("1/8", tut.Bar);

        tut.Finish();
        Assert.True(tut.Done);
        Assert.True(tut.CanLeaveCity);
    }

    [Fact(DisplayName = "教学：读不到表就不教，但游戏照常能玩（不卡在第一步）")]
    public void Tutorial_NoScript_IsDoneAtOnce()
    {
        var tut = new TutorialRun(null);

        Assert.True(tut.Done);
        Assert.True(tut.CanLeaveCity);
        Assert.Equal(("", ""), tut.Line);
        Assert.Equal(0, tut.Total);
    }
}
