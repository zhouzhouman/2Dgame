using Godot;
using Juhuo.Application;
using Juhuo.Art;
using Juhuo.Core;

namespace Juhuo;

/// <summary>
/// 一张立绘：16×16 的像素半身像，按 12 倍放大成 192×192（对话里用 8 倍、128×128）。
///
/// 这里只做三件事：铺一块底、把 <see cref="PortraitArt.Build"/> 给的矩形按倍数画出来、
/// 把颜料名换成 <see cref="Pal"/> 里的颜色。构图与"哪个参数落在哪一格"全在 Application，
/// 因为那部分要能被单测逐条问；这一层只是笔。
/// </summary>
public partial class PortraitView : Control
{
    private Portrait? _portrait;
    private int _pixel = PortraitArt.BigScale;

    /// <summary>现在画的是谁；null = 空板（淡的一层底，不出人）。</summary>
    public Portrait? Portrait => _portrait;

    /// <summary>放大倍数：192 用 12、128 用 8。</summary>
    public int PixelScale => _pixel;

    /// <summary>
    /// 换一张脸。尺寸跟着 <paramref name="pixel"/> 走：192 用 12、128 用 8。
    /// </summary>
    public void Show(Portrait? portrait, int pixel = PortraitArt.BigScale)
    {
        _portrait = portrait;
        _pixel = Mathf.Max(1, pixel);

        var side = new Vector2(PortraitArt.Grid * _pixel, PortraitArt.Grid * _pixel);
        CustomMinimumSize = side;
        Size = side;

        Visible = portrait is not null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var side = PortraitArt.Grid * _pixel;

        // 底板：袍子多是深色，没有这块底，暗处的人会糊进背景里
        DrawRect(new Rect2(0, 0, side, side), new Color(0.055f, 0.062f, 0.060f, 0.94f));

        // 一圈细边：和铭牌、对话框是同一个记号
        var edge = new Color(0.42f, 0.36f, 0.26f, 0.75f);
        DrawRect(new Rect2(0, 0, side, 1), edge);
        DrawRect(new Rect2(0, side - 1, side, 1), edge);
        DrawRect(new Rect2(0, 0, 1, side), edge);
        DrawRect(new Rect2(side - 1, 0, 1, side), edge);

        if (_portrait is null) return;

        foreach (var part in PortraitArt.Build(_portrait))
        {
            DrawRect(
                new Rect2(part.X * _pixel, part.Y * _pixel, part.W * _pixel, part.H * _pixel),
                Ink(part.Tone, _portrait));
        }
    }

    /// <summary>颜料名 → 调色板。四个影调都是 <see cref="Pal"/> 的派生色，不另起色相。</summary>
    private static Color Ink(PortraitTone tone, Portrait p)
    {
        var skin = SkinOf(p.Skin);
        var hair = HairOf(p.Hair);
        var robe = RobeOf(p.Robe);
        var hat = HatOf(p.Hat);

        return tone switch
        {
            // 1 = 苍白，4 = 深肤：表里写"面无血色"的是 1、"深肤"的是 4。
            PortraitTone.Skin1 => Pal.Lighten(Pal.Skin2, 0.14f),
            PortraitTone.Skin2 => Pal.Skin2,
            PortraitTone.Skin3 => Pal.Skin1,
            PortraitTone.Skin4 => Pal.Skin0,
            PortraitTone.SkinShade => Pal.Darken(skin, 0.24f, 0.45f),
            PortraitTone.SkinLight => Pal.Lighten(skin, 0.16f),

            PortraitTone.Hair1 => Pal.Hair0,
            PortraitTone.Hair2 => Pal.Hair1,
            PortraitTone.Hair3 => Pal.W3,
            PortraitTone.Hair4 => Pal.S2,
            PortraitTone.Hair5 => Pal.N1,
            PortraitTone.Hair6 => Pal.N3,
            PortraitTone.HairShade => Pal.Darken(hair, 0.34f, 0.20f),

            PortraitTone.Robe1 => Pal.W3,
            PortraitTone.Robe2 => Pal.S1,
            PortraitTone.Robe3 => Pal.A3,
            PortraitTone.Robe4 => Pal.A2,
            PortraitTone.Robe5 => Pal.V1,
            PortraitTone.Robe6 => Pal.V2,
            PortraitTone.RobeShade => Pal.Darken(robe, 0.30f, 0.30f),
            PortraitTone.RobeLight => Pal.Lighten(robe, 0.16f),

            PortraitTone.Ink => Pal.W0,
            PortraitTone.Hat => hat,
            PortraitTone.HatShade => Pal.Darken(hat, 0.28f, 0.20f),
            PortraitTone.Accent => Pal.V2,
            _ => Pal.ShadowDeep,
        };
    }

    private static Color SkinOf(int skin) => skin switch
    {
        1 => Pal.Lighten(Pal.Skin2, 0.14f),
        2 => Pal.Skin2,
        3 => Pal.Skin1,
        _ => Pal.Skin0,
    };

    private static Color HairOf(int hair) => hair switch
    {
        1 => Pal.Hair0,
        2 => Pal.Hair1,
        3 => Pal.W3,
        4 => Pal.S2,
        5 => Pal.N1,
        _ => Pal.N3,
    };

    /// <summary>六档衣色：褐 / 灰 / 青 / 靛 / 赭 / 朱。</summary>
    private static Color RobeOf(int robe) => robe switch
    {
        1 => Pal.W3,
        2 => Pal.S1,
        3 => Pal.A3,
        4 => Pal.A2,
        5 => Pal.V1,
        _ => Pal.V2,
    };

    /// <summary>帽色按种类走：斗笠是草黄，方巾与头巾是布，官帽是乌纱。</summary>
    private static Color HatOf(int hat) => hat switch
    {
        1 => Pal.Y3,
        2 => Pal.T2,
        3 => Pal.T0,
        4 => Pal.R2,
        _ => Pal.S2,
    };
}
