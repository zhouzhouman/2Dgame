using System;
using Juhuo.Core;

namespace Juhuo.Application;

/// <summary>
/// 存档是从哪儿来的。这个值决定玩家看到哪句话，也决定这份档还能不能写。
/// </summary>
public enum SaveOrigin
{
    /// <summary>根本没有档。这是第一次玩，不是错误。</summary>
    None,

    /// <summary>正常档读出来了。</summary>
    Primary,

    /// <summary>正常档坏了，从 save.bak 救回来的。</summary>
    Backup,

    /// <summary>正常档和备份都坏了，新开一档。</summary>
    Fresh,

    /// <summary>这份记录来自更新的版本。不许读，也不许盖。</summary>
    TooNew,
}

/// <summary>
/// 存档读写要用的几个动作，全部用委托注入。
///
/// 为什么不直接用 System.IO：Godot 的 <c>user://</c> 不是操作系统路径，
/// <c>File.*</c> 处理不了；这一层又不许引用 Godot。
/// 所以文件怎么读、怎么写，由外面给进来 —— Game 层给 Godot 的 FileAccess，
/// 单元测试给内存字典。
/// </summary>
public sealed class SaveFileSystem
{
    /// <summary>文件在不在。</summary>
    public Func<string, bool> Exists { get; init; } = _ => false;

    /// <summary>读整个文件。文件不在或读不出来时返回 null（不要抛异常）。</summary>
    public Func<string, string?> Read { get; init; } = _ => null;

    /// <summary>写整个文件（覆盖）。写不了就抛异常。</summary>
    public Action<string, string> Write { get; init; } = (_, _) => { };

    /// <summary>复制一个文件（覆盖目标）。</summary>
    public Action<string, string> Copy { get; init; } = (_, _) => { };

    /// <summary>改名（同卷）。目标已存在时由调用方决定怎么办。</summary>
    public Action<string, string> Move { get; init; } = (_, _) => { };

    /// <summary>删一个文件。</summary>
    public Action<string> Delete { get; init; } = _ => { };

    /// <summary>现在几点。坏档改名时用它拼时间戳。</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;
}

/// <summary>
/// 一次读档的结果。
/// </summary>
public sealed class SaveLoadResult
{
    /// <summary>读出来的存档。读不出来时是 null。</summary>
    public SaveState? State { get; init; }

    /// <summary>技术上为什么读不出来（写日志用）。没问题时是 null。</summary>
    public string? Error { get; init; }

    /// <summary>给玩家看的一句（手记体）。没问题时是 null。</summary>
    public string? Notice { get; init; }

    /// <summary>这份存档的来路。</summary>
    public SaveOrigin Origin { get; init; }

    /// <summary>这份档不许被覆盖（版本过新时为真）。</summary>
    public bool Blocked => Origin == SaveOrigin.TooNew;
}

/// <summary>
/// 存档服务：把 <see cref="SaveState"/> 落到文件上，再从文件上取回来。
///
/// 规矩来自 Docs/Rules/rule_flags_save.md §3：
///   · 写入是原子的 —— 先写 .tmp，旧档复制成 .bak，再把 .tmp 改名盖掉正式档；
///   · 坏档不删不改 —— 改名成 .corrupt-时间戳.json，再试备份，都不行才新开档；
///   · 坏档给一句人话，不抛异常、不弹窗。
///
/// 这一层纯 C#，不引用 Godot，也不碰 System.IO（路径是 <c>user://</c>）。
/// </summary>
public sealed class SaveService
{
    /// <summary>正式档的路径（schema_v2.md §C）。</summary>
    public const string SavePath = "user://save.json";

    /// <summary>写档过程中的临时档。同卷改名才叫原子。</summary>
    public const string TempPath = "user://save.tmp";

    /// <summary>上一份正式档的备份。坏档时的第二条命。</summary>
    public const string BackupPath = "user://save.bak";

    /// <summary>背包格数上限（CANON §4：背包 12 格，无仓库）。</summary>
    public const int MaxInventory = 12;

    private readonly SaveFileSystem _fs;
    private readonly string _savePath;
    private readonly string _tempPath;
    private readonly string _backupPath;

    /// <summary>
    /// 建一个存档服务。
    /// </summary>
    /// <param name="fs">文件动作。Game 层给 Godot 的 FileAccess，测试给内存字典。</param>
    /// <param name="savePath">正式档路径。</param>
    /// <param name="tempPath">临时档路径。</param>
    /// <param name="backupPath">备份档路径。</param>
    public SaveService(
        SaveFileSystem fs,
        string? savePath = null,
        string? tempPath = null,
        string? backupPath = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _savePath = savePath ?? SavePath;
        _tempPath = tempPath ?? TempPath;
        _backupPath = backupPath ?? BackupPath;
    }

    /// <summary>正式档的路径。给"这档存在吗"这类检查用。</summary>
    public string SaveFilePath => _savePath;

    /// <summary>备份档的路径。</summary>
    public string BackupFilePath => _backupPath;

    /// <summary>
    /// 写档。原子：先写 .tmp，旧档复制成 .bak，再把 .tmp 改名盖掉正式档。
    ///
    /// 任何一步失败都返回可读的错误，**不抛异常**；正式档在失败时保持原样
    /// （旧档已经被复制进 .bak，所以最多倒退一档）。
    /// </summary>
    /// <param name="state">要写的存档。</param>
    /// <returns>(写成功吗, 失败时为什么)。</returns>
    public (bool Ok, string? Error) TrySave(SaveState? state)
    {
        if (state == null) return (false, "存档内容是空的。");

        string json;
        try
        {
            json = state.ToJson();
        }
        catch (Exception e)
        {
            return (false, $"存档序列化失败：{e.Message}");
        }

        var bad = Validate(state);
        if (bad != null) return (false, bad);

        try
        {
            // 1. 写临时档。写不动就到此为止 —— 正式档一根汗毛都没掉。
            _fs.Write(_tempPath, json);

            // 2. 旧档留一份。坏档时的第二条命就是它。
            if (_fs.Exists(_savePath)) _fs.Copy(_savePath, _backupPath);

            // 3. 同卷改名。改名失败旧档还在（.bak 里也有一份）。
            _fs.Move(_tempPath, _savePath);
        }
        catch (Exception e)
        {
            return (false, $"存档没写成功：{e.Message}");
        }

        return (true, null);
    }

    /// <summary>
    /// 读正式档。坏档给一句可读的错误，不抛异常。
    /// </summary>
    /// <returns>(存档, 错误)。没有档时两个都是 null；坏档时存档是 null、错误有值。</returns>
    public (SaveState? State, string? Error) TryLoad() => TryLoadFrom(_savePath);

    /// <summary>
    /// 读档，并按坏档四步回退（rule_flags_save.md §3.2）：
    ///   1. 解析失败 → 改名 .corrupt-时间戳.json，内容一字不改；
    ///   2. 试 save.bak，成功就用它；
    ///   3. bak 也坏 → 新开档；
    ///   4. 任何情况下不静默重置、不覆盖 .corrupt、不弹窗。
    ///
    /// 版本比当前新的档不走这条路：那条明确"拒绝载入"，文件也不许动。
    /// </summary>
    /// <returns>读档结果，含来路与给玩家看的一句话。</returns>
    public SaveLoadResult LoadOrRecover()
    {
        var (state, error) = TryLoad();

        if (state != null) return new SaveLoadResult { State = state, Origin = SaveOrigin.Primary };

        // 根本没有档：这是第一次玩，不是错误。
        if (error == null) return new SaveLoadResult { Origin = SaveOrigin.None };

        // 版本过新：拒绝载入，文件一个字都不许动，也不许盖。
        if (error.StartsWith(TooNewPrefix, StringComparison.Ordinal))
            return new SaveLoadResult { Error = error, Origin = SaveOrigin.TooNew, Notice = "这份记录来自更新的版本。" };

        // 1. 坏档改名。改不动就跳过 —— 删是绝不允许的。
        TryQuarantine(_savePath);

        // 2. 试备份。
        if (_fs.Exists(_backupPath))
        {
            var (bak, bakError) = TryLoadFrom(_backupPath);
            if (bak != null)
                return new SaveLoadResult
                {
                    State = bak,
                    Error = error,
                    Notice = "你只记得上次歇脚的地方。",
                    Origin = SaveOrigin.Backup,
                };

            if (bakError != null && bakError.StartsWith(TooNewPrefix, StringComparison.Ordinal))
                return new SaveLoadResult { Error = bakError, Origin = SaveOrigin.TooNew, Notice = "这份记录来自更新的版本。" };

            // 备份也坏了：照样留底，不删。
            TryQuarantine(_backupPath);
        }

        // 3. 都坏了：新开档，但把原因留着。
        return new SaveLoadResult
        {
            Error = error,
            Notice = "前面的路，你记不清了。",
            Origin = SaveOrigin.Fresh,
        };
    }

    /// <summary>版本过新时错误信息的前缀。外面靠它认"这是版本问题，不是坏档"。</summary>
    public const string TooNewPrefix = "这份记录来自更新的版本";

    /// <summary>
    /// 写盘前的检查。查的都是规则卡里写死的上限，不自己发明标准。
    /// </summary>
    /// <param name="state">要写的存档。</param>
    /// <returns>不合格时给一句人话；合格时返回 null。</returns>
    public static string? Validate(SaveState? state)
    {
        if (state == null) return "存档内容是空的。";

        if (state.Flags.Count > FlagStore.MaxGlobalFlags)
            return $"旗标太多（{state.Flags.Count} > {FlagStore.MaxGlobalFlags}）。";

        foreach (var hub in state.Hubs)
        {
            var local = hub.Value?.Flags.Count ?? 0;
            if (local > FlagStore.MaxLocalFlagsPerHub)
                return $"{hub.Key} 的局部旗标太多（{local} > {FlagStore.MaxLocalFlagsPerHub}）。";
        }

        if (state.Inventory.Count > MaxInventory)
            return $"背包装不下（{state.Inventory.Count} > {MaxInventory}）。";

        foreach (var item in state.Inventory)
            if (item != null && item.N < 1)
                return $"背包里有件东西数量不对（{item.Item}）。";

        return null;
    }

    /// <summary>
    /// 从某个路径读一份档。
    /// </summary>
    private (SaveState? State, string? Error) TryLoadFrom(string path)
    {
        bool exists;
        try
        {
            exists = _fs.Exists(path);
        }
        catch (Exception e)
        {
            return (null, $"存档读不出来：{e.Message}");
        }

        if (!exists) return (null, null);

        string? json;
        try
        {
            json = _fs.Read(path);
        }
        catch (Exception e)
        {
            return (null, $"存档读不出来：{e.Message}");
        }

        if (json == null) return (null, "存档是空的。");

        try
        {
            var state = SaveState.FromJson(json);

            if (state.IsTooNew)
                return (null, $"{TooNewPrefix}（v{state.Version}），这一版只认到 v{SaveState.CurrentVersion}。");

            if (!state.IsSupported)
                return (null, $"这份记录的版本太老（v{state.Version}），读不了。");

            return (state, null);
        }
        catch (Exception e)
        {
            return (null, $"存档读不懂：{e.Message}");
        }
    }

    /// <summary>
    /// 坏档改名成 .corrupt-时间戳.原后缀。内容一个字不改，也不删。
    /// </summary>
    /// <param name="path">要留底的文件。</param>
    private void TryQuarantine(string path)
    {
        try
        {
            if (!_fs.Exists(path)) return;

            var stamp = _fs.Now().ToString("yyyyMMdd-HHmmss");
            var dot = path.LastIndexOf('.');
            var slash = path.LastIndexOf('/');
            var stem = dot > slash ? path[..dot] : path;
            var ext = dot > slash ? path[dot..] : "";

            _fs.Move(path, $"{stem}.corrupt-{stamp}{ext}");
        }
        catch
        {
            // 改名失败就留着原样。坏档不许被删，也不许被覆盖。
        }
    }
}
