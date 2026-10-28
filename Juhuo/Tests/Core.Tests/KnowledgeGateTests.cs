using System.Collections.Generic;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 知识门求值（票号 S1-02）。规则源：schema_v2.md §A.1、rule_time_clock.md §例外 3。
/// 门关着时要说得出缺哪几项，UI 才能提示。
/// </summary>
public class KnowledgeGateTests
{
    private static FlagProbe On(params string[] names)
    {
        var set = new HashSet<string>(names);
        return set.Contains;
    }

    [Fact]
    public void 条件满足时门开着()
    {
        var r = KnowledgeGate.Check("sees_ledger", On("sees_ledger"));

        Assert.True(r.Open);
        Assert.Empty(r.Missing);
        Assert.False(r.IsSilentlyClosed);
    }

    [Fact]
    public void 条件不满足时门关着并报出缺项()
    {
        var r = KnowledgeGate.Check("sees_ledger", On("other"));

        Assert.False(r.Open);
        Assert.Equal(new[] { "sees_ledger" }, r.Missing);
    }

    [Fact]
    public void 多项缺时全列出来()
    {
        var r = KnowledgeGate.Check("a&b&c", On("b"));

        Assert.False(r.Open);
        Assert.Equal(new[] { "a", "c" }, r.Missing);
    }

    [Fact]
    public void 取反项缺的时候带叹号显示()
    {
        // !met 的意思是"还没见过"；玩家已经见过，所以这项不成立
        var r = KnowledgeGate.Check("a&!met", On("a", "met"));

        Assert.False(r.Open);
        Assert.Equal(new[] { "!met" }, r.Missing);
    }

    [Fact]
    public void 空requires视为无条件开()
    {
        Assert.True(KnowledgeGate.Check("", On()).Open);
        Assert.True(KnowledgeGate.Check("   ", On()).Open);
        Assert.True(KnowledgeGate.Check(null, On()).Open);
    }

    [Fact]
    public void 非法表达式视为关且给不出提示()
    {
        var r = KnowledgeGate.Check("a&&b", On("a", "b"));

        Assert.False(r.Open);
        Assert.Empty(r.Missing);
        Assert.True(r.IsSilentlyClosed);
    }

    [Fact]
    public void 或表达式全部不成立时两项都报()
    {
        var r = KnowledgeGate.Check("a|b", On());

        Assert.False(r.Open);
        Assert.Equal(new[] { "a", "b" }, r.Missing);
    }

    [Fact]
    public void 或表达式成立时门开着()
    {
        var r = KnowledgeGate.Check("a|b", On("b"));

        Assert.True(r.Open);
        Assert.Empty(r.Missing);
    }
}
