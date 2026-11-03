using System;
using System.Collections.Generic;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 主菜单三项。**顺序就是画面上从上到下**——<see cref="BootMenu.Line"/> 按它取行。
/// </summary>
public enum BootChoice
{
    /// <summary>新的开始：清进度、旧档改名留底，从 hub1、第 1 天起跑。</summary>
    NewGame,

    /// <summary>继续：读 <c>user://save.json</c>。没有档、或档是坏的，这一项是灰的。</summary>
    Continue,

    /// <summary>退出：一个文件都不碰。</summary>
    Quit,
}

/// <summary>
/// 主菜单的模型。纯 C#、不认 Godot、不碰文件——所以在单测里可以直接摆开来验。
///
/// 只有一条规矩值得记：**灰着的项不能被选中**。上下移动要跳过它，
/// 默认项也不能落在一个按下去什么都不发生的项上。
/// </summary>
public sealed class BootMenu
{
    /// <summary>三项的文案。改这几个字要连 UI 的排布一起看。</summary>
    public const string NewGameText = "新的开始";
    public const string ContinueText = "继续";
    public const string QuitText = "退出";

    /// <summary>菜单一共几项。</summary>
    public const int Count = 3;

    private static readonly BootChoice[] Order =
        { BootChoice.NewGame, BootChoice.Continue, BootChoice.Quit };

    /// <summary>「继续」现在按得动吗。只有存档在、且不是坏档时才为真。</summary>
    public bool ContinueEnabled { get; }

    /// <summary>现在停在哪一项。</summary>
    public BootChoice Selected { get; private set; }

    /// <summary>现在停在第几行（0 起）。UI 画高亮用。</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>
    /// 造一张菜单。
    /// </summary>
    /// <param name="hasSave">存档文件在不在（且读得出来）。</param>
    /// <param name="saveBroken">档在、但坏了。坏档视同无档，但不许静默覆盖它。</param>
    public BootMenu(bool hasSave, bool saveBroken = false)
    {
        ContinueEnabled = hasSave && !saveBroken;

        // 无档时默认「新的开始」，有档时默认「继续」——玩家上一次停在哪，就从哪继续。
        Selected = ContinueEnabled ? BootChoice.Continue : BootChoice.NewGame;
        SelectedIndex = IndexOf(Selected);
    }

    /// <summary>某一项的文案。UI 与自检都从这里取，别在两处各写一份字。</summary>
    public static string TextOf(BootChoice choice) => choice switch
    {
        BootChoice.NewGame => NewGameText,
        BootChoice.Continue => ContinueText,
        BootChoice.Quit => QuitText,
        _ => "",
    };

    /// <summary>某一项现在按不按得动。灰的项按下去什么都不发生。</summary>
    public bool IsEnabled(BootChoice choice) => choice != BootChoice.Continue || ContinueEnabled;

    /// <summary>
    /// 给 UI 的一行：文案 + 能不能按。
    /// 灰的项由调用方自己调颜色——这一层只管"亮不亮"，不管"什么颜色"。
    /// </summary>
    /// <param name="i">第几行（0 起，见 <see cref="Count"/>）。</param>
    public (string Text, bool Enabled) Line(int i)
    {
        if (i < 0 || i >= Count) return ("", false);

        var choice = Order[i];
        return (TextOf(choice), IsEnabled(choice));
    }

    /// <summary>直接选中某一项。灰的项选不上去（保持原样）。</summary>
    public void Select(BootChoice choice)
    {
        if (!IsEnabled(choice)) return;

        Selected = choice;
        SelectedIndex = IndexOf(choice);
    }

    /// <summary>往上挪一格，跳过灰的项；到头绕回最后一项。</summary>
    public void MoveUp() => Step(-1);

    /// <summary>往下挪一格，跳过灰的项；到底绕回第一项。</summary>
    public void MoveDown() => Step(+1);

    /// <summary>按下确认：返回现在选中的那一项。</summary>
    public BootChoice Confirm() => Selected;

    /// <summary>某一项在第几行。</summary>
    public static int IndexOf(BootChoice choice)
    {
        for (var i = 0; i < Order.Length; i++)
            if (Order[i] == choice) return i;

        return 0;
    }

    /// <summary>
    /// 走一格。<paramref name="dir"/> 为 -1 向上、+1 向下。
    /// 一整圈都没有能按的项时原地不动——菜单不该把玩家卡在"选不了"的状态里。
    /// </summary>
    private void Step(int dir)
    {
        var i = SelectedIndex;

        for (var n = 0; n < Count; n++)
        {
            i = (i + dir + Count) % Count;
            if (!IsEnabled(Order[i])) continue;

            Selected = Order[i];
            SelectedIndex = i;
            return;
        }
    }
}

/// <summary>菜单选完之后该干什么。</summary>
public enum BootAction
{
    /// <summary>什么都不做（退出）：不读档、不写文件。</summary>
    Stay,

    /// <summary>继续：由调用方去读档。</summary>
    Continue,

    /// <summary>新的开始：进度已重置、旧档已改名，调用方从空档起跑。</summary>
    NewGame,
}

/// <summary>一次选择的后果。失败时带上原因，调用方只打日志、不拦人。</summary>
public sealed class BootOutcome
{
    /// <summary>接下来该做什么。</summary>
    public BootAction Action { get; init; }

    /// <summary>该动的文件都动成功了吗。只有 <see cref="BootAction.NewGame"/> 会为假。</summary>
    public bool Ok { get; init; } = true;

    /// <summary>失败时一句人话。</summary>
    public string? Error { get; init; }

    /// <summary>旧档挪去了哪（成功时是 <see cref="SaveService.PreBootPath"/>）。</summary>
    public string? ShelvedTo { get; init; }
}

/// <summary>
/// 启动流程：把"菜单上按了什么"翻译成"该动哪些文件"。
///
/// 规则只写在这一处，「新的开始」才不会在 Main 里散成三行文件操作：
///   · 进度：<see cref="Progress.TutorialDone"/> 置假、<see cref="Progress.LastHub"/> 清空，并且**落盘**；
///   · 存档：不删，改名成 <c>user://save.pre-boot.json</c>（连备份一起挪开）；
///   · 「继续」与「退出」一个文件都不碰。
/// </summary>
public sealed class BootFlow
{
    private readonly SaveService _save;
    private readonly Action<Progress> _saveProgress;

    /// <summary>
    /// 造一条启动流程。
    /// </summary>
    /// <param name="save">存档服务（改名这件事只走它）。</param>
    /// <param name="saveProgress">把进度写回 <c>user://progress.cfg</c> 的动作。</param>
    public BootFlow(SaveService save, Action<Progress> saveProgress)
    {
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _saveProgress = saveProgress ?? throw new ArgumentNullException(nameof(saveProgress));
    }

    /// <summary>
    /// 按菜单的选择动手。
    /// </summary>
    /// <param name="choice">玩家按下的那一项。</param>
    /// <param name="progress">这一局的进度对象（会被就地改）。</param>
    public BootOutcome Choose(BootChoice choice, Progress progress)
    {
        if (progress is null) throw new ArgumentNullException(nameof(progress));

        switch (choice)
        {
            case BootChoice.Continue:
                return new BootOutcome { Action = BootAction.Continue };

            case BootChoice.Quit:
                // 退出不写任何文件：连 Launches 都省了。
                return new BootOutcome { Action = BootAction.Stay };

            default:
                return StartNewGame(progress);
        }
    }

    /// <summary>
    /// 「新的开始」：教程重置、last_hub 清空、旧档改名留底。
    /// 进度写得进去、但旧档挪不开时照样算"开了新局"（Ok 为假，只用来打一句警告）。
    /// </summary>
    private BootOutcome StartNewGame(Progress progress)
    {
        progress.TutorialDone = false;
        progress.LastHub = "";

        try
        {
            _saveProgress(progress);
        }
        catch (Exception e)
        {
            return new BootOutcome
            {
                Action = BootAction.NewGame,
                Ok = false,
                Error = $"进度没写进去：{e.Message}",
            };
        }

        var (ok, error) = _save.TryShelveForNewGame();

        return new BootOutcome
        {
            Action = BootAction.NewGame,
            Ok = ok,
            Error = error,
            ShelvedTo = ok ? SaveService.PreBootPath : null,
        };
    }
}

/// <summary>
/// <c>--bootmenu</c> 的自检：无头跑一遍三种情形，打三行。
///
/// **不碰玩家那份档**——第三行用的是内存里的文件系统。
/// 会自己先把三条都走一遍再打印，所以这三行不是嘴说的，是跑出来的。
/// </summary>
public static class BootMenuSelfCheck
{
    /// <summary>无存档时的那一行。</summary>
    public const string NoSaveLine = "[菜单] 无存档：默认「新的开始」，继续=灰";

    /// <summary>有存档时的那一行。</summary>
    public const string HasSaveLine = "[菜单] 有存档：默认「继续」，继续=亮";

    /// <summary>「新的开始」那一行。</summary>
    public const string NewGameLine = "[菜单] 选新的开始 → 教程重置，存档改名 save.pre-boot.json";

    /// <summary>
    /// 跑一遍三种情形，返回要打的三行。任何一条对不上就把原因写进第三行，
    /// 让 CI 一眼看见（正常时就是上面三条常量）。
    /// </summary>
    public static string[] Lines()
    {
        var noSave = new BootMenu(hasSave: false);
        var hasSave = new BootMenu(hasSave: true);

        var first = noSave.Selected == BootChoice.NewGame && !noSave.ContinueEnabled
            ? NoSaveLine
            : $"[菜单] 无存档这一况不对：默认 {noSave.Selected}，继续={noSave.ContinueEnabled}";

        var second = hasSave.Selected == BootChoice.Continue && hasSave.ContinueEnabled
            ? HasSaveLine
            : $"[菜单] 有存档这一况不对：默认 {hasSave.Selected}，继续={hasSave.ContinueEnabled}";

        return new[] { first, second, CheckNewGame() };
    }

    /// <summary>内存里走一遍「新的开始」：进度要重置、save.json 要消失、save.pre-boot.json 要在。</summary>
    private static string CheckNewGame()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SaveService.SavePath] = new SaveState().ToJson(),
        };

        var fs = new SaveFileSystem
        {
            Exists = p => files.ContainsKey(p),
            Read = p => files.TryGetValue(p, out var t) ? t : null,
            Write = (p, t) => files[p] = t,
            Copy = (from, to) => files[to] = files[from],
            Move = (from, to) =>
            {
                files[to] = files[from];
                files.Remove(from);
            },
            Delete = p => files.Remove(p),
            Now = () => new DateTime(2026, 1, 1, 0, 0, 0),
        };

        var progress = new Progress { TutorialDone = true, LastHub = "hub7", Launches = 3 };
        var flow = new BootFlow(new SaveService(fs), _ => { });

        var outcome = flow.Choose(BootChoice.NewGame, progress);

        var ok = outcome.Action == BootAction.NewGame
                 && progress.TutorialDone == false
                 && progress.LastHub.Length == 0
                 && !files.ContainsKey(SaveService.SavePath)
                 && files.ContainsKey(SaveService.PreBootPath);

        return ok
            ? NewGameLine
            : $"[菜单] 新的开始这一况不对：Action={outcome.Action}，教程={progress.TutorialDone}，"
              + $"last_hub=「{progress.LastHub}」，旧档还在={files.ContainsKey(SaveService.SavePath)}，"
              + $"改名={files.ContainsKey(SaveService.PreBootPath)}";
    }
}
