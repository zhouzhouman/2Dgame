using System;
using System.Collections.Generic;

namespace Juhuo.Core;

/// <summary>一次可达性自检的结论。</summary>
/// <param name="Total">参与检查的可搜点总数。</param>
/// <param name="Reachable">够得着的件数。</param>
/// <param name="Unreachable">够不着的点（按传入顺序）。</param>
public sealed record ReachReport(int Total, int Reachable, IReadOnlyList<SearchPoint> Unreachable)
{
    /// <summary>全部够得着。</summary>
    public bool AllReachable => Unreachable.Count == 0;

    public override string ToString() => $"合计 {Total} 件，够不着 {Unreachable.Count} 件";
}

/// <summary>
/// 可搜点可达性自检（票号 S1-13）。
///
/// 为什么在 Core：这条检查是**关卡门禁**，CI 不该为了跑它去装 Godot。
///
/// 判定规则逐条照搬 <c>Src/Game/World.cs</c> 的 <c>CanReach</c> / <c>CanTouch</c>：
/// 1. 只在点的八邻格上试站人（站在点上不算，得贴着）；
/// 2. 邻格必须是可通行地形（<c>MapGrid.WalkableCell</c>）；
/// 3. 邻格不能被挡路的物件占着（与 <c>World.PropBlocks</c> 同口径，由调用方给谓词）；
/// 4. 邻格必须从起点走得到（四向、不穿墙——这条是对 <c>CanReach</c> 的收紧：
///    <c>CanReach</c> 只看局部，玩家其实走不过去的封闭房间它看不出来）；
/// 5. 斜角不许穿墙角：两个正交邻居至少有一个能站（<c>CanTouch</c> 那条）。
/// </summary>
public static class ReachAudit
{
    private static readonly (int Dx, int Dy)[] Ring8 =
    {
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1),
    };

    /// <summary>
    /// 跑一遍可达性自检。
    /// </summary>
    /// <param name="map">该 hub 的地形。</param>
    /// <param name="points">该 hub 的可搜点（坐标是 1-based）。</param>
    /// <param name="startX">玩家起点，1-based 列。</param>
    /// <param name="startY">玩家起点，1-based 行。</param>
    /// <param name="blocks">
    /// 这个点会不会挡路（对应 <c>Interactable.Blocks</c>）。
    /// 传 null 表示"所有可搜点都不挡路"——CI 没有 PropKinds 时用这个保守口径。
    /// </param>
    /// <returns>件数统计与够不着的清单。</returns>
    public static ReachReport Run(
        MapGrid map,
        IReadOnlyList<SearchPoint> points,
        int startX,
        int startY,
        Func<SearchPoint, bool>? blocks = null)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));
        if (points == null) throw new ArgumentNullException(nameof(points));

        var blocked = new HashSet<long>();
        if (blocks != null)
        {
            foreach (var p in points)
                if (blocks(p)) blocked.Add(Key(p.X, p.Y));
        }

        var reachable = Flood(map, startX, startY, blocked);

        var bad = new List<SearchPoint>();
        foreach (var p in points)
            if (!CanReachPoint(map, p, blocked, reachable)) bad.Add(p);

        return new ReachReport(points.Count, points.Count - bad.Count, bad);
    }

    /// <summary>点能不能被站在相邻格上的玩家够到。</summary>
    private static bool CanReachPoint(
        MapGrid map,
        SearchPoint point,
        HashSet<long> blocked,
        HashSet<long> reachable)
    {
        var cx = point.X;
        var cy = point.Y;

        foreach (var (dx, dy) in Ring8)
        {
            var nx = cx + dx;
            var ny = cy + dy;

            if (!map.WalkableCell(nx, ny)) continue;      // 墙 / 树林 / 水面
            if (blocked.Contains(Key(nx, ny))) continue;  // 被别的物件占着
            if (!reachable.Contains(Key(nx, ny))) continue; // 走不过去（封闭房间）

            // 八方向相邻 + 斜角不穿墙角（照抄 CanTouch）：
            // 站在 (nx,ny) 斜对点时，通往点的那两条正交格里至少有一个能站。
            // 注意方向是"从站的地方指向点"，即 (nx-dx, ny) 与 (nx, ny-dy)。
            if (dx == 0 || dy == 0) return true;
            if (map.WalkableCell(nx - dx, ny) || map.WalkableCell(nx, ny - dy)) return true;
        }

        return false;
    }

    /// <summary>从起点灌可站人的格子（四向、不穿墙、不穿挡路物件）。</summary>
    private static HashSet<long> Flood(MapGrid map, int startX, int startY, HashSet<long> blocked)
    {
        var seen = new HashSet<long>();
        if (!map.WalkableCell(startX, startY)) return seen;

        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((startX, startY));
        seen.Add(Key(startX, startY));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in MapGrid.FourDirs)
            {
                var nx = x + dx;
                var ny = y + dy;
                var k = Key(nx, ny);
                if (seen.Contains(k)) continue;
                if (!map.WalkableCell(nx, ny)) continue;
                if (blocked.Contains(k)) continue;

                seen.Add(k);
                queue.Enqueue((nx, ny));
            }
        }

        return seen;
    }

    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
}
