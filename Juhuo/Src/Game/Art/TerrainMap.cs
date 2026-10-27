using System;
using System.Collections.Generic;
using System.Text;

namespace Juhuo.Art;

/// <summary>
/// 地图文件的只读视图。
///
/// 地图 txt 里有两类字符混在一起：地形（. , : # T ~ ^ = +）和标记
/// （可搜点 1-9/a-k、NPC A-H、玩家 @、出口 >）。标记盖掉了原地形，
/// 所以这里用"邻域投票"把底层地形还原出来——点都放在室内或路上，
/// 周围同类地形一定占多数，还原是可靠的。
/// </summary>
public sealed class TerrainMap
{
    private static readonly HashSet<char> TerrainChars = new() { '.', ',', ':', '#', 'T', '~', '^', '=', '+', 'W' };
    private static readonly HashSet<char> MarkerChars = new() { '@', '>', 'X' };

    private readonly string[] _rows;
    private readonly char[] _ground;
    private readonly bool[] _indoor;

    public int W { get; }
    public int H { get; }

    public TerrainMap(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            if (raw.Length == 0) continue;
            lines.Add(raw);
        }

        _rows = lines.ToArray();
        H = _rows.Length;
        W = H > 0 ? _rows[0].Length : 0;

        _ground = new char[W * H];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            _ground[y * W + x] = Resolve(x, y);

        _indoor = ComputeIndoor();
    }

    /// <summary>
    /// 地图文件里 ',' 同时表示"屋外空地"和"屋里地面"。靠连通性区分：
    /// 从地图边缘能走到的 ',' 是室外，走不到的就是被墙围住的室内。
    /// </summary>
    private bool[] ComputeIndoor()
    {
        // 还没进场景时（World.Load 之前）先按"最大的一片可走区域"当室外。
        // 走廊 / 道路永远连成最大的一片，房间各自被门洞隔开，分得开。
        var seed = LargestComponentSeed();
        return IndoorFrom(seed);
    }

    /// <summary>
    /// 用当前场景的起点重新判定室内外。
    ///
    /// 之前的做法是"从地图第 2 圈撒种子"——这条在 18×10 的小院上是坏的：
    /// 第 3 圈正好是 y=7，而两间厢房的地面整行都在 y=7，
    /// 于是房间被当成室外：地里长草、屋顶也不画，走进去像站在院子里。
    ///
    /// 现在改成从玩家起点灌：起点一定在室外，房间被墙和门洞挡住，灌不到。
    /// </summary>
    public void MarkOutdoorsFrom(int cx, int cy) // 1-based
    {
        var sx = cx - 1;
        var sy = cy - 1;
        if ((uint)sx >= (uint)W || (uint)sy >= (uint)H) return;
        if (_ground[sy * W + sx] is '#' or '+') return;   // 起点在墙里，保持原判定

        var indoor = IndoorFrom((sx, sy));
        Array.Copy(indoor, _indoor, indoor.Length);
    }

    /// <summary>可走区域里最大的一片，取它的第一个格子当种子。</summary>
    private (int X, int Y) LargestComponentSeed()
    {
        var seen = new bool[W * H];
        (int X, int Y) best = (-1, -1);
        var bestSize = 0;

        for (var i = 0; i < W * H; i++)
        {
            if (seen[i] || _ground[i] is '#' or '+') continue;

            var size = 0;
            (int X, int Y) first = (i % W, i / W);
            var queue = new Queue<int>();
            queue.Enqueue(i);
            seen[i] = true;

            while (queue.Count > 0)
            {
                var idx = queue.Dequeue();
                size++;
                var x = idx % W;
                var y = idx / W;

                foreach (var (dx, dy) in Dirs)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if ((uint)nx >= (uint)W || (uint)ny >= (uint)H) continue;

                    var n = ny * W + nx;
                    if (seen[n] || _ground[n] is '#' or '+') continue;

                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }

            if (size <= bestSize) continue;
            bestSize = size;
            best = first;
        }

        return best;
    }

    private static readonly (int dx, int dy)[] Dirs = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    /// <summary>从种子灌出去，没灌到的非墙格就是室内。</summary>
    private bool[] IndoorFrom((int X, int Y) seed)
    {
        var outside = new bool[W * H];

        if (seed.X >= 0)
        {
            var queue = new Queue<int>();
            var start = seed.Y * W + seed.X;
            outside[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var idx = queue.Dequeue();
                var x = idx % W;
                var y = idx / W;

                foreach (var (dx, dy) in Dirs)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if ((uint)nx >= (uint)W || (uint)ny >= (uint)H) continue;

                    var n = ny * W + nx;
                    if (outside[n]) continue;
                    if (_ground[n] is '#' or '+') continue;   // 门洞算墙，不然室内会被它灌穿

                    outside[n] = true;
                    queue.Enqueue(n);
                }
            }
        }

        var indoor = new bool[W * H];
        for (var i = 0; i < indoor.Length; i++)
            indoor[i] = _ground[i] != '#' && !outside[i];

        return indoor;
    }

    /// <summary>被墙围住、从地图边缘走不到的地方。</summary>
    public bool IsIndoor(int x, int y)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return false;
        return _indoor[y * W + x];
    }

    /// <summary>原始字符（含标记）。</summary>
    public char Raw(int x, int y)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return '#';
        var row = _rows[y];
        return x < row.Length ? row[x] : '#';
    }

    /// <summary>还原后的纯地形。</summary>
    public char Ground(int x, int y)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return '#';
        return _ground[y * W + x];
    }

    public bool IsTerrain(int x, int y) => TerrainChars.Contains(Ground(x, y));

    /// <summary>这块地形是不是"能走、能站人"的地面。</summary>
    public bool IsWalkable(int x, int y)
    {
        var g = Ground(x, y);
        return g is '.' or ',' or ':' or '+' or '^';
    }

    public bool IsInterior(int x, int y) => Ground(x, y) == ',';

    public bool IsWall(int x, int y) => Ground(x, y) == '#';

    /// <summary>地图最外一圈：画成密林，把画面围住。</summary>
    public bool IsBorder(int x, int y) => x == 0 || y == 0 || x == W - 1 || y == H - 1;

    public IEnumerable<(int X, int Y, char C)> Markers()
    {
        for (var y = 0; y < H; y++)
        {
            var row = _rows[y];
            for (var x = 0; x < row.Length; x++)
            {
                var c = row[x];
                if (TerrainChars.Contains(c)) continue;
                yield return (x, y, c);
            }
        }
    }

    private char Resolve(int x, int y)
    {
        var c = Raw(x, y);
        if (TerrainChars.Contains(c)) return c;

        // 标记字符：先看四邻（权重 3），再看斜角（权重 1）
        Span<int> score = stackalloc int[128];
        ReadOnlySpan<(int dx, int dy, int w)> dirs = stackalloc (int, int, int)[]
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

        // 全无邻居时按标记类型给默认值
        if (bestScore <= 0) return MarkerChars.Contains(c) ? ',' : ',';
        return best;
    }

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
}
