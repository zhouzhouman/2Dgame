using System;
using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 十二节拍状态机（票号 S1-06）。守三件事：
/// ① beats.csv 十二行能一字不差读进来、顺序就是剧情顺序；
/// ② 四态判定不合并——早到是 TooEarly、不能跳是 Locked、重复完成是幂等；
/// ③ 进度能进存档、能读回来，v2 旧档读完等于"节拍一件没做"。
///
/// 节拍**不锁空间**：状态机压根不认识"玩家在哪"，只有表里的 hub 决定前置旗标查哪个作用域。
/// </summary>
public class BeatMachineTests
{
    /// <summary>三条小表：够摆出 Done / Available / Locked / TooEarly 四态。</summary>
    private const string MiniCsv =
        "id,name,hub,day,requires,sets,trigger,note\n" +
        "t1,甲,hub1,1,,beat.t1.done,choice,第一件\n" +
        "t2,乙,hub2,3,beat.t1.done,beat.t2.done,knock,第二件\n" +
        "t3,丙,hub3,30,beat.t2.done,\"beat.t3.done,beat.t3.extra\",search,第三件\n";

    private const string TwelveIds =
        "b01,b02,b03,b04,b05,b06,b07,b08,b09,b10,b11,b12";

    private static BeatTable Mini() => BeatTable.FromCsv(MiniCsv);

    private static BeatMachine Machine(string csv, int day = 1, FlagStore? flags = null) =>
        new(BeatTable.FromCsv(csv), new Calendar(day), flags ?? new FlagStore());

    private static Beat T(BeatTable table, string id) =>
        table.Get(id) ?? throw new InvalidOperationException($"表里没有 {id}");

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 ReachAuditTests 同一套找法）。</summary>
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

        throw new DirectoryNotFoundException($"从 {AppContext.BaseDirectory} 往上找不到 Content/data/beats.csv");
    }

    // ————————————————————————— 读表 —————————————————————————

    [Fact]
    public void 真表十二拍一条不少且顺序就是剧情顺序()
    {
        var table = BeatTable.Load(BeatsPath());

        Assert.Equal(12, table.Count);
        Assert.Equal(TwelveIds.Split(','), table.All.Select(b => b.Id));
        Assert.Equal("焚账", table.All[0].Name);
        Assert.Equal("hub1", table.All[0].Hub);
        Assert.Equal(1, table.All[0].Day);
        Assert.Equal("hub7", table.All[11].Hub);
        Assert.Equal(30, table.All[11].Day);

        // 每拍至少置一个旗标，且置的是"这一拍做完了"（前缀归数据表定，这里不写死）。
        Assert.All(table.All, b =>
        {
            Assert.Single(b.Sets);
            Assert.EndsWith(b.Id + ".done", b.Sets[0]);
        });
    }

    [Fact]
    public void 表头带BOM也能读()
    {
        var table = BeatTable.FromCsv("\uFEFF" + MiniCsv);

        Assert.Equal(3, table.Count);
        Assert.Equal("t1", table.All[0].Id);
    }

    [Fact]
    public void 缺列抛错并点出行号与列名()
    {
        const string csv = "id,name,hub,requires,sets,trigger,note\nb01,焚账,hub1,,beat.b01.done,choice,备注\n";

        var e = Assert.Throws<InvalidDataException>(() => BeatTable.FromCsv(csv));

        Assert.Contains("第 1 行", e.Message);
        Assert.Contains("day", e.Message);
    }

    [Fact]
    public void 空id抛错并点出行号()
    {
        const string csv =
            "id,name,hub,day,requires,sets,trigger,note\n" +
            "b01,焚账,hub1,1,,beat.b01.done,choice,备注\n" +
            ",敲门的人,hub1,1,beat.b01.done,beat.b02.done,knock,旧部\n";

        var e = Assert.Throws<InvalidDataException>(() => BeatTable.FromCsv(csv));

        Assert.Contains("第 3 行", e.Message);
        Assert.Contains("id", e.Message);
    }

    [Fact]
    public void 重复id抛错并点出行号()
    {
        const string csv =
            "id,name,hub,day,requires,sets,trigger,note\n" +
            "b01,焚账,hub1,1,,beat.b01.done,choice,备注\n" +
            "b01,又来一遍,hub2,3,,beat.b01.done,choice,重复\n";

        var e = Assert.Throws<InvalidDataException>(() => BeatTable.FromCsv(csv));

        Assert.Contains("第 3 行", e.Message);
        Assert.Contains("b01", e.Message);
    }

    [Fact]
    public void day不是整数抛错()
    {
        const string csv = "id,name,hub,day,requires,sets,trigger,note\nb01,焚账,hub1,初一,,beat.b01.done,choice,备注\n";

        var e = Assert.Throws<InvalidDataException>(() => BeatTable.FromCsv(csv));

        Assert.Contains("第 2 行", e.Message);
        Assert.Contains("day", e.Message);
    }

    [Fact]
    public void 按id取拍与按空间取拍()
    {
        var table = BeatTable.Load(BeatsPath());

        Assert.Equal("雪前", table.Get("b10")?.Name);
        Assert.Null(table.Get("b99"));
        Assert.Null(table.Get(null));

        Assert.Equal(new[] { "b05", "b06" }, table.ForHub("hub4").Select(b => b.Id));
        Assert.Equal(new[] { "b10", "b11", "b12" }, table.ForHub("hub7").Select(b => b.Id));
        Assert.Empty(table.ForHub("hub9"));
    }

    // ————————————————————————— 四态 —————————————————————————

    [Fact]
    public void 前置不满足是Locked()
    {
        var table = Mini();
        var m = Machine(MiniCsv, day: 3);

        Assert.Equal(BeatState.Locked, m.StateOf(T(table, "t2")));
        Assert.False(m.CanTrigger(T(table, "t2")));
    }

    [Fact]
    public void 前置满足且日子到了是Available()
    {
        var table = Mini();
        var m = Machine(MiniCsv, day: 1);

        Assert.Equal(BeatState.Available, m.StateOf(T(table, "t1")));
        Assert.True(m.CanTrigger(T(table, "t1")));
        Assert.Equal("t1", m.Current?.Id);
    }

    [Fact]
    public void 前置满足但日子没到是TooEarly()
    {
        var table = Mini();
        var m = Machine(MiniCsv, day: 1);

        Assert.True(m.Complete(T(table, "t1")));

        // t2 要到第 3 天，现在第 1 天：前置已经满足 → TooEarly，不是 Locked。
        Assert.Equal(BeatState.TooEarly, m.StateOf(T(table, "t2")));
        Assert.False(m.CanTrigger(T(table, "t2")));
        Assert.Null(m.Current);
    }

    [Fact]
    public void 做完之后是Done()
    {
        var table = Mini();
        var m = Machine(MiniCsv, day: 1);

        Assert.True(m.Complete(T(table, "t1")));

        Assert.Equal(BeatState.Done, m.StateOf(T(table, "t1")));
        Assert.True(m.IsDone(T(table, "t1")));
        Assert.False(m.CanTrigger(T(table, "t1")));
    }

    [Fact]
    public void 前置不满足压过日子没到()
    {
        // 第 1 天看 t3（第 30 天、要 t2）：两样都不满足时，报 Locked 而不是 TooEarly，
        // 免得 UI 说"日子没到"，让玩家白等 29 天。
        var table = Mini();
        var m = Machine(MiniCsv, day: 1);

        Assert.Equal(BeatState.Locked, m.StateOf(T(table, "t3")));
    }

    // ————————————————————————— 完成语义 —————————————————————————

    [Fact]
    public void 早到不能完成也不置旗标()
    {
        var table = Mini();
        var flags = new FlagStore();
        var m = Machine(MiniCsv, day: 1, flags);

        m.Complete(T(table, "t1"));

        Assert.False(m.Complete(T(table, "t2")));
        Assert.Equal(BeatState.TooEarly, m.StateOf(T(table, "t2")));
        Assert.False(flags.Has("beat.t2.done", "hub2"));
        Assert.Equal(1, m.DoneCount);
    }

    [Fact]
    public void 前置没做完不能跳()
    {
        var table = Mini();
        var flags = new FlagStore();
        var m = Machine(MiniCsv, day: 30, flags);

        Assert.False(m.Complete(T(table, "t3")));
        Assert.False(flags.Has("beat.t3.done", "hub3"));
        Assert.Equal(0, m.DoneCount);
    }

    [Fact]
    public void 重复完成返回false且旗标计数不变()
    {
        var table = Mini();
        var flags = new FlagStore();
        var m = Machine(MiniCsv, day: 1, flags);

        Assert.True(m.Complete(T(table, "t1")));
        var afterFirst = flags.GlobalCount;

        Assert.False(m.Complete(T(table, "t1")));
        Assert.Equal(afterFirst, flags.GlobalCount);
        Assert.Equal(1, m.DoneCount);
    }

    [Fact]
    public void sets多值用逗号分隔也能全置上()
    {
        var table = Mini();
        var flags = new FlagStore();
        var m = Machine(MiniCsv, day: 1, flags);

        m.Complete(T(table, "t1"));
        var day = 1;
        while (day < 3) day = m.AdvanceDay();
        m.Complete(T(table, "t2"));
        while (day < 30) day = m.AdvanceDay();

        Assert.True(m.Complete(T(table, "t3")));
        Assert.True(flags.Has("beat.t3.done", "hub3"));
        Assert.True(flags.Has("beat.t3.extra", "hub3"));
    }

    [Fact]
    public void 十二条按顺序做完后当前节拍为空()
    {
        var table = BeatTable.Load(BeatsPath());
        var flags = new FlagStore();
        var m = new BeatMachine(table, new Calendar(1), flags);

        var day = 1;
        for (var i = 0; i < table.Count; i++)
        {
            var want = table.All[i];

            while (day < want.Day) day = m.AdvanceDay();

            Assert.Equal(want.Id, m.Current?.Id);
            Assert.True(m.Complete(want), $"第 {i + 1} 拍「{want.Name}」应该能做");
            Assert.True(flags.Has(want.Sets[0], want.Hub));
        }

        Assert.Null(m.Current);
        Assert.Equal(12, m.DoneCount);
        Assert.Equal(12, flags.GlobalCount);
        Assert.Equal(30, day);
    }

    // ————————————————————————— 日历 —————————————————————————

    [Fact]
    public void 走一天返回新的第几天()
    {
        var m = Machine(MiniCsv, day: 1);

        Assert.Equal(2, m.AdvanceDay());
        Assert.Equal(3, m.AdvanceDay());
    }

    [Fact]
    public void 日历到第三十天封顶不抛异常()
    {
        var m = Machine(MiniCsv, day: 29);

        Assert.Equal(30, m.AdvanceDay());
        Assert.Equal(30, m.AdvanceDay());
        Assert.Equal(30, m.AdvanceDay());
    }

    // ————————————————————————— 进度与存档 —————————————————————————

    [Fact]
    public void 进度里有计数与当前拍名()
    {
        var table = Mini();
        var m = Machine(MiniCsv, day: 3);

        var before = m.Progress;
        Assert.Equal(0, before.DoneCount);
        Assert.Equal(3, before.TotalCount);
        Assert.Equal("t1", before.CurrentId);
        Assert.Equal("甲", before.CurrentName);

        m.Complete(T(table, "t1"));

        var after = m.Progress;
        Assert.Equal(1, after.DoneCount);
        Assert.Equal("t2", after.CurrentId);
        Assert.Equal("乙", after.CurrentName);
    }

    [Fact]
    public void 全部做完后进度里的当前拍是空的()
    {
        var table = Mini();
        var m = Machine(MiniCsv, day: 30);

        m.Complete(T(table, "t1"));
        m.Complete(T(table, "t2"));
        m.Complete(T(table, "t3"));

        var p = m.Progress;
        Assert.Equal(3, p.DoneCount);
        Assert.Null(p.CurrentId);
        Assert.Null(p.CurrentName);
    }

    [Fact]
    public void 存档往返后节拍与天数都对得上()
    {
        var table = Mini();
        var flags = new FlagStore();
        var m = Machine(MiniCsv, day: 3, flags);
        m.Complete(T(table, "t1"));

        var save = new SaveState();
        m.ToState(save);
        // 旗标由管 FlagStore 的那一层写进存档（ToState 只管天数与节拍）。
        foreach (var kv in flags.SnapshotGlobal()) save.Flags[kv.Key] = kv.Value;

        Assert.Equal(3, save.Day);
        Assert.Equal(new[] { "t1" }, save.Prologue.BeatsDone);

        var json = save.ToJson();
        Assert.Contains("\"beats_done\"", json);

        var back = SaveState.FromJson(json);

        var flags2 = new FlagStore();
        flags2.Load(back);
        var m2 = BeatMachine.FromState(table, flags2, back);

        Assert.Equal(BeatState.Done, m2.StateOf(T(table, "t1")));
        Assert.Equal("t2", m2.Current?.Id);
        Assert.Equal(1, m2.DoneCount);
        Assert.Equal(3, back.Day);
    }

    [Fact]
    public void 第二版旧档读进来等于节拍全没做()
    {
        var v2 = SaveState.FromJson("{\"version\":2,\"day\":7,\"flags\":{\"beat.b01.done\":true}}");

        Assert.Equal(3, v2.Version);
        Assert.False(v2.NeedsMigration);
        Assert.NotNull(v2.Prologue);
        Assert.Empty(v2.Prologue.BeatsDone);

        // 旗标还在，但"做过哪几拍"丢了：旧档等于从第一拍重来，这是可接受的降级。
        var table = BeatTable.Load(BeatsPath());
        var flags = new FlagStore();
        flags.Load(v2);
        var m = BeatMachine.FromState(table, flags, v2);

        Assert.Equal(0, m.DoneCount);
        Assert.Equal("b01", m.Current?.Id);
    }

    [Fact]
    public void 存档里不认识的节拍id会被忽略()
    {
        var table = Mini();
        var save = SaveState.FromJson(
            "{\"version\":3,\"day\":3,\"flags\":{\"beat.t1.done\":true},\"prologue\":{\"beats_done\":[\"t1\",\"t99\"]}}");

        var flags = new FlagStore();
        flags.Load(save);

        var m = BeatMachine.FromState(table, flags, save);

        Assert.Equal(1, m.DoneCount);
        Assert.Equal("t2", m.Current?.Id);
    }

    [Fact]
    public void 前置写成has壳是非法语法只关门不崩()
    {
        // 全工程只有一种文法：裸旗标名 + & | ! + 括号（S1-06b 裁定）。
        // 坏表达式不抛异常，只是这一拍永远推不动——坏数据关门，不砸游戏。
        const string csv =
            "id,name,hub,day,requires,sets,trigger,note\n" +
            "v1,甲,hub1,1,has(beat.v1.done),beat.v1.done,choice,一\n" +
            "v2,乙,hub2,1,beat.v1.done,beat.v2.done,choice,二\n";

        var table = BeatTable.FromCsv(csv);
        var m = new BeatMachine(table, new Calendar(30), new FlagStore());

        var bad = T(table, "v1");

        Assert.Equal(BeatState.Locked, m.StateOf(bad));
        Assert.False(m.CanTrigger(bad));
        Assert.False(m.Complete(bad));
        Assert.Null(m.Current);

        // 后面那拍被前一拍卡住，也推不动：跳不过去。
        Assert.Equal(BeatState.Locked, m.StateOf(T(table, "v2")));
    }

    [Fact]
    public void sets里旗标名不合法加载时就炸并点出行号()
    {
        // FlagStore.Set 对非法名是静默忽略的，节拍会"永远完不成"；所以契约在加载时就炸出来。
        const string csv =
            "id,name,hub,day,requires,sets,trigger,note\n" +
            "b01,焚账,hub1,1,,beat.b01.done,choice,备注\n" +
            "b02,敲门的人,hub1,1,beat.b01.done,b02.done,knock,旧部\n";

        var e = Assert.Throws<InvalidDataException>(() => BeatTable.FromCsv(csv));

        Assert.Contains("第 3 行", e.Message);
        Assert.Contains("b02.done", e.Message);
    }

    [Fact]
    public void sets多值时逐个校验不会漏掉后一个()
    {
        const string csv =
            "id,name,hub,day,requires,sets,trigger,note\n" +
            "b01,焚账,hub1,1,,\"beat.b01.done,b01.alt\",choice,备注\n";

        var e = Assert.Throws<InvalidDataException>(() => BeatTable.FromCsv(csv));

        Assert.Contains("第 2 行", e.Message);
        Assert.Contains("b01.alt", e.Message);
    }

    [Fact]
    public void 没有存档也能起一台新机器()
    {
        var m = BeatMachine.FromState(Mini(), new FlagStore(), null);

        Assert.Equal(0, m.DoneCount);
        Assert.Equal("t1", m.Current?.Id);
    }
}
