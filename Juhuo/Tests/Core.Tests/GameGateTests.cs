using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 教学要真强制（票号 S4-02）。
///
/// S4-01c 的八步只会"判定做到了没有"，不拦人：第一步写着按 WASD，玩家却能直接按 M
/// 开图、按 Tab 看三轴、走到出口出城。那是提示。这一层盯的是那张**闸门表**：
/// 每一步放行哪几个动作、拦下来时说什么、被拦下时状态有没有被动过、
/// 以及"这一步在这个空间里根本做不成"时怎么跳过。
///
/// 闸门是纯函数（<see cref="TutorialGate.Block"/>），所以这里不需要 Godot，也不需要世界。
/// </summary>
public class GameGateTests
{
    /// <summary>九个动作用同一个口径遍历——漏一个就等于漏一条输入口。</summary>
    private static readonly Act[] AllActs =
    {
        Act.Move, Act.Look, Act.Search, Act.Map, Act.Bag,
        Act.Trust, Act.Talk, Act.Exit, Act.Help, Act.Wait,
    };

    /// <summary>八步（不含 Done）。</summary>
    private static readonly TutorialStage[] Steps =
    {
        TutorialStage.Move, TutorialStage.Look, TutorialStage.Search, TutorialStage.Map,
        TutorialStage.Bag, TutorialStage.Trust, TutorialStage.Talk, TutorialStage.Leave,
    };

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

    // ————————————————————————— 那张表 —————————————————————————

    [Fact]
    public void Block_OnlyRequiredActionAndMovePass()
    {
        foreach (var stage in Steps)
        {
            var required = TutorialGate.Required(stage);

            foreach (var act in AllActs)
            {
                var allowed = act is Act.Move or Act.Help or Act.Wait || act == required;
                var hint = TutorialGate.Block(stage, act);

                if (allowed)
                    Assert.True(hint is null, $"{stage} 该放行 {act}，却拦下了：{hint}");
                else
                    Assert.False(string.IsNullOrEmpty(hint), $"{stage} 该拦下 {act}，却放行了");
            }
        }
    }

    [Fact]
    public void Block_MoveIsAlwaysAllowed()
    {
        foreach (var stage in Steps)
            Assert.Null(TutorialGate.Block(stage, Act.Move));
    }

    [Fact]
    public void Block_MoveStage_BlocksEverythingButWalking()
    {
        // 玩家原话最刺人的一条：第一步还没走，却已经能翻能开图能出门。
        Assert.Null(TutorialGate.Block(TutorialStage.Move, Act.Move));

        Assert.Equal(TutorialGate.MoveHint, TutorialGate.Block(TutorialStage.Move, Act.Search));
        Assert.Equal(TutorialGate.MoveHint, TutorialGate.Block(TutorialStage.Move, Act.Map));
        Assert.Equal(TutorialGate.MoveHint, TutorialGate.Block(TutorialStage.Move, Act.Bag));
        Assert.Equal(TutorialGate.MoveHint, TutorialGate.Block(TutorialStage.Move, Act.Trust));
        Assert.Equal(TutorialGate.MoveHint, TutorialGate.Block(TutorialStage.Move, Act.Talk));
        Assert.Equal(TutorialGate.MoveHint, TutorialGate.Block(TutorialStage.Move, Act.Exit));
        Assert.Equal(TutorialGate.MoveHint, TutorialGate.Block(TutorialStage.Move, Act.Look));
    }

    [Fact]
    public void Block_LookStage_AllowsCtrlAndBlocksSearch()
    {
        Assert.Null(TutorialGate.Block(TutorialStage.Look, Act.Look));
        Assert.Equal(TutorialGate.LookHint, TutorialGate.Block(TutorialStage.Look, Act.Search));
    }

    [Fact]
    public void Block_SearchStage_AllowsOnlySearch()
    {
        Assert.Null(TutorialGate.Block(TutorialStage.Search, Act.Search));
        Assert.Equal(TutorialGate.SearchHint, TutorialGate.Block(TutorialStage.Search, Act.Map));
    }

    [Fact]
    public void Block_PanelStages_AllowOnlyTheirOwnPanel()
    {
        Assert.Null(TutorialGate.Block(TutorialStage.Map, Act.Map));
        Assert.Equal(TutorialGate.MapHint, TutorialGate.Block(TutorialStage.Map, Act.Bag));

        Assert.Null(TutorialGate.Block(TutorialStage.Bag, Act.Bag));
        Assert.Equal(TutorialGate.BagHint, TutorialGate.Block(TutorialStage.Bag, Act.Map));

        Assert.Null(TutorialGate.Block(TutorialStage.Trust, Act.Trust));
        Assert.Equal(TutorialGate.TrustHint, TutorialGate.Block(TutorialStage.Trust, Act.Bag));
    }

    [Fact]
    public void Block_TalkStage_AllowsTalkAndBlocksExit()
    {
        Assert.Null(TutorialGate.Block(TutorialStage.Talk, Act.Talk));
        Assert.Equal(TutorialGate.TalkHint, TutorialGate.Block(TutorialStage.Talk, Act.Exit));
    }

    [Fact]
    public void Block_LeaveStage_AllowsExitAndBlocksTalk()
    {
        Assert.Null(TutorialGate.Block(TutorialStage.Leave, Act.Exit));
        Assert.Equal(TutorialGate.LeaveHint, TutorialGate.Block(TutorialStage.Leave, Act.Talk));
        Assert.Equal(TutorialGate.LeaveHint, TutorialGate.Block(TutorialStage.Leave, Act.Search));
    }

    // ————————————————————————— 提示文案 —————————————————————————

    [Fact]
    public void Hint_EveryStepHasOneAndItIsWhatBlockReturns()
    {
        foreach (var stage in Steps)
        {
            var hint = TutorialGate.Hint(stage);
            Assert.False(string.IsNullOrWhiteSpace(hint), $"{stage} 没有提示文案");

            foreach (var act in AllActs)
            {
                var blocked = TutorialGate.Block(stage, act);
                if (blocked is not null) Assert.Equal(hint, blocked);
            }
        }

        // 教完了就没有"该做什么"可说了。
        Assert.Equal("", TutorialGate.Hint(TutorialStage.Done));
    }

    [Fact]
    public void Hint_IsNeverTheSameSentenceForTwoSteps()
    {
        // 两条不同的指令写成同一句话，玩家会以为是同一件事。
        var hints = Steps.Select(TutorialGate.Hint).ToArray();
        Assert.Equal(hints.Length, hints.Distinct(StringComparer.Ordinal).Count());
    }

    // ————————————————————————— 永远放行的那两条 —————————————————————————

    [Fact]
    public void Help_EscIsNeverBlocked()
    {
        foreach (var stage in Steps)
            Assert.Null(TutorialGate.Block(stage, Act.Help));

        Assert.Null(TutorialGate.Block(TutorialStage.Done, Act.Help));
    }

    [Fact]
    public void Wait_IsNeverBlocked()
    {
        // "什么都没按"被拦，等于把游戏卡死。
        foreach (var stage in Steps)
            Assert.Null(TutorialGate.Block(stage, Act.Wait));
    }

    [Fact]
    public void Done_AllowsEveryAct()
    {
        foreach (var act in AllActs)
            Assert.Null(TutorialGate.Block(TutorialStage.Done, act));
    }

    // ————————————————————————— 被拦下时不许动 —————————————————————————

    /// <summary>一张最小的"玩家此刻"快照：位置、刻、已搜数、面板开关。</summary>
    private sealed class Snapshot
    {
        public float X;
        public float Y;
        public int Ke = 8;
        public int Searched;
        public bool MapOpen;
        public bool BagOpen;

        public string Fingerprint => $"{X}|{Y}|{Ke}|{Searched}|{MapOpen}|{BagOpen}";
    }

    [Fact]
    public void Permit_Blocked_ChangesNothingAndShowsTheHint()
    {
        foreach (var stage in Steps)
        {
            foreach (var act in AllActs)
            {
                if (TutorialGate.Block(stage, act) is null) continue;

                var state = new Snapshot { X = 3.5f, Y = 7, Ke = 5, Searched = 2, MapOpen = false, BagOpen = true };
                var before = state.Fingerprint;
                string? shown = null;

                var ok = TutorialGate.Permit(stage, act, () =>
                {
                    state.X += 4;          // 走了一步
                    state.Ke -= 1;         // 花了一刻
                    state.Searched += 1;   // 翻了一件
                    state.MapOpen = true;  // 开了面板
                }, hint => shown = hint);

                Assert.False(ok);
                Assert.Equal(before, state.Fingerprint);
                Assert.Equal(TutorialGate.Hint(stage), shown);
            }
        }
    }

    [Fact]
    public void Permit_Allowed_RunsTheActionAndSaysNothing()
    {
        var state = new Snapshot { Ke = 8 };
        var blocked = false;

        var ok = TutorialGate.Permit(TutorialStage.Move, Act.Move, () => state.X += 1, _ => blocked = true);

        Assert.True(ok);
        Assert.False(blocked);
        Assert.Equal(1f, state.X);
        Assert.Equal(8, state.Ke);
    }

    [Fact]
    public void Permit_NullCallbacks_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TutorialGate.Permit(TutorialStage.Move, Act.Move, null!, _ => { }));

        Assert.Throws<ArgumentNullException>(() =>
            TutorialGate.Permit(TutorialStage.Move, Act.Move, () => { }, null!));
    }

    [Fact]
    public void Block_IsPure_SameAnswerEveryTime()
    {
        // 拦一下不该把闸门自己的状态改掉：连问一百次，答案必须一模一样。
        foreach (var stage in Steps)
        {
            foreach (var act in AllActs)
            {
                var first = TutorialGate.Block(stage, act);
                for (var i = 0; i < 100; i++)
                    Assert.Equal(first, TutorialGate.Block(stage, act));
            }
        }
    }

    // ————————————————————————— 面板里也不许旁逸 —————————————————————————

    [Fact]
    public void BlockMapTravel_IsClosedUntilTheLessonIsDone()
    {
        foreach (var stage in Steps)
            Assert.Equal(TutorialGate.MapTravelBlocked, TutorialGate.BlockMapTravel(stage));

        Assert.Null(TutorialGate.BlockMapTravel(TutorialStage.Done));
        Assert.False(string.IsNullOrWhiteSpace(TutorialGate.MapTravelBlocked));
    }

    [Fact]
    public void BlockBagEdit_IsClosedUntilTheLessonIsDone()
    {
        foreach (var stage in Steps)
            Assert.Equal(TutorialGate.BagLocked, TutorialGate.BlockBagEdit(stage));

        Assert.Null(TutorialGate.BlockBagEdit(TutorialStage.Done));
        Assert.False(string.IsNullOrWhiteSpace(TutorialGate.BagLocked));
    }

    // ————————————————————————— 不许卡死 —————————————————————————

    [Fact]
    public void Impossible_SkipsTalkStepInASpaceWithoutAnyone()
    {
        // hub1（城南小院）没有 NPC：第七步在那儿永远做不成，必须自己跳过去。
        Assert.True(TutorialGate.Impossible(TutorialStage.Talk, hasProps: true, hasSpeakers: false, hasExit: true));
        Assert.False(TutorialGate.Impossible(TutorialStage.Talk, hasProps: true, hasSpeakers: true, hasExit: true));
    }

    [Fact]
    public void Impossible_SkipsSearchAndLeaveWhenThereIsNothingToDo()
    {
        Assert.True(TutorialGate.Impossible(TutorialStage.Search, hasProps: false, hasSpeakers: true, hasExit: true));
        Assert.True(TutorialGate.Impossible(TutorialStage.Leave, hasProps: true, hasSpeakers: true, hasExit: false));

        Assert.False(TutorialGate.Impossible(TutorialStage.Search, hasProps: true, hasSpeakers: true, hasExit: true));
        Assert.False(TutorialGate.Impossible(TutorialStage.Leave, hasProps: true, hasSpeakers: true, hasExit: true));
    }

    [Fact]
    public void Impossible_NeverSkipsWalkingOrPanels()
    {
        // 这些步骤不依赖空间里有什么，跳它就是白白少教一课。
        foreach (var stage in Steps)
        {
            if (stage is TutorialStage.Search or TutorialStage.Talk or TutorialStage.Leave) continue;

            Assert.False(TutorialGate.Impossible(stage, hasProps: false, hasSpeakers: false, hasExit: false));
        }
    }

    // ————————————————————————— 与教学表同序 —————————————————————————

    [Fact]
    public void StageKeys_FollowTheTutorialTableOrder()
    {
        // 闸门按阶段说话，阶段按 key 对齐教学表。两边一旦错位，
        // 玩家会看见"按 M 打开地图"写在第 3 步上。
        var stages = new[]
        {
            TutorialStage.Move, TutorialStage.Look, TutorialStage.Search, TutorialStage.Map,
            TutorialStage.Bag, TutorialStage.Trust, TutorialStage.Talk, TutorialStage.Leave,
        };

        Assert.Equal(TutorialScript.ExpectedCount, stages.Length);

        for (var i = 0; i < stages.Length; i++)
            Assert.Equal(TutorialScript.ExpectedKeys[i], TutorialGate.Key(stages[i]));

        Assert.Equal("", TutorialGate.Key(TutorialStage.Done));
        Assert.Equal((int)TutorialStage.Done, stages.Length);
    }

    [Fact]
    public void RealTutorialCsv_MatchesTheGate()
    {
        var script = TutorialScript.Load(FindData("tutorial.csv"));

        Assert.Equal(TutorialScript.ExpectedCount, script.Count);

        var stages = new[]
        {
            TutorialStage.Move, TutorialStage.Look, TutorialStage.Search, TutorialStage.Map,
            TutorialStage.Bag, TutorialStage.Trust, TutorialStage.Talk, TutorialStage.Leave,
        };

        for (var i = 0; i < stages.Length; i++)
            Assert.Equal(TutorialGate.Key(stages[i]), script[i].Key);
    }

    // ————————————————————————— --gate 的输出 —————————————————————————

    [Fact]
    public void Matrix_HasAHeaderAndOneLinePerStep()
    {
        var lines = TutorialGate.Matrix();

        Assert.Equal(2 + 8, lines.Length);
        Assert.Equal(TutorialGate.MatrixHeader, lines[0]);

        for (var i = 0; i < 8; i++)
        {
            var line = lines[2 + i];
            var stage = Steps[i];

            Assert.StartsWith($"[闸] {i + 1}/8 ", line, StringComparison.Ordinal);
            Assert.Contains(TutorialGate.Key(stage), line, StringComparison.Ordinal);
            Assert.Contains("→ " + TutorialGate.Hint(stage), line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Matrix_ShowsBothAllowedAndBlockedMarks()
    {
        var lines = TutorialGate.Matrix();

        foreach (var line in lines.Skip(2))
        {
            Assert.Contains("√", line, StringComparison.Ordinal);
            Assert.Contains("×", line, StringComparison.Ordinal);
        }

        // 第一步只有"走"和"帮"是放行的：矩阵里不许悄悄多出一个 √。
        Assert.Equal(2, lines[2].Split('√').Length - 1);
    }
}
