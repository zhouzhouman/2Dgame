using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Juhuo.Application;
using Juhuo.Art;
using Juhuo.Core;

namespace Juhuo;

/// <summary>
/// 序章的可运行场景。
///
/// 操作：
///   WASD / 方向键  走动
///   E / 空格       搜查 · 交谈 · 离开（对话开着时＝看完了）
///   1–4            选项面板里挑一项
///   Enter          选项面板里确认
///   Ctrl（按住）   把当前场景所有可交互物框出来
///   Esc            关掉对话；什么都没开时打开操作说明
///   H              操作说明
///
/// 新手引导不弹"点下一步"的窗：按玩家真的做了什么推进（走出去 → 按 Ctrl → 翻东西 → 走到门口）。
/// 每一步都是他自己做的，所以不打断。
/// </summary>
public partial class Main : Node2D
{
    private const int ViewW = 640;
    private const int ViewH = 440;
    private const int MapTop = 6;

    private PointTable _table = default!;
    private NpcTalkTable _talks = default!;
    private DialogueBook _book = default!;
    private World _world = default!;
    private MapView _view = default!;

    private Label _title = default!;
    private Label _subtitle = default!;
    private Label _hint = default!;
    private Panel _box = default!;
    private Label _boxHead = default!;
    private Label _boxBody = default!;
    private FontFile? _font;

    // —— 新手引导 ——
    private readonly Onboarding _tut = new();
    private Progress _progress = new();
    private Panel _tutPanel = default!;
    private Label _tutKey = default!;
    private Label _tutText = default!;
    private Vector2 _spawnCell;
    private float _ctrlHeld;
    private float _time;

    // —— 操作说明 ——
    private Panel _help = default!;
    private bool _helpOpen;

    /// <summary>临时消息（"到了。白幡村"）的保留时间，别被下一帧的提示盖掉。</summary>
    private float _hintHoldUntil;

    // 在这一处待了很久却一件东西都没翻过 —— 多半是忘了 Ctrl
    private float _idleHere;
    private bool _ctrlNudged;

    private const string ProgressPath = "user://progress.cfg";

    /// <summary>存档服务。文件动作由 Godot 那份提供，规则由 Core 那一层管。</summary>
    private SaveService _save = default!;

    // —— 十二节拍 ——
    /// <summary>节拍表（beats.csv）。</summary>
    private BeatTable _beatTable = default!;

    /// <summary>节拍运行器：日推进、选项、HUD 文案都在这一层（Application）。</summary>
    private PrologueBeats? _beats;

    /// <summary>读进来的那份档。节拍状态机要照它恢复，所以先留一手。</summary>
    private SaveState? _loadedState;

    /// <summary>HUD 最上面那一行："第 3 天 · 废驿 · 该做：③ 第一口锅"。</summary>
    private Label _beatLine = default!;

    // —— 选项面板 ——
    private const int MaxOptions = 4;

    private Panel _choice = default!;
    private Label _choiceHead = default!;
    private readonly Label[] _choiceItems = new Label[MaxOptions];
    private readonly string[] _choiceText = new string[MaxOptions];
    private Label _choiceFoot = default!;
    private Beat? _choiceBeat;
    private int _choiceSel;
    private bool _choiceOpen;

    /// <summary>这份档来自更新的版本，不许读也不许盖（rule_flags_save.md §4）。</summary>
    private bool _saveBlocked;

    /// <summary>自检跑完自己退出（Q-001）。</summary>
    private bool _quitAfterFrame;

    public override void _Ready()
    {
        _save = new SaveService(GodotSaveFileSystem.Create());

        LoadProgress();
        BuildUi();

        var args = OS.GetCmdlineUserArgs();

        try
        {
            _table = PointTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/points.csv"));

            // 对话表只解析一次：NpcTalkTable 出文本，DialogueBook 出分支。
            // 表里没有条件列时，两者内容完全一致（走原逻辑）。
            var talkCsv = FileAccess.GetFileAsString("res://Content/data/npcs.csv");
            _talks = NpcTalkTable.FromCsv(talkCsv);
            _book = DialogueBook.FromCsv(talkCsv);
            _world = new World(_table, _talks, _book);

            // 十二节拍：表是数据，状态机是 Core，接法是本票（S1-06c）。
            _beatTable = BeatTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/beats.csv"));

            // 状态变了就落盘：搜查后、对话后、换空间（rule_flags_save.md §2）
            _world.StateChanged += SaveGame;

            // 敲门：文本在世界那一层，这里只负责让玩家看见，并把 b02 记上
            _world.Knocked += OnKnocked;

            // 读档。坏档给一句人话，从新游戏开始——绝不崩、绝不白屏。
            LoadSave();

            // 节拍必须在读档之后造：旗标要先进 FlagStore，requires 才判得对
            // （S1-06 交接单的第二个坑：顺序写反，十二拍一起变 Locked）。
            _beats = PrologueBeats.FromState(_beatTable, _world.Flags, _loadedState);
            SettleKnock();
            GD.Print($"[节拍] 第 {_beats.Day} 天，已做 {_beats.Progress.DoneCount}/{_beats.Progress.TotalCount} 拍");

            foreach (var a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--hub=", StringComparison.Ordinal))
                    _world.Load(a[6..]);

            // 开发辅助：--beatskip=2 在内存里把前两拍做掉（**不落盘**），
            // 用来截"第 N 拍"那一帧，或验证某一处这会儿该弹什么。
            foreach (var a in args)
            {
                if (!a.StartsWith("--beatskip=", StringComparison.Ordinal)) continue;
                if (!int.TryParse(a[11..], out var skip)) continue;

                _saveBlocked = true;                 // 只改这一次运行，别把玩家那份档盖了
                for (var i = 0; i < skip && i < _beatTable.Count; i++)
                    _beats.Complete(_beatTable.All[i]);

                GD.Print($"[节拍·跳] 做到第 {skip} 拍 → 第 {_beats.Day} 天，当前 {_beats.Current?.Id ?? "无"}");
            }

            Mount();

            if (OS.GetCmdlineUserArgs().Contains("--highlight"))
                _view.ForceHighlight = true;

            GD.Print($"[举火] {_table.All.Count} 件可搜查物、{_talks.All.Count} 位可对话 NPC，当前 {_world.Hub}（{World.TitleOf(_world.Hub)}）有 {_world.Props.Count} 个");

            if (!_tut.Done)
                GD.Print($"[引导] 共 {_tutLineCount} 步，当前「{_tut.Line.Key}」");
        }
        catch (Exception e)
        {
            GD.PushError($"初始化失败：{e.Message}\n{e.StackTrace}");
            _hint.Text = $"初始化失败：{e.Message}";
        }

        // 开发辅助：--tutstep=2 直接把引导拨到第 N 步，--help 打开操作说明（都是为了截图）
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--tutstep=", StringComparison.Ordinal)) continue;
            if (!int.TryParse(a[10..], out var step)) continue;

            _tut.Reach((Onboarding.Stage)Math.Clamp(step, 0, 5));
            PaintTutorial();
        }

        if (OS.GetCmdlineUserArgs().Contains("--help"))
            ToggleHelp();

        // 开发辅助：--tutrun 把五步一次走完，验证引导真的推得动、进度真的落盘
        if (OS.GetCmdlineUserArgs().Contains("--tutrun") && _world is not null)
            RunTutorialOnce();

        // 开发辅助：--audit 逐关自检"有没有够不着的东西"，然后退出
        if (OS.GetCmdlineUserArgs().Contains("--audit") && _world is not null)
        {
            AuditReach();

            // Q-001：自检跑完自己退出，不让 CI 挂在这儿。
            // 但截图（--shot）在后面，所以截图在场时不抢它的退出权——
            // TouchDemo() 当年就踩过"提前 return 把后面的逻辑一起跳过"这个坑。
            if (!OS.GetCmdlineUserArgs().Contains("--shot")) _quitAfterFrame = true;
        }

        // 开发辅助：--spy 把玩家挪到衙役眼皮底下，用来看视野和红晕
        if (OS.GetCmdlineUserArgs().Contains("--spy") && _world is not null)
        {
            _world.Player.Cell = new Vector2(20, 19);
            // 让他被看住半秒以上，红晕才会亮起来
            for (var i = 0; i < 45; i++) _world.Update(1f / 60f);
        }

        // 开发辅助：--nearnpc 站到第一个人旁边但不说话，用来看"E 交谈"提示
        if (OS.GetCmdlineUserArgs().Contains("--nearnpc") && _world is not null)
        {
            var t = _world.TalksHere().FirstOrDefault();
            if (t is not null)
            {
                _world.Player.Cell = new Vector2(t.X, t.Y + 0.9f);
                RefreshHint();
            }
        }

        // 开发辅助：--talk 站到第一个人旁边并说一句，用来看对话框
        if (OS.GetCmdlineUserArgs().Contains("--talk") && _world is not null)
        {
            var t = _world.TalksHere().FirstOrDefault();
            if (t is not null)
            {
                _world.Player.Cell = new Vector2(t.X + 0.9f, t.Y);
                var say = _world.Talk();
                if (say is not null) ShowText(say.Value.Head, say.Value.Body);
            }
        }

        // 开发辅助：--knock 在 hub1 连搜几件，验证"第 3 刻敲门、只敲一次"
        if (OS.GetCmdlineUserArgs().Contains("--knock") && _world is not null)
        {
            // 用一局全新的世界，别读玩家那份档：不然"已经敲过"的旗标会让这条自检看着像坏了
            var w = new World(_table, _talks, _book);
            var flag = KnockTimer.HappenedFlag;
            GD.Print($"[敲门] 进 {w.Hub}：刻 {w.KeUsed}/{w.KeBudget}，已敲 {w.Flags.Has(flag, w.Hub)}");

            for (var i = 0; i < 20 && !w.Flags.Has(flag, w.Hub) && w.Props.Count > 0; i++)
            {
                var p = w.Props[i % w.Props.Count];
                w.Player.Cell = p.Cell + new Vector2(0.9f, 0f);
                w.Search();
            }

            GD.Print($"[敲门] 搜完：刻 {w.KeUsed}/{w.KeBudget}，happened={w.Flags.Has(flag, w.Hub)}，话「{w.LastMessage}」");

            // 再搜三次，确认不会敲第二遍
            var before = w.KeUsed;
            for (var i = 0; i < 3 && w.Props.Count > 0; i++)
            {
                var p = w.Props[i % w.Props.Count];
                w.Player.Cell = p.Cell + new Vector2(0.9f, 0f);
                w.Search();
            }
            GD.Print($"[敲门] 再搜 3 次（{before}→{w.KeUsed} 刻），旗标仍 {w.Flags.Has(flag, w.Hub)}——没再敲");
        }

        // 开发辅助：--dialogue 验证"初见免费 → 再谈 1 刻 → 被打断过先说那句 → 记忆用掉就清"
        if (OS.GetCmdlineUserArgs().Contains("--dialogue") && _world is not null)
        {
            var w = new World(_table, _talks, _book);
            w.Load("hub2");

            var t = w.TalksHere().FirstOrDefault();
            if (t is not null)
            {
                w.Player.Cell = new Vector2(t.X, t.Y + 0.9f);

                var a = w.Talk();
                var keA = w.KeUsed;
                var b = w.Talk();
                var keB = w.KeUsed;

                var key = DialogueBook.KeyAt("hub2", t.X, t.Y);
                w.Flags.Set(DialogueFlags.Interrupted(key));
                var c = w.Talk();
                var keC = w.KeUsed;

                GD.Print($"[对话] {t.Name}：初见「{a?.Head}」{keA} 刻；再谈「{b?.Head}」{keB} 刻；打断后「{c?.Head}」{keC} 刻");
                GD.Print($"[对话] 打断那句 =「{Cut(c?.Body)}」；记忆已清 = {!w.Flags.Has(DialogueFlags.Interrupted(key), "hub2")}");
            }
        }

        // 开发辅助：--interrogate 试着盘问身边的人，验证入口是开是关、收几刻
        if (OS.GetCmdlineUserArgs().Contains("--interrogate") && _world is not null)
        {
            var t = _world.TalksHere().FirstOrDefault();
            if (t is not null)
            {
                _world.Player.Cell = new Vector2(t.X, t.Y + 0.9f);
                var before = _world.KeUsed;
                var r = _world.Interrogate();
                GD.Print($"[盘问] 可盘问={_world.CanInterrogate}，结果={(r is null ? "没问成：" + _world.LastTalkNote : r.Value.Head)}，刻 {before}→{_world.KeUsed}");
            }
        }

        if (OS.GetCmdlineUserArgs().Contains("--demo") && _world is not null)
            RunDemo();

        // 开发辅助：--chain 连续走完七个场景，验证 hub 切换
        if (OS.GetCmdlineUserArgs().Contains("--chain") && _world is not null)
        {
            for (var i = 0; i < 8; i++) OnExit();
            GD.Print($"[链] 最终停在 {_world.Hub}（{World.TitleOf(_world.Hub)}）");
        }

        // 开发辅助：--walk 模拟按住"左"走三秒，验证通道通不通
        if (OS.GetCmdlineUserArgs().Contains("--walk") && _world is not null)
        {
            var start = _world.Player.Cell;
            for (var i = 0; i < 180; i++)
            {
                _world.Move(-4.2f / 60f, 0f);
                _world.Update(1f / 60f);
            }
            GD.Print($"[走] 从 {start} 向左三秒 → {_world.Player.Cell}");
        }

        // 开发辅助：--touch 站到物件的斜角上按 E，专门验证"贴着却摸不到"
        if (OS.GetCmdlineUserArgs().Contains("--touch") && _world is not null)
            TouchDemo();

        // 开发辅助：--atexit 站到出口但不按 E，用来看提示
        if (OS.GetCmdlineUserArgs().Contains("--atexit") && _world is not null)
            _world.Player.Cell = SceneConfig.Of(_world.Hub).Exit;

        // 开发辅助：--exit 站到出口按一次 E，验证真的能进下一关
        if (OS.GetCmdlineUserArgs().Contains("--exit") && _world is not null)
        {
            var ex = SceneConfig.Of(_world.Hub).Exit;
            var from = _world.Hub;
            _world.Player.Cell = ex;

            _view.PressInteract();
            GD.Print($"[出口] 第一次 E：仍在 {_world.Hub}（{World.TitleOf(_world.Hub)}）");

            _view.PressInteract();       // 还有没看的地方时，第一下只是提醒
            GD.Print($"[出口] 第二次 E：{from} → {_world.Hub}（{World.TitleOf(_world.Hub)}）");
        }

        // 开发辅助：--beats 打印十二节拍这张表（当前进度、四态、当前那一拍），然后退出
        if (OS.GetCmdlineUserArgs().Contains("--beats"))
        {
            if (_beats is null)
            {
                GD.Print("[节拍] 没有节拍表：beats.csv 读不进来。");
            }
            else
            {
                // 带上玩家所在的 hub：当前那一拍在别处时，表里补一句该往哪走。
                foreach (var line in _beats.DescribeTable(_world?.Hub)) GD.Print(line);
            }

            _quitAfterFrame = true;
        }

        // 开发辅助：--savetest 写一份档 → 重读 → 逐字段比对 → 退出
        if (OS.GetCmdlineUserArgs().Contains("--savetest"))
        {
            RunSaveTest();
            _quitAfterFrame = true;
        }

        if (OS.GetCmdlineUserArgs().Contains("--shot"))
            _ = CaptureAndQuitAsync();
    }

    public override void _Process(double delta)
    {
        // 自检类调试参数跑完自己走人（Q-001）。放在这里而不是在 _Ready 里直接退，
        // 是为了不把这一帧后面的逻辑（截图之类）一起跳过。
        if (_quitAfterFrame)
        {
            _quitAfterFrame = false;
            GetTree().Quit();
            return;
        }

        if (_world is null) return;

        _time += (float)delta;
        TickTutorial((float)delta);

        // Ctrl 只按一下不算学会，按住半秒才推进
        if (Input.IsKeyPressed(Key.Ctrl)) _ctrlHeld += (float)delta;
        else _ctrlHeld = 0f;

        TickNudge((float)delta);
    }

    /// <summary>
    /// 窗口要关的时候：先把档写完再走。
    /// 存盘时机之一（rule_flags_save.md §2 第 7 条）。
    /// </summary>
    public override void _Notification(int what)
    {
        base._Notification(what);

        if (what != NotificationWMCloseRequest) return;

        SaveGame();
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey k || !k.Pressed || k.Echo) return;

        // 选项面板开着时，键盘归它：数字挑、回车落、Esc 稍后再说。
        if (_choiceOpen)
        {
            HandleChoiceKey(k.Keycode);
            return;
        }

        switch (k.Keycode)
        {
            case Key.H:
                ToggleHelp();
                break;

            case Key.Escape:
                if (_helpOpen) ToggleHelp();
                else if (_box.Visible) CloseBox();
                else ToggleHelp();
                break;
        }
    }

    /// <summary>选项面板的按键。数字键只认到选项个数为止，多按的不算。</summary>
    private void HandleChoiceKey(Key key)
    {
        switch (key)
        {
            case Key.Key1 or Key.Kp1: SelectChoice(0); break;
            case Key.Key2 or Key.Kp2: SelectChoice(1); break;
            case Key.Key3 or Key.Kp3: SelectChoice(2); break;
            case Key.Key4 or Key.Kp4: SelectChoice(3); break;

            case Key.Enter:
            case Key.KpEnter:
            case Key.E:
            case Key.Space:
                ConfirmChoice();
                break;

            case Key.Escape:
                // 先让他看完这一处再说。离开这里再回来，这一拍还会再问一次。
                CloseChoice(restoreTutorial: true);
                SetHint("这件事还等着你。", 3f);
                break;
        }
    }

    private void SelectChoice(int index)
    {
        var count = _choiceBeat?.Options.Length ?? 0;
        if (count == 0) return;              // 没有选项文案，只有"继续"
        if (index < 0 || index >= count) return;

        _choiceSel = index;
        PaintChoice();
    }

    // ————————————————————————— 新手引导 —————————————————————————

    private const int _tutLineCount = 5;

    /// <summary>
    /// 按玩家实际做过的事推进，不用"点下一步"。
    /// 每一步的条件都能在 World / MapView 里查到，不额外记状态。
    /// </summary>
    private void TickTutorial(float dt)
    {
        if (_tut.Done) return;

        switch (_tut.Current)
        {
            case Onboarding.Stage.Move when (_world.Player.Cell - _spawnCell).Length() > 0.9f:
                AdvanceTutorial();
                break;

            case Onboarding.Stage.Look when _ctrlHeld > 0.45f:
                AdvanceTutorial();
                break;

            case Onboarding.Stage.Search when _world.Searched.Count > 0:
                AdvanceTutorial();
                break;

            case Onboarding.Stage.Talk when !_world.TalksHere().Any():
                // 这一关没人说话（小院就是），别让玩家等一个永远不会来的提示
                AdvanceTutorial();
                break;

            case Onboarding.Stage.Talk when _world.Talked.Count > 0:
                AdvanceTutorial();
                break;

            case Onboarding.Stage.Leave when AtExitHere() || _world.NextHub is null:
                AdvanceTutorial();
                break;
        }
    }

    /// <summary>
    /// 35 秒还没翻过东西，就轻轻提醒一次 Ctrl。
    /// 只提醒一次、只在本处提醒——提示说两遍就变成唠叨。
    /// </summary>
    private void TickNudge(float dt)
    {
        if (_ctrlNudged || _tut.Current is Onboarding.Stage.Move or Onboarding.Stage.Look) return;
        if (_world.Searched.Count > 0 || _box.Visible || _helpOpen) return;

        _idleHere += dt;
        if (_idleHere < 35f) return;

        _ctrlNudged = true;
        SetHint("按住 Ctrl　·　这一处能碰的东西会一起亮起来", 5f);
    }

    /// <summary>
    /// --tutrun：按顺序把每步的完成条件造出来，看引导能不能一路走到「完成」。
    /// 这个不替代手测，但它能保证"某一步永远推不动"这种死锁不会悄悄上线。
    /// </summary>
    private void RunTutorialOnce()
    {
        for (var guard = 0; guard < 12 && !_tut.Done; guard++)
        {
            switch (_tut.Current)
            {
                case Onboarding.Stage.Move:
                    _world.Player.Cell += new Vector2(1.4f, 0f);
                    break;

                case Onboarding.Stage.Look:
                    _ctrlHeld = 1f;
                    break;

                case Onboarding.Stage.Search:
                    if (_world.Props.Count > 0)
                    {
                        _world.Player.Cell = _world.Props[0].Cell + new Vector2(0.9f, 0f);
                        _world.Search();
                    }
                    break;

                case Onboarding.Stage.Talk:
                    var t = _world.TalksHere().FirstOrDefault();
                    if (t is not null)
                    {
                        _world.Player.Cell = new Vector2(t.X, t.Y + 0.9f);
                        _world.Talk();
                    }
                    break;

                case Onboarding.Stage.Leave:
                    _world.Player.Cell = SceneConfig.Of(_world.Hub).Exit;
                    break;
            }

            TickTutorial(0f);
            GD.Print($"[引导·走一遍] → {_tut.Current}");
        }

        GD.Print(_tut.Done ? "[引导·走一遍] 完成" : $"[引导·走一遍] 卡在 {_tut.Current}");
    }

    private void AdvanceTutorial()
    {
        var next = _tut.Current + 1;
        _tut.Reach((Onboarding.Stage)next);

        if (_tut.Done)
        {
            _tutPanel.Visible = false;
            _progress.TutorialDone = true;
            SaveProgress();
            SetHint("按 H 随时看操作", 6f);
            GD.Print("[引导] 完成");
            return;
        }

        PaintTutorial();
    }

    private void PaintTutorial()
    {
        var (key, text) = _tut.Line;
        _tutPanel.Visible = !_tut.Done && !_box.Visible && !_helpOpen;
        _tutKey.Text = key;
        _tutText.Text = text;
    }

    private bool AtExitHere()
    {
        var exit = SceneConfig.Of(_world.Hub).Exit;
        return exit.X > 0 && (_world.Player.Cell - exit).Length() <= 1.15f;
    }

    // ————————————————————————— 场景 —————————————————————————

    /// <summary>换 hub：重新烘焙一张图，挂给同一个 MapView。</summary>
    private void Mount()
    {
        var baked = MapBaker.Bake(_world.Map, _world.Hub, ViewW, ViewH, MapTop, _world.Props);

        if (_view is null)
        {
            _view = new MapView();
            AddChild(_view);
            _view.Setup(_world, baked);

            _view.TextShown += ShowText;
            _view.TextClosed += CloseBox;
            _view.PromptChanged += t => SetHint(t);
            _view.NearChanged += OnNearChanged;
            _view.Exited += OnExit;
            _view.Searched += OnSearched;
            _view.InteractFallback = TrySearchBeatFallback;
        }
        else
        {
            _view.Rebind(baked);
            _box.Visible = false;
            _hint.Visible = true;
            _view.SetBoxOpen(false);
            _view.UiBlocking = _helpOpen;
        }

        // 换了一处，上一个空间没答的选项先收起来：它属于那一段路，不该跟着人走。
        CloseChoice(restoreTutorial: false);

        _spawnCell = _world.Player.Cell;
        _idleHere = 0f;
        _ctrlNudged = false;
        _progress.LastHub = _world.Hub;
        SaveProgress();

        UpdateChrome();
        RefreshHint();
        PaintTutorial();

        // 进这一处、正好有一拍要选：把面板推上来。
        TryShowChoice();
    }

    private void OnExit()
    {
        if (_world.TryExit())
        {
            Mount();
            SetHint($"到了。{World.TitleOf(_world.Hub)}", 1.8f);
        }
        else
        {
            _world.Say("前面没路了。");
            SetHint(_world.LastMessage ?? "");
        }
    }

    private void OnNearChanged(Interactable? near)
    {
        if (_box.Visible || _helpOpen) return;
        RefreshHint();
    }

    /// <summary>
    /// 底部提示的优先级：人说第一，东西第二，出口第三，其余给一句闲话。
    /// 这条顺序必须和"按 E 到底会发生什么"一致，否则提示就是在骗人。
    /// </summary>
    private void RefreshHint()
    {
        if (_time < _hintHoldUntil) return;

        var talk = _world.NearestSpeaker();
        if (talk is not null)
        {
            SetHint($"E 交谈　·　{talk.Name}");
            return;
        }

        var near = _view.Near;
        if (near is not null)
        {
            var mw = PropKinds.MeasureWord(near.Kind);
            SetHint($"E 搜查　·　{mw}{near.Point.Name}");
            return;
        }

        if (AtExitHere())
        {
            var next = _world.NextHub;
            var left = _world.UnsearchedHere;
            var where = next is null ? "E 离开这里" : $"E 离开 · 去{World.TitleOf(next)}";
            SetHint(left > 0 ? $"{where}　（还有 {left} 处没看）" : where);
            return;
        }

        SetHint(IdleHint());
    }

    private void SetHint(string text, float holdSeconds = 0f)
    {
        if (holdSeconds > 0f) _hintHoldUntil = _time + holdSeconds;
        if (_hint is not null) _hint.Text = text;
    }

    private string IdleHint()
    {
        var searched = _world.Props.Count(p => _world.IsSearched(p.Point.Id));
        return $"WASD 走动　·　E 搜查　·　按住 Ctrl 看全部　·　本处已搜 {searched}/{_world.Props.Count}";
    }

    private void ShowText(string head, string body)
    {
        _box.Visible = true;
        _boxHead.Text = head;
        _boxBody.Text = body;

        _view.SetBoxOpen(true);
        _view.UiBlocking = true;
        if (_tutPanel is not null) _tutPanel.Visible = false;
        _hint.Visible = false;          // 提示条会被对话框压住，干脆让开

        UpdateChrome();
    }

    private void CloseBox()
    {
        _box.Visible = false;
        if (_view is not null)
        {
            _view.SetBoxOpen(false);
            _view.UiBlocking = _helpOpen || _choiceOpen;
        }

        _hint.Visible = true;

        UpdateChrome();
        RefreshHint();
        PaintTutorial();
    }

    private void ToggleHelp()
    {
        _helpOpen = !_helpOpen;
        _help.Visible = _helpOpen;

        if (_view is not null) _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen;

        if (_helpOpen)
        {
            _tutPanel.Visible = false;
            SetHint("Esc 关掉这份说明");
        }
        else
        {
            PaintTutorial();
            RefreshHint();
        }
    }

    private void UpdateChrome()
    {
        if (_world is null) return;

        _title.Text = World.TitleOf(_world.Hub);
        var searched = _world.Props.Count(p => _world.IsSearched(p.Point.Id));
        _subtitle.Text = $"刻 {_world.KeUsed}/{_world.KeBudget}　·　已搜 {searched}/{_world.Props.Count}";

        if (_beatLine is not null)
            _beatLine.Text = _beats is null ? "" : _beats.HudLine(ShortHub(_world.Hub));
    }

    /// <summary>
    /// HUD 里的空间短名：标题是"逃亡路 · 废驿"，那一行只放得下后半截。
    /// 标题只有一份（<see cref="World.TitleOf"/>），这里只做截取，不再抄一张表。
    /// </summary>
    private static string ShortHub(string hub)
    {
        var title = World.TitleOf(hub);
        var i = title.LastIndexOf('·');
        return i >= 0 ? title[(i + 1)..].Trim() : title;
    }

    // ————————————————————————— 十二节拍 —————————————————————————

    /// <summary>
    /// 进这一处、正好有一拍要选：弹面板。已经做完的拍不再是 Available，所以不会重复弹。
    /// </summary>
    private void TryShowChoice()
    {
        if (_beats is null || _world is null || _choiceOpen || _box.Visible || _helpOpen) return;

        var beat = _beats.PendingChoice(_world.Hub);
        if (beat is null) return;

        ShowChoice(beat);
    }

    private void ShowChoice(Beat beat)
    {
        _choiceBeat = beat;
        _choiceSel = 0;
        _choiceOpen = true;

        var hasOptions = beat.Options.Length > 0;
        _choiceHead.Text = $"{PrologueBeats.OrdinalOf(_beats?.IndexOf(beat) ?? 0)}　{beat.Name}";

        for (var i = 0; i < MaxOptions; i++)
        {
            var label = _choiceItems[i];

            if (i == 0 && !hasOptions)
            {
                label.Visible = true;
                _choiceText[i] = "按 E 继续";
            }
            else if (i < beat.Options.Length)
            {
                label.Visible = true;
                _choiceText[i] = $"{i + 1}　{beat.Options[i]}";
            }
            else
            {
                label.Visible = false;
                _choiceText[i] = "";
            }
        }

        _choiceFoot.Text = hasOptions
            ? "1–4 选一项　·　Enter 确认　·　Esc 稍后再说"
            : "E 或 Enter 确认　·　Esc 稍后再说";

        // 面板高度跟着项数走：两三项时空着一大片会显得像没画完。
        var rows = hasOptions ? beat.Options.Length : 1;
        var footY = 50 + rows * 30 + 6;
        _choiceFoot.Position = new Vector2(16, footY);
        _choice.Size = new Vector2(ViewW - 208, footY + 28);
        _choice.Position = new Vector2(104, Mathf.Clamp(220 - (footY + 28) / 2f, 84, 140));

        _choice.Visible = true;
        _tutPanel.Visible = false;
        _hint.Visible = false;
        _view.UiBlocking = true;

        PaintChoice();
        GD.Print($"[节拍] 弹出 {beat.Id} {beat.Name}（{beat.Options.Length} 个选项）");
    }

    /// <summary>把选中那一项点亮。每次按键都重画，别指望控件自己会跳。</summary>
    private void PaintChoice()
    {
        for (var i = 0; i < MaxOptions; i++)
        {
            if (!_choiceItems[i].Visible) continue;

            var picked = i == _choiceSel;
            _choiceItems[i].Text = (picked ? "▸ " : "　 ") + _choiceText[i];
            _choiceItems[i].AddThemeColorOverride("font_color", picked
                ? new Color(0.96f, 0.82f, 0.48f, 0.98f)
                : new Color(0.78f, 0.75f, 0.66f, 0.90f));
        }
    }

    /// <summary>确认这一项：置 <c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>，完成这一拍，日子往前走。</summary>
    private void ConfirmChoice()
    {
        if (!_choiceOpen || _choiceBeat is null || _beats is null) return;

        var beat = _choiceBeat;
        var index = PrologueBeats.NormalizePick(beat, _choiceSel);
        var picked = beat.Options.Length > 0 && index < beat.Options.Length ? beat.Options[index] : "继续";

        var ok = _beats.Choose(beat, index);
        CloseChoice(restoreTutorial: true);

        if (!ok)
        {
            GD.PushWarning($"[节拍] {beat.Id} 选不进去（多半是状态已经变了）");
            return;
        }

        SetHint($"「{picked}」——你记下了。", 3.5f);
        AfterBeat(beat);
    }

    private void CloseChoice(bool restoreTutorial)
    {
        if (!_choiceOpen) return;

        _choiceOpen = false;
        _choiceBeat = null;
        _choice.Visible = false;

        _hint.Visible = !_box.Visible;
        if (_view is not null) _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen;

        if (restoreTutorial) PaintTutorial();
    }

    /// <summary>一拍做完之后：刷新那一行、落盘、补记敲门、看看有没有下一拍能弹。</summary>
    private void AfterBeat(Beat beat)
    {
        GD.Print($"[节拍] 完成 {beat.Id} {beat.Name} → 第 {_beats?.Day ?? 0} 天");

        UpdateChrome();
        SaveGame();
        SettleKnock();
    }

    /// <summary>
    /// 门敲过了、但当时前一拍还没做完（b02 的前置是 b01 焚账）。
    /// 玩家后来补上 b01，这一拍不该就这么丢了——这里把它接上。
    /// </summary>
    private void SettleKnock()
    {
        if (_beats is null || _world is null) return;

        var late = _beats.PendingKnockLate(_world.Hub);
        if (late is null) return;
        if (!_beats.Complete(late)) return;

        GD.Print($"[节拍] 补记 {late.Id} {late.Name} → 第 {_beats.Day} 天");
        UpdateChrome();
        SaveGame();
    }

    /// <summary>
    /// 敲门这一下（<see cref="World.Knocked"/>）。文本在世界那层，这里只负责让玩家看见、
    /// 并把 b02 记上。读档时 World.Load 也会补敲一次，那时状态机可能还没造好，所以空判。
    /// </summary>
    private void OnKnocked()
    {
        SetHint(_world.LastMessage ?? "", 4f);

        var beat = _beats?.PendingKnock(_world.Hub);
        if (beat is not null && _beats!.Complete(beat))
        {
            GD.Print($"[节拍] 敲门完成 {beat.Id} {beat.Name} → 第 {_beats.Day} 天");
            UpdateChrome();
            SaveGame();
        }
    }

    /// <summary>搜到东西了：这一处要是有"搜到某个关键点"的拍，就记上（b08 空仓 → 仓门）。</summary>
    private void OnSearched(string pointId)
    {
        if (_beats is null || _world is null) return;

        var beat = _beats.PendingSearch(_world.Hub, pointId);
        if (beat is null) return;
        if (!_beats.Complete(beat)) return;

        GD.Print($"[节拍] 搜到 {pointId} 完成 {beat.Id} {beat.Name} → 第 {_beats.Day} 天");
        UpdateChrome();
        SaveGame();
    }

    /// <summary>
    /// 退化路径：这一处有一拍 search，但表里没给它指定点位——按 E 就算做完了。
    /// 返回 true 表示这一次 E 被接住了。
    /// </summary>
    private bool TrySearchBeatFallback()
    {
        if (_beats is null || _world is null) return false;

        var beat = _beats.PendingSearchFallback(_world.Hub);
        if (beat is null) return false;
        if (!_beats.Complete(beat)) return false;

        SetHint($"{beat.Name}　——　记下了。", 3f);
        AfterBeat(beat);
        return true;
    }

    // ————————————————————————— 存档 —————————————————————————

    /// <summary>
    /// 启动时读档。
    ///
    /// 坏档不崩、不白屏：坏档改名 .corrupt-*，再试备份，都不行就从新游戏开始，
    /// 手记里给玩家一句人话（rule_flags_save.md §3.2）。
    /// </summary>
    private void LoadSave()
    {
        var result = _save.LoadOrRecover();

        if (result.Blocked)
        {
            // 版本比当前新：拒绝载入，也不许拿新档盖掉它。
            _saveBlocked = true;
            GD.Print($"[存档] {result.Error}");
            SetHint("这份记录来自更新的版本，这一段路得重新走。", 6f);
            return;
        }

        if (result.State is not null)
        {
            _world.RestoreFrom(result.State);
            _loadedState = result.State;
            GD.Print($"[存档] 读到（{result.Origin}）：{_world.Hub}（{World.TitleOf(_world.Hub)}），刻 {_world.KeUsed}/{_world.KeBudget}，旗标 {_world.Flags.GlobalCount} 条，见过 {_world.Talked.Count} 个人");
            return;
        }

        if (result.Notice is not null)
        {
            GD.Print($"[存档] {result.Origin}：{result.Error}");
            SetHint(result.Notice, 6f);
        }
    }

    /// <summary>
    /// 存档。写不进去只打一条日志，不打断游戏——
    /// 存档是给玩家兜底的，不是拿来卡住他的。
    /// </summary>
    private void SaveGame()
    {
        if (_world is null) return;
        if (_saveBlocked) return;      // 版本过新的那份不许被盖

        var state = _world.ToSaveState();

        // 先写旗标（World.ToSaveState 已经把 FlagStore 抄进去了），再写"第几天 + 做完哪几拍"。
        // 反过来也不会错，但顺序写清楚，读档的人一眼能对上 S1-06 的语义表。
        _beats?.Machine.ToState(state);

        var (ok, error) = _save.TrySave(state);
        if (!ok) GD.PushWarning($"[存档] 没写成功：{error}");
    }

    /// <summary>
    /// --savetest：写一份档 → 重读 → 逐字段比对 → 打印结果。
    ///
    /// 走的是正式的 SaveService 与正式的文件动作，只是路径换成自检专用的那一份，
    /// 免得把玩家自己的档冲掉。
    /// </summary>
    private void RunSaveTest()
    {
        var service = new SaveService(
            GodotSaveFileSystem.Create(),
            "user://save.selftest.json",
            "user://save.selftest.tmp",
            "user://save.selftest.bak");

        var state = _world is not null ? _world.ToSaveState() : new SaveState();

        // 塞一点不是默认值的东西，比对才有东西可比
        state.Seed = 20260815;
        state.Day = 7;
        state.Player.X = 4.5;
        state.Player.Y = 20.25;
        state.Flags["has_medicine"] = true;
        state.Flags["took_temple_coin"] = false;
        state.Known.Clues.Add("knows_herb_path");
        state.Known.Npcs.Add(new NpcState { Hub = "hub3", Id = "A", Met = true, Again = 2, Quest = 1 });
        state.Inventory.Add(new InventoryEntry { Item = "bread", N = 1 });
        state.Inventory.Add(new InventoryEntry { Item = "shoe", N = 1 });
        state.Trust["prologue"] = new TrustState { Min = 12, Guan = 0, Yi = 8 };

        var village = new HubState { Entered = true, KeHalf = 14, Nudges = 1, CaughtCount = 1 };
        village.Searched.Add("bf_01");
        village.Searched.Add("bf_03");
        village.Flags["path_backhill_open"] = false;
        village.Patients["D"] = "alive";
        village.Patients["E"] = "dying";
        village.Patrol["G"] = new PatrolState { Wp = 2, T = 13.4 };
        state.Hubs["hub3"] = village;

        var (ok, error) = service.TrySave(state);
        if (!ok)
        {
            GD.Print($"写档 → 重读 → 写不进去：{error}");
            return;
        }

        var (back, loadError) = service.TryLoad();
        if (back is null)
        {
            GD.Print($"写档 → 重读 → 读不回来：{loadError}");
            return;
        }

        var diffs = SaveDiff.Compare(state, back);
        if (diffs.Count == 0)
        {
            GD.Print("写档 → 重读 → 一致");
            return;
        }

        GD.Print($"写档 → 重读 → 有 {diffs.Count} 处差异");
        foreach (var d in diffs) GD.Print($"[存自检] 差：{d}");
    }

    // ————————————————————————— 进度 —————————————————————————

    private void LoadProgress()
    {
        // 引导看过一次就不再挡人；--tutorial 可以强制重看，--no-tutorial 可以关掉
        var args = OS.GetCmdlineUserArgs();

        _progress = FileAccess.FileExists(ProgressPath)
            ? Progress.Parse(FileAccess.GetFileAsString(ProgressPath))
            : new Progress();

        _progress.Launches++;

        // 看过一次就不再挡人；--tutorial 强制重看，--no-tutorial 直接关掉。
        // 这里必须显式 Finish：否则"看过"的玩家会停在第一步，每次启动都被挡一下。
        if (args.Contains("--tutorial")) _tut.Reset();
        else if (_progress.TutorialDone || args.Contains("--no-tutorial")) _tut.Finish();
        else _tut.Reset();

        SaveProgress();
    }

    private void SaveProgress()
    {
        using var f = FileAccess.Open(ProgressPath, FileAccess.ModeFlags.Write);
        f?.StoreString(_progress.Serialize());
    }

    // ————————————————————————— UI —————————————————————————

    private void BuildUi()
    {
        var ui = new CanvasLayer { Layer = 10 };
        AddChild(ui);

        _font = LoadFont();

        // 左上铭牌
        var plate = new Panel
        {
            Position = new Vector2(12, 10),
            Size = new Vector2(196, 50),
        };
        plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.045f, 0.052f, 0.050f, 0.80f),
            BorderColor = new Color(0.62f, 0.24f, 0.18f, 0.90f),
            BorderWidthLeft = 2,
        });
        ui.AddChild(plate);

        _title = new Label
        {
            Position = new Vector2(24, 15),
            Size = new Vector2(176, 24),
            Text = "举火",
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_title, 15, new Color(0.93f, 0.87f, 0.70f, 0.96f));
        ui.AddChild(_title);

        _subtitle = new Label
        {
            Position = new Vector2(26, 38),
            Size = new Vector2(176, 16),
        };
        Style(_subtitle, 10, new Color(0.76f, 0.70f, 0.56f, 0.85f));
        ui.AddChild(_subtitle);

        // 节拍那一行：画面上沿，铭牌右边。玩家抬头就知道"现在该做什么"。
        _beatLine = new Label
        {
            Position = new Vector2(216, 20),
            Size = new Vector2(ViewW - 228, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_beatLine, 11, new Color(0.93f, 0.72f, 0.44f, 0.94f));
        ui.AddChild(_beatLine);

        // 新手引导条：一行，压在提示线上方；对话打开时让位
        _tutPanel = new Panel
        {
            Position = new Vector2(18, ViewH - 74),
            Size = new Vector2(ViewW - 36, 30),
        };
        _tutPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.060f, 0.058f, 0.86f),
            BorderColor = new Color(0.72f, 0.34f, 0.22f, 0.85f),
            BorderWidthLeft = 2,
        });
        ui.AddChild(_tutPanel);

        _tutKey = new Label
        {
            Position = new Vector2(12, 5),
            Size = new Vector2(88, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_tutKey, 11, new Color(0.94f, 0.74f, 0.42f, 0.96f));
        _tutPanel.AddChild(_tutKey);

        _tutText = new Label
        {
            Position = new Vector2(96, 5),
            Size = new Vector2(ViewW - 132, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_tutText, 11, new Color(0.86f, 0.82f, 0.72f, 0.94f));
        _tutPanel.AddChild(_tutText);

        _hint = new Label
        {
            Position = new Vector2(18, ViewH - 38),
            Size = new Vector2(ViewW - 36, 24),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_hint, 11, new Color(0.80f, 0.75f, 0.62f, 0.88f));
        ui.AddChild(_hint);

        BuildTextBox(ui);
        BuildHelp(ui);
        BuildChoice(ui);

        PaintTutorial();
        _help.Visible = false;
    }

    private void BuildTextBox(CanvasLayer ui)
    {
        _box = new Panel
        {
            // 三到四行：NPC 的"初见"最长有一百多字，两行会被裁掉半句
            Position = new Vector2(16, ViewH - 118),
            Size = new Vector2(ViewW - 32, 100),
            Visible = false,
        };
        _box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.062f, 0.060f, 0.96f),
            BorderColor = new Color(0.42f, 0.36f, 0.26f, 0.85f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        ui.AddChild(_box);

        _boxHead = new Label
        {
            Position = new Vector2(14, 7),
            Size = new Vector2(ViewW - 60, 17),
        };
        Style(_boxHead, 13, new Color(0.96f, 0.90f, 0.72f));
        _box.AddChild(_boxHead);

        _boxBody = new Label
        {
            Position = new Vector2(14, 28),
            Size = new Vector2(ViewW - 56, 64),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Style(_boxBody, 12, new Color(0.84f, 0.81f, 0.73f));
        _box.AddChild(_boxBody);

        // 明确告诉玩家怎么关：没有这句，很多人读完会卡在对话里
        var close = new Label
        {
            Position = new Vector2(ViewW - 132, 7),
            Size = new Vector2(96, 17),
            Text = "E / Esc 关掉",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Style(close, 9, new Color(0.58f, 0.55f, 0.46f, 0.80f));
        _box.AddChild(close);
    }

    private void BuildHelp(CanvasLayer ui)
    {
        _help = new Panel
        {
            Position = new Vector2(60, 62),
            Size = new Vector2(520, 300),
        };
        _help.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.050f, 0.055f, 0.053f, 0.96f),
            BorderColor = new Color(0.48f, 0.40f, 0.28f, 0.90f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
        });
        ui.AddChild(_help);

        var head = new Label
        {
            Position = new Vector2(22, 16),
            Size = new Vector2(480, 26),
            Text = "操作",
        };
        Style(head, 15, new Color(0.94f, 0.86f, 0.66f));
        _help.AddChild(head);

        var body = new Label
        {
            Position = new Vector2(22, 50),
            Size = new Vector2(478, 206),
            Text =
                "走　　W A S D ／ 方向键\n" +
                "搜　　E 或空格：搜查 · 交谈 · 离开\n" +
                "看　　按住 Ctrl：把这一处能碰的东西全框出来\n" +
                "选　　1–4 挑一项，Enter（或 E）落下\n" +
                "关　　Esc：关掉对话；什么都没开时打开这份说明\n\n" +
                "· 黄框 ＝ 能按 E。紫框的东西，得先知道它是什么才看得见。\n" +
                "· 出口是黄框加一根黄箭头，走过去按 E。还有没看的地方，会先拦你一次。\n" +
                "· 刻用完了不赶你走，只是天黑之前看不完了。\n" +
                "· 顶上那一行写着第几天、这一处叫什么、眼下该做什么。\n" +
                "· 有些事要做个决断，面板会自己推上来；Esc 可以先搁着，回头再问。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        Style(body, 12, new Color(0.84f, 0.81f, 0.73f));
        _help.AddChild(body);

        var foot = new Label
        {
            Position = new Vector2(22, 262),
            Size = new Vector2(478, 20),
            Text = "Esc / H　关掉",
        };
        Style(foot, 10, new Color(0.58f, 0.55f, 0.46f, 0.85f));
        _help.AddChild(foot);
    }

    /// <summary>
    /// 选项面板：册子里夹着的一张批条。进某一处、正好有一拍要选的时候推上来。
    /// 一屏最多四项（beats.csv 的 options 列现在最多四项），数字键挑、回车落。
    /// </summary>
    private void BuildChoice(CanvasLayer ui)
    {
        _choice = new Panel
        {
            Position = new Vector2(104, 116),
            Size = new Vector2(ViewW - 208, 196),
            Visible = false,
        };
        _choice.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.058f, 0.052f, 0.97f),
            BorderColor = new Color(0.70f, 0.30f, 0.22f, 0.92f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 2,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        ui.AddChild(_choice);

        _choiceHead = new Label
        {
            Position = new Vector2(16, 10),
            Size = new Vector2(ViewW - 240, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_choiceHead, 13, new Color(0.96f, 0.90f, 0.72f));
        _choice.AddChild(_choiceHead);

        var rule = new Label
        {
            Position = new Vector2(16, 32),
            Size = new Vector2(ViewW - 240, 12),
            Text = "—————",
        };
        Style(rule, 9, new Color(0.48f, 0.40f, 0.28f, 0.90f));
        _choice.AddChild(rule);

        for (var i = 0; i < MaxOptions; i++)
        {
            var item = new Label
            {
                Position = new Vector2(18, 50 + i * 30),
                Size = new Vector2(ViewW - 250, 26),
                Text = "",
                Visible = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(item, 13, new Color(0.78f, 0.75f, 0.66f, 0.90f));
            _choice.AddChild(item);
            _choiceItems[i] = item;
        }

        _choiceFoot = new Label
        {
            Position = new Vector2(16, 172),
            Size = new Vector2(ViewW - 240, 16),
            Text = "1–4 选一项　·　Enter 确认　·　Esc 稍后再说",
        };
        Style(_choiceFoot, 9, new Color(0.58f, 0.55f, 0.46f, 0.85f));
        _choice.AddChild(_choiceFoot);
    }

    private void Style(Label l, int size, Color color)
    {
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.70f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
        if (_font is not null) l.AddThemeFontOverride("font", _font);
    }

    private const string BundledFont = "res://Assets/Fonts/LXGWWenKai-Regular.ttf";

    private static FontFile? LoadFont()
    {
        // 优先用项目自带的霞鹜文楷（SIL OFL 1.1，可商用、可嵌入）。
        // 系统字体只作兜底：它能显示，但不能随游戏发行。
        if (ResourceLoader.Exists(BundledFont))
        {
            var f = GD.Load<FontFile>(BundledFont);
            if (f is not null) return f;
        }

        foreach (var path in new[] { "C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simsun.ttc" })
        {
            var f = new FontFile();
            if (f.LoadDynamicFont(path) == Error.Ok)
            {
                GD.PushWarning("正在使用系统字体，发行前必须换成 Assets/Fonts 下的 OFL 字体。");
                return f;
            }
        }

        GD.PushWarning("未找到中文字体，界面会显示成方块。");
        return null;
    }

    // ————————————————————————— 开发辅助 —————————————————————————

    /// <summary>
    /// 数据自检：每一关的每一件东西，四周必须有至少一个"站得住、而且从起点走得到"的格子。
    /// 只报"够不着"——那是玩家真的会卡住的地方，比任何截图都可靠。
    ///
    /// 判定只有一份实现：Core 的 <see cref="ReachAudit"/>（票号 S1-DIALOG-b）。
    /// 这里只做三件事：读地图、给起点、给"这格是不是被物件占着"的谓词。
    /// 以前 Main 自己写了一份只看局部的老口径，和 Core 那份迟早会漂，已经删掉。
    ///
    /// 坐标口径：<c>SearchPoint</c> 与 <c>ReachAudit</c> 都是 1-based，
    /// 减一只发生在 <see cref="MapGrid.WalkableCell"/> 内部——这里一次都不换算。
    /// </summary>
    private void AuditReach()
    {
        string[] order = { "hub1", "hub2", "hub3", "hub4", "hub5", "hub6", "hub7" };
        var total = 0;
        var bad = 0;

        foreach (var hub in order)
        {
            var map = MapGrid.Load(ProjectSettings.GlobalizePath($"res://Content/maps/{hub}.txt"));
            var points = _table.ForHub(hub).ToList();
            var spawn = SceneConfig.Of(hub).Spawn;

            var report = ReachAudit.Run(
                map,
                points,
                Mathf.RoundToInt(spawn.X),
                Mathf.RoundToInt(spawn.Y),
                BlocksHere);

            foreach (var p in report.Unreachable)
                GD.Print($"[够不着] {hub} {p.Id} {p.Name} ({p.X},{p.Y})");

            GD.Print($"[自检] {hub}：{report.Total} 件，其中够不着 {report.Unreachable.Count} 件");

            total += report.Total;
            bad += report.Unreachable.Count;
        }

        GD.Print($"[自检] 合计 {total} 件，够不着 {bad} 件");
    }

    /// <summary>
    /// 这件东西挡不挡路：名字 → 原型 → 挡不挡。规则本身只在 <see cref="PropKinds"/> 里有一份，
    /// 与 <c>World.PropBlocks</c> 同一口径，这里不另写判断。
    /// </summary>
    private static bool BlocksHere(SearchPoint p) => PropKinds.Blocks(PropKinds.Of(p.Name));

    /// <summary>
    /// --touch：随便找一件东西，站到它的斜角上按一次 E。
    /// 老规则（圆形半径 1.15）在这里一定失败——斜角距离 1.41。
    /// 这个方法不能 return，否则会把后面的 --shot 截图逻辑一起跳过。
    /// </summary>
    private void TouchDemo()
    {
        foreach (var p in _world.Props)
        {
            var cx = Mathf.RoundToInt(p.Cell.X);
            var cy = Mathf.RoundToInt(p.Cell.Y);

            foreach (var (dx, dy) in new[] { (-1, 1), (1, 1), (1, -1), (-1, -1) })
            {
                if (!_world.CellWalkable(cx + dx, cy + dy)) continue;

                _world.Player.Cell = new Vector2(cx + dx, cy + dy);
                var near = _world.Nearest();
                if (near is null || near.Point.Id != p.Point.Id) continue;

                var r = _world.Search();
                GD.Print($"[斜摸] {p.Point.Name} @({cx},{cy}) 站 @({cx + dx},{cy + dy}) → " +
                         (r is null ? "没反应" : r.Value.Text[..Mathf.Min(18, r.Value.Text.Length)]));

                if (r is not null) ShowText(p.Point.Name, r.Value.Text);
                return;
            }
        }

        GD.Print("[斜摸] 没找到可测的斜角位置");
    }

    /// <summary>--demo：把玩家挪到第一件东西旁边并搜一次，用来看完整画面。</summary>
    private void RunDemo()
    {
        if (_world.Props.Count == 0) return;

        var target = _world.Props
            .Where(p => p.Point.Layer != "knowledge" && !p.Point.Id.Contains("_e"))
            .OrderBy(p => p.Cell.X + p.Cell.Y)
            .FirstOrDefault() ?? _world.Props[0];

        _world.Player.Cell = target.Cell + new Vector2(0.85f, 0.15f);
        var result = _world.Search();
        if (result is not null)
        {
            var (text, _, prop) = result.Value;
            ShowText($"{prop.Point.Name}　　{LayerLabel(prop.Point.Layer)}", text);
        }

        UpdateChrome();
    }

    private static string LayerLabel(string layer) => layer switch
    {
        "close" => "细看",
        "knowledge" => "？",
        _ => "明面",
    };

    /// <summary>截一小段台词打日志。台词里有换行，整段打出来会把自检刷屏。</summary>
    private static string Cut(string? text, int n = 16)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var flat = text!.Replace("\n", " ");
        return flat.Length <= n ? flat : flat[..n] + "…";
    }

    private async Task CaptureAndQuitAsync()
    {
        try
        {
            for (var i = 0; i < 6; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            var img = GetViewport().GetTexture().GetImage();
            var err = img.SavePng("res://shot.png");
            GD.Print($"[举火] 截图结果：{err}");
        }
        catch (Exception e)
        {
            GD.PushError($"截图失败：{e.Message}");
        }
        finally
        {
            GetTree().Quit();
        }
    }
}
