using Godot;

namespace Juhuo.Art;

/// <summary>
/// 立体物件：树、残墙、井、坟、栅栏、丧幡、人物。
/// 全部按"底部 Y"排序绘制，靠后的盖住靠前的——这是纯 2D 做纵深的标准做法。
/// </summary>
public static class PropArt
{
    private const int T = TileArt.T;

    // ————————————————————————— 树 —————————————————————————

    /// <summary>
    /// 荒村的树：树干偏细，树冠分三簇，边缘留缺口。
    /// 树冠上伸约 20px，所以画的时候会盖住上面一格——这是对的。
    /// </summary>
    public static void Tree(PixelCanvas cv, int gx, int gy, int px, int py, int seed)
    {
        var baseX = px + 8;
        var baseY = py + 15;

        var r1 = PixelCanvas.Rand(gx, gy, seed + 1);
        var r2 = PixelCanvas.Rand(gx, gy, seed + 2);

        // —— 地面阴影 ——
        cv.DiscShade(baseX + 1, baseY + 1, 11f, Pal.ShadowDeep, 0.34f);

        // —— 树干 ——
        var trunkTop = baseY - 16 - (int)(r1 * 5f);
        for (var y = trunkTop; y <= baseY; y++)
        {
            var wob = (int)(Mathf.Sin(y * 0.62f + gx) * 0.7f);
            cv.Px(baseX - 2 + wob, y, Pal.W0);
            cv.Px(baseX - 1 + wob, y, Pal.W1);
            cv.Px(baseX + wob, y, Pal.W2);
            cv.Px(baseX + 1 + wob, y, Pal.W0);
        }
        // 树根
        cv.Px(baseX - 3, baseY, Pal.W1);
        cv.Px(baseX - 4, baseY + 1, Pal.W0);
        cv.Px(baseX + 2, baseY, Pal.W1);
        cv.Px(baseX + 3, baseY + 1, Pal.W0);

        // —— 树冠：五层同心，左上受光 ——
        var cx = baseX + (r2 - 0.5f) * 4f;
        var cy = trunkTop - 6f;

        // 树冠：几团重叠的圆，不是同心圆——同心圆看起来像球，团块才像树
        (float dx, float dy, float r, Color c)[] blobs =
        {
            (0f, 4f, 15f, Pal.L0),
            (-5f, 0f, 12f, Pal.L1),
            (5f, 2f, 11f, Pal.L1),
            (-2f, -6f, 12f, Pal.L1),
            (-8f, -3f, 8.5f, Pal.L2),
            (4f, -7f, 9f, Pal.L2),
            (-5f, -9f, 7f, Pal.L3),
            (1f, -12f, 6f, Pal.L3),
            (-10f, -8f, 4.5f, Pal.L4),
            (-6f, -14f, 3.8f, Pal.L4),
        };
        var sc = 0.84f + PixelCanvas.Rand(gx, gy, seed + 99) * 0.34f;   // 每棵树大小不同

        // 四成的树已经黄了。全是绿的会像夏天，全是黄的会像死村
        var autumn = PixelCanvas.Rand(gx, gy, seed + 7) > 0.62f;
        var ramp = autumn
            ? new[] { Pal.Y0, Pal.Y1, Pal.Y2, Pal.Y3, Pal.Y4 }
            : new[] { Pal.L0, Pal.L1, Pal.L2, Pal.L3, Pal.L4 };

        var shade = new[]
        {
            ramp[0], ramp[1], ramp[1], ramp[1],
            ramp[2], ramp[2], ramp[3], ramp[3], ramp[4], ramp[4],
        };

        for (var i = 0; i < blobs.Length; i++)
        {
            var (dx, dy, r, _) = blobs[i];
            cv.Disc(cx + dx * 1.1f * sc, cy + dy * 1.1f * sc, r * 1.1f * sc, shade[i]);
        }

        // 边缘破损：外圈随机挖缺，树冠就不像圆球
        for (var a = 0; a < 64; a++)
        {
            var ang = a / 64f * Mathf.Tau;
            var rad = 17.0f + PixelCanvas.Rand(gx * 13 + a, gy, seed + 9) * 3.6f;
            var ex = Mathf.RoundToInt(cx + Mathf.Cos(ang) * rad);
            var ey = Mathf.RoundToInt(cy + Mathf.Sin(ang) * rad);
            if (PixelCanvas.Rand(gx, gy, seed + 20 + a) > 0.36f)
                cv.Px(ex, ey, autumn ? Pal.Y0 : Pal.L0);
        }

        // 树冠顶上的受光：不加这一层，树会像一块扁平的绿饼
        for (var i = 0; i < 26; i++)
        {
            var lx = cx + (PixelCanvas.Rand(gx, gy, seed + 120 + i) - 0.62f) * 20f * sc;
            var ly = cy + (PixelCanvas.Rand(gx, gy, seed + 140 + i) - 0.78f) * 18f * sc;
            var col = PixelCanvas.Rand(gx, gy, seed + 160 + i) > 0.55f
                ? (autumn ? Pal.Y4 : Pal.L4)
                : (autumn ? Pal.Y3 : Pal.L3);
            cv.Px(Mathf.RoundToInt(lx), Mathf.RoundToInt(ly), col);
        }

        // 黄叶：秋天，稀疏几簇
        var leaves = 4 + (int)(r1 * 6f);
        for (var i = 0; i < leaves; i++)
        {
            var lx = cx + (PixelCanvas.Rand(gx, gy, seed + 30 + i) - 0.5f) * 28f;
            var ly = cy + (PixelCanvas.Rand(gx, gy, seed + 50 + i) - 0.45f) * 26f;
            var ix = Mathf.RoundToInt(lx);
            var iy = Mathf.RoundToInt(ly);
            cv.Px(ix, iy, Pal.L5);
            if (PixelCanvas.Rand(gx, gy, seed + 60 + i) > 0.45f)
                cv.Px(ix + 1, iy, Pal.L4);
            if (PixelCanvas.Rand(gx, gy, seed + 70 + i) > 0.75f)
                cv.Px(ix, iy + 1, Pal.L3);
        }
    }

    // ————————————————————————— 残墙 —————————————————————————

    /// <summary>
    /// 房屋的墙。设定上是"屋顶被掀了"，所以从上方看到的是石砌墙顶。
    /// 4px 石块错缝排列，内侧压暗、外侧投影，墙就有厚度。
    /// </summary>
    public static void Wall(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        const int BS = 6;

        for (var j = 0; j < T; j++)
        for (var i = 0; i < T; i++)
        {
            var wx = px + i;
            var wy = py + j;

            // 错缝砌石：6px 一块，奇数行错开 3px
            var row = wy / BS;
            var off = (row & 1) * 3;
            var bx = (wx + off) / BS;

            var r = PixelCanvas.Rand(bx, row, 701);
            var baseC = r < 0.22f ? Pal.S0 : r < 0.62f ? Pal.S1 : Pal.S2;

            var lx = (wx + off) % BS;
            var ly = wy % BS;

            Color c;
            if (lx == 0 || ly == 0) c = Pal.Darken(baseC, 0.40f, 0.15f);      // 石缝
            else if (lx == 1 || ly == 1) c = Pal.Lighten(baseC, 0.12f);        // 石面受光
            else c = baseC;

            // 坑洼与苔痕
            var g = PixelCanvas.Rand(wx, wy, 707);
            if (g > 0.90f) c = Pal.Lighten(baseC, 0.08f);
            else if (g < 0.05f) c = Pal.Darken(baseC, 0.18f, 0.2f);
            if (PixelCanvas.Rand(wx / 7, wy / 7, 709) > 0.972f) c = Pal.L1;

            cv.Px(wx, wy, c);
        }

        var south = !map.IsWall(gx, gy + 1);
        var west = !map.IsWall(gx - 1, gy);
        var east = !map.IsWall(gx + 1, gy);
        var north = !map.IsWall(gx, gy - 1);

        // 北、西外缘受光
        if (north)
        {
            for (var i = 0; i < T; i++) cv.Blend(px + i, py, Pal.Lighten(Pal.S3, 0.22f), 0.70f);
            for (var i = 0; i < T; i++) cv.Blend(px + i, py + 1, Pal.S3, 0.30f);
        }
        if (west)
        {
            for (var j = 0; j < T; j++) cv.Blend(px, py + j, Pal.Lighten(Pal.S3, 0.12f), 0.50f);
        }

        // 南面：墙的立面。从上方能看见墙有多厚——立体感全在这一条上。
        if (south)
        {
            const int faceH = 6;
            for (var j = T - faceH; j < T; j++)
            {
                var row = j - (T - faceH);
                for (var i = 0; i < T; i++)
                {
                    var wx = px + i;
                    var col = (wx + (row / 2) * 2) / 5;
                    var shade = PixelCanvas.Rand(col, row / 2, 811);
                    var c = shade < 0.33f ? Pal.S0 : shade < 0.70f ? Pal.S1 : Pal.S2;
                    if (j == T - faceH) c = Pal.Darken(c, 0.44f, 0.3f);   // 檐下暗线
                    cv.Px(wx, py + j, c);
                }
            }
            for (var i = 0; i < T; i++) cv.Blend(px + i, py + T - faceH - 1, Pal.S3, 0.60f);

            // 立面投在地上的影子
            for (var d = 1; d <= 4; d++)
            {
                var a = 0.36f * (1f - d / 5f);
                for (var i = 0; i < T; i++) cv.Blend(px + i, py + T - 1 + d, Pal.ShadowDeep, a);
            }
        }
        else
        {
            for (var i = 0; i < T; i++) cv.Blend(px + i, py + T - 1, Pal.ShadowDeep, 0.22f);
        }

        if (east)
        {
            for (var j = 0; j < T; j++) cv.Blend(px + T - 1, py + j, Pal.ShadowDeep, 0.34f);
        }
    }

    /// <summary>墙脚投在地上的影子。必须在墙画完之后调用。</summary>
    public static void WallShadow(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        if (map.IsWall(gx, gy - 1)) return;   // 只有最北的一排墙才该投影
        if (map.IsWall(gx, gy + 1)) return;

        // 只有"墙 + 下面是地面"的南缘才投影
        var below = map.Ground(gx, gy + 1);
        if (below is '#' or 'T') return;
    }

    // ————————————————————————— 瓦顶 —————————————————————————

    /// <summary>
    /// 塌了一半的屋顶：只盖北墙这一排，南边露出室内。
    /// 有了这一条，"一圈石头"才变成"一栋房子"。
    /// </summary>
    public static void RoofStrip(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        for (var j = 0; j < T; j++)
        for (var i = 0; i < T; i++)
        {
            var wx = px + i;
            var wy = py + j;

            // 瓦垄：竖向，每 3px 一沟。用世界坐标算，跨格子才连续
            var g = wx % 4;
            var baseC = g switch
            {
                0 => Pal.T0,
                1 => Pal.T2,
                2 => Pal.T4,
                _ => Pal.T2,
            };

            // 低频斑驳：让整片屋顶不要像一块塑料
            var n = PixelCanvas.Fbm(wx / 11f, wy / 11f, 751, 2);
            if (n < 0.36f) baseC = Pal.Darken(baseC, 0.16f, 0.3f);
            else if (n > 0.70f) baseC = Pal.Lighten(baseC, 0.08f);

            // 瓦片横缝
            if (wy % 6 == 0) baseC = Pal.Darken(baseC, 0.30f, 0.15f);
            else if (wy % 6 == 1) baseC = Pal.Lighten(baseC, 0.06f);

            cv.Px(wx, wy, baseC);
        }

        // 屋脊：这一排的最上沿受光
        var topIsOpen = !map.IsWall(gx, gy - 1);
        if (topIsOpen)
        {
            for (var i = 0; i < T; i++) cv.Blend(px + i, py, Pal.T5, 0.75f);
            for (var i = 0; i < T; i++) cv.Blend(px + i, py + 1, Pal.T4, 0.40f);
            for (var i = 0; i < T; i++) cv.Blend(px + i, py + 2, Pal.T3, 0.18f);
        }

        // 檐口：下沿压暗，再往室内投一道影
        for (var i = 0; i < T; i++)
        {
            cv.Blend(px + i, py + T - 1, Pal.T0, 0.85f);
            cv.Blend(px + i, py + T - 2, Pal.T1, 0.45f);
        }
        if (map.IsIndoor(gx, gy + 1))
        {
            for (var d = 0; d <= 4; d++)
            {
                var a = 0.42f * (1f - d / 5f);
                for (var i = 0; i < T; i++) cv.Blend(px + i, py + T + d, Pal.ShadowDeep, a);
            }
        }

        // 两端压暗，屋顶才有"厚度"
        if (!map.IsWall(gx - 1, gy))
            for (var j = 0; j < T; j++) cv.Blend(px, py + j, Pal.ShadowDeep, 0.30f);
        if (!map.IsWall(gx + 1, gy))
            for (var j = 0; j < T; j++) cv.Blend(px + T - 1, py + j, Pal.ShadowDeep, 0.30f);
    }

    // ————————————————————————— 井 —————————————————————————

    public static void Well(PixelCanvas cv, int gx, int gy, int px, int py)
    {
        var cx = px + 8f;
        var cy = py + 8f;

        // 地面阴影
        cv.DiscShade(cx + 1, cy + 2, 10f, Pal.ShadowDeep, 0.32f);

        // 井台：外圈石环
        cv.Disc(cx, cy, 10.5f, Pal.S0);
        cv.Disc(cx, cy - 0.6f, 9.6f, Pal.S1);
        cv.Disc(cx, cy - 1.0f, 8.2f, Pal.S2);

        // 石块分块
        for (var a = 0; a < 12; a++)
        {
            var ang = a / 12f * Mathf.Tau;
            var ex = cx + Mathf.Cos(ang) * 6.2f;
            var ey = cy - 1f + Mathf.Sin(ang) * 6.2f;
            cv.Blend(Mathf.RoundToInt(ex), Mathf.RoundToInt(ey), Pal.S0, 0.85f);
        }

        // 井口
        cv.Disc(cx, cy - 1f, 6.0f, Pal.A0);
        cv.Disc(cx, cy - 1.2f, 5.0f, Pal.A1);

        // 水面反光
        cv.Px(px + 6, py + 6, Pal.A3);
        cv.Px(px + 7, py + 6, Pal.A3);
        cv.Px(px + 9, py + 8, Pal.A2);
        cv.Px(px + 10, py + 7, Pal.A2);

        // 井栏：两根立柱 + 横木
        cv.VLine(px + 2, py + 1, py + 9, Pal.W1);
        cv.VLine(px + 3, py + 1, py + 9, Pal.W2);
        cv.VLine(px + 13, py + 1, py + 9, Pal.W1);
        cv.VLine(px + 12, py + 1, py + 9, Pal.W2);
        cv.HLine(px + 3, px + 12, py + 2, Pal.W3);
        cv.HLine(px + 3, px + 12, py + 3, Pal.W1);

        // 垂下的井绳
        cv.Px(px + 8, py + 4, Pal.P0);
        cv.Px(px + 8, py + 5, Pal.P0);
        cv.Px(px + 8, py + 6, Pal.P0);
        cv.Px(px + 8, py + 7, Pal.V1);
    }

    // ————————————————————————— 坟 —————————————————————————

    public static void Grave(PixelCanvas cv, int gx, int gy, int px, int py, int seed)
    {
        var cx = px + 8f;

        // 土堆：比格子略大，压到相邻格一点点，才不像孤零零的按钮
        cv.DiscShade(cx + 1, py + 15f, 9f, Pal.ShadowDeep, 0.40f);
        cv.Disc(cx, py + 12f, 7.4f, Pal.Darken(Pal.D1, 0.22f));
        cv.Disc(cx, py + 11.4f, 6.4f, Pal.D1);
        cv.Disc(cx - 0.6f, py + 10.8f, 5.0f, Pal.Lighten(Pal.D1, 0.12f));

        // 土块颗粒
        for (var i = 0; i < 10; i++)
        {
            var bx = px + 3 + (int)(PixelCanvas.Rand(gx, gy, seed + i) * 10f);
            var by = py + 8 + (int)(PixelCanvas.Rand(gx, gy, seed + 20 + i) * 7f);
            cv.Px(bx, by, PixelCanvas.Rand(gx, gy, seed + 40 + i) > 0.5f
                ? Pal.Darken(Pal.D1, 0.15f)
                : Pal.Lighten(Pal.D1, 0.10f));
        }

        // 土堆上的枯草
        for (var i = 0; i < 6; i++)
        {
            var bx = px + 3 + (int)(PixelCanvas.Rand(gx, gy, seed + 60 + i) * 10f);
            var by = py + 8 + (int)(PixelCanvas.Rand(gx, gy, seed + 80 + i) * 6f);
            cv.Px(bx, by, Pal.G2);
            cv.Px(bx, by - 1, Pal.G3);
        }

        // 石碑：立在土堆靠后
        var top = py + 1;
        cv.FillRect(px + 5, top, 6, 11, Pal.S1);
        cv.FillRect(px + 5, top, 2, 11, Pal.S2);            // 受光面
        cv.FillRect(px + 10, top, 1, 11, Pal.S0);           // 背光面
        cv.HLine(px + 5, px + 10, top, Pal.S3);             // 碑顶
        cv.Px(px + 6, top - 1, Pal.S3);                     // 碑头弧角
        cv.Px(px + 9, top - 1, Pal.S2);

        // 碑文：三道浅刻痕，看不清是刻意的
        cv.Blend(px + 7, py + 4, Pal.S3, 0.55f);
        cv.Blend(px + 8, py + 4, Pal.S3, 0.55f);
        cv.Blend(px + 8, py + 6, Pal.S0, 0.70f);
        cv.Blend(px + 7, py + 8, Pal.S0, 0.55f);

        // 碑脚
        cv.FillRect(px + 4, py + 11, 8, 2, Pal.S0);
        cv.HLine(px + 4, px + 11, py + 11, Pal.S1);
        cv.BlendRect(px + 4, py + 13, 8, 1, Pal.ShadowDeep, 0.55f);
    }

    // ————————————————————————— 栅栏 —————————————————————————

    public static void Fence(PixelCanvas cv, TerrainMap map, int gx, int gy, int px, int py)
    {
        var left = map.Ground(gx - 1, gy) == '=';
        var right = map.Ground(gx + 1, gy) == '=';

        // 桩
        for (var k = 0; k < 2; k++)
        {
            var sx = px + (k == 0 ? 3 : 11);
            cv.VLine(sx, py + 3, py + 14, Pal.W2);
            cv.VLine(sx + 1, py + 3, py + 14, Pal.W1);
            cv.Px(sx, py + 3, Pal.W3);
            cv.Px(sx + 1, py + 3, Pal.W2);
            cv.Blend(sx + 1, py + 15, Pal.ShadowDeep, 0.4f);
        }

        // 横杆：连到邻居那一侧
        var x0 = left ? px : px + 3;
        var x1 = right ? px + T - 1 : px + 12;
        cv.HLine(x0, x1, py + 6, Pal.W2);
        cv.HLine(x0, x1, py + 7, Pal.W1);
        cv.HLine(x0, x1, py + 11, Pal.W2);
        cv.HLine(x0, x1, py + 12, Pal.W1);

        // 影子
        cv.BlendRect(px, py + 14, T, 2, Pal.ShadowDeep, 0.18f);
    }

    // ————————————————————————— 丧幡 —————————————————————————

    /// <summary>白幡。村口的丧事标记，也是画面里唯一的高亮白色。</summary>
    public static void Banner(PixelCanvas cv, int px, int py, int seed)
    {
        var poleX = px + 8;

        // 竹竿：从地面一直竖到幡顶
        for (var y = py - 30; y <= py + 15; y++)
        {
            cv.Px(poleX, y, Pal.W2);
            cv.Px(poleX + 1, y, Pal.W1);
        }
        cv.Px(poleX, py - 31, Pal.W3);
        cv.Px(poleX + 1, py - 31, Pal.W2);

        // 幡布：上宽下窄，边缘被风吹得抖
        for (var y = -27; y <= -4; y++)
        {
            var len = 9 - (y + 27) / 6;
            var wob = Mathf.RoundToInt(Mathf.Sin((y + 27) * 0.42f + seed) * 1.4f);
            for (var i = 0; i <= len; i++)
            {
                var c = i == 0 ? Pal.P2 : i == len ? Pal.P0 : Pal.P1;
                cv.Px(poleX + 2 + i + wob, py + y, c);
            }
        }

        // 幡面上的朱砂印记
        cv.Px(poleX + 5, py - 20, Pal.V1);
        cv.Px(poleX + 6, py - 20, Pal.V1);
        cv.Px(poleX + 5, py - 19, Pal.V0);
        cv.Px(poleX + 6, py - 19, Pal.V2);

        // 地面阴影
        cv.BlendRect(poleX - 2, py + 12, 6, 3, Pal.ShadowDeep, 0.30f);
    }

    // ————————————————————————— 人 —————————————————————————

    public static void Person(PixelCanvas cv, int px, int py, string kind, int facing = 1)
    {
        var (coat, coatDark, trim, hair) = kind switch
        {
            "chen" => (Pal.S1, Pal.S0, Pal.D0, Pal.Hair0),                 // 陈三：灰褐病容
            "old" => (new Color(0.30f, 0.34f, 0.40f), new Color(0.20f, 0.23f, 0.28f), Pal.P0, Pal.Hair0), // 老妇
            "doctor" => (new Color(0.46f, 0.40f, 0.26f), new Color(0.32f, 0.28f, 0.18f), Pal.V1, Pal.Hair1), // 游医
            "yamen" => (new Color(0.16f, 0.15f, 0.15f), new Color(0.10f, 0.10f, 0.10f), Pal.V1, Pal.Hair0),   // 衙役
            "sick" => (new Color(0.38f, 0.35f, 0.30f), new Color(0.26f, 0.24f, 0.21f), Pal.S0, Pal.Hair0),
            _ => (Pal.W2, Pal.W1, Pal.D0, Pal.Hair0),
        };

        var cx = px + 8;
        var top = py + 1;

        if (kind == "sick")
        {
            // 躺着的人：横卧，头在左
            cv.DiscShade(cx, py + 13f, 7f, Pal.ShadowDeep, 0.28f);
            cv.FillRect(cx - 6, py + 10, 11, 3, coat);
            cv.FillRect(cx - 6, py + 10, 11, 1, Pal.Lighten(coat, 0.10f));
            cv.FillRect(cx + 5, py + 9, 3, 3, Pal.Skin1);
            cv.Px(cx + 7, py + 9, Pal.Hair0);
            cv.Px(cx + 7, py + 10, Pal.Hair0);
            cv.Px(cx + 5, py + 10, Pal.Skin2);
            return;
        }

        // 影子
        cv.DiscShade(cx, py + 15f, 5f, Pal.ShadowDeep, 0.30f);

        // 腿
        cv.FillRect(cx - 3, top + 11, 2, 4, Pal.Darken(coat, 0.25f));
        cv.FillRect(cx + 1, top + 11, 2, 4, Pal.Darken(coat, 0.35f));

        // 身体
        cv.FillRect(cx - 4, top + 5, 8, 7, coat);
        cv.FillRect(cx - 4, top + 5, 8, 1, Pal.Lighten(coat, 0.12f));
        cv.FillRect(cx + 3, top + 5, 1, 7, coatDark);

        // 腰带 / 交领
        cv.FillRect(cx - 4, top + 9, 8, 1, trim);
        cv.Px(cx - 1, top + 5, coatDark);
        cv.Px(cx, top + 6, coatDark);
        cv.Px(cx + 1, top + 7, coatDark);

        // 手
        cv.Px(cx - 5, top + 8, Pal.Skin1);
        cv.Px(cx + 4, top + 8, Pal.Skin0);

        // 头
        cv.FillRect(cx - 3, top, 6, 5, Pal.Skin1);
        cv.FillRect(cx - 3, top, 6, 1, hair);
        cv.Px(cx - 3, top + 1, hair);
        cv.Px(cx + 2, top + 1, hair);
        cv.Px(cx - 2, top + 2, Pal.Skin2);          // 额头受光

        // 眼：两点，朝 facing 方向偏
        var eo = facing >= 0 ? 0 : -1;
        cv.Px(cx - 1 + eo, top + 2, Pal.Hair0);
        cv.Px(cx + 1 + eo, top + 2, Pal.Hair0);

        // 颔下阴影
        cv.Px(cx - 3, top + 4, Pal.Skin0);
        cv.Px(cx + 2, top + 4, Pal.Skin0);
    }

    // ————————————————————————— 出口 —————————————————————————

    public static void Exit(PixelCanvas cv, int px, int py)
    {
        // 出村的豁口：地面被踩得更深，两侧有草被踩倒
        cv.BlendRect(px, py, T, T, Pal.R0, 0.30f);
        for (var i = 0; i < T; i++)
        for (var j = 0; j < T; j++)
        {
            if (PixelCanvas.Rand(px + i, py + j, 777) > 0.72f)
                cv.Blend(px + i, py + j, Pal.R1, 0.28f);
        }

        // 折断的草
        for (var i = 0; i < 6; i++)
        {
            var bx = px + 1 + (int)(PixelCanvas.Rand(px, py, 780 + i) * 14f);
            var by = py + 2 + (int)(PixelCanvas.Rand(px, py, 790 + i) * 12f);
            cv.Px(bx, by, Pal.G1);
            cv.Px(bx + 1, by, Pal.G0);
        }
    }

    // ————————————————————————— 杂项装饰 —————————————————————————

    /// <summary>低矮灌木。半径小，不挡视线，只负责把空地填满。</summary>
    public static void Shrub(PixelCanvas cv, int gx, int gy, int px, int py, int seed)
    {
        var cx = px + 6 + PixelCanvas.Rand(gx, gy, seed) * 5f;
        var cy = py + 10 + PixelCanvas.Rand(gx, gy, seed + 1) * 4f;
        var r = 4.5f + PixelCanvas.Rand(gx, gy, seed + 2) * 3f;

        cv.DiscShade(cx, cy + 2, r * 1.05f, Pal.ShadowDeep, 0.26f);
        cv.Disc(cx, cy, r, Pal.L1);
        cv.Disc(cx - r * 0.2f, cy - r * 0.25f, r * 0.72f, Pal.L2);
        cv.Disc(cx - r * 0.36f, cy - r * 0.42f, r * 0.36f, Pal.L3);

        // 几根伸出来的枯枝
        for (var i = 0; i < 3; i++)
        {
            var a = PixelCanvas.Rand(gx, gy, seed + 10 + i) * Mathf.Tau;
            var len = r * (1.0f + PixelCanvas.Rand(gx, gy, seed + 20 + i) * 0.5f);
            cv.Px(Mathf.RoundToInt(cx + Mathf.Cos(a) * len), Mathf.RoundToInt(cy + Mathf.Sin(a) * len), Pal.W1);
        }
    }

    /// <summary>碎石堆。</summary>
    public static void RockPile(PixelCanvas cv, int gx, int gy, int px, int py, int seed)
    {
        var cx = px + 8f;
        var cy = py + 10f;

        cv.DiscShade(cx, cy + 2f, 7f, Pal.ShadowDeep, 0.28f);

        for (var i = 0; i < 4; i++)
        {
            var ox = (PixelCanvas.Rand(gx, gy, seed + i) - 0.5f) * 9f;
            var oy = (PixelCanvas.Rand(gx, gy, seed + 10 + i) - 0.5f) * 5f;
            var r = 2f + PixelCanvas.Rand(gx, gy, seed + 20 + i) * 2.2f;
            cv.Disc(cx + ox, cy + oy, r, Pal.S1);
            cv.Disc(cx + ox - 0.5f, cy + oy - 0.8f, r * 0.65f, Pal.S2);
            cv.Px(Mathf.RoundToInt(cx + ox - 0.5f), Mathf.RoundToInt(cy + oy - 1.2f), Pal.S3);
        }
    }

    /// <summary>倒下的枯木。</summary>
    public static void Log(PixelCanvas cv, int gx, int gy, int px, int py, int seed)
    {
        var horiz = PixelCanvas.Rand(gx, gy, seed) > 0.45f;
        var cx = px + 8f;
        var cy = py + 11f;

        cv.DiscShade(cx, cy + 2f, 8f, Pal.ShadowDeep, 0.22f);

        for (var i = -7; i <= 7; i++)
        {
            for (var j = -2; j <= 1; j++)
            {
                var x = horiz ? cx + i : cx + j;
                var y = horiz ? cy + j : cy + i;
                var c = j <= -1 ? Pal.W3 : j >= 1 ? Pal.W0 : Pal.W1;
                cv.Px(Mathf.RoundToInt(x), Mathf.RoundToInt(y), c);
            }
        }

        // 断面
        if (horiz)
        {
            cv.Disc(cx + 7.5f, cy - 0.5f, 2.2f, Pal.W3);
            cv.Disc(cx + 7.5f, cy - 0.5f, 1.2f, Pal.W2);
        }
        else
        {
            cv.Disc(cx - 0.5f, cy + 7.5f, 2.2f, Pal.W3);
            cv.Disc(cx - 0.5f, cy + 7.5f, 1.2f, Pal.W2);
        }
    }

    /// <summary>散落在地上的杂物：柴捆、破陶罐、倒下的木板。手摆，不随机。</summary>
    public static void Debris(PixelCanvas cv, int kind, int px, int py)
    {
        switch (kind)
        {
            case 0: // 柴捆
                for (var i = 0; i < 5; i++)
                {
                    var oy = i * 2;
                    cv.HLine(px + 2, px + 12, py + 6 + oy, i % 2 == 0 ? Pal.W1 : Pal.W2);
                }
                cv.VLine(px + 6, py + 5, py + 15, Pal.W0);
                cv.VLine(px + 10, py + 5, py + 15, Pal.W0);
                break;

            case 1: // 破陶罐
                cv.Disc(px + 8, py + 10, 4.5f, Pal.Darken(Pal.D1, 0.1f));
                cv.Disc(px + 7.4f, py + 9.2f, 3.4f, Pal.D1);
                cv.Px(px + 6, py + 7, Pal.S0);
                cv.Px(px + 9, py + 7, Pal.S0);
                cv.BlendRect(px + 5, py + 13, 7, 1, Pal.ShadowDeep, 0.35f);
                break;

            case 2: // 倒下的门板
                cv.FillRect(px + 1, py + 8, 14, 5, Pal.W2);
                cv.HLine(px + 1, px + 14, py + 8, Pal.W3);
                cv.HLine(px + 1, px + 14, py + 12, Pal.W0);
                cv.VLine(px + 4, py + 8, py + 12, Pal.W1);
                cv.VLine(px + 11, py + 8, py + 12, Pal.W1);
                break;
        }
    }
}
