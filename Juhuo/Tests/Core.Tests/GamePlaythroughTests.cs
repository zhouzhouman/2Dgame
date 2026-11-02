using System;
using System.Collections.Generic;
using System.IO;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 序章通关跑批（票号 S1-09）。守四件事：
/// ① 十二拍能按表序走完——每一拍当场可触发，最后 12/12、第 30 天；
/// ② 某一拍失败就停在那拍、说清拍号，后面的拍一次都不再叫；
/// ③ 用刻汇总对 ClockTable 预算的超支计算（0 / 1 / 多个空间）；
/// ④ <c>--pick=2</c> 落在 <c>beat.&lt;id&gt;.pick.2</c>，越界夹回合法的两端。
///
/// 这里不碰 Godot：跑批驱动是 Application 层的纯函数，"按 E / 走出口"由 Game 那一层注入。
/// 真实入口能不能走通，归 <c>--playthrough</c> 与手测。
/// </summary>
public class GamePlaythroughTests
{
    // ————————————————————————— 夹具 —————————————————————————

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 GameBeatsTests 同一套找法）。</summary>
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

    private static PrologueBeats Fresh() => PrologueBeats.FromState(Real(), new FlagStore(), null);

    /// <summary>真的按表序做掉每一拍：前置不足或完成失败都当成失败原因返回。</summary>
    private static string? DoIt(PrologueBeats beats, Beat beat)
    {
        if (!beats.Machine.CanTrigger(beat)) return $"{beat.Id} 这时不可触发";
        return beats.Complete(beat) ? null : $"{beat.Id} 完成失败";
    }

    // ————————————————————————— 顺序与失败 —————————————————————————

    [Fact]
    public void 十二拍按表序走完_报12比12_停在第30天()
    {
        var beats = Fresh();
        var table = beats.Table;
        var calls = new List<string>();

        var result = ProloguePlaythrough.Run(beats, beat =>
        {
            calls.Add(beat.Id);
            return DoIt(beats, beat);
        });

        Assert.True(result.Ok);
        Assert.Equal(12, result.Total);
        Assert.Equal(12, result.Done);
        Assert.Equal(30, result.Day);
        Assert.True(beats.AllDone);

        // 顺序 = beats.csv 的行序，一个不少、一个不重。
        Assert.Equal(12, calls.Count);
        for (var i = 0; i < table.Count; i++) Assert.Equal(table.All[i].Id, calls[i]);
        Assert.Equal("b12", calls[^1]);
    }

    [Fact]
    public void 每一步记的是做那件事时的那一天_不是被推到下一天之后()
    {
        var beats = Fresh();

        var result = ProloguePlaythrough.Run(beats, beat => DoIt(beats, beat));

        // b02 是在第 1 天敲的门；完成后日历才推到 b03 的第 3 天。
        Assert.Equal(1, result.Steps[1].Day);
        Assert.Equal("b02", result.Steps[1].BeatId);
        Assert.Equal(3, result.Steps[2].Day);
        Assert.Equal(30, result.Steps[^1].Day);
        Assert.Equal(30, result.Day);
    }

    [Fact]
    public void 某一拍失败_停在那一拍并说清拍号()
    {
        var beats = Fresh();
        var calls = new List<string>();

        var result = ProloguePlaythrough.Run(beats, beat =>
        {
            calls.Add(beat.Id);
            if (beat.Id == "b04") return "选项面板没有弹出来";
            return DoIt(beats, beat);
        });

        Assert.False(result.Ok);
        Assert.Equal(4, result.FailedNo);
        Assert.Equal("b04", result.FailedBeat!.Id);
        Assert.Equal("选项面板没有弹出来", result.Failure);

        // 停之前走完三拍；失败那一拍不上榜。
        Assert.Equal(3, result.Done);
        Assert.Equal(3, result.Steps.Count);

        // 失败之后一次都不再叫：后面的拍前置不满足，硬跑只会把真原因埋掉。
        Assert.Equal(4, calls.Count);
        Assert.DoesNotContain("b05", calls);
    }

    [Fact]
    public void 失败时结论行指出第几拍和为什么()
    {
        var beats = Fresh();

        var result = ProloguePlaythrough.Run(beats, beat =>
            beat.Id == "b08" ? "搜不到点位 h6_04" : DoIt(beats, beat));

        Assert.Equal(
            "[通关] 结论：不能通关（第 8 拍 b08 空仓：搜不到点位 h6_04）",
            ProloguePlaythrough.ConclusionLine(result));
    }

    // ————————————————————————— 用刻与预算 —————————————————————————

    [Fact]
    public void 用刻汇总_没有空间超支()
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["hub1"] = 5,
            ["hub2"] = 2,
            ["hub6"] = 3,
        };

        var budget = ProloguePlaythrough.Sum(used);

        Assert.Equal(10, budget.TotalUsed);
        Assert.Equal(ClockTable.TotalBudgetKe, budget.TotalBudget);
        Assert.Equal(0, budget.OverHubs);
        Assert.Equal(7, budget.PerHub.Count);
        Assert.Equal("hub1 5/8", budget.PerHub[0]);
        Assert.Equal("hub6 3/14", budget.PerHub[5]);
        Assert.Equal("hub7 0/8", budget.PerHub[6]);
    }

    [Fact]
    public void 用刻汇总_一个空间超支()
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal) { ["hub3"] = 17 };

        var budget = ProloguePlaythrough.Sum(used);

        Assert.Equal(1, budget.OverHubs);
        Assert.Equal(17, budget.TotalUsed);
        Assert.Equal("hub3 17/16", budget.PerHub[2]);
    }

    [Fact]
    public void 用刻汇总_多个空间超支()
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["hub1"] = 9,      // 8 → 超 1
            ["hub4"] = 20,     // 刚好用光，不算超
            ["hub7"] = 10,     // 8 → 超 2
        };

        var budget = ProloguePlaythrough.Sum(used);

        Assert.Equal(2, budget.OverHubs);
        Assert.Equal(39, budget.TotalUsed);
        Assert.Contains("hub4 20/20", budget.PerHub);
    }

    [Fact]
    public void 合计行与分空间行的说法()
    {
        var beats = Fresh();
        var result = ProloguePlaythrough.Run(beats, beat => DoIt(beats, beat));
        var budget = ProloguePlaythrough.Sum(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["hub1"] = 5,
            ["hub2"] = 2,
        });

        Assert.Equal(
            "[通关] 合计：12/12 拍，第 30 天，用刻 7（预算 88，超支 0 空间）",
            ProloguePlaythrough.TotalLine(result, budget));

        Assert.StartsWith("[通关] 分空间：hub1 5/8  hub2 2/10", ProloguePlaythrough.HubLine(budget));
        Assert.EndsWith("（实际/预算）", ProloguePlaythrough.HubLine(budget));
        Assert.Equal("[通关] 结论：可以通关", ProloguePlaythrough.ConclusionLine(result));
    }

    // ————————————————————————— 选项与行格式 —————————————————————————

    [Fact]
    public void pick2时选项落在beat_id_pick_2()
    {
        var options = PlaythroughOptions.Parse(new[] { "--playthrough", "--pick=2" });
        var table = Real();
        var b11 = table.Get("b11")!;      // 围|攻|间|水火，四项

        var index = PrologueBeats.NormalizePick(b11, options.Pick);
        var flag = PrologueBeats.PickFlagOf(b11, index);

        Assert.Equal(2, options.Pick);
        Assert.Equal(2, index);
        Assert.Equal("beat.b11.pick.2", flag);
        Assert.True(FlagStore.IsValidName(flag));

        // 真的选进去，旗标也只有这一格。
        var beats = Fresh();
        beats.Complete(table.Get("b01"));
        beats.Complete(table.Get("b02"));
        beats.Complete(table.Get("b03"));
        beats.Complete(table.Get("b04"));
        beats.Complete(table.Get("b05"));
        beats.Complete(table.Get("b06"));
        beats.Complete(table.Get("b07"));
        beats.Complete(table.Get("b08"));
        beats.Complete(table.Get("b09"));
        beats.Complete(table.Get("b10"));

        Assert.True(beats.Choose(b11, index));
        Assert.True(beats.Machine.IsDone(b11));
    }

    [Fact]
    public void pick越界_夹回合法的两端()
    {
        var table = Real();
        var b11 = table.Get("b11")!;      // 4 项
        var b03 = table.Get("b03")!;      // 没有选项文案

        Assert.Equal(0, PlaythroughOptions.Parse(null).Pick);
        Assert.Equal(0, PlaythroughOptions.Parse(new[] { "--pick=-3" }).Pick);
        Assert.Equal(9, PlaythroughOptions.Parse(new[] { "--pick=9" }).Pick);

        // 选得再大也只落在最后一项；没有选项一律 0。
        Assert.Equal(3, PrologueBeats.NormalizePick(b11, 9));
        Assert.Equal("beat.b11.pick.3", PrologueBeats.PickFlagOf(b11, 9));
        Assert.Equal("beat.b03.pick.0", PrologueBeats.PickFlagOf(b03, 9));
    }

    [Fact]
    public void 每拍那一行_中英混排按显示宽度对齐()
    {
        var step = new PlaythroughStep(1, "b01", "焚账", "hub1", 1, 3, 8);
        var other = new PlaythroughStep(2, "b02", "敲门的人", "hub1", 1, 5, 8);

        Assert.Equal("[通关] 1/12 b01 焚账        hub1 第1天 刻 3/8",
            ProloguePlaythrough.StepLine(step, 12));
        Assert.Equal("[通关] 2/12 b02 敲门的人    hub1 第1天 刻 5/8",
            ProloguePlaythrough.StepLine(other, 12));

        // 两行里 hub 落在同一列：名字那一列宽 12 格，汉字算两格。
        // 注意要比的是"显示宽度"，不是字符下标——汉字在字符串里只占一个 char。
        Assert.Equal(
            ColumnOfHub(ProloguePlaythrough.StepLine(step, 12)),
            ColumnOfHub(ProloguePlaythrough.StepLine(other, 12)));
    }

    [Fact]
    public void 搜索型拍没有指定点位时_跑批能说清楚()
    {
        // S1-06c 把 b08 钉在 h6_04 上；这份对应关系只存一份，驱动按它找点位。
        Assert.Equal("h6_04", PrologueBeats.SearchPointOf("b08"));
        Assert.Null(PrologueBeats.SearchPointOf("b01"));
        Assert.Null(PrologueBeats.SearchPointOf(null));
    }

    /// <summary>hub 那一列在第几格（按显示宽度算，汉字两格）。</summary>
    private static int ColumnOfHub(string line)
    {
        var i = line.IndexOf("hub1", StringComparison.Ordinal);
        Assert.True(i > 0, $"这一行里没有 hub：{line}");
        return ProloguePlaythrough.DisplayWidth(line[..i]);
    }
}
