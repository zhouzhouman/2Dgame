using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 存档是玩家唯一会"丢"的东西。这里只验三件事：字段形状照 schema §C、
/// 缺字段补默认不多不少、类型不符当坏档。
/// </summary>
public class SaveStateTests
{
    private static SaveState 装满一档()
    {
        var s = new SaveState
        {
            Version = 2,
            Seed = 20260815,
            Day = 7,
            Tense = "prologue",
            Hub = "hub3",
        };

        s.Player.X = 4;
        s.Player.Y = 20;
        s.Flags["has_medicine"] = true;
        s.Flags["took_temple_coin"] = false;
        s.Inventory.Add(new InventoryEntry { Item = "bread", N = 1 });
        s.Inventory.Add(new InventoryEntry { Item = "shoe", N = 1 });
        s.Trust["prologue"] = new TrustState { Min = 12, Guan = 0, Yi = 8 };
        s.Known.Clues.Add("knows_herb_path");
        s.Known.Npcs.Add(new NpcState { Hub = "hub3", Id = "A", Met = true, Again = 2, Gift = false, Quest = 1 });

        var hub = new HubState { Entered = true, KeHalf = 14, CaughtCount = 1, Nudges = 0 };
        hub.Searched.Add("bf_01");
        hub.Searched.Add("bf_03");
        hub.Flags["path_backhill_open"] = false;
        hub.Patients["D"] = "alive";
        hub.Patients["E"] = "dying";
        hub.Patrol["G"] = new PatrolState { Wp = 2, T = 13.4 };
        s.Hubs["hub3"] = hub;

        return s;
    }

    [Fact]
    public void 存进去再读出来还是原来那档()
    {
        var a = 装满一档();

        var b = SaveState.FromJson(a.ToJson());

        Assert.Equal(2, b.Version);
        Assert.Equal(20260815, b.Seed);
        Assert.Equal(7, b.Day);
        Assert.Equal("prologue", b.Tense);
        Assert.Equal("hub3", b.Hub);
        Assert.Equal(4, b.Player.X);
        Assert.Equal(20, b.Player.Y);
        Assert.True(b.Flags["has_medicine"]);
        Assert.False(b.Flags["took_temple_coin"]);
        Assert.Equal(2, b.Inventory.Count);
        Assert.Equal("bread", b.Inventory[0].Item);
        Assert.Equal(12, b.Trust["prologue"].Min);
        Assert.Equal(8, b.Trust["prologue"].Yi);
        Assert.Equal("knows_herb_path", b.Known.Clues[0]);
        Assert.Equal("A", b.Known.Npcs[0].Id);
        Assert.Equal(2, b.Known.Npcs[0].Again);
        Assert.True(b.Hubs.ContainsKey("hub3"));
        Assert.Equal(14, b.Hubs["hub3"].KeHalf);
        Assert.Equal(1, b.Hubs["hub3"].CaughtCount);
        Assert.Equal(new[] { "bf_01", "bf_03" }, b.Hubs["hub3"].Searched);
        Assert.Equal("dying", b.Hubs["hub3"].Patients["E"]);
        Assert.Equal(13.4, b.Hubs["hub3"].Patrol["G"].T);
    }

    [Fact]
    public void 中文不转义()
    {
        var s = new SaveState();
        s.Known.Clues.Add("白幡村的井");

        var json = s.ToJson();

        Assert.Contains("白幡村的井", json);
        Assert.DoesNotContain("\\u767d", json);
    }

    [Fact]
    public void 缩进两格加小驼峰()
    {
        var json = new SaveState().ToJson();

        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\n  \"day\": 1", json);
    }

    [Fact]
    public void 缺字段补默认值()
    {
        var s = SaveState.FromJson("{}");

        Assert.Equal(2, s.Version);
        Assert.Equal(0, s.Seed);
        Assert.Equal(1, s.Day);
        Assert.Equal("prologue", s.Tense);
        Assert.Equal("hub1", s.Hub);
        Assert.Empty(s.Flags);
        Assert.Empty(s.Inventory);
        Assert.Empty(s.Trust);
        Assert.Empty(s.Known.Clues);
        Assert.Empty(s.Known.Npcs);
        Assert.Empty(s.Hubs);
    }

    [Fact]
    public void 半刻与被发现次数两种拼法都认()
    {
        var s = SaveState.FromJson("{\"hubs\":{\"hub3\":{\"keHalf\":14,\"caughtCount\":2}}}");

        Assert.Equal(14, s.Hubs["hub3"].KeHalf);
        Assert.Equal(2, s.Hubs["hub3"].CaughtCount);
    }

    [Fact]
    public void 字段严格照存档契约()
    {
        // 直接断言序列化出来的 JSON 键，而不是反射属性名：
        // 反射看不见"只读计算属性其实也会被写进 JSON"这类问题。
        var json = new SaveState().ToJson();
        using var doc = JsonDocument.Parse(json);

        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, System.StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[] { "day", "flags", "hub", "hubs", "inventory", "known", "player", "seed", "tense", "trust", "version" },
            keys);
    }

    [Fact]
    public void 版本判断不进存档()
    {
        var json = new SaveState().ToJson();

        // 这三个是只读计算属性，只给代码看，不许写进存档。
        Assert.DoesNotContain("isSupported", json);
        Assert.DoesNotContain("needsMigration", json);
        Assert.DoesNotContain("isTooNew", json);
    }

    [Fact]
    public void 嵌套对象的字段也照契约()
    {
        var s = new SaveState();
        s.Hubs["hub3"] = new HubState { Entered = true, KeHalf = 14, CaughtCount = 1 };

        using var doc = JsonDocument.Parse(s.ToJson());

        var hub = doc.RootElement.GetProperty("hubs").GetProperty("hub3");
        var hubKeys = hub.EnumerateObject().Select(p => p.Name).OrderBy(n => n, System.StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[] { "caught_count", "entered", "flags", "ke_half", "nudges", "patients", "patrol", "searched" },
            hubKeys);
    }

    [Fact]
    public void 缺子对象也不炸()
    {
        var s = SaveState.FromJson("{\"version\":2,\"day\":3,\"hubs\":{\"hub3\":{}}}");

        Assert.Equal(3, s.Day);
        Assert.NotNull(s.Player);
        Assert.NotNull(s.Hubs["hub3"].Flags);
        Assert.NotNull(s.Hubs["hub3"].Searched);
        Assert.Equal(0, s.Hubs["hub3"].KeHalf);
    }

    [Fact]
    public void 未知字段原样保留还写得回去()
    {
        var json = "{\"version\":2,\"future_thing\":{\"a\":1},\"hubs\":{\"hub3\":{\"future_hub\":true}}}";

        var s = SaveState.FromJson(json);
        var again = s.ToJson();

        Assert.Contains("future_thing", again);
        Assert.Contains("future_hub", again);
    }

    [Fact]
    public void 类型不符当坏档()
    {
        Assert.Throws<JsonException>(() => SaveState.FromJson("{\"version\":\"二\"}"));
        Assert.Throws<JsonException>(() => SaveState.FromJson("{\"day\":\"七天\"}"));
        Assert.Throws<JsonException>(() => SaveState.FromJson("{\"flags\":[]}"));
    }

    [Fact]
    public void 版本号认得清()
    {
        Assert.True(SaveState.IsSupportedVersion(2));
        Assert.True(SaveState.IsSupportedVersion(1));
        Assert.False(SaveState.IsSupportedVersion(99));
        Assert.False(SaveState.IsSupportedVersion(0));

        var current = new SaveState();
        Assert.True(current.IsSupported);
        Assert.False(current.NeedsMigration);
        Assert.False(current.IsTooNew);

        var old = new SaveState { Version = 1 };
        Assert.True(old.NeedsMigration);

        var future = new SaveState { Version = 99 };
        Assert.True(future.IsTooNew);
        Assert.False(future.IsSupported);
    }

    [Fact]
    public void 刻账能从存档里取出来()
    {
        var s = 装满一档();

        var clock = Clock.FromState("hub3", s.Hubs["hub3"].ToClockState());

        Assert.Equal(14, clock.KeHalf);
        Assert.Equal(32, clock.KeBudgetHalf);
    }

    [Fact]
    public void 刻账写回存档对得上()
    {
        var s = new SaveState();
        var clock = new Clock("hub3");
        clock.SpendSearch();
        clock.SpendSearch();

        var state = clock.ToState();
        s.Hubs["hub3"] = new HubState { Entered = true, KeHalf = state.KeHalf, Nudges = state.Nudges };

        var back = SaveState.FromJson(s.ToJson());

        Assert.Equal(4, back.Hubs["hub3"].KeHalf);
        Assert.Equal(clock.Nudges, back.Hubs["hub3"].Nudges);
    }

    [Fact]
    public void 空字符串是坏档()
    {
        Assert.Throws<JsonException>(() => SaveState.FromJson(""));
    }

    [Fact]
    public void 存档也能装进字典再读出来()
    {
        var s = 装满一档();

        var json = s.ToJson();
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        Assert.NotNull(dict);
        Assert.True(dict!.ContainsKey("hubs"));
    }
}
