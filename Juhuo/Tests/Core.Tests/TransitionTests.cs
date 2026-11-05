using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 转段结算的规则层（票号 S2-05b）。
/// 规则源：举火_游戏设计文档_v0.2 §16.6（转段六步）、§14.2（誓约卡 → 国策 / 债）。
///
/// 票面明确**不做分封功臣**——同伴系统还不存在，这里只验定都、国号、三誓约变国策、收尾四件。
/// 表的数据由另一人写：这里全用内联 CSV，真表只做"若存在"的体检。
/// </summary>
public class TransitionTests
{
    // ── 内联表 ───────────────────────────────────────────────────

    /// <summary>
    /// 五张誓约：<c>salt</c> 与 <c>salt0</c> 出自同一拍的两个不同选项——
    /// 选了第 1 项就只该认 <c>salt</c>，这是"只认真选过的选项"的核心用例。
    /// </summary>
    private const string OathCsv =
        "id,text,beneficiary,deadline,penalty,source,note\n" +
        "salt,三年不征盐税,江淮漕帮,三年,漕帮离心,b07:1,盐税是漕帮的命\n" +
        "salt0,盐税只减三成,江淮漕帮,三年,漕帮离心,b07:0,同一拍的另一项\n" +
        "nosurrender,开城之后不杀降,铁门关守军,开城后,守军死战,b09:0,铁门关\n" +
        "land,事成之后田归佃户,丰原百姓,事成后,丰原民变,b11:2,丰原\n" +
        "vanguard,封你为先锋将军,老鸦,开国后,老鸦出走,b03:1,老鸦\n";

    private const string RealmCsv =
        "id,label,motto,note\n" +
        "xie,大燮,承天,火德\n" +
        "liang,大梁,开元,旧朝国号的反面\n" +
        "yan,大燕,启明,北方来路\n";

    private static readonly string[] AllFourPicks =
    {
        "beat.b07.pick.1", "beat.b09.pick.0", "beat.b11.pick.2", "beat.b03.pick.1",
    };

    private static OathTable Oaths(string? csv = null) => OathTable.FromCsv(csv ?? OathCsv);

    private static RealmTable Realms(string? csv = null) => RealmTable.FromCsv(csv ?? RealmCsv);

    private static FlagStore Flags(params string[] picks)
    {
        var flags = new FlagStore();
        foreach (var pick in picks) Assert.True(flags.Set(pick), $"旗标「{pick}」没写进去。");
        return flags;
    }

    private static TransitionBook Book(FlagStore flags, SaveState? state = null) =>
        new(Oaths(), Realms(), flags, state ?? new SaveState());

    /// <summary>往存档里补 n 拍"做完了"。</summary>
    private static SaveState WithBeats(SaveState state, int n)
    {
        for (var i = 0; i < n; i++) state.Prologue.BeatsDone.Add($"b{i + 1:00}");
        return state;
    }

    /// <summary>四样全齐的一份：定都 hub5、国号大燮、3 条国策、12 拍走完。</summary>
    private static (TransitionBook Book, SaveState State, FlagStore Flags) Ready()
    {
        var flags = Flags(AllFourPicks);
        var state = WithBeats(new SaveState(), 12);
        var book = Book(flags, state);

        Assert.True(book.ChooseCapital("hub5").Ok);
        Assert.True(book.ChooseRealm("xie").Ok);
        Assert.True(book.ChooseOaths(new[] { "salt", "nosurrender", "land" }).Ok);

        return (book, state, flags);
    }

    // ── 一、source 解析 ──────────────────────────────────────────

    [Fact]
    public void source拆成节拍与序号()
    {
        var source = OathSource.Parse("b07:1", 3);

        Assert.Equal("b07", source.BeatId);
        Assert.Equal(1, source.OptionIndex);
        Assert.Equal("beat.b07.pick.1", source.FlagName);
        Assert.Equal("b07:1", source.ToString());
    }

    [Fact]
    public void source没有冒号带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() => OathSource.Parse("b07", 4));

        Assert.Contains("第 4 行", bad.Message);
        Assert.Contains("b07", bad.Message);
    }

    [Fact]
    public void source序号不是整数带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() => OathSource.Parse("b07:x", 2));

        Assert.Contains("第 2 行", bad.Message);
        Assert.Contains("不是整数", bad.Message);
    }

    [Fact]
    public void source序号为负带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() => OathSource.Parse("b07:-1", 2));

        Assert.Contains("第 2 行", bad.Message);
        Assert.Contains("负数", bad.Message);
    }

    [Fact]
    public void source节拍id为空带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() => OathSource.Parse(":1", 5));

        Assert.Contains("第 5 行", bad.Message);
        Assert.Contains("节拍 id 是空的", bad.Message);
    }

    // ── 二、誓约表 ───────────────────────────────────────────────

    [Fact]
    public void 誓约表读得进且七列都点名()
    {
        var table = Oaths();

        Assert.Equal(5, table.Count);
        Assert.True(table.TryGet("salt", out var salt));
        Assert.Equal("三年不征盐税", salt!.Text);
        Assert.Equal("江淮漕帮", salt.Beneficiary);
        Assert.Equal("三年", salt.Deadline);
        Assert.Equal("漕帮离心", salt.Penalty);
        Assert.Equal("b07", salt.Source.BeatId);
        Assert.Equal(1, salt.Source.OptionIndex);
        Assert.Equal("盐税是漕帮的命", salt.Note);

        Assert.Equal("salt", table.All[0].Id);
        Assert.Equal("vanguard", table.All[4].Id);
    }

    [Fact]
    public void 誓约表缺列抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            OathTable.FromCsv("id,text,beneficiary,deadline,penalty,source\nsalt,三年不征盐税,漕帮,三年,离心,b07:1\n"));

        Assert.Contains("缺少列", bad.Message);
        Assert.Contains("note", bad.Message);
    }

    [Fact]
    public void 誓约表id重复带行号抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            OathTable.FromCsv(
                "id,text,beneficiary,deadline,penalty,source,note\n" +
                "salt,三年不征盐税,漕帮,三年,离心,b07:1,\n" +
                "salt,盐税只减三成,漕帮,三年,离心,b07:0,\n"));

        Assert.Contains("第 3 行", bad.Message);
        Assert.Contains("重复", bad.Message);
    }

    [Fact]
    public void 誓约表空文本当坏表抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() => OathTable.FromCsv(""));

        Assert.Contains("空的", bad.Message);
    }

    [Fact]
    public void 誓约表找不到给null()
    {
        var table = Oaths();

        Assert.False(table.TryGet("nope", out var oath));
        Assert.Null(oath);
        Assert.False(table.TryGet(null, out _));
        Assert.False(table.TryGet("", out _));
    }

    // ── 三、国号表 ───────────────────────────────────────────────

    [Fact]
    public void 国号表读得进()
    {
        var table = Realms();

        Assert.Equal(3, table.Count);
        Assert.True(table.TryGet("xie", out var xie));
        Assert.Equal("大燮", xie!.Label);
        Assert.Equal("承天", xie.Motto);
        Assert.Equal("火德", xie.Note);
        Assert.False(table.TryGet("qin", out _));
    }

    [Fact]
    public void 国号表坏表带行号抛()
    {
        var dup = Assert.Throws<InvalidDataException>(() =>
            RealmTable.FromCsv(
                "id,label,motto,note\n" +
                "xie,大燮,承天,火德\n" +
                "xie,大梁,开元,重复\n"));

        Assert.Contains("第 3 行", dup.Message);
        Assert.Contains("重复", dup.Message);

        var noId = Assert.Throws<InvalidDataException>(() =>
            RealmTable.FromCsv("id,label,motto,note\n,大燮,承天,火德\n"));

        Assert.Contains("第 2 行", noId.Message);
        Assert.Contains("id 为空", noId.Message);
    }

    [Fact]
    public void 国号表缺列抛()
    {
        var bad = Assert.Throws<InvalidDataException>(() =>
            RealmTable.FromCsv("id,label,motto\nxie,大燮,承天\n"));

        Assert.Contains("缺少列", bad.Message);
        Assert.Contains("note", bad.Message);
    }

    // ── 四、MadeOaths：只认真选过的选项 ──────────────────────────

    [Fact]
    public void MadeOaths只认真选过的选项()
    {
        // b07 这一拍选了第 1 项：salt 算，同一拍的第 0 项 salt0 不算。
        var book = Book(Flags("beat.b07.pick.1", "beat.b09.pick.0"));
        var made = book.MadeOaths();

        Assert.Equal(new[] { "salt", "nosurrender" }, made.Select(o => o.Id));
        Assert.DoesNotContain(made, o => o.Id == "salt0");
        Assert.DoesNotContain(made, o => o.Id == "land");
    }

    [Fact]
    public void MadeOaths按表里的顺序不按选中的先后()
    {
        // 先选 b11，再选 b07；表里 b07 在前。
        var book = Book(Flags("beat.b11.pick.2", "beat.b07.pick.1"));

        Assert.Equal(new[] { "salt", "land" }, book.MadeOaths().Select(o => o.Id));
    }

    [Fact]
    public void 一拍都没选MadeOaths为空()
    {
        var book = Book(Flags());

        Assert.Empty(book.MadeOaths());
    }

    // ── 五、ChooseOaths ─────────────────────────────────────────

    [Fact]
    public void 选三条可以()
    {
        var (book, state, _) = Ready();

        Assert.Equal(3, state.Prologue.Oaths.Count);
        Assert.Equal(new[] { "salt", "nosurrender", "land" }, state.Prologue.Oaths);
        Assert.True(book.IsComplete);
    }

    [Fact]
    public void 选四条被拒且存档不动()
    {
        var flags = Flags(AllFourPicks);
        var state = new SaveState();
        var book = Book(flags, state);

        var result = book.ChooseOaths(new[] { "salt", "salt0", "nosurrender", "land", "vanguard" });

        Assert.False(result.Ok);
        Assert.Contains("最多只能立 3 条", result.Reason);
        Assert.Contains("5 条", result.Reason);
        Assert.Empty(state.Prologue.Oaths);
        Assert.Empty(state.Prologue.Debts);
    }

    [Fact]
    public void 选没做过的卡被拒()
    {
        // 只许过 b07 第 1 项，没做过 b09 那一拍。
        var flags = Flags("beat.b07.pick.1");
        var state = new SaveState();
        var book = Book(flags, state);

        // 只挑那张没许过的：挑两张会先撞上"最多立 N 条"的上限，测不到本意
        var result = book.ChooseOaths(new[] { "nosurrender" });

        Assert.False(result.Ok);
        Assert.Contains("没许过", result.Reason);
        Assert.Contains("nosurrender", result.Reason);
        Assert.Contains("b09:0", result.Reason);
        Assert.Empty(state.Prologue.Oaths);
        Assert.Empty(state.Prologue.Debts);
    }

    [Fact]
    public void 选表里没有的id被拒()
    {
        var book = Book(Flags(AllFourPicks));

        var result = book.ChooseOaths(new[] { "salt", "没这条" });

        Assert.False(result.Ok);
        Assert.Contains("不在誓约表里", result.Reason);
        Assert.Contains("没这条", result.Reason);
    }

    [Fact]
    public void 选中的进Oaths其余许过的进Debts()
    {
        var flags = Flags(AllFourPicks);
        var state = new SaveState();
        var book = Book(flags, state);

        var result = book.ChooseOaths(new[] { "salt", "nosurrender", "land" });

        Assert.True(result.Ok);
        Assert.Equal("", result.Reason);
        Assert.Equal(new[] { "salt", "nosurrender", "land" }, state.Prologue.Oaths);
        // 四张里没被选中的那张（同一拍的 salt0 根本没许过，不该进债）。
        Assert.Equal(new[] { "vanguard" }, state.Prologue.Debts);
    }

    [Fact]
    public void 重复id按一条算()
    {
        var state = new SaveState();
        var book = Book(Flags(AllFourPicks), state);

        var result = book.ChooseOaths(new[] { "salt", "salt", " salt " });

        Assert.True(result.Ok);
        Assert.Equal(new[] { "salt" }, state.Prologue.Oaths);
        Assert.Equal(new[] { "nosurrender", "land", "vanguard" }, state.Prologue.Debts);
    }

    [Fact]
    public void 一条都不选被拒()
    {
        var book = Book(Flags(AllFourPicks));

        Assert.False(book.ChooseOaths(System.Array.Empty<string>()).Ok);
        Assert.False(book.ChooseOaths(null).Ok);
    }

    [Fact]
    public void 再选一次会盖掉上一次的债()
    {
        var state = new SaveState();
        var book = Book(Flags(AllFourPicks), state);

        Assert.True(book.ChooseOaths(new[] { "salt" }).Ok);
        Assert.Equal(new[] { "nosurrender", "land", "vanguard" }, state.Prologue.Debts);

        Assert.True(book.ChooseOaths(new[] { "vanguard" }).Ok);
        Assert.Equal(new[] { "vanguard" }, state.Prologue.Oaths);
        Assert.Equal(new[] { "salt", "nosurrender", "land" }, state.Prologue.Debts);
    }

    // ── 六、定都 ─────────────────────────────────────────────────

    [Fact]
    public void 首都合法写进存档()
    {
        var state = new SaveState();
        var book = Book(Flags(), state);

        Assert.True(book.ChooseCapital("hub7").Ok);
        Assert.Equal("hub7", state.Prologue.Capital);

        Assert.True(book.ChooseCapital("hub1").Ok);
        Assert.Equal("hub1", state.Prologue.Capital);
    }

    [Fact]
    public void 首都非法被拒且存档不动()
    {
        var state = new SaveState();
        var book = Book(Flags(), state);

        foreach (var bad in new[] { "hub8", "hub0", "reg_jiang", "东都" })
        {
            var result = book.ChooseCapital(bad);
            Assert.False(result.Ok);
            Assert.Contains("hub1–hub7", result.Reason);
        }

        Assert.Contains("没给首都", book.ChooseCapital("").Reason);
        Assert.Contains("没给首都", book.ChooseCapital(null).Reason);
        Assert.Equal("", state.Prologue.Capital);
    }

    // ── 七、国号 ─────────────────────────────────────────────────

    [Fact]
    public void 国号必须表里有()
    {
        var state = new SaveState();
        var book = Book(Flags(), state);

        var bad = book.ChooseRealm("qin");
        Assert.False(bad.Ok);
        Assert.Contains("不在国号表里", bad.Reason);
        Assert.Equal("", state.Prologue.Realm);

        Assert.False(book.ChooseRealm("").Ok);
        Assert.False(book.ChooseRealm(null).Ok);

        var ok = book.ChooseRealm("liang");
        Assert.True(ok.Ok);
        Assert.Equal("liang", state.Prologue.Realm);
    }

    // ── 八、IsComplete / Missing ─────────────────────────────────

    [Fact]
    public void 四样齐了算完成()
    {
        var (book, _, _) = Ready();

        Assert.True(book.IsComplete);
        Assert.Empty(book.Missing());
    }

    [Fact]
    public void 缺首都不算完成且说明差定都()
    {
        var (book, state, _) = Ready();
        state.Prologue.Capital = "";

        Assert.False(book.IsComplete);
        Assert.Contains("还没定都。", book.Missing());
        Assert.Single(book.Missing());
    }

    [Fact]
    public void 缺国号不算完成且说明差国号()
    {
        var (book, state, _) = Ready();
        state.Prologue.Realm = "";

        Assert.False(book.IsComplete);
        Assert.Contains("还没定国号。", book.Missing());
    }

    [Fact]
    public void 国策只有两条不算完成()
    {
        var (book, state, _) = Ready();
        state.Prologue.Oaths.RemoveAt(2);

        Assert.False(book.IsComplete);
        Assert.Contains("国策要立 3 条，现在 2 条。", book.Missing());
    }

    [Fact]
    public void 节拍不足十二拍不算完成()
    {
        var flags = Flags(AllFourPicks);
        var state = WithBeats(new SaveState(), 11);
        var book = Book(flags, state);

        Assert.True(book.ChooseCapital("hub1").Ok);
        Assert.True(book.ChooseRealm("yan").Ok);
        Assert.True(book.ChooseOaths(new[] { "salt", "nosurrender", "land" }).Ok);

        Assert.False(book.IsComplete);
        Assert.Contains("节拍至少要走完 12 拍，现在 11 拍。", book.Missing());

        state.Prologue.BeatsDone.Add("b12");
        Assert.True(book.IsComplete);
    }

    [Fact]
    public void 全空时缺三样_没许过承诺就不缺国策()
    {
        // 一条承诺都没有时，国策这步根本不存在（要立 0 条），只缺定都/国号/节拍
        var book = Book(Flags());

        Assert.Equal(3, book.Missing().Count);
        Assert.DoesNotContain(book.Missing(), m => m.Contains("国策"));
        Assert.False(book.IsComplete);
    }

    [Fact]
    public void 只许过一条承诺_立一条就算齐()
    {
        // 十二拍里只有三拍能生成承诺；只做过 b07 那一张的玩家不该被卡在门口
        var flags = Flags("beat.b07.pick.1");
        var state = WithBeats(new SaveState(), 12);
        var book = Book(flags, state);

        var result = book.ChooseOaths(new[] { "salt" });  // 内联表里 b07:1 那张叫 salt
        Assert.True(result.Ok);

        Assert.DoesNotContain(book.Missing(), m => m.Contains("国策"));
    }

    // ── 九、Finish ───────────────────────────────────────────────

    [Fact]
    public void 齐了Finish置旗标()
    {
        var (book, state, flags) = Ready();

        var result = book.Finish();

        Assert.True(result.Ok);
        Assert.True(flags.Has(TransitionBook.DoneFlag, null));
        Assert.True(state.Flags[TransitionBook.DoneFlag]);
        Assert.True(book.IsDone);
    }

    [Fact]
    public void 差东西Finish不置旗标并说明()
    {
        var flags = Flags("beat.b07.pick.1");
        var state = new SaveState();
        var book = Book(flags, state);

        var result = book.Finish();

        Assert.False(result.Ok);
        Assert.Contains("还没定都。", result.Reason);
        Assert.Contains("还没定国号。", result.Reason);
        Assert.False(flags.Has(TransitionBook.DoneFlag, null));
        Assert.False(state.Flags.ContainsKey(TransitionBook.DoneFlag));
        Assert.False(book.IsDone);
    }

    // ── 十、存档 v4 → v5 ────────────────────────────────────────

    [Fact]
    public void v4存档读进来转段四样为空其余照旧()
    {
        var json =
            "{\"version\":4,\"day\":12,\"seed\":7,\"hub\":\"hub3\"," +
            "\"prologue\":{\"beats_done\":[\"b01\",\"b02\"],\"troops\":40}}";

        var state = SaveState.FromJson(json);

        Assert.Equal(SaveState.CurrentVersion, state.Version);
        Assert.Equal("", state.Prologue.Capital);
        Assert.Equal("", state.Prologue.Realm);
        Assert.Empty(state.Prologue.Oaths);
        Assert.Empty(state.Prologue.Debts);

        // 其余一个不动
        Assert.Equal(12, state.Day);
        Assert.Equal(7, state.Seed);
        Assert.Equal("hub3", state.Hub);
        Assert.Equal(40, state.Prologue.Troops);
        Assert.Equal(new[] { "b01", "b02" }, state.Prologue.BeatsDone);
    }

    [Fact]
    public void 转段四样写得进也读得回来()
    {
        var state = new SaveState { Day = 30 };
        state.Prologue.Capital = "hub5";
        state.Prologue.Realm = "xie";
        state.Prologue.Oaths = new List<string> { "salt", "nosurrender", "land" };
        state.Prologue.Debts = new List<string> { "vanguard" };

        var json = state.ToJson();
        Assert.Contains("\"capital\": \"hub5\"", json);
        Assert.Contains("\"realm\": \"xie\"", json);
        Assert.Contains("\"oaths\"", json);
        Assert.Contains("\"debts\"", json);

        var back = SaveState.FromJson(json);
        Assert.Equal(SaveState.CurrentVersion, back.Version);
        Assert.Equal("hub5", back.Prologue.Capital);
        Assert.Equal("xie", back.Prologue.Realm);
        Assert.Equal(new[] { "salt", "nosurrender", "land" }, back.Prologue.Oaths);
        Assert.Equal(new[] { "vanguard" }, back.Prologue.Debts);
        Assert.Equal(30, back.Day);
    }

    [Fact]
    public void 存档里缺这四项也补成空()
    {
        // v5 的档但 prologue 节写成了 null：补空壳，不抛。
        var state = SaveState.FromJson("{\"version\":5,\"prologue\":{\"capital\":null,\"oaths\":null}}");

        Assert.Equal("", state.Prologue.Capital);
        Assert.Empty(state.Prologue.Oaths);
        Assert.Empty(state.Prologue.Debts);
        Assert.Equal("", state.Prologue.Realm);
    }

    // ── 十一、真表体检（表还没到位就跳过）────────────────────────

    [Fact]
    public void 真表若已存在则至少三条誓约两个国号()
    {
        var oathPath = FindData("oaths.csv");
        var realmPath = FindData("realms.csv");
        if (oathPath == null || realmPath == null) return; // 数据还没写，不红

        var oaths = OathTable.Load(oathPath);
        var realms = RealmTable.Load(realmPath);

        Assert.True(oaths.Count >= 3, $"oaths.csv 只有 {oaths.Count} 条誓约，至少要 3 条。");
        Assert.True(realms.Count >= 2, $"realms.csv 只有 {realms.Count} 个国号，至少要 2 个。");

        foreach (var oath in oaths.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(oath.Text), $"誓约「{oath.Id}」没有 text。");
            Assert.True(FlagStore.IsValidName(oath.Source.FlagName),
                $"誓约「{oath.Id}」的 source 拼出来的旗标名不合法。");
        }

        foreach (var realm in realms.All)
            Assert.False(string.IsNullOrWhiteSpace(realm.Label), $"国号「{realm.Id}」没有 label。");
    }

    /// <summary>从测试输出目录往上找 <c>Content/data/&lt;name&gt;</c>；找不到返回 null。</summary>
    private static string? FindData(string name)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "Content", "data", name);
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }

        return null;
    }
}
