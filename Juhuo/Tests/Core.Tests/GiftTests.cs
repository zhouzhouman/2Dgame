using System;
using System.Collections.Generic;
using System.IO;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// "给他看"（票号 S1-03b）。哨的是序章关卡设计 §2.5 那条：
/// 不是送礼加好感，是同一件东西两种反应，区别只在"你看没看见"。
/// 所以 Matched（他认不认这件东西）与 KnownBefore（你事先知不知道）必须分开。
/// </summary>
public class GiftTests
{
    private const string MiniGifts =
        "npc,item,requires,sets,note\n" +
        "ahe,shoe,knows.shoe.inward,\"gift.ahe.shoe,gift.ahe.shoe.knew\",井边细看过才认\n" +
        "ahe,bread,,\"gift.ahe.bread\",\n" +
        "chen,bread,,\"gift.chen.bread\",\n";

    private static GiftTable Table() => GiftTable.FromCsv(MiniGifts);

    /// <summary>从测试输出目录往上找工程里的 Content/data（与 BeatMachineTests 同一套找法）。</summary>
    private static string? FindData(string fileName)
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

        return null;
    }

    /// <summary>把"置了哪几个旗标"做成取值委托，省得每条测试都写 lambda。</summary>
    private static FlagProbe Probe(params string[] on)
    {
        var set = new HashSet<string>(on, StringComparer.Ordinal);
        return name => set.Contains(name);
    }

    // ————————————————————————— CanShow —————————————————————————

    [Fact]
    public void 命中且事先知道时认得()
    {
        var table = Table();

        Assert.True(table.CanShow("ahe", "shoe", Probe("knows.shoe.inward")));
    }

    [Fact]
    public void 命中但事先不知道时不算认得()
    {
        var table = Table();

        Assert.False(table.CanShow("ahe", "shoe", Probe()));
    }

    [Fact]
    public void 没有requires的行谁都认得()
    {
        var table = Table();

        Assert.True(table.CanShow("ahe", "bread", Probe()));
        Assert.True(table.CanShow("chen", "bread", Probe()));
    }

    [Fact]
    public void 表里没有这一对就不算认得()
    {
        var table = Table();

        Assert.False(table.CanShow("chen", "shoe", Probe("knows.shoe.inward")));
        Assert.False(table.CanShow("nobody", "bread", Probe()));
        Assert.False(table.CanShow("", "bread", Probe()));
        Assert.False(table.CanShow("ahe", "", Probe()));
    }

    // ————————————————————————— Show —————————————————————————

    [Fact]
    public void 认得时置旗标且知道自己在看什么()
    {
        var table = Table();
        var set = new List<string>();

        var result = table.Show("ahe", "shoe", Probe("knows.shoe.inward"), set.Add);

        Assert.True(result.Matched);
        Assert.True(result.KnownBefore);
        Assert.Equal(new[] { "gift.ahe.shoe", "gift.ahe.shoe.knew" }, result.Applied);
        Assert.Equal(new[] { "gift.ahe.shoe", "gift.ahe.shoe.knew" }, set);
    }

    [Fact]
    public void 不认得时不置旗标但仍是命中()
    {
        var table = Table();
        var set = new List<string>();

        var result = table.Show("ahe", "shoe", Probe(), set.Add);

        // 命中的是"他有话"，只是这次是"这不是我的"。
        Assert.True(result.Matched);
        Assert.False(result.KnownBefore);
        Assert.Empty(result.Applied);
        Assert.Empty(set);
    }

    [Fact]
    public void 给错人不置旗标也不抛()
    {
        var table = Table();
        var set = new List<string>();

        var result = table.Show("chen", "shoe", Probe("knows.shoe.inward"), set.Add);

        Assert.False(result.Matched);
        Assert.False(result.KnownBefore);
        Assert.Empty(result.Applied);
        Assert.Empty(set);
    }

    [Fact]
    public void 拿一件表里没写的东西给谁看都无事发生()
    {
        var table = Table();
        var set = new List<string>();

        var result = table.Show("ahe", "算盘", Probe("knows.shoe.inward"), set.Add);

        Assert.False(result.Matched);
        Assert.Empty(result.Applied);
        Assert.Empty(set);
    }

    [Fact]
    public void 不传setFlag也只回该置的旗标()
    {
        var table = Table();

        var result = table.Show("chen", "bread", Probe());

        Assert.True(result.Matched);
        Assert.True(result.KnownBefore);
        Assert.Equal(new[] { "gift.chen.bread" }, result.Applied);
    }

    [Fact]
    public void 同一件东西给两个人各置各的()
    {
        var table = Table();
        var set = new List<string>();

        table.Show("ahe", "bread", Probe(), set.Add);
        table.Show("chen", "bread", Probe(), set.Add);

        Assert.Equal(new[] { "gift.ahe.bread", "gift.chen.bread" }, set);
    }

    // ————————————————————————— 坏表 —————————————————————————

    [Fact]
    public void sets里的旗标名不合法时带行号抛错()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            GiftTable.FromCsv(MiniGifts + "ahe,letter,,nope,\n"));

        Assert.Contains("第 5 行", ex.Message);
        Assert.Contains("不合法", ex.Message);
    }

    [Fact]
    public void 缺列时带行号抛错()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            GiftTable.FromCsv("npc,item,sets\n\"ahe\",\"bread\",\"gift.ahe.bread\"\n"));

        Assert.Contains("第 1 行", ex.Message);
        Assert.Contains("requires", ex.Message);
    }

    [Fact]
    public void 空npc或空item都抛错()
    {
        var emptyNpc = Assert.Throws<InvalidDataException>(() =>
            GiftTable.FromCsv(MiniGifts + ",bread,,\"gift.none.bread\",\n"));
        Assert.Contains("第 5 行", emptyNpc.Message);
        Assert.Contains("npc", emptyNpc.Message);

        var emptyItem = Assert.Throws<InvalidDataException>(() =>
            GiftTable.FromCsv(MiniGifts + "ahe,,,\"gift.ahe.none\",\n"));
        Assert.Contains("第 5 行", emptyItem.Message);
        Assert.Contains("item", emptyItem.Message);
    }

    [Fact]
    public void 真表能读进来且每行都有人有物()
    {
        var path = FindData("gifts.csv");
        if (path == null) return; // 数据还没生成：这条跳过，不算红。

        var table = GiftTable.Load(path);

        Assert.True(table.Count >= 1, "gifts.csv 是空的");
        Assert.All(table.All, g => Assert.False(string.IsNullOrWhiteSpace(g.Npc)));
        Assert.All(table.All, g => Assert.False(string.IsNullOrWhiteSpace(g.Item)));
    }
}
