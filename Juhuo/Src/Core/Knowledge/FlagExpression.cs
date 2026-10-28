using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>
/// 旗标取值委托：给一个旗标名，答"置位了没有"。
/// 表达式求值只认这个委托，不认识 FlagStore、不认识存档——用测试里的字典也能跑。
/// </summary>
/// <param name="name">旗标名（已去掉 <c>!</c> 与空白）。</param>
/// <returns>置位返回 true；没置位或查不到都返回 false。</returns>
public delegate bool FlagProbe(string name);

/// <summary>表达式里的一个原子项：旗标名 + 是否取反。</summary>
/// <param name="Name">旗标名。</param>
/// <param name="Negated">是否写成 <c>!name</c>。</param>
public readonly record struct FlagTerm(string Name, bool Negated);

/// <summary>
/// 旗标表达式求值（schema_v2.md §A.1 的 <c>requires</c> 列）。
///
/// 文法（D-017 允许点号与下划线两种名字）：
/// <code>
/// expr   := or
/// or     := and ('|' and)*
/// and    := unary ('&amp;' unary)*
/// unary  := '!' unary | primary
/// primary:= '(' expr ')' | name
/// name   := 除空白与 ! &amp; | ( ) 外的任意连续字符（含 '.' 与汉字）
/// </code>
///
/// 边界（rule_flags_save.md §公式 / 票面 S1-12）：
/// - 空串 / 全空白 = 无条件成立，返回 true；
/// - 未知旗标名由委托答 false，不抛；
/// - 语法错误（括号不配对、运算符缺边、空项）一律返回 false，**不抛异常**——
///   坏数据只该让门关上，不该让游戏崩。
/// </summary>
public static class FlagExpression
{
    /// <summary>括号嵌套上限。超过按语法错误处理，防止坏数据把栈打穿。</summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// 求一条旗标表达式。
    /// </summary>
    /// <param name="expr">表达式。空串 = 无条件成立。</param>
    /// <param name="probe">旗标取值委托。null 视为"任何旗标都不成立"。</param>
    /// <returns>成立返回 true；语法错误返回 false。</returns>
    public static bool Evaluate(string? expr, FlagProbe? probe)
    {
        if (string.IsNullOrWhiteSpace(expr)) return true;

        var node = Parse(expr!);
        if (node == null) return false;

        return node.Eval(probe ?? (_ => false));
    }

    /// <summary>
    /// 表达式里出现的旗标名（按出现顺序去重，不含 <c>!</c>）。给存档与自检用。
    /// </summary>
    /// <param name="expr">表达式。</param>
    /// <returns>旗标名数组；空串或语法错误返回空数组。</returns>
    public static string[] Names(string? expr)
    {
        var terms = Terms(expr);
        if (terms.Count == 0) return Array.Empty<string>();

        var names = new List<string>(terms.Count);
        foreach (var t in terms)
            if (!names.Contains(t.Name)) names.Add(t.Name);

        return names.ToArray();
    }

    /// <summary>
    /// 表达式里的原子项（按出现顺序，不去重，保留取反标记）。
    /// 知识门用它算出"缺哪几项"。
    /// </summary>
    /// <param name="expr">表达式。</param>
    /// <returns>原子项列表；空串或语法错误返回空列表。</returns>
    public static IReadOnlyList<FlagTerm> Terms(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return Array.Empty<FlagTerm>();

        var node = Parse(expr!);
        if (node == null) return Array.Empty<FlagTerm>();

        var list = new List<FlagTerm>(4);
        node.Collect(list);
        return list;
    }

    // ————————————————————————— 解析 —————————————————————————

    private static Node? Parse(string text)
    {
        var parser = new Parser(text);
        var node = parser.ParseOr(0);
        if (node == null) return null;

        parser.SkipSpace();
        return parser.AtEnd ? node : null;
    }

    private abstract class Node
    {
        public abstract bool Eval(FlagProbe probe);

        public abstract void Collect(List<FlagTerm> into);
    }

    private sealed class AtomNode : Node
    {
        private readonly string _name;
        private readonly bool _negated;

        public AtomNode(string name, bool negated)
        {
            _name = name;
            _negated = negated;
        }

        public override bool Eval(FlagProbe probe)
        {
            var v = probe(_name);
            return _negated ? !v : v;
        }

        public override void Collect(List<FlagTerm> into) => into.Add(new FlagTerm(_name, _negated));

        /// <summary>取反后再来一层 <c>!</c>：折进原子项，这样 <c>!</c> 不会丢掉取名。</summary>
        public AtomNode Flipped() => new(_name, !_negated);
    }

    private sealed class AndNode : Node
    {
        private readonly Node _left;
        private readonly Node _right;

        public AndNode(Node left, Node right)
        {
            _left = left;
            _right = right;
        }

        public override bool Eval(FlagProbe probe) => _left.Eval(probe) && _right.Eval(probe);

        public override void Collect(List<FlagTerm> into)
        {
            _left.Collect(into);
            _right.Collect(into);
        }
    }

    private sealed class OrNode : Node
    {
        private readonly Node _left;
        private readonly Node _right;

        public OrNode(Node left, Node right)
        {
            _left = left;
            _right = right;
        }

        public override bool Eval(FlagProbe probe) => _left.Eval(probe) || _right.Eval(probe);

        public override void Collect(List<FlagTerm> into)
        {
            _left.Collect(into);
            _right.Collect(into);
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s;

        public bool AtEnd => _i >= _s.Length;

        public void SkipSpace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        public Node? ParseOr(int depth)
        {
            if (depth > MaxDepth) return null;

            var left = ParseAnd(depth);
            if (left == null) return null;

            while (true)
            {
                SkipSpace();
                if (AtEnd || _s[_i] != '|') return left;

                _i++;
                var right = ParseAnd(depth);
                if (right == null) return null;
                left = new OrNode(left, right);
            }
        }

        private Node? ParseAnd(int depth)
        {
            if (depth > MaxDepth) return null;

            var left = ParseUnary(depth);
            if (left == null) return null;

            while (true)
            {
                SkipSpace();
                if (AtEnd || _s[_i] != '&') return left;

                _i++;
                var right = ParseUnary(depth);
                if (right == null) return null;
                left = new AndNode(left, right);
            }
        }

        private Node? ParseUnary(int depth)
        {
            if (depth > MaxDepth) return null;

            SkipSpace();
            if (AtEnd) return null;

            if (_s[_i] == '!')
            {
                _i++;
                var inner = ParseUnary(depth + 1);
                return inner switch
                {
                    null => null,
                    AtomNode atom => atom.Flipped(),
                    _ => new NotNode(inner),
                };
            }

            return ParsePrimary(depth);
        }

        private Node? ParsePrimary(int depth)
        {
            if (depth > MaxDepth) return null;

            SkipSpace();
            if (AtEnd) return null;

            if (_s[_i] == '(')
            {
                _i++;
                var inner = ParseOr(depth + 1);
                if (inner == null) return null;

                SkipSpace();
                if (AtEnd || _s[_i] != ')') return null;

                _i++;
                return inner;
            }

            var start = _i;
            while (_i < _s.Length && IsNameChar(_s[_i])) _i++;
            if (_i == start) return null;

            return new AtomNode(_s[start.._i], false);
        }

        private static bool IsNameChar(char c) =>
            !char.IsWhiteSpace(c) && c != '!' && c != '&' && c != '|' && c != '(' && c != ')';
    }

    private sealed class NotNode : Node
    {
        private readonly Node _inner;

        public NotNode(Node inner) => _inner = inner;

        public override bool Eval(FlagProbe probe) => !_inner.Eval(probe);

        public override void Collect(List<FlagTerm> into) => _inner.Collect(into);
    }
}
