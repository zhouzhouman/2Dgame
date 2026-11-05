using System;
using System.Collections.Generic;
using System.IO;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 攻城规则层（S2-03b）：四法读表、门槛判定、输赢结算、招兵选项，以及兵力进存档。
///
/// 用内联 CSV 而不是真表：规则层测的是"判定与算术"，真表只补一条冒烟式读取
/// （文件没落盘就跳过，不算红）。
/// </summary>
public class SiegeTests
{
    private const string MiniSiege =
        "id,label,ke,casualty,minyuan,tianming,needs,troops_needed,note\n" +
        "wei,围,6,10,20,-5,flag:city_hungry,48,长围\n" +
        "gong,攻,3,30,8,0,,72,强攻\n" +
        "jian,间,4,2,0,3,trust:guan>=60,30,内应\n" +
        "shuihuo,水火,5,18,30,-15,troops>=80,54,决堤\n";

    private const string MiniRecruit =
        "option,troops,trust,note\n" +
        "0,15,recruit_grain,给粮\n" +
        "1,15,recruit_promise,给承诺\n" +
        "2,-3,,抢兵器\n";

    private static SiegeTable 表() => SiegeTable.FromCsv(MiniSiege);

    private static SiegeMethod 法(string id)
    {
        Assert.True(表().TryGet(id, out var method), $"内联表里没有「{id}」");
        return method!;
    }

    /// <summary>信任账本：门槛只看信任轴，路线表用一条内联的最小表占位。</summary>
    private static TrustBook 账(int guan = 0, int min = 0, int yi = 0)
    {
        var trust = TrustTable.FromCsv(
            "id,label,guan,min,yi,note\n" +
            "bribe,行贿,3,-2,2,\n");

        var routes = RouteTable.FromCsv(
            "id,label,axis,threshold,note\n" +
            "inside,内应,guan,60,\n" +
            "revolt,民变,min,60,\n");

        return new TrustBook(new TrustState { Guan = guan, Min = min, Yi = yi }, trust, routes);
    }

    private static FlagProbe 旗标(params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        return name => set.Contains(name);
    }

    private static string[] Ids(IReadOnlyList<SiegeMethod> methods)
    {
        var ids = new string[methods.Count];
        for (var i = 0; i < methods.Count; i++) ids[i] = methods[i].Id;

        return ids;
    }

    // ————————————————————————— 读表 —————————————————————————

    [Fact]
    public void 四法读得出来且字段对得上()
    {
        var table = 表();

        Assert.Equal(4, table.Count);
        Assert.Equal(new[] { "wei", "gong", "jian", "shuihuo" }, Ids(table.All));

        Assert.True(table.TryGet("jian", out var jian));
        Assert.Equal("间", jian!.Label);
        Assert.Equal(4, jian.Ke);
        Assert.Equal(2, jian.Casualty);
        Assert.Equal(0, jian.Minyuan);
        Assert.Equal(3, jian.Tianming);
        Assert.Equal("内应", jian.Note);
        Assert.Equal(SiegeNeedKind.Trust, jian.Needs.Kind);
        Assert.Equal("guan", jian.Needs.Key);
        Assert.Equal(60, jian.Needs.Value);
        Assert.Equal(30, jian.TroopsNeeded);
    }

    [Fact]
    public void 未知id取不到且不抛()
    {
        var table = 表();

        Assert.False(table.TryGet("huogong", out var none));
        Assert.Null(none);
        Assert.False(table.TryGet(null, out _));
        Assert.False(table.TryGet("", out _));
    }

    [Fact]
    public void needs四类各解析成各的()
    {
        var table = 表();
        Assert.True(table.TryGet("wei", out var wei));
        Assert.True(table.TryGet("gong", out var gong));
        Assert.True(table.TryGet("shuihuo", out var shuihuo));

        Assert.Equal(SiegeNeedKind.Flag, wei!.Needs.Kind);
        Assert.Equal(SiegeBook.CityHungryFlag, wei.Needs.Key);

        Assert.Equal(SiegeNeedKind.None, gong!.Needs.Kind);
        Assert.True(gong.Needs.IsNone);

        Assert.Equal(SiegeNeedKind.Troops, shuihuo!.Needs.Kind);
        Assert.Equal(80, shuihuo.Needs.Value);
    }

    // ————————————————————————— 可用性 —————————————————————————

    [Fact]
    public void 无条件的一法兵够就能用()
    {
        var available = new SiegeBook(表()).Available(100, 账());

        Assert.Contains("gong", Ids(available));
    }

    [Fact]
    public void 信任门槛到了才出现()
    {
        var book = new SiegeBook(表());

        Assert.DoesNotContain("jian", Ids(book.Available(100, 账(guan: 59))));
        Assert.Contains("jian", Ids(book.Available(100, 账(guan: 60))));
    }

    [Fact]
    public void 兵力门槛到了才出现()
    {
        var book = new SiegeBook(表());

        Assert.DoesNotContain("shuihuo", Ids(book.Available(79, 账())));
        Assert.Contains("shuihuo", Ids(book.Available(80, 账())));
    }

    [Fact]
    public void 旗标没置位就不出现()
    {
        var book = new SiegeBook(表());
        var wei = 法("wei");

        Assert.DoesNotContain("wei", Ids(book.Available(100, 账())));
        Assert.Contains("wei", Ids(book.Available(100, 账(), 旗标(SiegeBook.CityHungryFlag))));
        Assert.False(book.CanUse(wei, 100, 账()));
    }

    [Fact]
    public void Available按文件顺序返回()
    {
        var available = new SiegeBook(表())
            .Available(100, 账(guan: 70), 旗标(SiegeBook.CityHungryFlag));

        Assert.Equal(new[] { "wei", "gong", "jian", "shuihuo" }, Ids(available));
    }

    [Fact]
    public void Available只卡可选门槛不卡打赢门槛()
    {
        // 0 兵：攻没有 needs，照样能选（选得动 ≠ 打得赢）；水火要 troops>=80，连选都不让选。
        var available = new SiegeBook(表()).Available(0, 账());

        Assert.Contains("gong", Ids(available));
        Assert.DoesNotContain("shuihuo", Ids(available));
    }

    [Fact]
    public void 兵不够打赢就撤退损三成()
    {
        // 65 兵：攻的 needs 为空让它可选，但 troops_needed=72——这一仗会输，然后按 §16.5 撤退。
        var book = new SiegeBook(表());

        Assert.Contains("gong", Ids(book.Available(65, 账())));

        var result = book.Resolve(法("gong"), 65, 账());
        Assert.True(result.Retreat);
        Assert.False(result.Win);
        Assert.Equal(45, result.Survived); // 65 − 20（30% = 19.5，向上取整），约剩七成
        Assert.Equal(SiegeBook.RetreatLossPercent, result.Casualty);
        Assert.Equal(SiegeBook.FailedFlag, result.Flag);
        Assert.Equal(72, result.Needed);
    }

    [Fact]
    public void 兵刚好够打赢门槛就赢()
    {
        // 72 兵：正好踩到攻的 troops_needed，边界算赢。
        var result = new SiegeBook(表()).Resolve(法("gong"), 72, 账());

        Assert.True(result.Win);
        Assert.False(result.Retreat);
        Assert.Equal(51, result.Survived); // 72 − 21（30% 伤亡向下取整）
        Assert.Equal(8, result.Minyuan);
        Assert.Null(result.Flag);
        Assert.Equal(72, result.Needed);
    }

    // ————————————————————————— 文案 —————————————————————————

    [Fact]
    public void 信任门槛的文案是当前值与门槛()
    {
        Assert.Equal("官望 42 / 需 60", new SiegeBook(表()).NeedsText(法("jian"), 100, 账(guan: 42)));
    }

    [Fact]
    public void 兵力门槛的文案是当前兵与门槛()
    {
        Assert.Equal("兵力 80 / 需 80", new SiegeBook(表()).NeedsText(法("shuihuo"), 80, 账()));
    }

    [Fact]
    public void 旗标门槛的文案分没备与已备()
    {
        var book = new SiegeBook(表());

        Assert.Equal("城里还没断粮", book.NeedsText(法("wei"), 100, 账()));
        Assert.Equal(
            SiegeBook.FlagReadyText,
            book.NeedsText(法("wei"), 100, 账(), 旗标(SiegeBook.CityHungryFlag)));
    }

    [Fact]
    public void 无条件的文案是无需条件()
    {
        Assert.Equal(SiegeBook.NoNeedText, new SiegeBook(表()).NeedsText(法("gong"), 100, 账()));
    }

    // ————————————————————————— 结算 —————————————————————————

    [Fact]
    public void 赢了按casualty扣人()
    {
        var result = new SiegeBook(表()).Resolve(法("gong"), 100, 账());

        Assert.True(result.Ok);
        Assert.True(result.Win);
        Assert.False(result.Retreat);
        Assert.Equal(70, result.Survived);
        Assert.Equal(30, result.Casualty);
        Assert.Equal(8, result.Minyuan);
        Assert.Null(result.Flag);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void 伤亡向下取整且带回民怨天命()
    {
        var book = new SiegeBook(表());

        var wei = book.Resolve(法("wei"), 96, 账(), 旗标(SiegeBook.CityHungryFlag));
        Assert.Equal(87, wei.Survived); // 96 × 10% = 9.6，向下取整扣 9
        Assert.Equal(20, wei.Minyuan);
        Assert.Equal(-5, wei.Tianming);

        var water = book.Resolve(法("shuihuo"), 100, 账());
        Assert.Equal(82, water.Survived);
        Assert.Equal(30, water.Minyuan);
        Assert.Equal(-15, water.Tianming);
    }

    [Fact]
    public void 兵不够就撤退损三成并置旗标()
    {
        var result = new SiegeBook(表()).Resolve(法("gong"), 70, 账());

        Assert.True(result.Ok);
        Assert.False(result.Win);
        Assert.True(result.Retreat);
        Assert.Equal(49, result.Survived); // 70 − 21
        Assert.Equal(SiegeBook.RetreatLossPercent, result.Casualty);
        Assert.Equal(SiegeBook.FailedFlag, result.Flag);
        Assert.Equal(0, result.Minyuan);
        Assert.Equal(0, result.Tianming);
    }

    [Fact]
    public void 撤退损兵向上取整()
    {
        // 120 × 30% = 36，不能因为整除截断变成 35。
        Assert.Equal(36, SiegeBook.RetreatLoss(120));
        Assert.Equal(1, SiegeBook.RetreatLoss(1));
        Assert.Equal(0, SiegeBook.RetreatLoss(0));
        Assert.True(FlagStore.IsValidName(SiegeBook.FailedFlag), "撤退旗标名必须过 FlagStore.IsValidName");
    }

    [Fact]
    public void 门没开时直接拒且一个兵都不动()
    {
        var result = new SiegeBook(表()).Resolve(法("jian"), 100, 账(guan: 50));

        Assert.False(result.Ok);
        Assert.False(result.Win);
        Assert.False(result.Retreat);
        Assert.Equal(100, result.Survived);
        Assert.Null(result.Flag);
        Assert.Contains("官望 50", result.Reason);
        Assert.Equal(30, result.Needed);
    }

    [Fact]
    public void 结算不碰信任账本()
    {
        var book = new SiegeBook(表());
        var trust = 账(guan: 70, min: 20, yi: 5);

        book.Resolve(法("jian"), 100, trust);

        Assert.Equal(70, trust.State.Guan);
        Assert.Equal(20, trust.State.Min);
        Assert.Equal(5, trust.State.Yi);
    }

    [Fact]
    public void 兵力门槛直接读表()
    {
        Assert.Equal(48, 法("wei").TroopsNeeded);
        Assert.Equal(72, 法("gong").TroopsNeeded);
        Assert.Equal(30, 法("jian").TroopsNeeded);
        Assert.Equal(54, 法("shuihuo").TroopsNeeded);
    }

    [Fact]
    public void 可选但打不赢的文案会提醒多半要撤()
    {
        Assert.Equal(
            "可打，但兵力 65 / 需 72，多半要撤",
            new SiegeBook(表()).NeedsText(法("gong"), 65, 账()));
    }

    [Fact]
    public void 文案三态各说各的()
    {
        // 同一法（围，troops_needed=48）走三种状态：门槛没过 / 过了但兵不够 / 两样都够。
        var book = new SiegeBook(表());
        var wei = 法("wei");
        var hungry = 旗标(SiegeBook.CityHungryFlag);

        Assert.Equal("城里还没断粮", book.NeedsText(wei, 40, 账()));
        Assert.Equal("可打，但兵力 40 / 需 48，多半要撤", book.NeedsText(wei, 40, 账(), hungry));
        Assert.Equal(SiegeBook.FlagReadyText, book.NeedsText(wei, 48, 账(), hungry));
    }

    // ————————————————————————— 坏表 —————————————————————————

    [Fact]
    public void 坏表ke越界带行号抛()
    {
        var tooLong = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("wei,围,6,", "wei,围,9,")));

        Assert.Contains("第 2 行", tooLong.Message);
        Assert.Contains("ke", tooLong.Message);

        var zero = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("wei,围,6,", "wei,围,0,")));
        Assert.Contains("ke", zero.Message);
    }

    [Fact]
    public void 坏表casualty越界带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("jian,间,4,2,", "jian,间,4,101,")));

        Assert.Contains("第 4 行", bad.Message);
        Assert.Contains("casualty", bad.Message);
    }

    [Fact]
    public void 坏表needs语法不认识带行号抛()
    {
        var noOperator = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("trust:guan>=60", "trust:guan>60")));
        Assert.Contains("第 4 行", noOperator.Message);
        Assert.Contains("needs", noOperator.Message);

        var badAxis = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("trust:guan>=60", "trust:cheng>=60")));
        Assert.Contains("cheng", badAxis.Message);
    }

    [Fact]
    public void 坏表旗标名不合规也抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("flag:city_hungry", "flag:断粮")));

        Assert.Contains("第 2 行", bad.Message);
        Assert.Contains("旗标名", bad.Message);
    }

    [Fact]
    public void 坏表troops_needed不是正整数抛()
    {
        var zero = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("gong,攻,3,30,8,0,,72,", "gong,攻,3,30,8,0,,0,")));
        Assert.Contains("troops_needed", zero.Message);

        var notInt = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(MiniSiege.Replace("gong,攻,3,30,8,0,,72,", "gong,攻,3,30,8,0,,七十二,")));
        Assert.Contains("troops_needed", notInt.Message);
    }

    [Fact]
    public void 坏表缺troops_needed列带行号抛()
    {
        // 整列没了，读表即抛，不能悄悄当成"不用兵"。
        var bad = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv(
                "id,label,ke,casualty,minyuan,tianming,needs,note\n" +
                "wei,围,6,10,20,-5,flag:city_hungry,长围\n"));

        Assert.Contains("第 1 行", bad.Message);
        Assert.Contains("troops_needed", bad.Message);
    }

    [Fact]
    public void 坏表缺列抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            SiegeTable.FromCsv("id,label,ke\nwei,围,9\n"));

        Assert.Contains("缺少列", bad.Message);
    }

    // ————————————————————————— 存档 v3 → v4 —————————————————————————

    [Fact]
    public void v3存档读进来兵力为0且其余照旧()
    {
        var json = "{\"version\":3,\"day\":9,\"prologue\":{\"beats_done\":[\"t1\",\"t2\"]}}";

        var state = SaveState.FromJson(json);

        Assert.Equal(SaveState.CurrentVersion, state.Version);
        Assert.Equal(0, state.Prologue.Troops);
        Assert.Equal(new[] { "t1", "t2" }, state.Prologue.BeatsDone);
        Assert.Equal(9, state.Day);
    }

    [Fact]
    public void 兵力写得进存档也读得回来()
    {
        var state = new SaveState { Day = 17 };
        state.Prologue.Troops = 40;

        var json = state.ToJson();
        Assert.Contains("\"troops\": 40", json);

        var back = SaveState.FromJson(json);
        Assert.Equal(SaveState.CurrentVersion, back.Version);
        Assert.Equal(40, back.Prologue.Troops);
        Assert.Equal(17, back.Day);
    }

    // ————————————————————————— 招兵表 —————————————————————————

    [Fact]
    public void 招兵三选项读得出来()
    {
        var table = RecruitTable.FromCsv(MiniRecruit);

        Assert.Equal(3, table.Count);

        var grain = table.ForOption(0);
        Assert.NotNull(grain);
        Assert.Equal(15, grain!.Troops);
        Assert.Equal("recruit_grain", grain.TrustAction);

        var promise = table.ForOption(1);
        Assert.NotNull(promise);
        Assert.Equal("recruit_promise", promise!.TrustAction);

        var weapons = table.ForOption(2);
        Assert.NotNull(weapons);
        Assert.Equal(-3, weapons!.Troops);
        Assert.Equal("", weapons.TrustAction);
    }

    [Fact]
    public void 招兵按option列取而不是按行序()
    {
        var table = RecruitTable.FromCsv(
            "option,troops,trust,note\n" +
            "2,15,,抢兵器\n" +
            "0,60,gruel,给粮\n" +
            "1,80,,给承诺\n");

        Assert.Equal(60, table.ForOption(0)!.Troops);
        Assert.Equal("gruel", table.ForOption(0)!.TrustAction);
        Assert.Equal(80, table.ForOption(1)!.Troops);
        Assert.Equal(15, table.ForOption(2)!.Troops);
    }

    [Fact]
    public void 招兵下标越界返回空()
    {
        var table = RecruitTable.FromCsv(MiniRecruit);

        Assert.Null(table.ForOption(3));
        Assert.Null(table.ForOption(-1));
    }

    [Fact]
    public void 招兵坏表带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            RecruitTable.FromCsv(
                "option,troops,trust,note\n" +
                "0,15,a,\n" +
                "0,15,b,\n"));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("重复", bad.Message);

        var notInt = Assert.Throws<InvalidDataException>(() =>
            RecruitTable.FromCsv(
                "option,troops,trust,note\n" +
                "0,十五,a,\n"));
        Assert.Contains("troops", notInt.Message);

        var badOption = Assert.Throws<InvalidDataException>(() =>
            RecruitTable.FromCsv(
                "option,troops,trust,note\n" +
                "给粮,15,gruel,\n"));
        Assert.Contains("option", badOption.Message);
    }

    // ————————————————————————— 真表 —————————————————————————

    [Fact]
    public void 真表能读且字段都在范围内()
    {
        var siegePath = FindData("siege.csv");
        if (siegePath != null)
        {
            var table = SiegeTable.Load(siegePath);
            Assert.True(table.Count >= 4, $"siege.csv 只有 {table.Count} 条，至少要四法。");

            foreach (var method in table.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(method.Label), $"「{method.Id}」没有 label。");
                Assert.InRange(method.Ke, SiegeMethod.MinKe, SiegeMethod.MaxKe);
                Assert.InRange(method.Casualty, 0, SiegeMethod.MaxCasualty);
                Assert.InRange(method.Minyuan, -SiegeMethod.MaxShift, SiegeMethod.MaxShift);
                Assert.InRange(method.Tianming, -SiegeMethod.MaxShift, SiegeMethod.MaxShift);
            }

            // 四法都必须有正的打赢门槛；数值本身归 D3 的表，测试只守"读到了"。
            foreach (var method in table.All)
                Assert.True(method.TroopsNeeded >= 1, $"「{method.Id}」的 troops_needed 不是正数。");
        }

        var recruitPath = FindData("recruit.csv");
        if (recruitPath != null)
        {
            var table = RecruitTable.Load(recruitPath);
            Assert.True(table.Count >= 3, $"recruit.csv 只有 {table.Count} 条，招兵要三个选项。");
            Assert.NotNull(table.ForOption(0));
        }
    }

    /// <summary>从测试输出目录往上找 <c>Content/data/&lt;name&gt;</c>；找不到返回 null。</summary>
    private static string? FindData(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var nested = Path.Combine(dir.FullName, "Juhuo", "Content", "data", name);
            if (File.Exists(nested)) return nested;

            var local = Path.Combine(dir.FullName, "Content", "data", name);
            if (File.Exists(local)) return local;

            dir = dir.Parent;
        }

        return null;
    }
}
