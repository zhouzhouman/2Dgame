using System;
using System.Collections.Generic;
using System.IO;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 招兵与攻城接进游戏（票号 S2-03c）。规则在 Core（S2-03b 的 <see cref="SiegeBook"/> /
/// <see cref="RecruitTable"/>），这一层只盯界面与接线：
/// 三档各加几个兵、哪一档真的动了三轴、四法行怎么写、灰不灰、
/// 兵不够时那句"多半要撤"、赢了扣民望（钳 0）、输了损三成并置旗标、
/// 输赢都不拦着这一拍做完，以及 <c>--siege</c> 那三行。
///
/// 全部不起 Godot：面板只算字，画在 Main 里；输出格式钉在 <see cref="SiegeReport"/>，
/// 命令行与这里读的是同一份。
/// </summary>
public class GameSiegeTests
{
    // ————————————————————————— 夹具 —————————————————————————

    /// <summary>从测试输出目录往上找工程里的 Content/data（与其他 Game* 测试同一套找法）。</summary>
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

    private static SiegeTable RealSiege() => SiegeTable.Load(FindData("siege.csv"));

    private static RecruitTable RealRecruit() => RecruitTable.Load(FindData("recruit.csv"));

    private static TrustBook RealTrust(int guan = 0, int min = 0, int yi = 0) =>
        new(new TrustState { Guan = guan, Min = min, Yi = yi },
            TrustTable.Load(FindData("trust.csv")),
            RouteTable.Load(FindData("routes.csv")));

    private static FlagProbe 旗标(params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        return name => set.Contains(name);
    }

    /// <summary>四法在表里的下标（测试里按 id 取，不写死序号）。</summary>
    private static int 法(SiegeTable table, string id)
    {
        for (var i = 0; i < table.Count; i++)
            if (string.Equals(table.All[i].Id, id, StringComparison.Ordinal))
                return i;

        throw new InvalidOperationException($"siege.csv 里没有「{id}」");
    }

    private static SiegeScreen 面板(
        int troops = 60,
        TrustBook? trust = null,
        FlagProbe? flags = null,
        FlagStore? store = null) =>
        new(new SiegeBook(RealSiege()), trust ?? RealTrust(), troops, flags, store);

    // ————————————————————————— 招兵（b07） —————————————————————————

    [Fact]
    public void 招兵给粮_加六十人_并借施粥动三轴()
    {
        var table = RealRecruit();
        var trust = RealTrust();
        var troops = 0;

        var line = RecruitWiring.Apply(table, 0, ref troops, id => trust.Apply(id));

        Assert.Equal(60, troops);
        Assert.Equal("招到 60 人（现有 60）", line);

        // 给粮那一档的 trust 是 gruel（施粥）：官望 −1（0 已见底，钳在 0）、民望 +4、疑心 +2
        Assert.Equal(0, trust.State.Guan);
        Assert.Equal(4, trust.State.Min);
        Assert.Equal(2, trust.State.Yi);
    }

    [Fact]
    public void 招兵给承诺_同样加六十人_但一个轴都不动()
    {
        var table = RealRecruit();
        var trust = RealTrust();
        var troops = 0;
        var called = 0;

        var line = RecruitWiring.Apply(table, 1, ref troops, _ => called++);

        Assert.Equal(60, troops);
        Assert.Equal("招到 60 人（现有 60）", line);
        Assert.Equal(0, called);                       // trust 列是空的：不该去问三轴
        Assert.Equal(0, trust.State.Min);
        Assert.Equal(0, trust.State.Guan);
        Assert.Equal(0, trust.State.Yi);
    }

    [Fact]
    public void 招兵抢兵器_只加十五人_也不动三轴()
    {
        var table = RealRecruit();
        var trust = RealTrust();
        var troops = 0;

        var line = RecruitWiring.Apply(table, 2, ref troops, id => trust.Apply(id));

        Assert.Equal(15, troops);
        Assert.Equal("招到 15 人（现有 15）", line);
        Assert.Equal(0, trust.State.Min);
    }

    [Fact]
    public void 招兵表里没有那一档_兵不变_也不吭声()
    {
        var table = RealRecruit();
        var troops = 48;
        var called = false;

        var line = RecruitWiring.Apply(table, 9, ref troops, _ => called = true);

        Assert.Equal(48, troops);
        Assert.Equal("", line);
        Assert.False(called);
    }

    // ————————————————————————— 四法行 —————————————————————————

    [Fact]
    public void 四法行_名字耗时伤亡都照表写_顺序也照表()
    {
        var rows = 面板().Rows();

        Assert.Equal(4, rows.Count);
        Assert.Equal("围　耗 6 刻　伤亡 10%", rows[0].Label);
        Assert.Equal("攻　耗 3 刻　伤亡 35%", rows[1].Label);
        Assert.Equal("间（内应）　耗 4 刻　伤亡 5%", rows[2].Label);
        Assert.Equal("水/火　耗 5 刻　伤亡 20%", rows[3].Label);
    }

    [Fact]
    public void 不到门槛的三种法子灰着_右边说缺什么()
    {
        // 没断粮、官望 0、没地形：只有「攻」是无条件的
        var rows = 面板(troops: 60).Rows();

        Assert.False(rows[0].Usable);
        Assert.Equal("城里还没断粮", rows[0].Note);

        Assert.True(rows[1].Usable);

        Assert.False(rows[2].Usable);
        Assert.Equal("官望 0 / 需 60", rows[2].Note);

        Assert.False(rows[3].Usable);
        Assert.Equal("这儿的地形用不上水火", rows[3].Note);
    }

    [Fact]
    public void 官望够了_间就能选_兵够时不多话()
    {
        var rows = 面板(troops: 60, trust: RealTrust(guan: 60)).Rows();

        Assert.True(rows[2].Usable);
        Assert.False(rows[2].WillRetreat);      // 60 ≥ 30，门一开这点人就够
        Assert.Equal("", rows[2].Note);
    }

    [Fact]
    public void 兵不够打赢时_那一行仍能选_但行尾提醒多半要撤()
    {
        var rows = 面板(troops: 60).Rows();

        Assert.True(rows[1].Usable);
        Assert.True(rows[1].WillRetreat);
        Assert.Equal("（兵力 60 / 需 72，多半要撤）", rows[1].Note);
    }

    // ————————————————————————— 结算 —————————————————————————

    [Fact]
    public void 点用不上的那一法_一个兵都不动_面板把原因说出来()
    {
        var trust = RealTrust(min: 20);
        var screen = new SiegeScreen(new SiegeBook(RealSiege()), trust, 60);

        var result = screen.Confirm(法(RealSiege(), "surround"));

        Assert.False(result.Ok);
        Assert.False(result.Win);
        Assert.False(result.Retreat);
        Assert.Equal(60, screen.Troops);
        Assert.Equal("没让你打：城里还没断粮", screen.Message);
        Assert.Equal(20, trust.State.Min);      // 没打成，民望也不该被扣
    }

    [Fact]
    public void 赢了_按伤亡扣兵_并按民怨扣民望()
    {
        var trust = RealTrust(min: 20);
        var screen = new SiegeScreen(new SiegeBook(RealSiege()), trust, 80);

        var result = screen.Confirm(法(RealSiege(), "assault"));

        Assert.True(result.Win);
        Assert.Equal(52, screen.Troops);                         // 80 × 35% 伤亡 = 28
        Assert.Equal("城开了。折损 28 人，余 52 人。", screen.Message);
        Assert.Equal(12, trust.State.Min);                       // 民怨 +8 记在民望上
    }

    [Fact]
    public void 赢了_民望见底时钳在零_不会扣成负数()
    {
        var trust = RealTrust(min: 3);
        var screen = new SiegeScreen(
            new SiegeBook(RealSiege()), trust, 80, 旗标(SiegeBook.TerrainRiverFlag));

        var result = screen.Confirm(法(RealSiege(), "burn"));

        Assert.True(result.Win);
        Assert.Equal(0, trust.State.Min);       // 水/火民怨 +30，3 − 30 应钳在 0
        Assert.Equal(64, screen.Troops);        // 80 × 20% 伤亡 = 16
    }

    [Fact]
    public void 输了_损三成_余兵对_并置siege旗标()
    {
        var flags = new FlagStore();
        var screen = 面板(troops: 60, store: flags);

        var result = screen.Confirm(法(RealSiege(), "assault"));

        Assert.True(result.Retreat);
        Assert.False(result.Win);
        Assert.Equal(42, screen.Troops);                         // ceil(60 × 30%) = 18
        Assert.Equal("没打开。退回营地，折损 30%，余 42 人。", screen.Message);
        Assert.Equal(SiegeBook.FailedFlag, result.Flag);
        Assert.True(flags.Has(SiegeBook.FailedFlag, null));
    }

    [Fact]
    public void 输了_不动民望_也不扣天命()
    {
        var trust = RealTrust(min: 30, yi: 10);
        var screen = new SiegeScreen(new SiegeBook(RealSiege()), trust, 60);

        var result = screen.Confirm(法(RealSiege(), "assault"));

        Assert.True(result.Retreat);
        Assert.Equal(30, trust.State.Min);
        Assert.Equal(10, trust.State.Yi);
        Assert.Equal(0, result.Minyuan);
        Assert.Equal(0, result.Tianming);
    }

    // ————————————————————————— 这一拍算不算做完 —————————————————————————

    [Fact]
    public void 撤退也照样记上这一拍_之后能接着做b12()
    {
        var table = BeatTable.Load(FindData("beats.csv"));
        var flags = new FlagStore();
        var beats = PrologueBeats.FromState(table, flags, null);

        // 前九拍按表序做完（b01–b10），停在攻城那一拍
        for (var i = 0; i < 10; i++) Assert.True(beats.Complete(table.All[i]));

        var siegeBeat = table.Get("b11")!;
        Assert.True(beats.Machine.CanTrigger(siegeBeat));

        // 60 兵硬攻：输，撤退——但"输"不是"没做完"（§16.5）
        var screen = 面板(troops: 60, store: flags);
        var index = 法(RealSiege(), "assault");
        var result = screen.Confirm(index);

        Assert.True(result.Retreat);
        Assert.True(beats.Choose(siegeBeat, index));
        Assert.True(beats.Machine.IsDone(siegeBeat));
        Assert.True(flags.Has(PrologueBeats.PickFlagOf(siegeBeat, index), null));

        // 下一拍（b12）的前置就满足了
        Assert.True(beats.Machine.CanTrigger(table.Get("b12")!));
    }

    [Fact]
    public void 第一条能用的法子_跳过点不动的围()
    {
        var screen = 面板(troops: 60);

        Assert.False(screen.CanUse(法(RealSiege(), "surround")));
        Assert.Equal(法(RealSiege(), "assault"), screen.FirstUsable());
    }

    // ————————————————————————— 兵力看得见 —————————————————————————

    [Fact]
    public void 兵力那一行_信任面板底与攻城抬头是同一句()
    {
        Assert.Equal("兵力 60", SiegeWiring.TroopLine(60));
        Assert.Equal("⑪　攻城　·　兵力 60", SiegeWiring.SiegeHead("⑪", "攻城", 60));
    }

    // ————————————————————————— --siege —————————————————————————

    [Fact]
    public void siege自检_三行说清选不了_打了要撤_打得下()
    {
        var lines = SiegeDemo.Run(RealSiege(), TrustTable.Load(FindData("trust.csv")),
            RouteTable.Load(FindData("routes.csv")));

        Assert.Equal(3, lines.Count);

        Assert.Equal(
            "[攻] 兵力 60｜围 不可用（城里还没断粮）｜攻 可打但多半撤（需 72）｜间 不可用（官望 0/60）｜水火 不可用（地形）",
            lines[0]);

        Assert.Equal("[攻] 兵力 60 硬攻 → 撤退，损 18，余 42，置 siege.battle.failed", lines[1]);
        Assert.Equal("[攻] 兵力 80 攻 → 城开了，损 28，余 52", lines[2]);
    }
}
