using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 立绘上的一层颜料。表现层拿它换调色板里的颜色；
/// 这里只写"哪一块用哪种料"，不碰 Godot 的 Color。
///
/// 名字里的数字就是 portraits.csv 的那一档：Skin1..4 对应 skin、
/// Hair1..6 对应 hair、Robe1..6 对应 robe、Hat 对 hat（具体色相由帽子种类定）。
/// </summary>
public enum PortraitTone
{
    Skin1,
    Skin2,
    Skin3,
    Skin4,
    SkinShade,
    SkinLight,

    Hair1,
    Hair2,
    Hair3,
    Hair4,
    Hair5,
    Hair6,
    HairShade,

    Robe1,
    Robe2,
    Robe3,
    Robe4,
    Robe5,
    Robe6,
    RobeShade,
    RobeLight,

    Ink,
    Hat,
    HatShade,
    Accent,
    Outline,
}

/// <summary>一条绘制指令：逻辑格坐标上的一块矩形 + 一种颜料。</summary>
/// <param name="X">左边界（0–15）。</param>
/// <param name="Y">上边界（0–15）。</param>
/// <param name="W">宽（格）。</param>
/// <param name="H">高（格）。</param>
/// <param name="Tone">这一块用的颜料。</param>
public readonly record struct PortraitPart(int X, int Y, int W, int H, PortraitTone Tone);

/// <summary>
/// 把 <see cref="Portrait"/> 的七个数字翻成一串矩形指令——一张 16×16 的像素半身像。
///
/// 为什么在这里而不是在 Godot 那一层：**每个参数都必须真的画出来**。
/// 只有把"参数 → 像素块"这件事做成纯 C# 的一张清单，单测才能逐条断言
/// "皮肤从 1 改到 2，脸上那块的颜色真的换了"。画的时候只是照着清单 DrawRect。
///
/// 版面（16×16）：
/// <code>
///   y0..3   帽 / 顶髻
///   y3..9   头（x5..10），脸心 x6..9
///   y10     脖子
///   y11..15 肩与袍
/// </code>
/// 七个参数各占一块互不重叠的地盘：肤色=脸、发色=发、发型=头两侧与顶上、
/// 胡须=下巴以下、帽=额上、袍=肩以下、记号=脸心。
/// </summary>
public static class PortraitArt
{
    /// <summary>逻辑格数（宽 = 高）。</summary>
    public const int Grid = 16;

    /// <summary>开场立绘的边长（像素）。</summary>
    public const int BigPixels = 192;

    /// <summary>对话里小立绘的边长（像素）。</summary>
    public const int SmallPixels = 128;

    /// <summary>192 / 16。</summary>
    public const int BigScale = BigPixels / Grid;

    /// <summary>128 / 16。</summary>
    public const int SmallScale = SmallPixels / Grid;

    /// <summary>肤色档 → 颜料名。</summary>
    public static PortraitTone SkinTone(int skin) => skin switch
    {
        1 => PortraitTone.Skin1,
        2 => PortraitTone.Skin2,
        3 => PortraitTone.Skin3,
        _ => PortraitTone.Skin4,
    };

    /// <summary>发色档 → 颜料名。</summary>
    public static PortraitTone HairTone(int hair) => hair switch
    {
        1 => PortraitTone.Hair1,
        2 => PortraitTone.Hair2,
        3 => PortraitTone.Hair3,
        4 => PortraitTone.Hair4,
        5 => PortraitTone.Hair5,
        _ => PortraitTone.Hair6,
    };

    /// <summary>衣色档 → 颜料名。</summary>
    public static PortraitTone RobeTone(int robe) => robe switch
    {
        1 => PortraitTone.Robe1,
        2 => PortraitTone.Robe2,
        3 => PortraitTone.Robe3,
        4 => PortraitTone.Robe4,
        5 => PortraitTone.Robe5,
        _ => PortraitTone.Robe6,
    };

    /// <summary>
    /// 按七个参数拼出整张图，顺序就是绘制顺序（后画的压住先画的）。
    /// 参数越界时按最近的一档画，不抛——表已经在校验层挡过了，
    /// 这里再抛只会让一个写错的数字变成整局黑屏。
    /// </summary>
    /// <param name="p">这个人的立绘参数。</param>
    /// <returns>按绘制顺序排好的矩形指令。</returns>
    public static IReadOnlyList<PortraitPart> Build(Portrait p)
    {
        if (p is null) throw new ArgumentNullException(nameof(p));

        var parts = new List<PortraitPart>(96);

        var skin = SkinTone(p.Skin);
        var hair = HairTone(p.Hair);
        var robe = RobeTone(p.Robe);

        void R(int x, int y, int w, int h, PortraitTone tone) =>
            parts.Add(new PortraitPart(x, y, w, h, tone));

        // —— 头的外轮廓：先铺一层暗，头再压在上面，四周就留下 1 格描边 ——
        R(4, 2, 8, 9, PortraitTone.Outline);

        // —— 肩与袍（y11..15）——
        R(4, 11, 8, 1, robe);
        R(3, 12, 10, 1, robe);
        R(2, 13, 12, 1, robe);
        R(2, 14, 12, 1, robe);
        R(2, 15, 12, 1, robe);

        // 右肩背光、左肩受光：没有这两笔，整件袍子是一块死色
        R(10, 12, 3, 1, PortraitTone.RobeShade);
        R(11, 13, 3, 1, PortraitTone.RobeShade);
        R(11, 14, 3, 1, PortraitTone.RobeShade);
        R(11, 15, 3, 1, PortraitTone.RobeShade);
        R(4, 11, 2, 1, PortraitTone.RobeLight);
        R(3, 12, 2, 1, PortraitTone.RobeLight);

        // 腰带
        R(2, 14, 12, 1, PortraitTone.Ink);

        // —— 脖子 ——
        R(7, 10, 2, 1, skin);

        // —— 头（x5..10 / y3..9）——
        R(5, 3, 6, 7, skin);
        R(4, 6, 1, 1, skin);    // 左耳
        R(11, 6, 1, 1, skin);   // 右耳

        // 脸上的一点明暗：额头偏亮、右颊偏暗
        R(6, 5, 3, 1, PortraitTone.SkinLight);
        R(9, 7, 1, 2, PortraitTone.SkinShade);
        R(7, 7, 1, 1, PortraitTone.SkinShade);

        // 眼与口
        R(6, 6, 1, 1, PortraitTone.Ink);
        R(8, 6, 1, 1, PortraitTone.Ink);
        R(7, 8, 1, 1, PortraitTone.Ink);

        // —— 发：额顶永远是头发，发型决定两侧与顶上多出什么 ——
        R(5, 3, 6, 1, hair);
        R(5, 4, 6, 1, hair);

        switch (p.Style)
        {
            case 2: // 披散：两侧垂到肩上
                R(5, 5, 1, 5, hair);
                R(10, 5, 1, 5, hair);
                R(4, 7, 1, 5, hair);
                R(11, 7, 1, 5, hair);
                R(3, 10, 1, 3, PortraitTone.HairShade);
                R(12, 10, 1, 3, PortraitTone.HairShade);
                break;

            case 3: // 短：只到耳上
                R(5, 5, 1, 2, hair);
                R(10, 5, 1, 2, hair);
                break;

            case 4: // 包头：厚刘海 + 包住两颊
                R(5, 4, 6, 2, hair);
                R(4, 5, 1, 4, hair);
                R(11, 5, 1, 4, hair);
                R(4, 4, 1, 1, PortraitTone.HairShade);
                R(11, 4, 1, 1, PortraitTone.HairShade);
                break;

            default: // 1 束发：两鬓 + 顶上一个髻
                R(5, 5, 1, 4, hair);
                R(10, 5, 1, 4, hair);
                R(7, 1, 2, 1, hair);
                R(7, 2, 2, 1, hair);
                break;
        }

        // —— 胡须：全用发色，所以"白头"连胡子一起白 ——
        switch (p.Beard)
        {
            case 1: // 短须
                R(6, 9, 4, 1, hair);
                break;

            case 2: // 长须：垂到胸前
                R(6, 9, 4, 1, hair);
                R(6, 10, 4, 1, hair);
                R(7, 11, 2, 1, PortraitTone.HairShade);
                R(7, 12, 2, 1, PortraitTone.HairShade);
                break;

            case 3: // 络腮：从两鬓连到下巴
                R(4, 8, 2, 1, hair);
                R(10, 8, 2, 1, hair);
                R(5, 9, 6, 1, hair);
                R(4, 7, 1, 1, hair);
                R(11, 7, 1, 1, hair);
                break;
        }

        // —— 帽（盖在头发上；0 = 不戴）——
        switch (p.Hat)
        {
            case 1: // 斗笠：宽檐
                R(6, 0, 4, 1, PortraitTone.Hat);
                R(5, 1, 6, 1, PortraitTone.Hat);
                R(2, 2, 12, 1, PortraitTone.Hat);
                R(1, 3, 2, 1, PortraitTone.HatShade);
                R(13, 3, 2, 1, PortraitTone.HatShade);
                break;

            case 2: // 方巾
                R(5, 1, 6, 1, PortraitTone.Hat);
                R(4, 2, 8, 1, PortraitTone.Hat);
                R(4, 3, 8, 1, PortraitTone.HatShade);
                break;

            case 3: // 官帽：两侧展脚
                R(5, 1, 6, 1, PortraitTone.Hat);
                R(4, 2, 8, 1, PortraitTone.Hat);
                R(1, 2, 3, 1, PortraitTone.HatShade);
                R(12, 2, 3, 1, PortraitTone.HatShade);
                R(4, 3, 8, 1, PortraitTone.HatShade);
                break;

            case 4: // 头巾：一道缠头，右侧打个结
                R(4, 2, 8, 1, PortraitTone.Hat);
                R(4, 3, 8, 1, PortraitTone.HatShade);
                R(11, 3, 1, 3, PortraitTone.Hat);
                R(12, 4, 1, 3, PortraitTone.HatShade);
                break;
        }

        // —— 记号（最后画，压在所有东西上）——
        switch (p.Mark)
        {
            case 1: // 疤：右颊一道竖的
                R(9, 7, 1, 2, PortraitTone.Accent);
                break;

            case 2: // 痣
                R(9, 8, 1, 1, PortraitTone.Accent);
                break;

            case 3: // 眼罩：压住右眼，梢上一道带子
                R(8, 6, 2, 2, PortraitTone.Ink);
                R(5, 5, 6, 1, PortraitTone.Ink);
                break;

            case 4: // 皱纹：额上一道、眼下两道
                R(6, 5, 3, 1, PortraitTone.SkinShade);
                R(6, 7, 1, 1, PortraitTone.SkinShade);
                R(9, 7, 1, 1, PortraitTone.SkinShade);
                break;
        }

        return parts;
    }
}
