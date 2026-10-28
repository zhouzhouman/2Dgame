using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 地形格的只读视图，纯 C#，供不装引擎的自检使用（票号 S1-13）。
///
/// 与 Game 层 <c>Juhuo.Art.TerrainMap</c> 的判定**必须一致**，否则 CI 与游戏里
/// 会得出两套"这格能不能站"的答案。地图文件里地形（<c>. , : # T ~ ^ = + W</c>）
/// 与标记（可搜点 1-9/a-k、NPC A-H、玩家 @、出口 &gt;）混在一起，标记盖掉了原地形，
/// 所以这里用同样的"四邻权重 3 / 斜角权重 1"投票把底层地形还原出来。
///
/// 坐标口径：<see cref="Ground"/>/<see cref="IsWalkable"/> 收 **0-based**，
/// <see cref="WalkableCell"/> 收 **1-based**（与 <c>World.CellWalkable</c> 同口径）。
/// </summary>
public sealed class MapGrid
{
    private static readonly HashSet<char> TerrainChars = new() { '.', ',', ':', '#', 'T', '~', '^', '=', '+', 'W' };
    private static readonly HashSet<char> MarkerChars = new() { '@', '>', 'X' };

    private static readonly (int Dx, int Dy)[] Dirs = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    private readonly string[] _rows;
    private readonly char[] _ground;

    /// <summary>地图宽（格）。</summary>
    public int W { get; }

    /// <summary>地图高（格）。</summary>
    public int H { get; }

    private MapGrid(string[] rows)
    {
        _rows = rows;
        H = rows.Length;
        W = H > 0 ? rows[0].Length : 0;

        _ground = new char[W * H];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            _ground[y * W + x] = Resolve(x, y);
    }

    /// <summary>
    /// 从文本建图（<c>\r</c> 先剥掉，空行跳过——与 TerrainMap 同处理）。
    /// </summary>
    /// <param name="text">地图文件全文。</param>
    /// <returns>地图视图。</returns>
    public static MapGrid FromText(string text)
    {
        var lines = new List<string>();
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            if (raw.Length == 0) continue;
            lines.Add(raw);
        }

        return new MapGrid(lines.ToArray());
    }

    /// <summary>
    /// 读一个地图文件（<c>Juhuo/Content/maps/hubN.txt</c>）。
    /// 文件缺失抛 <see cref="FileNotFoundException"/>——地图没了是编程/打包错误，不该静默。
    /// </summary>
    /// <param name="path">地图文件的绝对或相对路径。</param>
    /// <returns>地图视图。</returns>
    public static MapGrid Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("地图路径为空", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException($"找不到地图文件：{path}", path);

        return FromText(File.ReadAllText(path));
    }

    /// <summary>原始字符（含标记）。越界返回 <c>#</c>。</summary>
    /// <param name="x">0-based 列。</param>
    /// <param name="y">0-based 行。</param>
    /// <returns>文件里的原字符。</returns>
    public char Raw(int x, int y)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return '#';

        var row = _rows[y];
        return x < row.Length ? row[x] : '#';
    }

    /// <summary>还原后的纯地形。越界返回 <c>#</c>。</summary>
    /// <param name="x">0-based 列。</param>
    /// <param name="y">0-based 行。</param>
    /// <returns>地形字符。</returns>
    public char Ground(int x, int y)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return '#';

        return _ground[y * W + x];
    }

    /// <summary>
    /// 这格地形能不能走、能站人（0-based）。判定与 <c>TerrainMap.IsWalkable</c> 逐字一致：
    /// <c>. , : + ^</c> 能走，<c># T ~ = W</c> 不能。
    /// </summary>
    /// <param name="x">0-based 列。</param>
    /// <param name="y">0-based 行。</param>
    /// <returns>能站人返回 true。</returns>
    public bool IsWalkable(int x, int y)
    {
        var g = Ground(x, y);
        return g is '.' or ',' or ':' or '+' or '^';
    }

    /// <summary>
    /// 1-based 的"能不能走"（与 <c>World.CellWalkable</c> 同口径，内部减 1）。
    /// </summary>
    /// <param name="cx">1-based 列。</param>
    /// <param name="cy">1-based 行。</param>
    /// <returns>能站人返回 true。</returns>
    public bool WalkableCell(int cx, int cy) => IsWalkable(cx - 1, cy - 1);

    /// <summary>渲染用的地形全图，调试与测试断言用。</summary>
    /// <returns>每行一个换行的地形字符串。</returns>
    public string DebugString()
    {
        var sb = new StringBuilder();
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++) sb.Append(Ground(x, y));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>四向邻居（不穿斜角）。自检的连通性与游戏里的移动同一套方向。</summary>
    public static IReadOnlyList<(int Dx, int Dy)> FourDirs => Dirs;

    /// <summary>
    /// 标记字符还原：先看四邻（权重 3），再看斜角（权重 1），取分数最高的地形；
    /// 全无邻居时落到 <c>,</c>。这段是 <c>TerrainMap.Resolve</c> 的逐字移植，
    /// 只允许"跟着 Game 改"，不允许独立发明（票面 S1-13）。
    /// </summary>
    private char Resolve(int x, int y)
    {
        var c = Raw(x, y);
        if (TerrainChars.Contains(c)) return c;

        Span<int> score = stackalloc int[128];
        ReadOnlySpan<(int Dx, int Dy, int W)> dirs = stackalloc (int, int, int)[]
        {
            (0, -1, 3), (0, 1, 3), (-1, 0, 3), (1, 0, 3),
            (-1, -1, 1), (1, -1, 1), (-1, 1, 1), (1, 1, 1),
        };

        foreach (var (dx, dy, w) in dirs)
        {
            var n = Raw(x + dx, y + dy);
            if (!TerrainChars.Contains(n)) continue;
            score[n] += w;
        }

        var best = ',';
        var bestScore = -1;
        for (var i = 0; i < score.Length; i++)
        {
            if (score[i] > bestScore)
            {
                bestScore = score[i];
                best = (char)i;
            }
        }

        if (bestScore <= 0) return MarkerChars.Contains(c) ? ',' : ',';
        return best;
    }
}
