using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Juhuo.Core;

/// <summary>
/// 存档根对象。字段严格照 schema_v2.md §C，不多不少。
/// 只做内存里的 DTO 与 JSON 互转，不碰文件、不碰引擎。
/// </summary>
public sealed class SaveState
{
    /// <summary>当前存档版本（schema_v2.md §C / §D）。</summary>
    public const int CurrentVersion = 2;

    /// <summary>还能读的最低版本（更早的按"需迁移"处理）。</summary>
    public const int MinVersion = 1;

    /// <summary>存档版本号。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>随机种子。同一输入 + 同一种子 = 同一输出。</summary>
    public int Seed { get; set; }

    /// <summary>第几天。序章 1–30，正篇是回合 1–360。</summary>
    public int Day { get; set; } = 1;

    /// <summary>序章或正篇：prologue / reign。</summary>
    public string Tense { get; set; } = "prologue";

    /// <summary>当前所在空间。</summary>
    public string Hub { get; set; } = "hub1";

    /// <summary>玩家位置。</summary>
    public PlayerState Player { get; set; } = new();

    /// <summary>全局作用域旗标。</summary>
    public Dictionary<string, bool> Flags { get; set; } = new();

    /// <summary>背包。</summary>
    public List<InventoryEntry> Inventory { get; set; } = new();

    /// <summary>各地区的信任：min / guan / yi。</summary>
    public Dictionary<string, TrustState> Trust { get; set; } = new();

    /// <summary>手记与见过的人。</summary>
    public KnownState Known { get; set; } = new();

    /// <summary>进入过的空间。</summary>
    public Dictionary<string, HubState> Hubs { get; set; } = new();

    /// <summary>未知字段：读进来原样留着，写回去也原样带着（前向兼容）。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();

    /// <summary>这份存档的版本本构建认不认。</summary>
    public bool IsSupported => IsSupportedVersion(Version);

    /// <summary>这份存档比当前版本旧，需要逐级 upcast。</summary>
    public bool NeedsMigration => Version < CurrentVersion;

    /// <summary>这份存档来自更新的版本，拒收。</summary>
    public bool IsTooNew => Version > CurrentVersion;

    /// <summary>
    /// 这个版本号本构建认不认：1..当前之间都认，更新的拒收。
    /// </summary>
    /// <param name="version">存档里的版本号。</param>
    /// <returns>可读返回 true。</returns>
    public static bool IsSupportedVersion(int version) => version >= MinVersion && version <= CurrentVersion;

    /// <summary>
    /// 序列化成 JSON：camelCase、缩进两格、中文不转义。
    /// </summary>
    /// <returns>JSON 文本。</returns>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// 从 JSON 反序列化。缺字段取默认；类型不符抛 <see cref="JsonException"/>（由上层当坏档处理）。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <returns>存档对象。</returns>
    public static SaveState FromJson(string json)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));

        var state = JsonSerializer.Deserialize<SaveState>(json, Options)
            ?? throw new JsonException("存档内容为空或不是对象");

        state.Normalize();
        return state;
    }

    /// <summary>
    /// 序列化选项：camelCase、缩进、中文直出。
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 补齐空缺：null 的子对象换成空壳。
    /// </summary>
    private void Normalize()
    {
        Player ??= new PlayerState();
        Flags ??= new Dictionary<string, bool>();
        Inventory ??= new List<InventoryEntry>();
        Trust ??= new Dictionary<string, TrustState>();
        Known ??= new KnownState();
        Hubs ??= new Dictionary<string, HubState>();

        Known.Normalize();

        foreach (var item in Inventory)
        {
            if (item == null) continue;
            if (string.IsNullOrEmpty(item.Item)) item.Item = "";
        }

        foreach (var npc in Known.Npcs)
        {
            if (npc == null) continue;
            if (npc.Hub == null) npc.Hub = "";
            if (npc.Id == null) npc.Id = "";
        }

        foreach (var hub in Hubs.Values)
            hub?.Normalize();
    }
}

/// <summary>玩家位置（格坐标，1-based）。</summary>
public sealed class PlayerState
{
    /// <summary>格坐标 x。</summary>
    public double X { get; set; }

    /// <summary>格坐标 y。</summary>
    public double Y { get; set; }

    /// <summary>未知字段：保留并写回。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();
}

/// <summary>背包里的一项。</summary>
public sealed class InventoryEntry
{
    /// <summary>物品 id。</summary>
    public string Item { get; set; } = "";

    /// <summary>数量，≥1。</summary>
    public int N { get; set; }

    /// <summary>未知字段：保留并写回。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();
}

/// <summary>某地区的信任三条线，0–100。</summary>
public sealed class TrustState
{
    /// <summary>民望。</summary>
    public int Min { get; set; }

    /// <summary>官望。</summary>
    public int Guan { get; set; }

    /// <summary>疑心。</summary>
    public int Yi { get; set; }

    /// <summary>未知字段：保留并写回。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();
}

/// <summary>手记里记得的事 + 见过的人。</summary>
public sealed class KnownState
{
    /// <summary>记得的条目 id，只增不删。</summary>
    public List<string> Clues { get; set; } = new();

    /// <summary>见过的人，主键 (hub, id)。</summary>
    public List<NpcState> Npcs { get; set; } = new();

    /// <summary>未知字段：保留并写回。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();

    /// <summary>补齐空缺。</summary>
    internal void Normalize()
    {
        Clues ??= new List<string>();
        Npcs ??= new List<NpcState>();
    }
}

/// <summary>一个 NPC 的相处状态。</summary>
public sealed class NpcState
{
    /// <summary>所在空间。</summary>
    public string Hub { get; set; } = "";

    /// <summary>NPC id。</summary>
    public string Id { get; set; } = "";

    /// <summary>见过没有。</summary>
    public bool Met { get; set; }

    /// <summary>说过几次话（重复对话次数）。</summary>
    public int Again { get; set; }

    /// <summary>送过礼没有。</summary>
    public bool Gift { get; set; }

    /// <summary>委托进度 0–3。</summary>
    public int Quest { get; set; }

    /// <summary>未知字段：保留并写回。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();
}

/// <summary>一个空间的全部状态。</summary>
public sealed class HubState
{
    /// <summary>进没进来过。</summary>
    public bool Entered { get; set; }

    /// <summary>已用半刻，0 – 2B+16。</summary>
    [JsonPropertyName("ke_half")]
    public int KeHalf { get; set; }

    /// <summary>搜过的点 id，按搜的先后排。</summary>
    public List<string> Searched { get; set; } = new();

    /// <summary>该空间的局部作用域旗标。</summary>
    public Dictionary<string, bool> Flags { get; set; } = new();

    /// <summary>病人状态：alive / dying / dead。</summary>
    public Dictionary<string, string> Patients { get; set; } = new();

    /// <summary>被发现几次，每 3 次一次盘问。</summary>
    [JsonPropertyName("caught_count")]
    public int CaughtCount { get; set; }

    /// <summary>已触发催促次数，0–5。</summary>
    public int Nudges { get; set; }

    /// <summary>空间内的巡逻者。</summary>
    public Dictionary<string, PatrolState> Patrol { get; set; } = new();

    /// <summary>未知字段：保留并写回。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();

    /// <summary>
    /// 补齐空缺，并把 keHalf / caughtCount 两种拼法折成 schema 的写法。
    /// </summary>
    internal void Normalize()
    {
        Searched ??= new List<string>();
        Flags ??= new Dictionary<string, bool>();
        Patients ??= new Dictionary<string, string>();
        Patrol ??= new Dictionary<string, PatrolState>();

        if (Extra == null) return;

        if (Extra.TryGetValue("keHalf", out var ke) && ke.ValueKind == JsonValueKind.Number)
        {
            KeHalf = ke.GetInt32();
            Extra.Remove("keHalf");
        }

        if (Extra.TryGetValue("caughtCount", out var cc) && cc.ValueKind == JsonValueKind.Number)
        {
            CaughtCount = cc.GetInt32();
            Extra.Remove("caughtCount");
        }
    }

    /// <summary>取出这个空间的刻账，交给 <see cref="Clock"/> 用。</summary>
    /// <returns>刻账快照。</returns>
    public ClockState ToClockState() => new ClockState { KeHalf = KeHalf, Nudges = Nudges };
}

/// <summary>一个巡逻者的位置。</summary>
public sealed class PatrolState
{
    /// <summary>走到第几个路点。</summary>
    public int Wp { get; set; }

    /// <summary>在路点上的秒数。</summary>
    public double T { get; set; }

    /// <summary>未知字段：保留并写回。</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; } = new();
}
