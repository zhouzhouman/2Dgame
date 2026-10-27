using Godot;

namespace Juhuo.Art;

/// <summary>
/// 36 种可交互物的像素画法。
///
/// 统一的画法约定（保证一屏里 120 个东西不打架）：
///   1. 所有物件压 1px 深色轮廓线——背景是草和土，没有轮廓会糊成一片；
///   2. 左上受光、右下背光，光源方向永远一致；
///   3. 底部两像素投影，物件才"站"在地上而不是"贴"在地上。
/// </summary>
public static class PropPainters
{
    private const int T = 16;

    public static void Paint(PixelCanvas cv, PropKind kind, int px, int py, int seed)
    {
        switch (kind)
        {
            case PropKind.Stove: Stove(cv, px, py, seed); break;
            case PropKind.Table: Table(cv, px, py, seed); break;
            case PropKind.Cabinet: Cabinet(cv, px, py, seed); break;
            case PropKind.Bed: Bed(cv, px, py, seed); break;
            case PropKind.Jar: Jar(cv, px, py, seed); break;
            case PropKind.Bowl: Bowl(cv, px, py, seed); break;
            case PropKind.WallMark: WallMark(cv, px, py, seed); break;
            case PropKind.Door: Door(cv, px, py, seed); break;
            case PropKind.Fire: Fire(cv, px, py, seed); break;
            case PropKind.Rope: Rope(cv, px, py, seed); break;
            case PropKind.Mat: Mat(cv, px, py, seed); break;
            case PropKind.Ash: Ash(cv, px, py, seed); break;
            case PropKind.Step: Step(cv, px, py, seed); break;
            case PropKind.Notice: Notice(cv, px, py, seed); break;
            case PropKind.Trough: Trough(cv, px, py, seed); break;
            case PropKind.Beast: Beast(cv, px, py, seed); break;
            case PropKind.Post: Post(cv, px, py, seed); break;
            case PropKind.Well: Well(cv, px, py, seed); break;
            case PropKind.Tracks: Tracks(cv, px, py, seed); break;
            case PropKind.Litter: Litter(cv, px, py, seed); break;
            case PropKind.Wood: Wood(cv, px, py, seed); break;
            case PropKind.Crate: Crate(cv, px, py, seed); break;
            case PropKind.Book: Book(cv, px, py, seed); break;
            case PropKind.Chair: Chair(cv, px, py, seed); break;
            case PropKind.Scale: Scale(cv, px, py, seed); break;
            case PropKind.Censer: Censer(cv, px, py, seed); break;
            case PropKind.Bone: Bone(cv, px, py, seed); break;
            case PropKind.Statue: Statue(cv, px, py, seed); break;
            case PropKind.Gap: Gap(cv, px, py, seed); break;
            case PropKind.Rack: Rack(cv, px, py, seed); break;
            case PropKind.Tree: Pine(cv, px, py, seed); break;
            case PropKind.Grain: Grain(cv, px, py, seed); break;
            case PropKind.Banner: Banner(cv, px, py, seed); break;
            case PropKind.Plow: Plow(cv, px, py, seed); break;
            case PropKind.Grave: Grave(cv, px, py, seed); break;
            case PropKind.Rock: Rock(cv, px, py, seed); break;
            default: Litter(cv, px, py, seed); break;
        }
    }

    // ————————————————————————— 工具 —————————————————————————

    private static void Foot(PixelCanvas cv, int px, int py, int w = 12, float a = 0.34f)
    {
        cv.BlendRect(px + (T - w) / 2, py + T - 2, w, 2, Pal.ShadowDeep, a);
    }

    private static void Box(PixelCanvas cv, int x, int y, int w, int h, Color fill, Color line)
    {
        cv.FillRect(x, y, w, h, fill);
        cv.HLine(x, x + w - 1, y, line);
        cv.VLine(x, y, y + h - 1, line);
        cv.HLine(x, x + w - 1, y + h - 1, line);
        cv.VLine(x + w - 1, y, y + h - 1, line);
    }

    // ————————————————————————— 器物 —————————————————————————

    private static void Stove(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 14, 0.40f);

        // 灶身：夯土砖
        Box(cv, px + 1, py + 5, 14, 10, Pal.Darken(Pal.D1, 0.30f), Pal.Darken(Pal.S0, 0.2f));
        for (var j = 0; j < 10; j += 3)
            cv.HLine(px + 2, px + 13, py + 5 + j, Pal.Darken(Pal.D1, 0.45f));

        // 灶面受光
        cv.FillRect(px + 2, py + 5, 12, 2, Pal.Lighten(Pal.D1, 0.10f));

        // 灶口：黑洞 + 灰
        cv.FillRect(px + 5, py + 9, 6, 5, Pal.ShadowDeep);
        cv.HLine(px + 5, px + 10, py + 9, Pal.S0);

        // 锅
        cv.Disc(px + 8, py + 4.5f, 4.2f, Pal.Darken(Pal.S1, 0.25f));
        cv.Disc(px + 8, py + 4.2f, 3.4f, Pal.S1);
        cv.Disc(px + 7.4f, py + 3.8f, 2.2f, Pal.S2);
        cv.Px(px + 6, py + 3, Pal.S3);
    }

    private static void Table(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 14, 0.30f);

        // 腿
        cv.VLine(px + 2, py + 9, py + 14, Pal.W0);
        cv.VLine(px + 3, py + 9, py + 14, Pal.W1);
        cv.VLine(px + 12, py + 9, py + 14, Pal.W0);
        cv.VLine(px + 13, py + 9, py + 14, Pal.W1);

        // 桌面
        cv.FillRect(px + 1, py + 4, 14, 5, Pal.W2);
        cv.HLine(px + 1, px + 14, py + 4, Pal.W3);
        cv.HLine(px + 1, px + 14, py + 5, Pal.W3);
        cv.HLine(px + 1, px + 14, py + 8, Pal.W0);

        // 木纹
        cv.Px(px + 4, py + 6, Pal.W1);
        cv.Px(px + 9, py + 6, Pal.W1);
        cv.Px(px + 11, py + 7, Pal.W1);
    }

    private static void Cabinet(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 14, 0.34f);

        Box(cv, px + 1, py + 2, 14, 13, Pal.W2, Pal.W0);
        cv.HLine(px + 2, px + 13, py + 3, Pal.W3);

        // 双开门
        cv.VLine(px + 8, py + 3, py + 14, Pal.W0);
        cv.FillRect(px + 3, py + 5, 4, 7, Pal.W1);
        cv.FillRect(px + 10, py + 5, 4, 7, Pal.W1);
        cv.HLine(px + 3, px + 6, py + 5, Pal.W3);
        cv.HLine(px + 10, px + 13, py + 5, Pal.W3);

        // 铜环
        cv.Px(px + 6, py + 9, Pal.V1);
        cv.Px(px + 10, py + 9, Pal.V1);
    }

    private static void Bed(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 16, 0.28f);

        // 床板
        cv.FillRect(px + 1, py + 4, 14, 11, Pal.W1);
        cv.HLine(px + 1, px + 14, py + 4, Pal.W2);

        // 草席
        cv.FillRect(px + 2, py + 6, 12, 8, Pal.Darken(Pal.D1, 0.10f));
        for (var j = 6; j < 14; j += 2)
            cv.HLine(px + 2, px + 13, py + j, Pal.Darken(Pal.D1, 0.28f));

        // 卷起的被子
        cv.FillRect(px + 3, py + 5, 10, 3, Pal.S1);
        cv.HLine(px + 3, px + 12, py + 5, Pal.S2);

        // 床脚
        cv.VLine(px + 1, py + 14, py + 15, Pal.W0);
        cv.VLine(px + 14, py + 14, py + 15, Pal.W0);
    }

    private static void Jar(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 11, 0.34f);

        // 陶缸：鼓腹
        cv.Disc(px + 8, py + 9, 5.6f, Pal.Darken(Pal.D1, 0.40f));
        cv.Disc(px + 8, py + 8.6f, 4.9f, Pal.Darken(Pal.D1, 0.20f));
        cv.Disc(px + 6.8f, py + 7.6f, 2.8f, Pal.D1);

        // 缸口
        cv.FillRect(px + 5, py + 3, 7, 2, Pal.S0);
        cv.FillRect(px + 6, py + 3, 5, 1, Pal.ShadowDeep);

        // 釉光
        cv.Px(px + 5, py + 10, Pal.Lighten(Pal.D1, 0.20f));
        cv.Px(px + 5, py + 11, Pal.Lighten(Pal.D1, 0.12f));
    }

    private static void Bowl(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 7, 0.26f);
        cv.Disc(px + 8, py + 11.5f, 3.6f, Pal.S1);
        cv.Disc(px + 8, py + 11.0f, 2.8f, Pal.ShadowDeep);
        cv.Px(px + 6, py + 10, Pal.S3);
        cv.Px(px + 7, py + 10, Pal.S3);
    }

    private static void Fire(PixelCanvas cv, int px, int py, int seed)
    {
        // 柴
        cv.HLine(px + 2, px + 13, py + 12, Pal.W1);
        cv.HLine(px + 3, px + 12, py + 13, Pal.W0);
        cv.HLine(px + 4, px + 11, py + 11, Pal.W2);

        // 焰
        cv.Disc(px + 8, py + 8, 3.2f, Pal.V0);
        cv.Disc(px + 8, py + 7, 2.2f, Pal.V1);
        cv.Disc(px + 8, py + 6, 1.2f, Pal.V2);
        cv.Px(px + 8, py + 3 + (seed & 1), Pal.L5);

        // 火星
        cv.Px(px + 5, py + 5, Pal.V2);
        cv.Px(px + 11, py + 4, Pal.L5);

        cv.BlendRect(px + 3, py + 14, 10, 1, Pal.ShadowDeep, 0.30f);
    }

    private static void Censer(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 10, 0.30f);

        // 三足
        cv.VLine(px + 5, py + 11, py + 14, Pal.Darken(Pal.S0, 0.2f));
        cv.VLine(px + 10, py + 11, py + 14, Pal.Darken(Pal.S0, 0.2f));
        cv.Px(px + 8, py + 14, Pal.S0);

        // 炉身
        Box(cv, px + 3, py + 7, 10, 5, Pal.S1, Pal.S0);
        cv.HLine(px + 4, px + 11, py + 7, Pal.S2);

        // 炉口与香灰
        cv.FillRect(px + 5, py + 5, 6, 2, Pal.S0);
        cv.FillRect(px + 6, py + 5, 4, 1, Pal.S1);

        // 三炷香
        cv.VLine(px + 6, py + 1, py + 4, Pal.D1);
        cv.VLine(px + 8, py + 0, py + 4, Pal.D1);
        cv.VLine(px + 10, py + 2, py + 4, Pal.D1);
        cv.Px(px + 6, py + 0, Pal.L5);
        cv.Px(px + 8, py + 0, Pal.L5);
    }

    private static void Chair(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 8, 0.26f);

        // 靠背
        cv.FillRect(px + 5, py + 2, 6, 6, Pal.W1);
        cv.VLine(px + 5, py + 2, py + 7, Pal.W3);
        cv.VLine(px + 10, py + 2, py + 7, Pal.W0);

        // 座面
        cv.FillRect(px + 4, py + 8, 8, 2, Pal.W2);
        cv.HLine(px + 4, px + 11, py + 8, Pal.W3);

        // 腿
        cv.VLine(px + 4, py + 10, py + 14, Pal.W0);
        cv.VLine(px + 11, py + 10, py + 14, Pal.W0);
    }

    private static void Scale(PixelCanvas cv, int px, int py, int seed)
    {
        // 秤杆
        cv.HLine(px + 2, px + 13, py + 5, Pal.W2);
        cv.HLine(px + 2, px + 13, py + 6, Pal.W1);

        // 提绳
        cv.VLine(px + 8, py + 1, py + 4, Pal.D1);
        cv.Px(px + 7, py + 0, Pal.W1);
        cv.Px(px + 9, py + 0, Pal.W1);

        // 秤盘
        cv.VLine(px + 3, py + 6, py + 9, Pal.D1);
        cv.Disc(px + 3.5f, py + 10.5f, 2.4f, Pal.S1);

        // 秤砣
        cv.FillRect(px + 11, py + 8, 3, 3, Pal.S0);
        cv.Px(px + 12, py + 7, Pal.S1);
    }

    private static void Book(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 10, 0.22f);

        // 摊开的册子
        cv.FillRect(px + 2, py + 5, 12, 8, Pal.P1);
        cv.FillRect(px + 2, py + 5, 12, 1, Pal.P2);
        cv.VLine(px + 8, py + 5, py + 12, Pal.P0);
        cv.HLine(px + 2, px + 13, py + 12, Pal.P0);

        // 字行
        for (var j = 0; j < 4; j++)
        {
            cv.HLine(px + 4, px + 7, py + 7 + j, Pal.S0);
            cv.HLine(px + 9, px + 12, py + 7 + j, Pal.S0);
        }

        // 压着的笔
        cv.HLine(px + 9, px + 14, py + 3, Pal.W2);
        cv.Px(px + 14, py + 2, Pal.W3);
        cv.Px(px + 9, py + 3, Pal.V1);
    }

    private static void Wood(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 13, 0.30f);

        // 三根斜靠的柴，不是四根横线——横线看起来像"井"字
        for (var i = 0; i < 3; i++)
        {
            var x0 = px + 3 + i * 3;
            for (var t = 0; t < 10; t++)
            {
                var x = x0 + t / 3;
                var y = py + 13 - t;
                cv.Px(x, y, Pal.W1);
                cv.Px(x + 1, y, Pal.W2);
                cv.Px(x + 2, y, Pal.W0);
            }
            // 断面
            cv.Px(x0 + 3, py + 4, Pal.W3);
            cv.Px(x0 + 4, py + 4, Pal.W2);
        }

        // 捆绳：一道横向的草绳
        for (var i = 0; i < 12; i++)
        {
            var y = py + 9 - i / 4;
            cv.Px(px + 2 + i, y, i % 3 == 0 ? Pal.D1 : Pal.Darken(Pal.D1, 0.25f));
        }
    }

    private static void Crate(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 13, 0.34f);

        Box(cv, px + 1, py + 3, 14, 12, Pal.W2, Pal.W0);
        cv.HLine(px + 2, px + 13, py + 4, Pal.W3);

        // 板条
        cv.HLine(px + 2, px + 13, py + 8, Pal.W0);
        cv.VLine(px + 8, py + 4, py + 13, Pal.W0);

        // 铁角
        cv.Px(px + 2, py + 4, Pal.S1);
        cv.Px(px + 13, py + 4, Pal.S1);
        cv.Px(px + 2, py + 13, Pal.S1);
        cv.Px(px + 13, py + 13, Pal.S1);
    }

    private static void Rack(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 13, 0.30f);

        // 架身
        cv.VLine(px + 2, py + 3, py + 14, Pal.W1);
        cv.VLine(px + 13, py + 3, py + 14, Pal.W1);
        cv.HLine(px + 2, px + 13, py + 6, Pal.W2);
        cv.HLine(px + 2, px + 13, py + 12, Pal.W2);

        // 倚着的兵器
        cv.VLine(px + 5, py + 2, py + 11, Pal.S1);
        cv.VLine(px + 9, py + 4, py + 11, Pal.W2);
        cv.Px(px + 5, py + 1, Pal.S2);
        cv.Px(px + 9, py + 3, Pal.V1);

        // 鼓
        cv.Disc(px + 11, py + 10, 2.6f, Pal.V0);
        cv.Disc(px + 11, py + 10, 1.8f, Pal.V1);
    }

    private static void Grain(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 14, 0.32f);

        // 谷堆
        cv.Disc(px + 8, py + 11, 6.4f, Pal.Darken(Pal.F3, 0.24f));
        cv.Disc(px + 8, py + 10, 5.2f, Pal.Darken(Pal.F3, 0.10f));
        cv.Disc(px + 7, py + 8.5f, 3.2f, Pal.F3);

        // 谷粒
        for (var i = 0; i < 12; i++)
        {
            var gx = px + 3 + (int)(PixelCanvas.Rand(px, py, seed + i) * 11f);
            var gy = py + 6 + (int)(PixelCanvas.Rand(px, py, seed + 20 + i) * 8f);
            cv.Px(gx, gy, Pal.F4);
        }
    }

    // ————————————————————————— 建筑构件 —————————————————————————

    private static void Door(PixelCanvas cv, int px, int py, int seed)
    {
        // 门框
        cv.FillRect(px + 2, py + 1, 12, 15, Pal.W1);
        cv.VLine(px + 2, py + 1, py + 15, Pal.W3);
        cv.VLine(px + 13, py + 1, py + 15, Pal.W0);

        // 门板
        cv.FillRect(px + 4, py + 3, 8, 13, Pal.W2);
        cv.HLine(px + 4, px + 11, py + 3, Pal.W3);

        // 板缝
        cv.VLine(px + 6, py + 4, py + 15, Pal.W1);
        cv.VLine(px + 9, py + 4, py + 15, Pal.W1);

        // 门环
        cv.Px(px + 10, py + 9, Pal.V1);
        cv.Px(px + 10, py + 10, Pal.V0);

        // 门槛
        cv.HLine(px + 2, px + 13, py + 15, Pal.S1);
    }

    private static void Notice(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 8, 0.30f);

        // 立柱
        cv.VLine(px + 7, py + 6, py + 15, Pal.W1);
        cv.VLine(px + 8, py + 6, py + 15, Pal.W0);

        // 木牌
        Box(cv, px + 2, py + 1, 12, 9, Pal.P0, Pal.W0);
        cv.HLine(px + 3, px + 12, py + 2, Pal.P1);

        // 字
        for (var j = 0; j < 3; j++)
            cv.HLine(px + 4, px + 10 - j, py + 4 + j * 2, Pal.S0);

        // 朱印
        cv.Px(px + 11, py + 7, Pal.V1);
    }

    private static void Step(PixelCanvas cv, int px, int py, int seed)
    {
        // 三级石阶
        for (var i = 0; i < 3; i++)
        {
            var y = py + 4 + i * 4;
            var inset = 2 - i;
            cv.FillRect(px + inset, y, T - inset * 2, 4, i == 0 ? Pal.S2 : Pal.S1);
            cv.HLine(px + inset, px + T - 1 - inset, y, Pal.S3);
            cv.HLine(px + inset, px + T - 1 - inset, y + 3, Pal.S0);
        }
    }

    private static void Gap(PixelCanvas cv, int px, int py, int seed)
    {
        // 洞口：越往里越黑
        cv.Disc(px + 8, py + 9, 6.2f, Pal.S0);
        cv.Disc(px + 8, py + 9, 5.0f, Pal.ShadowDeep);
        cv.Disc(px + 8, py + 9, 3.2f, new Color(0.01f, 0.01f, 0.02f));

        // 边缘碎砖
        cv.Px(px + 3, py + 6, Pal.S1);
        cv.Px(px + 12, py + 8, Pal.S1);
        cv.Px(px + 8, py + 3, Pal.S2);
    }

    private static void WallMark(PixelCanvas cv, int px, int py, int seed)
    {
        // 墙面裂缝 / 刻痕：不是实物，是墙上的痕迹
        cv.HLine(px + 4, px + 11, py + 5, Pal.Darken(Pal.S0, 0.35f));
        cv.HLine(px + 4, px + 9, py + 6, Pal.Darken(Pal.S0, 0.20f));

        for (var j = 0; j < 3; j++)
            cv.VLine(px + 5 + j * 2, py + 7, py + 10, Pal.Darken(Pal.S0, 0.25f));

        cv.Px(px + 11, py + 11, Pal.Darken(Pal.S0, 0.10f));
    }

    // ————————————————————————— 地面 —————————————————————————

    private static void Tracks(PixelCanvas cv, int px, int py, int seed)
    {
        // 车辙 / 脚印：压出来的浅坑
        var vertical = PixelCanvas.Rand(px, py, seed) > 0.5f;

        if (vertical)
        {
            for (var j = 2; j < 14; j++)
            {
                cv.Blend(px + 5, py + j, Pal.Darken(Pal.R1, 0.30f), 0.55f);
                cv.Blend(px + 10, py + j, Pal.Darken(Pal.R1, 0.30f), 0.45f);
                if (j % 3 == 0)
                {
                    cv.Blend(px + 6, py + j, Pal.R4, 0.28f);
                    cv.Blend(px + 11, py + j, Pal.R4, 0.22f);
                }
            }
        }
        else
        {
            for (var j = 2; j < 14; j++)
            {
                cv.Blend(px + j, py + 5, Pal.Darken(Pal.R1, 0.30f), 0.50f);
                cv.Blend(px + j, py + 10, Pal.Darken(Pal.R1, 0.30f), 0.40f);
            }
        }
    }

    private static void Litter(PixelCanvas cv, int px, int py, int seed)
    {
        // 散落的杂物：碎瓦、枯叶、水洼、雪
        var r = PixelCanvas.Rand(px, py, seed);
        if (r < 0.30f)
        {
            // 碎瓦片
            cv.Px(px + 5, py + 8, Pal.S2);
            cv.Px(px + 6, py + 8, Pal.S1);
            cv.Px(px + 6, py + 9, Pal.S0);
            cv.Px(px + 9, py + 10, Pal.S1);
            cv.Px(px + 10, py + 10, Pal.S0);
        }
        else if (r < 0.58f)
        {
            // 枯叶
            for (var i = 0; i < 5; i++)
            {
                var lx = px + 3 + (int)(PixelCanvas.Rand(px, py, seed + i) * 10f);
                var ly = py + 4 + (int)(PixelCanvas.Rand(px, py, seed + 10 + i) * 9f);
                cv.Px(lx, ly, i % 2 == 0 ? Pal.L5 : Pal.D1);
            }
        }
        else if (r < 0.80f)
        {
            // 水洼
            cv.DiscShade(px + 8, py + 9, 3.6f, Pal.A1, 0.70f);
            cv.Px(px + 7, py + 8, Pal.A3);
            cv.Px(px + 9, py + 10, Pal.A2);
        }
        else
        {
            // 干草 / 碎屑
            for (var i = 0; i < 7; i++)
            {
                var lx = px + 2 + (int)(PixelCanvas.Rand(px, py, seed + i) * 12f);
                var ly = py + 5 + (int)(PixelCanvas.Rand(px, py, seed + 10 + i) * 8f);
                cv.Px(lx, ly, Pal.G1);
                cv.Px(lx, ly - 1, Pal.G2);
            }
        }
    }

    private static void Ash(PixelCanvas cv, int px, int py, int seed)
    {
        cv.DiscShade(px + 8, py + 10, 4.2f, Pal.S0, 0.60f);
        for (var i = 0; i < 9; i++)
        {
            var ax = px + 4 + (int)(PixelCanvas.Rand(px, py, seed + i) * 9f);
            var ay = py + 7 + (int)(PixelCanvas.Rand(px, py, seed + 10 + i) * 5f);
            cv.Px(ax, ay, PixelCanvas.Rand(px, py, seed + 20 + i) > 0.6f ? Pal.S0 : Pal.S1);
        }
        cv.Px(px + 6, py + 9, Pal.V0);
    }

    private static void Mat(PixelCanvas cv, int px, int py, int seed)
    {
        // 草席：编织纹
        cv.FillRect(px + 2, py + 4, 12, 10, Pal.Darken(Pal.D1, 0.18f));
        cv.HLine(px + 2, px + 13, py + 4, Pal.D1);

        for (var j = 5; j < 14; j += 2)
        {
            cv.HLine(px + 2, px + 13, py + j, Pal.Darken(Pal.D1, 0.34f));
            for (var i = 3; i < 14; i += 3)
                cv.Px(px + i, py + j, Pal.Lighten(Pal.D1, 0.10f));
        }

        // 卷起的一角
        cv.VLine(px + 13, py + 4, py + 13, Pal.Darken(Pal.D1, 0.45f));
    }

    // ————————————————————————— 活物 —————————————————————————

    private static void Beast(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 14, 0.34f);

        // 卧着的牲口：躯干 + 头 + 腿
        cv.Disc(px + 8, py + 9, 5.4f, Pal.Darken(Pal.D1, 0.30f));
        cv.Disc(px + 8, py + 8.4f, 4.4f, Pal.D1);

        // 头
        cv.Disc(px + 12.5f, py + 6.5f, 2.6f, Pal.D1);
        cv.Px(px + 13, py + 5, Pal.W1);       // 耳
        cv.Px(px + 14, py + 7, Pal.ShadowDeep); // 眼

        // 鬃毛 / 尾巴
        cv.HLine(px + 3, px + 5, py + 6, Pal.W1);
        cv.Px(px + 2, py + 7, Pal.W1);

        // 腿
        cv.VLine(px + 5, py + 12, py + 14, Pal.Darken(Pal.D1, 0.40f));
        cv.VLine(px + 10, py + 12, py + 14, Pal.Darken(Pal.D1, 0.40f));
    }

    private static void Pine(PixelCanvas cv, int px, int py, int seed)
    {
        cv.BlendRect(px + 4, py + 13, 8, 3, Pal.ShadowDeep, 0.30f);

        // 树干
        cv.VLine(px + 7, py + 8, py + 15, Pal.W1);
        cv.VLine(px + 8, py + 8, py + 15, Pal.W0);

        // 三层松针
        for (var i = 0; i < 3; i++)
        {
            var y = py + 2 + i * 4;
            var w = 2 + i * 2;
            cv.FillRect(px + 8 - w, y, w * 2 + 1, 3, Pal.L1);
            cv.HLine(px + 8 - w, px + 8 + w, y, Pal.L2);
            cv.Px(px + 8, y - 1, Pal.L3);
        }
    }

    private static void Bone(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 8, 0.22f);

        // 白骨：两根长骨 + 一个头骨
        cv.HLine(px + 3, px + 10, py + 8, Pal.P1);
        cv.Px(px + 2, py + 7, Pal.P2);
        cv.Px(px + 2, py + 9, Pal.P0);
        cv.Px(px + 11, py + 7, Pal.P2);
        cv.Px(px + 11, py + 9, Pal.P0);

        cv.HLine(px + 5, px + 12, py + 11, Pal.P0);
        cv.Px(px + 4, py + 10, Pal.P1);

        // 头骨
        cv.Disc(px + 8, py + 5, 2.6f, Pal.P2);
        cv.Px(px + 7, py + 5, Pal.ShadowDeep);
        cv.Px(px + 9, py + 5, Pal.ShadowDeep);
    }

    private static void Statue(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 12, 0.36f);

        // 底座
        Box(cv, px + 3, py + 12, 10, 3, Pal.S1, Pal.S0);

        // 立像
        cv.FillRect(px + 5, py + 4, 6, 8, Pal.S2);
        cv.VLine(px + 5, py + 4, py + 11, Pal.S3);
        cv.VLine(px + 10, py + 4, py + 11, Pal.S0);

        // 头
        cv.Disc(px + 8, py + 3, 2.2f, Pal.S3);
        cv.Px(px + 7, py + 3, Pal.S0);
        cv.Px(px + 9, py + 3, Pal.S0);

        // 断臂：只剩一边
        cv.VLine(px + 4, py + 5, py + 8, Pal.S2);
        cv.Px(px + 11, py + 6, Pal.S0);

        // 香火熏黑
        cv.BlendRect(px + 5, py + 9, 6, 3, Pal.ShadowDeep, 0.30f);
    }

    private static void Trough(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 14, 0.30f);

        // 木槽
        cv.FillRect(px + 1, py + 7, 14, 6, Pal.W1);
        cv.HLine(px + 1, px + 14, py + 7, Pal.W2);
        cv.HLine(px + 1, px + 14, py + 12, Pal.W0);

        // 槽内
        cv.FillRect(px + 2, py + 8, 12, 3, Pal.ShadowDeep);
        cv.HLine(px + 2, px + 13, py + 8, Pal.A1);

        // 槽脚
        cv.VLine(px + 3, py + 13, py + 15, Pal.W0);
        cv.VLine(px + 12, py + 13, py + 15, Pal.W0);
    }

    private static void Post(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 6, 0.26f);

        // 桩
        cv.FillRect(px + 6, py + 2, 4, 13, Pal.W1);
        cv.VLine(px + 6, py + 2, py + 14, Pal.W3);
        cv.VLine(px + 9, py + 2, py + 14, Pal.W0);
        cv.HLine(px + 6, px + 9, py + 2, Pal.W2);

        // 拴绳
        cv.HLine(px + 9, px + 14, py + 6, Pal.D1);
        cv.Px(px + 14, py + 7, Pal.D1);
    }

    // ————————————————————————— 大型 —————————————————————————

    private static void Well(PixelCanvas cv, int px, int py, int seed)
    {
        var cx = px + 8f;
        var cy = py + 8f;

        cv.DiscShade(cx + 1, cy + 2, 10f, Pal.ShadowDeep, 0.34f);

        cv.Disc(cx, cy, 9.6f, Pal.S0);
        cv.Disc(cx, cy - 0.6f, 8.6f, Pal.S1);
        cv.Disc(cx, cy - 1.0f, 7.2f, Pal.S2);

        // 井口
        cv.Disc(cx, cy - 1f, 5.4f, Pal.A0);
        cv.Disc(cx, cy - 1.2f, 4.4f, Pal.A1);
        cv.Px(px + 7, py + 6, Pal.A3);
        cv.Px(px + 8, py + 6, Pal.A3);

        // 井栏
        cv.VLine(px + 2, py + 2, py + 10, Pal.W1);
        cv.VLine(px + 13, py + 2, py + 10, Pal.W1);
        cv.HLine(px + 3, px + 12, py + 3, Pal.W3);
        cv.HLine(px + 3, px + 12, py + 4, Pal.W1);

        // 垂绳
        cv.Px(px + 8, py + 5, Pal.P0);
        cv.Px(px + 8, py + 6, Pal.P0);
    }

    private static void Banner(PixelCanvas cv, int px, int py, int seed)
    {
        var poleX = px + 8;

        for (var y = py - 22; y <= py + 15; y++)
        {
            cv.Px(poleX, y, Pal.W2);
            cv.Px(poleX + 1, y, Pal.W1);
        }

        for (var y = -19; y <= -2; y++)
        {
            var len = 6 - (y + 19) / 8;
            var wob = Mathf.RoundToInt(Mathf.Sin((y + 19) * 0.45f + seed) * 1.1f);
            for (var i = 0; i <= len; i++)
                cv.Px(poleX + 2 + i + wob, py + y, i == 0 ? Pal.P2 : i == len ? Pal.P0 : Pal.P1);
        }

        cv.Px(poleX + 4, py - 14, Pal.V1);
        cv.Px(poleX + 5, py - 14, Pal.V1);

        cv.BlendRect(poleX - 2, py + 13, 6, 2, Pal.ShadowDeep, 0.26f);
    }

    private static void Plow(PixelCanvas cv, int px, int py, int seed)
    {
        Foot(cv, px, py, 12, 0.24f);

        // 犁：斜插在地里的铁头 + 木柄
        cv.VLine(px + 4, py + 4, py + 12, Pal.W2);
        cv.Px(px + 5, py + 3, Pal.W3);
        cv.Px(px + 6, py + 4, Pal.W1);

        // 犁头
        cv.HLine(px + 6, px + 11, py + 11, Pal.S2);
        cv.HLine(px + 7, px + 12, py + 12, Pal.S1);
        cv.Px(px + 12, py + 11, Pal.S3);

        // 翻起的土
        cv.Px(px + 9, py + 9, Pal.D0);
        cv.Px(px + 11, py + 9, Pal.D1);
        cv.Px(px + 12, py + 10, Pal.D0);
    }

    private static void Grave(PixelCanvas cv, int px, int py, int seed)
    {
        var cx = px + 8f;

        cv.DiscShade(cx + 1, py + 14f, 9f, Pal.ShadowDeep, 0.40f);
        cv.Disc(cx, py + 12f, 7.2f, Pal.Darken(Pal.D1, 0.24f));
        cv.Disc(cx, py + 11.4f, 6.2f, Pal.D1);
        cv.Disc(cx - 0.6f, py + 10.8f, 4.8f, Pal.Lighten(Pal.D1, 0.10f));

        // 石碑
        cv.FillRect(px + 5, py + 1, 6, 11, Pal.S1);
        cv.FillRect(px + 5, py + 1, 2, 11, Pal.S2);
        cv.FillRect(px + 10, py + 1, 1, 11, Pal.S0);
        cv.HLine(px + 5, px + 10, py + 1, Pal.S3);
        cv.Px(px + 6, py + 0, Pal.S3);

        // 碑文
        cv.Blend(px + 7, py + 4, Pal.S3, 0.55f);
        cv.Blend(px + 8, py + 6, Pal.S0, 0.70f);
        cv.Blend(px + 7, py + 8, Pal.S0, 0.50f);

        cv.FillRect(px + 4, py + 11, 8, 2, Pal.S0);
    }

    private static void Rock(PixelCanvas cv, int px, int py, int seed)
    {
        cv.DiscShade(px + 8, py + 12, 6.4f, Pal.ShadowDeep, 0.30f);
        cv.Disc(px + 8, py + 9, 5.2f, Pal.S1);
        cv.Disc(px + 7.2f, py + 8.2f, 3.8f, Pal.S2);
        cv.Disc(px + 6.4f, py + 7.4f, 2.0f, Pal.S3);
        cv.Px(px + 11, py + 11, Pal.S0);
    }

    private static void Rope(PixelCanvas cv, int px, int py, int seed)
    {
        // 晾衣绳：两根桩 + 一条下垂的绳
        cv.VLine(px + 1, py + 4, py + 14, Pal.W1);
        cv.VLine(px + 14, py + 4, py + 14, Pal.W1);

        for (var i = 1; i < 15; i++)
        {
            var sag = Mathf.RoundToInt(Mathf.Sin(i / 15f * Mathf.Pi) * 2.2f);
            cv.Px(px + i, py + 4 + sag, Pal.D1);
        }

        // 挂着的布
        cv.FillRect(px + 5, py + 6, 4, 5, Pal.P1);
        cv.VLine(px + 5, py + 6, py + 10, Pal.P2);
        cv.Px(px + 6, py + 11, Pal.P0);
    }
}
