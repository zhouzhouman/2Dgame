using System;
using Godot;

namespace Juhuo.Art;

/// <summary>
/// 一块可以直接逐像素操作的画布。
/// 内部用 byte[] 而不是 Image.SetPixel，避免每像素一次引擎调用。
/// </summary>
public sealed class PixelCanvas
{
    public readonly int W;
    public readonly int H;

    private readonly byte[] _px;

    public PixelCanvas(int w, int h)
    {
        W = w;
        H = h;
        _px = new byte[w * h * 4];
    }

    public bool In(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H;

    private static byte B(float v) => (byte)Mathf.Clamp(v * 255f + 0.5f, 0f, 255f);

    public void Px(int x, int y, Color c)
    {
        if (!In(x, y)) return;
        var i = (y * W + x) << 2;
        _px[i] = B(c.R);
        _px[i + 1] = B(c.G);
        _px[i + 2] = B(c.B);
        _px[i + 3] = B(c.A);
    }

    public Color Get(int x, int y)
    {
        if (!In(x, y)) return new Color(0, 0, 0, 0);
        var i = (y * W + x) << 2;
        return new Color(_px[i] / 255f, _px[i + 1] / 255f, _px[i + 2] / 255f, _px[i + 3] / 255f);
    }

    /// <summary>alpha 混合写入。阴影、雾、光都用它。</summary>
    public void Blend(int x, int y, Color c, float a)
    {
        if (!In(x, y) || a <= 0f) return;
        var i = (y * W + x) << 2;

        var sa = Mathf.Clamp(a * c.A, 0f, 1f);
        var da = _px[i + 3] / 255f;
        var oa = sa + da * (1f - sa);
        if (oa <= 0.0001f) return;

        _px[i] = B((c.R * sa + (_px[i] / 255f) * da * (1f - sa)) / oa);
        _px[i + 1] = B((c.G * sa + (_px[i + 1] / 255f) * da * (1f - sa)) / oa);
        _px[i + 2] = B((c.B * sa + (_px[i + 2] / 255f) * da * (1f - sa)) / oa);
        _px[i + 3] = B(oa);
    }

    /// <summary>直接在当前像素上做乘法（用于光照烘焙）。</summary>
    public void Mul(int x, int y, float r, float g, float b)
    {
        if (!In(x, y)) return;
        var i = (y * W + x) << 2;
        if (_px[i + 3] == 0) return;
        _px[i] = B(_px[i] / 255f * r);
        _px[i + 1] = B(_px[i + 1] / 255f * g);
        _px[i + 2] = B(_px[i + 2] / 255f * b);
    }

    public void Fill(Color c)
    {
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            Px(x, y, c);
    }

    public void FillRect(int x, int y, int w, int h, Color c)
    {
        for (var j = 0; j < h; j++)
        for (var i = 0; i < w; i++)
            Px(x + i, y + j, c);
    }

    public void BlendRect(int x, int y, int w, int h, Color c, float a)
    {
        for (var j = 0; j < h; j++)
        for (var i = 0; i < w; i++)
            Blend(x + i, y + j, c, a);
    }

    public void HLine(int x0, int x1, int y, Color c)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        for (var x = x0; x <= x1; x++) Px(x, y, c);
    }

    public void VLine(int x, int y0, int y1, Color c)
    {
        if (y1 < y0) (y0, y1) = (y1, y0);
        for (var y = y0; y <= y1; y++) Px(x, y, c);
    }

    /// <summary>实心圆。用于树冠、土堆、阴影。</summary>
    public void Disc(float cx, float cy, float r, Color c)
    {
        var x0 = Mathf.FloorToInt(cx - r);
        var x1 = Mathf.CeilToInt(cx + r);
        var y0 = Mathf.FloorToInt(cy - r);
        var y1 = Mathf.CeilToInt(cy + r);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var dx = x + 0.5f - cx;
            var dy = y + 0.5f - cy;
            if (dx * dx + dy * dy <= r * r) Px(x, y, c);
        }
    }

    public void DiscShade(float cx, float cy, float r, Color c, float a)
    {
        var x0 = Mathf.FloorToInt(cx - r);
        var x1 = Mathf.CeilToInt(cx + r);
        var y0 = Mathf.FloorToInt(cy - r);
        var y1 = Mathf.CeilToInt(cy + r);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var dx = x + 0.5f - cx;
            var dy = y + 0.5f - cy;
            var d2 = dx * dx + dy * dy;
            if (d2 > r * r) continue;
            var edge = 1f - Mathf.Sqrt(d2) / r;
            Blend(x, y, c, a * Mathf.Clamp(edge * 2.2f, 0f, 1f));
        }
    }

    // —————————————————————————— 噪声 ——————————————————————————

    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    /// <summary>确定性随机，0..1。同一个 (x,y,seed) 永远同一值——地图每次打开都一样。</summary>
    public static float Rand(int x, int y, int seed) => Hash(x, y, seed) / 4294967295f;

    public static float Rand(int x, int y) => Rand(x, y, 0);

    /// <summary>双线性插值的值噪声。</summary>
    public static float Noise(float x, float y, int seed)
    {
        var xi = Mathf.FloorToInt(x);
        var yi = Mathf.FloorToInt(y);
        var xf = x - xi;
        var yf = y - yi;

        var u = xf * xf * (3f - 2f * xf);
        var v = yf * yf * (3f - 2f * yf);

        var a = Rand(xi, yi, seed);
        var b = Rand(xi + 1, yi, seed);
        var c = Rand(xi, yi + 1, seed);
        var d = Rand(xi + 1, yi + 1, seed);

        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    /// <summary>多倍频噪声：低频出大块，高频出颗粒。</summary>
    public static float Fbm(float x, float y, int seed, int octaves = 3)
    {
        var sum = 0f;
        var amp = 0.5f;
        var norm = 0f;
        for (var i = 0; i < octaves; i++)
        {
            sum += Noise(x, y, seed + i * 977) * amp;
            norm += amp;
            x *= 2.03f;
            y *= 1.97f;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    public ImageTexture ToTexture()
    {
        var img = Image.CreateFromData(W, H, false, Image.Format.Rgba8, _px);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>开发用：把画布原样存盘，方便逐层检查。</summary>
    public void SavePng(string resPath)
    {
        var img = Image.CreateFromData(W, H, false, Image.Format.Rgba8, _px);
        img.SavePng(resPath);
    }
}
