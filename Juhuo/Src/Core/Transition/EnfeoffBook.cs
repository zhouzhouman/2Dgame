using System;
using System.Collections.Generic;
using EnfeoffGrade = Juhuo.Core.Enfeoff;

namespace Juhuo.Core;

/// <summary>分封的三档（§16.6 第 4 步：给高了 → 未来权臣；给低了 → 离心；不给 → 走人）。</summary>
public enum Enfeoff
{
    /// <summary>厚封：给得比该得的还多。</summary>
    Heavy = 0,

    /// <summary>薄封：给得比该得的少一点。</summary>
    Light = 1,

    /// <summary>不封：一个字都不给。</summary>
    None = 2,
}

/// <summary>
/// 一次分封的结果。失败也是正常结果（这个人已经封过了），所以不抛——
/// 表现层拿 <see cref="Reason"/> 直接显示。
/// </summary>
/// <param name="Ok">封成了没有。</param>
/// <param name="Reason">没成时给玩家看的一句话；成了为空串。</param>
/// <param name="Lines">成了的话给玩家看的那句中文；没成为空表。</param>
/// <param name="LoyaltyAfter">封完之后的忠诚（没成时是原样）。</param>
public readonly record struct EnfeoffResult(
    bool Ok,
    string Reason,
    IReadOnlyList<string> Lines,
    int LoyaltyAfter)
{
    /// <summary>一次没封成：什么都没动。</summary>
    /// <param name="reason">给玩家看的一句话。</param>
    /// <param name="loyaltyAfter">这个人现在的忠诚（一般就是原样）。</param>
    /// <returns>失败结果。</returns>
    public static EnfeoffResult Fail(string reason, int loyaltyAfter = 0) =>
        new(false, reason, Array.Empty<string>(), loyaltyAfter);
}

/// <summary>
/// 分封的规则层（设计文档 §16.6 第 4 步）。
///
/// 三档的账：
/// <list type="bullet">
/// <item>厚封：忠 −8（给高了会骄）；野心 ≥60 的人置「未来权臣」；官望 +3。</item>
/// <item>薄封：忠 +6（给少了但有一份）；野心 ≥60 的人置「离心」；民望 +2。</item>
/// <item>不封：忠 −20（当晚就有人走）；置「走了」；疑心 +2。</item>
/// </list>
///
/// 规矩同 <see cref="TradeBook"/>：**只算账**。写存档、同步 <see cref="SaveState.Flags"/>、
/// 记 <c>prologue.enfeoffs</c> 都是调用方的事（见 <see cref="EnfeoffRecord"/>）。
/// 本类只改交给它的 <see cref="TrustState"/> 与 <see cref="FlagStore"/>。
///
/// 「同一个人只能封一次」是**这一本账内**的记性：读档后要重新建一本，
/// 谁是"已经封过"以存档里的 <c>prologue.enfeoffs</c> 为准，别指望这里的 HashSet。
/// </summary>
public sealed class EnfeoffBook
{
    /// <summary>厚封的忠诚变动。</summary>
    public const int HeavyLoyalty = -8;

    /// <summary>薄封的忠诚变动。</summary>
    public const int LightLoyalty = 6;

    /// <summary>不封的忠诚变动。</summary>
    public const int NoneLoyalty = -20;

    /// <summary>厚封的官望变动。</summary>
    public const int HeavyGuan = 3;

    /// <summary>薄封的民望变动。</summary>
    public const int LightMin = 2;

    /// <summary>不封的疑心变动。</summary>
    public const int NoneYi = 2;

    /// <summary>信任三轴的上下限，与 <see cref="TrustBook"/> 一致。</summary>
    private const int TrustMin = TrustBook.MinValue;

    /// <summary>信任三轴的上限。</summary>
    private const int TrustMax = TrustBook.MaxValue;

    /// <summary>
    /// 旗标名的两段前缀。**必须有三段**（域.对象.状态）：两段的 <c>will_be_powerful.shen</c>
    /// 过不了 <see cref="FlagStore.IsValidName"/>，会静默写不进去，将来"权臣"永远不发生。
    /// 所以真正的旗标是 <c>enfeoff.will_be_powerful.&lt;id&gt;</c>（见 <see cref="WillBePowerfulFlag"/>）。
    /// </summary>
    public const string FlagPrefix = "enfeoff.";

    /// <summary>「未来权臣」的旗标：<c>enfeoff.will_be_powerful.&lt;id&gt;</c>。</summary>
    public const string WillBePowerfulPrefix = FlagPrefix + "will_be_powerful.";

    /// <summary>「离心」的旗标：<c>enfeoff.resented.&lt;id&gt;</c>。</summary>
    public const string ResentedPrefix = FlagPrefix + "resented.";

    /// <summary>「走了」的旗标：<c>enfeoff.left.&lt;id&gt;</c>。</summary>
    public const string LeftPrefix = FlagPrefix + "left.";

    /// <summary>这一本账里已经封过的人。</summary>
    private readonly HashSet<string> _sealed = new(StringComparer.Ordinal);

    /// <summary>这一本账里封过几位。</summary>
    public int SealedCount => _sealed.Count;

    /// <summary>
    /// 给一位功臣封一档。
    /// </summary>
    /// <param name="companion">封谁（id 是唯一键）。</param>
    /// <param name="grade">封哪一档。</param>
    /// <param name="trust">本地区的三条轴，就地改（钳 0..100）。</param>
    /// <param name="flags">旗标仓库，就地置「权臣 / 离心 / 走了」。</param>
    /// <returns>成败、给玩家看的那句话、封完的忠诚。</returns>
    /// <exception cref="ArgumentNullException">三个参数有 null。</exception>
    public EnfeoffResult Enfeoff(Companion companion, Enfeoff grade, TrustState trust, FlagStore flags)
    {
        if (companion == null) throw new ArgumentNullException(nameof(companion));
        if (trust == null) throw new ArgumentNullException(nameof(trust));
        if (flags == null) throw new ArgumentNullException(nameof(flags));

        if (_sealed.Contains(companion.Id))
            return EnfeoffResult.Fail($"「{companion.Name}」已经封过了，一人只封一次。", companion.Loyalty);

        int loyaltyDelta;
        string axisName;
        int step;
        string? flagName;

        switch (grade)
        {
            case EnfeoffGrade.Heavy:
                loyaltyDelta = HeavyLoyalty;
                axisName = "官望";
                step = HeavyGuan;
                flagName = companion.Ambition >= CompanionTable.AmbitionThreshold
                    ? WillBePowerfulFlag(companion.Id)
                    : null;
                break;

            case EnfeoffGrade.Light:
                loyaltyDelta = LightLoyalty;
                axisName = "民望";
                step = LightMin;
                flagName = companion.Ambition >= CompanionTable.AmbitionThreshold
                    ? ResentedFlag(companion.Id)
                    : null;
                break;

            default:
                loyaltyDelta = NoneLoyalty;
                axisName = "疑心";
                step = NoneYi;
                flagName = LeftFlag(companion.Id);
                break;
        }

        // 先落旗标：写不进去（id 拼出来的名字不合法）就整笔作废，忠诚与信任一个字都不动。
        if (flagName != null && !flags.Set(flagName))
            return EnfeoffResult.Fail(
                $"「{companion.Name}」的封赏旗标「{flagName}」写不进去（名字不合法）。", companion.Loyalty);

        var after = Math.Clamp(companion.Loyalty + loyaltyDelta, CompanionTable.MinStat, CompanionTable.MaxStat);
        var clampedAxis = ApplyAxis(trust, grade, step);
        _sealed.Add(companion.Id);

        var line = $"{companion.Name} {Label(grade)}：忠 {companion.Loyalty} → {after}，{axisName} +{step}"
                   + (clampedAxis ? "（已到顶）" : "");

        return new EnfeoffResult(true, "", new[] { line }, after);
    }

    /// <summary>这个人这一本账里封过没有。</summary>
    /// <param name="id">功臣 id。</param>
    /// <returns>封过返回 true。</returns>
    public bool IsSealed(string? id) => !string.IsNullOrEmpty(id) && _sealed.Contains(id!);

    /// <summary>三档的中文名（厚封 / 薄封 / 不封），UI 不自己编。</summary>
    /// <param name="grade">档位。</param>
    /// <returns>中文名。</returns>
    public static string Label(Enfeoff grade) => grade switch
    {
        EnfeoffGrade.Heavy => "厚封",
        EnfeoffGrade.Light => "薄封",
        _ => "不封",
    };

    /// <summary>档位的存档写法：heavy / light / none。</summary>
    /// <param name="grade">档位。</param>
    /// <returns>小写英文码。</returns>
    public static string Code(Enfeoff grade) => grade switch
    {
        EnfeoffGrade.Heavy => "heavy",
        EnfeoffGrade.Light => "light",
        _ => "none",
    };

    /// <summary>把存档里的写法认回档位。</summary>
    /// <param name="code">heavy / light / none，大小写与首尾空白都容忍。</param>
    /// <param name="grade">认出来的档位；认不出为 <see cref="Enfeoff.None"/>。</param>
    /// <returns>认出来返回 true。</returns>
    public static bool TryParseCode(string? code, out Enfeoff grade)
    {
        switch ((code ?? "").Trim().ToLowerInvariant())
        {
            case "heavy":
                grade = EnfeoffGrade.Heavy;
                return true;
            case "light":
                grade = EnfeoffGrade.Light;
                return true;
            case "none":
                grade = EnfeoffGrade.None;
                return true;
            default:
                grade = EnfeoffGrade.None;
                return false;
        }
    }

    /// <summary>「未来权臣」旗标名：<c>enfeoff.will_be_powerful.&lt;id&gt;</c>。</summary>
    /// <param name="id">功臣 id。</param>
    /// <returns>旗标名。</returns>
    public static string WillBePowerfulFlag(string id) => WillBePowerfulPrefix + id;

    /// <summary>「离心」旗标名：<c>enfeoff.resented.&lt;id&gt;</c>。</summary>
    /// <param name="id">功臣 id。</param>
    /// <returns>旗标名。</returns>
    public static string ResentedFlag(string id) => ResentedPrefix + id;

    /// <summary>「走了」旗标名：<c>enfeoff.left.&lt;id&gt;</c>。</summary>
    /// <param name="id">功臣 id。</param>
    /// <returns>旗标名。</returns>
    public static string LeftFlag(string id) => LeftPrefix + id;

    /// <summary>
    /// 一个 id 可能写出的三个旗标名。读表时用它验名（名字不合法就读表即抛，
    /// 免得运行时 <see cref="FlagStore.Set"/> 静默吞掉）。
    /// </summary>
    /// <param name="id">功臣 id。</param>
    /// <returns>三个旗标名。</returns>
    public static IReadOnlyList<string> FlagsOf(string id) => new[]
    {
        WillBePowerfulFlag(id), ResentedFlag(id), LeftFlag(id),
    };

    /// <summary>按档位给一条信任轴加步长并钳 0..100；返回是否撞了上限（供那句话标"已到顶"）。</summary>
    private static bool ApplyAxis(TrustState trust, Enfeoff grade, int step)
    {
        switch (grade)
        {
            case EnfeoffGrade.Heavy:
                return AddClamped(trust, TrustAxes.Guan, step);
            case EnfeoffGrade.Light:
                return AddClamped(trust, TrustAxes.Min, step);
            default:
                return AddClamped(trust, TrustAxes.Yi, step);
        }
    }

    /// <summary>给某条轴加步长，钳 0..100；返回是否被钳过。</summary>
    private static bool AddClamped(TrustState trust, TrustAxes axis, int step)
    {
        var current = axis switch
        {
            TrustAxes.Guan => trust.Guan,
            TrustAxes.Min => trust.Min,
            _ => trust.Yi,
        };

        var from = Math.Clamp(current, TrustMin, TrustMax);
        var to = Math.Clamp(from + step, TrustMin, TrustMax);

        switch (axis)
        {
            case TrustAxes.Guan:
                trust.Guan = to;
                break;
            case TrustAxes.Min:
                trust.Min = to;
                break;
            default:
                trust.Yi = to;
                break;
        }

        return to != from + step;
    }
}
