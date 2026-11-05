using System;
using System.Text;

namespace Juhuo.Application;

/// <summary>
/// 玩家能对这个世界做的动作（S4-02 的闸门只认这几个口子）。
///
/// <see cref="Wait"/> 是"什么都没按"：它永远放行——把"站着不动"拦下来，等于把游戏卡死。
/// </summary>
public enum Act
{
    /// <summary>走（WASD / 方向键）。</summary>
    Move,

    /// <summary>按住 Ctrl 看全部。</summary>
    Look,

    /// <summary>按 E 伸手翻东西。</summary>
    Search,

    /// <summary>按 M 开地图。</summary>
    Map,

    /// <summary>按 I 开囊。</summary>
    Bag,

    /// <summary>按 Tab 看三轴。</summary>
    Trust,

    /// <summary>按 E 跟人说话。</summary>
    Talk,

    /// <summary>按 E 出城。</summary>
    Exit,

    /// <summary>Esc / H：看帮助、关面板。</summary>
    Help,

    /// <summary>没有动作（空闲）。</summary>
    Wait,
}

/// <summary>
/// 教学停在哪一步。顺序与 <see cref="TutorialScript.ExpectedKeys"/> 一一对应，
/// <see cref="TutorialGate.Key"/> 会把这份对应关系当场钉死。
///
/// 为什么不直接用 <c>Onboarding.Stage</c>：那个枚举在 Game 层（Godot 工程），
/// 而闸门要能在单测里直接问——Application 不认识 Godot，只认识这八个 key。
/// </summary>
public enum TutorialStage
{
    /// <summary>第一步：走两步。</summary>
    Move,

    /// <summary>第二步：按住 Ctrl 看。</summary>
    Look,

    /// <summary>第三步：翻一件东西。</summary>
    Search,

    /// <summary>第四步：按 M 看地图。</summary>
    Map,

    /// <summary>第五步：按 I 看囊。</summary>
    Bag,

    /// <summary>第六步：按 Tab 看三轴。</summary>
    Trust,

    /// <summary>第七步：跟人说话。</summary>
    Talk,

    /// <summary>第八步：走到出口出城。</summary>
    Leave,

    /// <summary>教完了。</summary>
    Done,
}

/// <summary>
/// 教学的**闸门**（票号 S4-02）：这一步只让做这一步要的事，别的按了不响。
///
/// 以前八步只"判定做到了没有"，不拦人——第一步写着"按 WASD 走两步"，
/// 玩家却可以直接按 E 翻东西、按 M 开图、走到出口出城。那是提示，不是强制。
/// 现在把"这一步允许做什么"做成**一张表**（<see cref="Block"/>），
/// 所有输入口在动手之前都问它一句：返回 null 放行，返回一句话就是拦下、并把这句话给玩家看。
///
/// 三条不变量（改这张表之前先读）：
/// · <b>Esc / 帮助永远放行</b>——关面板被拦，玩家会以为游戏卡死；
/// · <b>移动永远放行</b>——第一步本身就是走，拦走路等于拦教学；
/// · <b>教完了全部放行</b>（<see cref="TutorialStage.Done"/>）——闸门只在上课的十分钟里存在。
///
/// 中文只在这一张表里。提示文案不写成"按了没反应"，而是"现在该按什么"——
/// 玩家按下被拦的动作时，看到的必须是下一步的指令，不是一句"不行"。
/// </summary>
public static class TutorialGate
{
    /// <summary>第一步被拦时说的话。</summary>
    public const string MoveHint = "先走两步——按 W A S D";

    /// <summary>第二步被拦时说的话。</summary>
    public const string LookHint = "按住 Ctrl 看一眼";

    /// <summary>第三步被拦时说的话。</summary>
    public const string SearchHint = "站到东西旁边按 E";

    /// <summary>第四步被拦时说的话。</summary>
    public const string MapHint = "按 M 打开地图";

    /// <summary>第五步被拦时说的话。</summary>
    public const string BagHint = "按 I 打开囊";

    /// <summary>第六步被拦时说的话。</summary>
    public const string TrustHint = "按 Tab 看三轴";

    /// <summary>第七步被拦时说的话。</summary>
    public const string TalkHint = "走到人跟前按 E";

    /// <summary>第八步被拦时说的话。</summary>
    public const string LeaveHint = "走到黄框边按 E 出城";

    /// <summary>
    /// 地图面板里点"走"被拦时说的话（第四步）：地图是拿来看的，不是拿来赶路的。
    /// 单独一句，是因为这会儿玩家眼睛在面板上，说"按 M 打开地图"等于答非所问。
    /// </summary>
    public const string MapTravelBlocked = "先看一眼就好，走的事待会儿说";

    /// <summary>囊面板里丢东西 / 用东西被拦时说的话（第五步）：可以看，不能改。</summary>
    public const string BagLocked = "囊里的先只看不动，这一课是让你认东西";

    /// <summary>矩阵表头的第一行（<c>--gate</c> 用）。</summary>
    public const string MatrixHeader = "[闸] 教学闸门：八步 × 九个动作（√ 放行　× 拦下；Esc / 帮助永远放行）";

    /// <summary>
    /// 这一步允许 <paramref name="act"/> 吗。
    /// </summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <param name="act">玩家按下的动作。</param>
    /// <returns>
    /// <b>null = 放行</b>；非 null = 拦下，返回值就是给玩家看的那句提示。
    /// </returns>
    public static string? Block(TutorialStage stage, Act act)
    {
        // 教完了：这十分钟已经过去，全部放行。
        if (stage == TutorialStage.Done) return null;

        // 帮助 / 什么都不按：永远放行（见类型注释里的不变量）。
        if (act is Act.Help or Act.Wait) return null;

        // 走：每一课都允许。第一步要的就是走，后面的每一步也都要走到跟前。
        if (act == Act.Move) return null;

        // 这一步要求的那一个动作：放行。
        if (act == Required(stage)) return null;

        // 其余一律拦下，并告诉他现在该做什么。
        return Hint(stage);
    }

    /// <summary>这一步要求玩家做的那一个动作。</summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <returns>那一个动作；教完了返回 <see cref="Act.Wait"/>。</returns>
    public static Act Required(TutorialStage stage) => stage switch
    {
        TutorialStage.Move => Act.Move,
        TutorialStage.Look => Act.Look,
        TutorialStage.Search => Act.Search,
        TutorialStage.Map => Act.Map,
        TutorialStage.Bag => Act.Bag,
        TutorialStage.Trust => Act.Trust,
        TutorialStage.Talk => Act.Talk,
        TutorialStage.Leave => Act.Exit,
        _ => Act.Wait,
    };

    /// <summary>这一步被拦下时给玩家看的那句话；教完了是空串。</summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <returns>一句中文提示。</returns>
    public static string Hint(TutorialStage stage) => stage switch
    {
        TutorialStage.Move => MoveHint,
        TutorialStage.Look => LookHint,
        TutorialStage.Search => SearchHint,
        TutorialStage.Map => MapHint,
        TutorialStage.Bag => BagHint,
        TutorialStage.Trust => TrustHint,
        TutorialStage.Talk => TalkHint,
        TutorialStage.Leave => LeaveHint,
        _ => "",
    };

    /// <summary>这一步在内容表里的 key（move / look / …），与 <see cref="TutorialScript.ExpectedKeys"/> 同序。</summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <returns>那个 key；教完了返回空串。</returns>
    public static string Key(TutorialStage stage) => stage switch
    {
        TutorialStage.Move => TutorialScript.KeyMove,
        TutorialStage.Look => TutorialScript.KeyLook,
        TutorialStage.Search => TutorialScript.KeySearch,
        TutorialStage.Map => TutorialScript.KeyMap,
        TutorialStage.Bag => TutorialScript.KeyBag,
        TutorialStage.Trust => TutorialScript.KeyTrust,
        TutorialStage.Talk => TutorialScript.KeyTalk,
        TutorialStage.Leave => TutorialScript.KeyLeave,
        _ => "",
    };

    /// <summary>
    /// 地图面板里点"走"：教学没走完一律拦下。
    /// 这一步本来就是让玩家"看一眼地图"，赶路是第八步出门之后的事。
    /// </summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <returns>null = 放行；非 null = 拦下，并把这句写进面板。</returns>
    public static string? BlockMapTravel(TutorialStage stage) =>
        stage == TutorialStage.Done ? null : MapTravelBlocked;

    /// <summary>
    /// 囊面板里丢 / 用：教学没走完一律拦下。可以看，不能改——
    /// 这一步教的是"你的囊里有什么"，不是"怎么扔东西"。
    /// </summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <returns>null = 放行；非 null = 拦下，并把这句给玩家看。</returns>
    public static string? BlockBagEdit(TutorialStage stage) =>
        stage == TutorialStage.Done ? null : BagLocked;

    /// <summary>
    /// 这一步要的东西在这个空间里根本没有吗——是的话必须**跳过**，不能让人卡死。
    ///
    /// 例：hub1 没有 NPC，第七步"跟人说话"在那儿永远做不成。判定条件写在这里而不是散在
    /// Main 的 if 里，是为了单测能把八种组合一次问完。
    /// </summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <param name="hasProps">这个空间里有可翻的东西吗。</param>
    /// <param name="hasSpeakers">这个空间里有能说话的人吗。</param>
    /// <param name="hasExit">这个空间里有出口吗。</param>
    /// <returns>这一步不可能做到，返回 true（调用方应当直接跳过去并打日志）。</returns>
    public static bool Impossible(TutorialStage stage, bool hasProps, bool hasSpeakers, bool hasExit)
    {
        return stage switch
        {
            TutorialStage.Search => !hasProps,
            TutorialStage.Talk => !hasSpeakers,
            TutorialStage.Leave => !hasExit,
            _ => false,
        };
    }

    /// <summary>
    /// 放行才做：把"被拦下时什么都不做"这件事收在一个地方，免得十几处 if 各写一遍。
    ///
    /// <paramref name="onAllowed"/> 与 <paramref name="onBlocked"/> 只有一个会被调用，
    /// 所以被拦下时调用方的状态（位置 / 刻 / 已搜 / 面板开关）一个都不会动。
    /// </summary>
    /// <param name="stage">当前停在第几步。</param>
    /// <param name="act">玩家按下的动作。</param>
    /// <param name="onAllowed">放行时做的真事。</param>
    /// <param name="onBlocked">拦下时把提示交给玩家看。</param>
    /// <returns>放行返回 true；拦下返回 false。</returns>
    public static bool Permit(TutorialStage stage, Act act, Action onAllowed, Action<string> onBlocked)
    {
        if (onAllowed == null) throw new ArgumentNullException(nameof(onAllowed));
        if (onBlocked == null) throw new ArgumentNullException(nameof(onBlocked));

        var hint = Block(stage, act);
        if (hint is null)
        {
            onAllowed();
            return true;
        }

        onBlocked(hint);
        return false;
    }

    /// <summary>
    /// <c>--gate</c> 要打的那张矩阵：表头 + 八行，一行一步。
    /// 一行长这样：<c>[闸] 3/8 search　√走 ×看 √搜 …　→ 站到东西旁边按 E</c>。
    /// </summary>
    /// <returns>若干行，直接 <c>GD.Print</c> 即可。</returns>
    public static string[] Matrix()
    {
        var acts = new[] { Act.Move, Act.Look, Act.Search, Act.Map, Act.Bag, Act.Trust, Act.Talk, Act.Exit, Act.Help };
        var labels = new[] { "走", "看", "搜", "图", "囊", "轴", "说", "出", "帮" };

        var lines = new string[2 + 8];
        lines[0] = MatrixHeader;
        lines[1] = "[闸] 步　　　　" + string.Join(" ", labels) + "　→ 被拦时说什么";

        var stages = new[]
        {
            TutorialStage.Move, TutorialStage.Look, TutorialStage.Search, TutorialStage.Map,
            TutorialStage.Bag, TutorialStage.Trust, TutorialStage.Talk, TutorialStage.Leave,
        };

        var sb = new StringBuilder();
        for (var i = 0; i < stages.Length; i++)
        {
            var stage = stages[i];
            sb.Clear();
            sb.Append($"[闸] {i + 1}/8 {Key(stage),-7}");

            for (var a = 0; a < acts.Length; a++)
            {
                var allowed = Block(stage, acts[a]) is null;
                if (a > 0) sb.Append(' ');
                sb.Append(allowed ? "√" : "×").Append(labels[a]);
            }

            sb.Append("　→ ").Append(Hint(stage));
            lines[2 + i] = sb.ToString();
        }

        return lines;
    }
}
