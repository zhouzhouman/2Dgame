using System.Collections.Generic;
using Godot;

namespace Juhuo.Art;

/// <summary>
/// 角色行走图。程序化生成一张 64×96 的图集：4 帧 × 4 方向，每格 16×24。
///
/// 为什么自己做而不是用现成素材：中式衣冠的 16px 角色在免费库里几乎没有，
/// 而这套骨架只要换三个颜色就能派生出全部 NPC——衙役、文人、游医、病人。
/// 素材到位后，只要把 Sheet() 换成读 PNG，调用方一行都不用改。
/// </summary>
public static class ActorArt
{
    public const int CellW = 16;
    public const int CellH = 24;
    public const int Frames = 4;

    // 方向：0 下 1 左 2 右 3 上
    public const int Down = 0;
    public const int Left = 1;
    public const int Right = 2;
    public const int Up = 3;

    private static readonly Dictionary<string, ImageTexture> Cache = new();

    private sealed record Look(Color Coat, Color Dark, Color Trim, Color Hair, bool IsSick);

    public static ImageTexture Sheet(string kind)
    {
        if (Cache.TryGetValue(kind, out var cached)) return cached;

        var look = LookOf(kind);
        var cv = new PixelCanvas(CellW * Frames, CellH * 4);

        for (var dir = 0; dir < 4; dir++)
        for (var frame = 0; frame < Frames; frame++)
            Draw(cv, frame * CellW, dir * CellH, dir, frame, look);

        var tex = cv.ToTexture();
        Cache[kind] = tex;
        return tex;
    }

    /// <summary>图集里某一帧的矩形。给 DrawTextureRectRegion 用。</summary>
    public static Rect2 Src(int dir, int frame)
        => new(frame * CellW, dir * CellH, CellW, CellH);

    private static Look LookOf(string kind) => kind switch
    {
        "chen" => new(Rgb(0x6A6250), Rgb(0x474234), Rgb(0x8E2B22), Rgb(0x1A140F), false),
        "old" => new(Rgb(0x4C565E), Rgb(0x333B42), Rgb(0xA79A72), Rgb(0x6F6A60), false),
        "doctor" => new(Rgb(0x6E6440), Rgb(0x4A422A), Rgb(0x8E2B22), Rgb(0x241B12), false),
        "yamen" => new(Rgb(0x1C1A1A), Rgb(0x111010), Rgb(0xB03028), Rgb(0x0F0C0A), false),
        "sick" => new(Rgb(0x5E5A50), Rgb(0x403C34), Rgb(0x6B6455), Rgb(0x1A140F), true),
        "player" => new(Rgb(0x4E5F63), Rgb(0x33403F), Rgb(0x8E2B22), Rgb(0x1A140F), false),
        _ => new(Rgb(0x5A5244), Rgb(0x3C362C), Rgb(0x8E2B22), Rgb(0x241B12), false),
    };

    private static Color Rgb(int v) => new(
        (v >> 16 & 0xFF) / 255f, (v >> 8 & 0xFF) / 255f, (v & 0xFF) / 255f);

    // ————————————————————————— 画法 —————————————————————————

    private static void Draw(PixelCanvas cv, int ax, int ay, int dir, int frame, Look look)
    {
        var skin = new Color(0.53f, 0.38f, 0.25f);
        var skinLit = new Color(0.62f, 0.45f, 0.31f);
        var cx = ax + 8;

        // 帧 1 迈左腿，帧 3 迈右腿；0/2 站立
        var swing = frame switch { 1 => -1, 3 => 1, _ => 0 };

        // 影子
        cv.BlendRect(ax + 3, ay + 22, 10, 2, Pal.ShadowDeep, 0.34f);

        // —— 腿 ——
        if (dir is Left or Right)
        {
            // 侧面：两腿前后错开
            cv.FillRect(cx - 1 + swing, ay + 18, 2, 4, look.Dark);
            cv.FillRect(cx + 1 - swing, ay + 18, 2, 4, look.Dark.Darkened(0.2f));
        }
        else
        {
            cv.FillRect(cx - 3, ay + 18 + (swing < 0 ? 1 : 0), 2, 4, look.Dark);
            cv.FillRect(cx + 1, ay + 18 + (swing > 0 ? 1 : 0), 2, 4, look.Dark.Darkened(0.2f));
        }

        // —— 身体 ——
        cv.FillRect(cx - 4, ay + 9, 8, 9, look.Coat);
        cv.HLine(cx - 4, cx + 3, ay + 9, look.Coat.Lightened(0.14f));
        cv.VLine(cx + 3, ay + 9, ay + 17, look.Dark);

        // 腰带：在腰上，不在胯上，否则看着像红裤子
        cv.HLine(cx - 4, cx + 3, ay + 14, look.Trim);

        // 交领
        if (dir == Down)
        {
            cv.Px(cx - 1, ay + 9, look.Dark);
            cv.Px(cx, ay + 10, look.Dark);
            cv.Px(cx + 1, ay + 11, look.Dark);
        }

        // —— 头 ——
        var headY = ay + 2;
        cv.FillRect(cx - 3, headY, 6, 7, skin);

        switch (dir)
        {
            case Down:
                // 正面：头发盖住上两行，两侧鬓角
                cv.FillRect(cx - 3, headY, 6, 2, look.Hair);
                cv.VLine(cx - 3, headY + 2, headY + 4, look.Hair);
                cv.VLine(cx + 2, headY + 2, headY + 4, look.Hair);
                cv.HLine(cx - 2, cx + 1, headY + 2, skinLit);
                cv.Px(cx - 1, headY + 5, look.Hair);
                cv.Px(cx + 1, headY + 5, look.Hair);
                break;

            case Up:
                // 背面：整个头都是头发，中间一道发缝
                cv.FillRect(cx - 3, headY, 6, 5, look.Hair);
                cv.VLine(cx, headY, headY + 3, look.Hair.Lightened(0.12f));
                break;

            case Left:
                // 侧脸朝左：右半边是头发，左边缘露一点脸
                cv.FillRect(cx - 3, headY, 6, 2, look.Hair);
                cv.FillRect(cx, headY + 2, 3, 4, look.Hair);
                cv.VLine(cx - 3, headY + 2, headY + 4, look.Hair);
                cv.Px(cx - 2, headY + 4, look.Hair);
                cv.Px(cx - 3, headY + 3, skinLit);
                break;

            case Right:
                cv.FillRect(cx - 3, headY, 6, 2, look.Hair);
                cv.FillRect(cx - 3, headY + 2, 3, 4, look.Hair);
                cv.VLine(cx + 2, headY + 2, headY + 4, look.Hair);
                cv.Px(cx + 1, headY + 4, look.Hair);
                cv.Px(cx + 2, headY + 3, skinLit);
                break;
        }

        // 病人：脸色发灰，眼睛闭上
        if (look.IsSick)
        {
            cv.BlendRect(cx - 3, headY + 2, 6, 5, new Color(0.25f, 0.28f, 0.26f), 0.35f);
            cv.Px(cx - 1, headY + 4, new Color(0.20f, 0.17f, 0.14f));
            cv.Px(cx + 1, headY + 4, new Color(0.20f, 0.17f, 0.14f));
        }

        // 颔下阴影
        cv.HLine(cx - 3, cx + 2, headY + 7, skin.Darkened(0.25f));
    }
}
