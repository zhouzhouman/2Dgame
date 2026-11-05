using Juhuo.Application;

namespace Juhuo;

/// <summary>
/// 新手引导的**门面**（票号 S4-01c）：给 Game 层一个 <see cref="Stage"/> 枚举，
/// 真正"第几步、做到了没有"的规矩在 <see cref="TutorialRun"/>（Application 层，可在单测里直接问）。
///
/// 为什么分成两半：内容校验脚本读的是**这个文件里的 enum Stage**（tools/validate.py 的 E17），
/// 它要确认 tutorial.csv 的八行与代码里的八步一一对得上；而步骤机的推演不该被 Godot 绑住。
/// 所以枚举留在这里，状态机在那边，这里只做一层转手。
///
/// 纯转手，不存第二份状态：改这里的任何一个方法之前先去看 <see cref="TutorialRun"/>。
/// </summary>
public sealed class Onboarding
{
    /// <summary>八个 key：与 <see cref="TutorialScript.ExpectedKeys"/> 是同一份（这里只是转手）。</summary>
    public const string KeyMove = TutorialScript.KeyMove;

    /// <summary>按住 Ctrl 看清楚。</summary>
    public const string KeyLook = TutorialScript.KeyLook;

    /// <summary>伸手翻东西。</summary>
    public const string KeySearch = TutorialScript.KeySearch;

    /// <summary>按 M 打开地图。</summary>
    public const string KeyMap = TutorialScript.KeyMap;

    /// <summary>按 I 打开囊。</summary>
    public const string KeyBag = TutorialScript.KeyBag;

    /// <summary>按 Tab 看三轴。</summary>
    public const string KeyTrust = TutorialScript.KeyTrust;

    /// <summary>跟人说话。</summary>
    public const string KeyTalk = TutorialScript.KeyTalk;

    /// <summary>走到出口。</summary>
    public const string KeyLeave = TutorialScript.KeyLeave;

    /// <summary>
    /// 八步，加一个"教完了"。顺序即教的顺序，必须与 <see cref="TutorialScript.ExpectedKeys"/> 一致
    /// （内容校验 E17 会当场对这两份）。
    /// </summary>
    public enum Stage
    {
        Move = 0,
        Look = 1,
        Search = 2,
        Map = 3,
        Bag = 4,
        Trust = 5,
        Talk = 6,
        Leave = 7,
        Done = 8,
    }

    private readonly TutorialRun _run;

    /// <summary>
    /// 造一台步骤机。
    /// </summary>
    /// <param name="script">教学表；null = 这一局不教（<see cref="Done"/> 一上来就是 true）。</param>
    public Onboarding(TutorialScript? script = null) => _run = new TutorialRun(script);

    /// <summary>这一局读到的教学表；没读到为 null。</summary>
    public TutorialScript? Script => _run.Script;

    /// <summary>一共几步；没有表时为 0。</summary>
    public int Total => _run.Total;

    /// <summary>教完了。</summary>
    public bool Done => _run.Done;

    /// <summary>现在停在哪一步；教完是 <see cref="Stage.Done"/>。</summary>
    public Stage Current => _run.Done ? Stage.Done : (Stage)_run.Index;

    /// <summary>现在第几步（1 起）。</summary>
    public int Ordinal => _run.Ordinal;

    /// <summary>进度条上那个 <c>3/8</c>。</summary>
    public string Bar => _run.Bar;

    /// <summary>现在这一步；教完为 null。</summary>
    public TutorialStep? Step => _run.Step;

    /// <summary>教学条的两截字：左边进度，右边这一步的指令 + 补充。</summary>
    public (string Key, string Text) Line => _run.Line;

    /// <summary>八步走完了没有——没走完不许出城。</summary>
    public bool CanLeaveCity => _run.CanLeaveCity;

    /// <summary>现在是不是正停在 <paramref name="key"/> 这一步。</summary>
    /// <param name="key">八 key 之一。</param>
    /// <returns>是返回 true。</returns>
    public bool IsAt(string key) => _run.IsAt(key);

    /// <summary>往前走一步；返回是不是教完了。</summary>
    /// <returns>这一步之后教完了返回 true。</returns>
    public bool Advance() => _run.Advance();

    /// <summary>面板类的一步：打开那个面板就算做到。</summary>
    /// <param name="key">面板的 key（map / bag / trust）。</param>
    /// <returns>真的推进一步返回 true。</returns>
    public bool PanelOpened(string key) => _run.PanelOpened(key);

    /// <summary>直接拨到某一步（<c>--tutstep=N</c>）。</summary>
    /// <param name="stage">拨到哪一步；<see cref="Stage.Done"/> 等于 <see cref="Finish"/>。</param>
    public void Reach(Stage stage) => _run.Reach((int)stage);

    /// <summary>这一局不教了。</summary>
    public void Finish() => _run.Finish();

    /// <summary>重新教。</summary>
    public void Reset() => _run.Reset();
}
