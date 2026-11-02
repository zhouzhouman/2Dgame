using System;
using System.Collections.Generic;
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
///   I              打开 / 关上囊（12 格）
///   G              对话里把囊中之物给他看
///   T              对话里跟商人买卖（布商 = 官市，掮客 = 黑市）
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
    private ItemCatalog _items = default!;
    private GiftTable _gifts = default!;
    private World _world = default!;
    private MapView _view = default!;

    private Label _title = default!;
    private Label _subtitle = default!;
    private Label _hint = default!;
    private Panel _box = default!;
    private Label _boxHead = default!;
    private Label _boxBody = default!;
    private Label _boxFoot = default!;
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

    // —— 囊 ——
    /// <summary>囊的面板：12 格、方向键选、X / Del 丢。</summary>
    private Panel _bag = default!;
    private Label _bagHead = default!;
    private Label _bagCount = default!;
    private Label _bagFoot = default!;
    private readonly Label[] _bagCells = new Label[Inventory.DefaultCapacity];
    private readonly Panel[] _bagFrames = new Panel[Inventory.DefaultCapacity];
    private BagPanel? _bagPanel;
    private bool _bagOpen;

    /// <summary>开着的是"给他看"，不是自己翻囊。</summary>
    private bool _bagShowing;

    /// <summary>"给他看"是跟谁。gifts.csv 的 npc 列写的是显示名。</summary>
    private string _bagNpc = "";

    /// <summary>从对话框里点开的——关上之后把对话还回去。</summary>
    private bool _bagFromBox;

    /// <summary>HUD 那一行平时 / 快满时的颜色。</summary>
    private static readonly Color BeatInk = new(0.93f, 0.72f, 0.44f, 0.94f);
    private static readonly Color BagInk = new(0.86f, 0.42f, 0.32f, 0.98f);

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

    // —— 双信任轴（S1-01c）——
    /// <summary>信任动作表（trust.csv）。</summary>
    private TrustTable _trustTable = default!;

    /// <summary>攻城路线表（routes.csv）。</summary>
    private RouteTable _routeTable = default!;

    /// <summary>三条轴的账本；三轴指向存档里的那一份。</summary>
    private TrustBook? _trustBook;

    /// <summary>变化提示：HUD 那一行下面，2 秒后淡出。</summary>
    private Label _trustToast = default!;
    private float _toastUntil;

    /// <summary>"信任"面板（Tab）。</summary>
    private Panel _trust = default!;
    private Label _trustHead = default!;
    private readonly Label[] _trustAxis = new Label[3];
    private readonly Label[] _trustValue = new Label[3];
    private readonly Panel[] _trustTrack = new Panel[3];
    private readonly Panel[] _trustFill = new Panel[3];
    private readonly Label[] _trustRoutes = new Label[4];
    private bool _trustOpen;

    // —— 买卖（S2-02c）——
    /// <summary>货架表（shops.csv）。</summary>
    private ShopTable _shopTable = default!;

    /// <summary>这一趟的买卖账；存货跟着它走，不随面板开关重置。</summary>
    private TradeBook? _trade;

    /// <summary>现在开着的那张柜台。</summary>
    private ShopScreen? _shopScreen;

    /// <summary>交易面板是可操作的面板，开着时键盘只归它。</summary>
    private Panel _shop = default!;
    private Label _shopHead = default!;
    private Label _shopMoney = default!;
    private Label _shopGate = default!;
    private Label _shopMsg = default!;
    private Label _shopFoot = default!;

    /// <summary>面板一次放得下几行；多出来的靠 <see cref="_shopTop"/> 滚。</summary>
    private const int ShopVisibleRows = 8;

    private readonly Label[] _shopRows = new Label[ShopVisibleRows];
    private int _shopTop;
    private bool _shopOpen;
    private bool _shopFromBox;

    /// <summary>这一局是 <c>--shop</c> 自检（同样不碰玩家那份档）。</summary>
    private bool _shopRun;

    /// <summary>提示停留多久之后开始淡出（秒）。</summary>
    private const float ToastHold = 2f;

    /// <summary>淡出用多久（秒）。</summary>
    private const float ToastFade = 0.45f;

    /// <summary>横条满格的像素宽。</summary>
    private const int BarWidth = 236;

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

    /// <summary>选项面板底下那行提示的常态文案（选中被门禁挡住时临时换成拒绝的那句）。</summary>
    private string _choiceFootDefault = "";

    /// <summary>这份档来自更新的版本，不许读也不许盖（rule_flags_save.md §4）。</summary>
    private bool _saveBlocked;

    /// <summary>自检跑完自己退出（Q-001）。</summary>
    private bool _quitAfterFrame;

    /// <summary>退出码：--playthrough 没走完时给 1，让 CI 一眼看见。</summary>
    private int _exitCode;

    /// <summary>这一局是通关跑批（不读不写玩家那份档）。</summary>
    private bool _playthrough;

    /// <summary>这一局是 <c>--bag</c> 自检（同样不碰玩家那份档）。</summary>
    private bool _bagRun;

    public override void _Ready()
    {
        _save = new SaveService(GodotSaveFileSystem.Create());

        var args = OS.GetCmdlineUserArgs();

        // 开发辅助：--playthrough 一局打通十二拍。**不读不写玩家那份档**——
        // 它只证明这条路走得完，不该把玩家的进度换成一份机器跑出来的。
        // 这一句要在 LoadProgress 之前：进度文件也不能被跑批改掉。
        _playthrough = args.Contains("--playthrough");
        if (_playthrough) _saveBlocked = true;

        // --bag 也一样：囊里装的是这一趟捡到的，不能掺进玩家的旧物。
        _bagRun = args.Contains("--bag");
        if (_bagRun) _saveBlocked = true;

        // --shop 只走内存里的一百文，别拿它改玩家的钱与信任。
        _shopRun = args.Contains("--shop");
        if (_shopRun) _saveBlocked = true;

        LoadProgress();
        BuildUi();


        try
        {
            _table = PointTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/points.csv"));

            // 对话表只解析一次：NpcTalkTable 出文本，DialogueBook 出分支。
            // 表里没有条件列时，两者内容完全一致（走原逻辑）。
            var talkCsv = FileAccess.GetFileAsString("res://Content/data/npcs.csv");
            _talks = NpcTalkTable.FromCsv(talkCsv);
            _book = DialogueBook.FromCsv(talkCsv);

            // 囊与"给他看"：两张表都是数据，规则在 Core，接线在本票（S1-03c）
            _items = ItemCatalog.FromCsv(FileAccess.GetFileAsString("res://Content/data/items.csv"));
            _gifts = GiftTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/gifts.csv"));

            _world = new World(_table, _talks, _book, _items, _gifts);
            _bagPanel = new BagPanel(_world.Bag, _items);

            // 十二节拍：表是数据，状态机是 Core，接法是本票（S1-06c）。
            _beatTable = BeatTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/beats.csv"));

            // 双信任轴：两张表都是数据，加多少、开不开门在 Core，接线与显示是本票（S1-01c）
            _trustTable = TrustTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/trust.csv"));
            _routeTable = RouteTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/routes.csv"));

            // 货架（S2-02c）：价钱与存货在表里，买卖规则在 TradeBook，面板在 Application
            _shopTable = ShopTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/shops.csv"));

            // 状态变了就落盘：搜查后、对话后、换空间（rule_flags_save.md §2）
            _world.StateChanged += SaveGame;

            // 敲门：文本在世界那一层，这里只负责让玩家看见，并把 b02 记上
            _world.Knocked += OnKnocked;

            // 读档。坏档给一句人话，从新游戏开始——绝不崩、绝不白屏。
            // 通关跑批与 --bag 从干净的一局起跑，旧档一律不认：不然跑出来的是玩家的进度。
            if (_playthrough) GD.Print("[通关] 不读存档，从第 1 天、0 拍开始");
            else if (_bagRun) GD.Print("[囊] 不读存档，从空囊开始");
            else if (!_shopRun) LoadSave();

            // 三轴只在存档里存一份：这里拿到的就是 SaveState.Trust["prologue"] 那个对象，
            // 账本就地改它，SaveGame 再原样交回去。没有条目时用存档默认值，不自己设初值。
            _trustBook = new TrustBook(TrustWiring.Restore(_loadedState), _trustTable, _routeTable);
            GD.Print($"[信任] 官望 {_trustBook.State.Guan} 民望 {_trustBook.State.Min} 疑心 {_trustBook.State.Yi}，"
                     + $"路线 {_trustBook.OpenRoutes().Count}/{_routeTable.Count} 开着");

            // 买卖账：只改囊、不改信任；路引旗标按当前所在的 hub 问。
            // 存货跟着这一本走——关掉面板再打开，卖掉的货不会又长回来。
            _trade = new TradeBook(
                _shopTable, _world.Bag, _items, _trustBook.State,
                name => _world.Flags.Has(name, _world.Hub));

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

        // 开发辅助：--playthrough 一局打通十二拍，把耗时摊开看，然后自己退出（S1-09）。
        // 走的是真路径：换空间走出口、搜查按 E、选择按面板确认、敲门靠刻自己走到触发线。
        if (_playthrough)
        {
            if (_world is not null && _beats is not null)
            {
                RunPlaythrough(args);
            }
            else
            {
                GD.Print("[通关] 初始化没完成（表读不进来），跑不了");
                _exitCode = 1;
            }

            _quitAfterFrame = true;
        }

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

        // 开发辅助：--bag 在 hub1 把可搜点依次搜一遍（走真实搜查入口），打印囊然后退出（S1-03c）
        if (_bagRun && _world is not null)
        {
            RunBagCheck();
            _quitAfterFrame = true;
        }

        // 开发辅助：--shop 打印两边货架、各买一升粮、再撞一次门槛，然后退出（S2-02c）
        if (_shopRun)
        {
            if (_shopTable is not null && _items is not null && _trustTable is not null && _routeTable is not null)
            {
                foreach (var line in ShopDemo.Run(_shopTable, _items, _trustTable, _routeTable))
                    GD.Print(line);
            }
            else
            {
                GD.Print($"{ShopReport.Prefix} 初始化没完成（shops.csv / items.csv / trust.csv 读不进来），跑不了");
                _exitCode = 1;
            }

            _quitAfterFrame = true;
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

        // 开发辅助：--trust=<id> 拿真表走一次 Apply，把 Explain 打出来（S1-01c）。
        foreach (var a in args)
        {
            if (!a.StartsWith("--trust=", StringComparison.Ordinal)) continue;
            RunTrustCheck(a[8..]);
        }

        // 开发辅助：--shotbag / --shottrust 开面板截一张就退，和 --shot 同一条截图路径。
        // 先把手上的选项收起来，免得截到两层面板叠在一起。
        if (args.Contains("--shotbag"))
        {
            CloseChoice(restoreTutorial: false);
            OpenBag(showing: false, npc: "");
            _ = CaptureAndQuitAsync();
        }
        else if (args.Contains("--shottrust"))
        {
            CloseChoice(restoreTutorial: false);
            OpenTrust();
            _ = CaptureAndQuitAsync();
        }
        else if (args.Contains("--shotshop"))
        {
            // 只为截图：借四十文看柜台，绝不落盘（玩家那份档还是原样）
            _saveBlocked = true;
            CloseChoice(restoreTutorial: false);
            if (_world is not null) _world.Bag.TryAdd(TradeBook.MoneyItem, 40, _items!, out _);
            // 塞完钱要刷一次 HUD：囊计数是"事件推"的，直接改背包没人通知它，
            // 截图里就会出「囊中铜钱 40」配「囊 0/12」这种自相矛盾（实测踩过）。
            UpdateChrome();
            OpenShop(ShopTable.Official);
            _ = CaptureAndQuitAsync();
        }
        else if (args.Contains("--shot"))
        {
            _ = CaptureAndQuitAsync();
        }
    }

    public override void _Process(double delta)
    {
        // 自检类调试参数跑完自己走人（Q-001）。放在这里而不是在 _Ready 里直接退，
        // 是为了不把这一帧后面的逻辑（截图之类）一起跳过。
        if (_quitAfterFrame)
        {
            _quitAfterFrame = false;
            GetTree().Quit(_exitCode);
            return;
        }

        if (_world is null) return;

        _time += (float)delta;
        TickTutorial((float)delta);
        TickTrustToast();

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

        // 柜台开着时，键盘归它：↑↓ 选行、Enter 成交、Tab 换块、Esc / T 关上。
        if (_shopOpen)
        {
            HandleShopKey(k.Keycode);
            return;
        }

        // 「信任」面板开着时，键盘归它：Tab / Esc 关上，别的键什么都不做
        // （走动与 E 由 MapView 的 UiBlocking 拦住，这里只需要别再分发）。
        if (_trustOpen)
        {
            HandleTrustKey(k.Keycode);
            return;
        }

        // 囊开着时，方向键归格子，E 归"给他看"，X 归"丢"。
        if (_bagOpen)
        {
            HandleBagKey(k.Keycode);
            return;
        }

        // 选项面板开着时，键盘归它：数字挑、回车落、Esc 稍后再说。
        if (_choiceOpen)
        {
            HandleChoiceKey(k.Keycode);
            return;
        }

        switch (k.Keycode)
        {
            case Key.I:
                ToggleBag();
                break;

            case Key.G:
                OpenGiftPanel();
                break;

            case Key.T:
                ToggleShop();
                break;

            case Key.Tab:
                ToggleTrust();
                break;

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

        // 跟人说话时才提"给他看"——搜东西搜出来的那一页没有这个入口
        if (_boxFoot is not null)
            _boxFoot.Text = _world.NearestSpeaker() is not null
                ? "G 给他看　·　E / Esc 关掉"
                : "E / Esc 关掉";

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
            // 囊还压在上面时不能放开走路
            _view.UiBlocking = _helpOpen || _choiceOpen || _bagOpen || _trustOpen;
        }

        _hint.Visible = true;

        UpdateChrome();
        RefreshHint();
        PaintTutorial();

        // 读完这一页，这一处等着你的决定就该推上来了。
        // 不然同一空间连着两拍（b05→b06、b08→b09、b11→b12）第二拍永远没机会弹——
        // 而七个空间只能往前走，走回去是不可能的。
        TryShowChoice();
    }

    private void ToggleHelp()
    {
        _helpOpen = !_helpOpen;
        _help.Visible = _helpOpen;

        if (_view is not null) _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _trustOpen;

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
        {
            var line = _beats is null ? "" : _beats.HudLine(ShortHub(_world.Hub));

            // 囊跟在节拍后面。快满了才变色——一直刺眼等于没提示。
            _beatLine.Text = (line + BagHud.Suffix(_world.Bag)).Trim();
            _beatLine.AddThemeColorOverride("font_color",
                BagHud.Warn(_world.Bag) ? BagInk : BeatInk);
        }
    }

    // ————————————————————————— 囊 —————————————————————————

    /// <summary>
    /// I：开合自己的囊。对话 / 说明 / 选项压着的时候不开——
    /// 玩家这会儿要看的是那一页，不是自己的东西。
    /// </summary>
    private void ToggleBag()
    {
        if (_world is null || _bagPanel is null) return;

        if (_bagOpen)
        {
            CloseBag(restoreBox: true);
            return;
        }

        if (_box.Visible || _helpOpen || _choiceOpen || _trustOpen) return;

        OpenBag(showing: false, npc: "");
    }

    /// <summary>
    /// G：在对话里把囊打开，选中的那一样就是"给他看"的东西。
    /// 跟前得有人；没人的时候按 G 什么都不发生，别弹一个用不上的面板。
    /// </summary>
    private void OpenGiftPanel()
    {
        if (_world is null || _bagPanel is null || _bagOpen) return;
        if (!_box.Visible) return;

        var who = _world.NearestSpeaker();
        if (who is null) return;

        OpenBag(showing: true, npc: who.Name);
    }

    private void OpenBag(bool showing, string npc)
    {
        if (_bagPanel is null) return;

        _bagShowing = showing;
        _bagNpc = npc;
        _bagFromBox = _box.Visible;
        _bagOpen = true;
        _bagPanel.Select(0);

        // 两层册子不叠着放：囊压在上面时，先把对话收起来（关囊时还回去）
        if (_bagFromBox) _box.Visible = false;
        _hint.Visible = false;
        _tutPanel.Visible = false;

        _bag.Visible = true;
        _view.UiBlocking = true;

        PaintBag();
    }

    private void CloseBag(bool restoreBox)
    {
        if (!_bagOpen) return;

        _bagOpen = false;
        _bag.Visible = false;

        if (restoreBox && _bagFromBox) _box.Visible = true;

        _bagFromBox = false;
        _bagShowing = false;
        _bagNpc = "";

        _hint.Visible = !_box.Visible;
        if (_view is not null) _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _trustOpen;

        UpdateChrome();
        RefreshHint();
        PaintTutorial();
    }

    /// <summary>囊开着时的键盘。WASD 也能用——走动这时候本来就是停的。</summary>
    private void HandleBagKey(Key key)
    {
        switch (key)
        {
            case Key.Left or Key.A:
                MoveBag(-1, 0);
                break;

            case Key.Right or Key.D:
                MoveBag(1, 0);
                break;

            case Key.Up or Key.W:
                MoveBag(0, -1);
                break;

            case Key.Down or Key.S:
                MoveBag(0, 1);
                break;

            case Key.X or Key.Delete:
                DropSelected();
                break;

            case Key.E:
            case Key.Enter:
            case Key.KpEnter:
                if (_bagShowing) ConfirmShow();
                break;

            case Key.I:
            case Key.Escape:
                CloseBag(restoreBox: true);
                break;
        }
    }

    private void MoveBag(int dx, int dy)
    {
        if (_bagPanel is null) return;
        _bagPanel.Move(dx, dy);
        PaintBag();
    }

    /// <summary>丢一件。可叠的减一个，不可叠的腾出一格。</summary>
    private void DropSelected()
    {
        if (_bagPanel is null || _bagShowing) return;

        var item = _bagPanel.SelectedItem;
        if (item is null || !_bagPanel.Drop()) return;

        PaintBag();
        _bagFoot.Text = $"你丢下了{BagPickup.NameOf(_items, item)}。";
        UpdateChrome();
    }

    /// <summary>给他看：三种反应分开，落旗标由 World 转给 FlagStore。</summary>
    private void ConfirmShow()
    {
        if (_bagPanel is null || _world is null) return;

        var item = _bagPanel.SelectedItem;
        if (item is null) return;

        var result = _world.ShowItem(_bagNpc, item);
        var text = GiftPresenter.Reaction(result);
        var npc = _bagNpc;

        CloseBag(restoreBox: false);

        _box.Visible = true;
        _boxHead.Text = $"{npc}　　给他看";
        _boxBody.Text = text;
        _boxFoot.Text = "E / Esc 关掉";

        _view.SetBoxOpen(true);
        _view.SwallowInteract();      // 这一下 E 已经用掉了，别再让它把这句话关掉
        _view.UiBlocking = true;
        _hint.Visible = false;

        UpdateChrome();
    }

    /// <summary>把 12 格重画一遍。选中那格换框、换字色，其余照旧。</summary>
    private void PaintBag()
    {
        if (_bagPanel is null) return;

        _bagHead.Text = _bagShowing ? $"给他看　·　{_bagNpc}" : "囊";
        _bagCount.Text = _bagPanel.CountLabel;
        _bagFoot.Text = _bagShowing
            ? "方向键选一件　·　E 给他看　·　Esc / I 收起来"
            : "方向键选格　·　X / Del 丢一件　·　I / Esc 关上";

        for (var i = 0; i < _bagCells.Length; i++)
        {
            var label = _bagPanel.LabelAt(i);
            var picked = i == _bagPanel.Selected && label.Length > 0;

            _bagCells[i].Text = label.Length == 0 ? "" : (picked ? "▸ " : "　 ") + label;
            _bagCells[i].AddThemeColorOverride("font_color", picked
                ? new Color(0.97f, 0.86f, 0.55f, 0.99f)
                : new Color(0.84f, 0.81f, 0.73f, 0.94f));

            _bagFrames[i].AddThemeStyleboxOverride("panel", picked
                ? new StyleBoxFlat
                {
                    BgColor = new Color(0.14f, 0.12f, 0.08f, 0.90f),
                    BorderColor = new Color(0.86f, 0.66f, 0.34f, 0.95f),
                    BorderWidthTop = 1,
                    BorderWidthBottom = 1,
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                }
                : new StyleBoxFlat
                {
                    BgColor = new Color(0.045f, 0.050f, 0.048f, 0.70f),
                    BorderColor = new Color(0.34f, 0.30f, 0.24f, 0.85f),
                    BorderWidthTop = 1,
                    BorderWidthBottom = 1,
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                });
        }
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

    // ————————————————————————— 双信任轴 —————————————————————————

    /// <summary>
    /// 做一件带信任代价的事：改三条轴、把变化说给玩家、落盘。
    ///
    /// 现在只有 <c>--trust=&lt;id&gt;</c> 从这里走——十二拍里没有任何一处该加信任，
    /// 文档没给数值的地方不许自己编（S1-01c 的硬约束）。以后哪一拍要加减，
    /// 走同一个入口即可，提示与存档不用再写第二遍。
    /// </summary>
    /// <param name="id">trust.csv 的动作 id。</param>
    /// <returns>实际发生的增量。</returns>
    private TrustDelta ApplyTrust(string id)
    {
        if (_trustBook is null) return TrustDelta.None;

        var delta = _trustBook.Apply(id);
        if (!delta.Applied) return delta;

        _trustTable.TryGet(id, out var action);
        ShowTrustToast(TrustText.Toast(_trustBook, action, delta));

        UpdateChrome();
        SaveGame();
        return delta;
    }

    /// <summary>把那一条提示亮起来，2 秒后交给 <see cref="TickTrustToast"/> 淡出。</summary>
    /// <param name="text">已经拼好的一行字。</param>
    private void ShowTrustToast(string text)
    {
        if (_trustToast is null || string.IsNullOrEmpty(text)) return;

        _trustToast.Text = text;
        _trustToast.Visible = true;
        _trustToast.Modulate = new Color(1f, 1f, 1f, 1f);
        _toastUntil = _time + ToastHold;
    }

    /// <summary>
    /// 提示的淡出。停满 2 秒之后才开始透明，最后 0.45 秒线性收掉——
    /// 一下就消失的提示，玩家低头看一眼脚下就错过了。
    /// </summary>
    private void TickTrustToast()
    {
        if (_trustToast is null || !_trustToast.Visible) return;

        var left = _toastUntil - _time;
        if (left <= 0f)
        {
            _trustToast.Visible = false;
            return;
        }

        var alpha = left >= ToastFade ? 1f : left / ToastFade;
        _trustToast.Modulate = new Color(1f, 1f, 1f, alpha);
    }

    /// <summary>Tab：开合「信任」面板。对话 / 说明 / 选项 / 囊压着的时候不开。</summary>
    private void ToggleTrust()
    {
        if (_trustBook is null) return;

        if (_trustOpen)
        {
            CloseTrust();
            return;
        }

        if (_box.Visible || _helpOpen || _choiceOpen || _bagOpen) return;

        OpenTrust();
    }

    /// <summary>打开面板：三条横条、两条路线，顺手把走动与 E 停下来。</summary>
    private void OpenTrust()
    {
        if (_trustBook is null)
        {
            GD.Print("[信任] 没有信任表：trust.csv 读不进来。");
            return;
        }

        _trustOpen = true;
        _trust.Visible = true;
        _tutPanel.Visible = false;
        _hint.Visible = false;
        if (_view is not null) _view.UiBlocking = true;

        PaintTrust();
    }

    private void CloseTrust()
    {
        if (!_trustOpen) return;

        _trustOpen = false;
        _trust.Visible = false;

        _hint.Visible = !_box.Visible;
        if (_view is not null)
            _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _bagOpen || _trustOpen;

        PaintTutorial();
        RefreshHint();
    }

    /// <summary>面板开着时的键盘：Tab / Esc 关上，其余一律不动——面板就是拿来读的。</summary>
    private void HandleTrustKey(Key key)
    {
        switch (key)
        {
            case Key.Tab:
            case Key.Escape:
                CloseTrust();
                break;
        }
    }

    /// <summary>把三条横条与两行路线画成现在的样子。</summary>
    private void PaintTrust()
    {
        if (_trustBook is null) return;

        var panel = new TrustPanel(_trustBook);

        var bars = panel.Bars();
        for (var i = 0; i < _trustAxis.Length && i < bars.Count; i++)
        {
            var bar = bars[i];
            _trustAxis[i].Text = bar.Name;
            _trustValue[i].Text = bar.Value.ToString();
            _trustFill[i].Size = new Vector2(Math.Max(0f, BarWidth * bar.Ratio), 8f);
        }

        var lines = panel.RouteLines();
        var open = panel.RouteOpen();

        for (var i = 0; i < _trustRoutes.Length; i++)
        {
            var label = _trustRoutes[i];
            if (i >= lines.Count)
            {
                label.Visible = false;
                continue;
            }

            label.Visible = true;
            label.Text = lines[i];
            label.AddThemeColorOverride("font_color", open[i]
                ? new Color(0.88f, 0.79f, 0.52f, 0.96f)
                : new Color(0.66f, 0.63f, 0.55f, 0.82f));
        }
    }

    // ————————————————————————— 买卖 —————————————————————————

    /// <summary>
    /// T：跟身边的商人做买卖。布商守官市、掮客守黑市（<see cref="ShopWiring"/>）。
    ///
    /// 只有正在跟人说话时才开：柜台是从对话里推开的，不是凭空站在街上按一下就有。
    /// 旁边没商人也得说话——不然玩家会以为 T 坏了。
    /// </summary>
    private void ToggleShop()
    {
        if (_shopOpen)
        {
            CloseShop(restoreBox: true);
            return;
        }

        if (_trade is null || _trustBook is null || _shopTable is null) return;

        var who = _box.Visible ? _world?.NearestSpeaker() : null;
        var shop = ShopWiring.ShopOf(who?.Name);

        if (shop is null || who is null)
        {
            SetHint(ShopWiring.NoMerchantText, 3f);
            return;
        }

        OpenShop(shop);
    }

    /// <summary>推开柜台：对话先收起来（关柜台时还回去），走动与 E 一并停住。</summary>
    /// <param name="shop">official 或 black。</param>
    private void OpenShop(string shop)
    {
        if (_trade is null || _trustBook is null || _world is null) return;

        _shopScreen = new ShopScreen(_shopTable, _trade, _world.Bag, _items, _trustBook, shop);
        _shopFromBox = _box.Visible;
        _shopOpen = true;
        _shopTop = 0;

        if (_shopFromBox) _box.Visible = false;

        _hint.Visible = false;
        _tutPanel.Visible = false;
        _shop.Visible = true;

        if (_view is not null) _view.UiBlocking = true;

        PaintShop();
    }

    /// <summary>关柜台。从对话里点开的就还回对话，别让玩家关完发现自己站在街上。</summary>
    /// <param name="restoreBox">把先前那页对话还回去。</param>
    private void CloseShop(bool restoreBox)
    {
        if (!_shopOpen) return;

        _shopOpen = false;
        _shop.Visible = false;
        _shopScreen = null;

        if (restoreBox && _shopFromBox)
        {
            _box.Visible = true;
            if (_view is not null) _view.SetBoxOpen(true);
        }

        _shopFromBox = false;

        if (_view is not null)
        {
            if (!_box.Visible) _view.SetBoxOpen(false);
            _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _bagOpen || _trustOpen;
        }

        _hint.Visible = !_box.Visible;
        RefreshHint();
        PaintTutorial();
        UpdateChrome();
    }

    /// <summary>柜台开着时的键盘。E 也能成交——对话框一直用 E，手指不必换地方。</summary>
    private void HandleShopKey(Key key)
    {
        switch (key)
        {
            case Key.Up or Key.W:
                MoveShop(-1);
                break;

            case Key.Down or Key.S:
                MoveShop(1);
                break;

            case Key.Tab:
                _shopScreen?.ToggleMode();
                _shopTop = 0;
                PaintShop();
                break;

            case Key.Enter or Key.KpEnter or Key.E or Key.Space:
                ConfirmShop();
                break;

            case Key.Escape or Key.T:
                CloseShop(restoreBox: true);
                break;
        }
    }

    private void MoveShop(int delta)
    {
        if (_shopScreen is null) return;
        _shopScreen.Move(delta);
        PaintShop();
    }

    /// <summary>
    /// 成交一件。成功的账由面板算（它拿 Core 的结果去加信任），
    /// 这里只管把那一句信任提示亮起来、把档落下去。
    /// </summary>
    private void ConfirmShop()
    {
        if (_shopScreen is null) return;

        var result = _shopScreen.Confirm();
        if (result is { Ok: true })
        {
            ShowTrustToast(_shopScreen.LastToast);
            UpdateChrome();
            SaveGame();
        }

        PaintShop();
    }

    /// <summary>把柜台重画一遍：买 / 卖两块、选中那行点灯、失败的话原样贴在底下。</summary>
    private void PaintShop()
    {
        if (_shopScreen is null) return;

        _shopHead.Text = _shopScreen.HeadLine;
        _shopMoney.Text = _shopScreen.MoneyLine;
        _shopGate.Text = _shopScreen.ThresholdLine;
        _shopFoot.Text = _shopScreen.FootLine;

        var rows = _shopScreen.Rows();
        var sel = _shopScreen.Selected;

        // 选中行滚进视野：八行之外的东西不是丢了，只是要按 ↓ 才看得到。
        if (sel < _shopTop) _shopTop = sel;
        if (sel >= _shopTop + ShopVisibleRows) _shopTop = sel - ShopVisibleRows + 1;
        _shopTop = Math.Clamp(_shopTop, 0, Math.Max(0, rows.Count - ShopVisibleRows));

        for (var i = 0; i < _shopRows.Length; i++)
        {
            var at = _shopTop + i;
            var label = _shopRows[i];

            if (at >= rows.Count)
            {
                label.Text = "";
                continue;
            }

            var row = rows[at];
            var picked = at == sel;

            label.Text = (picked ? "▸ " : "　 ") + row.Label;
            label.AddThemeColorOverride("font_color", picked
                ? new Color(0.96f, 0.82f, 0.48f, 0.99f)
                : row.Sellable
                    ? new Color(0.84f, 0.81f, 0.73f, 0.94f)
                    : new Color(0.56f, 0.53f, 0.46f, 0.72f));
        }

        _shopMsg.Text = _shopScreen.Message;
        _shopMsg.AddThemeColorOverride("font_color", _shopScreen.LastResult is { Ok: false }
            ? new Color(0.84f, 0.46f, 0.36f, 0.98f)
            : new Color(0.86f, 0.76f, 0.52f, 0.96f));
    }

    // ————————————————————————— 十二节拍 —————————————————————————

    /// <summary>
    /// 进这一处、正好有一拍要选：弹面板。已经做完的拍不再是 Available，所以不会重复弹。
    /// </summary>
    private void TryShowChoice()
    {
        if (_beats is null || _world is null || _choiceOpen || _box.Visible || _helpOpen) return;

        // --bag 只翻东西，不答决断：面板压上来会把后面的按键全吃掉。
        if (_bagRun) return;

        // 囊压在上面时也不弹：两层面板叠在一起，玩家不知道该听谁的。
        if (_bagOpen) return;

        // 「信任」面板同理。
        if (_trustOpen) return;

        // 柜台也压在上面：这时候推一张决断上来，键盘会被两层抢。
        if (_shopOpen) return;

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

        _choiceFootDefault = hasOptions
            ? "1–4 选一项　·　Enter 确认　·　Esc 稍后再说"
            : "E 或 Enter 确认　·　Esc 稍后再说";
        _choiceFoot.Text = _choiceFootDefault;

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

    /// <summary>
    /// 把选中那一项点亮。每次按键都重画，别指望控件自己会跳。
    ///
    /// 攻城那一拍里，「间」要官望够才走得通（design §8.6：间 = 内应）；
    /// 走不通的项灰着，选中时底下那行直接说为什么——玩家不该按了回车才知道白按。
    /// </summary>
    private void PaintChoice()
    {
        for (var i = 0; i < MaxOptions; i++)
        {
            if (!_choiceItems[i].Visible) continue;

            var picked = i == _choiceSel;
            var locked = TrustChoice.Locked(_trustBook, _choiceBeat, i);

            _choiceItems[i].Text = (picked ? "▸ " : "　 ") + _choiceText[i];
            _choiceItems[i].AddThemeColorOverride("font_color", locked
                ? new Color(0.46f, 0.44f, 0.40f, 0.58f)
                : picked
                    ? new Color(0.96f, 0.82f, 0.48f, 0.98f)
                    : new Color(0.78f, 0.75f, 0.66f, 0.90f));
        }

        _choiceFoot.Text = TrustChoice.RefusalFor(_trustBook, _choiceBeat, _choiceSel) ?? _choiceFootDefault;
    }

    /// <summary>确认这一项：置 <c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>，完成这一拍，日子往前走。</summary>
    private void ConfirmChoice()
    {
        if (!_choiceOpen || _choiceBeat is null || _beats is null) return;

        var beat = _choiceBeat;
        var index = PrologueBeats.NormalizePick(beat, _choiceSel);

        // 攻城选「间」：内应门没开就走不通。面板不关，让他换个法子——
        // 这一下要是被吞掉又不说话，玩家会以为按键失灵。
        if (TrustChoice.Locked(_trustBook, beat, index))
        {
            GD.Print($"[信任] {beat.Id} 第 {index + 1} 项「{beat.Options[index]}」走不通：{TrustChoice.Refusal}");
            PaintChoice();
            _choiceFoot.Text = TrustChoice.Refusal;
            return;
        }

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
        if (_view is not null) _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _trustOpen;

        if (restoreTutorial) PaintTutorial();
    }

    /// <summary>一拍做完之后：刷新那一行、落盘、补记敲门、看看有没有下一拍能弹。</summary>
    private void AfterBeat(Beat beat)
    {
        GD.Print($"[节拍] 完成 {beat.Id} {beat.Name} → 第 {_beats?.Day ?? 0} 天");

        UpdateChrome();
        SaveGame();
        SettleKnock();

        // 同一空间里下一拍也是选择型（b05→b06、b11→b12）：进来那一刻弹过一次就不会再弹，
        // 而玩家没有理由再"进一次"这个空间。这一拍落定之后立刻问下一拍。
        TryShowChoice();
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

        // 三条轴写回现有的 Trust["prologue"]：形状不变、不加字段。
        if (_trustBook is not null) TrustWiring.Store(state, _trustBook);

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

    /// <summary>
    /// <c>--trust=&lt;id&gt;</c>：拿真表走一次 <see cref="TrustBook.Apply"/>，把 Explain 打出来。
    ///
    /// 这一趟只动内存：<c>_saveBlocked</c> 先立起来，绝不自检结果盖掉玩家那份档。
    /// 无头时打一行就退（exit 0）；有窗口时留在画面上，方便 <c>--shot</c> 截到那条提示。
    /// </summary>
    /// <param name="id">trust.csv 的动作 id。</param>
    private void RunTrustCheck(string id)
    {
        if (_trustBook is null || _trustTable is null)
        {
            GD.Print($"{TrustReport.Prefix} 没有信任表：trust.csv 读不进来。");
            _exitCode = 1;
            _quitAfterFrame = true;
            return;
        }

        _saveBlocked = true;

        var delta = ApplyTrust(id);
        if (!delta.Applied)
        {
            GD.Print(TrustReport.UnknownLine(id));
            _quitAfterFrame = true;
            return;
        }

        if (_trustTable.TryGet(id, out var action) && action is not null)
            GD.Print(TrustReport.ActionLine(action, TrustText.Toast(_trustBook, action, delta)));

        if (DisplayServer.GetName() == "headless") _quitAfterFrame = true;
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
        // 通关跑批会把七个空间各走一遍（LastHub 一路变），别拿它去改玩家那份进度。
        if (_playthrough || _bagRun) return;

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

        // 三轴变化的那一条：HUD 那行下面一点，字小一号，淡出后不占地方。
        _trustToast = new Label
        {
            Position = new Vector2(216, 40),
            Size = new Vector2(ViewW - 228, 16),
            Visible = false,
        };
        Style(_trustToast, 10, new Color(0.90f, 0.80f, 0.56f, 0.96f));
        ui.AddChild(_trustToast);

        BuildTextBox(ui);
        BuildHelp(ui);
        BuildChoice(ui);
        BuildBag(ui);
        BuildTrust(ui);
        BuildShop(ui);

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
        _boxFoot = new Label
        {
            Position = new Vector2(ViewW - 236, 7),
            Size = new Vector2(200, 17),
            Text = "E / Esc 关掉",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Style(_boxFoot, 9, new Color(0.58f, 0.55f, 0.46f, 0.80f));
        _box.AddChild(_boxFoot);
    }

    private void BuildHelp(CanvasLayer ui)
    {
        _help = new Panel
        {
            Position = new Vector2(60, 40),
            Size = new Vector2(520, 374),
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
            Size = new Vector2(478, 284),
            Text =
                "走　　W A S D ／ 方向键\n" +
                "搜　　E 或空格：搜查 · 交谈 · 离开\n" +
                "看　　按住 Ctrl：把这一处能碰的东西全框出来\n" +
                "选　　1–4 挑一项，Enter（或 E）落下\n" +
                "囊　　I：打开 / 关上；方向键选格，X 或 Del 丢一件\n" +
                "示　　G：对话里把囊中之物给他看；T：跟商人买卖\n" +
                "信　　Tab：官望 / 民望 / 疑心，和两条攻城路线\n" +
                "关　　Esc：关掉这一页；什么都没开时打开这份说明\n\n" +
                "· 黄框 ＝ 能按 E。紫框的东西，得先知道它是什么才看得见。\n" +
                "· 囊只有十二格，没有仓库。装不下就丢一件，回来还能再搜。\n" +
                "· 给别人看东西，他认不认、你事先知不知道，是两种反应。\n" +
                "· 官望换内应开门，民望换民变开城——攻城时只能挑一扇。\n" +
                "· 出口是黄框加一根黄箭头，走过去按 E。还有没看的地方，会先拦你一次。\n" +
                "· 刻用完了不赶你走，只是天黑之前看不完了。\n" +
                "· 顶上一行写着第几天、这一处叫什么、眼下该做什么、囊还剩几格。\n" +
                "· 有些事要做个决断，面板会自己推上来；Esc 可以先搁着，回头再问。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        Style(body, 12, new Color(0.84f, 0.81f, 0.73f));
        _help.AddChild(body);

        var foot = new Label
        {
            Position = new Vector2(22, 338),
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

    /// <summary>
    /// 囊的面板：4 × 3 的十二格。自己翻囊时按 X 丢，对话里按 G 打开时选中的那一样
    /// 就是"给他看"的东西——两种用法共用这一张网。
    /// </summary>
    private void BuildBag(CanvasLayer ui)
    {
        const int cols = BagPanel.Columns;
        const int cellW = 100;
        const int cellH = 50;

        _bag = new Panel
        {
            Position = new Vector2(100, 90),
            Size = new Vector2(440, 244),
            Visible = false,
        };
        _bag.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.048f, 0.052f, 0.050f, 0.97f),
            BorderColor = new Color(0.66f, 0.50f, 0.28f, 0.92f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        ui.AddChild(_bag);

        _bagHead = new Label
        {
            Position = new Vector2(18, 12),
            Size = new Vector2(260, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_bagHead, 13, new Color(0.96f, 0.90f, 0.72f));
        _bag.AddChild(_bagHead);

        _bagCount = new Label
        {
            Position = new Vector2(340, 12),
            Size = new Vector2(84, 20),
            Text = "0/12",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_bagCount, 12, new Color(0.86f, 0.76f, 0.52f, 0.95f));
        _bag.AddChild(_bagCount);

        for (var i = 0; i < _bagCells.Length; i++)
        {
            var frame = new Panel
            {
                Position = new Vector2(18 + i % cols * cellW, 40 + i / cols * cellH),
                Size = new Vector2(cellW - 4, cellH - 4),
            };
            frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.045f, 0.050f, 0.048f, 0.70f),
                BorderColor = new Color(0.34f, 0.30f, 0.24f, 0.85f),
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
            });
            _bag.AddChild(frame);
            _bagFrames[i] = frame;

            var cell = new Label
            {
                Position = new Vector2(6, 5),
                Size = new Vector2(cellW - 18, cellH - 14),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(cell, 11, new Color(0.84f, 0.81f, 0.73f, 0.94f));
            frame.AddChild(cell);
            _bagCells[i] = cell;
        }

        _bagFoot = new Label
        {
            Position = new Vector2(18, 206),
            Size = new Vector2(404, 18),
            Text = "方向键选格　·　X / Del 丢一件　·　I / Esc 关上",
        };
        Style(_bagFoot, 10, new Color(0.60f, 0.56f, 0.47f, 0.88f));
        _bag.AddChild(_bagFoot);
    }

    /// <summary>
    /// 「信任」面板：三条 0–100 的横条加两行攻城路线。
    ///
    /// 只有显示，没有可操作的东西——所以面板开着时按键只认 Tab / Esc，
    /// 横条数值由 <see cref="PaintTrust"/> 现算，不在控件里存第二份。
    /// </summary>
    private void BuildTrust(CanvasLayer ui)
    {
        // 官望 秋褐、民望 墨青、疑心 朱砂：三条轴各有各的颜色，扫一眼就分得开。
        Color[] ink =
        {
            new(0.80f, 0.62f, 0.34f, 0.96f),
            new(0.44f, 0.63f, 0.57f, 0.96f),
            new(0.72f, 0.32f, 0.26f, 0.96f),
        };

        _trust = new Panel
        {
            Position = new Vector2(96, 104),
            Size = new Vector2(448, 252),
            Visible = false,
        };
        _trust.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.048f, 0.052f, 0.050f, 0.97f),
            BorderColor = new Color(0.62f, 0.48f, 0.28f, 0.92f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        ui.AddChild(_trust);

        _trustHead = new Label
        {
            Position = new Vector2(18, 12),
            Size = new Vector2(220, 20),
            Text = "信任",
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_trustHead, 13, new Color(0.96f, 0.90f, 0.72f));
        _trust.AddChild(_trustHead);

        var scale = new Label
        {
            Position = new Vector2(326, 12),
            Size = new Vector2(100, 20),
            Text = "0 – 100",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(scale, 9, new Color(0.56f, 0.53f, 0.45f, 0.85f));
        _trust.AddChild(scale);

        for (var i = 0; i < _trustAxis.Length; i++)
        {
            var y = 46 + i * 38;

            _trustAxis[i] = new Label
            {
                Position = new Vector2(18, y),
                Size = new Vector2(56, 18),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(_trustAxis[i], 12, new Color(0.86f, 0.82f, 0.72f, 0.96f));
            _trust.AddChild(_trustAxis[i]);

            _trustTrack[i] = new Panel
            {
                Position = new Vector2(80, y + 4),
                Size = new Vector2(236, 10),
            };
            _trustTrack[i].AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.10f, 0.11f, 0.10f, 0.90f),
                BorderColor = new Color(0.30f, 0.28f, 0.23f, 0.85f),
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
            });
            _trust.AddChild(_trustTrack[i]);

            _trustFill[i] = new Panel { Position = new Vector2(1, 1), Size = new Vector2(0, 8) };
            _trustFill[i].AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = ink[i],
                BorderWidthTop = 0,
                BorderWidthBottom = 0,
                BorderWidthLeft = 0,
                BorderWidthRight = 0,
            });
            _trustTrack[i].AddChild(_trustFill[i]);

            _trustValue[i] = new Label
            {
                Position = new Vector2(326, y),
                Size = new Vector2(62, 18),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(_trustValue[i], 12, new Color(0.90f, 0.84f, 0.68f, 0.96f));
            _trust.AddChild(_trustValue[i]);
        }

        for (var i = 0; i < _trustRoutes.Length; i++)
        {
            _trustRoutes[i] = new Label
            {
                Position = new Vector2(18, 168 + i * 22),
                Size = new Vector2(412, 18),
                VerticalAlignment = VerticalAlignment.Center,
                Visible = false,
            };
            Style(_trustRoutes[i], 11, new Color(0.66f, 0.63f, 0.55f, 0.82f));
            _trust.AddChild(_trustRoutes[i]);
        }

        var foot = new Label
        {
            Position = new Vector2(18, 220),
            Size = new Vector2(412, 16),
            Text = "Tab / Esc 关上",
        };
        Style(foot, 9, new Color(0.58f, 0.55f, 0.46f, 0.85f));
        _trust.AddChild(foot);
    }

    /// <summary>
    /// 柜台：上半买、下半卖，用 Tab 换块。每行一句"名字 ×数量 单价 n 文"，
    /// 顶部写囊中铜钱与官市门槛，底部留给成交结果 / 失败原因。
    ///
    /// 只有显示，账在 <see cref="ShopScreen"/>：面板不存第二份钱数，
    /// 每次重画都从囊里现取——否则卖掉一件之后抬头还挂着旧数，最难解释。
    /// </summary>
    private void BuildShop(CanvasLayer ui)
    {
        _shop = new Panel
        {
            Position = new Vector2(64, 66),
            Size = new Vector2(512, 312),
            Visible = false,
        };
        _shop.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.050f, 0.054f, 0.050f, 0.97f),
            BorderColor = new Color(0.72f, 0.56f, 0.30f, 0.94f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 2,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        ui.AddChild(_shop);

        _shopHead = new Label
        {
            Position = new Vector2(18, 10),
            Size = new Vector2(280, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_shopHead, 13, new Color(0.96f, 0.90f, 0.72f));
        _shop.AddChild(_shopHead);

        _shopMoney = new Label
        {
            Position = new Vector2(320, 10),
            Size = new Vector2(172, 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_shopMoney, 12, new Color(0.86f, 0.76f, 0.52f, 0.95f));
        _shop.AddChild(_shopMoney);

        _shopGate = new Label
        {
            Position = new Vector2(18, 32),
            Size = new Vector2(476, 18),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_shopGate, 10, new Color(0.74f, 0.70f, 0.60f, 0.92f));
        _shop.AddChild(_shopGate);

        var rule = new Label
        {
            Position = new Vector2(18, 52),
            Size = new Vector2(476, 10),
            Text = "—————",
        };
        Style(rule, 9, new Color(0.48f, 0.40f, 0.28f, 0.90f));
        _shop.AddChild(rule);

        for (var i = 0; i < _shopRows.Length; i++)
        {
            _shopRows[i] = new Label
            {
                Position = new Vector2(18, 64 + i * 24),
                Size = new Vector2(476, 22),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(_shopRows[i], 12, new Color(0.84f, 0.81f, 0.73f, 0.94f));
            _shop.AddChild(_shopRows[i]);
        }

        _shopMsg = new Label
        {
            Position = new Vector2(18, 258),
            Size = new Vector2(476, 18),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_shopMsg, 10, new Color(0.86f, 0.76f, 0.52f, 0.96f));
        _shop.AddChild(_shopMsg);

        _shopFoot = new Label
        {
            Position = new Vector2(18, 280),
            Size = new Vector2(476, 16),
            Text = "↑↓ 选行　·　Enter 买卖　·　Tab 切买 / 卖　·　Esc / T 关上",
        };
        Style(_shopFoot, 9, new Color(0.58f, 0.55f, 0.46f, 0.88f));
        _shop.AddChild(_shopFoot);
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

    /// <summary>
    /// <c>--bag</c>：在 hub1 走真实搜查入口把每一处翻一遍，打印囊里有什么。
    ///
    /// 囊满被拒也照常往下走——"拒收、这一处不算搜过"本身就是要看的东西之一。
    /// 不读不写玩家的档，跑完退出 0。
    /// </summary>
    private void RunBagCheck()
    {
        var picked = 0;

        // 进 hub1 可能先弹一个决断；它会把按键吃掉，先搁着
        if (_choiceOpen) CloseChoice(restoreTutorial: false);

        foreach (var p in _world.Props.ToList())
        {
            if (_choiceOpen) CloseChoice(restoreTutorial: false);

            if (!StandAt(p, out var why))
            {
                GD.Print($"[囊] {p.Point.Id} 站不到跟前：{why}");
                continue;
            }

            _view.PressInteract();
            CloseIfOpen();

            picked += _world.LastPicked.Count;

            if (_world.LastRejectedItem is { Length: > 0 } rejected)
                GD.Print(BagReport.RejectedLine(_items, rejected));
        }

        foreach (var line in BagReport.Lines(_world.Bag, _items, picked)) GD.Print(line);
    }

    /// <summary>截一小段台词打日志。台词里有换行，整段打出来会把自检刷屏。</summary>
    private static string Cut(string? text, int n = 16)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var flat = text!.Replace("\n", " ");
        return flat.Length <= n ? flat : flat[..n] + "…";
    }

    // ————————————————————————— 通关跑批（S1-09） —————————————————————————

    /// <summary>站到物件跟前时的候选格子：四正 + 四斜，和 <see cref="World.CanTouch"/> 同一个口径。</summary>
    private static readonly (int Dx, int Dy)[] Around =
    {
        (0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>
    /// --playthrough：从第 1 天、0 拍起，按表序把十二拍走完，打印每一拍的耗时并退出。
    ///
    /// 每一拍都走真实入口，不伪造内部状态：
    /// · 换空间 = <see cref="OnExit"/>（和 --chain 同一条路）；
    /// · 搜查 = <see cref="MapView.PressInteract"/>（玩家按 E 的那个方法）；
    /// · 选择 = <see cref="ConfirmChoice"/>（面板回车）；
    /// · 敲门 = 真实的刻消耗，让 <see cref="KnockTimer"/> 自己到点。
    /// 落盘在 _Ready 一开头就用 _saveBlocked 拦掉了，跑完也不碰玩家的档。
    /// </summary>
    /// <param name="args">命令行参数（认 --pick=N）。</param>
    private void RunPlaythrough(string[] args)
    {
        var options = PlaythroughOptions.Parse(args);

        var result = ProloguePlaythrough.Run(
            _beats!,
            beat => PlayOneBeat(beat, options.Pick),
            beat => (_world.KeUsedIn(beat.Hub), ClockTable.ForHub(beat.Hub) ?? 0));

        foreach (var step in result.Steps)
            GD.Print(ProloguePlaythrough.StepLine(step, result.Total));

        if (!result.Ok) GD.Print(ProloguePlaythrough.FailureLine(result));

        // 出门赶路的 2 刻记在离开的那一处，所以汇总要等全部走完再抄一遍账。
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var hub in ClockTable.Hubs) used[hub] = _world.KeUsedIn(hub);

        var budget = ProloguePlaythrough.Sum(used);
        GD.Print(ProloguePlaythrough.TotalLine(result, budget));
        GD.Print(ProloguePlaythrough.HubLine(budget));
        GD.Print(ProloguePlaythrough.ConclusionLine(result));

        if (!result.Ok) _exitCode = 1;
    }

    /// <summary>
    /// 走一拍：先挪到这一拍的空间，再按它的触发方式走真实入口。
    /// 返回 null = 这一拍做成了；返回一句话 = 为什么没成（跑批会停在这里）。
    /// </summary>
    private string? PlayOneBeat(Beat beat, int pick)
    {
        var walk = WalkTo(beat.Hub);
        if (walk is not null) return walk;

        return beat.Trigger switch
        {
            PrologueBeats.ChoiceTrigger => PlayChoiceBeat(beat, pick),
            PrologueBeats.KnockTrigger => PlayKnockBeat(beat),
            PrologueBeats.SearchTrigger => PlaySearchBeat(beat),
            _ => $"不认识的触发方式「{beat.Trigger}」",
        };
    }

    /// <summary>一关一关走到目标空间。走不到就说明白卡在哪一处。</summary>
    private string? WalkTo(string hub)
    {
        for (var guard = 0; _world.Hub != hub; guard++)
        {
            if (guard > 8) return $"走不到 {hub}（卡在 {_world.Hub}）";

            var from = _world.Hub;
            OnExit();

            if (string.Equals(_world.Hub, from, StringComparison.Ordinal))
                return $"{from} 没有出口，走不到 {hub}";
        }

        return null;
    }

    /// <summary>选择型节拍：等面板弹出来（进空间时已经弹过一次），按 --pick 选中，再确认。</summary>
    private string? PlayChoiceBeat(Beat beat, int pick)
    {
        if (!_choiceOpen) TryShowChoice();
        if (!_choiceOpen || _choiceBeat is null) return "选项面板没有弹出来";

        if (!string.Equals(_choiceBeat.Id, beat.Id, StringComparison.Ordinal))
            return $"弹出的是 {_choiceBeat.Id}，不是 {beat.Id}";

        _choiceSel = PrologueBeats.NormalizePick(beat, pick);
        ConfirmChoice();

        return _beats!.Machine.IsDone(beat) ? null : "确认之后这一拍没记上";
    }

    /// <summary>
    /// 搜查型节拍：把人挪到点位旁边，按一次 E（<see cref="MapView.PressInteract"/> →
    /// World.Search → Main.OnSearched），读完再按一次 E 关掉。
    /// </summary>
    private string? PlaySearchBeat(Beat beat)
    {
        var pointId = PrologueBeats.SearchPointOf(beat.Id);
        if (pointId is null) return "这一拍没有指定点位";

        var prop = _world.Props.FirstOrDefault(p => string.Equals(p.Point.Id, pointId, StringComparison.Ordinal));
        if (prop is null) return $"搜不到点位 {pointId}";

        if (!StandAt(prop, out var why)) return why;

        _view!.PressInteract();
        CloseIfOpen();

        return _beats!.Machine.IsDone(beat) ? null : $"按了 E，{beat.Id} 没记上";
    }

    /// <summary>
    /// 敲门型节拍：不碰 KnockTimer，靠真实的刻消耗等它自己到点——
    /// 每翻一件东西记 1 刻，<see cref="World.CheckKnock"/> 到线就敲，Knocked 事件把这一拍记上。
    /// </summary>
    private string? PlayKnockBeat(Beat beat)
    {
        for (var guard = 0; !_beats!.Machine.IsDone(beat); guard++)
        {
            if (guard > 16) return $"翻到 {_world.KeUsed}/{_world.KeBudget} 刻，门还没响";

            Interactable? target = null;
            foreach (var p in _world.Props)
            {
                if (p.Point.IsKnowledge) continue;      // 知识门没开时按 E 不记账，会死循环
                if (!StandAt(p, out _)) continue;
                target = p;
                break;
            }

            if (target is null) return "这一处没有站得到跟前的可翻之物";

            _view!.PressInteract();
            CloseIfOpen();
        }

        return null;
    }

    /// <summary>
    /// 站到这件东西跟前：挑一个走得过、按 E 真的能碰到它、而且不会被旁边的人截走的格子。
    /// 站不下就说清楚是哪一件。
    /// </summary>
    private bool StandAt(Interactable prop, out string why)
    {
        var cx = Mathf.RoundToInt(prop.Cell.X);
        var cy = Mathf.RoundToInt(prop.Cell.Y);

        foreach (var (dx, dy) in Around)
        {
            var x = cx + dx;
            var y = cy + dy;
            if (!_world.Walkable(x, y)) continue;

            _world.Player.Cell = new Vector2(x, y);
            if (_world.Nearest()?.Point.Id != prop.Point.Id) continue;
            if (_world.NearestSpeaker() is not null) continue;   // 人优先，会把东西挡掉

            why = "";
            return true;
        }

        why = $"点位 {prop.Point.Id} 旁边没有能站的地方";
        return false;
    }

    /// <summary>对话框开着时，E 是"看完了"——和玩家按第二下 E 一样。</summary>
    private void CloseIfOpen()
    {
        if (!_box.Visible) return;
        _view!.PressInteract();
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
