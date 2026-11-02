using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Juhuo.Core;

/// <summary>
/// 一次攻城的结算结果。
///
/// 三种结局用一个结构装：**赢了**（<see cref="Win"/>）、**撤了**（<see cref="Retreat"/>）、
/// **根本没让打**（两者皆 false，<see cref="Reason"/> 说为什么）。
/// 本结构只描述发生了什么，不落任何状态——信任、旗标、兵力都由调用方按这里的字段去写。
/// </summary>
/// <param name="Win">打赢了。</param>
/// <param name="Retreat">打输了，撤退（§16.5：损 30% 兵力，不是游戏结束）。</param>
/// <param name="Survived">打完之后还剩多少兵（撤退时是扣掉 30% 之后的数）。</param>
/// <param name="Casualty">这一仗实际伤亡百分比：赢 = 表里的 casualty，撤退 = 30，没打成 = 0。</param>
/// <param name="Minyuan">民怨增量；输了或没打成为 0。</param>
/// <param name="Tianming">天命增量；输了或没打成为 0。</param>
/// <param name="Flag">该由调用方去置的旗标名；只有撤退时非空（<see cref="SiegeBook.FailedFlag"/>）。</param>
/// <param name="Reason">没让打时给玩家看的一句话；其余情况为 null。</param>
/// <param name="Needed">这一法要求的兵力门槛（表里 <c>troops_needed</c>），给 UI 标"还差多少"。</param>
public readonly record struct SiegeResult(
    bool Win,
    bool Retreat,
    int Survived,
    int Casualty,
    int Minyuan,
    int Tianming,
    string? Flag,
    string? Reason,
    int Needed)
{
    /// <summary>这一仗打没打（赢了或撤了都算打了）。没让打为 false。</summary>
    public bool Ok => Win || Retreat;

    /// <summary>门槛没过的结果：兵一个不动，旗标一个不置。</summary>
    /// <param name="troops">进场时的兵力，原样带回。</param>
    /// <param name="reason">给玩家看的一句话（通常就是 <see cref="SiegeBook.NeedsText"/>）。</param>
    /// <param name="needed">这一法要求的兵力门槛。</param>
    /// <returns>拒绝结果。</returns>
    public static SiegeResult Refused(int troops, string reason, int needed) =>
        new(false, false, troops, 0, 0, 0, null, reason, needed);
}

/// <summary>
/// 攻城的规则层（设计文档 §8.6 四法、§16.5 失败不是结束）。
///
/// 三件事：现在能用哪几法、每一法还差什么、打了之后还剩多少兵。
/// **只算账，不落账**：信任由调用方交给 <see cref="TrustBook.Apply"/>，旗标由调用方交给
/// <see cref="FlagStore"/>，兵力写回存档也是调用方的事——和 <see cref="TradeBook"/> 一个规矩，
/// 这样"打仗"和"信任/旗标/存档"各归各的账，不会出现撤了退却涨了官望这种糊涂账。
///
/// **两个量刻意分开**（`CANON.md §8`：数值不进代码）：
/// - **能不能选** = 表里的 <c>needs</c>（断粮 / 官望 / 地形），见 <see cref="Available"/>；
/// - **打不打得下** = 表里的 <c>troops_needed</c>，<see cref="SiegeMethod.TroopsNeeded"/>，
///   各法各一个数，代码里没有任何基数与系数。
/// 兵不够却仍选了是允许的：照打，输了按 §16.5 撤退损 30%。这是"硬攻要付代价"，不是 bug。
/// </summary>
public sealed class SiegeBook
{
    /// <summary>撤退时损失的兵力百分比（§16.5「损失 30% 兵力」）。</summary>
    public const int RetreatLossPercent = 30;

    /// <summary>撤退时要置的旗标。点号式命名须 3–4 段，所以不是 <c>siege.failed</c>。</summary>
    public const string FailedFlag = "siege.battle.failed";

    /// <summary>无门槛时给 UI 的那句话。</summary>
    public const string NoNeedText = "无需条件";

    /// <summary>旗标门槛已满足时给 UI 的那句话。</summary>
    public const string FlagReadyText = "已备";

    /// <summary>围城要的"城里断粮"旗标名（与 <c>siege.csv</c> 一致）。换了名字也不崩，只退化成通用说法。</summary>
    public const string CityHungryFlag = "city_hungry";

    /// <summary>水/火要的"地形允许"旗标名（与 <c>siege.csv</c> 一致）。</summary>
    public const string TerrainRiverFlag = "terrain_river";

    /// <summary>两种旗标门槛的人话。其余旗标走通用说法。</summary>
    private static readonly Dictionary<string, string> FlagHints = new(StringComparer.Ordinal)
    {
        [CityHungryFlag] = "城里还没断粮",
        [TerrainRiverFlag] = "这儿的地形用不上水火",
    };

    private readonly SiegeTable _table;

    /// <summary>
    /// 建一本攻城账。表来自数据，状态（兵力 / 信任 / 旗标）由调用方按参数传进来。
    /// </summary>
    /// <param name="table">攻城手段表。</param>
    public SiegeBook(SiegeTable table)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    /// <summary>全部手段，按文件顺序。</summary>
    public IReadOnlyList<SiegeMethod> All => _table.All;

    /// <summary>手段条数。</summary>
    public int Count => _table.Count;

    /// <summary>按 id 取一种手段。</summary>
    /// <param name="id">手段 id。</param>
    /// <param name="method">取到的手段；找不到为 null。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out SiegeMethod? method) => _table.TryGet(id, out method);

    /// <summary>
    /// 现在能选的手段，按文件顺序（= UI 选项顺序）。
    ///
    /// **只按 <c>needs</c> 筛，不看兵力**：兵力是"打不打得下"（<see cref="SiegeMethod.TroopsNeeded"/>），
    /// 不是"能不能选"。§16.5 的撤退必须真的会发生——若这里就把兵不够打赢的方法藏掉，
    /// 玩家永远打不了败仗，撤退损 30% 就成了死代码。兵不够的提醒交给
    /// <see cref="NeedsText"/>（"可打，但兵力 65 / 需 72，多半要撤"）。
    /// </summary>
    /// <param name="troops">手里多少兵（只供 <c>needs=troops&gt;=N</c> 那类门槛用）。</param>
    /// <param name="trustBook">本地区的三条轴。</param>
    /// <param name="flags">旗标探针；不传 = 没有任何旗标。</param>
    /// <returns>可选的手段；一个都没有则空表。</returns>
    public IReadOnlyList<SiegeMethod> Available(int troops, TrustBook trustBook, FlagProbe? flags = null)
    {
        if (trustBook == null) throw new ArgumentNullException(nameof(trustBook));

        var list = new List<SiegeMethod>(_table.Count);
        foreach (var method in _table.All)
            if (NeedsMet(method.Needs, troops, trustBook, flags))
                list.Add(method);

        return list;
    }

    /// <summary>
    /// 这一法现在能不能选：只看 <c>needs</c>。**不代表打得赢**——打赢还要兵力 ≥
    /// <see cref="SiegeMethod.TroopsNeeded"/>，不够就撤退（§16.5）。
    /// </summary>
    /// <param name="method">手段。</param>
    /// <param name="troops">手里多少兵。</param>
    /// <param name="trustBook">本地区的三条轴。</param>
    /// <param name="flags">旗标探针。</param>
    /// <returns>能选返回 true。</returns>
    public bool CanUse(SiegeMethod method, int troops, TrustBook trustBook, FlagProbe? flags = null)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (trustBook == null) throw new ArgumentNullException(nameof(trustBook));

        return NeedsMet(method.Needs, troops, trustBook, flags);
    }

    /// <summary>
    /// 这一法现在什么状态，给 UI 的一句话，三种情况：
    /// 1. <c>needs</c> 没满足 —— 缺什么说什么：<c>官望 42 / 需 60</c>、<c>兵力 80 / 需 80</c>、
    ///    <c>城里还没断粮</c>；无条件则是 <c>无需条件</c>。
    /// 2. <c>needs</c> 过了但兵 &lt; <see cref="SiegeMethod.TroopsNeeded"/> ——
    ///    <c>可打，但兵力 65 / 需 72，多半要撤</c>。这句是给玩家避坑用的：能点，但会输。
    /// 3. 两样都满足 —— <c>无需条件</c> / <c>已备</c> / <c>官望 60 / 需 60</c> 这类"已就绪"说法。
    /// </summary>
    /// <param name="method">手段。</param>
    /// <param name="troops">手里多少兵。</param>
    /// <param name="trustBook">本地区的三条轴。</param>
    /// <param name="flags">旗标探针；不传 = 当作没置位。</param>
    /// <returns>一句话。</returns>
    public string NeedsText(SiegeMethod method, int troops, TrustBook trustBook, FlagProbe? flags = null)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (trustBook == null) throw new ArgumentNullException(nameof(trustBook));

        if (!NeedsMet(method.Needs, troops, trustBook, flags))
            return NeedUnmetText(method.Needs, troops, trustBook);

        var needed = method.TroopsNeeded;
        if (troops < needed)
            return $"可打，但兵力 {troops} / 需 {needed}，多半要撤";

        return NeedReadyText(method.Needs, troops, trustBook);
    }

    /// <summary>门槛没满足时的说法：把现在的值与门槛并排，让玩家知道差多少。</summary>
    private static string NeedUnmetText(SiegeNeed need, int troops, TrustBook trustBook) =>
        need.Kind switch
        {
            SiegeNeedKind.Trust => $"{AxisLabel(need.Key)} {AxisValue(trustBook, need.Key)} / 需 {need.Value}",
            SiegeNeedKind.Troops => $"兵力 {troops} / 需 {need.Value}",
            SiegeNeedKind.Flag => FlagHint(need.Key),
            _ => NoNeedText,
        };

    /// <summary>门槛已满足时的说法：旗标说"已备"，其余照旧报当前值。</summary>
    private static string NeedReadyText(SiegeNeed need, int troops, TrustBook trustBook) =>
        need.Kind switch
        {
            SiegeNeedKind.Trust => $"{AxisLabel(need.Key)} {AxisValue(trustBook, need.Key)} / 需 {need.Value}",
            SiegeNeedKind.Troops => $"兵力 {troops} / 需 {need.Value}",
            SiegeNeedKind.Flag => FlagReadyText,
            _ => NoNeedText,
        };

    /// <summary>
    /// 打这一法。
    ///
    /// 判定顺序：**先看能不能选，再看打不打得赢**。
    /// - <c>needs</c> 没满足：直接拒，一个兵都不动——这不是"打输了"，是"没让你打"。
    /// - 能选但兵 &lt; <see cref="SiegeMethod.TroopsNeeded"/>：照打，然后撤退，损 30% 兵力并置
    ///   <see cref="FailedFlag"/>（§16.5）。这是设计上的"硬攻要付代价"，不是 bug。
    /// - 兵够：赢，按表里的 casualty 扣人，带回民怨 / 天命增量。
    /// </summary>
    /// <param name="method">手段。</param>
    /// <param name="troops">手里多少兵。</param>
    /// <param name="trustBook">本地区的三条轴（只读，不改）。</param>
    /// <param name="flags">旗标探针（只读，不改）。</param>
    /// <returns>结算结果；该落的状态由调用方按字段去落。</returns>
    public SiegeResult Resolve(SiegeMethod method, int troops, TrustBook trustBook, FlagProbe? flags = null)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (trustBook == null) throw new ArgumentNullException(nameof(trustBook));

        var needed = method.TroopsNeeded;

        if (!NeedsMet(method.Needs, troops, trustBook, flags))
            return SiegeResult.Refused(troops, NeedsText(method, troops, trustBook, flags), needed);

        if (troops < needed)
        {
            var lost = RetreatLoss(troops);
            return new SiegeResult(false, true, Math.Max(0, troops - lost), RetreatLossPercent, 0, 0, FailedFlag, null, needed);
        }

        var dead = troops * method.Casualty / 100;
        return new SiegeResult(true, false, Math.Max(0, troops - dead), method.Casualty, method.Minyuan, method.Tianming, null, null, needed);
    }

    /// <summary>
    /// 撤退损兵：<c>兵力 × 30%</c> 向上取整，至少 1（有兵就不该"零损失撤退"）。
    /// </summary>
    /// <param name="troops">撤退前的兵力。</param>
    /// <returns>损失的兵数。</returns>
    public static int RetreatLoss(int troops)
    {
        if (troops <= 0) return 0;
        return Math.Max(1, (troops * RetreatLossPercent + 99) / 100);
    }

    /// <summary>门槛条件满不满足（只看 <c>needs</c>，兵力够不够打赢是另一层）。</summary>
    private static bool NeedsMet(SiegeNeed need, int troops, TrustBook trustBook, FlagProbe? flags) =>
        need.Kind switch
        {
            SiegeNeedKind.Trust => AxisValue(trustBook, need.Key) >= need.Value,
            SiegeNeedKind.Troops => troops >= need.Value,
            SiegeNeedKind.Flag => flags != null && flags(need.Key),
            _ => true,
        };

    /// <summary>信任轴现在多少。轴名已在读表时校验过。</summary>
    private static int AxisValue(TrustBook trustBook, string axis) =>
        axis == "min" ? trustBook.State.Min : axis == "yi" ? trustBook.State.Yi : trustBook.State.Guan;

    /// <summary>轴的中文名，给 NeedsText 用。</summary>
    private static string AxisLabel(string axis) =>
        axis == "min" ? "民望" : axis == "yi" ? "疑心" : "官望";

    /// <summary>
    /// 旗标门槛的人话。两种已知旗标各有句子（"城里还没断粮"比"还差 city_hungry"像人话），
    /// 其余旗标退化成通用说法——数据里加一个门不该让规则层崩。
    /// </summary>
    private static string FlagHint(string name) =>
        FlagHints.TryGetValue(name, out var text) ? text : $"还差「{name}」";
}
