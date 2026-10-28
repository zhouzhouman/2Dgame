using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>
/// 一扇知识门的判定结果。
/// </summary>
/// <param name="Open">能不能开：<c>requires</c> 成立为 true。</param>
/// <param name="Missing">
/// 缺哪些项（给 UI 提示用）：只在不成立时非空，按表达式里的书写顺序排列，
/// 取反项带 <c>!</c> 前缀（如 <c>!knows_herb_path</c> 表示"这项成立反而不对"）。
/// 表达式语法错误时为空——那种情况下写不出人话提示。
/// </param>
public readonly record struct GateResult(bool Open, IReadOnlyList<string> Missing)
{
    /// <summary>关着但一条都说不出为什么（语法错误 / 纯复合取反）。</summary>
    public bool IsSilentlyClosed => !Open && Missing.Count == 0;

    public override string ToString() =>
        Open ? "开" : Missing.Count == 0 ? "关（无提示）" : $"关（缺 {string.Join(" / ", Missing)}）";
}

/// <summary>
/// 知识门：<c>points.csv.requires</c> 成立才让搜（schema_v2.md §A.1）。
///
/// 只做一件事：把表达式求值结果包装成 <see cref="GateResult"/>，顺带列出缺项。
/// 取值一律走 <see cref="FlagProbe"/> 委托，不认识 FlagStore，也不读文件。
/// 规则源：Docs/Rules/rule_flags_save.md §公式、rule_time_clock.md §例外 3
/// （未满足条件的知识门按 E 无反应，0 刻）。
/// </summary>
public static class KnowledgeGate
{
    /// <summary>
    /// 判一扇知识门。
    /// </summary>
    /// <param name="requires">表达式。空串 / 全空白 = 无条件，视为开。</param>
    /// <param name="probe">旗标取值委托。null 视为任何旗标都不成立。</param>
    /// <returns>开/关，关闭时附缺项清单。</returns>
    public static GateResult Check(string? requires, FlagProbe? probe)
    {
        // 空 requires = 无门槛（数据表里 surface 点都是空串）。
        if (string.IsNullOrWhiteSpace(requires)) return new GateResult(true, Array.Empty<string>());

        if (FlagExpression.Evaluate(requires, probe)) return new GateResult(true, Array.Empty<string>());

        // 语法错误：Terms 返回空列表，这里就自然给不出提示（不猜）。
        var terms = FlagExpression.Terms(requires);
        if (terms.Count == 0) return new GateResult(false, Array.Empty<string>());

        var missing = new List<string>(terms.Count);
        foreach (var term in terms)
        {
            var actual = probe?.Invoke(term.Name) ?? false;
            var satisfied = term.Negated ? !actual : actual;
            if (satisfied) continue;

            var text = term.Negated ? "!" + term.Name : term.Name;
            if (!missing.Contains(text)) missing.Add(text);
        }

        return new GateResult(false, missing.Count == 0 ? Array.Empty<string>() : missing.ToArray());
    }
}
