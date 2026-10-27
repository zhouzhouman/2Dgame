using System.Collections.Generic;
using Godot;

namespace Juhuo;

/// <summary>
/// 场景配置：起点、出口、站在那里的 NPC。
///
/// 这些原本是画在地图 txt 里的 @ &gt; A-H，但地图文件已经退化成纯地形数据
/// （数据生成器只导地形）。与其把标记塞回地图、再去重算 120 条坐标，
/// 不如把"地形"和"场景配置"彻底分开：地图管长得像什么，这里管这里站着谁。
/// </summary>
public static class SceneConfig
{
    public readonly record struct Npc(string Kind, Vector2 Cell);

    public sealed class Scene
    {
        public Vector2 Spawn;
        public Vector2 Exit;
        public Npc[] Npcs = System.Array.Empty<Npc>();

        public Scene(Vector2 spawn, Vector2 exit, Npc[]? npcs = null)
        {
            Spawn = spawn;
            Exit = exit;
            Npcs = npcs ?? System.Array.Empty<Npc>();
        }
    }

    public static readonly Dictionary<string, Scene> All = new()
    {
        // 城南小院：玩家在屋檐下，从西侧的巷子出去
        ["hub1"] = new(new Vector2(8, 9), new Vector2(2, 9)),

        // 废驿：从西边进来，往东走上大路
        ["hub2"] = new(new Vector2(2, 8), new Vector2(23, 8), new[]
        {
            new Npc("doctor", new Vector2(12, 4)),   // 沈砚
            new Npc("old", new Vector2(5, 7)),       // 老仆
        }),

        // 白幡村：从村口牌坊下进
        ["hub3"] = new(new Vector2(4, 20), new Vector2(2, 20), new[]
        {
            new Npc("chen", new Vector2(6, 14)),     // 陈三，抱着孩子
            new Npc("old", new Vector2(13, 10)),     // 老妇，坐在灶前
            new Npc("doctor", new Vector2(18, 10)),  // 游医，摊子后面
            new Npc("sick", new Vector2(8, 14)),     // 病人
            new Npc("sick", new Vector2(24, 15)),
            new Npc("sick", new Vector2(11, 16)),
            // 两个衙役不在这里摆——他们由 BuildPatrol 生成，会走。
            // 以前两边各摆一个，画面上就是四个人叠成两个。
        }),

        // 县城：从南门进，往北出
        ["hub4"] = new(new Vector2(12, 22), new Vector2(18, 2), new[]
        {
            // 坐标必须和 npcs.csv 一字不差：差一格，这个人就变成"不会说话的摆设"
            new Npc("old", new Vector2(7, 5)),       // 米铺老板
            new Npc("old", new Vector2(14, 5)),      // 布商
            new Npc("yamen", new Vector2(25, 5)),    // 县丞
            new Npc("doctor", new Vector2(32, 6)),   // 师爷
            new Npc("chen", new Vector2(25, 18)),    // 掮客
            new Npc("sick", new Vector2(20, 20)),    // 乞丐
            new Npc("old", new Vector2(9, 16)),      // 客栈掌柜
            new Npc("chen", new Vector2(12, 13)),    // 卖艺的
        }),

        // 山神庙：从南边石阶上来
        ["hub5"] = new(new Vector2(12, 15), new Vector2(2, 14), new[]
        {
            new Npc("yamen", new Vector2(14, 6)),    // 老鸦
            new Npc("old", new Vector2(18, 9)),      // 庙祝
        }),

        // 官仓：从南边的缺口进
        ["hub6"] = new(new Vector2(15, 11), new Vector2(2, 12), new[]
        {
            new Npc("doctor", new Vector2(21, 7)),   // 三个使者
            new Npc("doctor", new Vector2(24, 7)),
            new Npc("doctor", new Vector2(27, 7)),
        }),

        // 城下营地：最后一处，没有出口
        ["hub7"] = new(new Vector2(3, 9), new Vector2(-1, -1), new[]
        {
            new Npc("chen", new Vector2(6, 6)),
            new Npc("old", new Vector2(12, 6)),
            new Npc("yamen", new Vector2(15, 6)),
            new Npc("sick", new Vector2(5, 9)),
            new Npc("doctor", new Vector2(14, 9)),
            new Npc("old", new Vector2(8, 10)),
            new Npc("chen", new Vector2(11, 10)),
        }),
    };

    public static Scene Of(string hub) => All.TryGetValue(hub, out var s) ? s : All["hub1"];
}
