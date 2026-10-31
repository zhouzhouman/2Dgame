using System;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 十二节拍接进游戏之后的玩法层（票号 S1-06c）。守四件事：
/// ① **完成一拍之后日子怎么走**——下一拍同日就不动，跳天就走到那一天，最后一拍停 30；
/// ② **选择型节拍选了什么**落在 <c>beat.&lt;id&gt;.pick.&lt;i&gt;</c> 上，只落一格；
/// ③ **HUD 那一行**三段拼得对，全做完与"没得做"各有各的说法；
/// ④ **读档往返**之后 Current 还是同一拍——旗标先灌、状态机后造，否则十二拍全锁。
///
/// 这里测的是 Godot 之外那一层（<see cref="PrologueBeats"/>）。"按下去有没有反应"归 --beats 与手测。
/// </summary>
public class GameBeatsTests
{
    // ————————————————————————— 夹具 —————————————————————————

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

        throw new InvalidOperationException("找不到 beats.csv");
    }

    private static BeatTable Real() => BeatTable.Load(BeatsPath());

    private static Beat B(BeatTable table, string id) =>
        table.Get(id) ?? throw new InvalidOperationException($"表里没有 {id}");

    /// <summary>全新一局：第 1 天、一拍没做、旗标仓库空的。</summary>
    private static PrologueBeats Fresh(out BeatTable table, out FlagStore flags)
    {
        table = Real();
        flags = new FlagStore();
        return PrologueBeats.FromState(table, flags, null);
    }

    /// <summary>造一份"做到第几拍、停在星期几"的存档并读回来。</summary>
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

        // 读档口径：旗标先进仓库，再造状态机（S1-06 的第二个坑）。
        var flags = new FlagStore();
        flags.Load(state);
        return PrologueBeats.FromState(table, flags, state);
    }

    /// <summary>按表序一拍一拍做完，每一拍都必须当场可触发。</summary>
    private static void CompleteAll(PrologueBeats beats)
    {
        foreach (var beat in beats.Table.All)
        {
            Assert.True(beats.Machine.CanTrigger(beat), $"{beat.Id} 在这时应该可触发");
            Assert.True(beats.Complete(beat), $"{beat.Id} 应该能完成");
        }
    }

    private static void SaveFlags(FlagStore flags, SaveState state)
    {
        foreach (var kv in flags.SnapshotGlobal()) state.Flags[kv.Key] = kv.Value;
    }

    // ————————————————————————— 日推进 —————————————————————————

    [Fact]
    public void 完成第一拍_下一拍同日_日子不动()
    {
        var beats = Fresh(out var table, out _);

        Assert.Equal(1, beats.Day);
        Assert.True(beats.Complete(B(table, "b01")));

        // b02 也是第 1 天：没有道理烧完账就把人踢到第二天。
        Assert.Equal(1, beats.Day);
        Assert.Equal("b02", beats.Current?.Id);
    }

    [Fact]
    public void 完成第二拍_跳到下一拍的最早天()
    {
        var beats = Fresh(out var table, out _);

        beats.Complete(B(table, "b01"));
        beats.Complete(B(table, "b02"));

        // b03 在第 3 天。
        Assert.Equal(3, beats.Day);
        Assert.Equal("b03", beats.Current?.Id);
    }

    [Fact]
    public void 做完最后一拍_停在第30天()
    {
        var beats = Fresh(out _, out _);

        CompleteAll(beats);

        Assert.True(beats.AllDone);
        Assert.Equal(30, beats.Day);
        Assert.Null(beats.Current);
    }

    [Fact]
    public void 日子只往前走_不因为下一拍更早而后退()
    {
        // 第 20 天，b01 还没做（人家先到处逛）。做完 b01 不能把日子倒回第 1 天。
        var beats = Restored(Real(), 20);

        beats.Complete(beats.Table.Get("b01"));

        Assert.Equal(20, beats.Day);
    }

    // ————————————————————————— 选择型 —————————————————————————

    [Fact]
    public void 选第二项_只置pick1_不置pick0()
    {
        var beats = Fresh(out var table, out var flags);

        beats.Complete(B(table, "b01"));
        beats.Complete(B(table, "b02"));
        beats.Choose(B(table, "b03"), 0);      // 第一口锅（无选项文案，按第 0 项过）

        var b04 = B(table, "b04");
        Assert.Equal(7, beats.Day);
        Assert.True(beats.Machine.CanTrigger(b04));

        Assert.True(beats.Choose(b04, 1));

        Assert.True(flags.Has("beat.b04.pick.1", "hub3"));
        Assert.False(flags.Has("beat.b04.pick.0", "hub3"));
        Assert.False(flags.Has("beat.b04.pick.2", "hub3"));
        Assert.True(flags.Has("beat.b04.done", "hub3"));
    }

    [Fact]
    public void 没有选项的选择拍_确认后置pick0()
    {
        var beats = Fresh(out var table, out var flags);

        beats.Complete(B(table, "b01"));
        beats.Complete(B(table, "b02"));

        var b03 = B(table, "b03");
        Assert.Empty(b03.Options);

        Assert.True(beats.Choose(b03, 3));

        Assert.True(flags.Has("beat.b03.pick.0", "hub2"));
        Assert.False(flags.Has("beat.b03.pick.3", "hub2"));
    }

    [Fact]
    public void 选项旗标名是四段点号_过FlagStore校验()
    {
        var table = Real();
        var b11 = B(table, "b11");

        var flag = PrologueBeats.PickFlagOf(b11, 3);

        Assert.Equal("beat.b11.pick.3", flag);
        Assert.True(FlagStore.IsValidName(flag));
    }

    [Fact]
    public void 进空间只弹该弹的那一拍_做完就不再有()
    {
        var beats = Fresh(out var table, out _);

        Assert.Equal("b01", beats.PendingChoice("hub1")?.Id);
        Assert.Null(beats.PendingChoice("hub2"));      // b03 还没到前置

        beats.Complete(B(table, "b01"));

        Assert.Null(beats.PendingChoice("hub1"));      // 做完的拍不再 Available
        Assert.Equal("b02", beats.PendingKnock("hub1")?.Id);
    }

    // ————————————————————————— 敲门与空仓 —————————————————————————

    [Fact]
    public void 敲门能完成b02_门先敲了前置后补也能接上()
    {
        var table = Real();
        var flags = new FlagStore();
        var beats = PrologueBeats.FromState(table, flags, null);

        Assert.Null(beats.PendingKnock("hub1"));       // b01 没烧账，门敲了也不算

        // 门先敲了（旗标已置），账还没烧——这时候补记也不该动手。
        flags.Set(KnockTimer.HappenedFlag);
        Assert.Null(beats.PendingKnockLate("hub1"));

        beats.Complete(B(table, "b01"));

        Assert.Equal("b02", beats.PendingKnockLate("hub1")?.Id);
        Assert.True(beats.Complete(beats.PendingKnock("hub1")));

        Assert.True(beats.Machine.IsDone(B(table, "b02")));
        Assert.Null(beats.PendingKnock("hub1"));       // 只敲一次
    }

    [Fact]
    public void 空仓用既有的仓门点位_不是新造的点()
    {
        var beats = Fresh(out var table, out _);

        beats.Complete(B(table, "b01"));
        beats.Complete(B(table, "b02"));
        beats.Complete(B(table, "b03"));
        beats.Complete(B(table, "b04"));
        beats.Complete(B(table, "b05"));
        beats.Complete(B(table, "b06"));
        beats.Complete(B(table, "b07"));

        Assert.Equal(20, beats.Day);
        Assert.Equal("b08", beats.Current?.Id);

        Assert.Null(beats.PendingSearch("hub6", "h6_01"));   // 仓底不算
        Assert.Equal("b08", beats.PendingSearch("hub6", "h6_04")?.Id);
        Assert.Null(beats.PendingSearchFallback("hub6"));    // 有指定点位就不走退化
    }

    // ————————————————————————— HUD 与调试表 —————————————————————————

    [Fact]
    public void HUD三段拼装_天数空间该做()
    {
        var beats = Fresh(out var table, out _);

        beats.Complete(B(table, "b01"));
        beats.Complete(B(table, "b02"));

        Assert.Equal("第 3 天 · 废驿 · 该做：③ 第一口锅", beats.HudLine("废驿"));
    }

    [Fact]
    public void HUD当前拍在别处_末尾补一句该往哪走()
    {
        // 玩家站在城下营地（hub7），该做的是 hub1 的焚账——不说去哪，他就得自己把七个空间走一遍。
        var beats = Fresh(out _, out _);

        Assert.Equal("第 1 天 · 城下营地 · 该做：① 焚账（城南小院）", beats.HudLine("城下营地"));
    }

    [Fact]
    public void HUD当前拍就在脚下_不加括号()
    {
        var beats = Fresh(out _, out _);

        // 已经在城南小院：保持原来的三段式，不在旁边还指路。
        Assert.Equal("第 1 天 · 城南小院 · 该做：① 焚账", beats.HudLine("城南小院"));
        Assert.DoesNotContain("（", beats.HudLine("城南小院"));
    }

    [Fact]
    public void HUD全做完说完成_没得做说等一等()
    {
        var done = Fresh(out _, out _);
        CompleteAll(done);
        Assert.Equal("第 30 天 · 序章已完成", done.HudLine("废驿"));

        // 做到 b11、人还停在第 1 天：b12 没到日子，当前是空的，但也没全做完。
        var stuck = Restored(Real(), 1, "b01", "b02", "b03", "b04", "b05", "b06", "b07", "b08", "b09", "b10", "b11");
        Assert.Null(stuck.Current);
        Assert.False(stuck.AllDone);
        Assert.Equal("第 1 天 · 等一等", stuck.HudLine("废驿"));
    }

    [Fact]
    public void 调试表行数与四态说法()
    {
        var beats = Fresh(out _, out _);
        var lines = beats.DescribeTable();

        Assert.Equal(13, lines.Count);                          // 抬头 + 12 行
        Assert.Equal("[节拍] 共 12 拍，已完成 0 拍", lines[0]);
        Assert.Equal("[节拍] 1 b01 焚账 第1天 hub1 可触发 ← 当前", lines[1]);
        Assert.Equal("[节拍] 3 b03 第一口锅 第3天 hub2 锁定", lines[3]);

        var early = Restored(Real(), 1, "b01", "b02");
        var earlyLines = early.DescribeTable();
        Assert.Equal("[节拍] 3 b03 第一口锅 第3天 hub2 未到天数", earlyLines[3]);

        var done = Fresh(out _, out _);
        CompleteAll(done);
        var doneLines = done.DescribeTable();
        Assert.Equal("[节拍] 共 12 拍，已完成 12 拍", doneLines[0]);
        Assert.DoesNotContain("← 当前", string.Join("\n", doneLines));
        Assert.Equal("[节拍] 1 b01 焚账 第1天 hub1 已完成", doneLines[1]);
    }

    // ————————————————————————— 存档往返 —————————————————————————

    [Fact]
    public void 存档往返之后_Current还是同一拍()
    {
        var table = Real();
        var flags = new FlagStore();
        var beats = PrologueBeats.FromState(table, flags, null);

        beats.Complete(B(table, "b01"));
        beats.Complete(B(table, "b02"));

        var state = new SaveState();
        beats.Machine.ToState(state);        // 只写"第几天 + 做完哪几拍"
        SaveFlags(flags, state);             // 旗标走 SaveState.Flags，两边都写才是同一份事实

        Assert.Equal(3, state.Day);
        Assert.Equal(new[] { "b01", "b02" }, state.Prologue.BeatsDone.ToArray());

        var backFlags = new FlagStore();
        backFlags.Load(state);
        var back = PrologueBeats.FromState(table, backFlags, state);

        Assert.Equal(3, back.Day);
        Assert.Equal("b03", back.Current?.Id);
        Assert.True(back.Machine.IsDone(B(table, "b01")));
        Assert.True(backFlags.Has("beat.b02.done", "hub1"));
    }

    [Fact]
    public void 读档忘了灌旗标_十二拍会一起锁住()
    {
        // 反面教材：只把 beats_done 塞回去、旗标仓库空着。
        // 这条测试是在钉 S1-06 交接单里的第二个坑——接线时顺序写反就会变成这样。
        var table = Real();
        var state = new SaveState { Day = 3 };
        state.Prologue.BeatsDone.Add("b01");
        state.Prologue.BeatsDone.Add("b02");
        state.Flags["beat.b01.done"] = true;
        state.Flags["beat.b02.done"] = true;

        var empty = new FlagStore();
        var wrong = PrologueBeats.FromState(table, empty, state);

        Assert.Null(wrong.Current);
        Assert.Equal(BeatState.Locked, wrong.Machine.StateOf(B(table, "b03")));

        var right = new FlagStore();
        right.Load(state);
        var ok = PrologueBeats.FromState(table, right, state);

        Assert.Equal("b03", ok.Current?.Id);
    }
}
