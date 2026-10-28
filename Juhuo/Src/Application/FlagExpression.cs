using System;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 旗标表达式求值（Application 层薄壳）。
///
/// 真正的解析与求值已经收进 <see cref="Juhuo.Core.FlagExpression"/>（票号 S1-12），
/// 这一层只负责把 <see cref="FlagStore"/> + hub 适配成 Core 要的取值委托。
/// 保留原来的公开签名，<c>World</c> 那边不用改。
///
/// 取值规则仍是全项目唯一的旗标入口（rule_flags_save.md §公式）：
/// 全局优先、全局没有才看空间的局部、都没有就是 false。
/// </summary>
public static class FlagExpression
{
    /// <summary>
    /// 求一条旗标表达式。
    /// </summary>
    /// <param name="expr">表达式。空串 = 无条件成立。</param>
    /// <param name="flags">旗标仓库。为 null 时一律不成立（保持旧行为）。</param>
    /// <param name="hub">当前空间 id。查局部作用域时用。</param>
    /// <returns>成立返回 true；语法错误返回 false。</returns>
    public static bool Evaluate(string? expr, FlagStore? flags, string? hub)
    {
        if (flags == null) return false;

        return Juhuo.Core.FlagExpression.Evaluate(expr, name => flags.Has(name, hub));
    }

    /// <summary>
    /// 表达式里的旗标名（按出现顺序，去重）。给存档与自检用。
    /// </summary>
    /// <param name="expr">表达式。</param>
    /// <returns>旗标名列表。</returns>
    public static string[] Names(string? expr) => Juhuo.Core.FlagExpression.Names(expr);
}
