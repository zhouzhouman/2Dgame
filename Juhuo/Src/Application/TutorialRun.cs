using System;

namespace Juhuo.Application;

/// <summary>
/// 新手引导的**步骤机**（票号 S4-01c）：第几步、做到了没有、按什么推进。
///
/// 原则：不弹"点下一步"的教学窗，而是**按玩家真的做了什么推进**。
/// 走出一步、按住 Ctrl、真的翻了一件东西、真的按 M 打开地图、真的走到门口——
/// 每一步都是玩家自己做的，所以他不会觉得被拦下来听课。
///
/// 八步的顺序是钉死的：<c>move → look → search → map → bag → trust → talk → leave</c>。
/// 第八步（出门）走完才算 <see cref="Done"/>，在那之前 <see cref="CanLeaveCity"/> 是 false——
/// 这是玩家要的"强制教学"：城里那点事没学完，不许出城。
///
/// 这里不认识 <c>Onboarding.Stage</c>（那个枚举在 Game 层，给 <c>--tutstep</c> 与内容校验用），
/// 状态就是一个 0..8 的下标；<c>Onboarding</c> 只是一层转手。这样这台机器在单测里能直接问，
/// 又不必把 Godot 那层拖进来。
///
/// 中文一个字都不写在这里：每一步说什么是 <see cref="TutorialScript"/>（tutorial.csv）的事。
/// </summary>
public sealed class TutorialRun
{
    /// <summary>八步。</summary>
    public const int StepCount = 8;

    private readonly TutorialScript? _script;
    private int _index;
    private bool _forcedDone;

    /// <summary>
    /// 造一台步骤机。
    /// </summary>
    /// <param name="script">
    /// 教学表。<b>null = 这局不教</b>（tutorial.csv 读不进来，或者跑批明确要跳过）——
    /// 这时 <see cref="Done"/> 一开始就是 true，游戏照常能玩，只是没人带路。
    /// </param>
    public TutorialRun(TutorialScript? script = null) => _script = script;

    /// <summary>这一局读到的教学表；没读到为 null。</summary>
    public TutorialScript? Script => _script;

    /// <summary>一共几步；没有表时为 0。</summary>
    public int Total => _script?.Count ?? 0;

    /// <summary>教完了（八步走完、或者这一局本来就不教）。</summary>
    public bool Done => _forcedDone || _script is null || _index >= Total;

    /// <summary>现在停在第几步（0 起）；教完时等于 <see cref="Total"/>。</summary>
    public int Index => Done ? Total : _index;

    /// <summary>现在第几步（1 起）。教完时报总数。</summary>
    public int Ordinal => Done ? Total : _index + 1;

    /// <summary>进度条上那个 <c>3/8</c>。</summary>
    public string Bar => Total == 0 ? "" : $"{Ordinal}/{Total}";

    /// <summary>现在这一步；教完为 null。</summary>
    public TutorialStep? Step => Done ? null : _script![_index];

    /// <summary>教学条的两截字：左边进度（<c>3/8</c>），右边这一步的指令 + 补充。</summary>
    public (string Key, string Text) Line => Step is { } step ? (Bar, step.Text) : ("", "");

    /// <summary>
    /// 八步走完了没有——<b>没走完不许出城</b>。地板上的出口由 Main 的 <c>OnExit</c> 把守，
    /// 地图上的"去别处"由 <c>ConfirmMapTravel</c> 把守，两边问的是同一句话。
    /// </summary>
    public bool CanLeaveCity => Done;

    /// <summary>现在是不是正停在 <paramref name="key"/> 这一步。</summary>
    /// <param name="key">八 key 之一。</param>
    /// <returns>是返回 true；教完了、或者停在别的步都返回 false。</returns>
    public bool IsAt(string key)
    {
        var step = Step;
        return step is not null && string.Equals(step.Key, key, StringComparison.Ordinal);
    }

    /// <summary>
    /// 往前走一步。
    /// </summary>
    /// <returns>这一步之后教完了返回 true。</returns>
    public bool Advance()
    {
        if (Done) return true;

        _index = Math.Min(_index + 1, Total);
        return Done;
    }

    /// <summary>
    /// 面板类的一步（地图 / 囊 / 信任）：**打开那个面板就算做到**，不再要求别的动作。
    /// 玩家按 M 就是为了看地图，地图开出来了，这一课就已经上了。
    /// </summary>
    /// <param name="key">面板的 key（map / bag / trust）。</param>
    /// <returns>这一下真的推进一步返回 true；停的不是这一步返回 false（按错了不算）。</returns>
    public bool PanelOpened(string key)
    {
        if (!IsAt(key)) return false;

        Advance();
        return true;
    }

    /// <summary>
    /// 直接把引导拨到某一步（开发辅助 <c>--tutstep=N</c>）。只许往前走。
    /// </summary>
    /// <param name="stage">拨到第几步（0 起）；<see cref="StepCount"/> 等于 <see cref="Finish"/>。</param>
    public void Reach(int stage)
    {
        if (stage >= StepCount)
        {
            Finish();
            return;
        }

        if (Done) return;

        if (stage > _index && stage < Total) _index = stage;
    }

    /// <summary>这一局不教了（看过一次 / 跑批 / 自检）。</summary>
    public void Finish() => _forcedDone = true;

    /// <summary>重新教（新档 / <c>--tutorial</c>）。</summary>
    public void Reset()
    {
        _forcedDone = false;
        _index = 0;
    }
}
