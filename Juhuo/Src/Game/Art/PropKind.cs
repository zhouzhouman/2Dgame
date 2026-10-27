using System;

namespace Juhuo.Art;

/// <summary>
/// 可交互物的视觉原型。
///
/// 数据里 120 条的名字几乎全不重复（灶台、陈家灶台、老妇家灶……），
/// 但玩家只需要认出"那是一口灶"，不需要"那是第 47 号灶"。
/// 所以归到 36 种原型：每种一个画法，名字只决定题字和文本。
/// </summary>
public enum PropKind
{
    Stove,      // 灶
    Table,      // 桌、柜台、供桌、香案
    Cabinet,    // 柜、书箱、琴匣、暗格
    Bed,        // 床（搜的是床下）
    Jar,        // 缸、坛、筐
    Bowl,       // 碗
    WallMark,   // 墙缝、刻字、墙角
    Door,       // 门、门框、门槛
    Fire,       // 灯台、火堆
    Rope,       // 晾衣绳、断绳
    Mat,        // 席
    Ash,        // 柴灰、煤渣、垃圾
    Step,       // 台阶、石墩
    Notice,     // 告示牌、告示墙
    Trough,     // 马槽
    Beast,      // 老马、狗
    Post,       // 木桩、拴马桩
    Well,       // 井
    Tracks,     // 车辙、脚印、影子
    Litter,     // 落叶、碎瓦、水洼、雪
    Wood,       // 柴堆
    Crate,      // 货箱
    Book,       // 账册、名册、笔
    Chair,      // 椅子
    Scale,      // 秤
    Censer,     // 香炉
    Bone,       // 骸骨、断手、死鼠
    Statue,     // 神像
    Gap,        // 洞口、入口
    Rack,       // 兵器架、鼓、马鞍
    Tree,       // 松树
    Grain,      // 谷堆
    Banner,     // 白幡
    Plow,       // 荒田、犁
    Grave,      // 坟
    Rock,       // 石头
}

public static class PropKinds
{
    /// <summary>
    /// 关键词 → 原型。顺序有讲究：越具体的词必须越靠前。
    /// "老妇家门前"必须排在"门"前面，否则会被认成一扇门。
    /// </summary>
    private static readonly (string Key, PropKind Kind)[] Rules =
    {
        ("灶", PropKind.Stove),

        ("供桌", PropKind.Table),
        ("香案", PropKind.Table),
        ("书桌", PropKind.Table),
        ("柜台", PropKind.Table),
        ("桌上", PropKind.Table),

        ("床", PropKind.Bed),
        ("房间", PropKind.Bed),

        ("井", PropKind.Well),
        ("坟", PropKind.Grave),
        ("白幡", PropKind.Banner),
        ("告示", PropKind.Notice),
        ("香炉", PropKind.Censer),

        ("柜", PropKind.Cabinet),
        ("书箱", PropKind.Cabinet),
        ("琴匣", PropKind.Cabinet),
        ("暗格", PropKind.Cabinet),

        ("缸", PropKind.Jar),
        ("坛", PropKind.Jar),
        ("筐", PropKind.Jar),
        ("碗", PropKind.Bowl),

        ("墙根草", PropKind.Litter),
        ("墙缝", PropKind.WallMark),
        ("刻痕", PropKind.WallMark),
        ("刻字", PropKind.WallMark),
        ("墙角", PropKind.WallMark),
        ("墙根", PropKind.WallMark),

        ("门前", PropKind.Litter),
        ("门槛", PropKind.Door),
        ("门框", PropKind.Door),
        ("门", PropKind.Door),

        ("灯台", PropKind.Fire),
        ("火堆", PropKind.Fire),
        ("绳", PropKind.Rope),
        ("席", PropKind.Mat),

        ("柴灰", PropKind.Ash),
        ("煤渣", PropKind.Ash),
        ("垃圾", PropKind.Ash),

        ("台阶", PropKind.Step),
        ("石阶", PropKind.Step),
        ("石墩", PropKind.Step),

        ("马槽", PropKind.Trough),
        ("马鞍", PropKind.Rack),
        ("老马", PropKind.Beast),
        ("一条狗", PropKind.Beast),
        ("木桩", PropKind.Post),
        ("拴马桩", PropKind.Post),

        ("车辙", PropKind.Tracks),
        ("脚印", PropKind.Tracks),
        ("影子", PropKind.Tracks),

        ("货箱", PropKind.Crate),
        ("仓底", PropKind.Crate),

        ("账", PropKind.Book),
        ("名册", PropKind.Book),
        ("笔", PropKind.Book),

        ("椅子", PropKind.Chair),
        ("秤", PropKind.Scale),

        ("骸骨", PropKind.Bone),
        ("断手", PropKind.Bone),
        ("死老鼠", PropKind.Bone),
        ("佛脚", PropKind.Bone),

        ("神像", PropKind.Statue),
        ("佛像", PropKind.Statue),

        ("洞", PropKind.Gap),
        ("入口", PropKind.Gap),
        ("田角", PropKind.Gap),

        ("兵器架", PropKind.Rack),
        ("车马行", PropKind.Rack),
        ("堂前鼓", PropKind.Rack),

        ("松树", PropKind.Tree),
        ("谷堆", PropKind.Grain),

        ("荒田", PropKind.Plow),
        ("犁", PropKind.Plow),

        ("柴", PropKind.Wood),
        ("路边石", PropKind.Rock),
        ("石", PropKind.Rock),

        ("雪", PropKind.Litter),
        ("水", PropKind.Litter),
        ("泥", PropKind.Litter),
        ("草", PropKind.Litter),
        ("叶", PropKind.Litter),
        ("瓦", PropKind.Litter),
        ("砖", PropKind.Litter),
        ("粪", PropKind.Litter),
        ("鞋", PropKind.Litter),
    };

    public static PropKind Of(string name)
    {
        foreach (var (key, kind) in Rules)
            if (name.Contains(key, StringComparison.Ordinal))
                return kind;
        return PropKind.Litter;
    }

    /// <summary>挡不挡路。地面上的痕迹不挡，家具和器物挡。</summary>
    public static bool Blocks(PropKind k) => k switch
    {
        // 不挡路的一律是"踩上去/跨过去"的东西。
        // 台阶尤其不能挡：它是走廊上的一段石阶，挡住就把整条路封死了。
        PropKind.WallMark or PropKind.Tracks or PropKind.Litter or PropKind.Ash
            or PropKind.Mat or PropKind.Bowl or PropKind.Book or PropKind.Bone
            or PropKind.Scale or PropKind.Rope or PropKind.Fire or PropKind.Gap
            or PropKind.Door or PropKind.Chair or PropKind.Step or PropKind.Censer => false,
        _ => true,
    };

    /// <summary>悬停时显示在名字前面的量词，让"缸"读起来像话。</summary>
    public static string MeasureWord(PropKind k) => k switch
    {
        PropKind.Stove => "一座",
        PropKind.Table => "一张",
        PropKind.Cabinet => "一口",
        PropKind.Bed => "一张",
        PropKind.Jar => "一口",
        PropKind.Bowl => "一只",
        PropKind.Door => "一道",
        PropKind.Fire => "一堆",
        PropKind.Rope => "一根",
        PropKind.Mat => "一张",
        PropKind.Notice => "一块",
        PropKind.Trough => "一方",
        PropKind.Post => "一根",
        PropKind.Well => "一口",
        PropKind.Wood => "一捆",
        PropKind.Crate => "一只",
        PropKind.Book => "一本",
        PropKind.Chair => "一把",
        PropKind.Scale => "一杆",
        PropKind.Censer => "一只",
        PropKind.Bone => "一具",
        PropKind.Statue => "一尊",
        PropKind.Rack => "一架",
        PropKind.Tree => "一棵",
        PropKind.Grain => "一堆",
        PropKind.Banner => "一面",
        PropKind.Grave => "一座",
        PropKind.Rock => "一块",
        PropKind.Beast => "一头",
        _ => "",
    };
}
