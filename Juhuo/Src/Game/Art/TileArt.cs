using System;
using Godot;

namespace Juhuo.Art;

/// <summary>
/// 地面层绘制。每种地形都按"低频噪声出大块 + 高频噪声出颗粒"的方式上色，
/// 再补少量手绘细节（草叶、石子、车辙、犁沟）。
/// 这是让 16px 地块不再像色块的关键。
/// </summary>
public static class TileArt
{
    public const int T = 16;

    // ————————————————————————— 密林（背景） —————————————————————————

    /// <summary>
    /// 整张视口先铺密林。地图最外圈也是墙，画成树冠就和背景连成一片，
    /// 视觉上变成"村子被林子围住"，而不是一张地图贴在地上。
    /// </summary>
    public static void Forest(PixelCanvas cv, int seed = 4021)
    {
        for (var y = 0; y < cv.H; y++)
        for (var x = 0; x < cv.W; x++)
        {
            var n = PixelCanvas.Fbm(x / 12f, y / 12f, seed, 3);
            var f = PixelCanvas.Rand(x, y, seed + 3);
            var v = n * 0.72f + f * 0.28f;

            var c = v < 0.16f ? Pal.L0
                : v < 0.40f ? Pal.L1
                : v < 0.72f ? Pal.L2
                : Pal.L3;
            cv.Px(x, y, c);
        }

        // 树冠簇：小而密，避免出现"气泡"
        var rnd = new System.Random(seed);
        for (var i = 0; i < 620; i++)
        {
            var cx = (float)rnd.NextDouble() * cv.W;
            var cy = (float)rnd.NextDouble() * cv.H;
            var r = 3.5f + (float)rnd.NextDouble() * 7.5f;

            cv.Disc(cx, cy, r, Pal.L1);
            cv.Disc(cx - r * 0.16f, cy - r * 0.20f, r * 0.68f, Pal.L2);
            if (r > 7f) cv.Disc(cx - r * 0.30f, cy - r * 0.36f, r * 0.28f, Pal.L3);
        }

        // 极少量暖色透光，避免背景死黑
        for (var i = 0; i < 60; i++)
        {
            var cx = (float)rnd.NextDouble() * cv.W;
            var cy = (float)rnd.NextDouble() * cv.H;
            cv.DiscShade(cx, cy, 5f + (float)rnd.NextDouble() * 8f, Pal.L5, 0.05f);
        }
    }

    /// <summary>地图最外圈：画成延伸出去的树冠，让地图矩形边界消失。</summary>
    private static void BorderTrees(PixelCanvas cv, int gx, int gy, int px, int py)
    {
        cv.BlendRect(px, py, T, T, Pal.ShadowDeep, 0.28f);

        for (var i = 0; i < 3; i++)
        {
            var cx = px + 3 + PixelCanvas.Rand(gx, gy, 610 + i) * 10f;
            var cy = py + 3 + PixelCanvas.Rand(gx, gy, 620 + i) * 10f;
            var r = 4.5f + PixelCanvas.Rand(gx, gy, 630 + i) * 4f;

            cv.Disc(cx, cy, r, Pal.L0);
            cv.Disc(cx - r * 0.18f, cy - r * 0.22f, r * 0.66f, Pal.L1);
            cv.Disc(cx - r * 0.34f, cy - r * 0.40f, r * 0.26f, Pal.L2);
        }
    }

    // ————————————————————————— 地面总入口 —————————————————————————

    public static void PaintGround(PixelCanvas cv, TerrainMap map, int ox, int oy, int style = 0)
    {
        for (var gy = 0; gy < map.H; gy++)
        for (var gx = 0; gx < map.W; gx++)
        {
            var px = ox + gx * T;
            var py = oy + gy * T;
            var g = map.Ground(gx, gy);

            // 地图最外圈画成延伸的树冠，边界就消失了
            if (g == '#' && map.IsBorder(gx, gy))
            {
                BorderTrees(cv, gx, gy, px, py);
                continue;
            }

            switch (g)
            {
                case '.':
                    Road(cv, map, gx, gy, px, py);
                    break;
                case ':':
                    Field(cv, map, gx, gy, px, py);
                    break;
                case '#':
                    // 瓦顶由 PropArt 画；这里先铺瓦缝底色，避免透出草色
                    cv.FillRect(px, py, T, T, Pal.T0);
                    break;
                case 'W':
                    // 城墙：夯土包砖，顶部一条受光的墙脊
                    CityWall(cv, gx, gy, px, py);
                    break;
                case '+':
                    Interior(cv, map, gx, gy, px, py);
                    Threshold(cv, px, py);
                    break;
                case ',' when map.IsIndoor(gx, gy):
                    Interior(cv, map, gx, gy, px, py);
                    break;
                default:
                    Grass(cv, map, gx, gy, px, py, style);
                    break;
            }
        }

        // 边缘过渡放在最后，这样它读得到两种地形的最终颜色
        for (var gy = 0; gy < map.H; gy++)
        for (var gx = 0; gx < map.W; gx++)
        {
            if (map.IsBorder(gx, gy) && map.Ground(gx, gy) == '#') continue;
            BlendEdges(cv, map, gx, gy, ox + gx * T, oy + gy * T);
        }
    }

    // ————————————————————————— 草 —————————————————————————

    private static void Grass(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py, int style)
    {
        for (var j = 0; j < T; j++)
        for (var i = 0; i < T; i++)
        {
            var wx = px + i;
            var wy = py + j;

            // 大尺度明暗：避免出现棋盘格，也避免整片一个色
            // 两层噪声：大块决定明暗分区，小块决定颗粒
            var big = PixelCanvas.Fbm(wx / 58f, wy / 58f, 97, 2);
            var n = PixelCanvas.Fbm(wx / 27f, wy / 27f, 101, 2);
            var f = PixelCanvas.Rand(wx, wy, 7);

            // 高频扰动打破噪声的圆形轮廓，土斑才不会像"泥点"
            var rough = (PixelCanvas.Rand(wx / 2, wy / 2, 99) - 0.5f) * 0.14f;
            var wither = big + rough;

            var v = n * 0.52f + f * 0.18f + (big - 0.5f) * 0.58f + 0.20f;

            Color c;
            if (style == 2)
            {
                // 雪：冷灰白。不能靠提亮灰褐——那出来是米黄，不是雪
                c = v < 0.30f ? Pal.N0
                    : v < 0.52f ? Pal.N1
                    : v < 0.76f ? Pal.N2
                    : v < 0.92f ? Pal.N3
                    : Pal.N4;
            }
            else if (style == 1)
            {
                // 城里踩得没草了，只剩夯土和碎石
                c = v < 0.30f ? Pal.Darken(Pal.D0, 0.18f)
                    : v < 0.52f ? Pal.D0
                    : v < 0.76f ? Pal.D1
                    : Pal.Lighten(Pal.D1, 0.10f);
            }
            else if (wither < 0.20f)
            {
                // 小块枯死的地皮，只露一点点干土
                c = v < 0.48f ? Pal.D0 : v < 0.66f ? Pal.D1 : Pal.G1;
            }
            else if (v < 0.20f) c = Pal.G1;
            else if (v < 0.38f) c = Pal.G2;
            else if (v < 0.60f) c = Pal.G3;
            else c = Pal.G4;

            cv.Px(wx, wy, c);
        }

        // 草叶簇：∧ 形，2~3 像素。这是星露谷式的"细碎生命感"
        var blades = 2 + (int)(PixelCanvas.Rand(gx, gy, 311) * 3f);
        for (var b = 0; b < blades; b++)
        {
            var bx = 2 + (int)(PixelCanvas.Rand(gx, gy, 320 + b) * 12f);
            var by = 3 + (int)(PixelCanvas.Rand(gx, gy, 340 + b) * 11f);
            var col = style switch
            {
                2 => PixelCanvas.Rand(gx, gy, 360 + b) > 0.7f ? Pal.N4 : Pal.N2,
                1 => PixelCanvas.Rand(gx, gy, 360 + b) > 0.7f ? Pal.S2 : Pal.D1,
                _ => PixelCanvas.Rand(gx, gy, 360 + b) > 0.7f ? Pal.G4 : Pal.G3,
            };

            cv.Px(px + bx, py + by - 1, col);
            cv.Px(px + bx - 1, py + by, col);
            cv.Px(px + bx + 1, py + by, col);
            if (PixelCanvas.Rand(gx, gy, 380 + b) > 0.5f)
                cv.Px(px + bx, py + by + 1, Pal.G1);
        }

        // 石子 / 落叶
        var r = PixelCanvas.Rand(gx, gy, 55);
        if (r < 0.07f)
        {
            var sx = 3 + (int)(PixelCanvas.Rand(gx, gy, 56) * 10f);
            var sy = 3 + (int)(PixelCanvas.Rand(gx, gy, 57) * 10f);
            cv.Px(px + sx, py + sy, Pal.S1);
            cv.Px(px + sx + 1, py + sy, Pal.S0);
            cv.Px(px + sx, py + sy + 1, Pal.ShadowDeep);
        }
        else if (r < 0.165f)
        {
            var sx = 2 + (int)(PixelCanvas.Rand(gx, gy, 58) * 12f);
            var sy = 2 + (int)(PixelCanvas.Rand(gx, gy, 59) * 12f);
            cv.Px(px + sx, py + sy, Pal.D1);
            cv.Px(px + sx + 1, py + sy, Pal.L5);
        }
    }

    // ————————————————————————— 土路 —————————————————————————

    private static void Road(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        var left = map.Ground(gx - 1, gy) == '.';
        var right = map.Ground(gx + 1, gy) == '.';
        var up = map.Ground(gx, gy - 1) == '.';
        var down = map.Ground(gx, gy + 1) == '.';
        var horizontal = left || right;

        for (var j = 0; j < T; j++)
        for (var i = 0; i < T; i++)
        {
            var wx = px + i;
            var wy = py + j;

            var n = PixelCanvas.Fbm(wx / 28f, wy / 28f, 211, 2);
            var f = PixelCanvas.Rand(wx, wy, 213);
            var v = n * 0.74f + f * 0.26f;

            Color c;
            if (v < 0.38f) c = Pal.R1;
            else if (v < 0.56f) c = Pal.R2;
            else if (v < 0.74f) c = Pal.R3;
            else c = Pal.R4;
            cv.Px(wx, wy, c);
        }

        // 车辙：横向路上画两道纵向浅沟，纵向路画横行
        if (horizontal)
        {
            for (var j = 0; j < T; j++)
            {
                cv.Blend(px + 5, py + j, Pal.R0, 0.16f);
                cv.Blend(px + 11, py + j, Pal.R0, 0.16f);
                cv.Blend(px + 4, py + j, Pal.R4, 0.10f);
                cv.Blend(px + 10, py + j, Pal.R4, 0.10f);
            }
        }
        else if (up || down)
        {
            for (var i = 0; i < T; i++)
            {
                cv.Blend(px + i, py + 5, Pal.R0, 0.16f);
                cv.Blend(px + i, py + 11, Pal.R0, 0.16f);
                cv.Blend(px + i, py + 4, Pal.R4, 0.10f);
                cv.Blend(px + i, py + 10, Pal.R4, 0.10f);
            }
        }

        // 碎石
        var stones = 1 + (int)(PixelCanvas.Rand(gx, gy, 221) * 3f);
        for (var s = 0; s < stones; s++)
        {
            var sx = 1 + (int)(PixelCanvas.Rand(gx, gy, 230 + s) * 13f);
            var sy = 1 + (int)(PixelCanvas.Rand(gx, gy, 240 + s) * 13f);
            var big = PixelCanvas.Rand(gx, gy, 250 + s) > 0.6f;
            cv.Px(px + sx, py + sy, Pal.S3);
            if (big)
            {
                cv.Px(px + sx + 1, py + sy, Pal.S2);
                cv.Px(px + sx, py + sy + 1, Pal.S1);
            }
        }
    }

    // ————————————————————————— 荒田 —————————————————————————

    private static void Field(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        // 犁沟沿横向（地图上田是横条，纹理要跟着走）
        for (var j = 0; j < T; j++)
        {
            var groove = j % 8;
            Color c;
            if (groove <= 1) c = Pal.F0;          // 沟底
            else if (groove == 2) c = Pal.F1;
            else if (groove <= 5) c = Pal.F2;
            else if (groove == 6) c = Pal.F3;     // 垄顶受光
            else c = Pal.F2;

            for (var i = 0; i < T; i++)
            {
                var f = PixelCanvas.Rand(px + i, py + j, 301);
                var cc = f > 0.86f ? Pal.F3 : f < 0.14f ? Pal.F1 : c;
                cv.Px(px + i, py + j, cc);
            }
        }

        // 残茬：枯死的苗，稀疏竖线
        var stalks = 1 + (int)(PixelCanvas.Rand(gx, gy, 331) * 3f);
        for (var s = 0; s < stalks; s++)
        {
            var sx = 2 + (int)(PixelCanvas.Rand(gx, gy, 340 + s) * 12f);
            var sy = 2 + (int)(PixelCanvas.Rand(gx, gy, 350 + s) * 11f);
            var len = 2 + (int)(PixelCanvas.Rand(gx, gy, 360 + s) * 2f);
            for (var t = 0; t < len; t++)
                cv.Px(px + sx, py + sy - t, t == len - 1 ? Pal.F4 : Pal.F1);
        }

        // 地头偶尔一块干土
        if (PixelCanvas.Rand(gx, gy, 370) < 0.12f)
        {
            cv.DiscShade(px + 8, py + 8, 5f, Pal.D0, 0.35f);
        }
    }

    // ————————————————————————— 室内 —————————————————————————

    private static void Interior(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        for (var j = 0; j < T; j++)
        for (var i = 0; i < T; i++)
        {
            var wx = px + i;
            var wy = py + j;
            var n = PixelCanvas.Fbm(wx / 26f, wy / 26f, 401, 2);
            var f = PixelCanvas.Rand(wx, wy, 403);
            var v = n * 0.78f + f * 0.22f;

            var c = v < 0.42f ? Pal.H0 : v < 0.55f ? Pal.H1 : v < 0.72f ? Pal.H2 : Pal.H3;
            cv.Px(wx, wy, c);
        }

        // 室内整体进阴影，屋子才有深度
        cv.BlendRect(px, py, T, T, Pal.ShadowDeep, 0.20f);

        // 墙根阴影：贴墙的一侧压暗，室内立刻有"高差"
        if (map.IsWall(gx, gy - 1))
            for (var d = 0; d < 7; d++)
                cv.BlendRect(px, py + d, T, 1, Pal.ShadowDeep, 0.42f * (1f - d / 7f));
        if (map.IsWall(gx, gy + 1))
            for (var d = 0; d < 5; d++)
                cv.BlendRect(px, py + T - 1 - d, T, 1, Pal.ShadowDeep, 0.30f * (1f - d / 5f));
        if (map.IsWall(gx - 1, gy))
            for (var d = 0; d < 5; d++)
                cv.BlendRect(px + d, py, 1, T, Pal.ShadowDeep, 0.32f * (1f - d / 5f));
        if (map.IsWall(gx + 1, gy))
            for (var d = 0; d < 5; d++)
                cv.BlendRect(px + T - 1 - d, py, 1, T, Pal.ShadowDeep, 0.26f * (1f - d / 5f));

        // 灰尘、草屑、干涸的污渍
        var r = PixelCanvas.Rand(gx, gy, 411);
        if (r < 0.30f)
        {
            var sx = 2 + (int)(PixelCanvas.Rand(gx, gy, 412) * 12f);
            var sy = 2 + (int)(PixelCanvas.Rand(gx, gy, 413) * 12f);
            cv.Px(px + sx, py + sy, Pal.H0);
            cv.Px(px + sx + 1, py + sy, Pal.H1);
        }
        if (r > 0.86f)
        {
            cv.DiscShade(px + 4 + PixelCanvas.Rand(gx, gy, 414) * 8f,
                py + 4 + PixelCanvas.Rand(gx, gy, 415) * 8f,
                4f, Pal.ShadowDeep, 0.16f);
        }

        // 屋里总有点没扫干净的东西：草屑、碎陶、踩实的脚印
        var junk = PixelCanvas.Rand(gx, gy, 421);
        if (junk < 0.30f)
        {
            var jx = 3 + (int)(PixelCanvas.Rand(gx, gy, 422) * 10f);
            var jy = 3 + (int)(PixelCanvas.Rand(gx, gy, 423) * 10f);
            cv.Px(px + jx, py + jy, Pal.G1);
            cv.Px(px + jx + 1, py + jy, Pal.H0);
            cv.Px(px + jx, py + jy + 1, Pal.H1);
        }
        else if (junk < 0.42f)
        {
            var jx = 4 + (int)(PixelCanvas.Rand(gx, gy, 424) * 8f);
            var jy = 4 + (int)(PixelCanvas.Rand(gx, gy, 425) * 8f);
            cv.Px(px + jx, py + jy, Pal.D1);
            cv.Px(px + jx, py + jy + 1, Pal.D0);
        }
    }

    private static void CityWall(PixelCanvas cv, int gx, int gy, int px, int py)
    {
        for (var j = 0; j < T; j++)
        for (var i = 0; i < T; i++)
        {
            var wx = px + i;
            var wy = py + j;
            var row = wy / 5;
            var off = (row & 1) * 3;
            var bx = (wx + off) / 5;
            var r = PixelCanvas.Rand(bx, row, 1701);
            var c = r < 0.30f ? Pal.Darken(Pal.S1, 0.10f)
                : r < 0.72f ? Pal.S1
                : Pal.S2;

            if ((wx + off) % 5 == 0 || wy % 5 == 0) c = Pal.Darken(c, 0.40f, 0.2f);
            cv.Px(wx, wy, c);
        }

        // 墙脊：整排最上面一条受光
        for (var i = 0; i < T; i++) cv.Blend(px + i, py, Pal.Lighten(Pal.S3, 0.20f), 0.70f);
        for (var i = 0; i < T; i++) cv.Blend(px + i, py + T - 1, Pal.ShadowDeep, 0.45f);
    }

    private static void Threshold(PixelCanvas cv, int px, int py)
    {
        // 门槛：一条横向石条
        cv.FillRect(px, py + 12, T, 4, Pal.S1);
        cv.HLine(px, px + T - 1, py + 12, Pal.S2);
        cv.HLine(px, px + T - 1, py + 15, Pal.S0);
    }

    // ————————————————————————— 边缘过渡 —————————————————————————

    /// <summary>
    /// 两种地形交界处互相"化"过去一点。没有这一步，格子感会非常硬。
    /// </summary>
    private static void BlendEdges(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        var self = map.Ground(gx, gy);
        if (self is '#' or '~' or '^' or '=' or 'T') return;

        ReadOnlySpan<(int dx, int dy)> dirs = stackalloc (int, int)[]
        {
            (0, -1), (0, 1), (-1, 0), (1, 0),
        };

        const float str = 0.55f;

        foreach (var (dx, dy) in dirs)
        {
            var other = map.Ground(gx + dx, gy + dy);
            if (other == self || other == '#' || other == 'T') continue;
            if (!IsLoose(other) && !IsLoose(self)) continue;

            var oc = OtherColor(other);
            for (var d = 0; d < 5; d++)
            {
                var density = (1f - d / 5f) * str;
                for (var k = 0; k < T; k++)
                {
                    if (PixelCanvas.Rand(px + k, py + d, 900 + d) > density * 0.55f) continue;

                    int tx, ty;
                    if (dy < 0) { tx = px + k; ty = py + d; }
                    else if (dy > 0) { tx = px + k; ty = py + T - 1 - d; }
                    else if (dx < 0) { tx = px + d; ty = py + k; }
                    else { tx = px + T - 1 - d; ty = py + k; }

                    cv.Blend(tx, ty, oc, 0.55f);
                }
            }
        }
    }

    private static bool IsLoose(char c) => c is ',' or '.' or ':' or '+';

    private static Color OtherColor(char c) => c switch
    {
        '.' => Pal.R2,
        ':' => Pal.F1,
        ',' => Pal.G2,
        '+' => Pal.H1,
        _ => Pal.G2,
    };
}
