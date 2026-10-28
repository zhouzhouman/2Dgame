namespace Juhuo.Core;

/// <summary>
/// 白幡村的死亡节拍：按**刻**算，不按搜点数算。
///
/// 公式（rule_time_clock.md §公式、§验收 10）：
/// <code>
/// deaths = min(3, ke_used / 4)      （整数除，向下取整）
/// </code>
/// 即进入村子的第 4 / 8 / 12 刻各死一人，第 12 刻三人全死光；
/// 之后不再死（没有第四个人）。这与催促同一个步长（4 刻），
/// 是"你翻得越多，时间过得越快"的统一写法（关卡设计 §3.3）。
///
/// 只算人数，不管谁死——"给药救的人不死"是调用方（Game）的判定顺序，
/// 规则卡 §验收 10 明确写了药优先于死亡判定，那是玩法接线，不是这里的公式。
/// </summary>
public static class Plague
{
    /// <summary>死一人的刻数间隔（rule_time_clock.md §公式 deaths 的除数）。</summary>
    public const int KePerDeath = 4;

    /// <summary>白幡村里躺着的人数上限（三个 sick）。</summary>
    public const int MaxDeaths = 3;

    /// <summary>
    /// 到这一刻为止该死几个人。
    /// </summary>
    /// <param name="keUsed">已用整刻（<c>Clock.KeUsedKe</c>）。负数按 0 处理。</param>
    /// <returns>0–3。</returns>
    public static int DeathsByKe(int keUsed)
    {
        if (keUsed <= 0) return 0;

        var deaths = keUsed / KePerDeath;
        return deaths > MaxDeaths ? MaxDeaths : deaths;
    }

    /// <summary>
    /// 下一个死亡节拍需要多少刻（绝对刻数，不是增量）。
    /// </summary>
    /// <param name="keUsed">已用整刻。负数按 0 处理。</param>
    /// <returns>再攒够这么多刻就死下一个；三个人都死完了返回 -1。</returns>
    public static int NextDeathAtKe(int keUsed)
    {
        var deaths = DeathsByKe(keUsed);
        if (deaths >= MaxDeaths) return -1;

        return (deaths + 1) * KePerDeath;
    }

    /// <summary>
    /// 这一刻是不是刚好踩到死亡节拍（用来决定要不要播"又倒下一个"）。
    /// </summary>
    /// <param name="keUsed">已用整刻。</param>
    /// <returns>keUsed 是 4 的正整数倍、且这一次真的多死了一个，返回 true。</returns>
    public static bool IsDeathBeat(int keUsed)
    {
        if (keUsed <= 0 || keUsed % KePerDeath != 0) return false;

        // 死满 3 人之后 16、20 刻这些倍数不再是"节拍"——没人可死了。
        return keUsed / KePerDeath == DeathsByKe(keUsed);
    }
}
