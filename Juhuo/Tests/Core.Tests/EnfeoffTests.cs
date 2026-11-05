using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 目标文本、六功臣、分封结算（票号 S4-01b）。
/// 规则源：举火_游戏设计文档_v0.2 §16.6 第 4 步「分封功臣：给 6 位同伴封官。
/// 给高了 → 未来权臣；给低了 → 离心」。
///
/// 表的数据由 D3 写：这里全用内联 CSV，真表只做"若存在"的体检（缺表不红）。
/// </summary>
public class EnfeoffTests
{
    // ── 内联表 ───────────────────────────────────────────────────

    private const string GoalCsv =
        "when,text,note\n" +
        "start,活过这三十天，把该办的事办完,\n" +
        "realm,坐稳这把椅子，别让火熄了,\n" +
        "deficit,十二拍都走完了：该定都、定国号了,\n";

    /// <summary>
    /// 六个人，野心刚好卡在阈值两侧：<c>tie_niu</c> 是 59（不算野心家），
    /// <c>shen_yan</c> / <c>lu_wenzhao</c> 是 68 / 75（封赏会左右他们）。
    /// </summary>
    private const string CompanionCsv =
        "id,name,office,role,loyalty,ambition,note\n" +
        "shen_yan,沈砚,掌书记,文,62,68,老书生\n" +
        "lao_ya,老鸦,先锋,武,55,40,悍将\n" +
        "a_jiu,阿九,斥候,谍,70,30,耳朵最灵\n" +
        "tie_niu,铁牛,刀手,武,48,59,野心差一点\n" +
        "lu_wenzhao,陆文昭,县丞,文,40,75,旧朝小吏\n" +
        "xiao_qi,小七,杂役,杂,80,20,还是个孩子\n";

    private const string BeatCsvWithGoal =
        "id,name,hub,day,requires,sets,trigger,note,goal\n" +
        "b01,焚账,hub1,1,,beat.b01.done,choice,备注,\"烧掉或带走那本账\"\n";

    private const string BeatCsvWithoutGoal =
        "id,name,hub,day,requires,sets,trigger,note\n" +
        "b01,焚账,hub1,1,,beat.b01.done,choice,备注\n";

    private static GoalTable Goals(string? csv = null) => GoalTable.FromCsv(csv ?? GoalCsv);

    private static CompanionTable Companions(string? csv = null) => CompanionTable.FromCsv(csv ?? CompanionCsv);

    private static Companion Who(CompanionTable table, string id)
    {
        Assert.True(table.TryGet(id, out var companion), $"功臣表里没有「{id}」。");
        return companion!;
    }

    private static TrustState Trust(int guan = 10, int min = 10, int yi = 10) =>
        new() { Guan = guan, Min = min, Yi = yi };

    // ── 一、目标表读表 ───────────────────────────────────────────

    [Fact]
    public void 目标表读得进三句()
    {
        var goals = Goals();

        Assert.Equal("活过这三十天，把该办的事办完", goals.Start);
        Assert.Equal("坐稳这把椅子，别让火熄了", goals.Realm);
        Assert.Equal("十二拍都走完了：该定都、定国号了", goals.Deficit);
    }

    [Fact]
    public void 目标表缺列抛且点出第1行()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            GoalTable.FromCsv("when,note\nstart,活着\n"));

        Assert.Contains("第 1 行", bad.Message);
        Assert.Contains("text", bad.Message);
    }

    [Fact]
    public void 目标表正文为空带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            GoalTable.FromCsv(
                "when,text,note\n" +
                "start,活着,\n" +
                "realm,,\n" +
                "deficit,该定都了,\n"));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("realm", bad.Message);
        Assert.Contains("空的", bad.Message);
    }

    [Fact]
    public void 目标表when认不出带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            GoalTable.FromCsv(
                "when,text,note\n" +
                "start,活着,\n" +
                "realmx,坐稳,\n" +
                "realm,坐稳,\n" +
                "deficit,该定都了,\n"));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("realmx", bad.Message);
    }

    [Fact]
    public void 目标表when重复带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            GoalTable.FromCsv(
                "when,text,note\n" +
                "start,活着,\n" +
                "start,再活一次,\n" +
                "realm,坐稳,\n" +
                "deficit,该定都了,\n"));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("重复", bad.Message);
    }

    [Fact]
    public void 目标表三档不齐抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            GoalTable.FromCsv("when,text,note\nstart,活着,\nrealm,坐稳,\n"));

        Assert.Contains("deficit", bad.Message);
    }

    [Fact]
    public void 目标表空文本抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() => GoalTable.FromCsv(""));

        Assert.Contains("空的", bad.Message);
    }

    // ── 二、GoalTable.Current 的四种取值 ────────────────────────

    [Fact]
    public void 已转段给realm那句()
    {
        var goals = Goals();

        Assert.Equal(goals.Realm, goals.Current(pending: null, done: true, turnedRealm: true));
    }

    [Fact]
    public void 十二拍走完未转段给deficit那句()
    {
        var goals = Goals();
        var pending = new Beat("b13", "定都", "hub1", 30, "", Array.Empty<string>(), "choice",
            Array.Empty<string>(), "") { Goal = "随便，这一句不该出现" };

        Assert.Equal(goals.Deficit, goals.Current(pending, done: true, turnedRealm: false));
    }

    [Fact]
    public void 有等着的拍就给那一拍自己的话()
    {
        var goals = Goals();
        var pending = new Beat("b05", "过江", "hub3", 8, "", Array.Empty<string>(), "choice",
            Array.Empty<string>(), "") { Goal = "去江边找船" };

        Assert.Equal("去江边找船", goals.Current(pending, done: false, turnedRealm: false));
    }

    [Fact]
    public void 什么都没有给起手那句()
    {
        var goals = Goals();

        Assert.Equal(goals.Start, goals.Current(null, done: false, turnedRealm: false));
    }

    [Fact]
    public void 转段优先于等着的拍()
    {
        var goals = Goals();
        var pending = new Beat("b05", "过江", "hub3", 8, "", Array.Empty<string>(), "choice",
            Array.Empty<string>(), "") { Goal = "去江边找船" };

        Assert.Equal(goals.Realm, goals.Current(pending, done: true, turnedRealm: true));
    }

    [Fact]
    public void 拍上没写目标退回起手那句不给空串()
    {
        var goals = Goals();
        var pending = new Beat("b05", "过江", "hub3", 8, "", Array.Empty<string>(), "choice",
            Array.Empty<string>(), "");

        Assert.Equal(goals.Start, goals.Current(pending, done: false, turnedRealm: false));
    }

    // ── 三、beats.csv 的 goal 列 ─────────────────────────────────

    [Fact]
    public void 节拍表读得出goal列()
    {
        var table = BeatTable.FromCsv(BeatCsvWithGoal);

        Assert.Equal("烧掉或带走那本账", table.All[0].Goal);
    }

    [Fact]
    public void 节拍表没有goal列不炸且为空串()
    {
        var table = BeatTable.FromCsv(BeatCsvWithoutGoal);

        Assert.Equal("", table.All[0].Goal);
        Assert.NotNull(table.All[0].Goal);
    }

    // ── 四、功臣表 ───────────────────────────────────────────────

    [Fact]
    public void 功臣表读得进六个人()
    {
        var table = Companions();

        Assert.Equal(6, table.Count);
        Assert.Equal("shen_yan", table.All[0].Id);
        Assert.Equal("小七", table.All[5].Name);

        var shen = Who(table, "shen_yan");
        Assert.Equal("沈砚", shen.Name);
        Assert.Equal("掌书记", shen.Office);
        Assert.Equal("文", shen.Role);
        Assert.Equal(62, shen.Loyalty);
        Assert.Equal(68, shen.Ambition);
        Assert.Equal("老书生", shen.Note);
    }

    [Fact]
    public void 功臣表按名字取也取得到()
    {
        var table = Companions();

        Assert.Equal("lao_ya", table.Get("老鸦")!.Id);
        Assert.Null(table.Get("没有这个人"));
        Assert.Null(table.Get(null));
    }

    [Fact]
    public void 功臣表找不到id给null()
    {
        var table = Companions();

        Assert.False(table.TryGet("nope", out var companion));
        Assert.Null(companion);
        Assert.False(table.TryGet("", out _));
        Assert.False(table.TryGet(null, out _));
    }

    [Fact]
    public void 功臣表少一个人带行号抛()
    {
        var csv = Header() + string.Join("\n", CsvBody().Take(5)) + "\n";

        var bad = Assert.Throws<InvalidDataException>(() => CompanionTable.FromCsv(csv));

        Assert.Contains("第 2 行起", bad.Message);
        Assert.Contains("6", bad.Message);
        Assert.Contains("5", bad.Message);
    }

    [Fact]
    public void 功臣表多一个人带行号抛()
    {
        var rows = CsvBody().ToList();
        rows.Add("extra,多出来的人,多余的官,杂,50,50,");

        var bad = Assert.Throws<InvalidDataException>(() => CompanionTable.FromCsv(Header() + string.Join("\n", rows) + "\n"));

        Assert.Contains("6", bad.Message);
        Assert.Contains("7", bad.Message);
    }

    [Fact]
    public void 功臣表id重复带行号抛()
    {
        var csv =
            "id,name,office,role,loyalty,ambition,note\n" +
            "a,甲,官一,文,50,50,\n" +
            "a,乙,官二,文,50,50,\n" +
            "c,丙,官三,文,50,50,\n" +
            "d,丁,官四,文,50,50,\n" +
            "e,戊,官五,文,50,50,\n" +
            "f,己,官六,文,50,50,\n";

        var bad = Assert.Throws<InvalidDataException>(() => CompanionTable.FromCsv(csv));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("重复", bad.Message);
    }

    [Fact]
    public void 功臣表office重复带行号抛()
    {
        var csv =
            "id,name,office,role,loyalty,ambition,note\n" +
            "a,甲,掌书记,文,50,50,\n" +
            "b,乙,掌书记,文,50,50,\n" +
            "c,丙,官三,文,50,50,\n" +
            "d,丁,官四,文,50,50,\n" +
            "e,戊,官五,文,50,50,\n" +
            "f,己,官六,文,50,50,\n";

        var bad = Assert.Throws<InvalidDataException>(() => CompanionTable.FromCsv(csv));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("掌书记", bad.Message);
        Assert.Contains("重复", bad.Message);
    }

    [Fact]
    public void 功臣表忠诚越界带行号抛()
    {
        var csv =
            "id,name,office,role,loyalty,ambition,note\n" +
            "a,甲,官一,文,101,50,\n" +
            "b,乙,官二,文,50,50,\n" +
            "c,丙,官三,文,50,50,\n" +
            "d,丁,官四,文,50,50,\n" +
            "e,戊,官五,文,50,50,\n" +
            "f,己,官六,文,50,50,\n";

        var bad = Assert.Throws<InvalidDataException>(() => CompanionTable.FromCsv(csv));

        Assert.Contains("第 2 行", bad.Message);
        Assert.Contains("loyalty", bad.Message);
        Assert.Contains("超出", bad.Message);
    }

    [Fact]
    public void 功臣表野心越界带行号抛()
    {
        var csv =
            "id,name,office,role,loyalty,ambition,note\n" +
            "a,甲,官一,文,50,50,\n" +
            "b,乙,官二,文,50,-1,\n" +
            "c,丙,官三,文,50,50,\n" +
            "d,丁,官四,文,50,50,\n" +
            "e,戊,官五,文,50,50,\n" +
            "f,己,官六,文,50,50,\n";

        var bad = Assert.Throws<InvalidDataException>(() => CompanionTable.FromCsv(csv));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("ambition", bad.Message);
    }

    // ── 五、厚封 ─────────────────────────────────────────────────

    [Fact]
    public void 厚封扣八点忠加三点官望()
    {
        var companion = Who(Companions(), "shen_yan"); // 忠 62、野心 68
        var trust = Trust();
        var flags = new FlagStore();

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.Heavy, trust, flags);

        Assert.True(result.Ok);
        Assert.Equal("", result.Reason);
        Assert.Equal(54, result.LoyaltyAfter);
        Assert.Equal(13, trust.Guan);
        Assert.Equal(10, trust.Min);
        Assert.Equal(10, trust.Yi);
    }

    [Fact]
    public void 厚封那句话照票面的格式()
    {
        var companion = Who(Companions(), "shen_yan");

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.Heavy, Trust(), new FlagStore());

        Assert.Equal("沈砚 厚封：忠 62 → 54，官望 +3", Assert.Single(result.Lines));
    }

    [Fact]
    public void 厚封野心六十的人成未来权臣()
    {
        var companion = Who(Companions(), "shen_yan"); // 野心 68
        var flags = new FlagStore();

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.Heavy, Trust(), flags);

        Assert.True(result.Ok);
        Assert.True(flags.Has(EnfeoffBook.WillBePowerfulFlag("shen_yan"), null));
        Assert.False(flags.Has(EnfeoffBook.ResentedFlag("shen_yan"), null));
    }

    [Fact]
    public void 厚封野心五十九的人不是权臣()
    {
        var companion = Who(Companions(), "tie_niu"); // 野心 59，卡在阈值下沿
        var flags = new FlagStore();

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.Heavy, Trust(), flags);

        Assert.True(result.Ok);
        Assert.False(flags.Has(EnfeoffBook.WillBePowerfulFlag("tie_niu"), null));
    }

    // ── 六、薄封 ─────────────────────────────────────────────────

    [Fact]
    public void 薄封加六点忠加两点民望()
    {
        var companion = Who(Companions(), "a_jiu"); // 忠 70、野心 30
        var trust = Trust();
        var flags = new FlagStore();

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.Light, trust, flags);

        Assert.True(result.Ok);
        Assert.Equal(76, result.LoyaltyAfter);
        Assert.Equal(12, trust.Min);
        Assert.Equal(10, trust.Guan);
        Assert.Equal(10, trust.Yi);
        Assert.Equal("阿九 薄封：忠 70 → 76，民望 +2", Assert.Single(result.Lines));
        Assert.False(flags.Has(EnfeoffBook.ResentedFlag("a_jiu"), null));
    }

    [Fact]
    public void 薄封野心六十的人会离心()
    {
        var companion = Who(Companions(), "lu_wenzhao"); // 忠 40、野心 75
        var flags = new FlagStore();

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.Light, Trust(), flags);

        Assert.True(result.Ok);
        Assert.True(flags.Has(EnfeoffBook.ResentedFlag("lu_wenzhao"), null));
        Assert.False(flags.Has(EnfeoffBook.WillBePowerfulFlag("lu_wenzhao"), null));
    }

    [Fact]
    public void 薄封野心五十九的人不离心()
    {
        var companion = Who(Companions(), "tie_niu");
        var flags = new FlagStore();

        new EnfeoffBook().Enfeoff(companion, Enfeoff.Light, Trust(), flags);

        Assert.False(flags.Has(EnfeoffBook.ResentedFlag("tie_niu"), null));
    }

    // ── 七、不封 ─────────────────────────────────────────────────

    [Fact]
    public void 不封扣二十点忠加两点疑心且当晚走人()
    {
        var companion = Who(Companions(), "lao_ya"); // 忠 55、野心 40
        var trust = Trust();
        var flags = new FlagStore();

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.None, trust, flags);

        Assert.True(result.Ok);
        Assert.Equal(35, result.LoyaltyAfter);
        Assert.Equal(12, trust.Yi);
        Assert.Equal(10, trust.Guan);
        Assert.Equal(10, trust.Min);
        Assert.Equal("老鸦 不封：忠 55 → 35，疑心 +2", Assert.Single(result.Lines));
        Assert.True(flags.Has(EnfeoffBook.LeftFlag("lao_ya"), null));
    }

    [Fact]
    public void 不封不看野心人人都会走()
    {
        var flags = new FlagStore();
        var abject = new Companion("nobody", "无名", "杂役", "杂", 50, 0, "");

        var result = new EnfeoffBook().Enfeoff(abject, Enfeoff.None, Trust(), flags);

        Assert.True(result.Ok);
        Assert.True(flags.Has(EnfeoffBook.LeftFlag("nobody"), null));
    }

    // ── 八、钳位与重复 ───────────────────────────────────────────

    [Fact]
    public void 忠诚见底钳到零()
    {
        var fragile = new Companion("fragile", "易碎", "杂役", "杂", 10, 20, "");

        var result = new EnfeoffBook().Enfeoff(fragile, Enfeoff.None, Trust(), new FlagStore());

        Assert.True(result.Ok);
        Assert.Equal(0, result.LoyaltyAfter);
    }

    [Fact]
    public void 信任到顶那句话标已到顶()
    {
        var companion = Who(Companions(), "shen_yan");
        var trust = Trust(guan: 99);

        var result = new EnfeoffBook().Enfeoff(companion, Enfeoff.Heavy, trust, new FlagStore());

        Assert.True(result.Ok);
        Assert.Equal(100, trust.Guan);
        Assert.Contains("（已到顶）", Assert.Single(result.Lines));
    }

    [Fact]
    public void 同一个人封两次第二次失败且不再扣()
    {
        var companion = Who(Companions(), "shen_yan");
        var trust = Trust();
        var book = new EnfeoffBook();

        Assert.True(book.Enfeoff(companion, Enfeoff.Heavy, trust, new FlagStore()).Ok);

        var again = book.Enfeoff(companion, Enfeoff.Heavy, trust, new FlagStore());

        Assert.False(again.Ok);
        Assert.Contains("已经封过", again.Reason);
        Assert.Contains("沈砚", again.Reason);
        Assert.Equal(62, again.LoyaltyAfter); // 报的是原样，不是再扣一次的 54
        Assert.Equal(13, trust.Guan);         // 官望只加了一次
        Assert.Empty(again.Lines);
    }

    // ── 九、存档 v6 ──────────────────────────────────────────────

    [Fact]
    public void 封赏写得进存档也读得回来()
    {
        var state = new SaveState { Day = 30 };
        EnfeoffRecord.Put(state, "shen_yan", Enfeoff.Heavy);
        EnfeoffRecord.Put(state, "lao_ya", Enfeoff.None);

        var json = state.ToJson();
        Assert.Contains($"\"version\": {SaveState.CurrentVersion}", json);
        Assert.Contains("\"enfeoffs\"", json);
        Assert.Contains("\"shen_yan\": \"heavy\"", json);

        var back = SaveState.FromJson(json);
        Assert.Equal(SaveState.CurrentVersion, back.Version);
        Assert.True(EnfeoffRecord.TryGet(back, "shen_yan", out var heavy));
        Assert.Equal(Enfeoff.Heavy, heavy);
        Assert.True(EnfeoffRecord.TryGet(back, "lao_ya", out var none));
        Assert.Equal(Enfeoff.None, none);
        Assert.False(EnfeoffRecord.TryGet(back, "xiao_qi", out _));
        Assert.Equal(2, EnfeoffRecord.Count(back));
    }

    [Fact]
    public void v5老档读进来封赏为空且版本升到六()
    {
        var json =
            "{\"version\":5,\"day\":30,\"prologue\":{" +
            "\"beats_done\":[\"b01\"],\"troops\":40,\"capital\":\"hub5\"," +
            "\"realm\":\"xie\",\"oaths\":[\"salt\"],\"debts\":[]}}";

        var state = SaveState.FromJson(json);

        Assert.Equal(SaveState.CurrentVersion, state.Version);
        Assert.Empty(state.Prologue.Enfeoffs);
        Assert.Equal(0, EnfeoffRecord.Count(state));

        // 其余一个不动
        Assert.Equal("hub5", state.Prologue.Capital);
        Assert.Equal("xie", state.Prologue.Realm);
        Assert.Equal(new[] { "salt" }, state.Prologue.Oaths);
        Assert.Equal(40, state.Prologue.Troops);
        Assert.Equal(30, state.Day);
    }

    [Fact]
    public void 封赏不是转段的前置条件()
    {
        // 十二拍、定都、国号、国策都齐了，封赏一条没记：照样算完成（Done 判定不动）。
        var flags = new FlagStore();
        Assert.True(flags.Set("beat.b07.pick.1"));

        var state = SaveState.FromJson(
            "{\"version\":6,\"prologue\":{" +
            "\"beats_done\":[\"b01\",\"b02\",\"b03\",\"b04\",\"b05\",\"b06\"," +
            "\"b07\",\"b08\",\"b09\",\"b10\",\"b11\",\"b12\"]," +
            "\"capital\":\"hub5\",\"realm\":\"xie\",\"oaths\":[\"salt\"]}}");

        var book = new TransitionBook(
            OathTable.FromCsv(
                "id,text,beneficiary,deadline,penalty,source,note\n" +
                "salt,三年不征盐税,漕帮,三年,离心,b07:1,\n"),
            RealmTable.FromCsv("id,label,motto,note\nxie,大燮,承天,火德\n"),
            flags,
            state);

        Assert.True(book.IsComplete);
        Assert.True(book.Finish().Ok);
        Assert.False(EnfeoffRecord.AllRecorded(state, Companions()));
    }

    [Fact]
    public void 六个人都记过才算分封做完()
    {
        var table = Companions();
        var state = new SaveState();

        foreach (var companion in table.All)
            EnfeoffRecord.Put(state, companion.Id, Enfeoff.Light);

        Assert.True(EnfeoffRecord.AllRecorded(state, table));
        Assert.Equal(6, EnfeoffRecord.Count(state));
    }

    [Fact]
    public void 封赏的存档写法认得回来()
    {
        Assert.Equal("heavy", EnfeoffBook.Code(Enfeoff.Heavy));
        Assert.Equal("light", EnfeoffBook.Code(Enfeoff.Light));
        Assert.Equal("none", EnfeoffBook.Code(Enfeoff.None));

        Assert.True(EnfeoffBook.TryParseCode(" HEAVY ", out var heavy));
        Assert.Equal(Enfeoff.Heavy, heavy);
        Assert.True(EnfeoffBook.TryParseCode("light", out var light));
        Assert.Equal(Enfeoff.Light, light);
        Assert.True(EnfeoffBook.TryParseCode("none", out var none));
        Assert.Equal(Enfeoff.None, none);

        Assert.False(EnfeoffBook.TryParseCode("veto", out _));
        Assert.False(EnfeoffBook.TryParseCode(null, out _));
    }

    // ── 十、真表体检（表还没到位就跳过）─────────────────────────

    [Fact]
    public void 真功臣表若已存在则正好六人且数值合规()
    {
        var path = FindData("companions.csv");
        if (path == null) return; // 数据还没写，不红

        var table = CompanionTable.Load(path);

        Assert.Equal(CompanionTable.RequiredCount, table.Count);
        foreach (var companion in table.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(companion.Name), $"功臣「{companion.Id}」没有名字。");
            Assert.InRange(companion.Loyalty, CompanionTable.MinStat, CompanionTable.MaxStat);
            Assert.InRange(companion.Ambition, CompanionTable.MinStat, CompanionTable.MaxStat);
            Assert.True(FlagStore.IsValidName(EnfeoffBook.LeftFlag(companion.Id)),
                $"功臣「{companion.Id}」拼出来的旗标名不合法。");
        }
    }

    [Fact]
    public void 真目标表若已存在则三档都有话()
    {
        var path = FindData("goals.csv");
        if (path == null) return; // 数据还没写，不红

        var goals = GoalTable.Load(path);

        Assert.False(string.IsNullOrWhiteSpace(goals.Start));
        Assert.False(string.IsNullOrWhiteSpace(goals.Realm));
        Assert.False(string.IsNullOrWhiteSpace(goals.Deficit));
    }

    [Fact]
    public void 真节拍表若带goal列则每拍都读得进()
    {
        var path = FindData("beats.csv");
        if (path == null) return;

        var table = BeatTable.Load(path);

        Assert.NotEmpty(table.All);
        foreach (var beat in table.All)
            Assert.NotNull(beat.Goal);
    }

    // ── 辅助 ────────────────────────────────────────────────────

    /// <summary>功臣表的表头行。</summary>
    private static string Header() => "id,name,office,role,loyalty,ambition,note\n";

    /// <summary>内联功臣表的六行正文（不含表头）。</summary>
    private static IEnumerable<string> CsvBody() =>
        CompanionCsv.Split('\n').Skip(1).Where(l => l.Length > 0);

    /// <summary>从测试输出目录往上找 <c>Content/data/&lt;name&gt;</c>；找不到返回 null。</summary>
    private static string? FindData(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "Content", "data", name);
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }

        return null;
    }
}
