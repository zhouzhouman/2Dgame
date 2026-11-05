using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 目标点指的是什么东西（票号 S4-03b）。玩家的原话是
/// <b>「光有目标，没告诉玩家怎么去做，玩家一头雾水，可以像原神一样，有个黄色目标点，或者有旁边提醒」</b>。
///
/// 数据在 <c>beats.csv</c> 的 <c>target</c> 列，五种写法：
/// <list type="bullet">
///   <item><c>exit</c> —— 本空间的出口；</item>
///   <item><c>point:&lt;id&gt;</c> —— 一个可搜点（<c>points.csv</c> 的 id）；</item>
///   <item><c>npc:&lt;名字&gt;</c> —— 一个人（<c>npcs.csv</c> 的 name）；</item>
///   <item><c>hub:&lt;id&gt;</c> —— 另一个空间（按 M 过去）；</item>
///   <item>空 —— 这一拍没有目标点。</item>
/// </list>
/// </summary>
public enum TargetKind
{
    /// <summary>没有目标点（空列 / 认不出的写法）。</summary>
    None,

    /// <summary>本空间的出口。</summary>
    Exit,

    /// <summary>一个可搜点。</summary>
    Point,

    /// <summary>一个站着的人。</summary>
    Npc,

    /// <summary>另一个空间。</summary>
    Hub,
}

/// <summary>
/// 一拍的目标点：指什么、指的哪一个、以及"怎么去"那句话。
///
/// 纯值类型，不认表也不认引擎——解析（<see cref="Parse"/>）与落点判定
/// （<see cref="ObjectiveHud"/>）都在这一层，MapView 只负责画。
/// </summary>
/// <param name="Kind">目标类型。</param>
/// <param name="Id">点位 id / NPC 名字 / hub id；<see cref="TargetKind.Exit"/> 与 <see cref="TargetKind.None"/> 为空串。</param>
/// <param name="Hint">"怎么去"的一句话；没有就是空串。</param>
public readonly record struct Objective(TargetKind Kind, string Id, string Hint)
{
    /// <summary>没有目标点。表里没这两列时全表都退化成它。</summary>
    public static readonly Objective Empty = new(TargetKind.None, "", "");

    /// <summary><c>exit</c> 那一种写法（大小写不敏感）。</summary>
    public const string ExitToken = "exit";

    /// <summary>点位前缀。</summary>
    public const string PointPrefix = "point:";

    /// <summary>人物前缀。</summary>
    public const string NpcPrefix = "npc:";

    /// <summary>空间前缀。</summary>
    public const string HubPrefix = "hub:";

    /// <summary>有没有目标点。</summary>
    public bool HasTarget => Kind != TargetKind.None;

    /// <summary>
    /// 解析 <c>beats.csv</c> 的 <c>target</c> / <c>hint</c> 两列。
    ///
    /// 认不出、写空、前缀后面没东西，一律退化成 <see cref="TargetKind.None"/>——
    /// 但 <c>hint</c> 照留：那句"怎么去"不依赖目标点也能给玩家看。
    /// 大小写与前后空格都容错（<c>" EXIT "</c> 就是出口，<c>hub:HUB4</c> 认成 <c>hub4</c>）。
    /// </summary>
    /// <param name="target">target 列的原文；null 当空串。</param>
    /// <param name="hint">hint 列的原文；null 当空串。</param>
    /// <returns>解析结果，永不抛。</returns>
    public static Objective Parse(string? target, string? hint)
    {
        var text = (hint ?? "").Trim();
        var raw = (target ?? "").Trim();

        if (raw.Length == 0) return new Objective(TargetKind.None, "", text);
        if (raw.Equals(ExitToken, StringComparison.OrdinalIgnoreCase))
            return new Objective(TargetKind.Exit, "", text);

        if (TryId(raw, PointPrefix, out var pointId))
            return pointId.Length == 0
                ? new Objective(TargetKind.None, "", text)
                : new Objective(TargetKind.Point, pointId, text);

        if (TryId(raw, NpcPrefix, out var npcName))
            return npcName.Length == 0
                ? new Objective(TargetKind.None, "", text)
                : new Objective(TargetKind.Npc, npcName, text);

        if (TryId(raw, HubPrefix, out var hubId))
            return hubId.Length == 0
                ? new Objective(TargetKind.None, "", text)
                : new Objective(TargetKind.Hub, hubId.ToLowerInvariant(), text);

        // 「exitx」「point h1_05」这类写法当没写：宁可不指，也别指错。
        return new Objective(TargetKind.None, "", text);
    }

    /// <summary>前缀匹配（大小写不敏感），返回去掉前缀并修过空格的那一段。</summary>
    private static bool TryId(string raw, string prefix, out string id)
    {
        id = "";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        id = raw[prefix.Length..].Trim();
        return true;
    }

    /// <summary>回写成 target 列的样子（日志与 <c>--objective</c> 用）。</summary>
    public string Token => Kind switch
    {
        TargetKind.Exit => ExitToken,
        TargetKind.Point => PointPrefix + Id,
        TargetKind.Npc => NpcPrefix + Id,
        TargetKind.Hub => HubPrefix + Id,
        _ => "",
    };
}

/// <summary>
/// 目标点相关的文案。中文只写在这一处：HUD、<c>--objective</c> 与单测读的是同一份。
/// </summary>
public static class ObjectiveText
{
    /// <summary>目标行下面那一行的开头。中间是全角空格，和游戏里其它行一致。</summary>
    public const string HintPrefix = "怎么去　";

    /// <summary><c>--objective</c> 里带冒号的写法。</summary>
    public const string HintPrefixReport = "怎么去：";

    /// <summary>走近目标时菱形旁边那两个字。</summary>
    public const string ArrivedWord = "到了";

    /// <summary>目标在别的空间时的整句：<c>在 城下营地 · 按 M 过去（2 刻）</c>。</summary>
    public const string AwayFormat = "在 {0} · 按 M 过去（{1} 刻）";

    /// <summary>没有路程账时的退化写法：只说是哪一处。</summary>
    public const string AwayOnlyFormat = "在 {0}";

    /// <summary>目标点行下面的小字：<c>怎么去　…</c>；没有 hint 就是空串（不占位）。</summary>
    /// <param name="hint">beats.csv 的 hint 列。</param>
    /// <returns>一行字或空串。</returns>
    public static string HintLine(string? hint)
        => string.IsNullOrWhiteSpace(hint) ? "" : HintPrefix + hint.Trim();

    /// <summary>目标在另一个空间时那一句。</summary>
    /// <param name="hubId">目标 hub id。</param>
    /// <param name="cost">从当前空间过去要几刻；<c>&lt; 0</c>（没有路）退化成只说地点。</param>
    /// <returns>一行字。</returns>
    public static string Away(string hubId, int cost)
    {
        var where = HubNames.ShortOf(hubId);
        return cost < 0
            ? string.Format(AwayOnlyFormat, where)
            : string.Format(AwayFormat, where, cost);
    }

    /// <summary>
    /// 目标行下面那一行现在该写什么。
    ///
    /// 八步教学期间**整行空着**：底下那条教学条已经在说同一件事，
    /// 顶上再抄一遍就是两行一样的字，玩家只会觉得界面啰嗦。
    /// </summary>
    /// <param name="objective">当前这一拍的目标点。</param>
    /// <param name="tutorialActive">八步教学还没走完。</param>
    /// <param name="travelCost">目标在别的空间时的路程；不是 hub 目标时不看。</param>
    /// <returns>一行字或空串（空串 = 不占位）。</returns>
    public static string HintRow(Objective objective, bool tutorialActive, int travelCost)
    {
        if (tutorialActive) return "";
        if (objective.Kind == TargetKind.Hub) return Away(objective.Id, travelCost);
        return HintLine(objective.Hint);
    }
}

/// <summary>目标点这一帧该以什么形式出现。</summary>
public enum ObjectiveView
{
    /// <summary>这一拍没有目标点，或者目标不在这处。</summary>
    None,

    /// <summary>目标就在这个空间、也在屏幕里：画世界里的黄色菱形。</summary>
    WorldDiamond,

    /// <summary>目标在这个空间，但在屏幕外：画屏幕边缘的箭头。</summary>
    ScreenArrow,

    /// <summary>目标在别的空间：不画箭头，只在目标行下面写"按 M 过去"。</summary>
    AwayHint,
}

/// <summary>
/// 目标点的判定（纯 C#）：走没走到、这一帧画什么。
/// MapView 只照着结果画，规则不在绘制代码里长第二份。
/// </summary>
public static class ObjectiveHud
{
    /// <summary>走到多近算"到了"。格为单位。</summary>
    public const float ArriveRange = 1.5f;

    /// <summary>玩家离目标够不够近——够近就变实心、显示"到了"。</summary>
    /// <param name="playerX">玩家格坐标 X。</param>
    /// <param name="playerY">玩家格坐标 Y。</param>
    /// <param name="targetX">目标格坐标 X。</param>
    /// <param name="targetY">目标格坐标 Y。</param>
    /// <returns>在 <see cref="ArriveRange"/> 格以内返回 true。</returns>
    public static bool IsArrived(float playerX, float playerY, float targetX, float targetY)
    {
        var dx = playerX - targetX;
        var dy = playerY - targetY;
        return dx * dx + dy * dy <= ArriveRange * ArriveRange;
    }

    /// <summary>这一帧该画什么。</summary>
    /// <param name="objective">目标点。</param>
    /// <param name="here">目标解析得到、而且就在当前空间里。</param>
    /// <param name="onScreen">目标的屏幕坐标落在视口内。</param>
    /// <returns>画法。</returns>
    public static ObjectiveView Decide(Objective objective, bool here, bool onScreen)
    {
        if (objective.Kind == TargetKind.None) return ObjectiveView.None;
        if (objective.Kind == TargetKind.Hub) return ObjectiveView.AwayHint;   // 换空间不画箭头
        if (!here) return ObjectiveView.None;                                   // 目标在别的空间
        return onScreen ? ObjectiveView.WorldDiamond : ObjectiveView.ScreenArrow;
    }
}

/// <summary>边缘箭头摆在哪、指哪、多长。</summary>
/// <param name="X">箭头中心的屏幕 X（已贴在内缩边上）。</param>
/// <param name="Y">箭头中心的屏幕 Y。</param>
/// <param name="DirX">指向目标的单位向量 X。</param>
/// <param name="DirY">指向目标的单位向量 Y。</param>
/// <param name="Length">箭头总长（像素），越远越长，有上限。</param>
public readonly record struct EdgeArrow(float X, float Y, float DirX, float DirY, float Length);

/// <summary>
/// 屏幕边缘箭头（票号 S4-03b）：目标在视口外时，沿"屏幕中心 → 目标"的射线
/// 打到内缩边上的那一点画一枚小三角。离得越远，三角拉得越长（上限固定），
/// 玩家低头走一阵就能感到"还远"。
///
/// 纯几何，不认 Godot：四个象限的方向能在单测里直接对。
/// </summary>
public static class ObjectiveArrow
{
    /// <summary>箭头离屏幕边留多少像素，免得贴着边框看不清。</summary>
    public const float Margin = 22f;

    /// <summary>最短长度（像素）。</summary>
    public const float MinLength = 16f;

    /// <summary>最长长度（像素）。上限固定，远到天边也不会占掉半块屏幕。</summary>
    public const float MaxLength = 40f;

    /// <summary>每远一像素长多少。太陡会像根棍子，0.06 刚好。</summary>
    public const float LengthPerPixel = 0.06f;

    /// <summary>
    /// 目标在视口外时算箭头；在视口内返回 false（这时该画世界里的菱形）。
    /// </summary>
    /// <param name="targetX">目标屏幕 X。</param>
    /// <param name="targetY">目标屏幕 Y。</param>
    /// <param name="viewW">视口宽。</param>
    /// <param name="viewH">视口高。</param>
    /// <param name="margin">内缩边距。</param>
    /// <param name="arrow">算出来的箭头。</param>
    /// <returns>目标在视口外返回 true。</returns>
    public static bool TryPlace(
        float targetX, float targetY, float viewW, float viewH, float margin, out EdgeArrow arrow)
    {
        arrow = default;
        if (viewW <= 0f || viewH <= 0f) return false;

        // 在视口里：不画边缘箭头，交给世界里的菱形。
        if (targetX >= 0f && targetX <= viewW && targetY >= 0f && targetY <= viewH) return false;

        var cx = viewW * 0.5f;
        var cy = viewH * 0.5f;
        var dx = targetX - cx;
        var dy = targetY - cy;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 0.001f) return false;

        dx /= dist;
        dy /= dist;

        // 射线打到内缩矩形上的那一点。矩形被视口挤得太小时，退化成中心线。
        var minX = MathF.Min(margin, cx);
        var maxX = MathF.Max(viewW - margin, cx);
        var minY = MathF.Min(margin, cy);
        var maxY = MathF.Max(viewH - margin, cy);

        var tx = dx > 0f ? (maxX - cx) / dx : dx < 0f ? (minX - cx) / dx : float.PositiveInfinity;
        var ty = dy > 0f ? (maxY - cy) / dy : dy < 0f ? (minY - cy) / dy : float.PositiveInfinity;
        var t = MathF.Min(tx, ty);
        if (!(t > 0f) || float.IsInfinity(t)) t = 0f;

        var px = cx + dx * t;
        var py = cy + dy * t;

        var length = Math.Clamp(dist * LengthPerPixel, MinLength, MaxLength);
        arrow = new EdgeArrow(px, py, dx, dy, length);
        return true;
    }
}

/// <summary>
/// <c>--objective</c> 打印的那几行：十二拍各自解析成什么、怎么去。
///
/// 格式与票面一字不差：
/// <code>
/// [目标点] b01 exit　怎么去：顺着屋子北边的黄框走
/// [目标点] b02 point:h1_05（城南小院）　怎么去：…
/// [目标点] b05 hub:hub4　在 县城 · 按 M 过去（2 刻）
/// </code>
/// 数据从表里读，id / hub / 名字一律由调用方查，不在这里写死。
/// </summary>
public static class ObjectiveReport
{
    /// <summary>每一行的开头。</summary>
    public const string Prefix = "[目标点] ";

    /// <summary>目标为空时那几个字。</summary>
    public const string NoTarget = "无目标";

    /// <summary>打印十二拍。顺序与 <paramref name="beats"/> 一致。</summary>
    /// <param name="beats">节拍表。</param>
    /// <param name="objectives">目标表（beats.csv 的 target / hint 两列）。</param>
    /// <param name="hubOfPoint">点位 id → 它在哪个 hub；查不到返回 null。</param>
    /// <param name="travelCost">当前空间 → 目标空间要几刻；没有路返回负数。</param>
    /// <returns>按打印顺序排好的行。</returns>
    public static IReadOnlyList<string> Lines(
        BeatTable beats, ObjectiveTable objectives,
        Func<string, string?> hubOfPoint, Func<string, int> travelCost)
    {
        if (beats == null) throw new ArgumentNullException(nameof(beats));
        if (objectives == null) throw new ArgumentNullException(nameof(objectives));

        var lines = new List<string>(beats.Count);
        foreach (var beat in beats.All)
            lines.Add(Line(beat.Id, objectives.Of(beat.Id), hubOfPoint, travelCost));

        return lines;
    }

    /// <summary>一拍一行。</summary>
    /// <param name="beatId">节拍 id（如 b01）。</param>
    /// <param name="objective">解析好的目标。</param>
    /// <param name="hubOfPoint">点位 id → hub；null 表示不认识这个点位。</param>
    /// <param name="travelCost">去目标 hub 的刻数；没有路返回负数。</param>
    /// <returns>一行字。</returns>
    public static string Line(
        string beatId, Objective objective,
        Func<string, string?> hubOfPoint, Func<string, int> travelCost)
    {
        var what = What(objective, hubOfPoint);
        var head = Prefix + beatId + " " + what;

        if (objective.Kind == TargetKind.Hub)
            return head + "　" + ObjectiveText.Away(objective.Id, travelCost(objective.Id));

        if (objective.Hint.Length > 0)
            return head + "　" + ObjectiveText.HintPrefixReport + objective.Hint;

        return head;
    }

    /// <summary>target 那一截怎么写：<c>exit</c> / <c>point:h1_05（城南小院）</c> / …</summary>
    private static string What(Objective objective, Func<string, string?> hubOfPoint)
    {
        switch (objective.Kind)
        {
            case TargetKind.Exit:
                return Objective.ExitToken;

            case TargetKind.Point:
            {
                var hub = hubOfPoint(objective.Id);
                return hub is null
                    ? Objective.PointPrefix + objective.Id
                    : Objective.PointPrefix + objective.Id + $"（{HubNames.ShortOf(hub)}）";
            }

            case TargetKind.Npc:
                return Objective.NpcPrefix + objective.Id;

            case TargetKind.Hub:
                return Objective.HubPrefix + objective.Id;

            default:
                return NoTarget;
        }
    }
}
