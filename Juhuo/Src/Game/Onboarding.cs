namespace Juhuo;

/// <summary>
/// 新手引导的步骤机。
///
/// 原则：不弹"点下一步"的教学窗，而是**按玩家真的做了什么推进**。
/// 走出一步、按住 Ctrl、真的翻了一件东西、真的走到门口——每一步都是玩家自己做的，
/// 所以他不会觉得被拦下来听课。
///
/// 这里只管"第几步、说什么"，不碰 UI，也不碰 Godot。
/// </summary>
public sealed class Onboarding
{
    public enum Stage
    {
        Move = 0,
        Look = 1,
        Search = 2,
        Talk = 3,
        Leave = 4,
        Done = 5,
    }

    public Stage Current { get; private set; } = Stage.Move;

    public bool Done => Current == Stage.Done;

    /// <summary>这一步的提示。第一段是动作，第二段是他该做什么。</summary>
    public (string Key, string Text) Line => Current switch
    {
        Stage.Move => ("① 先走走", "W A S D 或方向键　·　走出这间屋子"),
        Stage.Look => ("② 看清楚", "按住 Ctrl　·　这一处能碰的东西会一起亮起来"),
        Stage.Search => ("③ 伸手", "走到东西旁边按 E　·　斜着站也够得着"),
        Stage.Talk => ("④ 说话", "走到人跟前按 E　·　第一次是初见，之后会不一样"),
        Stage.Leave => ("⑤ 出门", "黄框和箭头就是出口　·　走过去按 E"),
        _ => ("", ""),
    };

    /// <summary>只许往前走，回退没有意义。</summary>
    public void Reach(Stage stage)
    {
        if ((int)stage > (int)Current) Current = stage;
    }

    public void Finish() => Current = Stage.Done;

    public void Reset() => Current = Stage.Move;
}
