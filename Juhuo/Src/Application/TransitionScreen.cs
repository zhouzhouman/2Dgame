using System;
using System.Collections.Generic;
using System.Linq;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 转段五页（票号 S2-05c / S4-01d）：定都、国号、国策、分封、国情报告。
/// 后头再挂一页「序章完成」。
///
/// 规则仍在 <see cref="TransitionBook"/> 与 <see cref="EnfeoffBook"/> 里；这一层只管
/// "玩家翻到第几页、选中哪一行、按下去之后存档里多了什么"，以及报告页上那些字。
/// 纯 C#，不认引擎。
/// </summary>
public enum TransitionPage
{
    /// <summary>第一页：定都。</summary>
    Capital,

    /// <summary>第二页：国号与年号。</summary>
    Realm,

    /// <summary>第三页：国策（一条承诺都没许过时整页跳过）。</summary>
    Oaths,

    /// <summary>第四页：分封六位功臣（已经封过的档整页跳过）。</summary>
    Enfeoff,

    /// <summary>第五页：第一份国情报告。</summary>
    Report,

    /// <summary>收尾页：序章完成。</summary>
    Done,
}

/// <summary>面板上的一行：<paramref name="Id"/> 是存档里记的那个值，<paramref name="Picked"/> 是这一行立没立。</summary>
/// <param name="Id">hub id / 国号 id / 誓约 id。</param>
/// <param name="Text">显示的那一句。</param>
/// <param name="Picked">国策页用：这一条立没立。</param>
public readonly record struct TransitionRow(string Id, string Text, bool Picked);

/// <summary>
/// 国情报告最后一行要的实数。传进来而不是自己去问，是为了这一层不必认识 World / 存档。
/// </summary>
/// <param name="Troops">手里多少兵。</param>
/// <param name="Guan">官望。</param>
/// <param name="Min">民望。</param>
/// <param name="Yi">疑心。</param>
/// <param name="BagUsed">囊用掉几格。</param>
/// <param name="BagCapacity">囊一共几格。</param>
/// <param name="Day">第几天。</param>
public readonly record struct ReportFacts(
    int Troops, int Guan, int Min, int Yi, int BagUsed, int BagCapacity, int Day);

/// <summary>
/// 报告页「分封」那一行的一小段：<c>沈砚 薄封（忠 62→68）</c>。
/// 只记封完那一刻的实数——忠不是现在的忠，是**当时**的忠。
/// </summary>
/// <param name="Name">功臣名。</param>
/// <param name="Grade">档位的中文名（厚封 / 薄封 / 不封）。</param>
/// <param name="Before">封之前的忠诚。</param>
/// <param name="After">封之后的忠诚。</param>
public readonly record struct EnfeoffPart(string Name, string Grade, int Before, int After)
{
    /// <summary>这一段的字：<c>阿禾 薄封（忠 78→84）</c>。</summary>
    public string Text => $"{Name} {Grade}（忠 {Before}→{After}）";
}

/// <summary>
/// 转段界面上的固定字（票号 S2-05c）。**报告页下面五行直接抄设计文档 §16.6 第 5 步，不改写。**
/// </summary>
public static class TransitionText
{
    /// <summary>所有调试行共用的抬头。</summary>
    public const string Prefix = "[转段]";

    /// <summary>第一页抬头。</summary>
    public const string CapitalHead = "一　定都";

    /// <summary>第二页抬头。</summary>
    public const string RealmHead = "二　国号";

    /// <summary>第三页抬头。</summary>
    public const string OathsHead = "三　国策";

    /// <summary>第四页抬头。</summary>
    public const string EnfeoffHead = "四　分封";

    /// <summary>第五页抬头。</summary>
    public const string ReportHead = "五　国情报告";

    /// <summary>收尾页那四个字。</summary>
    public const string DoneHead = "序章完成";

    /// <summary>收尾页底下那一句。</summary>
    public const string DoneFoot = "按 Esc 退出";

    /// <summary>一条承诺都没许过、国策页被跳过时顶上闪的那一句。</summary>
    public const string NoOathsFlash = "你这一趟没许下什么";

    /// <summary>定都页的底注。</summary>
    public const string CapitalFoot = "1–7 选　·　Enter 定下";

    /// <summary>国号页的底注。</summary>
    public const string RealmFoot = "1–4 选　·　Enter 定下";

    /// <summary>国策页的底注。</summary>
    public const string OathsFoot = "↑↓ 选　·　空格 立 / 不立　·　Enter 定下";

    /// <summary>分封页的底注。</summary>
    public const string EnfeoffFoot = "↑↓ 选人　·　←→ 改封赏　·　Enter 定下";

    /// <summary>报告页的底注。</summary>
    public const string ReportFoot = "Enter 收下这份报告";

    /// <summary>空着手转段时，那一行写这个。</summary>
    public const string CarryNothing = "带走 空手";

    /// <summary>钱在囊里叫"铜钱"，转段报告上只说"钱"——跟玩家嘴边那个字一致。</summary>
    public const string CarryCoinName = "钱";

    /// <summary>
    /// 报告页那一行"带走"：<c>带走 粮 ×3、药 ×1、钱 17</c>。
    ///
    /// 念的是转段这一刻囊里真有的东西——**不另做一套存档字段**，
    /// 交易换来的粮、药、钱本来就都在 <c>inventory</c> 里。
    /// </summary>
    /// <param name="bag">玩家的囊；null 或空囊时写<c>带走 空手</c>。</param>
    /// <param name="catalog">物品表，用来取显示名。</param>
    /// <returns>一行字。</returns>
    public static string CarryLine(Inventory? bag, ItemCatalog? catalog)
    {
        if (bag == null || bag.SlotsUsed == 0) return CarryNothing;

        var text = new System.Text.StringBuilder("带走 ");

        for (var i = 0; i < bag.SlotsUsed; i++)
        {
            var slot = bag.Items[i];
            if (i > 0) text.Append('、');

            // 钱论"文"不论"件"，所以不写 ×：<c>钱 17</c>。
            if (string.Equals(slot.Item, TradeBook.MoneyItem, StringComparison.Ordinal))
            {
                text.Append(CarryCoinName).Append(' ').Append(slot.N);
                continue;
            }

            text.Append(BagPickup.NameOf(catalog, slot.Item)).Append(" ×").Append(slot.N);
        }

        return text.ToString();
    }

    /// <summary>报告页下半页那五行：文档原句，直接念。</summary>
    public static readonly IReadOnlyList<string> ReportFixedLines = new[]
    {
        "国库空",
        "粮价高",
        "旧朝三路集结",
        "宗室要供养",
        "官员在等你发俸禄",
    };

    /// <summary>国号那一行：<c>国号 炎 · 火德既明</c>；年号为空就只写国号。</summary>
    /// <param name="realm">国号。</param>
    /// <returns>一行字。</returns>
    public static string RealmLine(Realm realm)
    {
        if (realm == null) throw new ArgumentNullException(nameof(realm));
        return string.IsNullOrEmpty(realm.Motto)
            ? $"国号 {realm.Label}"
            : $"国号 {realm.Label} · {realm.Motto}";
    }

    /// <summary>
    /// 报告页最后一行：<c>兵力 54　官望 0　民望 0　疑心 3　囊 7/12　第 30 天</c>。
    /// 全角空格隔开，和页面里其它行一个呼吸。
    /// </summary>
    /// <param name="facts">当前状态。</param>
    /// <returns>一行字。</returns>
    public static string NumberLine(ReportFacts facts) =>
        $"兵力 {facts.Troops}　官望 {facts.Guan}　民望 {facts.Min}　疑心 {facts.Yi}"
        + $"　囊 {facts.BagUsed}/{facts.BagCapacity}　第 {facts.Day} 天";

    /// <summary>
    /// 定都那一行：<c>4　县城（hub4）</c>。名字只有 <see cref="HubNames"/> 那一份。
    /// </summary>
    /// <param name="index">0 起的序号。</param>
    /// <param name="hub">hub id。</param>
    /// <returns>一行字。</returns>
    public static string CapitalRow(int index, string hub) =>
        $"{index + 1}　{HubNames.Of(hub)}（{hub}）";

    /// <summary>国号那一行：<c>1　炎 · 火德既明</c>。</summary>
    /// <param name="index">0 起的序号。</param>
    /// <param name="realm">国号。</param>
    /// <returns>一行字。</returns>
    public static string RealmRow(int index, Realm realm)
    {
        if (realm == null) throw new ArgumentNullException(nameof(realm));
        return string.IsNullOrEmpty(realm.Motto)
            ? $"{index + 1}　{realm.Label}"
            : $"{index + 1}　{realm.Label} · {realm.Motto}";
    }

    // ————————————————————————— 分封（S4-01d） —————————————————————————

    /// <summary>
    /// 分封页的一行：<c>沈砚　御史大夫　忠 62 野心 55　［薄封］</c>。
    /// 官、忠、野心都照表念；档位读的是玩家当下改到的那一档。
    /// </summary>
    /// <param name="companion">这位功臣。</param>
    /// <param name="grade">当前档位。</param>
    /// <returns>一行字。</returns>
    public static string EnfeoffRow(Companion companion, Enfeoff grade)
    {
        if (companion == null) throw new ArgumentNullException(nameof(companion));

        return $"{companion.Name}　{companion.Office}　忠 {companion.Loyalty} 野心 {companion.Ambition}"
               + $"　［{EnfeoffBook.Label(grade)}］";
    }

    /// <summary>报告页那一行的宽度上限（按全角字宽算；面板一行放得下这么多）。</summary>
    public const int ReportLineMaxWidth = 45;

    /// <summary>
    /// 报告页的「分封」行：<c>分封　阿禾 薄封（忠 78→84）· 沈砚 薄封（忠 62→68）· …</c>。
    ///
    /// 六个都列在这一行里放不下，按面板宽度截到第几个人，后面缀一个「…」——
    /// 玩家上一页刚亲手定过六个，这里要的是"记下了"，不是再抄一遍。
    /// </summary>
    /// <param name="parts">按封赏顺序排好的六段；空表返回空串（这一趟没封过）。</param>
    /// <returns>一行字。</returns>
    public static string EnfeoffReportLine(IReadOnlyList<EnfeoffPart>? parts)
    {
        if (parts == null || parts.Count == 0) return "";

        var text = new System.Text.StringBuilder("分封　");
        var width = 3.0;
        var shown = 0;

        for (var i = 0; i < parts.Count; i++)
        {
            var chunk = (i == 0 ? "" : "· ") + parts[i].Text;
            var w = WidthOf(chunk);
            if (shown > 0 && width + w > ReportLineMaxWidth) break;

            text.Append(chunk);
            width += w;
            shown++;
        }

        if (shown < parts.Count) text.Append("· …");
        return text.ToString();
    }

    /// <summary>
    /// 报告页的提醒行：把几句提醒并成一行。放不下就截断。
    /// </summary>
    /// <param name="notes">每句提醒（一个人一句）。</param>
    /// <returns>一行字；一句都没有返回空串。</returns>
    public static string EnfeoffNoticeLine(IReadOnlyList<string>? notes)
    {
        if (notes == null || notes.Count == 0) return "";

        var text = new System.Text.StringBuilder();
        var width = 0.0;
        var shown = 0;

        for (var i = 0; i < notes.Count; i++)
        {
            var chunk = (i == 0 ? "" : "　") + notes[i];
            var w = WidthOf(chunk);
            if (shown > 0 && width + w > ReportLineMaxWidth) break;

            text.Append(chunk);
            width += w;
            shown++;
        }

        if (shown < notes.Count) text.Append("…");
        return text.ToString();
    }

    /// <summary>薄封的野心家：他心里记着。</summary>
    /// <param name="name">功臣名。</param>
    /// <param name="grade">档位中文名。</param>
    /// <returns>一句提醒。</returns>
    public static string EnfeoffWorry(string name, string grade) => $"{name} {grade}——他心里记着。";

    /// <summary>厚封的野心家：他迟早要更多。</summary>
    /// <param name="name">功臣名。</param>
    /// <param name="grade">档位中文名。</param>
    /// <returns>一句提醒。</returns>
    public static string EnfeoffAmbition(string name, string grade) => $"{name} {grade}——他迟早要更多。";

    /// <summary>没封的人：当晚就走了。</summary>
    /// <param name="name">功臣名。</param>
    /// <returns>一句提醒。</returns>
    public static string EnfeoffGone(string name) => $"{name} 走了——他没等到天亮。";

    /// <summary>一段字的宽度：全角算 1，半角（数字、空格、箭头）算 0.55。</summary>
    private static double WidthOf(string text)
    {
        var width = 0.0;
        foreach (var ch in text) width += ch <= 0x7E ? 0.55 : 1.0;
        return width;
    }
}

/// <summary>
/// 什么时候该弹转段界面（票号 S2-05c）：<c>b12</c> 落定的同一刻，且这一档还没转过段。
/// 读档时若 <see cref="TransitionBook.IsDone"/> 已为真，这里一律返回 false——不会再弹第二遍。
/// </summary>
public static class TransitionTrigger
{
    /// <summary>最后一拍：<c>b12</c> 入城三令。</summary>
    public const string LastBeat = "b12";

    /// <summary>
    /// 这一拍做完之后要不要开转段。
    /// </summary>
    /// <param name="book">转段结算器；没接上（表坏）就不弹。</param>
    /// <param name="completedBeatId">刚做完的那一拍。</param>
    /// <returns>要弹返回 true。</returns>
    public static bool IsDue(TransitionBook? book, string? completedBeatId) =>
        book != null
        && !book.IsDone
        && string.Equals(completedBeatId, LastBeat, StringComparison.Ordinal);
}

/// <summary>
/// 转段五页的界面状态（票号 S2-05c / S4-01d）：翻到第几页、光标在哪、哪几条立了、六个人各封哪一档。
///
/// **不可逆**：五页之内 <see cref="CanClose"/> 恒为 false，Esc 不退；落定之后才放人走。
/// 每一次真的落定都调用 <see cref="TransitionBook"/> / <see cref="EnfeoffBook"/>，
/// 存档里的 <c>prologue</c> 当场变——这里不另记一份"玩家选了什么"，免得两份真相对不上。
///
/// 分封那一页要三样东西才活得起来：功臣表、分封账本、以及往哪写三轴与旗标。
/// 老构造（三参数）这三样都没有，分封页整页跳过——旧的转段测试与 <c>--transition</c> 就是这么走的。
/// </summary>
public sealed class TransitionScreen
{
    private readonly TransitionBook _book;
    private readonly RealmTable _realms;
    private readonly SaveState _state;
    private readonly CompanionTable? _companions;
    private readonly EnfeoffBook? _enfeoff;
    private readonly TrustState? _trust;
    private readonly FlagStore? _flags;
    private readonly List<Oath> _made = new();
    private readonly HashSet<string> _picked = new(StringComparer.Ordinal);
    private readonly List<Enfeoff> _grades = new();
    private readonly List<EnfeoffPart> _sealed = new();
    private readonly List<string> _worries = new();

    /// <summary>
    /// 摊开转段界面。构造函数就把第一页摆好（<see cref="Open"/>），Main 拿到的永远是可画的一份。
    /// </summary>
    /// <param name="book">转段结算器（定都 / 国号 / 国策 / 收尾都在它手上）。</param>
    /// <param name="realms">国号表。</param>
    /// <param name="state">要写进去的存档（与 <paramref name="book"/> 手上的是同一份）。</param>
    public TransitionScreen(TransitionBook book, RealmTable realms, SaveState state)
        : this(book, realms, state, null, null, null, null)
    {
    }

    /// <summary>
    /// 带分封的转段界面（S4-01d）：六位功臣一人一档，Enter 时真结算并落进 <c>prologue.enfeoffs</c>。
    /// </summary>
    /// <param name="book">转段结算器。</param>
    /// <param name="realms">国号表。</param>
    /// <param name="state">要写进去的存档。</param>
    /// <param name="companions">六位功臣；null 表示这一版不比分封（分封页跳过）。</param>
    /// <param name="enfeoff">分封账本；与 <paramref name="companions"/> 同生同灭。</param>
    /// <param name="trust">三轴：厚封加官望、薄封加民望、不封加疑心，就地改。</param>
    /// <param name="flags">旗标仓库：离心 / 权臣 / 走了写这里，存档跟着它走。</param>
    public TransitionScreen(
        TransitionBook book,
        RealmTable realms,
        SaveState state,
        CompanionTable? companions,
        EnfeoffBook? enfeoff,
        TrustState? trust,
        FlagStore? flags)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));
        _realms = realms ?? throw new ArgumentNullException(nameof(realms));
        _state = state ?? throw new ArgumentNullException(nameof(state));

        // 四样要么都给，要么都不给：只给一半的分封账本比没有更危险（会静默算半笔）。
        if (companions is not null && enfeoff is not null && trust is not null && flags is not null)
        {
            _companions = companions;
            _enfeoff = enfeoff;
            _trust = trust;
            _flags = flags;
        }

        Open();
    }

    /// <summary>现在翻到第几页。</summary>
    public TransitionPage Page { get; private set; }

    /// <summary>光标停在第几行（0 起）。</summary>
    public int Selected { get; private set; }

    /// <summary>顶上闪的那一句（国策页被跳过时说"你这一趟没许下什么"）；平时空串。</summary>
    public string Flash { get; private set; } = "";

    /// <summary>最近一次不让过的原因；做成之后清空。</summary>
    public string Note { get; private set; } = "";

    /// <summary>这一趟真许过的誓约，按表里的顺序。</summary>
    public IReadOnlyList<Oath> Made => _made;

    /// <summary>五页之内退不出去：只有「序章完成」那一页能按 Esc。</summary>
    public bool CanClose => Page == TransitionPage.Done;

    /// <summary>这一档还差分封：有表、且六个人没都记过。</summary>
    public bool NeedsEnfeoff => _companions is not null
                                && !EnfeoffRecord.AllRecorded(_state, _companions);

    /// <summary>
    /// Esc 在这一页上不管用。留一句话在顶上，别让玩家以为按键坏了。
    /// </summary>
    public void RefuseBack()
    {
        if (CanClose) return;
        Note = "这一步退不回去。";
    }

    /// <summary>
    /// 摆第一页。已经定过都 / 选过国号的档（上次走到一半退出）会照原样高亮，
    /// 但页仍从第一页走起——那十分钟本来就是从头念到尾的。
    /// 分封档位同理：记过的照记过的摆，没记过的一律默认薄封。
    /// </summary>
    public void Open()
    {
        _made.Clear();
        _made.AddRange(_book.MadeOaths());

        _picked.Clear();
        var saved = _state.Prologue.Oaths;
        if (saved is { Count: > 0 })
        {
            foreach (var id in saved)
                if (_made.Any(o => string.Equals(o.Id, id, StringComparison.Ordinal)))
                    _picked.Add(id);
        }
        else
        {
            foreach (var oath in _made.Take(Math.Min(TransitionBook.MaxOaths, _made.Count)))
                _picked.Add(oath.Id);
        }

        _grades.Clear();
        _sealed.Clear();
        _worries.Clear();
        if (_companions is not null)
            foreach (var companion in _companions.All)
                _grades.Add(EnfeoffRecord.TryGet(_state, companion.Id, out var grade)
                    ? grade
                    : Enfeoff.Light);

        Page = TransitionPage.Capital;
        Selected = Math.Max(0, IndexOfCapital(_state.Prologue.Capital));
        Flash = "";
        Note = "";
    }

    // ————————————————————————— 三页的列表 —————————————————————————

    /// <summary>定都页的七行，顺序即 <see cref="TransitionBook.CapitalHubs"/>。</summary>
    /// <returns>七行。</returns>
    public IReadOnlyList<TransitionRow> CapitalRows()
    {
        var rows = new List<TransitionRow>(TransitionBook.CapitalHubs.Count);
        for (var i = 0; i < TransitionBook.CapitalHubs.Count; i++)
        {
            var hub = TransitionBook.CapitalHubs[i];
            rows.Add(new TransitionRow(hub, TransitionText.CapitalRow(i, hub),
                string.Equals(hub, _state.Prologue.Capital, StringComparison.Ordinal)));
        }

        return rows;
    }

    /// <summary>国号页的四行，顺序即 <c>realms.csv</c>。</summary>
    /// <returns>国号表里有几行就是几行。</returns>
    public IReadOnlyList<TransitionRow> RealmRows()
    {
        var rows = new List<TransitionRow>(_realms.Count);
        for (var i = 0; i < _realms.Count; i++)
        {
            var realm = _realms.All[i];
            rows.Add(new TransitionRow(realm.Id, TransitionText.RealmRow(i, realm),
                string.Equals(realm.Id, _state.Prologue.Realm, StringComparison.Ordinal)));
        }

        return rows;
    }

    /// <summary>国策页：**只列真许过的**，一条都没许过就是空表。</summary>
    /// <returns>许过的那些。</returns>
    public IReadOnlyList<TransitionRow> OathRows()
    {
        var rows = new List<TransitionRow>(_made.Count);
        foreach (var oath in _made)
            rows.Add(new TransitionRow(oath.Id, oath.Text, _picked.Contains(oath.Id)));

        return rows;
    }

    /// <summary>已立几条、剩下几条将转成债。</summary>
    /// <returns><c>已立 2 条，其余 1 条将转成债</c>。</returns>
    public string OathSummary() =>
        $"已立 {_picked.Count} 条，其余 {_made.Count - _picked.Count} 条将转成债";

    // ————————————————————————— 分封页（S4-01d） —————————————————————————

    /// <summary>分封页的六行（顺序即 <c>companions.csv</c>）。没有功臣表时是空表。</summary>
    /// <returns>六行。</returns>
    public IReadOnlyList<TransitionRow> EnfeoffRows()
    {
        var rows = new List<TransitionRow>(_companions?.Count ?? 0);
        if (_companions is null) return rows;

        for (var i = 0; i < _companions.Count; i++)
        {
            var companion = _companions.All[i];
            rows.Add(new TransitionRow(companion.Id,
                TransitionText.EnfeoffRow(companion, _grades[i]), false));
        }

        return rows;
    }

    /// <summary>六个人这一趟各封哪一档（按表里的顺序）。</summary>
    /// <returns>档位表；没有功臣表时是空表。</returns>
    public IReadOnlyList<Enfeoff> EnfeoffGrades => _grades;

    /// <summary>光标那一位封的是哪一档。</summary>
    /// <returns>档位；光标不在合法行上时是 <see cref="Enfeoff.Light"/>。</returns>
    public Enfeoff GradeAtCursor =>
        Selected >= 0 && Selected < _grades.Count ? _grades[Selected] : Enfeoff.Light;

    /// <summary>分封页挪光标：到头就停，不绕圈（和国策页同一条手感）。</summary>
    /// <param name="delta">−1 / +1。</param>
    public void MoveEnfeoffCursor(int delta)
    {
        if (Page != TransitionPage.Enfeoff || _grades.Count == 0) return;

        Selected = Math.Clamp(Selected + delta, 0, _grades.Count - 1);
        Note = "";
    }

    /// <summary>
    /// 分封页改档：厚封 → 薄封 → 不封 → 厚封 循环。
    /// 只改面板上的档，不结算——结算要等 Enter，免得玩家左右翻两下忠诚就掉两回。
    /// </summary>
    /// <param name="delta">−1 往左、+1 往右；其余值不认。</param>
    /// <returns>改成了返回 true。</returns>
    public bool CycleGrade(int delta)
    {
        if (Page != TransitionPage.Enfeoff || _grades.Count == 0) return false;
        if (delta == 0) return false;

        var step = delta > 0 ? 1 : -1;
        var count = 3;
        var next = ((int)_grades[Selected] + step + count) % count;
        _grades[Selected] = (Enfeoff)next;
        Note = "";
        return true;
    }

    /// <summary>
    /// 报告页的「分封」行；这一趟没封过就是空串。
    /// </summary>
    /// <returns>一行字。</returns>
    public string EnfeoffLine() => TransitionText.EnfeoffReportLine(_sealed);

    /// <summary>报告页的提醒行（离心 / 权臣 / 走了）；一个都没有就是空串。</summary>
    /// <returns>一行字。</returns>
    public string EnfeoffNotice() => TransitionText.EnfeoffNoticeLine(_worries);

    /// <summary>这一趟定下的封赏（报告页那一行的来源），按表里的顺序；没封过是空表。</summary>
    public IReadOnlyList<EnfeoffPart> SealedParts => _sealed;

    // ————————————————————————— 按键 —————————————————————————

    /// <summary>数字键选都：选中即落进存档。</summary>
    /// <param name="index">0 起。</param>
    /// <returns>存档真的动了返回 true。</returns>
    public bool SelectCapital(int index)
    {
        if (Page != TransitionPage.Capital) return false;
        if (index < 0 || index >= TransitionBook.CapitalHubs.Count)
        {
            Note = "序章里没有这块地。";
            return false;
        }

        var hub = TransitionBook.CapitalHubs[index];
        var result = _book.ChooseCapital(hub);
        if (!result.Ok)
        {
            Note = result.Reason;
            return false;
        }

        Selected = index;
        Note = "";
        return true;
    }

    /// <summary>数字键选国号：选中即落进存档。</summary>
    /// <param name="index">0 起。</param>
    /// <returns>存档真的动了返回 true。</returns>
    public bool SelectRealm(int index)
    {
        if (Page != TransitionPage.Realm) return false;
        if (index < 0 || index >= _realms.Count)
        {
            Note = "国号表里没有这一号。";
            return false;
        }

        var realm = _realms.All[index];
        var result = _book.ChooseRealm(realm.Id);
        if (!result.Ok)
        {
            Note = result.Reason;
            return false;
        }

        Selected = index;
        Note = "";
        return true;
    }

    /// <summary>国策页挪光标。到头就停，不绕圈——三四行里绕圈比按不动更难受。</summary>
    /// <param name="delta">−1 / +1。</param>
    public void MoveCursor(int delta)
    {
        if (Page != TransitionPage.Oaths || _made.Count == 0) return;

        Selected = Math.Clamp(Selected + delta, 0, _made.Count - 1);
        Note = "";
    }

    /// <summary>
    /// 空格：把光标那一行立上 / 撤下。**最多立 <see cref="TransitionBook.MaxOaths"/> 条**；
    /// 多出来的不是不许选，而是注定要转成债——先把话说在前头，别让玩家按了没反应。
    /// </summary>
    /// <returns>选中状态变了返回 true。</returns>
    public bool ToggleOath()
    {
        if (Page != TransitionPage.Oaths || _made.Count == 0) return false;
        if (Selected < 0 || Selected >= _made.Count) return false;

        var id = _made[Selected].Id;

        if (_picked.Remove(id))
        {
            Note = "";
            return true;
        }

        var cap = Math.Min(TransitionBook.MaxOaths, _made.Count);
        if (_picked.Count >= cap)
        {
            Note = $"最多立 {cap} 条，再立的就只能转成债。";
            return false;
        }

        _picked.Add(id);
        Note = "";
        return true;
    }

    /// <summary>
    /// 回车：把这一页交出去，翻下一页。定都 / 国号没选就挡住；
    /// 国策页在这里才真正调 <see cref="TransitionBook.ChooseOaths"/>（空格只改面板上的勾）。
    /// </summary>
    /// <returns>存档真的动了返回 true（国策落定与收尾各算一次）。</returns>
    public bool Advance()
    {
        switch (Page)
        {
            case TransitionPage.Capital:
                if (string.IsNullOrEmpty(_state.Prologue.Capital))
                {
                    Note = "先定都。";
                    return false;
                }

                Page = TransitionPage.Realm;
                Selected = Math.Max(0, IndexOfRealm(_state.Prologue.Realm));
                Note = "";
                return false;

            case TransitionPage.Realm:
                if (string.IsNullOrEmpty(_state.Prologue.Realm))
                {
                    Note = "先定国号。";
                    return false;
                }

                if (_made.Count == 0)
                {
                    // 一条都没许过：国策这页根本不存在，顶上闪一句就过去。
                    Flash = TransitionText.NoOathsFlash;
                    EnterAfterOaths();
                    Note = "";
                    return false;
                }

                Page = TransitionPage.Oaths;
                Selected = 0;
                Flash = "";
                Note = "";
                return false;

            case TransitionPage.Oaths:
                if (_picked.Count == 0)
                {
                    Note = "一条都不立，就别往下走。";
                    return false;
                }

                // 许过几条就要立几条（≤3）：少了的那条不是债，是这道门不让过。
                // 只有许过的超过 3 条时，多出来的才真的转成债。
                var need = Math.Min(TransitionBook.MaxOaths, _made.Count);
                if (_picked.Count < need)
                {
                    Note = $"还差 {need - _picked.Count} 条：许过的都要立，少立的过不了这道门。";
                    return false;
                }

                var ids = _made.Where(o => _picked.Contains(o.Id)).Select(o => o.Id);
                var result = _book.ChooseOaths(ids);
                if (!result.Ok)
                {
                    Note = result.Reason;
                    return false;
                }

                EnterAfterOaths();
                Note = "";
                return true;

            case TransitionPage.Enfeoff:
                if (!SettleEnfeoff())
                {
                    Note = Note.Length > 0 ? Note : "这六个人还没定全。";
                    return false;
                }

                Page = TransitionPage.Report;
                Selected = 0;
                Flash = "";
                Note = "";
                return true;

            case TransitionPage.Report:
                var done = _book.Finish();
                if (!done.Ok)
                {
                    Note = done.Reason;
                    return false;
                }

                Page = TransitionPage.Done;
                Selected = 0;
                Flash = "";
                Note = "";
                return true;

            default:
                return false;
        }
    }

    // ————————————————————————— 分封结算（S4-01d） —————————————————————————

    /// <summary>国策页之后往哪走：还差分封就进分封页，否则直接进报告页。</summary>
    private void EnterAfterOaths()
    {
        Page = NeedsEnfeoff ? TransitionPage.Enfeoff : TransitionPage.Report;
        Selected = 0;
    }

    /// <summary>
    /// 把六个人逐个封下去：先算账（<see cref="EnfeoffBook.Enfeoff"/>），再落档
    /// （<see cref="EnfeoffRecord.Put"/>），最后把人话留给报告页。
    ///
    /// 已经记过的人不再封第二遍——重进转段、或上一次走到一半退出，都不该第二次扣忠诚。
    /// 六个都封成返回 true；有一个没封成（旗标名坏）就整页停住，一个字也不往下走。
    /// </summary>
    /// <returns>六个人都记上了返回 true。</returns>
    private bool SettleEnfeoff()
    {
        if (_companions is null || _enfeoff is null || _trust is null || _flags is null)
        {
            Note = "分封这一页没接上账本。";
            return false;
        }

        _sealed.Clear();
        _worries.Clear();

        for (var i = 0; i < _companions.Count; i++)
        {
            var companion = _companions.All[i];

            if (EnfeoffRecord.TryGet(_state, companion.Id, out _))
                continue;                       // 记过了：这一次不再算

            var grade = _grades[i];
            var result = _enfeoff.Enfeoff(companion, grade, _trust, _flags);
            if (!result.Ok)
            {
                Note = result.Reason;
                return false;
            }

            EnfeoffRecord.Put(_state, companion.Id, grade);

            var label = EnfeoffBook.Label(grade);
            _sealed.Add(new EnfeoffPart(companion.Name, label, companion.Loyalty, result.LoyaltyAfter));

            if (_flags.Has(EnfeoffBook.ResentedFlag(companion.Id), null))
                _worries.Add(TransitionText.EnfeoffWorry(companion.Name, label));
            else if (_flags.Has(EnfeoffBook.WillBePowerfulFlag(companion.Id), null))
                _worries.Add(TransitionText.EnfeoffAmbition(companion.Name, label));
            else if (_flags.Has(EnfeoffBook.LeftFlag(companion.Id), null))
                _worries.Add(TransitionText.EnfeoffGone(companion.Name));
        }

        // 六个人各有一条记录才算这一页做完（记过的也算）。
        return EnfeoffRecord.AllRecorded(_state, _companions);
    }

    // ————————————————————————— 报告页 —————————————————————————

    /// <summary>
    /// 第一份国情报告：上四行是玩家刚选的，接着是分封那一行（有过分封才有）与提醒，
    /// 中间五行是文档原句，最后一行是当下的实数。
    /// </summary>
    /// <param name="facts">兵力 / 三轴 / 囊 / 第几天。</param>
    /// <returns>按打印顺序排好的行（没分封时仍是十行）。</returns>
    public IReadOnlyList<string> ReportLines(ReportFacts facts)
    {
        _realms.TryGet(_state.Prologue.Realm, out var realm);

        var lines = new List<string>(12)
        {
            $"都城 {HubNames.ShortOf(_state.Prologue.Capital)}",
            realm is null ? "国号 —" : TransitionText.RealmLine(realm),
            $"国策 {_state.Prologue.Oaths.Count} 条",
            $"未决 {_state.Prologue.Debts.Count} 条",
        };

        var enfeoff = EnfeoffLine();
        if (enfeoff.Length > 0) lines.Add(enfeoff);

        var notice = EnfeoffNotice();
        if (notice.Length > 0) lines.Add(notice);

        lines.AddRange(TransitionText.ReportFixedLines);
        lines.Add(TransitionText.NumberLine(facts));

        return lines;
    }

    /// <summary>第几页的抬头。收尾页就是那四个字。</summary>
    /// <returns>抬头。</returns>
    public string Head => Page switch
    {
        TransitionPage.Capital => TransitionText.CapitalHead,
        TransitionPage.Realm => TransitionText.RealmHead,
        TransitionPage.Oaths => TransitionText.OathsHead,
        TransitionPage.Enfeoff => TransitionText.EnfeoffHead,
        TransitionPage.Report => TransitionText.ReportHead,
        _ => TransitionText.DoneHead,
    };

    /// <summary>底下那行操作提示。</summary>
    /// <returns>一行字。</returns>
    public string Foot => Page switch
    {
        TransitionPage.Capital => TransitionText.CapitalFoot,
        TransitionPage.Realm => TransitionText.RealmFoot,
        TransitionPage.Oaths => $"{OathSummary()}　·　{TransitionText.OathsFoot}",
        TransitionPage.Enfeoff => TransitionText.EnfeoffFoot,
        TransitionPage.Report => TransitionText.ReportFoot,
        _ => TransitionText.DoneFoot,
    };

    /// <summary>某一页上有几行（画的时候按它决定显示几行）。</summary>
    /// <returns>行数。</returns>
    public int RowCount() => Page switch
    {
        TransitionPage.Capital => TransitionBook.CapitalHubs.Count,
        TransitionPage.Realm => _realms.Count,
        TransitionPage.Oaths => _made.Count,
        TransitionPage.Enfeoff => _grades.Count,
        TransitionPage.Report => 13,
        _ => 0,
    };

    /// <summary>光标那一行是不是"选中的那一行"（报告页与收尾页没有光标）。</summary>
    /// <param name="index">0 起。</param>
    /// <returns>画金色时返回 true。</returns>
    public bool IsCursorAt(int index) =>
        Page is TransitionPage.Capital or TransitionPage.Realm
            or TransitionPage.Oaths or TransitionPage.Enfeoff
        && index == Selected;

    /// <summary>国号 id 在第几行；不认识返回 −1。</summary>
    private int IndexOfRealm(string? id)
    {
        for (var i = 0; i < _realms.Count; i++)
            if (string.Equals(_realms.All[i].Id, id, StringComparison.Ordinal))
                return i;

        return -1;
    }

    /// <summary>hub id 在第几行；不认识返回 −1。</summary>
    private static int IndexOfCapital(string? hub)
    {
        for (var i = 0; i < TransitionBook.CapitalHubs.Count; i++)
            if (string.Equals(TransitionBook.CapitalHubs[i], hub, StringComparison.Ordinal))
                return i;

        return -1;
    }
}

/// <summary>
/// <c>--transition</c> 的那一趟（票号 S2-05c）：十二拍都做完、许过 b07:1 与 b12:1 两个承诺，
/// 自动定都 hub4、取第一个国号、把这两条都立成国策，然后走完四页。
///
/// 走的是真的 <see cref="TransitionScreen"/>，不是另写一条捷径——打印出来的就是玩家那样点出来的。
/// 只动自己造的那一份存档，绝不碰玩家那份。
/// </summary>
public static class TransitionDemo
{
    /// <summary>调试跑批假设许过的两条承诺（oaths.csv 的 source 列）。</summary>
    public static readonly IReadOnlyList<string> PickFlags = new[]
    {
        "beat.b07.pick.1", "beat.b12.pick.1",
    };

    /// <summary>自动定的都（hub4 县城，<see cref="TransitionBook.CapitalHubs"/> 的第 4 项）。</summary>
    public const string Capital = "hub4";

    /// <summary>
    /// 跑一趟，把该打印的行按顺序交回来。
    /// </summary>
    /// <param name="oaths">誓约表。</param>
    /// <param name="realms">国号表。</param>
    /// <param name="state">要写的那份存档；不传就新造一份（测试可以塞进来再看结果）。</param>
    /// <returns>六行：可立条数、定都国号、国策、未决、是否完成、存档说明。</returns>
    public static IReadOnlyList<string> Run(OathTable oaths, RealmTable realms, SaveState? state = null)
    {
        if (oaths == null) throw new ArgumentNullException(nameof(oaths));
        if (realms == null) throw new ArgumentNullException(nameof(realms));

        var flags = new FlagStore();
        foreach (var flag in PickFlags) flags.Set(flag);

        var save = state ?? new SaveState();
        save.Prologue.BeatsDone.Clear();
        for (var i = 1; i <= TransitionBook.MinBeats; i++)
            save.Prologue.BeatsDone.Add($"b{i:00}");

        var book = new TransitionBook(oaths, realms, flags, save);
        var screen = new TransitionScreen(book, realms, save);

        var made = book.MadeOaths();
        var lines = new List<string>(6)
        {
            $"{TransitionText.Prefix} 可立的国策：{Math.Min(TransitionBook.MaxOaths, made.Count)} 条"
            + $"（许过 {made.Count} 条）",
        };

        // 第一页：定都 hub4。第二页：第一个国号。第三页：默认全选，回车即过。
        screen.SelectCapital(IndexOfCapital());
        screen.Advance();
        screen.SelectRealm(0);
        screen.Advance();
        screen.Advance();

        realms.TryGet(save.Prologue.Realm, out var realm);
        lines.Add($"{TransitionText.Prefix} 定都 {HubNames.ShortOf(save.Prologue.Capital)}"
                  + $"（{save.Prologue.Capital}）｜{TransitionText.RealmLine(realm!)}");

        var chosen = new List<string>(save.Prologue.Oaths.Count);
        foreach (var id in save.Prologue.Oaths)
            if (oaths.TryGet(id, out var oath)) chosen.Add(oath.Text);

        lines.Add($"{TransitionText.Prefix} 国策 {chosen.Count} 条：{string.Join(" / ", chosen)}");
        lines.Add($"{TransitionText.Prefix} 未决 {save.Prologue.Debts.Count} 条");

        screen.Advance();   // 报告页回车 → Finish

        lines.Add($"{TransitionText.Prefix} 序章完成：{(book.IsDone ? "是" : "否")}");
        lines.Add($"{TransitionText.Prefix} 存档：未写（调试跑批不碰玩家的档）");

        return lines;
    }

    /// <summary><see cref="Capital"/> 在七块地里的下标。</summary>
    private static int IndexOfCapital()
    {
        for (var i = 0; i < TransitionBook.CapitalHubs.Count; i++)
            if (string.Equals(TransitionBook.CapitalHubs[i], Capital, StringComparison.Ordinal))
                return i;

        return 0;
    }
}

/// <summary>
/// <c>--enfeoff</c> 的那一趟（票号 S4-01d）：六个人的官、默认薄封的账、
/// 把裴无咎改成厚封之后的样子，以及报告页那一行。
///
/// 走的是真的 <see cref="EnfeoffBook"/> 与 <see cref="TransitionText"/>，
/// 只动自己造的那份三轴与旗标，绝不碰玩家那份档。
/// </summary>
public static class EnfeoffDemo
{
    /// <summary>调试行共用的抬头。</summary>
    public const string Prefix = "[分封]";

    /// <summary>调试里改成厚封的那一位：裴无咎（野心 78，厚封会变权臣）。</summary>
    public const string HeavyTarget = "peiwujiu";

    /// <summary>
    /// 跑一趟，把四行按顺序交回来。
    /// </summary>
    /// <param name="companions">六位功臣（companions.csv）。</param>
    /// <returns>四行：六人的官、默认薄封、裴无咎改厚封、报告行。</returns>
    public static IReadOnlyList<string> Lines(CompanionTable companions)
    {
        if (companions == null) throw new ArgumentNullException(nameof(companions));

        var lines = new List<string>(4)
        {
            $"{Prefix} 六人：{string.Join("｜", companions.All.Select(c => $"{c.Name} {c.Office}"))}",
        };

        // 第二行：六个人全按默认的薄封算一遍，忠诚各 +6。
        var lightTrust = new TrustState();
        var lightFlags = new FlagStore();
        var lightBook = new EnfeoffBook();
        var allPlusSix = true;
        foreach (var companion in companions.All)
        {
            var before = companion.Loyalty;
            var result = lightBook.Enfeoff(companion, Enfeoff.Light, lightTrust, lightFlags);
            if (!result.Ok || result.LoyaltyAfter - before != EnfeoffBook.LightLoyalty) allPlusSix = false;
        }

        lines.Add($"{Prefix} 默认薄封：六人忠诚 +{EnfeoffBook.LightLoyalty}"
                  + (allPlusSix ? "" : "（有人已到顶）"));

        // 第三行：把裴无咎单独改成厚封，看野心家的那笔账。
        companions.TryGet(HeavyTarget, out var heavy);
        if (heavy is not null)
        {
            var trust = new TrustState();
            var flags = new FlagStore();
            var result = new EnfeoffBook().Enfeoff(heavy, Enfeoff.Heavy, trust, flags);
            var flag = flags.Has(EnfeoffBook.WillBePowerfulFlag(heavy.Id), null)
                ? EnfeoffBook.WillBePowerfulFlag(heavy.Id)
                : "（无）";

            lines.Add($"{Prefix} 把{heavy.Name}改成厚封 → 忠 {heavy.Loyalty} → {result.LoyaltyAfter}"
                      + $"，置 {flag}，官望 +{EnfeoffBook.HeavyGuan}");
        }
        else
        {
            lines.Add($"{Prefix} 把{HeavyTarget}改成厚封：功臣表里没有这个人");
        }

        // 第四行：五个薄封 + 裴无咎厚封，走真的结算，再看报告页那一行。
        var trust2 = new TrustState();
        var flags2 = new FlagStore();
        var book2 = new EnfeoffBook();
        var parts = new List<EnfeoffPart>(companions.Count);
        foreach (var companion in companions.All)
        {
            var grade = string.Equals(companion.Id, HeavyTarget, StringComparison.Ordinal)
                ? Enfeoff.Heavy
                : Enfeoff.Light;

            var result = book2.Enfeoff(companion, grade, trust2, flags2);
            if (result.Ok)
                parts.Add(new EnfeoffPart(companion.Name, EnfeoffBook.Label(grade),
                    companion.Loyalty, result.LoyaltyAfter));
        }

        lines.Add($"{Prefix} 报告行：{TransitionText.EnfeoffReportLine(parts)}");
        return lines;
    }

    /// <summary>
    /// <c>--shotenfeoff</c> 要的那一屏：真的转段界面停在分封页，光标停在裴无咎、已改成厚封。
    /// </summary>
    /// <param name="companions">六位功臣。</param>
    /// <param name="oaths">誓约表（转段结算器要）。</param>
    /// <param name="realms">国号表。</param>
    /// <returns>画的那一屏与它写的那份存档（自检专用，不落盘）。</returns>
    public static (TransitionScreen Screen, SaveState State) OpenScreen(
        CompanionTable companions, OathTable oaths, RealmTable realms)
    {
        if (companions == null) throw new ArgumentNullException(nameof(companions));
        if (oaths == null) throw new ArgumentNullException(nameof(oaths));
        if (realms == null) throw new ArgumentNullException(nameof(realms));

        var state = new SaveState();
        for (var i = 1; i <= TransitionBook.MinBeats; i++)
            state.Prologue.BeatsDone.Add($"b{i:00}");

        var flags = new FlagStore();
        foreach (var flag in TransitionDemo.PickFlags) flags.Set(flag);

        var trust = new TrustState { Min = 6, Guan = 2, Yi = 3 };
        var book = new TransitionBook(oaths, realms, flags, state);
        var screen = new TransitionScreen(
            book, realms, state, companions, new EnfeoffBook(), trust, flags);

        // 第一页定都 hub4，第二页取第一个国号，第三页默认全立 → 第四页分封。
        screen.SelectCapital(3);
        screen.Advance();
        screen.SelectRealm(0);
        screen.Advance();
        screen.Advance();

        var at = 0;
        for (var i = 0; i < companions.Count; i++)
            if (string.Equals(companions.All[i].Id, HeavyTarget, StringComparison.Ordinal))
                at = i;

        screen.MoveEnfeoffCursor(at - screen.Selected);
        screen.CycleGrade(-1);              // 薄封 → 厚封

        return (screen, state);
    }
}
