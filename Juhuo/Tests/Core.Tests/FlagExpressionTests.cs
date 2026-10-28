using System.Collections.Generic;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 旗标表达式求值（票号 S1-12）。规则源：schema_v2.md §A.1、D-017。
/// 求值只走一个委托，所以这里用字典就能把灯全点亮，不需要 FlagStore。
/// </summary>
public class FlagExpressionTests
{
    private static FlagProbe On(params string[] names)
    {
        var set = new HashSet<string>(names);
        return set.Contains;
    }

    private static FlagProbe Probe() => On("a", "b");

    [Fact]
    public void 单项成立()
    {
        Assert.True(FlagExpression.Evaluate("a", Probe()));
    }

    [Fact]
    public void 单项不成立()
    {
        Assert.False(FlagExpression.Evaluate("a", On("b")));
    }

    [Fact]
    public void 取反()
    {
        Assert.False(FlagExpression.Evaluate("!a", Probe()));
        Assert.True(FlagExpression.Evaluate("!c", Probe()));
    }

    [Fact]
    public void 与()
    {
        Assert.True(FlagExpression.Evaluate("a&b", Probe()));
        Assert.False(FlagExpression.Evaluate("a&c", Probe()));
        Assert.False(FlagExpression.Evaluate("c&a", Probe()));
    }

    [Fact]
    public void 或()
    {
        Assert.True(FlagExpression.Evaluate("a|c", Probe()));
        Assert.True(FlagExpression.Evaluate("c|b", Probe()));
        Assert.False(FlagExpression.Evaluate("c|d", Probe()));
    }

    [Fact]
    public void 括号改变结合()
    {
        // (a|c)&b 与 a|c&b 不是一回事：后者被 & 绑得更紧
        Assert.True(FlagExpression.Evaluate("(a|c)&b", Probe()));
        Assert.True(FlagExpression.Evaluate("a|(c&b)", Probe()));
        Assert.False(FlagExpression.Evaluate("(a|c)&d", Probe()));

        // !(a&c) = 至少一个不成立
        Assert.True(FlagExpression.Evaluate("!(a&c)", Probe()));
        Assert.False(FlagExpression.Evaluate("!(a&b)", Probe()));
    }

    [Fact]
    public void 点号与下划线名字都认()
    {
        var probe = On("序章.账册.已读", "knows_herb_path");

        Assert.True(FlagExpression.Evaluate("序章.账册.已读&knows_herb_path", probe));
        Assert.False(FlagExpression.Evaluate("序章.账册.未读", probe));
        Assert.Equal(
            new[] { "序章.账册.已读", "knows_herb_path" },
            FlagExpression.Names("序章.账册.已读&knows_herb_path"));
    }

    [Fact]
    public void 未知名字当没置位不抛()
    {
        var probe = On("a");

        Assert.False(FlagExpression.Evaluate("从来没写过的旗标", probe));
        Assert.True(FlagExpression.Evaluate("!从来没写过的旗标", probe));
    }

    [Fact]
    public void 空串视为无条件成立()
    {
        Assert.True(FlagExpression.Evaluate("", Probe()));
        Assert.True(FlagExpression.Evaluate("   ", Probe()));
        Assert.True(FlagExpression.Evaluate(null, Probe()));
        Assert.Empty(FlagExpression.Names(""));
    }

    [Fact]
    public void 非法表达式一律返回false不抛()
    {
        var bad = new[]
        {
            "&a",       // 运算符在开头
            "a&",       // 运算符在结尾
            "a&&b",     // 空项
            "(a&b",     // 括号不配对
            "a&b)",     // 多一个右括号
            "a|",       // 或号缺右边
            "()",       // 空括号
        };

        foreach (var expr in bad)
        {
            Assert.False(FlagExpression.Evaluate(expr, Probe()));
            Assert.Empty(FlagExpression.Names(expr));
        }
    }

    [Fact]
    public void 委托为null时任何表达式都不成立()
    {
        Assert.False(FlagExpression.Evaluate("a", null));
        Assert.True(FlagExpression.Evaluate("", null));
    }

    [Fact]
    public void 深层嵌套不会打穿栈()
    {
        var deep = new string('(', 60) + "a" + new string(')', 60);
        Assert.False(FlagExpression.Evaluate(deep, Probe()));
    }

    [Fact]
    public void 解析出的原子项保留取反标记()
    {
        var terms = FlagExpression.Terms("a&!b");

        Assert.Equal(2, terms.Count);
        Assert.Equal(new FlagTerm("a", false), terms[0]);
        Assert.Equal(new FlagTerm("b", true), terms[1]);
    }
}
