using System;
using System.Collections.Generic;
using Godot;

namespace Juhuo.Art;

public sealed class BakedMap
{
    public ImageTexture Texture = default!;
    public PixelCanvas Canvas = default!;

    /// <summary>地图左上角在视口里的位置。</summary>
    public Vector2 Origin;

    /// <summary>地图的整数倍放大倍率。小场景（18×10）放大 2×，大场景（34×22）保持 1×。</summary>
    public int Scale = 1;

    public int ViewW;
    public int ViewH;
    public int MapW;
    public int MapH;

    /// <summary>格子坐标（1-based）→ 视口像素坐标（格子中心）。</summary>
    public Vector2 CellToScreen(Vector2 cell)
    {
        return Origin + new Vector2(
            (cell.X - 1 + 0.5f) * TileArt.T * Scale,
            (cell.Y - 1 + 0.5f) * TileArt.T * Scale);
    }
}

/// <summary>
/// 把地形、物件、光照一次性烘焙成一张整图。
///
/// 两段式：先在地图自身尺寸的画布上把东西画完，再整数倍放大贴进视口。
/// 这样小场景（城南小院 18×10）不会缩在屏幕中间一小块，
/// 而像素仍然是整格的，不会糊。
/// </summary>
public static class MapBaker
{
    private const int T = TileArt.T;

    /// <summary>视口底部只留一条提示的位置，其余全给地图。</summary>
    private const int UiBand = 18;

    private static readonly (int X, int Y, int Kind)[] Hub3Props =
    {
        (8, 15, 0), (11, 16, 1), (22, 14, 2), (26, 15, 0),
        (30, 16, 1), (6, 13, 1), (12, 13, 1), (29, 13, 1),
    };

    public static BakedMap Bake(TerrainMap map, string hub, int viewW, int viewH, int originY,
        IReadOnlyList<Interactable> props)
    {
        // 室内外要用"这个场景的起点"重新判一遍：
        // 从起点灌不到的地方才叫室内。否则小院里 y=7 那两间厢房会被当成院子。
        var scene = SceneConfig.Of(hub);
        map.MarkOutdoorsFrom(
            Mathf.RoundToInt(scene.Spawn.X),
            Mathf.RoundToInt(scene.Spawn.Y));

        var mapW = map.W * T;
        var mapH = map.H * T;

        // 整数倍放大：取宽高都能放下的最大值，限制在 1~3。
        // 必须用浮点再取整——整除会把 1.95 截成 1，营地和院子就永远放不大。
        var fit = Mathf.Min(
            viewW / (float)mapW,
            (viewH - originY - UiBand) / (float)mapH);
        var scale = Mathf.Clamp(Mathf.FloorToInt(fit), 1, 3);

        var drawnW = mapW * scale;
        var drawnH = mapH * scale;
        var ox = (viewW - drawnW) / 2;

        // 竖直方向居中：小场景（18×10）只放大到 2×，如果顶格放，
        // 底下会剩一大条空森林。居中之后上下留白对称，像一幅画。
        var slack = viewH - originY - UiBand - drawnH;
        var oy = originY + Mathf.Max(0, slack / 2);

        // ———— 第一段：在地图自身尺寸的画布上画 ————
        var layer = new PixelCanvas(mapW, mapH);
        DrawLayer(layer, map, hub, props);

        // ———— 第二段：铺密林，把地图放大贴上去 ————
        var cv = new PixelCanvas(viewW, viewH);
        TileArt.Forest(cv);

        for (var y = 0; y < drawnH; y++)
        {
            var sy = y / scale;
            if (sy >= mapH) break;
            for (var x = 0; x < drawnW; x++)
            {
                var sx = x / scale;
                if (sx >= mapW) break;
                cv.Px(ox + x, oy + y, layer.Get(sx, sy));
            }
        }

        Grade(cv, ox, oy, drawnW, drawnH);

        return new BakedMap
        {
            Texture = cv.ToTexture(),
            Canvas = cv,
            Origin = new Vector2(ox, oy),
            Scale = scale,
            ViewW = viewW,
            ViewH = viewH,
            MapW = drawnW,
            MapH = drawnH,
        };
    }

    // ————————————————————————— 地图层 —————————————————————————

    private static void DrawLayer(PixelCanvas cv, TerrainMap map, string hub, IReadOnlyList<Interactable> props)
    {
        // 1. 地面
        // 0 = 草（村子/野外），1 = 夯土（县城），2 = 雪（城下营地）
        var style = hub switch
        {
            "hub4" => 1,
            "hub7" => 2,
            _ => 0,
        };
        TileArt.PaintGround(cv, map, 0, 0, style);

        // 2. 地形自带的物件，按底部 Y 排序
        var items = new List<(int sortY, int order, Action act)>();
        var order = 0;

        for (var gy = 0; gy < map.H; gy++)
        for (var gx = 0; gx < map.W; gx++)
        {
            var g = map.Ground(gx, gy);
            var px = gx * T;
            var py = gy * T;

            switch (g)
            {
                case 'T':
                {
                    var seed = 1000 + gx * 7 + gy * 13;
                    var ggx = gx;
                    var ggy = gy;
                    items.Add((py + T, order++, () => PropArt.Tree(cv, ggx, ggy, px, py, seed)));
                    break;
                }
                case '^':
                {
                    var seed = 2000 + gx * 11 + gy * 5;
                    items.Add((py + T, order++, () => PropArt.Grave(cv, gx, gy, px, py, seed)));
                    break;
                }
                case '=':
                    items.Add((py + T, order++, () => PropArt.Fence(cv, map, gx, gy, px, py)));
                    break;
                case '~':
                    items.Add((py + T, order++, () => PropArt.Well(cv, gx, gy, px, py)));
                    break;
                case '#':
                    if (map.IsBorder(gx, gy)) break;
                    if (map.IsIndoor(gx, gy + 1))
                        items.Add((py + T, order++, () => PropArt.RoofStrip(cv, map, gx, gy, px, py)));
                    else
                        items.Add((py + T, order++, () => PropArt.Wall(cv, map, gx, gy, px, py)));
                    break;
            }
        }

        // 3. 地图上标注的人
        foreach (var (mx, my, mc) in map.Markers())
        {
            var px = mx * T;
            var py = my * T;

            // NPC 现在由 MapView 在动态层画（他们能走、会动，烘焙进去就动不了了）
            if (mc == '>')
                items.Add((py + T, order++, () => PropArt.Exit(cv, px, py)));
            else if (mc == 'X')
            {
                var seed = 7000 + mx * 13 + my * 29;
                items.Add((py + T, order++, () => PropPainters.Paint(cv, PropKind.Fire, px, py, seed)));
            }
        }

        // 4. 自动撒的碎东西：只填空地，不抢戏
        var occupied = new HashSet<(int, int)>();
        foreach (var (mx, my, _) in map.Markers()) occupied.Add((mx, my));
        if (hub == "hub3")
            foreach (var (hx, hy, _) in Hub3Props) occupied.Add((hx, hy));

        for (var gy = 1; gy < map.H - 1; gy++)
        for (var gx = 1; gx < map.W - 1; gx++)
        {
            if (map.Ground(gx, gy) != ',') continue;
            if (occupied.Contains((gx, gy))) continue;

            var clear = true;
            for (var dy = -1; dy <= 1 && clear; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (map.Ground(gx + dx, gy + dy) != ',') { clear = false; break; }
            }

            if (!clear) continue;

            var r = PixelCanvas.Rand(gx, gy, 1500);
            if (r > 0.085f) continue;

            var px = gx * T;
            var py = gy * T;
            var seed = 3000 + gx * 31 + gy * 17;

            if (r < 0.042f)
                items.Add((py + T, order++, () => PropArt.Shrub(cv, gx, gy, px, py, seed)));
            else if (r < 0.070f)
                items.Add((py + T, order++, () => PropArt.RockPile(cv, gx, gy, px, py, seed)));
            else
                items.Add((py + T, order++, () => PropArt.Log(cv, gx, gy, px, py, seed)));
        }

        // 5. 手摆装饰
        if (hub == "hub3")
        {
            foreach (var (hx, hy, kind) in Hub3Props)
            {
                var px = (hx - 1) * T;
                var py = (hy - 1) * T;
                var k = kind;
                items.Add((py + T, order++, () => PropArt.Debris(cv, k, px, py)));
            }
        }

        // 6. 可搜查的实物。地图本身已画了坟/井/篱笆/树的格子不再重复画，
        //    但交互照样保留——玩家看到的是一座坟，不是"坟上面又叠了一个坟"。
        foreach (var prop in props)
        {
            var cx = Mathf.RoundToInt(prop.Cell.X);
            var cy = Mathf.RoundToInt(prop.Cell.Y);
            if (cx < 1 || cy < 1 || cx > map.W || cy > map.H) continue;

            var ground = map.Ground(cx - 1, cy - 1);
            if (ground is '^' or '~' or '=' or 'T') continue;

            var px = (cx - 1) * T;
            var py = (cy - 1) * T;
            var kind = prop.Kind;
            var indoor = map.IsIndoor(cx - 1, cy - 1);
            var seed = 5000 + cx * 37 + cy * 61;

            items.Add((py + T, order++, () =>
            {
                PropPainters.Paint(cv, kind, px, py, seed);
                if (indoor) cv.BlendRect(px, py, T, T, Pal.ShadowDeep, 0.18f);
            }));
        }

        // 7. 村口的丧幡
        var fenceXs = new List<int>();
        for (var gy = 0; gy < map.H; gy++)
        for (var gx = 0; gx < map.W; gx++)
            if (map.Ground(gx, gy) == '=' && (map.Ground(gx - 1, gy) != '=' || map.Ground(gx + 1, gy) != '='))
                fenceXs.Add(gx * 1000 + gy);

        foreach (var packed in fenceXs)
        {
            var fx = packed / 1000;
            var fy = packed % 1000;
            var px = fx * T;
            var py = fy * T;
            var seed = fx * 3;
            items.Add((py + T, order++, () => PropArt.Banner(cv, px, py, seed)));
        }

        // 8. 统一 Y 排序后执行
        items.Sort((a, b) => a.sortY != b.sortY ? a.sortY.CompareTo(b.sortY) : a.order.CompareTo(b.order));
        foreach (var (_, _, act) in items) act();
    }

    // ————————————————————————— 光照 —————————————————————————

    /// <summary>
    /// 统一色温：整体偏暖，暗部压向冷青，四周加暗角。
    /// 星露谷的"舒服"基本来自这一步——暖高光 + 冷阴影，而不是加饱和度。
    /// </summary>
    private static void Grade(PixelCanvas cv, int ox, int oy, int mapW, int mapH)
    {
        var cx = cv.W * 0.5f;
        var cy = cv.H * 0.5f;
        var maxD = Mathf.Sqrt(cx * cx + cy * cy);

        for (var y = 0; y < cv.H; y++)
        for (var x = 0; x < cv.W; x++)
        {
            var c = cv.Get(x, y);
            if (c.A <= 0f) continue;

            var lum = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

            var wr = 1.070f;
            var wg = 1.005f;
            var wb = 0.895f;

            // 暗部压冷
            var t = Mathf.Clamp(1f - lum / 0.36f, 0f, 1f);
            wr -= t * 0.050f;
            wg += t * 0.020f;
            wb += t * 0.115f;

            // 高光回冷：不然雪地会被暖调染成土黄
            var hot = Mathf.Clamp((lum - 0.52f) / 0.40f, 0f, 1f);
            wr -= hot * 0.070f;
            wb += hot * 0.060f;

            var dx = (x - cx) / maxD;
            var dy = (y - cy) / maxD;
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            var vig = 1f - 0.26f * d * d * 1.55f;

            var outside = x < ox - 2 || y < oy - 2 || x > ox + mapW + 2 || y > oy + mapH + 2;
            var fog = outside ? 0.96f : 1f;

            cv.Mul(x, y, wr * vig * fog, wg * vig * fog, wb * vig * fog);
        }

        // 地图边缘的雾化：把地图和密林"焊"在一起
        var band = Mathf.Min(10, Mathf.Min(mapW, mapH) / 4);
        for (var i = 0; i < band; i++)
        {
            var a = (1f - i / (float)band) * 0.12f;
            for (var x = ox; x < ox + mapW; x++)
            {
                cv.Blend(x, oy + i, Pal.ShadowDeep, a);
                cv.Blend(x, oy + mapH - 1 - i, Pal.ShadowDeep, a);
            }
            for (var y = oy; y < oy + mapH; y++)
            {
                cv.Blend(ox + i, y, Pal.ShadowDeep, a);
                cv.Blend(ox + mapW - 1 - i, y, Pal.ShadowDeep, a);
            }
        }
    }
}
