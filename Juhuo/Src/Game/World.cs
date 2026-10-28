using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Juhuo.Application;
using Juhuo.Art;
using Juhuo.Core;

// E1 在 Core 里也有一份 FlagExpression（S1-02，收 FlagProbe 的重载）。Game 这边继续用
// Application 那份，参数是 (表达式, FlagStore, hub)——这里用别名钉死，免得两个 using 打架。
using FlagExpression = Juhuo.Application.FlagExpression;

namespace Juhuo;

/// <summary>场景里一件可以搜查的实物。</summary>
public sealed class Interactable
{
    public SearchPoint Point = default!;
    public PropKind Kind;
    public Vector2 Cell;        // 1-based 格子坐标
    public bool Blocks;
}

/// <summary>玩家。位置用格子坐标存，渲染时再换算成像素。</summary>
public sealed class Player
{
    public Vector2 Cell = new(6, 14);
    /// <summary>0 下 1 左 2 右 3 上。</summary>
    public int Dir = 0;
    public float Anim;
    public bool Moving;
}

/// <summary>巡逻的衙役。沿路点来回走，走到一头停一会儿。</summary>
public sealed class Patroller
{
    public List<Vector2> Path = new();
    public int Index;
    public float Speed = 2.2f;
    public float WaitEach = 1.2f;
    public float WaitLeft;
    public Vector2 Cell;
    public bool Moving;
    public bool PingPong = true;
    public int Dir = 1;

    /// <summary>名字。巡逻的人会走开，说话时只能靠名字认。</summary>
    public string Name = "衙役";

    // —— 视野 ——
    /// <summary>朝向（单位向量）。停下来时保持上一次的方向。</summary>
    public Vector2 Facing = new(0, 1);
    /// <summary>视野半径，单位是格。</summary>
    public float ViewRange = 5.5f;
    /// <summary>视野张角，单位是度。</summary>
    public float ViewAngle = 110f;
    /// <summary>警觉度 0~1。看见你会涨，看不见会慢慢退。</summary>
    public float Alert;

    public void Update(float dt)
    {
        if (Path.Count < 2) return;

        if (WaitLeft > 0f)
        {
            WaitLeft -= dt;
            Moving = false;
            return;
        }

        var target = Path[Index];
        var d = target - Cell;
        var dist = d.Length();
        if (dist < 0.05f)
        {
            Cell = target;
            WaitLeft = WaitEach;
            Moving = false;

            if (PingPong)
            {
                if (Index + Dir < 0 || Index + Dir >= Path.Count) Dir = -Dir;
                Index += Dir;
            }
            else
            {
                Index = (Index + 1) % Path.Count;
            }
            return;
        }

        Moving = true;
        var step = Speed * dt;
        if (step > dist) step = dist;
        Facing = d / dist;
        Cell += Facing * step;
    }
}

/// <summary>
/// 一个 hub 的运行时状态：地形、可交互物、玩家、巡逻。
/// 纯逻辑，不知道 Godot 的绘制——MapView 读它、画它。
/// </summary>
public sealed class World
{
    private readonly PointTable _table;
    private readonly NpcTalkTable _talks;

    /// <summary>
    /// 对话分支。数据表没有条件列时，建出来就是 first / again / interrupt 三段纯文本，
    /// 和改造前一样；补了条件列就自动有分支（见 <see cref="DialogueBook"/>）。
    /// </summary>
    private readonly DialogueBook _book;

    /// <summary>Hub1 的敲门计时。按刻算，不按秒（见 <see cref="KnockTimer"/>）。</summary>
    private readonly KnockTimer _knock = new();

    /// <summary>
    /// 每个空间一本刻账。刻数只有 Core 说了算，这里只负责取——
    /// Game 层不再自己算 KeUsed / KeLeft（rule_time_clock.md 是唯一数值源）。
    /// </summary>
    private readonly Dictionary<string, Clock> _clocks = new();

    /// <summary>
    /// 每个空间搜过的东西，按搜的先后排。存档里的 searched 是有序的，
    /// 后搜的文本要引用先搜的，所以不能用 HashSet。
    /// </summary>
    private readonly Dictionary<string, List<string>> _searched = new();

    /// <summary>每个空间被看见过几次（存档里的 hubs.*.caught_count）。</summary>
    private readonly Dictionary<string, int> _caught = new();

    /// <summary>
    /// 旗标仓库。取值只有 <see cref="FlagStore.Has"/> 一条路
    /// （rule_flags_save.md §公式），Game 层不自己判真假。
    /// </summary>
    private readonly FlagStore _flags = new();

    /// <summary>
    /// 状态变了，该存盘了。
    ///
    /// 刻账、旗标、搜过的东西、说过的话 —— 这些动过了就该落盘
    /// （rule_flags_save.md §2 的存盘时机）。
    /// 由表现层接上存档服务，这一层自己不碰文件。
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// 门外敲了三下。表现层接上它就够——文本在世界这边，界面不用再抄一遍。
    /// </summary>
    public event Action? Knocked;

    /// <summary>这个空间的刻账。</summary>
    private Clock _clock = new();

    public string Hub { get; private set; } = "hub1";
    public TerrainMap Map { get; private set; } = default!;
    public List<Interactable> Props { get; } = new();
    public List<Patroller> Guards { get; } = new();

    /// <summary>站在那里的 NPC（不巡逻的那些）。</summary>
    public List<SceneConfig.Npc> Npcs { get; } = new();
    public Player Player { get; } = new();

    /// <summary>说过话的人，key 是 hub:x,y 或 hub:guard:名字。</summary>
    public HashSet<string> Talked { get; } = new();

    /// <summary>跟每个人说过几次（存档里的 known.npcs[].again）。</summary>
    private readonly Dictionary<string, int> _talkCounts = new();

    /// <summary>这一处搜过的东西，按搜的先后排。</summary>
    public IReadOnlyList<string> Searched =>
        _searched.TryGetValue(Hub, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>这件东西搜过了没有。</summary>
    public bool IsSearched(string id) =>
        _searched.TryGetValue(Hub, out var list) && list.Contains(id);

    /// <summary>旗标仓库。给表现层与存档用。</summary>
    public FlagStore Flags => _flags;

    /// <summary>已用刻。整数刻，向下取整（Core 说了算，对玩家有利）。</summary>
    public int KeUsed => _clock.KeUsedKe;

    /// <summary>这一处的刻预算（Core 的 ClockTable 查出来的那份）。</summary>
    public int KeBudget => _clock.KeBudgetHalf / 2;

    /// <summary>还剩几刻。</summary>
    public int KeLeft => _clock.KeLeftKe;

    /// <summary>被催过几次。</summary>
    public int Nudges => _clock.Nudges;

    public string? LastMessage { get; private set; }
    public float MessageTime { get; private set; }

    /// <summary>
    /// 上一次搜查为什么没成。空串 = 没被拦（要么压根没有可搜的东西）。
    /// 表现层靠它决定说"这里没什么可翻的"还是"这里现在还看不出来"。
    /// </summary>
    public string LastSearchNote { get; private set; } = "";

    /// <summary>
    /// 上一次盘问为什么没成。空串 = 没试过或成了。
    /// 盘点入口默认是关的：数据表没有 interrogate 那一段，就问不出东西。
    /// </summary>
    public string LastTalkNote { get; private set; } = "";

    // —— 被发现 ——
    /// <summary>被发现后屏幕边缘的红晕强度，1.2 秒内衰减到 0。</summary>
    public float SpotFlash { get; private set; }
    /// <summary>这一处被看见过几次。每三次触发一次盘问。</summary>
    public int TimesSpotted => _caught.TryGetValue(Hub, out var n) ? n : 0;
    /// <summary>当前有没有人正看着你。</summary>
    public bool BeingWatched { get; private set; }

    private float _seenTimer;

    /// <summary>白幡村每搜四个点，村那头就少一个人。这是设计里最狠的一条。</summary>
    public int VillagerDeaths { get; private set; }

    /// <summary>村里死了的人，按死的先后（存档里的 hubs.hub3.patients）。</summary>
    private readonly List<string> _deadVillagers = new();

    /// <summary>
    /// 七个空间的标题。
    ///
    /// 刻预算不在这里 —— 预算只有 Core 的 ClockTable 一份，
    /// 数值源是 rule_time_clock.md §2。这里再抄一遍就是第二套真相。
    /// </summary>
    private static readonly Dictionary<string, string> HubTitles = new()
    {
        ["hub1"] = "城南小院",
        ["hub2"] = "逃亡路 · 废驿",
        ["hub3"] = "白幡村",
        ["hub4"] = "县城",
        ["hub5"] = "山神庙",
        ["hub6"] = "官仓 · 破庙",
        ["hub7"] = "城下营地",
    };

    private static readonly string[] HubOrder =
        { "hub1", "hub2", "hub3", "hub4", "hub5", "hub6", "hub7" };

    public World(PointTable table, NpcTalkTable talks, DialogueBook? book = null)
    {
        _table = table;
        _talks = talks;
        _book = book ?? DialogueBook.FromTalks(talks);
        Load("hub1");
    }

    public static string TitleOf(string hub) => HubTitles.TryGetValue(hub, out var v) ? v : hub;

    public static int IndexOf(string hub) => Array.IndexOf(HubOrder, hub);

    public void Load(string hub)
    {
        if (!HubTitles.ContainsKey(hub)) hub = "hub1";

        Hub = hub;
        var text = FileAccess.GetFileAsString($"res://Content/maps/{hub}.txt").Replace("\r", "");
        Map = new TerrainMap(text);

        Props.Clear();
        Guards.Clear();
        Npcs.Clear();

        // 刻账：进过的空间接着算，没进过的从 0 起（rule_time_clock.md §例外 7）。
        // 预算是 Core 的 ClockTable 查的，不是这里写死的。
        if (!_clocks.TryGetValue(hub, out var clock))
        {
            clock = new Clock(hub);
            _clocks[hub] = clock;
        }
        _clock = clock;

        if (!_searched.TryGetValue(hub, out _))
            _searched[hub] = new List<string>();

        foreach (var p in _table.ForHub(hub))
        {
            var kind = PropKinds.Of(p.Name);
            Props.Add(new Interactable
            {
                Point = p,
                Kind = kind,
                Cell = new Vector2(p.X, p.Y),
                Blocks = PropKinds.Blocks(kind),
            });
        }

        // 起点和 NPC 来自场景配置，不再依赖地图里的 @ 和 A-H
        var scene = SceneConfig.Of(hub);
        Player.Cell = scene.Spawn;
        foreach (var n in scene.Npcs) Npcs.Add(n);

        // 回到白幡村时，死了的人不会站起来
        for (var i = 0; i < VillagerDeaths; i++)
        {
            var idx = Npcs.FindLastIndex(x => x.Kind == "sick");
            if (idx >= 0) Npcs.RemoveAt(idx);
        }

        BuildPatrol(hub);

        GD.Print($"[世界] {hub} 起点 {scene.Spawn}，可搜 {Props.Count} 件，NPC {Npcs.Count} 人，巡逻 {Guards.Count} 人");

        // 站着的人必须有台词。少一条，画面上就是"有个人，但按 E 没反应"——
        // 玩家会以为交互坏了，其实是内容没写。
        foreach (var n in Npcs)
        {
            if (_talks.At(hub, (int)n.Cell.X, (int)n.Cell.Y) is not null) continue;
            GD.Print($"[哑巴] {hub} {n.Kind} @({(int)n.Cell.X},{(int)n.Cell.Y}) 没有台词");
        }

        // 出发前先把脸转向第一个路点
        foreach (var g in Guards)
        {
            if (g.Path.Count < 2) continue;
            var to = g.Path[g.Index] - g.Cell;
            if (to.Length() > 0.01f) g.Facing = to.Normalized();
        }

        // 读档回到小院时，刻已经过了线、旗标却没置——补敲一次。
        // 敲过的档带着 hub1.knock.happened，所以重进不会重放。
        CheckKnock();
    }

    /// <summary>走到出口就进下一个 hub；最后一个 hub 就停在原地。</summary>
    /// <summary>这一处还剩几件没看过。出口提示要用它劝一句。</summary>
    public int UnsearchedHere => Props.Count(p => !IsSearched(p.Point.Id));

    /// <summary>下一关的 hub id；已经是最后一关就返回 null。</summary>
    public string? NextHub
    {
        get
        {
            var i = IndexOf(Hub);
            return i >= 0 && i < HubOrder.Length - 1 ? HubOrder[i + 1] : null;
        }
    }

    public bool TryExit()
    {
        var i = IndexOf(Hub);
        if (i < 0 || i >= HubOrder.Length - 1) return false;

        // 赶路 2 刻，记在离开的那个空间（rule_time_clock.md §3 / §验收 8）。
        _clock.SpendTravel();

        Load(HubOrder[i + 1]);
        StateChanged?.Invoke();
        return true;
    }

    // ————————————————————————— 移动 —————————————————————————

    private const float Radius = 0.30f;

    public void Update(float dt)
    {
        if (MessageTime > 0f)
        {
            MessageTime -= dt;
            if (MessageTime <= 0f) LastMessage = null;
        }

        foreach (var g in Guards) g.Update(dt);
        UpdateVision(dt);
    }

    // ————————————————————————— 视野 —————————————————————————

    /// <summary>谁正看着你。看的是朝向、张角、距离，还有中间有没有墙。</summary>
    public Patroller? WhoSeesMe()
    {
        foreach (var g in Guards)
        {
            var d = Player.Cell - g.Cell;
            var dist = d.Length();
            if (dist > g.ViewRange || dist < 0.01f) continue;

            // 点积判角度：正中是 1，边缘是 cos(半角)
            var dot = d.Dot(g.Facing) / dist;
            if (dot < Mathf.Cos(g.ViewAngle * 0.5f * (Mathf.Pi / 180f))) continue;

            if (LineBlocked(g.Cell, Player.Cell)) continue;
            return g;
        }

        return null;
    }

    /// <summary>视线中间隔着墙就看不见——这条比什么都重要，否则躲墙后没意义。</summary>
    private bool LineBlocked(Vector2 a, Vector2 b)
    {
        var dist = (b - a).Length();
        var steps = Mathf.Max(2, Mathf.CeilToInt(dist * 3f));

        for (var i = 1; i < steps; i++)
        {
            var p = a.Lerp(b, i / (float)steps);
            if (!CellWalkable(Mathf.RoundToInt(p.X), Mathf.RoundToInt(p.Y))) return true;
        }

        return false;
    }

    private void UpdateVision(float dt)
    {
        var seer = WhoSeesMe();
        BeingWatched = seer is not null;

        foreach (var g in Guards)
            g.Alert = Mathf.Max(0f, g.Alert - dt * 0.7f);

        if (seer is null)
        {
            _seenTimer = Mathf.Max(0f, _seenTimer - dt * 0.8f);
            return;
        }

        seer.Alert = Mathf.Min(1f, seer.Alert + dt * 1.8f);
        _seenTimer += dt;

        // 不是一照面就喊，得看住半秒——给玩家一个"赶紧走开"的窗口
        if (_seenTimer < 0.55f) return;

        _seenTimer = 0f;
        var spotted = TimesSpotted + 1;
        _caught[Hub] = spotted;
        SpotFlash = 1.2f;

        // 被打断 2 刻；第三次被看见改成盘问 4 刻，是替代不是叠加
        // （rule_time_clock.md §3）。记账交给 Core。
        if (spotted % 3 == 0) _clock.SpendInterrogate();
        else _clock.SpendInterrupt();

        // 打断记忆：这个人下次见你，第一句换成"上次你走了"。
        // 盘问也算打断——话说了一半被摁住，比被看见更该记住。
        var seerKey = DialogueBook.KeyOfGuard(Hub, seer.Name);
        if (!_flags.Set(DialogueFlags.Interrupted(seerKey)))
            GD.PushWarning($"[旗标] 打断记忆写不进去：{seerKey}");

        CheckKnock();

        Say(spotted % 3 == 0
            ? "「站住。」他把手按在刀上。你多花了些工夫才脱身。"
            : "衙役看见你了。他没喊，只是把你的脸记下了。");
    }

    /// <summary>分轴移动：撞到墙时还能贴着墙滑，手感比整块卡住好。</summary>
    public void Move(float dx, float dy)
    {
        var moved = false;

        if (Mathf.Abs(dx) > 0.0001f)
        {
            var nx = Player.Cell.X + dx;
            if (Walkable(nx, Player.Cell.Y))
            {
                Player.Cell = new Vector2(nx, Player.Cell.Y);
                moved = true;
            }
        }

        if (Mathf.Abs(dy) > 0.0001f)
        {
            var ny = Player.Cell.Y + dy;
            if (Walkable(Player.Cell.X, ny))
            {
                Player.Cell = new Vector2(Player.Cell.X, ny);
                moved = true;
            }
        }

        if (Mathf.Abs(dx) > 0.0001f) Player.Dir = dx > 0 ? 2 : 1;
        else if (Mathf.Abs(dy) > 0.0001f) Player.Dir = dy > 0 ? 0 : 3;
        Player.Moving = moved;
        if (moved) Player.Anim += 0.24f;
    }

    public bool Walkable(float cx, float cy)
    {
        Span<(float dx, float dy)> pts = stackalloc (float, float)[]
        {
            (-Radius, -Radius), (Radius, -Radius),
            (-Radius, Radius), (Radius, Radius),
            (0f, 0f),
        };

        foreach (var (ox, oy) in pts)
        {
            // 用 Round 而不是 Floor：Cell 的整数位表示"第几格的中心"，
            // 格子 i 的实际范围是 [i-0.5, i+0.5]。用 Floor 会整体偏半格，
            // 结果是 1 格宽的走廊永远走不过去——上半身会跨进墙里。
            var gx = Mathf.RoundToInt(cx + ox);
            var gy = Mathf.RoundToInt(cy + oy);

            if (!CellWalkable(gx, gy)) return false;
            if (PropBlocks(gx, gy)) return false;
        }

        return true;
    }

    /// <summary>1-based 格子能不能走。视野射线也用它判断遮挡。</summary>
    public bool CellWalkable(int cx, int cy)
    {
        // IsWalkable 收 0-based，地图里的坐标是 1-based
        return Map.IsWalkable(cx - 1, cy - 1);
    }

    private bool PropBlocks(int cx, int cy)
    {
        foreach (var p in Props)
        {
            if (!p.Blocks) continue;
            if (Mathf.FloorToInt(p.Cell.X) == cx && Mathf.FloorToInt(p.Cell.Y) == cy) return true;
        }
        return false;
    }

    // ————————————————————————— 交互 —————————————————————————

    /// <summary>离玩家最近、且在伸手范围内的东西。</summary>
    /// <summary>
    /// 伸手够得着的东西。判定用"八方向相邻"，不用圆形半径。
    ///
    /// 原来用半径 1.15：斜对角（距离 1.41）就不够长。玩家明明贴着柜子站，
    /// 按 E 却什么都不发生——看起来像交互坏了，其实是数学把斜角排除了。
    /// </summary>
    public Interactable? Nearest()
    {
        Interactable? best = null;
        var bestD = float.MaxValue;

        foreach (var p in Props)
        {
            if (!CanTouch(Player.Cell, p.Cell)) continue;

            var dist = (p.Cell - Player.Cell).LengthSquared();
            if (dist >= bestD) continue;
            bestD = dist;
            best = p;
        }

        return best;
    }

    /// <summary>
    /// 站在 from 能不能碰到 at。判定是八方向相邻（含斜角），
    /// 斜角再加一条"不许穿墙角"：两个正交邻居至少有一个站得下。
    ///
    /// 之前用圆形半径 1.15，斜对角的距离是 1.41，够不着——
    /// 玩家贴着柜子站，按 E 却没反应，看起来就是"交互坏了"。
    /// </summary>
    public bool CanTouch(Vector2 from, Vector2 at)
    {
        var d = at - from;
        if (Mathf.Abs(d.X) > 1.1f || Mathf.Abs(d.Y) > 1.1f) return false;

        if (Mathf.Abs(d.X) <= 0.6f || Mathf.Abs(d.Y) <= 0.6f) return true;

        var px = Mathf.RoundToInt(from.X);
        var py = Mathf.RoundToInt(from.Y);
        var sx = (int)Mathf.Sign(d.X);
        var sy = (int)Mathf.Sign(d.Y);

        return CellWalkable(px + sx, py) || CellWalkable(px, py + sy);
    }

    // 这里原来还有一份 CanReach（只看局部的可达性判断）。票号 S1-DIALOG-b：
    // 可达性只留 Core 的 ReachAudit 一份实现，那份已经删掉，别再长回来。

    /// <summary>本场景所有能说话的人（Ctrl 高亮用）。</summary>
    public IEnumerable<NpcTalk> TalksHere()
    {
        foreach (var n in _talks.ForHub(Hub)) yield return n;
    }

    /// <summary>伸手够得着的、能说话的人。判定和物件一致：八方向相邻。</summary>
    public NpcTalk? NearestTalk()
    {
        NpcTalk? best = null;
        var bestD = float.MaxValue;

        foreach (var n in _talks.ForHub(Hub))
        {
            var here = new Vector2(n.X, n.Y);
            if (!CanTouch(Player.Cell, here)) continue;

            var dist = (here - Player.Cell).LengthSquared();
            if (dist >= bestD) continue;
            bestD = dist;
            best = n;
        }

        return best;
    }

    /// <summary>一个能说话的人：站着不动的，或者正在巡逻的衙役。</summary>
    public sealed record Speaker(string Name, string First, string Again, string Key);

    /// <summary>
    /// 伸手够得着的、能说话的人。先找站在那儿的，再找路过的。
    /// 巡逻的人坐标一直在变，所以按名字去台词表里认，不按坐标。
    /// </summary>
    public Speaker? NearestSpeaker()
    {
        var t = NearestTalk();
        if (t is not null)
            return new Speaker(t.Name, t.First, t.Again, $"{Hub}:{t.X},{t.Y}");

        foreach (var g in Guards)
        {
            if (!CanTouch(Player.Cell, g.Cell)) continue;

            var talk = _talks.ByName(Hub, g.Name);
            if (talk is null) continue;

            return new Speaker(g.Name, talk.First, talk.Again, $"{Hub}:guard:{g.Name}");
        }

        return null;
    }

    /// <summary>
    /// 跟人说话。说哪一段由 <see cref="DialogueVariant.Choose"/> 定：
    /// 初见 → 被打断过 → 再谈。首次 0 刻，其余 1 刻（CANON 3 / rule_time_clock.md §3）。
    ///
    /// "被打断后重谈仍按再谈计费"是这条的硬要求：<c>met</c> 一旦成立就不再免费，
    /// 哪怕这一次说的是被打断的那句。
    /// </summary>
    public (string Head, string Body)? Talk()
    {
        var who = NearestSpeaker();
        if (who is null) return null;

        var met = Talked.Contains(who.Key);
        var interrupted = met && _flags.Has(DialogueFlags.Interrupted(who.Key), Hub);

        var variant = DialogueVariant.Choose(met, interrupted, false);
        var node = _book.Resolve(who.Key, variant, _flags, Hub);

        // 没这一段（或这一段没字）就退回纯文本：again 空了说 first——改造前就是这个行为。
        var body = node is { Text.Length: > 0 }
            ? node.Text
            : met && who.Again.Length > 0 ? who.Again : who.First;

        var label = variant switch
        {
            DialogueVariant.First => "初见",
            DialogueVariant.Interrupt when node is not null => "上次没说完",
            _ => "再谈",
        };

        Talked.Add(who.Key);
        _talkCounts[who.Key] = _talkCounts.TryGetValue(who.Key, out var n) ? n + 1 : 1;

        _clock.SpendTalk(!met);

        // 打断记忆是一次性的：说过了就清掉，别让这个人一辈子都在"上次你走了"。
        if (interrupted) _flags.Clear(DialogueFlags.Interrupted(who.Key));

        ApplyFlag(node?.SetFlag);

        CheckKnock();
        StateChanged?.Invoke();
        return ($"{who.Name}　　{label}", body);
    }

    /// <summary>
    /// 盘问：4 刻，替代打断不是叠加（rule_time_clock.md §3）。
    ///
    /// 两道门：这个人得能盘问（旗标 <c>talk.&lt;npc&gt;.allowed</c>），
    /// 而且台词表里得有 <c>interrogate</c> 那一段——缺一样，入口就是关的（票面要求）。
    /// 现在 <c>npcs.csv</c> 没有这一列，所以默认全关。
    /// </summary>
    public (string Head, string Body)? Interrogate()
    {
        var who = NearestSpeaker();
        if (who is null)
        {
            LastTalkNote = "身边没人。";
            return null;
        }

        if (!_flags.Has(DialogueFlags.Allowed(who.Key), Hub))
        {
            LastTalkNote = "这时候问不出什么。";
            return null;
        }

        var node = _book.Resolve(who.Key, DialogueVariant.Interrogate, _flags, Hub);
        if (node is null)
        {
            LastTalkNote = "他没什么可说的。";
            return null;
        }

        LastTalkNote = "";
        _clock.SpendInterrogate();
        ApplyFlag(node.SetFlag);

        Talked.Add(who.Key);
        _talkCounts[who.Key] = _talkCounts.TryGetValue(who.Key, out var n) ? n + 1 : 1;

        CheckKnock();
        StateChanged?.Invoke();
        return ($"{who.Name}　　盘问", node.Text);
    }

    /// <summary>身边的人现在能不能盘问。界面拿它决定要不要给出这个入口。</summary>
    public bool CanInterrogate
    {
        get
        {
            var who = NearestSpeaker();
            if (who is null) return false;
            if (!_flags.Has(DialogueFlags.Allowed(who.Key), Hub)) return false;
            return _book.Resolve(who.Key, DialogueVariant.Interrogate, _flags, Hub) is not null;
        }
    }

    /// <summary>
    /// 到点就敲门。只敲一次（旗标 <c>hub1.knock.happened</c> 保证），
    /// 刻从 <see cref="Clock.KeUsedKe"/> 读，这里不自己算。
    /// </summary>
    private void CheckKnock()
    {
        if (!_knock.TryKnock(_flags, Hub, _clock.KeUsedKe)) return;

        Say("院门被拍响了三下。门外有人站着，没喊你的名字。", 3.6f);
        Knocked?.Invoke();
    }

    /// <summary>
    /// 搜一次。返回要显示的文本；已经搜过就返回重复文本。
    /// </summary>
    public (string Text, bool First, Interactable Prop)? Search()
    {
        var p = Nearest();
        if (p is null)
        {
            LastSearchNote = "";
            return null;
        }

        // 知识门：条件没满足就当没这回事。0 刻、不置 searched、不置旗标
        // （rule_time_clock.md §例外 3 / rule_flags_save.md §例外 5）。
        if (!CanReveal(p.Point))
        {
            LastSearchNote = "这里现在还看不出来。";
            Say(LastSearchNote);
            return null;
        }

        LastSearchNote = "";

        // 结算顺序（rule_time_clock.md §边界）：先记刻，再落世界后果。
        // 重搜一样收费——不然"翻不完"的取舍就不成立（§3）。
        _clock.SpendSearch(p.Point.Cost);

        var list = SearchedList();
        var first = !list.Contains(p.Point.Id);
        if (first)
        {
            list.Add(p.Point.Id);
            ApplyFlag(p.Point.SetFlag);
            TickVillage();
        }

        var text = first ? p.Point.TextOnce : p.Point.TextRepeat;
        CheckKnock();
        StateChanged?.Invoke();
        return (text, first, p);
    }

    /// <summary>这一处搜过的东西的列表（按搜的先后）。</summary>
    private List<string> SearchedList()
    {
        if (!_searched.TryGetValue(Hub, out var list))
        {
            list = new List<string>();
            _searched[Hub] = list;
        }
        return list;
    }

    /// <summary>
    /// 这件东西现在能不能看。知识门看旗标，明面与细看永远能看。
    /// 旗标取值一律走 <see cref="FlagStore.Has"/>。
    /// </summary>
    private bool CanReveal(SearchPoint point)
    {
        if (!point.IsKnowledge) return true;
        return FlagExpression.Evaluate(point.Requires, _flags, Hub);
    }

    /// <summary>
    /// 搜查成功后置旗标。默认写全局（rule_flags_save.md §1.4「默认写全局」）。
    /// 空串 = 不置任何旗标（合法，不是错误）。
    /// </summary>
    private void ApplyFlag(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!_flags.Set(name))
            GD.PushWarning($"[旗标] 「{name}」写不进去（名字超长或仓库满了），这一条作废。");
    }

    /// <summary>
    /// 白幡村每搜满四个点，村里就少一个人。
    /// 这不是惩罚，是这一关的论题：你翻得越多，时间过得越快。
    /// </summary>
    private void TickVillage()
    {
        if (Hub != "hub3") return;

        var n = 0;
        foreach (var id in Searched)
            if (id.StartsWith("bf_", StringComparison.Ordinal)) n++;

        var should = n / 4;
        while (VillagerDeaths < should)
        {
            var idx = -1;
            for (var i = Npcs.Count - 1; i >= 0; i--)
                if (Npcs[i].Kind == "sick") { idx = i; break; }

            if (idx < 0) break;

            // 记下走的是谁，存档里按名字记（schema_v2.md §C 的 hubs.*.patients）
            var dying = Npcs[idx];
            _deadVillagers.Add(SickNameOf(dying));
            Npcs.RemoveAt(idx);
            VillagerDeaths++;
            Say("村那头有人没声了。", 2.8f);
        }
    }

    /// <summary>病人是谁。优先用台词表里的名字，没有就用坐标。</summary>
    private string SickNameOf(SceneConfig.Npc npc)
    {
        var talk = _talks.At(Hub, (int)npc.Cell.X, (int)npc.Cell.Y);
        if (talk != null && !string.IsNullOrEmpty(talk.Name)) return talk.Name;
        return $"({(int)npc.Cell.X},{(int)npc.Cell.Y})";
    }

    public void Say(string msg, float seconds = 2.2f)
    {
        LastMessage = msg;
        MessageTime = seconds;
    }

    // ————————————————————————— 存档 —————————————————————————

    /// <summary>
    /// 把这一局的运行时状态装成一份存档（schema_v2.md §C）。
    ///
    /// 这里只做"搬"，不做"算"：刻数从 Core 的 Clock 取，旗标从 Core 的 FlagStore 取，
    /// Game 层不自己算任何规则。
    /// </summary>
    /// <returns>可以落盘的存档。</returns>
    public SaveState ToSaveState()
    {
        var state = new SaveState
        {
            Version = SaveState.CurrentVersion,
            Seed = 0,
            Day = 1,
            Tense = "prologue",
            Hub = Hub,
        };

        state.Player.X = Player.Cell.X;
        state.Player.Y = Player.Cell.Y;

        foreach (var kv in _flags.SnapshotGlobal())
            state.Flags[kv.Key] = kv.Value;

        foreach (var (hub, clock) in _clocks)
        {
            var hubState = new HubState
            {
                Entered = true,
                KeHalf = clock.KeHalf,
                Nudges = clock.Nudges,
                CaughtCount = _caught.TryGetValue(hub, out var c) ? c : 0,
            };

            if (_searched.TryGetValue(hub, out var searched))
                hubState.Searched = new List<string>(searched);

            foreach (var kv in _flags.SnapshotLocal(hub))
                hubState.Flags[kv.Key] = kv.Value;

            state.Hubs[hub] = hubState;
        }

        // 村里死了的人：按名字记在 hubs.hub3.patients 里（值 alive / dying / dead）
        if (_deadVillagers.Count > 0)
        {
            var hubState = state.Hubs.TryGetValue("hub3", out var h3)
                ? h3
                : new HubState { Entered = true };

            foreach (var name in _deadVillagers)
                hubState.Patients[name] = "dead";

            state.Hubs["hub3"] = hubState;
        }

        // 说过话的人
        foreach (var key in Talked)
        {
            var sep = key.IndexOf(':');
            var hub = sep > 0 ? key[..sep] : Hub;
            var id = sep > 0 ? key[(sep + 1)..] : key;
            if (id.Length == 0) continue;

            var again = _talkCounts.TryGetValue(key, out var n) ? Math.Max(0, n - 1) : 0;

            state.Known.Npcs.Add(new NpcState
            {
                Hub = hub,
                Id = id,
                Met = true,
                Again = again,
            });
        }

        return state;
    }

    /// <summary>
    /// 用一份存档把这一局恢复回来。坏档不会走到这里（坏档在 SaveService 那一层就被拦下了）。
    /// </summary>
    /// <param name="state">读出来的存档。null 时什么都不做。</param>
    public void RestoreFrom(SaveState? state)
    {
        if (state == null) return;

        // 旗标：Core 的仓库自己负责全局 / 局部的优先级
        _flags.Load(state);

        // 每个空间的刻账、搜过的东西、被看见次数
        foreach (var (hub, hubState) in state.Hubs)
        {
            if (!ClockTable.IsKnownHub(hub)) continue;

            _clocks[hub] = Clock.FromState(hub, hubState?.ToClockState());
            _searched[hub] = hubState?.Searched != null
                ? new List<string>(hubState.Searched)
                : new List<string>();
            _caught[hub] = hubState?.CaughtCount ?? 0;
        }

        // 村里死了的人
        _deadVillagers.Clear();
        VillagerDeaths = 0;
        if (state.Hubs.TryGetValue("hub3", out var village) && village?.Patients != null)
            foreach (var (name, status) in village.Patients)
                if (status == "dead")
                {
                    _deadVillagers.Add(name);
                    VillagerDeaths++;
                }

        // 说过话的人
        Talked.Clear();
        _talkCounts.Clear();
        foreach (var npc in state.Known.Npcs)
        {
            if (npc == null || string.IsNullOrEmpty(npc.Hub) || string.IsNullOrEmpty(npc.Id)) continue;

            var key = $"{npc.Hub}:{npc.Id}";
            Talked.Add(key);
            _talkCounts[key] = Math.Max(1, npc.Again + 1);
        }

        // 回当前所在的那一关。刻账已经在上面装好了，Load 不会再重置它。
        var where = ClockTable.IsKnownHub(state.Hub) ? state.Hub : "hub1";
        Load(where);

        if (state.Player.X > 0 && state.Player.Y > 0)
            Player.Cell = new Vector2((float)state.Player.X, (float)state.Player.Y);
    }

    // ————————————————————————— 巡逻 —————————————————————————

    private void BuildPatrol(string hub)
    {
        if (hub == "hub3")
        {
            Guards.Add(new Patroller
            {
                Path = new List<Vector2>
                {
                    new(16, 19), new(32, 19), new(32, 6), new(2, 6), new(2, 19),
                },
                Cell = new Vector2(16, 19),
                Name = "衙役甲",
                Index = 1,          // 直接朝第一个路点看，否则他一开始会瞪着栅栏
                Speed = 2.2f,
                WaitEach = 1.4f,
                PingPong = false,
                ViewRange = 6.0f,
                ViewAngle = 120f,
            });
            Guards.Add(new Patroller
            {
                Path = new List<Vector2> { new(17, 13), new(17, 18) },
                Cell = new Vector2(17, 18),
                Name = "衙役乙",
                Speed = 1.6f,
                WaitEach = 3.0f,
                PingPong = true,
                Dir = -1,
                ViewRange = 4.5f,
                ViewAngle = 100f,
            });
        }
        else if (hub == "hub4")
        {
            Guards.Add(new Patroller
            {
                Path = new List<Vector2>
                {
                    new(2, 11), new(33, 11), new(33, 12), new(2, 12),
                },
                Cell = new Vector2(2, 11),
                Index = 1,
                Speed = 2.6f,
                WaitEach = 0.6f,
                PingPong = false,
                ViewRange = 5.0f,
                ViewAngle = 110f,
            });
        }
    }
}
