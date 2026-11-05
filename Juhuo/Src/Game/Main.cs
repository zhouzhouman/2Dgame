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
///   M              打开 / 关上地图（去别的空间，按路程扣刻）
///   G              对话里把囊中之物给他看
///   T              对话里跟商人买卖（布商 = 官市，掮客 = 黑市）
///   Esc            关掉对话；什么都没开时打开操作说明
///   H              操作说明
///
/// 新手引导不弹"点下一步"的窗：按玩家真的做了什么推进
/// （走出去 → 按 Ctrl → 翻东西 → 开地图 → 开囊 → 看三轴 → 说话 → 走到门口）。
/// 每一步都是他自己做的，所以不打断；八步没走完，不许出城。
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
    private Panel _plate = default!;
    private Panel _box = default!;
    private Label _boxHead = default!;
    private Label _boxBody = default!;
    private Label _boxFoot = default!;
    private FontFile? _font;

    // —— 新手引导 ——
    /// <summary>
    /// 八步教学（S4-01c）。教学表在 <c>_Ready</c> 里读，读到之前先是一台"不教"的空机器
    /// ——<c>LoadProgress</c> 会在表读进来之前就 Reset / Finish 它。
    /// </summary>
    private Onboarding _tut = new();

    private Progress _progress = new();
    private Panel _tutPanel = default!;
    private Label _tutKey = default!;
    private Label _tutText = default!;
    private Vector2 _spawnCell;
    private float _ctrlHeld;
    private float _time;

    // —— 教学闸门（S4-02）——
    /// <summary>教学条的常态底框；按错时换成 <see cref="_tutStyleHot"/> 闪 0.3 秒。</summary>
    private StyleBoxFlat? _tutStyleCold;
    private StyleBoxFlat? _tutStyleHot;

    /// <summary>闪到什么时候（<see cref="_time"/> 口径）；0 表示没在闪。</summary>
    private float _tutFlashUntil;

    /// <summary>Ctrl 上一帧按着没有：只在"刚按下"那一下问闸门，免得每帧刷提示。</summary>
    private bool _ctrlWasDown;

    /// <summary>这一下 Ctrl 被闸门拦了：不许高亮，也不许拿着它推"看"这一步。</summary>
    private bool _lookBlocked;

    // —— 常驻目标 + 开局目标卡（S4-01c）——
    /// <summary>目标表（goals.csv）：起手 / 已转段 / 十二拍走完三句，加每拍自己的 goal。</summary>
    private GoalTable? _goals;

    /// <summary>顶部那一行"目标　活过今夜"。与天数/刻分开一行，字号略大一点。</summary>
    private Label _goalLine = default!;

    /// <summary>目标行下面那行小字：<c>怎么去　…</c> / <c>在 县城 · 按 M 过去（2 刻）</c>（S4-03b）。</summary>
    private Label _hintLine = default!;

    /// <summary>beats.csv 的 target / hint 两列（S4-03b）。表里没这两列时是空表，照常能玩。</summary>
    private ObjectiveTable _objectives = ObjectiveTable.Empty;

    /// <summary>开局那张卡的状态机：2.5 秒 / 任意键自己走，不挡键。</summary>
    private readonly GoalCard _goalCard = new();
    private Panel _goalCardPanel = default!;
    private Label _goalCardHead = default!;
    private Label _goalCardBody = default!;

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

    // —— 地图（S3-01c）——
    /// <summary>面板模型：七行、选中哪一行、走不动的那句话。</summary>
    private MapScreen? _mapScreen;

    /// <summary>地图面板：标题、顶上一行、七行地名、一句话、底部按键。</summary>
    private Panel _map = default!;
    private Label _mapTitle = default!;
    private Label _mapHead = default!;
    private Label _mapMsg = default!;
    private Label _mapFoot = default!;
    private readonly Label[] _mapRows = new Label[7];
    private bool _mapOpen;

    /// <summary>这一局是从菜单「新的开始」进来的：造好世界后要发开局那点钱。</summary>
    private bool _newGame;

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

    // —— 招兵与攻城（S2-03c）——
    /// <summary>攻城四法（siege.csv）。</summary>
    private SiegeTable _siegeTable = default!;

    /// <summary>招兵三档（recruit.csv）。</summary>
    private RecruitTable _recruitTable = default!;

    /// <summary>四法的规则层。</summary>
    private SiegeBook? _siegeBook;

    /// <summary>攻城那一拍弹出来的面板；别的拍为 null。</summary>
    private SiegeScreen? _siegeScreen;

    /// <summary>
    /// 手里多少兵。**只在这里存一份**，落盘时写进 <c>prologue.troops</c>；
    /// 招兵加、攻城扣，都改这个字段。
    /// </summary>
    private int _troops;

    /// <summary>这一局是 <c>--siege</c> 自检（同样不碰玩家那份档）。</summary>
    private bool _siegeRun;

    /// <summary>信任面板底部那一行：<c>兵力 60</c>。</summary>
    private Label _trustTroops = default!;

    // —— 转段四页（S2-05c）——
    /// <summary>誓约表（oaths.csv）。</summary>
    private OathTable _oathTable = default!;

    /// <summary>国号表（realms.csv）。</summary>
    private RealmTable _realmTable = default!;

    /// <summary>转段结算器；表读不进来时为 null。</summary>
    private TransitionBook? _transitionBook;

    /// <summary>六位功臣（companions.csv）；读不进来时为 null，分封页整页跳过。</summary>
    private CompanionTable? _companionTable;

    /// <summary>分封账本：一人只封一次，每次开转段新起一本。</summary>
    private EnfeoffBook? _enfeoffBook;

    /// <summary>转段面板；没开时为 null。</summary>
    private TransitionScreen? _transitionScreen;

    /// <summary>
    /// 转段要写进 <c>prologue</c> 的那一份存档。**SaveGame 每次都要从这里抄四样**
    /// （Capital / Realm / Oaths / Debts）——SaveGame 另起一份 SaveState，不抄就白选了。
    /// </summary>
    private SaveState _transitionState = new();

    private Panel _transition = default!;
    private Label _transitionHead = default!;
    private Label _transitionFlash = default!;

    /// <summary>五页共用的一组行：定都七行、报告十三行（含"带走"与分封两行），按最长的那一页备。</summary>
    private readonly Label[] _transitionRows = new Label[13];
    private Label _transitionFoot = default!;
    private bool _transitionOpen;

    /// <summary>这一局是 <c>--transition</c> 自检（同样不碰玩家那份档）。</summary>
    private bool _transitionRun;

    /// <summary>这一局是 <c>--enfeoff</c> 自检：只算六个人的封赏账，打印四行就退。</summary>
    private bool _enfeoffRun;

    /// <summary>这一局是 <c>--effects</c> 自检：三条真实选项各结算一次，打印然后退出（S3-02c）。</summary>
    private bool _effectsRun;

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

    /// <summary>每行右边那半句（攻城才用：门槛缺什么 / 兵不够要撤）；其余拍收起来不占地方。</summary>
    private readonly Label[] _choiceNotes = new Label[MaxOptions];
    private readonly string[] _choiceText = new string[MaxOptions];
    private Label _choiceFoot = default!;
    private Beat? _choiceBeat;
    private int _choiceSel;
    private bool _choiceOpen;

    /// <summary>选项面板底下那行提示的常态文案（选中被门禁挡住时临时换成拒绝的那句）。</summary>
    private string _choiceFootDefault = "";

    /// <summary>
    /// 敲门 / 搜查触发的、带选项的那一拍（S3-02c）。
    ///
    /// 触发那一刻对话框正要弹出来，面板压上去会被下一句文本盖住，所以先记在这里，
    /// 由 <see cref="TryShowChoice"/> 在"面板终于放得下"的时候（读完这一页、教程走完、换空间）补弹。
    /// </summary>
    private string? _deferredChoiceId;

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

    /// <summary>这一次是不是自检/跑批：是的话一个玩家文件都不许写。</summary>
    private bool _selfCheckRun;

    // —— 主菜单（S3-00）——
    /// <summary>启动时的命令行。菜单选完之后还要给 <see cref="BootWorld"/> 用。</summary>
    private string[] _args = Array.Empty<string>();

    /// <summary>菜单模型：默认停哪一项、哪一项灰着，都由它说了算。</summary>
    private BootMenu? _bootMenu;

    /// <summary>菜单开着时为真：不走动、不推引导、键全归菜单。</summary>
    private bool _bootMenuOpen;

    private Panel _bootPanel = default!;
    private Label _bootTitle = default!;
    private Label _bootSub = default!;
    private readonly Label[] _bootRows = new Label[BootMenu.Count];
    private Panel _bootCursor = default!;
    private Label _bootNote = default!;
    private Label _bootFoot = default!;

    // —— 开场介绍（S3-01d）——
    /// <summary>开场白表（intro.csv）。读不进来时为 null，开场就不放。</summary>
    private IntroTable? _introTable;

    /// <summary>立绘参数表（portraits.csv）。读不进来时为 null，对话退回一行字。</summary>
    private PortraitTable? _portraits;

    /// <summary>正在放的这一段；没在放时为 null。</summary>
    private IntroFlow? _intro;

    /// <summary>开场开着：世界与键都归它，别的面板一律不许弹。</summary>
    private bool _introOpen;

    /// <summary>这一趟已经放过开场（看完或跳过）——不再放第二遍。</summary>
    private bool _introSeen;

    private Panel _introPanel = default!;
    private Label _introTop = default!;
    private Label _introOrdinal = default!;
    private Label _introName = default!;
    private Label _introBody = default!;
    private Label _introFoot = default!;
    private PortraitView _introPortrait = default!;

    /// <summary>对话框左侧的那张脸（128×128）；没有立绘时收起来。</summary>
    private PortraitView _boxPortrait = default!;

    public override void _Ready()
    {
        _save = new SaveService(GodotSaveFileSystem.Create());

        _args = OS.GetCmdlineUserArgs();
        ParseRunFlags(_args);

        // 无头自检：三行说完菜单的三种情形，然后走人（S3-00）。
        // 三行是跑出来的，不是写死的——第三行会在内存里真走一遍「新的开始」。
        // 放在 LoadProgress 之前：这条自检连玩家的 progress.cfg 都不该碰。
        if (_args.Contains("--bootmenu"))
        {
            foreach (var line in BootMenuSelfCheck.Lines()) GD.Print(line);
            _quitAfterFrame = true;
            return;
        }

        // 一次自检就是一次自检：带这些参数的运行不许碰玩家任何一份文件
        // （存档由各 _xxxRun 各自把关，进度与截图类在这里统一拦）。
        // 判据放宽到"只要带了 -- 参数就算自检"：逐个点名永远漏（--audit / --chain / --beats /
        // --savetest / --hub= 都漏过），漏一个就会往玩家的档里写一笔。
        // 只有这两个不算——它们不改变"这是一次正常启动"（见 WantsBootMenu 同一套口径）。
        _selfCheckRun = _args.Any(a => a.StartsWith("--", StringComparison.Ordinal)
                                       && a is not "--tutorial" and not "--no-tutorial");

        // 无头自检：把九行开场原样打出来，然后走人（S3-01d）。
        // 不建世界、不碰玩家任何一份文件——它验的就是"这段字读得出来、顺序没乱"。
        if (_args.Contains("--intro"))
        {
            RunIntroCheck();
            _quitAfterFrame = true;
            return;
        }

        // 教学表要在 LoadProgress 之前读：进度那一步会 Reset / Finish 这台步骤机
        // （"看过一次就不再挡人"这条规矩），表还没读进来就分不清八步是哪八步。
        // 读不进来只打一句警告、这一局不教——别让一张表把整局挡住（与 intro.csv 同一口径）。
        _tut = new Onboarding(TryLoadTutorialScript());

        LoadProgress();

        // 跑批不学走路：脚本自己会走位。教程没标完成的话，
        // TryShowChoice 会一直让路，通关脚本就一拍都答不了（门禁当场红过一次）。
        // S4-01c 之后多一条：八步没走完不许出城，所以"会按出口的跑批"也得先把教学记完——
        // 否则 --chain 这种一进来就 OnExit 八次的自检会被出口自己挡住（七关串联当场红）。
        // S4-02 之后 --exit 也算：它按的是真 E，闸门会把它拦在"这一步不是出门"上。
        if (_playthrough || _bagRun || _shopRun || _siegeRun || _transitionRun || _effectsRun
            || _enfeoffRun || _args.Contains("--chain") || _args.Contains("--walk") || _args.Contains("--demo")
            || _args.Contains("--exit"))
            _tut.Finish();

        BuildUi();

        // 只为截图：把菜单摆上来，截一张就退（和 --shotbag 同一条路）。
        if (_args.Contains("--shotmenu"))
        {
            OpenBootMenu();
            _ = CaptureAndQuitAsync();
            return;
        }

        // 玩家真的在玩：先给菜单，选完才造世界。世界还没造出来，HUD 只是一层底，被菜单盖着。
        if (WantsBootMenu(_args))
        {
            OpenBootMenu();
            return;
        }

        // 调试：--newgame 走一遍「新的开始」那条路（发开局钱、放开场、弹教程）。
        // 不碰玩家的档——带 -- 参数的运行本来就是自检（见 _selfCheckRun）。
        if (_args.Contains("--newgame"))
        {
            _newGame = true;
            _tut.Reset();
        }

        BootWorld(_args);
    }

    /// <summary>
    /// 造世界、读档、跑自检参数。菜单选完之后才进得来；
    /// 各类 <c>--playthrough / --bag / --shop / --siege / --transition</c> 自检不经过菜单，直接进来。
    /// </summary>
    private void BootWorld(string[] args)
    {
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
            var beatsCsv = FileAccess.GetFileAsString("res://Content/data/beats.csv");
            _beatTable = BeatTable.FromCsv(beatsCsv);

            // 目标点（S4-03b）：同一张 beats.csv 的 target / hint 两列。
            // 旧表没有这两列时读成空表——不崩、不挡路，只是没有黄点和提醒。
            _objectives = ObjectiveTable.FromCsv(beatsCsv);

            // 目标表（S4-01c）：HUD 那一行与开局卡都读这一份，代码里不留第二份中文。
            _goals = GoalTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/goals.csv"));

            // 双信任轴：两张表都是数据，加多少、开不开门在 Core，接线与显示是本票（S1-01c）
            _trustTable = TrustTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/trust.csv"));
            _routeTable = RouteTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/routes.csv"));

            // 货架（S2-02c）：价钱与存货在表里，买卖规则在 TradeBook，面板在 Application
            _shopTable = ShopTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/shops.csv"));

            // 招兵与攻城（S2-03c）：四法与三档都在表里，判定在 Core，面板在 Application
            _siegeTable = SiegeTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/siege.csv"));
            _recruitTable = RecruitTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/recruit.csv"));

            // 转段四页（S2-05c）：誓约表与国号表都是数据，结算在 Core，四页在 Application
            _oathTable = OathTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/oaths.csv"));
            _realmTable = RealmTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/realms.csv"));

            // 分封六功臣（S4-01d）：表是数据，账在 Core，这一页在 Application
            _companionTable = CompanionTable.FromCsv(
                FileAccess.GetFileAsString("res://Content/data/companions.csv"));

            // 开场白与立绘（S3-01d）：两张表都只读一次；读不进来只是不放开场 / 不挂立绘，
            // 不能让整局死在这里——别的表都用 FromCsv 直抛，这两张是"锦上添花"，退一步更好。
            _introTable = TryLoadIntroTable();
            _portraits = TryLoadPortraitTable();

            // 状态变了就落盘：搜查后、对话后、换空间（rule_flags_save.md §2）
            _world.StateChanged += SaveGame;

            // 敲门：文本在世界那一层，这里只负责让玩家看见，并把 b02 记上
            _world.Knocked += OnKnocked;

            // 读档。坏档给一句人话，从新游戏开始——绝不崩、绝不白屏。
            // 通关跑批与 --bag 从干净的一局起跑，旧档一律不认：不然跑出来的是玩家的进度。
            if (_playthrough) GD.Print("[通关] 不读存档，从第 1 天、0 拍开始");
            else if (_bagRun) GD.Print("[囊] 不读存档，从空囊开始");
            // --tutrun 也从干净的一局起跑：教程是"第一次玩"的那一课，读一份已经教过的档，
            // 八步一步都不会走，门禁就成了空转（一开始就是这样，S4-01c 顺手补上）。
            // 进度里"看过一次"的标记在这里不算数：这条自检要看的就是第一次那八步推不推得动。
            else if (_args.Contains("--tutrun"))
            {
                GD.Print("[引导] 不读存档，从第一步开始走一遍");
                _tut.Reset();
            }
            // 新档（_newGame）绝不读档：菜单那条路会先把旧档改名，但调试入口没改名，
            // 少了这一条就会把旧档读进来，于是"开局 30 文"永远发不出去（实测踩过）。
            else if (!_shopRun && !_siegeRun && !_transitionRun && !_effectsRun && !_newGame
                     && !_enfeoffRun && !_args.Contains("--shotenfeoff")
                     && !WantsMapDemo(_args) && !WantsIntroDemo(_args)) LoadSave();

            // 新档的开局资金（S3-01c）：钱数只有 StartKit 一份，这里只负责把它放进囊里。
            // 只在「新的开始」且确实没读到档时发——旧档的钱在 inventory 里，再塞一次就是白送。
            if (_newGame && _loadedState is null)
            {
                var coins = StartKit.Fill(_world.Bag, _items, out var starterError);
                GD.Print(starterError is null
                    ? $"[开局] 囊里放进 {coins} 文"
                    : $"[开局] 钱没放进：{starterError}");
            }

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

            // 攻城（S2-03c）：四法只算账；兵力从存档的 prologue.troops 拾起来（v3 及更早是 0）。
            _siegeBook = new SiegeBook(_siegeTable);
            _troops = _loadedState?.Prologue.Troops ?? 0;
            GD.Print($"[攻城] 四法 {_siegeBook.Count} 条，兵力 {_troops}");

            // 节拍必须在读档之后造：旗标要先进 FlagStore，requires 才判得对
            // （S1-06 交接单的第二个坑：顺序写反，十二拍一起变 Locked）。
            _beats = PrologueBeats.FromState(_beatTable, _world.Flags, _loadedState);
            SettleKnock();
            GD.Print($"[节拍] 第 {_beats.Day} 天，已做 {_beats.Progress.DoneCount}/{_beats.Progress.TotalCount} 拍");

            // 转段（S2-05c）：四样结果写进这一份存档，SaveGame 每次从这里抄进新档。
            // 读进来的档若已经置了 prologue.done，IsDone 为真，后面不会再弹。
            _transitionState = _loadedState ?? new SaveState();
            _transitionBook = new TransitionBook(_oathTable, _realmTable, _world.Flags, _transitionState);
            _enfeoffBook = new EnfeoffBook();
            GD.Print($"[转段·表] 誓约 {_oathTable.Count} 条、国号 {_realmTable.Count} 个"
                     + $"，这一档{(_transitionBook.IsDone ? "已" : "未")}转段"
                     + $"；功臣 {_companionTable.Count} 位，这一档分封 {EnfeoffRecord.Count(_transitionState)}/6");

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
                {
                    var skipped = _beatTable.All[i];
                    _beats.Complete(skipped);

                    // 招兵那一拍被"跳"过去时，兵也得照第 0 档加上去：
                    // 不然 --beatskip=10 跳到攻城会看见 0 兵，截图与手测都对不上。
                    if (_recruitTable is not null
                        && string.Equals(skipped.Id, SiegeWiring.RecruitBeat, StringComparison.Ordinal))
                        RecruitWiring.Apply(_recruitTable, 0, ref _troops, id => ApplyTrust(id));
                }

                GD.Print($"[节拍·跳] 做到第 {skip} 拍 → 第 {_beats.Day} 天，当前 {_beats.Current?.Id ?? "无"}");
            }

            Mount();

            // 读档时十二拍已经走完、但上一次没走完转段：把界面接起来接着走。
            // IsDone 的档不会走到这里（IsDone 为真时 MaybeResumeTransition 直接不动）。
            MaybeResumeTransition();

            if (OS.GetCmdlineUserArgs().Contains("--highlight"))
                _view.ForceHighlight = true;

            GD.Print($"[举火] {_table.All.Count} 件可搜查物、{_talks.All.Count} 位可对话 NPC，当前 {_world.Hub}（{World.TitleOf(_world.Hub)}）有 {_world.Props.Count} 个");

            if (!_tut.Done)
                GD.Print($"[引导] 共 {_tut.Total} 步，当前「{_tut.Bar} {_tut.Step?.Title}」");
        }
        catch (Exception e)
        {
            GD.PushError($"初始化失败：{e.Message}\n{e.StackTrace}");
            _hint.Text = $"初始化失败：{e.Message}";
        }

        // 开场介绍（S3-01d）：新档在世界造好之后、教程之前先放这九行。
        // 老档不放开场——他上一次已经看过了；三个条件都收在 IntroFlow.ShouldPlay 里。
        if (_world is not null && IntroFlow.ShouldPlay(_newGame, _loadedState is not null, _introSeen))
            StartIntro(0);

        // 开发辅助：--tutstep=2 直接把引导拨到第 N 步，--help 打开操作说明（都是为了截图）
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--tutstep=", StringComparison.Ordinal)) continue;
            if (!int.TryParse(a[10..], out var step)) continue;

            _tut.Reach((Onboarding.Stage)Math.Clamp(step, 0, (int)Onboarding.Stage.Done));
            PaintTutorial();
        }

        if (OS.GetCmdlineUserArgs().Contains("--help"))
            ToggleHelp();

        // 开发辅助：--tutrun 把八步一次走完，验证引导真的推得动、进度真的落盘
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

        // 开发辅助：--effects 三条真实选项各结算一次（杀马 / 卖书 / 写给旧部），打印然后退出（S3-02c）
        if (_effectsRun)
        {
            if (_beatTable is not null && _items is not null)
            {
                foreach (var line in EffectsDemo.Run(_beatTable, _items))
                    GD.Print(line);
            }
            else
            {
                GD.Print($"{BeatEffectWiring.Prefix} 初始化没完成（beats.csv / items.csv 读不进来），跑不了");
                _exitCode = 1;
            }

            _quitAfterFrame = true;
        }

        // 开发辅助：--siege 兵力 60 选不了 / 打要撤 / 兵力 80 打得下，三行说完，然后退出（S2-03c）
        if (_siegeRun)
        {
            if (_siegeTable is not null && _trustTable is not null && _routeTable is not null)
            {
                foreach (var line in SiegeDemo.Run(_siegeTable, _trustTable, _routeTable))
                    GD.Print(line);
            }
            else
            {
                GD.Print($"{SiegeReport.Prefix} 初始化没完成（siege.csv / trust.csv / routes.csv 读不进来），跑不了");
                _exitCode = 1;
            }

            _quitAfterFrame = true;
        }

        // 开发辅助：--transition 十二拍都做完、许过两条承诺，走完四页并打印六行（S2-05c）
        if (_transitionRun)
        {
            if (_oathTable is not null && _realmTable is not null)
            {
                foreach (var line in TransitionDemo.Run(_oathTable, _realmTable))
                    GD.Print(line);
            }
            else
            {
                GD.Print($"{TransitionText.Prefix} 初始化没完成（oaths.csv / realms.csv 读不进来），跑不了");
                _exitCode = 1;
            }

            _quitAfterFrame = true;
        }

        // 开发辅助：--enfeoff 六个人各封哪一档、默认薄封的账、裴无咎改厚封、报告行（S4-01d）
        if (_enfeoffRun)
        {
            if (_companionTable is not null)
            {
                foreach (var line in EnfeoffDemo.Lines(_companionTable)) GD.Print(line);
            }
            else
            {
                GD.Print($"{EnfeoffDemo.Prefix} 初始化没完成（companions.csv 读不进来），跑不了");
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

        // 开发辅助：--gate 打印八步各自的"允许 / 拦下"矩阵，然后退出（S4-02）
        if (OS.GetCmdlineUserArgs().Contains("--gate"))
        {
            foreach (var line in TutorialGate.Matrix()) GD.Print(line);
            _quitAfterFrame = true;
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

        // 开发辅助：--objective 打印十二拍各自的目标点解析结果，然后退出（S4-03b）
        if (OS.GetCmdlineUserArgs().Contains("--objective"))
        {
            if (_beats is null || _world is null)
            {
                GD.Print("[目标点] 没有节拍表：beats.csv 读不进来。");
                _exitCode = 1;
            }
            else
            {
                // id / 点位归属 / 路程全部问真数据，这里一个地名都不写死。
                foreach (var line in ObjectiveReport.Lines(
                             _beats.Table, _objectives, _world.HubOfPoint, _world.TravelCostTo))
                    GD.Print(line);
            }

            _quitAfterFrame = true;
        }

        // 开发辅助：--map 打地图四行（当前 / 六条路各几刻 / 走一趟 / 再走一趟），然后退出（S3-01c）
        if (OS.GetCmdlineUserArgs().Contains("--map"))
        {
            if (_world is not null)
            {
                // 先把"新档此刻"的账抄下来：下面那张自检会真的把人挪走（World.TryTravel），
                // 挪完再算这四行的话，"当前"就变成别的空间了。
                var lines = MapReport.Lines(_world.Travel, _world.Hub, _world.KeLeft);

                // 真走一趟，证明"扣刻 + 换场景"这条接线没接错。
                // 走成了什么都不多打；没走成才补一行，免得把票面那四行的格式撑开。
                var before = _world.Hub;
                var live = _world.TryTravel("hub4");
                if (!live.Ok || !string.Equals(_world.Hub, "hub4", StringComparison.Ordinal))
                    GD.Print($"{MapReport.Prefix} 接线异常：{before} → {_world.Hub}，{live.Message}");

                foreach (var line in lines) GD.Print(line);
            }
            else
            {
                GD.Print($"{MapReport.Prefix} 初始化没完成（travel.csv 读不进来），跑不了");
                _exitCode = 1;
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
        var shotWanted = args.Contains("--shotbag") || args.Contains("--shottut")
                         || args.Contains("--shotmap") || args.Contains("--shot")
                         || args.Contains("--shotshop") || args.Contains("--shottrust")
                         || args.Contains("--shotenfeoff");

        // 截图截的是**游戏本身**；开场那九行另有 --shotintro。新档（--newgame）一进来就开场，
        // 不收掉的话 --newgame --shotmap 截到的是九行字压在地图上面。
        if (shotWanted && _introOpen) FinishIntro(skipped: true);

        // 面板类截图（--shotmap 等）不许目标卡压在上面；--shottut 要看的正是底下那条教学。
        // 只有什么都不带的 --shot 留着卡——那是它唯一能截到卡的时候。
        if (shotWanted && !args.Contains("--shot") && _goalCard.Visible)
        {
            _goalCard.Dismiss();
            HideGoalCard();
        }

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
        else if (args.Contains("--shotenfeoff"))
        {
            // 只为截图（S4-01d）：真的转段界面停在分封页，不碰玩家那份档。
            CloseChoice(restoreTutorial: false);
            OpenEnfeoffShot();
            _ = CaptureAndQuitAsync();
        }
        else if (args.Contains("--shotmap"))
        {
            CloseChoice(restoreTutorial: false);

            // --mapspent：先在这儿真翻几件东西，把余刻压到 2——
            // 新档余 8 刻时七条路全走得起，截出来看不见"今天走不动了"那一档。
            if (args.Contains("--mapspent") && _world is not null)
            {
                for (var i = 0; i < 20 && _world.KeLeft > 2 && _world.Props.Count > 0; i++)
                {
                    if (!StandAt(_world.Props[i % _world.Props.Count], out _)) continue;
                    _world.Search();
                }
            }

            OpenMap();
            _ = CaptureAndQuitAsync();
        }
        else if (args.FirstOrDefault(a => a.StartsWith("--shotintro", StringComparison.Ordinal)) is { } shotIntro)
        {
            // 只为截图：默认拨到第 7 行（老仆有立绘的那一句），截一张就退；
            // --shotintro=0 可以看第一句（旁白，左边不出人）。
            var at = IntroShotLine;
            var eq = shotIntro.IndexOf('=');
            if (eq > 0 && int.TryParse(shotIntro[(eq + 1)..], out var n)) at = n;

            CloseChoice(restoreTutorial: false);
            StartIntro(at);
            _ = CaptureAndQuitAsync();
        }
        else if (args.Contains("--shottut"))
        {
            // 只为截图（S4-01c）：留下一帧"玩家刚学会走路之前"的样子——
            // 顶部那行目标、底下那条 1/8 的教学，两张东西互不压着。
            // 开场与目标卡在上面那段公共逻辑里已经收掉了。
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

        // 菜单开着时世界还没开始：不推引导、不走路、不敲门。
        if (_bootMenuOpen) return;

        if (_world is null) return;

        _time += (float)delta;

        TickTutorialFlash();

        // 开场放着时只走时间（立绘脚下那条光要呼吸），不推引导、不提醒 Ctrl：
        // 玩家这会儿该看的是那九行字，不是"按住 Ctrl"。
        if (_introOpen) return;

        TickGoalCard((float)delta);
        TickTutorial((float)delta);
        TickTrustToast();

        // Ctrl 只按一下不算学会，按住半秒才推进；而且这一步得先允许"看"（S4-02）。
        // 只在"刚按下"那一下问闸门——每帧都问会把提示刷成一片。
        var ctrlDown = Input.IsKeyPressed(Key.Ctrl);
        if (ctrlDown && !_ctrlWasDown) _lookBlocked = !Gate(Act.Look);
        if (!ctrlDown) _lookBlocked = false;
        _ctrlWasDown = ctrlDown;

        if (ctrlDown && !_lookBlocked) _ctrlHeld += (float)delta;
        else _ctrlHeld = 0f;

        if (_view is not null) _view.LookAllowed = !_lookBlocked;

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

        // 主菜单压在一切之上：世界还没开始，键全归它（↑↓ / Enter / Esc）。
        if (_bootMenuOpen)
        {
            HandleBootMenuKey(k.Keycode);
            return;
        }

        // 开场压在一切之上（菜单之后、世界之前的那九行）：只认 Enter 与 Esc，
        // 别的键一律吞掉——这一段里不许有对话、地图、囊、信任弹出来。
        if (_introOpen)
        {
            HandleIntroKey(k.Keycode);
            return;
        }

        // 开局那张目标卡：任意键收起来，但**这个键照常往下走**——
        // 按 W 就是走一步，按 M 就是开地图。它是提示，不是"按 Enter 继续"的门。
        if (_goalCard.Visible)
        {
            _goalCard.Dismiss();
            HideGoalCard();
        }

        // 转段四页压在一切之上：四页之内 Esc 也不退（这是不可逆的十分钟）。
        if (_transitionOpen)
        {
            HandleTransitionKey(k.Keycode);
            return;
        }

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

        // 地图开着时，键盘归它：↑↓ 选、Enter 走、M / Esc 收起。
        if (_mapOpen)
        {
            HandleMapKey(k.Keycode);
            return;
        }

        switch (k.Keycode)
        {
            case Key.I:
                ToggleBag();
                break;

            case Key.M:
                ToggleMap();
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

    /// <summary>
    /// 按玩家实际做过的事推进，不用"点下一步"。
    /// 每一步的条件都能在 World / MapView 里查到，不额外记状态。
    ///
    /// 八步（S4-01c）：走 → 看 → 翻 → 图 → 囊 → 三轴 → 说 → 出门。
    /// 中间三步是**面板类**：按 M / I / Tab 把面板打开，这一步就算做到——
    /// 玩家本来就是想看它，不该再要求他按第二下。
    /// </summary>
    private void TickTutorial(float dt)
    {
        // 教完了、或者开局那张目标卡还挂在正中间：都不推。
        // 卡在的时候推，玩家会看见教学条从"1/8"直接跳成"2/8"，还以为自己漏了一课。
        if (_tut.Done || _goalCard.Visible) return;

        // 先看这一步在这个空间里做不做得到：hub1 没有 NPC，第七步在那儿永远做不成。
        // 做不到就**跳过去**——强制教学的第一条是"不许把人卡住"，不是"逼他完成"。
        if (TutorialGate.Impossible(TutStage(), _world.Props.Count > 0,
                _world.TalksHere().Any(), SceneConfig.Of(_world.Hub).Exit.X > 0))
        {
            GD.Print($"[引导·跳] {_tut.Current}（{TutorialGate.Key(TutStage())}）：这一处没有这一步要的东西");
            AdvanceTutorial();
            return;
        }

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

            // 面板三步：打开就算做到。面板关上的时候 PaintTutorial 才把下一条亮出来。
            case Onboarding.Stage.Map when _mapOpen:
                AdvanceTutorial();
                break;

            case Onboarding.Stage.Bag when _bagOpen:
                AdvanceTutorial();
                break;

            case Onboarding.Stage.Trust when _trustOpen:
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

    // ————————————————————————— 教学闸门（S4-02） —————————————————————————

    /// <summary>八步的教学阶段（Application 那套），与 <c>Onboarding.Stage</c> 同序。</summary>
    private TutorialStage TutStage() =>
        (TutorialStage)Math.Clamp((int)_tut.Current, 0, (int)TutorialStage.Done);

    /// <summary>
    /// 问一句闸门：放行返回 true；被拦下时把提示写出来、教学条闪一下，返回 false。
    /// 调用方拿到 false 就**什么都别做**——位置、刻、已搜、面板开关一个都不许动。
    /// </summary>
    private bool Gate(Act act)
    {
        var hint = TutorialGate.Block(TutStage(), act);
        if (hint is null) return true;

        Deny(act, hint);
        return false;
    }

    /// <summary>被拦下的统一动作：提示三秒 + 教学条闪一下 + 留一行日志。</summary>
    private void Deny(Act act, string hint)
    {
        SetHint(hint, 3f);
        FlashTutorial();
        GD.Print($"[引导·拦] {_tut.Current}（{TutorialGate.Key(TutStage())}）不许 {act}：{hint}");
    }

    /// <summary>
    /// 教学条闪一下：底框换成暖红 0.3 秒。
    /// 玩家按了没反应，必须有个"我看见了，但这一步不行"的回音，否则他会以为键坏了。
    /// </summary>
    private void FlashTutorial()
    {
        if (_tutPanel is null) return;

        _tutFlashUntil = _time + 0.3f;

        if (_tutStyleHot is not null) _tutPanel.AddThemeStyleboxOverride("panel", _tutStyleHot);
        _tutText.AddThemeColorOverride("font_color", new Color(1.00f, 0.66f, 0.48f, 1.00f));
        _tutKey.AddThemeColorOverride("font_color", new Color(1.00f, 0.78f, 0.46f, 1.00f));

        // 面板（地图 / 囊 / 三轴）压在上面时教学条本来是让位的：
        // 那几层的账由面板自己写一句，这里只在没被压住的时候把它亮出来。
        var covered = _mapOpen || _bagOpen || _trustOpen || _box.Visible || _helpOpen
                      || _shopOpen || _transitionOpen || _introOpen || _goalCard.Visible;

        if (!covered) _tutPanel.Visible = true;
    }

    /// <summary>闪完了换回常态配色。每帧查一次，到点就收。</summary>
    private void TickTutorialFlash()
    {
        if (_tutFlashUntil <= 0f || _time < _tutFlashUntil) return;

        _tutFlashUntil = 0f;

        if (_tutPanel is not null && _tutStyleCold is not null)
            _tutPanel.AddThemeStyleboxOverride("panel", _tutStyleCold);

        _tutText?.AddThemeColorOverride("font_color", new Color(0.86f, 0.82f, 0.72f, 0.94f));
        _tutKey?.AddThemeColorOverride("font_color", new Color(0.94f, 0.74f, 0.42f, 0.96f));
    }


    /// <summary>
    /// 35 秒还没翻过东西，就轻轻提醒一次"这一步该做什么"。
    /// 只提醒一次、只在本处提醒——提示说两遍就变成唠叨。
    ///
    /// 提示的话从闸门那张表里取，不写死"按住 Ctrl"（S4-02）：
    /// 这一步要是根本不许看，提醒他按 Ctrl 等于教他一个不会响的键。
    /// </summary>
    private void TickNudge(float dt)
    {
        if (_ctrlNudged || _tut.Current is Onboarding.Stage.Move or Onboarding.Stage.Look) return;
        if (_world.Searched.Count > 0 || _box.Visible || _helpOpen) return;

        _idleHere += dt;
        if (_idleHere < 35f) return;

        _ctrlNudged = true;

        var hint = TutorialGate.Hint(TutStage());
        if (hint.Length > 0) SetHint(hint, 5f);
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

                // 面板三步：走真路径（开、推一步、关）。绕过去直接用 Advance 就测不出
                // "面板按住打开状态能不能被识别成做到了"。
                case Onboarding.Stage.Map:
                    OpenMap();
                    TickTutorial(0f);
                    CloseMap();
                    break;

                case Onboarding.Stage.Bag:
                    OpenBag(showing: false, npc: "");
                    TickTutorial(0f);
                    CloseBag(restoreBox: false);
                    break;

                case Onboarding.Stage.Trust:
                    OpenTrust();
                    TickTutorial(0f);
                    CloseTrust();
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
        if (_tut.Advance())
        {
            _tutPanel.Visible = false;
            _progress.TutorialDone = true;
            SaveProgress();
            SetHint("按 H 随时看操作", 6f);
            GD.Print("[引导] 完成");

            // 教程让路之后，把刚才忍住没弹的第一拍补上
            TryShowChoice();
            return;
        }

        PaintTutorial();
    }

    private void PaintTutorial()
    {
        if (_tutPanel is null) return;

        var (key, text) = _tut.Line;

        // 目标卡、开场、对话框、说明、地图、囊、三轴、柜台、转段——任何一层在上面都让位。
        _tutPanel.Visible = !_tut.Done && !_goalCard.Visible && !_introOpen && !_box.Visible
                            && !_helpOpen && !_mapOpen && !_bagOpen && !_trustOpen
                            && !_shopOpen && !_transitionOpen;

        _tutKey.Text = key;
        _tutText.Text = text;

        // 教学条变了，"怎么去"那行也要跟着换（教学期间它整行空着）。
        UpdateHintLine();
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
            _view.MarkerFont = _font;      // 目标点旁边"到了"用同一份字体（S4-03b）

            _view.TextShown += ShowText;
            _view.TextClosed += CloseBox;
            _view.PromptChanged += t => SetHint(t);
            _view.NearChanged += OnNearChanged;
            _view.Exited += OnExit;
            _view.Searched += OnSearched;
            _view.InteractFallback = TrySearchBeatFallback;

            // 教学闸门（S4-02）：E 这一下先问"这一步让不让翻 / 说 / 出门"。
            // 拦下时不动作，只把提示给玩家；提示与闪动都在 Deny 里统一做。
            _view.Gate = act =>
            {
                var hint = TutorialGate.Block(TutStage(), act);
                if (hint is not null) Deny(act, hint);
                return hint;
            };
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
        // 八步没走完不出城（S4-01c）。站在出口、而且正停在"出门"这一步：这一下本来就是
        // 教程要求的动作，先把最后一步记上再放他走——不是给他开后门。
        if (!_tut.Done)
        {
            if (_tut.Current == Onboarding.Stage.Leave && AtExitHere()) AdvanceTutorial();

            if (!_tut.Done)
            {
                // 这一刻该做什么，由闸门那张表说话（第八步就是"走到黄框边按 E 出城"）。
                var hint = TutorialGate.Block(TutStage(), Act.Exit) ?? MapText.FinishLessonFirst;
                Deny(Act.Exit, hint);
                GD.Print("[引导] 不让他出城：这一课还没上完");
                return;
            }
        }

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

        // 这一步按了不响的键，就不许再写在底下（S4-02）：
        // "按住 Ctrl 看全部"挂在屏幕上、按下去却什么都不亮，玩家只会以为键坏了。
        var search = TutorialGate.Block(TutStage(), Act.Search) is null ? "　·　E 搜查" : "";
        var look = TutorialGate.Block(TutStage(), Act.Look) is null ? "　·　按住 Ctrl 看全部" : "";

        return $"WASD 走动{search}{look}　·　本处已搜 {searched}/{_world.Props.Count}";
    }

    private void ShowText(string head, string body)
    {
        // 对话框要压上来了：选项面板先让开，读完这一页 CloseBox 会再弹（S3-02c）。
        // 敲门是在"这一页还没画出来"的时候结算的，不让开的话两层面板会叠在一起，
        // 玩家看见的是"一句话 + 一张决断"，按 E 也不知道是在关哪一层。
        DeferOpenChoice();

        // 说话的人是谁，由"伸手够得着的那个人"决定——翻东西翻出来的那一页没有人，左边就不出人。
        var who = _world?.NearestSpeaker();
        var card = DialogueCard.From(head, body, who?.Name, _portraits);

        _box.Visible = true;
        _boxHead.Text = card.Title;
        _boxBody.Text = card.Body;

        // 有立绘的人：框长高到 144，左边站一个 128×128 的半身像；
        // 数据里没有这个人（或立绘表没读进来）就退回原来那一行字的样子。
        if (card.HasPortrait && ResolvePortrait(card.PortraitNpc) is { } face)
        {
            _box.Position = new Vector2(16, ViewH - 162);
            _box.Size = new Vector2(ViewW - 32, 144);

            _boxPortrait.Position = new Vector2(8, 8);
            _boxPortrait.Show(face, PortraitArt.SmallScale);

            _boxHead.Position = new Vector2(148, 8);
            _boxHead.Size = new Vector2(240, 17);
            _boxBody.Position = new Vector2(148, 30);
            _boxBody.Size = new Vector2(440, 104);
        }
        else
        {
            _box.Position = new Vector2(16, ViewH - 118);
            _box.Size = new Vector2(ViewW - 32, 100);

            _boxPortrait.Visible = false;

            _boxHead.Position = new Vector2(14, 7);
            _boxHead.Size = new Vector2(ViewW - 60, 17);
            _boxBody.Position = new Vector2(14, 28);
            _boxBody.Size = new Vector2(ViewW - 56, 64);
        }

        // 跟人说话时才提"给他看"——搜东西搜出来的那一页没有这个入口
        if (_boxFoot is not null)
            _boxFoot.Text = _world?.NearestSpeaker() is not null
                ? "G 给他看　·　E / Esc 关掉"
                : "E / Esc 关掉";

        _view.SetBoxOpen(true);
        _view.UiBlocking = true;
        if (_tutPanel is not null) _tutPanel.Visible = false;
        _hint.Visible = false;          // 提示条会被对话框压住，干脆让开

        UpdateChrome();
    }

    /// <summary>
    /// 面板正开着时来了对话框：先收面板、记着它，读完那一页再弹。
    /// 攻城面板不让——它的四法选择有中途状态，收起来等于让玩家重挑一遍。
    /// </summary>
    private void DeferOpenChoice()
    {
        if (!_choiceOpen || _choiceBeat is null || _siegeScreen is not null) return;

        _deferredChoiceId = _choiceBeat.Id;
        CloseChoice(restoreTutorial: false);
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
            // 转段五页开着时，别让 HUD 提前写「序章已完成」——那四个字要留在收尾页。
            var line = _transitionOpen && _beats is not null
                ? $"第 {_beats.Day} 天 · 转段：定都 → 国号 → 国策 → 分封 → 国情报告"
                : _beats is null ? "" : _beats.HudLine(ShortHub(_world.Hub));

            // 囊跟在节拍后面。快满了才变色——一直刺眼等于没提示。
            // 钱也跟在后面：钱就在囊里（coin 那一格），这里只把数念出来。
            _beatLine.Text = (line + BagHud.Suffix(_world.Bag) + BagHud.MoneySuffix(_world.Bag)).Trim();
            _beatLine.AddThemeColorOverride("font_color",
                BagHud.Warn(_world.Bag) ? BagInk : BeatInk);
        }

        UpdateGoalLine();
        PushObjective();
        UpdateHintLine();
    }

    /// <summary>
    /// 顶上那一行常驻目标（S4-01c）：<c>目标　活过今夜</c>。
    ///
    /// 每拍完成（<see cref="AfterBeat"/>）、转段收尾、换空间与读档（都走 <see cref="Mount"/>）
    /// 都会到这里来一次——所以目标不会停在上一拍那句上。
    /// 只在字真的变了的时候打一行日志：跑批与自检靠它认"目标确实跟着剧情走"。
    /// </summary>
    private void UpdateGoalLine()
    {
        if (_goalLine is null || _goals is null) return;

        var text = GoalHud.Text(
            _goals,
            _beats?.Current,
            _beats?.AllDone ?? false,
            _transitionBook?.IsDone ?? false);

        var line = GoalHud.Prefix + text;
        if (string.Equals(_goalLine.Text, line, StringComparison.Ordinal)) return;

        _goalLine.Text = line;
        GD.Print($"[目标] {text}");
    }

    // ————————————————————————— 目标点（S4-03b）—————————————————————————

    /// <summary>
    /// 当前这一拍的目标点。没有正等着的一拍（十二拍走完 / 转段）时是 <see cref="Objective.Empty"/>。
    /// </summary>
    private Objective CurrentObjective()
    {
        var beat = _beats?.Current;
        if (beat is null) return Objective.Empty;
        return _objectives.Of(beat.Id);
    }

    /// <summary>把目标点递给地图：黄菱形、边缘箭头都由它决定画不画。</summary>
    private void PushObjective()
    {
        if (_view is null) return;
        _view.SetObjective(CurrentObjective());
    }

    /// <summary>
    /// 目标行下面那行小字（S4-03b）。
    ///
    /// 三种情形：有 hint 写"怎么去　…"；目标在别的空间写"在 县城 · 按 M 过去（2 刻）"；
    /// 八步教学没走完时整行空着——底下那条教学条已经在说同一件事，不重复。
    /// </summary>
    private void UpdateHintLine()
    {
        if (_hintLine is null || _world is null) return;

        var objective = CurrentObjective();
        var cost = objective.Kind == TargetKind.Hub ? _world.TravelCostTo(objective.Id) : 0;
        var text = ObjectiveText.HintRow(objective, _tut is { Done: false }, cost);

        _hintLine.Text = text;
        _hintLine.Visible = text.Length > 0;
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

        if (_box.Visible || _helpOpen || _choiceOpen || _trustOpen || _mapOpen) return;

        // 开囊要过闸（S4-02）；关囊在上面早早就 return 了，永远放行。
        if (!Gate(Act.Bag)) return;

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
        if (!BagEditAllowed()) return;

        var item = _bagPanel.SelectedItem;
        if (item is null || !_bagPanel.Drop()) return;

        PaintBag();
        _bagFoot.Text = $"你丢下了{BagPickup.NameOf(_items, item)}。";
        UpdateChrome();
    }

    /// <summary>
    /// 囊里改东西（丢 / 用）之前先问一句：教学没走完只让看，不让改（S4-02）。
    /// 被拦时提示写进囊底那一行——囊开着的时候屏幕底下那条提示是让了位的，
    /// 写在它上面玩家一个字都看不见。
    /// </summary>
    private bool BagEditAllowed()
    {
        var hint = TutorialGate.BlockBagEdit(TutStage());
        if (hint is null) return true;

        _bagFoot.Text = hint;
        SetHint(hint, 3f);
        FlashTutorial();
        GD.Print($"[引导·拦] 囊里不许改：{hint}");
        return false;
    }

    /// <summary>给他看：三种反应分开，落旗标由 World 转给 FlagStore。</summary>
    private void ConfirmShow()
    {
        if (_bagPanel is null || _world is null) return;
        if (!BagEditAllowed()) return;

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

        if (_box.Visible || _helpOpen || _choiceOpen || _bagOpen || _mapOpen) return;

        // 开三轴要过闸（S4-02）；关在上面那段早 return，永远放行。
        if (!Gate(Act.Trust)) return;

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

        _trustTroops.Text = SiegeWiring.TroopLine(_troops);
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

    // ————————————————————————— 地图（S3-01c） —————————————————————————

    /// <summary>
    /// 地图面板：七个空间一行一个。标题「去何处」，顶上是第几天与余刻，
    /// 底下三句按键。选中那行点灯，走不动的行灰着。
    /// </summary>
    private void BuildMap(CanvasLayer ui)
    {
        _map = new Panel
        {
            Position = new Vector2(128, 70),
            Size = new Vector2(384, 288),
            Visible = false,
        };
        _map.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.045f, 0.050f, 0.048f, 0.97f),
            BorderColor = new Color(0.60f, 0.46f, 0.26f, 0.92f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 2,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        ui.AddChild(_map);

        _mapTitle = new Label
        {
            Position = new Vector2(18, 12),
            Size = new Vector2(160, 22),
            Text = MapText.Title,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_mapTitle, 15, new Color(0.96f, 0.90f, 0.72f, 0.98f));
        _map.AddChild(_mapTitle);

        _mapHead = new Label
        {
            Position = new Vector2(160, 14),
            Size = new Vector2(206, 18),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_mapHead, 10, new Color(0.78f, 0.72f, 0.56f, 0.92f));
        _map.AddChild(_mapHead);

        for (var i = 0; i < _mapRows.Length; i++)
        {
            var row = new Label
            {
                Position = new Vector2(18, 44 + i * 26),
                Size = new Vector2(348, 24),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(row, 13, new Color(0.84f, 0.81f, 0.73f, 0.94f));
            _map.AddChild(row);
            _mapRows[i] = row;
        }

        _mapMsg = new Label
        {
            Position = new Vector2(18, 228),
            Size = new Vector2(348, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_mapMsg, 10, new Color(0.86f, 0.62f, 0.40f, 0.96f));
        _map.AddChild(_mapMsg);

        _mapFoot = new Label
        {
            Position = new Vector2(18, 258),
            Size = new Vector2(348, 18),
            Text = MapText.Foot,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_mapFoot, 9, new Color(0.58f, 0.55f, 0.46f, 0.88f));
        _map.AddChild(_mapFoot);
    }

    /// <summary>
    /// M：开合地图。**别的面板压着时一律不开**——对话、囊、信任、柜台、攻城、转段，
    /// 任何一层在上面，这一下 M 都不该再叠一张。
    /// </summary>
    private void ToggleMap()
    {
        if (_world is null) return;

        if (_mapOpen)
        {
            CloseMap();
            return;
        }

        if (_box.Visible || _helpOpen || _choiceOpen || _bagOpen || _trustOpen
            || _shopOpen || _transitionOpen || _introOpen) return;

        // 关面板永远放行（上面那条 return 之前），开面板才问闸门——
        // 拦"关"会让玩家以为界面卡死。
        if (!Gate(Act.Map)) return;

        OpenMap();
    }

    /// <summary>摆开地图：按此刻的天数、余刻、预算现算七行。</summary>
    private void OpenMap()
    {
        if (_world is null) return;

        _mapScreen = new MapScreen(
            _world.Travel, _world.Hub, _beats?.Day ?? 1, _world.KeLeft, _world.KeBudget);

        _mapOpen = true;
        _map.Visible = true;
        _tutPanel.Visible = false;
        _hint.Visible = false;
        if (_view is not null) _view.UiBlocking = true;

        PaintMap();
        GD.Print($"[图] 打开：{_world.Hub}，余刻 {_world.KeLeft}");
    }

    private void CloseMap()
    {
        if (!_mapOpen) return;

        _mapOpen = false;
        _map.Visible = false;
        _mapScreen = null;

        _hint.Visible = !_box.Visible;
        if (_view is not null)
            _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _bagOpen
                               || _trustOpen || _shopOpen || _transitionOpen || _introOpen;

        PaintTutorial();
        RefreshHint();
    }

    /// <summary>地图上只有四件事：上下选、Enter 走、M / Esc 收起。</summary>
    private void HandleMapKey(Key key)
    {
        switch (key)
        {
            case Key.Up or Key.W:
                MoveMap(-1);
                break;

            case Key.Down or Key.S:
                MoveMap(1);
                break;

            case Key.Enter or Key.KpEnter or Key.E or Key.Space:
                ConfirmMapTravel();
                break;

            case Key.M:
            case Key.Escape:
                CloseMap();
                break;
        }
    }

    private void MoveMap(int delta)
    {
        if (_mapScreen is null) return;
        _mapScreen.Move(delta);
        PaintMap();
    }

    /// <summary>
    /// 回车：走这一行。走成了就换场景、把"从哪儿到哪儿走了几刻"留在屏幕底下；
    /// 走不成就**原地不动**，把原因写在面板里——不扣刻、不换场景。
    /// </summary>
    private void ConfirmMapTravel()
    {
        if (_mapScreen is null || _world is null) return;

        // 教学没走完，地图可以看、可以选，但走不了：这是玩家要的"强制"。
        // 面板不关，把原因写在面板里——他抬头就能读完这一步该做什么。
        if (TutorialGate.BlockMapTravel(TutStage()) is { } byLesson)
        {
            _mapScreen.Refuse(byLesson);
            PaintMap();
            SetHint(byLesson, 3f);
            GD.Print($"[图] 不让他出城：这一课还没上完（{byLesson}）");
            return;
        }

        var row = _mapScreen.Current;

        if (row.State == MapRowState.Here)
        {
            _mapScreen.Refuse(MapText.AlreadyHere);
            PaintMap();
            return;
        }

        var result = _world.TryTravel(row.Hub);

        if (!result.Ok)
        {
            _mapScreen.Refuse(result.Message);
            PaintMap();
            GD.Print($"[图] {row.Hub} 走不动：{result.Message}");
            return;
        }

        CloseMap();
        Mount();
        SetHint(result.Message, 5f);
        GD.Print($"[图] {result.Message}");
    }

    /// <summary>把七行重画一遍：选中点灯、走不动的灰着、其余照旧。</summary>
    private void PaintMap()
    {
        if (_mapScreen is null) return;

        _mapTitle.Text = _mapScreen.Title;
        _mapHead.Text = _mapScreen.Head;
        _mapFoot.Text = _mapScreen.Foot;

        var rows = _mapScreen.Rows();
        var sel = _mapScreen.Selected;

        for (var i = 0; i < _mapRows.Length; i++)
        {
            var label = _mapRows[i];
            if (i >= rows.Count)
            {
                label.Text = "";
                continue;
            }

            var row = rows[i];
            var picked = i == sel;
            var pad = picked ? "▸ " : row.State == MapRowState.Here ? "" : "　";

            label.Text = pad + row.Text;
            label.AddThemeColorOverride("font_color", picked
                ? new Color(0.96f, 0.82f, 0.48f, 0.99f)
                : row.State == MapRowState.Here
                    ? new Color(0.88f, 0.79f, 0.52f, 0.96f)
                    : row.Dim
                        ? new Color(0.46f, 0.44f, 0.40f, 0.60f)
                        : new Color(0.84f, 0.81f, 0.73f, 0.94f));
        }

        _mapMsg.Text = _mapScreen.Message;
        _mapMsg.Visible = _mapMsg.Text.Length > 0;
    }

    // ————————————————————————— 十二节拍 —————————————————————————

    /// <summary>
    /// 进这一处、正好有一拍要选：弹面板。已经做完的拍不再是 Available，所以不会重复弹。
    /// </summary>
    private void TryShowChoice()
    {
        if (_beats is null || _world is null || _choiceOpen || _box.Visible || _helpOpen) return;

        // 开场那九行压在最上面：这一段里什么面板都不许弹（玩家的原话是"一开始一头雾水"，
        // 再叠一张决断上来只会更雾）。
        if (_introOpen) return;

        // --bag 只翻东西，不答决断：面板压上来会把后面的按键全吃掉。
        if (_bagRun) return;

        // 囊压在上面时也不弹：两层面板叠在一起，玩家不知道该听谁的。
        if (_bagOpen) return;

        // 「信任」面板同理。
        if (_trustOpen) return;

        // 地图压在上面时也不弹：玩家正在看"去何处"，推一张决断上来会把 ↑↓ 抢走。
        if (_mapOpen) return;

        // 柜台也压在上面：这时候推一张决断上来，键盘会被两层抢。
        if (_shopOpen) return;

        // 教学八步是第一课：它没走完就别推决断上来，两个框叠着玩家不知道该看谁。
        // （玩家报的「没有新手教程」就是这个：新档一进场，b01 焚账先弹出来把教程压住了。）
        if (!_tut.Done) return;

        var beat = _beats.PendingChoice(_world.Hub) ?? DeferredChoice();
        if (beat is null) return;

        ShowChoice(beat);
    }

    /// <summary>
    /// 敲门 / 搜查触发的、带选项的那一拍（S3-02c）：触发那一刻对话框正要弹出来，
    /// 面板压上去会被下一句文本盖住，于是先记在 <see cref="_deferredChoiceId"/> 上。
    /// 这里把它取出来——人还在那一处、这一拍还没做完才给，否则当它不存在。
    /// </summary>
    private Beat? DeferredChoice()
    {
        if (_deferredChoiceId is null || _beats is null || _world is null) return null;

        var beat = _beats.Table.Get(_deferredChoiceId);
        if (beat is null
            || !string.Equals(beat.Hub, _world.Hub, StringComparison.Ordinal)
            || !_beats.Machine.CanTrigger(beat))
        {
            // 已经做完了（别的路补上的）就不必再等；人不在那一处则留着，回去时再问。
            if (beat is null || !_beats.Machine.CanTrigger(beat)) _deferredChoiceId = null;
            return null;
        }

        return beat;
    }

    private void ShowChoice(Beat beat)
    {
        _deferredChoiceId = null;
        _choiceBeat = beat;
        _choiceSel = 0;
        _choiceOpen = true;

        // 攻城那一拍不念 beats.csv 的四个字，念四法的真账：耗时、伤亡、用不用得上。
        _siegeScreen = BuildSiegeScreen(beat);

        if (_siegeScreen is not null)
        {
            ShowSiegeRows(beat);
            PaintChoice();
            _choice.Visible = true;
            _tutPanel.Visible = false;
            _hint.Visible = false;
            _view.UiBlocking = true;
            GD.Print($"[攻城] 弹出四法面板（兵力 {_troops}）");
            return;
        }

        var hasOptions = beat.Options.Length > 0;
        _choiceHead.Text = $"{PrologueBeats.OrdinalOf(_beats?.IndexOf(beat) ?? 0)}　{beat.Name}";

        for (var i = 0; i < MaxOptions; i++)
        {
            var label = _choiceItems[i];
            _choiceNotes[i].Visible = false;

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
        ResizeChoice(rows);

        _choice.Visible = true;
        _tutPanel.Visible = false;
        _hint.Visible = false;
        _view.UiBlocking = true;

        PaintChoice();
        GD.Print($"[节拍] 弹出 {beat.Id} {beat.Name}（{beat.Options.Length} 个选项）");
    }

    /// <summary>
    /// 攻城那一拍要一张 <see cref="SiegeScreen"/>；别的拍、或者四法读不进来时为 null。
    /// 旗标探针按**当前所在的 hub** 问——<c>city_hungry</c> 这类门是随场景走的。
    /// </summary>
    private SiegeScreen? BuildSiegeScreen(Beat beat)
    {
        if (_siegeBook is null || _trustBook is null || _world is null) return null;
        if (!string.Equals(beat.Id, SiegeWiring.SiegeBeat, StringComparison.Ordinal)) return null;

        return new SiegeScreen(
            _siegeBook, _trustBook, _troops,
            name => _world.Flags.Has(name, _world.Hub),
            _world.Flags);
    }

    /// <summary>把四法摊成四行：左边名字 + 耗时 + 伤亡，右边门槛 / 兵不够的那句。</summary>
    private void ShowSiegeRows(Beat beat)
    {
        if (_siegeScreen is null) return;

        _choiceHead.Text = SiegeWiring.SiegeHead(
            PrologueBeats.OrdinalOf(_beats?.IndexOf(beat) ?? 0), beat.Name, _troops);

        var rows = _siegeScreen.Rows();
        for (var i = 0; i < MaxOptions; i++)
        {
            var label = _choiceItems[i];
            var usable = i < rows.Count;

            label.Visible = usable;
            _choiceText[i] = usable ? $"{i + 1}　{rows[i].Label}" : "";

            _choiceNotes[i].Visible = usable && rows[i].Note.Length > 0;
            _choiceNotes[i].Text = usable ? rows[i].Note : "";
        }

        _choiceFootDefault = SiegeScreen.FootLine;
        _choiceFoot.Text = _choiceFootDefault;
        ResizeChoice(rows.Count);
    }

    /// <summary>面板高度跟着行数走。</summary>
    private void ResizeChoice(int rows)
    {
        var footY = 50 + Math.Max(1, rows) * 30 + 6;
        _choiceFoot.Position = new Vector2(16, footY);
        _choice.Size = new Vector2(ViewW - 208, footY + 28);
        _choice.Position = new Vector2(104, Mathf.Clamp(220 - (footY + 28) / 2f, 84, 140));
    }

    /// <summary>
    /// 把选中那一项点亮。每次按键都重画，别指望控件自己会跳。
    ///
    /// 攻城那一拍里，「间」要官望够才走得通（design §8.6：间 = 内应），
    /// 其余三法各有各的门槛（断粮 / 地形）；走不通的项灰着，右边直接说缺什么——
    /// 玩家不该按了回车才知道白按。兵不够打赢的也先告诉他"多半要撤"，再让他自己决定。
    /// </summary>
    private void PaintChoice()
    {
        if (_siegeScreen is not null)
        {
            PaintSiegeChoice();
            return;
        }

        for (var i = 0; i < MaxOptions; i++)
        {
            if (!_choiceItems[i].Visible) continue;

            var picked = i == _choiceSel;
            var locked = TrustChoice.Locked(_trustBook, _choiceBeat, i);

            _choiceNotes[i].Visible = false;
            _choiceItems[i].Text = (picked ? "▸ " : "　 ") + _choiceText[i];
            _choiceItems[i].AddThemeColorOverride("font_color", locked
                ? new Color(0.46f, 0.44f, 0.40f, 0.58f)
                : picked
                    ? new Color(0.96f, 0.82f, 0.48f, 0.98f)
                    : new Color(0.78f, 0.75f, 0.66f, 0.90f));
        }

        _choiceFoot.Text = TrustChoice.RefusalFor(_trustBook, _choiceBeat, _choiceSel) ?? _choiceFootDefault;
    }

    /// <summary>攻城面板的重画：三档颜色——点不动（灰）、要撤（褐）、正常；选中永远是金的那一层。</summary>
    private void PaintSiegeChoice()
    {
        var screen = _siegeScreen!;
        var rows = screen.Rows();

        for (var i = 0; i < _choiceItems.Length && i < rows.Count; i++)
        {
            var row = rows[i];
            var picked = i == _choiceSel;

            _choiceItems[i].Text = (picked ? "▸ " : "　 ") + _choiceText[i];
            _choiceItems[i].AddThemeColorOverride("font_color",
                !row.Usable ? new Color(0.46f, 0.44f, 0.40f, 0.58f)
                : picked ? new Color(0.96f, 0.82f, 0.48f, 0.98f)
                : row.WillRetreat ? new Color(0.86f, 0.64f, 0.36f, 0.94f)
                : new Color(0.78f, 0.75f, 0.66f, 0.90f));

            _choiceNotes[i].Text = row.Note;
            _choiceNotes[i].Visible = row.Note.Length > 0;
            _choiceNotes[i].AddThemeColorOverride("font_color",
                !row.Usable ? new Color(0.52f, 0.50f, 0.45f, 0.78f)
                : new Color(0.88f, 0.66f, 0.34f, 0.94f));
        }

        _choiceFoot.Text = _choiceSel >= 0 && _choiceSel < rows.Count && !rows[_choiceSel].Usable
            ? $"这一法用不上：{rows[_choiceSel].Note}"
            : SiegeScreen.FootLine;
    }

    /// <summary>确认这一项：置 <c>beat.&lt;id&gt;.pick.&lt;i&gt;</c>，完成这一拍，日子往前走。</summary>
    private void ConfirmChoice()
    {
        if (!_choiceOpen || _choiceBeat is null || _beats is null) return;

        var beat = _choiceBeat;
        var index = PrologueBeats.NormalizePick(beat, _choiceSel);

        // 攻城：四法各有门槛，打不打、剩多少兵都由 SiegeScreen 落账（S2-03c）。
        if (_siegeScreen is not null && string.Equals(beat.Id, SiegeWiring.SiegeBeat, StringComparison.Ordinal))
        {
            ConfirmSiege(beat, index);
            return;
        }

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

        if (_trustBook is null || _world is null)
        {
            GD.PushWarning($"[节拍] {beat.Id} 没有可落的账（信任 / 世界还没造好）");
            return;
        }

        // 效果先落、成了才记这一拍（S3-02c，顺序钉在 BeatChoiceRunner 里）：
        // 囊满 / 钱不够时整组不落，这一拍也**不算完**，把原因留在面板底下让他清囊再来。
        var outcome = BeatChoiceRunner.Confirm(
            beat, index, _beats, _trustBook.State, _world.Bag, _items, ref _troops, _world.Flags);

        if (!outcome.Ok)
        {
            GD.Print($"[效果] {beat.Id} {outcome.Reason}");
            PaintChoice();
            _choiceFoot.Text = outcome.Reason;
            return;
        }

        CloseChoice(restoreTutorial: true);

        // 屏幕上那一行：`杀马：谷子 ×3，民望 −5`（效果一句都没有的拍退回原来那句）。
        var line = outcome.Line.Length > 0 ? outcome.Line : $"「{picked}」——你记下了。";
        if (outcome.Line.Length > 0) GD.Print($"[效果] {beat.Id} {outcome.Line}");

        // 招兵（b07）：选哪一档就加多少兵；给粮那档还要借「施粥」动三轴（§18.5 场景 7）。
        if (_recruitTable is not null && string.Equals(beat.Id, SiegeWiring.RecruitBeat, StringComparison.Ordinal))
        {
            var recruit = RecruitWiring.Apply(_recruitTable, index, ref _troops, id => ApplyTrust(id));
            if (recruit.Length > 0)
            {
                GD.Print($"[招兵] 第 {index + 1} 档：{recruit}");
                line = recruit;
            }
        }

        SetHint(line, 3.5f);
        AfterBeat(beat);
    }

    /// <summary>
    /// 攻城这一下：先结算，成了才记这一拍。门槛没过的面板不关——把原因留在底下那行。
    /// </summary>
    private void ConfirmSiege(Beat beat, int index)
    {
        if (_siegeScreen is null || _beats is null) return;
        if (!_beats.Machine.CanTrigger(beat)) return;

        var screen = _siegeScreen;
        var result = screen.Confirm(index);

        if (!result.Ok)
        {
            GD.Print($"[攻城] 第 {index + 1} 法用不上：{result.Reason}");
            _choiceFoot.Text = screen.Message;
            return;
        }

        // 结算之后剩下的兵要接回主账：Main 只存 _troops 这一份，面板算完就地交回来。
        _troops = screen.Troops;

        // 赢也好、撤也好，这一拍都算做完（§16.5：输不是结束）——之后照常进 b12。
        var ok = _beats.Choose(beat, index);
        CloseChoice(restoreTutorial: true);

        if (!ok)
        {
            GD.PushWarning($"[节拍] {beat.Id} 选不进去（多半是状态已经变了）");
            return;
        }

        GD.Print($"[攻城] {screen.Message}（余兵 {_troops}，城破={result.Win}）");
        SetHint(screen.Message, 4f);
        AfterBeat(beat);
    }

    /// <summary>这一拍是"敲门 / 搜查"触发的（只有这两种没有"再进一次"的机会）。</summary>
    private static bool IsTriggeredBeat(Beat beat)
        => string.Equals(beat.Trigger, PrologueBeats.KnockTrigger, StringComparison.Ordinal)
        || string.Equals(beat.Trigger, PrologueBeats.SearchTrigger, StringComparison.Ordinal);

    private void CloseChoice(bool restoreTutorial)
    {
        if (!_choiceOpen) return;

        var closed = _choiceBeat;

        _choiceOpen = false;
        _choiceBeat = null;
        _siegeScreen = null;
        _choice.Visible = false;

        // 敲门 / 搜查触发的拍没有"再进一次这一处"的机会：Esc 之后得记着它，
        // 等下一次面板放得下的时候（读完一页、走回这一处）再问一遍。
        // 做完的那一拍这时已经不再是 Available，不会被重新挂上。
        if (restoreTutorial && closed is not null && _beats is not null && IsTriggeredBeat(closed)
            && _beats.Machine.CanTrigger(closed))
            _deferredChoiceId = closed.Id;

        _hint.Visible = !_box.Visible;
        if (_view is not null) _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _trustOpen;

        if (restoreTutorial) PaintTutorial();
    }

    // ————————————————————————— 转段四页（S2-05c） —————————————————————————

    /// <summary>
    /// 读档之后接上没走完的转段：十二拍都做完、而且还没转过段时才开。
    /// 已转过段的档（<see cref="TransitionBook.IsDone"/>）这里一步都不动——不再弹第二次。
    /// </summary>
    private void MaybeResumeTransition()
    {
        if (_transitionRun || _playthrough || _bagRun || _shopRun || _siegeRun || _enfeoffRun) return;
        if (_transitionBook is null || _transitionBook.IsDone) return;
        if (_beats is null || !_beats.AllDone) return;

        OpenTransition();
    }

    /// <summary>开转段：四页压在最上面，走动与 E 全交给它。</summary>
    private void OpenTransition()
    {
        if (_transitionBook is null || _realmTable is null) return;
        if (_transitionScreen is not null) return;

        // 转段这一份存档要自己知道"十二拍走完了"：TransitionBook.Missing 读的是
        // prologue.beats_done，不是运行中的状态机。不同步，Finish 会永远说"还差 12 拍"。
        _beats?.Machine.ToState(_transitionState);

        // 每次开转段新起一本账：谁是"已经封过"以存档里的 enfeoffs 为准，
        // 不能指望上一局留下的 HashSet（OpenTransition 可能被开第二次）。
        _enfeoffBook = new EnfeoffBook();

        _transitionScreen = new TransitionScreen(
            _transitionBook, _realmTable, _transitionState,
            _companionTable, _enfeoffBook, _trustBook?.State, _world?.Flags);
        _transitionOpen = true;

        if (_choiceOpen) CloseChoice(restoreTutorial: false);
        if (_box.Visible) CloseBox();

        _transition.Visible = true;
        _tutPanel.Visible = false;
        _hint.Visible = false;
        if (_view is not null) _view.UiBlocking = true;

        UpdateChrome();
        PaintTransition();
        GD.Print($"[转段] 打开转段：许过 {_transitionScreen.Made.Count} 条承诺"
                 + $"，{(_transitionScreen.NeedsEnfeoff ? "要分封" : "不用分封")}");
    }

    /// <summary>
    /// 只为截图（S4-01d）：把真的转段界面拨到分封页，光标停在裴无咎、已改成厚封。
    /// 走的是 <see cref="EnfeoffDemo.OpenScreen"/>，造的是自己的一份存档，绝不碰玩家那份。
    /// </summary>
    private void OpenEnfeoffShot()
    {
        if (_companionTable is null || _oathTable is null || _realmTable is null) return;

        var (screen, state) = EnfeoffDemo.OpenScreen(_companionTable, _oathTable, _realmTable);
        _transitionState = state;
        _transitionScreen = screen;
        _transitionOpen = true;

        _transition.Visible = true;
        _tutPanel.Visible = false;
        _hint.Visible = false;
        if (_view is not null) _view.UiBlocking = true;

        UpdateChrome();
        PaintTransition();
        GD.Print($"[分封] 截图：{screen.Head}，光标 {screen.Selected}，档位 {screen.GradeAtCursor}");
    }

    /// <summary>收尾页按 Esc：退出转段，回到游戏里。之后 HUD 会写「序章已完成」。</summary>
    private void CloseTransition()
    {
        if (!_transitionOpen) return;

        _transitionOpen = false;
        _transitionScreen = null;
        _transition.Visible = false;

        _hint.Visible = !_box.Visible;
        if (_view is not null)
            _view.UiBlocking = _helpOpen || _box.Visible || _choiceOpen || _trustOpen || _shopOpen;

        UpdateChrome();
        SetHint("序章完成。", 4f);
        GD.Print("[转段] 四页走完，序章完成");
    }

    /// <summary>报告页最后一行要的实数：兵力 / 三轴 / 囊 / 第几天。</summary>
    private ReportFacts TransitionFacts()
    {
        var axes = _trustBook?.State ?? new TrustState();

        return new ReportFacts(
            _troops,
            axes.Guan,
            axes.Min,
            axes.Yi,
            _world?.Bag.SlotsUsed ?? 0,
            _world?.Bag.Capacity ?? Inventory.DefaultCapacity,
            _beats?.Day ?? 0);
    }

    /// <summary>
    /// 报告页的十三行：前四行照刚选的，分封那两行（有分封才有），中间五行是文档原句，
    /// 再一行"带走"（转段这一刻囊里真有的东西），最后一行是实数。
    /// </summary>
    private IReadOnlyList<TransitionRow> ReportRows(TransitionScreen screen)
    {
        var lines = new List<string>(screen.ReportLines(TransitionFacts()));

        // "带走"紧挨着实数行上面：先说带了什么走，再说手里有多少兵、第几天。
        lines.Insert(Math.Max(0, lines.Count - 1), TransitionText.CarryLine(_world?.Bag, _items));

        var rows = new List<TransitionRow>(lines.Count);
        foreach (var line in lines) rows.Add(new TransitionRow("", line, false));
        return rows;
    }

    /// <summary>把当前那一页画出来。每次按键都重画，不指望控件自己会跳。</summary>
    private void PaintTransition()
    {
        if (_transitionScreen is null) return;

        var screen = _transitionScreen;
        _transitionHead.Text = screen.Head;

        var flash = screen.Flash.Length > 0 ? screen.Flash : screen.Note;
        _transitionFlash.Text = flash;
        _transitionFlash.Visible = flash.Length > 0;

        var rows = screen.Page switch
        {
            TransitionPage.Capital => screen.CapitalRows(),
            TransitionPage.Realm => screen.RealmRows(),
            TransitionPage.Oaths => screen.OathRows(),
            TransitionPage.Enfeoff => screen.EnfeoffRows(),
            TransitionPage.Report => ReportRows(screen),
            _ => Array.Empty<TransitionRow>(),
        };

        for (var i = 0; i < _transitionRows.Length; i++)
        {
            var label = _transitionRows[i];
            if (i >= rows.Count)
            {
                label.Visible = false;
                continue;
            }

            var row = rows[i];
            var cursor = screen.IsCursorAt(i);
            var mark = screen.Page == TransitionPage.Oaths ? (row.Picked ? "✓ " : "　 ") : "";

            label.Visible = true;
            label.Text = (cursor ? "▸ " : "　 ") + mark + row.Text;
            label.AddThemeColorOverride("font_color",
                cursor ? new Color(0.96f, 0.82f, 0.48f, 0.98f)
                : screen.Page == TransitionPage.Oaths && !row.Picked
                    ? new Color(0.62f, 0.59f, 0.51f, 0.88f)
                    : new Color(0.80f, 0.77f, 0.68f, 0.94f));
        }

        _transitionFoot.Text = screen.Foot;
        _transitionFoot.AddThemeColorOverride("font_color",
            screen.Page == TransitionPage.Done
                ? new Color(0.90f, 0.80f, 0.56f, 0.96f)
                : new Color(0.60f, 0.56f, 0.46f, 0.90f));
    }

    /// <summary>
    /// 四页的键盘：数字挑、Enter 落、空格改勾。**Esc 在四页之内不退**，只在「序章完成」那页放人走。
    /// 每一步落定都立刻落盘一次——这十分钟不能靠"退出时再存"。
    /// </summary>
    private void HandleTransitionKey(Key key)
    {
        if (_transitionScreen is null) return;

        var screen = _transitionScreen;

        if (screen.Page == TransitionPage.Done)
        {
            if (key == Key.Escape) CloseTransition();
            return;
        }

        var saved = false;

        switch (screen.Page)
        {
            case TransitionPage.Capital:
                saved = key switch
                {
                    Key.Key1 or Key.Kp1 => screen.SelectCapital(0),
                    Key.Key2 or Key.Kp2 => screen.SelectCapital(1),
                    Key.Key3 or Key.Kp3 => screen.SelectCapital(2),
                    Key.Key4 or Key.Kp4 => screen.SelectCapital(3),
                    Key.Key5 or Key.Kp5 => screen.SelectCapital(4),
                    Key.Key6 or Key.Kp6 => screen.SelectCapital(5),
                    Key.Key7 or Key.Kp7 => screen.SelectCapital(6),
                    _ => false,
                };
                break;

            case TransitionPage.Realm:
                saved = key switch
                {
                    Key.Key1 or Key.Kp1 => screen.SelectRealm(0),
                    Key.Key2 or Key.Kp2 => screen.SelectRealm(1),
                    Key.Key3 or Key.Kp3 => screen.SelectRealm(2),
                    Key.Key4 or Key.Kp4 => screen.SelectRealm(3),
                    _ => false,
                };
                break;

            case TransitionPage.Enfeoff:
                switch (key)
                {
                    case Key.Up or Key.W: screen.MoveEnfeoffCursor(-1); break;
                    case Key.Down or Key.S: screen.MoveEnfeoffCursor(1); break;
                    case Key.Left or Key.A: screen.CycleGrade(-1); break;
                    case Key.Right or Key.D: screen.CycleGrade(1); break;
                }

                break;

            case TransitionPage.Oaths:
                switch (key)
                {
                    case Key.Up or Key.W: screen.MoveCursor(-1); break;
                    case Key.Down or Key.S: screen.MoveCursor(1); break;
                    case Key.Space: screen.ToggleOath(); break;
                }

                break;
        }

        if (key is Key.Enter or Key.KpEnter or Key.E)
            saved |= screen.Advance();
        else if (key == Key.Escape)
            screen.RefuseBack();

        if (saved)
        {
            SaveGame();
            GD.Print($"[转段] 落盘：都城 {_transitionState.Prologue.Capital}｜国号 {_transitionState.Prologue.Realm}"
                     + $"｜国策 {_transitionState.Prologue.Oaths.Count} 条｜未决 {_transitionState.Prologue.Debts.Count} 条"
                     + $"｜分封 {EnfeoffRecord.Count(_transitionState)}/6");
        }

        PaintTransition();
    }

    /// <summary>一拍做完之后：刷新那一行、落盘、补记敲门、看看有没有下一拍能弹。</summary>
    private void AfterBeat(Beat beat)
    {
        GD.Print($"[节拍] 完成 {beat.Id} {beat.Name} → 第 {_beats?.Day ?? 0} 天");

        UpdateChrome();
        SaveGame();
        SettleKnock();

        // b12 落定的同一刻开转段（S2-05c）：不等玩家再按什么，也不许他自己走开。
        if (TransitionTrigger.IsDue(_transitionBook, beat.Id))
        {
            MaybeResumeTransition();
            return;
        }

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

        // 补记的这一拍也可能带选项（b02）：同样要玩家先拿主意，不能替他做完。
        if (BeatEffectWiring.NeedsChoice(late))
        {
            DeferChoice(late);
            return;
        }

        if (!_beats.Complete(late)) return;

        GD.Print($"[节拍] 补记 {late.Id} {late.Name} → 第 {_beats.Day} 天");
        UpdateChrome();
        SaveGame();
    }

    /// <summary>
    /// 敲门这一下（<see cref="World.Knocked"/>）。文本在世界那层，这里只负责让玩家看见、
    /// 并把 b02 记上。读档时 World.Load 也会补敲一次，那时状态机可能还没造好，所以空判。
    ///
    /// 这一拍带选项（S3-02c）：不能直接算完——先弹面板，玩家确认了才 <see cref="PrologueBeats.Choose"/>。
    /// 敲门发生在"对话框正要弹出来"之前，面板这时候推上去会被那一页盖住，所以交给 <see cref="DeferChoice"/>。
    /// </summary>
    private void OnKnocked()
    {
        SetHint(_world.LastMessage ?? "", 4f);

        var beat = _beats?.PendingKnock(_world.Hub);
        if (beat is null) return;

        if (BeatEffectWiring.NeedsChoice(beat))
        {
            DeferChoice(beat);
            return;
        }

        if (_beats!.Complete(beat))
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

        // b08 这一拍也有选项：搜到仓门只是"够得着了"，砸锁 / 记账 / 放火还得他自己挑。
        if (BeatEffectWiring.NeedsChoice(beat))
        {
            DeferChoice(beat);
            return;
        }

        if (!_beats.Complete(beat)) return;

        GD.Print($"[节拍] 搜到 {pointId} 完成 {beat.Id} {beat.Name} → 第 {_beats.Day} 天");
        UpdateChrome();
        SaveGame();
    }

    /// <summary>
    /// 敲门 / 搜查触发的、带选项的那一拍：面板现在弹不出来（对话框正开着、教程还没走完），
    /// 先记下来，等 <see cref="TryShowChoice"/> 能弹的时候补上。已经在等同一拍的不重复记。
    /// </summary>
    private void DeferChoice(Beat beat)
    {
        // 面板上正等着同一拍就不必再记一遍；记着也能重记，ShowChoice 会把它清掉。
        if (BeatEffectWiring.PendingShow(beat, _choiceOpen ? _choiceBeat : null) is null) return;

        _deferredChoiceId = beat.Id;
        TryShowChoice();
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

        // 这一拍要是带选项，按 E 只把它"够着了"——选项还得他自己挑，确认了才算完。
        if (BeatEffectWiring.NeedsChoice(beat))
        {
            DeferChoice(beat);
            return true;
        }

        if (!_beats.Complete(beat)) return false;

        SetHint($"{beat.Name}　——　记下了。", 3f);
        AfterBeat(beat);
        return true;
    }

    // ————————————————————————— 主菜单 —————————————————————————

    /// <summary>
    /// 把几个"不碰玩家存档"的自检开关先立起来。
    /// 必须在 <see cref="LoadProgress"/> 之前：跑批连进度文件都不该改。
    /// </summary>
    private void ParseRunFlags(string[] args)
    {
        // 开发辅助：--playthrough 一局打通十二拍。**不读不写玩家那份档**——
        // 它只证明这条路走得完，不该把玩家的进度换成一份机器跑出来的。
        _playthrough = args.Contains("--playthrough");
        if (_playthrough) _saveBlocked = true;

        // --bag 也一样：囊里装的是这一趟捡到的，不能掺进玩家的旧物。
        _bagRun = args.Contains("--bag");
        if (_bagRun) _saveBlocked = true;

        // --shop 只走内存里的一百文，别拿它改玩家的钱与信任。
        _shopRun = args.Contains("--shop");
        if (_shopRun) _saveBlocked = true;

        // --siege 只算自己造的那份账：撤退不置旗标、招兵不加玩家的兵。
        _siegeRun = args.Contains("--siege");
        if (_siegeRun) _saveBlocked = true;

        // --transition 自造十二拍与两条承诺走完四页，走完就退；同样不碰玩家那份档。
        _transitionRun = args.Contains("--transition");
        if (_transitionRun) _saveBlocked = true;

        // --enfeoff 只算六个人的封赏账（四行），不碰玩家那份档。
        _enfeoffRun = args.Contains("--enfeoff");
        if (_enfeoffRun) _saveBlocked = true;

        // --effects 只结算自己造的那份三轴 / 囊 / 兵，玩家的档一个字都不改（S3-02c）。
        _effectsRun = args.Contains("--effects");
        if (_effectsRun) _saveBlocked = true;
    }

    /// <summary>
    /// 这一次启动是不是"只看地图"（<c>--map</c> / <c>--shotmap</c>）。
    ///
    /// 这两条要看的是**新档第一天的七条路**，读进玩家那份档就会把"当前"写成他上次停的地方，
    /// 截出来与打印出来的都对不上票面。
    /// </summary>
    private static bool WantsMapDemo(string[] args) =>
        args.Contains("--map") || args.Contains("--shotmap");

    /// <summary>
    /// 这一次启动是不是"只看开场"（<c>--intro</c> / <c>--shotintro</c>）。
    ///
    /// 开场是新档的第一屏，看的就是 hub1 的第 1 天；读进玩家那份档，
    /// 截出来会变成他上次停的地方，还带着他已经改过的包袱。
    /// </summary>
    private static bool WantsIntroDemo(string[] args) =>
        args.Contains("--intro")
        || args.Any(a => a.StartsWith("--shotintro", StringComparison.Ordinal));

    /// <summary>
    /// 这一次启动该不该先给菜单。
    ///
    /// 只有"人真的在玩"才给：带任何自检/调试参数的都不给——
    /// <c>--playthrough / --bag / --shop / --siege / --transition / --audit / --chain ...</c>
    /// 要的是直接进世界，菜单挡在中间会把这些跑批和 smoke 一起卡死。
    /// </summary>
    private static bool WantsBootMenu(string[] args)
    {
        foreach (var a in args)
        {
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;

            // 这两类不改变"这是一次正常启动"：引导开关与指定落脚点。
            if (a is "--tutorial" or "--no-tutorial") continue;
            if (a.StartsWith("--hub=", StringComparison.Ordinal)) continue;

            return false;
        }

        return true;
    }

    /// <summary>
    /// 把菜单摆上来。**只探一下档在不在**——这里不隔离坏档、不写文件，
    /// 玩家要是直接退出，磁盘上什么都不该变。
    /// </summary>
    private void OpenBootMenu()
    {
        var (state, error) = _save.TryLoad();

        // 坏档视同无档：继续那一项灰着，走「新的开始」才轮到隔离/改名。
        _bootMenu = new BootMenu(hasSave: state is not null, saveBroken: state is null && error is not null);

        BuildBootMenu();
        _bootPanel.Visible = true;
        _bootMenuOpen = true;

        if (_view is not null) _view.UiBlocking = true;

        PaintBootMenu();

        GD.Print($"[菜单] 存档{(state is not null ? "在" : "不在")}"
                 + $"{(error is not null ? "、且读不出来" : "")}；"
                 + $"「继续」{(_bootMenu.ContinueEnabled ? "亮" : "灰")}，"
                 + $"默认停在「{BootMenu.TextOf(_bootMenu.Selected)}」");
    }

    /// <summary>画一遍菜单：高亮条跟着选中项走，灰的项连字带色一起压下去。</summary>
    private void PaintBootMenu()
    {
        if (_bootMenu is null) return;

        for (var i = 0; i < BootMenu.Count; i++)
        {
            var (text, enabled) = _bootMenu.Line(i);
            var row = _bootRows[i];
            var here = i == _bootMenu.SelectedIndex;

            row.Text = here ? $"· {text} ·" : text;
            row.AddThemeColorOverride("font_color", !enabled
                ? new Color(0.40f, 0.38f, 0.33f, 0.85f)
                : here
                    ? new Color(1.00f, 0.93f, 0.74f, 0.98f)
                    : new Color(0.88f, 0.82f, 0.68f, 0.92f));
            row.AddThemeFontSizeOverride("font_size", here ? 16 : 14);
        }

        // 灰的「继续」要说清楚为什么灰——玩家不需要猜。
        var canContinue = _bootMenu.ContinueEnabled;
        _bootNote.Text = canContinue ? "接着上次停下的地方走。" : "还没有能续的档，这一趟得从头来。";
        _bootNote.AddThemeColorOverride("font_color", canContinue
            ? new Color(0.72f, 0.66f, 0.52f, 0.92f)
            : new Color(0.60f, 0.50f, 0.42f, 0.92f));

        _bootCursor.Position = new Vector2(CursorX, RowY(_bootMenu.SelectedIndex) + 3);
    }

    /// <summary>第 i 行离画面顶多远。</summary>
    private static float RowY(int i) => 212 + i * 34;

    /// <summary>菜单文字的左边（整块大致居中；三项都是左对齐，读起来才齐）。</summary>
    private const float RowX = 277;

    /// <summary>选中项左边那一条红。</summary>
    private const float CursorX = 265;

    /// <summary>造菜单那一层。只造一次——重开菜单时只改文字与高亮。</summary>
    private void BuildBootMenu()
    {
        if (_bootPanel is not null) return;

        // 压在 Layer 10 的 HUD 上面：世界还没开始时，玩家不该看见血条似的那些字。
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);

        _bootPanel = new Panel
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(ViewW, ViewH),
        };
        _bootPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.032f, 0.036f, 0.034f, 1f),
            BorderColor = new Color(0.62f, 0.24f, 0.18f, 0.55f),
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
        });
        layer.AddChild(_bootPanel);

        _bootTitle = new Label
        {
            Position = new Vector2(0, 92),
            Size = new Vector2(ViewW, 44),
            Text = "举火",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_bootTitle, 30, new Color(0.95f, 0.88f, 0.70f, 0.98f));
        _bootPanel.AddChild(_bootTitle);

        _bootSub = new Label
        {
            Position = new Vector2(0, 138),
            Size = new Vector2(ViewW, 18),
            Text = "序章 · 白幡村",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_bootSub, 11, new Color(0.66f, 0.60f, 0.48f, 0.90f));
        _bootPanel.AddChild(_bootSub);

        // 一道细红横线：跟左上铭牌那一条是同一个记号。
        var rule = new Panel
        {
            Position = new Vector2(ViewW / 2f - 72, 166),
            Size = new Vector2(144, 1),
        };
        rule.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.62f, 0.24f, 0.18f, 0.75f) });
        _bootPanel.AddChild(rule);

        // 选中项左边那一条红：不靠字形（↑ ↓ 这类符号未必在字库里）。
        _bootCursor = new Panel
        {
            Position = new Vector2(CursorX, RowY(0) + 3),
            Size = new Vector2(3, 20),
        };
        _bootCursor.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.78f, 0.30f, 0.20f, 0.95f) });
        _bootPanel.AddChild(_bootCursor);

        for (var i = 0; i < BootMenu.Count; i++)
        {
            _bootRows[i] = new Label
            {
                Position = new Vector2(RowX, RowY(i)),
                Size = new Vector2(240, 26),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(_bootRows[i], 14, new Color(0.88f, 0.82f, 0.68f, 0.92f));
            _bootPanel.AddChild(_bootRows[i]);
        }

        _bootNote = new Label
        {
            Position = new Vector2(0, 320),
            Size = new Vector2(ViewW, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Style(_bootNote, 10, new Color(0.72f, 0.66f, 0.52f, 0.92f));
        _bootPanel.AddChild(_bootNote);

        _bootFoot = new Label
        {
            Position = new Vector2(0, ViewH - 34),
            Size = new Vector2(ViewW, 18),
            Text = "方向键选　·　Enter 确认　·　Esc 退出",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_bootFoot, 10, new Color(0.52f, 0.49f, 0.41f, 0.85f));
        _bootPanel.AddChild(_bootFoot);
    }

    /// <summary>菜单里的键：上下选、Enter 确认、Esc 退出。菜单里没有别的可以退。</summary>
    private void HandleBootMenuKey(Key key)
    {
        if (_bootMenu is null) return;

        switch (key)
        {
            case Key.Up:
            case Key.W:
                _bootMenu.MoveUp();
                PaintBootMenu();
                break;

            case Key.Down:
            case Key.S:
                _bootMenu.MoveDown();
                PaintBootMenu();
                break;

            case Key.Enter:
            case Key.KpEnter:
                ConfirmBootMenu();
                break;

            case Key.Escape:
                QuitFromBootMenu();
                break;
        }
    }

    private void ConfirmBootMenu()
    {
        if (_bootMenu is null) return;

        switch (_bootMenu.Confirm())
        {
            case BootChoice.Quit:
                QuitFromBootMenu();
                break;

            case BootChoice.Continue:
                ChooseContinue();
                break;

            default:
                ChooseNewGame();
                break;
        }
    }

    /// <summary>退出：不读档、不写档。世界还没造出来，关窗口就是全部。</summary>
    private void QuitFromBootMenu()
    {
        GetTree().Quit();
    }

    /// <summary>「继续」：菜单收起来，照旧读档进世界。</summary>
    private void ChooseContinue()
    {
        CloseBootMenu();
        BootWorld(_args);
    }

    /// <summary>
    /// 「新的开始」：进度重置、旧档改名留底，然后从 hub1、第 1 天起跑。
    /// 文件动作全由 <see cref="BootFlow"/> 做，这里只负责把结果说一句话。
    /// </summary>
    private void ChooseNewGame()
    {
        var flow = new BootFlow(_save, _ => SaveProgress());
        var outcome = flow.Choose(BootChoice.NewGame, _progress);

        if (!outcome.Ok) GD.PushWarning($"[菜单] 新的开始没走干净：{outcome.Error}");

        // 教程要重新弹：LoadProgress 在菜单之前就跑过了，这里得手动拨回去。
        _tut.Reset();

        // 开局那点钱只在「新的开始」发（S3-01c）；续档那条路不动它。
        _newGame = true;

        GD.Print($"[菜单] 新的开始 → 教程重置，旧档改名 {(outcome.ShelvedTo ?? "没挪成")}");

        CloseBootMenu();
        BootWorld(_args);
    }

    private void CloseBootMenu()
    {
        _bootMenuOpen = false;
        if (_bootPanel is not null) _bootPanel.Visible = false;
    }

    // ————————————————————————— 开场介绍（S3-01d）—————————————————————————

    /// <summary><c>--shotintro</c> 截的那一行：第 7 句，老仆，带立绘。</summary>
    private const int IntroShotLine = 6;

    /// <summary>
    /// 开场第 N 句（0 起）摆上来。世界已经造好、教程还没开始——
    /// 玩家先知道自己在哪儿、手里是什么，再学怎么走路。
    /// </summary>
    /// <param name="index">从第几句开始（0 起；截图会从第 7 句起）。</param>
    private void StartIntro(int index)
    {
        if (_introTable is null)
        {
            GD.Print("[开场] 没有开场白表（intro.csv 读不进来），这次不放");
            return;
        }

        _intro = new IntroFlow(_introTable);

        for (var i = 0; i < index && i < _intro.Count - 1; i++) _intro.Advance();

        // 开场期间别的面板一律收起来：对话、选项、地图都不许压在这九行上面。
        CloseChoice(restoreTutorial: false);

        _introOpen = true;

        if (_tutPanel is not null) _tutPanel.Visible = false;
        if (_hint is not null) _hint.Visible = false;
        if (_box is not null) _box.Visible = false;
        ShowHud(false);
        if (_view is not null)
        {
            _view.SetBoxOpen(false);
            _view.UiBlocking = true;
        }

        _introPanel.Visible = true;
        PaintIntro();

        var line = _intro.Current;
        GD.Print($"[开场] 第 {_intro.Ordinal}/{_intro.Count} 句："
                 + $"{(line.IsNarration ? "旁白" : line.Speaker + (line.HasPortrait ? "（立绘）" : ""))}");
    }

    /// <summary>把现在这一句摆到画面上：旁白只有字，有人说话时左边出立绘、右边出话。</summary>
    private void PaintIntro()
    {
        if (_intro is null) return;

        var line = _intro.Current;
        var portrait = ResolvePortrait(line.Portrait);

        _introName.Text = line.IsNarration ? "旁白" : line.Speaker;
        _introName.AddThemeColorOverride("font_color", line.IsNarration
            ? new Color(0.62f, 0.58f, 0.48f, 0.92f)
            : new Color(0.96f, 0.88f, 0.68f, 0.98f));

        _introBody.Text = line.Text;
        _introOrdinal.Text = $"{_intro.Ordinal} / {_intro.Count}";

        // 有人站着，话就往右让，给立绘腾出左边那一块；没人就把整条框都给字。
        if (portrait is not null)
        {
            _introPortrait.Show(portrait, PortraitArt.BigScale);
            _introName.Position = new Vector2(232, 12);
            _introBody.Position = new Vector2(232, 36);
            _introBody.Size = new Vector2(352, 78);
        }
        else
        {
            _introPortrait.Show(null);
            _introName.Position = new Vector2(20, 12);
            _introBody.Position = new Vector2(20, 36);
            _introBody.Size = new Vector2(568, 78);
        }
    }

    /// <summary>开场里的键：Enter 翻一行，Esc 不看了。别的键什么都不做（这一层已经全吞了）。</summary>
    private void HandleIntroKey(Key key)
    {
        switch (key)
        {
            case Key.Enter:
            case Key.KpEnter:
                if (_intro is null) return;

                if (_intro.Advance()) PaintIntro();
                else FinishIntro(skipped: false);
                break;

            case Key.Escape:
                FinishIntro(skipped: true);
                break;
        }
    }

    /// <summary>
    /// 收场：九行全部记作看过，把教程接上来。
    /// 跳过和看完走同一条路——差别只在打印那一句。
    /// </summary>
    private void FinishIntro(bool skipped)
    {
        if (_intro is null || !_introOpen) return;

        if (skipped) _intro.Skip();

        _introOpen = false;
        _introSeen = true;

        _introPanel.Visible = false;
        _introPortrait.Show(null);
        ShowHud(true);

        if (_view is not null)
            _view.UiBlocking = _helpOpen || _choiceOpen || _bagOpen || _trustOpen || _mapOpen || _shopOpen;

        if (_hint is not null) _hint.Visible = true;

        PaintTutorial();
        RefreshHint();

        // 九句话说完，先告诉他"你要做的"这四个字后面跟着什么，再开始教走路。
        ShowGoalCard();

        GD.Print(skipped
            ? $"[开场] 跳过（{_intro.Count} 行都记作看过），进教程"
            : $"[开场] 九行放完，进教程");
    }

    // ————————————————————————— 开局目标卡（S4-01c）—————————————————————————

    /// <summary>
    /// 把卡摆出来：正文取 goals.csv 的 <c>start</c> 那一句（"活过今夜"）。
    /// 只在还没教完的时候摆——教完的玩家早就知道要干什么了。
    /// </summary>
    private void ShowGoalCard()
    {
        if (_goals is null || _tut.Done || _goalCardPanel is null) return;

        _goalCard.Show(_goals.Start);
        _goalCardHead.Text = GoalCard.Head;
        _goalCardBody.Text = _goalCard.Body;
        _goalCardPanel.Modulate = new Color(1f, 1f, 1f, 0f);
        _goalCardPanel.Visible = true;

        // 卡在上面时教学条让开：两张东西一起说"你要做什么"会互相抢。
        if (_tutPanel is not null) _tutPanel.Visible = false;

        GD.Print($"[目标] 开局卡：{GoalCard.Head} / {_goalCard.Body}");
    }

    /// <summary>淡入 + 到点自己收。不吃键——它只是提示。</summary>
    private void TickGoalCard(float dt)
    {
        if (!_goalCard.Visible) return;

        var still = _goalCard.Tick(dt);
        _goalCardPanel.Modulate = new Color(1f, 1f, 1f, _goalCard.Alpha);

        if (!still) HideGoalCard();
    }

    private void HideGoalCard()
    {
        _goalCardPanel.Visible = false;
        _goalCardPanel.Modulate = new Color(1f, 1f, 1f, 0f);
        PaintTutorial();
    }

    /// <summary>表里的立绘名 → 这个人的脸。没有这张表 / 没这个人，都返回 null。</summary>
    private Portrait? ResolvePortrait(string? npc)
    {
        if (string.IsNullOrEmpty(npc) || _portraits is null) return null;
        return _portraits.TryGet(npc, out var p) ? p : null;
    }

    /// <summary>
    /// 开场那九行压上来时，HUD 让开。
    /// 压暗层只有九成不透明，HUD 的字会从底下透出来，和"序章 · 白幡村"叠成一团。
    /// </summary>
    private void ShowHud(bool on)
    {
        if (_plate is not null) _plate.Visible = on;
        if (_title is not null) _title.Visible = on;
        if (_subtitle is not null) _subtitle.Visible = on;
        if (_beatLine is not null) _beatLine.Visible = on;
        if (_goalLine is not null) _goalLine.Visible = on;
    }

    /// <summary>读 intro.csv。读不进来只是不放开场，打一句警告就走。</summary>
    private static IntroTable? TryLoadIntroTable()
    {
        try
        {
            return IntroTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/intro.csv"));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[开场] intro.csv 读不进来：{e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 读 tutorial.csv（S4-01c）。读不进来只是这一局不教，打一句警告就走——
    /// 一张表坏掉不该把整局挡在白屏上；但警告要能一眼认出来是表的问题。
    /// </summary>
    private static TutorialScript? TryLoadTutorialScript()
    {
        try
        {
            return TutorialScript.FromCsv(FileAccess.GetFileAsString("res://Content/data/tutorial.csv"));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[引导] tutorial.csv 读不进来，这一局不教：{e.Message}");
            return null;
        }
    }

    /// <summary>读 portraits.csv。读不进来只是不挂立绘，对话退回一行字。</summary>
    private static PortraitTable? TryLoadPortraitTable()
    {
        try
        {
            return PortraitTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/portraits.csv"));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[立绘] portraits.csv 读不进来：{e.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>--intro</c>：无头把九行打出来（第几句、谁、带不带立绘、说了什么），然后退出。
    /// 不走世界、不写文件——验的就是"这张表读得出来、顺序没乱、旁白没挂立绘"。
    /// </summary>
    private void RunIntroCheck()
    {
        try
        {
            var table = IntroTable.FromCsv(FileAccess.GetFileAsString("res://Content/data/intro.csv"));

            foreach (var line in IntroReport.Lines(table)) GD.Print(line);
        }
        catch (Exception e)
        {
            GD.Print($"{IntroReport.Prefix} 读不出来：{e.Message}");
            _exitCode = 1;
        }
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
        if (_selfCheckRun) return;     // 自检/跑批一律不碰玩家的档（见 SaveProgress 同款注释）

        var state = _world.ToSaveState();

        // 先写旗标（World.ToSaveState 已经把 FlagStore 抄进去了），再写"第几天 + 做完哪几拍"。
        // 反过来也不会错，但顺序写清楚，读档的人一眼能对上 S1-06 的语义表。
        _beats?.Machine.ToState(state);

        // 转段那一份也保持同一份进度：它要拿 prologue.beats_done 判"十二拍走完没有"。
        _beats?.Machine.ToState(_transitionState);

        // 兵力跟在节拍进度后面：招兵加的、攻城扣的都在 _troops 这一份里（schema v4 的 prologue.troops）。
        state.Prologue.Troops = _troops;

        // 转段四样（schema v5）：SaveGame 每次另起一份 SaveState，不抄就白选了。
        // 旗标走 _world.Flags（Finish 两边都置），这里只把四样值原样带过去。
        var prologue = _transitionState.Prologue;
        state.Prologue.Capital = prologue.Capital;
        state.Prologue.Realm = prologue.Realm;
        state.Prologue.Oaths = new List<string>(prologue.Oaths ?? new List<string>());
        state.Prologue.Debts = new List<string>(prologue.Debts ?? new List<string>());

        // 分封六样（schema v6）：一人一档，重开转段时靠它跳过已经封过的那一页。
        state.Prologue.Enfeoffs = new Dictionary<string, string>(
            prologue.Enfeoffs ?? new Dictionary<string, string>(), StringComparer.Ordinal);

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
        // 自检/跑批一律不写玩家进度。这条以前只挡了 --playthrough 与 --bag，
        // 于是 `--tutrun` 每跑一次就把 tutorial_done 写成 1 ——
        // 门禁越跑，玩家越看不到新手教程（玩家报的「没有教程」就是这么来的）。
        if (_selfCheckRun) return;

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
        _plate = new Panel
        {
            Position = new Vector2(12, 10),
            Size = new Vector2(196, 50),
        };
        _plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.045f, 0.052f, 0.050f, 0.80f),
            BorderColor = new Color(0.62f, 0.24f, 0.18f, 0.90f),
            BorderWidthLeft = 2,
        });
        ui.AddChild(_plate);

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
            Position = new Vector2(216, 18),
            Size = new Vector2(ViewW - 228, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_beatLine, 11, new Color(0.93f, 0.72f, 0.44f, 0.94f));
        ui.AddChild(_beatLine);

        // 目标那一行（S4-01c）：单独一行、字号比天数那行大半号，前缀"目标　"。
        // 天数说"现在第几天"，目标说"你到底要干什么"——后者才是玩家嘴里"目的明确一点"。
        _goalLine = new Label
        {
            Position = new Vector2(216, 38),
            Size = new Vector2(ViewW - 228, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_goalLine, 12, new Color(0.96f, 0.86f, 0.60f, 0.97f));
        ui.AddChild(_goalLine);

        // 目标行下面那行小字（S4-03b）："怎么去　…"，比目标小一号、淡一点。
        // 没有 hint 就整行收起来，不留一条空档——顶上空着一行比不写更奇怪。
        _hintLine = new Label
        {
            Position = new Vector2(228, 58),
            Size = new Vector2(ViewW - 240, 16),
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        Style(_hintLine, 10, new Color(0.82f, 0.76f, 0.62f, 0.86f));
        ui.AddChild(_hintLine);

        // 新手引导条：一行，压在提示线上方；对话打开时让位。
        // 两份底框：常态 / 被闸门拦下时闪的暖红（S4-02）。
        _tutStyleCold = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.060f, 0.058f, 0.86f),
            BorderColor = new Color(0.72f, 0.34f, 0.22f, 0.85f),
            BorderWidthLeft = 2,
        };

        _tutStyleHot = new StyleBoxFlat
        {
            BgColor = new Color(0.170f, 0.070f, 0.050f, 0.92f),
            BorderColor = new Color(0.98f, 0.62f, 0.36f, 1.00f),
            BorderWidthLeft = 3,
        };

        _tutPanel = new Panel
        {
            Position = new Vector2(18, ViewH - 74),
            Size = new Vector2(ViewW - 36, 30),
        };
        _tutPanel.AddThemeStyleboxOverride("panel", _tutStyleCold);
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

        // 三轴变化的那一条：让到"目标 + 怎么去"两行下面，字小一号，淡出后不占地方。
        _trustToast = new Label
        {
            Position = new Vector2(216, 78),
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
        BuildTransition(ui);
        BuildMap(ui);
        BuildIntro();
        BuildGoalCard(ui);

        PaintTutorial();
        _help.Visible = false;
    }

    /// <summary>
    /// 开局那张目标卡（S4-01c）：屏幕正中、深底一条横线，抬头看见"你要做的 / 活过今夜"。
    /// 它不接键盘、不挡键——收它靠 <see cref="GoalCard"/> 的计时或任意键（那个键照常生效）。
    /// </summary>
    private void BuildGoalCard(CanvasLayer ui)
    {
        const int w = 348;
        const int h = 108;

        _goalCardPanel = new Panel
        {
            Position = new Vector2((ViewW - w) / 2f, (ViewH - h) / 2f),
            Size = new Vector2(w, h),
            Visible = false,
            Modulate = new Color(1f, 1f, 1f, 0f),
        };
        _goalCardPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.050f, 0.048f, 0.042f, 0.94f),
            BorderColor = new Color(0.72f, 0.34f, 0.22f, 0.90f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 3,
            BorderWidthRight = 1,
        });
        ui.AddChild(_goalCardPanel);

        _goalCardHead = new Label
        {
            Position = new Vector2(20, 14),
            Size = new Vector2(w - 40, 18),
            Text = GoalCard.Head,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_goalCardHead, 11, new Color(0.70f, 0.64f, 0.50f, 0.95f));
        _goalCardPanel.AddChild(_goalCardHead);

        _goalCardBody = new Label
        {
            Position = new Vector2(20, 38),
            Size = new Vector2(w - 40, 44),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_goalCardBody, 22, new Color(0.97f, 0.88f, 0.66f, 0.98f));
        _goalCardPanel.AddChild(_goalCardBody);
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

        // 说话的人站在框的左边：128×128 的小立绘，占的是"名字 + 三行字"的宽度。
        // 没有立绘数据的人（巡逻的衙役、临时搭话的）就不摆这块，框恢复成原来那一行字。
        _boxPortrait = new PortraitView
        {
            Position = new Vector2(8, 6),
            Size = new Vector2(PortraitArt.SmallPixels, PortraitArt.SmallPixels),
            Visible = false,
        };
        _box.AddChild(_boxPortrait);

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

    /// <summary>
    /// 开场那一层（S3-01d）：整屏压暗 + 顶上一行地名 + 底部一条字幕框 + 左边的立绘。
    ///
    /// 单独一层（Layer 18）：它要盖住 HUD、教程条、底部提示，
    /// 但得让主菜单（Layer 20）盖在它上面——菜单还没选完的时候不该看见开场。
    /// </summary>
    private void BuildIntro()
    {
        var layer = new CanvasLayer { Layer = 18 };
        AddChild(layer);

        _introPanel = new Panel
        {
            Position = new Vector2(0, 0),
            Size = new Vector2(ViewW, ViewH),
            Visible = false,
        };
        _introPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.020f, 0.024f, 0.023f, 0.90f),
        });
        layer.AddChild(_introPanel);

        _introTop = new Label
        {
            Position = new Vector2(0, 16),
            Size = new Vector2(ViewW, 16),
            Text = "序章 · 白幡村",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_introTop, 10, new Color(0.66f, 0.60f, 0.48f, 0.92f));
        _introPanel.AddChild(_introTop);

        _introOrdinal = new Label
        {
            Position = new Vector2(ViewW - 70, 16),
            Size = new Vector2(54, 16),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_introOrdinal, 9, new Color(0.52f, 0.49f, 0.41f, 0.85f));
        _introPanel.AddChild(_introOrdinal);

        // 立绘：192×192，底边压进字幕框 20 像素，像人从桌子后面探出半身。
        _introPortrait = new PortraitView
        {
            Position = new Vector2(28, ViewH - 140 - PortraitArt.BigPixels + 22),
            Size = new Vector2(PortraitArt.BigPixels, PortraitArt.BigPixels),
            Visible = false,
        };
        _introPanel.AddChild(_introPortrait);

        var box = new Panel
        {
            Position = new Vector2(16, ViewH - 140),
            Size = new Vector2(ViewW - 32, 124),
        };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.062f, 0.060f, 0.96f),
            BorderColor = new Color(0.42f, 0.36f, 0.26f, 0.85f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
        });
        _introPanel.AddChild(box);

        _introName = new Label
        {
            Position = new Vector2(20, 12),
            Size = new Vector2(240, 18),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_introName, 13, new Color(0.96f, 0.88f, 0.68f, 0.98f));
        box.AddChild(_introName);

        _introBody = new Label
        {
            Position = new Vector2(20, 36),
            Size = new Vector2(568, 78),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Style(_introBody, 12, new Color(0.86f, 0.83f, 0.75f, 0.96f));
        box.AddChild(_introBody);

        _introFoot = new Label
        {
            Position = new Vector2(0, ViewH - 24),
            Size = new Vector2(ViewW, 16),
            Text = "Enter 继续　·　Esc 跳过",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_introFoot, 10, new Color(0.58f, 0.55f, 0.46f, 0.88f));
        _introPanel.AddChild(_introFoot);
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
                "图　　M：去别处；↑↓ 选、Enter 走，按路程扣刻\n" +
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
            var y = 50 + i * 30;

            var item = new Label
            {
                Position = new Vector2(18, y),
                Size = new Vector2(ViewW - 250, 26),
                Text = "",
                Visible = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(item, 13, new Color(0.78f, 0.75f, 0.66f, 0.90f));
            _choice.AddChild(item);
            _choiceItems[i] = item;

            // 右侧那半句只给攻城用：门槛缺什么、兵够不够打赢。别的拍空着。
            var note = new Label
            {
                Position = new Vector2(226, y),
                Size = new Vector2(196, 26),
                Text = "",
                Visible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(note, 10, new Color(0.62f, 0.59f, 0.51f, 0.90f));
            _choice.AddChild(note);
            _choiceNotes[i] = note;
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

        // 兵力放在最底：三条轴与两条路线说的是"人心"，兵力说的是"手里还有什么"。
        _trustTroops = new Label
        {
            Position = new Vector2(18, 212),
            Size = new Vector2(412, 18),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_trustTroops, 11, new Color(0.90f, 0.84f, 0.68f, 0.96f));
        _trust.AddChild(_trustTroops);

        var foot = new Label
        {
            Position = new Vector2(18, 232),
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

    /// <summary>
    /// 转段五页的面板（S2-05c / S4-01d）：定都、国号、国策、分封、国情报告，最后一页是「序章完成」。
    ///
    /// 五页共用一组行：定都七行、报告十三行（含"带走"与分封两行），按最长的那一页备十三行；
    /// 每页只点亮自己用得上的。页面内容全由 <see cref="TransitionScreen"/> 算，这里只负责画和递按键。
    /// </summary>
    private void BuildTransition(CanvasLayer ui)
    {
        _transition = new Panel
        {
            Position = new Vector2(20, 62),
            Size = new Vector2(600, 340),
            Visible = false,
        };
        _transition.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.042f, 0.046f, 0.044f, 0.98f),
            BorderColor = new Color(0.74f, 0.36f, 0.24f, 0.94f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 2,
            BorderWidthRight = 1,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        ui.AddChild(_transition);

        _transitionHead = new Label
        {
            Position = new Vector2(18, 12),
            Size = new Vector2(560, 22),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_transitionHead, 15, new Color(0.96f, 0.90f, 0.72f, 0.98f));
        _transition.AddChild(_transitionHead);

        _transitionFlash = new Label
        {
            Position = new Vector2(18, 44),
            Size = new Vector2(560, 16),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_transitionFlash, 10, new Color(0.90f, 0.68f, 0.36f, 0.96f));
        _transition.AddChild(_transitionFlash);

        for (var i = 0; i < _transitionRows.Length; i++)
        {
            var row = new Label
            {
                // 十三行（报告页最长）压进 340 的面板：行距 18，最后一行落在 286。
                Position = new Vector2(18, 70 + i * 18),
                Size = new Vector2(564, 18),
                Text = "",
                Visible = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(row, 12, new Color(0.78f, 0.75f, 0.66f, 0.92f));
            _transition.AddChild(row);
            _transitionRows[i] = row;
        }

        _transitionFoot = new Label
        {
            Position = new Vector2(18, 310),
            Size = new Vector2(564, 18),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Style(_transitionFoot, 9, new Color(0.60f, 0.56f, 0.46f, 0.90f));
        _transition.AddChild(_transitionFoot);
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
            PrologueBeats.KnockTrigger => PlayKnockBeat(beat, pick),
            PrologueBeats.SearchTrigger => PlaySearchBeat(beat, pick),
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

        return PlayPick(beat, pick);
    }

    /// <summary>
    /// 面板正在等这一拍时确认它。返回 null = 没面板或确认成功；返回一句话 = 没成。
    ///
    /// 搜查 / 敲门那两拍本来不经过这里，可它们现在也可能带选项（b02、b08），
    /// 按完 E 之后面板就等在那儿——跑批得替玩家按下去，不然这一拍永远不算完。
    /// </summary>
    private string? ConfirmIfWaiting(Beat beat, int pick)
    {
        if (!_choiceOpen || _choiceBeat is null) return null;
        if (!string.Equals(_choiceBeat.Id, beat.Id, StringComparison.Ordinal)) return null;

        return PlayPick(beat, pick);
    }

    /// <summary>
    /// 在面板上确认这一拍：先试 <paramref name="pick"/>，效果落不下（钱不够、囊满）就换下一项。
    ///
    /// 跑批要证明"这条路走得完"。真玩家遇到装不下会自己清囊再来，脚本没有这个本事，
    /// 只能照着候选顺序找一条落得下的——和攻城"第 0 法点不动就退到第一种能用的"同一个道理。
    /// </summary>
    private string? PlayPick(Beat beat, int pick)
    {
        var count = Math.Max(1, beat.Options.Length);

        for (var attempt = 0; attempt < count; attempt++)
        {
            var index = PrologueBeats.NormalizePick(beat, (Math.Max(0, pick) + attempt) % count);

            if (attempt > 0)
                GD.Print($"[通关] {beat.Id} 第 {(Math.Max(0, pick) + attempt - 1) % count + 1} 项没落成，改走第 {index + 1} 项");

            _choiceSel = index;

            // 攻城那一拍：票面默认第 0 项「围」，可城里没断粮时它点不动。
            // 跑批要证明"这条路走得完"，那就退到第一种能用的法子（通常是「攻」）。
            if (_siegeScreen is not null && !_siegeScreen.CanUse(index))
            {
                var usable = _siegeScreen.FirstUsable();
                GD.Print($"[通关] {beat.Id} 第 {index + 1} 法用不上，改走第 {usable + 1} 法");
                _choiceSel = usable;
            }

            ConfirmChoice();

            if (_beats!.Machine.IsDone(beat)) return null;
        }

        return $"确认之后这一拍没记上（{beat.Id} 每一项都没落成）";
    }

    /// <summary>
    /// 搜查型节拍：把人挪到点位旁边，按一次 E（<see cref="MapView.PressInteract"/> →
    /// World.Search → Main.OnSearched），读完再按一次 E 关掉；这一拍带选项就替玩家确认。
    /// </summary>
    private string? PlaySearchBeat(Beat beat, int pick)
    {
        var pointId = PrologueBeats.SearchPointOf(beat.Id);
        if (pointId is null) return "这一拍没有指定点位";

        var prop = _world.Props.FirstOrDefault(p => string.Equals(p.Point.Id, pointId, StringComparison.Ordinal));
        if (prop is null) return $"搜不到点位 {pointId}";

        if (!StandAt(prop, out var why)) return why;

        _view!.PressInteract();
        CloseIfOpen();

        var confirm = ConfirmIfWaiting(beat, pick);
        if (confirm is not null) return confirm;

        return _beats!.Machine.IsDone(beat) ? null : $"按了 E，{beat.Id} 没记上";
    }

    /// <summary>
    /// 敲门型节拍：不碰 KnockTimer，靠真实的刻消耗等它自己到点——
    /// 每翻一件东西记 1 刻，<see cref="World.CheckKnock"/> 到线就敲，Knocked 事件把这一拍交出来。
    /// 敲完门这一拍可能带选项（b02）：面板弹出来之后照样替玩家按下确认。
    /// </summary>
    private string? PlayKnockBeat(Beat beat, int pick)
    {
        for (var guard = 0; !_beats!.Machine.IsDone(beat); guard++)
        {
            if (guard > 16) return $"翻到 {_world.KeUsed}/{_world.KeBudget} 刻，门还没响";

            // 门响过了，这一拍在面板上等着：这一轮就把选项答掉。
            if (_choiceOpen && BeatEffectWiring.SameBeat(_choiceBeat, beat))
            {
                var confirm = PlayPick(beat, pick);
                if (confirm is not null) return confirm;
                continue;
            }

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

            // CloseIfOpen 关掉对话框时会把等着的那一拍推上来（TryShowChoice），下一轮接上。
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
