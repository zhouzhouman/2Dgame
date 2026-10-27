using Godot;

namespace Juhuo.Art;

/// <summary>
/// 序章调色板。全部颜色集中在这里，改一处全图变。
///
/// 基调：阴天，深秋，荒村。
/// 三条硬规则：
///   1. 草偏绿、路偏红、田偏黑——靠色相区分材质，不靠亮度；
///   2. 每个材质只有 4~5 阶，多一阶就脏；
///   3. 暗部压向冷青，高光提向暖黄——这是画面"不闷"的唯一来源。
/// </summary>
public static class Pal
{
    private static Color C(string hex) => Color.FromHtml(hex);

    // —— 枯草（橄榄绿） ——
    public static readonly Color G0 = C("#414A22");
    public static readonly Color G1 = C("#525E2A");
    public static readonly Color G2 = C("#667236");
    public static readonly Color G3 = C("#7C8A41");
    public static readonly Color G4 = C("#93A24D");

    // —— 秃土斑 ——
    public static readonly Color D0 = C("#544731");
    public static readonly Color D1 = C("#68583E");

    // —— 土路（灰褐，红多于绿） ——
    public static readonly Color R0 = C("#4C4234");
    public static readonly Color R1 = C("#615546");
    public static readonly Color R2 = C("#786A57");
    public static readonly Color R3 = C("#90806A");
    public static readonly Color R4 = C("#A79780");

    // —— 荒田（更暗更褐，犁沟很深） ——
    public static readonly Color F0 = C("#3B2F1D");
    public static readonly Color F1 = C("#4D3E27");
    public static readonly Color F2 = C("#604E33");
    public static readonly Color F3 = C("#755F41");
    public static readonly Color F4 = C("#7E7439");   // 枯苗

    // —— 室内夯土（暗暖褐） ——
    public static readonly Color H0 = C("#453928");
    public static readonly Color H1 = C("#564934");
    public static readonly Color H2 = C("#695A43");
    public static readonly Color H3 = C("#7C6C52");

    // —— 青瓦 ——
    public static readonly Color T0 = C("#1A2021");
    public static readonly Color T1 = C("#252D2E");
    public static readonly Color T2 = C("#323C3D");
    public static readonly Color T3 = C("#404C4D");
    public static readonly Color T4 = C("#4F5C5D");
    public static readonly Color T5 = C("#616F70");

    // —— 木 ——
    public static readonly Color W0 = C("#241A10");
    public static readonly Color W1 = C("#352718");
    public static readonly Color W2 = C("#473521");
    public static readonly Color W3 = C("#5A432B");

    // —— 水 ——
    public static readonly Color A0 = C("#0B1518");
    public static readonly Color A1 = C("#122428");
    public static readonly Color A2 = C("#1B343A");
    public static readonly Color A3 = C("#2C4C52");

    // —— 树冠 ——
    public static readonly Color L0 = C("#1C2416");
    public static readonly Color L1 = C("#26321B");
    public static readonly Color L2 = C("#324223");
    public static readonly Color L3 = C("#40522C");
    public static readonly Color L4 = C("#506438");
    public static readonly Color L5 = C("#6E6531");   // 黄叶

    // —— 秋叶（深秋的第二个色相，全图靠它破单调） ——
    public static readonly Color Y0 = C("#3E2E14");
    public static readonly Color Y1 = C("#5C451C");
    public static readonly Color Y2 = C("#7C5F26");
    public static readonly Color Y3 = C("#9C7A31");
    public static readonly Color Y4 = C("#BC983F");

    // —— 雪（冷灰白，R≈G≈B 且略偏青，提亮灰褐是调不出这个的） ——
    public static readonly Color N0 = C("#767C79");
    public static readonly Color N1 = C("#8E9691");
    public static readonly Color N2 = C("#A8B0AA");
    public static readonly Color N3 = C("#C2CAC3");
    public static readonly Color N4 = C("#DAE2DA");

    // —— 石（灰褐，不许发紫） ——
    public static readonly Color S0 = C("#33302A");
    public static readonly Color S1 = C("#474238");
    public static readonly Color S2 = C("#5E584B");
    public static readonly Color S3 = C("#776F5F");

    // —— 朱砂（点缀，全图唯一的红） ——
    public static readonly Color V0 = C("#5E1813");
    public static readonly Color V1 = C("#82261C");
    public static readonly Color V2 = C("#A03B2D");

    // —— 白幡（全图最亮的东西，所以少用） ——
    public static readonly Color P0 = C("#8E8878");
    public static readonly Color P1 = C("#B5AD99");
    public static readonly Color P2 = C("#D9D0BA");

    // —— 人 ——
    public static readonly Color Skin0 = C("#6E4C33");
    public static readonly Color Skin1 = C("#87603F");
    public static readonly Color Skin2 = C("#9E7551");
    public static readonly Color Hair0 = C("#14100B");
    public static readonly Color Hair1 = C("#241B12");

    // —— 阴影（永远偏冷） ——
    public static readonly Color Shadow = new(0.035f, 0.065f, 0.085f);
    public static readonly Color ShadowDeep = new(0.020f, 0.040f, 0.055f);

    /// <summary>压暗并偏向冷色，用于阴影、沟壑、背光面。</summary>
    public static Color Darken(Color c, float amount, float cool = 0f)
    {
        return new Color(
            c.R * (1f - amount),
            c.G * (1f - amount) * (1f - cool * 0.30f),
            c.B * (1f - amount) * (1f + cool * 0.10f),
            c.A);
    }

    /// <summary>提亮并偏向暖色，用于受光面。</summary>
    public static Color Lighten(Color c, float amount)
    {
        return new Color(
            Mathf.Min(1f, c.R * (1f + amount * 1.20f)),
            Mathf.Min(1f, c.G * (1f + amount * 1.08f)),
            Mathf.Min(1f, c.B * (1f + amount * 0.80f)),
            c.A);
    }

    public static Color Mix(Color a, Color b, float t) => a.Lerp(b, t);
}
