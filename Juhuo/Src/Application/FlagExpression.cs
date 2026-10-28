using System;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 旗标表达式求值。
///
/// 表达式长这样（schema_v2.md §A.1 的 <c>requires</c> 列）：
///   <c>f</c>　　—— 旗标成立
///   <c>!f</c>　 —— 旗标不成立
///   <c>f&amp;g</c> —— 两个都成立（最多三项）
///
/// 每一项的取值一律走 <see cref="FlagStore.Has"/> —— 全局优先、全局没有才看空间的局部、
/// 都没有就是 false。这是全项目唯一的旗标取值入口（rule_flags_save.md §公式），
/// 这里只负责把字符串拆开，不自己判真假。
/// </summary>
public static class FlagExpression
{
    /// <summary>
    /// 求一条旗标表达式。
    /// </summary>
    /// <param name="expr">表达式。空串 = 无条件成立。</param>
    /// <param name="flags">旗标仓库。</param>
    /// <param name="hub">当前空间 id。查局部作用域时用。</param>
    /// <returns>成立返回 true。</returns>
    public static bool Evaluate(string? expr, FlagStore? flags, string? hub)
    {
        if (flags == null) return false;
        if (string.IsNullOrWhiteSpace(expr)) return true;

        foreach (var raw in expr!.Split('&'))
        {
            var term = raw.Trim();
            if (term.Length == 0) continue;

            var negated = term[0] == '!';
            if (negated) term = term[1..].Trim();
            if (term.Length == 0) continue;

            var value = flags.Has(term, hub);
            if (negated ? value : !value) return false;
        }

        return true;
    }

    /// <summary>
    /// 表达式里的旗标名（按出现顺序，去重）。给存档与自检用。
    /// </summary>
    /// <param name="expr">表达式。</param>
    /// <returns>旗标名列表。</returns>
    public static string[] Names(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return Array.Empty<string>();

        var names = new System.Collections.Generic.List<string>();
        foreach (var raw in expr!.Split('&'))
        {
            var term = raw.Trim();
            if (term.StartsWith("!", StringComparison.Ordinal)) term = term[1..].Trim();
            if (term.Length == 0) continue;
            if (!names.Contains(term)) names.Add(term);
        }

        return names.ToArray();
    }
}
