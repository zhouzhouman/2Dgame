using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>
/// 一次 <see cref="BeatEffectBook.Apply"/> 的结果。
///
/// 失败是正常结果（囊满、钱不够、id 不存在），所以不抛——UI 拿 <see cref="Reason"/>
/// 直接显示那一句人话。<see cref="Lines"/> 是成功时**每一步**的人话，顺序即施加顺序。
/// </summary>
/// <param name="Ok">成没成。没成时状态一点没动，<see cref="Lines"/> 为空。</param>
/// <param name="Reason">没成时给玩家看的一句话；成为空串。</param>
/// <param name="Lines">成了的话，每一步一句给玩家看的话（<c>钱 +15</c> / <c>谷子 ×3</c>）。</param>
public readonly record struct ApplyResult(bool Ok, string Reason, IReadOnlyList<string> Lines)
{
    /// <summary>成了，带上每一步的人话。</summary>
    /// <param name="lines">按施加顺序排好的人话；可以为空表。</param>
    /// <returns>成功结果。</returns>
    public static ApplyResult Applied(IReadOnlyList<string> lines) =>
        new(true, "", lines ?? Array.Empty<string>());

    /// <summary>没成：只带原因，什么都没动。</summary>
    /// <param name="reason">给玩家看的一句话。</param>
    /// <returns>失败结果。</returns>
    public static ApplyResult Fail(string reason) =>
        new(false, reason ?? "", Array.Empty<string>());

    /// <summary>成功了但这一步什么也不用说（空组）。</summary>
    public static ApplyResult Nothing { get; } = new(true, "", Array.Empty<string>());
}

/// <summary>
/// 选项效果的结算层：把 <c>beats.csv</c> 里 <c>effects</c> 那几组原子效果落到状态上
/// （设计文档里"选了之后发生什么"）。
///
/// 规矩跟 <see cref="TradeBook"/> 一样：
/// - **先全量校验、再施加**：任何一步失败（囊满装不下、钱不够、物品 id 不存在）整组不落，
///   不会出现"钱加了、粮没进来"这种半截账；
/// - 只改调用方递进来的四样东西（<see cref="TrustState"/>、<see cref="Inventory"/>、
///   <c>troops</c>、<see cref="FlagStore"/>），不写存档、不碰 World、不碰引擎；
/// - 每一句 <see cref="ApplyResult.Lines"/> 都是给玩家看的中文人话，UI 不自己编。
/// </summary>
public static class BeatEffectBook
{
    /// <summary>
    /// 把某个选项的效果落到状态上。
    ///
    /// 施加顺序就是表里的书写顺序；数值为 0 的原子是空操作，不产生人话行
    /// （表里写 <c>min:+0</c> 多半是笔误，但不值得为它炸掉整拍）。
    /// 三轴按 <see cref="TrustBook"/> 的口径钳 0..100；兵力钳在 0 以上（不让兵变成负数）。
    /// </summary>
    /// <param name="beat">这一拍（效果从 <see cref="Beat.Effects"/> 取）。</param>
    /// <param name="optionIndex">选了第几项（0 起）。</param>
    /// <param name="trust">本地区的三条轴，就地改。</param>
    /// <param name="bag">背包（钱也在里面），就地改。</param>
    /// <param name="catalog">物品表，决定 id 存不存在、能不能叠。</param>
    /// <param name="troops">兵力，就地改（引用传入）。</param>
    /// <param name="flags">旗标仓库；不传 = 旗标这一步直接跳过。</param>
    /// <returns>成没成、没成的原因、成了的每一步人话。</returns>
    public static ApplyResult Apply(
        Beat beat,
        int optionIndex,
        TrustState trust,
        Inventory bag,
        ItemCatalog catalog,
        ref int troops,
        FlagStore? flags)
    {
        if (beat == null) throw new ArgumentNullException(nameof(beat));
        if (trust == null) throw new ArgumentNullException(nameof(trust));
        if (bag == null) throw new ArgumentNullException(nameof(bag));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        var groups = beat.Effects;
        if (groups.Count == 0) return ApplyResult.Nothing; // 这一拍没配效果（旧表 / 非选择拍）

        if (optionIndex < 0 || optionIndex >= groups.Count)
            return ApplyResult.Fail($"节拍「{beat.Id}」没有第 {optionIndex + 1} 个选项。");

        var effects = groups[optionIndex];
        if (effects.Count == 0) return ApplyResult.Nothing;

        // ① 全量校验：拿一份背包副本试一遍，任何一步不行就整组作废。
        //    会翻车的是背包容量、钱够不够、物品存不存在、旗标名能不能写；
        //    三轴与兵力只做钳位，不会失败。
        var preview = new Inventory(bag.Capacity);
        preview.LoadFrom(bag.ToSave());

        foreach (var effect in effects)
        {
            var error = Simulate(effect, preview, catalog, flags);
            if (error != null) return ApplyResult.Fail(error);
        }

        // ② 施加：走到这里每一步都确定成功。
        var lines = new List<string>(effects.Count);
        foreach (var effect in effects)
        {
            if (IsZeroNoOp(effect)) continue;

            var line = Realize(effect, trust, bag, catalog, ref troops, flags);
            if (line.Length > 0) lines.Add(line);
        }

        return ApplyResult.Applied(lines);
    }

    /// <summary>
    /// 在副本上试走一步：只处理会失败的两种（钱与物品），其余一律放行。
    /// </summary>
    /// <returns>不行时给玩家看的一句话；行则 null。</returns>
    private static string? Simulate(Effect effect, Inventory bag, ItemCatalog catalog, FlagStore? flags)
    {
        switch (effect.Kind)
        {
            case EffectKind.Coin:
                if (effect.Amount == 0) return null;

                if (effect.Amount > 0)
                    return bag.TryAdd(TradeBook.MoneyItem, effect.Amount, catalog, out var addError)
                        ? null
                        : addError ?? Inventory.FullMessage;

                var need = -effect.Amount;
                var have = bag.Count(TradeBook.MoneyItem);
                if (have < need) return $"钱不够（要 {need} 文，只有 {have} 文）。";

                bag.Remove(TradeBook.MoneyItem, need);
                return null;

            case EffectKind.Item:
                if (effect.Amount == 0) return null;

                if (catalog.TryGet(effect.Target) == null)
                    return $"没有「{effect.Target}」这件东西。";

                return bag.TryAdd(effect.Target, effect.Amount, catalog, out var itemError)
                    ? null
                    : itemError ?? Inventory.FullMessage;

            case EffectKind.Flag:
                // 名字写错不能装看不见：FlagStore.Set 对非法名是静默失败的，
                // 那样玩家会看到"旗标 xxx"却没真正置上，后面的剧情永远开不了。
                if (flags != null && !FlagStore.IsValidName(effect.Target))
                    return $"旗标名「{effect.Target}」不合法（须符合 FlagStore.IsValidName）。";

                return null;

            default:
                return null;
        }
    }

    /// <summary>真正落一步，并给出一句人话。</summary>
    private static string Realize(
        Effect effect, TrustState trust, Inventory bag, ItemCatalog catalog, ref int troops, FlagStore? flags)
    {
        switch (effect.Kind)
        {
            case EffectKind.Coin:
                if (effect.Amount > 0)
                    bag.TryAdd(TradeBook.MoneyItem, effect.Amount, catalog, out _);
                else if (effect.Amount < 0)
                    bag.Remove(TradeBook.MoneyItem, -effect.Amount);

                return "钱 " + Signed(effect.Amount);

            case EffectKind.Item:
                bag.TryAdd(effect.Target, effect.Amount, catalog, out _);
                var name = catalog.TryGet(effect.Target)?.Name ?? effect.Target ?? "";
                return $"{name} ×{effect.Amount}";

            case EffectKind.Guan:
                trust.Guan = ClampAxis(trust.Guan + effect.Amount);
                return "官望 " + Signed(effect.Amount);

            case EffectKind.Min:
                trust.Min = ClampAxis(trust.Min + effect.Amount);
                return "民望 " + Signed(effect.Amount);

            case EffectKind.Yi:
                trust.Yi = ClampAxis(trust.Yi + effect.Amount);
                return "疑心 " + Signed(effect.Amount);

            case EffectKind.Troops:
                troops = Math.Max(0, troops + effect.Amount);
                return "兵 " + Signed(effect.Amount);

            case EffectKind.Flag:
                if (flags == null) return "";
                flags.Set(effect.Target);
                return "旗标 " + effect.Target;

            default:
                return "";
        }
    }

    /// <summary>数值为 0 的增量是空操作：不落账、不说话（旗标没有数值，不算）。</summary>
    private static bool IsZeroNoOp(Effect effect) =>
        effect.Kind != EffectKind.Flag && effect.Amount == 0;

    /// <summary>三轴钳在 0..100，口径与 <see cref="TrustBook"/> 一致。</summary>
    private static int ClampAxis(int value) => Math.Clamp(value, TrustBook.MinValue, TrustBook.MaxValue);

    /// <summary>带符号的增量：<c>+15</c> / <c>−15</c>（用真正的减号，不用连字符）。</summary>
    private static string Signed(int value) =>
        value >= 0 ? $"+{value}" : $"\u2212{-value}";
}
