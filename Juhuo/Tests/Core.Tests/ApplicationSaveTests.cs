using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 存档是玩家唯一会"丢"的东西。这里的每一条都对应一个真实会发生的场景：
/// 写完读回来、字段缺了、文件被手改坏、磁盘写不动、备份也坏、版本比这份新。
///
/// 存档的形状由 Core 的 SaveState 管，这里只验「落盘」这一段：
/// 原子写入、坏档回退、错误可读。
/// </summary>
public class ApplicationSaveTests
{
    private const string SavePath = "user://save.json";
    private const string TempPath = "user://save.tmp";
    private const string BackupPath = "user://save.bak";

    /// <summary>内存里的文件系统。写不动、读炸了都能模拟。</summary>
    private sealed class FakeFs
    {
        public readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);

        /// <summary>写这些路径时抛异常。null 表示都能写。</summary>
        public Func<string, bool>? FailOnWrite;

        /// <summary>读这些路径时抛异常。null 表示都能读。</summary>
        public Func<string, bool>? FailOnRead;

        public SaveFileSystem Build() => new()
        {
            Exists = p => Files.ContainsKey(p),
            Read = p =>
            {
                if (FailOnRead != null && FailOnRead(p)) throw new IOException("读不出来。");
                return Files.TryGetValue(p, out var t) ? t : null;
            },
            Write = (p, text) =>
            {
                if (FailOnWrite != null && FailOnWrite(p)) throw new IOException("磁盘写不动。");
                Files[p] = text;
            },
            Copy = (from, to) =>
            {
                if (!Files.TryGetValue(from, out var t)) throw new FileNotFoundException(from);
                Files[to] = t;
            },
            Move = (from, to) =>
            {
                if (!Files.TryGetValue(from, out var t)) throw new FileNotFoundException(from);
                Files[to] = t;
                Files.Remove(from);
            },
            Delete = p => Files.Remove(p),
            Now = () => new DateTime(2026, 10, 28, 12, 0, 0),
        };
    }

    private static SaveState 装满一档()
    {
        var s = new SaveState
        {
            Version = SaveState.CurrentVersion,
            Seed = 20260815,
            Day = 7,
            Tense = "prologue",
            Hub = "hub3",
        };

        s.Player.X = 4.5;
        s.Player.Y = 20.25;
        s.Flags["has_medicine"] = true;
        s.Flags["took_temple_coin"] = false;
        s.Inventory.Add(new InventoryEntry { Item = "bread", N = 1 });
        s.Known.Clues.Add("knows_herb_path");
        s.Known.Npcs.Add(new NpcState { Hub = "hub3", Id = "A", Met = true, Again = 2 });
        s.Trust["prologue"] = new TrustState { Min = 12, Guan = 0, Yi = 8 };

        var hub = new HubState { Entered = true, KeHalf = 14, Nudges = 1, CaughtCount = 1 };
        hub.Searched.Add("bf_01");
        hub.Searched.Add("bf_03");
        hub.Flags["path_backhill_open"] = false;
        hub.Patients["D"] = "alive";
        s.Hubs["hub3"] = hub;

        return s;
    }

    [Fact]
    public void 写进去再读出来还是原来那档()
    {
        var fs = new FakeFs();
        var save = new SaveService(fs.Build());
        var a = 装满一档();

        var (ok, error) = save.TrySave(a);
        Assert.True(ok, error);

        var (b, loadError) = save.TryLoad();
        Assert.NotNull(b);
        Assert.Null(loadError);

        Assert.Empty(SaveDiff.Compare(a, b!));
    }

    [Fact]
    public void 缺字段取默认值()
    {
        var fs = new FakeFs();
        fs.Files[SavePath] = "{}";

        var save = new SaveService(fs.Build());
        var (state, error) = save.TryLoad();

        Assert.NotNull(state);
        Assert.Null(error);
        Assert.Equal(2, state!.Version);
        Assert.Equal(1, state.Day);
        Assert.Equal("hub1", state.Hub);
        Assert.Empty(state.Flags);
        Assert.Empty(state.Inventory);
        Assert.Empty(state.Hubs);
    }

    [Fact]
    public void 坏档给一句能读的话而不是抛异常()
    {
        var fs = new FakeFs();
        fs.Files[SavePath] = "{ 这不是 JSON ###";

        var save = new SaveService(fs.Build());

        var (state, error) = save.TryLoad();

        Assert.Null(state);
        Assert.NotNull(error);
        Assert.Contains("读不懂", error);
    }

    [Fact]
    public void 写不动的时候旧档还在()
    {
        var fs = new FakeFs();
        var save = new SaveService(fs.Build());

        // 先写一份好的
        Assert.True(save.TrySave(装满一档()).Ok);
        var before = fs.Files[SavePath];

        // 这次临时档写不动
        fs.FailOnWrite = p => p == TempPath;

        var a = 装满一档();
        a.Day = 8;

        var (ok, error) = save.TrySave(a);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal(before, fs.Files[SavePath]);      // 旧档一根汗毛都没掉
    }

    [Fact]
    public void 旗标与刻能存进去也能读出来()
    {
        var fs = new FakeFs();
        var save = new SaveService(fs.Build());

        var state = new SaveState { Hub = "hub3" };
        state.Flags["has_shoe"] = true;
        state.Flags["path_backhill_open"] = false;

        var clock = new Clock("hub3");
        clock.SpendSearch(1);
        clock.SpendSearch(1);
        var cs = clock.ToState();
        var hub = new HubState { Entered = true, KeHalf = cs.KeHalf, Nudges = cs.Nudges };
        state.Hubs["hub3"] = hub;

        Assert.True(save.TrySave(state).Ok);

        var (back, error) = save.TryLoad();
        Assert.NotNull(back);
        Assert.Null(error);

        Assert.True(back!.Flags["has_shoe"]);
        Assert.False(back.Flags["path_backhill_open"]);
        Assert.Equal(4, back.Hubs["hub3"].KeHalf);

        // 刻账回到 Core 手里还能用
        var clock2 = Clock.FromState("hub3", back.Hubs["hub3"].ToClockState());
        Assert.Equal(4, clock2.KeHalf);
        Assert.Equal(2, clock2.KeUsedKe);
    }

    [Fact]
    public void 版本太新明确说不读()
    {
        var fs = new FakeFs();
        fs.Files[SavePath] = "{\"version\":99,\"hub\":\"hub3\"}";

        var save = new SaveService(fs.Build());
        var (state, error) = save.TryLoad();

        Assert.Null(state);
        Assert.NotNull(error);
        Assert.StartsWith(SaveService.TooNewPrefix, error);
    }

    [Fact]
    public void 版本太新连文件都不许动()
    {
        var fs = new FakeFs();
        fs.Files[SavePath] = "{\"version\":99,\"hub\":\"hub3\"}";

        var save = new SaveService(fs.Build());
        var result = save.LoadOrRecover();

        Assert.True(result.Blocked);
        Assert.Null(result.State);
        Assert.Contains("{\"version\":99", fs.Files[SavePath]);    // 一字没改
        Assert.DoesNotContain(fs.Files.Keys, p => p.Contains("corrupt")); // 也不许改名
    }

    [Fact]
    public void 坏档改名留底然后从备份救回来()
    {
        var fs = new FakeFs();
        var save = new SaveService(fs.Build());

        // 先有一份好档，让它变成备份
        Assert.True(save.TrySave(装满一档()).Ok);
        var good = fs.Files[SavePath];
        fs.Files[BackupPath] = good;

        // 正式档被手改坏
        fs.Files[SavePath] = "坏掉的内容";

        var result = save.LoadOrRecover();

        Assert.Equal(SaveOrigin.Backup, result.Origin);
        Assert.NotNull(result.State);
        Assert.Equal("你只记得上次歇脚的地方。", result.Notice);

        // 坏档改名留底，内容一字不改
        var corrupt = fs.Files.Keys.Where(p => p.Contains("corrupt")).ToArray();
        Assert.Single(corrupt);
        Assert.Equal("坏掉的内容", fs.Files[corrupt[0]]);
        Assert.Contains("20261028-120000", corrupt[0]);
    }

    [Fact]
    public void 备份也坏就新开档()
    {
        var fs = new FakeFs();
        fs.Files[SavePath] = "坏掉的内容";
        fs.Files[BackupPath] = "备份也坏";

        var save = new SaveService(fs.Build());
        var result = save.LoadOrRecover();

        Assert.Equal(SaveOrigin.Fresh, result.Origin);
        Assert.Null(result.State);
        Assert.Equal("前面的路，你记不清了。", result.Notice);

        // 两份坏档都留底，一份都不删
        Assert.Equal(2, fs.Files.Keys.Count(p => p.Contains("corrupt")));
    }

    [Fact]
    public void 根本没有档不算错()
    {
        var fs = new FakeFs();
        var save = new SaveService(fs.Build());

        var result = save.LoadOrRecover();

        Assert.Equal(SaveOrigin.None, result.Origin);
        Assert.Null(result.State);
        Assert.Null(result.Error);
    }

    [Fact]
    public void 读不出来的时候也给一句人话()
    {
        var fs = new FakeFs();
        fs.Files[SavePath] = "{}";
        fs.FailOnRead = p => p == SavePath;

        var save = new SaveService(fs.Build());
        var (state, error) = save.TryLoad();

        Assert.Null(state);
        Assert.NotNull(error);
        Assert.Contains("读不出来", error);
    }

    [Fact]
    public void 写档前先查上限()
    {
        var state = new SaveState();
        for (var i = 0; i < 13; i++) state.Inventory.Add(new InventoryEntry { Item = "x", N = 1 });

        var error = SaveService.Validate(state);

        Assert.NotNull(error);
        Assert.Contains("背包装不下", error);

        var tooManyFlags = new SaveState();
        for (var i = 0; i < FlagStore.MaxGlobalFlags + 1; i++) tooManyFlags.Flags[$"k{i}"] = true;

        Assert.Contains("旗标太多", SaveService.Validate(tooManyFlags));
    }

    [Fact]
    public void 逐字段比对能看出差在哪()
    {
        var a = 装满一档();
        var b = 装满一档();
        b.Day = 8;
        b.Hubs["hub3"].KeHalf = 20;

        var diffs = SaveDiff.Compare(a, b);

        Assert.Equal(2, diffs.Count);
        Assert.Contains(diffs, d => d.StartsWith("day"));
        Assert.Contains(diffs, d => d.Contains("ke_half"));
    }

    [Fact]
    public void 旗标表达式按公式求值()
    {
        var flags = new FlagStore();
        flags.Set("has_letter_unsent");

        Assert.True(FlagExpression.Evaluate("", flags, "hub6"));
        Assert.True(FlagExpression.Evaluate("has_letter_unsent", flags, "hub6"));
        Assert.False(FlagExpression.Evaluate("!has_letter_unsent", flags, "hub6"));
        Assert.True(FlagExpression.Evaluate("!knows_herb_path", flags, "hub6"));
        Assert.True(FlagExpression.Evaluate("has_letter_unsent&!knows_herb_path", flags, "hub6"));
        Assert.False(FlagExpression.Evaluate("has_letter_unsent&knows_herb_path", flags, "hub6"));

        // 缺失即 false
        Assert.False(FlagExpression.Evaluate("knows_herb_path", flags, "hub6"));
    }

    [Fact]
    public void 旗标取值只有一条路()
    {
        var flags = new FlagStore();
        flags.Set("hub3_well_shoe_taken");
        flags.SetLocal("hub3_well_shoe_taken", "hub3");

        // 全局优先：全局有键就用全局的
        Assert.True(flags.Has("hub3_well_shoe_taken", "hub3"));
        Assert.True(flags.Has("hub3_well_shoe_taken", "hub5"));

        // 局部只有本空间读得到
        flags.SetLocal("path_backhill_open", "hub5");
        Assert.True(flags.Has("path_backhill_open", "hub5"));
        Assert.False(flags.Has("path_backhill_open", "hub3"));
    }
}
