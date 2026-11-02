using System;
using System.Collections.Generic;
using System.Linq;

namespace Juhuo.Core;

/// <summary>
/// 一次转段操作的成败。失败必带一句人话——表现层直接把它显示出来，不自己编。
/// </summary>
/// <param name="Ok">做成了没有。</param>
/// <param name="Reason">没做成时差什么；成功时为空串。</param>
public readonly record struct TransitionResult(bool Ok, string Reason)
{
    /// <summary>做成了。</summary>
    public static TransitionResult Success { get; } = new(true, "");

    /// <summary>没做成，附一句原因。</summary>
    /// <param name="reason">差什么。</param>
    /// <returns>失败结果。</returns>
    public static TransitionResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// 转段结算的规则层（设计文档 §16.6）。序章最后十分钟要做的四件事，这里管三件半：
///
/// 1. 定都 —— <see cref="ChooseCapital"/>；
/// 2. 国号 —— <see cref="ChooseRealm"/>；
/// 3. 三誓约变国策、其余变债 —— <see cref="MadeOaths"/> / <see cref="ChooseOaths"/>；
/// 4. 收尾 —— <see cref="IsComplete"/> / <see cref="Finish"/>（第一份国情报告是表现层的事）。
///
/// **分封六位功臣不在这一版**：同伴系统还不存在，先说清楚，免得有人以为漏了。
///
/// 这个类只认 Core 的三样东西：誓约表、国号表、旗标仓库，加一份要写进去的存档。
/// 不碰文件、不碰引擎、不自己编文案。
/// </summary>
public sealed class TransitionBook
{
    /// <summary>开国国策的条数（§14.2：从所有誓约里选 3 条）。</summary>
    public const int MaxOaths = 3;

    /// <summary>转段前至少要走过多少拍（十二拍走完）。</summary>
    public const int MinBeats = 12;

    /// <summary>序章完成时置的旗标。</summary>
    public const string DoneFlag = "prologue.done";

    /// <summary>可以定都的七个空间，顺序即 hub1–hub7。</summary>
    public static readonly IReadOnlyList<string> CapitalHubs = new[]
    {
        "hub1", "hub2", "hub3", "hub4", "hub5", "hub6", "hub7",
    };

    private readonly OathTable _oaths;
    private readonly RealmTable _realms;
    private readonly FlagStore _flags;
    private readonly SaveState _state;

    /// <summary>
    /// 建一个转段结算器。
    /// </summary>
    /// <param name="oaths">誓约表（oaths.csv）。</param>
    /// <param name="realms">国号表（realms.csv）。</param>
    /// <param name="flags">旗标仓库：这里只读 <c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>、只写 <see cref="DoneFlag"/>。</param>
    /// <param name="state">要往 <c>prologue</c> 节里写结果的存档。</param>
    public TransitionBook(OathTable oaths, RealmTable realms, FlagStore flags, SaveState state)
    {
        _oaths = oaths ?? throw new ArgumentNullException(nameof(oaths));
        _realms = realms ?? throw new ArgumentNullException(nameof(realms));
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.Prologue ??= new PrologueState();
    }

    /// <summary>
    /// 真正许过的誓约，按表里的顺序。
    ///
    /// 判据只有一个：**对应选项被选过**——<c>beat.&lt;节拍&gt;.pick.&lt;序号&gt;</c> 为真。
    /// 节拍"做完了"不算数：一拍里选 A 还是选 B，许下的是不同的誓约。
    /// </summary>
    /// <returns>许过的誓约；一张都没许过就是空表。</returns>
    public IReadOnlyList<Oath> MadeOaths()
    {
        var list = new List<Oath>();
        foreach (var oath in _oaths.All)
            if (_flags.Has(oath.Source.FlagName, null))
                list.Add(oath);

        return list;
    }

    /// <summary>
    /// 立国策：从许过的誓约里挑最多 <see cref="MaxOaths"/> 条。
    ///
    /// 选中的写进 <c>prologue.oaths</c>；**其余许过的**全部写进 <c>prologue.debts</c>，
    /// 留到正篇按期限一条条到期。不在 <see cref="MadeOaths"/> 里的选不了。
    /// 先全量校验再落盘：被拒时存档一个字都不动。
    /// </summary>
    /// <param name="ids">要立为国策的誓约 id；重复的按一条算。</param>
    /// <returns>成败与原因。</returns>
    public TransitionResult ChooseOaths(IEnumerable<string>? ids)
    {
        if (ids == null) return TransitionResult.Fail("没给要立的誓约。");

        var picked = new List<string>(MaxOaths);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in ids)
        {
            var id = (raw ?? "").Trim();
            if (id.Length == 0) continue;
            if (seen.Add(id)) picked.Add(id);
        }

        if (picked.Count == 0)
            return TransitionResult.Fail("一条誓约都没选。");

        var made = MadeOaths();

        var cap = Math.Min(MaxOaths, made.Count);
        if (picked.Count > cap)
            return TransitionResult.Fail(cap == 0
                ? "你这一趟没许下任何承诺，没有国策可立。"
                : $"最多只能立 {cap} 条国策（你许下 {made.Count} 条），这次选了 {picked.Count} 条。");
        var madeIds = new HashSet<string>(made.Select(o => o.Id), StringComparer.Ordinal);

        foreach (var id in picked)
        {
            if (!_oaths.TryGet(id, out var oath))
                return TransitionResult.Fail($"誓约「{id}」不在誓约表里。");

            if (!madeIds.Contains(id))
                return TransitionResult.Fail(
                    $"誓约「{id}」没许过：{oath.Source} 那一拍没选这一项，不能立为国策。");
        }

        var chosen = new HashSet<string>(picked, StringComparer.Ordinal);
        _state.Prologue!.Oaths = new List<string>(picked);
        _state.Prologue.Debts = made.Where(o => !chosen.Contains(o.Id)).Select(o => o.Id).ToList();

        return TransitionResult.Success;
    }

    /// <summary>
    /// 定都：只能定在序章的七块土地上（hub1–hub7）。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>成败与原因。</returns>
    public TransitionResult ChooseCapital(string? hub)
    {
        var id = (hub ?? "").Trim();

        if (id.Length == 0)
            return TransitionResult.Fail("没给首都。");

        if (!CapitalHubs.Contains(id, StringComparer.Ordinal))
            return TransitionResult.Fail($"「{id}」不是序章的土地，首都只能定在 hub1–hub7。");

        _state.Prologue!.Capital = id;
        return TransitionResult.Success;
    }

    /// <summary>
    /// 定国号：必须是国号表里有的那一个。
    /// </summary>
    /// <param name="realmId">国号 id（realms.csv 的 id 列，不是国号正文）。</param>
    /// <returns>成败与原因。</returns>
    public TransitionResult ChooseRealm(string? realmId)
    {
        var id = (realmId ?? "").Trim();

        if (id.Length == 0)
            return TransitionResult.Fail("没给国号。");

        if (!_realms.TryGet(id, out var realm))
            return TransitionResult.Fail($"国号「{id}」不在国号表里。");

        _state.Prologue!.Realm = realm.Id;
        return TransitionResult.Success;
    }

    /// <summary>
    /// 还差哪几样。每一条都是一句人话，可以直接显示。
    /// </summary>
    /// <returns>缺的项；全齐就是空表。</returns>
    public IReadOnlyList<string> Missing()
    {
        var prologue = _state.Prologue!;
        var list = new List<string>(4);

        if (string.IsNullOrEmpty(prologue.Capital)) list.Add("还没定都。");
        if (string.IsNullOrEmpty(prologue.Realm)) list.Add("还没定国号。");

        // 要立几条 = 最多 3 条，但不超过他真许下的那些。
        // 写死 "必须 3 条" 会把"只许过一条"的玩家永久卡在转段门口——
        // 十二拍里只有三拍能生成承诺，全选第 0 项的人一条都没有。
        var need = Math.Min(MaxOaths, MadeOaths().Count);
        var oaths = prologue.Oaths?.Count ?? 0;
        if (oaths < need) list.Add($"国策要立 {need} 条，现在 {oaths} 条。");

        var beats = prologue.BeatsDone?.Count ?? 0;
        if (beats < MinBeats) list.Add($"节拍至少要走完 {MinBeats} 拍，现在 {beats} 拍。");

        return list;
    }

    /// <summary>四样齐了没有：首都、国号、3 条国策、至少 12 拍。</summary>
    public bool IsComplete => Missing().Count == 0;

    /// <summary>这一档是不是已经转过段了（<see cref="DoneFlag"/> 在不在）。</summary>
    public bool IsDone => _flags.Has(DoneFlag, null);

    /// <summary>
    /// 收尾：四样全齐才置 <see cref="DoneFlag"/>，否则不动存档并说明差什么。
    /// 旗标同时写进旗标仓库与存档的 <c>flags</c>——读档走的是后者，运行中问的是前者，两边都得有。
    /// </summary>
    /// <returns>成败与原因。</returns>
    public TransitionResult Finish()
    {
        var missing = Missing();
        if (missing.Count > 0)
            return TransitionResult.Fail(string.Concat(missing));

        _flags.Set(DoneFlag);
        _state.Flags[DoneFlag] = true;

        return TransitionResult.Success;
    }
}
