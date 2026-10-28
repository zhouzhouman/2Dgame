using System.Collections.Generic;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 旗标只有一条取值规则：全局有键 > 空间局部 > false。
/// 这里把这条规则钉死，别的地方不许再写第二套。
/// 规则源：Docs/Rules/rule_flags_save.md §公式、§边界、§验收。
/// </summary>
public class FlagStoreTests
{
    [Fact]
    public void 没有的旗标是假的()
    {
        var f = new FlagStore();

        Assert.False(f.Has("has_shoe", "hub3"));
    }

    [Fact]
    public void 空名一律是假的()
    {
        var f = new FlagStore();
        f.Set("has_shoe");

        Assert.False(f.Has("", "hub3"));
        Assert.False(f.Has(null, "hub3"));
    }

    [Fact]
    public void hub未知时全局旗标仍可读()
    {
        var f = new FlagStore();
        f.Set("has_shoe");

        // 全局旗标本就不属于任何空间，不该因为 hub 拼错就消失（D-016）。
        Assert.True(f.Has("has_shoe", "hub99"));
        Assert.True(f.Has("has_shoe", ""));
        Assert.True(f.Has("has_shoe", null));
    }

    [Fact]
    public void hub未知时局部旗标读不到()
    {
        var f = new FlagStore();
        f.SetLocal("path_backhill_open", "hub3");

        Assert.False(f.Has("path_backhill_open", "hub99"));
        Assert.False(f.Has("path_backhill_open", ""));
        Assert.False(f.Has("path_backhill_open", null));
    }

    [Fact]
    public void 全局旗标在任何空间都读得到()
    {
        var f = new FlagStore();
        f.Set("has_medicine");

        Assert.True(f.Has("has_medicine", "hub1"));
        Assert.True(f.Has("has_medicine", "hub3"));
        Assert.True(f.Has("has_medicine", "hub7"));
    }

    [Fact]
    public void 全局优先于局部()
    {
        var f = new FlagStore();
        f.Set("path_backhill_open");
        f.SetLocal("path_backhill_open", "hub3");

        Assert.True(f.Has("path_backhill_open", "hub3"));
    }

    [Fact]
    public void 局部旗标只在本空间成立()
    {
        var f = new FlagStore();
        f.SetLocal("path_backhill_open", "hub3");

        Assert.True(f.Has("path_backhill_open", "hub3"));
        Assert.False(f.Has("path_backhill_open", "hub5"));
    }

    [Fact]
    public void 置位是幂等的()
    {
        var f = new FlagStore();

        f.Set("has_medicine");
        f.Set("has_medicine");
        f.Set("has_medicine");

        Assert.True(f.Has("has_medicine", "hub1"));
        Assert.Equal(1, f.GlobalCount);
    }

    [Fact]
    public void 清掉之后读作假()
    {
        var f = new FlagStore();
        f.Set("has_shoe");

        Assert.True(f.Clear("has_shoe"));

        Assert.False(f.Has("has_shoe", "hub3"));
        Assert.Equal(0, f.GlobalCount);
    }

    [Fact]
    public void 旧式旗标名照写不误()
    {
        var f = new FlagStore();

        Assert.True(f.Set("knows_herb_path"));
        Assert.True(f.Has("knows_herb_path", "hub3"));
        Assert.True(FlagStore.IsValidName("knows_herb_path"));
    }

    [Fact]
    public void 点号式旗标名合法()
    {
        // 新式：域.对象.状态，域可以用汉字（D-017）。
        Assert.True(FlagStore.IsValidName("序章.账册.已读"));
        Assert.True(FlagStore.IsValidName("序章.账册.已读.备份"));
        Assert.True(FlagStore.IsValidName("hub3.well.shoe_taken"));

        var f = new FlagStore();
        Assert.True(f.Set("序章.账册.已读"));
        Assert.True(f.Has("序章.账册.已读", "hub3"));
    }

    [Fact]
    public void 名字校验认新旧两种写法()
    {
        Assert.True(FlagStore.IsValidName("hub3_well_shoe_taken"));
        Assert.True(FlagStore.IsValidName("reg_fengyuan_gate_opened"));
        Assert.True(FlagStore.IsValidName("meta_tutorial_done"));

        Assert.False(FlagStore.IsValidName("HasShoe"));
        Assert.False(FlagStore.IsValidName("shoe"));
        Assert.False(FlagStore.IsValidName(""));
        Assert.False(FlagStore.IsValidName(null));
    }

    [Fact]
    public void 超长的名字不写()
    {
        var f = new FlagStore();
        var tooLong = new string('a', 41);

        Assert.False(f.Set(tooLong));
        Assert.Equal(0, f.GlobalCount);
    }

    [Fact]
    public void 名字长度按字节算()
    {
        // 规则卡 §边界：name ≤40 字节。汉字 1 字 = 3 字节。
        // 名字形状要合法，长度才是唯一的变量。
        var ascii40 = new string('a', 20) + "_" + new string('b', 19);
        var ascii41 = new string('a', 21) + "_" + new string('b', 19);

        Assert.True(FlagStore.IsValidName(ascii40));
        Assert.False(FlagStore.IsValidName(ascii41));

        // 8 个汉字 + ".账册.已读" = 24 + 14 = 38 字节，合法。
        Assert.True(FlagStore.IsValidName(new string('字', 8) + ".账册.已读"));

        // 9 个汉字 + ".账册.已读" = 27 + 14 = 41 字节，超了。
        Assert.False(FlagStore.IsValidName(new string('字', 9) + ".账册.已读"));
    }

    [Fact]
    public void 快照是副本改不动仓库()
    {
        var f = new FlagStore();
        f.Set("has_shoe");

        var snap = f.SnapshotGlobal();
        Assert.True(snap["has_shoe"]);

        Assert.True(f.Clear("has_shoe"));
        Assert.False(f.Has("has_shoe", "hub3"));
    }

    [Fact]
    public void 快照装载回来还是一样()
    {
        var f = new FlagStore();
        f.Set("has_shoe");
        f.SetLocal("path_backhill_open", "hub3");

        var global = f.SnapshotGlobal();
        var local = f.SnapshotLocal("hub3");

        var g = new FlagStore();
        g.Load(global, new Dictionary<string, IReadOnlyDictionary<string, bool>> { ["hub3"] = local });

        Assert.True(g.Has("has_shoe", "hub1"));
        Assert.True(g.Has("path_backhill_open", "hub3"));
        Assert.False(g.Has("path_backhill_open", "hub5"));
    }

    [Fact]
    public void 未知空间装不进来()
    {
        var f = new FlagStore();

        f.Load(
            new Dictionary<string, bool> { ["has_shoe"] = true },
            new Dictionary<string, IReadOnlyDictionary<string, bool>>
            {
                ["hub99"] = new Dictionary<string, bool> { ["evil"] = true },
            });

        Assert.True(f.Has("has_shoe", "hub1"));
        Assert.False(f.Has("evil", "hub1"));
    }

    [Fact]
    public void 从整档存档装载旗标()
    {
        var s = new SaveState();
        s.Flags["has_shoe"] = true;
        s.Hubs["hub3"] = new HubState();
        s.Hubs["hub3"].Flags["path_backhill_open"] = true;

        var f = new FlagStore();
        f.Load(s);

        Assert.True(f.Has("has_shoe", "hub1"));
        Assert.True(f.Has("path_backhill_open", "hub3"));
        Assert.False(f.Has("path_backhill_open", "hub5"));
    }
}
