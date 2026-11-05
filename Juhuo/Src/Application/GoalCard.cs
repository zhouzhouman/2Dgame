using System;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// HUD 上那一行常驻目标（票号 S4-01c）。玩家原话：<b>「主线剧情中主角的目的要明确一点」</b>。
///
/// 说什么由 <see cref="GoalTable"/> 决定（goals.csv 三句 + 每拍自己的 goal 列），
/// 这里只负责<b>加前缀</b>：<c>目标　活过今夜</c>。
/// 前缀与拼法收在一处，画的地方与验的地方读的是同一份——不然某天改了个全角空格，
/// 测试还在验旧的那一句。
/// </summary>
public static class GoalHud
{
    /// <summary>HUD 那一行的开头。中间是全角空格，与游戏里其它行一致。</summary>
    public const string Prefix = "目标　";

    /// <summary>
    /// 现在该挂在屏幕上的那句话。
    /// </summary>
    /// <param name="goals">目标表（goals.csv）。</param>
    /// <param name="pending">正等着的那一拍；没有传 null。</param>
    /// <param name="beatsDone">十二拍走完了没有。</param>
    /// <param name="turnedRealm">转段了没有（国号已定）。</param>
    /// <returns>带前缀的一行字，不会是空串。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="goals"/> 为 null。</exception>
    public static string Line(GoalTable goals, Beat? pending, bool beatsDone, bool turnedRealm)
        => Prefix + Text(goals, pending, beatsDone, turnedRealm);

    /// <summary>
    /// 不带前缀的那一句（日志与卡片正文用）。挑哪一句的规矩照旧在 <see cref="GoalTable.Current"/>。
    /// </summary>
    /// <param name="goals">目标表（goals.csv）。</param>
    /// <param name="pending">正等着的那一拍；没有传 null。</param>
    /// <param name="beatsDone">十二拍走完了没有。</param>
    /// <param name="turnedRealm">转段了没有。</param>
    /// <returns>一句话，不会是空串。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="goals"/> 为 null。</exception>
    public static string Text(GoalTable goals, Beat? pending, bool beatsDone, bool turnedRealm)
    {
        if (goals == null) throw new ArgumentNullException(nameof(goals));

        return goals.Current(pending, beatsDone, turnedRealm);
    }
}

/// <summary>
/// 开局那张"目标卡"（票号 S4-01c）：开场九句放完、教学第一步之前，屏幕正中淡入一小会儿。
///
/// 它是**提示，不是关卡**：
///   · 停 <see cref="HoldSeconds"/> 秒自己走；
///   · 按任意键也走，而且<b>这个键照常生效</b>（<see cref="BlocksInput"/> 永远是 false）——
///     玩家按 W 就是走一步，不是"关掉一个框"；
///   · 不用 Enter 确认，也不许它把教学压住。
///
/// 纯 C#，认识时间靠外面喂 dt，所以能在单测里把 2.5 秒走完。
/// </summary>
public sealed class GoalCard
{
    /// <summary>自己停留多久（秒）。再长就变成拦人了。</summary>
    public const float HoldSeconds = 2.5f;

    /// <summary>淡入用多久（秒）。</summary>
    public const float FadeSeconds = 0.35f;

    /// <summary>卡片顶上那四个字。正文来自 goals.csv 的 start 那一句。</summary>
    public const string Head = "你要做的";

    /// <summary>这张卡挡不挡键。**永远不挡**——它是提示，不是门。</summary>
    public bool BlocksInput => false;

    /// <summary>这会儿还在屏幕上。</summary>
    public bool Visible { get; private set; }

    /// <summary>已经挂了多久（秒）。</summary>
    public float Age { get; private set; }

    /// <summary>卡片正文（"活过今夜"）。</summary>
    public string Body { get; private set; } = "";

    /// <summary>淡入算到几成，0–1。</summary>
    public float Alpha => Visible ? Math.Clamp(Age / FadeSeconds, 0f, 1f) : 0f;

    /// <summary>
    /// 摆出来。
    /// </summary>
    /// <param name="body">正文；空串按"没有正文"处理，不会画成一片空白。</param>
    public void Show(string body)
    {
        Body = body ?? "";
        Age = 0f;
        Visible = true;
    }

    /// <summary>
    /// 走一帧。
    /// </summary>
    /// <param name="dt">这一帧多少秒。</param>
    /// <returns>还在屏幕上返回 true（调用方据此画淡入）。</returns>
    public bool Tick(float dt)
    {
        if (!Visible) return false;

        Age += Math.Max(0f, dt);
        if (Age < HoldSeconds) return true;

        Visible = false;
        return false;
    }

    /// <summary>按了任意键：收起来。</summary>
    /// <returns>这一下真的收起来了返回 true（没在显示时按无效）。</returns>
    public bool Dismiss()
    {
        if (!Visible) return false;

        Visible = false;
        return true;
    }
}
