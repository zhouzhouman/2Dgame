using System;
using System.Collections.Generic;
using Godot;
using Juhuo.Art;

namespace Juhuo;

/// <summary>
/// 地图视图：烘焙图当背景，玩家、巡逻、高亮全部画在上面的动态层。
///
/// 交互规则（对应设计文档"三层可交互物"）：
///   · 平时：靠近实物才出现提示，远处没有任何标记；
///   · 按住 Ctrl：把当前场景所有可交互物一次性框出来；
///   · 知识门不发光——它本来就该藏在墙里，等玩家自己认出来。
/// </summary>
public partial class MapView : Node2D
{
    private const int T = TileArt.T;
    private const float WalkSpeed = 4.2f;

    private World _world = default!;
    private BakedMap _baked = default!;

    private float _time;
    private bool _highlight;
    public bool ForceHighlight { get; set; }
    private Interactable? _near;
    private Vector2 _lastPlayerCell = new(-99, -99);
    private bool _searchHeld;
    private bool _atExit;
    private bool _boxOpen;
    private float _exitArmedAt = -99f;

    /// <summary>UI 挡在前面（对话框 / 操作说明）时，不许走动。</summary>
    public bool UiBlocking { get; set; }

    /// <summary>当前伸手够得着的东西，给 HUD 拼提示用。</summary>
    public Interactable? Near => _near;

    public event Action<string, string>? TextShown;
    public event Action? TextClosed;
    public event Action<string>? PromptChanged;
    public event Action<Interactable?>? NearChanged;
    public event Action? Exited;

    public void Setup(World world, BakedMap baked)
    {
        _world = world;
        _baked = baked;
        QueueRedraw();
    }

    /// <summary>换 hub 后重新挂一张烘焙图，玩家位置缓存要清掉。</summary>
    public void Rebind(BakedMap baked)
    {
        _baked = baked;
        _lastPlayerCell = new Vector2(-99, -99);
        QueueRedraw();
    }

    // ————————————————————————— 每帧 —————————————————————————

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;

        _world.Update(dt);
        HandleMovement(dt);

        var near = _world.Nearest();
        if (!ReferenceEquals(near, _near))
        {
            _near = near;
            NearChanged?.Invoke(near);
        }

        CheckExit();

        // 搜查键用边沿触发：按住不放不会连续搜，也躲开引擎启动时可能漏进来的按键事件
        var held = Input.IsKeyPressed(Key.E) || Input.IsKeyPressed(Key.Space);
        if (held && !_searchHeld) DoSearch();
        _searchHeld = held;

        QueueRedraw();
    }

    private void HandleMovement(float dt)
    {
        // 读字的时候人物不该还能走。误触一根手指，人就飘到别处去了。
        if (UiBlocking || _boxOpen)
        {
            _world.Player.Moving = false;
            _highlight = ForceHighlight || Input.IsKeyPressed(Key.Ctrl);
            return;
        }

        float dx = 0, dy = 0;

        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dx -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dx += 1f;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dy -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dy += 1f;

        if (dx != 0f && dy != 0f)
        {
            dx *= 0.7071f;
            dy *= 0.7071f;
        }

        if (dx != 0f || dy != 0f)
            _world.Move(dx * WalkSpeed * dt, dy * WalkSpeed * dt);
        else
            _world.Player.Moving = false;

        _highlight = ForceHighlight || Input.IsKeyPressed(Key.Ctrl);
    }

    /// <summary>
    /// 走到出口只提示，不自动切场景。
    /// 踩上去就传送会把玩家从"还在看这个院子"的状态里硬拽出来。
    ///
    /// 判定用半径而不是"正好站在出口那一格"：出口多半贴在地图边上，
    /// 要求踩准一格，等于让玩家拿像素去对位。
    /// </summary>
    private bool AtExit()
    {
        var exit = SceneConfig.Of(_world.Hub).Exit;
        return exit.X > 0 && (_world.Player.Cell - exit).Length() <= 1.15f;
    }

    private void CheckExit()
    {
        var here = AtExit();

        if (here == _atExit) return;

        _atExit = here;
        _exitArmedAt = -99f;             // 离开一次，确认就作废

        if (!_atExit) return;

        var next = _world.NextHub;
        var left = _world.UnsearchedHere;

        var where = next is null ? "E 离开这里" : $"E 离开 · 去{World.TitleOf(next)}";
        PromptChanged?.Invoke(left > 0 ? $"{where}　（还有 {left} 处没看）" : where);
    }

    // ————————————————————————— 输入 —————————————————————————


    /// <summary>开发/测试用：等价于按一次 E。</summary>
    public void PressInteract() => DoSearch();

    /// <summary>对话框的开合由 Main 管，这里只需要知道"现在别让玩家走"。</summary>
    public void SetBoxOpen(bool open) => _boxOpen = open;

    /// <summary>等价于按 Esc：关掉对话框。</summary>
    public void CloseText()
    {
        _boxOpen = false;
        TextClosed?.Invoke();
    }

    private void DoSearch()
    {
        // 对话开着的时候，E 是"看完了"，不是"再翻一次"。
        // 这条最容易误操作：玩家读完顺手按 E，结果又搜了一遍同一件东西。
        if (_boxOpen)
        {
            CloseText();
            return;
        }

        // 用实时判定，不读 _atExit 缓存：缓存是给底部提示用的，
        // 而且它只在 _Process 里更新，测试钩子调进来时会是一帧前的旧值。
        if (AtExit())
        {
            var left = _world.UnsearchedHere;

            // 还有没看过的地方：第一次按只提醒，第二次才真的走。
            // 出口就在脚底下，误触一次 E 就回不去这一关了。
            if (left > 0 && _time - _exitArmedAt > 3f)
            {
                _exitArmedAt = _time;
                PromptChanged?.Invoke($"还有 {left} 处没看　·　再按一次 E 就走");
                return;
            }

            Exited?.Invoke();
            return;
        }

        // 人优先：走到一个人跟前，先说话，再翻东西
        var talk = _world.Talk();
        if (talk is not null)
        {
            TextShown?.Invoke(talk.Value.Head, talk.Value.Body);
            return;
        }

        var result = _world.Search();
        if (result is null)
        {
            _world.Say("这里没什么可翻的。");
            PromptChanged?.Invoke(_world.LastMessage ?? "");
            return;
        }

        var (text, first, prop) = result.Value;
        var head = $"{prop.Point.Name}　　{LayerLabel(prop.Point.Layer)}";
        if (!string.IsNullOrEmpty(prop.Point.Loot)) head += $"　　+ {prop.Point.Loot}";

        TextShown?.Invoke(head, text);

        if (first && _world.KeLeft == 0)
            _world.Say("天快黑了。剩下的地方，你今天看不完了。");
    }

    private static string LayerLabel(string layer) => layer switch
    {
        "close" => "细看",
        "knowledge" => "？",
        _ => "明面",
    };

    // ————————————————————————— 绘制 —————————————————————————

    public override void _Draw()
    {
        if (_baked is null) return;

        // 烘焙图本身就是整个视口大小（含四周密林），必须画在原点；
        // Origin 只是"地图内容在这张图里的位置"，用于坐标换算，不是绘制位置。
        DrawTexture(_baked.Texture, Vector2.Zero);

        // 已搜过的地方留个灰点，提醒"这里我来过"
        foreach (var p in _world.Props)
        {
            if (!_world.Searched.Contains(p.Point.Id)) continue;
            var c = Screen(p.Cell, 0f, 6f);
            var d = _baked.Scale;
            DrawRect(new Rect2(c.X - d, c.Y - d, d * 2, d * 2), new Color(0.20f, 0.20f, 0.18f, 0.60f));
        }

        DrawViewCones();

        if (_highlight) DrawHighlights();

        DrawExit();

        foreach (var npc in _world.Npcs) DrawNpc(npc);
        foreach (var g in _world.Guards) DrawGuard(g);

        DrawPlayer();
        DrawExitArrow();

        // 没按 Ctrl 时，只有伸手能碰到的那一件会亮框
        if (_near is not null && !_highlight)
        {
            var c = Screen(_near.Cell);
            var half = T * _baked.Scale / 2f;
            var box = new Rect2(c.X - half, c.Y - half, T * _baked.Scale, T * _baked.Scale);
            Bracket(box, new Color(0.98f, 0.88f, 0.55f, 0.60f), 4f * _baked.Scale);
        }

        DrawSpotFlash();
    }

    /// <summary>
    /// 衙役的视野扇形。每一条射线单独测距，遇到墙就截断——
    /// 这样玩家能一眼读出"墙后面是安全的"，而不是靠猜。
    /// </summary>
    private void DrawViewCones()
    {
        if (_world.Guards.Count == 0) return;

        var baseAlpha = _highlight ? 0.26f : 0.13f;
        const int seg = 18;
        const int raySteps = 16;

        foreach (var g in _world.Guards)
        {
            var c = Screen(g.Cell, 0f, 3f);
            var maxR = g.ViewRange * T * _baked.Scale;
            var half = g.ViewAngle * 0.5f * (Mathf.Pi / 180f);
            var baseAng = Mathf.Atan2(g.Facing.Y, g.Facing.X);

            var col = new Color(1.00f, 0.72f + g.Alert * 0.16f, 0.34f,
                baseAlpha + g.Alert * 0.18f);

            var pts = new List<Vector2>(seg + 2) { c };
            Vector2? prev = null;

            for (var i = 0; i <= seg; i++)
            {
                var a = baseAng - half + 2f * half * i / seg;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                var r = maxR;
                for (var s = 1; s <= raySteps; s++)
                {
                    var rr = maxR * s / raySteps;
                    if (ScreenWalkable(c + dir * rr))
                    {
                        r = rr;
                        continue;
                    }
                    r = maxR * (s - 1) / raySteps;
                    break;
                }

                // 贴着墙时半径会趋近 0，顶点全部重合会让三角化失败
                var p = c + dir * Mathf.Max(r, 1.5f);
                if (prev.HasValue && (p - prev.Value).LengthSquared() < 1f) continue;

                pts.Add(p);
                prev = p;
            }

            if (pts.Count < 3) continue;

            var poly = pts.ToArray();
            DrawColoredPolygon(poly, col);

            // 描一圈边，扇形边界才清楚（尤其在草地上）
            var edge = new Vector2[poly.Length + 1];
            poly.CopyTo(edge, 0);
            edge[^1] = poly[0];
            DrawPolyline(edge, new Color(col.R, col.G, col.B, col.A * 1.3f), 1f);
        }
    }

    private bool ScreenWalkable(Vector2 p)
    {
        var s = T * _baked.Scale;
        var gx = Mathf.RoundToInt((p.X - _baked.Origin.X) / s - 0.5f) + 1;
        var gy = Mathf.RoundToInt((p.Y - _baked.Origin.Y) / s - 0.5f) + 1;
        return _world.CellWalkable(gx, gy);
    }

    /// <summary>
    /// 出口：一块木牌 + 一枚向外指的箭头，离得远时屏幕边上还有个指标。
    ///
    /// 出口以前只是 SceneConfig 里的一对坐标，画面上什么都不画。
    /// 玩家把整个院子翻完，也不知道"往哪走出门"——这一关就死了。
    /// 场景里凡是能离开的地方，都必须长成同一个样子，玩家才学得会。
    /// </summary>
    /// <summary>出图方向：从地图中心指向出口。出口都在边上，取主轴就够了。</summary>
    private Vector2 ExitDir(Vector2 exit)
    {
        var dx = exit.X - (_world.Map.W + 1) / 2f;
        var dy = exit.Y - (_world.Map.H + 1) / 2f;

        return Mathf.Abs(dx) >= Mathf.Abs(dy)
            ? new Vector2(Mathf.Sign(dx), 0f)
            : new Vector2(0f, Mathf.Sign(dy));
    }

    /// <summary>地面那一层：暖光 + 黄框。和可搜物同一套视觉语言。</summary>
    private void DrawExit()
    {
        var exit = SceneConfig.Of(_world.Hub).Exit;
        if (exit.X < 0 || exit.Y < 0) return;

        var s = _baked.Scale;
        var c = Screen(exit);
        var half = T * s / 2f;
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 2.2f);

        DrawCircle(c, half * 1.15f, new Color(0.99f, 0.86f, 0.48f, 0.07f + 0.04f * pulse));
        DrawCircle(c, half * 0.75f, new Color(0.99f, 0.86f, 0.48f, 0.06f + 0.04f * pulse));

        var box = new Rect2(c.X - half, c.Y - half, T * s, T * s);
        Bracket(box, new Color(0.99f, 0.88f, 0.55f, 0.40f + 0.40f * pulse), 5f * s);
    }

    /// <summary>
    /// 箭头画在贴着地图外面那一侧，而且画在人物之后。
    /// 画在格子里会被玩家自己的脑袋挡住——站在出口时反而看不见出口。
    /// </summary>
    private void DrawExitArrow()
    {
        var exit = SceneConfig.Of(_world.Hub).Exit;
        if (exit.X < 0 || exit.Y < 0) return;

        var s = _baked.Scale;
        var c = Screen(exit);
        var half = T * s / 2f;
        var dir = ExitDir(exit);

        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 2.2f);
        var bob = Mathf.Sin(_time * 2.2f) * 1.5f * s;

        var tip = c + dir * (half + 8f * s) + new Vector2(0f, bob);
        tip = new Vector2(
            Mathf.Clamp(tip.X, 8f, _baked.ViewW - 8f),
            Mathf.Clamp(tip.Y, 8f, _baked.ViewH - 8f));

        Arrow(tip, dir, new Color(0.99f, 0.90f, 0.58f, 0.60f + 0.32f * pulse), s);
    }

    /// <summary>两笔画的箭头，带暗边。点阵箭头在 2× 下根本看不清。</summary>
    private void Arrow(Vector2 tip, Vector2 dir, Color col, int s)
    {
        var perp = new Vector2(-dir.Y, dir.X);
        var back = tip - dir * 5.5f * s;
        var w = Mathf.Max(2f, 1.4f * s);

        var ink = new Color(0.06f, 0.05f, 0.04f, Mathf.Min(1f, col.A * 0.9f));

        foreach (var side in new[] { 1f, -1f })
        {
            var end = back + perp * 5f * s * side;
            DrawLine(tip, end, ink, w + 2.5f);
        }
        foreach (var side in new[] { 1f, -1f })
        {
            var end = back + perp * 5f * s * side;
            DrawLine(tip, end, col, w);
        }
    }

    /// <summary>被发现时四边泛红。不用弹窗，弹窗会把玩家从场景里拽出来。</summary>
    private void DrawSpotFlash()
    {
        if (_world.SpotFlash <= 0f) return;

        var a = Mathf.Clamp(_world.SpotFlash, 0f, 1f) * 0.32f;
        var w = _baked.ViewW;
        var h = _baked.ViewH;
        var band = 30f;
        var col = new Color(0.72f, 0.13f, 0.09f, a);

        DrawRect(new Rect2(0, 0, w, band), col);
        DrawRect(new Rect2(0, h - band, w, band), col);
        DrawRect(new Rect2(0, 0, band, h), col);
        DrawRect(new Rect2(w - band, 0, band, h), col);
    }

    private void DrawHighlights()
    {
        var pulse = 0.62f + 0.38f * Mathf.Sin(_time * 3.0f);

        // 能说话的人：青色框，和物件的黄/蓝/紫分开
        foreach (var n in _world.TalksHere())
        {
            var done = _world.Talked.Contains($"{_world.Hub}:{n.X},{n.Y}");
            var c = Screen(new Vector2(n.X, n.Y));
            var half = T * _baked.Scale / 2f;
            var r = new Rect2(c.X - half, c.Y - half, T * _baked.Scale, T * _baked.Scale);
            var col = new Color(0.55f, 0.90f, 0.86f, (done ? 0.34f : 0.85f) * pulse);
            Bracket(r, col, 3f * _baked.Scale);
        }

        // 巡逻的衙役也能说话，同一个青色框——他们位置在变，所以每帧重算
        foreach (var g in _world.Guards)
        {
            var done = _world.Talked.Contains($"{_world.Hub}:guard:{g.Name}");
            var c = Screen(g.Cell);
            var half = T * _baked.Scale / 2f;
            var r = new Rect2(c.X - half, c.Y - half, T * _baked.Scale, T * _baked.Scale);
            var col = new Color(0.55f, 0.90f, 0.86f, (done ? 0.30f : 0.75f) * pulse);
            Bracket(r, col, 3f * _baked.Scale);
        }

        foreach (var p in _world.Props)
        {
            var done = _world.Searched.Contains(p.Point.Id);
            var c = Screen(p.Cell);
            var half = T * _baked.Scale / 2f;
            var r = new Rect2(c.X - half, c.Y - half, T * _baked.Scale, T * _baked.Scale);

            var col = p.Point.Layer switch
            {
                "knowledge" => new Color(0.70f, 0.52f, 0.94f),
                "close" => new Color(0.48f, 0.76f, 0.96f),
                _ => new Color(0.98f, 0.84f, 0.44f),
            };

            if (done) col = col.Darkened(0.55f);

            DrawRect(r, new Color(col, 0.09f * pulse));
            Bracket(r, new Color(col, (done ? 0.42f : 0.95f) * pulse), 4f);
        }
    }

    private static readonly Dictionary<string, (int Coat, int Dark)> NpcColors = new()
    {
        ["chen"] = (0x6A6250, 0x474234),
        ["old"] = (0x4C565E, 0x333B42),
        ["doctor"] = (0x6E6440, 0x4A422A),
        ["yamen"] = (0x1C1A1A, 0x111010),
        ["sick"] = (0x5E5A50, 0x403C34),
        ["villager"] = (0x5A5244, 0x3C362C),
    };

    private void DrawNpc(SceneConfig.Npc npc)
    {
        Blit(npc.Kind, Screen(npc.Cell, 0f, 8f), 0, 0);
    }

    private void DrawGuard(Patroller g)
    {
        var frame = g.Moving ? (int)(_time * 8f) % 4 : 0;
        Blit("yamen", Screen(g.Cell, 0f, 8f), DirOf(g.Facing), frame);
    }

    private void DrawPlayer()
    {
        var frame = _world.Player.Moving ? (int)(_world.Player.Anim * 1.2f) % 4 : 0;
        var foot = Screen(_world.Player.Cell, 0f, 8f);

        // 脚下一圈很淡的光：地图上人多、物件多，不给个记号容易找不着自己
        var glow = 0.09f + 0.04f * Mathf.Sin(_time * 1.8f);
        DrawCircle(new Vector2(foot.X, foot.Y - 2 * _baked.Scale), 9f * _baked.Scale,
            new Color(0.96f, 0.86f, 0.58f, glow));

        Blit("player", foot, _world.Player.Dir, frame);
    }

    /// <summary>把图集里的一帧贴到脚下。所有角色都走这条路。</summary>
    private void Blit(string kind, Vector2 foot, int dir, int frame)
    {
        var s = _baked.Scale;
        var tex = ActorArt.Sheet(kind);

        var dest = new Rect2(
            Mathf.Round(foot.X - ActorArt.CellW * s / 2f),
            Mathf.Round(foot.Y - ActorArt.CellH * s),
            ActorArt.CellW * s,
            ActorArt.CellH * s);

        DrawTextureRectRegion(tex, dest, ActorArt.Src(dir, frame));
    }

    /// <summary>朝向向量 → 四向索引。</summary>
    private static int DirOf(Vector2 f)
    {
        if (Mathf.Abs(f.X) > Mathf.Abs(f.Y)) return f.X > 0 ? 2 : 1;
        return f.Y > 0 ? 0 : 3;
    }

    /// <summary>10×18 的小人。所有角色共用一套骨架，只换三个颜色。s 是像素放大倍率。</summary>
    private void Person(Vector2 foot, int coat, int coatDark, int trim, float step, int facing, int s)
    {
        var c1 = Rgb(coat);
        var c2 = Rgb(coatDark);
        var c3 = Rgb(trim);
        var skin = new Color(0.53f, 0.38f, 0.25f);
        var skinLit = new Color(0.61f, 0.45f, 0.31f);
        var hair = new Color(0.08f, 0.06f, 0.05f);

        var x = Mathf.RoundToInt(foot.X);
        var y = Mathf.RoundToInt(foot.Y);

        void R(float ox, float oy, float w, float h, Color c)
        {
            DrawRect(new Rect2(x + ox * s, y + oy * s,
                Mathf.Max(1f, w * s), Mathf.Max(1f, h * s)), c);
        }

        // 腿：走路时一前一后
        R(-3, -5 + step, 2, 5, c2);
        R(1, -5 + (1f - step), 2, 5, c2.Darkened(0.18f));

        // 身体
        R(-4, -12, 8, 7, c1);
        R(-4, -12, 8, 1, c1.Lightened(0.14f));
        R(3, -12, 1, 7, c2);

        // 腰带
        R(-4, -7, 8, 1, c3);

        // 交领
        R(-1, -12, 1, 1, c2);
        R(0, -11, 1, 1, c2);

        // 头
        R(-3, -18, 6, 6, skin);
        R(-3, -18, 6, 2, hair);
        R(-3, -16, 1, 3, hair);
        R(2, -16, 1, 3, hair);
        R(-2, -16, 4, 1, skinLit);

        // 眼
        var eo = facing >= 0 ? 0 : -1;
        R(-1 + eo, -15, 1, 1, hair);
        R(1 + eo, -15, 1, 1, hair);
    }

    private static Color Rgb(int v) => new(
        (v >> 16 & 0xFF) / 255f,
        (v >> 8 & 0xFF) / 255f,
        (v & 0xFF) / 255f);

    // ————————————————————————— 工具 —————————————————————————

    /// <summary>格子坐标 → 屏幕坐标（格子中心，加偏移）。缩放由烘焙层统一处理。</summary>
    private Vector2 Screen(Vector2 cell, float ox = 0f, float oy = 0f)
    {
        var s = _baked.Scale;
        return _baked.CellToScreen(cell) + new Vector2(ox * s, oy * s);
    }

    private void Bracket(Rect2 r, Color c, float len)
    {
        var p = r.Position;
        var s = r.Size;

        DrawRect(new Rect2(p.X, p.Y, len, 1), c);
        DrawRect(new Rect2(p.X, p.Y, 1, len), c);
        DrawRect(new Rect2(p.X + s.X - len, p.Y, len, 1), c);
        DrawRect(new Rect2(p.X + s.X - 1, p.Y, 1, len), c);
        DrawRect(new Rect2(p.X, p.Y + s.Y - 1, len, 1), c);
        DrawRect(new Rect2(p.X, p.Y + s.Y - len, 1, len), c);
        DrawRect(new Rect2(p.X + s.X - len, p.Y + s.Y - 1, len, 1), c);
        DrawRect(new Rect2(p.X + s.X - 1, p.Y + s.Y - len, 1, len), c);
    }
}
