namespace Juhuo.Core;

/// <summary>一个可搜点。字段与 Content/data/points.csv 一一对应。</summary>
public sealed class SearchPoint
{
    public string Hub { get; init; } = "";
    public string Id { get; init; } = "";
    public string Mark { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>surface / close / knowledge</summary>
    public string Layer { get; init; } = "surface";

    /// <summary>编辑器内显示名，不进游戏。</summary>
    public string Name { get; init; } = "";

    /// <summary>前置旗标；知识门必填。</summary>
    public string Requires { get; init; } = "";

    public string TextOnce { get; init; } = "";
    public string TextRepeat { get; init; } = "";

    /// <summary>形如 "medicine:3|coin:1"。</summary>
    public string Loot { get; init; } = "";

    public string SetFlag { get; init; } = "";
    public string WatchNpc { get; init; } = "";

    /// <summary>搜查消耗的刻数（整数，0–3）。刻是半刻整数记账的，字段不许用浮点（D-018）。</summary>
    public int Cost { get; init; } = 1;

    public bool IsKnowledge => Layer == "knowledge";

    public override string ToString() => $"{Id} ({X},{Y}) {Name}";
}
